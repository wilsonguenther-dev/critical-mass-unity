using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Sound coverage (2026-11-12): combat sound moved to the one sound player, Audio.Sound.SoundDirector, which decodes
    /// every sim event through Audio.Sound.SoundTable (the 42 Kenney CC0 sounds in Assets/_Game/Audio/Resources/CMSfx plus
    /// procedural cues for everything the files do not cover). This class stays as the effects lane's IFxSink so CombatFx,
    /// FxGallery and tests keep compiling: it plays nothing itself (the director already hears every decoded event, so
    /// playing here would double each sound); it forwards PlayUi to the director.
    /// </summary>
    public sealed class CombatAudio : IFxSink, IDisposable
    {
        public const float NearDistance = 9f, FarDistance = 36f;
        public const string MixerResource = CriticalMass.Runtime.Menu.GameSettings.MixerResource, ClipFolder = SoundDirector.ClipFolder;

        /// <summary>The recorded files the sound table uses, one row per cue (tests: all 42 credited sounds have a cue).</summary>
        public static readonly (string Id, string[] Files)[] Table = BuildTable();

        private static (string Id, string[] Files)[] BuildTable()
        {
            var rows = new List<(string, string[])>();
            // b15: the original Skybreak effects (sb_*, Resources CMSfxScore) are not library files
            foreach (CueDef c in SoundTable.Cues) if (c.Files.Length > 0 && !SoundTable.IsScoreFile(c.Files[0])) rows.Add((c.Id, c.Files));
            return rows.ToArray();
        }

        private readonly GameObject _root;

        public AudioMixer Mixer { get; private set; }
        public AudioMixerGroup SfxGroup { get; private set; }
        public AudioMixerGroup MusicGroup { get; private set; }
        /// <summary>Kept for callers that report it: the director's loaded clip count (0 before it exists).</summary>
        public int ClipsLoaded => SoundDirector.Instance != null ? SoundDirector.Instance.ClipsLoaded : 0;
        public int Played => SoundDirector.Instance != null ? SoundDirector.Instance.Played : 0;
        public int Refused => SoundDirector.Instance != null ? SoundDirector.Instance.Refused : 0;

        public CombatAudio(Transform parent, GameConfig cfg)
        {
            Mixer = CriticalMass.Runtime.Menu.GameSettings.Mixer;
            SfxGroup = CriticalMass.Runtime.Menu.GameSettings.EffectsGroup;
            MusicGroup = CriticalMass.Runtime.Menu.GameSettings.MusicGroup;
            _root = new GameObject("Fx Audio");
            _root.transform.SetParent(parent, false);
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null && UnityEngine.Camera.main != null)
                UnityEngine.Camera.main.gameObject.AddComponent<AudioListener>();
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);
        }

        /// <summary>Plays a named UI cue ("ui_click", "ability_denied", "hill_capture", ...) through the sound director.</summary>
        public static void PlayUi(string id) => SoundDirector.PlayNamed(id);

        /// <summary>Per-frame hook CombatFx calls (the director mixes in its own LateUpdate). b15 wave two: the one combat-feel
        /// beat the sim does not say, through Audio.Sound.CombatSoundHooks: <see cref="WindupWarning"/>.</summary>
        public void Update(double now, World w) => WindupWarning(w);

        /// <summary>Per actor: the SwingTick whose wind-up already played its warning (fixed pool, no allocation per frame).</summary>
        private readonly int[] _windupCued = new int[256];
        /// <summary>Gain of the wind-up warning (under a landed hit; the cue's own voice cap and cooldown still apply).</summary>
        public const float WindupWarningGain = 0.55f;

        /// <summary>
        /// b15 combat feel x Skybreak sound: an enemy's wind-up that will reach the local fighter (the same reach + arc test as
        /// FxScene.DrawSwingTelegraphs' orange "coming at you" wedge) plays one swing cue, once per wind-up, at the swinger, by
        /// weight class (Warrior / Miner heavy, Builder / Healer light, others mid). The release and the hit keep their sim-event
        /// sounds (SoundFeed), so nothing plays twice. Only while CombatFeel is on (its wind-ups are long enough to warn).
        /// </summary>
        private void WindupWarning(World w)
        {
            if (w == null || !CombatFeel.On(w.Config)) return;
            int me = CriticalMass.Runtime.LocalSeat.Actor;
            if (me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return;
            Actor mine = w.Actors[me];
            int n = Math.Min(w.ActorCount, _windupCued.Length);
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (i == me || !a.Alive || a.Team == mine.Team || !a.SwingPending || a.SwingWindup <= 0) continue;
                if (_windupCued[i] == a.SwingTick + 1) continue;
                if (a.SwingTick + a.SwingWindup - w.Tick <= 0) continue;
                ClassConfig cc = w.Config.Class(a.Cls);
                double dx = w.X[me] - w.X[i], dz = w.Z[me] - w.Z[i], d = Math.Sqrt(dx * dx + dz * dz);
                if (d > cc.AttackRange + w.Config.Class(mine.Cls).Radius) continue;
                double off = Math.Abs(Math.IEEERemainder(Math.Atan2(dz, dx) - a.SwingFacing, Math.PI * 2));
                if (off > cc.AttackArcDeg * 0.5 * Math.PI / 180) continue;
                _windupCued[i] = a.SwingTick + 1;
                CombatCue cue = a.Cls == ClassId.Warrior || a.Cls == ClassId.Miner ? CombatCue.SwingHeavy
                              : a.Cls == ClassId.Builder || a.Cls == ClassId.Healer ? CombatCue.SwingLight : CombatCue.SwingMid;
                CombatSoundHooks.Play(cue, w.X[i], w.Z[i], false, WindupWarningGain);
            }
        }

        /// <summary>
        /// Nothing: sim events reach the director through its own ring decode, and view-injected effects (Effects.Splash,
        /// Explode, Land) reach it as world forces (SoundTable.Forces), so playing here would double them.
        /// </summary>
        public void OnFx(ref FxEvent e, World w) { }

        /// <summary>Distance gain from the listener: 1 within NearDistance, 0 at FarDistance (SoundSpace's falloff).</summary>
        public static float Falloff(float d) => SoundSpace.Gain(d, NearDistance, FarDistance);
    }
}
