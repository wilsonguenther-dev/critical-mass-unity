using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.Secrets;
using UnityEngine;

namespace CriticalMass.Runtime.Flow
{
    /// <summary>
    /// Integration batch 2: the small hooks the batch's items asked each other for, in one place, so no item edits
    /// another's file. The hill-alone easter egg speaks through the announcer (line an-22), and a relic found in a
    /// match is counted by the match rewards.
    /// </summary>
    public static class BatchHooks
    {
        // BeforeSceneLoad: after every item's SubsystemRegistration reset (domain reload is off, statics survive).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SecretsBoard.AnnouncerLine -= OnSecretLine;
            SecretsBoard.AnnouncerLine += OnSecretLine;
            RelicField.PickedUp -= OnRelic;
            RelicField.PickedUp += OnRelic;
        }

        static void OnSecretLine(string lineId)
        {
            // Batch 4 finisher: handed to the announcer only when it is free (SecretWatcher captions it itself otherwise),
            // so the line is never lost by ageing out behind another one.
            if (lineId == "an-22" && Announcer.Free) Announcer.Say(AnnouncerTrigger.SecretHillAlone);
        }

        static void OnRelic(int index)
        {
            string id = CodexProgress.IdOf(index);
            if (!string.IsNullOrEmpty(id)) MatchRewardTracker.FoundRelic(id);
        }
    }
}
