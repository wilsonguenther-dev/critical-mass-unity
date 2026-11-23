using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// px lane F (2026-11-22): the FIRST PLAY NOW of a profile picks a class instead of forcing the Warrior. Six cards, the
    /// three recommended starters first (Farmer: the easiest to land hits with; Warrior: the fighter; Miner: heavy hits,
    /// walls down), each with one line of what you DO and its difficulty pips; focus starts on the Farmer. One press on a
    /// card starts the Play Now match with that class (and it is the class every later PLAY NOW keeps). PRACTICE FIRST
    /// opens the practice range with the focused class. Shown once per profile (StarterPick.DoneKey); never in the Editor,
    /// batch mode or autoplay unless ForceForTests.
    /// </summary>
    public sealed class StarterPick : FlowScreen
    {
        public const string DoneKey = "cm.flow.starterpick.done";
        public const float CardW = 500f, CardH = 250f, CardGap = 30f, Row0 = 200f, Row1 = 520f;
        /// <summary>Fix round 5 (item 8, review pass 4: the card text read very small at 1080p): the card's body line at 26 px
        /// (was the 22 px body size; shrinks to 22 only if a line would not fit its box), three lines in the 104 px box; the
        /// DIFFICULTY label at the 18 px secondary size (was the 16 px floor).</summary>
        public const int BodyType = 26, BodyTypeMin = UiScale.TypeBody, DifficultyType = UiScale.TypeSmall;
        public const float BodyBoxH = 104f, BodyLineSpacing = 1.05f;

        /// <summary>The order on screen: the three recommended starters first.</summary>
        public static readonly ClassId[] Order = { ClassId.Farmer, ClassId.Warrior, ClassId.Miner, ClassId.Healer, ClassId.Demolitions, ClassId.Builder };
        public const int Recommended = 3;

        /// <summary>Tests: show it in the Editor too.</summary>
        public static bool ForceForTests;

        readonly Dictionary<ClassId, MenuItem> _cards = new Dictionary<ClassId, MenuItem>();
        readonly Dictionary<ClassId, Text[]> _texts = new Dictionary<ClassId, Text[]>();
        readonly Dictionary<ClassId, GameObject> _marks = new Dictionary<ClassId, GameObject>();
        readonly Dictionary<ClassId, Text> _badgeWords = new Dictionary<ClassId, Text>();
        readonly List<KeyValuePair<Text, string>> _words = new List<KeyValuePair<Text, string>>();

        /// <summary>Gives a label its words through the containment rule (and remembers them so OnShow re-applies them
        /// once the canvas has its real size).</summary>
        void Words(Text t, string s)
        {
            if (t == null) return;
            _words.Add(new KeyValuePair<Text, string>(t, s));
            Apply(t, s);
        }

        static void Apply(Text t, string s)
        {
            ContainedText c = ContainedText.Of(t);
            if (c != null) c.SetSource(s);
            else t.text = s;
        }

        protected override void OnShow()
        {
            for (int i = 0; i < _words.Count; i++) Apply(_words[i].Key, _words[i].Value);
        }

        /// <summary>Tests: the labels of card c (name, line, difficulty word) and its badge word (null if not recommended).</summary>
        public Text[] CardLabels(ClassId c) => _texts.TryGetValue(c, out Text[] t) ? t : null;
        public Text BadgeWord(ClassId c) => _badgeWords.TryGetValue(c, out Text t) ? t : null;
        public MenuItem CardItem(ClassId c) => _cards.TryGetValue(c, out MenuItem m) ? m : null;

        /// <summary>px lane F (pass 2 review: "cards have empty bodies"): what card c shows (tests and captures).</summary>
        public string CardTitle(ClassId c) => _texts.TryGetValue(c, out Text[] t) ? t[0].text : null;
        public string CardLine(ClassId c) => _texts.TryGetValue(c, out Text[] t) ? t[1].text : null;
        /// <summary>Cards carrying the RECOMMENDED badge (Farmer, Warrior, Miner).</summary>
        public int RecommendedMarks
        {
            get { int n = 0; foreach (GameObject g in _marks.Values) if (g != null && g.activeSelf) n++; return n; }
        }
        public bool IsRecommended(ClassId c) => _marks.TryGetValue(c, out GameObject g) && g != null && g.activeSelf;

        /// <summary>Opaque: the screen sits over a live match in review captures, and nothing behind it may show.</summary>
        protected override float WashAlpha => 1f;
        MenuItem _practice, _back;

        public IReadOnlyDictionary<ClassId, MenuItem> Cards => _cards;
        public MenuItem PracticeItem => _practice;
        public MenuItem BackItem => _back;

        protected override string HintKeys => "ENTER PLAY   ·   ARROWS MOVE   ·   ESC BACK";
        protected override string HintPad => "A PLAY   ·   STICK MOVE   ·   B BACK";

        /// <summary>The first PLAY NOW of this profile, in a player build, with the flow running.</summary>
        public static bool ShouldShow =>
            (ForceForTests || (!Application.isEditor && !Application.isBatchMode)) && !GameFlow.Autoplay && !Done && FlowDirector.Current != null;

        public static bool Done => CmPrefs.GetInt(DoneKey, 0) != 0;

        public static void MarkDone()
        {
            CmPrefs.SetInt(DoneKey, 1);
            CmPrefs.Save();
        }

        /// <summary>One line of what you DO as this class (no numbers: the class cards have them).</summary>
        public static string Does(ClassId c)
        {
            switch (c)
            {
                case ClassId.Farmer: return "Quick staff hooks that land easily. Lob seeds to slow a whole crowd.";
                case ClassId.Warrior: return "The fighter. A wide cleave, and a shield that takes hits from the front.";
                case ClassId.Miner: return "Heavy pick swings. Knock walls down and drop barricades.";
                case ClassId.Healer: return "Mend the ally you aim at. Healing wings for everyone near you.";
                case ClassId.Demolitions: return "Throw TNT from range and lay mines. Breaks walls and the ground.";
                default: return "Ladders up walls, bridges over gaps: the team's shortcut maker.";
            }
        }

        /// <summary>Difficulty pips 1..3 (Resources/Lore/classes.json's ratings).</summary>
        public static int Pips(ClassId c) => c == ClassId.Warrior ? 1 : c == ClassId.Demolitions || c == ClassId.Builder ? 3 : 2;

        protected override void BuildContent(RectTransform space)
        {
            // pass 3 review: labels given their words at creation showed blank in the standalone copy (the containment rule,
            // ContainedText, is attached by HudText.Make before the box is placed); every label here is built empty and gets
            // its words through Words() -> ContainedText.SetSource, as the screens that render (VersusSetup) do via HudText.Set.
            Words(MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleLeft, 152f, 52f, 1300f, 70f, "", 4f), "PICK YOUR FIGHTER");
            Words(MenuUi.Label(space, "Sub", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(Cream, 0.88f), TextAnchor.MiddleLeft, 152f, 118f, 1500f, 40f, "", 2f),
                "New here? The top row is the easiest start. You can switch class at every respawn.");
            for (int i = 0; i < Order.Length; i++)
            {
                int col = i % 3, row = i / 3;
                BuildCard(space, Order[i], 152f + col * (CardW + CardGap), row == 0 ? Row0 : Row1, i < Recommended);
            }
            _back = Button(space, "Back", 152f, 860f, 300f, 84f, "BACK", Back, UiScale.TypeHeading);
            _practice = Button(space, "Practice First", 1268f, 860f, 500f, 84f, "PRACTICE FIRST", PracticeFirst, UiScale.TypeHeading);
            Wire();
            DefaultFocus = _cards[Order[0]];
        }

        void BuildCard(RectTransform space, ClassId c, float x, float y, bool recommended)
        {
            MenuItem card = MenuUi.Item(space, "Class " + c, x, y, CardW, CardH, "", MenuItem.Kind.Button, UiScale.TypeHeading, true, CMTokens.Display);
            card.Body.Radius = 20f;
            card.Body.RimPx = recommended ? 4f : 2f;
            if (recommended) card.Body.SetRim(Brass);
            ClassId pick = c;
            card.Submitted = () => Pick(pick);
            _cards[c] = card;
            Transform t = card.transform;
            Text name = MenuUi.Label(t, "Name", CMTokens.Display, UiScale.TypeLead, Cream, TextAnchor.MiddleLeft, 28f, 24f, CardW - 56f, 54f, "", 3f);
            MenuUi.Fit(name, UiScale.TypeSmall);
            Words(name, FlowWords.ClassName(c));
            Text line = MenuUi.Label(t, "Does", CMTokens.Ui, BodyType, Cream, TextAnchor.UpperLeft, 28f, 88f, CardW - 56f, BodyBoxH, "", 2f);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            line.lineSpacing = BodyLineSpacing;
            MenuUi.Fit(line, BodyTypeMin);
            Words(line, Does(c));
            Text dl = MenuUi.Label(t, "Difficulty Label", CMTokens.Ui, DifficultyType, CMTokens.WithAlpha(Cream, 0.75f), TextAnchor.MiddleLeft, 28f, 198f, 140f, 28f, "", 2f);
            Words(dl, "DIFFICULTY");
            if (recommended)
            {
                // a corner tab ABOVE the card (pass 3: a pill inside the card covered the name): brass tab, navy word
                VectorShape badge = MenuUi.Shape(space, "Recommended Tab " + c, ShapeKind.RoundRect, x + 18f, y - 36f, 220f, 34f, Brass, 10f);
                badge.RimPx = 0f;
                Text bt = MenuUi.Label(badge.transform, "Word", CMTokens.Ui, UiScale.TypeMin, Navy, TextAnchor.MiddleCenter, 0f, 0f, 220f, 34f, "", 0f);
                Words(bt, "RECOMMENDED");
                _marks[c] = badge.gameObject;
                _badgeWords[c] = bt;
            }
            _texts[c] = new[] { name, line, dl };
        }

        void Wire()
        {
            for (int i = 0; i < Order.Length; i++)
            {
                int col = i % 3, row = i / 3;
                MenuItem it = _cards[Order[i]];
                it.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = _cards[Order[row * 3 + (col + 2) % 3]],
                    selectOnRight = _cards[Order[row * 3 + (col + 1) % 3]],
                    selectOnUp = row == 0 ? (col == 2 ? _practice : _back) : _cards[Order[col]],
                    selectOnDown = row == 0 ? _cards[Order[3 + col]] : (col == 2 ? _practice : _back),
                };
            }
            _back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _cards[Order[3]], selectOnDown = _cards[Order[0]], selectOnRight = _practice, selectOnLeft = _practice };
            _practice.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _cards[Order[5]], selectOnDown = _cards[Order[2]], selectOnLeft = _back, selectOnRight = _back };
        }

        protected override void Tick()
        {
            MenuItem f = Focused;
            foreach (KeyValuePair<ClassId, MenuItem> kv in _cards)
            {
                Color want = f == kv.Value ? Navy : Cream;
                Text[] ts = _texts[kv.Key];
                for (int k = 0; k < ts.Length; k++)
                {
                    Color c = k == 2 ? CMTokens.WithAlpha(want, 0.75f) : want;
                    if (ts[k].color == c) continue;
                    ts[k].color = c;
                    TextOutline o = ts[k].GetComponent<TextOutline>();
                    if (o != null) o.enabled = f != kv.Value;
                }
            }
        }

        /// <summary>The class under the focus (PRACTICE FIRST uses it).</summary>
        public ClassId FocusedClass()
        {
            MenuItem f = Focused;
            foreach (KeyValuePair<ClassId, MenuItem> kv in _cards) if (kv.Value == f) return kv.Key;
            return Order[0];
        }

        /// <summary>A class card: remember it as the Play Now class, mark the pick done, start the match.</summary>
        public void Pick(ClassId c)
        {
            Remember(c);
            Hide();
            PlayNow.GoNow();
        }

        /// <summary>PRACTICE FIRST: the range with the focused class (a standing dummy of the same class), via the ready screen.</summary>
        public void PracticeFirst()
        {
            ClassId c = FocusedClass();
            Remember(c);
            VersusPrefs.SetMine(MatchMode.Range, c);
            if (Director == null) return;
            Director.ShowReady(new PlayLaunch { Choice = PracticeChoice(c), BackTo = LobbyScreen.Home });
        }

        /// <summary>The range PRACTICE FIRST plays (tests).</summary>
        public static MatchSetupChoice PracticeChoice(ClassId c) =>
            PlayModes.RangeChoice(SetupPrefs.EnsureLoaded(), c, ClassId.Warrior, RangeDummy.Standing, CriticalMass.Sim.Core.BotDifficulty.Normal);

        /// <summary>The pick is the class every later PLAY NOW keeps (the saved setup's class), and the screen never shows again.</summary>
        public static void Remember(ClassId c)
        {
            MatchSetupChoice s = SetupPrefs.EnsureLoaded();
            s.Class = c;
            GameFlow.LastSetup = s;
            CmPrefs.SetInt(SetupPrefs.ClassKey, (int)c);
            MarkDone();
        }

        public override void Back()
        {
            if (Director != null) Director.ShowTitle();
        }
    }
}
