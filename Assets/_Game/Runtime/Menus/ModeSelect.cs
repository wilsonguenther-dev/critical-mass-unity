using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Home lobby MODE SELECT: six cards in two rows (Quick match, Full battle, Training / Practice, Spectate, Custom), each
    /// with its name, one line, team size, typical length and difficulty pips (Spectate says WATCH ONLY instead: the player
    /// does not play). Under the grid: BACK and the skirmish size (5 v 5 or 8 v 8) for Quick match. A card's press goes on
    /// to class select (Spectate straight to the ready screen; Custom to the full match setup). Back: home. One focus,
    /// arrows / stick / d-pad between cards, the mouse hovers to focus like every other menu.
    /// </summary>
    public sealed class ModeSelect : FlowScreen
    {
        public const float CardW = 520f, CardH = 300f, CardGap = 28f, Row0 = 176f, Row1 = 504f;

        readonly Dictionary<MatchMode, MenuItem> _cards = new Dictionary<MatchMode, MenuItem>();
        readonly Dictionary<MatchMode, Text[]> _texts = new Dictionary<MatchMode, Text[]>();
        readonly Dictionary<MatchMode, Text[]> _labels = new Dictionary<MatchMode, Text[]>();
        readonly Dictionary<MatchMode, Text> _sizeValues = new Dictionary<MatchMode, Text>();
        readonly Dictionary<MatchMode, Text> _lengthValues = new Dictionary<MatchMode, Text>();
        readonly List<System.Action> _refresh = new List<System.Action>();
        MenuItem _back, _size;

        public MenuItem Card(MatchMode m) => _cards.TryGetValue(m, out MenuItem it) ? it : null;
        public MenuItem BackItem => _back;
        public MenuItem SizeItem => _size;
        public IReadOnlyDictionary<MatchMode, MenuItem> Cards => _cards;

        protected override string HintKeys => "ENTER CHOOSE   ·   ARROWS MOVE   ·   ESC HOME";
        protected override string HintPad => "A CHOOSE   ·   LEFT / RIGHT MOVE   ·   B HOME";

        protected override void BuildContent(RectTransform space)
        {
            MenuUi.Label(space, "Title", CMTokens.Display, 52, Cream, TextAnchor.MiddleLeft, 152f, 52f, 1000f, 70f, "CHOOSE A MODE", 4f);
            MenuUi.Label(space, "Offline", CMTokens.Ui, 22, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, 152f, 116f, 1300f, 34f,
                "Every match is offline. Everyone else on the field is a bot.", 2f);

            for (int i = 0; i < PlayModes.SelectOrder.Length; i++)
            {
                int col = i % 3, row = i / 3;
                BuildCard(space, PlayModes.SelectOrder[i], 152f + col * (CardW + CardGap), row == 0 ? Row0 : Row1);
            }

            _back = Button(space, "Back", 152f, 870f, 300f, 84f, "BACK", Back, 30);
            _size = Stepper(space, "Skirmish Size", 476f, 870f, 640f, 84f, "QUICK MATCH SIZE",
                () => PlayModes.SizeLabel(PlayModes.ValidQuickSize(PlayFlow.QuickSize)), StepSize, _refresh);
            Wire();
            DefaultFocus = Card(MatchMode.Quick);
        }

        void BuildCard(RectTransform space, MatchMode m, float x, float y)
        {
            ModeInfo info = PlayModes.Info(m);
            MenuItem card = MenuUi.Item(space, "Mode " + m, x, y, CardW, CardH, "", MenuItem.Kind.Button, 30, true, CMTokens.Display);
            card.Body.Radius = 20f;
            card.Body.RimPx = 3f;
            MatchMode pick = m;
            card.Submitted = () => PlayFlow.Choose(pick);
            _cards[m] = card;
            Transform t = card.transform;

            VectorShape emblem = MenuUi.Shape(t, "Emblem", Emblem(m), CardW - 84f, 24f, 56f, 56f, info.Watching ? Cream : Brass, 8f);
            emblem.OutlinePx = 3f;
            emblem.RimPx = 0f;

            Text title = MenuUi.Label(t, "Name", CMTokens.Display, 38, Cream, TextAnchor.MiddleLeft, 28f, 24f, CardW - 130f, 54f, info.Title, 3f);
            Text line = MenuUi.Label(t, "Line", CMTokens.Ui, 22, Cream, TextAnchor.UpperLeft, 28f, 88f, CardW - 56f, 64f, info.Line, 2f);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            line.lineSpacing = 1.05f;

            VectorShape rule = MenuUi.Shape(t, "Rule", ShapeKind.RoundRect, 28f, 166f, CardW - 56f, 3f, CMTokens.WithAlpha(Cream, 0.35f), 1f);
            rule.OutlinePx = 0f;
            rule.RimPx = 0f;

            Color dim = CMTokens.WithAlpha(Cream, 0.75f);
            Text l0 = MenuUi.Label(t, "Teams Label", CMTokens.Ui, 16, dim, TextAnchor.MiddleLeft, 28f, 184f, 150f, 24f, "TEAMS", 2f);
            Text l1 = MenuUi.Label(t, "Length Label", CMTokens.Ui, 16, dim, TextAnchor.MiddleLeft, 186f, 184f, 190f, 24f, "LENGTH", 2f);
            Text l2 = MenuUi.Label(t, "Difficulty Label", CMTokens.Ui, 16, dim, TextAnchor.MiddleLeft, 384f, 184f, 120f, 24f,
                info.Watching ? "YOU" : "CHALLENGE", 2f);
            Text size = MenuUi.Label(t, "Teams", CMTokens.Display, 24, Cream, TextAnchor.MiddleLeft, 28f, 212f, 150f, 40f, "", 2f);
            Text length = MenuUi.Label(t, "Length", CMTokens.Display, 22, Cream, TextAnchor.MiddleLeft, 186f, 212f, 196f, 40f, "", 2f);
            length.resizeTextForBestFit = true;
            length.resizeTextMinSize = 14;
            length.resizeTextMaxSize = 22;
            _sizeValues[m] = size;
            _lengthValues[m] = length;
            _texts[m] = new[] { title, line, size, length };
            _labels[m] = new[] { l0, l1, l2 };
            if (info.Watching)
            {
                MenuUi.Label(t, "Watching", CMTokens.Display, 22, Brass, TextAnchor.MiddleLeft, 384f, 212f, 130f, 40f, "WATCH", 2f);
            }
            else
            {
                for (int k = 0; k < 3; k++)
                {
                    VectorShape pip = MenuUi.Shape(t, "Pip " + k, ShapeKind.Diamond, 386f + k * 34f, 220f, 24f, 24f,
                        k < info.Difficulty ? Brass : CMTokens.MeterEmpty.Color, 4f);
                    pip.RimPx = 0f;
                    pip.OutlinePx = 2f;
                }
            }
            if (m == MatchMode.Custom) length.text = "YOUR CHOICE";
        }

        static ShapeKind Emblem(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.Quick: return ShapeKind.TriangleRight;
                case MatchMode.FullBattle: return ShapeKind.Banner;
                case MatchMode.Training: return ShapeKind.Egg;
                case MatchMode.Practice: return ShapeKind.Hexagon;
                case MatchMode.Spectate: return ShapeKind.Circle;
                default: return ShapeKind.Chamfer;
            }
        }

        void StepSize(int d)
        {
            int i = System.Array.IndexOf(PlayModes.QuickSizes, PlayModes.ValidQuickSize(PlayFlow.QuickSize));
            int n = PlayModes.QuickSizes.Length;
            PlayFlow.QuickSize = PlayModes.QuickSizes[((i + (d < 0 ? -1 : 1)) % n + n) % n];
            RefreshCards();
        }

        /// <summary>Explicit grid navigation: left/right inside a row (wrapping), up/down between rows, the bottom bar under row two.</summary>
        void Wire()
        {
            MatchMode[] o = PlayModes.SelectOrder;
            for (int i = 0; i < o.Length; i++)
            {
                int col = i % 3, row = i / 3;
                MenuItem it = _cards[o[i]];
                MenuItem left = _cards[o[row * 3 + (col + 2) % 3]];
                MenuItem right = _cards[o[row * 3 + (col + 1) % 3]];
                MenuItem up = row == 0 ? (col == 0 ? _back : _size) : _cards[o[col]];
                MenuItem down = row == 0 ? _cards[o[3 + col]] : (col == 0 ? _back : _size);
                it.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit, selectOnLeft = left, selectOnRight = right, selectOnUp = up, selectOnDown = down,
                };
            }
            _back.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = _cards[o[3]], selectOnDown = _cards[o[0]], selectOnRight = _size, selectOnLeft = _size,
            };
            // The size line keeps left/right for itself (a stepper); up goes back to the cards.
            _size.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = _cards[o[4]], selectOnDown = _cards[o[1]],
            };
        }

        /// <summary>Focuses a mode's card (the home screen opens the select on the mode it offers).</summary>
        public void FocusMode(MatchMode m)
        {
            MenuItem c = Card(m);
            if (c != null) Focus(c);
        }

        protected override void OnShow()
        {
            for (int i = 0; i < _refresh.Count; i++) _refresh[i]();
            RefreshCards();
        }

        void RefreshCards()
        {
            if (_cards.Count == 0) return;
            for (int i = 0; i < PlayModes.SelectOrder.Length; i++)
            {
                MatchMode m = PlayModes.SelectOrder[i];
                MatchSetupChoice c = PlayFlow.ChoiceFor(m);
                HudText.Set(_sizeValues[m], PlayModes.SizeLabel(c.TeamSize));
                if (m != MatchMode.Custom) HudText.Set(_lengthValues[m], PlayModes.LengthLabel(m, c.Pace));
            }
        }

        protected override void Tick()
        {
            // Focused cards draw navy text on the brass fill (outlined cream on brass reads as a smudge).
            MenuItem f = Focused;
            foreach (KeyValuePair<MatchMode, MenuItem> kv in _cards)
            {
                bool focus = f == kv.Value;
                Color c = focus ? Navy : Cream;
                Recolour(_texts[kv.Key], c, focus);
                Recolour(_labels[kv.Key], CMTokens.WithAlpha(c, 0.75f), focus);
            }
        }

        static void Recolour(Text[] ts, Color want, bool focus)
        {
            for (int k = 0; k < ts.Length; k++)
            {
                if (ts[k].color == want) continue;
                ts[k].color = want;
                TextOutline o = ts[k].GetComponent<TextOutline>();
                if (o != null) o.enabled = !focus;
                ts[k].SetVerticesDirty();
            }
        }

        public override void Back() => Director.ShowMenu();
    }
}
