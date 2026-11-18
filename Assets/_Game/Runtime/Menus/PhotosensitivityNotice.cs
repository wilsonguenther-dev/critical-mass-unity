using System;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus.Story;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// b15 safe defaults (blind-spot audit C1, owner-approved 2026-11-17): a one-time photosensitivity notice on the very first
    /// launch, before the intro film. One screen, plain words, any key (after 1 s) dismisses it, and it never shows again on
    /// this profile (PlayerPrefs <see cref="SeenKey"/>). Started by StoryIntro's launch rule on the boot's title; never in
    /// autoplay, capture or other automation runs. While it is up the title reads no keys (OverlayGuard) and the Pause press
    /// is consumed. View only: nothing here reaches the sim.
    /// </summary>
    public sealed class PhotosensitivityNotice : MonoBehaviour
    {
        /// <summary>1 once the notice was dismissed on this profile.</summary>
        public const string SeenKey = "cm.access.photoNoticeSeen";
        /// <summary>Nothing dismisses the notice before this (a key still held from the launch never does).</summary>
        public const float DismissLockSeconds = 1f;
        public const int Sorting = 70;   // above the intro film's canvas (60)

        public const string Heading = "PHOTOSENSITIVITY WARNING";
        /// <summary>The notice text: plain words, short sentences (it may be read by a child or a parent).</summary>
        public const string Body =
            "A very small number of people can have a seizure when they see flashing lights or patterns, even if they have never had one before.\n\n" +
            "If you or someone in your family has epilepsy, ask a doctor before you play. Stop playing right away if you feel dizzy, " +
            "your eyes or face twitch, your sight blurs, or you feel confused.\n\n" +
            "Critical Mass keeps its flashes gentle by default: no more than three a second and no full-screen colour flips. " +
            "You can change this in Settings, Access, Intense flashing.";
        public const string PromptKeyboard = "PRESS ANY KEY TO CONTINUE";
        public const string PromptPad = "PRESS ANY BUTTON TO CONTINUE";

        public static PhotosensitivityNotice Instance { get; private set; }
        /// <summary>The notice is on screen.</summary>
        public static bool IsShowing => Instance != null && Instance._showing;

        /// <summary>Dismissed once on this profile.</summary>
        public static bool Seen
        {
            get => PlayerPrefs.GetInt(SeenKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(SeenKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The launch rule (pure; tests): the boot's title of a person's launch, not yet seen.</summary>
        public static bool ShouldShow(bool titleOnBoot, FlowState state, bool autoplay, bool capture, bool seen, string[] args) =>
            titleOnBoot && state == FlowState.Title && !autoplay && !capture && !seen && !AutomationRun(args);

        /// <summary>Any automation flag on the command line (AutomationWindow's list): scripted runs never stop on the notice.</summary>
        public static bool AutomationRun(string[] args)
        {
            if (args == null) return false;
            foreach (string raw in args)
            {
                string a = (raw ?? "").Trim().ToLowerInvariant();
                if (Array.IndexOf(CriticalMass.Runtime.Game.AutomationWindow.AutomationFlags, a) >= 0) return true;
            }
            return false;
        }

        /// <summary>Shows the notice on host (the game object the title and the intro live on); then runs after it is
        /// dismissed (null: nothing). False when there is no host.</summary>
        public static bool Show(GameObject host, Action then)
        {
            if (host == null) return false;
            PhotosensitivityNotice n = host.GetComponent<PhotosensitivityNotice>();
            if (n == null) n = host.AddComponent<PhotosensitivityNotice>();
            n.Begin(then);
            return true;
        }

        Canvas _canvas;
        Text _prompt;
        bool _showing;
        double _t;
        SkipGate _gate;
        Action _then;
        ControlDevice _device = (ControlDevice)(-1);

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (_showing) OverlayGuard.End();
            if (Instance == this) Instance = null;
        }

        void Begin(Action then)
        {
            Build();
            _then = then;
            _t = 0;
            _gate = new SkipGate(DismissLockSeconds);
            _showing = true;
            _canvas.enabled = true;
            _prompt.color = CMTokens.WithAlpha(CMTokens.ToolBrass.Color, 0f);
            OverlayGuard.Begin(gameObject, () => true, true);
        }

        /// <summary>Closes the notice now and remembers it (tests and the dismiss press).</summary>
        public void Dismiss()
        {
            if (!_showing) return;
            _showing = false;
            Seen = true;
            if (_canvas != null) _canvas.enabled = false;
            OverlayGuard.End();
            Action then = _then;
            _then = null;
            then?.Invoke();
        }

        void Update()
        {
            OverlayGuard.Tick();
            if (!_showing) return;
            MenuCanvas.UpdateScale(_canvas);
            _t += Mathf.Min(0.1f, Time.unscaledDeltaTime);
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d != _device)
            {
                _device = d;
                HudText.Set(_prompt, d == ControlDevice.Gamepad ? PromptPad : PromptKeyboard);
            }
            _prompt.color = CMTokens.WithAlpha(CMTokens.ToolBrass.Color, MotionComic.SkipPromptAlpha(_t, DismissLockSeconds));
            if (_gate.Update(_t, StoryIntro.SkipAnyKeyDown())) Dismiss();
        }

        void Build()
        {
            if (_canvas != null) return;
            _canvas = MenuCanvas.Make(transform, "Photosensitivity Notice Canvas", Sorting, out RectTransform space);
            MenuCanvas.Wash(_canvas.transform, 1f);   // opaque Void Navy: nothing flashes behind the words
            const float w = 1120f, h = 620f;
            float x = (HudLayout.RefW - w) * 0.5f, y = (HudLayout.RefH - h) * 0.5f;
            VectorShape panel = MenuUi.Panel(space, "Notice Panel", x, y, w, h);
            const float pad = 56f;
            Text head = MenuUi.Label(panel.transform, "Heading", CMTokens.Display, UiScale.TypeDisplay, CMTokens.WarmCream.Color,
                TextAnchor.UpperLeft, pad, 44f, w - 2f * pad, 64f, Heading, 3f);
            MenuUi.Fit(head, UiScale.TypeLead);
            Text body = MenuUi.Label(panel.transform, "Body", CMTokens.Ui, UiScale.TypeButton, CMTokens.WarmCream.Color,
                TextAnchor.UpperLeft, pad, 132f, w - 2f * pad, h - 132f - 104f, Body, 2f);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.lineSpacing = 1.1f;
            MenuUi.Fit(body, UiScale.TypeBody);
            _prompt = MenuUi.Label(panel.transform, "Prompt", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color,
                TextAnchor.MiddleLeft, pad, h - 84f, w - 2f * pad, 44f, PromptKeyboard, 2f);
            _canvas.enabled = false;
            MenuCanvas.UpdateScale(_canvas);
        }
    }
}
