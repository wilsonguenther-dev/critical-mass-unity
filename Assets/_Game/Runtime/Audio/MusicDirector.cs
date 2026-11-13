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
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) _root.AddComponent<AudioListener>();
            _root.AddComponent<MusicDirector>();
            _root.AddComponent<Announcer>();
            _root.AddComponent<UiSoundBinder>();
        }
    }

    /// <summary>Which music is wanted.</summary>
    public enum MusicState { Silent, Title, Match, FinalMinute, Berserk, Results }

    /// <summary>
    /// DG-18 music director: title, match and berserk loops with equal-power crossfades, a final-minute lift (a faster
    /// loop, 1.5 dB louder) at one minute left, and a victory, defeat or draw sting at the match end (from the human's
    /// side), after which the title loop returns quietly under the results. The music ducks under announcer lines and
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
        public const float PauseGain = 0.5f;
        public const double FinalMinuteSeconds = 60;

        public const string MusicFolder = "CMMusic/";
        public const string StingFolder = "CMStings/";
        public static readonly string[] TrackNames = { null, "music-title", "music-match", "music-final", "music-berserk", "music-title" };

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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            _driver = null;
            _input = null;
            _lookupFrames = 0;
        }

        void Update()
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
            float duckTarget = (now < _duckUntil ? DuckGain : 1f) * (paused ? PauseGain : 1f);
            float rate = duckTarget < _duck ? dt / 0.15f : dt / 0.4f;
            _duck = Mathf.MoveTowards(_duck, duckTarget, rate);

            Vector2 x = Crossfade((now - _fadeStart) / Mathf.Max(0.01f, _fadeLen));
            AudioSource inc = _loops[_cur], outg = _loops[1 - _cur];
            float g = _gain * _duck;
            inc.volume = x.y * g;
            outg.volume = x.x * g;
            if (x.x <= 0.001f && outg.isPlaying) outg.Stop();
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
                        TeamId human = w.ActorCount > 0 ? w.Actors[0].Team : TeamId.Azure;
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
