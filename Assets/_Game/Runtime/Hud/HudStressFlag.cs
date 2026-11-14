using System;
using CriticalMass.Runtime.Hud.Ui;
using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// hud-containment-1 capture flag: <c>-hudStress</c> pads every contained HUD and menu text by 30% (its own words
    /// repeated), the length a future language may need, so the integrator can see every box hold its longest text.
    /// Use it with the existing capture run, once at 16:9 and once at the smallest window:
    /// <code>-shots -hudStress -screen-width 1920 -screen-height 1080</code> and
    /// <code>-shots -hudStress -screen-width 640 -screen-height 360</code>.
    /// View only: it changes what labels show, never the sim. Off unless the flag is on the command line.
    /// </summary>
    public static class HudStressFlag
    {
        public const string Flag = "-hudstress";

        /// <summary>True when the flag is among the given arguments (case-insensitive; pure, for tests).</summary>
        public static bool In(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { return; }
            if (!In(args)) return;
            TextFit.Stress = true;
            Debug.Log("[CM.HudStress] every contained text padded by 30% for the containment capture");
        }
    }
}
