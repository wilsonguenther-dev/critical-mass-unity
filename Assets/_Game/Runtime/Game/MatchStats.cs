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

        // ---- b15 item 2: role counters, view side, from sim events (MatchStats.Consume); nothing in the sim holds them ----
        /// <summary>HP this actor's heals restored (HealLanded, B = the Healer; herself included).</summary>
        public double HealingDone;
        /// <summary>Heals that landed on another ally below SaveHpFraction of max HP.</summary>
        public int Saves;
        /// <summary>Walls, blocks, ladders and bridges this actor put down (WallBuilt that succeeded, PiecePlaced).</summary>
        public int StructuresBuilt;
        /// <summary>Wall cells in the walls built (WallBuilt Value).</summary>
        public int WallCells;
        /// <summary>Ore this actor banked at the hut (BankDeposit, Bio = Ore).</summary>
        public int OreBanked;
        /// <summary>Hut levels this actor's bank completed (HutLevel, B = the banker).</summary>
        public int HutLevelsFunded;
        /// <summary>Loads handed to an allied feeder (Handoff, A = the giver).</summary>
        public int Handoffs;
        /// <summary>Enemy walls and pieces broken inside this actor's blast in the same tick (Explosion with WallBroken / PieceDestroyed).</summary>
        public int StructuresBlown;
        /// <summary>Blasts that hurt two or more fighters (Explosion Value &gt;= 2).</summary>
        public int MultiHits;
        /// <summary>The job points inside Points (RoleAwards.RolePoints).</summary>
        public int RolePoints;
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
        /// <summary>b15 item 1: the lead graph's samples and marks (view side).</summary>
        public MatchTimeline Timeline = MatchTimeline.Begin(480);
        /// <summary>A heal on an ally below this share of max HP (before the heal) counts as a save.</summary>
        public const double SaveHpFraction = 0.3;
        /// <summary>A structure this far outside a blast's radius (u) still counts for it (a wall event names its first cell).</summary>
        public const double BlastSlack = 2.0;
        const int BlastCap = 16;
        readonly int[] _blastOwner = new int[BlastCap];
        readonly double[] _blastX = new double[BlastCap], _blastZ = new double[BlastCap], _blastR = new double[BlastCap];
        int _blastCount;
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
                StartTeam = w.ActorCount > 0 ? w.Actors[LocalSeat.Actor].Team : w.Opts.HumanTeam,
                StartClass = w.ActorCount > 0 ? w.Actors[LocalSeat.Actor].Cls : w.Opts.HumanClass,
                ActorCount = w.ActorCount,
                Actors = new ActorStats[w.ActorCount],
                _head = w.EventHead,
                Timeline = MatchTimeline.Begin(w.Config != null ? w.Config.MatchSeconds : 480),
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
            // b15: this batch's blasts first, so a wall that broke in the same tick credits the blast whichever came first
            _blastCount = 0;
            for (int k = _head; k < head; k++)
            {
                SimEvent e = w.Events[k % World.EventCapacity];
                if (e.Type != SimEventType.Explosion || _blastCount >= BlastCap) continue;
                _blastOwner[_blastCount] = e.A;
                _blastX[_blastCount] = e.X;
                _blastZ[_blastCount] = e.Z;
                _blastR[_blastCount] = e.Radius;
                _blastCount++;
            }
            for (; _head < head; _head++)
            {
                SimEvent e = w.Events[_head % World.EventCapacity];
                switch (e.Type)
                {
                    case SimEventType.HealLanded:
                        if (Valid(w, e.B) && e.B < ActorCount && e.Value > 0)
                        {
                            Actors[e.B].HealingDone += e.Value;
                            if (e.A != e.B && Valid(w, e.A))
                            {
                                Actor t = w.Actors[e.A];
                                if (t.MaxHp > 0 && t.Hp - e.Value < t.MaxHp * SaveHpFraction) Actors[e.B].Saves++;
                            }
                        }
                        break;
                    case SimEventType.WallBuilt:
                        if (e.B >= 0 && e.Value > 0 && Valid(w, e.A) && e.A < ActorCount)
                        {
                            Actors[e.A].StructuresBuilt++;
                            Actors[e.A].WallCells += (int)Math.Round(e.Value);
                        }
                        break;
                    case SimEventType.PiecePlaced:
                        if (Valid(w, e.A) && e.A < ActorCount) Actors[e.A].StructuresBuilt++;
                        break;
                    case SimEventType.BankDeposit:
                        if (e.Bio == BiomassType.Ore && Valid(w, e.A) && e.A < ActorCount) Actors[e.A].OreBanked += (int)Math.Round(e.Value);
                        break;
                    case SimEventType.HutLevel:
                        if (Valid(w, e.B) && e.B < ActorCount) Actors[e.B].HutLevelsFunded++;
                        if (e.A == 0 || e.A == 1)
                            Timeline.AddMark(new TimelineMark
                            {
                                Kind = TimelineMarkKind.HutLevel, Time = w.Match.Time, Team = (TeamId)e.A, Code = (int)Math.Round(e.Value),
                                Azure = w.Match.HillSeconds.Azure, Crimson = w.Match.HillSeconds.Crimson,
                            });
                        break;
                    case SimEventType.Handoff:
                        if (Valid(w, e.A) && e.A < ActorCount) Actors[e.A].Handoffs++;
                        break;
                    case SimEventType.Explosion:
                        if (e.Value >= 2 && Valid(w, e.A) && e.A < ActorCount) Actors[e.A].MultiHits++;
                        break;
                    case SimEventType.WallBroken:
                    case SimEventType.PieceDestroyed:
                        CreditBlast(w, e);
                        break;
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
                        Timeline.AddMark(new TimelineMark
                        {
                            Kind = TimelineMarkKind.Hatch, Time = w.Match.Time, Team = w.Kaiju.State == KaijuState.Crimson ? TeamId.Crimson : TeamId.Azure,
                            Code = (int)w.Kaiju.State, Azure = w.Match.HillSeconds.Azure, Crimson = w.Match.HillSeconds.Crimson,
                        });
                        HatchAllegianceAzure = w.Match.Allegiance.Azure;
                        HatchAllegianceCrimson = w.Match.Allegiance.Crimson;
                        HatchInstability = w.Match.Instability;
                        break;
                }
            }
            if (w.Match != null) Timeline.Observe(w.Match.Time, w.Match.HillSeconds.Azure, w.Match.HillSeconds.Crimson);
        }

        /// <summary>b15: an enemy wall or piece that broke inside a blast of this batch credits the blast's owner.</summary>
        void CreditBlast(World w, SimEvent e)
        {
            int owner = e.A;
            for (int b = 0; b < _blastCount; b++)
            {
                int by = _blastOwner[b];
                if (!Valid(w, by) || by >= ActorCount) continue;
                if (Valid(w, owner) && w.Actors[owner].Team == w.Actors[by].Team) continue;
                double dx = e.X - _blastX[b], dz = e.Z - _blastZ[b], r = _blastR[b] + BlastSlack;
                if (dx * dx + dz * dz > r * r) continue;
                Actors[by].StructuresBlown++;
                return;
            }
        }

        /// <summary>Takes the actors' own counters and scores everyone (idempotent).</summary>
        public void Finish(World w)
        {
            Consume(w);
            CombatTally tally = CombatLog.Current;
            if (tally != null && tally.World != w) tally = null;
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
                if (tally != null)
                {
                    // DG-11's CombatLog follows the same world from its own event pump: its knock-outs and deliveries
                    // are the numbers the kill feed and scoreboard showed, so the results agree with them.
                    ActorTally t = tally.Tally(i);
                    s.Kills = t.Knockouts;
                    s.Biomass = t.BiomassDelivered;
                    s.Corruption = t.CorruptionDelivered;
                }
                s.Points = PointsFor(s);
            }
            Finished = w.Match.Phase == MatchPhase.Ended;
            Timeline.Close(w.Match.Time, w.Match.HillSeconds.Azure, w.Match.HillSeconds.Crimson);
        }

        /// <summary>
        /// MVP points: the fight (knock-outs, Kaiju knock-outs), the hill, the egg, and since b15 the job points of every role
        /// (RoleAwards.RolePoints: healing and saves, structures built, ore banked and hut levels, structures blown and
        /// multi-hits), so a Healer or a Builder who did the job can be the MVP without a knock-out.
        /// </summary>
        public static int PointsFor(ActorStats s)
        {
            s.RolePoints = RoleAwards.RolePoints(s);
            return s.Kills * PointsPerKill + (int)Math.Round(s.HillSeconds * PointsPerHillSecond) + s.Biomass * PointsPerBiomass +
                   s.Corruption * PointsPerCorruption + s.KaijuKOs * PointsPerKaijuKO + s.RolePoints;
        }

        /// <summary>b15 item 2: the six awards of this match.</summary>
        public AwardResult[] Awards() => RoleAwards.Pick(Actors, ActorCount);

        /// <summary>b15 item 2: actor id's best award stat.</summary>
        public BestStat BestOf(int id) => RoleAwards.BestOf(Actors, ActorCount, id);

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
            // b15 item 1: the turning point in one plain sentence
            sum.TurningPoint = Timeline.TurningPoint(sum.Winner, m.HillSeconds.Azure, m.HillSeconds.Crimson);
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
        /// <summary>b15 item 1: "Crimson took the lead at 3:12 and never lost it."</summary>
        public string TurningPoint;
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
