using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Game
{
    /// <summary>What a mark on the lead graph stands for.</summary>
    public enum TimelineMarkKind { Hatch, HutLevel, LeadChange }

    /// <summary>One mark on the lead graph: a moment of the match.</summary>
    public struct TimelineMark
    {
        public TimelineMarkKind Kind;
        /// <summary>Match seconds.</summary>
        public double Time;
        /// <summary>HutLevel: the team whose hut levelled; LeadChange: the new leader; Hatch: the side it chose (Azure / Crimson, else Azure with Code).</summary>
        public TeamId Team;
        /// <summary>HutLevel: the new level; Hatch: (int)KaijuState; LeadChange: 0.</summary>
        public int Code;
        /// <summary>Both teams' hill seconds at the mark (the graph places it on the leader's line).</summary>
        public double Azure, Crimson;
    }

    /// <summary>
    /// b15 item 1 (systems audit W1): the match story, recorded on the VIEW side from what the sim already exposes. Once per
    /// whole second it samples both teams' hill seconds (MatchState.HillSeconds, read only) for the lead graph; on every
    /// tick it watches the sign of the hill-time difference and marks each lead change (the audit's definition: the sign of
    /// the cumulative difference flips; ties do not end a lead); Hatch and HutLevel sim events become marks. Nothing here
    /// writes the world, so no checksum can move. Fixed arrays sized from the match length at Begin: no per-tick allocation.
    /// </summary>
    public sealed class MatchTimeline
    {
        public const double SampleSeconds = 1.0;
        public const int MarkCapacity = 64;
        /// <summary>A comeback sentence needs the winner to have trailed by at least this many hill seconds.</summary>
        public const double ComebackSeconds = 15.0;
        /// <summary>The final lead came this close to the end: the "clock almost out" sentence.</summary>
        public const double LateSeconds = 30.0;

        public float[] T = Array.Empty<float>(), Azure = Array.Empty<float>(), Crimson = Array.Empty<float>();
        public int Count;
        public readonly TimelineMark[] Marks = new TimelineMark[MarkCapacity];
        public int MarkCount;
        /// <summary>Marks dropped because the ring was full (the graph shows the first MarkCapacity).</summary>
        public int MarksDropped;
        public int LeadChanges;
        /// <summary>The leader now: +1 Azure, -1 Crimson, 0 nobody has led yet.</summary>
        public int Leader;
        /// <summary>Largest deficit each team had, hill seconds (index TeamId).</summary>
        public readonly double[] WorstDeficit = new double[2];
        public double MatchSeconds;
        double _nextSample;

        /// <summary>A timeline for a match of matchSeconds (the arrays hold one sample per second plus slack).</summary>
        public static MatchTimeline Begin(double matchSeconds)
        {
            double len = double.IsFinite(matchSeconds) && matchSeconds > 0 ? matchSeconds : 480.0;
            int cap = (int)Math.Ceiling(len / SampleSeconds) + 8;
            return new MatchTimeline { T = new float[cap], Azure = new float[cap], Crimson = new float[cap], MatchSeconds = len };
        }

        /// <summary>Every tick: lead changes, deficits, and a sample once per SampleSeconds (and always the first).</summary>
        public void Observe(double t, double az, double cr)
        {
            double diff = az - cr;
            int sign = diff > 0 ? 1 : diff < 0 ? -1 : 0;
            if (sign != 0 && sign != Leader)
            {
                Leader = sign;
                LeadChanges++;
                AddMark(new TimelineMark
                {
                    Kind = TimelineMarkKind.LeadChange, Time = t, Team = sign > 0 ? TeamId.Azure : TeamId.Crimson, Azure = az, Crimson = cr,
                });
            }
            if (-diff > WorstDeficit[0]) WorstDeficit[0] = -diff;
            if (diff > WorstDeficit[1]) WorstDeficit[1] = diff;
            if (Count == 0 || t >= _nextSample) Sample(t, az, cr);
        }

        /// <summary>The last sample at the end of the match (idempotent for the same time).</summary>
        public void Close(double t, double az, double cr)
        {
            if (Count > 0 && T[Count - 1] >= (float)t) { Azure[Count - 1] = (float)az; Crimson[Count - 1] = (float)cr; return; }
            Sample(t, az, cr);
        }

        void Sample(double t, double az, double cr)
        {
            if (Count >= T.Length)
            {
                // a longer match than planned (a custom clock): keep the newest at the end instead of growing
                T[T.Length - 1] = (float)t;
                Azure[T.Length - 1] = (float)az;
                Crimson[T.Length - 1] = (float)cr;
                return;
            }
            T[Count] = (float)t;
            Azure[Count] = (float)az;
            Crimson[Count] = (float)cr;
            Count++;
            _nextSample = Math.Floor(t / SampleSeconds) * SampleSeconds + SampleSeconds;
        }

        public void AddMark(TimelineMark m)
        {
            if (MarkCount >= MarkCapacity) { MarksDropped++; return; }
            Marks[MarkCount++] = m;
        }

        public double MaxSeconds()
        {
            double m = 0;
            for (int i = 0; i < Count; i++) m = Math.Max(m, Math.Max(Azure[i], Crimson[i]));
            return m;
        }

        /// <summary>The time the winner took the lead for the last time (NaN when it never did), and whether it ever lost one.</summary>
        public double FinalLeadOf(TeamId winner, out bool lostOnce)
        {
            lostOnce = false;
            double at = double.NaN;
            bool winnerLed = false;
            for (int i = 0; i < MarkCount; i++)
            {
                if (Marks[i].Kind != TimelineMarkKind.LeadChange) continue;
                if (Marks[i].Team == winner) { if (winnerLed) lostOnce = true; at = Marks[i].Time; winnerLed = true; }
                else if (winnerLed) lostOnce = true;
            }
            return at;
        }

        /// <summary>
        /// The one plain sentence naming the turning point, for the results screen: the moment the winner took the lead for
        /// good, said as a comeback when the winner had trailed by ComebackSeconds or more.
        /// </summary>
        public string TurningPoint(MatchResult result, double azureTotal, double crimsonTotal)
        {
            if (azureTotal <= 0 && crimsonTotal <= 0) return MatchWords.TurnNobody;
            if (result == MatchResult.Draw || result == MatchResult.None) return MatchWords.TurnDraw;
            TeamId win = result == MatchResult.Crimson ? TeamId.Crimson : TeamId.Azure;
            string team = MatchWords.Team(win);
            double at = FinalLeadOf(win, out bool lost);
            if (double.IsNaN(at)) return MatchWords.Fill(MatchWords.TurnNoMark, "team", team);
            string time = MatchWords.Clock(at);
            double deficit = WorstDeficit[(int)win];
            if (deficit >= ComebackSeconds)
                return MatchWords.Fill(MatchWords.TurnComeback, "team", team, "deficit", MatchWords.N(deficit), "time", time);
            if (MatchSeconds - at <= LateSeconds)
                return MatchWords.Fill(MatchWords.TurnLate, "team", team, "time", time);
            return MatchWords.Fill(lost ? MatchWords.TurnRetook : MatchWords.TurnLedAndHeld, "team", team, "time", time);
        }
    }
}
