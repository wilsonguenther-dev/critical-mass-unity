using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>When an overlay is installed on the Game root.</summary>
    public enum OverlayScope
    {
        /// <summary>Every Boot build (menus and matches).</summary>
        Always,
        /// <summary>Only when a real match is played (flow state Match, or the flow is off).</summary>
        MatchOnly,
        /// <summary>Only behind the title and menus (the backdrop world).</summary>
        MenusOnly,
    }

    /// <summary>
    /// Put on a MonoBehaviour to have GameBootstrap add it to the Game root on every build (HUD extras, banners, kill
    /// feed, announcer). Later items register themselves this way and never edit GameBootstrap.cs. The component finds
    /// what it needs with GetComponent (SimDriverBehaviour, GameHud, HumanInputSource sit on the same object).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GameOverlayAttribute : Attribute
    {
        /// <summary>Install order, low first (ties by full type name).</summary>
        public int Order;
        public OverlayScope Scope = OverlayScope.Always;
    }

    /// <summary>A screen a main-menu entry opens (Barracks, Codex, How to play, Credits...).</summary>
    public interface IMenuScreen
    {
        /// <summary>Show the screen; call onClosed exactly once when the player backs out (the main menu returns).</summary>
        void Open(Action onClosed);
    }

    /// <summary>Optional on an IMenuScreen: the pause key (Escape / Start) while the screen is open calls Back.</summary>
    public interface IMenuBack
    {
        void Back();
    }

    /// <summary>
    /// Put on a MonoBehaviour that implements IMenuScreen to add it to the main menu, or to fill one of the menu's
    /// "coming soon" places by using its id ("howto", "barracks", "codex", "credits"). The main menu adds the component to
    /// the Game root when the entry is chosen and calls Open.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class MenuEntryAttribute : Attribute
    {
        public readonly string Id;
        public readonly string Label;
        public readonly int Order;

        public MenuEntryAttribute(string id, string label, int order)
        {
            Id = id;
            Label = label;
            Order = order;
        }
    }

    /// <summary>
    /// Put on a public static parameterless method that starts the guided first match (DG-12). "How to play" in the main
    /// menu and the pause menu calls it; with none registered (and no [MenuEntry("howto")] screen) the line shows as
    /// coming soon.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TutorialStartAttribute : Attribute
    {
    }

    /// <summary>One line of the main menu.</summary>
    public sealed class MenuEntry
    {
        public string Id;
        public string Label;
        public int Order;
        /// <summary>A registered IMenuScreen component type, or null.</summary>
        public Type ScreenType;
        /// <summary>Built into the main menu itself (Play, Settings, Quit).</summary>
        public bool Builtin;

        /// <summary>false: shown as "coming soon" and skipped by navigation.</summary>
        public bool Available => Builtin || ScreenType != null;
    }

    /// <summary>
    /// DG-01 overlay registry: finds GameOverlay components and MenuEntry screens by attribute (one reflection scan per
    /// domain, cached) so later items plug in without editing GameBootstrap.cs or the menu files. Test assemblies are not
    /// scanned, so a test's probe types never reach the real menu.
    /// </summary>
    public static class OverlayRegistry
    {
        public const string Play = "play", HowTo = "howto", Barracks = "barracks", Settings = "settings",
            Codex = "codex", Credits = "credits", Quit = "quit";

        /// <summary>The menu's own lines; ids without a screen show as coming soon until an item registers one.</summary>
        public static MenuEntry[] Defaults() => new[]
        {
            new MenuEntry { Id = PlayNow, Label = Flow.PlayNow.Label, Order = 5, Builtin = true },
            new MenuEntry { Id = Play, Label = "CUSTOM MATCH", Order = 10, Builtin = true },
            new MenuEntry { Id = HowTo, Label = "HOW TO PLAY", Order = 20, Builtin = TutorialStart() != null },
            new MenuEntry { Id = Barracks, Label = "BARRACKS", Order = 30 },
            new MenuEntry { Id = Controls, Label = "CONTROLS", Order = 35, Builtin = true },
            new MenuEntry { Id = Settings, Label = "SETTINGS", Order = 40, Builtin = true },
            new MenuEntry { Id = Spectate, Label = "SPECTATE", Order = 15, Builtin = true },
            new MenuEntry { Id = Codex, Label = "CODEX", Order = 50, Builtin = true },
            new MenuEntry { Id = Credits, Label = "CREDITS", Order = 60 },
            new MenuEntry { Id = Quit, Label = "QUIT", Order = 90, Builtin = true },
        };

        public const string Controls = "controls";
        /// <summary>The first line: one press into a match with the last class and settings (Flow.PlayNow).</summary>
        public const string PlayNow = "playnow";
        /// <summary>Integration batch 2: watch a match (match setup opens on SPECTATE).</summary>
        public const string Spectate = "spectate";

        static List<Type> _types;
        static bool _tutorialScanned;
        static MethodInfo _tutorial;

        /// <summary>The [TutorialStart] method (DG-12's guided match), or null when no item has registered one.</summary>
        public static MethodInfo TutorialStart()
        {
            if (_tutorialScanned) return _tutorial;
            _tutorialScanned = true;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length && _tutorial == null; i++)
            {
                if (Skip(all[i].GetName().Name)) continue;
                Type[] ts;
                try
                {
                    ts = all[i].GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    ts = e.Types;
                }
                if (ts == null) continue;
                for (int k = 0; k < ts.Length && _tutorial == null; k++)
                {
                    if (ts[k] == null) continue;
                    MethodInfo[] ms = ts[k].GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    for (int m = 0; m < ms.Length; m++)
                    {
                        if (ms[m].GetParameters().Length == 0 && ms[m].IsDefined(typeof(TutorialStartAttribute), false))
                        {
                            _tutorial = ms[m];
                            break;
                        }
                    }
                }
            }
            return _tutorial;
        }

        /// <summary>Starts the guided match; false when none is registered or it threw (logged).</summary>
        public static bool StartTutorial()
        {
            MethodInfo m = TutorialStart();
            if (m == null) return false;
            try
            {
                m.Invoke(null, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        static List<Type> Types()
        {
            if (_types != null) return _types;
            var list = new List<Type>();
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                Assembly a = all[i];
                string n = a.GetName().Name;
                if (Skip(n)) continue;
                Type[] ts;
                try
                {
                    ts = a.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    ts = e.Types;
                }
                if (ts == null) continue;
                for (int k = 0; k < ts.Length; k++)
                {
                    Type t = ts[k];
                    if (t == null || t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                    if (t.IsDefined(typeof(GameOverlayAttribute), false) || t.IsDefined(typeof(MenuEntryAttribute), false)) list.Add(t);
                }
            }
            _types = list;
            return list;
        }

        static bool Skip(string n)
        {
            if (n.StartsWith("System", StringComparison.Ordinal) || n.StartsWith("Unity", StringComparison.Ordinal) ||
                n.StartsWith("Mono.", StringComparison.Ordinal) || n == "mscorlib" || n == "netstandard" ||
                n.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Bee.", StringComparison.Ordinal))
                return true;
            return n.EndsWith(".Tests", StringComparison.Ordinal) || n.EndsWith(".PlayTests", StringComparison.Ordinal);
        }

        /// <summary>Overlay types in install order (Order, then full name).</summary>
        public static List<Type> OverlayTypes(IEnumerable<Type> source = null)
        {
            var result = new List<Type>();
            foreach (Type t in source ?? Types())
                if (t.GetCustomAttribute<GameOverlayAttribute>(false) != null) result.Add(t);
            result.Sort((x, y) =>
            {
                int c = x.GetCustomAttribute<GameOverlayAttribute>(false).Order.CompareTo(y.GetCustomAttribute<GameOverlayAttribute>(false).Order);
                return c != 0 ? c : string.CompareOrdinal(x.FullName, y.FullName);
            });
            return result;
        }

        /// <summary>Whether an overlay of scope s belongs in a world built for flow state.</summary>
        public static bool InScope(OverlayScope s, FlowState state)
        {
            switch (s)
            {
                case OverlayScope.MatchOnly:
                    return state == FlowState.Match || state == FlowState.Off;
                case OverlayScope.MenusOnly:
                    return state == FlowState.Title || state == FlowState.Menu || state == FlowState.Setup;
                default:
                    return true;
            }
        }

        /// <summary>Adds every registered overlay in scope to game (once each); returns how many were added.</summary>
        public static int InstallOverlays(GameObject game)
        {
            if (game == null) return 0;
            int added = 0;
            List<Type> types = OverlayTypes();
            for (int i = 0; i < types.Count; i++)
            {
                Type t = types[i];
                GameOverlayAttribute a = t.GetCustomAttribute<GameOverlayAttribute>(false);
                if (!InScope(a.Scope, GameFlow.State) || game.GetComponent(t) != null) continue;
                try
                {
                    game.AddComponent(t);
                    added++;
                }
                catch (Exception e)
                {
                    // One broken overlay must never stop the game from building.
                    Debug.LogException(e);
                }
            }
            return added;
        }

        /// <summary>
        /// The main menu's lines: the defaults, each filled by a registered screen with the same id, plus registered screens
        /// with new ids, sorted by Order. source: the types to read (tests); null = the scanned assemblies.
        /// </summary>
        public static List<MenuEntry> MenuEntries(IEnumerable<Type> source = null)
        {
            var list = new List<MenuEntry>(Defaults());
            foreach (Type t in source ?? Types())
            {
                MenuEntryAttribute a = t.GetCustomAttribute<MenuEntryAttribute>(false);
                if (a == null || string.IsNullOrEmpty(a.Id) || !typeof(IMenuScreen).IsAssignableFrom(t)) continue;
                MenuEntry e = list.Find(x => x.Id == a.Id);
                if (e == null)
                {
                    e = new MenuEntry { Id = a.Id, Label = a.Label, Order = a.Order };
                    list.Add(e);
                }
                if (e.Builtin) continue;   // Play, Settings and Quit are the menu's own
                e.ScreenType = t;
                if (!string.IsNullOrEmpty(a.Label)) e.Label = a.Label;
            }
            list.Sort((x, y) => x.Order != y.Order ? x.Order.CompareTo(y.Order) : string.CompareOrdinal(x.Id, y.Id));
            return list;
        }
    }
}
