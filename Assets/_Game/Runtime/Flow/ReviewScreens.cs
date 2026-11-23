using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// px lane F fix round: entry points a reviewer (or a capture script) can call in the Editor to see lane F's three
    /// screens and to start its two modes, since the Editor has no title flow and the first-match class pick hides itself
    /// in the Editor (the PlayTests press PLAY NOW and expect control). Nothing in the game calls these. Exact calls:
    /// data/f-modes-audio/REVIEWER-CALLS.txt.
    /// </summary>
    public static class ReviewScreens
    {
        /// <summary>The first-match class pick. With a FlowDirector: its own screen (every button works). Without one:
        /// a standalone copy on its own canvas (looks the same; its buttons need the director, so it is for a capture).</summary>
        public static bool ShowStarterPick()
        {
            if (FlowDirector.Current != null) return FlowDirector.Current.ShowStarterPick();
            Standalone<StarterPick>().Show();
            return true;
        }

        /// <summary>The DUEL setup (your class, enemy class, enemy bot, START).</summary>
        public static bool ShowDuelSetup() => ShowVersus(MatchMode.Duel);

        /// <summary>The PRACTICE RANGE setup (your class, dummy class, STANDS / MOVES / FIGHTS BACK, START).</summary>
        public static bool ShowRangeSetup() => ShowVersus(MatchMode.Range);

        static bool ShowVersus(MatchMode m)
        {
            if (FlowDirector.Current != null)
            {
                FlowDirector.Current.ShowVersus(m);
                return true;
            }
            VersusSetup v = Standalone<VersusSetup>();
            v.Bind(m);
            v.Show();
            return true;
        }

        /// <summary>Makes the NEXT PLAY NOW show the class pick in the Editor too (clears the profile's "done" key).</summary>
        public static void ArmStarterPick()
        {
            CmPrefs.DeleteKey(StarterPick.DoneKey);
            CmPrefs.Save();
            StarterPick.ForceForTests = true;
        }

        /// <summary>The choice START on the duel screen makes (1 v 1, both classes, the bot's difficulty).</summary>
        public static MatchSetupChoice Duel(ClassId mine, ClassId enemy, BotDifficulty bots) =>
            PlayModes.DuelChoice(SetupPrefs.EnsureLoaded(), mine, enemy, bots);

        /// <summary>The choice START on the range screen makes (one dummy of a class; STANDS / MOVES / FIGHTS BACK).</summary>
        public static MatchSetupChoice Range(ClassId mine, ClassId dummy, RangeDummy kind) =>
            PlayModes.RangeChoice(SetupPrefs.EnsureLoaded(), mine, dummy, kind, BotDifficulty.Normal);

        /// <summary>Starts a duel the way START does (reloads into it; the short loading screen). false: the flow is off.</summary>
        public static bool StartDuel(ClassId mine, ClassId enemy, BotDifficulty bots)
        {
            if (!GameFlow.Enabled) return false;
            GameFlow.BeginMatch(Duel(mine, enemy, bots), true);
            return true;
        }

        /// <summary>Starts the practice range the way START does.</summary>
        public static bool StartRange(ClassId mine, ClassId dummy, RangeDummy kind)
        {
            if (!GameFlow.Enabled) return false;
            GameFlow.BeginMatch(Range(mine, dummy, kind), true);
            return true;
        }

        /// <summary>The standalone copies' canvas order: above the HUD and every overlay.</summary>
        public const int StandaloneSortingOrder = 32000;

        static T Standalone<T>() where T : FlowScreen
        {
            var go = GameObject.Find("Px Review Screens") ?? new GameObject("Px Review Screens");
            T s = go.GetComponent<T>();
            if (s == null) s = go.AddComponent<T>();
            s.EnsureBuilt();
            // pass 2 review: the match HUD showed through. A screen-space-camera canvas always draws under overlay canvases
            // (the HUD), so the standalone copy goes to an overlay canvas above everything; its wash is opaque.
            if (s.Canvas != null)
            {
                s.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                s.Canvas.overrideSorting = true;
                s.Canvas.sortingOrder = StandaloneSortingOrder;
            }
            return s;
        }
    }
}
