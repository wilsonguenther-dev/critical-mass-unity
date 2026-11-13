using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// Reads the local player's match stats from the sim (read-only, allocation-free per frame) and, once, when the match
    /// ends, computes the rewards (RewardRules), adds them to the profile and saves it. Per-actor kills, deaths and hill
    /// seconds come from Actor; deliveries and Kaiju knock-outs from the event ring (DeliverBiomass / DeliverCorruption A =
    /// actor, Value = units; KaijuKO B = source). Nothing is written back to the sim.
    /// <para>Public API for other items: Current (live stats), LiveCoins (HUD chip), Last and RewardsReady (results
    /// screen), FoundRelic (DG-25 relics). The game-flow item (DG-01) can replace the event reading with its MatchStats by
    /// feeding Finish(stats) itself and setting AutoFinish false.</para>
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class MatchRewardTracker : MonoBehaviour
    {
        /// <summary>The local (human) actor id; the sim puts the human at 0.</summary>
        public const int LocalActor = 0;

        /// <summary>Raised once per finished match after the profile is updated and saved.</summary>
        public static event Action<MatchRewards> RewardsReady;

        /// <summary>The rewards of the last finished match (null before the first).</summary>
        public static MatchRewards Last { get; private set; }

        /// <summary>The tracker of the running game (null when no game exists).</summary>
        public static MatchRewardTracker Instance { get; private set; }

        /// <summary>False: the tracker never finishes by itself (DG-01 calls Finish with its own stats).</summary>
        public static bool AutoFinish = true;

        public SimDriverBehaviour Driver;

        int _epoch = int.MinValue;
        int _cursor;
        int _bio, _cor, _kaiju, _relics;
        bool _finished;
        /// <summary>Events were overwritten before they were read (ring overflow at high speed): counts may be low.</summary>
        public bool MissedEvents { get; private set; }

        /// <summary>The local player's stats so far (Completed and Outcome set only after the end).</summary>
        public MatchStatsInput Current { get; private set; }

        /// <summary>Coins earned so far this match (the HUD chip).</summary>
        public int LiveCoins => RewardRules.LiveCoins(Current);

        public bool Finished => _finished;

        void Awake()
        {
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || w.ActorCount <= LocalActor) return;
            if (w.Epoch != _epoch) BeginMatch(w);
            ReadEvents(w);
            Actor me = w.Actors[LocalActor];
            var s = new MatchStatsInput
            {
                KnockOuts = me.Kills,
                Deaths = me.Deaths,
                HillSeconds = me.HillSeconds,
                BiomassDelivered = _bio,
                CorruptionDelivered = _cor,
                KaijuKnockOuts = _kaiju,
                RelicsFound = _relics,
                // ITEM DG-39 part 3: CoinsPicked = me.CoinsPicked once the rules lane adds world pickups.
            };
            if (w.Match.Phase == MatchPhase.Ended)
            {
                s.Completed = true;
                s.Outcome = OutcomeFor(w.Match.Winner, me.Team);
            }
            Current = s;
            if (s.Completed && !_finished && AutoFinish && !SimDriverBehaviour.CaptureMode) Finish(s);
        }

        void BeginMatch(World w)
        {
            _epoch = w.Epoch;
            _cursor = Math.Max(0, w.EventHead - World.EventCapacity);
            _bio = _cor = _kaiju = _relics = 0;
            _finished = false;
            MissedEvents = false;
            Current = default;
        }

        void ReadEvents(World w)
        {
            if (_cursor > w.EventHead) _cursor = w.EventHead;
            if (w.EventHead - _cursor > World.EventCapacity)
            {
                MissedEvents = true;
                _cursor = w.EventHead - World.EventCapacity;
            }
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e.Type == SimEventType.DeliverBiomass && e.A == LocalActor) _bio += Units(e.Value);
                else if (e.Type == SimEventType.DeliverCorruption && e.A == LocalActor) _cor += Units(e.Value);
                else if (e.Type == SimEventType.KaijuKO && e.B == LocalActor) _kaiju++;
            }
        }

        static int Units(double v) => double.IsFinite(v) && v > 0 ? (int)Math.Round(v) : 0;

        public static MatchOutcome OutcomeFor(MatchResult winner, TeamId team)
        {
            if (winner == MatchResult.Draw || winner == MatchResult.None) return MatchOutcome.Draw;
            bool azureWon = winner == MatchResult.Azure;
            return azureWon == (team == TeamId.Azure) ? MatchOutcome.Win : MatchOutcome.Loss;
        }

        /// <summary>DG-25: a relic was picked up this match; counts toward rewards only the first time ever.</summary>
        public static void FoundRelic(string relicId)
        {
            if (Instance == null || Instance._finished) return;
            if (Profile.Current.AddRelic(relicId)) Instance._relics++;
        }

        /// <summary>Pays a finished match once: computes, adds to the profile, saves, raises RewardsReady.</summary>
        public MatchRewards Finish(MatchStatsInput stats)
        {
            if (_finished) return Last;
            _finished = true;
            MatchRewards r = RewardRules.Compute(stats);
            RewardRules.Apply(r, Profile.Current);
            Profile.Commit();
            Last = r;
            RewardsReady?.Invoke(r);
            return r;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            RewardsReady = null;
            Last = null;
            Instance = null;
            AutoFinish = true;
        }
    }
}
