using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// px lane F (2026-11-22): the DUEL and PRACTICE RANGE setup, one screen (the mode select's bottom bar opens it: two
    /// presses from the PLAY menu). DUEL: YOUR CLASS, ENEMY CLASS, ENEMY BOT (Easy / Normal / Hard), START. RANGE: YOUR
    /// CLASS, DUMMY CLASS, DUMMY (STANDS / MOVES / FIGHTS BACK), START. START is focused, so a returning player presses once.
    /// Choices are kept in PlayerPrefs (VersusPrefs). Back: the mode select. The match itself is configured only through
    /// MatchSetupChoice and PlayModes.Rules(choice) (team size 1, the bot roster's class, passive window, clock).
    /// </summary>
    public sealed class VersusSetup : FlowScreen
    {
        readonly List<System.Action> _refresh = new List<System.Action>();
        MenuItem _mine, _enemy, _kind, _start, _back;
        UnityEngine.UI.Text _title, _line, _how;

        /// <summary>Opaque: nothing of a live match shows through the setup.</summary>
        protected override float WashAlpha => 1f;

        /// <summary>px lane F (pass 2 review): how the mode is won, said plainly on its setup screen.</summary>
        public static string HowToWin(MatchMode m) => m == MatchMode.Duel
            ? "HOW TO WIN: most hill time at 3:00. No knock-out limit: a knock-out keeps them off the hill."
            : "NOTHING SCORES HERE: no hill points, no clock to beat, nothing to lose.";

        public string HowLine => _how != null ? _how.text : null;
        MatchMode _mode = MatchMode.Duel;

        public MatchMode Mode => _mode;
        public MenuItem StartItem => _start;
        public MenuItem BackItem => _back;
        public MenuItem MineItem => _mine;
        public MenuItem EnemyItem => _enemy;
        public MenuItem KindItem => _kind;

        protected override string HintKeys => "ENTER START   ·   LEFT / RIGHT CHANGE   ·   ESC BACK";
        protected override string HintPad => "A START   ·   LEFT / RIGHT CHANGE   ·   B BACK";

        /// <summary>Which screen this is (call before Show).</summary>
        public void Bind(MatchMode m)
        {
            _mode = m == MatchMode.Range ? MatchMode.Range : MatchMode.Duel;
            EnsureBuilt();
            Refresh();
        }

        protected override void BuildContent(RectTransform space)
        {
            _title = MenuUi.Label(space, "Title", CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleCenter, 460f, 60f, 1000f, 80f, "DUEL", 4f);
            _line = MenuUi.Label(space, "Line", CMTokens.Ui, UiScale.TypeBody, CMTokens.WithAlpha(Cream, 0.9f), TextAnchor.MiddleCenter, 360f, 150f, 1200f, 40f, "", 2f);
            _how = MenuUi.Label(space, "How To Win", CMTokens.Ui, UiScale.TypeSmall, Brass, TextAnchor.MiddleCenter, 260f, 186f, 1400f, 32f, "", 2f);
            _how.horizontalOverflow = HorizontalWrapMode.Wrap;
            _how.verticalOverflow = VerticalWrapMode.Overflow;
            MenuUi.Panel(space, "Panel", 360f, 220f, 1200f, 560f);
            const float sx = 460f, sw = 1000f, sh = 66f, step = 96f, sy = 280f;
            _mine = Stepper(space, "Mine", sx, sy, sw, sh, "YOUR CLASS", () => FlowWords.ClassName(VersusPrefs.Mine(_mode)),
                d => VersusPrefs.SetMine(_mode, VersusPrefs.Step(VersusPrefs.Mine(_mode), d)), _refresh);
            _enemy = Stepper(space, "Enemy", sx, sy + step, sw, sh, "ENEMY CLASS", () => FlowWords.ClassName(VersusPrefs.Enemy(_mode)),
                d => VersusPrefs.SetEnemy(_mode, VersusPrefs.Step(VersusPrefs.Enemy(_mode), d)), _refresh);
            _kind = Stepper(space, "Kind", sx, sy + 2 * step, sw, sh, "ENEMY BOT", KindText, StepKind, _refresh);
            _start = Button(space, "Start", 1080f, sy + 3 * step + 20f, 380f, 84f, "START", StartMatch, UiScale.TypeLead);
            _start.Body.RimPx = 4f;
            _start.Body.SetRim(Brass);
            _back = Button(space, "Back", 460f, sy + 3 * step + 20f, 300f, 84f, "BACK", Back, UiScale.TypeHeading);
            ChainVertical(new[] { _mine, _enemy, _kind, _start });
            UnityEngine.UI.Navigation sn = _start.navigation;
            sn.selectOnLeft = _back;
            sn.selectOnRight = _back;
            _start.navigation = sn;
            _back.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnUp = _kind, selectOnDown = _mine, selectOnLeft = _start, selectOnRight = _start,
            };
            DefaultFocus = _start;
            Refresh();
        }

        string KindText()
        {
            if (_mode == MatchMode.Range) return VersusPrefs.DummyWord(VersusPrefs.Dummy());
            return MatchSpeedWords.Bots(VersusPrefs.Bots());
        }

        void StepKind(int d)
        {
            if (_mode == MatchMode.Range) VersusPrefs.SetDummy((RangeDummy)(((int)VersusPrefs.Dummy() + (d < 0 ? 2 : 1)) % 3));
            else VersusPrefs.SetBots(VersusPrefs.StepBots(VersusPrefs.Bots(), d));
        }

        void Refresh()
        {
            if (_title == null) return;
            ModeInfo info = PlayModes.Info(_mode);
            HudText.Set(_title, info.Title);
            HudText.Set(_line, info.Line);
            if (_how != null) HudText.Set(_how, HowToWin(_mode));
            if (_enemy != null && _enemy.Label != null) HudText.Set(_enemy.Label, _mode == MatchMode.Range ? "DUMMY CLASS" : "ENEMY CLASS");
            if (_kind != null && _kind.Label != null) HudText.Set(_kind.Label, _mode == MatchMode.Range ? "DUMMY" : "ENEMY BOT");
            for (int i = 0; i < _refresh.Count; i++) _refresh[i]();
        }

        protected override void OnShow() => Refresh();

        /// <summary>The choice START plays now (tests read it).</summary>
        public MatchSetupChoice Choice() => VersusPrefs.ChoiceFor(_mode, SetupPrefs.EnsureLoaded());

        void StartMatch()
        {
            if (Director == null) return;
            VersusPrefs.SaveLastMode(_mode);
            Director.ShowReady(new PlayLaunch { Choice = Choice(), BackTo = LobbyScreen.Versus });
        }

        public override void Back()
        {
            if (Director != null) Director.ShowModes(MatchMode.Quick);
        }
    }

    /// <summary>px lane F: the words for the enemy bot difficulty (the match setup's own wording).</summary>
    public static class MatchSpeedWords
    {
        public static string Bots(BotDifficulty b) => b == BotDifficulty.Easy ? "EASY" : b == BotDifficulty.Hard ? "HARD" : "NORMAL";
    }

    /// <summary>
    /// px lane F: the DUEL and PRACTICE RANGE choices kept between launches (PlayerPrefs). Pure statics; tests back up
    /// AllKeys. The defaults: duel Warrior against a Farmer at Normal; range Warrior at a Standing Warrior dummy.
    /// </summary>
    public static class VersusPrefs
    {
        public const string DuelMineKey = "cm.flow.duel.mine", DuelEnemyKey = "cm.flow.duel.enemy", DuelBotsKey = "cm.flow.duel.bots",
            RangeMineKey = "cm.flow.range.mine", RangeEnemyKey = "cm.flow.range.dummyclass", RangeDummyKey = "cm.flow.range.dummy",
            LastKey = "cm.flow.versus.last";
        public static readonly string[] AllKeys = { DuelMineKey, DuelEnemyKey, DuelBotsKey, RangeMineKey, RangeEnemyKey, RangeDummyKey, LastKey };

        static readonly ClassId[] Order = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        static ClassId Read(string key, ClassId fallback)
        {
            int v = PlayerPrefs.GetInt(key, (int)fallback);
            ClassId c = (ClassId)Mathf.Clamp(v, 0, ClassIds.All.Count - 1);
            return MatchSetup.Playable.Contains(c) ? c : fallback;
        }

        public static ClassId Mine(MatchMode m) => Read(m == MatchMode.Range ? RangeMineKey : DuelMineKey, SetupPrefs.Load().Class);
        public static ClassId Enemy(MatchMode m) => Read(m == MatchMode.Range ? RangeEnemyKey : DuelEnemyKey, m == MatchMode.Range ? ClassId.Warrior : ClassId.Farmer);
        public static void SetMine(MatchMode m, ClassId c) => PlayerPrefs.SetInt(m == MatchMode.Range ? RangeMineKey : DuelMineKey, (int)c);
        public static void SetEnemy(MatchMode m, ClassId c) => PlayerPrefs.SetInt(m == MatchMode.Range ? RangeEnemyKey : DuelEnemyKey, (int)c);

        public static BotDifficulty Bots() => (BotDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(DuelBotsKey, (int)BotDifficulty.Normal), 0, 2);
        public static void SetBots(BotDifficulty b) => PlayerPrefs.SetInt(DuelBotsKey, (int)b);

        /// <summary>Easy, Normal, Hard in reading order (the enum keeps Normal first for the browser bots).</summary>
        public static BotDifficulty StepBots(BotDifficulty b, int d)
        {
            BotDifficulty[] o = { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard };
            int i = System.Array.IndexOf(o, b);
            return o[((i + (d < 0 ? -1 : 1)) % 3 + 3) % 3];
        }

        public static RangeDummy Dummy() => (RangeDummy)Mathf.Clamp(PlayerPrefs.GetInt(RangeDummyKey, 0), 0, 2);
        public static void SetDummy(RangeDummy k) => PlayerPrefs.SetInt(RangeDummyKey, (int)k);

        public static string DummyWord(RangeDummy k) => k == RangeDummy.Moving ? "MOVES, NEVER HITS" : k == RangeDummy.Fighting ? "FIGHTS BACK" : "STANDS, NEVER HITS";

        /// <summary>The next playable class in the card order.</summary>
        public static ClassId Step(ClassId c, int d)
        {
            int i = System.Array.IndexOf(Order, c);
            for (int k = 0; k < Order.Length; k++)
            {
                i = ((i + (d < 0 ? -1 : 1)) % Order.Length + Order.Length) % Order.Length;
                if (MatchSetup.Playable.Contains(Order[i])) return Order[i];
            }
            return c;
        }

        public static void SaveLastMode(MatchMode m)
        {
            PlayerPrefs.SetInt(LastKey, (int)m);
            PlayerPrefs.Save();
        }

        /// <summary>The setup a duel or range launch plays from the saved choices (team and speed from basis).</summary>
        public static MatchSetupChoice ChoiceFor(MatchMode m, MatchSetupChoice basis)
        {
            if (m == MatchMode.Range) return PlayModes.RangeChoice(basis, Mine(m), Enemy(m), Dummy(), Bots());
            return PlayModes.DuelChoice(basis, Mine(m), Enemy(m), Bots());
        }
    }
}
