using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// DG-11 scoreboard, shown while the Scoreboard action is held (Tab on keyboard, View on a controller; rebindable in
    /// Settings). Two team tables side by side, up to 16 rows each (32 at 16v16): class, knock-outs, deaths, hill seconds,
    /// biomass delivered, corruption delivered, sorted by knock-outs then hill time. The local player's row is on a Tool
    /// Brass plate; knocked-out players are dimmed. Reads CombatTally (sim counters plus delivery tally); allocation-free.
    /// </summary>
    public sealed class Scoreboard : MonoBehaviour
    {
        public const int MaxRows = 16;
        public const int Columns = 6;   // class, KO, deaths, hill s, biomass, corruption
        public const float TableW = 780f, Gap = 40f, RowH = 36f, Pitch = 40f, HeadH = 132f, FootH = 24f;
        public const float FadeSeconds = 0.12f;

        static readonly string[] Headings = { "CLASS", "KO", "DEATHS", "HILL S", "BIOMASS", "CORRUPTION" };
        /// <summary>Column x (from the table's left) and width.</summary>
        static readonly float[] ColX = { 52f, 270f, 360f, 460f, 560f, 670f };
        static readonly float[] ColW = { 210f, 80f, 90f, 90f, 100f, 100f };

        /// <summary>Opens the board regardless of input (captures, the results screen). Default false.</summary>
        public static bool ForceOpen;

        public Canvas Canvas { get; private set; }
        public bool IsOpen { get; private set; }

        CombatLog _log;
        RectTransform _board;
        CanvasGroup _group;
        VectorShape _panel;
        float _alpha;
        int _tier = 2;
        int _rowsShown = -1;
        readonly Table[] _tables = new Table[2];
        World _world;

        sealed class Row
        {
            public RectTransform Rt;
            public VectorShape Plate;
            public HudIcon Badge;
            /// <summary>Mechanics depth (U-065): the team's level of this row's class, as three pips under the badge.</summary>
            public readonly VectorShape[] Pips = new VectorShape[3];
            public readonly Text[] Cells = new Text[Columns];
            public readonly TextOutline[] Outlines = new TextOutline[Columns];
            public int Index;
            public int Actor = -1;
            public bool Own, Dim, Styled;
        }

        sealed class Table
        {
            public RectTransform Rt;
            public Text Name, Total;
            public HudIcon Crest;
            public readonly Row[] Rows = new Row[MaxRows];
            public readonly int[] Order = new int[MaxRows];
            public int Count;
        }

        void Awake() => Build();

        public void Build()
        {
            if (Canvas != null) return;
            Canvas = FeedUi.MakeCanvas(transform, "Scoreboard Canvas", 20);
            float w = TableW * 2f + Gap + 48f;
            _board = FeedUi.Anchored(Canvas.transform, "Scoreboard", new Vector2(0.5f, 0.5f), 0f, 0f, w, Height(MaxRows));
            _group = _board.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _panel = MenuUi.Panel(_board, "Panel", 0f, 0f, w, Height(MaxRows));
            _panel.SetFill(CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.94f));
            for (int t = 0; t < 2; t++) _tables[t] = BuildTable((TeamId)t, 24f + t * (TableW + Gap));
            _board.gameObject.SetActive(false);
        }

        static float Height(int rows) => HeadH + rows * Pitch + FootH;

        Table BuildTable(TeamId team, float x)
        {
            var tb = new Table();
            tb.Rt = MenuUi.Rt(_board, team + " Table");
            MenuUi.Place(tb.Rt, x, 0f, TableW, Height(MaxRows));
            Color tc = CMTokens.TeamInk(team);   // polish pass 1: text ink (4.5 : 1 on the panel)
            tb.Crest = HudIcon.Make(tb.Rt, "Crest", 8f, 18f, 44f, 44f);
            tb.Crest.Set(UiIcons.Crest[(int)team], _tier);
            tb.Name = MenuUi.Fit(MenuUi.Label(tb.Rt, "Team", CMTokens.Display, UiScale.TypeLead, tc, TextAnchor.MiddleLeft, 62f, 18f, 360f, 44f,
                team == TeamId.Azure ? "AZURE" : "CRIMSON", 3f), 34);
            MenuUi.Label(tb.Rt, "Total Label", CMTokens.Ui, UiScale.TypeMin, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.8f), TextAnchor.MiddleRight,
                420f, 18f, 240f, 44f, "HILL SECONDS", 2f);
            tb.Total = MenuUi.Label(tb.Rt, "Total", CMTokens.Mono, UiScale.TypeHeading, tc, TextAnchor.MiddleRight, 670f, 18f, 100f, 44f, "", 3f);
            for (int c = 0; c < Columns; c++)
                MenuUi.Label(tb.Rt, "Heading " + c, CMTokens.Ui, UiScale.TypeMin, CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.8f),
                    c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, ColX[c], 82f, ColW[c], 30f, Headings[c], 2f);
            for (int r = 0; r < MaxRows; r++)
            {
                var row = new Row { Index = r };
                row.Rt = MenuUi.Rt(tb.Rt, "Row " + r);
                MenuUi.Place(row.Rt, 0f, HeadH + r * Pitch, TableW, RowH);
                row.Plate = MenuUi.Shape(row.Rt, "Plate", ShapeKind.RoundRect, 0f, 0f, TableW, RowH,
                    CMTokens.WithAlpha(CMTokens.VoidNavy.Color, r % 2 == 0 ? 0.55f : 0.3f), 10f);
                row.Plate.OutlinePx = 0f;
                row.Plate.RimPx = 0f;
                row.Badge = HudIcon.Make(row.Rt, "Badge", 12f, 2f, 32f, 32f);
                for (int p = 0; p < 3; p++)
                {
                    row.Pips[p] = MenuUi.Shape(row.Rt, "Level Pip " + p, ShapeKind.Diamond, 13f + p * 11f, RowH - 9f, 8f, 9f, CMTokens.MeterEmpty.Color, 2f);
                    row.Pips[p].OutlinePx = 1.5f;
                    row.Pips[p].RimPx = 0f;
                    row.Pips[p].gameObject.SetActive(false);
                }
                for (int c = 0; c < Columns; c++)
                    row.Cells[c] = MenuUi.Label(row.Rt, "Cell " + c, c == 0 ? CMTokens.Ui : CMTokens.Mono, c == 0 ? 21 : 22,
                        CMTokens.WarmCream.Color, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, ColX[c], 0f, ColW[c], RowH, "", 2f);
                for (int c = 0; c < Columns; c++) row.Outlines[c] = row.Cells[c].GetComponent<TextOutline>();
                row.Rt.gameObject.SetActive(false);
                tb.Rows[r] = row;
            }
            return tb;
        }

        void LateUpdate()
        {
            if (_log == null) _log = GetComponent<CombatLog>();
            int tier = FeedUi.Refresh(Canvas);
            if (tier != _tier) { _tier = tier; SetTier(tier); }
            World w = _log != null && _log.Driver != null ? _log.Driver.World : null;
            bool want = w != null && !_log.Suppressed && (ForceOpen || _log.ScoreboardHeld);
            float dt = Time.unscaledDeltaTime;
            _alpha = Mathf.MoveTowards(_alpha, want ? 1f : 0f, dt / FadeSeconds);
            bool show = _alpha > 0f;
            if (_board.gameObject.activeSelf != show) _board.gameObject.SetActive(show);
            if (Canvas.enabled != show) Canvas.enabled = show;
            IsOpen = want;
            if (!show) return;
            if (Mathf.Abs(_group.alpha - _alpha) > 1e-3f) _group.alpha = _alpha;
            Refresh(w, _log.Log, _log.LocalActor);
        }

        void SetTier(int tier)
        {
            for (int t = 0; t < 2; t++)
            {
                _tables[t].Crest.SetTier(tier);
                for (int r = 0; r < MaxRows; r++) _tables[t].Rows[r].Badge.SetTier(tier);
            }
        }

        /// <summary>Fills both tables from the world and tally (tests call it directly). Allocation-free.</summary>
        public void Refresh(World w, CombatTally tally, int localActor)
        {
            if (Canvas == null) Build();
            if (w == null || tally == null) return;
            _world = w;
            View.Mechanics.DepthNames.Bind(w);
            int n = Mathf.Min(w.ActorCount, CombatTally.MaxActors);
            _tables[0].Count = 0;
            _tables[1].Count = 0;
            for (int id = 0; id < n; id++)
            {
                Table tb = _tables[(int)w.Actors[id].Team];
                if (tb.Count >= MaxRows) continue;
                // insertion sort: knock-outs desc, then hill seconds desc, then id
                int k = tb.Count++;
                while (k > 0 && Before(w, id, tb.Order[k - 1]))
                {
                    tb.Order[k] = tb.Order[k - 1];
                    k--;
                }
                tb.Order[k] = id;
            }
            int rows = Mathf.Max(_tables[0].Count, _tables[1].Count);
            if (rows != _rowsShown)
            {
                _rowsShown = rows;
                float h = Height(rows);
                _board.sizeDelta = new Vector2(_board.sizeDelta.x, h);
                MenuUi.Place(_panel.rectTransform, 0f, 0f, _board.sizeDelta.x, h);
                for (int t = 0; t < 2; t++) MenuUi.Place(_tables[t].Rt, 24f + t * (TableW + Gap), 0f, TableW, h);
            }
            MatchState m = w.Match;
            for (int t = 0; t < 2; t++)
            {
                Table tb = _tables[t];
                double hill = t == 0 ? m.HillSeconds.Azure : m.HillSeconds.Crimson;
                HudText.Set(tb.Total, NumberCache.Int((int)System.Math.Floor(hill)));
                for (int r = 0; r < MaxRows; r++)
                {
                    Row row = tb.Rows[r];
                    bool on = r < tb.Count;
                    if (row.Rt.gameObject.activeSelf != on) row.Rt.gameObject.SetActive(on);
                    if (!on) { row.Actor = -1; continue; }
                    FillRow(row, tally.Tally(tb.Order[r]), localActor);
                }
            }
        }

        static bool Before(World w, int a, int b)
        {
            Actor x = w.Actors[a], y = w.Actors[b];
            if (x.Kills != y.Kills) return x.Kills > y.Kills;
            if (x.HillSeconds != y.HillSeconds) return x.HillSeconds > y.HillSeconds;
            return a < b;
        }

        void FillRow(Row row, ActorTally s, int localActor)
        {
            int cls = Mathf.Clamp((int)s.Cls, 0, KillWords.ClassUpper.Length - 1);
            bool own = s.Id == localActor;
            bool dim = !s.Alive;
            if (row.Actor != s.Id || row.Own != own || row.Dim != dim || !row.Styled)
            {
                row.Actor = s.Id;
                row.Own = own;
                row.Dim = dim;
                row.Styled = true;
                row.Plate.SetFill(own ? CMTokens.ToolBrass.Color : CMTokens.WithAlpha(CMTokens.VoidNavy.Color, row.Index % 2 == 0 ? 0.55f : 0.3f));
                Color text = own ? CMTokens.VoidNavy.Color : CMTokens.WarmCream.Color;
                text.a = dim ? 0.5f : 1f;
                // navy text on brass takes a cream outline to stay a sticker; cream text keeps the navy outline
                Color outline = own ? CMTokens.WarmCream.Color : CMTokens.VoidNavy.Color;
                for (int c = 0; c < Columns; c++)
                {
                    row.Cells[c].color = text;
                    TextOutline o = row.Outlines[c];
                    if (o != null && o.OutlineColor != outline) { o.OutlineColor = outline; row.Cells[c].SetVerticesDirty(); }
                }
            }
            row.Badge.Set(UiIcons.ClassBadge[cls, (int)s.Team], _tier);
            // Mechanics depth (U-067): named bots with their play style ("THORN  BRAWLER"); the class word without names.
            HudText.Set(row.Cells[0], View.Mechanics.DepthNames.RowLabel(s.Id, own, KillWords.You, KillWords.ClassUpper[cls]));
            World w = _world;
            bool levels = w != null && View.Mechanics.DepthRead.StationsOn(w);
            int lvl = levels ? View.Mechanics.DepthRead.Level(w, s.Team, s.Cls) : 0, max = levels ? View.Mechanics.DepthRead.MaxLevel(w) : 0;
            for (int p = 0; p < 3; p++)
            {
                bool on = levels && p < max;
                VectorShape pip = row.Pips[p];
                if (pip.gameObject.activeSelf != on) pip.gameObject.SetActive(on);
                if (!on) continue;
                Color pc = p < lvl ? CMTokens.ToolBrass.Color : CMTokens.MeterEmpty.Color;
                if (pip.FillColor != pc) pip.SetFill(pc);
            }
            HudText.Set(row.Cells[1], NumberCache.Int(s.Knockouts));
            HudText.Set(row.Cells[2], NumberCache.Int(s.Deaths));
            HudText.Set(row.Cells[3], NumberCache.Int((int)System.Math.Floor(s.HillSeconds)));
            HudText.Set(row.Cells[4], NumberCache.Int(s.BiomassDelivered));
            HudText.Set(row.Cells[5], NumberCache.Int(s.CorruptionDelivered));
        }

        // ---- read-back for tests and captures

        /// <summary>Rows shown for a team.</summary>
        public int RowCount(TeamId team) => _tables[(int)team].Count;
        /// <summary>Actor id on row r of team's table (-1 when the row is empty).</summary>
        public int RowActor(TeamId team, int r) => r >= 0 && r < MaxRows ? _tables[(int)team].Rows[r].Actor : -1;
        /// <summary>Cell text (column 0 class ... 5 corruption).</summary>
        public string Cell(TeamId team, int r, int column) =>
            r >= 0 && r < MaxRows && column >= 0 && column < Columns ? _tables[(int)team].Rows[r].Cells[column].text : null;
    }
}
