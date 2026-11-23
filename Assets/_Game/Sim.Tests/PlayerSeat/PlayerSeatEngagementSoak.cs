using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// S49 (Slow + PlayerSeatSoak, PLAN THE STANDARD row 3 "never bored"). Full 16v16 and 5v5 matches, T.EngagementSeeds seeds
    /// each, seat 0 a Warrior or a Farmer. STAND-IN: seat 0 is driven by the game's own bot brain (MatchTick.Advance with no
    /// human frame), as a proxy for a player who always heads for the action; a real human who wanders scores worse.
    /// Each tick seat 0 is MEANINGFUL when alive AND (an enemy within T.EngageEnemyRadius, OR inside the hill disc, OR busy with a
    /// verb: carrying, delivering, mining channel, at a plant node, or a build/dig/mine/bank event of its own in the last second,
    /// OR within T.EngageEggRadius of the egg before the hatch / of the Kaiju after). Every other tick (dead, walking back,
    /// roaming) is "nothing to do". Measured: the longest such stretch, the share of the match dead or walking back from a
    /// respawn, and respawn-to-first-hit (given or taken). Per-seed rows go to $CM_PX_DATA_DIR/s49-*.csv.
    /// </summary>
    [TestFixture, Category("Slow"), Category("PlayerSeatSoak")]
    public sealed class PlayerSeatEngagementSoak
    {
        sealed class Run
        {
            public double Longest, DeadOrWalkShare, RespawnToHitMedian = double.NaN;
            public int Respawns;
        }

        static int Seeds()
        {
            string s = Environment.GetEnvironmentVariable("CM_PX_SOAK_SEEDS");
            return int.TryParse(s, out int n) && n > 0 ? n : T.EngagementSeeds;
        }

        static Run Play(int teamSize, ClassId cls, int seed)
        {
            GameConfig cfg = teamSize >= 16 ? GameConfig.CreateDefault().ApplyRules(RulesProfile.Full) : GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            cfg.TeamSize = teamSize;
            World w = World.CreateWorld(new WorldOptions { Seed = 7000 + seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg, BotDifficulty = BotDifficulty.Normal, AllyBotDifficulty = BotDifficulty.Normal });
            var scratch = new InputFrame();
            double hz = w.Config.SimHz;
            int cap = (int)Math.Ceiling(w.Config.MatchSeconds * hz * 1.5);
            int head = w.EventHead, lastVerb = -100000, run = 0, longest = 0, deadOrWalk = 0, ticks = 0;
            bool wasAlive = w.Actors[0].Alive, walking = false;
            int respawnAt = -1;
            var toHit = new List<double>();
            int respawns = 0;
            for (int t = 0; t < cap && w.Match.Winner == MatchResult.None; t++)
            {
                MatchTick.Advance(w, null, scratch);
                ticks++;
                bool hit0 = false;
                for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
                {
                    SimEvent e = w.Events[i % World.EventCapacity];
                    if (e.Type == SimEventType.Hit && (e.A == 0 || e.B == 0)) hit0 = true;
                    if (e.A == 0 && (e.Type == SimEventType.WallBuilt || e.Type == SimEventType.PiecePlaced || e.Type == SimEventType.Terraform
                        || e.Type == SimEventType.BankDeposit || e.Type == SimEventType.DeliverBiomass || (e.Type == SimEventType.OreMined && e.Value > 0))) lastVerb = w.Tick;
                }
                head = w.EventHead;
                Actor a = w.Actors[0];
                bool alive = a.Alive;
                if (alive && !wasAlive) { respawnAt = w.Tick; walking = true; respawns++; }
                wasAlive = alive;
                if (respawnAt >= 0 && hit0 && alive) { toHit.Add((w.Tick - respawnAt) / hz); respawnAt = -1; }
                bool meaningful = false;
                if (alive)
                {
                    double x = w.X[0], z = w.Z[0], r2 = T.EngageEnemyRadius * T.EngageEnemyRadius;
                    for (int j = 0; j < w.ActorCount && !meaningful; j++)
                    {
                        Actor o = w.Actors[j];
                        if (!o.Alive || o.Team == a.Team) continue;
                        double dx = w.X[j] - x, dz = w.Z[j] - z;
                        meaningful = dx * dx + dz * dz <= r2;
                    }
                    double hx = x - w.Map.Hill.X, hzz = z - w.Map.Hill.Z;
                    meaningful |= hx * hx + hzz * hzz <= w.Map.Hill.R * w.Map.Hill.R;
                    meaningful |= a.Carry != CarryKind.None || a.DeliverTicks > 0 || w.Tick - lastVerb <= hz;
                    if (w.Hut != null && w.Hut.ChannelVein != null) meaningful |= w.Hut.ChannelVein[0] >= 0;
                    foreach (ResourceNode nd in w.Nodes) { double nx = nd.X - x, nz = nd.Z - z; if (nx * nx + nz * nz <= 1.5 * 1.5) { meaningful = true; break; } }
                    double ex, ez, er = T.EngageEggRadius;
                    if (w.Match.Hatched && w.Kaiju != null) { ex = w.Kaiju.X; ez = w.Kaiju.Z; er += w.Config.Kaiju.Radius; }
                    else { ex = w.Map.Shrine.X; ez = w.Map.Shrine.Z; }
                    meaningful |= (ex - x) * (ex - x) + (ez - z) * (ez - z) <= er * er;
                }
                if (meaningful) walking = false;
                if (!alive || walking) deadOrWalk++;
                run = meaningful ? 0 : run + 1;
                longest = Math.Max(longest, run);
            }
            toHit.Sort();
            return new Run
            {
                Longest = longest / hz,
                DeadOrWalkShare = ticks > 0 ? (double)deadOrWalk / ticks : 0,
                RespawnToHitMedian = toHit.Count > 0 ? toHit[toHit.Count / 2] : double.NaN,
                Respawns = respawns,
            };
        }

        static double Median(List<double> v)
        {
            var s = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            return s.Count == 0 ? double.NaN : s[s.Count / 2];
        }

        static void Size(int teamSize)
        {
            int n = Seeds();
            var longest = new List<double>();
            var dead = new List<double>();
            var toHit = new List<double>();
            var rows = new List<string>();
            foreach (ClassId cls in new[] { ClassId.Warrior, ClassId.Farmer })
            {
                var cl = new List<double>();
                var cd = new List<double>();
                var ch = new List<double>();
                for (int s = 0; s < n; s++)
                {
                    Run r = Play(teamSize, cls, s);
                    cl.Add(r.Longest); cd.Add(r.DeadOrWalkShare); ch.Add(r.RespawnToHitMedian);
                    PxLog.Csv("s49-" + teamSize + "v" + teamSize + ".csv", "class,seed,longest_idle_s,dead_or_walking_share,respawn_to_hit_median_s,respawns",
                        cls + "," + s + "," + PxLog.F(r.Longest, "0.0") + "," + PxLog.F(r.DeadOrWalkShare) + "," + PxLog.F(r.RespawnToHitMedian, "0.0") + "," + r.Respawns);
                }
                rows.Add(cls + ": longest median " + PxLog.F(Median(cl), "0.0") + " s / worst " + PxLog.F(cl.Max(), "0.0") + " s, dead or walking " + PxLog.F(cd.Average()) + ", respawn to hit " + PxLog.F(Median(ch), "0.0") + " s");
                longest.AddRange(cl); dead.AddRange(cd); toHit.AddRange(ch);
            }
            double ml = Median(longest), worst = longest.Max(), share = dead.Average(), mh = Median(toHit);
            bool ok = PxLog.Line("S49 Engagement " + teamSize + "v" + teamSize, "seat 0 on the bot brain (stand-in for a player who heads for the action), " + n + " seeds x Warrior/Farmer",
                "longest idle median " + PxLog.F(ml, "0.0") + " s, worst " + PxLog.F(worst, "0.0") + " s, dead or walking back " + PxLog.F(share) + ", respawn to first hit median " + PxLog.F(mh, "0.0") + " s [" + string.Join("; ", rows) + "]",
                "median <= " + T.LongestIdleMedianMax + " s, worst <= " + T.LongestIdleWorstMax + " s, share <= " + T.DeadOrWalkingShareMax + ", respawn to hit <= " + T.RespawnToHitMedianMax + " s",
                ml <= T.LongestIdleMedianMax && worst <= T.LongestIdleWorstMax && share <= T.DeadOrWalkingShareMax && !(mh > T.RespawnToHitMedianMax));
            Assert.IsTrue(ok);
        }

        [Test]
        public void S49_Engagement_16v16_NeverBored() => Size(16);

        [Test]
        public void S49_Engagement_5v5_NeverBored() => Size(5);
    }
}
