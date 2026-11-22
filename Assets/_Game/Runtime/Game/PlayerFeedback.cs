using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>What a cue told the player (player-experience wave, 2026-11-22). Append only.</summary>
    public enum FeedbackChannel
    {
        HitFlash, HitStop, KnockVisual, ImpactBurst, DamageNumber, HitMarker, EnemyHealthBar, CameraKick, ImpactSound,
        SwingCommit, WhiffCue, BlockedTag, InterruptedTag, OwnHurtVignette, DamageDirection, OwnHurtSound, LowHealthCue,
        KillConfirm, KillSound, PersonalTally, HittableCue, AimAssistLock, WorldActionPop, RefusalTag, AllyHealthBar,
        HealNumber, AssistCredit, ObjectivePop,
    }

    /// <summary>One cue that became visible or audible to the local player.</summary>
    public struct FeedbackEntry
    {
        public FeedbackChannel Channel;
        /// <summary>The sim actor the cue is about (the victim of a flash, the target of a bar; -1 none).</summary>
        public int Actor;
        /// <summary>Channel value: damage shown, kick size in u, pose weight, bar fill, direction in degrees...</summary>
        public float Value;
        /// <summary>A PlayerReasons code where the channel has one (BlockedTag, RefusalTag, WorldActionPop), else 0.</summary>
        public int Reason;
        /// <summary>The component or object that shows the cue (tests check it is active and enabled); may be null for sound.</summary>
        public Object Source;
        public int Frame;
        public int SimTick;
        public float RealTime;
    }

    /// <summary>
    /// The player-feedback ledger: a fixed ring every presentation system writes to AT THE MOMENT its cue really becomes
    /// visible or audible to the local player. View-only: it never reads or writes the sim, allocates nothing after start,
    /// and costs one struct write per cue. The player-seat gate (Runtime.PlayTests/PlayerSeat) and the Editor review read it
    /// to prove "a press produced something the player could see". Report only what is truly shown: a cue hidden by a
    /// setting (reduce motion, reduce flashing at 0) must not be reported.
    /// </summary>
    public static class PlayerFeedback
    {
        public const int Capacity = 512;
        private static readonly FeedbackEntry[] Ring = new FeedbackEntry[Capacity];
        private static int _head;

        /// <summary>The sim tick stamped on entries; SimDriverBehaviour (or a test) sets it each step.</summary>
        public static int CurrentTick;

        /// <summary>Total entries ever written this match (a cursor: read [cursor, Head) with At).</summary>
        public static int Head => _head;

        public static void Report(FeedbackChannel channel, int actor, float value, Object source = null, int reason = 0)
        {
            ref FeedbackEntry e = ref Ring[_head % Capacity];
            e.Channel = channel;
            e.Actor = actor;
            e.Value = value;
            e.Reason = reason;
            e.Source = source;
            e.Frame = Time.frameCount;
            e.SimTick = CurrentTick;
            e.RealTime = Time.realtimeSinceStartup;
            _head++;
        }

        /// <summary>Entry number i (0-based since the last Clear); valid for Head - Capacity <= i < Head.</summary>
        public static FeedbackEntry At(int i) => Ring[((i % Capacity) + Capacity) % Capacity];

        /// <summary>First entry of channel at or after cursor (and for actor when actor >= 0); false when none.</summary>
        public static bool TryFind(FeedbackChannel channel, int cursor, int actor, out FeedbackEntry entry)
        {
            int from = cursor > _head - Capacity ? cursor : _head - Capacity;
            if (from < 0) from = 0;
            for (int i = from; i < _head; i++)
            {
                FeedbackEntry e = Ring[i % Capacity];
                if (e.Channel != channel || (actor >= 0 && e.Actor != actor)) continue;
                entry = e;
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>How many entries of channel since cursor (for actor when actor >= 0).</summary>
        public static int Count(FeedbackChannel channel, int cursor, int actor = -1)
        {
            int from = cursor > _head - Capacity ? cursor : _head - Capacity;
            if (from < 0) from = 0;
            int n = 0;
            for (int i = from; i < _head; i++)
            {
                FeedbackEntry e = Ring[i % Capacity];
                if (e.Channel == channel && (actor < 0 || e.Actor == actor)) n++;
            }
            return n;
        }

        /// <summary>Match load and domain reload: forget everything.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            for (int i = 0; i < Capacity; i++) Ring[i] = default;
            _head = 0;
            CurrentTick = 0;
        }
    }
}
