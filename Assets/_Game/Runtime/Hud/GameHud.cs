using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// The in-game HUD of bible p15 (match top-centre, Kaiju top-right, player bottom-left, minimap bottom-right) plus the
    /// world layer of art direction 6.3/6.4 that lives on screen (hill marker, off-screen pointers, overhead hearts, damage
    /// numbers, death state).
    ///
    /// uGUI, not UI Toolkit: a Screen Space - Camera canvas is drawn inside the gameplay camera's own render, so the capture
    /// tool's render-to-texture shows it with no second path, and every shape is a VectorShape tessellated at the live
    /// canvas scale (sharp at 720p, 1080p and 4K).
    ///
    /// Read-only on the Sim: each LateUpdate copies the World into a HudState (HudBinder.Read, allocation-free) and applies
    /// it with change checks; animation (health drain, heart pop, cooldown sweep, ready flash) is view-side only.
    /// </summary>
    // Physics audit M3: LateUpdate after CinemachineBrain (order 100) and the camera shake (150), so damage numbers,
    // hearts, the hill marker and edge pointers are projected with the camera this frame is rendered from.
    [DefaultExecutionOrder(200)]
    public sealed class GameHud : MonoBehaviour
    {
        public SimDriverBehaviour Driver;
        /// <summary>Capture/preview hook: edits the state copy after it is read (never the Sim).</summary>
        public static Action<HudState> PreviewOverride;
        /// <summary>true: animations jump to their targets (deterministic captures).</summary>
        public bool SnapAnimations;

        public readonly HudState State = new HudState();
        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }
        public RectTransform[] Clusters { get; } = new RectTransform[4];

        const string MatchesPlayedKey = "cm.hud.matchesPlayed";
        const int AbilityNameMatches = 3;

        // ---------------------------------------------------------------- build

        void Awake() => Build();

        public void Build()
        {
            if (Root != null) return;
            long tg = LoadTimer.Now();
            CMTokens.PushShaderGlobals();
            LoadTimer.Part("hud globals", tg);
            var go = new GameObject("HUD Canvas", typeof(RectTransform));
            go.layer = 5; // UI
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 10;
            Canvas.planeDistance = 0.5f;
            Canvas.pixelPerfect = false;
            Root = (RectTransform)go.transform;
            AttachCamera();

            // perf/start-hitch-1: the cold boot spends about 970 ms in this build (19 ms on a reload); name its stages.
            long t0 = LoadTimer.Now();
            BuildWorldLayer();
            LoadTimer.Part("hud world layer", t0);
            t0 = LoadTimer.Now();
            Clusters[(int)HudCluster.Match] = Cluster(HudCluster.Match);
            Clusters[(int)HudCluster.Kaiju] = Cluster(HudCluster.Kaiju);
            HudZones.RegisterPanel(Clusters[(int)HudCluster.Match]);   // fix/hud-containment-5: the top band (clock, scores,
            HudZones.RegisterPanel(Clusters[(int)HudCluster.Kaiju]);   // hill bar and its word) and the Kaiju panel mask world labels
            Clusters[(int)HudCluster.Player] = Cluster(HudCluster.Player);
            Clusters[(int)HudCluster.Minimap] = Cluster(HudCluster.Minimap);
            HudZones.RegisterPanel(Clusters[(int)HudCluster.Player]);    // fix/hud-containment-4: world labels stay off
            HudZones.RegisterPanel(Clusters[(int)HudCluster.Minimap]);   // the player card and the minimap
            // Owner walk 1: the four clusters sit in the safe area (the camera notch of a full-screen MacBook, overscan).
            RectTransform safe = FeedUi.SafeRoot(Root);
            for (int i = 0; i < Clusters.Length; i++)
            {
                Clusters[i].SetParent(safe, false);
                HudKeepOut.Add(Clusters[i]);   // world labels and edge pointers keep off the panels
            }
            LoadTimer.Part("hud clusters", t0);
            t0 = LoadTimer.Now();
            BuildMatch(Clusters[0]);
            LoadTimer.Part("hud match", t0);
            t0 = LoadTimer.Now();
            BuildKaiju(Clusters[1]);
            LoadTimer.Part("hud kaiju", t0);
            t0 = LoadTimer.Now();
            BuildPlayer(Clusters[2]);
            LoadTimer.Part("hud player", t0);
            t0 = LoadTimer.Now();
            BuildMinimap(Clusters[3]);
            LoadTimer.Part("hud minimap", t0);
            t0 = LoadTimer.Now();
            BuildDeath();
            // player-experience lane D: the local player's combat HUD (bars, hit marker, direction, kills, tags, tally)
            PlayerHud = new CriticalMass.Runtime.Hud.Player.PlayerHud(Root, _world, Clusters[(int)HudCluster.Player]);
            UpdateScale();
            LoadTimer.Part("hud death and scale", t0);
        }

        void AttachCamera()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null && Canvas.worldCamera != cam)
            {
                Canvas.worldCamera = cam;
                Canvas.planeDistance = Mathf.Max(0.5f, cam.nearClipPlane * 1.5f);
            }
        }

        /// <summary>Sets the canvas scale for the camera's current pixel size (capture: after setting targetTexture).</summary>
        public void UpdateScale()
        {
            UnityEngine.Camera cam = Canvas.worldCamera;
            float w = cam != null ? cam.pixelWidth : Screen.width;
            float h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(Canvas.scaleFactor - s) > 1e-4f) Canvas.scaleFactor = s;
            int tier = UiIcons.TierFor(s);
            if (tier != _tier)
            {
                _tier = tier;
                for (int i = 0; i < _icons.Count; i++) _icons[i].SetTier(tier);
            }
            int mapTier = MinimapGeometry.TierFor(s);
            if (_mapImage != null && (mapTier != _mapTier || _mapImage.texture == null))
            {
                _mapTier = mapTier;
                // polish-3: the image may still be baking on a worker (null): keep it hidden (a RawImage with no texture
                // draws white) and ask again next frame; never blocks the main thread.
                Texture2D tex = MapTexture(mapTier);
                if (tex != null || _mapImage.texture == null) _mapImage.texture = tex;
                bool show = _mapImage.texture != null;
                if (_mapImage.enabled != show) _mapImage.enabled = show;
            }
        }

        // ---------------------------------------------------------------- illustrated icons (v2)

        readonly System.Collections.Generic.List<HudIcon> _icons = new System.Collections.Generic.List<HudIcon>(64);
        int _tier = 2;
        public int IconTier => _tier;
        public System.Collections.Generic.IReadOnlyList<HudIcon> Icons => _icons;

        HudIcon Icon(Transform parent, string goName, string icon, float x, float y, float w, float h, bool leftPivot = false)
        {
            HudIcon i = HudIcon.Make(parent, goName, x, y, w, h, leftPivot);
            i.Set(icon, _tier);
            _icons.Add(i);
            return i;
        }

        RectTransform Cluster(HudCluster c)
        {
            RectTransform rt = Rt(Root, c.ToString());
            Vector2 a = HudLayout.Anchor(c);
            rt.anchorMin = rt.anchorMax = rt.pivot = a;
            rt.sizeDelta = HudLayout.RefRect(c).size;
            rt.anchoredPosition = HudLayout.AnchoredPosition(c);
            return rt;
        }

        static RectTransform Rt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Places rt at (x, y) from the parent's top-left, y down, size (w, h).</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        }

        static VectorShape Shape(Transform parent, string name, ShapeKind kind, float x, float y, float w, float h, Color fill,
            float radius = 8f)
        {
            RectTransform rt = Rt(parent, name);
            Place(rt, x, y, w, h);
            VectorShape s = rt.gameObject.AddComponent<VectorShape>();
            s.Kind = kind;
            s.Radius = radius;
            s.FillColor = fill;
            s.raycastTarget = false;
            return s;
        }

        static Text Label(Transform parent, string name, Typeface face, int size, Color c, TextAnchor anchor, float x, float y,
            float w, float h, string text, float outline = CMTokens.OutlinePx)
        {
            Text t = HudText.Make(parent, name, face, size, c, anchor, outline);
            Place(t.rectTransform, x, y, w, h);
            t.text = text;
            return t;
        }

        static Color Navy => CMTokens.VoidNavy.Color;
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Panel => CMTokens.PanelNavy.Color;
        static Color Empty => CMTokens.MeterEmpty.Color;

        // ---------------------------------------------------------------- 01 MATCH

        /// <summary>b16 finish: ten hill pips, one per hill point (a Warrior fills two), so the pips and the number agree.</summary>
        public const int Pips = 10;
        const float PipPitch = 11f;
        Text _clock, _scoreA, _scoreC, _hillWord, _onHillA, _onHillC;
        VectorShape _hillBar, _medallion, _medallionHalf;
        readonly VectorShape[] _pipsA = new VectorShape[Pips], _pipsC = new VectorShape[Pips];

        void BuildMatch(RectTransform c)
        {
            // team panels: Azure left, Crimson right (team by position, emblem and words, not colour alone)
            BuildTeamPanel(c, TeamId.Azure, 36f, out _scoreA, _pipsA, out _onHillA);
            BuildTeamPanel(c, TeamId.Crimson, 456f, out _scoreC, _pipsC, out _onHillC);
            // clock pill
            Shape(c, "Clock Pill", ShapeKind.RoundRect, 316f, 0f, 168f, 72f, Navy, -1f);
            _clock = Label(c, "Clock", CMTokens.Mono, UiScale.TypeDisplay, Cream, TextAnchor.MiddleCenter, 316f, 2f, 168f, 68f, "08:00", 0f);
            // hill-control bar with the crown medallion
            _hillBar = Shape(c, "Hill Bar", ShapeKind.RoundRect, 236f, 80f, 328f, 26f, CMTokens.Azure.Color, -1f);
            _hillBar.EmptyColor = CMTokens.Crimson.Color;
            _medallion = Shape(c, "Crown Medallion", ShapeKind.Circle, 370f, 62f, 60f, 60f, CMTokens.Stone.Color);
            _medallionHalf = Shape(_medallion.transform, "Contested Half", ShapeKind.Pie, 5f, 5f, 50f, 50f, CMTokens.Crimson.Color);
            _medallionHalf.Fill01 = 0.5f;
            _medallionHalf.gameObject.SetActive(false);
            Icon(_medallion.transform, "Crown", UiIcons.Crown, 13f, 11f, 34f, 34f);
            _hillWord = Label(c, "Hill Word", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, 250f, 106f, 300f, 22f, "NEUTRAL");   // fix/hud-containment-5: ends at the band's 128 (was 130, under the job line's gap)
            // b15 view polish (Editor review item 8): the Kaiju-on-the-hill state is a pill under the hill meter, not the
            // small brass word (B15Hud.KaijuPillRect: 340 x 28, 18 px type, 16 px floor, clear of the objective line).
            Rect pr = CriticalMass.Runtime.Hud.Mechanics.B15Hud.KaijuPillRect;
            float mx = HudLayout.RefRect(HudCluster.Match).x;
            _kaijuPill = Rt(c, "Kaiju Hill Pill");
            Place(_kaijuPill, pr.x - mx, pr.y, pr.width, pr.height);
            _kaijuPillShape = Shape(_kaijuPill, "Fill", ShapeKind.RoundRect, 0f, 0f, pr.width, pr.height, Panel, pr.height * 0.5f);
            _kaijuPillShape.OutlinePx = 3f;
            _kaijuPillText = CriticalMass.Runtime.Menu.MenuUi.Fit(Label(_kaijuPill, "Text", CMTokens.Display, CriticalMass.Runtime.Hud.Mechanics.B15Hud.KaijuPillFont,
                Cream, TextAnchor.MiddleCenter, 10f, 0f, pr.width - 20f, pr.height, "", 0f), CriticalMass.Runtime.Hud.Mechanics.B15Hud.KaijuPillFloor);
            _kaijuPill.gameObject.SetActive(false);
        }

        RectTransform _kaijuPill;
        VectorShape _kaijuPillShape;
        Text _kaijuPillText;
        int _kaijuPillState = -1;   // -1 hidden, 0 your Kaiju, 1 their Kaiju
        float _kaijuPillAt = -10f;

        /// <summary>b15 view polish: the Kaiju-on-the-hill pill (tests read its rect, text and type size).</summary>
        public RectTransform KaijuPill => _kaijuPill;
        public Text KaijuPillText => _kaijuPillText;

        void ApplyKaijuPill(HudState s)
        {
            int state = s.KaijuBlocksHill < 0 ? -1 : (s.HasPlayer && (int)s.Team == s.KaijuBlocksHill ? 0 : 1);
            if (state != _kaijuPillState)
            {
                _kaijuPillState = state;
                bool on = state >= 0;
                _kaijuPill.gameObject.SetActive(on);
                _hillWord.gameObject.SetActive(!on);
                if (on)
                {
                    _kaijuPillAt = Time.unscaledTime;
                    HudText.Set(_kaijuPillText, state == 0 ? ComebackNames.KaijuHillOwnWord : ComebackNames.KaijuHillEnemyWord);
                    _kaijuPillText.color = state == 0 ? Navy : Cream;
                    _kaijuPillShape.RimColor = state == 0 ? Navy : Cream;
                    _kaijuPillShape.RimPx = 2f;
                    _kaijuPillDirty = true;
                }
            }
            if (state < 0) return;
            float t = SnapAnimations ? 10f : Time.unscaledTime - _kaijuPillAt;
            float k = CriticalMass.Runtime.Hud.Mechanics.B15Hud.Pulse(t);
            _kaijuPill.localScale = new Vector3(k, k, 1f);
            Color baseFill = state == 0 ? CMTokens.ParseHex(CriticalMass.Runtime.Hud.Mechanics.B15Hud.KaijuPillWarnHex) : Panel;
            float flash = CriticalMass.Runtime.Hud.Mechanics.B15Hud.PulseFlash(t, !CriticalMass.Runtime.Menu.GameSettings.ReduceFlashing);
            Color fill = flash > 0f ? Color.Lerp(baseFill, Cream, flash) : baseFill;
            _kaijuPillShape.SetFill(fill);
            if (_kaijuPillDirty) { _kaijuPillDirty = false; _kaijuPillShape.SetVerticesDirty(); }
        }

        bool _kaijuPillDirty = true;

        void BuildTeamPanel(RectTransform c, TeamId team, float x, out Text score, VectorShape[] pips, out Text onHill)
        {
            bool azure = team == TeamId.Azure;
            Color tc = CMTokens.Team(team);
            Shape(c, team + " Panel", ShapeKind.Chamfer, x, 4f, 308f, 64f, Panel, 14f);
            float shieldX = azure ? x - 6f : x + 308f - 52f;
            Icon(c, team + " Crest", UiIcons.Crest[(int)team], shieldX - 6f, 0f, 70f, 70f);   // illustrated crest (v2)
            float pipX0 = azure ? x + 64f : x + 308f - 64f - Pips * PipPitch;
            for (int i = 0; i < Pips; i++)
            {
                int slot = azure ? i : Pips - 1 - i;
                VectorShape p = Shape(c, team + " Pip " + i, ShapeKind.Diamond, pipX0 + slot * PipPitch, 13f, 10f, 22f, Empty);
                p.OutlinePx = 1.5f;
                p.RimPx = 0f;
                pips[i] = p;
            }
            // b16 finish: the count at TypeSmall (18, never under 16) in cream, so "HILL 10/10" reads at 1080p (was 16 -> ~9 px brass)
            onHill = CriticalMass.Runtime.Menu.MenuUi.Fit(Label(c, team + " On Hill", CMTokens.Ui, UiScale.TypeSmall, Cream, azure ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                pipX0 - 2f, 37f, Pips * PipPitch + 4f, 26f, "HILL 0/10", 2f), UiScale.TypeMin);
            score = CriticalMass.Runtime.Menu.MenuUi.Fit(Label(c, team + " Score", CMTokens.Mono, UiScale.TypeDisplay, Cream, azure ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                azure ? 168f : 500f, 4f, 132f, 64f, "0"), 44);
        }

        // ---------------------------------------------------------------- 02 KAIJU

        const int MeterSegments = 8;
        /// <summary>hud-art-1: the egg badge shows the cracking egg in the last seconds before the hatch.</summary>
        public const double CrackSeconds = 30.0;
        public const int RagePips = 5;
        HudIcon _kaijuHead;
        VectorShape _eggFrame;
        Text _kaijuWord, _kaijuNumber;
        readonly VectorShape[] _segments = new VectorShape[MeterSegments];
        readonly VectorShape[] _rage = new VectorShape[RagePips];

        /// <summary>The Kaiju word (hatch timer or allegiance) sits between the last rage pip and the panel edge.</summary>
        public const float KaijuWordX = RagePipX0 + RagePips * RagePipPitch + 4f, KaijuWordRight = 338f;
        public const float RagePipX0 = 136f, RagePipPitch = 22f;
        public const int KaijuWordMax = UiScale.TypeSmall, KaijuWordFloor = 12;

        void BuildKaiju(RectTransform c)
        {
            VectorShape frame = Shape(c, "Egg Frame", ShapeKind.Chamfer, 0f, 0f, 88f, 98f, Navy, 16f);
            _eggFrame = frame;
            _kaijuHead = Icon(frame.transform, "Kaiju", UiIcons.Egg, 6f, 11f, 76f, 76f);   // egg, hatchling or adult head
            Shape(c, "Meter Frame", ShapeKind.RoundRect, 80f, 12f, 216f, 42f, Navy, 10f);
            for (int i = 0; i < MeterSegments; i++)
            {
                VectorShape s = Shape(c, "Segment " + i, ShapeKind.RoundRect, 91f + i * 24.6f, 21f, 21f, 24f, Empty, 3f);
                s.OutlinePx = 0f;
                s.RimPx = 0f;
                _segments[i] = s;
            }
            VectorShape cap = Shape(c, "Meter Cap", ShapeKind.Hexagon, 288f, 6f, 52f, 54f, Navy);
            _kaijuNumber = Label(cap.transform, "Meter Number", CMTokens.Mono, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, 0f, 0f, 52f, 54f, "0", 2f);
            CriticalMass.Runtime.Menu.MenuUi.Fit(Label(c, "Rage Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, 92f, 60f, 44f, 22f, "RAGE", 2f), 13);
            for (int i = 0; i < RagePips; i++)
            {
                VectorShape p = Shape(c, "Rage " + i, ShapeKind.TriangleDown, RagePipX0 + i * RagePipPitch, 62f, 20f, 16f, Empty);
                p.OutlinePx = 2f;
                p.RimPx = 0f;
                _rage[i] = p;
            }
            // hud-overlaps-1: the word's box starts after the last rage pip (it began at x 214 and "HATCH 10:00", right
            // aligned, ran over the pips); the word shrinks to fit and is cut with an ellipsis below the floor.
            _kaijuWord = Label(c, "Kaiju Word", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleRight, KaijuWordX, 60f, KaijuWordRight - KaijuWordX, 28f, "EGG");
        }

        // ---------------------------------------------------------------- 03 PLAYER

        const int Hearts = 4;
        VectorShape _portraitFrame, _hpBar, _hpGhost;
        HudIcon _portrait, _classBadge;
        Text _className, _hpNumber;
        readonly HeartSlot[] _hearts = new HeartSlot[Hearts];

        sealed class HeartSlot
        {
            public RectTransform Rt;
            public HudIcon Empty, Full;
        }
        readonly AbilityButton[] _buttons = new AbilityButton[3];
        float _hpShown = -1f, _hpGhostShown = -1f, _ghostHold;
        readonly float[] _heartPop = new float[Hearts];
        readonly float[] _heartLast = new float[Hearts];

        sealed class AbilityButton
        {
            public RectTransform Rt;
            public VectorShape Body, Sweep;
            public HudIcon Icon;
            public Text Seconds, Name, Count;
            public float Flash;
            public bool WasCooling;
        }

        void BuildPlayer(RectTransform c)
        {
            _portraitFrame = Shape(c, "Portrait", ShapeKind.Chamfer, 0f, 6f, 116f, 120f, Panel, 18f);
            _portraitFrame.RimPx = 3f;
            // v2: the illustrated class portrait (bible p15 player card), a class badge and the class name on a plate
            _portrait = Icon(_portraitFrame.transform, "Portrait", UiIcons.Portrait[0, 0], 5f, 1f, 106f, 106f);   // fills the card like p15
            // Closer (batch 4): the plate spans the card (was 104 px: DEMOLITIONS was cut); the name best-fits down to 12 px.
            Shape(_portraitFrame.transform, "Name Plate", ShapeKind.RoundRect, 2f, 92f, 112f, 24f, Navy, -1f).RimPx = 0f;
            _classBadge = Icon(_portraitFrame.transform, "Class Badge", UiIcons.ClassBadge[0, 0], -6f, -6f, 36f, 36f);
            _className = CriticalMass.Runtime.Menu.MenuUi.Fit(Label(_portraitFrame.transform, "Class Name", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, 2f, 92f, 112f, 24f,
                "WARRIOR", 2f), 12);

            Shape(c, "Health Plate", ShapeKind.RoundRect, 124f, 22f, 252f, 90f, Panel, 14f);
            for (int i = 0; i < Hearts; i++)
            {
                RectTransform hr = Rt(c, "Heart " + i);
                Place(hr, 136f + i * 36f, 30f, 34f, 34f);
                _hearts[i] = new HeartSlot
                {
                    Rt = hr,
                    Empty = Icon(hr, "Empty", UiIcons.HeartEmpty, 0f, 0f, 34f, 34f),
                    Full = Icon(hr, "Full", UiIcons.Heart[0], 0f, 0f, 34f, 34f, true),
                };
            }
            _hpNumber = Label(c, "HP Number", CMTokens.Mono, UiScale.TypeBody, Cream, TextAnchor.MiddleRight, 284f, 30f, 82f, 32f, "160", 2f);
            _hpGhost = Shape(c, "HP Ghost", ShapeKind.RoundRect, 136f, 72f, 228f, 28f, CriticalMass.Runtime.Hud.Player.PxPalette.Chunk, -1f);
            _hpGhost.EmptyColor = Empty;
            _hpBar = Shape(c, "HP Bar", ShapeKind.RoundRect, 136f, 72f, 228f, 28f, CMTokens.Azure.Color, -1f);

            string[] keys = { "", "", "" };   // drawn by HudPrompts through InputGlyphs (never a literal key name);   // U-006 / U-020: attack, class ability, carry/interact
            for (int i = 0; i < 3; i++)
            {
                float bx = HudLayout.AbilityX0 + i * HudLayout.AbilityPitch;
                var b = new AbilityButton();
                b.Body = Shape(c, "Ability " + i, ShapeKind.RoundRect, bx, 6f, HudLayout.AbilityButton, HudLayout.AbilityButton, Panel, 16f);
                b.Rt = b.Body.rectTransform;
                b.Icon = Icon(b.Body.transform, "Icon", i == 2 ? UiIcons.CarryEmpty : null, 10f, 7f, 56f, 56f);
                b.Sweep = Shape(b.Body.transform, "Cooldown", ShapeKind.Pie, 5f, 5f, 66f, 66f, CMTokens.WithAlpha(Navy, 0.72f));
                b.Sweep.Fill01 = 0f;
                b.Seconds = Label(b.Body.transform, "Seconds", CMTokens.Mono, UiScale.TypeHeading, Cream, TextAnchor.MiddleCenter, 0f, 0f, 76f, 76f, "", 3f);
                b.Count = Label(b.Body.transform, "Count", CMTokens.Mono, UiScale.TypeBody, Cream, TextAnchor.MiddleRight, 34f, 44f, 38f, 28f, "", 3f);
                Shape(c, "Key " + i, ShapeKind.RoundRect, bx + 14f, 86f, 48f, 28f, Navy, -1f).RimColor = Cream;
                CriticalMass.Runtime.Menu.MenuUi.Fit(Label(c, "Key Label " + i, CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, bx + 14f, 86f, 48f, 28f, keys[i], 0f), 15);
                // hud-overlaps-1: one line, measured and laid out with its neighbours (LayoutAbilityNames); was a
                // 104 px best-fit box on the 84 px pitch, so wide neighbours overlapped.
                b.Name = Label(c, "Ability Name " + i, CMTokens.Ui, HudLayout.AbilityNameMax, Cream, TextAnchor.MiddleCenter, bx, AbilityNameY, HudLayout.AbilityButton, AbilityNameH, "", 2f);
                _buttons[i] = b;
            }
            bool showNames = PlayerPrefs.GetInt(MatchesPlayedKey, 0) < AbilityNameMatches;
            for (int i = 0; i < 3; i++) _buttons[i].Name.gameObject.SetActive(showNames);
        }

        // Six classes (batch 3): names of the new ones come from the sim's name tables (DemolitionsNames, BuilderNames, HealerNames).
        /// <summary>The six class names as the HUD shows them; the info cards and the full map read this list too
        /// (skills-and-walls-audit-1: their own five-name copies clamped a Healer to BUILDER).</summary>
        public static readonly string[] ClassNames =
        {
            "WARRIOR", "FARMER", "MINER", CriticalMass.Sim.Core.DemolitionsNames.ClassName, CriticalMass.Sim.Core.BuilderNames.ClassName,
            CriticalMass.Sim.Core.HealerNames.ClassName,
        };
        static readonly string[] AbilityNames =
        {
            "SHIELD", "ROOTS", MinerAbility, CriticalMass.Sim.Core.DemolitionsNames.Mine, CriticalMass.Sim.Core.BuilderNames.Build,
            CriticalMass.Sim.Core.HealerNames.Pulse,
        };
        /// <summary>skills-and-walls-audit-1: the Miner's Space skill places a barricade (Barricades.cs); breaking walls is
        /// his normal swing. The HUD said WALL BREAK.</summary>
        public const string MinerAbility = "HAUL";   // b13: the Miner's Space is HAUL (item H)
        static readonly string[] AttackNames =
        {
            // feat/light-weapons-1: the Demolitions slot names her pry bar (was NO SWORD), the Builder's his mallet; the
            // Healer's press still MENDS first (the lantern swings only when nobody can be mended). b12 r3: under the b12-jz
            // rule her attack throws TNT when ready, so the slot reads TNT then (DemolitionsTntWord) and PRY BAR while it reloads.
            "ATTACK", "ATTACK", "ATTACK", CriticalMass.Sim.Core.LightWeapons.DemolitionsName, CriticalMass.Sim.Core.LightWeapons.BuilderName,
            CriticalMass.Sim.Core.HealerNames.Mend,
        };

        /// <summary>The icon row for a class: the Healer has no icon art yet and borrows the Farmer's (support) row.</summary>
        static int IconRow(int cls) => cls >= UiIcons.Classes.Length ? 1 : cls;

        /// <summary>feat/light-weapons-1 placeholder: the Demolitions attack slot borrows the Builder's hammer icon (existing
        /// art) until a pry-bar icon is drawn; every other class keeps its own row.</summary>
        static int AttackIconRow(int cls) => cls == (int)ClassId.Demolitions && (int)ClassId.Builder < UiIcons.Classes.Length ? (int)ClassId.Builder : IconRow(cls);

        /// <summary>b12 r3: the attack slot word when the Demolitionist's attack press throws TNT now.</summary>
        public const string DemolitionsTntWord = CriticalMass.Sim.Core.DemolitionsNames.Tnt;

        /// <summary>b12 r3: the attack slot's icon row: the Demolitionist's own (the TNT bundle) while her attack throws TNT,
        /// else the pry-bar stand-in (AttackIconRow).</summary>
        static int SlotIconRow(int cls, bool demoTntReady) => cls == (int)ClassId.Demolitions && demoTntReady ? IconRow(cls) : AttackIconRow(cls);

        // ---------------------------------------------------------------- minimap

        const int MaxDots = 40;
        /// <summary>An enemy shows on the minimap only within this many sim units of a living teammate (seen enemies).</summary>
        public const double SightRadius = 16.0;
        RawImage _mapImage;
        MinimapGround _ground;
        /// <summary>Batch 12 item P: the fog-of-war grey over the minimap (shown only while the fog is drawn).</summary>
        global::CriticalMass.Runtime.View.Fog.FogMapLayer _mapFog;
        RectTransform _mapArea;
        readonly VectorShape[] _dots = new VectorShape[MaxDots];
        VectorShape _mapHill;
        HudIcon _mapEgg;
        RectTransform _mapYou, _youFacing;
        MapDef _mapFor;
        MinimapProjection _proj;
        int _mapTier = 1;
        public RectTransform MapArea => _mapArea;
        public int MapTier => _mapTier;
        public MinimapProjection MapProjection => _proj;

        /// <summary>
        /// Minimap: the image baked from the real map (Ui/MinimapBake, camera-up like the gameplay view), ground changes
        /// over it, then the live layer from sim state: teammates and seen enemies as team dots, the hill tinted by its
        /// owner, the Kaiju egg / hatchling / adult in its allegiance, both base banners, and the brass YOU arrow turning
        /// with the player's facing. Every marker goes through the one projection the image was baked with.
        /// </summary>
        void BuildMinimap(RectTransform c)
        {
            _proj = MinimapGeometry.Projection;
            float f = HudLayout.MinimapFrame, w = _proj.Width, h = _proj.Height;
            Shape(c, "Map Frame", ShapeKind.Chamfer, 0f, 0f, w + 2f * f, h + 2f * f, Navy, 20f);
            _mapArea = Rt(c, "Map Area");
            Place(_mapArea, f, f, w, h);
            MinimapCanvas = NestedCanvas(_mapArea);   // M5: 40 moving dots and markers re-batch only the map
            _mapImage = _mapArea.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = false;
            _mapImage.texture = MapTexture(_mapTier);   // polish-3: null while the worker bake runs (UpdateScale asks again)
            _mapImage.enabled = _mapImage.texture != null;
            RectTransform g = Rt(_mapArea, "Ground Changes");
            g.anchorMin = Vector2.zero;
            g.anchorMax = Vector2.one;
            g.sizeDelta = Vector2.zero;
            _ground = g.gameObject.AddComponent<MinimapGround>();
            _ground.raycastTarget = false;
            _ground.Bind(_proj, MinimapGeometry.BakedMap);
            // Batch 12 item P: the fog-of-war grey, over the ground changes and under every dot and marker
            RectTransform fg = Rt(_mapArea, "Fog Of War");
            fg.anchorMin = Vector2.zero;
            fg.anchorMax = Vector2.one;
            fg.sizeDelta = Vector2.zero;
            _mapFog = fg.gameObject.AddComponent<global::CriticalMass.Runtime.View.Fog.FogMapLayer>();
            _mapFog.Bind(_proj);
            for (int i = 0; i < MaxDots; i++)
            {
                VectorShape d = Shape(_mapArea, "Dot " + i, ShapeKind.Circle, 0f, 0f, 7f, 7f, CMTokens.Azure.Color);
                d.OutlinePx = 1.5f;
                d.RimPx = 0f;
                d.gameObject.SetActive(false);
                _dots[i] = d;
            }
            // illustrated markers, each with its word (the v1 report's missing minimap labels), sized to leave the plazas visible
            _mapHill = Shape(_mapArea, "Map Hill", ShapeKind.Circle, 0f, 0f, 24f, 24f, Navy);
            Icon(_mapHill.transform, "Crown", UiIcons.MapCrown, 4f, 4f, 16f, 16f);
            CriticalMass.Runtime.Menu.MenuUi.Fit(Label(_mapHill.transform, "Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -23f, 24f, 70f, 14f, "HILL", 2f), 11);
            for (int t = 0; t < 2; t++)
            {
                HudIcon b = Icon(_mapArea, (TeamId)t + " Base", UiIcons.Banner[t], 0f, 0f, 24f, 24f);
                CriticalMass.Runtime.Menu.MenuUi.Fit(Label(b.Rt, "Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -23f, 23f, 70f, 14f, t == 0 ? "AZURE" : "CRIMSON", 2f), 11);
                _mapBases[t] = b;
            }
            _mapEgg = Icon(_mapArea, "Map Egg", UiIcons.MapEgg, 0f, 0f, 22f, 22f);
            CriticalMass.Runtime.Menu.MenuUi.Fit(Label(_mapEgg.Rt, "Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -24f, 20f, 70f, 14f, "KAIJU", 2f), 11);
            _mapYou = Rt(_mapArea, "Map You");
            Place(_mapYou, 0f, 0f, 18f, 18f);
            _youFacing = Rt(_mapYou, "Facing");
            Place(_youFacing, 0f, 0f, 18f, 18f);
            VectorShape arrow = Shape(_youFacing, "Arrow", ShapeKind.TriangleRight, 13f, 3f, 11f, 12f, CMTokens.ToolBrass.Color, 2f);
            arrow.OutlinePx = 1.5f;
            arrow.RimPx = 0f;
            Icon(_mapYou, "Marker", UiIcons.You, 0f, 0f, 18f, 18f);
            CriticalMass.Runtime.Menu.MenuUi.Fit(Label(_mapYou, "Label", CMTokens.Ui, UiScale.TypeMin, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 20f, -12f, 40f, 14f, "YOU", 2f), 11);
            _mapYou.gameObject.SetActive(false);
        }

        static Texture2D MapTexture(int tier) => MinimapGeometry.Texture(tier);

        readonly HudIcon[] _mapBases = new HudIcon[2];

        void PlaceOnMap(RectTransform rt, double simX, double simZ)
        {
            Vector2 p = _proj.ToPixel(simX, simZ);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(p.x, -p.y);
        }

        // ---------------------------------------------------------------- world layer

        const int Pointers = 2;
        RectTransform _world;
        VectorShape _hillMarker;
        Text _hillMarkerWord;
        readonly RectTransform[] _ptr = new RectTransform[Pointers];
        readonly RectTransform[] _ptrArrow = new RectTransform[Pointers];
        readonly Text[] _ptrDist = new Text[Pointers];

        void BuildWorldLayer()
        {
            _world = Rt(Root, "World Layer");
            _world.anchorMin = Vector2.zero;
            _world.anchorMax = Vector2.one;
            _world.sizeDelta = Vector2.zero;
            // Physics audit M5 / DG-110: the world-anchored marks move every fight frame; their own nested canvas re-batches
            // alone instead of the whole HUD (the static frame stays on the root canvas, the minimap on its own).
            WorldCanvas = NestedCanvas(_world);

            RectTransform marker = Rt(_world, "Hill Marker");
            Center(marker, 120f, 80f);
            _hillMarker = Shape(marker, "Medallion", ShapeKind.Circle, 38f, 0f, 44f, 44f, CMTokens.Stone.Color);
            Icon(_hillMarker.transform, "Crown", UiIcons.Crown, 4f, 4f, 36f, 36f);
            _hillMarkerWord = Label(marker, "Word", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, 0f, 46f, 120f, 22f, "NEUTRAL", 2.5f);

            // player-experience lane D: the overhead hearts became always-on health bars (Hud/Player/HealthBars.cs)
            for (int i = 0; i < Pointers; i++)
            {
                RectTransform p = Rt(_world, "Pointer " + i);
                Center(p, 60f, 70f);
                Shape(p, "Icon", ShapeKind.Circle, 10f, 0f, 40f, 40f, Navy).RimColor = i == 0 ? Cream : CMTokens.EggSeam.Color;
                if (i == 0) Icon(p.GetChild(0), "Crown", UiIcons.Crown, 4f, 4f, 32f, 32f);
                else Icon(p.GetChild(0), "Egg", UiIcons.MapEgg, 5f, 5f, 30f, 30f);
                RectTransform arrowPivot = Rt(p, "Arrow Pivot");
                Place(arrowPivot, 30f, 20f, 0f, 0f);
                VectorShape arrow = Shape(arrowPivot, "Arrow", ShapeKind.TriangleRight, 0f, 0f, 16f, 18f, Cream);
                arrow.RimPx = 0f;
                arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                arrow.rectTransform.anchoredPosition = new Vector2(30f, 0f);
                _ptrArrow[i] = arrowPivot;
                _ptrDist[i] = Label(p, "Distance", CMTokens.Mono, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, -10f, 42f, 80f, 26f, "0m", 2.5f);
                p.gameObject.SetActive(false);
                _ptr[i] = p;
            }
        }

        /// <summary>Player-experience lane D: the local player's combat HUD (Hud/Player/PlayerHud.cs).</summary>
        public CriticalMass.Runtime.Hud.Player.PlayerHud PlayerHud { get; private set; }

        /// <summary>The nested canvases of the M5 split (tests read them).</summary>
        public Canvas WorldCanvas { get; private set; }
        public Canvas MinimapCanvas { get; private set; }

        /// <summary>A nested canvas on rt: inherits the root's camera, sorting and scale; no raycaster (nothing to click).</summary>
        static Canvas NestedCanvas(RectTransform rt)
        {
            Canvas c = rt.gameObject.AddComponent<Canvas>();
            c.overrideSorting = false;
            c.pixelPerfect = false;
            return c;
        }

        /// <summary>Sets a group's alpha only when it changed by a visible step (pure helper; HUD dirty test).</summary>
        public static bool SetAlpha(CanvasGroup g, float a)
        {
            a = Mathf.Clamp01(a);
            if (Mathf.Abs(g.alpha - a) < 1f / 255f) return false;
            g.alpha = a;
            return true;
        }

        static void Center(RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
        }

        // ---------------------------------------------------------------- death

        RectTransform _death;
        Text _respawn;
        VectorShape _koRing;
        Vector3 _koWorld;
        bool _wasAlive = true;

        DeathGrade _grade;

        void BuildDeath()
        {
            _death = Rt(Root, "Death");
            _death.SetSiblingIndex(0);
            _death.anchorMin = Vector2.zero;
            _death.anchorMax = Vector2.one;
            _death.sizeDelta = Vector2.zero;
            ScreenVignette vignette = _death.gameObject.AddComponent<ScreenVignette>();
            vignette.raycastTarget = false;
            // v2: the world grade (70% brightness, 60% saturation) is a shader pass on URP's opaque texture under the HUD;
            // with it the vignette keeps only its edge band. Without the shader the v1 navy wash stays as the fallback.
            if (DeathGrade.GradeMaterial != null)
            {
                RectTransform g = Rt(Root, "Death Grade");
                g.SetSiblingIndex(0);
                g.anchorMin = Vector2.zero;
                g.anchorMax = Vector2.one;
                g.sizeDelta = Vector2.zero;
                _grade = g.gameObject.AddComponent<DeathGrade>();
                _grade.raycastTarget = false;
                vignette.Wash = 0f;
                g.gameObject.SetActive(false);
            }
            RectTransform mid = Rt(_death, "Centre");
            mid.anchorMin = mid.anchorMax = new Vector2(0.5f, 0.5f);
            mid.sizeDelta = new Vector2(400f, 160f);
            Label(mid, "Respawn Label", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.MiddleCenter, 0f, 0f, 400f, 34f, "RESPAWN IN", 3f);
            _respawn = CriticalMass.Runtime.Menu.MenuUi.Fit(Label(mid, "Respawn Seconds", CMTokens.Mono, UiScale.TypeHero, Cream, TextAnchor.MiddleCenter, 0f, 34f, 400f, 110f, "5", 3f), 100);
            _koRing = Shape(_world, "KO Ring", ShapeKind.Circle, 0f, 0f, 72f, 72f, new Color(0, 0, 0, 0));
            Center(_koRing.rectTransform, 72f, 72f);
            _koRing.OutlinePx = 4f;
            _koRing.RimPx = 3f;
            _death.gameObject.SetActive(false);
            _koRing.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- update

        void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Hud, cost); }

        void LateUpdateCost()
        {
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
            if (Driver == null || Driver.World == null || Root == null) return;
            // fix/hud-containment-5: one finish (HudStyle tokens) for every panel, pill and chip, built here or later
            if (Time.frameCount % 60 == 2) HudStyle.Sweep(transform);   // the HUD object: its canvas and the feed, toast and ping canvases under it
            AttachCamera();
            UpdateScale();
            Refresh(Driver.World, Time.unscaledDeltaTime);
        }

        /// <summary>Reads the World and applies it. Allocation-free after the first call (GameHudTests).</summary>
        public void Refresh(World w, float dt)
        {
            HudBinder.Read(w, HudBinder.LocalActor, State);
            PreviewOverride?.Invoke(State);
            if (SnapAnimations) dt = 10f;
            ApplyMatch(State);
            ApplyKaiju(State);
            ApplyPlayer(State, dt);
            ApplyMinimap(w, State);
            ApplyWorld(w, State, dt);
            ApplyDeath(w, State);
            if (State.Ended && !_countedMatch)
            {
                _countedMatch = true;
                PlayerPrefs.SetInt(MatchesPlayedKey, PlayerPrefs.GetInt(MatchesPlayedKey, 0) + 1);
                if (!CriticalMass.Runtime.Game.GamePaths.IsolatedRun) PlayerPrefs.Save();   // DG-53: once per match; never in a -dataDir run
            }
        }

        bool _countedMatch;

        static readonly string[] OnHillA = { "0 ON HILL", "1 ON HILL", "2 ON HILL", "3 ON HILL", "4 ON HILL", "5+ ON HILL" };

        /// <summary>b16: "n/cap ON HILL" words, built once per cap (no per-frame allocation).</summary>
        static string[] _capWords = new string[0];
        static int _capWordsFor = -1;

        public static string CapWord(int counted, int cap)
        {
            if (cap <= 0) return OnHillA[Math.Min(5, Math.Max(0, counted))];
            if (_capWordsFor != cap)
            {
                _capWords = new string[cap + 1];
                for (int i = 0; i <= cap; i++) _capWords[i] = "HILL " + i + "/" + cap;   // b16 finish: short enough for 18 px
                _capWordsFor = cap;
            }
            return _capWords[Math.Max(0, Math.Min(cap, counted))];
        }

        /// <summary>
        /// b16 finish: how many of the ten pips are lit. With a hill cap each pip is one counted hill point (a Warrior
        /// fills two), the same number the words show; without one (classic rules) each body fills two pips, up to the
        /// "5+ ON HILL" the words say. Pure (tests).
        /// </summary>
        public static int PipsLit(int bodies, int counted, int cap) =>
            cap > 0 ? Math.Max(0, Math.Min(Math.Min(cap, Pips), counted)) : Math.Max(0, Math.Min(5, bodies)) * 2;

        /// <summary>b16 finish: pips drawn (a cap under ten hides the rest; no cap shows all ten). Pure (tests).</summary>
        public static int PipsShown(int cap) => cap > 0 ? Math.Min(cap, Pips) : Pips;

        static void ApplyPips(VectorShape[] pips, int lit, int shown, bool full, Color team)
        {
            Color on = full ? CMTokens.ToolBrass.Color : team;   // the whole row turns brass at the cap: full, at a glance
            for (int i = 0; i < pips.Length; i++)
            {
                bool vis = i < shown;
                if (pips[i].gameObject.activeSelf != vis) pips[i].gameObject.SetActive(vis);
                pips[i].SetFill(i < lit ? on : Empty);
            }
        }

        void ApplyMatch(HudState s)
        {
            HudText.Set(_clock, NumberCache.Mmss(s.ClockRemaining));
            _clock.color = s.ClockRemaining <= 60 ? CMTokens.ToolBrass.Color : Cream;
            HudText.Set(_scoreA, NumberCache.Int(s.ScoreAzure));
            HudText.Set(_scoreC, NumberCache.Int(s.ScoreCrimson));
            if (s.HillCap > 0)
            {
                // b16 the hill count: "7/10 ON HILL" (weighted, a Warrior counts 2), brass at the cap so players learn it
                HudText.Set(_onHillA, CapWord(s.CountedAzure, s.HillCap));
                HudText.Set(_onHillC, CapWord(s.CountedCrimson, s.HillCap));
            }
            else
            {
                HudText.Set(_onHillA, OnHillA[Math.Min(5, s.OnHillAzure)]);
                HudText.Set(_onHillC, OnHillA[Math.Min(5, s.OnHillCrimson)]);
            }
            // b16 finish: cream words always (brass text on navy read ~9 px dark gold); the cap shows as a brass pip row
            _onHillA.color = Cream;
            _onHillC.color = Cream;
            int shown = PipsShown(s.HillCap);
            ApplyPips(_pipsA, PipsLit(s.OnHillAzure, s.CountedAzure, s.HillCap), shown, s.HillCap > 0 && s.CountedAzure >= s.HillCap, CMTokens.Azure.Color);
            ApplyPips(_pipsC, PipsLit(s.OnHillCrimson, s.CountedCrimson, s.HillCap), shown, s.HillCap > 0 && s.CountedCrimson >= s.HillCap, CMTokens.Crimson.Color);
            bool anyHeld = s.ScoreAzure + s.ScoreCrimson > 0;
            _hillBar.SetFill(anyHeld ? CMTokens.Azure.Color : CMTokens.Stone.Color);
            _hillBar.SetEmpty(anyHeld ? CMTokens.Crimson.Color : CMTokens.Stone.Color);
            _hillBar.Fill01 = s.HillShareAzure;
            _medallionHalf.gameObject.SetActive(s.Hill == HillState.Contested);
            _medallion.SetFill(HillColour(s.Hill));
            // b15 D0: the owner's own Kaiju stands on the hill, so the owner scores nothing; the word says so from each side's
            // point of view (words, not colour alone: brass ink is a second cue)
            // b15 view polish (item 8): that state is now the pill under the hill meter (ApplyKaijuPill); the word stays the hill's.
            HudText.Set(_hillWord, HillWord(s.Hill));
            _hillWord.color = Cream;
            ApplyKaijuPill(s);
        }

        static Color HillColour(HillState h) =>
            h == HillState.Azure ? CMTokens.Azure.Color : h == HillState.Crimson ? CMTokens.Crimson.Color :
            h == HillState.Contested ? CMTokens.Azure.Color : CMTokens.Stone.Color;

        static string HillWord(HillState h) =>
            h == HillState.Azure ? "AZURE HOLDS" : h == HillState.Crimson ? "CRIMSON HOLDS" : h == HillState.Contested ? "CONTESTED" : "NEUTRAL";

        static readonly Color[] RageColour = BuildRageColours();

        static Color[] BuildRageColours()
        {
            var a = new Color[RagePips];
            Color hot = CMTokens.ParseHex(CriticalMass.Runtime.View.Art.ArtMapping.GlowBerserk);
            for (int i = 0; i < RagePips; i++) a[i] = Color.Lerp(CMTokens.Meat.Color, hot, i / (float)(RagePips - 1));
            return a;
        }

        static readonly string[] HatchWords = BuildHatchWords();

        static string[] BuildHatchWords()
        {
            var a = new string[601];
            for (int i = 0; i < a.Length; i++) a[i] = "HATCH " + (i / 60).ToString("0") + ":" + (i % 60).ToString("00");
            return a;
        }

        void ApplyKaiju(HudState s)
        {
            // review fix (lane F): no Kaiju in this mode (Duel, Practice Range): no egg countdown, no meter, no panel
            RectTransform kc = Clusters[(int)HudCluster.Kaiju];
            if (kc != null && kc.gameObject.activeSelf == s.NoKaiju) kc.gameObject.SetActive(!s.NoKaiju);
            if (s.NoKaiju) return;
            bool egg = !s.Hatched || s.Kaiju == KaijuState.Egg;
            Color state = KaijuColour(s.Kaiju);
            _eggFrame.SetRim(egg ? CMTokens.EggSeam.Color : state);
            _kaijuHead.Set(KaijuIcon(s, egg), _tier);
            int lit = Mathf.CeilToInt(s.KaijuMeter01 * MeterSegments - 1e-4f);
            Color litColour = s.Kaiju == KaijuState.Berserk ? CMTokens.Corruption.Color : CMTokens.MeterOrange.Color;
            for (int i = 0; i < MeterSegments; i++) _segments[i].SetFill(i < lit ? litColour : Empty);
            HudText.Set(_kaijuNumber, NumberCache.Int(Mathf.RoundToInt(s.KaijuMeter01 * 100f)));
            int rage = Mathf.FloorToInt(s.Instability01 * RagePips + 1e-4f);
            // hud-art-1: the rage pips heat from meat orange toward the berserk magenta as the egg nears berserk
            for (int i = 0; i < RagePips; i++) _rage[i].SetFill(i < rage ? RageColour[i] : Empty);
            string word = egg ? HatchWords[Math.Min(600, (int)Math.Ceiling(s.UntilHatch - 1e-6))] :
                s.Kaiju == KaijuState.Azure ? "AZURE" : s.Kaiju == KaijuState.Crimson ? "CRIMSON" :
                s.Kaiju == KaijuState.Berserk ? "BERSERK" : "NEUTRAL";
            HudText.SetOneLine(_kaijuWord, word, KaijuWordMax, KaijuWordFloor);
            _kaijuWord.color = egg || s.Kaiju == KaijuState.Neutral ? Cream : state;
        }

        /// <summary>Egg before the hatch; the wild (neutral) Kaiju is the hatchling of bible p10; once a team turns it
        /// (or it goes berserk) the adult head in that state's colours (p10 azure / crimson / berserk adult).</summary>
        static string KaijuIcon(HudState s, bool egg) =>
            egg ? (s.UntilHatch <= CrackSeconds ? UiIcons.EggCracking : UiIcons.Egg) : s.Kaiju == KaijuState.Azure ? UiIcons.Adult[1] : s.Kaiju == KaijuState.Crimson ? UiIcons.Adult[2] :
            s.Kaiju == KaijuState.Berserk ? UiIcons.Adult[3] : UiIcons.Hatchling;

        static Color KaijuColour(KaijuState k) =>
            k == KaijuState.Azure ? CMTokens.Azure.Color : k == KaijuState.Crimson ? CMTokens.Crimson.Color :
            k == KaijuState.Berserk ? CMTokens.Corruption.Color : k == KaijuState.Egg ? CMTokens.EggSeam.Color : CMTokens.Stone.Color;

        void ApplyPlayer(HudState s, float dt)
        {
            if (!s.HasPlayer) return;
            Color tc = CMTokens.Team(s.Team);
            int cls = Mathf.Clamp((int)s.Cls, 0, ClassNames.Length - 1);
            int team = s.Team == TeamId.Crimson ? 1 : 0;
            int iconRow = IconRow(cls);
            _portrait.Set(UiIcons.Portrait[iconRow, team], _tier);
            _classBadge.Set(UiIcons.ClassBadge[iconRow, team], _tier);
            HudText.Set(_className, ClassNames[cls]);

            // health: shown value drains toward the target, the cream ghost holds 0.4 s then follows
            float target = s.Alive ? s.Hp01 : 0f;
            if (_hpShown < 0) _hpShown = _hpGhostShown = target;
            // player-experience lane D: the same chunk rule as the overhead bars (the fill drops at once, a white chunk holds
            // 0.25 s, then drains); below 30% the bar takes the low-health colour and pulse (Hud/Player/HurtEdge.cs)
            CriticalMass.Runtime.Hud.Player.HealthBars.Step(ref _hpShown, ref _hpGhostShown, ref _ghostHold, target, dt);
            _hpBar.SetFill(PlayerHud != null && PlayerHud.Hurt.Low ? PlayerHud.Hurt.LowBarColour : tc);
            _hpBar.Fill01 = _hpShown;
            _hpGhost.Fill01 = _hpGhostShown;
            HudText.Set(_hpNumber, NumberCache.Int(Mathf.CeilToInt(s.Alive ? s.Hp : 0f)));
            for (int i = 0; i < Hearts; i++)
            {
                float q = Mathf.Clamp01(target * Hearts - i);
                if (q < _heartLast[i] - 1e-3f) _heartPop[i] = 0.25f;
                _heartLast[i] = q;
                _hearts[i].Full.Set(UiIcons.Heart[team], _tier);
                _hearts[i].Full.Fill01 = q;
                _heartPop[i] = Mathf.Max(0, _heartPop[i] - dt);
                float k = 1f + 0.3f * (_heartPop[i] / 0.25f);
                _hearts[i].Rt.localScale = new Vector3(k, k, 1f);
            }

            // buttons: attack (LMB), class ability (RMB), carry (E)
            // b12 r3: a Demolitionist under the b12-jz rule: TNT (its icon, its own reload) when ready, else PRY BAR with the
            // TNT reload sweeping over the slot and its seconds, so the slot always says what the attack press does now.
            bool demoTnt = cls == (int)ClassId.Demolitions && s.DemoTntOnAttack;
            string attackName = AttackName(cls, s.HealerLantern, demoTnt && s.DemoTntReady);
            float slotCd = demoTnt && !s.DemoTntReady ? s.DemoTntCd01 : s.AttackCd01, slotSec = demoTnt && !s.DemoTntReady ? s.DemoTntSeconds : 0f;
            ApplyButton(_buttons[0], slotCd, slotSec, UiIcons.Attack[SlotIconRow(cls, demoTnt && s.DemoTntReady), team], attackName, dt);
            ApplyButton(_buttons[1], s.AbilityCd01, s.AbilityCdSeconds, UiIcons.Ability[iconRow, team], AbilityNames[cls], dt);
            ApplyDenied(_buttons[1], dt);
            AbilityButton carry = _buttons[2];
            carry.Icon.Set(s.Carry == CarryKind.Corruption ? UiIcons.Corruption : s.Carry == CarryKind.Biomass ? UiIcons.Biomass :
                UiIcons.CarryEmpty, _tier);
            HudText.Set(carry.Count, s.CarryAmount > 0 ? NumberCache.Int(s.CarryAmount) : "");
            ApplyNames(attackName, AbilityNames[cls], CarryWord(s.Carry));
        }

        /// <summary>feat/light-weapons-2: the attack slot's word. The Healer's reads LANTERN while her press would swing the
        /// lantern (nobody hurt in mend range) and MEND otherwise; every other class has one fixed word.</summary>
        public static string AttackName(int cls, bool healerLantern) => AttackName(cls, healerLantern, false);

        /// <summary>b12 r3: as above; demoTntReady = a Demolitionist whose attack press throws TNT right now (the b12-jz rule
        /// and TNT ready): her slot reads TNT. While it reloads (or without the rule) it reads PRY BAR.</summary>
        public static string AttackName(int cls, bool healerLantern, bool demoTntReady)
        {
            if (cls < 0 || cls >= AttackNames.Length) return "ATTACK";
            if (cls == (int)ClassId.Demolitions && demoTntReady) return DemolitionsTntWord;
            return cls == (int)ClassId.Healer && healerLantern ? CriticalMass.Sim.Core.LightWeapons.HealerName : AttackNames[cls];
        }

        /// <summary>The carry button's name for what the player holds.</summary>
        public static string CarryWord(CarryKind k) => k == CarryKind.Corruption ? "CORRUPTION" : k == CarryKind.Biomass ? "BIOMASS" : "CARRY";

        /// <summary>Every word the attack slot can show: the class words plus the Healer's LANTERN (feat/light-weapons-2).</summary>
        static string[] AttackSlotNames()
        {
            var a = new string[AttackNames.Length + 2];
            Array.Copy(AttackNames, a, AttackNames.Length);
            a[AttackNames.Length] = CriticalMass.Sim.Core.LightWeapons.HealerName;
            a[AttackNames.Length + 1] = DemolitionsTntWord;
            return a;
        }

        /// <summary>Every name the attack, ability and carry buttons can show, per slot (for the layout tests).</summary>
        public static string[][] AbilitySlotNames() => new[]
        {
            AttackSlotNames(), (string[])AbilityNames.Clone(), new[] { CarryWord(CarryKind.None), CarryWord(CarryKind.Corruption), CarryWord(CarryKind.Biomass) },
        };

        public const float AbilityNameY = -20f, AbilityNameH = 22f;
        readonly string[] _namesShown = new string[3];
        readonly Text[] _nameLabels = new Text[3];
        readonly float[] _nameW = new float[3], _nameX = new float[3], _nameBox = new float[3];

        void ApplyNames(string attack, string ability, string carry)
        {
            if (attack == _namesShown[0] && ability == _namesShown[1] && carry == _namesShown[2]) return;   // measure only on change
            _namesShown[0] = attack;
            _namesShown[1] = ability;
            _namesShown[2] = carry;
            for (int i = 0; i < 3; i++) _nameLabels[i] = _buttons[i].Name;
            LayoutAbilityNameTexts(_nameLabels, _namesShown, _nameW, _nameX, _nameBox);
        }

        /// <summary>
        /// hud-overlaps-1: measures the three names at full size, lets HudLayout pick one shared size and a box per name
        /// that keeps a gap to its neighbours (centred on its button where it can), places the boxes and cuts a name that still does not
        /// fit with an ellipsis. Public so the EditMode layout test runs the same code on real labels.
        /// </summary>
        public static int LayoutAbilityNameTexts(Text[] labels, string[] names, float[] widths, float[] lefts, float[] boxes)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                string n = names[i] ?? string.Empty;
                labels[i].resizeTextForBestFit = false;
                labels[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                labels[i].fontSize = HudLayout.AbilityNameMax;
                labels[i].text = n;
                // fix/hud-containment-3: the full name at the full size, measured by the rule's own measure (not the label's
                // current state, which the rule may have shrunk and cut for its previous box)
                widths[i] = n.Length == 0 ? 0f : TextFit.WidthOf(labels[i], n, HudLayout.AbilityNameMax);
            }
            int size = HudLayout.LayoutAbilityNames(widths, lefts, boxes);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].fontSize = size;
                ContainedText.Of(labels[i])?.Configure(size, Mathf.Min(size, HudLayout.AbilityNameFloor), 1);   // the shared size is the rule's top
                Place(labels[i].rectTransform, lefts[i], AbilityNameY, boxes[i], AbilityNameH);
                HudText.FitWithEllipsis(labels[i], names[i], boxes[i]);
                ContainedText.Of(labels[i])?.SetSource(names[i] ?? string.Empty);   // the one rule, at the shared size, in the final box
            }
            return size;
        }

        void ApplyButton(AbilityButton b, float cd01, float seconds, string icon, string name, float dt)
        {
            bool cooling = cd01 > 0.0005f;
            b.Sweep.Fill01 = cd01;
            b.Icon.Set(icon, _tier);
            HudText.Set(b.Seconds, cooling && seconds >= 1f ? NumberCache.Int(Mathf.CeilToInt(seconds)) : "");
            if (b.WasCooling && !cooling) b.Flash = 0.3f;
            b.WasCooling = cooling;
            b.Flash = Mathf.Max(0, b.Flash - dt);
            Color rim = cooling ? Cream : CMTokens.ToolBrass.Color;   // p05: Tool Brass = ready ability
            b.Body.SetRim(rim);
            float k = 1f + 0.1f * (b.Flash / 0.3f);
            b.Rt.localScale = new Vector3(k, k, 1f);
        }

        // ---- "not ready" cue (physics audit M7 view half, DG-101): an ability press the sim refuses shakes the icon 4 px
        // and plays a soft click, once per press (CriticalMass.Runtime.Input.AbilityDenial counts refused presses).
        public const float DeniedShakePx = 4f, DeniedSeconds = 0.24f;
        int _deniedSeen = -1;
        float _denied = DeniedSeconds;
        Vector2 _deniedBase;
        bool _deniedBaseSet;

        /// <summary>The ability icon's horizontal shake for the time since a refused press (pure; tests): 0 at the end.</summary>
        public static float DeniedOffset(float sinceSeconds)
        {
            if (sinceSeconds < 0f || sinceSeconds >= DeniedSeconds) return 0f;
            float k = 1f - sinceSeconds / DeniedSeconds;
            return DeniedShakePx * k * Mathf.Sin(sinceSeconds / DeniedSeconds * Mathf.PI * 6f);
        }

        /// <summary>Times the denied cue fired (tests).</summary>
        public int DeniedCues { get; private set; }

        void ApplyDenied(AbilityButton b, float dt)
        {
            int count = CriticalMass.Runtime.Input.AbilityDenial.Count;
            if (_deniedSeen < 0) _deniedSeen = count;
            if (count != _deniedSeen)
            {
                _deniedSeen = count;
                _denied = 0f;
                DeniedCues++;
                CriticalMass.Runtime.View.Effects.CombatAudio.PlayUi("ability_denied");
            }
            else if (_denied < DeniedSeconds) _denied += dt;
            if (b.Icon == null) return;
            RectTransform rt = b.Icon.Rt;
            if (!_deniedBaseSet)
            {
                _deniedBase = rt.anchoredPosition;
                _deniedBaseSet = true;
            }
            float x = SnapAnimations ? 0f : DeniedOffset(_denied >= DeniedSeconds ? DeniedSeconds : _denied);
            Vector2 want = _deniedBase + new Vector2(x, 0f);
            if (rt.anchoredPosition != want) rt.anchoredPosition = want;
        }

        static float Approach(float v, float t, float step) => v < t ? Mathf.Min(t, v + step) : Mathf.Max(t, v - step);

        void ApplyMinimap(World w, HudState s)
        {
            if (_mapFor != w.Map)
            {
                _mapFor = w.Map;
                PlaceOnMap(_mapHill.rectTransform, w.Map.Hill.X, w.Map.Hill.Z);
                for (int t = 0; t < 2 && t < w.Map.Pads.Length; t++) PlaceOnMap(_mapBases[t].Rt, w.Map.Pads[t].X, w.Map.Pads[t].Z);
            }
            _ground.Watch(w.Map, w.GridVersion);
            if (_mapFog != null) _mapFog.Sync();
            _mapHill.SetFill(s.Hill == HillState.Neutral ? Navy : HillColour(s.Hill));
            bool eggOnMap = !s.Hatched || s.Kaiju == KaijuState.Egg;
            _mapEgg.Set(eggOnMap ? UiIcons.MapEgg : KaijuIcon(s, false), _tier);
            if (s.Hatched) PlaceOnMap(_mapEgg.Rt, w.Kaiju.X, w.Kaiju.Z);
            else PlaceOnMap(_mapEgg.Rt, w.Map.Shrine.X, w.Map.Shrine.Z);
            int n = Math.Min(w.ActorCount, MaxDots);
            int me = HudBinder.LocalActor;
            TeamId mine = me < w.ActorCount ? w.Actors[me].Team : TeamId.Azure;
            for (int i = 0; i < MaxDots; i++)
            {
                bool on = i < n && w.Actors[i].Alive;
                if (i == me)
                {
                    // own player: the brass arrow marker with its word, turned to the facing, above every dot
                    if (_mapYou.gameObject.activeSelf != on) _mapYou.gameObject.SetActive(on);
                    if (on)
                    {
                        PlaceOnMap(_mapYou, w.X[i], w.Z[i]);
                        _youFacing.localEulerAngles = new Vector3(0f, 0f, _proj.FacingToUiDeg(w.Actors[i].Facing));
                        _mapYou.SetAsLastSibling();
                    }
                    on = false;
                }
                // Batch 12 item P: with the fog drawn an enemy dot follows the fog (12 u of a teammate, 10 u of a held hill,
                // the base, faded); without it, the older SightRadius rule
                if (on && w.Actors[i].Team != mine)
                    on = global::CriticalMass.Runtime.View.Fog.FogOfWar.Active ? !global::CriticalMass.Runtime.View.Fog.FogOfWar.Hides(i) : Seen(w, i, mine, n);
                VectorShape d = _dots[i];
                if (d.gameObject.activeSelf != on) d.gameObject.SetActive(on);
                if (!on) continue;
                PlaceOnMap(d.rectTransform, w.X[i], w.Z[i]);
                d.SetFill(CMTokens.Team(w.Actors[i].Team));
                // Polish pass 1: colour is never the only team signal: Azure dots are circles, Crimson dots diamonds
                // (the crests' shapes on the scoreboard and kill feed carry the same split).
                ShapeKind k = TeamDot(w.Actors[i].Team);
                if (d.Kind != k)
                {
                    d.Kind = k;
                    d.SetVerticesDirty();
                }
            }
        }

        /// <summary>The minimap dot shape of a team (polish pass 1: shape as well as colour).</summary>
        public static ShapeKind TeamDot(TeamId t) => t == TeamId.Azure ? ShapeKind.Circle : ShapeKind.Diamond;

        /// <summary>True when a living actor of team <paramref name="mine"/> is within SightRadius of actor i.</summary>
        static bool Seen(World w, int i, TeamId mine, int n)
        {
            double r2 = SightRadius * SightRadius;
            for (int j = 0; j < n; j++)
            {
                if (!w.Actors[j].Alive || w.Actors[j].Team != mine) continue;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i];
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        void ApplyWorld(World w, HudState s, float dt)
        {
            UnityEngine.Camera cam = Canvas.worldCamera;
            if (cam == null) return;
            float scale = Canvas.scaleFactor;
            float sw = cam.pixelWidth, sh = cam.pixelHeight;

            // hill marker or its edge pointer
            double hx = w.Map.Hill.X, hz = w.Map.Hill.Z;
            Vector3 hillWorld = SimSpace.ToWorld(hx, GroundHeight.At(w.Map, hx, hz) + 3.2, hz);
            bool hillOn = Project(cam, hillWorld, out Vector2 hp);
            _hillMarker.transform.parent.gameObject.SetActive(hillOn);
            if (hillOn)
            {
                var hm = (RectTransform)_hillMarker.transform.parent;
                hm.anchoredPosition = hp / scale;
                // fix/hud-containment-5 (owner: "CRIMSON" over the hill bar and CRIMSON HOLDS): off the top band and its stack
                HudZones.SlideOffPanels(hm, Canvas);
                _hillMarker.SetFill(HillColour(s.Hill));
                HudText.Set(_hillMarkerWord, s.Hill == HillState.Contested ? "CONTESTED" : s.Hill == HillState.Neutral ? "NEUTRAL" :
                    s.Hill == HillState.Azure ? "AZURE" : "CRIMSON");
            }
            Pointer(0, cam, hillWorld, hillOn, s, hx, hz, scale, sw, sh);
            Vector3 kWorld = SimSpace.ToWorld(w.Kaiju.X, GroundHeight.At(w.Map, w.Kaiju.X, w.Kaiju.Z) + 2.0, w.Kaiju.Z);
            bool kOn = Project(cam, kWorld, out _);
            Pointer(1, cam, kWorld, kOn, s, w.Kaiju.X, w.Kaiju.Z, scale, sw, sh);

            // player-experience lane D: damage numbers moved to Hud/Player/DamageNumbers.cs (setting honoured, stacked,
            // sized per camera, heal / Kaiju / wall numbers)
            // player-experience lane D: health bars over every unit in view, hit marker, damage direction, kill confirm,
            // tags and pops (Hud/Player); replaces the 3-second overhead hearts
            PlayerFeedback.CurrentTick = w.Tick;   // review fix round 2 (gate tier F): ledger entries carry the sim tick
            PlayerHud?.Tick(w, s, Driver != null ? Driver.Alpha : 1.0, cam, scale, dt, w.EventHead);   // GameHud is the allowed event reader
        }

        static readonly string[] Metres = BuildMetres();

        static string[] BuildMetres()
        {
            var a = new string[1000];
            for (int i = 0; i < a.Length; i++) a[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
            return a;
        }

        void Pointer(int i, UnityEngine.Camera cam, Vector3 world, bool onScreen, HudState s, double tx, double tz, float scale,
            float sw, float sh)
        {
            RectTransform p = _ptr[i];
            if (onScreen || !s.HasPlayer) { if (p.gameObject.activeSelf) p.gameObject.SetActive(false); return; }
            if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
            Vector3 sp = cam.WorldToScreenPoint(world);
            Vector2 c = new Vector2(sw * 0.5f, sh * 0.5f);
            Vector2 d = new Vector2(sp.x, sp.y) - c;
            if (sp.z < 0) d = -d;
            if (d.sqrMagnitude < 1e-4f) d = Vector2.up;
            float pad = 70f * scale;
            float kx = Mathf.Abs(d.x) > 1e-4f ? (c.x - pad) / Mathf.Abs(d.x) : float.MaxValue;
            float ky = Mathf.Abs(d.y) > 1e-4f ? (c.y - pad) / Mathf.Abs(d.y) : float.MaxValue;
            Vector2 pos = c + d * Mathf.Min(kx, ky);
            // fix/hud-edge-markers-1: the whole marker (badge and distance label) slides along the free edge clear of every
            // HUD panel (was: only its centre point pushed up or down, so the label could still sit on a panel).
            pos = HudEdgeMarkers.PlaceLive(pos, HudEdgeMarkers.MarkerHalf, sw, sh, scale, 1f, pad);
            p.anchoredPosition = pos / scale;
            _ptrArrow[i].localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            double dx = tx - s.PlayerX, dz = tz - s.PlayerZ;
            HudText.Set(_ptrDist[i], Metres[Math.Min(999, (int)Math.Round(Math.Sqrt(dx * dx + dz * dz)))]);
        }

        static bool Project(UnityEngine.Camera cam, Vector3 world, out Vector2 px)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            px = new Vector2(sp.x, sp.y);
            return sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= cam.pixelWidth && sp.y <= cam.pixelHeight;
        }

        void ApplyDeath(World w, HudState s)
        {
            bool dead = s.HasPlayer && !s.Alive && !s.Ended && !(CriticalMass.Runtime.Flow.GameFlow.State == CriticalMass.Runtime.Flow.FlowState.Results);   // batch 5: the results screen hides the death layer
            if (dead && _wasAlive && s.HasPlayer)
                _koWorld = SimSpace.ToWorld(s.PlayerX, GroundHeight.At(w.Map, s.PlayerX, s.PlayerZ) + 0.05, s.PlayerZ);
            _wasAlive = !dead;
            if (_death.gameObject.activeSelf != dead) _death.gameObject.SetActive(dead);
            if (_grade != null && _grade.gameObject.activeSelf != dead)
            {
                _grade.gameObject.SetActive(dead);
                UnityEngine.Camera gc = Canvas.worldCamera;
                // the opaque copy only while dead (GetUniversalAdditionalCameraData adds the component if it is missing)
                if (gc != null) UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(gc).requiresColorTexture = dead;
            }
            if (_koRing.gameObject.activeSelf != dead) _koRing.gameObject.SetActive(dead);
            if (!dead) return;
            HudText.Set(_respawn, NumberCache.Int(Mathf.CeilToInt((float)s.RespawnIn)));
            _koRing.SetRim(CMTokens.Team(s.Team));
            UnityEngine.Camera cam = Canvas.worldCamera;
            if (cam != null && Project(cam, _koWorld, out Vector2 p)) _koRing.rectTransform.anchoredPosition = p / Canvas.scaleFactor;
        }
    }
}
