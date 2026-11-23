using UnityEngine;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// The guided match's saved state, kept between launches in PlayerPrefs. View side only. Key other code may read:
    /// cm.trial.done (int, 1 = the first-launch offer was answered or the trial was finished, so the next launch goes
    /// straight to the menu).
    /// </summary>
    public static class TrialProgress
    {
        public const string DoneKey = "cm.trial.done";
        /// <summary>fix/hud-containment-4: the FIRST TIME? card was shown (or a match was played): it never shows again.</summary>
        public const string OfferedKey = "cm.trial.offered";

        /// <summary>Every key this class writes (tests back these up and restore them).</summary>
        public static readonly string[] AllKeys = { DoneKey, OfferedKey };

        /// <summary>The first-time card has had its one showing (persisted).</summary>
        public static bool Offered
        {
            get => CmPrefs.GetInt(OfferedKey, 0) != 0;
            set
            {
                CmPrefs.SetInt(OfferedKey, value ? 1 : 0);
                CmPrefs.Save();
            }
        }

        public static bool Done
        {
            get => CmPrefs.GetInt(DoneKey, 0) != 0;
            set
            {
                CmPrefs.SetInt(DoneKey, value ? 1 : 0);
                CmPrefs.Save();
            }
        }
    }
}
