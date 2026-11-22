using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b14-c item 3: no living grounded actor ever ends a tick with its centre inside the hatched Kaiju's body
    /// (Collide.KaijuDepenetrate after the Kaiju system and after respawns, Full profile). Every case prints its numbers.
    /// </summary>
    [TestFixture]
    public sealed class KaijuDepenetrateTests
    {
        static World HatchedField()
        {
            World w = DepthKit.Field(DepthKit.Cfg(), ClassId.Warrior);
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.IsTrue(w.Match.Hatched, "the Kaiju hatched");
            return w;
        }

        /// <summary>
        /// Every Kaiju spot on the map (every 2 u where its collider fits) x actor spots inside its body (centre, and 0.3 / 0.7 /
        /// 0.95 of the body radius in 8 directions, on walkable ground): after one KaijuDepenetrate the actor's centre is outside
        /// the body and on walkable ground. Wall-side spots are the pinned case (the radial push is blocked; the ring search
        /// places it).
        /// </summary>
        [Test]
        public void AnActorInsideTheBodyAnywhereOnTheMapEndsOutsideItOnWalkableGround()
        {
            World w = HatchedField();
            Kaiju k = w.Kaiju;
            double body = w.Config.Physics.KaijuBodyRadius;
            double rr = body + Collide.ActorRadius(w, 0);
            int cases = 0, inside = 0, blocked = 0, viaRing = 0, spots = 0;
            string firstFail = "";
            double[] fr = { 0.0, 0.3, 0.7, 0.95 };
            for (double kz = 2; kz < w.Map.Height - 2; kz += 2)
                for (double kx = 2; kx < w.Map.Width - 2; kx += 2)
                {
                    k.X = kx;
                    k.Z = kz;
                    if (!Kaiju.CircleClear(w)) continue;
                    spots++;
                    for (int f = 0; f < fr.Length; f++)
                        for (int dir = 0; dir < (f == 0 ? 1 : 8); dir++)
                        {
                            double ang = dir * Math.PI / 4;
                            double ax = kx + Math.Cos(ang) * fr[f] * body, az = kz + Math.Sin(ang) * fr[f] * body;
                            if (!Terraform.WalkableAt(w.Map, ax, az)) continue;
                            DepthKit.Put(w, 0, ax, az);
                            // b16 [1]: a pinned fighter may hold the Kaiju back (it gives way): each case starts from the spot, and
                            // the distance is measured to where the Kaiju ends.
                            k.X = kx; k.Z = kz; k.PrevX = kx; k.PrevZ = kz;
                            cases++;
                            Collide.KaijuDepenetrate(w);
                            double dx = w.X[0] - k.X, dz = w.Z[0] - k.Z, d = Math.Sqrt(dx * dx + dz * dz);
                            if (d > rr + 0.01) viaRing++;
                            bool bad = false;
                            if (d < body) { inside++; bad = true; }
                            if (!Terraform.WalkableAt(w.Map, w.X[0], w.Z[0])) { blocked++; bad = true; }
                            if (bad && firstFail.Length == 0) firstFail = " first failure: Kaiju (" + kx + ", " + kz + ") actor from (" + ax.ToString("F2") + ", " + az.ToString("F2") + ") to (" + w.X[0].ToString("F2") + ", " + w.Z[0].ToString("F2") + ") distance " + d.ToString("F3");
                        }
                }
            Console.WriteLine("[b14-c depenetrate] Kaiju spots " + spots + ", actor cases " + cases + ", placed by the ring search (pinned) " + viaRing + ", left inside the body " + inside + ", on blocked ground " + blocked + " (limit 0 and 0)" + firstFail);
            Assert.Greater(cases, 1000, "the sweep covers the map");
            Assert.Greater(viaRing, 0, "the sweep includes pinned (wall-side) cases");
            Assert.AreEqual(0, inside, "left inside the Kaiju body");
            Assert.AreEqual(0, blocked, "put on blocked ground");
        }

        [Test]
        public void ClassicAndPhysicsOnlyAreUntouched()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                GameConfig c = GameConfig.CreateBrowser5v5().ApplyRules(p);
                World w = DepthKit.Field(c, ClassId.Warrior);
                w.ForceHatch = true;
                SimStep.Step(w);
                DepthKit.Put(w, 0, w.Kaiju.X, w.Kaiju.Z);
                double x0 = w.X[0], z0 = w.Z[0];
                int moved = Collide.KaijuDepenetrate(w);
                Console.WriteLine("[b14-c depenetrate] " + p + ": actors moved " + moved + ", actor moved by " + Math.Abs(w.X[0] - x0 + w.Z[0] - z0) + " (limit 0, 0)");
                Assert.AreEqual(0, moved);
                Assert.AreEqual(x0, w.X[0]);
                Assert.AreEqual(z0, w.Z[0]);
            }
        }

        static int CountInside(World w, int ticks)
        {
            InputFrame scratch = SimInput.EmptyInput();
            double body = w.Config.Physics.KaijuBodyRadius;
            int count = 0;
            for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                Kaiju k = w.Kaiju;
                if (k.State == KaijuState.Egg) continue;
                for (int id = 0; id < w.ActorCount; id++)
                {
                    Actor a = w.Actors[id];
                    if (!a.Alive || a.AirLen > 0) continue;
                    double dx = w.X[id] - k.X, dz = w.Z[id] - k.Z;
                    if (dx * dx + dz * dz < body * body) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Whole 16v16 matches: the PR #203 probe seed (101, six-class mix, Normal and Hard, where 1 to 11 actor-ticks ended inside)
        /// and the default game on seeds 1 to 4: zero actor-ticks inside the body at the end of any tick.
        /// </summary>
        [Test, Category("Slow")]
        public void WholeMatchesEndNoTickWithAnActorInsideTheKaiju()
        {
            int total = 0;
            foreach (BotDifficulty d in new[] { BotDifficulty.Normal, BotDifficulty.Hard })
            {
                GameConfig cfg = SimPhysicsSoakTests.SixClassMix(false);
                World w = World.CreateWorld(new WorldOptions { Seed = 101, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg, BotDifficulty = d, AllyBotDifficulty = d });
                int n = CountInside(w, (int)(cfg.MatchSeconds * cfg.SimHz) + 30);
                Console.WriteLine("[b14-c depenetrate] six-class mix 16v16 seed 101 " + d + ": " + n + " actor-ticks inside the Kaiju body (limit 0)");
                total += n;
            }
            for (int seed = 1; seed <= 4; seed++)
            {
                GameConfig cfg = GameConfig.CreateDefault();
                World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                int n = CountInside(w, (int)(cfg.MatchSeconds * cfg.SimHz) + 30);
                Console.WriteLine("[b14-c depenetrate] default 16v16 seed " + seed + ": " + n + " actor-ticks inside the Kaiju body (limit 0)");
                total += n;
            }
            Assert.AreEqual(0, total, "actor-ticks ending inside the Kaiju body");
        }

        /// <summary>16v16 seed 4242 Full (Kaiju out from 0:10), 600 warm-up ticks, then 300 ticks: the whole tick allocates 0 B, and
        /// so do 300 direct calls that each start with a fighter at the Kaiju's centre (the ring search path).</summary>
        [Test]
        public void NoAllocationOver300TicksAndOnThePinnedPath()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.HatchSeconds = 10;
            World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch);
            Assert.IsTrue(w.Match.Hatched);
            Collide.KaijuDepenetrate(w);
            long tick = 0, own = 0;
            int forced = 0;
            for (int t = 0; t < 300; t++)
            {
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                MatchTick.Advance(w, null, scratch);
                tick += GC.GetAllocatedBytesForCurrentThread() - a0;
                int id = 1 + t % (w.ActorCount - 1);
                if (!w.Actors[id].Alive) continue;
                double sx = w.X[id], sz = w.Z[id];
                w.X[id] = w.Kaiju.X;
                w.Z[id] = w.Kaiju.Z;
                long o0 = GC.GetAllocatedBytesForCurrentThread();
                forced += Collide.KaijuDepenetrate(w);
                own += GC.GetAllocatedBytesForCurrentThread() - o0;
                w.X[id] = sx;
                w.Z[id] = sz;
            }
            Console.WriteLine("[b14-c depenetrate] 16v16 seed 4242 Full: whole tick " + tick + " B over 300 ticks, depenetration calls " + own + " B over " + forced + " forced cases (limit: the depenetration 0 B; the whole tick printed)");
            Assert.Greater(forced, 0);
            Assert.AreEqual(0, own, "the depenetration allocates nothing");
        }
    }
}
