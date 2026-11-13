using System;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// DG-51 and DG-61: hardware and OS events during a live match. View side only: it pauses through the pause menu
    /// (the same path as the Pause key), so the sim sees an ordinary pause in its input stream and determinism holds.
    /// <list type="bullet">
    /// <item>Focus lost (a streamer clicks chat) during a live match: the pause menu opens. Drawing and sound go on (FrameGovernor).</item>
    /// <item>A controller is unplugged during a live match: pause with "Controller disconnected"; when it comes back and the
    /// menu we opened is still up, the match resumes.</item>
    /// <item>The audio output changes (headphones, a dock): the audio system is reset and AudioDeviceChanged fires so the
    /// sound lane (DG-18) can restart its music.</item>
    /// <item>Wake from sleep or a display change: FrameGovernor re-reads the display; this logs the wake.</item>
    /// </list>
    /// "Live match" = a game exists, no title screen, the match is not over, it is not a backdrop behind the menus, and it
    /// is not an autoplay run. Self-installs (DontDestroyOnLoad) like the other services.
    /// </summary>
    public sealed class SystemEvents : MonoBehaviour
    {
        public const string ControllerNotice = "Controller disconnected. Reconnect it to continue.";

        /// <summary>The notice FrameGovernor shows while a controller is missing (null otherwise).</summary>
        public static string Notice { get; private set; }

        /// <summary>Raised on the main thread after the audio output device changed and audio was reset.</summary>
        public static event Action AudioDeviceChanged;
        /// <summary>Integration batch 5: tests that assert no unexpected log turn this off; a real audio device change on the
        /// test machine (headphones, a call) otherwise logs "audio output changed" mid-test and fails it. The reset still runs.</summary>
        public static bool LogAudioResets = true;

        /// <summary>Pauses opened by this class since boot (tests).</summary>
        public static int PausesOpened { get; private set; }

        static GameObject _host;
        bool _pausedForController, _audioChanged, _pendingControllerPause;
        float _lastFrameRealtime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_host != null) return;
            _host = new GameObject("CriticalMass SystemEvents");
            DontDestroyOnLoad(_host);
            _host.AddComponent<SystemEvents>();
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        /// <summary>true while a human is playing a real match (pure inputs; tests).</summary>
        public static bool IsLiveMatch(bool hasWorld, bool titleUp, bool ended, bool backdrop, bool autoplay) =>
            hasWorld && !titleUp && !ended && !backdrop && !autoplay;

        static PauseMenu FindMenu(out SimDriverBehaviour driver, out bool live)
        {
            driver = FindAnyObjectByType<SimDriverBehaviour>();
            PauseMenu menu = FindAnyObjectByType<PauseMenu>();
            bool hasWorld = driver != null && driver.World != null;
            live = IsLiveMatch(hasWorld, FindAnyObjectByType<TitleScreen>() != null,
                hasWorld && driver.World.Match != null && driver.World.Match.Phase == MatchPhase.Ended,
                hasWorld && driver.Request != null && driver.Request.Backdrop, AutoplayArgs.Current.Autoplay);
            return menu;
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus || Application.isEditor) return;
            PauseMenu menu = FindMenu(out _, out bool live);
            if (!live || menu == null || menu.IsOpen) return;
            menu.Pause();
            PausesOpened++;
            Debug.Log("[CM.System] focus lost during a match: paused (drawing and sound continue)");
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad)) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                _pendingControllerPause = true;   // acted on next frame, on the main loop, after the input update
            }
            else if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                if (Notice == ControllerNotice) Notice = null;
                if (_pausedForController)
                {
                    _pausedForController = false;
                    PauseMenu menu = FindMenu(out _, out _);
                    if (menu != null && menu.IsOpen && menu.CurrentPage == PauseMenu.Page.Root) menu.Resume();
                    Debug.Log("[CM.System] controller reconnected: resumed");
                }
            }
        }

        /// <summary>Applies a pending controller pause now (the next frame after the device left; tests call it directly).</summary>
        public void ApplyPending()
        {
            if (!_pendingControllerPause) return;
            _pendingControllerPause = false;
            if (Gamepad.all.Count > 0) return;   // another pad is still connected
            PauseMenu menu = FindMenu(out _, out bool live);
            if (!live || menu == null) return;
            Notice = ControllerNotice;
            if (!menu.IsOpen)
            {
                menu.Pause();
                PausesOpened++;
                _pausedForController = true;
            }
            Debug.Log("[CM.System] controller disconnected during a match: paused");
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (deviceWasChanged) _audioChanged = true;   // handled on the next Update, not inside the audio callback
        }

        void Update()
        {
            ApplyPending();
            if (Notice == ControllerNotice && Gamepad.all.Count > 0) Notice = null;
            float now = Time.realtimeSinceStartup;
            if (_lastFrameRealtime > 0 && now - _lastFrameRealtime > 20f)
                Debug.Log($"[CM.System] resumed after {now - _lastFrameRealtime:0} s without frames (sleep or a hidden window)");
            _lastFrameRealtime = now;
            if (!_audioChanged) return;
            _audioChanged = false;
            try
            {
                AudioConfiguration cfg = AudioSettings.GetConfiguration();
                AudioSettings.Reset(cfg);
                if (LogAudioResets) Debug.Log("[CM.System] audio output changed: audio reset");
                AudioDeviceChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CM.System] audio reset failed: {e.Message}");
            }
        }
    }
}
