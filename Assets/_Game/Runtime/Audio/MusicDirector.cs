using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Audio
{
    /// <summary>
    /// The one persistent audio object ("CM Audio", kept across scene loads): the game's AudioListener when the scene
    /// has none, the music director, the announcer and the UI sound binder. Created before the first scene loads, the
    /// way the pause menu installs itself (a RuntimeInitializeOnLoadMethod), so GameBootstrap is never edited.
    /// Every source routes through the settings mixer (Resources/Audio/CMMixer, GameSettings.MusicGroup and
    /// GameSettings.EffectsGroup); this adds no mixer of its own.
    /// </summary>
    public static class AudioHost
    {
        static GameObject _root;

        public static GameObject Root => _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (_root != null) return;
            _root = new GameObject("CM Audio");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            // CM b13 S0: the listener sits on its own child (no AudioSource beside it), so the master bus filter on it
            // (MasterBusFilter, OnAudioFilterRead) processes the whole mix; Unity refuses a filter on a GameObject that holds
            // both a listener and sources.
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
            {
                var ear = new GameObject("CM Listener");
                ear.transform.SetParent(_root.transform, false);
                ear.AddComponent<AudioListener>();
            }
            _root.AddComponent<MusicDirector>();
            _root.AddComponent<Announcer>();
            _root.AddComponent<UiSoundBinder>();
            _root.AddComponent<MasterBusKeeper>();   // CM b13 S0: -1 dB headroom + a -1 dBFS limiter on the final mix
        }
    }

    /// <summary>Which music is wanted.</summary>
    public enum MusicState { Silent, Title, Match, FinalMinute, Berserk, Results }

    /// <summary>
    /// DG-18 music director, b15 Skybreak score: ONE original score in F minor at 80 BPM, built from the intro film's
    /// approved cue (tools/audio/skybreak). The menus play its 16-bar title loop; a match plays three layered stems (bed,
    /// rhythm, theme) re-sequenced section by section on the bar grid and mixed by match state (ScoreMix: calm, contested,
    /// leading, trailing, rally, egg, final minute, berserk; the Kaiju's side colours the bed and theme feed, berserk
    /// corrupt); the hatch plays its type's stinger on the beat; a victory, defeat or draw sting ends the match (from the
    /// human's side), after which the title loop returns quietly under the results. The final minute keeps its 1.5 dB lift. The music ducks under announcer lines and
    /// while the pause menu is open. View only: it reads the match and never writes it. Clips: Resources CMMusic/ and
    /// CMStings/ (Assets/_Game/Audio/Music, credits in Assets/_Game/Audio/Resources/CREDITS.md).
    /// <para>Public API: <see cref="SetMenuMusic"/> (game-flow menus: title music without a match),
    /// <see cref="CurrentState"/>, <see cref="StateChanged"/>, <see cref="Duck"/>, <see cref="Decide"/>.</para>
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        public const float CrossfadeSeconds = 1.5f;
        public const float LiftCrossfadeSeconds = 3f;
        public const float EndFadeSeconds = 0.8f;
        /// <summary>Loop gain before the lift (-1.5 dB); the final minute plays at 1.</summary>
        public const float BaseGain = 0.84f;
        public const float LiftGain = 1f;
        public const float ResultsGain = 0.5f;
        public const float DuckGain = 0.55f;
        /// <summary>Story film mix, set by StoryIntro while the intro plays (defaults leave the music untouched): the music bed
        /// level under the film and how deep the narration duck goes.</summary>
        public static float FilmBed = 1f, FilmDuck = DuckGain;
        public const float PauseGain = 0.5f;
        public const double FinalMinuteSeconds = 60;

        public const string MusicFolder = "CMMusic/";
        public const string StingFolder = "CMStings/";
        /// <summary>b15: one loop file is left, the Skybreak title loop (menus, under the results). Match, final minute and
        /// berserk play the layered Skybreak score (ScoreMix, Resources CMScore/), so they have no single track.</summary>
        public static readonly string[] TrackNames = { null, "music-title", null, null, null, "music-title" };
        /// <summary>b15: the rally hook for the comeback rule (feat/b15-comeback-hut): true while the local player's team has
        /// its rally on. Null = never rally. Wired at the wave-two combine: the local team is earning RALLY credit
        /// (Comeback.Earning, the same read as the HUD's RALLY tag; false with the rule off).</summary>
        public static Func<World, bool> RallyProbe = LocalTeamRallies;

        static bool LocalTeamRallies(World w)
        {
            if (w == null || w.ActorCount <= 0 || w.Rally == null) return false;
            int seat = LocalSeat.Actor;
            if (seat < 0 || seat >= w.ActorCount) return false;
            return CriticalMass.Sim.Core.Comeback.Earning(w, w.Actors[seat].Team);
        }
        /// <summary>b15: the score's current target (tests, captures; default before the first match).</summary>
        public static ScoreTarget CurrentScore => Instance != null ? Instance._target : default;

        public static MusicDirector Instance { get; private set; }
        /// <summary>Raised when the wanted music changes (old, new).</summary>
        public static event Action<MusicState, MusicState> StateChanged;

        public static MusicState CurrentState => Instance != null ? Instance._state : MusicState.Silent;

        static bool _menuMusic;
        static float _duckUntil;

        /// <summary>Game flow: menus are up with no match running (title music). Off when the match starts.</summary>
        public static void SetMenuMusic(bool on) => _menuMusic = on;

        /// <summary>Lowers the music for the next seconds (announcer lines).</summary>
        public static void Duck(float seconds) => _duckUntil = Mathf.Max(_duckUntil, Time.unscaledTime + seconds);

        /// <summary>
        /// The music for a moment of the game: menus or the title screen = Title; no match = Title; match over =
        /// Results; berserk Kaiju = Berserk; one minute or less left = FinalMinute; otherwise Match.
        /// </summary>
        public static MusicState Decide(World w, bool titleShowing, bool menuMusic)
        {
            if (menuMusic || titleShowing || w == null) return MusicState.Title;
            if (w.Match.Phase == MatchPhase.Ended) return MusicState.Results;
            if (w.Kaiju != null && w.Kaiju.State == KaijuState.Berserk) return MusicState.Berserk;
            if (Match.GetMatchClock(w).Remaining <= FinalMinuteSeconds && w.Config.MatchSeconds > FinalMinuteSeconds)
                return MusicState.FinalMinute;
            return MusicState.Match;
        }

        /// <summary>The loop gain a state wants (before ducking): the lift is the final minute, berserk included.</summary>
        public static float GainFor(MusicState s, World w)
        {
            switch (s)
            {
                case MusicState.FinalMinute: return LiftGain;
                case MusicState.Berserk:
                    return w != null && Match.GetMatchClock(w).Remaining <= FinalMinuteSeconds ? LiftGain : BaseGain;
                case MusicState.Results: return ResultsGain;
                case MusicState.Silent: return 0f;
                default: return BaseGain;
            }
        }

        /// <summary>The sting for a finished match, from the human team's side (null while it is not over).</summary>
        public static string StingFor(MatchResult winner, TeamId human)
        {
            if (winner == MatchResult.None) return null;
            if (winner == MatchResult.Draw) return "sting-draw";
            TeamId t = winner == MatchResult.Azure ? TeamId.Azure : TeamId.Crimson;
            return t == human ? "sting-victory" : "sting-defeat";
        }

        /// <summary>Equal-power crossfade gains for progress t in 0..1: (outgoing, incoming).</summary>
        public static Vector2 Crossfade(float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(Mathf.Cos(t * Mathf.PI * 0.5f), Mathf.Sin(t * Mathf.PI * 0.5f));
        }

        readonly AudioSource[] _loops = new AudioSource[2];
        readonly AudioClip[] _tracks = new AudioClip[TrackNames.Length];
        AudioSource _sting;
        AudioClip _victory, _defeat, _draw;
        int _cur;                       // index of the incoming / current loop source
        float _fadeStart = -100f, _fadeLen = CrossfadeSeconds;
        float _gain = BaseGain, _duck = 1f;
        MusicState _state = MusicState.Silent;
        World _stingWorld;
        float _resultsBedAt = float.MaxValue;

        SimDriverBehaviour _driver;
        HumanInputSource _input;
        int _lookupFrames;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < 2; i++)
            {
                AudioSource s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.volume = 0f;
                s.outputAudioMixerGroup = GameSettings.MusicGroup;
                _loops[i] = s;
            }
            _sting = gameObject.AddComponent<AudioSource>();
            _sting.playOnAwake = false;
            _sting.loop = false;
            _sting.spatialBlend = 0f;
            _sting.priority = 1;
            _sting.outputAudioMixerGroup = GameSettings.MusicGroup;
            for (int i = 0; i < TrackNames.Length; i++)
                if (TrackNames[i] != null) _tracks[i] = Resources.Load<AudioClip>(MusicFolder + TrackNames[i]);
            _victory = Resources.Load<AudioClip>(StingFolder + "sting-victory");
            _defeat = Resources.Load<AudioClip>(StingFolder + "sting-defeat");
            _draw = Resources.Load<AudioClip>(StingFolder + "sting-draw");
            BuildScore();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // ------------------------------------------------------------------ b15: the Skybreak score (layered, bar-synced)

        const int Layers = 3;
        const double Lookahead = 1.0;
        readonly AudioSource[,] _sSrc = new AudioSource[Layers, 2];
        readonly int[,] _sVar = new int[Layers, 2];
        readonly double[,] _sFadeAt = new double[Layers, 2];
        readonly int[,] _sFadeDir = new int[Layers, 2];
        readonly float[,] _sLevel = new float[Layers, 2];
        readonly int[] _sCur = new int[Layers];
        readonly float[] _sGain = new float[Layers];
        readonly double[] _sStopAt = { double.MaxValue, double.MaxValue, double.MaxValue };
        AudioClip[][][] _sClips;          // [section][layer][variant]
        readonly AudioClip[] _hatchClips = new AudioClip[5];
        AudioSource _hatch;
        bool _scoreOk, _scoreOn, _scheduled;
        double _dsp0, _secStart, _nextDsp, _lastDsp;
        long _step;
        float _scoreLevel;                // 0..1 fade of the whole score against the title loop
        ScoreTarget _target, _pending;
        double _pendingAt = double.MaxValue;
        double _hatchAt = double.NaN, _hatchLen;
        World _hatchWorld;
        bool _hatchSeen;

        void BuildScore()
        {
            _sClips = new AudioClip[ScoreMix.Sections.Length][][];
            _scoreOk = true;
            for (int s = 0; s < ScoreMix.Sections.Length; s++)
            {
                _sClips[s] = new AudioClip[Layers][];
                for (int l = 0; l < Layers; l++)
                {
                    int n = ScoreMix.VariantNames[l].Length;
                    _sClips[s][l] = new AudioClip[n];
                    for (int v = 0; v < n; v++)
                    {
                        AudioClip c = Resources.Load<AudioClip>(ScoreMix.Folder + ScoreMix.Clip(s, (ScoreLayer)l, v));
                        _sClips[s][l][v] = c;
                        if (c == null) _scoreOk = false;
                    }
                }
            }
            if (!_scoreOk) Debug.LogWarning("[MusicDirector] Skybreak score stems missing under Resources/" + ScoreMix.Folder + ": the match plays without music");
            for (int i = 0; i < _hatchClips.Length; i++) _hatchClips[i] = Resources.Load<AudioClip>(StingFolder + ScoreMix.HatchSting((KaijuType)i));
            for (int l = 0; l < Layers; l++)
                for (int k = 0; k < 2; k++)
                {
                    AudioSource s = gameObject.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.loop = false;
                    s.spatialBlend = 0f;
                    s.priority = 0;
                    s.volume = 0f;
                    s.outputAudioMixerGroup = GameSettings.MusicGroup;
                    _sSrc[l, k] = s;
                    _sVar[l, k] = -1;
                }
            _hatch = gameObject.AddComponent<AudioSource>();
            _hatch.playOnAwake = false;
            _hatch.loop = false;
            _hatch.spatialBlend = 0f;
            _hatch.priority = 1;
            _hatch.outputAudioMixerGroup = GameSettings.MusicGroup;
        }

        bool ScoreReady()
        {
            if (!_scoreOk) return false;
            bool ready = true;
            for (int s = 0; s < _sClips.Length; s++)
                for (int l = 0; l < Layers; l++)
                    foreach (AudioClip c in _sClips[s][l])
                    {
                        if (c.loadState == AudioDataLoadState.Loaded) continue;
                        if (c.loadState == AudioDataLoadState.Unloaded || c.loadState == AudioDataLoadState.Failed) c.LoadAudioData();
                        ready = false;
                    }
            return ready;
        }

        static ScoreInputs Inputs(World w)
        {
            var i = new ScoreInputs { LessSound = GameSettings.LessSound };
            if (w == null) return i;
            MatchClock c = Match.GetMatchClock(w);
            TeamId human = w.ActorCount > 0 ? w.Actors[LocalSeat.Actor].Team : TeamId.Azure;
            TeamId other = human == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
            i.HillContested = w.Match.HillState == HillState.Contested;
            i.LeadSeconds = w.Match.HillSeconds[human] - w.Match.HillSeconds[other];
            i.Hatched = w.Match.Hatched;
            i.UntilHatch = c.UntilHatch;
            i.Kaiju = w.Kaiju != null ? w.Kaiju.State : KaijuState.Egg;
            i.Remaining = c.Remaining;
            i.MatchSeconds = w.Config.MatchSeconds;
            try { i.Rally = RallyProbe != null && RallyProbe(w); }
            catch (Exception) { i.Rally = false; }
            return i;
        }

        void StartScore(double dsp)
        {
            _dsp0 = dsp + 0.25;
            _secStart = _dsp0;
            _nextDsp = _dsp0 + ScoreMix.Section;
            _step = 0;
            _scheduled = false;
            _pendingAt = double.MaxValue;
            int sec = ScoreMix.SectionAt(0);
            for (int l = 0; l < Layers; l++)
            {
                for (int k = 0; k < 2; k++) { _sSrc[l, k].Stop(); _sVar[l, k] = -1; _sFadeDir[l, k] = 0; _sLevel[l, k] = 0f; }
                int v = _target.Variant((ScoreLayer)l);
                AudioSource s = _sSrc[l, 0];
                s.clip = _sClips[sec][l][v];
                s.timeSamples = 0;
                s.PlayScheduled(_dsp0);
                _sVar[l, 0] = v;
                _sLevel[l, 0] = 1f;
                _sCur[l] = 0;
                _sGain[l] = _target.Gain((ScoreLayer)l);
                _sStopAt[l] = double.MaxValue;
            }
            _scoreOn = true;
        }

        void StopScore()
        {
            for (int l = 0; l < Layers; l++)
                for (int k = 0; k < 2; k++) { _sSrc[l, k].Stop(); _sVar[l, k] = -1; _sLevel[l, k] = 0f; _sFadeDir[l, k] = 0; }
            _hatch.Stop();
            _hatchAt = double.NaN;
            _scoreOn = false;
        }

        /// <summary>The conductor, once a frame: schedules the next section 1 s ahead on each layer's free source (its old
        /// source rings out its own tail), switches a layer's form at the next bar line with a one-beat equal-power fade,
        /// moves the layer gains at bar lines, ducks under the hatch stinger, and sets every source's volume.</summary>
        void TickScore(World w, bool wantScore, float baseGain, float dt)
        {
            double now = AudioSettings.dspTime;
            if (_scoreOn && now + 0.5 < _lastDsp) StopScore();      // the audio device restarted: start over cleanly
            _lastDsp = now;
            if (!_scoreOn && (!wantScore || !ScoreReady())) { _scoreLevel = 0f; return; }
            ScoreTarget want = ScoreMix.Decide(Inputs(w));
            if (!_scoreOn)
            {
                _target = _pending = want;
                _scoreLevel = 0f;
                StartScore(now);
                _hatchWorld = w;
                _hatchSeen = w != null && w.Match.Hatched;
            }
            _scoreLevel = Mathf.MoveTowards(_scoreLevel, wantScore ? 1f : 0f, dt / (wantScore ? CrossfadeSeconds : EndFadeSeconds));
            if (!wantScore && _scoreLevel <= 0f) { StopScore(); return; }

            // a new target takes effect at the next bar line
            if (!want.SameAs(_pending)) { _pending = want; _pendingAt = ScoreMix.NextLine(_dsp0, ScoreMix.Bar, now); }
            if (now >= _pendingAt) { _target = _pending; _pendingAt = double.MaxValue; }

            // the next section, one second ahead, on each layer's free source
            if (!_scheduled && now >= _nextDsp - Lookahead)
            {
                int sec = ScoreMix.SectionAt(_step + 1);
                for (int l = 0; l < Layers; l++)
                {
                    int alt = 1 - _sCur[l];
                    AudioSource s = _sSrc[l, alt];
                    s.Stop();
                    int v = _target.Variant((ScoreLayer)l);
                    s.clip = _sClips[sec][l][v];
                    s.timeSamples = 0;
                    s.PlayScheduled(_nextDsp);
                    _sVar[l, alt] = v;
                    _sFadeDir[l, alt] = 0;
                    _sLevel[l, alt] = 1f;
                }
                _scheduled = true;
            }
            if (_scheduled && now >= _nextDsp)
            {
                for (int l = 0; l < Layers; l++)
                {
                    int old = _sCur[l];
                    _sCur[l] = 1 - old;
                    // the old source keeps its level and rings out its tail (the clip ends by itself)
                    _sStopAt[l] = double.MaxValue;
                }
                _step++;
                _secStart = _nextDsp;
                _nextDsp += ScoreMix.Section;
                _scheduled = false;
            }

            // a form change mid-section: at the next bar line (bars 1..7), once the last section's tail has rung out
            for (int l = 0; l < Layers; l++)
            {
                int cur = _sCur[l], alt = 1 - cur;
                if (now >= _sStopAt[l]) { _sSrc[l, alt].Stop(); _sLevel[l, alt] = 0f; _sFadeDir[l, alt] = 0; _sStopAt[l] = double.MaxValue; }
                int v = _target.Variant((ScoreLayer)l);
                if (_scheduled || v == _sVar[l, cur] || _sStopAt[l] != double.MaxValue) continue;
                if (now < _secStart + ScoreMix.Tail + 0.05 || _sSrc[l, alt].isPlaying) continue;
                double at = ScoreMix.NextLine(_secStart, ScoreMix.Bar, now, 0.08);
                if (at > _nextDsp - ScoreMix.Bar + 1e-6) continue;           // bar 8 is the next section's job
                int sec = ScoreMix.SectionAt(_step);
                AudioSource s = _sSrc[l, alt];
                AudioClip c = _sClips[sec][l][v];
                s.clip = c;
                s.timeSamples = Mathf.Clamp((int)Math.Round((at - _secStart) * c.frequency), 0, c.samples - 1);
                s.PlayScheduled(at);
                _sVar[l, alt] = v;
                _sFadeAt[l, alt] = at; _sFadeDir[l, alt] = +1;
                _sFadeAt[l, cur] = at; _sFadeDir[l, cur] = -1;
                _sCur[l] = alt;
                _sStopAt[l] = at + ScoreMix.Beat + 0.05;
            }

            // the hatch: the type's stinger on the next beat, the score ducked under it
            if (w != null && !ReferenceEquals(w, _hatchWorld)) { _hatchWorld = w; _hatchSeen = w.Match.Hatched; }
            if (w != null && w.Match.Hatched && !_hatchSeen)
            {
                _hatchSeen = true;
                KaijuType t = w.KaijuKind != null ? w.KaijuKind.Type : KaijuType.Elder;
                AudioClip c = _hatchClips[(int)t >= 0 && (int)t < _hatchClips.Length ? (int)t : 0];
                if (c != null)
                {
                    _hatchAt = ScoreMix.NextLine(_dsp0, ScoreMix.Beat, now, 0.06);
                    _hatchLen = c.length;
                    _hatch.Stop();
                    _hatch.clip = c;
                    _hatch.PlayScheduled(_hatchAt);
                }
            }
            float duck = ScoreMix.HatchDuck(now, _hatchAt, _hatchLen);
            _hatch.volume = baseGain;

            // gains: toward the target, up over a beat and down over a bar and a half
            for (int l = 0; l < Layers; l++)
            {
                float g = _target.Gain((ScoreLayer)l);
                float rate = g > _sGain[l] ? dt / (float)ScoreMix.Beat : dt / 1.5f;
                _sGain[l] = Mathf.MoveTowards(_sGain[l], g, rate);
                for (int k = 0; k < 2; k++)
                {
                    float f = ScoreMix.Fade(now, _sFadeAt[l, k], ScoreMix.Beat, _sFadeDir[l, k], _sLevel[l, k]);
                    if (_sFadeDir[l, k] != 0 && now >= _sFadeAt[l, k] + ScoreMix.Beat) { _sLevel[l, k] = _sFadeDir[l, k] > 0 ? 1f : 0f; _sFadeDir[l, k] = 0; }
                    _sSrc[l, k].volume = baseGain * _scoreLevel * duck * _sGain[l] * f;
                }
            }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        // perf/start-hitch-1: timed for the scene-start report (LoadTimer).
        void OnSceneLoaded(Scene s, LoadSceneMode mode) { long t0 = CriticalMass.Runtime.Game.LoadTimer.Now(); OnSceneLoadedBody(s, mode); CriticalMass.Runtime.Game.LoadTimer.Part("MusicDirector.SceneLoaded", t0); }

        void OnSceneLoadedBody(Scene s, LoadSceneMode mode)
        {
            _driver = null;
            _input = null;
            _lookupFrames = 0;
        }

        void Update() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); UpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Sound, cost); }

        void UpdateCost()
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_driver == null && (_lookupFrames++ % 15) == 0)
            {
                _driver = FindAnyObjectByType<SimDriverBehaviour>();
                if (_driver != null) _input = _driver.GetComponent<HumanInputSource>();
            }
            World w = _driver != null ? _driver.World : null;
            bool title = _driver != null && _driver.TryGetComponent(out TitleScreen _);
            bool paused = _input != null && _input.State.Paused;

            // Integration batch 2: the game flow's menus (main menu, match setup) play the menu music too.
            Flow.FlowState fs = Flow.GameFlow.State;
            bool menus = _menuMusic || fs == Flow.FlowState.Menu || fs == Flow.FlowState.Setup || fs == Flow.FlowState.Title;
            MusicState want = Decide(w, title, menus);
            if (want != _state) Enter(want, w, now);

            // Results: the sting first, then the title loop returns under the results screen.
            if (_state == MusicState.Results && now >= _resultsBedAt)
            {
                _resultsBedAt = float.MaxValue;
                Switch(_tracks[(int)MusicState.Results], CrossfadeSeconds, now);
            }

            // Gain: smooth toward the state's gain, the voice duck and the pause duck (attack 0.15 s, release 0.4 s).
            float target = GainFor(_state, w);
            _gain = Mathf.MoveTowards(_gain, target, dt / 0.6f);
            float duckTarget = (now < _duckUntil ? FilmDuck : 1f) * FilmBed * (paused ? PauseGain : 1f);
            float rate = duckTarget < _duck ? dt / 0.15f : dt / 0.4f;
            _duck = Mathf.MoveTowards(_duck, duckTarget, rate);

            Vector2 x = Crossfade((now - _fadeStart) / Mathf.Max(0.01f, _fadeLen));
            AudioSource inc = _loops[_cur], outg = _loops[1 - _cur];
            float g = _gain * _duck;
            inc.volume = x.y * g;
            outg.volume = x.x * g;
            if (x.x <= 0.001f && outg.isPlaying) outg.Stop();

            // b15: the match plays the layered Skybreak score (the title loop above fades out as it comes in)
            bool wantScore = _state == MusicState.Match || _state == MusicState.FinalMinute || _state == MusicState.Berserk;
            TickScore(w, wantScore, g, dt);
        }

        void Enter(MusicState next, World w, float now)
        {
            MusicState prev = _state;
            _state = next;
            switch (next)
            {
                case MusicState.Results:
                {
                    Switch(null, EndFadeSeconds, now);
                    _resultsBedAt = now + EndFadeSeconds;
                    if (w != null && !ReferenceEquals(w, _stingWorld))
                    {
                        _stingWorld = w;
                        TeamId human = w.ActorCount > 0 ? w.Actors[LocalSeat.Actor].Team : TeamId.Azure;
                        string id = StingFor(w.Match.Winner, human);
                        AudioClip c = id == "sting-victory" ? _victory : id == "sting-defeat" ? _defeat : id == "sting-draw" ? _draw : null;
                        if (c != null)
                        {
                            _sting.Stop();
                            _sting.clip = c;
                            _sting.volume = 1f;
                            _sting.Play();
                            _resultsBedAt = now + c.length + 1f;
                        }
                    }
                    break;
                }
                case MusicState.Silent:
                    Switch(null, EndFadeSeconds, now);
                    break;
                default:
                    _resultsBedAt = float.MaxValue;
                    if (prev == MusicState.Results && _sting.isPlaying) _sting.Stop();
                    float len = next == MusicState.FinalMinute ? LiftCrossfadeSeconds : CrossfadeSeconds;
                    Switch(_tracks[(int)next], len, now);
                    break;
            }
            StateChanged?.Invoke(prev, next);
        }

        /// <summary>Story intro 3: the intro film's own soundtrack crossfades into music-title.ogg over its last 1.5 s at
        /// exactly the level the title plays it; on the film's last frame this continues the title loop from the same sample
        /// offset at full level, so the hand-off has no seam, no dip and no pop. Outside the title (a replay from a match's
        /// pause menu) it only restores the music bed.</summary>
        public static void ResumeTitleAt(float seconds)
        {
            FilmBed = 1f;
            MusicDirector m = Instance;
            if (m == null) return;
            AudioSource cur = m._loops[m._cur];
            AudioClip title = m._tracks[(int)MusicState.Title];
            bool settled = Time.unscaledTime - m._fadeStart >= m._fadeLen;
            if (title == null || cur.clip != title || !cur.isPlaying || !settled) return;
            cur.time = Mathf.Repeat(Mathf.Max(0f, seconds), title.length);
            m._duck = Time.unscaledTime < _duckUntil ? FilmDuck : 1f;
            cur.volume = m._gain * m._duck;
        }

        /// <summary>Crossfades to clip (null = fade to silence). The same clip already playing just keeps playing.</summary>
        void Switch(AudioClip clip, float seconds, float now)
        {
            AudioSource cur = _loops[_cur];
            if (clip != null && cur.clip == clip && cur.isPlaying) return;
            // A fade still running: the outgoing source is cut so only two sources ever sound.
            AudioSource other = _loops[1 - _cur];
            other.Stop();
            _cur = 1 - _cur;
            AudioSource inc = _loops[_cur];
            inc.clip = clip;
            inc.volume = 0f;
            if (clip != null) inc.Play();
            _fadeStart = now;
            _fadeLen = seconds;
        }
    }
}
