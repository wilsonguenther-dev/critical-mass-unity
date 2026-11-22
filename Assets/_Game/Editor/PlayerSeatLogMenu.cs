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
            bool on = PlayerPrefs.GetInt(PlayerSeatLogger.PrefKey, 0) != 1;
            PlayerPrefs.SetInt(PlayerSeatLogger.PrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
            if (Application.isPlaying)
            {
                if (on) PlayerSeatLogger.Enable();
                else PlayerSeatLogger.Disable();
            }
            Debug.Log("[CM.PlayerSeatLog] " + (on ? "ON: each match writes " + Application.persistentDataPath + "/PlayerSeatLogs/*.jsonl" : "OFF"));
        }

        [MenuItem(Item, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(Item, PlayerPrefs.GetInt(PlayerSeatLogger.PrefKey, 0) == 1);
            return true;
        }
    }
}
