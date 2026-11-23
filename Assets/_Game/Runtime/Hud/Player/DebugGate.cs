using System;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 29 (session audit P13): the debug boxes stay out of the player's way. F1 (DebugHud) and F3 (PerfOverlay)
    /// toggle only in the Editor, in a development build, or with the -debugHud launch argument. Tools that set the boxes'
    /// Visible flag directly (captures, automation) are not affected.
    /// </summary>
    public static class DebugGate
    {
        public const string Arg = "-debugHud";
        static int _allowed = -1;

        public static bool Allowed
        {
            get
            {
                if (_allowed < 0) _allowed = Application.isEditor || Debug.isDebugBuild || HasArg(Environment.GetCommandLineArgs()) ? 1 : 0;
                return _allowed == 1;
            }
        }

        /// <summary>True when args carry -debugHud (any case; pure; tests).</summary>
        public static bool HasArg(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++) if (string.Equals(args[i], Arg, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _allowed = -1;
    }
}
