using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Audio
{
    /// <summary>The UI sound language: one sound per meaning, the same on every screen.</summary>
    public enum UiCue { Hover, Step, Confirm, Back, Error }

    /// <summary>
    /// DG-31: the UI sound of one focusable control (any uGUI Selectable: the menus' MenuItem, Button, Toggle, Slider).
    /// Focus (mouse hover selects, keyboard and controller moves select) = Hover; left or right on a stepper or slider =
    /// Step; submit or click = Confirm, or Back for a control whose role is Back ("Back", "Resume", "Close", "Cancel");
    /// a press on a control that is switched off = Error. The binder adds this to every Selectable on every screen, so
    /// screens never wire sounds by hand; call <see cref="Bind"/> or <see cref="BindAll"/> to do it explicitly.
    /// Sounds play on the Effects mixer group from a small pool (UiSoundBinder); the same cue never repeats within
    /// 60 ms, and a focus tick right after a press is skipped, so one press is one sound.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSound : MonoBehaviour, ISelectHandler, ISubmitHandler, IPointerClickHandler, ICancelHandler, IMoveHandler
    {
        public enum Role { Confirm, Back }

        public Role PressRole = Role.Confirm;
        Selectable _sel;
        MenuItem _item;

        static readonly string[] BackWords = { "back", "resume", "close", "cancel" };

        /// <summary>Raised for every cue played (tests, captures).</summary>
        public static event Action<UiCue> Played;

        /// <summary>Plays a cue (other systems: an invalid action, a rebind conflict).</summary>
        public static void Play(UiCue cue) => UiSoundBinder.PlayCue(cue);

        /// <summary>
        /// Plays a cue by name (the hook the full map, pings and the clickable HUD look for): a refused or failed action
        /// is the error cue, a close or back the back cue, a hover the hover cue, everything else (open, click, a ping
        /// placed) the confirm cue.
        /// </summary>
        public static void Play(string cue)
        {
            if (string.IsNullOrEmpty(cue)) return;
            // Sound coverage: names the sound table knows (each ping kind, map open / close) get their own cue.
            if (CriticalMass.Runtime.Audio.Sound.SoundDirector.PlayNamed(cue)) return;
            UiCue c = UiCue.Confirm;
            if (cue.IndexOf("refus", StringComparison.OrdinalIgnoreCase) >= 0 || cue.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) c = UiCue.Error;
            else if (cue.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 || cue.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0) c = UiCue.Back;
            else if (cue.IndexOf("hover", StringComparison.OrdinalIgnoreCase) >= 0) c = UiCue.Hover;
            UiSoundBinder.PlayCue(c);
        }

        /// <summary>Adds UiSound to one Selectable (role from its label); returns the component.</summary>
        public static UiSound Bind(Selectable s)
        {
            if (s == null) return null;
            if (s.TryGetComponent(out UiSound u)) return u;
            u = s.gameObject.AddComponent<UiSound>();
            u.PressRole = RoleFromLabel(LabelOf(s));
            return u;
        }

        static readonly List<Selectable> Scratch = new List<Selectable>(64);

        /// <summary>Binds every Selectable under root, inactive ones included; returns how many were newly bound.</summary>
        public static int BindAll(Transform root)
        {
            if (root == null) return 0;
            Scratch.Clear();
            root.GetComponentsInChildren(true, Scratch);
            int n = 0;
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].TryGetComponent(out UiSound _)) continue;
                Bind(Scratch[i]);
                n++;
            }
            Scratch.Clear();
            return n;
        }

        /// <summary>Counts Selectables under root (inactive included) without UiSound; their paths go to missing.</summary>
        public static int CountUnbound(Transform root, List<string> missing)
        {
            if (root == null) return 0;
            Scratch.Clear();
            root.GetComponentsInChildren(true, Scratch);
            int n = 0;
            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].TryGetComponent(out UiSound _)) continue;
                n++;
                missing?.Add(PathOf(Scratch[i].transform));
            }
            Scratch.Clear();
            return n;
        }

        /// <summary>Selectables under root, inactive included.</summary>
        public static int CountSelectables(Transform root)
        {
            if (root == null) return 0;
            Scratch.Clear();
            root.GetComponentsInChildren(true, Scratch);
            int n = Scratch.Count;
            Scratch.Clear();
            return n;
        }

        public static Role RoleFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return Role.Confirm;
            string l = label.Trim().ToLowerInvariant();
            for (int i = 0; i < BackWords.Length; i++)
                if (l.StartsWith(BackWords[i], StringComparison.Ordinal)) return Role.Back;
            return Role.Confirm;
        }

        static string LabelOf(Selectable s)
        {
            if (s is MenuItem mi && mi.Label != null) return mi.Label.text;
            Text t = s.GetComponentInChildren<Text>(true);
            return t != null ? t.text : s.gameObject.name;
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (Transform x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        void Awake()
        {
            _sel = GetComponent<Selectable>();
            _item = _sel as MenuItem;
        }

        bool Interactable => _sel == null || _sel.IsInteractable();

        bool Blocked => Time.unscaledTime < MenuItem.SubmitBlockedUntil;

        bool StepsValue
        {
            get
            {
                if (_item != null) return _item.ItemKind != MenuItem.Kind.Button;
                return _sel is Slider || _sel is Scrollbar;
            }
        }

        UiCue PressCue => PressRole == Role.Back ? UiCue.Back : UiCue.Confirm;

        public void OnSelect(BaseEventData e)
        {
            if (Interactable) UiSoundBinder.PlayCue(UiCue.Hover);
        }

        public void OnSubmit(BaseEventData e)
        {
            if (Blocked) return;
            UiSoundBinder.PlayCue(Interactable ? (_item != null && StepsValue ? UiCue.Step : PressCue) : UiCue.Error);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || Blocked) return;
            UiSoundBinder.PlayCue(Interactable ? (StepsValue ? UiCue.Step : PressCue) : UiCue.Error);
        }

        public void OnCancel(BaseEventData e) => UiSoundBinder.PlayCue(UiCue.Back);

        public void OnMove(AxisEventData e)
        {
            if (!Interactable || !StepsValue) return;
            if (e.moveDir == MoveDirection.Left || e.moveDir == MoveDirection.Right) UiSoundBinder.PlayCue(UiCue.Step);
        }

        internal static void RaisePlayed(UiCue c) => Played?.Invoke(c);
    }

    /// <summary>
    /// Lives on the persistent "CM Audio" object (AudioHost). Every frame it checks the enabled Selectables (no
    /// allocation: Selectable.AllSelectablesNoAlloc into a kept buffer) and binds UiSound to any screen that appears,
    /// inactive pages included; it gives the pause menu its open, close and page sounds (Confirm on open, Back on close
    /// or back to the root page, Error when a rebind conflict warning appears) without editing the menu; and it owns
    /// the pool of four UI voices on the Effects mixer group.
    /// </summary>
    public sealed class UiSoundBinder : MonoBehaviour
    {
        public const string UiFolder = "CMUi/";
        public const int Voices = 4;
        public const float RepeatGuardSeconds = 0.06f;
        public const float HoverAfterPressGuardSeconds = 0.1f;

        public static readonly string[] CueFiles = { "ui-hover", "ui-step", "ui-confirm", "ui-back", "ui-error" };
        public static readonly float[] CueGain = { 0.55f, 0.6f, 0.8f, 0.75f, 0.8f };

        static UiSoundBinder _instance;

        readonly AudioClip[] _clips = new AudioClip[5];
        readonly float[] _lastAt = { -1f, -1f, -1f, -1f, -1f };
        readonly AudioSource[] _voices = new AudioSource[Voices];
        int _next;
        float _lastPressAt = -1f;
        Selectable[] _buffer = new Selectable[64];

        SimDriverBehaviour _driver;
        PauseMenu _menu;
        bool _menuOpen;
        PauseMenu.Page _menuPage;
        string _menuStatus = string.Empty;
        int _lookupFrames;

        /// <summary>Cues played since start (tests).</summary>
        public static int PlayedCount { get; private set; }

        public static void PlayCue(UiCue cue)
        {
            UiSound.RaisePlayed(cue);
            if (_instance != null) _instance.PlayInternal(cue);
        }

        void Awake()
        {
            _instance = this;
            for (int i = 0; i < _clips.Length; i++) _clips[i] = Resources.Load<AudioClip>(UiFolder + CueFiles[i]);
            for (int i = 0; i < Voices; i++)
            {
                AudioSource s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.priority = 16;
                s.ignoreListenerPause = true;
                s.outputAudioMixerGroup = GameSettings.EffectsGroup;
                _voices[i] = s;
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_instance == this) _instance = null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            _driver = null;
            _menu = null;
            _menuOpen = false;
            _lookupFrames = 0;
        }

        void PlayInternal(UiCue cue)
        {
            float now = Time.unscaledTime;
            int i = (int)cue;
            if (now - _lastAt[i] < RepeatGuardSeconds) return;
            if (cue == UiCue.Hover && now - _lastPressAt < HoverAfterPressGuardSeconds) return;
            _lastAt[i] = now;
            if (cue == UiCue.Confirm || cue == UiCue.Back || cue == UiCue.Error) _lastPressAt = now;
            AudioClip c = _clips[i];
            if (c == null) return;
            AudioSource s = _voices[_next];
            _next = (_next + 1) % Voices;
            s.Stop();
            s.clip = c;
            s.volume = CueGain[i];
            s.Play();
            PlayedCount++;
        }

        void LateUpdate()
        {
            BindNew();
            WatchPauseMenu();
        }

        void BindNew()
        {
            int n = Selectable.allSelectableCount;
            if (n == 0) return;
            if (_buffer.Length < n) _buffer = new Selectable[Mathf.NextPowerOfTwo(n)];
            int got = Selectable.AllSelectablesNoAlloc(_buffer);
            for (int i = 0; i < got; i++)
            {
                Selectable s = _buffer[i];
                if (s == null || s.TryGetComponent(out UiSound _)) continue;
                // A new screen: bind all of it at once, its hidden pages included.
                Canvas c = s.GetComponentInParent<Canvas>();
                if (c != null) UiSound.BindAll(c.rootCanvas.transform);
                else UiSound.Bind(s);
            }
            Array.Clear(_buffer, 0, got);
        }

        void WatchPauseMenu()
        {
            if (_driver == null && (_lookupFrames++ % 15) == 0)
            {
                _driver = FindAnyObjectByType<SimDriverBehaviour>();
                _menu = null;
            }
            if (_driver == null) return;
            if (_menu == null && !_driver.TryGetComponent(out _menu)) return;
            bool open = _menu.IsOpen;
            if (open != _menuOpen)
            {
                _menuOpen = open;
                PlayCue(open ? UiCue.Confirm : UiCue.Back);
                _menuPage = _menu.CurrentPage;
            }
            if (!open) return;
            PauseMenu.Page page = _menu.CurrentPage;
            if (page != _menuPage)
            {
                if (page == PauseMenu.Page.Root) PlayCue(UiCue.Back);
                _menuPage = page;
            }
            string status = _menu.Status;
            if (!ReferenceEquals(status, _menuStatus) && status != _menuStatus)
            {
                // Only the conflict warning is an error ("Warning: X is also bound to ..."); other status lines are news.
                if (!string.IsNullOrEmpty(status) && status.StartsWith("Warning", StringComparison.Ordinal)) PlayCue(UiCue.Error);
                _menuStatus = status;
            }
        }
    }
}
