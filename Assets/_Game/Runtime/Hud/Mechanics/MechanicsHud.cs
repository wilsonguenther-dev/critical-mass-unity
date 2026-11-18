using System;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Mechanics
{
    /// <summary>
    /// The HUD side of the new classes and the physics lane, on its own canvas (sorting 13: over the HUD and the kill feed,
    /// under the interactive layer): the class kit panel for Demolitions, Builder and Healer (the second ability, its
    /// cooldown, charges and the binding through InputGlyphs; the team biomass bank for a Builder's team), the stun badge,
    /// the "DODGED" tick and the "JUMP" hint inside a slam, off-screen damage chevrons, the "you were hit by" recap while
    /// down, and the minimap and full map markers for ladders, bridges, your team's mines, drops and TNT, with legend rows.
    /// View only; never writes the sim. Added to the driver by MechanicsWorldViewInstaller.
    /// </summary>
    /// <summary>
    /// b13-v: the kit rows of the batch 13 jobs, read from the S0 getters (zeros and false until the rule items land, so with
    /// the rules off the kit is exactly today's ClassKit): the Miner's F MINE with the ore he carries (0/3) and Space HAUL;
    /// the Builder's F BUILD with the selected piece and tier (SHORT WALL / TALL WALL / BLOCK) and Space STAIRS / BRIDGE,
    /// his switch key on the wood line; every class's DODGE chip. Pure helpers; nothing allocates per frame.
    /// </summary>
    public static class B13Kit
    {
        public const float DodgeW = 150f;
        public const string Mine = "MINE", Haul = "HAUL", Build = "BUILD", Stairs = "STAIRS / BRIDGE", NextTool = "NEXT TOOL";
        public const string ShortWall = "SHORT WALL", TallWall = "TALL WALL", LongWall = "TALL WALL x5", Block = "BLOCK";
        public static readonly string[] OreNotes = { "ORE 0/3", "ORE 1/3", "ORE 2/3", "ORE 3/3" };

        public static bool Overrides(World w, ClassId c) => w != null && ((c == ClassId.Miner && Blacksmith.On(w)) || (c == ClassId.Builder && BuilderWalls.On(w)));
        public static int RowCount(World w, ClassId c) => Overrides(w, c) ? 2 : ClassKit.RowCount(c);
        public static string OreNote(int n) => OreNotes[Mathf.Clamp(n, 0, OreNotes.Length - 1)];

        /// <summary>The two row names of a class with the batch 13 rule on (row 0 = F, row 1 = Space); null = today's rows.</summary>
        public static string[] SlotNames(ClassId c) =>
            c == ClassId.Miner ? new[] { Mine, Haul } : c == ClassId.Builder ? new[] { Build, NextTool } : null;

        static string[] _toolNotes;

        /// <summary>b14: the Builder's BUILD row note for a tool: "LADDER  2 WOOD", "SHORT WALL  1 WOOD", "DIG" (cached, no
        /// allocation after the first call; tier 2 names the five-cell tall wall).</summary>
        public static string ToolNote(BuilderTool t, int tier, int cost)
        {
            const int Costs = 10;
            if (_toolNotes == null) _toolNotes = new string[BuilderToolbelt.Count * 3 * Costs];
            int ti = Mathf.Clamp((int)t, 0, BuilderToolbelt.Count - 1), tr = Mathf.Clamp(tier, 0, 2), c = Mathf.Clamp(cost, 0, Costs - 1);
            int k = (ti * 3 + tr) * Costs + c;
            if (_toolNotes[k] == null)
            {
                string word = BuilderToolbelt.Word((BuilderTool)ti, tr);
                _toolNotes[k] = c > 0 ? word + "  " + c.ToString(CultureInfo.InvariantCulture) + " WOOD" : word;
            }
            return _toolNotes[k];
        }

        /// <summary>b14: the Builder's selected tool: the local wheel's choice, else the sim's own selection.</summary>
        public static BuilderTool ToolOf(World w, int id)
        {
            BuilderToolbelt belt = BuilderToolbelt.Local;
            if (belt != null && (belt.Actor < 0 || belt.Actor == id)) return belt.Selected;
            return BuilderToolbelt.SimTool(w, id, out BuilderTool t) ? t : BuilderTool.Ladder;
        }

        /// <summary>The Builder's piece word: BLOCK, SHORT WALL, TALL WALL (tier 1) or TALL WALL x5 (tier 2).</summary>
        public static string PieceWord(BuildPiece p, bool tall, int tier) => p == BuildPiece.Block ? Block : !tall ? ShortWall : tier >= 2 ? LongWall : TallWall;

        public static bool Read(World w, int id, int r, ref KitRow row, string[] numbers)
        {
            if (w == null || id < 0 || id >= w.ActorCount) return false;
            Actor a = w.Actors[id];
            if (!Overrides(w, a.Cls)) return ClassKit.Read(w, id, r, ref row, numbers);
            ClassConfig cc = w.Config.Class(a.Cls);
            row.Note = null;
            row.Usable = a.Alive;
            row.MaxCharges = 0;
            row.Charges = 0;
            if (r == 0)
            {
                row.Action = "DigPlace";
                row.Cd01 = 0f;
                row.CdSeconds = 0f;
                if (a.Cls == ClassId.Miner)
                {
                    row.Name = Mine;
                    row.Note = OreNote(a.Carry == CarryKind.Ore ? a.CarryAmount : 0);
                }
                else
                {
                    // b14: the BUILD row names the selected tool of the one wheel and what it costs (the wood itself is on
                    // the TEAM WOOD line only, once).
                    row.Name = Build;
                    int tier = BuilderWalls.Tier(w, a.Team);
                    BuilderTool tool = ToolOf(w, id);
                    if (BuilderToolbelt.IsWallPiece(tool) && BuilderWalls.Ghost(w, id, out _, out _, out _, out _, out _, out _, out bool ok)) row.Usable = a.Alive && ok;
                    row.Note = ToolNote(tool, tier, BuilderToolbelt.Cost(w, tool));
                }
                return true;
            }
            if (r != 1) return false;
            if (a.Cls == ClassId.Builder)
            {
                // b14: the second row is the wheel's next tool on the switch key (the ability buttons use the tool too).
                BuilderToolbelt.Rules(w, id, out bool walls, out bool terrain, out bool tall2);
                BuilderTool next = BuilderToolbelt.Next(ToolOf(w, id), 1, walls, terrain, tall2);
                row.Action = "SwitchDigPlace";
                row.Name = NextTool;
                row.Note = ToolNote(next, BuilderWalls.Tier(w, a.Team), 0);
                row.Cd01 = 0f;
                row.CdSeconds = 0f;
                return true;
            }
            row.Action = "Ability";
            row.Name = a.Cls == ClassId.Miner ? Haul : Stairs;
            row.Cd01 = cc.AbilityCooldown > 0 ? Mathf.Clamp01((float)(a.AbilityCd / cc.AbilityCooldown)) : 0f;
            row.CdSeconds = (float)Math.Max(0, a.AbilityCd);
            return true;
        }

        /// <summary>The Builder's wood line with his switch key (allocates; the HUD calls it only when a value changes).</summary>
        public static string BankLine(string wood, string switchKey) => wood + "   SWITCH " + switchKey;

        /// <summary>The dodge ring 0..1: full when ready, else how far it has recharged (maxSeen = the cooldown it started at).</summary>
        public static float DodgeFill(int ticksLeft, int maxSeen) => ticksLeft <= 0 ? 1f : maxSeen <= 0 ? 0f : 1f - Mathf.Clamp01(ticksLeft / (float)maxSeen);

        public static string DodgeLabel(string key) => "DODGE " + key;

        /// <summary>The key words of an action from the current bindings, upper case, or the fallback (allocates; rebinds only).</summary>
        public static string KeyOr(string action, string fallback)
        {
            string s = InputGlyphs.Words(action);
            return string.IsNullOrEmpty(s) || s == "-" ? fallback : s.ToUpperInvariant();
        }
    }

    public sealed class MechanicsHud : MonoBehaviour
    {
        public const int SortingOrder = 13;
        public const float KitX = HudZones.M, KitW = HudZones.KitW, RowH = 54f;
        public const int MaxPieces = 16, MaxMines = 16, MaxDrops = 24, MaxBombs = 8;

        public static MechanicsHud Instance { get; private set; }
        /// <summary>True while the class kit panel is shown (game-flow overlays can read it).</summary>
        public bool KitShown => _kit != null && _kit.gameObject.activeSelf;
        public bool StunShown => _stun != null && _stun.gameObject.activeSelf;
        /// <summary>The hit recap panel, reference px (top-left origin): centred, just under the centre lane that holds the
        /// death recap card, clear of the guide column, the stingers, the player card and the lower lane.</summary>
        public static readonly Rect RecapRect = new Rect((HudZones.RefW - 620f) * 0.5f, HudZones.GuideUpperY, 620f, 40f + HitRecapWords.Rows * 32f + 12f);
        public bool RecapOpen => _recapOpen;
        public string RecapText => _recapRows != null ? _recapRows[0].text : string.Empty;
        public int ChevronsShown { get; private set; }

        Canvas _canvas;
        RectTransform _root, _space;
        SimDriverBehaviour _driver;
        CombatLog _log;

        // kit
        RectTransform _kit;
        VectorShape _kitPanel;
        Text _kitTitle, _bank;
        readonly VectorShape[] _rowChip = new VectorShape[2], _rowPie = new VectorShape[2];
        readonly Text[] _rowName = new Text[2], _rowKey = new Text[2], _rowCd = new Text[2], _rowNote = new Text[2];
        readonly VectorShape[,] _rowPip = new VectorShape[2, 3];
        readonly string[] _rowAction = new string[2];
        readonly string[] _rowKeyWord = new string[2];
        bool _glyphDirty = true;
        int _kitClass = -1;

        // b13-v: the dodge chip in the kit's title row (every class, while the dodge rule is on) and the Builder's switch key
        VectorShape _dodgePie;
        Text _dodgeKey;
        int _dodgeMax;
        string _switchKey = "WHEEL";
        int _bankWood = -1;
        string _bankSwitch, _bankLine = string.Empty;
        /// <summary>b13-v: the dodge chip shows (tests and captures).</summary>
        public bool DodgeShown => _dodgeKey != null && _dodgeKey.gameObject.activeSelf;

        // stun, dodge, jump
        RectTransform _stun, _dodge, _jump;
        VectorShape _stunPie;
        CanvasGroup _dodgeGroup;
        Text _jumpText;
        int _seenDodges;
        float _dodgeAt = -9f;

        // chevrons
        readonly RectTransform[] _chev = new RectTransform[DamageDirection.Slots];
        readonly CanvasGroup[] _chevGroup = new CanvasGroup[DamageDirection.Slots];

        // recap
        RectTransform _recap;
        Text _recapHead;
        readonly Text[] _recapRows = new Text[HitRecapWords.Rows];
        bool _recapOpen;

        // minimap and full map
        GameHud _hud;
        RectTransform _mapArea;
        int _hudSearch, _logSearch;
        readonly RectTransform[] _mPiece = new RectTransform[MaxPieces], _mMine = new RectTransform[MaxMines], _mDrop = new RectTransform[MaxDrops], _mBomb = new RectTransform[MaxBombs];
        readonly VectorShape[] _mPieceS = new VectorShape[MaxPieces], _mMineS = new VectorShape[MaxMines], _mDropS = new VectorShape[MaxDrops];
        readonly RectTransform[] _fPiece = new RectTransform[MaxPieces], _fMine = new RectTransform[MaxMines], _fDrop = new RectTransform[MaxDrops], _fBomb = new RectTransform[MaxBombs];
        readonly VectorShape[] _fPieceS = new VectorShape[MaxPieces], _fMineS = new VectorShape[MaxMines], _fDropS = new VectorShape[MaxDrops];
        bool _fullBuilt, _legendBuilt;

        static readonly string[] Tenths = BuildTenths();
        static readonly string[] Whole = BuildWhole();

        static string[] BuildTenths()
        {
            var a = new string[601];
            for (int i = 0; i < a.Length; i++) a[i] = (i / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
            return a;
        }

        static readonly string[] BankLine = BuildBankLines();

        /// <summary>first-minutes-1: the class card's build-row note for n team wood ("WOOD 2"; tests).</summary>
        public static string WoodNote(int n) => Whole[Mathf.Clamp(n, 0, Whole.Length - 1)];

        /// <summary>first-minutes-1: the kit footer for n team wood ("TEAM WOOD 2  LADDER 1, BRIDGE 2"; tests).</summary>
        public static string TeamWoodLine(int n) => BankLine[Mathf.Clamp(n, 0, BankLine.Length - 1)];

        static string[] BuildWhole()
        {
            var a = new string[200];
            for (int i = 0; i < a.Length; i++) a[i] = BuilderBank.RowWord + " " + i.ToString(CultureInfo.InvariantCulture);   // first-minutes-1: one wood word (was "BANK n")
            return a;
        }

        static string[] BuildBankLines()
        {
            var a = new string[200];
            for (int i = 0; i < a.Length; i++) a[i] = BuilderBank.WoodWord + " " + i.ToString(CultureInfo.InvariantCulture) + "  LADDER 1, BRIDGE 2";   // first-minutes-1 (was TEAM BANK n)
            return a;
        }

        /// <summary>A cooldown label: tenths under 60 s, else whole seconds (cached strings, no allocation).</summary>
        public static string CdLabel(float seconds)
        {
            if (seconds <= 0.05f) return string.Empty;
            int t = Mathf.CeilToInt(seconds * 10f - 1e-4f);
            return t < Tenths.Length ? Tenths[t] : Tenths[Tenths.Length - 1];
        }

        void Awake()
        {
            Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
            Build();
            ControlBindings.BindingsChanged += MarkGlyphs;
            ControlBindings.ActiveDeviceChanged += OnDevice;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ControlBindings.BindingsChanged -= MarkGlyphs;
            ControlBindings.ActiveDeviceChanged -= OnDevice;
        }

        void MarkGlyphs() => _glyphDirty = true;
        void OnDevice(ControlDevice d) => _glyphDirty = true;

        void Build()
        {
            _canvas = InteractUi.MakeHudCanvas(transform, "Mechanics HUD Canvas", SortingOrder, out _root, out _space);
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color, navy = CMTokens.VoidNavy.Color;

            // class kit: above the player cluster (bottom left)
            // hud-containment-1: the kit has its own slot in the left stack (HudZones.Kit) and is sized to its rows (KitHeight)
            float kitH = HudZones.KitMaxH;
            // fix/hud-containment-3 (walk: team calls drew inside the TEAM BUILD BANK box): the kit sits in the same
            // bottom-left safe frame as the ping feed and the player card; in the centred space it rose 60 px on a 16:10
            // screen and met the ping feed, which is anchored to the bottom.
            _kit = MenuUi.Rt(FeedUi.SafeRoot(_root), "Class Kit");
            HudZones.RegisterPanel(_kit);   // fix/hud-containment-4: name tags stay off the kit and the build bank box
            MenuUi.Place(_kit, KitX, HudZones.Kit.y, KitW, kitH);
            _kitPanel = MenuUi.Panel(_kit, "Panel", 0f, 0f, KitW, kitH);
            _kitTitle = MenuUi.Fit(MenuUi.Label(_kit, "Title", CMTokens.Display, UiScale.TypeSmall, brass, TextAnchor.MiddleLeft, 18f, 8f, KitW - 36f - B13Kit.DodgeW, 28f, "", 2f), 13);
            // b13-v: DODGE with its cooldown ring, right end of the title row (two graphics on this canvas)
            _dodgePie = MenuUi.Shape(_kit, "Dodge Ring", ShapeKind.Pie, KitW - B13Kit.DodgeW - 6f, 7f, 28f, 28f, brass, 14f);
            _dodgePie.EmptyColor = CMTokens.MeterEmpty.Color;
            _dodgePie.RoundPie = true;
            _dodgePie.OutlinePx = 2f;
            _dodgePie.RimPx = 0f;
            _dodgeKey = MenuUi.Fit(MenuUi.Label(_kit, "Dodge Key", CMTokens.Ui, UiScale.TypeMin, cream, TextAnchor.MiddleLeft, KitW - B13Kit.DodgeW + 28f, 6f, B13Kit.DodgeW - 46f, 30f, "", 2f), 10);
            _dodgePie.gameObject.SetActive(false);
            _dodgeKey.gameObject.SetActive(false);
            for (int r = 0; r < 2; r++)
            {
                float y = 42f + r * RowH;
                _rowChip[r] = MenuUi.Shape(_kit, "Row " + r, ShapeKind.RoundRect, 12f, y, KitW - 24f, RowH - 6f, CMTokens.PanelNavy.Color, 12f);
                _rowChip[r].OutlinePx = 3f;
                _rowChip[r].RimPx = 0f;
                _rowPie[r] = MenuUi.Shape(_kit, "Pie " + r, ShapeKind.Pie, 20f, y + 5f, 38f, 38f, brass, 19f);
                _rowPie[r].EmptyColor = CMTokens.MeterEmpty.Color;
                _rowPie[r].RoundPie = true;   // fix/hud-containment-5: a round cooldown under the skill's icon (was a tan square)
                _rowPie[r].OutlinePx = 3f;
                _rowPie[r].RimPx = 0f;
                _rowIcon[r] = HudIcon.Make(_kit, "Icon " + r, 23f, y + 8f, 32f, 32f);   // the skill's painted icon over its cooldown
                _rowCd[r] = MenuUi.Fit(MenuUi.Label(_kit, "Cd " + r, CMTokens.Mono, UiScale.TypeMin, cream, TextAnchor.MiddleCenter, 16f, y + 5f, 46f, 38f, "", 2f), 15);
                _rowName[r] = MenuUi.Fit(MenuUi.Label(_kit, "Name " + r, CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, 70f, y + 2f, 154f, 26f, "", 2f), 12);
                _rowNote[r] = MenuUi.Fit(MenuUi.Label(_kit, "Note " + r, CMTokens.Ui, UiScale.TypeMin, brass, TextAnchor.MiddleLeft, 70f, y + 26f, 154f, 20f, "", 2f), 11);
                for (int p = 0; p < 3; p++)
                {
                    VectorShape pip = MenuUi.Shape(_kit, "Pip " + r + p, ShapeKind.Diamond, 230f + p * 18f, y + 15f, 16f, 18f, brass, 3f);
                    pip.OutlinePx = 2f;
                    pip.RimPx = 0f;
                    _rowPip[r, p] = pip;
                }
                VectorShape keyChip = MenuUi.Shape(_kit, "Key Chip " + r, ShapeKind.RoundRect, KitW - 92f, y + 9f, 68f, 30f, navy, 8f);
                keyChip.RimColor = cream;
                keyChip.RimPx = 2f;
                _rowKey[r] = MenuUi.Fit(MenuUi.Label(keyChip.transform, "Key", CMTokens.Ui, UiScale.TypeMin, cream, TextAnchor.MiddleCenter, 0f, 0f, 68f, 30f, "", 2f), 14);
                _rowKeyChip[r] = keyChip;
                _rowGlyph[r] = HudPrompts.PlateGlyph(keyChip.transform);   // fix/hud-containment-3: the ability plates' glyph style
                _rowKey[r].resizeTextMinSize = 9;
                _rowKey[r].resizeTextMaxSize = 14;
                MenuUi.Contain(_rowKey[r], 2);   // "RIGHT CLICK" wraps inside its key chip
            }
            _bank = MenuUi.Fit(MenuUi.Label(_kit, "Bank", CMTokens.Ui, UiScale.TypeMin, CMTokens.Biomass.Color, TextAnchor.MiddleLeft, 18f, 44f + 2f * RowH - 2f, KitW - 36f, 28f, "", 2f), 11);
            _kit.gameObject.SetActive(false);

            // stun badge, dodge tick, jump hint: centre, below the player's head
            _stun = MenuUi.Rt(_space, "Stun");
            MenuUi.Place(_stun, 960f - 120f, 600f, 240f, 52f);
            VectorShape sp = MenuUi.Shape(_stun, "Panel", ShapeKind.RoundRect, 0f, 0f, 240f, 52f, CMTokens.PanelNavy.Color, 14f);
            sp.OutlinePx = 4f;
            _stunPie = MenuUi.Shape(_stun, "Pie", ShapeKind.Pie, 10f, 8f, 36f, 36f, CMTokens.MeterOrange.Color, 18f);
            _stunPie.EmptyColor = CMTokens.MeterEmpty.Color;
            _stunPie.RimPx = 0f;
            MenuUi.Label(_stun, "Word", CMTokens.Display, UiScale.TypeBody, cream, TextAnchor.MiddleLeft, 56f, 0f, 180f, 52f, "STUNNED", 3f);
            _stun.gameObject.SetActive(false);

            _dodge = MenuUi.Rt(_space, "Dodged");
            MenuUi.Place(_dodge, 960f - 110f, 540f, 220f, 56f);
            _dodgeGroup = _dodge.gameObject.AddComponent<CanvasGroup>();
            _dodgeGroup.blocksRaycasts = false;
            VectorShape dp = MenuUi.Shape(_dodge, "Panel", ShapeKind.Banner, 0f, 0f, 220f, 56f, brass, 10f);
            dp.OutlinePx = 4f;
            MenuUi.Label(_dodge, "Word", CMTokens.Display, UiScale.TypeButton, navy, TextAnchor.MiddleCenter, 0f, 0f, 220f, 56f, "DODGED", 0f);
            _dodge.gameObject.SetActive(false);

            _jump = MenuUi.Rt(_space, "Jump Hint");
            MenuUi.Place(_jump, 960f - 150f, 470f, 300f, 50f);
            VectorShape jp = MenuUi.Shape(_jump, "Panel", ShapeKind.RoundRect, 0f, 0f, 300f, 50f, CMTokens.PanelNavy.Color, 14f);
            jp.OutlinePx = 4f;
            jp.RimColor = cream;
            _jumpText = MenuUi.Label(_jump, "Word", CMTokens.Ui, UiScale.TypeBody, cream, TextAnchor.MiddleCenter, 0f, 0f, 300f, 50f, "", 2f);
            _jump.gameObject.SetActive(false);

            // off-screen damage chevrons
            for (int i = 0; i < DamageDirection.Slots; i++)
            {
                RectTransform rt = InteractUi.Centred(_space, "Damage Chevron " + i, 54f, 40f);
                _chevGroup[i] = rt.gameObject.AddComponent<CanvasGroup>();
                _chevGroup[i].blocksRaycasts = false;
                VectorShape s = MenuUi.Shape(rt, "Shape", ShapeKind.TriangleDown, 0f, 0f, 54f, 40f, CMTokens.Crimson.Color, 6f);
                s.OutlinePx = 4f;
                s.RimColor = cream;
                rt.gameObject.SetActive(false);
                _chev[i] = rt;
            }

            // "you were hit by": under the death recap card, in the centre column (integration batch 5: it sat at y 826,
            // over the player card and the spectate offer)
            _recap = MenuUi.Rt(_space, "Hit Recap");
            HudZones.RegisterPanel(_recap);   // fix/hud-containment-4: world labels stay off YOU WERE HIT BY
            MenuUi.Place(_recap, RecapRect.x, RecapRect.y, RecapRect.width, RecapRect.height);
            MenuUi.Panel(_recap, "Panel", 0f, 0f, RecapRect.width, RecapRect.height);
            _recapHead = MenuUi.Label(_recap, "Head", CMTokens.Display, UiScale.TypeSmall, brass, TextAnchor.MiddleLeft, 22f, 8f, RecapRect.width - 44f, 28f, "YOU WERE HIT BY", 2f);
            for (int i = 0; i < HitRecapWords.Rows; i++)
                _recapRows[i] = MenuUi.Label(_recap, "Row " + i, CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, 22f, 40f + i * 32f, RecapRect.width - 44f, 30f, "", 2f);
            _recap.gameObject.SetActive(false);
            HudKeepOut.Add(_recap);   // integration batch 5: name tags keep off the recap (BROOK drew through it)
        }

        void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.NameTags, cost); }

        void LateUpdateCost()
        {
            if (CriticalMass.Runtime.Flow.GameFlow.MenuIdle)   // batch 4: hidden behind a menu over a backdrop match
            {
                // b11-af: and drawn nowhere then (it returned with the canvas still on, so the last frame's damage
                // chevrons stayed on screen over the menu)
                if (_canvas != null && _canvas.enabled) { _canvas.enabled = false; HideChevrons(); }
                return;
            }
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_log == null && --_logSearch <= 0) { _logSearch = 30; _log = FindAnyObjectByType<CombatLog>(); }
            World w = _driver != null ? _driver.World : null;
            MechanicsWorldView view = MechanicsWorldView.Active;
            InteractUi.UpdateScale(_canvas);
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            bool hide = w == null || view == null || (_log != null && _log.Suppressed) || HiddenOver(CriticalMass.Runtime.Flow.GameFlow.State, w.Match.Phase);
            if (_canvas.enabled == hide) _canvas.enabled = !hide;
            if (hide) { _recapOpen = false; HideChevrons(); return; }
            int me = view.Feed.LocalActor;
            if (me < 0 || me >= w.ActorCount) return;
            float realNow = Time.unscaledTime;
            TickKit(w, me);
            TickStatus(w, view, me, realNow);
            TickChevrons(w, view, me);
            TickRecap(w, view, me);
            TickMaps(w, me);
        }

        // ------------------------------------------------------------------ class kit

        void TickKit(World w, int me)
        {
            Actor a = w.Actors[me];
            int rows = B13Kit.RowCount(w, a.Cls);   // b13-v: the Miner's MINE / HAUL and the Builder's BUILD / STAIRS rows
            bool teamBank = rows == 0 && BuilderBank.TeamHasBuilder(w, a.Team);
            bool dodgeOn = CriticalMass.Sim.Core.Dodge.On(w);   // b13-v: every class has the dodge chip, so the kit shows for every class then
            // fix/hud-containment-3: an empty team bank is no box at all (it was a panel saying the bank is empty)
            bool show = (rows > 0 || (teamBank && BuilderBank.Whole(w, a.Team) > 0) || dodgeOn) && a.Alive;
            if (_kit.gameObject.activeSelf != show) _kit.gameObject.SetActive(show);
            if (!show) return;
            int cls = (int)a.Cls;
            if (cls != _kitClass)
            {
                _kitClass = cls;
                _glyphDirty = true;
                HudText.Set(_kitTitle, rows > 0 || !teamBank ? HitRecapWords.ClassNames[Mathf.Clamp(cls, 0, HitRecapWords.ClassNames.Length - 1)] : "TEAM BUILD BANK");
                _kitPanel.SetRim(CMTokens.Team(a.Team));
            }
            var row = new KitRow();
            for (int r = 0; r < 2; r++)
            {
                bool on = r < rows && B13Kit.Read(w, me, r, ref row, Whole);
                SetRowActive(r, on);
                if (!on) continue;
                if (_rowAction[r] != row.Action) { _rowAction[r] = row.Action; _glyphDirty = true; }
                HudText.Set(_rowName[r], row.Name);
                HudText.Set(_rowNote[r], row.Note ?? string.Empty);
                HudText.Set(_rowCd[r], CdLabel(row.CdSeconds));
                _rowPie[r].Fill01 = 1f - row.Cd01;
                // fix/hud-containment-5: the row's icon (the class's ability art, or its class badge for a second skill), shown
                // over the round cooldown once the skill is ready. The Healer has her own art since hud-art (UiIcons.Ability).
                string icon = RowIcon(row.Action, (int)a.Cls, (int)a.Team);
                if (_rowIcon[r].Name != icon) _rowIcon[r].Set(icon, 2);
                bool showIcon = icon != null && row.Cd01 <= 0f;
                if (_rowIcon[r].Image.gameObject.activeSelf != showIcon) _rowIcon[r].Image.gameObject.SetActive(showIcon);
                bool ready = row.Cd01 <= 0f && row.Usable && (row.MaxCharges == 0 || row.Charges > 0);
                _rowChip[r].SetFill(ready ? CMTokens.PanelNavy.Color : CMTokens.MeterEmpty.Color);
                _rowName[r].color = row.Usable ? CMTokens.WarmCream.Color : CMTokens.Stone.Color;
                for (int p = 0; p < 3; p++)
                {
                    bool pipOn = p < row.MaxCharges;
                    VectorShape pip = _rowPip[r, p];
                    if (pip.gameObject.activeSelf != pipOn) pip.gameObject.SetActive(pipOn);
                    if (pipOn) pip.SetFill(p < row.Charges ? CMTokens.ToolBrass.Color : CMTokens.MeterEmpty.Color);
                }
            }
            // b14 review (wood shown once): with the sources rule the stock strip at the top already shows the team wood
            // (BuilderBank.Whole, the same number), so the Builder's kit drops its TEAM WOOD footer there.
            bool bankOn = teamBank || (a.Cls == ClassId.Builder && !DepthRead.SourcesOn(w));
            if (_bank.gameObject.activeSelf != bankOn) _bank.gameObject.SetActive(bankOn);
            LayoutKit(Mathf.Min(rows, 2), bankOn);
            if (bankOn)
            {
                int b = BuilderBank.Whole(w, a.Team);
                if (a.Cls == ClassId.Builder && B13Kit.Overrides(w, a.Cls))
                {
                    // b13-v: "TEAM WOOD 4   SWITCH WHEEL", rebuilt only when the wood or the switch key changes
                    if (b != _bankWood || !ReferenceEquals(_bankSwitch, _switchKey))
                    {
                        _bankWood = b;
                        _bankSwitch = _switchKey;
                        _bankLine = BuilderBank.WoodWord + " " + b.ToString(CultureInfo.InvariantCulture);   // b14: "TEAM WOOD 4" (the switch key is on the NEXT TOOL row)
                    }
                    HudText.Set(_bank, _bankLine);
                }
                else HudText.Set(_bank, BankLine[Mathf.Clamp(b, 0, BankLine.Length - 1)]);
            }
            TickDodge(w, me, dodgeOn);
            if (_glyphDirty) RefreshGlyphs();
        }

        /// <summary>b13-v: the dodge chip: ring full = ready, filling while it recharges (Dodge.CooldownTicks).</summary>
        void TickDodge(World w, int me, bool on)
        {
            if (_dodgeKey.gameObject.activeSelf != on) { _dodgeKey.gameObject.SetActive(on); _dodgePie.gameObject.SetActive(on); }
            if (!on) return;
            int left = CriticalMass.Sim.Core.Dodge.CooldownTicks(w, me);
            if (left <= 0) _dodgeMax = 0;
            else if (left > _dodgeMax) _dodgeMax = left;
            _dodgePie.Fill01 = B13Kit.DodgeFill(left, _dodgeMax);
            _dodgePie.SetFill(left <= 0 ? CMTokens.ToolBrass.Color : CMTokens.Stone.Color);
        }

        readonly VectorShape[] _rowKeyChip = new VectorShape[2];
        readonly InputGlyphView[] _rowGlyph = new InputGlyphView[2];

        int _kitRows = -1;
        bool _kitBank;

        /// <summary>hud-containment-1: the kit panel's height for its rows and bank line (no empty panel).</summary>
        public static float KitHeight(int rows, bool bank) => 44f + rows * RowH + (bank ? 34f : 0f) + (rows > 0 && !bank ? 6f : 0f);

        /// <summary>Sizes the kit to its content, its bottom on the bottom of its slot (work only when rows or bank change).</summary>
        void LayoutKit(int rows, bool bank)
        {
            if (rows == _kitRows && bank == _kitBank) return;
            _kitRows = rows;
            _kitBank = bank;
            float h = KitHeight(rows, bank);
            Rect slot = HudZones.Kit;
            _kit.anchorMin = _kit.anchorMax = Vector2.zero;   // bottom-left, like the player card and the ping feed
            _kit.pivot = Vector2.zero;
            _kit.sizeDelta = new Vector2(KitW, h);
            _kit.anchoredPosition = new Vector2(slot.x, HudLayout.RefH - slot.yMax);
            MenuUi.Place(_kitPanel.rectTransform, 0f, 0f, KitW, h);
            MenuUi.Place(_bank.rectTransform, 18f, 44f + rows * RowH - 2f, KitW - 36f, 28f);
            // rows keep their place under the title: they are children of _kit, placed from its top
        }

        readonly HudIcon[] _rowIcon = new HudIcon[2];

        /// <summary>The painted icon for a kit row's action: the class's attack or ability art, else its class badge.</summary>
        public static string RowIcon(string action, int cls, int team)
        {
            if (cls < 0 || cls >= UiIcons.Classes.Length) cls = 1;   // as GameHud.IconRow
            int row = Mathf.Clamp(cls, 0, UiIcons.Ability.GetLength(0) - 1), t = Mathf.Clamp(team, 0, UiIcons.Ability.GetLength(1) - 1);
            if (action == "Ability") return UiIcons.Ability[row, t];
            if (action == "Attack") return UiIcons.Attack[Mathf.Clamp(cls, 0, UiIcons.Attack.GetLength(0) - 1), Mathf.Clamp(team, 0, UiIcons.Attack.GetLength(1) - 1)];
            // a second skill with no art of its own (Purify, TNT, Carry) wears the class badge, never a blank square
            return UiIcons.ClassBadge[Mathf.Clamp(cls, 0, UiIcons.ClassBadge.GetLength(0) - 1), Mathf.Clamp(team, 0, UiIcons.ClassBadge.GetLength(1) - 1)];
        }

        void SetRowActive(int r, bool on)
        {
            if (_rowIcon[r] != null && !on && _rowIcon[r].Image.gameObject.activeSelf) _rowIcon[r].Image.gameObject.SetActive(false);
            if (_rowChip[r].gameObject.activeSelf == on) return;
            _rowChip[r].gameObject.SetActive(on);
            _rowPie[r].gameObject.SetActive(on);
            _rowCd[r].gameObject.SetActive(on);
            _rowName[r].gameObject.SetActive(on);
            _rowNote[r].gameObject.SetActive(on);
            _rowKey[r].transform.parent.gameObject.SetActive(on);
            for (int p = 0; p < 3; p++) _rowPip[r, p].gameObject.SetActive(on);
        }

        /// <summary>Re-reads the binding words (allocates; only after a rebind, a device change or a class change).</summary>
        void RefreshGlyphs()
        {
            _glyphDirty = false;
            for (int r = 0; r < 2; r++)
            {
                string action = _rowAction[r];
                string words = string.IsNullOrEmpty(action) ? string.Empty : InputGlyphs.Words(action);
                _rowKeyWord[r] = string.IsNullOrEmpty(words) || words == "-" ? string.Empty : words.ToUpperInvariant();
                HudText.Set(_rowKey[r], _rowKeyWord[r]);
                // fix/hud-containment-3: one key-hint style: the drawn glyph on the plate, the plate widened to the left for it
                float cw = HudPrompts.SetPlate(_rowGlyph[r], _rowKey[r], _rowKeyWord[r].Length > 0 ? action : null, ControlBindings.ActiveDevice);
                if (_rowKeyWord[r].Length == 0) HudText.Set(_rowKey[r], string.Empty);
                cw = Mathf.Max(68f, cw);
                RectTransform kc = _rowKeyChip[r].rectTransform;
                if (Mathf.Abs(kc.sizeDelta.x - cw) > 0.5f) MenuUi.Place(kc, KitW - 24f - cw, 42f + r * RowH + 9f, cw, 30f);
            }
            // b13-v: the dodge and switch keys from the current bindings (J's defaults while no Dodge binding exists)
            HudText.Set(_dodgeKey, B13Kit.DodgeLabel(B13Kit.KeyOr("Dodge", ControlBindings.ActiveDevice == ControlDevice.Gamepad ? "RB" : "SHIFT")));
            _switchKey = B13Kit.KeyOr("SwitchDigPlace", ControlBindings.ActiveDevice == ControlDevice.Gamepad ? "LB" : "WHEEL");
            string jump = InputGlyphs.Words("Jump");
            HudText.Set(_jumpText, "JUMP IT  " + (string.IsNullOrEmpty(jump) ? "SPACE" : jump.ToUpperInvariant()));
        }

        // ------------------------------------------------------------------ stun, dodge, jump hint

        void TickStatus(World w, MechanicsWorldView view, int me, float realNow)
        {
            MechFeed f = view.Feed;
            double now = view.SimNow;
            bool alive = w.Actors[me].Alive;
            bool stunned = alive && f.Stunned(me, now);
            float stunLeft = stunned ? f.StunLeft01(me, now) : 0f;
            if (MechPhysics.Stun(w.Actors[me], out double ticksLeft, out double ticksLen) && ticksLeft > 0 && ticksLen > 0)
            {
                stunned = alive;   // the sim's own stun (CM_SIM_PHYSICS): exact, frame for frame
                stunLeft = (float)(ticksLeft / ticksLen);
            }
            if (_stun.gameObject.activeSelf != stunned) _stun.gameObject.SetActive(stunned);
            if (stunned) _stunPie.Fill01 = stunLeft;

            if (f.Dodges != _seenDodges)
            {
                _seenDodges = f.Dodges;
                _dodgeAt = realNow;
            }
            float da = realNow - _dodgeAt;
            bool dodge = da >= 0f && da < 0.8f;
            if (_dodge.gameObject.activeSelf != dodge) _dodge.gameObject.SetActive(dodge);
            if (dodge)
            {
                float pop = da < 0.12f ? Mathf.Lerp(1.25f, 1f, da / 0.12f) : 1f;
                _dodge.localScale = new Vector3(pop, pop, 1f);
                _dodgeGroup.alpha = da > 0.55f ? 1f - (da - 0.55f) / 0.25f : 1f;
            }

            Kaiju k = w.Kaiju;
            bool inside = alive && !stunned && k != null && k.State != KaijuState.Egg && k.TelegraphIn > 0 &&
                          SlamRead.Inside(w.X[me], w.Z[me], k.TelegraphX, k.TelegraphZ, w.Config.Kaiju.SlamRadius);
            if (_jump.gameObject.activeSelf != inside) _jump.gameObject.SetActive(inside);
            if (inside && _glyphDirty) RefreshGlyphs();
        }

        // ------------------------------------------------------------------ off-screen damage chevrons (DG-101)

        /// <summary>
        /// b11-af (results chevron): the mechanics HUD (damage chevrons, kit, recap) is hidden once the match has ended
        /// and on the results screen, whatever world is loaded behind it. Pure; tests.
        /// </summary>
        public static bool HiddenOver(CriticalMass.Runtime.Flow.FlowState state, MatchPhase phase) =>
            phase == MatchPhase.Ended || state == CriticalMass.Runtime.Flow.FlowState.Results;

        /// <summary>b11-af: every damage chevron off (a hidden HUD keeps none active, so none can show when it comes back).</summary>
        void HideChevrons()
        {
            for (int i = 0; i < _chev.Length; i++)
                if (_chev[i] != null && _chev[i].gameObject.activeSelf) _chev[i].gameObject.SetActive(false);
            ChevronsShown = 0;
        }

        void TickChevrons(World w, MechanicsWorldView view, int me)
        {
            MechFeed f = view.Feed;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            double now = view.SimNow;
            Vector2 half = _space.rect.size * 0.5f;
            int shown = 0;
            for (int i = 0; i < DamageDirection.Slots; i++)
            {
                bool on = false;
                if (cam != null && i < f.HitCount && i < MechFeed.LocalHits)
                {
                    MechFeed.LocalHit h = f.HitRing[(f.HitCount - 1 - i) % MechFeed.LocalHits];
                    float age = (float)f.Wall(now - h.T);
                    float alpha = DamageDirection.Alpha(age);
                    if (alpha > 0f && h.Cause != DamageCause.Fall)
                    {
                        Vector3 src = SimSpace.ToWorld(h.SrcX, GroundHeight.At(w.Map, h.SrcX, h.SrcZ) + 1.0, h.SrcZ);
                        Vector3 vp = cam.WorldToViewportPoint(src);
                        if (DamageDirection.OffScreen(vp))
                        {
                            Vector2 dir = DamageDirection.ScreenDir(vp);
                            RectTransform rt = _chev[i];
                            rt.anchoredPosition = DamageDirection.EdgePoint(dir, half, 46f);
                            rt.localEulerAngles = new Vector3(0f, 0f, DamageDirection.AngleDeg(dir) + 180f);
                            float s = DamageDirection.Scale(h.Dmg);
                            rt.localScale = new Vector3(s, s, 1f);
                            _chevGroup[i].alpha = alpha;
                            on = true;
                            shown++;
                        }
                    }
                }
                if (_chev[i].gameObject.activeSelf != on) _chev[i].gameObject.SetActive(on);
            }
            ChevronsShown = shown;
        }

        // ------------------------------------------------------------------ "you were hit by"

        void TickRecap(World w, MechanicsWorldView view, int me)
        {
            bool down = !w.Actors[me].Alive;
            if (down && !_recapOpen) OpenRecap(view.Feed, w);
            if (!down && _recapOpen) _recapOpen = false;
            // polish-3 (respawn capture: an EMPTY "KNOCKED OUT" box under the "Knocked out by ..." card): with no hits to
            // list (a fall, a forced knock-out) the box had only its title, and the card above already says how it
            // happened. It shows only when it has rows: no empty boxes.
            bool show = RecapShows(_recapOpen, RecapRowsShown);
            if (_recap.gameObject.activeSelf != show) _recap.gameObject.SetActive(show);
        }

        /// <summary>polish-3: hit rows in the open recap (0 keeps the box hidden).</summary>
        public int RecapRowsShown { get; private set; }

        /// <summary>polish-3: the recap box shows only with at least one hit row (pure; tests).</summary>
        public static bool RecapShows(bool open, int rows) => open && rows > 0;

        /// <summary>Fills the recap from the newest hits (allocates once per knock-out).</summary>
        void OpenRecap(MechFeed f, World w)
        {
            _recapOpen = true;
            int n = Math.Min(Math.Min(f.HitCount, MechFeed.LocalHits), HitRecapWords.Rows);
            for (int i = 0; i < HitRecapWords.Rows; i++)
            {
                if (i >= n) { HudText.Set(_recapRows[i], string.Empty); continue; }
                MechFeed.LocalHit h = f.HitRing[(f.HitCount - 1 - i) % MechFeed.LocalHits];
                HudText.Set(_recapRows[i], RecapLine(h));
                _recapRows[i].color = i == 0 ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color;
            }
            HudText.Set(_recapHead, "YOU WERE HIT BY");
            RecapRowsShown = n;
        }

        /// <summary>One recap line: "TNT  from CRIMSON DEMOLITIONS  55" (KO on the finishing blow).</summary>
        public static string RecapLine(in MechFeed.LocalHit h)
        {
            string blow = HitRecapWords.Blow(h.Cause, h.Cls, h.Attacker >= 0, h.BlastKind);
            string who = HitRecapWords.Who(h.Cause, h.Attacker, h.Team, h.Cls, HitRecapWords.ClassNames);
            string dmg = ((int)Math.Round(h.Dmg)).ToString(CultureInfo.InvariantCulture);
            return blow + "  from " + who + "  " + dmg + (h.Fatal ? "  KO" : string.Empty);
        }

        // ------------------------------------------------------------------ minimap and full map

        void TickMaps(World w, int me)
        {
            if (_mapArea == null && --_hudSearch <= 0)
            {
                _hudSearch = 30;
                if (_hud == null) _hud = FindAnyObjectByType<GameHud>();
                if (_hud != null && _hud.MapArea != null) BuildMini(_hud.MapArea);
            }
            HudInteract hi = HudInteract.Instance;
            FullMapView fm = hi != null ? hi.Map : null;
            if (fm != null && fm.Built && !_fullBuilt) BuildFull(fm);
            if (fm != null && fm.Built && !_legendBuilt) BuildLegend(fm);
            bool full = fm != null && fm.IsOpen && _fullBuilt;
            TeamId viewer = w.Actors[me].Team;
            MinimapProjection proj = MinimapGeometry.Projection;

            // pieces
            BuiltPiece[] ps = w.Builder != null ? w.Builder.Pieces : null;
            int pi = 0;
            if (ps != null)
                for (int k = 0; k < ps.Length && pi < MaxPieces; k++)
                {
                    BuiltPiece p = ps[k];
                    if (!p.Active || p.Carried) continue;
                    Vector2 a0 = proj.ToPixel(p.X, p.Z), a1 = proj.ToPixel(p.X + p.Dx, p.Z + p.Dz);
                    Vector2 d = a1 - a0;
                    float ang = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
                    float lenPx = (float)(p.HalfLen * 2 * MinimapProjection.PxPerUnit);
                    Color c = CMTokens.Team(p.Team);
                    Place(_mPiece[pi], _mPieceS[pi], new Vector2(a0.x, -a0.y), ang, Mathf.Max(6f, lenPx), 5f, c);
                    if (full)
                    {
                        Vector2 v = fm.SimToView(p.X, p.Z);
                        Place(_fPiece[pi], _fPieceS[pi], v, ang, Mathf.Max(10f, lenPx * fm.Scale), 8f, c, FullMapView.InsideView(v));
                    }
                    pi++;
                }
            Hide(_mPiece, pi);
            Hide(_fPiece, full ? pi : 0);

            // your team's mines
            Mine[] ms = w.Demolitions != null ? w.Demolitions.Mines : null;
            int mi = 0;
            if (ms != null)
                for (int k = 0; k < ms.Length && mi < MaxMines; k++)
                {
                    Mine m = ms[k];
                    if (!m.Active || !MineSight.OnMap(m.Team, viewer)) continue;
                    double x = m.Cx + 0.5, z = m.Cz + 0.5;
                    Vector2 px = proj.ToPixel(x, z);
                    Color c = m.Armed ? CMTokens.Team(m.Team) : CMTokens.Stone.Color;
                    Place(_mMine[mi], _mMineS[mi], new Vector2(px.x, -px.y), 0f, 8f, 8f, c);
                    if (full)
                    {
                        Vector2 v = fm.SimToView(x, z);
                        Place(_fMine[mi], _fMineS[mi], v, 0f, 14f, 14f, c, FullMapView.InsideView(v));
                    }
                    mi++;
                }
            Hide(_mMine, mi);
            Hide(_fMine, full ? mi : 0);

            // drops (loose orbs)
            LooseOrb[] orbs = w.Orbs;
            int di = 0;
            if (orbs != null)
                for (int k = 0; k < orbs.Length && di < MaxDrops; k++)
                {
                    LooseOrb o = orbs[k];
                    if (o == null || !o.Active) continue;
                    Color c = o.Kind == BiomassType.Meat ? CMTokens.Meat.Color : CMTokens.Biomass.Color;
                    Vector2 px = proj.ToPixel(o.X, o.Z);
                    Place(_mDrop[di], _mDropS[di], new Vector2(px.x, -px.y), 0f, 5f, 5f, c);
                    if (full)
                    {
                        Vector2 v = fm.SimToView(o.X, o.Z);
                        Place(_fDrop[di], _fDropS[di], v, 0f, 10f, 10f, c, FullMapView.InsideView(v));
                    }
                    di++;
                }
            Hide(_mDrop, di);
            Hide(_fDrop, full ? di : 0);

            // TNT landings
            Bomb[] bs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            int bi = 0;
            if (bs != null)
                for (int k = 0; k < bs.Length && bi < MaxBombs; k++)
                {
                    Bomb b = bs[k];
                    if (!b.Active) continue;
                    Vector2 px = proj.ToPixel(b.X, b.Z);
                    Place(_mBomb[bi], null, new Vector2(px.x, -px.y), 0f, 12f, 12f, CMTokens.MeterOrange.Color);
                    if (full)
                    {
                        Vector2 v = fm.SimToView(b.X, b.Z);
                        Place(_fBomb[bi], null, v, 0f, 20f, 20f, CMTokens.MeterOrange.Color, FullMapView.InsideView(v));
                    }
                    bi++;
                }
            Hide(_mBomb, bi);
            Hide(_fBomb, full ? bi : 0);
        }

        static void Place(RectTransform rt, VectorShape s, Vector2 at, float angleDeg, float w, float h, Color c, bool visible = true)
        {
            if (rt == null) return;
            if (rt.gameObject.activeSelf != visible) rt.gameObject.SetActive(visible);
            if (!visible) return;
            rt.anchoredPosition = at;
            rt.localEulerAngles = new Vector3(0f, 0f, angleDeg);
            Vector2 size = new Vector2(w, h);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            if (s != null && s.FillColor != c) s.SetFill(c);
        }

        static void Hide(RectTransform[] pool, int from)
        {
            for (int i = from; i < pool.Length; i++)
                if (pool[i] != null && pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        static RectTransform Marker(Transform parent, string name, ShapeKind kind, Vector2 anchor, float outline, out VectorShape shape, bool ring = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(8f, 8f);
            shape = go.AddComponent<VectorShape>();
            shape.Kind = kind;
            shape.Radius = kind == ShapeKind.RoundRect ? 2f : 4f;
            shape.FillColor = ring ? new Color(0f, 0f, 0f, 0f) : Color.white;
            shape.OutlinePx = outline;
            shape.RimPx = ring ? 2f : 0f;
            if (ring) shape.RimColor = CMTokens.MeterOrange.Color;
            shape.raycastTarget = false;
            go.SetActive(false);
            return rt;
        }

        void BuildMini(RectTransform mapArea)
        {
            _mapArea = mapArea;
            Vector2 tl = new Vector2(0f, 1f);
            for (int i = 0; i < MaxPieces; i++) _mPiece[i] = Marker(mapArea, "Piece " + i, ShapeKind.RoundRect, tl, 1.5f, out _mPieceS[i]);
            for (int i = 0; i < MaxMines; i++) _mMine[i] = Marker(mapArea, "Mine " + i, ShapeKind.Diamond, tl, 1.5f, out _mMineS[i]);
            for (int i = 0; i < MaxDrops; i++) _mDrop[i] = Marker(mapArea, "Drop " + i, ShapeKind.Circle, tl, 1f, out _mDropS[i]);
            for (int i = 0; i < MaxBombs; i++) _mBomb[i] = Marker(mapArea, "TNT " + i, ShapeKind.Circle, tl, 1.5f, out _, true);
        }

        void BuildFull(FullMapView fm)
        {
            if (fm.Viewport == null) return;
            _fullBuilt = true;
            Vector2 c = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < MaxPieces; i++) _fPiece[i] = Marker(fm.Viewport, "Piece " + i, ShapeKind.RoundRect, c, 2.5f, out _fPieceS[i]);
            for (int i = 0; i < MaxMines; i++) _fMine[i] = Marker(fm.Viewport, "Mine " + i, ShapeKind.Diamond, c, 2.5f, out _fMineS[i]);
            for (int i = 0; i < MaxDrops; i++) _fDrop[i] = Marker(fm.Viewport, "Drop " + i, ShapeKind.Circle, c, 2f, out _fDropS[i]);
            for (int i = 0; i < MaxBombs; i++) _fBomb[i] = Marker(fm.Viewport, "TNT " + i, ShapeKind.Circle, c, 2.5f, out _, true);
        }

        /// <summary>Panel-local top of the extra legend rows: under the full map's own legend and ping row.</summary>
        public const float LegendX = FullMapView.SideX - FullMapView.PanelX, LegendY = 790f;
        /// <summary>Step between these legend rows (30: the four rows and DepthHud's two end inside the panel).</summary>
        public const float LegendStep = 30f;

        void BuildLegend(FullMapView fm)
        {
            if (fm.Panel == null) return;
            _legendBuilt = true;
            Transform p = fm.Panel;
            Color cream = CMTokens.WarmCream.Color;
            float y = LegendY;
            Row(p, y, ShapeKind.RoundRect, CMTokens.Azure.Color, "Ladder or rope bridge, team colour", cream, false); y += LegendStep;
            Row(p, y, ShapeKind.Diamond, CMTokens.Azure.Color, "Your team's mine (enemies see the mound)", cream, false); y += LegendStep;
            Row(p, y, ShapeKind.Circle, CMTokens.Biomass.Color, "Drop to pick up: biomass or meat", cream, false); y += LegendStep;
            Row(p, y, ShapeKind.Circle, CMTokens.MeterOrange.Color, CriticalMass.Runtime.View.Affordance.GrenadeTuning.Name + " about to land", cream, true);
        }

        static void Row(Transform p, float y, ShapeKind kind, Color c, string word, Color cream, bool ring)
        {
            VectorShape s = MenuUi.Shape(p, "Legend " + word, kind, LegendX + 3f, y + 3f, 20f, kind == ShapeKind.RoundRect ? 10f : 20f, ring ? new Color(0f, 0f, 0f, 0f) : c, 4f);
            s.OutlinePx = 2f;
            s.RimPx = ring ? 3f : 0f;
            if (ring) s.RimColor = c;
            MenuUi.Label(p, "Legend Word " + word, CMTokens.Ui, UiScale.TypeMin, cream, TextAnchor.MiddleLeft, LegendX + 38f, y, FullMapView.SideW - 38f, 26f, word, 2f);
        }
    }
}
