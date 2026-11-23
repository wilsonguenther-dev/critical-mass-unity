using System;
using System.Globalization;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Mechanics
{
    /// <summary>
    /// The HUD side of the mechanics depth, on its own canvas (sorting 14: over the class kit, under the banner): the small
    /// persistent "your job now" line (Objective, by class and state), the interact prompt (pass to a Farmer, bank, take food,
    /// base station, rebuild the bridge) with its key through InputGlyphs and the refusal lines the sim answers with, the team
    /// stock strip (wood, water, food; own team), the gather ring, the coin counter with coins flying to it, the power-up timer
    /// (Haste, Might, Ward, with the counter hint), the map-event bar (supply drop, egg exposed, bridge) and the drop's
    /// off-screen pointer, name tags over nearby bots with their answers as speech marks and their order icon, "+N" and
    /// "BLOCKED" pops, and the minimap and full-map markers with their legend rows. Team feed lines go to the ping feed.
    /// View only; never writes the sim. Added to the driver by MechanicsWorldViewInstaller.
    /// </summary>
    /// <summary>
    /// b13-v: the words and places of the hut badge and the corruption meter (pure; tests). The badge ("HUT 3 +15%", "ore
    /// 4/18": banked / next cost) sits right of the match cluster, the meter under it; both are clear of the match and Kaiju
    /// clusters, the kill feed and the centre stack at every screen shape (HudZones registers them as panels).
    /// </summary>
    public static class B13Hud
    {
        public static readonly Rect HutRect = new Rect(HudLayout.RefRect(HudCluster.Match).xMax + HudZones.ColumnGap, 10f,
            HudLayout.RefRect(HudCluster.Kaiju).x - HudZones.ColumnGap - (HudLayout.RefRect(HudCluster.Match).xMax + HudZones.ColumnGap), 54f);
        public static readonly Rect CorruptionRect = new Rect(HutRect.x, HutRect.yMax + 6f, HutRect.width, 60f);
        static readonly string[] HutLines = { "HUT 0 +0%", "HUT 1 +5%", "HUT 2 +10%", "HUT 3 +15%", "HUT 4 +20%", "HUT 5 +25%" };
        /// <summary>The chance bar is full at the 70% cap.</summary>
        public const double ChanceCap = 0.70;

        public static string HutLine(int level) => HutLines[Mathf.Clamp(level, 0, HutLines.Length - 1)];
        static readonly string[] HutChoiceLines = { "HUT 0", "HUT 1", "HUT 2", "HUT 3", "HUT 4", "HUT 5" };
        /// <summary>b15: with hut choices on there is no flat bump, so the badge shows only the level.</summary>
        public static string HutChoiceLine(int level) => HutChoiceLines[Mathf.Clamp(level, 0, HutChoiceLines.Length - 1)];
        public static string OreLine(int banked, int nextCost) => nextCost <= 0 ? "max level" : "ore " + banked + "/" + nextCost;
        public static bool MeterShown(bool on, double pool) => on && pool > 0;
        public static string ChanceLine(double chance) => "CORRUPTION " + Math.Round(Math.Max(0, chance) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        public static string PoolLine(double pool)
        {
            double r = Math.Round(pool, 1);
            string n = r.ToString(r == Math.Floor(r) ? "0" : "0.0", CultureInfo.InvariantCulture);
            return n + (r == 1 ? " crystal" : " crystals");
        }
        public static int SecondsUntil(int tick, int now, double hz) => hz > 0 ? Math.Max(0, (int)Math.Ceiling((tick - now) / hz)) : 0;
        public static string RollLine(int seconds, bool hatched)
        {
            if (seconds < 0) return string.Empty;
            string t = (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
            return (hatched ? "roll when it wakes " : "roll at hatch ") + t;
        }
        public static float ChanceBar01(double chance) => Mathf.Clamp01((float)(chance / ChanceCap));
    }

    /// <summary>b13-v: the badge and meter texts, rebuilt only when their value changes (no allocation while unchanged).</summary>
    public sealed class B13HudCache
    {
        int _banked = int.MinValue, _cost = int.MinValue, _pct = int.MinValue, _poolTenths = int.MinValue, _secs = int.MinValue;
        bool _hatched;
        string _ore = "", _chance = "", _pool = "", _roll = "";

        public string Ore(int banked, int cost)
        {
            if (banked != _banked || cost != _cost) { _banked = banked; _cost = cost; _ore = B13Hud.OreLine(banked, cost); }
            return _ore;
        }

        public string Chance(double chance)
        {
            int pct = (int)Math.Round(Math.Max(0, chance) * 100);
            if (pct != _pct) { _pct = pct; _chance = B13Hud.ChanceLine(chance); }
            return _chance;
        }

        public string Pool(double pool)
        {
            int t = (int)Math.Round(pool * 10);
            if (t != _poolTenths) { _poolTenths = t; _pool = B13Hud.PoolLine(pool); }
            return _pool;
        }

        public string Roll(int seconds, bool hatched)
        {
            if (seconds != _secs || hatched != _hatched) { _secs = seconds; _hatched = hatched; _roll = B13Hud.RollLine(seconds, hatched); }
            return _roll;
        }
    }

    /// <summary>Fix round 5 (item 6, review pass 4: the enemy's name dropped off his health bar on the impact frame): the
    /// name tags were placed here at the default order 0, BEFORE CameraShake (150) adds the hit kick, the bars by GameHud
    /// (200) AFTER it. Fix round 6: moving this whole HUD to order 210 (round 5) broke the player-seat bar tests (F02 / F23 /
    /// D4), so DepthHud is back at the default order and only the tag POSITIONS are re-projected late, by DepthHudTagsLate
    /// (order 210), through the camera as it renders.</summary>
    public sealed class DepthHud : MonoBehaviour
    {
        /// <summary>The late tag pass: after CameraShake (150) and GameHud (200), before LowViewTags (1100).</summary>
        public const int LateOrder = 210;
        readonly Vector3[] _tagHead = new Vector3[Tags];

        /// <summary>Fix round 6: put every shown name tag back on its fighter's head through the camera as it renders now
        /// (after the kick), the same projection the health bars use. Positions only; what shows was decided earlier.</summary>
        public void ReprojectTags()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || _canvas == null || !_canvas.enabled) return;
            for (int i = 0; i < Tags; i++)
            {
                RectTransform rt = _tag[i];
                if (rt == null || _tagActor[i] < 0 || !rt.gameObject.activeSelf) continue;
                if (ToLocal(cam, _tagHead[i], out Vector2 at)) rt.anchoredPosition = at;
            }
        }

        public const int SortingOrder = 14;
        public const int Tags = 20, Bubbles = 8, Floats = 10, Coins = 8, MaxSources = 12, MaxPowers = 6;
        /// <summary>Bot name tags show within this many sim units of the local player.</summary>
        public const double TagRange = 14;

        public static DepthHud Instance { get; private set; }
        /// <summary>True while the match coin counter (right column) shows; the progression coin chip hides then (integration
        /// batch 5: a match showed two coin readouts).</summary>
        public bool CoinsShown => _coins != null && _coins.gameObject.activeSelf;
        /// <summary>The objective line's current words (tests and captures).</summary>
        public string ObjectiveText => _objText != null ? _objText.text : string.Empty;
        public string PromptText => _promptText != null && _prompt.gameObject.activeSelf ? _promptText.text : string.Empty;
        public ObjectiveId Objective { get; private set; }

        Canvas _canvas;
        RectTransform _root, _space;

        // b13-v: the team's hut badge and the corruption meter (top bar, right of the match cluster), the roll banner
        RectTransform _hut, _corrupt;
        Text _hutLevel, _hutOre, _corruptHead, _corruptPool, _corruptRoll;
        VectorShape _corruptFill;
        readonly B13HudCache _b13 = new B13HudCache();
        /// <summary>b13-v: shown flags (tests and captures).</summary>
        public bool HutShown => _hut != null && _hut.gameObject.activeSelf;
        public bool CorruptionShown => _corrupt != null && _corrupt.gameObject.activeSelf;
        SimDriverBehaviour _driver;
        CombatLog _log;

        // objective, prompt
        RectTransform _obj, _prompt;
        Text _objText, _promptText, _promptKey;
        VectorShape _promptPanel, _promptPie, _promptKeyChip;
        InputGlyphView _promptGlyph;
        string _promptAction;
        string _refusal;
        float _refusalUntil = -9f;
        bool _glyphDirty = true;

        // stock strip
        RectTransform _stock;
        readonly Text[] _stockNum = new Text[3];
        readonly int[] _stockSeen = { -1, -1, -1 };
        readonly float[] _stockPopAt = { -9f, -9f, -9f };
        readonly RectTransform[] _stockChip = new RectTransform[3];

        // event bar
        RectTransform _event;
        Text _eventWord, _eventTime;
        VectorShape _eventFill;
        public const float EventW = 560f, EventH = 38f;
        /// <summary>The pill clock's box (right aligned) and the pill word's size range (hud-overlaps-1: below the floor
        /// the word is cut with an ellipsis, HudText.SetOneLine, so it can never run under the clock).</summary>
        public const float ClockX = EventW - 90f, ClockW = 76f, WordX = 14f;
        public const int WordMax = 17, WordFloor = 11;
        /// <summary>The pill word's width: from x 14 to 10 px before the clock (x EventW - 90).</summary>
        public const float WordW = EventW - 90f - 10f - 14f;

        // gather ring
        RectTransform _gather;
        VectorShape _gatherPie;

        // coins and power-up
        RectTransform _coins, _power;
        Text _coinNum, _powerName, _powerTime, _powerHint;
        VectorShape _powerIcon, _powerPie;
        RawImage _powerArt;
        readonly RectTransform[] _coinFly = new RectTransform[Coins];
        int _powerKindShown = -1;

        // tags, bubbles, floats
        readonly RectTransform[] _tag = new RectTransform[Tags];
        readonly Text[] _tagText = new Text[Tags];
        readonly VectorShape[] _tagOrder = new VectorShape[Tags];
        readonly int[] _tagActor = new int[Tags];
        readonly RectTransform[] _bubble = new RectTransform[Bubbles];
        readonly Text[] _bubbleText = new Text[Bubbles];
        readonly RectTransform[] _float = new RectTransform[Floats];
        readonly Text[] _floatText = new Text[Floats];

        // off-screen pointer to the drop
        RectTransform _pointer;

        // maps
        GameHud _hud;
        RectTransform _mapArea;
        int _hudSearch, _logSearch;
        readonly RectTransform[] _mSrc = new RectTransform[MaxSources], _fSrc = new RectTransform[MaxSources];
        readonly VectorShape[] _mSrcS = new VectorShape[MaxSources], _fSrcS = new VectorShape[MaxSources];
        readonly RectTransform[] _mPow = new RectTransform[MaxPowers], _fPow = new RectTransform[MaxPowers];
        readonly VectorShape[] _mPowS = new VectorShape[MaxPowers], _fPowS = new VectorShape[MaxPowers];
        RectTransform _mDrop, _fDrop, _mBridge, _fBridge, _mEgg, _fEgg;
        bool _fullBuilt, _legendBuilt;

        static readonly string[] Clock = BuildClock();
        static string[] BuildClock()
        {
            var a = new string[601];
            for (int i = 0; i < a.Length; i++) a[i] = (i / 60).ToString(CultureInfo.InvariantCulture) + ":" + (i % 60).ToString("00", CultureInfo.InvariantCulture);
            return a;
        }

        Action<string, int, int> _onLine;
        Action<string, float> _onPrompt;

        void Awake()
        {
            Instance = this;
            _driver = GetComponent<SimDriverBehaviour>();
            DepthHudTagsLate late = GetComponent<DepthHudTagsLate>();
            if (late == null) late = gameObject.AddComponent<DepthHudTagsLate>();
            late.Hud = this;
            DepthWords.Ensure();
            Build();
            _onLine = OnLine;
            _onPrompt = OnPrompt;
            DepthOut.Line += _onLine;
            DepthOut.Prompt += _onPrompt;
            DepthOut.CorruptionRolled += OnRoll;
            ControlBindings.BindingsChanged += MarkGlyphs;
            ControlBindings.ActiveDeviceChanged += OnDevice;
            PingWords.LineOverride = PingLine;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DepthOut.Line -= _onLine;
            DepthOut.Prompt -= _onPrompt;
            DepthOut.CorruptionRolled -= OnRoll;
            ControlBindings.BindingsChanged -= MarkGlyphs;
            ControlBindings.ActiveDeviceChanged -= OnDevice;
            if (PingWords.LineOverride == (Func<CriticalMass.Runtime.Hud.Interact.Ping, string>)PingLine) PingWords.LineOverride = null;
        }

        void MarkGlyphs() => _glyphDirty = true;
        void OnDevice(ControlDevice d) => _glyphDirty = true;

        /// <summary>The ping feed's line for a call by a teammate or a bot ("Brook: Help!"); null keeps "You: ...".</summary>
        string PingLine(CriticalMass.Runtime.Hud.Interact.Ping p)
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || p.Owner == 0 || p.Owner < 0 || p.Owner >= w.ActorCount) return null;
            int c = Mathf.Clamp((int)w.Actors[p.Owner].Cls, 0, KillWords.ClassUpper.Length - 1);
            return DepthLines.Call(w.Epoch, p.Owner, 0, (int)p.Kind, KillWords.ClassUpper[c]);
        }

        void OnLine(string line, int pickup, int call)
        {
            HudInteract hi = HudInteract.Instance;
            if (hi == null) return;
            ShapeKind shape = pickup > 0 ? PowerTimers.Icon[Mathf.Clamp(pickup, 0, 3)] : call >= 0 ? PingWords.Shape[Mathf.Clamp(call, 0, 3)] : ShapeKind.Diamond;
            Color c = pickup > 0 ? (Color)PowerTimers.Colour[Mathf.Clamp(pickup, 0, 3)] : call >= 0 ? PingWords.Colour((PingKind)Mathf.Clamp(call, 0, 3)) : CMTokens.Biomass.Color;
            hi.Feed.AddLine(line, shape, c, Time.unscaledTime);
        }

        void OnPrompt(string text, float seconds)
        {
            _refusal = text;
            _refusalUntil = Time.unscaledTime + seconds;
        }

        // ------------------------------------------------------------------ build

        static VectorShape Chip(Transform p, string n, ShapeKind k, float x, float y, float w, float h, Color fill, float r = 10f)
        {
            VectorShape s = MenuUi.Shape(p, n, k, x, y, w, h, fill, r);
            s.OutlinePx = 3f;
            s.RimPx = 0f;
            return s;
        }

        void Build()
        {
            _canvas = InteractUi.MakeHudCanvas(transform, "Depth HUD Canvas", SortingOrder, out _root, out _space);
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color, panel = CMTokens.PanelNavy.Color;

            // the "your job now" line: top centre, small and steady
            _obj = MenuUi.Rt(_space, "Objective");
            MenuUi.Place(_obj, (HudLayout.RefW - HudZones.JobW) * 0.5f, HudZones.JobY, HudZones.JobW, HudZones.JobH);
            VectorShape op = Chip(_obj, "Panel", ShapeKind.RoundRect, 0f, 0f, 760f, 40f, CMTokens.WithAlpha(panel, 0.92f), 14f);
            op.RimPx = 2f;
            op.RimColor = brass;
            Chip(_obj, "Mark", ShapeKind.TriangleRight, 14f, 11f, 16f, 18f, brass, 3f);
            _objText = MenuUi.Label(_obj, "Text", CMTokens.Ui, 20, cream, TextAnchor.MiddleLeft, 40f, 0f, 708f, 40f, "", 2f);
            _obj.gameObject.SetActive(false);

            BuildB13(cream, brass, panel);

            // team stock: three chips under the objective (own team only)
            _stock = MenuUi.Rt(_space, "Team Stock");
            HudZones.RegisterPanel(_stock);   // fix/hud-containment-5: the top-centre stack masks world labels
            HudZones.RegisterPanel(_obj);
            MenuUi.Place(_stock, (HudLayout.RefW - HudZones.StockW) * 0.5f, HudZones.StockY, HudZones.StockW, HudZones.StockH);
            ShapeKind[] icons = { ShapeKind.Hexagon, ShapeKind.Egg, ShapeKind.Circle };
            Color[] cols = { new Color32(160, 116, 72, 255), new Color32(96, 176, 232, 255), CMTokens.Biomass.Color };
            for (int k = 0; k < 3; k++)
            {
                RectTransform chip = MenuUi.Rt(_stock, "Chip " + k);
                // fix/hud-containment-3 (walk: WATER clipped): 150 px chips, the word and the count in their own columns
                MenuUi.Place(chip, k * (HudZones.StockChipW + HudZones.StockChipGap), 0f, HudZones.StockChipW, 40f);
                Chip(chip, "Panel", ShapeKind.RoundRect, 0f, 0f, HudZones.StockChipW, 40f, CMTokens.WithAlpha(panel, 0.92f), 12f);
                // hud-art-1: the painted resource icon (UI/Icons/res-*), the vector chip only when the file is missing
                Texture2D tex = UiIcons.Get(UiIcons.Stock[k], 2);
                if (tex != null)
                {
                    RawImage ri = MenuUi.Rt(chip, "Icon").gameObject.AddComponent<RawImage>();
                    ri.texture = tex;
                    ri.raycastTarget = false;
                    MenuUi.Place(ri.rectTransform, 6f, 4f, 32f, 32f);
                }
                else Chip(chip, "Icon", icons[k], 10f, 9f, 22f, 22f, cols[k], 4f);
                MenuUi.Fit(MenuUi.Label(chip, "Word", CMTokens.Ui, 13, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleLeft, 42f, 1f, 58f, 38f,
                    k == 0 ? DepthWords.Wood : k == 1 ? DepthWords.Water : DepthWords.Food, 1.5f), 9);   // hud-containment-1: "WATER" ran under its count
                _stockNum[k] = MenuUi.Label(chip, "Count", CMTokens.Mono, 22, cream, TextAnchor.MiddleRight, 102f, 0f, HudZones.StockChipW - 108f, 40f, "0", 2f);
                _stockChip[k] = chip;
            }
            _stock.gameObject.SetActive(false);

            // map-event bar
            _event = MenuUi.Rt(_space, "Map Event");
            HudZones.RegisterPanel(_event);
            MenuUi.Place(_event, (HudLayout.RefW - EventW) * 0.5f, HudZones.EventY, EventW, EventH);
            Chip(_event, "Track", ShapeKind.RoundRect, 0f, 0f, EventW, EventH, CMTokens.WithAlpha(panel, 0.92f), 12f);
            _eventFill = Chip(_event, "Fill", ShapeKind.RoundRect, 3f, 3f, EventW - 6f, EventH - 6f, CMTokens.MeterOrange.Color, 10f);
            _eventFill.OutlinePx = 0f;
            // hud-art-1: the word ends 10 px short of the clock and shrinks to fit (SetOneLine). Best fit never worked here:
            // Unity ignores it while the text overflows horizontally, so the egg line ran under the clock.
            _eventWord = MenuUi.Label(_event, "Word", CMTokens.Display, 17, cream, TextAnchor.MiddleLeft, WordX, 0f, WordW, EventH, "", 2.5f);
            _eventTime = MenuUi.Label(_event, "Time", CMTokens.Mono, 20, cream, TextAnchor.MiddleRight, ClockX, 0f, ClockW, EventH, "", 2.5f);
            _event.gameObject.SetActive(false);

            // interact prompt: lower centre (key chip, words, a progress ring for gathering or the bridge repair)
            _prompt = MenuUi.Rt(_space, "Depth Prompt");
            Rect pz = HudZones.Prompt;   // owner walk 1: under the lower lane (the intro line and the recap drew over it)
            MenuUi.Place(_prompt, pz.x, pz.y, pz.width, pz.height);
            _promptPanel = Chip(_prompt, "Panel", ShapeKind.RoundRect, 0f, 0f, 520f, 50f, panel, 14f);
            _promptPanel.OutlinePx = 4f;
            _promptPanel.RimPx = 2f;
            _promptPanel.RimColor = cream;
            _promptKeyChip = Chip(_prompt, "Key Chip", ShapeKind.RoundRect, 10f, 9f, 74f, 32f, CMTokens.VoidNavy.Color, 8f);
            _promptKeyChip.RimPx = 2f;
            _promptKeyChip.RimColor = cream;
            _promptKey = MenuUi.Label(_promptKeyChip.transform, "Key", CMTokens.Ui, 15, cream, TextAnchor.MiddleCenter, 0f, 0f, 74f, 32f, "", 2f);
            _promptKey.resizeTextMinSize = 9;
            _promptKey.resizeTextMaxSize = 15;
            MenuUi.Contain(_promptKey, 1);
            _promptGlyph = CriticalMass.Runtime.Menu.HudPrompts.PlateGlyph(_promptKeyChip.transform);   // fix/hud-containment-3: one key-hint style
            _promptText = MenuUi.Label(_prompt, "Words", CMTokens.Ui, 20, cream, TextAnchor.MiddleLeft, 96f, 0f, 370f, 50f, "", 2f);
            _promptText.resizeTextMinSize = 12;
            _promptText.resizeTextMaxSize = 20;
            MenuUi.Contain(_promptText, 2);   // key plus words; a long line wraps to a second, then shrinks, then ends with an ellipsis
            _promptPie = Chip(_prompt, "Progress", ShapeKind.Pie, 474f, 9f, 32f, 32f, brass, 16f);
            _promptPie.EmptyColor = CMTokens.MeterEmpty.Color;
            _prompt.gameObject.SetActive(false);

            // gather ring over the local player
            _gather = InteractUi.Centred(_space, "Gather Ring", 46f, 46f);
            _gatherPie = Chip(_gather, "Pie", ShapeKind.Pie, 0f, 0f, 46f, 46f, brass, 23f);
            _gatherPie.EmptyColor = CMTokens.MeterEmpty.Color;
            _gather.gameObject.SetActive(false);

            // coin counter (bottom right) and power-up timer beside it
            _coins = MenuUi.Rt(_space, "Coins");
            Rect cr = HudZones.Coins;   // owner walk 1: above the minimap frame, not on it
            MenuUi.Place(_coins, cr.x, cr.y, cr.width, cr.height);
            Chip(_coins, "Panel", ShapeKind.RoundRect, 0f, 0f, 168f, 52f, panel, 14f);
            VectorShape coin = Chip(_coins, "Coin", ShapeKind.Circle, 12f, 11f, 30f, 30f, PowerTimers.Colour[0], 15f);
            coin.RimPx = 2f;
            coin.RimColor = new Color32(196, 140, 40, 255);
            MenuUi.Label(_coins, "Word", CMTokens.Ui, 14, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleLeft, 50f, 0f, 60f, 52f, DepthWords.Pickup(0), 1.5f);
            _coinNum = MenuUi.Label(_coins, "Count", CMTokens.Mono, 24, cream, TextAnchor.MiddleRight, 96f, 0f, 60f, 52f, "0", 2f);
            _coins.gameObject.SetActive(false);
            HudKeepOut.Add(_coins);
            for (int i = 0; i < Coins; i++)
            {
                _coinFly[i] = InteractUi.Centred(_space, "Coin Fly " + i, 22f, 22f);
                Chip(_coinFly[i], "Coin", ShapeKind.Circle, 0f, 0f, 22f, 22f, PowerTimers.Colour[0], 11f);
                _coinFly[i].gameObject.SetActive(false);
            }

            _power = MenuUi.Rt(_space, "Power Up");
            Rect pr = HudZones.Power;
            MenuUi.Place(_power, pr.x, pr.y, pr.width, pr.height);
            VectorShape pp = Chip(_power, "Panel", ShapeKind.RoundRect, 0f, 0f, 330f, 68f, panel, 14f);
            pp.RimPx = 2f;
            _powerPie = Chip(_power, "Pie", ShapeKind.Pie, 10f, 10f, 48f, 48f, cream, 24f);
            _powerPie.EmptyColor = CMTokens.MeterEmpty.Color;
            _powerIcon = Chip(_power, "Icon", ShapeKind.Shield, 20f, 20f, 28f, 28f, CMTokens.VoidNavy.Color, 4f);
            _powerIcon.OutlinePx = 0f;
            // hud-art-1: the painted power-up icon over the timer pie (the vector glyph stays as the fallback)
            _powerArt = MenuUi.Rt(_power, "Art").gameObject.AddComponent<RawImage>();
            _powerArt.raycastTarget = false;
            MenuUi.Place(_powerArt.rectTransform, 12f, 12f, 44f, 44f);
            _powerArt.gameObject.SetActive(false);
            _powerName = MenuUi.Label(_power, "Name", CMTokens.Display, 22, cream, TextAnchor.MiddleLeft, 68f, 4f, 170f, 32f, "", 2.5f);
            _powerTime = MenuUi.Label(_power, "Time", CMTokens.Mono, 22, cream, TextAnchor.MiddleRight, 240f, 4f, 76f, 32f, "", 2f);
            _powerHint = MenuUi.Label(_power, "Counter", CMTokens.Ui, 13, CMTokens.WithAlpha(cream, 0.8f), TextAnchor.MiddleLeft, 68f, 36f, 250f, 26f, "", 1.5f);
            _powerHint.resizeTextMinSize = 9;
            _powerHint.resizeTextMaxSize = 13;
            MenuUi.Contain(_powerHint, 1);
            _power.gameObject.SetActive(false);
            HudKeepOut.Add(_power);

            // name tags, speech marks, floating pops
            for (int i = 0; i < Tags; i++)
            {
                RectTransform rt = InteractUi.Centred(_space, "Name Tag " + i, 200f, 26f);
                _tagText[i] = MenuUi.Label(rt, "Name", CMTokens.Ui, 16, cream, TextAnchor.MiddleCenter, 0f, 0f, 200f, 26f, "", 2.5f);
                _tagOrder[i] = Chip(rt, "Order", ShapeKind.TriangleDown, 92f, -20f, 16f, 16f, CMTokens.MeterOrange.Color, 3f);
                _tagOrder[i].OutlinePx = 2f;
                rt.gameObject.SetActive(false);
                _tag[i] = rt;
                _tagActor[i] = -1;
            }
            for (int i = 0; i < Bubbles; i++)
            {
                RectTransform rt = InteractUi.Centred(_space, "Speech " + i, 150f, 32f);
                VectorShape b = Chip(rt, "Bubble", ShapeKind.RoundRect, 0f, 0f, 150f, 32f, cream, 12f);
                b.OutlinePx = 3f;
                _bubbleText[i] = MenuUi.Label(rt, "Words", CMTokens.Ui, 16, CMTokens.VoidNavy.Color, TextAnchor.MiddleCenter, 0f, 0f, 150f, 32f, "", 0f);
                rt.gameObject.SetActive(false);
                _bubble[i] = rt;
            }
            for (int i = 0; i < Floats; i++)
            {
                RectTransform rt = InteractUi.Centred(_space, "Pop " + i, 120f, 30f);
                _floatText[i] = MenuUi.Label(rt, "Words", CMTokens.Display, 22, brass, TextAnchor.MiddleCenter, 0f, 0f, 120f, 30f, "", 2.5f);
                rt.gameObject.SetActive(false);
                _float[i] = rt;
            }

            // the supply drop's off-screen pointer (both teams)
            _pointer = InteractUi.Centred(_space, "Drop Pointer", 50f, 40f);
            VectorShape ptr = Chip(_pointer, "Shape", ShapeKind.TriangleDown, 0f, 0f, 50f, 40f, PowerTimers.Colour[0], 6f);
            ptr.OutlinePx = 4f;
            ptr.RimPx = 2f;
            ptr.RimColor = cream;
            _pointer.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.NameTags, cost); }

        void LateUpdateCost()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_log == null && --_logSearch <= 0) { _logSearch = 30; _log = FindAnyObjectByType<CombatLog>(); }
            World w = _driver != null ? _driver.World : null;
            DepthWorldView view = DepthWorldView.Active;
            InteractUi.UpdateScale(_canvas);
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            // owner walk 1: the depth HUD (job line, prompt, coins, power-up) steps away under the scoreboard (its prompt drew
            // through the board's bottom edge)
            bool hide = w == null || view == null || (_log != null && _log.Suppressed) || w.Match.Phase == MatchPhase.Ended || Scoreboard.AnyOpen;
            if (_canvas.enabled == hide) _canvas.enabled = !hide;
            if (hide) return;
            int me = view.Feed.LocalActor;
            if (me < 0 || me >= w.ActorCount) return;
            float real = Time.unscaledTime;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            bool on = DepthRead.On(w);
            TickObjective(w, me);
            TickPrompt(w, me, real);
            TickStock(w, me, on, real);
            TickEvent(w, view, on);
            TickGather(w, me, cam, on);
            TickCoins(w, view, me, cam, on, real);
            TickPower(w, me, on);
            TickB13(w, me);
            // fix/hud-containment-3 (by eye: name tags over the knock-out recap): no world name tags under a full panel
            TickTags(w, view, me, cam, on && !HudZones.Blocked(HudZone.Callout, HudPriority.Lockout));
            TickFloats(w, view, cam);
            TickPointer(w, cam, on);
            TickMaps(w, on, real);
        }

        bool ToLocal(UnityEngine.Camera cam, Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            if (cam == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) return false;
            // review fix round 2 (pass-2 defect 4b, the name 120-200 px off its health bar during a hit): the canvas plane
            // sits 0.42 u in front of the camera and only follows it when the canvas syncs, so a ray cast against it while
            // the camera kicks or swings (fight framing) lands far off. Screen pixels / scale, the same math as the health
            // bar (Hud/Player UnitStack), keeps the name and the bar on one anchor. _space fills the screen, centre pivot.
            float sf = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            local = (new Vector2(sp.x, sp.y) - cam.pixelRect.center) / sf;
            return true;
        }

        static Vector3 Head(World w, int id, double alpha, float lift)
        {
            double x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha, z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
            return SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + lift, z);
        }

        // ------------------------------------------------------------------ objective and prompt

        /// <summary>
        /// fix/hud-containment-5: a top-centre stack piece enters over HudZones.StackIn and leaves over StackOut instead of
        /// popping; when the lane is taken (laneFree false) it leaves at once so it never sits under a banner. Returns true
        /// while the piece is on screen (the caller keeps it current).
        /// </summary>
        static bool StackFade(RectTransform rt, bool want, bool laneFree)
        {
            CanvasGroup g = rt.GetComponent<CanvasGroup>();
            if (g == null) { g = rt.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false; g.alpha = 0f; }
            bool on = rt.gameObject.activeSelf;
            if (!laneFree || (!want && (!on || !Application.isPlaying)))
            {
                if (on) rt.gameObject.SetActive(false);
                g.alpha = 0f;
                return false;
            }
            if (want && !on) { rt.gameObject.SetActive(true); g.alpha = Application.isPlaying ? 0f : 1f; }
            float dt = Time.unscaledDeltaTime;
            g.alpha = want ? Mathf.Min(1f, g.alpha + dt / HudZones.StackIn) : Mathf.Max(0f, g.alpha - dt / HudZones.StackOut);
            if (!want && g.alpha <= 0f) { rt.gameObject.SetActive(false); return false; }
            return true;
        }

        void TickObjective(World w, int me)
        {
            ObjectiveFacts f = View.Mechanics.Objective.Facts(w, me);
            ObjectiveId id = View.Mechanics.Objective.For(f);
            Objective = id;
            // Integration batch 4: the job line sits on the top of the centre lane; it steps back while the autopilot banner or a
            // big-moment takeover holds the lane (HudZones), and comes back the frame after.
            // review fix round 2 (pass-2 N4): no carriers-and-hill job line in Duel or the Practice Range (no Kaiju)
            bool show = id != ObjectiveId.None && w.Match.Phase != MatchPhase.Ended && !HudZones.Blocked(HudZone.Callout, HudPriority.Tip) &&
                        !HudState.NoKaijuIn(w.Config);   // owner walk 1: lane resident, steps back for any banner
            bool want = show;
            show = StackFade(_obj, want, !HudZones.Blocked(HudZone.Callout, HudPriority.Tip));
            if (show && want) HudText.Set(_objText, View.Mechanics.Objective.Words(id, f.Cls));
        }

        int _promptSlot = -2;

        void TickPrompt(World w, int me, float real)
        {
            Actor a = w.Actors[me];
            string words = null, action = null;
            float progress = -1f;
            bool refusal = real < _refusalUntil && !string.IsNullOrEmpty(_refusal);
            if (refusal) words = _refusal;
            else if (a.Alive && DepthRead.On(w))
            {
                bool food = (a.CarryAmount > 0 && a.Carry == CarryKind.Biomass) || a.CarryMeat > 0;
                // b15 item 4: no BANK prompt for a water load while banked water has no spender (it still goes to the egg)
                bool woodWater = a.CarryAmount > 0 && ((int)a.Carry == DepthEvents.CarryWood ||
                                                       ((int)a.Carry == DepthEvents.CarryWater && DeadSystemViews.BankedWaterShown(w)));
                ObjectiveFacts f = View.Mechanics.Objective.Facts(w, me);
                if (f.NearBrokenBridge && a.Cls == ClassId.Builder && DeadSystemViews.BridgeRepairShown(w))   // b15 item 4
                {
                    words = f.WoodStock >= DepthRead.RepairWood(w) ? DepthWords.RepairPrompt : DepthWords.RepairNeedWood;
                    action = "Interact";
                    progress = DepthRead.RepairProgress01(w);
                }
                else if (food && a.Cls != ClassId.Farmer && DepthRead.HandoffTarget(w, me) >= 0) { words = DepthWords.PassToFarmer; action = "Interact"; }
                else if ((food || woodWater) && f.InBase) { words = DepthWords.Bank; action = "Interact"; }
                else if (!food && a.CarryAmount <= 0 && a.Cls == ClassId.Farmer && f.InBase && f.FoodStock > 0) { words = DepthWords.Withdraw; action = "Interact"; }
                else if (f.InStation && !StationPanel.IsOpen) { words = DepthWords.OpenStation; action = "OpenStation"; }
                float g = DepthRead.GatherProgress01(w, me);
                if (words == null && g > 0f) { words = DepthWords.Gathering; progress = g; }
            }
            bool show = words != null;
            // b14: one stacking region for the bottom-centre prompts (PromptStack): the station prompt has the first slot,
            // the terrain pill and the Builder's tool strip queue behind it, at most two show, none overlaps.
            int slot = -1;
            if (show) { PromptStack.Want(PromptStack.Source.Station); slot = PromptStack.SlotOf(PromptStack.Source.Station); }
            else PromptStack.Release(PromptStack.Source.Station);
            show = show && slot >= 0;
            if (_prompt.gameObject.activeSelf != show) _prompt.gameObject.SetActive(show);
            if (show) HudZones.HoldPromptRow();   // fix/hud-containment-3: the terrain pill yields the shared prompt row
            if (!show) return;
            if (slot != _promptSlot)
            {
                _promptSlot = slot;
                _prompt.anchorMin = _prompt.anchorMax = new Vector2(0.5f, 0f);
                _prompt.pivot = new Vector2(0.5f, 0f);
                _prompt.sizeDelta = new Vector2(HudZones.PromptW, HudZones.PromptH);
                _prompt.anchoredPosition = new Vector2(PromptStack.CentreX[(int)PromptStack.Source.Station], PromptStack.Bottom(slot, HudZones.PromptH));
            }
            HudText.Set(_promptText, words);
            _promptPanel.SetRim(refusal ? (Color)CMTokens.MeterOrange.Color : CMTokens.WarmCream.Color);
            if (action != _promptAction) { _promptAction = action; _glyphDirty = true; }
            bool keyOn = action != null;
            if (_promptKeyChip.gameObject.activeSelf != keyOn) _promptKeyChip.gameObject.SetActive(keyOn);
            bool pieOn = progress >= 0f;
            if (_promptPie.gameObject.activeSelf != pieOn) _promptPie.gameObject.SetActive(pieOn);
            if (pieOn) _promptPie.Fill01 = progress;
            if (_glyphDirty)
            {
                _glyphDirty = false;
                string k = keyOn ? InputGlyphs.Words(action) : string.Empty;
                bool has = !string.IsNullOrEmpty(k) && k != "-";
                CriticalMass.Runtime.Menu.HudPrompts.SetPlate(_promptGlyph, _promptKey, has ? action : null, ControlBindings.ActiveDevice);
                HudText.Set(_promptKey, has ? k.ToUpperInvariant() : string.Empty);
            }
        }

        // ------------------------------------------------------------------ stock, event bar, gather

        void TickStock(World w, int me, bool on, float real)
        {
            bool show = on && DepthRead.SourcesOn(w) && !HudZones.Blocked(HudZone.Callout, HudPriority.Tip);   // owner walk 1: lane resident, steps back for any banner
            if (!StackFade(_stock, show, !HudZones.Blocked(HudZone.Callout, HudPriority.Tip))) return;
            TeamId t = w.Actors[me].Team;
            LayoutStock(DeadSystemViews.BankedWaterShown(w));
            for (int k = 0; k < 3; k++)
            {
                // first-minutes-1 (walk 1 #6): the WOOD chip shows the one team wood number (BuilderBank.Whole), the same as the
                // class card, kit footer and build prompt. It read the banked stock alone (WOOD 0 beside TEAM WOOD 2).
                int n = k == DepthRead.StockWood ? BuilderBank.Whole(w, t) : DepthRead.Stock(w, t, k);
                if (n != _stockSeen[k])
                {
                    if (_stockSeen[k] >= 0) _stockPopAt[k] = real;
                    _stockSeen[k] = n;
                    HudText.Set(_stockNum[k], DepthLines.Int(n));
                }
                float age = real - _stockPopAt[k];
                float s = age >= 0f && age < 0.2f && !FeelGate.ReduceMotion ? 1f + 0.18f * (1f - age / 0.2f) : 1f;
                _stockChip[k].localScale = new Vector3(s, s, 1f);
            }
        }

        int _stockLayout = -1;

        /// <summary>b15 item 4: the WATER chip hides while banked water has no spender; the other chips close up, centred.</summary>
        void LayoutStock(bool water)
        {
            int key = water ? 1 : 0;
            if (key == _stockLayout) return;
            _stockLayout = key;
            int n = water ? 3 : 2;
            float used = n * HudZones.StockChipW + (n - 1) * HudZones.StockChipGap, x0 = (HudZones.StockW - used) * 0.5f;
            int slot = 0;
            for (int k = 0; k < 3; k++)
            {
                bool on = k != 1 || water;
                if (_stockChip[k].gameObject.activeSelf != on) _stockChip[k].gameObject.SetActive(on);
                if (!on) continue;
                MenuUi.Place(_stockChip[k], x0 + slot * (HudZones.StockChipW + HudZones.StockChipGap), 0f, HudZones.StockChipW, 40f);
                slot++;
            }
        }

        /// <summary>b15 item 4 (tests): the stock chip k is on show.</summary>
        public bool StockChipShown(int k) => k >= 0 && k < 3 && _stockChip[k] != null && _stockChip[k].gameObject.activeSelf;

        void TickEvent(World w, DepthWorldView view, bool on)
        {
            string word = null;
            int ticksLeft = 0, span = 1;
            Color col = CMTokens.MeterOrange.Color;
            if (on && DepthRead.EventsOn(w))
            {
                DepthFeed f = view.Feed;
                int ds = DepthRead.DropState(w, out _, out _, out int dtick);
                int bs = DepthRead.BridgeState(w, out int btick, out _);
                int egg = DepthRead.EggExposedUntil(w);
                if (egg > w.Tick) { word = DepthWords.EggExposedPill; ticksLeft = egg - w.Tick; span = DepthRead.EggWindowTicks(w); col = CMTokens.EggSeam.Color; }
                else if (ds == DepthRead.DropIncoming) { word = DepthWords.DropIncoming; ticksLeft = dtick - w.Tick; span = Math.Max(1, (int)(8 * w.Config.SimHz)); col = PowerTimers.Colour[0]; }
                else if (ds == DepthRead.DropLanded)
                {
                    int contest = DepthRead.DropContest(w);
                    word = contest == DepthRead.ContestBoth ? DepthWords.DropContested : DepthWords.DropLanded;
                    ticksLeft = dtick - w.Tick;
                    span = Math.Max(1, (int)(30 * w.Config.SimHz));
                    col = contest == DepthRead.ContestAzure ? CMTokens.Azure.Color : contest == DepthRead.ContestCrimson ? CMTokens.Crimson.Color : (Color)PowerTimers.Colour[0];
                }
                else if (bs == DepthRead.BridgeWarned) { word = DepthWords.BridgeWarning; ticksLeft = btick - w.Tick; span = Math.Max(1, (int)(8 * w.Config.SimHz)); col = CMTokens.Crimson.Color; }
                else if (bs == DepthRead.BridgeBroken) { word = DepthWords.BridgeCollapsed; ticksLeft = btick - w.Tick; span = Math.Max(1, (int)(45 * w.Config.SimHz)); col = CMTokens.Stone.Color; }
                if (f == null) word = null;
            }
            // The map-event timer shares the centre lane at the bottom of the order: it waits while a Kaiju callout, a banner,
            // the lockout or autopilot line or a big-moment takeover holds the lane (integration batch 4).
            bool show = word != null && !HudZones.Blocked(HudZone.Callout, HudPriority.Tip);   // owner walk 1: lane resident
            if (!StackFade(_event, show, !HudZones.Blocked(HudZone.Callout, HudPriority.Tip)) || word == null) return;
            HudText.SetOneLine(_eventWord, word, WordMax, WordFloor);
            int secs = StationRules.Seconds(ticksLeft, w.Config.SimHz);
            HudText.Set(_eventTime, Clock[Mathf.Clamp(secs, 0, Clock.Length - 1)]);
            float frac = Mathf.Clamp01((float)ticksLeft / Math.Max(1, span));
            _eventFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, (EventW - 6f) * frac), EventH - 6f);
            _eventFill.rectTransform.anchoredPosition = new Vector2(3f + _eventFill.rectTransform.sizeDelta.x * 0.5f, -EventH * 0.5f);
            _eventFill.SetFill(CMTokens.WithAlpha(col, 0.55f));
        }

        void TickGather(World w, int me, UnityEngine.Camera cam, bool on)
        {
            float g = on ? DepthRead.GatherProgress01(w, me) : 0f;
            Vector2 p = Vector2.zero;
            bool show = g > 0f && w.Actors[me].Alive && ToLocal(cam, Head(w, me, _driver.Alpha, 2.9f), out p);
            if (_gather.gameObject.activeSelf != show) _gather.gameObject.SetActive(show);
            if (!show) return;
            _gather.anchoredPosition = p;
            _gatherPie.Fill01 = g;
        }

        // ------------------------------------------------------------------ coins and power-up

        void TickCoins(World w, DepthWorldView view, int me, UnityEngine.Camera cam, bool on, float real)
        {
            bool show = on && DepthRead.PickupCapacity(w) > 0 && DeadSystemViews.CoinsShown();   // b15 item 4: coins have no sink
            if (_coins.gameObject.activeSelf != show) _coins.gameObject.SetActive(show);
            if (!show) { for (int i = 0; i < Coins; i++) if (_coinFly[i].gameObject.activeSelf) _coinFly[i].gameObject.SetActive(false); return; }
            HudText.Set(_coinNum, DepthLines.Int(DepthRead.CoinsPicked(w, me)));
            DepthFeed f = view.Feed;
            // the counter's coin icon in centre space (MenuUi.Place positions from the space's top-left corner, y down)
            // owner walk 1: the counter lives in its anchor frame, so its centre is read through the transforms
            Vector2 cc = _space.InverseTransformPoint(_coins.position);
            Vector2 target = new Vector2(cc.x - 84f + 27f, cc.y);
            bool started = ToLocal(cam, Head(w, me, _driver.Alpha, 1.8f), out Vector2 from);
            const float life = 0.55f;
            for (int i = 0; i < Coins; i++)
            {
                float age = (float)f.Wall(view.SimNow - f.CoinAt[i]);
                bool fly = started && age >= 0f && age < life;
                if (_coinFly[i].gameObject.activeSelf != fly) _coinFly[i].gameObject.SetActive(fly);
                if (!fly) continue;
                float t = PowerTimers.Fly01(age, life);
                _coinFly[i].anchoredPosition = Vector2.Lerp(from, target, t) + Vector2.up * (FeelGate.ReduceMotion ? 0f : 120f * t * (1f - t));
            }
        }

        // ------------------------------------------------------------------ b13-v: hut badge, corruption meter, roll banner

        void BuildB13(Color cream, Color brass, Color panel)
        {
            Rect h = B13Hud.HutRect, c = B13Hud.CorruptionRect;
            _hut = MenuUi.Rt(_space, "Hut Badge");
            HudZones.RegisterPanel(_hut);
            MenuUi.Place(_hut, h.x, h.y, h.width, h.height);
            VectorShape hp = Chip(_hut, "Panel", ShapeKind.RoundRect, 0f, 0f, h.width, h.height, CMTokens.WithAlpha(panel, 0.92f), 12f);
            hp.RimPx = 2f;
            hp.RimColor = brass;
            _hutLevel = MenuUi.Fit(MenuUi.Label(_hut, "Level", CMTokens.Display, 20, brass, TextAnchor.MiddleCenter, 6f, 2f, h.width - 12f, 28f, "", 2f), 12);
            _hutOre = MenuUi.Fit(MenuUi.Label(_hut, "Ore", CMTokens.Mono, 16, cream, TextAnchor.MiddleCenter, 6f, 28f, h.width - 12f, 24f, "", 1.5f), 10);
            _hut.gameObject.SetActive(false);

            _corrupt = MenuUi.Rt(_space, "Corruption Meter");
            HudZones.RegisterPanel(_corrupt);
            MenuUi.Place(_corrupt, c.x, c.y, c.width, c.height);
            VectorShape cp = Chip(_corrupt, "Panel", ShapeKind.RoundRect, 0f, 0f, c.width, c.height, CMTokens.WithAlpha(panel, 0.92f), 12f);
            cp.RimPx = 2f;
            cp.RimColor = CMTokens.Corruption.Color;
            _corruptFill = Chip(_corrupt, "Chance", ShapeKind.RoundRect, 6f, c.height - 9f, c.width - 12f, 5f, CMTokens.Corruption.Color, 2f);
            _corruptFill.OutlinePx = 0f;
            _corruptHead = MenuUi.Fit(MenuUi.Label(_corrupt, "Head", CMTokens.Display, 15, cream, TextAnchor.MiddleCenter, 6f, 2f, c.width - 12f, 20f, "", 1.5f), 10);
            _corruptPool = MenuUi.Fit(MenuUi.Label(_corrupt, "Pool", CMTokens.Ui, 14, cream, TextAnchor.MiddleCenter, 6f, 21f, c.width - 12f, 18f, "", 1.5f), 10);
            _corruptRoll = MenuUi.Fit(MenuUi.Label(_corrupt, "Roll", CMTokens.Ui, 13, CMTokens.WithAlpha(cream, 0.85f), TextAnchor.MiddleCenter, 6f, 38f, c.width - 12f, 17f, "", 1.5f), 9);
            _corrupt.gameObject.SetActive(false);
        }

        void TickB13(World w, int me)
        {
            TeamId team = w.Actors[me].Team;
            bool hut = Blacksmith.On(w) && !HudState.NoHillIn(w.Config);   // review fix round 3: no "HUT 0 ore 0/6" in the range
            if (_hut.gameObject.activeSelf != hut) _hut.gameObject.SetActive(hut);
            if (hut)
            {
                HudText.Set(_hutLevel, HutChoices.On(w) ? B13Hud.HutChoiceLine(Blacksmith.Level(w, team)) : B13Hud.HutLine(Blacksmith.Level(w, team)));   // b15-hook
                HudText.Set(_hutOre, _b13.Ore(Blacksmith.OreBanked(w, team), Blacksmith.NextCost(w, team)));
            }
            bool corruptOn = CriticalMass.Sim.Core.Corruption.On(w);
            double pool = corruptOn ? CriticalMass.Sim.Core.Corruption.Pool(w) : 0;
            bool meter = B13Hud.MeterShown(corruptOn, pool);
            if (_corrupt.gameObject.activeSelf != meter) _corrupt.gameObject.SetActive(meter);
            if (meter)
            {
                double chance = CriticalMass.Sim.Core.Corruption.Chance(w);
                HudText.Set(_corruptHead, _b13.Chance(chance));
                HudText.Set(_corruptPool, _b13.Pool(pool));
                int next = CriticalMass.Sim.Core.Corruption.NextRollTick(w);
                bool hatched = w.Kaiju != null && w.Kaiju.State != KaijuState.Egg;
                HudText.Set(_corruptRoll, _b13.Roll(next < 0 ? -1 : B13Hud.SecondsUntil(next, w.Tick, w.Config.SimHz), hatched));
                float f = B13Hud.ChanceBar01(chance);
                RectTransform fr = _corruptFill.rectTransform;
                float fw = Mathf.Max(0.01f, (B13Hud.CorruptionRect.width - 12f) * f);
                if (Mathf.Abs(fr.sizeDelta.x - fw) > 0.5f) MenuUi.Place(fr, 6f, B13Hud.CorruptionRect.height - 9f, fw, 5f);
            }
        }

        /// <summary>A corruption roll raises the result banner (b13 r2: from DepthOut.CorruptionRolled, not the event ring).</summary>
        static void OnRoll(bool wild) => Banner.Show(Banner.CorruptionRollSpec(wild));

        void TickPower(World w, int me, bool on)
        {
            int kind = on ? DepthRead.PowerKind(w, me) : 0;
            int end = DepthRead.PowerEndTick(w, me);
            bool show = on && w.Actors[me].Alive && PowerTimers.Active(kind, w.Tick, end);
            if (_power.gameObject.activeSelf != show) _power.gameObject.SetActive(show);
            if (!show) { _powerKindShown = -1; return; }
            if (kind != _powerKindShown)
            {
                _powerKindShown = kind;
                Color c = PowerTimers.Colour[kind];
                _powerPie.SetFill(c);
                _powerIcon.Kind = PowerTimers.Icon[kind];
                _powerIcon.SetVerticesDirty();
                Texture2D art = kind >= 1 && kind <= UiIcons.Items.Length ? UiIcons.Get(UiIcons.Items[kind - 1], 2) : null;
                _powerArt.texture = art;
                _powerArt.gameObject.SetActive(art != null);
                _powerIcon.gameObject.SetActive(art == null);
                HudText.Set(_powerName, DepthWords.Pickup(kind));
                _powerName.color = c;
                HudText.Set(_powerHint, DepthWords.Counter(kind));
            }
            if (kind == DepthRead.KindWard)
            {
                // the ward thins as it soaks: show what is left of it as well as the clock
                _powerPie.Fill01 = PowerTimers.WardShell01(DepthRead.WardLeft(w, me), DepthRead.WardFull(w));
            }
            else _powerPie.Fill01 = PowerTimers.Left01(w.Tick, _driver.Alpha, end, DepthRead.FullTicks(w, kind));
            int secs = PowerTimers.SecondsLeft(w.Tick, end, w.Config.SimHz);
            HudText.Set(_powerTime, Clock[Mathf.Clamp(secs, 0, Clock.Length - 1)]);
        }

        // ------------------------------------------------------------------ name tags, speech marks, pops

        void TickTags(World w, DepthWorldView view, int me, UnityEngine.Camera cam, bool on)
        {
            int used = 0;
            if (on && DepthRead.BotNamesOn(w) && cam != null)
            {
                DepthNames.Bind(w);
                TeamId mine = w.Actors[me].Team;
                double mx = w.X[me], mz = w.Z[me];
                for (int id = 0; id < w.ActorCount && used < Tags; id++)
                {
                    if (id == me) continue;
                    Actor a = w.Actors[id];
                    if (!a.Alive) continue;
                    if (global::CriticalMass.Runtime.View.Fog.FogOfWar.Hides(id)) continue;
                    double dx = w.X[id] - mx, dz = w.Z[id] - mz;
                    if (dx * dx + dz * dz > TagRange * TagRange) continue;
                    Vector3 head = Head(w, id, _driver.Alpha, 2.65f);
                    if (!ToLocal(cam, head, out Vector2 at)) continue;
                    // owner walk 1: a tag that would sit on a HUD panel (the Kaiju panel, the feed, the minimap) waits
                    Vector3 hp = cam.WorldToScreenPoint(head);
                    if (HudKeepOut.Contains(new Vector2(hp.x, hp.y), 40f * _canvas.scaleFactor)) continue;
                    RectTransform rt = _tag[used];
                    rt.anchoredPosition = at;
                    _tagHead[used] = head;
                    // fix/hud-containment-4 (by eye: JUNO over the build bank box): no tag over a HUD panel (tested before
                    // it shows, so a hidden tag does not flick on and off every frame)
                    if (HudZones.OverPanel(rt, _canvas)) { if (rt.gameObject.activeSelf) rt.gameObject.SetActive(false); _tagActor[used] = -1; continue; }
                    if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                    if (_tagActor[used] != id)
                    {
                        _tagActor[used] = id;
                        _tagText[used].color = CMTokens.Team(a.Team);
                    }
                    bool escort = DepthRead.Escorting(w, id);
                    HudText.Set(_tagText[used], escort ? DepthWords.Escort : DepthNames.OrFallback(id, KillWords.ClassUpper[Mathf.Clamp((int)a.Cls, 0, 5)]));
                    int call = a.Team == mine ? DepthRead.OrderCall(w, id) : -1;
                    bool order = call >= 0;
                    VectorShape o = _tagOrder[used];
                    if (o.gameObject.activeSelf != order) o.gameObject.SetActive(order);
                    if (order && (o.Kind != PingWords.Shape[call]))
                    {
                        o.Kind = PingWords.Shape[call];
                        o.SetFill(PingWords.Colour((PingKind)call));
                        o.SetVerticesDirty();
                    }
                    used++;
                }
            }
            for (int i = used; i < Tags; i++)
                if (_tag[i].gameObject.activeSelf) { _tag[i].gameObject.SetActive(false); _tagActor[i] = -1; }

            // speech marks: the newest answers (1.5 s), own team only (the feed only keeps those)
            DepthFeed f = view.Feed;
            int b = 0;
            for (int i = 0; i < DepthFeed.Acks && b < Bubbles; i++)
            {
                DepthFeed.Ack k = f.AckRing[i];
                float age = (float)f.Wall(view.SimNow - k.T);
                if (age < 0f || age > 1.5f || k.Bot < 0 || k.Bot >= w.ActorCount || !w.Actors[k.Bot].Alive) continue;
                if (!ToLocal(cam, Head(w, k.Bot, _driver.Alpha, 3.3f), out Vector2 at)) continue;
                if (HudZones.FullPanelHeld) break;   // fix/hud-containment-4: no call bubble over the knock-out recap
                RectTransform rt = _bubble[b];
                rt.anchoredPosition = at;
                rt.localScale = Vector3.one;
                // fix/hud-containment-4 (by eye: "Help!" over YOU WERE HIT BY): no call bubble over a HUD panel
                if (HudZones.OverPanel(rt, _canvas)) continue;
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                float pop = age < 0.12f && !FeelGate.ReduceMotion ? Mathf.Lerp(0.6f, 1f, age / 0.12f) : 1f;
                rt.localScale = new Vector3(pop, pop, 1f);
                rt.anchoredPosition = at;
                HudText.Set(_bubbleText[b], DepthWords.CallShort[Mathf.Clamp(k.Call, 0, 3)]);
                b++;
            }
            for (int i = b; i < Bubbles; i++) if (_bubble[i].gameObject.activeSelf) _bubble[i].gameObject.SetActive(false);
        }

        void TickFloats(World w, DepthWorldView view, UnityEngine.Camera cam)
        {
            DepthFeed f = view.Feed;
            int used = 0;
            for (int i = 0; i < DepthFeed.Pops && used < Floats; i++)
            {
                DepthFeed.Pop p = f.PopRing[i];
                if (p.Kind != DepthFeed.PopPlus && p.Kind != DepthFeed.PopBlocked) continue;
                float age = (float)f.Wall(view.SimNow - p.T);
                if (age < 0f || age > 0.8f) continue;
                Vector3 at3 = p.Actor >= 0 && p.Actor < w.ActorCount ? Head(w, p.Actor, _driver.Alpha, 2.4f) : SimSpace.ToWorld(p.X, GroundHeight.At(w.Map, p.X, p.Z) + 2.4, p.Z);
                if (!ToLocal(cam, at3, out Vector2 at)) continue;
                RectTransform rt = _float[used];
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                rt.anchoredPosition = at + Vector2.up * (FeelGate.ReduceMotion ? 0f : 40f * age);
                Text t = _floatText[used];
                bool blocked = p.Kind == DepthFeed.PopBlocked;
                HudText.Set(t, blocked ? DepthWords.Blocked : DepthLines.Plus(p.Value));
                t.color = CMTokens.WithAlpha(blocked ? CMTokens.Stone.Color : CMTokens.ToolBrass.Color, age > 0.55f ? 1f - (age - 0.55f) / 0.25f : 1f);
                used++;
            }
            for (int i = used; i < Floats; i++) if (_float[i].gameObject.activeSelf) _float[i].gameObject.SetActive(false);
        }

        void TickPointer(World w, UnityEngine.Camera cam, bool on)
        {
            bool show = false;
            if (on && cam != null && DepthRead.DropState(w, out double x, out double z, out _) != DepthRead.DropNone)
            {
                Vector3 vp = cam.WorldToViewportPoint(SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 0.5, z));
                if (DamageDirection.OffScreen(vp))
                {
                    Vector2 dir = DamageDirection.ScreenDir(vp);
                    // fix/hud-edge-markers-1: the drop pointer sat on the COINS panel with its label on the minimap (it never
                    // checked the panels). It now slides along the free edge clear of every HUD panel.
                    Vector2 size = _space.rect.size;
                    Vector2 edge = DamageDirection.EdgePoint(dir, size * 0.5f, 60f) + size * 0.5f;
                    float sf = Mathf.Max(1e-4f, _canvas.scaleFactor);
                    float k = HudLayout.Scale(size.x * sf, size.y * sf) / sf;
                    _pointer.anchoredPosition = HudEdgeMarkers.PlaceLive(edge, HudEdgeMarkers.MarkerHalf, size.x, size.y, k, sf, 60f) - size * 0.5f;
                    _pointer.localEulerAngles = new Vector3(0f, 0f, DamageDirection.AngleDeg(dir) + 180f);
                    show = true;
                }
            }
            if (_pointer.gameObject.activeSelf != show) _pointer.gameObject.SetActive(show);
        }

        // ------------------------------------------------------------------ minimap and full map

        void TickMaps(World w, bool on, float real)
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
            MinimapProjection proj = MinimapGeometry.Projection;
            bool steady = FeelGate.ReduceFlashing;
            float blink = steady ? 1f : (Mathf.Repeat(real * 2f, 1f) < 0.5f ? 1f : 0.45f);

            // groves and springs (dimmed when empty)
            int ns = on ? Math.Min(DepthRead.SourceCount(w), MaxSources) : 0;
            for (int i = 0; i < ns; i++)
            {
                DepthRead.Source(w, i, out bool water, out double x, out double z, out int ch, out _, out _);
                Color c = water ? new Color32(96, 176, 232, 255) : new Color32(92, 166, 84, 255);
                if (ch <= 0) c = CMTokens.WithAlpha(c, 0.4f);
                Mark(_mSrc[i], _mSrcS[i], proj, x, z, 8f, c, water ? ShapeKind.Egg : ShapeKind.TriangleDown);
                if (full) MarkFull(_fSrc[i], _fSrcS[i], fm, x, z, 14f, c, water ? ShapeKind.Egg : ShapeKind.TriangleDown);
            }
            HideFrom(_mSrc, ns);
            HideFrom(_fSrc, full ? ns : 0);

            // power-ups waiting (arming or armed): their kind's icon; coins are not on the map
            int np = 0;
            int cap = on ? DepthRead.PickupCapacity(w) : 0;
            for (int s = 0; s < cap && np < MaxPowers; s++)
            {
                if (!DepthRead.PickupAt(w, s, out int kind, out double x, out double z, out _, out _, out _) || kind == DepthRead.KindCoin) continue;
                Color c = PowerTimers.Colour[Mathf.Clamp(kind, 0, 3)];
                Mark(_mPow[np], _mPowS[np], proj, x, z, 10f, c, PowerTimers.Icon[kind]);
                if (full) MarkFull(_fPow[np], _fPowS[np], fm, x, z, 18f, c, PowerTimers.Icon[kind]);
                np++;
            }
            HideFrom(_mPow, np);
            HideFrom(_fPow, full ? np : 0);

            // supply drop, falling bridge, exposed egg
            double dx = 0, dz = 0;
            bool drop = on && DepthRead.DropState(w, out dx, out dz, out _) != DepthRead.DropNone;
            Single(_mDrop, _fDrop, proj, fm, full, drop, dx, dz, 12f, PowerTimers.Colour[0], 1f);
            int bs = on ? DepthRead.BridgeState(w, out _, out _) : DepthRead.BridgeNone;
            DepthWorldView view = DepthWorldView.Active;
            bool bridge = bs != DepthRead.BridgeNone && view != null;
            Single(_mBridge, _fBridge, proj, fm, full, bridge, bridge ? view.Feed.BridgeX : 0, bridge ? view.Feed.BridgeZ : 0, 11f, CMTokens.Crimson.Color,
                bs == DepthRead.BridgeWarned ? blink : 1f);
            bool egg = on && DepthRead.EggExposedUntil(w) > w.Tick && w.Map.Shrine != null;
            Single(_mEgg, _fEgg, proj, fm, full, egg, egg ? w.Map.Shrine.X : 0, egg ? w.Map.Shrine.Z : 0, 18f, CMTokens.EggSeam.Color, steady ? 1f : 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(real * 3f)));
        }

        static void Mark(RectTransform rt, VectorShape s, MinimapProjection proj, double x, double z, float size, Color c, ShapeKind k)
        {
            if (rt == null) return;
            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
            Vector2 px = proj.ToPixel(x, z);
            rt.anchoredPosition = new Vector2(px.x, -px.y);
            Size(rt, size);
            Style(s, c, k);
        }

        static void MarkFull(RectTransform rt, VectorShape s, FullMapView fm, double x, double z, float size, Color c, ShapeKind k)
        {
            if (rt == null) return;
            Vector2 v = fm.SimToView(x, z);
            bool vis = FullMapView.InsideView(v);
            if (rt.gameObject.activeSelf != vis) rt.gameObject.SetActive(vis);
            if (!vis) return;
            rt.anchoredPosition = v;
            Size(rt, size);
            Style(s, c, k);
        }

        static void Size(RectTransform rt, float size)
        {
            var d = new Vector2(size, size);
            if (rt.sizeDelta != d) rt.sizeDelta = d;
        }

        static void Style(VectorShape s, Color c, ShapeKind k)
        {
            if (s == null) return;
            if (s.Kind != k) { s.Kind = k; s.SetVerticesDirty(); }
            if (s.FillColor != c) s.SetFill(c);
        }

        void Single(RectTransform mini, RectTransform fullRt, MinimapProjection proj, FullMapView fm, bool full, bool on, double x, double z, float size, Color c, float alpha)
        {
            Color ca = CMTokens.WithAlpha(c, alpha);
            if (mini != null)
            {
                if (mini.gameObject.activeSelf != on) mini.gameObject.SetActive(on);
                if (on) Mark(mini, mini.GetComponent<VectorShape>(), proj, x, z, size, ca, mini.GetComponent<VectorShape>().Kind);
            }
            if (fullRt != null)
            {
                bool f = full && on;
                if (!f) { if (fullRt.gameObject.activeSelf) fullRt.gameObject.SetActive(false); }
                else MarkFull(fullRt, fullRt.GetComponent<VectorShape>(), fm, x, z, size * 1.6f, ca, fullRt.GetComponent<VectorShape>().Kind);
            }
        }

        static void HideFrom(RectTransform[] pool, int from)
        {
            for (int i = from; i < pool.Length; i++)
                if (pool[i] != null && pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }

        static RectTransform Marker(Transform parent, string name, ShapeKind kind, Vector2 anchor, float outline, out VectorShape shape)
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
            shape.Radius = 3f;
            shape.FillColor = Color.white;
            shape.OutlinePx = outline;
            shape.RimPx = 0f;
            shape.raycastTarget = false;
            go.SetActive(false);
            return rt;
        }

        void BuildMini(RectTransform area)
        {
            _mapArea = area;
            Vector2 tl = new Vector2(0f, 1f);
            for (int i = 0; i < MaxSources; i++) _mSrc[i] = Marker(area, "Source " + i, ShapeKind.TriangleDown, tl, 1.5f, out _mSrcS[i]);
            for (int i = 0; i < MaxPowers; i++) _mPow[i] = Marker(area, "Power " + i, ShapeKind.Shield, tl, 1.5f, out _mPowS[i]);
            _mDrop = Marker(area, "Supply Drop", ShapeKind.RoundRect, tl, 2f, out _);
            _mBridge = Marker(area, "Bridge", ShapeKind.Diamond, tl, 2f, out _);
            _mEgg = Marker(area, "Egg Exposed", ShapeKind.Circle, tl, 2f, out VectorShape e);
            e.FillColor = new Color(0f, 0f, 0f, 0f);
            e.RimPx = 3f;
            e.RimColor = CMTokens.EggSeam.Color;
        }

        void BuildFull(FullMapView fm)
        {
            if (fm.Viewport == null) return;
            _fullBuilt = true;
            Vector2 c = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < MaxSources; i++) _fSrc[i] = Marker(fm.Viewport, "Source " + i, ShapeKind.TriangleDown, c, 2.5f, out _fSrcS[i]);
            for (int i = 0; i < MaxPowers; i++) _fPow[i] = Marker(fm.Viewport, "Power " + i, ShapeKind.Shield, c, 2.5f, out _fPowS[i]);
            _fDrop = Marker(fm.Viewport, "Supply Drop", ShapeKind.RoundRect, c, 3f, out _);
            _fBridge = Marker(fm.Viewport, "Bridge", ShapeKind.Diamond, c, 3f, out _);
            _fEgg = Marker(fm.Viewport, "Egg Exposed", ShapeKind.Circle, c, 3f, out VectorShape e);
            e.FillColor = new Color(0f, 0f, 0f, 0f);
            e.RimPx = 4f;
            e.RimColor = CMTokens.EggSeam.Color;
        }

        /// <summary>Panel-local top of these legend rows: under the class-mechanics rows (MechanicsHud.LegendY + 4 rows).</summary>
        public const float LegendY = MechanicsHud.LegendY + 4f * MechanicsHud.LegendStep, LegendColW = FullMapView.SideW / 3f;
        /// <summary>Step between the two rows of three (16 px type, the minimum, in a 24 px row).</summary>
        public const float LegendRowStep = 26f;

        void BuildLegend(FullMapView fm)
        {
            if (fm.Panel == null) return;
            _legendBuilt = true;
            Transform p = fm.Panel;
            Row(p, 0, ShapeKind.TriangleDown, new Color32(92, 166, 84, 255), DepthWords.LegendGrove);
            Row(p, 1, ShapeKind.Egg, new Color32(96, 176, 232, 255), DepthWords.LegendSpring);
            Row(p, 2, ShapeKind.Shield, PowerTimers.Colour[3], DepthWords.LegendPower);
            Row(p, 3, ShapeKind.RoundRect, PowerTimers.Colour[0], DepthWords.LegendDrop);
            Row(p, 4, ShapeKind.Diamond, CMTokens.Crimson.Color, DepthWords.LegendBridge);
            Row(p, 5, ShapeKind.Circle, CMTokens.EggSeam.Color, DepthWords.EggExposed.Length > 18 ? "Egg exposed" : DepthWords.EggExposed);
        }

        static void Row(Transform p, int i, ShapeKind kind, Color c, string word)
        {
            float x = MechanicsHud.LegendX + (i % 3) * LegendColW, y = LegendY + (i / 3) * LegendRowStep;
            VectorShape s = MenuUi.Shape(p, "Depth Legend " + i, kind, x + 2f, y + 3f, 16f, 16f, c, 3f);
            s.OutlinePx = 2f;
            s.RimPx = 0f;
            // 16 px, the type minimum (was 15 with best-fit down to 10).
            MenuUi.Label(p, "Depth Legend Word " + i, CMTokens.Ui, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, x + 24f, y, LegendColW - 28f, 24f, word, 2f);
        }
    }
}
