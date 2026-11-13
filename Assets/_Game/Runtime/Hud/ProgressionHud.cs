using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// The DG-39 HUD pieces, on their own small canvas just above the HUD (sorting 11) so the HUD root is not edited:
    /// <list type="bullet">
    /// <item>the coin chip above the player card: a brass coin and this match's coins so far (MatchRewardTracker.LiveCoins),
    /// with a 180 ms pop when the number rises; allocation-free (NumberCache);</item>
    /// <item>the portrait hook: a transparent click target laid over the HUD's class portrait (the GameHud object named
    /// "Portrait" in the player cluster, found by name once), which opens the character panel;</item>
    /// <item>the keys: C, or D-pad up on a controller (free in Controls.inputactions on main), toggle the character panel.</item>
    /// </list>
    /// Visible only while the HUD canvas is (the title screen hides it). Read-only on the sim.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 53, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]   // installed once by GameBootstrap through the overlay registry
    public sealed class ProgressionHud : MonoBehaviour
    {
        public static ProgressionHud Instance { get; private set; }
        /// <summary>False: the C key and D-pad up do nothing (a screen that needs them, e.g. a text field elsewhere).</summary>
        public static bool HotkeysEnabled = true;

        const float ChipW = 168f, ChipH = 46f;
        const float PopSeconds = 0.18f;

        GameHud _hud;
        MatchRewardTracker _tracker;
        HumanInputSource _input;
        Canvas _canvas;
        RectTransform _root, _chip, _portraitHit;
        Text _count;
        RectTransform _portrait;
        float _nextPortraitSearch;
        int _shown = -1;
        float _popAt = -10f;
        readonly Vector3[] _corners = new Vector3[4];

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            _hud = GetComponent<GameHud>();
            _tracker = GetComponent<MatchRewardTracker>();
            _input = GetComponent<HumanInputSource>();
            Build();
        }

        void Build()
        {
            var go = new GameObject("Progression HUD Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.renderMode = cam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.5f, cam.nearClipPlane * 1.5f) : 0.5f;
            _canvas.sortingOrder = 11;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            // Coin chip: bottom-left, 12 px above the player card (HudLayout Player cluster).
            _chip = MenuUi.Rt(FeedUi.SafeRoot(_root), "Coin Chip");   // owner walk 1: inside the safe area
            _chip.anchorMin = _chip.anchorMax = Vector2.zero;
            _chip.pivot = Vector2.zero;
            _chip.sizeDelta = new Vector2(ChipW, ChipH);
            _chip.anchoredPosition = new Vector2(HudZones.CoinChip.x, HudLayout.RefH - HudZones.CoinChip.yMax);
            VectorShape body = MenuUi.Shape(_chip, "Body", ShapeKind.RoundRect, 0f, 0f, ChipW, ChipH, CMTokens.PanelNavy.Color, -1f);
            body.OutlinePx = 3f;
            body.RimPx = 2f;
            ProgressionUi.Coin(_chip, "Coin", 9f, 8f, 30f);
            _count = MenuUi.Fit(MenuUi.Label(_chip, "Count", CMTokens.Mono, UiScale.TypeButton, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 50f, 0f, ChipW - 60f, ChipH, "0", 3f), 26);

            // Portrait click target (transparent; placed over the HUD portrait every frame).
            _portraitHit = MenuUi.Rt(_root, "Portrait Click");
            _portraitHit.anchorMin = _portraitHit.anchorMax = new Vector2(0.5f, 0.5f);
            _portraitHit.pivot = new Vector2(0.5f, 0.5f);
            Image hit = _portraitHit.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            _portraitHit.gameObject.AddComponent<PortraitClick>();
            _portraitHit.gameObject.SetActive(false);
            UpdateScale();
        }

        void UpdateScale()
        {
            UnityEngine.Camera cam = _canvas.worldCamera;
            if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && cam == null)
            {
                cam = UnityEngine.Camera.main;
                _canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            bool hudOn = _hud != null && _hud.Canvas != null && _hud.Canvas.enabled;
            if (_canvas.enabled != hudOn) _canvas.enabled = hudOn;
            Hotkeys();
            if (!hudOn) return;
            UpdateScale();

            int coins = _tracker != null ? _tracker.LiveCoins : 0;
            if (coins != _shown)
            {
                if (_shown >= 0 && coins > _shown) _popAt = Time.unscaledTime;
                _shown = coins;
                HudText.Set(_count, NumberCache.Int(coins));
            }
            float t = (Time.unscaledTime - _popAt) / PopSeconds;
            float sc = t >= 0f && t < 1f ? 1f + 0.12f * (1f - ProgressionUi.EaseOut(t)) : 1f;
            Vector3 want = new Vector3(sc, sc, 1f);
            if (_chip.localScale != want) _chip.localScale = want;
            // Integration batch 5: one coin readout in a match. The match coin counter above the minimap wins; the chip
            // shows when the match has no coin pickups.
            bool chipOn = Mechanics.DepthHud.Instance == null || !Mechanics.DepthHud.Instance.CoinsShown;
            if (_chip.gameObject.activeSelf != chipOn) _chip.gameObject.SetActive(chipOn);

            PlacePortraitHit();
        }

        /// <summary>This component's own click target over the HUD portrait (off: HudInteract owns the portrait click).</summary>
        public static bool PortraitClickEnabled;

        void Hotkeys()
        {
            if (!HotkeysEnabled || CharacterPanel.IsTyping || GetComponent<TitleScreen>() != null) return;
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            bool paused = _input != null && _input.State.Paused;
            bool key = kb != null && kb.pKey.wasPressedThisFrame;   // controls-1: P (C is the arrow hand's jump)
            // D-pad up navigates the pause menu while paused; it opens the panel only in play.
            // Integration batch 2: D-pad up is the Map action (full map) and a spectator objective; the panel opens with P (controls-1; was C)
            // or a click on the HUD portrait, and closes with B.
            bool padKey = false, padClose = false;
            if (key || padKey || padClose) CharacterPanel.Toggle();
        }

        /// <summary>Keeps the click target over the HUD portrait (found by name; searched at most twice a second until found).</summary>
        void PlacePortraitHit()
        {
            // Integration batch 2: the clickable HUD (HudInteract's portrait hotspot) opens the character panel; a second
            // click target here would toggle it twice on one click. Off unless a build has no clickable HUD.
            if (!PortraitClickEnabled)
            {
                if (_portraitHit != null && _portraitHit.gameObject.activeSelf) _portraitHit.gameObject.SetActive(false);
                return;
            }
            if (_portrait == null)
            {
                if (Time.unscaledTime < _nextPortraitSearch) return;
                _nextPortraitSearch = Time.unscaledTime + 0.5f;
                _portrait = FindPortrait();
                if (_portrait == null)
                {
                    if (_portraitHit.gameObject.activeSelf) _portraitHit.gameObject.SetActive(false);
                    return;
                }
            }
            bool on = _portrait.gameObject.activeInHierarchy;
            if (_portraitHit.gameObject.activeSelf != on) _portraitHit.gameObject.SetActive(on);
            if (!on) return;
            _portrait.GetWorldCorners(_corners);
            UnityEngine.Camera hudCam = _hud.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _hud.Canvas.worldCamera;
            UnityEngine.Camera myCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(hudCam, _corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(hudCam, _corners[2]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, a, myCam, out Vector2 la)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, b, myCam, out Vector2 lb)) return;
            Vector2 centre = (la + lb) * 0.5f, size = new Vector2(Mathf.Abs(lb.x - la.x), Mathf.Abs(lb.y - la.y));
            if ((_portraitHit.anchoredPosition - centre).sqrMagnitude > 0.01f) _portraitHit.anchoredPosition = centre;
            if ((_portraitHit.sizeDelta - size).sqrMagnitude > 0.01f) _portraitHit.sizeDelta = size;
        }

        RectTransform FindPortrait()
        {
            if (_hud == null || _hud.Canvas == null) return null;
            RectTransform cluster = _hud.Clusters[(int)HudCluster.Player];
            Transform t = cluster != null ? Cosmetics.FindDeep(cluster, "Portrait") : null;
            if (t == null) t = Cosmetics.FindDeep(_hud.Canvas.transform, "Portrait");
            return t as RectTransform;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            HotkeysEnabled = true;
        }
    }

    /// <summary>The portrait click target: a left click opens (or closes) the character panel.</summary>
    public sealed class PortraitClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) CharacterPanel.Toggle();
        }
    }
}
