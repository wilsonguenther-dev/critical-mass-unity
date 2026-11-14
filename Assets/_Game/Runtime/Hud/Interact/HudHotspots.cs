using System;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>What clicking a HUD element does.</summary>
    public enum HotClick
    {
        None, Scoreboard, PingHill, PingKaiju, KaijuCard, CharacterPanel, AbilityCard, HealthCard, OpenMap, RespawnCard,
    }

    /// <summary>One clickable HUD element: where it is (paths under a canvas), its tooltip and its click.</summary>
    public sealed class HotspotDef
    {
        public readonly string Id;
        /// <summary>Which HUD it belongs to ("main" = GameHud on main and PR #17, else the branch that adds it).</summary>
        public readonly string Source;
        /// <summary>Canvas GameObject name to search under; null = GameHud.Root.</summary>
        public readonly string Canvas;
        /// <summary>Paths (Transform.Find under the canvas) or plain names (found anywhere under it); the hotspot is their union.</summary>
        public readonly string[] Paths;
        public readonly string Name, Meaning;
        /// <summary>Player action whose key is shown (null = none).</summary>
        public readonly string KeyAction;
        public readonly HotClick Click;
        public readonly int Arg;
        /// <summary>Live even without the HUD cursor (the minimap).</summary>
        public readonly bool AlwaysLive;
        /// <summary>The element moves every frame (world-layer markers).</summary>
        public readonly bool Moving;
        public readonly string ClickText;

        public HotspotDef(string id, string source, string canvas, string[] paths, string name, string meaning, string keyAction,
            HotClick click, int arg, string clickText, bool alwaysLive = false, bool moving = false)
        {
            Id = id; Source = source; Canvas = canvas; Paths = paths; Name = name; Meaning = meaning; KeyAction = keyAction;
            Click = click; Arg = arg; ClickText = clickText; AlwaysLive = alwaysLive; Moving = moving;
        }
    }

    /// <summary>
    /// Every HUD element on main (GameHud, PR #17) and on the open HUD branches (DG-11 kill feed, death recap, achievements,
    /// scoreboard; DG-12 mascot guide), with its tooltip and its click. Elements of a branch that is not in the build are
    /// simply never found. The report's table is generated from this list.
    /// </summary>
    public static class HotspotTable
    {
        const string Main = "main";
        static string[] P(params string[] p) => p;

        public static readonly HotspotDef[] All =
        {
            new HotspotDef("clock", Main, null, P("Match/Clock Pill"), "MATCH CLOCK",
                "Time left. When it hits zero, the team with more hill seconds wins.", "Scoreboard", HotClick.Scoreboard, 0, "Opens the scoreboard"),
            new HotspotDef("score-azure", Main, null, P("Match/Azure Panel"), "AZURE SCORE",
                "Seconds Azure has held the hill. Diamonds: Azure players on the hill now.", "Scoreboard", HotClick.Scoreboard, 0, "Opens the scoreboard"),
            new HotspotDef("score-crimson", Main, null, P("Match/Crimson Panel"), "CRIMSON SCORE",
                "Seconds Crimson has held the hill. Diamonds: Crimson players on the hill now.", "Scoreboard", HotClick.Scoreboard, 0, "Opens the scoreboard"),
            new HotspotDef("hill-bar", Main, null, P("Match/Hill Bar", "Match/Hill Word"), "HILL CONTROL",
                "Share of hill time per team. You score while more of your team stands on the hill than theirs.", "Ping", HotClick.PingHill, 0,
                "Pings the hill (defend if yours, attack if not)"),
            new HotspotDef("hill-medallion", Main, null, P("Match/Crown Medallion"), "HILL OWNER",
                "Who holds the hill now: neutral, Azure, Crimson or contested (half and half).", "Ping", HotClick.PingHill, 0,
                "Pings the hill"),
            new HotspotDef("kaiju-head", Main, null, P("Kaiju/Egg Frame"), "KAIJU",
                "The egg until it hatches, then the Kaiju in its colours: the team that fed it more owns it.", "Map", HotClick.KaijuCard, 0,
                "Opens the Kaiju card"),
            new HotspotDef("kaiju-meter", Main, null, P("Kaiju/Meter Frame", "Kaiju/Meter Cap"), "KAIJU METER",
                "Before the hatch: hatch progress. After it: the Kaiju's health.", "Map", HotClick.KaijuCard, 0, "Opens the Kaiju card"),
            new HotspotDef("kaiju-rage", Main, null, P("Kaiju/Rage Label", "Kaiju/Rage 0", "Kaiju/Rage 4", "Kaiju/Kaiju Word"), "RAGE",
                "Corruption fed to the egg. Full rage turns the Kaiju berserk for good: it fights everyone.", "Map", HotClick.KaijuCard, 0,
                "Opens the Kaiju card"),
            new HotspotDef("portrait", Main, null, P("Player/Portrait"), "YOUR CLASS",
                "Your class and team. Pick another while you wait to respawn.", null, HotClick.CharacterPanel, 0,
                "Opens the character panel"),
            new HotspotDef("health", Main, null, P("Player/Health Plate"), "HEALTH",
                "Four hearts, your health number and bar. At zero you are knocked out and respawn at base.", null, HotClick.HealthCard, 0,
                "Shows the exact numbers"),
            new HotspotDef("ability-attack", Main, null, P("Player/Ability 0", "Player/Key 0"), "ATTACK",
                "Your class's basic attack. The ring sweeps while it recovers.", "Attack", HotClick.AbilityCard, 0, "Opens the ability card"),
            new HotspotDef("ability-class", Main, null, P("Player/Ability 1", "Player/Key 1"), "CLASS ABILITY",
                "Your class's special move. Brass rim: ready. The number counts down the cooldown.", "Ability", HotClick.AbilityCard, 1,
                "Opens the ability card"),
            new HotspotDef("ability-carry", Main, null, P("Player/Ability 2", "Player/Key 2"), "CARRY",
                "What you carry. Biomass feeds the egg's allegiance to your team; corruption raises its rage.", "Interact", HotClick.AbilityCard, 2,
                "Opens the carry card"),
            new HotspotDef("minimap", Main, null, P("Minimap/Map Frame"), "MINIMAP",
                "Click for the full map. Hold and drag to look around. Enemies show only near a teammate.", "Map", HotClick.OpenMap, 0,
                "Opens the full map; drag peeks the camera", alwaysLive: true),
            new HotspotDef("pointer-hill", Main, null, P("World Layer/Pointer 0"), "HILL, OFF SCREEN",
                "Direction and distance to the hill.", "Ping", HotClick.PingHill, 0, "Pings the hill", moving: true),
            new HotspotDef("pointer-kaiju", Main, null, P("World Layer/Pointer 1"), "KAIJU, OFF SCREEN",
                "Direction and distance to the egg or the Kaiju.", "Ping", HotClick.PingKaiju, 0, "Pings the Kaiju", moving: true),
            new HotspotDef("hill-marker", Main, null, P("World Layer/Hill Marker"), "THE HILL",
                "The hill in the world, coloured by its owner.", "Ping", HotClick.PingHill, 0, "Pings the hill", moving: true),
            new HotspotDef("respawn", Main, null, P("Death/Centre"), "RESPAWN",
                "Seconds until you are back at your base, safe for a moment after.", null, HotClick.RespawnCard, 0,
                "Shows respawn and class switch info"),
            // open HUD branches (found only when that branch is in the build)
            new HotspotDef("kill-feed", "DG-11 (PR #20)", "Kill Feed Canvas", P("Kill Feed"), "KILL FEED",
                "Knockouts in the last few seconds. Your own lines are framed in brass.", "Scoreboard", HotClick.Scoreboard, 0, "Opens the scoreboard"),
            new HotspotDef("death-recap", "DG-11 (PR #20)", "Death Recap Canvas", P("Death Recap"), "KNOCKED OUT BY",
                "Who knocked you out and with what.", null, HotClick.RespawnCard, 0, "Shows respawn info"),
            new HotspotDef("achievement", "DG-11 (PR #20)", "Achievements Canvas", P("Achievement Toast"), "ACHIEVEMENT",
                "Something you just did for the first time.", null, HotClick.None, 0, "None (it fades on its own)"),
            new HotspotDef("scoreboard", "DG-11 (PR #20)", "Scoreboard Canvas", P("Scoreboard"), "SCOREBOARD",
                "Every player: class, knockouts, deaths, hill seconds and deliveries.", "Scoreboard", HotClick.Scoreboard, 0, "Closes the scoreboard"),
            new HotspotDef("mascot", "DG-12 (PR #22)", "Mascot Canvas", P("Mascot Panel", "Mascot Chip"), "GUIDE",
                "Your guide for the first match: the current step and what to do.", null, HotClick.None, 0, "None (the guide has its own buttons)"),
        };
    }

    /// <summary>
    /// Transparent hit areas over the HUD's own elements (found by name; the HUD root is not edited), with a hover or
    /// focus tooltip and a click. Rects follow their targets every frame (no allocation). The minimap is always live; the
    /// rest go live while the HUD cursor is free (Alt held) or in controller HUD focus, so aiming across the HUD in a fight
    /// never loses an attack. A live element under the mouse holds back attacks (InputGate).
    /// </summary>
    public sealed class HudHotspots
    {
        sealed class Spot
        {
            public HotspotDef Def;
            public RectTransform[] Targets;
            public Canvas Canvas;
            public bool Found;
            public Rect Rect;
            public bool Visible;
            public string Key;
        }

        Spot[] _spots;
        GameHud _hud;
        Transform _game;
        RectTransform _root;
        RectTransform _highlight;
        VectorShape _highlightShape;
        readonly Vector3[] _corners = new Vector3[4];
        float _nextFind;

        public int Count => _spots != null ? _spots.Length : 0;
        public HotspotDef Def(int i) => _spots[i].Def;
        public bool Found(int i) => _spots != null && _spots[i].Found;
        public bool Visible(int i) => _spots != null && _spots[i].Visible;
        public Rect RectOf(int i) => _spots[i].Rect;
        public string KeyText(int i) => _spots[i].Key;
        public int Focused { get; set; } = -1;

        public void Build(RectTransform root, GameHud hud, Transform game)
        {
            _root = root;
            _hud = hud;
            _game = game;
            _spots = new Spot[HotspotTable.All.Length];
            for (int i = 0; i < _spots.Length; i++) _spots[i] = new Spot { Def = HotspotTable.All[i] };
            _highlight = InteractUi.Centred(root, "HUD Focus", 10f, 10f);
            _highlightShape = _highlight.gameObject.AddComponent<VectorShape>();
            _highlightShape.Kind = ShapeKind.RoundRect;
            _highlightShape.Radius = 12f;
            _highlightShape.FillColor = CMTokens.WithAlpha(InteractUi.Brass, 0.12f);
            _highlightShape.OutlinePx = 3f;
            _highlightShape.RimPx = 3f;
            _highlightShape.RimColor = InteractUi.Brass;
            _highlightShape.raycastTarget = false;
            _highlight.gameObject.SetActive(false);
            FindTargets(true);
        }

        /// <summary>Finds targets not found yet (allocates; on build and at most once a second after).</summary>
        public void FindTargets(bool force = false)
        {
            if (_spots == null) return;
            if (!force && Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 2f;
            for (int i = 0; i < _spots.Length; i++)
            {
                Spot s = _spots[i];
                if (s.Found && TargetsAlive(s)) continue;
                Transform canvas = CanvasFor(s.Def);
                if (canvas == null) { s.Found = false; continue; }
                var list = new RectTransform[s.Def.Paths.Length];
                int n = 0;
                for (int p = 0; p < s.Def.Paths.Length; p++)
                {
                    Transform t = Resolve(canvas, s.Def.Paths[p]);
                    if (t is RectTransform rt) list[n++] = rt;
                }
                if (n == 0) { s.Found = false; continue; }
                if (n < list.Length) Array.Resize(ref list, n);
                s.Targets = list;
                s.Canvas = list[0].GetComponentInParent<Canvas>();
                s.Found = true;
            }
        }

        static bool TargetsAlive(Spot s)
        {
            if (s.Targets == null) return false;
            for (int i = 0; i < s.Targets.Length; i++) if (s.Targets[i] == null) return false;
            return true;
        }

        Transform CanvasFor(HotspotDef d)
        {
            if (d.Canvas == null) return _hud != null ? _hud.Root : null;
            // the other items' canvases are direct children of the game object (FeedUi.MakeCanvas, MascotGuide)
            return _game != null ? _game.Find(d.Canvas) : null;
        }

        /// <summary>A hotspot path under a canvas: as written, then under the canvas's "Safe Area" root (owner walk 1 moved the
        /// four HUD clusters there, so "Match/Clock Pill" lives at "Safe Area/Match/Clock Pill"), then a plain name anywhere.</summary>
        public static Transform Resolve(Transform canvas, string path) =>
            canvas.Find(path) ?? canvas.Find("Safe Area/" + path) ?? FindDeep(canvas, path);

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Re-reads every found target's screen rect into canvas-root units (centre origin). Allocation-free.</summary>
        public void UpdateRects(UnityEngine.Camera cam)
        {
            if (_spots == null) return;
            for (int i = 0; i < _spots.Length; i++)
            {
                Spot s = _spots[i];
                s.Visible = false;
                if (!s.Found || s.Targets == null) continue;
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                for (int k = 0; k < s.Targets.Length; k++)
                {
                    RectTransform t = s.Targets[k];
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    Canvas c = s.Canvas;
                    if (c != null && !c.enabled) continue;
                    UnityEngine.Camera tc = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? (c.worldCamera != null ? c.worldCamera : cam) : null;
                    t.GetWorldCorners(_corners);
                    for (int q = 0; q < 4; q++)
                    {
                        Vector2 screen = RectTransformUtility.WorldToScreenPoint(tc, _corners[q]);
                        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, cam, out Vector2 l)) continue;
                        if (l.x < xMin) xMin = l.x;
                        if (l.x > xMax) xMax = l.x;
                        if (l.y < yMin) yMin = l.y;
                        if (l.y > yMax) yMax = l.y;
                    }
                }
                if (xMax <= xMin || yMax <= yMin) continue;
                s.Rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                s.Visible = true;
            }
        }

        public bool IsLive(int i, bool cursorFree) => _spots[i].Visible && (cursorFree || _spots[i].Def.AlwaysLive);

        /// <summary>The live hotspot under a canvas-root point (the smallest when several overlap), or -1.</summary>
        public int HitTest(Vector2 rootLocal, bool cursorFree)
        {
            if (_spots == null) return -1;
            int best = -1;
            float bestArea = float.MaxValue;
            for (int i = 0; i < _spots.Length; i++)
            {
                if (!IsLive(i, cursorFree) || !_spots[i].Rect.Contains(rootLocal)) continue;
                float area = _spots[i].Rect.width * _spots[i].Rect.height;
                if (area < bestArea) { bestArea = area; best = i; }
            }
            return best;
        }

        /// <summary>Spatial navigation for controller focus: the nearest visible hotspot in a direction (or the first).</summary>
        public int Navigate(int from, Vector2 dir)
        {
            if (_spots == null) return -1;
            if (from < 0 || from >= _spots.Length || !_spots[from].Visible) return First();
            Vector2 c = _spots[from].Rect.center;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _spots.Length; i++)
            {
                if (i == from || !_spots[i].Visible) continue;
                Vector2 d = _spots[i].Rect.center - c;
                float along = Vector2.Dot(d, dir);
                if (along <= 1f) continue;
                float across = Mathf.Abs(d.x * dir.y - d.y * dir.x);
                float score = along + 2f * across;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best >= 0 ? best : from;
        }

        public int First()
        {
            for (int i = 0; i < _spots.Length; i++) if (_spots[i].Visible && _spots[i].Def.Id == "portrait") return i;
            for (int i = 0; i < _spots.Length; i++) if (_spots[i].Visible) return i;
            return -1;
        }

        /// <summary>Key prompt texts per hotspot for a device (allocates; on device or binding change only).</summary>
        public void RefreshKeys(UnityEngine.InputSystem.InputActionMap map, ControlDevice d)
        {
            if (_spots == null) return;
            for (int i = 0; i < _spots.Length; i++)
            {
                string a = _spots[i].Def.KeyAction;
                if (a == null) { _spots[i].Key = null; continue; }
                UnityEngine.InputSystem.InputAction act = map != null ? map.FindAction(a, false) : null;
                // Every key prompt goes through InputGlyphs (plain words from the current binding, never LMB / RMB).
                string k = InputGlyphs.Words(a, d);
                k = string.IsNullOrEmpty(k) || k == "-" ? (act != null ? ControlBindings.PromptLabel(act, d) : null) : k.ToUpperInvariant();
                if (string.IsNullOrEmpty(k) || k == "-") k = Fallback(a, d);
                _spots[i].Key = k;
            }
        }

        static string Fallback(string action, ControlDevice d)
        {
            bool pad = d == ControlDevice.Gamepad;
            switch (action)
            {
                case "Map": return pad ? "D-PAD UP" : "M";
                case "Ping": return pad ? "D-PAD LEFT" : "G";
                case "Scoreboard": return pad ? "VIEW" : "TAB";
                case "Attack": return pad ? "RT" : "LEFT CLICK";
                case "Ability": return pad ? "LT" : "RIGHT CLICK";
                case "Interact": return pad ? "X" : "E";
                default: return null;
            }
        }

        /// <summary>Draws the brass focus frame around a hotspot (-1 hides it).</summary>
        public void Highlight(int i)
        {
            if (_highlight == null) return;
            bool on = i >= 0 && i < _spots.Length && _spots[i].Visible;
            if (_highlight.gameObject.activeSelf != on) _highlight.gameObject.SetActive(on);
            if (!on) return;
            Rect r = _spots[i].Rect;
            _highlight.anchoredPosition = r.center;
            Vector2 size = r.size + new Vector2(10f, 10f);
            if ((_highlight.sizeDelta - size).sqrMagnitude > 0.25f) _highlight.sizeDelta = size;
        }
    }

    /// <summary>
    /// The HUD minimap's own controls: a click opens the full map; press and drag peeks the camera at the point under the
    /// cursor while held (view only, through CameraFollowTarget.SetOverride, which is public on main; back on release); a
    /// two-level zoom (1x, 2x centred on you, the baked image swapped to the next tier so it stays sharp) that keeps
    /// markers their size; rotation stays camera-up. The HUD root is not edited: the map area is re-parented under a
    /// clipping mask the first time zoom is used, and the HUD keeps placing its markers as before.
    /// </summary>
    public sealed class MinimapControls
    {
        /// <summary>The camera peek. On: the hook exists on main (CameraFollowTarget.SetOverride / ClearOverride).</summary>
        public static bool PeekEnabled = true;
        public const float DragThreshold = 6f, ClickMaxSeconds = 0.35f, Zoomed = 2f;

        GameHud _hud;
        RectTransform _area, _mask;
        CameraFollowTarget _follow;
        bool _lookedForFollow;
        bool _pressed, _peeking;
        Vector2 _pressAt;
        float _pressTime;
        Texture2D _zoomTex;

        public int ZoomLevel { get; private set; } = 1;
        public bool Peeking => _peeking;
        public bool Pressed => _pressed;
        public RectTransform Area => _area;

        public void Bind(GameHud hud)
        {
            _hud = hud;
            _area = hud != null ? hud.MapArea : null;
        }

        CameraFollowTarget Follow()
        {
            if (_follow == null && !_lookedForFollow)
            {
                _lookedForFollow = true;
                _follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            }
            return _follow;
        }

        /// <summary>Screen point to the sim ground point under it on the minimap (zoom included); false when outside.</summary>
        public bool ScreenToSim(Vector2 screen, UnityEngine.Camera cam, out double x, out double z)
        {
            x = z = 0;
            if (_area == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screen, cam, out Vector2 local)) return false;
            Rect r = _area.rect;
            if (!r.Contains(local)) return false;
            MinimapProjection p = MinimapGeometry.Projection;
            p.ToSim(local.x - r.xMin, r.yMax - local.y, 1, out x, out z);
            return true;
        }

        /// <summary>Mouse went down over the minimap.</summary>
        public void Down(Vector2 screen, float now)
        {
            _pressed = true;
            _peeking = false;
            _pressAt = screen;
            _pressTime = now;
        }

        /// <summary>While held: past the drag threshold (or the click time) the camera peeks at the point under the cursor.</summary>
        public void Held(Vector2 screen, float now, UnityEngine.Camera cam, float canvasScale)
        {
            if (!_pressed) return;
            if (!_peeking && ((screen - _pressAt).magnitude > DragThreshold * Mathf.Max(1f, canvasScale) || now - _pressTime > ClickMaxSeconds))
                _peeking = PeekEnabled && Follow() != null;
            if (_peeking && ScreenToSim(screen, cam, out double x, out double z)) Follow().SetOverride(x, z);
        }

        /// <summary>Mouse released: true when it was a click (open the full map). A peek ends and the camera flies home.</summary>
        public bool Up(float now)
        {
            if (!_pressed) return false;
            _pressed = false;
            bool click = !_peeking && now - _pressTime <= ClickMaxSeconds;
            EndPeek();
            return click;
        }

        public void EndPeek()
        {
            if (_peeking && _follow != null) _follow.ClearOverride();
            _peeking = false;
            _pressed = false;
        }

        public void ToggleZoom() => SetZoom(ZoomLevel == 1 ? 2 : 1);

        public void SetZoom(int level)
        {
            if (_area == null) return;
            level = level >= 2 ? 2 : 1;
            if (level == ZoomLevel) return;
            ZoomLevel = level;
            if (level == 2) EnsureMask();
            if (level == 1) Unzoom();
        }

        void EnsureMask()
        {
            if (_mask != null || _area == null) return;
            Transform cluster = _area.parent;
            var go = new GameObject("Map Zoom Mask", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(cluster, false);
            _mask = (RectTransform)go.transform;
            _mask.anchorMin = _area.anchorMin;
            _mask.anchorMax = _area.anchorMax;
            _mask.pivot = _area.pivot;
            _mask.sizeDelta = _area.sizeDelta;
            _mask.anchoredPosition = _area.anchoredPosition;
            _mask.SetSiblingIndex(_area.GetSiblingIndex());
            go.AddComponent<RectMask2D>();
            _area.SetParent(_mask, false);
            _area.anchorMin = _area.anchorMax = _area.pivot = new Vector2(0.5f, 0.5f);
            _area.anchoredPosition = Vector2.zero;
        }

        void Unzoom()
        {
            _area.localScale = Vector3.one;
            if (_mask != null) _area.anchoredPosition = Vector2.zero;
            ScaleMarkers(1f);
            RawImage img = _area.GetComponent<RawImage>();
            if (img != null && _hud != null) img.texture = Ui.MinimapGeometry.Texture(_hud.MapTier);
            _zoomTex = null;
        }

        /// <summary>Per frame after the HUD placed its markers: keeps the zoomed map centred on you.</summary>
        public void LateTick(World w, int me)
        {
            if (ZoomLevel == 1 || _area == null || w == null) return;
            float k = Zoomed;
            _area.localScale = new Vector3(k, k, 1f);
            ScaleMarkers(1f / k);
            MinimapProjection p = MinimapGeometry.Projection;
            Vector2 c = new Vector2(p.Width * 0.5f, p.Height * 0.5f);
            if (me >= 0 && me < w.ActorCount) c = p.ToPixel(w.X[me], w.Z[me]);
            Vector2 local = new Vector2(c.x - p.Width * 0.5f, p.Height * 0.5f - c.y);
            float mx = (k - 1f) * p.Width * 0.5f, my = (k - 1f) * p.Height * 0.5f;
            _area.anchoredPosition = new Vector2(Mathf.Clamp(-k * local.x, -mx, mx), Mathf.Clamp(-k * local.y, -my, my));
            RawImage img = _area.GetComponent<RawImage>();
            if (img != null && _hud != null)
            {
                if (_zoomTex == null) _zoomTex = Ui.MinimapGeometry.Texture(Mathf.Min(4, _hud.MapTier * 2));
                if (_zoomTex != null && img.texture != _zoomTex) img.texture = _zoomTex;
            }
        }

        void ScaleMarkers(float s)
        {
            Vector3 v = new Vector3(s, s, 1f);
            for (int i = 0; i < _area.childCount; i++)
            {
                Transform t = _area.GetChild(i);
                if (t.name == "Ground Changes") continue;
                if (t.localScale != v) t.localScale = v;
            }
        }
    }
}
