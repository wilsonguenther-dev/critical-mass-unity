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
            CMTokens.PushShaderGlobals();
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

            BuildWorldLayer();
            Clusters[(int)HudCluster.Match] = Cluster(HudCluster.Match);
            Clusters[(int)HudCluster.Kaiju] = Cluster(HudCluster.Kaiju);
            Clusters[(int)HudCluster.Player] = Cluster(HudCluster.Player);
            Clusters[(int)HudCluster.Minimap] = Cluster(HudCluster.Minimap);
            BuildMatch(Clusters[0]);
            BuildKaiju(Clusters[1]);
            BuildPlayer(Clusters[2]);
            BuildMinimap(Clusters[3]);
            BuildDeath();
            UpdateScale();
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
                _mapImage.texture = MapTexture(mapTier);
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

        const int Pips = 5;
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
            _clock = Label(c, "Clock", CMTokens.Mono, 50, Cream, TextAnchor.MiddleCenter, 316f, 2f, 168f, 68f, "08:00", 0f);
            // hill-control bar with the crown medallion
            _hillBar = Shape(c, "Hill Bar", ShapeKind.RoundRect, 236f, 80f, 328f, 26f, CMTokens.Azure.Color, -1f);
            _hillBar.EmptyColor = CMTokens.Crimson.Color;
            _medallion = Shape(c, "Crown Medallion", ShapeKind.Circle, 370f, 62f, 60f, 60f, CMTokens.Stone.Color);
            _medallionHalf = Shape(_medallion.transform, "Contested Half", ShapeKind.Pie, 5f, 5f, 50f, 50f, CMTokens.Crimson.Color);
            _medallionHalf.Fill01 = 0.5f;
            _medallionHalf.gameObject.SetActive(false);
            Icon(_medallion.transform, "Crown", UiIcons.Crown, 13f, 11f, 34f, 34f);
            _hillWord = Label(c, "Hill Word", CMTokens.Ui, 19, Cream, TextAnchor.MiddleCenter, 250f, 106f, 300f, 24f, "NEUTRAL");
        }

        void BuildTeamPanel(RectTransform c, TeamId team, float x, out Text score, VectorShape[] pips, out Text onHill)
        {
            bool azure = team == TeamId.Azure;
            Color tc = CMTokens.Team(team);
            Shape(c, team + " Panel", ShapeKind.Chamfer, x, 4f, 308f, 64f, Panel, 14f);
            float shieldX = azure ? x - 6f : x + 308f - 52f;
            Icon(c, team + " Crest", UiIcons.Crest[(int)team], shieldX - 6f, 0f, 70f, 70f);   // illustrated crest (v2)
            float pipX0 = azure ? x + 64f : x + 308f - 64f - Pips * 22f;
            for (int i = 0; i < Pips; i++)
            {
                int slot = azure ? i : Pips - 1 - i;
                VectorShape p = Shape(c, team + " Pip " + i, ShapeKind.Diamond, pipX0 + slot * 22f, 14f, 18f, 24f, Empty);
                p.OutlinePx = 2f;
                p.RimPx = 0f;
                pips[i] = p;
            }
            onHill = Label(c, team + " On Hill", CMTokens.Ui, 14, Cream, azure ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                pipX0, 40f, Pips * 22f, 20f, "0 ON HILL", 2f);
            score = Label(c, team + " Score", CMTokens.Mono, 44, Cream, azure ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
                azure ? 168f : 500f, 4f, 132f, 64f, "0");
        }

        // ---------------------------------------------------------------- 02 KAIJU

        const int MeterSegments = 8;
        const int RagePips = 5;
        HudIcon _kaijuHead;
        VectorShape _eggFrame;
        Text _kaijuWord, _kaijuNumber;
        readonly VectorShape[] _segments = new VectorShape[MeterSegments];
        readonly VectorShape[] _rage = new VectorShape[RagePips];

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
            _kaijuNumber = Label(cap.transform, "Meter Number", CMTokens.Mono, 20, Cream, TextAnchor.MiddleCenter, 0f, 0f, 52f, 54f, "0", 2f);
            Label(c, "Rage Label", CMTokens.Ui, 13, Cream, TextAnchor.MiddleLeft, 92f, 60f, 44f, 22f, "RAGE", 2f);
            for (int i = 0; i < RagePips; i++)
            {
                VectorShape p = Shape(c, "Rage " + i, ShapeKind.TriangleDown, 136f + i * 22f, 62f, 20f, 16f, Empty);
                p.OutlinePx = 2f;
                p.RimPx = 0f;
                _rage[i] = p;
            }
            _kaijuWord = Label(c, "Kaiju Word", CMTokens.Ui, 20, Cream, TextAnchor.MiddleRight, 214f, 60f, 124f, 28f, "EGG");
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
            Shape(_portraitFrame.transform, "Name Plate", ShapeKind.RoundRect, 6f, 92f, 104f, 24f, Navy, -1f).RimPx = 0f;
            _classBadge = Icon(_portraitFrame.transform, "Class Badge", UiIcons.ClassBadge[0, 0], -6f, -6f, 36f, 36f);
            _className = Label(_portraitFrame.transform, "Class Name", CMTokens.Ui, 15, Cream, TextAnchor.MiddleCenter, 6f, 92f, 104f, 24f,
                "WARRIOR", 2f);

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
            _hpNumber = Label(c, "HP Number", CMTokens.Mono, 24, Cream, TextAnchor.MiddleRight, 284f, 30f, 82f, 32f, "160", 2f);
            _hpGhost = Shape(c, "HP Ghost", ShapeKind.RoundRect, 136f, 72f, 228f, 28f, Cream, -1f);
            _hpGhost.EmptyColor = Empty;
            _hpBar = Shape(c, "HP Bar", ShapeKind.RoundRect, 136f, 72f, 228f, 28f, CMTokens.Azure.Color, -1f);

            string[] keys = { "LMB", "RMB", "E" };   // U-006 / U-020: attack, class ability, carry/interact
            for (int i = 0; i < 3; i++)
            {
                float bx = 392f + i * 84f;
                var b = new AbilityButton();
                b.Body = Shape(c, "Ability " + i, ShapeKind.RoundRect, bx, 6f, HudLayout.AbilityButton, HudLayout.AbilityButton, Panel, 16f);
                b.Rt = b.Body.rectTransform;
                b.Icon = Icon(b.Body.transform, "Icon", i == 2 ? UiIcons.CarryEmpty : null, 10f, 7f, 56f, 56f);
                b.Sweep = Shape(b.Body.transform, "Cooldown", ShapeKind.Pie, 5f, 5f, 66f, 66f, CMTokens.WithAlpha(Navy, 0.72f));
                b.Sweep.Fill01 = 0f;
                b.Seconds = Label(b.Body.transform, "Seconds", CMTokens.Mono, 30, Cream, TextAnchor.MiddleCenter, 0f, 0f, 76f, 76f, "", 3f);
                b.Count = Label(b.Body.transform, "Count", CMTokens.Mono, 22, Cream, TextAnchor.MiddleRight, 34f, 44f, 38f, 28f, "", 3f);
                Shape(c, "Key " + i, ShapeKind.RoundRect, bx + 14f, 86f, 48f, 28f, Navy, -1f).RimColor = Cream;
                Label(c, "Key Label " + i, CMTokens.Ui, 15, Cream, TextAnchor.MiddleCenter, bx + 14f, 86f, 48f, 28f, keys[i], 0f);
                b.Name = Label(c, "Ability Name " + i, CMTokens.Ui, 15, Cream, TextAnchor.MiddleCenter, bx - 14f, -20f, 104f, 22f, "", 2f);
                _buttons[i] = b;
            }
            bool showNames = PlayerPrefs.GetInt(MatchesPlayedKey, 0) < AbilityNameMatches;
            for (int i = 0; i < 3; i++) _buttons[i].Name.gameObject.SetActive(showNames);
        }

        static readonly string[] ClassNames = { "WARRIOR", "FARMER", "MINER", "DEMOLITIONS", "BUILDER" };
        static readonly string[] AbilityNames = { "SHIELD", "ROOTS", "WALL BREAK", "MINE", "BUILD" };

        // ---------------------------------------------------------------- minimap

        const int MaxDots = 40;
        /// <summary>An enemy shows on the minimap only within this many sim units of a living teammate (seen enemies).</summary>
        public const double SightRadius = 16.0;
        RawImage _mapImage;
        MinimapGround _ground;
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
            _mapImage = _mapArea.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = false;
            _mapImage.texture = MapTexture(_mapTier);
            RectTransform g = Rt(_mapArea, "Ground Changes");
            g.anchorMin = Vector2.zero;
            g.anchorMax = Vector2.one;
            g.sizeDelta = Vector2.zero;
            _ground = g.gameObject.AddComponent<MinimapGround>();
            _ground.raycastTarget = false;
            _ground.Bind(_proj, MinimapGeometry.BakedMap);
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
            Label(_mapHill.transform, "Label", CMTokens.Ui, 11, Cream, TextAnchor.MiddleCenter, -23f, 24f, 70f, 14f, "HILL", 2f);
            for (int t = 0; t < 2; t++)
            {
                HudIcon b = Icon(_mapArea, (TeamId)t + " Base", UiIcons.Banner[t], 0f, 0f, 24f, 24f);
                Label(b.Rt, "Label", CMTokens.Ui, 11, Cream, TextAnchor.MiddleCenter, -23f, 23f, 70f, 14f, t == 0 ? "AZURE" : "CRIMSON", 2f);
                _mapBases[t] = b;
            }
            _mapEgg = Icon(_mapArea, "Map Egg", UiIcons.MapEgg, 0f, 0f, 22f, 22f);
            Label(_mapEgg.Rt, "Label", CMTokens.Ui, 11, Cream, TextAnchor.MiddleCenter, -24f, 20f, 70f, 14f, "KAIJU", 2f);
            _mapYou = Rt(_mapArea, "Map You");
            Place(_mapYou, 0f, 0f, 18f, 18f);
            _youFacing = Rt(_mapYou, "Facing");
            Place(_youFacing, 0f, 0f, 18f, 18f);
            VectorShape arrow = Shape(_youFacing, "Arrow", ShapeKind.TriangleRight, 13f, 3f, 11f, 12f, CMTokens.ToolBrass.Color, 2f);
            arrow.OutlinePx = 1.5f;
            arrow.RimPx = 0f;
            Icon(_mapYou, "Marker", UiIcons.You, 0f, 0f, 18f, 18f);
            Label(_mapYou, "Label", CMTokens.Ui, 11, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 20f, -12f, 40f, 14f, "YOU", 2f);
            _mapYou.gameObject.SetActive(false);
        }

        static Texture2D MapTexture(int tier) => Resources.Load<Texture2D>("UI/Minimap/minimap@" + tier + "x");

        readonly HudIcon[] _mapBases = new HudIcon[2];

        void PlaceOnMap(RectTransform rt, double simX, double simZ)
        {
            Vector2 p = _proj.ToPixel(simX, simZ);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(p.x, -p.y);
        }

        // ---------------------------------------------------------------- world layer

        const int HeartRows = 24;
        const int DamageNumbers = 12;
        const int Pointers = 2;
        RectTransform _world;
        VectorShape _hillMarker;
        Text _hillMarkerWord;
        readonly RectTransform[] _heartRows = new RectTransform[HeartRows];
        readonly VectorShape[,] _rowHearts = new VectorShape[HeartRows, Hearts];
        readonly Text[] _dmg = new Text[DamageNumbers];
        readonly float[] _dmgAge = new float[DamageNumbers];
        readonly Vector3[] _dmgPos = new Vector3[DamageNumbers];
        int _dmgNext;
        readonly RectTransform[] _ptr = new RectTransform[Pointers];
        readonly RectTransform[] _ptrArrow = new RectTransform[Pointers];
        readonly Text[] _ptrDist = new Text[Pointers];
        double[] _lastHit = new double[64];
        int _eventCursor = -1;

        void BuildWorldLayer()
        {
            _world = Rt(Root, "World Layer");
            _world.anchorMin = Vector2.zero;
            _world.anchorMax = Vector2.one;
            _world.sizeDelta = Vector2.zero;

            RectTransform marker = Rt(_world, "Hill Marker");
            Center(marker, 120f, 80f);
            _hillMarker = Shape(marker, "Medallion", ShapeKind.Circle, 38f, 0f, 44f, 44f, CMTokens.Stone.Color);
            Icon(_hillMarker.transform, "Crown", UiIcons.Crown, 4f, 4f, 36f, 36f);
            _hillMarkerWord = Label(marker, "Word", CMTokens.Ui, 16, Cream, TextAnchor.MiddleCenter, 0f, 46f, 120f, 22f, "NEUTRAL", 2.5f);

            for (int r = 0; r < HeartRows; r++)
            {
                RectTransform row = Rt(_world, "Hearts " + r);
                Center(row, Hearts * 16f, 14f);
                for (int i = 0; i < Hearts; i++)
                {
                    VectorShape h = Shape(row, "H" + i, ShapeKind.Heart, i * 16f, 0f, 14f, 13f, CMTokens.Azure.Color);
                    h.OutlinePx = 2f;
                    h.RimPx = 0f;
                    h.EmptyColor = Empty;
                    _rowHearts[r, i] = h;
                }
                row.gameObject.SetActive(false);
                _heartRows[r] = row;
            }
            for (int i = 0; i < DamageNumbers; i++)
            {
                Text t = HudText.Make(_world, "Damage " + i, CMTokens.Mono, 30, Cream, TextAnchor.MiddleCenter, 3f);
                Center(t.rectTransform, 80f, 32f);
                t.gameObject.SetActive(false);
                _dmg[i] = t;
            }
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
                _ptrDist[i] = Label(p, "Distance", CMTokens.Mono, 22, Cream, TextAnchor.MiddleCenter, -10f, 42f, 80f, 26f, "0m", 2.5f);
                p.gameObject.SetActive(false);
                _ptr[i] = p;
            }
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
            Label(mid, "Respawn Label", CMTokens.Ui, 24, Cream, TextAnchor.MiddleCenter, 0f, 0f, 400f, 34f, "RESPAWN IN", 3f);
            _respawn = Label(mid, "Respawn Seconds", CMTokens.Mono, 100, Cream, TextAnchor.MiddleCenter, 0f, 34f, 400f, 110f, "5", 3f);
            _koRing = Shape(_world, "KO Ring", ShapeKind.Circle, 0f, 0f, 72f, 72f, new Color(0, 0, 0, 0));
            Center(_koRing.rectTransform, 72f, 72f);
            _koRing.OutlinePx = 4f;
            _koRing.RimPx = 3f;
            _death.gameObject.SetActive(false);
            _koRing.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- update

        void LateUpdate()
        {
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
            if (Driver == null || Driver.World == null || Root == null) return;
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
            }
        }

        bool _countedMatch;

        static readonly string[] OnHillA = { "0 ON HILL", "1 ON HILL", "2 ON HILL", "3 ON HILL", "4 ON HILL", "5+ ON HILL" };

        void ApplyMatch(HudState s)
        {
            HudText.Set(_clock, NumberCache.Mmss(s.ClockRemaining));
            _clock.color = s.ClockRemaining <= 60 ? CMTokens.ToolBrass.Color : Cream;
            HudText.Set(_scoreA, NumberCache.Int(s.ScoreAzure));
            HudText.Set(_scoreC, NumberCache.Int(s.ScoreCrimson));
            HudText.Set(_onHillA, OnHillA[Math.Min(5, s.OnHillAzure)]);
            HudText.Set(_onHillC, OnHillA[Math.Min(5, s.OnHillCrimson)]);
            for (int i = 0; i < Pips; i++)
            {
                _pipsA[i].SetFill(i < s.OnHillAzure ? CMTokens.Azure.Color : Empty);
                _pipsC[i].SetFill(i < s.OnHillCrimson ? CMTokens.Crimson.Color : Empty);
            }
            bool anyHeld = s.ScoreAzure + s.ScoreCrimson > 0;
            _hillBar.SetFill(anyHeld ? CMTokens.Azure.Color : CMTokens.Stone.Color);
            _hillBar.SetEmpty(anyHeld ? CMTokens.Crimson.Color : CMTokens.Stone.Color);
            _hillBar.Fill01 = s.HillShareAzure;
            _medallionHalf.gameObject.SetActive(s.Hill == HillState.Contested);
            _medallion.SetFill(HillColour(s.Hill));
            HudText.Set(_hillWord, HillWord(s.Hill));
        }

        static Color HillColour(HillState h) =>
            h == HillState.Azure ? CMTokens.Azure.Color : h == HillState.Crimson ? CMTokens.Crimson.Color :
            h == HillState.Contested ? CMTokens.Azure.Color : CMTokens.Stone.Color;

        static string HillWord(HillState h) =>
            h == HillState.Azure ? "AZURE HOLDS" : h == HillState.Crimson ? "CRIMSON HOLDS" : h == HillState.Contested ? "CONTESTED" : "NEUTRAL";

        static readonly string[] HatchWords = BuildHatchWords();

        static string[] BuildHatchWords()
        {
            var a = new string[601];
            for (int i = 0; i < a.Length; i++) a[i] = "HATCH " + (i / 60).ToString("0") + ":" + (i % 60).ToString("00");
            return a;
        }

        void ApplyKaiju(HudState s)
        {
            bool egg = !s.Hatched || s.Kaiju == KaijuState.Egg;
            Color state = KaijuColour(s.Kaiju);
            _eggFrame.SetRim(egg ? CMTokens.EggSeam.Color : state);
            _kaijuHead.Set(KaijuIcon(s, egg), _tier);
            int lit = Mathf.CeilToInt(s.KaijuMeter01 * MeterSegments - 1e-4f);
            Color litColour = s.Kaiju == KaijuState.Berserk ? CMTokens.Corruption.Color : CMTokens.MeterOrange.Color;
            for (int i = 0; i < MeterSegments; i++) _segments[i].SetFill(i < lit ? litColour : Empty);
            HudText.Set(_kaijuNumber, NumberCache.Int(Mathf.RoundToInt(s.KaijuMeter01 * 100f)));
            int rage = Mathf.FloorToInt(s.Instability01 * RagePips + 1e-4f);
            for (int i = 0; i < RagePips; i++) _rage[i].SetFill(i < rage ? CMTokens.Meat.Color : Empty);
            string word = egg ? HatchWords[Math.Min(600, (int)Math.Ceiling(s.UntilHatch - 1e-6))] :
                s.Kaiju == KaijuState.Azure ? "AZURE" : s.Kaiju == KaijuState.Crimson ? "CRIMSON" :
                s.Kaiju == KaijuState.Berserk ? "BERSERK" : "NEUTRAL";
            HudText.Set(_kaijuWord, word);
            _kaijuWord.color = egg || s.Kaiju == KaijuState.Neutral ? Cream : state;
        }

        /// <summary>Egg before the hatch; the wild (neutral) Kaiju is the hatchling of bible p10; once a team turns it
        /// (or it goes berserk) the adult head in that state's colours (p10 azure / crimson / berserk adult).</summary>
        static string KaijuIcon(HudState s, bool egg) =>
            egg ? UiIcons.Egg : s.Kaiju == KaijuState.Azure ? UiIcons.Adult[1] : s.Kaiju == KaijuState.Crimson ? UiIcons.Adult[2] :
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
            _portrait.Set(UiIcons.Portrait[cls, team], _tier);
            _classBadge.Set(UiIcons.ClassBadge[cls, team], _tier);
            HudText.Set(_className, ClassNames[cls]);

            // health: shown value drains toward the target, the cream ghost holds 0.4 s then follows
            float target = s.Alive ? s.Hp01 : 0f;
            if (_hpShown < 0) _hpShown = _hpGhostShown = target;
            if (target < _hpGhostShown - 1e-4f && _ghostHold <= 0 && _hpShown - target > 1e-4f) _ghostHold = 0.4f;
            _hpShown = Approach(_hpShown, target, dt * 2.5f);
            _ghostHold -= dt;
            if (_ghostHold <= 0) _hpGhostShown = Approach(_hpGhostShown, _hpShown, dt * 0.8f);
            if (_hpGhostShown < _hpShown) _hpGhostShown = _hpShown;
            _hpBar.SetFill(tc);
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
            ApplyButton(_buttons[0], s.AttackCd01, 0f, UiIcons.Attack[cls, team], "ATTACK", dt);
            ApplyButton(_buttons[1], s.AbilityCd01, s.AbilityCdSeconds, UiIcons.Ability[cls, team], AbilityNames[cls], dt);
            AbilityButton carry = _buttons[2];
            carry.Icon.Set(s.Carry == CarryKind.Corruption ? UiIcons.Corruption : s.Carry == CarryKind.Biomass ? UiIcons.Biomass :
                UiIcons.CarryEmpty, _tier);
            HudText.Set(carry.Count, s.CarryAmount > 0 ? NumberCache.Int(s.CarryAmount) : "");
            HudText.Set(carry.Name, s.Carry == CarryKind.Corruption ? "CORRUPTION" : s.Carry == CarryKind.Biomass ? "BIOMASS" : "CARRY");
        }

        void ApplyButton(AbilityButton b, float cd01, float seconds, string icon, string name, float dt)
        {
            bool cooling = cd01 > 0.0005f;
            b.Sweep.Fill01 = cd01;
            b.Icon.Set(icon, _tier);
            HudText.Set(b.Seconds, cooling && seconds >= 1f ? NumberCache.Int(Mathf.CeilToInt(seconds)) : "");
            HudText.Set(b.Name, name);
            if (b.WasCooling && !cooling) b.Flash = 0.3f;
            b.WasCooling = cooling;
            b.Flash = Mathf.Max(0, b.Flash - dt);
            Color rim = cooling ? Cream : CMTokens.ToolBrass.Color;   // p05: Tool Brass = ready ability
            b.Body.SetRim(rim);
            float k = 1f + 0.1f * (b.Flash / 0.3f);
            b.Rt.localScale = new Vector3(k, k, 1f);
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
                if (on && w.Actors[i].Team != mine) on = Seen(w, i, mine, n);
                VectorShape d = _dots[i];
                if (d.gameObject.activeSelf != on) d.gameObject.SetActive(on);
                if (!on) continue;
                PlaceOnMap(d.rectTransform, w.X[i], w.Z[i]);
                d.SetFill(CMTokens.Team(w.Actors[i].Team));
            }
        }

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
                ((RectTransform)_hillMarker.transform.parent).anchoredPosition = hp / scale;
                _hillMarker.SetFill(HillColour(s.Hill));
                HudText.Set(_hillMarkerWord, s.Hill == HillState.Contested ? "CONTESTED" : s.Hill == HillState.Neutral ? "NEUTRAL" :
                    s.Hill == HillState.Azure ? "AZURE" : "CRIMSON");
            }
            Pointer(0, cam, hillWorld, hillOn, s, hx, hz, scale, sw, sh);
            Vector3 kWorld = SimSpace.ToWorld(w.Kaiju.X, GroundHeight.At(w.Map, w.Kaiju.X, w.Kaiju.Z) + 2.0, w.Kaiju.Z);
            bool kOn = Project(cam, kWorld, out _);
            Pointer(1, cam, kWorld, kOn, s, w.Kaiju.X, w.Kaiju.Z, scale, sw, sh);

            // events: damage numbers (local player's hits only) and the last-hit time for overhead hearts
            if (_lastHit.Length < w.ActorCount) _lastHit = new double[w.ActorCount];
            if (_eventCursor < 0 || _eventCursor > w.EventHead) _eventCursor = w.EventHead;
            if (w.EventHead - _eventCursor > World.EventCapacity) _eventCursor = w.EventHead - World.EventCapacity;
            for (; _eventCursor < w.EventHead; _eventCursor++)
            {
                SimEvent e = w.Events[_eventCursor % World.EventCapacity];
                if (e.Type != SimEventType.Hit) continue;
                if (e.A >= 0 && e.A < _lastHit.Length) _lastHit[e.A] = w.Match.Time;
                if (e.B == HudBinder.LocalActor && e.A != HudBinder.LocalActor)
                {
                    int k = _dmgNext;
                    _dmgNext = (_dmgNext + 1) % DamageNumbers;
                    _dmgAge[k] = 0f;
                    _dmgPos[k] = SimSpace.ToWorld(e.X, GroundHeight.At(w.Map, e.X, e.Z) + 2.2, e.Z);
                    HudText.Set(_dmg[k], NumberCache.Int(Mathf.RoundToInt((float)e.Value)));
                    _dmg[k].gameObject.SetActive(true);
                }
            }
            for (int k = 0; k < DamageNumbers; k++)
            {
                if (!_dmg[k].gameObject.activeSelf) continue;
                _dmgAge[k] += dt;
                if (_dmgAge[k] >= 0.7f && !SnapAnimations) { _dmg[k].gameObject.SetActive(false); continue; }
                float t = SnapAnimations ? 0.25f : _dmgAge[k] / 0.7f;
                if (!Project(cam, _dmgPos[k] + Vector3.up * (0.6f * t), out Vector2 dp)) { _dmg[k].gameObject.SetActive(false); continue; }
                _dmg[k].rectTransform.anchoredPosition = dp / scale;
                Color c = _dmg[k].color;
                c.a = 1f - t * t;
                _dmg[k].color = c;
            }

            // overhead hearts: other units, 3 s after damage, never at full health
            int row = 0;
            for (int i = 0; i < w.ActorCount && row < HeartRows; i++)
            {
                Actor a = w.Actors[i];
                if (i == HudBinder.LocalActor || !a.Alive || a.Hp >= a.MaxHp || w.Match.Time - _lastHit[i] > 3.0 || _lastHit[i] <= 0) continue;
                Vector3 head = SimSpace.ToWorld(w.X[i], GroundHeight.At(w.Map, w.X[i], w.Z[i]) + 2.8, w.Z[i]);
                if (!Project(cam, head, out Vector2 p)) continue;
                RectTransform r = _heartRows[row];
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                r.anchoredPosition = p / scale;
                float hp01 = (float)(a.Hp / a.MaxHp);
                Color tc = CMTokens.Team(a.Team);
                for (int h = 0; h < Hearts; h++)
                {
                    _rowHearts[row, h].SetFill(tc);
                    _rowHearts[row, h].Fill01 = Mathf.Clamp01(hp01 * Hearts - h);
                }
                row++;
            }
            for (; row < HeartRows; row++) if (_heartRows[row].gameObject.activeSelf) _heartRows[row].gameObject.SetActive(false);
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
            bool dead = s.HasPlayer && !s.Alive && !s.Ended;
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
