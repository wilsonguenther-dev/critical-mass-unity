using System;
using System.Globalization;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>What the pointer (or the controller reticle) is over on the full map.</summary>
    public enum MapHoverKind { None, Actor, Hill, Shrine, Kaiju, Base, Node, Ping }

    /// <summary>
    /// The full map: a large contained panel over a dimmed, still-running match. The same baked image as the minimap
    /// (minimap@8x when the baker has written it, else @4x: 1:1 at 1080p at fit zoom, mipmapped when zoomed out), live
    /// ground changes over it (MinimapGround: dug, built, broken), and markers from sim state through the one projection
    /// the bake used (MinimapGeometry.Projection): both bases with their respawning allies and timers, the hill with its
    /// owner and capture share, the shrine, the egg or the Kaiju in its state with health, biomass and corruption sources,
    /// teammates as class badges with names and health pips, enemies only under the minimap's visibility rule
    /// (MapRules.Seen), pings, and the player's brass arrow. Zoom 1x..4x (wheel around the cursor, or triggers), pan (drag
    /// or right stick), hover or reticle tooltips (who, class, health, distance), an objective summary and a legend.
    ///
    /// Performance: markers are re-read from the sim at most every RefreshInterval (10 per second); between reads only a
    /// view change (zoom, pan) moves them. Fixed pools, cached strings, no allocation per frame. The texture is loaded on
    /// Show and released on Hide.
    /// </summary>
    public sealed class FullMapView
    {
        // layout in the 1920x1080 reference space (top-left origin, y down)
        public const float PanelX = 150f, PanelY = 54f, PanelW = 1620f, PanelH = 972f;
        public const float ViewX = 186f, ViewY = 132f, ViewW = 1000f, ViewH = 860f;
        public const float SideX = 1222f, SideW = 512f;
        /// <summary>Top of the one-line key hints: under the map frame (ViewY + ViewH + 6), above the panel's bottom edge.</summary>
        public const float HintsY = ViewY + ViewH + 8f;
        public const float MinZoom = 1f, MaxZoom = 4f;
        public const float RefreshInterval = 0.1f;
        public const int MaxActors = 64, MaxNodes = 48, MaxPings = PingBook.Capacity;
        /// <summary>Hover pick radius around a marker, in reference units.</summary>
        public const float HoverRadius = 22f;
        public const float PadPanSpeed = 900f, PadZoomRate = 1.8f;
        const int Pips = 4;

        readonly MinimapProjection _proj = MinimapGeometry.Projection;
        RectTransform _root, _space, _panel, _viewport, _mapLayer, _markers;
        RawImage _image;
        MinimapGround _ground;
        Texture2D _tex;
        int _tier = 2;

        float _zoom = 1f;
        Vector2 _centre;          // map pixel (1x) at the viewport centre
        float _fit;
        bool _viewDirty = true;
        float _nextRefresh;
        public int Refreshes { get; private set; }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool Built => _root != null;
        public float Zoom => _zoom;
        public float Scale => _fit * _zoom;
        public Vector2 Centre => _centre;
        public RectTransform Viewport => _viewport;
        public RectTransform Panel => _panel;
        public Texture Texture => _image != null ? _image.texture : null;

        // ---------------------------------------------------------------- markers

        sealed class ActorMarker
        {
            public RectTransform Rt;
            public VectorShape Disc;
            public HudIcon Badge;
            public Text Name, Timer;
            public readonly VectorShape[] Pips = new VectorShape[FullMapView.Pips];
            public bool Shown, Respawning, Enemy;
            public int RespawnSlot;
            public double X, Z;
            public int Cls = -1, Team = -1;
        }

        readonly ActorMarker[] _actors = new ActorMarker[MaxActors];
        readonly RectTransform[] _nodes = new RectTransform[MaxNodes];
        readonly VectorShape[] _nodeDots = new VectorShape[MaxNodes];
        readonly bool[] _nodeShown = new bool[MaxNodes];
        readonly RectTransform[] _pings = new RectTransform[MaxPings];
        readonly CanvasGroup[] _pingGroups = new CanvasGroup[MaxPings];
        readonly VectorShape[] _pingShapes = new VectorShape[MaxPings];
        readonly Text[] _pingWords = new Text[MaxPings];
        readonly int[] _pingSerial = new int[MaxPings];
        readonly RectTransform[] _bases = new RectTransform[2];
        RectTransform _hill, _shrine, _kaiju, _you, _youFacing, _reticle;
        VectorShape _hillDisc, _hillShare, _kaijuHp;
        HudIcon _kaijuIcon;
        Text _hillA, _hillC, _kaijuWord;
        double _youX, _youZ, _kaijuX, _kaijuZ;
        bool _youShown;
        World _world;
        int _me;
        readonly HudState _state = new HudState();

        // side column
        Text _scoreA, _scoreC, _clock, _kaijuState, _allyA, _allyC, _closeKey, _hints;
        VectorShape _hillBar, _allyBar;
        readonly VectorShape[] _rage = new VectorShape[5];

        // map tooltip (fixed fields, value strings from caches)
        RectTransform _tip;
        VectorShape _tipPanel;
        Text _tipTitle, _tipSub, _tipL1, _tipA1, _tipSep1, _tipB1, _tipL2, _tipA2;
        public MapHoverKind HoverKind { get; private set; }
        public int HoverIndex { get; private set; } = -1;

        static readonly string[] ClassUpper = { "WARRIOR", "FARMER", "MINER", "DEMOLITIONS", "BUILDER" };
        static readonly string[,] ClassTeam = BuildClassTeam();
        static readonly string[] Metres = BuildTable(1000, "m");
        static readonly string[] Seconds = BuildTable(100, "s");
        static readonly string[] PlaceUpper = BuildPlaceUpper();
        static readonly string[] RespawnClass = { "RESPAWNING · WARRIOR", "RESPAWNING · FARMER", "RESPAWNING · MINER", "RESPAWNING · DEMOLITIONS", "RESPAWNING · BUILDER" };

        static string[,] BuildClassTeam()
        {
            var a = new string[ClassUpper.Length, 2];
            for (int c = 0; c < ClassUpper.Length; c++)
            {
                a[c, 0] = ClassUpper[c] + " · AZURE";
                a[c, 1] = ClassUpper[c] + " · CRIMSON";
            }
            return a;
        }

        static string[] BuildTable(int n, string suffix)
        {
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = i.ToString(CultureInfo.InvariantCulture) + suffix;
            return a;
        }

        static string[] BuildPlaceUpper()
        {
            var a = new string[PingWords.Places.Length];
            for (int i = 0; i < a.Length; i++) a[i] = "YOU · " + PingWords.Places[i].ToUpperInvariant();
            return a;
        }

        static Color Navy => CMTokens.VoidNavy.Color;
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;
        static Color Sky => CMTokens.ParseHex("#1E2D4A");

        // ---------------------------------------------------------------- build

        /// <summary>Builds the whole panel once under the canvas root (hidden). The texture is not loaded until Show.</summary>
        public void Build(RectTransform canvasRoot, int iconTier)
        {
            if (_root != null) return;
            _tier = iconTier;
            _root = MenuUi.Rt(canvasRoot, "Full Map");
            MenuUi.Stretch(_root);
            InteractUi.Wash(_root, "Dim", 0.6f);
            _space = MenuUi.Rt(_root, "Space");
            _space.anchorMin = _space.anchorMax = _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            VectorShape panel = MenuUi.Panel(_space, "Panel", PanelX, PanelY, PanelW, PanelH);
            _panel = panel.rectTransform;
            MenuUi.Label(_space, "Title", CMTokens.Display, UiScale.TypeLead, Cream, TextAnchor.MiddleLeft, ViewX, PanelY + 18f, 700f, 56f, "BASIN MAP", 3f);
            VectorShape chip = MenuUi.Shape(_space, "Close Chip", ShapeKind.RoundRect, PanelX + PanelW - 36f - 220f, PanelY + 24f, 220f, 46f, Navy, -1f);
            chip.RimColor = Cream;
            _closeChip = chip.rectTransform;
            _closeKey = MenuUi.Label(chip.transform, "Label", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, 0f, 0f, 220f, 46f, "M  CLOSE", 2f);

            // viewport: sky behind, the masked map layer, then the marker layer (unscaled, centre origin)
            VectorShape frame = MenuUi.Shape(_space, "Map Frame", ShapeKind.RoundRect, ViewX - 6f, ViewY - 6f, ViewW + 12f, ViewH + 12f, Sky, 14f);
            frame.OutlinePx = 4f;
            frame.RimPx = 2f;
            _viewport = MenuUi.Rt(_space, "Viewport");
            MenuUi.Place(_viewport, ViewX, ViewY, ViewW, ViewH);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _mapLayer = MenuUi.Rt(_viewport, "Map Layer");
            _mapLayer.anchorMin = _mapLayer.anchorMax = new Vector2(0.5f, 0.5f);
            _mapLayer.pivot = new Vector2(0f, 1f);
            _mapLayer.sizeDelta = new Vector2(_proj.Width, _proj.Height);
            _image = _mapLayer.gameObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            RectTransform g = MenuUi.Rt(_mapLayer, "Ground Changes");
            MenuUi.Stretch(g);
            _ground = g.gameObject.AddComponent<MinimapGround>();
            _ground.raycastTarget = false;
            _ground.Bind(_proj, MinimapGeometry.BakedMap);
            _markers = MenuUi.Rt(_viewport, "Markers");
            MenuUi.Stretch(_markers);
            _fit = Mathf.Min(ViewW / _proj.Width, ViewH / _proj.Height);
            _centre = new Vector2(_proj.Width * 0.5f, _proj.Height * 0.5f);

            BuildMarkers();
            BuildSide();
            BuildTip();
            ApplyView();
            _root.gameObject.SetActive(false);
        }

        RectTransform _closeChip;

        void BuildMarkers()
        {
            for (int i = 0; i < MaxNodes; i++)
            {
                RectTransform rt = InteractUi.Centred(_markers, "Node " + i, 14f, 14f);
                VectorShape d = MenuUi.Shape(rt, "Dot", ShapeKind.Circle, 0f, 0f, 14f, 14f, CMTokens.Biomass.Color, 8f);
                d.OutlinePx = 2f;
                d.RimPx = 0f;
                _nodes[i] = rt;
                _nodeDots[i] = d;
                rt.gameObject.SetActive(false);
            }
            for (int t = 0; t < 2; t++)
            {
                RectTransform b = InteractUi.Centred(_markers, (TeamId)t + " Base", 36f, 36f);
                HudIcon icon = HudIcon.Make(b, "Banner", 0f, 0f, 36f, 36f);
                icon.Set(UiIcons.Banner[t], _tier);
                MenuUi.Fit(MenuUi.Label(b, "Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -42f, 36f, 120f, 18f, t == 0 ? "AZURE BASE" : "CRIMSON BASE", 2f), 14);
                _bases[t] = b;
            }
            _hill = InteractUi.Centred(_markers, "Hill", 48f, 48f);
            _hillShare = MenuUi.Shape(_hill, "Capture Share", ShapeKind.Pie, -6f, -6f, 60f, 60f, CMTokens.Azure.Color, 8f);
            _hillShare.EmptyColor = CMTokens.Crimson.Color;
            _hillShare.Fill01 = 0.5f;
            _hillDisc = MenuUi.Shape(_hill, "Owner", ShapeKind.Circle, 4f, 4f, 40f, 40f, Navy, 8f);
            HudIcon crown = HudIcon.Make(_hill, "Crown", 10f, 10f, 28f, 28f);
            crown.Set(UiIcons.MapCrown, _tier);
            MenuUi.Fit(MenuUi.Label(_hill, "Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -36f, 56f, 120f, 18f, "HILL", 2f), 14);
            _hillA = MenuUi.Label(_hill, "On Hill Azure", CMTokens.Mono, UiScale.TypeSmall, CMTokens.Azure.Color, TextAnchor.MiddleRight, -46f, 12f, 40f, 24f, "0", 2.5f);
            _hillC = MenuUi.Label(_hill, "On Hill Crimson", CMTokens.Mono, UiScale.TypeSmall, CMTokens.CrimsonInk.Color, TextAnchor.MiddleLeft, 54f, 12f, 40f, 24f, "0", 2.5f);

            _shrine = InteractUi.Centred(_markers, "Shrine", 30f, 30f);
            VectorShape sh = MenuUi.Shape(_shrine, "Shrine", ShapeKind.Hexagon, 0f, 0f, 30f, 30f, Navy, 8f);
            sh.RimColor = CMTokens.EggSeam.Color;
            MenuUi.Fit(MenuUi.Label(_shrine, "Label", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -45f, -20f, 120f, 18f, "SHRINE", 2f), 13);

            _kaiju = InteractUi.Centred(_markers, "Kaiju", 40f, 40f);
            _kaijuIcon = HudIcon.Make(_kaiju, "Icon", 0f, 0f, 40f, 40f);
            _kaijuIcon.Set(UiIcons.MapEgg, _tier);
            _kaijuHp = MenuUi.Shape(_kaiju, "Health", ShapeKind.RoundRect, -4f, 42f, 48f, 7f, CMTokens.MeterOrange.Color, -1f);
            _kaijuHp.EmptyColor = CMTokens.MeterEmpty.Color;
            _kaijuHp.OutlinePx = 1.5f;
            _kaijuHp.RimPx = 0f;
            _kaijuWord = MenuUi.Fit(MenuUi.Label(_kaiju, "Word", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -40f, 50f, 120f, 18f, "EGG", 2f), 14);

            for (int i = 0; i < MaxActors; i++)
            {
                var m = new ActorMarker();
                m.Rt = InteractUi.Centred(_markers, "Unit " + i, 26f, 26f);
                m.Disc = MenuUi.Shape(m.Rt, "Disc", ShapeKind.Circle, 0f, 0f, 26f, 26f, CMTokens.Azure.Color, 8f);
                m.Disc.OutlinePx = 2f;
                m.Disc.RimPx = 0f;
                m.Badge = HudIcon.Make(m.Rt, "Badge", 2f, 2f, 22f, 22f);
                for (int p = 0; p < Pips; p++)
                {
                    VectorShape pip = MenuUi.Shape(m.Rt, "Pip " + p, ShapeKind.RoundRect, -1f + p * 7.5f, 28f, 6f, 5f, Cream, 1f);
                    pip.OutlinePx = 1f;
                    pip.RimPx = 0f;
                    m.Pips[p] = pip;
                }
                m.Name = MenuUi.Fit(MenuUi.Label(m.Rt, "Name", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -37f, 33f, 100f, 16f, "", 2f), 12);
                m.Timer = MenuUi.Fit(MenuUi.Label(m.Rt, "Timer", CMTokens.Mono, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -12f, -18f, 50f, 18f, "", 2f), 14);
                m.Rt.gameObject.SetActive(false);
                _actors[i] = m;
            }

            for (int i = 0; i < MaxPings; i++)
            {
                RectTransform rt = InteractUi.Centred(_markers, "Ping " + i, 34f, 34f);
                _pingGroups[i] = rt.gameObject.AddComponent<CanvasGroup>();
                _pingGroups[i].blocksRaycasts = false;
                _pingShapes[i] = MenuUi.Shape(rt, "Shape", ShapeKind.TriangleDown, 0f, 0f, 34f, 34f, CMTokens.MeterOrange.Color, 6f);
                _pingWords[i] = MenuUi.Fit(MenuUi.Label(rt, "Word", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleCenter, -53f, 36f, 140f, 18f, "", 2f), 14);
                _pings[i] = rt;
                rt.gameObject.SetActive(false);
            }

            _you = InteractUi.Centred(_markers, "You", 30f, 30f);
            _youFacing = InteractUi.Centred(_you, "Facing", 30f, 30f);
            VectorShape arrow = MenuUi.Shape(_youFacing, "Arrow", ShapeKind.TriangleRight, 24f, 7f, 16f, 16f, Brass, 2f);
            arrow.OutlinePx = 2f;
            arrow.RimPx = 0f;
            HudIcon you = HudIcon.Make(_you, "Marker", 0f, 0f, 30f, 30f);
            you.Set(UiIcons.You, _tier);
            MenuUi.Fit(MenuUi.Label(_you, "Label", CMTokens.Ui, UiScale.TypeMin, Brass, TextAnchor.MiddleLeft, 34f, -10f, 60f, 18f, "YOU", 2f), 14);
            _you.gameObject.SetActive(false);

            _reticle = InteractUi.Centred(_markers, "Reticle", 40f, 40f);
            VectorShape ring = MenuUi.Shape(_reticle, "Ring", ShapeKind.Circle, 0f, 0f, 40f, 40f, new Color(0, 0, 0, 0), 8f);
            ring.OutlinePx = 3f;
            ring.RimPx = 2f;
            ring.RimColor = Brass;
            _reticle.gameObject.SetActive(false);
        }

        void BuildSide()
        {
            float x = SideX, w = SideW;
            MenuUi.Label(_space, "Objectives", CMTokens.Display, UiScale.TypeBody, Brass, TextAnchor.MiddleLeft, x, ViewY - 4f, w, 34f, "OBJECTIVES", 2f);
            MenuUi.Label(_space, "Hill Head", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleLeft, x, ViewY + 38f, w, 24f, "HILL SECONDS", 2f);
            _scoreA = MenuUi.Label(_space, "Score Azure", CMTokens.Mono, UiScale.TypeLead, CMTokens.Azure.Color, TextAnchor.MiddleLeft, x, ViewY + 62f, 200f, 50f, "0", 3f);
            _scoreC = MenuUi.Label(_space, "Score Crimson", CMTokens.Mono, UiScale.TypeLead, CMTokens.CrimsonInk.Color, TextAnchor.MiddleRight, x + w - 200f, ViewY + 62f, 200f, 50f, "0", 3f);
            MenuUi.Fit(MenuUi.Label(_space, "Azure Word", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, x + 110f, ViewY + 72f, 120f, 30f, "AZURE", 2f), 15);
            MenuUi.Fit(MenuUi.Label(_space, "Crimson Word", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleRight, x + w - 230f, ViewY + 72f, 120f, 30f, "CRIMSON", 2f), 15);
            _hillBar = MenuUi.Shape(_space, "Hill Share", ShapeKind.RoundRect, x, ViewY + 118f, w, 20f, CMTokens.Azure.Color, -1f);
            _hillBar.EmptyColor = CMTokens.Crimson.Color;
            _hillBar.Fill01 = 0.5f;

            MenuUi.Label(_space, "Kaiju Head", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleLeft, x, ViewY + 156f, 200f, 24f, "KAIJU", 2f);
            _kaijuState = MenuUi.Label(_space, "Kaiju State", CMTokens.Display, UiScale.TypeSmall, Cream, TextAnchor.MiddleRight, x + 160f, ViewY + 154f, w - 160f, 28f, "EGG", 2f);
            MenuUi.Fit(MenuUi.Label(_space, "Allegiance Head", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, x, ViewY + 186f, 200f, 22f, "ALLEGIANCE", 2f), 15);
            _allyA = MenuUi.Label(_space, "Allegiance Azure", CMTokens.Mono, UiScale.TypeSmall, CMTokens.Azure.Color, TextAnchor.MiddleLeft, x + 150f, ViewY + 186f, 90f, 22f, "0", 2f);
            _allyC = MenuUi.Label(_space, "Allegiance Crimson", CMTokens.Mono, UiScale.TypeSmall, CMTokens.CrimsonInk.Color, TextAnchor.MiddleRight, x + w - 90f, ViewY + 186f, 90f, 22f, "0", 2f);
            _allyBar = MenuUi.Shape(_space, "Allegiance Bar", ShapeKind.RoundRect, x, ViewY + 214f, w, 18f, CMTokens.Azure.Color, -1f);
            _allyBar.EmptyColor = CMTokens.Crimson.Color;
            _allyBar.Fill01 = 0.5f;
            MenuUi.Fit(MenuUi.Label(_space, "Rage Head", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, x, ViewY + 242f, 120f, 22f, "RAGE", 2f), 15);
            for (int i = 0; i < _rage.Length; i++)
            {
                VectorShape p = MenuUi.Shape(_space, "Rage " + i, ShapeKind.TriangleDown, x + 70f + i * 26f, ViewY + 244f, 22f, 18f, CMTokens.MeterEmpty.Color, 4f);
                p.OutlinePx = 2f;
                p.RimPx = 0f;
                _rage[i] = p;
            }
            MenuUi.Label(_space, "Time Head", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.MiddleLeft, x, ViewY + 284f, 200f, 24f, "TIME LEFT", 2f);
            _clock = MenuUi.Label(_space, "Clock", CMTokens.Mono, UiScale.TypeLead, Cream, TextAnchor.MiddleRight, x + w - 220f, ViewY + 274f, 220f, 46f, "8:00", 3f);

            // legend
            float ly = ViewY + 344f;
            MenuUi.Label(_space, "Legend", CMTokens.Display, UiScale.TypeBody, Brass, TextAnchor.MiddleLeft, x, ly, w, 30f, "LEGEND", 2f);
            ly += 38f;
            LegendIcon(x, ly, UiIcons.You, "You and where you face"); ly += 32f;
            LegendDisc(x, ly, CMTokens.Azure.Color, "Your team: class, name, health"); ly += 32f;
            LegendDisc(x, ly, CMTokens.Crimson.Color, "Enemy, seen near a teammate"); ly += 32f;
            LegendDisc(x, ly, CMTokens.Stone.Color, "Respawning at base, seconds left"); ly += 32f;
            LegendIcon(x, ly, UiIcons.MapCrown, "The hill: owner and capture share"); ly += 32f;
            LegendIcon(x, ly, UiIcons.MapEgg, "Egg at the shrine, then the Kaiju"); ly += 32f;
            LegendIcon(x, ly, UiIcons.Banner[0], "Base: spawn and safe ground"); ly += 32f;
            LegendDisc(x, ly, CMTokens.Biomass.Color, "Biomass source (feeds allegiance)"); ly += 32f;
            LegendDisc(x, ly, CMTokens.Corruption.Color, "Corruption source (raises rage)"); ly += 32f;
            VectorShape ping = MenuUi.Shape(_space, "Legend Ping", ShapeKind.TriangleDown, x + 2f, ly + 2f, 22f, 22f, CMTokens.MeterOrange.Color, 4f);
            ping.RimPx = 0f;
            MenuUi.Label(_space, "Legend Ping Word", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, x + 38f, ly, w - 38f, 26f, "Ping: attack, on my way, defend, help", 2f);

            // Integration batch 4: the key hints are one line in the strip under the map frame, so the side column's
            // mechanics and depth legend rows (MechanicsHud, DepthHud) never run into them.
            _hints = MenuUi.Label(_space, "Hints", CMTokens.Ui, UiScale.TypeMin, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleCenter, ViewX, HintsY, ViewW, 24f, "", 2f);
        }

        void LegendIcon(float x, float y, string icon, string word)
        {
            HudIcon i = HudIcon.Make(_space, "Legend " + word, x, y, 26f, 26f);
            i.Set(icon, _tier);
            MenuUi.Label(_space, "Legend Word " + word, CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, x + 38f, y, SideW - 38f, 26f, word, 2f);
        }

        void LegendDisc(float x, float y, Color c, string word)
        {
            VectorShape d = MenuUi.Shape(_space, "Legend " + word, ShapeKind.Circle, x + 3f, y + 3f, 20f, 20f, c, 8f);
            d.OutlinePx = 2f;
            d.RimPx = 0f;
            MenuUi.Label(_space, "Legend Word " + word, CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, x + 38f, y, SideW - 38f, 26f, word, 2f);
        }

        void BuildTip()
        {
            const float w = 300f, h = 128f;
            _tip = InteractUi.Floating(_space, "Map Tooltip", w, h);
            _tipPanel = MenuUi.Panel(_tip, "Panel", 0f, 0f, w, h);
            _tipTitle = MenuUi.Label(_tip, "Title", CMTokens.Display, UiScale.TypeSmall, Cream, TextAnchor.MiddleLeft, 16f, 10f, w - 32f, 28f, "", 2f);
            _tipSub = MenuUi.Fit(MenuUi.Label(_tip, "Sub", CMTokens.Ui, UiScale.TypeMin, Brass, TextAnchor.MiddleLeft, 16f, 38f, w - 32f, 22f, "", 2f), 15);
            _tipL1 = MenuUi.Fit(MenuUi.Label(_tip, "Line 1", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, 16f, 64f, 120f, 24f, "", 2f), 15);
            _tipA1 = MenuUi.Label(_tip, "Value 1a", CMTokens.Mono, UiScale.TypeSmall, Cream, TextAnchor.MiddleRight, 136f, 64f, 64f, 24f, "", 2f);
            _tipSep1 = MenuUi.Label(_tip, "Value 1 sep", CMTokens.Mono, UiScale.TypeSmall, Cream, TextAnchor.MiddleCenter, 200f, 64f, 24f, 24f, "", 2f);
            _tipB1 = MenuUi.Label(_tip, "Value 1b", CMTokens.Mono, UiScale.TypeSmall, Cream, TextAnchor.MiddleLeft, 224f, 64f, 64f, 24f, "", 2f);
            _tipL2 = MenuUi.Fit(MenuUi.Label(_tip, "Line 2", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, 16f, 92f, 120f, 24f, "", 2f), 15);
            _tipA2 = MenuUi.Label(_tip, "Value 2", CMTokens.Mono, UiScale.TypeSmall, Cream, TextAnchor.MiddleRight, 136f, 92f, 152f, 24f, "", 2f);
            _tip.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- open, close

        /// <summary>Loads the map texture (8x if baked, else 4x), shows the panel and reads the sim at once.</summary>
        public void Show(World w, int me, Texture hudTexture)
        {
            if (_root == null) return;
            _hudTexture = hudTexture;
            if (_tex == null)
            {
                _tex = Ui.MinimapGeometry.Texture(8);
                if (_tex == null) _tex = Resources.Load<Texture2D>("UI/Minimap/minimap@4x");
            }
            _image.texture = _tex;
            _root.gameObject.SetActive(true);
            _viewDirty = true;
            if (w != null) Refresh(w, me);
            Reposition();
        }

        Texture _hudTexture;

        /// <summary>Hides the panel and releases the texture (unless the HUD minimap is drawing the same asset).</summary>
        public void Hide()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(false);
            HoverKind = MapHoverKind.None;
            HoverIndex = -1;
            if (_tip != null) _tip.gameObject.SetActive(false);
            if (_image != null) _image.texture = null;
            if (_tex != null && !ReferenceEquals(_tex, _hudTexture)) Resources.UnloadAsset(_tex);
            _tex = null;
        }

        // ---------------------------------------------------------------- view transform

        /// <summary>Map pixel (1x, top-left origin, y down) to viewport units (centre origin, y up) at the current view.</summary>
        public Vector2 MapPxToView(Vector2 px)
        {
            float k = _fit * _zoom;
            return new Vector2(k * (px.x - _centre.x), -k * (px.y - _centre.y));
        }

        public Vector2 ViewToMapPx(Vector2 v)
        {
            float k = _fit * _zoom;
            return new Vector2(v.x / k + _centre.x, _centre.y - v.y / k);
        }

        /// <summary>Sim point to viewport units through the minimap's projection.</summary>
        public Vector2 SimToView(double x, double z) => MapPxToView(_proj.ToPixel(x, z));

        public void ViewToSim(Vector2 v, out double x, out double z)
        {
            Vector2 px = ViewToMapPx(v);
            _proj.ToSim(px.x, px.y, 1, out x, out z);
        }

        public static bool InsideView(Vector2 v) => Mathf.Abs(v.x) <= ViewW * 0.5f && Mathf.Abs(v.y) <= ViewH * 0.5f;

        /// <summary>Sets zoom and centre (clamped so the map always covers the viewport where it can).</summary>
        public void SetView(float zoom, Vector2 centre)
        {
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            _centre = centre;
            ClampCentre();
            _viewDirty = true;
        }

        /// <summary>Zooms by factor keeping the map point under viewport point v fixed.</summary>
        public void ZoomAt(float factor, Vector2 v)
        {
            Vector2 p = ViewToMapPx(v);
            _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
            float k = _fit * _zoom;
            _centre = new Vector2(p.x - v.x / k, p.y + v.y / k);
            ClampCentre();
            _viewDirty = true;
        }

        /// <summary>Pans by a viewport-unit delta (the map follows the drag).</summary>
        public void PanBy(Vector2 dv)
        {
            float k = _fit * _zoom;
            _centre = new Vector2(_centre.x - dv.x / k, _centre.y + dv.y / k);
            ClampCentre();
            _viewDirty = true;
        }

        void ClampCentre()
        {
            float k = _fit * _zoom;
            float hw = ViewW * 0.5f / k, hh = ViewH * 0.5f / k;
            _centre.x = _proj.Width <= 2f * hw ? _proj.Width * 0.5f : Mathf.Clamp(_centre.x, hw, _proj.Width - hw);
            _centre.y = _proj.Height <= 2f * hh ? _proj.Height * 0.5f : Mathf.Clamp(_centre.y, hh, _proj.Height - hh);
        }

        void ApplyView()
        {
            float k = _fit * _zoom;
            _mapLayer.localScale = new Vector3(k, k, 1f);
            _mapLayer.anchoredPosition = new Vector2(-k * _centre.x, k * _centre.y);
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Per frame while open: re-reads the sim at most every RefreshInterval, re-places markers on a view change.</summary>
        public void Tick(World w, int me, float now)
        {
            if (!IsOpen) return;
            if (w != null && now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshInterval;
                Refresh(w, me);
                _viewDirty = true;
            }
            if (_viewDirty) Reposition();
        }

        /// <summary>Reads the sim into the marker pools (positions cached as sim points). Allocation-free.</summary>
        public void Refresh(World w, int me)
        {
            if (_root == null || w == null) return;
            Refreshes++;
            _world = w;
            _me = me;
            HudBinder.Read(w, me, _state);
            HudState s = _state;
            MapDef map = w.Map;
            _ground.Watch(map, w.GridVersion);
            TeamId mine = s.HasPlayer ? s.Team : TeamId.Azure;

            // side column
            HudText.Set(_scoreA, NumberCache.Int(s.ScoreAzure));
            HudText.Set(_scoreC, NumberCache.Int(s.ScoreCrimson));
            _hillBar.Fill01 = s.HillShareAzure;
            HudText.Set(_clock, NumberCache.Mmss(s.ClockRemaining));
            _clock.color = s.ClockRemaining <= 60 ? Brass : Cream;
            float allyTotal = s.AllegianceAzure + s.AllegianceCrimson;
            _allyBar.Fill01 = allyTotal > 1e-4f ? s.AllegianceAzure / allyTotal : 0.5f;
            HudText.Set(_allyA, NumberCache.Int(Mathf.RoundToInt(s.AllegianceAzure)));
            HudText.Set(_allyC, NumberCache.Int(Mathf.RoundToInt(s.AllegianceCrimson)));
            int rage = Mathf.FloorToInt(s.Instability01 * _rage.Length + 1e-4f);
            for (int i = 0; i < _rage.Length; i++) _rage[i].SetFill(i < rage ? CMTokens.Meat.Color : CMTokens.MeterEmpty.Color);
            bool egg = !s.Hatched || s.Kaiju == KaijuState.Egg;
            HudText.Set(_kaijuState, KaijuWord(s, egg));
            _kaijuState.color = KaijuColour(s.Kaiju, egg);

            // hill
            _hillDisc.SetFill(s.Hill == HillState.Azure ? CMTokens.Azure.Color : s.Hill == HillState.Crimson ? CMTokens.Crimson.Color :
                s.Hill == HillState.Contested ? CMTokens.Stone.Color : Navy);
            _hillShare.Fill01 = s.HillShareAzure;
            HudText.Set(_hillA, NumberCache.Int(s.OnHillAzure));
            HudText.Set(_hillC, NumberCache.Int(s.OnHillCrimson));

            // kaiju or egg
            _kaijuIcon.Set(egg ? UiIcons.MapEgg : s.Kaiju == KaijuState.Azure ? UiIcons.Adult[1] : s.Kaiju == KaijuState.Crimson ? UiIcons.Adult[2] :
                s.Kaiju == KaijuState.Berserk ? UiIcons.Adult[3] : UiIcons.Hatchling, _tier);
            _kaijuX = egg ? map.Shrine.X : w.Kaiju.X;
            _kaijuZ = egg ? map.Shrine.Z : w.Kaiju.Z;
            _kaijuHp.Fill01 = s.KaijuMeter01;
            _kaijuHp.SetFill(egg ? CMTokens.EggSeam.Color : s.Kaiju == KaijuState.Berserk ? CMTokens.Corruption.Color : CMTokens.MeterOrange.Color);
            HudText.Set(_kaijuWord, egg ? HatchWord(s.UntilHatch) : KaijuWord(s, false));
            _shrine.gameObject.SetActive(!egg);

            // nodes
            int nodes = w.Nodes != null ? Math.Min(w.Nodes.Length, MaxNodes) : 0;
            for (int i = 0; i < MaxNodes; i++)
            {
                bool on = i < nodes && w.Nodes[i] != null;
                _nodeShown[i] = on;
                if (_nodes[i].gameObject.activeSelf != on) _nodes[i].gameObject.SetActive(on);
                if (!on) continue;
                ResourceNode n = w.Nodes[i];
                Color c = n.Kind == NodeKind.Corruption ? CMTokens.Corruption.Color : CMTokens.Biomass.Color;
                _nodeDots[i].SetFill(n.Charges > 0 ? c : CMTokens.WithAlpha(c, 0.35f));
            }

            // units
            int count = Math.Min(w.ActorCount, MaxActors);
            int[] respawnCount = _respawnCount;
            respawnCount[0] = respawnCount[1] = 0;
            for (int i = 0; i < MaxActors; i++)
            {
                ActorMarker m = _actors[i];
                bool on = false;
                if (i < count && i != me)
                {
                    Actor a = w.Actors[i];
                    bool ally = a.Team == mine;
                    m.Enemy = !ally;
                    m.Respawning = !a.Alive && ally;
                    on = a.Alive ? (ally || MapRules.Seen(w, i, mine, w.ActorCount)) : m.Respawning;
                    if (on) Fill(m, w, i, a, ally, respawnCount);
                }
                m.Shown = on;
                if (m.Rt.gameObject.activeSelf != on) m.Rt.gameObject.SetActive(on);
            }
            _youShown = s.HasPlayer && s.Alive;
            if (_you.gameObject.activeSelf != _youShown) _you.gameObject.SetActive(_youShown);
            if (s.HasPlayer)
            {
                _youX = w.X[me];
                _youZ = w.Z[me];
                _youFacing.localEulerAngles = new Vector3(0f, 0f, _proj.FacingToUiDeg(w.Actors[me].Facing));
            }
            _you.SetAsLastSibling();
            _reticle.SetAsLastSibling();

            // pings
            Pings.Track(w);
            double now = w.Match.Time;
            for (int i = 0; i < MaxPings; i++)
            {
                Ping p = Pings.Book.Get(i);
                float alpha = PingBook.Alpha(p, now);
                bool on = alpha > 0f;
                if (_pings[i].gameObject.activeSelf != on) _pings[i].gameObject.SetActive(on);
                if (!on) continue;
                if (_pingSerial[i] != p.Serial)
                {
                    _pingSerial[i] = p.Serial;
                    _pingShapes[i].Kind = PingWords.Shape[(int)p.Kind];
                    _pingShapes[i].SetFill(PingWords.Colour(p.Kind));
                    _pingShapes[i].SetVerticesDirty();
                    HudText.Set(_pingWords[i], PingWords.Upper[(int)p.Kind]);
                }
                _pingGroups[i].alpha = alpha;
            }
        }

        readonly int[] _respawnCount = new int[2];

        void Fill(ActorMarker m, World w, int i, Actor a, bool ally, int[] respawnCount)
        {
            int team = a.Team == TeamId.Crimson ? 1 : 0;
            int cls = Mathf.Clamp((int)a.Cls, 0, ClassUpper.Length - 1);
            if (m.Cls != cls || m.Team != team)
            {
                m.Cls = cls;
                m.Team = team;
                m.Badge.Set(UiIcons.ClassBadge[cls, team], _tier);
            }
            if (m.Respawning)
            {
                PadDef pad = w.Map.Pads[Mathf.Clamp(team, 0, w.Map.Pads.Length - 1)];
                m.X = pad.X;
                m.Z = pad.Z;
                m.RespawnSlot = respawnCount[team]++;
                m.Disc.SetFill(CMTokens.Stone.Color);
                HudText.Set(m.Timer, Seconds[Math.Min(99, (int)Math.Ceiling(Math.Max(0, global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(a.RespawnIn))))]);
                if (!m.Timer.gameObject.activeSelf) m.Timer.gameObject.SetActive(true);
            }
            else
            {
                m.X = w.X[i];
                m.Z = w.Z[i];
                m.Disc.SetFill(CMTokens.Team(a.Team));
                // Polish pass 1: team by shape too (Azure circle, Crimson diamond), as on the minimap.
                ShapeKind dk = GameHud.TeamDot(a.Team);
                if (m.Disc.Kind != dk)
                {
                    m.Disc.Kind = dk;
                    m.Disc.SetVerticesDirty();
                }
                if (m.Timer.gameObject.activeSelf) m.Timer.gameObject.SetActive(false);
            }
            float hp01 = a.MaxHp > 0 ? Mathf.Clamp01((float)(a.Hp / a.MaxHp)) : 0f;
            int lit = a.Alive ? Mathf.CeilToInt(hp01 * Pips - 1e-4f) : 0;
            for (int p = 0; p < Pips; p++) m.Pips[p].SetFill(p < lit ? Cream : CMTokens.MeterEmpty.Color);
            bool named = ally;
            if (m.Name.gameObject.activeSelf != named) m.Name.gameObject.SetActive(named);
            if (named) HudText.Set(m.Name, Callsigns.For(w, i, _me));
        }

        /// <summary>Moves every shown marker to its sim point at the current view (no sim read).</summary>
        public void Reposition()
        {
            if (_root == null) return;
            _viewDirty = false;
            ApplyView();
            if (_world != null)
            {
                MapDef map = _world.Map;
                _hill.anchoredPosition = SimToView(map.Hill.X, map.Hill.Z);
                for (int t = 0; t < 2 && t < map.Pads.Length; t++) _bases[t].anchoredPosition = SimToView(map.Pads[t].X, map.Pads[t].Z);
                _shrine.anchoredPosition = SimToView(map.Shrine.X, map.Shrine.Z);
                int nodes = _world.Nodes != null ? Math.Min(_world.Nodes.Length, MaxNodes) : 0;
                for (int i = 0; i < nodes; i++)
                    if (_nodeShown[i]) _nodes[i].anchoredPosition = SimToView(_world.Nodes[i].X, _world.Nodes[i].Z);
                double now = _world.Match.Time;
                for (int i = 0; i < MaxPings; i++)
                {
                    if (!_pings[i].gameObject.activeSelf) continue;
                    Ping p = Pings.Book.Get(i);
                    _pings[i].anchoredPosition = SimToView(p.X, p.Z);
                }
            }
            _kaiju.anchoredPosition = SimToView(_kaijuX, _kaijuZ);
            for (int i = 0; i < MaxActors; i++)
            {
                ActorMarker m = _actors[i];
                if (!m.Shown) continue;
                Vector2 v = SimToView(m.X, m.Z);
                if (m.Respawning)
                {
                    // fan the waiting allies around their base banner
                    float ang = (m.RespawnSlot * 40f + 200f) * Mathf.Deg2Rad;
                    float r = 34f + 14f * (m.RespawnSlot / 9);
                    v += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                }
                m.Rt.anchoredPosition = v;
            }
            if (_youShown) _you.anchoredPosition = SimToView(_youX, _youZ);
        }

        // ---------------------------------------------------------------- hover and tooltip

        /// <summary>Picks the marker nearest viewport point v within HoverRadius (pings, you, units, then objectives).</summary>
        public void Hover(Vector2 v, bool inside)
        {
            MapHoverKind kind = MapHoverKind.None;
            int index = -1;
            if (inside && _world != null)
            {
                float best = HoverRadius * HoverRadius;
                for (int i = 0; i < MaxActors; i++)
                    if (_actors[i].Shown) Consider(ref kind, ref index, ref best, MapHoverKind.Actor, i, _actors[i].Rt.anchoredPosition, v);
                if (_youShown) Consider(ref kind, ref index, ref best, MapHoverKind.Actor, _me, _you.anchoredPosition, v);
                for (int i = 0; i < MaxPings; i++)
                    if (_pings[i].gameObject.activeSelf) Consider(ref kind, ref index, ref best, MapHoverKind.Ping, i, _pings[i].anchoredPosition, v);
                Consider(ref kind, ref index, ref best, MapHoverKind.Hill, 0, _hill.anchoredPosition, v);
                Consider(ref kind, ref index, ref best, MapHoverKind.Kaiju, 0, _kaiju.anchoredPosition, v);
                if (_shrine.gameObject.activeSelf) Consider(ref kind, ref index, ref best, MapHoverKind.Shrine, 0, _shrine.anchoredPosition, v);
                for (int t = 0; t < 2; t++) Consider(ref kind, ref index, ref best, MapHoverKind.Base, t, _bases[t].anchoredPosition, v);
                for (int i = 0; i < MaxNodes; i++)
                    if (_nodeShown[i]) Consider(ref kind, ref index, ref best, MapHoverKind.Node, i, _nodes[i].anchoredPosition, v);
            }
            HoverKind = kind;
            HoverIndex = index;
            if (kind == MapHoverKind.None) { if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false); return; }
            FillTip(kind, index);
            // beside the marker, inside the panel; the viewport is centred at (ViewX + ViewW / 2, ViewY + ViewH / 2) of the space
            Vector2 m = MarkerView(kind, index);
            Vector2 spaceCentre = new Vector2(ViewX + ViewW * 0.5f - HudLayout.RefW * 0.5f, HudLayout.RefH * 0.5f - (ViewY + ViewH * 0.5f));
            Vector2 pos = spaceCentre + m + new Vector2(24f, 18f);
            Rect bounds = new Rect(PanelX - HudLayout.RefW * 0.5f, HudLayout.RefH * 0.5f - (PanelY + PanelH), PanelW, PanelH);
            _tip.anchoredPosition = InteractUi.ClampTopLeft(pos, _tip.sizeDelta.x, _tip.sizeDelta.y, bounds);
            if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
        }

        static void Consider(ref MapHoverKind kind, ref int index, ref float best, MapHoverKind k, int i, Vector2 at, Vector2 v)
        {
            float d = (at - v).sqrMagnitude;
            if (d < best) { best = d; kind = k; index = i; }
        }

        Vector2 MarkerView(MapHoverKind k, int i)
        {
            switch (k)
            {
                case MapHoverKind.Actor: return i == _me ? _you.anchoredPosition : _actors[i].Rt.anchoredPosition;
                case MapHoverKind.Hill: return _hill.anchoredPosition;
                case MapHoverKind.Kaiju: return _kaiju.anchoredPosition;
                case MapHoverKind.Shrine: return _shrine.anchoredPosition;
                case MapHoverKind.Base: return _bases[i].anchoredPosition;
                case MapHoverKind.Node: return _nodes[i].anchoredPosition;
                case MapHoverKind.Ping: return _pings[i].anchoredPosition;
                default: return Vector2.zero;
            }
        }

        void SetTip(string title, string sub, string l1, string a1, string sep1, string b1, string l2, string a2)
        {
            HudText.Set(_tipTitle, title);
            HudText.Set(_tipSub, sub);
            HudText.Set(_tipL1, l1);
            HudText.Set(_tipA1, a1);
            HudText.Set(_tipSep1, sep1);
            HudText.Set(_tipB1, b1);
            HudText.Set(_tipL2, l2);
            HudText.Set(_tipA2, a2);
        }

        string Distance(double x, double z)
        {
            if (_world == null || _me < 0 || _me >= _world.ActorCount) return "";
            double dx = x - _world.X[_me], dz = z - _world.Z[_me];
            return Metres[Math.Min(999, (int)Math.Round(Math.Sqrt(dx * dx + dz * dz)))];
        }

        public string TipTitle => _tipTitle != null ? _tipTitle.text : null;
        public string TipSub => _tipSub != null ? _tipSub.text : null;

        void FillTip(MapHoverKind kind, int i)
        {
            World w = _world;
            HudState s = _state;
            switch (kind)
            {
                case MapHoverKind.Actor:
                {
                    Actor a = w.Actors[i];
                    int cls = Mathf.Clamp((int)a.Cls, 0, ClassUpper.Length - 1);
                    int team = a.Team == TeamId.Crimson ? 1 : 0;
                    bool respawning = !a.Alive;
                    double x = i == _me ? _youX : _actors[i].X, z = i == _me ? _youZ : _actors[i].Z;
                    if (respawning)
                        SetTip(Callsigns.For(w, i, _me), RespawnClass[cls], "BACK IN", Seconds[Math.Min(99, (int)Math.Ceiling(Math.Max(0, global::CriticalMass.Runtime.Flow.MatchSpeed.ToReal(a.RespawnIn))))], "", "", "AT", team == 0 ? "AZURE BASE" : "CRIMSON BASE");
                    else
                        SetTip(Callsigns.For(w, i, _me), ClassTeam[cls, team], "HEALTH", NumberCache.Int(Mathf.CeilToInt((float)Math.Max(0, a.Hp))), "/",
                            NumberCache.Int(Mathf.CeilToInt((float)a.MaxHp)), "DISTANCE", i == _me ? "-" : Distance(x, z));
                    break;
                }
                case MapHoverKind.Hill:
                    SetTip("THE HILL", HillWord(s.Hill), "ON THE HILL", NumberCache.Int(s.OnHillAzure), "v", NumberCache.Int(s.OnHillCrimson),
                        "DISTANCE", Distance(w.Map.Hill.X, w.Map.Hill.Z));
                    break;
                case MapHoverKind.Kaiju:
                {
                    bool egg = !s.Hatched || s.Kaiju == KaijuState.Egg;
                    if (egg)
                        SetTip("KAIJU EGG", "FEED IT TO WIN ITS ALLEGIANCE", "HATCHES IN", NumberCache.Mmss(s.UntilHatch), "", "", "DISTANCE", Distance(_kaijuX, _kaijuZ));
                    else
                        SetTip("THE KAIJU", KaijuWord(s, false), "HEALTH", NumberCache.Int(Mathf.CeilToInt((float)Math.Max(0, w.Kaiju.Hp))), "/",
                            NumberCache.Int(Mathf.CeilToInt((float)w.Kaiju.MaxHp)), "DISTANCE", Distance(_kaijuX, _kaijuZ));
                    break;
                }
                case MapHoverKind.Shrine:
                    SetTip("THE SHRINE", "WHERE THE EGG HATCHED", "", "", "", "", "DISTANCE", Distance(w.Map.Shrine.X, w.Map.Shrine.Z));
                    break;
                case MapHoverKind.Base:
                {
                    bool own = (i == 0 ? TeamId.Azure : TeamId.Crimson) == (s.HasPlayer ? s.Team : TeamId.Azure);
                    SetTip(i == 0 ? "AZURE BASE" : "CRIMSON BASE", own ? "YOUR SPAWN AND SAFE GROUND" : "ENEMY SPAWN", "RESPAWNING",
                        own ? NumberCache.Int(_respawnCount[i]) : "-", "", "", "DISTANCE", Distance(w.Map.Pads[i].X, w.Map.Pads[i].Z));
                    break;
                }
                case MapHoverKind.Node:
                {
                    ResourceNode n = w.Nodes[i];
                    bool corrupt = n.Kind == NodeKind.Corruption;
                    SetTip(corrupt ? "CORRUPTION" : "BIOMASS", corrupt ? "RAISES THE KAIJU'S RAGE" : "FEEDS THE KAIJU'S ALLEGIANCE", "CHARGES",
                        NumberCache.Int(Math.Max(0, n.Charges)), "/", NumberCache.Int(Math.Max(0, n.MaxCharges)), "DISTANCE", Distance(n.X, n.Z));
                    break;
                }
                case MapHoverKind.Ping:
                {
                    Ping p = Pings.Book.Get(i);
                    SetTip(PingWords.Upper[(int)p.Kind], PlaceUpper[Mathf.Clamp(p.Place, 0, PlaceUpper.Length - 1)], "", "", "", "", "DISTANCE", Distance(p.X, p.Z));
                    break;
                }
            }
            _tipPanel.SetRim(kind == MapHoverKind.Actor && i != _me && i < w.ActorCount && w.Actors[i].Team != (s.HasPlayer ? s.Team : TeamId.Azure)
                ? CMTokens.Crimson.Color : Cream);
        }

        // ---------------------------------------------------------------- controller reticle and hints

        public void ShowReticle(bool on)
        {
            if (_reticle == null || _reticle.gameObject.activeSelf == on) return;
            _reticle.gameObject.SetActive(on);
            _reticle.anchoredPosition = Vector2.zero;
        }

        /// <summary>Key hints for the device (allocates; call on open and on a device or binding change only).</summary>
        public void SetHints(ControlDevice d, string mapKey, string pingKey)
        {
            if (_hints == null) return;
            bool pad = d == ControlDevice.Gamepad;
            HudText.Set(_hints, pad
                ? "LT / RT  ZOOM      R STICK  PAN      " + pingKey + "  HOLD TO PING      " + mapKey + " / B  CLOSE"
                : "WHEEL  ZOOM      DRAG  PAN      " + pingKey + "  HOLD TO PING      " + mapKey + " / ESC  CLOSE");
            HudText.Set(_closeKey, (pad ? "B" : mapKey) + "  CLOSE");
        }

        /// <summary>Whether a viewport-space point is on the close chip (top right of the panel).</summary>
        public bool OnCloseChip(Vector2 spaceLocal)
        {
            if (_closeChip == null) return false;
            Vector2 c = SpaceTopLeftToCentre(_closeChip);
            Vector2 half = _closeChip.sizeDelta * 0.5f;
            return Mathf.Abs(spaceLocal.x - c.x) <= half.x && Mathf.Abs(spaceLocal.y - c.y) <= half.y;
        }

        /// <summary>Whether a point in the space (centre origin, y up) is inside the panel.</summary>
        public static bool OnPanel(Vector2 spaceLocal) =>
            spaceLocal.x >= PanelX - HudLayout.RefW * 0.5f && spaceLocal.x <= PanelX + PanelW - HudLayout.RefW * 0.5f &&
            spaceLocal.y <= HudLayout.RefH * 0.5f - PanelY && spaceLocal.y >= HudLayout.RefH * 0.5f - (PanelY + PanelH);

        static Vector2 SpaceTopLeftToCentre(RectTransform rt) =>
            new Vector2(rt.anchoredPosition.x - HudLayout.RefW * 0.5f, rt.anchoredPosition.y + HudLayout.RefH * 0.5f);

        public RectTransform SpaceRect => _space;

        // ---------------------------------------------------------------- test accessors

        public bool ActorShown(int id) => id >= 0 && id < MaxActors && _actors[id].Shown;
        public bool ActorRespawning(int id) => id >= 0 && id < MaxActors && _actors[id].Shown && _actors[id].Respawning;
        public Vector2 ActorView(int id) => _actors[id].Rt.anchoredPosition;
        public bool YouShown => _youShown;
        public Vector2 YouView => _you.anchoredPosition;
        public Vector2 HillView => _hill.anchoredPosition;
        public Vector2 BaseView(int t) => _bases[t].anchoredPosition;
        public Vector2 KaijuView => _kaiju.anchoredPosition;
        public bool PingShown(int slot) => _pings[slot].gameObject.activeSelf;
        public Vector2 PingView(int slot) => _pings[slot].anchoredPosition;
        public string ActorName(int id) => _actors[id].Name.text;

        // ---------------------------------------------------------------- words

        static string HillWord(HillState h) =>
            h == HillState.Azure ? "AZURE HOLDS" : h == HillState.Crimson ? "CRIMSON HOLDS" : h == HillState.Contested ? "CONTESTED" : "NEUTRAL";

        static string KaijuWord(HudState s, bool egg) =>
            egg ? "EGG" : s.Kaiju == KaijuState.Azure ? "AZURE" : s.Kaiju == KaijuState.Crimson ? "CRIMSON" :
            s.Kaiju == KaijuState.Berserk ? "BERSERK" : "WILD";

        static Color KaijuColour(KaijuState k, bool egg) =>
            egg ? CMTokens.EggSeam.Color : k == KaijuState.Azure ? CMTokens.Azure.Color : k == KaijuState.Crimson ? CMTokens.Crimson.Color :
            k == KaijuState.Berserk ? CMTokens.Corruption.Color : Cream;

        static readonly string[] HatchWords = BuildHatchWords();

        static string[] BuildHatchWords()
        {
            var a = new string[601];
            for (int i = 0; i < a.Length; i++) a[i] = "HATCH " + (i / 60).ToString("0", CultureInfo.InvariantCulture) + ":" + (i % 60).ToString("00", CultureInfo.InvariantCulture);
            return a;
        }

        static string HatchWord(double until) => HatchWords[Math.Max(0, Math.Min(600, (int)Math.Ceiling(until - 1e-6)))];
    }
}
