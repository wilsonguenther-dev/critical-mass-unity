using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>Who plays actor 0: Bot (a competent player), Passive (no input: AFK at the pad) or Wander (circles, never contributes).</summary>
    public enum HumanMode { Bot, Passive, Wander }

    /// <summary>What one headless match measured (browser outcomes.ts MatchOutcome).</summary>
    public sealed class MatchOutcome
    {
        public int Seed;
        public int TeamSize;
        public bool DriveHuman;
        public HumanMode Human;
        public MatchResult Winner;
        public PerTeam HillSeconds;
        /// <summary>Share of 1 Hz samples (t &gt; 30 s) with hillState azure or crimson.</summary>
        public double OwnedShare;
        public double ContestedShare;
        /// <summary>Owner changes on the owned samples (azure to crimson or back; contested / neutral samples in between are skipped).</summary>
        public int OwnerChanges;
        /// <summary>Kaiju state at the natural hatch (null if it never hatched).</summary>
        public KaijuState? Hatch;
        /// <summary>Mean bots per team in gather / deliver / corrupt over the 1 Hz samples in [20, 240] s.</summary>
        public double KaijuDuty;
        public PerTeam Allegiance;
        /// <summary>Browser delivered.{azure,crimson}.</summary>
        public PerTeam DeliveredBiomass;
        /// <summary>Browser delivered.{corruptionAzure,corruptionCrimson}.</summary>
        public PerTeam DeliveredCorruption;
        public PerTeam Kills;
        /// <summary>Team hill seconds when the egg hatched (null if it never hatched).</summary>
        public PerTeam HillAtHatch;
        /// <summary>Times the Kaiju was knocked out.</summary>
        public int KaijuKOs;
    }

    /// <summary>Headless outcome runner (browser src/bots/outcomes.ts): plays one full match and measures it; deterministic per seed.</summary>
    public static class OutcomeRunner
    {
        /// <summary>Samples for ownership / owner changes start after this many seconds (spawn walk excluded).</summary>
        public const double OutcomeSampleFrom = 30;
        /// <summary>Kaiju-duty window start (seconds of match time).</summary>
        public const double DutyFrom = 20;
        /// <summary>Kaiju-duty window end (seconds of match time).</summary>
        public const double DutyTo = 240;
        /// <summary>Wander: one full circle every WanderPeriod seconds.</summary>
        public const int WanderPeriod = 6;

        /// <summary>The scratch frame every bot-driven actor is filled into (browser module-level `frame`).</summary>
        private static readonly InputFrame Frame = SimInput.EmptyInput();
        /// <summary>The kept frame of the wandering actor 0 (browser `wanderFrame`).</summary>
        private static readonly InputFrame WanderFrame = SimInput.EmptyInput();

        /// <summary>One tick in the SimDriver order for the given actor-0 mode.</summary>
        private static void Tick(World w, HumanMode human)
        {
            if (human == HumanMode.Bot)
            {
                MatchTick.Advance(w, null, Frame);
            }
            else if (human == HumanMode.Wander)
            {
                InputFrame f = WanderFrame;
                double ang = 2 * Math.PI * w.Match.Time / WanderPeriod;
                f.MoveX = JsMath.Cos(ang);
                f.MoveZ = JsMath.Sin(ang);
                MatchTick.Advance(w, f, Frame);
            }
            else
            {
                MatchTick.AdvanceNoHuman(w, Frame);
            }
        }

        /// <summary>Alive actors of team in gather / deliver / corrupt (actor 0 skipped when it is not bot-driven).</summary>
        private static int DutyCount(World w, TeamId team, bool driveHuman)
        {
            int c = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == 0 && !driveHuman) continue;
                Actor a = w.Actors[i];
                if (!a.Alive || a.Team != team) continue;
                AiState s = a.AiState;
                if (s == AiState.Gather || s == AiState.Deliver || s == AiState.Corrupt) c++;
            }
            return c;
        }

        /// <summary>Play one full match (MatchSeconds) and measure it.</summary>
        public static MatchOutcome RunOutcomeMatch(int seed, int teamSize, HumanMode human)
        {
            bool driveHuman = human == HumanMode.Bot;
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg,
            });
            double hz = cfg.SimHz;
            int maxSteps = (int)Math.Ceiling(cfg.MatchSeconds * hz) + (int)hz;
            int samples = 0;
            int owned = 0;
            int contested = 0;
            HillState? lastOwner = null;
            int changes = 0;
            int dutySamples = 0;
            double dutySum = 0;
            KaijuState? hatch = null;
            PerTeam hillAtHatch = null;
            int kos = 0;
            bool wasKO = false;
            for (int s = 0; s < maxSteps && w.Match.Phase != MatchPhase.Ended; s++)
            {
                Tick(w, human);
                MatchState m = w.Match;
                if (hatch == null && m.Hatched)
                {
                    hatch = w.Kaiju.State;
                    hillAtHatch = new PerTeam { Azure = Round1(m.HillSeconds.Azure), Crimson = Round1(m.HillSeconds.Crimson) };
                }
                bool ko = w.Kaiju.KoIn > 0;
                if (ko && !wasKO) kos++;
                wasKO = ko;
                if (w.Tick % hz != 0) continue;
                double t = m.Time;
                if (t >= DutyFrom - 1e-6 && t <= DutyTo + 1e-6)
                {
                    dutySamples++;
                    dutySum += (DutyCount(w, TeamId.Azure, driveHuman) + DutyCount(w, TeamId.Crimson, driveHuman)) / 2.0;
                }
                if (t <= OutcomeSampleFrom + 1e-6) continue;
                samples++;
                HillState hs = m.HillState;
                if (hs == HillState.Azure || hs == HillState.Crimson)
                {
                    owned++;
                    if (lastOwner != null && hs != lastOwner.Value) changes++;
                    lastOwner = hs;
                }
                else if (hs == HillState.Contested)
                {
                    contested++;
                }
            }
            MatchState fm = w.Match;
            int killsA = 0;
            int killsC = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team == TeamId.Azure) killsA += w.Actors[i].Kills;
                else killsC += w.Actors[i].Kills;
            }
            DeliveredTally dA = fm.Delivered[(int)TeamId.Azure];
            DeliveredTally dC = fm.Delivered[(int)TeamId.Crimson];
            return new MatchOutcome
            {
                Seed = seed,
                TeamSize = teamSize,
                DriveHuman = driveHuman,
                Human = human,
                Winner = fm.Winner,
                HillSeconds = new PerTeam { Azure = Round1(fm.HillSeconds.Azure), Crimson = Round1(fm.HillSeconds.Crimson) },
                OwnedShare = samples > 0 ? (double)owned / samples : 0,
                ContestedShare = samples > 0 ? (double)contested / samples : 0,
                OwnerChanges = changes,
                Hatch = hatch,
                KaijuDuty = dutySamples > 0 ? dutySum / dutySamples : 0,
                Allegiance = new PerTeam { Azure = fm.Allegiance.Azure, Crimson = fm.Allegiance.Crimson },
                DeliveredBiomass = new PerTeam { Azure = dA.Biomass, Crimson = dC.Biomass },
                DeliveredCorruption = new PerTeam { Azure = dA.Corruption, Crimson = dC.Corruption },
                Kills = new PerTeam { Azure = killsA, Crimson = killsC },
                HillAtHatch = hillAtHatch,
                KaijuKOs = kos,
            };
        }

        /// <summary>Round to one decimal (browser round1).</summary>
        private static double Round1(double v)
        {
            return JsMath.Round(v * 10) / 10;
        }
    }
}
