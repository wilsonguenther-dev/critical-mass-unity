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

        /// <summary>Every key this class writes (tests back these up and restore them).</summary>
        public static readonly string[] AllKeys = { DoneKey };

        public static bool Done
        {
            get => PlayerPrefs.GetInt(DoneKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(DoneKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
