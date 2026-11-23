using CriticalMass.Runtime.Game;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor
{
    /// <summary>
    /// px lane T: Editor switch for the player-seat telemetry logger (docs/testing/PLAYER-SEAT-TELEMETRY.md). Ticking it sets
    /// the PlayerPrefs key the logger reads when play mode starts (and starts or stops it at once while playing).
    /// </summary>
    public static class PlayerSeatLogMenu
    {
        private const string Item = "Critical Mass/Dev/Player-seat log";

        [MenuItem(Item)]
        private static void Toggle()
        {
            bool on = CriticalMass.Runtime.CmPrefs.GetInt(PlayerSeatLogger.PrefKey, 0) != 1;
            CriticalMass.Runtime.CmPrefs.SetInt(PlayerSeatLogger.PrefKey, on ? 1 : 0);
            CriticalMass.Runtime.CmPrefs.Save();
            if (Application.isPlaying)
            {
                if (on) PlayerSeatLogger.Enable();
                else PlayerSeatLogger.Disable();
            }
            Debug.Log("[CM.PlayerSeatLog] " + (on ? "ON: each match writes " + CriticalMass.Runtime.Game.GamePaths.PersistentRoot + "/PlayerSeatLogs/*.jsonl" : "OFF"));
        }

        [MenuItem(Item, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(Item, CriticalMass.Runtime.CmPrefs.GetInt(PlayerSeatLogger.PrefKey, 0) == 1);
            return true;
        }
    }
}
