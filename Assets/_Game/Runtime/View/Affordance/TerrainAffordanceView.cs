using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>
    /// feat/terrain-affordances (Jeisil: "the player could easily see what they can use: mine, farm, build or place"; owner:
    /// "the object is supposed to indicate it ... how much it's breaking"). For the LOCAL player only (actor 0, never a
    /// bot-driven actor 0, never in spectator mode) and only for his terrain class (Miner, Builder, Farmer, Demolitions):
    /// <list type="bullet">
    /// <item>quiet markers on every cell or object the class can act on within AffordanceScan.Radius, brighter in reach,
    /// fading with distance, capped (MarkerCapCrowd), per-action colour and icon (pick, sprout, hammer, block, blast);</item>
    /// <item>the single target the action would hit RIGHT NOW (the Sim's own picker for his position and aim) in a strong
    /// outline, with a prompt naming the bound key or button and the action, and the reason in two or three words when the
    /// Sim would refuse it; a placement ghost (block, barricade, ladder or bridge cells, mine, TNT blast and crater),
    /// green when valid and red when not;</item>
    /// <item>a progress bar on the object he is working on or hit last: health or progress, the ghost segment one more hit
    /// takes, the exact hits or seconds left; his team's barricades under enemy attack get a smaller bar;</item>
    /// <item>world hit reactions for everyone (WorldReactions): shake and squash, flash, chips in the material colour,
    /// persistent crack stages, break and build bursts;</item>
    /// <item>a first-minute hint per class for the first two matches (setting), and hold ShowTerrain to see everything
    /// usable in a wider radius.</item>
    /// </list>
    /// Drawing: ONE dynamic mesh (MaxQuads quads, sized once), ONE material (CM/Affordance, a procedural atlas): one draw
    /// call, at most 2 x MaxQuads triangles; no per-frame allocation (prompt text is rebuilt only when it changes).
    /// Read-only on the Sim. Every element obeys its WorldFeedbackSettings switch; shake obeys Reduce motion too.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 56, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]
    public sealed class TerrainAffordanceView : MonoBehaviour
    {
        public const int MaxQuads = 320;
        /// <summary>Markers drawn at most in normal play (a 32-player fight stays readable) and while the show key is held.</summary>
        public const int MarkerCapCrowd = 40, MarkerCapAll = AffordanceScan.MaxMarkers;
        public const string ShaderResource = "Affordance/CMAffordance";
        public const string ShowAction = "ShowTerrain";
        public const int LocalActor = HudBinder.LocalActor;
        /// <summary>Marker opacity at rest (out of reach) and in reach.</summary>
        public const float QuietAlpha = 0.26f, ReachAlpha = 0.78f;
        /// <summary>Ground lift of markers (under the unit rings at 0.075 u, over the floor).</summary>
        public const float GroundLift = 0.045f;
        /// <summary>Hint lifetime: the first minute of match time.</summary>
        public const double HintSeconds = 60;

        // Atlas cells (4 x 3, 64 px each): see BakeAtlas.
        public const int CellTile = 0, CellOutline = 1, CellPick = 2, CellSprout = 3, CellHammer = 4, CellBlock = 5, CellBlast = 6,
            CellDisc = 7, CellCrack1 = 8, CellCrack2 = 9, CellCrack3 = 10, CellRing = 11;

        public static TerrainAffordanceView Instance { get; private set; }
        /// <summary>-affordanceShots captures: show for actor 0 even while the autopilot drives it.</summary>
        public static bool CaptureForce;
        /// <summary>Captures and tests: behave as if the show-all key were held.</summary>
        public static bool ShowAllForced;

        public readonly AffordanceScan Scan = new AffordanceScan();
        public readonly WorldReactions Reactions = new WorldReactions();
        public readonly ProgressFocus Focus = new ProgressFocus();

        /// <summary>The affordances (markers, target, prompt) are on for the local player this frame.</summary>
        public bool Showing { get; private set; }
        public bool ShowAllHeld { get; private set; }
        public int MarkersDrawn { get; private set; }
        public int QuadsDrawn { get; private set; }
        public int DecalsDrawn { get; private set; }
        public bool OutlineDrawn { get; private set; }
        public int GhostCellsDrawn { get; private set; }
        public bool GhostValid { get; private set; }
        /// <summary>The prompt text now ("" = hidden), the hint now, and the bar's value (-1 = hidden).</summary>
        public string PromptText => _promptShown ? _prompt : string.Empty;
        public string HintText => _hintShown ? _hint : string.Empty;
        public float BarValue => _barShown ? (float)Focus.Value01 : -1f;
        public float BarNext => _barShown ? (float)Focus.Next01 : -1f;
        public string BarLabel => _barShown ? _barText : string.Empty;
        public Mesh Mesh => _mesh;
        public Material Material => _material;

        SimDriverBehaviour _driver;
        HumanInputSource _human;
        GameHud _hud;
        InputAction _show;
        Mesh _mesh;
        Material _material;
        Texture2D _atlas;
        GameObject _drawObject;
        Vector3[] _v;
        Vector2[] _uv;
        Color32[] _c;
        int _q;
        int _lastQuads = MaxQuads;

        Canvas _canvas;
        RectTransform _root, _chip, _hintChip, _bar, _barFill, _barGhost;
        Text _label, _hintLabel, _barLabel;
        Image _fill, _ghost;
        readonly RectTransform[] _mini = new RectTransform[3];
        readonly Image[] _miniFill = new Image[3];
        string _prompt = string.Empty, _hint = string.Empty, _barText = string.Empty;
        bool _promptShown, _hintShown, _barShown;
        int _promptKey = int.MinValue, _hintKey = int.MinValue, _barKey = int.MinValue;
        int _hintEpoch = int.MinValue;
        bool _hintThisMatch;
        int _bindingsVersion;

        void OnEnable()
        {
            Instance = this;
            ControlBindings.BindingsChanged += OnBindings;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            ControlBindings.BindingsChanged -= OnBindings;
        }

        void OnBindings() => _bindingsVersion++;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            _human = FindAnyObjectByType<HumanInputSource>();
            _hud = GetComponent<GameHud>();
            if (_hud == null) _hud = FindAnyObjectByType<GameHud>();
            BuildMesh();
            BuildUi();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-affordanceShots") >= 0 && GetComponent<AffordanceCapture>() == null)
                gameObject.AddComponent<AffordanceCapture>();
        }

        void OnDestroy()
        {
            Release(_drawObject);
            Release(_mesh);
            Release(_material);
            Release(_atlas);
        }

        static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // ------------------------------------------------------------------ build

        void BuildMesh()
        {
            _v = new Vector3[MaxQuads * 4];
            _uv = new Vector2[MaxQuads * 4];
            _c = new Color32[MaxQuads * 4];
            var idx = new int[MaxQuads * 6];
            for (int i = 0; i < MaxQuads; i++)
            {
                int v = i * 4, t = i * 6;
                idx[t] = v; idx[t + 1] = v + 1; idx[t + 2] = v + 2;
                idx[t + 3] = v; idx[t + 4] = v + 2; idx[t + 5] = v + 3;
            }
            _mesh = new Mesh { name = "Terrain Affordances" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(4000f, 400f, 4000f));
            _atlas = BakeAtlas();
            Shader shader = UnityEngine.Resources.Load<Shader>(ShaderResource);
            if (shader != null)
            {
                _material = new Material(shader) { name = "CM Affordance" };
                _material.SetTexture("_MainTex", _atlas);
            }
            _drawObject = new GameObject("Terrain Affordances");
            _drawObject.transform.SetParent(transform, false);
            _drawObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _drawObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
        }

        void BuildUi()
        {
            var go = new GameObject("Terrain Affordance Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            _root = (RectTransform)go.transform;

            _chip = Chip(_root, "Target Prompt", 560f, 40f, out _label, UiScale.TypeBody);
            _hintChip = Chip(_root, "Class Hint", 980f, 44f, out _hintLabel, UiScale.TypeSmall);
            _hintChip.anchorMin = _hintChip.anchorMax = new Vector2(0.5f, 1f);
            _hintChip.pivot = new Vector2(0.5f, 1f);
            _hintChip.anchoredPosition = new Vector2(0f, -196f);

            _bar = MenuUi.Rt(_root, "Progress Bar");
            _bar.anchorMin = _bar.anchorMax = Vector2.zero;
            _bar.pivot = new Vector2(0.5f, 0f);
            _bar.sizeDelta = new Vector2(132f, 14f);
            Image bg = _bar.gameObject.AddComponent<Image>();
            bg.color = new Color(0.094f, 0.137f, 0.231f, 0.9f);
            bg.raycastTarget = false;
            _ghost = Bar(_bar, "Ghost", new Color(1f, 1f, 1f, 0.55f), out _barGhost);
            _fill = Bar(_bar, "Fill", Color.white, out _barFill);
            _barLabel = MenuUi.Label(_bar, "Count", CMTokens.Display, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 0f, 0f, 160f, 26f, "", 3f);
            RectTransform lr = _barLabel.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(1f, 0.5f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.anchoredPosition = new Vector2(8f, 0f);
            _bar.gameObject.SetActive(false);
            for (int k = 0; k < _mini.Length; k++)
            {
                RectTransform m = MenuUi.Rt(_root, "Under Attack " + k);
                m.anchorMin = m.anchorMax = Vector2.zero;
                m.pivot = new Vector2(0.5f, 0f);
                m.sizeDelta = new Vector2(72f, 8f);
                Image mb = m.gameObject.AddComponent<Image>();
                mb.color = new Color(0.094f, 0.137f, 0.231f, 0.7f);
                mb.raycastTarget = false;
                _miniFill[k] = Bar(m, "Fill", Color.white, out _);
                _mini[k] = m;
                m.gameObject.SetActive(false);
            }
            _chip.gameObject.SetActive(false);
            _hintChip.gameObject.SetActive(false);
        }

        static RectTransform Chip(RectTransform root, string name, float w, float h, out Text label, int size)
        {
            RectTransform chip = MenuUi.Rt(root, name);
            chip.anchorMin = chip.anchorMax = Vector2.zero;
            chip.pivot = new Vector2(0.5f, 0f);
            chip.sizeDelta = new Vector2(w, h);
            VectorShape body = MenuUi.Shape(chip, "Body", ShapeKind.RoundRect, 0f, 0f, w, h, CMTokens.PanelNavy.Color, -1f);
            MenuUi.Stretch(body.rectTransform);
            body.OutlinePx = 3f;
            body.RimPx = 2f;
            label = MenuUi.Fit(MenuUi.Label(chip, "Line", CMTokens.Display, size, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 10f, 0f, w - 20f, h, "", 3f), 14);
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(14f, 4f);
            lr.offsetMax = new Vector2(-14f, -4f);
            return chip;
        }

        static Image Bar(RectTransform parent, string name, Color c, out RectTransform rt)
        {
            rt = MenuUi.Rt(parent, name);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(2f, 2f);
            rt.offsetMax = new Vector2(-2f, -2f);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            World w = _driver != null ? _driver.World : null;
            _q = 0;
            MarkersDrawn = 0;
            DecalsDrawn = 0;
            OutlineDrawn = false;
            GhostCellsDrawn = 0;
            GhostValid = false;
            bool hudOn = _hud == null || (_hud.Canvas != null && _hud.Canvas.enabled);
            if (_canvas != null && _canvas.enabled != hudOn) _canvas.enabled = hudOn;
            if (_canvas != null)
            {
                float sc = HudLayout.Scale(Screen.width, Screen.height);
                if (sc > 0f && Mathf.Abs(_canvas.scaleFactor - sc) > 1e-4f) _canvas.scaleFactor = sc;
            }
            double now = Time.unscaledTimeAsDouble;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (w == null || w.Map == null || CriticalMass.Runtime.Flow.GameFlow.MenuIdle)
            {
                Showing = false;
                HideUi();
                Upload();
                return;
            }
            Reactions.LocalActor = LocalActor;
            Reactions.Read(w, now);

            Actor me = w.ActorCount > LocalActor ? w.Actors[LocalActor] : null;
            bool local = me != null && me.Alive && w.Match.Phase == MatchPhase.Playing && hudOn
                         && (!_driver.HumanAutopilot || CaptureForce) && !CriticalMass.Runtime.Spectator.SpectatorMode.IsSpectating;
            bool terrain = local && AffordanceScan.IsTerrainClass(me.Cls);
            if (_show == null && InputSystem.actions != null)
            {
                InputActionMap map = InputSystem.actions.FindActionMap(ControlBindings.MapName, false);
                if (map != null) _show = map.FindAction(ShowAction, false);
            }
            ShowAllHeld = ShowAllForced || (_show != null && _show.IsPressed() && !CriticalMass.Runtime.Hud.Mechanics.StationPanel.IsOpen);
            Showing = terrain;

            Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;
            Vector3 camUp = cam != null ? cam.transform.up : Vector3.up;
            Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;

            // Order = priority under the quad cap: the target and ghost, the reactions, the damage stages, the markers.
            if (terrain)
            {
                AimOf(w, out bool hasAim, out double ax, out double az);
                Scan.Scan(w, LocalActor, hasAim, ax, az, ShowAllHeld);
                if (WorldFeedbackSettings.Outline) { DrawTarget(w, Scan.Primary, true); DrawTarget(w, Scan.Secondary, false); }
            }
            DrawReactions(w, now, camRight, camUp, camFwd);
            DrawStages(w, camRight, camUp, camFwd);
            if (terrain && (WorldFeedbackSettings.Markers || ShowAllHeld)) DrawMarkers(w);
            Upload();

            if (local) Focus.Update(w, LocalActor, Reactions, now);
            UpdatePrompt(w, cam, terrain);
            UpdateHint(w, terrain);
            UpdateBar(w, cam, local);
            UpdateMiniBars(w, cam, local, now);
        }

        void AimOf(World w, out bool hasAim, out double ax, out double az)
        {
            if (_human != null && !_driver.HumanAutopilot)
            {
                HumanInputState s = _human.State;
                hasAim = s.HasAim;
                ax = s.AimX;
                az = s.AimZ;
                return;
            }
            hasAim = w.HasAim[LocalActor] == 1;
            ax = w.Inputs[LocalActor].AimX;
            az = w.Inputs[LocalActor].AimZ;
        }

        void HideUi()
        {
            if (_chip != null && _chip.gameObject.activeSelf) _chip.gameObject.SetActive(false);
            if (_hintChip != null && _hintChip.gameObject.activeSelf) _hintChip.gameObject.SetActive(false);
            if (_bar != null && _bar.gameObject.activeSelf) _bar.gameObject.SetActive(false);
            for (int k = 0; k < _mini.Length; k++) if (_mini[k] != null && _mini[k].gameObject.activeSelf) _mini[k].gameObject.SetActive(false);
            _promptShown = _hintShown = _barShown = false;
        }

        // ------------------------------------------------------------------ colours

        public static Color32 ActionColour(AffAction a)
        {
            switch (AffordanceScan.IconOf(a))
            {
                case AffIcon.Pick: return new Color32(0xFF, 0xD4, 0x47, 0xFF);    // pick: sun yellow
                case AffIcon.Sprout: return new Color32(0x7B, 0xE0, 0x8D, 0xFF);  // farm: leaf green
                case AffIcon.Hammer: return new Color32(0xC3, 0xA6, 0xFF, 0xFF);  // build: lilac (clear of both team hues)
                case AffIcon.Block: return new Color32(0xF5, 0xEB, 0xDC, 0xFF);   // place: warm cream
                default: return new Color32(0xFF, 0x8A, 0x3D, 0xFF);              // blast: orange
            }
        }

        public static readonly Color32 ValidGreen = new Color32(0x5C, 0xE6, 0x6E, 0xFF);
        public static readonly Color32 RefusedRed = new Color32(0xF0, 0x3A, 0x3A, 0xFF);

        public static Color32 MaterialColour(ReactKind k)
        {
            switch (k)
            {
                case ReactKind.Wall: return new Color32(0xB8, 0xA4, 0x84, 0xFF);      // sandstone
                case ReactKind.Barricade: return new Color32(0xB0, 0x7A, 0x4A, 0xFF); // timber
                case ReactKind.Pillar: return new Color32(0xD2, 0xC8, 0xB8, 0xFF);    // carved stone
                case ReactKind.Ground: return new Color32(0x8A, 0x6A, 0x4A, 0xFF);    // soil
                case ReactKind.Block: return new Color32(0xEA, 0xDF, 0xC8, 0xFF);     // packed earth block
                case ReactKind.Piece: return new Color32(0xC0, 0x8A, 0x52, 0xFF);     // rope and plank
                case ReactKind.Node: return new Color32(0x7B, 0xE0, 0x8D, 0xFF);      // leaves and water
                default: return new Color32(0xC0, 0x8A, 0x52, 0xFF);
            }
        }

        static int IconCell(AffAction a) => CellPick + (int)AffordanceScan.IconOf(a);

        // ------------------------------------------------------------------ quads

        static Vector2 UvMin(int cell) => new Vector2((cell % 4) / 4f, (cell / 4) / 3f);

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int cell, Color32 col)
        {
            if (_q >= MaxQuads) return;
            int v = _q * 4;
            _v[v] = a; _v[v + 1] = b; _v[v + 2] = c; _v[v + 3] = d;
            Vector2 m = UvMin(cell);
            const float du = 0.25f - 0.002f, dv = 1f / 3f - 0.002f;
            m.x += 0.001f;
            m.y += 0.001f;
            _uv[v] = m;
            _uv[v + 1] = new Vector2(m.x, m.y + dv);
            _uv[v + 2] = new Vector2(m.x + du, m.y + dv);
            _uv[v + 3] = new Vector2(m.x + du, m.y);
            _c[v] = _c[v + 1] = _c[v + 2] = _c[v + 3] = col;
            _q++;
        }

        /// <summary>A flat quad on the ground centred at sim (x, z), half size h (u), at height y.</summary>
        void GroundQuad(double x, double z, float y, float h, int cell, Color32 col)
        {
            Vector3 c = SimSpace.ToWorld(x, y, z);
            Quad(new Vector3(c.x - h, c.y, c.z - h), new Vector3(c.x - h, c.y, c.z + h), new Vector3(c.x + h, c.y, c.z + h), new Vector3(c.x + h, c.y, c.z - h), cell, col);
        }

        /// <summary>A camera-facing quad centred at p, half size h.</summary>
        void Billboard(Vector3 p, float h, Vector3 right, Vector3 up, int cell, Color32 col)
        {
            Vector3 r = right * h, u = up * h;
            Quad(p - r - u, p - r + u, p + r + u, p + r - u, cell, col);
        }

        static Color32 A(Color32 c, float alpha) { c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255); return c; }

        void Upload()
        {
            if (_mesh == null) return;
            QuadsDrawn = _q;
            int used = _q * 4, clear = Math.Max(used, _lastQuads * 4);
            for (int i = used; i < clear; i++) _v[i] = Vector3.zero;
            _lastQuads = _q;
            const MeshUpdateFlags f = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.SetVertices(_v, 0, _v.Length, f);
            _mesh.SetUVs(0, _uv, 0, _uv.Length, f);
            _mesh.SetColors(_c, 0, _c.Length, f);
        }

        float CellY(MapDef m, double x, double z) => (float)GroundHeight.At(m, x, z) + GroundLift;

        // ------------------------------------------------------------------ markers and target

        void DrawMarkers(World w)
        {
            MapDef m = w.Map;
            int cap = ShowAllHeld ? MarkerCapAll : MarkerCapCrowd;
            double r = ShowAllHeld ? AffordanceScan.RadiusAll : AffordanceScan.Radius;
            double ox = w.X[LocalActor], oz = w.Z[LocalActor];
            float meY = CellY(m, ox, oz);
            for (int k = 0; k < Scan.MarkerCount && MarkersDrawn < cap; k++)
            {
                AffMarker mk = Scan.Markers[k];
                double d = Math.Sqrt((mk.X - ox) * (mk.X - ox) + (mk.Z - oz) * (mk.Z - oz));
                float fade = 1f - Mathf.Clamp01((float)((d - r * 0.6) / (r * 0.4 + 1e-6)));
                float alpha = (mk.InReach ? ReachAlpha : QuietAlpha) * (ShowAllHeld ? Mathf.Max(fade, 0.6f) : fade);
                if (alpha < 0.03f) continue;
                Color32 col = ActionColour(mk.Action);
                bool cell = mk.Cx >= 0;
                float y = mk.Action == AffAction.PlaceBlock || mk.Action == AffAction.Build ? meY : CellY(m, mk.X, mk.Z);
                if (mk.Action == AffAction.BreakWall || (mk.Action == AffAction.Tnt && cell && IsWall(m, mk.Cx, mk.Cz))) y += WallTop(m, mk.Cx, mk.Cz);
                float half = cell ? 0.46f : 0.7f;
                GroundQuad(mk.X, mk.Z, y, half, CellTile, A(col, alpha));
                GroundQuad(mk.X, mk.Z, y + 0.005f, cell ? 0.26f : 0.36f, IconCell(mk.Action), A(col, Mathf.Min(1f, alpha + 0.15f)));
                MarkersDrawn++;
            }
        }

        static bool IsWall(MapDef m, int cx, int cz)
        {
            if (!MapGrid.InBounds(m, cx, cz)) return false;
            byte t = m.Cells[cz * m.Width + cx];
            return t == Cell.BreakableIntact || t == Cell.BreakableCracked || t == Cell.Wall;
        }

        /// <summary>Height of a wall cell's top above its ground (GridChunkMeshView.CellBox).</summary>
        static float WallTop(MapDef m, int cx, int cz)
        {
            if (!MapGrid.InBounds(m, cx, cz)) return 0f;
            byte t = m.Cells[cz * m.Width + cx];
            return t == Cell.BreakableCracked ? 0.77f : t == Cell.BreakableIntact || t == Cell.Wall ? 1.52f : 0f;
        }

        void DrawTarget(World w, AffTarget t, bool primary)
        {
            if (!t.Has) return;
            MapDef m = w.Map;
            float meY = CellY(m, w.X[LocalActor], w.Z[LocalActor]);
            Color32 tint = t.Valid ? ActionColour(t.Action) : RefusedRed;
            bool point = t.Cx < 0 || t.Action == AffAction.Gather || t.Action == AffAction.GatherSource || t.Action == AffAction.Deliver
                         || t.Action == AffAction.Bank || t.Action == AffAction.Carry || t.Action == AffAction.Repair;
            if (primary || t.Ghost == 0 || point)
            {
                if (point) GroundQuad(t.X, t.Z, CellY(m, t.X, t.Z) + 0.01f, 0.95f, CellRing, A(tint, primary ? 1f : 0.7f));
                else if (t.Ghost == 0 || t.Ghost == 5)
                {
                    float y = CellY(m, t.X, t.Z) + (IsWall(m, t.Cx, t.Cz) ? WallTop(m, t.Cx, t.Cz) : 0f) + 0.01f;
                    float pulse = primary ? 0.82f + 0.18f * Mathf.Sin((float)(Time.unscaledTimeAsDouble * 6.0)) : 0.7f;
                    GroundQuad(t.Cx + 0.5, t.Cz + 0.5, y, 0.5f, CellDisc, A(tint, primary ? 0.32f : 0.18f));
                    GroundQuad(t.Cx + 0.5, t.Cz + 0.5, y + 0.004f, 0.64f, CellOutline, A(tint, pulse));
                }
                if (primary) OutlineDrawn = true;
            }
            if (t.Ghost == 0) return;
            Color32 g = t.Valid ? ValidGreen : RefusedRed;
            if (primary) GhostValid = t.Valid;
            if (t.Ghost == 5)
            {
                float y = CellY(m, t.X, t.Z) + 0.02f;
                GroundQuad(t.X, t.Z, y, (float)t.GhostRadius, CellRing, A(t.Valid ? ActionColour(AffAction.Tnt) : RefusedRed, 0.85f));
                GroundQuad(t.X, t.Z, y, (float)t.GhostRadius, CellDisc, A(t.Valid ? ActionColour(AffAction.Tnt) : RefusedRed, 0.14f));
                for (int k = 0; k < t.GhostCount; k++)
                {
                    int i = t.GhostCells[k];
                    GroundQuad(i % m.Width + 0.5, i / m.Width + 0.5, CellY(m, i % m.Width + 0.5, i / m.Width + 0.5) + 0.015f, 0.42f, CellCrack3, A(ActionColour(AffAction.Tnt), 0.7f));
                }
                if (primary) GhostCellsDrawn = t.GhostCount;
                return;
            }
            for (int k = 0; k < t.GhostCount; k++)
            {
                int i = t.GhostCells[k];
                double x = i % m.Width + 0.5, z = i / m.Width + 0.5;
                float y = t.Ghost == 4 || t.Ghost == 2 ? CellY(m, x, z) + 0.02f : meY + 0.02f;
                int cell = t.Ghost == 4 ? CellDisc : t.Ghost == 3 ? CellHammer : CellBlock;
                GroundQuad(x, z, y, 0.5f, CellTile, A(g, 0.6f));
                GroundQuad(x, z, y + 0.005f, t.Ghost == 4 ? 0.18f : 0.32f, cell, A(g, 0.95f));
                if (t.Ghost == 4) GroundQuad(x, z, y + 0.006f, (float)w.Config.Abilities.DemolitionsMine.BlastRadius, CellRing, A(g, 0.5f));
            }
            if (primary || GhostCellsDrawn == 0) GhostCellsDrawn = Math.Max(GhostCellsDrawn, t.GhostCount);
        }

        // ------------------------------------------------------------------ reactions and damage stages

        static float Hash01(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= h >> 13;
                h *= 1274126177u;
                return (h & 0xFFFF) / 65535f;
            }
        }

        float ObjectY(MapDef m, ReactKind k, double x, double z)
        {
            float g = (float)GroundHeight.At(m, x, z);
            switch (k)
            {
                case ReactKind.Wall: return g + 0.9f;
                case ReactKind.Pillar: return g + 1.3f;
                case ReactKind.Barricade: return g + 0.6f;
                case ReactKind.Bridge:
                case ReactKind.Piece: return g + 0.15f;
                case ReactKind.Node: return g + 0.6f;
                default: return g + 0.1f;
            }
        }

        void DrawReactions(World w, double now, Vector3 right, Vector3 up, Vector3 fwd)
        {
            bool particles = WorldFeedbackSettings.Particles;
            bool shake = WorldFeedbackSettings.Shake && !CriticalMass.Runtime.View.Effects.FeelGate.ReduceMotion;
            bool dimFlash = CriticalMass.Runtime.View.Effects.FeelGate.ReduceFlashing;
            MapDef m = w.Map;
            ReactPulse[] ps = Reactions.Pulses;
            for (int k = 0; k < ps.Length; k++)
            {
                ReactPulse p = ps[k];
                double life = WorldReactions.Life(p.Phase);
                double age = now - p.T0;
                if (!(age >= 0 && age < life)) continue;
                float u = (float)(age / life);
                Color32 mat = MaterialColour(p.Kind);
                Vector3 c = SimSpace.ToWorld(p.X, ObjectY(m, p.Kind, p.X, p.Z), p.Z) - fwd * 0.55f;
                float s = (float)p.Strength;
                // Shake and squash: a direction-aware jolt of the object's shell (decays in about a fifth of a second).
                if (shake && p.Phase == 0)
                {
                    float jolt = Mathf.Sin((float)age * 70f) * Mathf.Exp(-(float)age * 14f) * 0.14f * (0.5f + s);
                    Vector3 dir = SimSpace.ToWorld(p.DirX, 0, p.DirZ);
                    float squash = 1f + 0.18f * s * Mathf.Exp(-(float)age * 16f);
                    Billboard(c + dir * jolt, 0.62f * squash, right * squash, up / squash, CellOutline, A(mat, 0.75f * (1f - u)));
                }
                if (!particles) continue;
                // Flash: a short white disc (dimmer under Reduce flashing).
                if (u < 0.3f && p.Phase != 2)
                {
                    float fa = (1f - u / 0.3f) * (dimFlash ? 0.25f : 0.6f) * (0.5f + 0.5f * s);
                    Billboard(c, 0.45f + 0.35f * s, right, up, CellDisc, A(new Color32(255, 250, 240, 255), fa));
                }
                // Chips (hit), pieces and dust (break), settling pieces and a rising ring (build).
                int n = p.Phase == 0 ? 5 : p.Phase == 1 ? 10 : 6;
                for (int j = 0; j < n; j++)
                {
                    float h1 = Hash01(p.Seed + j * 31), h2 = Hash01(p.Seed + j * 57 + 7), h3 = Hash01(p.Seed + j * 89 + 3);
                    float t = (float)age;
                    Vector3 pos;
                    if (p.Phase == 2)
                    {
                        // Build: pieces drop in from above and settle.
                        float spread = (float)p.Size;
                        Vector3 baseP = SimSpace.ToWorld(p.X + (h1 - 0.5) * 2 * spread, ObjectY(m, p.Kind, p.X, p.Z), p.Z + (h2 - 0.5) * 2 * spread);
                        float drop = Mathf.Max(0f, 1.6f * (1f - u * 1.6f));
                        pos = baseP + Vector3.up * drop;
                    }
                    else
                    {
                        float ang = h1 * Mathf.PI * 2f;
                        Vector3 dir = SimSpace.ToWorld(p.DirX, 0, p.DirZ);
                        Vector3 v = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (1.2f + 2.2f * h2) + dir * 1.6f + Vector3.up * (2.2f + 2.5f * h3);
                        if (p.Phase == 1) v *= 1.3f;
                        pos = c + v * t + Vector3.down * (9.8f * 0.5f * t * t);
                    }
                    float size = (p.Phase == 1 ? 0.13f : 0.08f) + 0.06f * h3;
                    Billboard(pos, size, right, up, p.Phase == 1 && j % 3 == 0 ? CellBlock : CellDisc, A(mat, 1f - u));
                }
                if (p.Phase == 2)
                {
                    float ring = 0.4f + (float)p.Size * u * 1.4f;
                    GroundQuad(p.X, p.Z, CellY(m, p.X, p.Z) + 0.03f, ring, CellRing, A(ValidGreen, 0.7f * (1f - u)));
                }
                else if (p.Phase == 1)
                {
                    float ring = 0.5f + (float)p.Size * u * 2f;
                    GroundQuad(p.X, p.Z, CellY(m, p.X, p.Z) + 0.03f, ring, CellDisc, A(mat, 0.35f * (1f - u)));
                }
            }
        }

        /// <summary>Persistent damage stages: a crack decal on the object's top and front, heavier per stage.</summary>
        void DrawStages(World w, Vector3 right, Vector3 up, Vector3 fwd)
        {
            MapDef m = w.Map;
            int cap = 40;
            // Breakable walls near the local player (the camera follows him).
            double ox = w.ActorCount > LocalActor ? w.X[LocalActor] : m.Width * 0.5, oz = w.ActorCount > LocalActor ? w.Z[LocalActor] : m.Height * 0.5;
            const int R = 16;
            int x0 = Math.Max(0, JsMath.FloorToInt(ox) - R), x1 = Math.Min(m.Width - 1, JsMath.FloorToInt(ox) + R);
            int z0 = Math.Max(0, JsMath.FloorToInt(oz) - R), z1 = Math.Min(m.Height - 1, JsMath.FloorToInt(oz) + R);
            for (int cz = z0; cz <= z1 && DecalsDrawn < cap; cz++)
            {
                for (int cx = x0; cx <= x1 && DecalsDrawn < cap; cx++)
                {
                    int i = cz * m.Width + cx;
                    double left = WorldReactions.WallLeft01(w, i);
                    if (left < 0) continue;
                    int stage = WorldReactions.StageOf(left);
                    if (stage == 0) continue;
                    float g = (float)GroundHeight.At(m, cx + 0.5, cz + 0.5);
                    GroundQuad(cx + 0.5, cz + 0.5, g + WallTop(m, cx, cz) + 0.012f, 0.5f, CellCrack1 + stage - 1, A(new Color32(255, 255, 255, 255), 0.9f));
                    Billboard(SimSpace.ToWorld(cx + 0.5, g + WallTop(m, cx, cz) * 0.5f, cz + 0.5) - fwd * 0.52f, 0.5f, right, up, CellCrack1 + stage - 1, A(new Color32(255, 255, 255, 255), 0.85f));
                    DecalsDrawn++;
                }
            }
            Barricade[] bs = w.Barricades;
            for (int k = 0; bs != null && k < bs.Length && DecalsDrawn < cap; k++)
            {
                double left = WorldReactions.BarricadeLeft01(w, k);
                if (left < 0) continue;
                int stage = WorldReactions.StageOf(left);
                if (stage == 0) continue;
                Barricade b = bs[k];
                float g = (float)GroundHeight.At(m, b.Cx + 0.5, b.Cz + 0.5);
                Vector3 c = SimSpace.ToWorld(b.Cx + 0.5, g + 0.6f, b.Cz + 0.5) - fwd * 0.5f;
                // Heavily damaged and breaking lean a little (loosened planks).
                Vector3 lean = stage >= 2 ? right * (0.06f * (stage - 1)) : Vector3.zero;
                Billboard(c + lean, 0.55f, right, up, CellCrack1 + stage - 1, A(new Color32(255, 255, 255, 255), 0.9f));
                DecalsDrawn++;
            }
            DecorationDef[] ds = m.Decorations;
            for (int k = 0; ds != null && k < ds.Length && DecalsDrawn < cap; k++)
            {
                double left = WorldReactions.PillarLeft01(w, k);
                if (left < 0) continue;
                int stage = WorldReactions.StageOf(left);
                if (stage == 0) continue;
                float g = (float)GroundHeight.At(m, ds[k].Cx + 0.5, ds[k].Cz + 0.5);
                Billboard(SimSpace.ToWorld(ds[k].Cx + 0.5, g + 1.1f, ds[k].Cz + 0.5) - fwd * 0.6f, 0.6f, right, up, CellCrack1 + stage - 1, A(new Color32(255, 255, 255, 255), 0.9f));
                GroundQuad(ds[k].Cx + 0.5, ds[k].Cz + 0.5, g + 0.03f, 0.8f, CellCrack1 + stage - 1, A(new Color32(255, 255, 255, 255), 0.8f));
                DecalsDrawn++;
            }
        }

        // ------------------------------------------------------------------ prompt, hint, bars

        static bool OnScreen(UnityEngine.Camera cam, Vector3 world, out Vector2 screen)
        {
            screen = Vector2.zero;
            if (cam == null) return false;
            Vector3 s = cam.WorldToScreenPoint(world);
            if (s.z <= 0f) return false;
            screen = new Vector2(Mathf.Clamp(s.x, 80f, cam.pixelWidth - 80f), Mathf.Clamp(s.y, 60f, cam.pixelHeight - 120f));
            return true;
        }

        public static string Glyph(string button, ControlDevice device)
        {
            string g = TrialText.Glyph(button, device);
            if (!string.IsNullOrEmpty(g)) return g;
            InputActionMap map = InputSystem.actions != null ? InputSystem.actions.FindActionMap(ControlBindings.MapName, false) : null;
            InputAction a = map != null ? map.FindAction(button, false) : null;
            string s = a != null ? ControlBindings.PromptLabel(a, device) : null;
            return string.IsNullOrEmpty(s) || s == "-" ? button.ToUpperInvariant() : s;
        }

        /// <summary>One prompt line: "F  PLACE BLOCK (3)", "HOLD E  GATHER", "SPACE  BUILD LADDER (12)   ·   bank empty".</summary>
        public static string Line(AffTarget t, ControlDevice device)
        {
            if (t == null || !t.Has) return string.Empty;
            string s = (t.Hold ? "HOLD " : "") + Glyph(t.Button, device) + "  " + t.Verb + (t.Count >= 0 ? " (" + t.Count + ")" : "");
            if (!t.Valid && !string.IsNullOrEmpty(t.Why))
                s += "   ·   " + t.Why + (t.WaitSeconds > 0 ? " " + t.WaitSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" : "");
            return s;
        }

        void UpdatePrompt(World w, UnityEngine.Camera cam, bool terrain)
        {
            bool show = terrain && WorldFeedbackSettings.Prompts && Scan.Primary.Has;
            if (!show)
            {
                if (_chip != null && _chip.gameObject.activeSelf) _chip.gameObject.SetActive(false);
                _promptShown = false;
                return;
            }
            int key = Scan.Primary.TextKey() * 31 + Scan.Secondary.TextKey();
            key = key * 31 + (int)ControlBindings.ActiveDevice * 7 + _bindingsVersion * 131 + (ControlBindings.KeyboardOnly ? 1 : 0) + (int)ControlBindings.Hand * 3;
            if (key != _promptKey)
            {
                _promptKey = key;
                string a = Line(Scan.Primary, ControlBindings.ActiveDevice), b = Line(Scan.Secondary, ControlBindings.ActiveDevice);
                _prompt = b.Length > 0 ? a + "\n" + b : a;
                HudText.Set(_label, _prompt);
                // Fit the chip to the longer line (preferredWidth is the unwrapped width at the label's size).
                float pw = _label.preferredWidth;
                _chip.sizeDelta = new Vector2(Mathf.Clamp(pw + 40f, 180f, 720f), b.Length > 0 ? 70f : 42f);
            }
            AffTarget t = Scan.Primary;
            MapDef m = w.Map;
            Vector3 p = SimSpace.ToWorld(t.X, GroundHeight.At(m, t.X, t.Z) + 1.6, t.Z);
            if (OnScreen(cam, p, out Vector2 sp))
            {
                float sc = _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1f;
                _chip.anchoredPosition = new Vector2(sp.x / sc, sp.y / sc + 18f);
            }
            if (!_chip.gameObject.activeSelf) _chip.gameObject.SetActive(true);
            _promptShown = true;
        }

        static string HintFor(ClassId c, ControlDevice d)
        {
            string show = Glyph(ShowAction, d);
            switch (c)
            {
                case ClassId.Miner: return TrialText.Fill("MINER: {DigPlace} digs the marked ground and places it back, {Ability} drops a barricade. Hold " + show + " to see everything you can use.", d);
                case ClassId.Builder: return TrialText.Fill("BUILDER: face a cliff or a gap; {Ability} builds the ladder or bridge shown, {Interact} carries one. Hold " + show + " to see everything you can use.", d);
                case ClassId.Farmer: return TrialText.Fill("FARMER: hold {Interact} at the green markers to gather, then feed the egg at the shrine. Hold " + show + " to see everything you can use.", d);
                case ClassId.Demolitions: return TrialText.Fill("DEMOLITIONS: {DigPlace} throws TNT at the marked circle, {Ability} lays a mine. Hold " + show + " to see what a blast can break.", d);
                default: return string.Empty;
            }
        }

        void UpdateHint(World w, bool terrain)
        {
            if (w.Epoch != _hintEpoch)
            {
                _hintEpoch = w.Epoch;
                _hintThisMatch = false;
                _hintDecided = false;
            }
            Actor me = w.Actors[LocalActor];
            bool inWindow = terrain && w.Match.Time < HintSeconds;
            if (inWindow && !_hintDecided)
            {
                _hintDecided = true;
                int ci = ClassIndex(me.Cls);
                _hintThisMatch = WorldFeedbackSettings.HintDue(ci);
                if (_hintThisMatch) WorldFeedbackSettings.NoteHinted(ci);
            }
            bool show = inWindow && _hintThisMatch && WorldFeedbackSettings.Hints != WorldFeedbackSettings.HintsOff;
            if (!show)
            {
                if (_hintChip != null && _hintChip.gameObject.activeSelf) _hintChip.gameObject.SetActive(false);
                _hintShown = false;
                return;
            }
            int key = (int)me.Cls * 13 + (int)ControlBindings.ActiveDevice * 3 + _bindingsVersion * 101 + (ControlBindings.KeyboardOnly ? 1 : 0);
            if (key != _hintKey)
            {
                _hintKey = key;
                _hint = HintFor(me.Cls, ControlBindings.ActiveDevice);
                HudText.Set(_hintLabel, _hint);
            }
            if (!_hintChip.gameObject.activeSelf) _hintChip.gameObject.SetActive(true);
            _hintShown = true;
        }

        bool _hintDecided;

        static int ClassIndex(ClassId c)
        {
            for (int k = 0; k < ClassIds.All.Count; k++) if (ClassIds.All[k] == c) return k;
            return 0;
        }

        void UpdateBar(World w, UnityEngine.Camera cam, bool local)
        {
            bool show = local && WorldFeedbackSettings.Progress && Focus.Has;
            Vector2 sp = Vector2.zero;
            if (show)
            {
                Vector3 p = SimSpace.ToWorld(Focus.X, GroundHeight.At(w.Map, Focus.X, Focus.Z) + Focus.Lift + 0.6, Focus.Z);
                show = OnScreen(cam, p, out sp);
            }
            if (!show)
            {
                if (_bar != null && _bar.gameObject.activeSelf) _bar.gameObject.SetActive(false);
                _barShown = false;
                return;
            }
            float sc = _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1f;
            _bar.anchoredPosition = new Vector2(sp.x / sc, sp.y / sc);
            float v = Mathf.Clamp01((float)Focus.Value01), nx = Mathf.Clamp01((float)Focus.Next01);
            _barFill.anchorMax = new Vector2(Focus.Filling ? v : nx, 1f);
            _barGhost.anchorMax = new Vector2(v, 1f);
            _barGhost.anchorMin = new Vector2(Focus.Filling ? v : nx, 0f);
            TeamId own = w.Actors[LocalActor].Team;
            _fill.color = Focus.Filling ? (Color)ValidGreen : Focus.Neutral ? (Color)new Color32(0xF5, 0xEB, 0xDC, 0xFF) : CMTokens.Team(Focus.Owner);
            int key = Focus.HitsLeft * 1009 + (int)Math.Ceiling(Focus.SecondsLeft * 10) + (int)own;
            if (key != _barKey)
            {
                _barKey = key;
                _barText = Focus.HitsLeft >= 0 ? Focus.HitsLeft + (Focus.HitsLeft == 1 ? " HIT" : " HITS")
                    : Focus.SecondsLeft >= 0 ? Focus.SecondsLeft.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" : string.Empty;
                HudText.Set(_barLabel, _barText);
            }
            if (!_bar.gameObject.activeSelf) _bar.gameObject.SetActive(true);
            _barShown = true;
        }

        /// <summary>His team's barricades under enemy attack (hit in the last 3 s): smaller bars, lower emphasis.</summary>
        void UpdateMiniBars(World w, UnityEngine.Camera cam, bool local, double now)
        {
            int used = 0;
            if (WorldFeedbackSettings.Progress && w.ActorCount > LocalActor)
            {
                TeamId own = w.Actors[LocalActor].Team;
                double[] hitAt = Reactions.BarricadeHitAt;
                for (int k = 0; k < hitAt.Length && used < _mini.Length; k++)
                {
                    if (hitAt[k] < 0 || now - hitAt[k] > 3.0) continue;
                    Barricade b = w.Barricades[k];
                    if (!b.Active || b.Team != own) continue;
                    if (local && Focus.Has && Focus.Kind == ReactKind.Barricade && Math.Abs(Focus.X - (b.Cx + 0.5)) < 0.01 && Math.Abs(Focus.Z - (b.Cz + 0.5)) < 0.01) continue;
                    Vector3 p = SimSpace.ToWorld(b.Cx + 0.5, GroundHeight.At(w.Map, b.Cx + 0.5, b.Cz + 0.5) + 1.8, b.Cz + 0.5);
                    if (!OnScreen(cam, p, out Vector2 sp)) continue;
                    float sc = _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1f;
                    _mini[used].anchoredPosition = new Vector2(sp.x / sc, sp.y / sc);
                    RectTransform fr = _miniFill[used].rectTransform;
                    fr.anchorMax = new Vector2(Mathf.Clamp01((float)WorldReactions.BarricadeLeft01(w, k)), 1f);
                    Color tc = CMTokens.Team(own);
                    tc.a = 0.7f;
                    _miniFill[used].color = tc;
                    if (!_mini[used].gameObject.activeSelf) _mini[used].gameObject.SetActive(true);
                    used++;
                }
            }
            for (int k = used; k < _mini.Length; k++) if (_mini[k] != null && _mini[k].gameObject.activeSelf) _mini[k].gameObject.SetActive(false);
            MiniBarsShown = used;
        }

        public int MiniBarsShown { get; private set; }

        // ------------------------------------------------------------------ atlas

        /// <summary>
        /// The 4 x 3 atlas (64 px cells), baked once: alpha = coverage, red = 1 colour / 0 Void Navy line. Cells: tile, strong
        /// outline, pick, sprout, hammer, block, blast, disc, crack 1 to 3, ring.
        /// </summary>
        public static Texture2D BakeAtlas()
        {
            const int S = 64, W = S * 4, H = S * 3;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "CM Affordance Atlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int cell = 0; cell < 12; cell++)
            {
                int ox = (cell % 4) * S, oy = (cell / 4) * S;
                for (int y = 0; y < S; y++)
                {
                    for (int x = 0; x < S; x++)
                    {
                        float fx = x + 0.5f, fy = y + 0.5f;
                        Shade(cell, fx, fy, out float fill, out float line);
                        float a = Mathf.Clamp01(Mathf.Max(fill, line));
                        float red = a > 1e-3f ? Mathf.Clamp01(fill / a) : 0f;
                        if (cell >= CellCrack1 && cell <= CellCrack3) { a = fill * 0.95f; red = 1f - line; }
                        px[(oy + y) * W + ox + x] = new Color32((byte)(red * 255f), 0, 0, (byte)(a * 255f));
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / Mathf.Max(1e-6f, vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static float RBox(float px, float py, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px - cx) - hx + r, qy = Mathf.Abs(py - cy) - hy + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        static float Cov(float d) => Mathf.Clamp01(0.5f - d);

        /// <summary>Fill and edge-line coverage of a pixel of an atlas cell (distances in pixels; the line is 2 px outside the shape).</summary>
        static void Shade(int cell, float x, float y, out float fill, out float line)
        {
            float d;
            switch (cell)
            {
                case CellTile:
                {
                    float box = RBox(x, y, 32, 32, 28, 28, 9);
                    float band = Mathf.Abs(box + 2.5f) - 2.5f;
                    fill = Mathf.Max(Cov(band), 0.3f * Cov(box));
                    line = Cov(Mathf.Abs(box - 1f) - 1f);
                    return;
                }
                case CellOutline:
                {
                    float box = RBox(x, y, 32, 32, 28, 28, 9);
                    float band = Mathf.Abs(box + 4f) - 4f;
                    fill = Cov(band);
                    line = Cov(Mathf.Abs(band) - 6f);
                    return;
                }
                case CellPick:
                    // A shovel: rounded blade, shaft, cross grip.
                    d = Mathf.Min(RBox(x, y, 32, 20, 12, 13, 9), Mathf.Min(Seg(x, y, 32, 30, 32, 52) - 3.5f, Seg(x, y, 21, 54, 43, 54) - 4f));
                    break;
                case CellSprout:
                {
                    float stem = Seg(x, y, 32, 8, 32, 36) - 3.5f;
                    float l1 = new Vector2((x - 21f) / 1.6f, y - 40f).magnitude - 8f;
                    float l2 = new Vector2((x - 43f) / 1.6f, y - 46f).magnitude - 8f;
                    d = Mathf.Min(stem, Mathf.Min(l1, l2));
                    break;
                }
                case CellHammer:
                    d = Mathf.Min(Seg(x, y, 20, 8, 36, 40) - 4f, RBox(x, y, 38, 44, 17, 8, 2));
                    break;
                case CellBlock:
                    d = Mathf.Max(RBox(x, y, 32, 32, 20, 20, 3), -RBox(x, y, 32, 32, 13, 13, 1) - 0.0f);
                    d = Mathf.Min(d, RBox(x, y, 32, 32, 13, 13, 1) + 6f);
                    d = Mathf.Min(d, RBox(x, y, 32, 32, 20, 20, 3));
                    break;
                case CellBlast:
                {
                    d = new Vector2(x - 32f, y - 32f).magnitude - 9f;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI / 4f;
                        float cx = Mathf.Cos(a) * 24f, cy = Mathf.Sin(a) * 24f;
                        d = Mathf.Min(d, Seg(x, y, 32 - cx, 32 - cy, 32 + cx, 32 + cy) - 3f);
                    }
                    break;
                }
                case CellDisc:
                {
                    float r = new Vector2(x - 32f, y - 32f).magnitude;
                    fill = Mathf.Clamp01((30f - r) / 8f);
                    line = 0f;
                    return;
                }
                case CellRing:
                {
                    float r = new Vector2(x - 32f, y - 32f).magnitude;
                    fill = Cov(Mathf.Abs(r - 28f) - 1.6f);
                    line = Cov(Mathf.Abs(r - 28f) - 3f);
                    return;
                }
                default:
                {
                    // Cracks: dark Void Navy lines from a fixed seed, more and longer per stage.
                    int stage = cell - CellCrack1 + 1;
                    float best = 99f;
                    int segs = stage * 4;
                    float pxs = 32f, pys = 32f;
                    for (int k = 0; k < segs; k++)
                    {
                        float a = Hash01(k * 97 + stage * 13) * Mathf.PI * 2f;
                        float len = 8f + 10f * Hash01(k * 61 + 5) + stage * 3f;
                        float nx = Mathf.Clamp(pxs + Mathf.Cos(a) * len, 4f, 60f), ny = Mathf.Clamp(pys + Mathf.Sin(a) * len, 4f, 60f);
                        best = Mathf.Min(best, Seg(x, y, pxs, pys, nx, ny) - (stage >= 3 ? 1.6f : 1.1f));
                        if (k % 3 == 2) { pxs = 32f; pys = 32f; } else { pxs = nx; pys = ny; }
                    }
                    // Thick enough to read at gameplay distance: a Void Navy core inside a light halo (CrackShade below).
                    fill = Cov(best - 2.2f);
                    line = Cov(best + 0.6f);
                    return;
                }
            }
            fill = Cov(d);
            line = Cov(d - 2.5f);
        }
    }
}
