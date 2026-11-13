using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// The 42 Kenney CC0 sounds (Assets/_Game/Audio/Resources/CMSfx, credits in Assets/_Game/Audio/CREDITS.md) wired to
    /// the decoded sim events through the CMAudioMixer (groups Master > Music, Sfx; exposed MasterVol, MusicVol, SfxVol
    /// for the settings page). Positional falloff is measured from the local player (the camera follows them): full
    /// within 9 u, silent beyond 36 u, panned by screen side. A 20-voice pool, a voice cap and a sim-time cooldown per cue,
    /// and priority for the local player's own events keep 32 fighters from becoming noise. Sound never feeds the sim.
    /// Cue table ported from the browser bank (09e0904 src/audio/bank.ts): same files, gains, pitch jitter and rates.
    /// </summary>
    public sealed class CombatAudio : IFxSink, IDisposable
    {
        public const int Voices = 20;
        public const float NearDistance = 9f, FarDistance = 36f;
        public const string MixerResource = "CMAudioMixer", ClipFolder = "CMSfx";

        public sealed class Cue
        {
            public string Id;
            public string[] Files;
            public AudioClip[] Clips;
            public float Gain, Jitter, Rate;
            public int MaxVoices;
            /// <summary>Minimum sim seconds between two plays of this cue (local player's own events skip it).</summary>
            public double Cooldown;
            public bool Ui;
            public double LastPlay = double.NegativeInfinity;
            public int Next;
        }

        private struct Voice { public AudioSource Src; public Cue Cue; public double Ends; public float Priority; }

        /// <summary>Every cue (id, files, gain, pitch jitter, rate, voice cap, cooldown sim s).</summary>
        public static readonly (string Id, string[] Files, float Gain, float Jitter, float Rate, int Max, double Cd, bool Ui)[] Table =
        {
            ("footstep_grass", new[] { "footstep_grass_000", "footstep_grass_001", "footstep_grass_002" }, 0.35f, 0.08f, 1f, 1, 0.2, false),
            ("footstep_wood", new[] { "footstep_wood_000", "footstep_wood_001", "footstep_wood_002" }, 0.4f, 0.08f, 1f, 1, 0.2, false),
            ("footstep_concrete", new[] { "footstep_concrete_000", "footstep_concrete_001", "footstep_concrete_002" }, 0.4f, 0.08f, 1f, 1, 0.2, false),
            ("swing", new[] { "knifeSlice", "knifeSlice2" }, 0.3f, 0.1f, 1.15f, 3, 0.08, false),
            ("hit_punch", new[] { "impactPunch_medium_000", "impactPunch_medium_001" }, 0.55f, 0.08f, 1f, 4, 0.06, false),
            ("hit_plate", new[] { "impactPlate_medium_000", "impactPlate_medium_001" }, 0.45f, 0.08f, 1f, 3, 0.06, false),
            ("block", new[] { "impactPlate_light_000", "impactPlate_light_002" }, 0.5f, 0.06f, 1.2f, 2, 0.08, false),
            ("kaiju_melee", new[] { "impactPunch_heavy_000", "impactPunch_heavy_001" }, 0.7f, 0.06f, 0.8f, 2, 0.1, false),
            ("kaiju_slam", new[] { "impactWood_heavy_000", "impactWood_heavy_001" }, 0.85f, 0.05f, 0.6f, 1, 0.5, false),
            ("roar", new[] { "impactPunch_heavy_002", "impactPlate_heavy_001" }, 0.8f, 0.04f, 0.55f, 1, 0.6, false),
            ("gather_tick", new[] { "impactMining_000", "impactMining_001" }, 0.25f, 0.1f, 1.3f, 1, 0.5, false),
            ("gather_done", new[] { "handleCoins" }, 0.4f, 0.05f, 1f, 2, 0.15, false),
            ("deliver_biomass", new[] { "confirmation_002" }, 0.4f, 0.03f, 1f, 2, 0.2, false),
            ("deliver_corruption", new[] { "bong_001" }, 0.45f, 0.03f, 0.7f, 2, 0.2, false),
            ("death", new[] { "impactSoft_heavy_000", "impactSoft_heavy_001" }, 0.6f, 0.08f, 1f, 3, 0.12, false),
            ("respawn", new[] { "maximize_003" }, 0.4f, 0.03f, 1f, 2, 0.2, false),
            ("barricade_place", new[] { "impactWood_medium_000", "impactPlank_medium_000" }, 0.5f, 0.06f, 1f, 2, 0.15, false),
            ("barricade_invalid", new[] { "error_004" }, 0.4f, 0f, 1f, 1, 0.3, true),
            ("barricade_break", new[] { "impactPlank_medium_001" }, 0.55f, 0.06f, 0.85f, 2, 0.15, false),
            ("wall_crack", new[] { "impactMining_002", "impactMining_003" }, 0.55f, 0.06f, 1f, 2, 0.15, false),
            ("wall_break", new[] { "impactWood_heavy_003" }, 0.7f, 0.05f, 0.75f, 2, 0.2, false),
            ("hill_enter", new[] { "toggle_001" }, 0.35f, 0.02f, 1f, 1, 0.5, true),
            ("hill_capture", new[] { "confirmation_003" }, 0.5f, 0f, 1f, 1, 0.5, true),
            ("ui_click", new[] { "click_001", "click3" }, 0.35f, 0.04f, 1f, 2, 0.03, true),
            ("berserk_lock", new[] { "impactPunch_heavy_002", "impactPlate_heavy_001" }, 0.8f, 0.04f, 0.6f, 1, 1.0, true),
        };

        private readonly Dictionary<string, Cue> _cues = new Dictionary<string, Cue>();
        private readonly Voice[] _voices = new Voice[Voices];
        private readonly GameObject _root;
        private readonly GameConfig _cfg;
        private double _now;
        private float _listenerX, _listenerZ;
        private Vector3 _camRight = Vector3.right;
        private double _nextStep, _nextGatherTick;
        private bool _wasOnHill;
        private static CombatAudio _instance;

        public AudioMixer Mixer { get; private set; }
        public AudioMixerGroup SfxGroup { get; private set; }
        public AudioMixerGroup MusicGroup { get; private set; }
        /// <summary>Clips that loaded (42 when the CMSfx folder is complete).</summary>
        public int ClipsLoaded { get; private set; }
        /// <summary>Plays started and plays refused by a cap or a cooldown (tests, budget).</summary>
        public int Played { get; private set; }
        public int Refused { get; private set; }
        public int ActiveVoices
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Voices; i++) if (_voices[i].Src != null && _voices[i].Src.isPlaying) n++;
                return n;
            }
        }

        public CombatAudio(Transform parent, GameConfig cfg)
        {
            _cfg = cfg;
            _instance = this;
            Mixer = Resources.Load<AudioMixer>(MixerResource);
            if (Mixer != null)
            {
                AudioMixerGroup[] g = Mixer.FindMatchingGroups("Sfx");
                SfxGroup = g.Length > 0 ? g[0] : null;
                g = Mixer.FindMatchingGroups("Music");
                MusicGroup = g.Length > 0 ? g[0] : null;
            }
            var byName = new Dictionary<string, AudioClip>();
            foreach (AudioClip c in Resources.LoadAll<AudioClip>(ClipFolder)) byName[c.name] = c;
            var loaded = new HashSet<string>();
            foreach (var row in Table)
            {
                var cue = new Cue { Id = row.Id, Files = row.Files, Gain = row.Gain, Jitter = row.Jitter, Rate = row.Rate, MaxVoices = row.Max, Cooldown = row.Cd, Ui = row.Ui };
                var clips = new List<AudioClip>();
                foreach (string f in row.Files)
                    if (byName.TryGetValue(f, out AudioClip c)) { clips.Add(c); loaded.Add(f); }
                cue.Clips = clips.ToArray();
                _cues[row.Id] = cue;
            }
            ClipsLoaded = loaded.Count;
            _root = new GameObject("Fx Audio");
            _root.transform.SetParent(parent, false);
            for (int i = 0; i < Voices; i++)
            {
                AudioSource s = _root.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f; // falloff and pan are computed here from the top-down camera, not from the 3D listener
                s.outputAudioMixerGroup = SfxGroup;
                _voices[i].Src = s;
            }
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null && UnityEngine.Camera.main != null)
                UnityEngine.Camera.main.gameObject.AddComponent<AudioListener>();
        }

        public void Dispose()
        {
            if (_instance == this) _instance = null;
            if (_root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);
        }

        public Cue Find(string id) => _cues.TryGetValue(id, out Cue c) ? c : null;

        /// <summary>Plays a UI cue ("ui_click", "hill_capture", ...) for other systems (menus, settings). No-op without a match.</summary>
        public static void PlayUi(string id) => _instance?.Play(id, 0, 0, true, 1f, 0);

        public void Update(double now, World w)
        {
            _now = now;
            if (w.ActorCount > 0) { _listenerX = (float)w.X[0]; _listenerZ = (float)w.Z[0]; }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null) _camRight = cam.transform.right;
            if (w.ActorCount == 0) return;
            Actor me = w.Actors[0];
            if (!me.Alive) return;
            // Footsteps, local player only, stride from sim speed and sim time; surface from the cell under them.
            double speed = Math.Sqrt(w.Vx[0] * w.Vx[0] + w.Vz[0] * w.Vz[0]);
            if (speed > 1.2 && now >= _nextStep)
            {
                _nextStep = now + 1.0 / Math.Max(1.6, speed * 0.62);
                Play(Surface(w), w.X[0], w.Z[0], true, 1f, w.Tick);
            }
            if (me.GatherProgress > 0 && now >= _nextGatherTick)
            {
                _nextGatherTick = now + 0.8;
                Play("gather_tick", w.X[0], w.Z[0], true, 1f, w.Tick);
            }
            HillDef hill = w.Map.Hill;
            double hx = w.X[0] - hill.X, hz = w.Z[0] - hill.Z;
            bool on = hx * hx + hz * hz <= hill.R * hill.R;
            if (on && !_wasOnHill) Play("hill_enter", 0, 0, true, 1f, w.Tick);
            _wasOnHill = on;
        }

        private static string Surface(World w)
        {
            MapDef map = w.Map;
            int cx = (int)Math.Floor(w.X[0]), cz = (int)Math.Floor(w.Z[0]);
            byte c = map.Cells != null && cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height ? map.Cells[cz * map.Width + cx] : Cell.Floor;
            if (c == Cell.Bridge || c == Cell.Barricade) return "footstep_wood";
            PadDef pad = map.Pads[(int)w.Actors[0].Team];
            double dx = w.X[0] - pad.X, dz = w.Z[0] - pad.Z;
            return dx * dx + dz * dz < (pad.R + 2) * (pad.R + 2) ? "footstep_concrete" : "footstep_grass";
        }

        public void OnFx(ref FxEvent e, World w)
        {
            double t = e.Tick / w.Config.SimHz;
            _now = Math.Max(_now, t);
            switch (e.Kind)
            {
                case FxKind.Hit:
                    Play(e.B == Damage.KaijuSource ? "kaiju_melee" : e.Cls == ClassId.Warrior ? "hit_plate" : "hit_punch", e.X, e.Z, e.Local, 1f, e.Tick);
                    break;
                case FxKind.Block: Play("block", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.Death:
                    Play("death", e.X, e.Z, e.Local, 1f, e.Tick);
                    Play(e.Cls == ClassId.Warrior ? "hit_plate" : "hit_punch", e.X, e.Z, e.Local, 0.8f, e.Tick);
                    break;
                case FxKind.Swing: Play("swing", e.X, e.Z, e.Local, e.Cls == ClassId.Miner ? 0.85f : 1f, e.Tick); break;
                case FxKind.Respawn: Play("respawn", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.KaijuHit: Play("hit_punch", e.X, e.Z, false, 0.85f, e.Tick); break;
                case FxKind.KaijuSlam: Play("kaiju_slam", e.X, e.Z, true, 1f, e.Tick); break;
                case FxKind.KaijuRoar:
                case FxKind.Hatch: Play("roar", e.X, e.Z, true, 1f, e.Tick); break;
                case FxKind.BerserkLock: Play("berserk_lock", e.X, e.Z, true, 1f, e.Tick); break;
                case FxKind.Gather: Play("gather_done", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.DeliverBiomass: Play("deliver_biomass", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.DeliverCorruption: Play("deliver_corruption", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.BarricadePlaced: Play("barricade_place", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.BarricadeRefused: if (e.Local) Play("barricade_invalid", e.X, e.Z, true, 1f, e.Tick); break;
                case FxKind.BarricadeBroken: Play("barricade_break", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.WallCrack: Play("wall_crack", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.WallBreak: Play("wall_break", e.X, e.Z, e.Local, 1f, e.Tick); break;
                case FxKind.HillChange: Play("hill_capture", e.X, e.Z, true, 1f, e.Tick); break;
                case FxKind.Explosion:
                {
                    // v2: no new files (the 42 credited sounds stay the set): the slam thump pitched per kind, layered
                    // with the wall break crunch. WallBreak is the crunch alone.
                    var kind = (ExplosionKind)Mathf.Clamp(e.B, 0, (int)ExplosionKind.KaijuSlam);
                    if (kind != ExplosionKind.WallBreak)
                        Play("kaiju_slam", e.X, e.Z, false, kind == ExplosionKind.Mine ? 0.95f : kind == ExplosionKind.Barrel ? 0.7f : 0.8f, e.Tick);
                    Play("wall_break", e.X, e.Z, false, kind == ExplosionKind.WallBreak ? 1f : 0.85f, e.Tick);
                    break;
                }
            }
        }

        /// <summary>Distance gain from the local player: 1 within NearDistance, 0 at FarDistance (smooth).</summary>
        public static float Falloff(float d)
        {
            float t = Mathf.Clamp01((d - NearDistance) / (FarDistance - NearDistance));
            return (1f - t) * (1f - t);
        }

        /// <summary>Start a cue if its cooldown, voice cap and the falloff allow it; returns true when a voice started.</summary>
        public bool Play(string id, double x, double z, bool local, float rateScale, int tick)
        {
            if (!_cues.TryGetValue(id, out Cue cue)) return false;
            float gain = cue.Gain;
            float pan = 0f;
            if (!cue.Ui && !local)
            {
                float dx = (float)x - _listenerX, dz = (float)z - _listenerZ;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float f = Falloff(d);
                if (f <= 0.02f) { Refused++; return false; }
                gain *= f;
                Vector3 dir = SimSpace.ToWorld(dx, 0, dz);
                pan = d > 0.5f ? Mathf.Clamp(Vector3.Dot(dir / d, _camRight), -1f, 1f) * 0.6f : 0f;
            }
            if (!local && _now - cue.LastPlay < cue.Cooldown) { Refused++; return false; }
            if (cue.Clips == null || cue.Clips.Length == 0) { Refused++; return false; }
            int active = 0, free = -1, weakest = -1;
            float weakestP = float.MaxValue;
            float prio = local ? 10f + gain : gain;
            for (int i = 0; i < Voices; i++)
            {
                AudioSource s = _voices[i].Src;
                bool playing = s != null && s.isPlaying;
                if (!playing) { if (free < 0) free = i; continue; }
                if (_voices[i].Cue == cue) active++;
                if (_voices[i].Priority < weakestP) { weakestP = _voices[i].Priority; weakest = i; }
            }
            if (active >= cue.MaxVoices && !local) { Refused++; return false; }
            int slot = free >= 0 ? free : weakestP < prio ? weakest : -1;
            if (slot < 0) { Refused++; return false; }
            AudioClip clip = cue.Clips[cue.Next++ % cue.Clips.Length];
            uint h = FxScene.Hash((uint)tick, (uint)id.Length, (uint)cue.Next);
            float jitter = ((h & 0xFFFF) / 65535f * 2f - 1f) * cue.Jitter;
            AudioSource src = _voices[slot].Src;
            src.Stop();
            src.clip = clip;
            src.pitch = cue.Rate * rateScale * (1f + jitter);
            src.volume = Mathf.Clamp01(gain * (local ? 1.1f : 1f));
            src.panStereo = pan;
            src.Play();
            _voices[slot].Cue = cue;
            _voices[slot].Priority = prio;
            cue.LastPlay = _now;
            Played++;
            return true;
        }
    }
}
