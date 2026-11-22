using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// The ways into a match from the home screen. Custom (0) is the match-setup path and today's rules bit for bit, so a
    /// MatchSetupChoice made anywhere else (Play Now, the guided match, tests) keeps behaving exactly as before.
    /// </summary>
    public enum MatchMode
    {
        Custom = 0,
        Quick = 1,
        FullBattle = 2,
        Training = 3,
        Practice = 4,
        Spectate = 5,
        Demo = 6,
        /// <summary>px lane F: one on one against a bot: both classes and its difficulty chosen (VersusSetup), a 3:00 clock.</summary>
        Duel = 7,
        /// <summary>px lane F: the practice range: one dummy (stands, moves, or fights back), no clock pressure, no idle autopilot.</summary>
        Range = 8,
    }

    /// <summary>px lane F: what the practice range's dummy does (closest existing bot behaviour; see PlayModes.RangeRules).</summary>
    public enum RangeDummy
    {
        /// <summary>Never attacks you (passive for the whole match), Easy: it walks to the hill and mostly stands there.</summary>
        Standing = 0,
        /// <summary>Never attacks you, Hard: it keeps moving and dodges (Hard reflexes).</summary>
        Moving = 1,
        /// <summary>A real bot at the chosen difficulty: it fights back.</summary>
        Fighting = 2,
    }

    /// <summary>
    /// Match settings a mode changes on the fresh GameConfig of its own world (SimDriverBehaviour makes one per world with
    /// GameConfig.CreateDefault, so nothing leaks into the next match). 0 = the config's own value. Only fields the sim
    /// already reads are used: MatchSeconds and HatchSeconds (Match.MatchSystem, Kaiju.Ai) and Bots.PassiveSeconds (DG-16:
    /// bots neither target nor damage the human slot inside that window). Nothing under Sim changes.
    /// </summary>
    [Serializable]
    public struct MatchRules
    {
        public double MatchSeconds;
        public double HatchSeconds;
        public double PassiveBotSeconds;
        /// <summary>px lane F: the bot roster's only class, + 1 (0 = the config's own mix). Duel and range: the enemy's class.</summary>
        public int OnlyClassPlusOne;
        /// <summary>px lane F fix round: the egg never hatches (duel and range): HatchSeconds goes an hour past the final
        /// whistle, so Kaiju.Ai's "time + eps >= hatch" can never fire on the last tick.</summary>
        public bool NoKaiju;

        /// <summary>How far past the final whistle a NoKaiju match puts the hatch.</summary>
        public const double NoKaijuMargin = 3600;

        public bool IsDefault => MatchSeconds <= 0 && HatchSeconds <= 0 && PassiveBotSeconds <= 0 && OnlyClassPlusOne <= 0 && !NoKaiju;

        /// <summary>Writes the non-zero fields into cfg (the hatch never lands after the final whistle).</summary>
        public void ApplyTo(GameConfig cfg)
        {
            if (cfg == null || IsDefault) return;
            if (MatchSeconds > 0) cfg.MatchSeconds = MatchSeconds;
            if (HatchSeconds > 0) cfg.HatchSeconds = Math.Min(HatchSeconds, cfg.MatchSeconds);
            if (PassiveBotSeconds > 0 && cfg.Bots != null) cfg.Bots.PassiveSeconds = PassiveBotSeconds;
            if (OnlyClassPlusOne > 0) OnlyClass(cfg, (ClassId)(OnlyClassPlusOne - 1));
            if (NoKaiju) cfg.HatchSeconds = cfg.MatchSeconds + NoKaijuMargin;
        }

        /// <summary>px lane F: every bot-filled roster slot takes class c (all three class-mix tables the sim reads, one-hot).
        /// The human's own class is still forced by WorldOptions.HumanClass; at team size 1 this is exactly "the enemy is c".</summary>
        public static void OnlyClass(GameConfig cfg, ClassId c)
        {
            int k = (int)c;
            OneHot(cfg.Bots != null ? cfg.Bots.ClassMix : null, k);
            if (cfg.Depth != null && cfg.Depth.Roles != null)
            {
                OneHot(cfg.Depth.Roles.ClassMix, k);
                OneHot(cfg.Depth.Roles.SmallTeamClassMix, k);
            }
        }

        static void OneHot(double[] mix, int k)
        {
            if (mix == null || k < 0 || k >= mix.Length) return;
            for (int i = 0; i < mix.Length; i++) mix[i] = i == k ? 1.0 : 0.0;
        }
    }

    /// <summary>What a mode card says: title, one line, team size, typical length, difficulty pips (0 = watching).</summary>
    public sealed class ModeInfo
    {
        public MatchMode Mode;
        public string Title;
        public string Line;
        /// <summary>1 easy .. 3 hard; 0 for Spectate (nobody to beat).</summary>
        public int Difficulty;
        /// <summary>Spectate: the player watches, never plays.</summary>
        public bool Watching;
        /// <summary>Skips class select (Spectate watches; Custom picks its class on the setup screen).</summary>
        public bool SkipsClassSelect;
    }

    /// <summary>
    /// Home lobby modes: their cards, how each maps onto MatchSetupChoice and MatchRequest, and the continue logic (Play
    /// again, Rematch with the same seed). Pure statics and PlayerPrefs; screens call PlayFlow to move between them.
    /// <para>Team sizes: World.ClampTeamSize accepts 1..GameConfig.TeamSizeMax (16) and the 8 v 8 backdrop behind every menu
    /// already runs on that path, so 5 v 5, 8 v 8 and 16 v 16 are all real sim sizes, not labels.</para>
    /// <para>Human control: every mode except Spectate starts as a normal match request (Backdrop false, so the driver
    /// never puts actor 0 on autopilot), with SpectatorMode.SpectateNextMatch and the guided-match flag cleared. The
    /// autopilot lock-out bug itself is fixed on branch fix/human-control; this code only sets request flags.</para>
    /// </summary>
    public static class PlayModes
    {
        public const string LastModeKey = "cm.flow.mode.last", QuickSizeKey = "cm.flow.mode.quicksize";
        public static readonly string[] AllKeys = { LastModeKey, QuickSizeKey };

        /// <summary>The mode select's cards, in reading order (two rows of three).</summary>
        public static readonly MatchMode[] SelectOrder =
        {
            MatchMode.Quick, MatchMode.FullBattle, MatchMode.Training, MatchMode.Practice, MatchMode.Spectate, MatchMode.Custom,
        };

        /// <summary>Quick match sizes (players per team).</summary>
        public static readonly int[] QuickSizes = { 5, 8 };
        public const int FullBattleSize = 16, PracticeSize = 5, DemoSize = 5, TrainingSize = 5, SpectateSize = 16, DuelSize = 1, RangeSize = 1;

        /// <summary>px lane F: a duel is a 3:00 clock (the sim has no knock-out limit: first to 3 needs a sim rule), the egg
        /// never hatches inside it (the Kaiju is a 16 v 16 story, not a duel's).</summary>
        public const double DuelMatchSeconds = 180;
        /// <summary>px lane F: the range runs an hour (no shorter clock to beat; the sim has no "no clock" option) with the egg
        /// held back for all of it; Standing and Moving dummies are passive for the whole hour.</summary>
        public const double RangeMatchSeconds = 3600;

        /// <summary>Quick match: 4:00 at Standard, the egg at 2:00 (half of the full clock, so the match keeps its shape).</summary>
        public const double QuickMatchSeconds = 240, QuickHatchSeconds = 120;
        /// <summary>Practice: a 20-minute clock and bots that never target or damage the player for all of it.</summary>
        public const double PracticeMatchSeconds = 1200;
        /// <summary>Quick demo: 80 s at Standard, a 14 s grace before bots attack, the egg hatching at 0:36.</summary>
        public const double DemoMatchSeconds = 80, DemoHatchSeconds = 36, DemoPassiveSeconds = 14;

        public const string MapName = "SKYBREAK BASIN";

        // ---- session state (statics survive Boot reloads; reset per play session) ----

        static bool _hasSession;
        static MatchSetupChoice _sessionLast;
        static MatchMode _sessionMode;
        static bool _hasAbandoned;
        static MatchSetupChoice _abandoned;
        static int _abandonedSeed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => ResetForTests();

        public static void ResetForTests()
        {
            _hasSession = false;
            _sessionLast = default;
            _sessionMode = MatchMode.Custom;
            _hasAbandoned = false;
            _abandoned = default;
            _abandonedSeed = 0;
            if (_idleSuppressed) CriticalMass.Runtime.Game.HumanControlGuard.IdleAutopilotEnabled = _idleSaved;
            _idleSuppressed = false;
        }

        // ---- cards ----

        public static ModeInfo Info(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.Quick:
                    return new ModeInfo { Mode = m, Title = "QUICK MATCH", Line = "A short skirmish. Fewer fighters, faster fights, same hill.", Difficulty = 2 };
                case MatchMode.FullBattle:
                    return new ModeInfo { Mode = m, Title = "FULL BATTLE", Line = "Sixteen a side. The whole basin fights over one hill.", Difficulty = 3 };
                case MatchMode.Training:
                    return new ModeInfo { Mode = m, Title = "TRAINING", Line = MascotName() + " walks you through your first match, one step at a time.", Difficulty = 1 };
                case MatchMode.Practice:
                    return new ModeInfo { Mode = m, Title = "PRACTICE", Line = "Bots play the match around you but never target you. Try things.", Difficulty = 1 };
                case MatchMode.Spectate:
                    return new ModeInfo { Mode = m, Title = "SPECTATE", Line = "Watch a full bot match with the free camera. You are not playing.", Difficulty = 0, Watching = true, SkipsClassSelect = true };
                case MatchMode.Demo:
                    return new ModeInfo { Mode = m, Title = "QUICK DEMO", Line = "A guided minute: move, hit, the hill and the Kaiju.", Difficulty = 1 };
                case MatchMode.Duel:
                    return new ModeInfo { Mode = m, Title = "DUEL", Line = "One on one against a bot. Pick both classes and how hard it fights.", Difficulty = 2, SkipsClassSelect = true };
                case MatchMode.Range:
                    return new ModeInfo { Mode = m, Title = "PRACTICE RANGE", Line = "A dummy that never hits back (unless you ask). Switch class any time.", Difficulty = 1, SkipsClassSelect = true };
                default:
                    return new ModeInfo { Mode = MatchMode.Custom, Title = "CUSTOM", Line = "Your rules: team, size, speed, enemy bots and class.", Difficulty = 2, SkipsClassSelect = true };
            }
        }

        static string MascotName() => Hud.MascotGuide.MascotName;

        /// <summary>The card's team-size line ("5 V 5"); Custom shows the size it will use.</summary>
        public static string SizeLabel(int teamSize) => teamSize + " V " + teamSize;

        /// <summary>Players per team a mode uses (Quick: the chosen quick size; Custom: the basis' size).</summary>
        public static int TeamSize(MatchMode m, MatchSetupChoice basis, int quickSize)
        {
            switch (m)
            {
                case MatchMode.Quick: return ValidQuickSize(quickSize);
                case MatchMode.FullBattle: return FullBattleSize;
                case MatchMode.Training: return TrainingSize;
                case MatchMode.Practice: return PracticeSize;
                case MatchMode.Spectate: return SpectateSize;
                case MatchMode.Demo: return DemoSize;
                case MatchMode.Duel: return DuelSize;
                case MatchMode.Range: return RangeSize;
                default: return basis.TeamSize <= 5 ? 5 : 16;
            }
        }

        public static int ValidQuickSize(int n) => Array.IndexOf(QuickSizes, n) >= 0 ? n : QuickSizes[0];

        /// <summary>The rules a mode applies to its world's config (Custom, Full battle, Training, Spectate: none).</summary>
        public static MatchRules Rules(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.Quick: return new MatchRules { MatchSeconds = QuickMatchSeconds, HatchSeconds = QuickHatchSeconds };
                case MatchMode.Practice: return new MatchRules { MatchSeconds = PracticeMatchSeconds, PassiveBotSeconds = PracticeMatchSeconds };
                case MatchMode.Demo:
                    return new MatchRules { MatchSeconds = DemoMatchSeconds, HatchSeconds = DemoHatchSeconds, PassiveBotSeconds = DemoPassiveSeconds };
                case MatchMode.Duel: return new MatchRules { MatchSeconds = DuelMatchSeconds, HatchSeconds = DuelMatchSeconds, NoKaiju = true };
                case MatchMode.Range: return new MatchRules { MatchSeconds = RangeMatchSeconds, HatchSeconds = RangeMatchSeconds, NoKaiju = true };
                default: return default;
            }
        }

        /// <summary>
        /// px lane F: the rules for a whole choice: the mode's, plus the duel's enemy class and the range's dummy (its class,
        /// and passive for the whole match unless it fights back). Every other mode: Rules(c.Mode) exactly.
        /// </summary>
        public static MatchRules Rules(MatchSetupChoice c)
        {
            MatchRules r = Rules(c.Mode);
            if (c.Mode == MatchMode.Duel || c.Mode == MatchMode.Range)
                r.OnlyClassPlusOne = MatchSetup.Playable.Contains(c.EnemyClass) ? (int)c.EnemyClass + 1 : (int)ClassId.Warrior + 1;
            if (c.Mode == MatchMode.Range && c.Dummy != RangeDummy.Fighting) r.PassiveBotSeconds = RangeMatchSeconds;
            return r;
        }

        /// <summary>
        /// px lane F fix round (Editor review: "QueueMatch with Mode = Range / Duel produced a 16 v 16 world"): a choice that
        /// names Duel or Range is a duel or the range whoever built it. Team size 1 always; the range dummy's difficulty
        /// follows its kind (Standing Easy, Moving Hard). Every other mode is returned unchanged. MatchRequest.From and
        /// GameFlow.PrepareMatch both pass through here, so a script that sets only Mode gets the real mode.
        /// </summary>
        public static MatchSetupChoice Normalize(MatchSetupChoice c)
        {
            if (c.Mode != MatchMode.Duel && c.Mode != MatchMode.Range) return c;
            c.TeamSize = c.Mode == MatchMode.Duel ? DuelSize : RangeSize;
            if (!MatchSetup.Playable.Contains(c.EnemyClass)) c.EnemyClass = ClassId.Warrior;
            if (c.Mode == MatchMode.Range)
            {
                if (c.Dummy == RangeDummy.Standing) c.BotDifficulty = CriticalMass.Sim.Core.BotDifficulty.Easy;
                else if (c.Dummy == RangeDummy.Moving) c.BotDifficulty = CriticalMass.Sim.Core.BotDifficulty.Hard;
            }
            return c;
        }

        /// <summary>px lane F: a duel setup (team size 1, my class, the enemy's class, its difficulty).</summary>
        public static MatchSetupChoice DuelChoice(MatchSetupChoice basis, ClassId mine, ClassId enemy, CriticalMass.Sim.Core.BotDifficulty bots)
        {
            basis.Class = mine;
            basis.EnemyClass = enemy;
            basis.BotDifficulty = bots;
            return ToChoice(MatchMode.Duel, basis, 0);
        }

        /// <summary>px lane F: a range setup. Standing = Easy, Moving = Hard (its reflexes keep it moving), Fighting = bots.</summary>
        public static MatchSetupChoice RangeChoice(MatchSetupChoice basis, ClassId mine, ClassId dummy, RangeDummy kind, CriticalMass.Sim.Core.BotDifficulty fightBots)
        {
            basis.Class = mine;
            basis.EnemyClass = dummy;
            basis.Dummy = kind;
            basis.BotDifficulty = kind == RangeDummy.Standing ? CriticalMass.Sim.Core.BotDifficulty.Easy
                : kind == RangeDummy.Moving ? CriticalMass.Sim.Core.BotDifficulty.Hard : fightBots;
            return ToChoice(MatchMode.Range, basis, 0);
        }

        /// <summary>px lane F: SWAP CLASSES on a duel's results: my class and the enemy's change places.</summary>
        public static MatchSetupChoice Swapped(MatchSetupChoice c)
        {
            ClassId t = c.Class;
            c.Class = c.EnemyClass;
            c.EnemyClass = t;
            return c;
        }

        /// <summary>
        /// The setup a mode plays: team and class from basis (the last setup), size, pace and enemy bots from the mode.
        /// Quick, Practice, Spectate, Training and Demo run at Standard so their card's length is true; Full battle and
        /// Custom keep the player's chosen speed.
        /// </summary>
        public static MatchSetupChoice ToChoice(MatchMode m, MatchSetupChoice basis, int quickSize)
        {
            MatchSetupChoice c = basis;
            c.Mode = m;
            c.TeamSize = TeamSize(m, basis, quickSize);
            double chosen = basis.Pace > 0 ? MatchSpeed.Snap(basis.Pace) : MatchSpeed.DefaultPace;
            switch (m)
            {
                case MatchMode.FullBattle:
                case MatchMode.Custom:
                    c.Pace = chosen;
                    break;
                default:
                    c.Pace = MatchSpeed.StandardPace;
                    break;
            }
            if (m == MatchMode.Demo) c.BotDifficulty = CriticalMass.Sim.Core.BotDifficulty.Easy;
            if ((m == MatchMode.Duel || m == MatchMode.Range) && !MatchSetup.Playable.Contains(c.EnemyClass)) c.EnemyClass = ClassId.Warrior;
            if (m == MatchMode.Spectate) c.Team = TeamId.Azure;
            if (!MatchSetup.Playable.Contains(c.Class)) c.Class = ClassId.Warrior;
            return c;
        }

        /// <summary>Sim seconds of a mode's clock (its rules, else the config's).</summary>
        public static double MatchSeconds(MatchMode m)
        {
            MatchRules r = Rules(m);
            return r.MatchSeconds > 0 ? r.MatchSeconds : DefaultMatchSeconds;
        }

        static double _defaultMatchSeconds = -1;
        static double DefaultMatchSeconds
        {
            get
            {
                if (_defaultMatchSeconds <= 0) _defaultMatchSeconds = GameConfig.CreateDefault().MatchSeconds;
                return _defaultMatchSeconds;
            }
        }

        /// <summary>Typical length on the card, in whole real minutes at the pace the mode runs ("ABOUT 4 MIN").</summary>
        public static string LengthLabel(MatchMode m, double pace)
        {
            if (m == MatchMode.Training) return "AT YOUR PACE";
            if (m == MatchMode.Range) return "NO CLOCK TO BEAT";
            double real = MatchSeconds(m) / (pace > 0 ? pace : 1);
            if (real < 120) return Mathf.RoundToInt((float)real) + " SECONDS";
            int min = Mathf.Max(1, Mathf.RoundToInt((float)(real / 60.0)));
            return (m == MatchMode.Practice ? "UP TO " : "ABOUT ") + min + " MIN";
        }

        // ---- flags: the human is in control in every mode but Spectate ----

        /// <summary>
        /// Called by GameFlow when a match is queued. Custom leaves every flag as the caller set it (match setup's SPECTATE,
        /// the guided match's own start). Spectate turns the spectator camera on; every other mode turns it off and, outside
        /// Training, clears the guided-match flag, so the human drives actor 0 from the first frame.
        /// </summary>
        public static void OnMatchPrepared(MatchSetupChoice s)
        {
            MatchMode mode = Trial.PendingStart ? MatchMode.Training : s.Mode;
            if (s.Mode != MatchMode.Custom)
            {
                SpectatorMode.SpectateNextMatch = s.Mode == MatchMode.Spectate;
                if (s.Mode != MatchMode.Training) Trial.PendingStart = false;
            }
            _hasSession = true;
            _sessionLast = s;
            _sessionMode = mode;
            _hasAbandoned = false;
            SuppressIdleAutopilot(mode == MatchMode.Range);
        }

        static bool _idleSuppressed, _idleSaved = true;

        /// <summary>px lane F: the idle autopilot never takes the player's fighter on the practice range (standing still to
        /// read a tooltip there is the point); every other mode gets back whatever it was before.</summary>
        static void SuppressIdleAutopilot(bool range)
        {
            if (range && !_idleSuppressed)
            {
                _idleSaved = CriticalMass.Runtime.Game.HumanControlGuard.IdleAutopilotEnabled;
                CriticalMass.Runtime.Game.HumanControlGuard.IdleAutopilotEnabled = false;
                _idleSuppressed = true;
            }
            else if (!range && _idleSuppressed)
            {
                CriticalMass.Runtime.Game.HumanControlGuard.IdleAutopilotEnabled = _idleSaved;
                _idleSuppressed = false;
            }
        }

        /// <summary>px lane F (tests): the range turned the idle autopilot off.</summary>
        public static bool IdleAutopilotSuppressed => _idleSuppressed;

        /// <summary>The player left a match through pause (Quit to title) before the end. Demo and Training are not offered back.</summary>
        public static void NoteAbandoned(MatchSetupChoice s, int seed)
        {
            if (_sessionMode == MatchMode.Demo || _sessionMode == MatchMode.Training || seed == 0) return;
            _hasAbandoned = true;
            _abandoned = s;
            _abandonedSeed = seed;
        }

        /// <summary>The guided match will start in the next world (tests: the flag is internal to Onboarding).</summary>
        public static bool GuidedMatchPending => Trial.PendingStart;

        public static bool HasAbandoned => _hasAbandoned;
        public static int AbandonedSeed => _abandonedSeed;
        public static MatchSetupChoice AbandonedSetup => _abandoned;
        public static void ClearAbandoned() => _hasAbandoned = false;

        /// <summary>The mode of the last match queued this session (Custom when none).</summary>
        public static MatchMode SessionMode => _sessionMode;
        public static bool PlayedThisSession => _hasSession;

        // ---- prefs ----

        public static MatchMode SavedMode
        {
            get
            {
                int v = PlayerPrefs.GetInt(LastModeKey, (int)MatchMode.Quick);
                MatchMode m = (MatchMode)v;
                return Array.IndexOf(SelectOrder, m) >= 0 && m != MatchMode.Training ? m : MatchMode.Quick;
            }
        }

        public static int SavedQuickSize => ValidQuickSize(PlayerPrefs.GetInt(QuickSizeKey, QuickSizes[0]));

        public static void Save(MatchMode m, int quickSize)
        {
            if (m != MatchMode.Training && m != MatchMode.Demo && Array.IndexOf(SelectOrder, m) >= 0) PlayerPrefs.SetInt(LastModeKey, (int)m);   // Duel / Range: not cards (saved by VersusSetup)
            PlayerPrefs.SetInt(QuickSizeKey, ValidQuickSize(quickSize));
            PlayerPrefs.Save();
        }

        // ---- continue logic ----

        /// <summary>What the home screen's primary button and rematch tile offer right now.</summary>
        public static HomeOffer Offer() => Offer(_hasSession, _sessionMode, _sessionLast, SavedMode, _hasAbandoned, _abandoned, _abandonedSeed);

        /// <summary>The continue rules, from explicit inputs (tests).</summary>
        public static HomeOffer Offer(bool playedThisSession, MatchMode sessionMode, MatchSetupChoice sessionSetup, MatchMode savedMode,
            bool abandoned, MatchSetupChoice abandonedSetup, int abandonedSeed)
        {
            var o = new HomeOffer { Mode = savedMode, Rematch = abandoned && abandonedSeed != 0, RematchSeed = abandonedSeed, RematchSetup = abandonedSetup };
            if (!playedThisSession) return o;
            if (sessionMode == MatchMode.Training || sessionMode == MatchMode.Demo)
            {
                // After the guided match or the demo the next step is a real one, not the lesson again.
                o.Mode = MatchMode.Quick;
                o.NextStep = true;
                return o;
            }
            o.PlayAgain = true;
            o.Mode = sessionMode;
            o.Setup = sessionSetup;
            return o;
        }
    }

    /// <summary>The home screen's offer: PLAY (choose a mode) or PLAY AGAIN (the last mode, one press), and REMATCH.</summary>
    public struct HomeOffer
    {
        public MatchMode Mode;
        /// <summary>true: the primary button replays Setup straight to the ready screen.</summary>
        public bool PlayAgain;
        public MatchSetupChoice Setup;
        /// <summary>After Training or the demo: PLAY with Quick match suggested.</summary>
        public bool NextStep;
        /// <summary>A match was left through the pause menu: replay it from the start on the same seed (it cannot be resumed).</summary>
        public bool Rematch;
        public int RematchSeed;
        public MatchSetupChoice RematchSetup;

        public string PrimaryTitle => PlayAgain ? "PLAY AGAIN" : "PLAY";
    }

    /// <summary>One prepared launch: the setup, its seed (0 = a new one when it starts), and where Back on the ready screen goes.</summary>
    public sealed class PlayLaunch
    {
        public MatchSetupChoice Choice;
        public int Seed;
        public bool Rematch;
        public LobbyScreen BackTo = LobbyScreen.Home;
    }

    /// <summary>The lobby's screens (the navigation graph the tests walk).</summary>
    public enum LobbyScreen { Title, Home, ModeSelect, ClassSelect, Ready, CustomSetup, Barracks, Codex, HowTo, Settings, Match, Results, Versus, StarterPick }

    /// <summary>
    /// The lobby's navigation graph: where each screen's actions lead and where Back goes. The screens implement it; the
    /// EditMode tests check every screen is reachable from the title and has a way back (no dead ends).
    /// </summary>
    public static class LobbyNav
    {
        public static readonly Dictionary<LobbyScreen, LobbyScreen[]> Forward = new Dictionary<LobbyScreen, LobbyScreen[]>
        {
            { LobbyScreen.Title, new[] { LobbyScreen.Home, LobbyScreen.Match, LobbyScreen.StarterPick } },   // HOME; PLAY NOW and QUICK DEMO start a match; the first PLAY NOW picks a class
            { LobbyScreen.Home, new[] { LobbyScreen.ModeSelect, LobbyScreen.Ready, LobbyScreen.Barracks, LobbyScreen.Codex, LobbyScreen.HowTo, LobbyScreen.Settings } },
            { LobbyScreen.ModeSelect, new[] { LobbyScreen.ClassSelect, LobbyScreen.Ready, LobbyScreen.CustomSetup, LobbyScreen.Match, LobbyScreen.Versus } },
            { LobbyScreen.Versus, new[] { LobbyScreen.Ready } },   // px lane F: DUEL / PRACTICE RANGE setup
            { LobbyScreen.StarterPick, new[] { LobbyScreen.Match, LobbyScreen.Ready } },   // a class starts PLAY NOW; PRACTICE FIRST the range
            { LobbyScreen.ClassSelect, new[] { LobbyScreen.Ready } },
            { LobbyScreen.Ready, new[] { LobbyScreen.Match } },
            { LobbyScreen.CustomSetup, new[] { LobbyScreen.Match } },
            { LobbyScreen.Match, new[] { LobbyScreen.Results, LobbyScreen.Title, LobbyScreen.Home } },   // results; pause quit; the demo lands home
            { LobbyScreen.Results, new[] { LobbyScreen.Home, LobbyScreen.Match, LobbyScreen.CustomSetup } },   // a duel: REMATCH and SWAP CLASSES are Match
            { LobbyScreen.Barracks, new LobbyScreen[0] },
            { LobbyScreen.Codex, new LobbyScreen[0] },
            { LobbyScreen.HowTo, new[] { LobbyScreen.Match } },
            { LobbyScreen.Settings, new LobbyScreen[0] },
        };

        /// <summary>Escape / B on each screen (the title's Back is HOME too: it is the way in, never a dead end).</summary>
        public static readonly Dictionary<LobbyScreen, LobbyScreen> Back = new Dictionary<LobbyScreen, LobbyScreen>
        {
            { LobbyScreen.Title, LobbyScreen.Home },
            { LobbyScreen.Home, LobbyScreen.Title },
            { LobbyScreen.ModeSelect, LobbyScreen.Home },
            { LobbyScreen.ClassSelect, LobbyScreen.ModeSelect },
            { LobbyScreen.Ready, LobbyScreen.ClassSelect },   // or the mode cards / home, wherever the launch came from
            { LobbyScreen.CustomSetup, LobbyScreen.ModeSelect },
            { LobbyScreen.Barracks, LobbyScreen.Home },
            { LobbyScreen.Codex, LobbyScreen.Home },
            { LobbyScreen.HowTo, LobbyScreen.Home },
            { LobbyScreen.Settings, LobbyScreen.Home },
            { LobbyScreen.Match, LobbyScreen.Match },      // pause menu (resume); its Quit goes to the title
            { LobbyScreen.Results, LobbyScreen.Home },
            { LobbyScreen.Versus, LobbyScreen.ModeSelect },
            { LobbyScreen.StarterPick, LobbyScreen.Title },
        };
    }

    /// <summary>
    /// Moves the player through the play flow: home, mode select, class select, ready, match. Screens call these; each
    /// keeps one screen visible at a time through FlowDirector (the overlay registry's director).
    /// </summary>
    public static class PlayFlow
    {
        /// <summary>The mode select or the ready screen is the current Setup-state screen (a backdrop renewal reopens mode select).</summary>
        public static bool InModeFlow { get; internal set; }

        static int _quickSize;

        /// <summary>The quick-match size chosen on the mode select (the saved one until the player changes it).</summary>
        public static int QuickSize
        {
            get
            {
                if (_quickSize == 0) _quickSize = PlayModes.SavedQuickSize;
                return _quickSize;
            }
            set => _quickSize = PlayModes.ValidQuickSize(value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            InModeFlow = false;
            _quickSize = 0;
        }

        /// <summary>The setup a mode would play now (team and class from the last setup).</summary>
        public static MatchSetupChoice ChoiceFor(MatchMode m)
        {
            MatchSetupChoice basis = SetupPrefs.EnsureLoaded();
            // Speed and enemy bots are the player's saved choices (a quick match's Standard pace never overwrites them).
            MatchSetupChoice saved = SetupPrefs.Load();
            basis.Pace = saved.Pace;
            basis.BotDifficulty = saved.BotDifficulty;
            return PlayModes.ToChoice(m, basis, QuickSize);
        }

        /// <summary>A card on the mode select was chosen.</summary>
        public static void Choose(MatchMode m)
        {
            FlowDirector d = FlowDirector.Current;
            if (d == null) return;
            PlayModes.Save(m, QuickSize);
            switch (m)
            {
                case MatchMode.Custom:
                    InModeFlow = false;
                    SpectatorMode.SpectateNextMatch = false;
                    d.ShowSetup();
                    return;
                case MatchMode.Spectate:
                    d.ShowReady(new PlayLaunch { Choice = ChoiceFor(m), BackTo = LobbyScreen.ModeSelect });
                    return;
                case MatchMode.Duel:
                case MatchMode.Range:
                    d.ShowVersus(m);
                    return;
                default:
                    PickClass(d, m);
                    return;
            }
        }

        /// <summary>The class screen (DG-10's full class select) for mode m; confirm goes to the ready screen, back to mode select.</summary>
        public static void PickClass(FlowDirector d, MatchMode m)
        {
            if (d == null) return;
            MatchSetupChoice c = ChoiceFor(m);
            d.HideFlowScreens();
            ClassSelect.SelectedClass = c.Class;
            ClassSelect.ShowSetup(c.Team, picked =>
            {
                MatchSetupChoice chosen = c;
                if (MatchSetup.Playable.Contains(picked)) chosen.Class = picked;
                d.ShowReady(new PlayLaunch { Choice = chosen, BackTo = LobbyScreen.ClassSelect });
            }, () => d.ShowModes(m));
        }

        /// <summary>The home screen's PLAY AGAIN: the last setup straight to the ready screen (a new seed).</summary>
        public static void Again(HomeOffer o)
        {
            FlowDirector d = FlowDirector.Current;
            if (d == null) return;
            d.ShowReady(new PlayLaunch { Choice = o.Setup, BackTo = LobbyScreen.Home });
        }

        /// <summary>REMATCH: the abandoned setup on its own seed, through the ready screen.</summary>
        public static void Rematch(HomeOffer o)
        {
            FlowDirector d = FlowDirector.Current;
            if (d == null || !o.Rematch) return;
            d.ShowReady(new PlayLaunch { Choice = o.RematchSetup, Seed = o.RematchSeed, Rematch = true, BackTo = LobbyScreen.Home });
        }

        /// <summary>The ready screen's countdown ended (or was skipped): start the match.</summary>
        public static void Launch(PlayLaunch l)
        {
            if (l == null) return;
            InModeFlow = false;
            MatchSetupChoice c = l.Choice;
            if (c.Mode != MatchMode.Spectate)
            {
                ClassSelect.SelectedClass = c.Class;   // DG-10's respawn strip and pause menu
                // The home diorama shows the last class played, next launch too (speed and bots stay the player's own).
                PlayerPrefs.SetInt(SetupPrefs.ClassKey, (int)c.Class);
                PlayerPrefs.SetInt(SetupPrefs.TeamKey, (int)c.Team);
                PlayerPrefs.Save();
            }
            if (c.Mode == MatchMode.Training)
            {
                // The guided match keeps its own start (5 v 5 at 1x with the mascot); it reads team and class from LastSetup.
                MatchSetupChoice keep = GameFlow.LastSetup;
                keep.Team = c.Team;
                keep.Class = c.Class;
                GameFlow.LastSetup = keep;
                if (OverlayRegistry.StartTutorial()) return;
                c.Mode = MatchMode.Practice;   // no guided match registered: the nearest thing, never a dead button
                c = PlayModes.ToChoice(MatchMode.Practice, c, QuickSize);
            }
            if (l.Rematch) PlayModes.ClearAbandoned();
            GameFlow.BeginMatchWithSeed(c, l.Seed, true);
        }

        /// <summary>QUICK DEMO (title and home): straight into the guided minute, no screens in between.</summary>
        public static bool StartDemo()
        {
            if (!GameFlow.Enabled) return false;
            InModeFlow = false;
            MatchSetupChoice c = PlayModes.ToChoice(MatchMode.Demo, SetupPrefs.EnsureLoaded(), QuickSize);
            ClassSelect.SelectedClass = c.Class;
            GameFlow.BeginMatchWithSeed(c, 0, true);
            return true;
        }
    }
}
