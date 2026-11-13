using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Runtime.View.Reactions;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>
    /// Every game sound outside music, announcer and menu clicks: the one player for SoundTable. Self-installed on the
    /// persistent "CM Audio" object (AudioHost) after the first scene loads, the way the pause menu installs itself, so
    /// GameBootstrap is never edited.
    /// <list type="bullet">
    /// <item>Event driven: SoundFeed decodes each sim tick's event ring (SimDriverBehaviour.Ticked); footsteps come from the
    /// animation pass's Footfalls sink; Kaiju steps, launched bodies and the low-frequency layer from the world-force bus
    /// (WorldForceBus.Active); stingers from big moments (BigMomentsAudio.Sink); pings and the map through UiSound.Play(string);
    /// relics and rewards from their events. Nothing polls the sim for changes.</item>
    /// <item>A pool of 28 one-shot voices (each on its own child with a low-pass filter), 4 fuse loops, wind, water and berserk
    /// loops and 2 sub voices, all made at load. VoicePlanner caps voices per cue, applies cooldowns, caps new voices per
    /// frame and steals by priority (the player's own actions and nearby threats win).</item>
    /// <item>Distance attenuation, stereo pan, low-pass and blast delay from the gameplay camera's ground focus (SoundSpace).</item>
    /// <item>Ducking under announcer lines and big-moment takeovers (DuckMix); crowd gain when many voices play.</item>
    /// <item>Low-frequency layer for big impacts: full when camera shake is off (it replaces the shake), softer with shake on.</item>
    /// <item>Captions for important non-speech cues while the Access tab's SUBTITLES line is on.</item>
    /// <item>Silent while muted (master or effects volume at zero); one-shots stop and loops pause with the pause menu.</item>
    /// </list>
    /// Missing recorded sounds are synthesised at load (SoundSynth) so every cue is audible. View only; never writes the sim.
    /// <para>Public API: <see cref="PlayNamed"/>, <see cref="PlayCueAt"/>, <see cref="Instance"/>, <see cref="CaptionShown"/>.</para>
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class SoundDirector : MonoBehaviour, ISoundSink, IFootfallSink, IWorldForceListener, IBigMomentsAudio
    {
        public const int Voices = 28, FuseVoices = 4, SubVoices = 2;
        public const string ClipFolder = "CMSfx";
        /// <summary>World sounds behind the title screen (the attract match) play at this gain.</summary>
        public const float TitleGain = 0.5f;
        /// <summary>Story film: while the intro plays, the backdrop match's world sounds and wind sit under the film at this
        /// gain (1 = untouched). Set by StoryIntro; view only.</summary>
        public static float FilmWorldGain = 1f;
        /// <summary>The sub layer's gain while camera shake is on (it adds to the shake instead of replacing it).</summary>
        public const float SubWithShake = 0.55f;

        public static SoundDirector Instance { get; private set; }
        /// <summary>Raised with the caption text when an important non-speech cue is captioned.</summary>
        public static event Action<string> CaptionShown;

        private AudioClip[][] _clips;
        private int[] _cursor;
        private AudioSource[] _src;
        private AudioLowPassFilter[] _lpf;
        private float[] _base;
        private bool[] _own;
        private SoundBus[] _busOf;
        private VoicePlanner _plan;

        private AudioSource _wind, _water, _berserk;
        private AudioLowPassFilter _berserkLpf;
        private readonly AudioSource[] _fuse = new AudioSource[FuseVoices];
        private readonly AudioLowPassFilter[] _fuseLpf = new AudioLowPassFilter[FuseVoices];
        private readonly int[] _fuseSlot = new int[FuseVoices], _fuseEnd = new int[FuseVoices], _fuseCue = new int[FuseVoices];
        private readonly double[] _fuseX = new double[FuseVoices], _fuseZ = new double[FuseVoices];
        private int _berserkCue = -1;
        private readonly AudioSource[] _sub = new AudioSource[SubVoices];
        private readonly double[] _subEnds = new double[SubVoices];
        private double[] _subLast;

        private SimDriverBehaviour _driver, _subscribedDriver;
        private HumanInputSource _input;
        private WorldForceBus _forceBus;
        private readonly SoundFeed _feed = new SoundFeed(256);
        private int _lookupFrames;
        private bool _paused, _muted, _title;
        private float _lx, _lz, _rx = 1f, _rz;
        private float _duckSfx = 1f, _duckAmb = 1f, _duckOwn = 1f;
        private double _momentUntil, _levelUpAt = double.MaxValue;
        private uint _serial;

        private const int MaxFalls = 64;
        private readonly float[] _fallX = new float[MaxFalls], _fallZ = new float[MaxFalls];
        private int _fallCount, _fallTries;
        private bool _fallsFound;
        private double _nextBird;

        private SoundCaptions _captions;

        /// <summary>Starts, refusals and clips loaded since start (tests, budget report).</summary>
        public int Played { get; private set; }
        public int Refused { get; private set; }
        public int ClipsLoaded { get; private set; }
        public SoundFeed Feed => _feed;
        public VoicePlanner Planner => _plan;
        public bool Muted => _muted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Instance != null) return;
            GameObject host = AudioHost.Root;
            if (host == null)
            {
                host = new GameObject("CM Sound");
                DontDestroyOnLoad(host);
            }
            if (host.GetComponent<SoundDirector>() == null) host.AddComponent<SoundDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            LoadClips();
            BuildVoices();
            _plan = VoicePlanner.FromTable(Voices);
            _subLast = new double[SoundTable.Cues.Length];
            for (int i = 0; i < _subLast.Length; i++) _subLast[i] = double.NegativeInfinity;
            for (int i = 0; i < FuseVoices; i++) _fuseSlot[i] = -1;
            _captions = new SoundCaptions(transform);
            ReadMute();
            GameSettings.Changed += ReadMute;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RelicField.PickedUp += OnRelic;
            MatchRewardTracker.RewardsReady += OnRewards;
            SystemEvents.AudioDeviceChanged += OnDeviceChanged;
            Footfalls.Register(this);
            BigMomentsAudio.Sink = this;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            GameSettings.Changed -= ReadMute;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RelicField.PickedUp -= OnRelic;
            MatchRewardTracker.RewardsReady -= OnRewards;
            SystemEvents.AudioDeviceChanged -= OnDeviceChanged;
            Footfalls.Unregister(this);
            if (ReferenceEquals(BigMomentsAudio.Sink, this)) BigMomentsAudio.Sink = new AudioBridge();
            if (_subscribedDriver != null) _subscribedDriver.Ticked -= OnTicked;
            if (_forceBus != null) _forceBus.Unsubscribe(this);
        }

        // ------------------------------------------------------------------ load

        private void LoadClips()
        {
            var byName = new Dictionary<string, AudioClip>();
            foreach (AudioClip c in Resources.LoadAll<AudioClip>(ClipFolder)) byName[c.name] = c;
            int n = SoundTable.Cues.Length;
            _clips = new AudioClip[n][];
            _cursor = new int[n];
            var list = new List<AudioClip>(8);
            for (int i = 0; i < n; i++)
            {
                CueDef c = SoundTable.Cues[i];
                list.Clear();
                foreach (string f in c.Files)
                    if (byName.TryGetValue(f, out AudioClip clip)) list.Add(clip);
                foreach (string s in c.Synth)
                {
                    SynthRecipe r = SynthRecipes.Find(s);
                    if (r == null) continue;
                    int variants = c.Variants > 0 ? c.Variants : r.Loop ? 1 : r.Length >= 1f ? 2 : 3;
                    for (int v = 0; v < variants; v++)
                    {
                        AudioClip clip = SoundSynth.Clip(s, v);
                        if (clip != null) list.Add(clip);
                    }
                }
                _clips[i] = list.ToArray();
                ClipsLoaded += _clips[i].Length;
            }
        }

        private void BuildVoices()
        {
            AudioMixerGroupRef();
            _src = new AudioSource[Voices];
            _lpf = new AudioLowPassFilter[Voices];
            _base = new float[Voices];
            _own = new bool[Voices];
            _busOf = new SoundBus[Voices];
            for (int i = 0; i < Voices; i++) _src[i] = Source("Voice " + i, false, out _lpf[i]);
            for (int i = 0; i < FuseVoices; i++) _fuse[i] = Source("Fuse " + i, true, out _fuseLpf[i]);
            _wind = Source("Wind", true, out _);
            _water = Source("Water", true, out _);
            _berserk = Source("Berserk", true, out _berserkLpf);
            for (int i = 0; i < SubVoices; i++) _sub[i] = Source("Sub " + i, false, out _);
        }

        private UnityEngine.Audio.AudioMixerGroup _group;

        private void AudioMixerGroupRef() => _group = GameSettings.EffectsGroup;

        private AudioSource Source(string name, bool loop, out AudioLowPassFilter lpf)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;   // falloff, pan and filtering come from the top-down camera here, not the 3D listener
            s.dopplerLevel = 0f;
            s.priority = 64;
            s.outputAudioMixerGroup = _group;
            lpf = go.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = SoundSpace.OpenCutoff;
            return s;
        }

        // ------------------------------------------------------------------ hooks

        private void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            _driver = null;
            _input = null;
            _lookupFrames = 0;
            _fallsFound = false;
            _fallTries = 0;
            _fallCount = 0;
            StopAll(true);
            _feed.Reset();
        }

        private void OnDeviceChanged()
        {
            // The device reset stopped every source; loops restart from LateUpdate.
            StopAll(false);
        }

        private void ReadMute()
        {
            bool m = GameSettings.MasterVolume <= 0.001f || GameSettings.EffectsVolume <= 0.001f;
            if (m && !_muted) StopAll(false);
            _muted = m;
        }

        private void OnRelic(int index) => PlayUiCue(SoundTable.EdgeCue(SoundEdge.RelicPickup), 1f);

        private void OnRewards(MatchRewards r)
        {
            if (r == null) return;
            if (r.Coins > 0) PlayUiCue(SoundTable.EdgeCue(SoundEdge.Coins), 1f);
            if (r.LevelsGained > 0) _levelUpAt = Time.unscaledTimeAsDouble + 0.7;
        }

        /// <summary>Plays the cue a hook name maps to (UiSound.Play(string), CombatAudio.PlayUi); false when the name has no cue.</summary>
        public static bool PlayNamed(string name)
        {
            int cue = SoundTable.NamedCue(name);
            if (cue < 0 || Instance == null) return false;
            Instance.PlayUiCue(cue, 1f);
            return true;
        }

        /// <summary>Plays a cue by id at a sim position (other systems, captures). False when refused or unknown.</summary>
        public static bool PlayCueAt(string id, double simX, double simZ, bool own = false)
        {
            int cue = SoundTable.IndexOf(id);
            return cue >= 0 && Instance != null && Instance.PlayCue(cue, simX, simZ, own, false, 1f, 1f, 0);
        }

        // ---- big moments (IBigMomentsAudio)
        public void OnTakeover(MomentId id, float seconds, bool playerOwned)
        {
            MusicDirector.Duck(seconds);
            _momentUntil = Math.Max(_momentUntil, Time.unscaledTimeAsDouble + seconds);
            PlayUiCue(SoundTable.MomentCue(id), 1f);
        }

        public void OnStinger(MomentId id) => PlayUiCue(SoundTable.IndexOf("sting_small"), 1f);

        // ---- footsteps (IFootfallSink)
        public void OnFootfall(in Footfall f)
        {
            if (f.Kind != FootfallKind.Step || _muted || _paused) return;
            World w = _driver != null ? _driver.World : null;
            if (w == null || f.Actor < 0 || f.Actor >= w.ActorCount) return;
            SimSpace.ToSim(f.Position, out double x, out double z);
            bool own = f.Actor == 0;
            if (!own)
            {
                float dx = (float)x - _lx, dz = (float)z - _lz;
                if (dx * dx + dz * dz > 28f * 28f) return; // beyond the Near range: skip the surface lookup
            }
            int[] steps = SoundTable.StepCues;
            int cue = steps[StepIndex(w, f.Actor, x, z)];
            if (cue < 0) return;
            ClassId cls = w.Actors[f.Actor].Cls;
            bool heavy = cls == ClassId.Warrior || cls == ClassId.Miner;
            bool light = cls == ClassId.Healer || cls == ClassId.Builder;
            PlayCue(cue, x, z, own, false, light ? 0.85f : 1f, heavy ? 0.9f : light ? 1.08f : 1f, w.Tick);
            if (heavy && steps.Length > 5) PlayCue(steps[5], x, z, own, false, 0.5f, 1f, w.Tick);
        }

        /// <summary>Index into SoundTable.StepCues: grass, wood, stone, dirt, water (team pads are stone).</summary>
        public static int StepIndex(World w, int actor, double x, double z)
        {
            MapDef map = w.Map;
            if (map == null || map.Cells == null) return 0;
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return 0;
            switch (WorldForceBus.SurfaceOf(map.Cells[cz * map.Width + cx]))
            {
                case Surface.Wood: return 1;
                case Surface.Stone: return 2;
                case Surface.Dirt: return 3;
                case Surface.Water: return 4;
            }
            if (map.Pads != null && actor >= 0 && actor < w.ActorCount)
            {
                int t = (int)w.Actors[actor].Team;
                if (t >= 0 && t < map.Pads.Length && map.Pads[t] != null)
                {
                    PadDef pad = map.Pads[t];
                    double dx = x - pad.X, dz = z - pad.Z;
                    if (dx * dx + dz * dz < (pad.R + 2) * (pad.R + 2)) return 2;
                }
            }
            return 0;
        }

        // ---- world forces (IWorldForceListener)
        public void OnForce(ref WorldForce f)
        {
            if (_muted || _paused) return;
            World w = _driver != null ? _driver.World : null;
            if (w == null) return;
            int cue = SoundTable.ForceCue(f.Source, out Pick pick);
            if (cue >= 0)
            {
                bool corpse = f.Actor >= 0 && f.Actor < w.ActorCount && !w.Actors[f.Actor].Alive;
                if (pick != Pick.CorpseOnly || corpse) PlayCue(cue, f.X, f.Z, f.Actor == 0, false, 1f, 1f, f.Tick);
            }
            int sub = SoundTable.ForceSub(f.Source);
            if (sub < 0 || w.ActorCount == 0) return;
            float felt = CameraResponse.Felt(in f, w.X[0], w.Z[0]);
            bool shakeOff = FeelGate.ShakeScale <= 0f;
            if (felt >= (shakeOff ? CameraResponse.BassThreshold : CameraResponse.BassThreshold * 1.6f)) PlaySub(sub, felt, shakeOff);
        }

        // ---- the sim feed (ISoundSink)
        public void Play(in SoundRequest r)
        {
            CueDef c = SoundTable.Cue(r.Cue);
            if (c == null) return;
            if (!PlayCue(r.Cue, r.X, r.Z, r.Own, r.Threat, r.Gain, r.Rate, r.Tick)) return;
            if (c.Layer != null) PlayCue(SoundTable.IndexOf(c.Layer), r.X, r.Z, r.Own, r.Threat, r.Gain * c.LayerGain, r.Rate, r.Tick);
        }

        public void Fuse(int slot, double x, double z, int endTick, int cue)
        {
            if (_muted || cue < 0 || _clips[cue].Length == 0) return;
            int k = -1, oldest = 0;
            for (int i = 0; i < FuseVoices; i++)
            {
                if (_fuseSlot[i] == slot) { k = i; break; }
                if (k < 0 && _fuseSlot[i] < 0) k = i;
                if (_fuseEnd[i] < _fuseEnd[oldest]) oldest = i;
            }
            if (k < 0) k = oldest;
            _fuseSlot[k] = slot;
            _fuseEnd[k] = endTick;
            _fuseCue[k] = cue;
            _fuseX[k] = x;
            _fuseZ[k] = z;
            AudioSource s = _fuse[k];
            s.clip = _clips[cue][0];
            s.volume = 0f;
            if (!_paused) s.Play();
            CueDef c = SoundTable.Cue(cue);
            float g = Audible(c, x, z, false, out float pan, out _);
            if (c.Caption != null && g >= 0.25f && !_title) _captions.Show(cue, pan, Time.unscaledTime, CaptionShown);
        }

        public void Berserk(bool on, int cue)
        {
            _berserkCue = on ? cue : -1;
            if (!on) _berserk.Stop();
        }

        private void OnTicked(World w)
        {
            _feed.OnTick(w, this);
            for (int i = 0; i < FuseVoices; i++)
                if (_fuseSlot[i] >= 0 && w.Tick >= _fuseEnd[i])
                {
                    _fuseSlot[i] = -1;
                    _fuse[i].Stop();
                }
        }

        // ------------------------------------------------------------------ playing

        private void PlayUiCue(int cue, float gain)
        {
            if (cue < 0) return;
            PlayCue(cue, _lx, _lz, true, false, gain, 1f, Time.frameCount);
        }

        /// <summary>Gain after distance (0 = inaudible), pan and the low-pass cutoff for a cue at sim (x, z).</summary>
        private float Audible(CueDef c, double x, double z, bool own, out float pan, out float cutoff)
        {
            pan = 0f;
            cutoff = SoundSpace.OpenCutoff;
            if (c.Range == SoundRange.Ui) return 1f;
            float dx = (float)x - _lx, dz = (float)z - _lz;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            SoundSpace.Range(c.Range, out float near, out float far);
            float g = SoundSpace.Gain(d, near, far);
            if (own) g = Mathf.Max(g, 0.8f);
            // Sim z runs south; world z = -sim z (SimSpace).
            pan = SoundSpace.Pan(dx, -dz, _rx, _rz);
            cutoff = SoundSpace.LowPass(d, near, far);
            return g;
        }

        private bool PlayCue(int cue, double x, double z, bool own, bool threat, float gainScale, float rateScale, int tick)
        {
            CueDef c = SoundTable.Cue(cue);
            if (c == null || c.Loop) return false;
            bool uiBus = c.Bus == SoundBus.Ui || c.Bus == SoundBus.Stinger;
            if (_muted || (_paused && !uiBus)) { Refused++; return false; }
            AudioClip[] clips = _clips[cue];
            if (clips.Length == 0) { Refused++; return false; }
            if (c.Bus == SoundBus.Sub) return PlaySub(cue, gainScale, true);

            float dist = 0f;
            if (c.Range != SoundRange.Ui)
            {
                float dx = (float)x - _lx, dz = (float)z - _lz;
                dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (c.FarCue != null && dist > c.FarDistance)
                {
                    int far = SoundTable.IndexOf(c.FarCue);
                    if (far >= 0 && far != cue) return PlayCue(far, x, z, own, threat, gainScale, rateScale, tick);
                }
            }
            float g = Audible(c, x, z, own, out float pan, out float cutoff);
            if (g < 0.02f) { Refused++; return false; }
            float delay = c.Range >= SoundRange.Far ? SoundSpace.Delay(dist) : 0f;

            uint seed = (uint)tick * 0x9E3779B1u ^ (uint)cue * 0x85EBCA77u ^ ++_serial * 0xC2B2AE3Du;
            float pj = (SoundSynth.Unit(seed, 1) * 2f - 1f) * c.PitchJitter;
            float vj = (SoundSynth.Unit(seed, 2) * 2f - 1f) * c.VolJitter;
            float pitch = Mathf.Clamp(c.Rate * rateScale * (1f + pj), 0.3f, 3f);
            // Never the same variation twice in a row.
            int n = clips.Length;
            int idx = n == 1 ? 0 : (_cursor[cue] + 1 + (int)(SoundSynth.Unit(seed, 3) * (n - 1) * 0.999f)) % n;
            _cursor[cue] = idx;
            AudioClip clip = clips[idx];
            double now = Time.unscaledTimeAsDouble;
            float worldGain = (_title && !uiBus ? TitleGain : 1f) * (uiBus ? 1f : FilmWorldGain);
            float gain = Mathf.Clamp01(c.Gain * gainScale * g * (1f + vj) * worldGain * (own ? 1.1f : 1f));

            _plan.BeginFrame(Time.frameCount);
            int slot = _plan.Request(cue, SoundSpace.Priority(c.Priority, g, own, threat), now, clip.length / pitch + delay, own);
            if (slot < 0) { Refused++; return false; }
            AudioSource s = _src[slot];
            s.Stop();
            s.clip = clip;
            s.pitch = pitch;
            s.panStereo = pan;
            _lpf[slot].cutoffFrequency = cutoff;
            _base[slot] = gain;
            _own[slot] = own;
            _busOf[slot] = c.Bus;
            s.volume = gain * DuckFor(c.Bus, own);
            if (delay > 0.005f) s.PlayDelayed(delay);
            else s.Play();
            Played++;
            if (c.Caption != null && g >= 0.15f && !_title) _captions.Show(cue, pan, Time.unscaledTime, CaptionShown);
            return true;
        }

        private bool PlaySub(int cue, float intensity, bool shakeOff)
        {
            if (_muted || _paused || cue < 0) return false;
            AudioClip[] clips = _clips[cue];
            if (clips.Length == 0) return false;
            double now = Time.unscaledTimeAsDouble;
            CueDef c = SoundTable.Cue(cue);
            if (now - _subLast[cue] < c.Cooldown) { Refused++; return false; }
            int k = _subEnds[0] <= _subEnds[1] ? 0 : 1;
            AudioSource s = _sub[k];
            s.Stop();
            s.clip = clips[0];
            s.pitch = 1f;
            s.volume = Mathf.Clamp01(c.Gain * Mathf.Clamp01(intensity) * (shakeOff ? 1f : SubWithShake) * _duckSfx);
            s.Play();
            _subEnds[k] = now + clips[0].length;
            _subLast[cue] = now;
            Played++;
            return true;
        }

        private float DuckFor(SoundBus bus, bool own)
        {
            switch (bus)
            {
                case SoundBus.Ui:
                case SoundBus.Stinger: return 1f;
                case SoundBus.Ambience: return _duckAmb;
                default: return own ? _duckOwn : _duckSfx;
            }
        }

        private void StopAll(bool loopsToo)
        {
            if (_src == null) return;
            for (int i = 0; i < Voices; i++) _src[i].Stop();
            for (int i = 0; i < SubVoices; i++) { _sub[i].Stop(); _subEnds[i] = 0; }
            _plan?.ReleaseAll();
            for (int i = 0; i < FuseVoices; i++) { _fuse[i].Stop(); _fuseSlot[i] = -1; }
            _wind.Stop();
            _water.Stop();
            _berserk.Stop();
            if (loopsToo) _berserkCue = -1;
        }

        // ------------------------------------------------------------------ per frame (mixing only: no allocation)

        private void LateUpdate() { var c = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateBody(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Sound, c); }

        private void LateUpdateBody()
        {
            if (_driver == null && (_lookupFrames++ % 15) == 0)
            {
                _driver = FindAnyObjectByType<SimDriverBehaviour>();
                _input = _driver != null ? _driver.GetComponent<HumanInputSource>() : null;
            }
            if (_driver != _subscribedDriver)
            {
                if (_subscribedDriver != null) _subscribedDriver.Ticked -= OnTicked;
                _subscribedDriver = _driver;
                _feed.Reset();
                if (_driver != null) _driver.Ticked += OnTicked;
            }
            WorldForceBus bus = WorldForceBus.Active;
            if (bus != _forceBus)
            {
                if (_forceBus != null) _forceBus.Unsubscribe(this);
                _forceBus = bus;
                bus?.Subscribe(this);
            }
            World w = _driver != null ? _driver.World : null;
            _title = _driver != null && _driver.TryGetComponent(out TitleScreen _);

            bool paused = _input != null && _input.State.Paused;
            if (paused != _paused)
            {
                _paused = paused;
                if (paused)
                {
                    for (int i = 0; i < Voices; i++) if (_busOf[i] != SoundBus.Ui && _busOf[i] != SoundBus.Stinger) { _src[i].Stop(); _plan.Release(i); }
                    for (int i = 0; i < SubVoices; i++) _sub[i].Stop();
                    for (int i = 0; i < FuseVoices; i++) _fuse[i].Pause();
                    _wind.Pause();
                    _water.Pause();
                    _berserk.Pause();
                }
                else
                {
                    for (int i = 0; i < FuseVoices; i++) if (_fuseSlot[i] >= 0) _fuse[i].UnPause();
                    _wind.UnPause();
                    _water.UnPause();
                    _berserk.UnPause();
                }
            }

            UpdateListener(w);
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            bool announcer = Announcer.IsSpeaking;
            bool moment = Time.unscaledTimeAsDouble < _momentUntil;
            _duckSfx = DuckMix.Step(_duckSfx, DuckMix.Target(SoundBus.Sfx, false, announcer, moment), dt);
            _duckOwn = DuckMix.Step(_duckOwn, DuckMix.Target(SoundBus.Sfx, true, announcer, moment), dt);
            _duckAmb = DuckMix.Step(_duckAmb, DuckMix.Target(SoundBus.Ambience, false, announcer, moment), dt);

            if (!_muted)
            {
                double dnow = Time.unscaledTimeAsDouble;
                int busy = _plan.BusyCount(dnow);
                for (int i = 0; i < Voices; i++)
                {
                    if (!_plan.Busy(i, dnow)) continue;
                    _src[i].volume = _base[i] * DuckFor(_busOf[i], _own[i]) * SoundSpace.Crowd(busy, _own[i] || _busOf[i] != SoundBus.Sfx);
                }
                UpdateLoops(w);
                UpdateBirds(w);
                if (Time.unscaledTimeAsDouble >= _levelUpAt)
                {
                    _levelUpAt = double.MaxValue;
                    PlayUiCue(SoundTable.EdgeCue(SoundEdge.LevelUp), 1f);
                }
            }
            if (!_fallsFound && w != null && (Time.frameCount % 30) == 0 && _fallTries < 40) FindFalls(w);
            _captions.Update(now, _paused || _title);
        }

        private void UpdateListener(World w)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Transform t = cam.transform;
                Vector3 p = t.position, f = t.forward;
                Vector3 focus = f.y < -0.05f ? p + f * (-p.y / f.y) : p;
                SimSpace.ToSim(focus, out double sx, out double sz);
                _lx = (float)sx;
                _lz = (float)sz;
                Vector3 r = t.right;
                float len = Mathf.Sqrt(r.x * r.x + r.z * r.z);
                if (len > 1e-4f) { _rx = r.x / len; _rz = r.z / len; }
            }
            else if (w != null && w.ActorCount > 0)
            {
                _lx = (float)w.X[0];
                _lz = (float)w.Z[0];
            }
        }

        private void UpdateLoops(World w)
        {
            bool world = w != null && !_paused;
            int windCue = SoundTable.EdgeCue(SoundEdge.Wind), waterCue = SoundTable.EdgeCue(SoundEdge.Water);
            // Wind: always under the world.
            Loop(_wind, windCue, world, SoundTable.Cue(windCue).Gain * _duckAmb * (_title ? 0.7f : 1f) * FilmWorldGain, 0f);
            // Water: the nearest waterfall.
            float wg = 0f, wp = 0f;
            if (world && _fallCount > 0)
            {
                int best = -1;
                float bd = float.MaxValue;
                for (int i = 0; i < _fallCount; i++)
                {
                    float dx = _fallX[i] - _lx, dz = _fallZ[i] - _lz, d = dx * dx + dz * dz;
                    if (d < bd) { bd = d; best = i; }
                }
                float dist = Mathf.Sqrt(bd);
                wg = SoundSpace.Gain(dist, 4f, 26f) * SoundTable.Cue(waterCue).Gain * _duckAmb;
                wp = SoundSpace.Pan(_fallX[best] - _lx, -(_fallZ[best] - _lz), _rx, _rz);
            }
            Loop(_water, waterCue, world && _fallCount > 0, wg, wp);
            // Berserk: at the Kaiju.
            if (_berserkCue >= 0 && world)
            {
                CueDef c = SoundTable.Cue(_berserkCue);
                float g = Audible(c, w.Kaiju.X, w.Kaiju.Z, false, out float pan, out float cutoff);
                _berserkLpf.cutoffFrequency = cutoff;
                Loop(_berserk, _berserkCue, true, Mathf.Max(0.35f, g) * c.Gain * _duckSfx, pan);
            }
            else if (_berserk.isPlaying) _berserk.Stop();
            // Fuses: where they lie.
            for (int i = 0; i < FuseVoices; i++)
            {
                if (_fuseSlot[i] < 0) continue;
                CueDef c = SoundTable.Cue(_fuseCue[i]);
                float g = Audible(c, _fuseX[i], _fuseZ[i], false, out float pan, out float cutoff);
                _fuseLpf[i].cutoffFrequency = cutoff;
                _fuse[i].volume = g * c.Gain * _duckSfx;
                _fuse[i].panStereo = pan;
                if (!_fuse[i].isPlaying && world) _fuse[i].Play();
            }
        }

        private void Loop(AudioSource s, int cue, bool on, float volume, float pan)
        {
            if (!on || cue < 0 || _clips[cue].Length == 0)
            {
                if (s.isPlaying && !_paused) s.Stop();
                return;
            }
            if (s.clip != _clips[cue][0]) s.clip = _clips[cue][0];
            s.volume = volume;
            s.panStereo = pan;
            if (!s.isPlaying) s.Play();
        }

        private void UpdateBirds(World w)
        {
            if (w == null || _paused) return;
            double now = Time.unscaledTimeAsDouble;
            if (now < _nextBird) return;
            uint seed = ++_serial * 0x27D4EB2Fu;
            KaijuState k = w.Kaiju != null ? w.Kaiju.State : KaijuState.Egg;
            // Birds go quiet once the Kaiju is up and vanish in berserk.
            double gap = 3.0 + 6.0 * SoundSynth.Unit(seed, 1);
            if (k != KaijuState.Egg) gap *= 3.0;
            _nextBird = now + gap;
            if (k == KaijuState.Berserk || w.Match.Phase == MatchPhase.Ended) return;
            float ang = SoundSynth.Unit(seed, 2) * Mathf.PI * 2f, r = 6f + 10f * SoundSynth.Unit(seed, 3);
            int cue = SoundTable.EdgeCue(SoundSynth.Unit(seed, 4) < 0.7f ? SoundEdge.Bird : SoundEdge.BirdTrill);
            PlayCue(cue, _lx + Mathf.Cos(ang) * r, _lz + Mathf.Sin(ang) * r, false, false, 1f, 1f, w.Tick);
        }

        private void FindFalls(World w)
        {
            _fallTries++;
            WorldView view = FindAnyObjectByType<WorldView>();
            if (view == null || view.Plan == null) return;
            _fallCount = 0;
            List<WorldPlacement> ps = view.Plan.Placements;
            for (int i = 0; i < ps.Count && _fallCount < MaxFalls; i++)
            {
                if (!string.Equals(ps[i].Piece, "env_waterfall", StringComparison.Ordinal)) continue;
                _fallX[_fallCount] = (float)ps[i].SimX;
                _fallZ[_fallCount] = (float)ps[i].SimZ;
                _fallCount++;
            }
            _fallsFound = true;
        }
    }

    /// <summary>
    /// Captions for important non-speech cues (bottom centre, above the announcer's subtitle band): up to three chunky
    /// stickers in the HUD's Panel Navy and Warm Cream, each 2.4 s, the same cue at most every 4 s, with a side marker
    /// when the sound is off to the left or right. Shown while subtitles are on (the Access tab's SUBTITLES line). Text
    /// comes from the words pack (Resources Lore/sound-captions.json: { "captions": [ { "id", "text" } ] }) when present,
    /// else the table's built-in caption. Every string is built at load: showing one never allocates.
    /// </summary>
    internal sealed class SoundCaptions
    {
        public const int Lines = 3;
        public const float Life = 2.4f, RepeatGap = 4f, Fade = 0.15f, LineH = 44f, LineW = 440f, Gap = 6f;

        [Serializable] private sealed class Row { public string id; public string text; }
        [Serializable] private sealed class Pack { public Row[] captions; }

        private readonly Canvas _canvas;
        private readonly RectTransform _space;
        private readonly CanvasGroup[] _group = new CanvasGroup[Lines];
        private readonly Text[] _text = new Text[Lines];
        private readonly float[] _start = new float[Lines], _until = new float[Lines];
        private readonly int[] _cue = new int[Lines];
        private readonly string[][] _words;
        private readonly float[] _last;

        public SoundCaptions(Transform parent)
        {
            int n = SoundTable.Cues.Length;
            _words = new string[n][];
            _last = new float[n];
            var overrides = LoadPack();
            for (int i = 0; i < n; i++)
            {
                _last[i] = -100f;
                string t = SoundTable.Cues[i].Caption;
                if (t == null) continue;
                if (overrides != null && overrides.TryGetValue(SoundTable.Cues[i].Id, out string o) && !string.IsNullOrEmpty(o)) t = o;
                _words[i] = new[] { "< " + t, t, t + " >" };
            }
            for (int k = 0; k < Lines; k++) _cue[k] = -1;

            var go = new GameObject("Sound Captions Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 29;   // just under the announcer's subtitles (30)
            var root = (RectTransform)go.transform;
            _space = MenuUi.Rt(root, "Space");
            _space.anchorMin = _space.anchorMax = new Vector2(0.5f, 0.5f);
            _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);
            float baseY = HudLayout.RefH - HudLayout.Margin - 60f - 4f - Gap - LineH;   // above the subtitle band
            for (int k = 0; k < Lines; k++)
            {
                VectorShape panel = MenuUi.Panel(_space, "Caption " + k, (HudLayout.RefW - LineW) * 0.5f, baseY - k * (LineH + Gap), LineW, LineH);
                panel.OutlinePx = 3f;
                panel.RimPx = 2f;
                _group[k] = panel.gameObject.AddComponent<CanvasGroup>();
                _group[k].alpha = 0f;
                _group[k].interactable = false;
                _group[k].blocksRaycasts = false;
                _text[k] = MenuUi.Label(panel.transform, "Text", CMTokens.Ui, 22, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 0f, 0f, LineW, LineH, string.Empty, 2f);
            }
            _canvas.enabled = false;
        }

        private static Dictionary<string, string> LoadPack()
        {
            TextAsset ta = Resources.Load<TextAsset>("Lore/sound-captions");
            if (ta == null) return null;
            try
            {
                Pack p = JsonUtility.FromJson<Pack>(ta.text);
                if (p == null || p.captions == null) return null;
                var d = new Dictionary<string, string>();
                foreach (Row r in p.captions) if (r != null && r.id != null) d[r.id] = r.text;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SoundCaptions] words pack unreadable: " + e.Message);
                return null;
            }
        }

        public void Show(int cue, float pan, float now, Action<string> raised)
        {
            if (cue < 0 || cue >= _words.Length || _words[cue] == null) return;
            if (now - _last[cue] < RepeatGap) return;
            if (!Announcer.SubtitlesEnabled) return;
            _last[cue] = now;
            int k = 0;
            for (int i = 0; i < Lines; i++)
            {
                if (_until[i] <= now) { k = i; break; }
                if (_until[i] < _until[k]) k = i;
            }
            string s = _words[cue][pan < -0.25f ? 0 : pan > 0.25f ? 2 : 1];
            HudText.Set(_text[k], s);
            _cue[k] = cue;
            _start[k] = now;
            _until[k] = now + Life;
            raised?.Invoke(s);
        }

        public void Update(float now, bool hidden)
        {
            bool any = false;
            for (int k = 0; k < Lines; k++)
            {
                float a = 0f;
                if (!hidden && now < _until[k])
                {
                    a = Mathf.Min(Mathf.Clamp01((now - _start[k]) / Fade), Mathf.Clamp01((_until[k] - now) / Fade));
                    any = true;
                }
                if (_group[k].alpha != a) _group[k].alpha = a;
            }
            if (_canvas.enabled != any) _canvas.enabled = any;
            if (!any) return;
            float s = HudLayout.Scale(Screen.width, Screen.height);
            if (s > 0f && Mathf.Abs(_space.localScale.x - s) > 1e-4f) _space.localScale = new Vector3(s, s, 1f);
        }
    }
}
