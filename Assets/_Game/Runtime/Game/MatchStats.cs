using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Runtime.Flow;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>One actor's match line.</summary>
    public sealed class ActorStats
    {
        public int Id;
        public TeamId Team;
        public ClassId Cls;
        public int Kills;
        public int Deaths;
        public double HillSeconds;
        /// <summary>Biomass units delivered to the egg.</summary>
        public int Biomass;
        /// <summary>Corruption units delivered to the egg.</summary>
        public int Corruption;
        public double Damage;
        /// <summary>Kaiju knock-outs credited to this actor.</summary>
        public int KaijuKOs;
        public int Points;
    }

    /// <summary>
    /// DG-01 match statistics, read from the sim's event ring once per tick (SimDriverBehaviour.Ticked) and from the
    /// actors' own counters (Kills, Deaths, HillSeconds) at the end. Read only: nothing here writes the world. DG-11
    /// (kill feed, scoreboard) extends this file.
    /// <para>MVP points: 100 per knock-out, 10 per hill second, 25 per biomass unit, 10 per corruption unit, 150 per Kaiju
    /// knock-out. Ties: fewer deaths, then the lower id.</para>
    /// </summary>
    public sealed class MatchStats
    {
        public const int PointsPerKill = 100, PointsPerHillSecond = 10, PointsPerBiomass = 25, PointsPerCorruption = 10,
            PointsPerKaijuKO = 150;

        public int Seed;
        public int TeamSize;
        public int StartTick;
        public TeamId StartTeam;
        public ClassId StartClass;
        public ActorStats[] Actors = Array.Empty<ActorStats>();
        public int ActorCount;

        /// <summary>Kaiju knock-outs per team (index TeamId).</summary>
        public readonly int[] KaijuKOsByTeam = new int[2];
        /// <summary>Players of each team the Kaiju knocked out.</summary>
        public readonly int[] KaijuKillsOfTeam = new int[2];
        public bool HatchSeen;
        public KaijuState HatchState = KaijuState.Egg;
        /// <summary>Allegiance and instability the moment the egg hatched.</summary>
        public double HatchAllegianceAzure, HatchAllegianceCrimson, HatchInstability;
        public bool Finished;
        /// <summary>Events missed because more than the ring's capacity arrived between two reads.</summary>
        public int EventsMissed;

        int _head;

        /// <summary>Starts the record for w (call at tick 0: actor 0's team and class are captured here).</summary>
        public static MatchStats Begin(World w)
        {
            var s = new MatchStats
            {
                Seed = w.Opts.Seed,
                TeamSize = w.Opts.TeamSize,
                StartTick = w.Tick,
                StartTeam = w.ActorCount > 0 ? w.Actors[0].Team : w.Opts.HumanTeam,
                StartClass = w.ActorCount > 0 ? w.Actors[0].Cls : w.Opts.HumanClass,
                ActorCount = w.ActorCount,
                Actors = new ActorStats[w.ActorCount],
                _head = w.EventHead,
            };
            for (int i = 0; i < w.ActorCount; i++)
                s.Actors[i] = new ActorStats { Id = i, Team = w.Actors[i].Team, Cls = w.Actors[i].Cls };
            return s;
        }

        static bool Valid(World w, int id) => id >= 0 && id < w.ActorCount;

        /// <summary>Reads every event pushed since the last call.</summary>
        public void Consume(World w)
        {
            int head = w.EventHead;
            if (head < _head) _head = 0;   // the world was reset in place
            if (head - _head > World.EventCapacity)
            {
                EventsMissed += head - _head - World.EventCapacity;
                _head = head - World.EventCapacity;
            }
            for (; _head < head; _head++)
            {
                SimEvent e = w.Events[_head % World.EventCapacity];
                switch (e.Type)
                {
                    case SimEventType.Hit:
                        if (Valid(w, e.B) && e.B < ActorCount) Actors[e.B].Damage += e.Value;
                        break;
                    case SimEventType.Death:
                        if (e.B == Damage.KaijuSource && Valid(w, e.A)) KaijuKillsOfTeam[(int)w.Actors[e.A].Team]++;
                        break;
                    case SimEventType.DeliverBiomass:
                        if (Valid(w, e.A) && e.A < ActorCount) Actors[e.A].Biomass += (int)Math.Round(e.Value);
                        break;
                    case SimEventType.DeliverCorruption:
                        if (Valid(w, e.A) && e.A < ActorCount) Actors[e.A].Corruption += (int)Math.Round(e.Value);
                        break;
                    case SimEventType.KaijuKO:
                        if (Valid(w, e.B))
                        {
                            KaijuKOsByTeam[(int)w.Actors[e.B].Team]++;
                            if (e.B < ActorCount) Actors[e.B].KaijuKOs++;
                        }
                        break;
                    case SimEventType.Hatch:
                        HatchSeen = true;
                        HatchState = w.Kaiju.State;
                        HatchAllegianceAzure = w.Match.Allegiance.Azure;
                        HatchAllegianceCrimson = w.Match.Allegiance.Crimson;
                        HatchInstability = w.Match.Instability;
                        break;
                }
            }
        }

        /// <summary>Takes the actors' own counters and scores everyone (idempotent).</summary>
        public void Finish(World w)
        {
            Consume(w);
            int n = Math.Min(ActorCount, w.ActorCount);
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                ActorStats s = Actors[i];
                s.Team = a.Team;
                s.Cls = a.Cls;
                s.Kills = a.Kills;
                s.Deaths = a.Deaths;
                s.HillSeconds = a.HillSeconds;
                s.Points = PointsFor(s);
            }
            Finished = w.Match.Phase == MatchPhase.Ended;
        }

        public static int PointsFor(ActorStats s) =>
            s.Kills * PointsPerKill + (int)Math.Round(s.HillSeconds * PointsPerHillSecond) + s.Biomass * PointsPerBiomass +
            s.Corruption * PointsPerCorruption + s.KaijuKOs * PointsPerKaijuKO;

        /// <summary>The team's most valuable player (-1 when the team has nobody).</summary>
        public int Mvp(TeamId team)
        {
            int best = -1;
            for (int i = 0; i < ActorCount; i++)
            {
                ActorStats s = Actors[i];
                if (s == null || s.Team != team) continue;
                if (best < 0) { best = i; continue; }
                ActorStats b = Actors[best];
                if (s.Points > b.Points || (s.Points == b.Points && s.Deaths < b.Deaths)) best = i;
            }
            return best;
        }

        // ---- the result in words ----

        static string Team(TeamId t) => t == TeamId.Azure ? "Azure" : "Crimson";
        static string Secs(double s) => Math.Round(s).ToString("0", CultureInfo.InvariantCulture);
        static string Num(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

        /// <summary>Why the match ended the way it did, for the results screen.</summary>
        public MatchSummary Summarize(World w)
        {
            MatchState m = w.Match;
            TeamId mine = StartTeam;
            var sum = new MatchSummary { Winner = m.Winner, PlayerTeam = mine };
            if (m.Winner == MatchResult.None)
                sum.Winner = Match.DecideWinner(m.HillSeconds.Azure, m.HillSeconds.Crimson, w.Config.DrawEpsilonSeconds);
            bool draw = sum.Winner == MatchResult.Draw;
            TeamId winTeam = sum.Winner == MatchResult.Crimson ? TeamId.Crimson : TeamId.Azure;
            TeamId loseTeam = World.OtherTeam(winTeam);
            sum.Outcome = draw ? "draw" : winTeam == mine ? "victory" : "defeat";
            sum.Title = draw ? "DRAW" : winTeam == mine ? "VICTORY" : "DEFEAT";

            var run = new Dictionary<string, string>
            {
                { "run:winner", Team(winTeam) },
                { "run:winnerHillSeconds", Secs(m.HillSeconds[winTeam]) },
                { "run:loserHillSeconds", Secs(m.HillSeconds[loseTeam]) },
                { "run:azureHillSeconds", Secs(m.HillSeconds.Azure) },
                { "run:crimsonHillSeconds", Secs(m.HillSeconds.Crimson) },
                { "cfg:drawEpsilon", w.Config.DrawEpsilonSeconds.ToString("0.#", CultureInfo.InvariantCulture) },
                { "cfg:berserkThreshold", Num(w.Config.KaijuFeed.BerserkThreshold) },
            };

            FlowWords.ResultWords rw = FlowWords.Result(sum.Outcome);
            sum.Line = rw != null && !string.IsNullOrEmpty(rw.line) && rw.line.IndexOf('{') < 0 ? rw.line
                : draw ? "Dead even. The hill will take a rematch."
                : winTeam == mine ? "The hill is yours." : "The hill is theirs, for now. Climb again.";
            string reason = rw != null ? FlowWords.Fill(rw.reason, run) : null;
            sum.Reason = (reason ?? (draw
                ? $"Hill time within {run["cfg:drawEpsilon"]} s: {run["run:azureHillSeconds"]} s to {run["run:crimsonHillSeconds"]} s."
                : $"{Team(winTeam)} held the hill {run["run:winnerHillSeconds"]} s to {run["run:loserHillSeconds"]} s.")).ToUpperInvariant();

            // Kaiju outcome: the state it hatched into (or its state now when the hatch was not seen) and why.
            KaijuState ks = HatchSeen ? HatchState : w.Kaiju.State;
            double az = HatchSeen ? HatchAllegianceAzure : m.Allegiance.Azure;
            double cr = HatchSeen ? HatchAllegianceCrimson : m.Allegiance.Crimson;
            double inst = HatchSeen ? HatchInstability : m.Instability;
            sum.Kaiju = ks;
            string outroId;
            switch (ks)
            {
                case KaijuState.Azure: outroId = "azure"; break;
                case KaijuState.Crimson: outroId = "crimson"; break;
                case KaijuState.Berserk: outroId = "berserk"; break;
                case KaijuState.Neutral: outroId = "neutral"; break;
                default: outroId = null; break;
            }
            run["run:topAllegiance"] = Num(Math.Max(az, cr));
            run["run:otherAllegiance"] = Num(Math.Min(az, cr));
            run["run:tiedAllegiance"] = Num(az);
            FlowWords.OutroWords ow = outroId != null ? FlowWords.Outro(outroId) : null;
            string caption = ow != null && !string.IsNullOrEmpty(ow.caption) && ow.caption.IndexOf('{') < 0 ? ow.caption : null;
            string why = ow != null ? FlowWords.Fill(ow.reason, run) : null;
            switch (ks)
            {
                case KaijuState.Azure:
                case KaijuState.Crimson:
                    TeamId fed = ks == KaijuState.Azure ? TeamId.Azure : TeamId.Crimson;
                    sum.KaijuCaption = caption ?? $"The Kaiju stood with {Team(fed)}.";
                    sum.KaijuReason = why ?? $"Fed most by {Team(fed)}: {run["run:topAllegiance"]} to {run["run:otherAllegiance"]} allegiance.";
                    break;
                case KaijuState.Berserk:
                    sum.KaijuCaption = caption ?? "Corruption won. The Kaiju turned on everyone.";
                    sum.KaijuReason = why ?? $"Instability reached {Num(inst)} of {run["cfg:berserkThreshold"]} before the hatch.";
                    break;
                case KaijuState.Neutral:
                    sum.KaijuCaption = caption ?? "The Kaiju chose no one.";
                    sum.KaijuReason = why ?? $"Allegiance tied at the hatch: {run["run:tiedAllegiance"]} each.";
                    break;
                default:
                    sum.KaijuCaption = "The egg never hatched.";
                    sum.KaijuReason = "The match ended before the hatch.";
                    break;
            }
            int kos = KaijuKOsByTeam[0] + KaijuKOsByTeam[1];
            sum.KaijuKOLine = kos == 0 ? "Never knocked out."
                : $"Knocked out {kos}x: Azure {KaijuKOsByTeam[0]}, Crimson {KaijuKOsByTeam[1]}.";
            return sum;
        }
    }

    /// <summary>The results screen's words for one finished match.</summary>
    public sealed class MatchSummary
    {
        public MatchResult Winner;
        public TeamId PlayerTeam;
        /// <summary>"victory", "defeat" or "draw".</summary>
        public string Outcome;
        public string Title;
        public string Line;
        /// <summary>Why: hill seconds of both sides.</summary>
        public string Reason;
        public KaijuState Kaiju;
        public string KaijuCaption;
        public string KaijuReason;
        public string KaijuKOLine;
    }

    /// <summary>
    /// Keeps MatchStats.Current for the driver on this GameObject: begins at the world's tick 0 and reads the events after
    /// every tick. View side only. Added by FlowDirector.Install on every Boot build.
    /// </summary>
    public sealed class MatchStatsRecorder : MonoBehaviour
    {
        /// <summary>The match being played (or just finished); null before the first world.</summary>
        public static MatchStats Current { get; private set; }

        SimDriverBehaviour _driver;
        Action<World> _onTick;

        void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _onTick = OnTick;
            if (_driver != null && _driver.World != null) Current = MatchStats.Begin(_driver.World);
        }

        void OnEnable()
        {
            if (_driver != null) _driver.Ticked += _onTick;
        }

        void OnDisable()
        {
            if (_driver != null) _driver.Ticked -= _onTick;
        }

        void OnTick(World w)
        {
            if (Current == null) Current = MatchStats.Begin(w);
            Current.Consume(w);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Current = null;
    }
}
