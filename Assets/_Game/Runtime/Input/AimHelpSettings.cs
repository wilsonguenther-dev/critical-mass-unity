using System;
using UnityEngine;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Player-experience wave, lane I (2026-11-22): AIM HELP OFF / LOW / HIGH per device class, stored in PlayerPrefs like
    /// GameSettings (one int per key). Defaults (master plan call 8): mouse LOW, controller HIGH, keys HIGH. The rows live on
    /// the ACCESS tab (SettingsExtras). View side only: the sim only ever receives the aim point.
    /// </summary>
    public static class AimHelpSettings
    {
        public const string MouseKey = "cm.aimHelp.mouse", ControllerKey = "cm.aimHelp.controller", KeysKey = "cm.aimHelp.keys";
        public static readonly string[] AllKeys = { MouseKey, ControllerKey, KeysKey };

        /// <summary>Raised after any AIM HELP row changes.</summary>
        public static event Action Changed;

        static string KeyOf(AimDevice d) => d == AimDevice.Mouse ? MouseKey : d == AimDevice.Controller ? ControllerKey : KeysKey;

        public static AimHelpLevel Get(AimDevice d)
        {
            int v = CmPrefs.GetInt(KeyOf(d), (int)AimAssistTunables.DefaultFor(d));
            return v >= (int)AimHelpLevel.Off && v <= (int)AimHelpLevel.High ? (AimHelpLevel)v : AimAssistTunables.DefaultFor(d);
        }

        public static void Set(AimDevice d, AimHelpLevel level)
        {
            CmPrefs.SetInt(KeyOf(d), (int)level);
            CmPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Settings stepper: Off, Low, High, wrapping.</summary>
        public static void Step(AimDevice d, int dir)
        {
            int v = ((int)Get(d) + (dir < 0 ? 2 : 1)) % 3;
            Set(d, (AimHelpLevel)v);
        }

        public static string Label(AimHelpLevel l) => l == AimHelpLevel.Off ? "OFF" : l == AimHelpLevel.Low ? "LOW" : "HIGH";

        public static void ResetAll()
        {
            for (int i = 0; i < AllKeys.Length; i++) CmPrefs.DeleteKey(AllKeys[i]);
            CmPrefs.Save();
            Changed?.Invoke();
        }
    }
}
