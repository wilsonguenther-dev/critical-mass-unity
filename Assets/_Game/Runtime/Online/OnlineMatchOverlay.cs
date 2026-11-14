using CriticalMass.Net;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-2: the online match's only extra screen element. A small strip under the top edge says who the game is
    /// waiting for (after half a second of waiting, so ordinary network jitter never shows). If the match cannot go on
    /// (the two games went out of step, the host left, the connection dropped) a centred panel says so in one plain
    /// sentence and Enter / Escape / A returns to the menu. Built once; no per-frame allocation (the strings change only
    /// on a state change). Every text sits in its own fixed box and is contained (TextFit), so nothing can overlap.
    /// </summary>
    public sealed class OnlineMatchOverlay : MonoBehaviour
    {
        const float StripW = 760f, StripH = 64f, PanelW = 980f, PanelH = 300f;

        Canvas _canvas;
        GameObject _strip, _panel;
        Text _stripText, _panelTitle, _panelBody;
        LockstepSession _session;
        LockstepStatus _shown = LockstepStatus.Running;
        bool _waitingShown;
        float _endedAt = -1f;

        static OnlineMatchOverlay _live;

        /// <summary>online-rooms-6: the stopped panel (CANNOT START, MATCH ENDED, host left) is up: nothing online draws behind it.</summary>
        public static bool ModalUp => _live != null && _live._panel != null && _live._panel.activeSelf;

        void OnEnable() => _live = this;

        void OnDisable() { if (_live == this) _live = null; }

        void Start()
        {
            _session = OnlineMatch.Session;
            var go = new GameObject("Online Overlay", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HudLayout.RefW, HudLayout.RefH);
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)go.transform;
            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            // online-rooms-5: the strip lives in the HUD's centre callout lane (HudZones.Callout) and holds it while shown,
            // so the banner, lockout warning and Kaiju callout wait instead of drawing on top of it.
            float sx = (HudLayout.RefW - StripW) * 0.5f;
            _strip = MenuUi.Panel(space, "Waiting Strip", sx, HudZones.CalloutTop, StripW, StripH).gameObject;
            _stripText = MenuUi.Label(_strip.transform, "Waiting", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                24f, 8f, StripW - 48f, StripH - 16f, "", 2f);
            MenuUi.Contain(_stripText, 1);
            _strip.SetActive(false);

            float px = (HudLayout.RefW - PanelW) * 0.5f, py = (HudLayout.RefH - PanelH) * 0.5f;
            _panel = MenuUi.Panel(space, "Stopped Panel", px, py, PanelW, PanelH).gameObject;
            _panelTitle = MenuUi.Label(_panel.transform, "Title", CMTokens.Display, UiScale.TypeLead, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                40f, 28f, PanelW - 80f, 56f, "", 3f);
            MenuUi.Contain(_panelTitle, 1);
            _panelBody = MenuUi.Label(_panel.transform, "Body", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                40f, 100f, PanelW - 80f, 120f, "", 2f);
            _panelBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUi.Contain(_panelBody, 3);
            Text hint = MenuUi.Label(_panel.transform, "Hint", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.85f),
                TextAnchor.MiddleLeft, 40f, PanelH - 64f, PanelW - 80f, 40f, "ENTER OR A: BACK TO THE MENU", 2f);
            MenuUi.Contain(hint, 1);
            _panel.SetActive(false);
        }

        void Update()
        {
            if (_session == null || _canvas == null) return;
            LockstepStatus st = _session.Status;
            if (st != _shown)
            {
                _shown = st;
                switch (st)
                {
                    case LockstepStatus.Desynced:
                        if (_session.SetupMismatch != null)
                        {
                            Debug.LogWarning("[online] setup mismatch: " + _session.SetupMismatch + " (" + _session.SetupMismatchDetail + ")");
                            Show("CANNOT START", _session.SetupMismatch);
                        }
                        else Show("MATCH STOPPED", "The two games stopped agreeing about the match (at moment " + _session.DesyncTick +
                            "), so it cannot go on fairly. This is a bug on our side, not your connection.");
                        break;
                    case LockstepStatus.HostLeft:
                        Show("MATCH ENDED", _session.IsHost
                            ? (_session.ClosedReason ?? "The connection to the room was lost.")
                            : "The host left the match or lost their connection.");
                        break;
                }
            }
            bool waiting = st == LockstepStatus.Running && _session.WaitingMs > 500;
            if (waiting != _waitingShown)
            {
                _waitingShown = waiting;
                _strip.SetActive(waiting);
                if (waiting) HudText.Set(_stripText, _session.IsHost ? "WAITING FOR A SLOW CONNECTION..." : "WAITING FOR THE HOST...");
            }
            if (_strip.activeSelf) HudZones.Hold(HudZone.Callout, HudPriority.Takeover);
            if (_panel.activeSelf && Time.unscaledTime - _endedAt > 0.6f && Pressed()) GameFlow.ReturnTo(FlowState.Menu);
        }

        /// <summary>online-rooms-4, -onlineShots only: show the stopped panel (or the waiting strip) without a session.</summary>
        public void Preview(string title, string body, string waiting = null)
        {
            if (_panel == null) return;
            if (waiting != null) { _panel.SetActive(false); _strip.SetActive(true); HudText.Set(_stripText, waiting); return; }
            Show(title, body);
        }

        public void ClearPreview() { if (_panel != null) { _panel.SetActive(false); _strip.SetActive(false); } }

        void Show(string title, string body)
        {
            _endedAt = Time.unscaledTime;
            _strip.SetActive(false);
            _panel.SetActive(true);
            HudText.Set(_panelTitle, title);
            HudText.Set(_panelBody, body);
        }

        static bool Pressed()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }
    }
}
