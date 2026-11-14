using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// fix/movement-shiver-1 (owner 2026-11-14, v1.0.573: the Kaiju "shaking and vibrating at the door"). The repro on the real
    /// map, the fix's two outcomes (a stable decision at a spot the body cannot reach, net progress at one it can) and the
    /// "no shiver" soak over whole bot matches. Every test runs the game's own rules (GameConfig.CreateDefault, Full).
    /// </summary>
    [TestFixture]
    public sealed class MovementShiverTests
    {
        private static World NewWorld(bool guard, int teamSize = 5, int seed = 7)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.Physics.KaijuNavGuard = guard;
            cfg.Physics.BotWideLine = guard;
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg,
            });
        }

        private static void Pin(World w, int id, double x, double z)
        {
            w.X[id] = x;
            w.Z[id] = z;
            w.PrevX[id] = x;
            w.PrevZ[id] = z;
            w.Vx[id] = 0;
            w.Vz[id] = 0;
            w.Actors[id].SpawnProtect = 0;
        }

        private static void Bench(World w, int id)
        {
            Actor a = w.Actors[id];
            a.Alive = false;
            a.Hp = 0;
            a.RespawnIn = 1e9;
            a.Moving = false;
        }

        /// <summary>An Azure-owned Kaiju (one biomass delivered by Azure), hatched, standing at (kx, kz); every actor benched but
        /// the first Crimson one, which is returned.</summary>
        private static int Setup(World w, double kx, double kz)
        {
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.AreEqual(true, w.Match.Hatched);
            Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
            int target = -1;
            for (int id = 0; id < w.ActorCount; id++)
            {
                if (target < 0 && w.Actors[id].Team == TeamId.Crimson) target = id;
                else Bench(w, id);
            }
            Kaiju k = w.Kaiju;
            k.X = kx;
            k.Z = kz;
            k.PrevX = kx;
            k.PrevZ = kz;
            return target;
        }

        /// <summary>Run `ticks` sim steps with the target pinned at (tx, tz); returns the detector's Kaiju flags.</summary>
        private static int RunPinned(World w, int target, double tx, double tz, int ticks, ShiverWatch watch, out double closest)
        {
            closest = double.PositiveInfinity;
            int kaijuFlags = 0;
            for (int t = 0; t < ticks; t++)
            {
                Pin(w, target, tx, tz);
                w.Actors[target].Hp = w.Actors[target].MaxHp; // never dies, so the scene holds for the whole run
                SimStep.Step(w);
                watch.Observe(w);
                double dx = w.X[target] - w.Kaiju.X;
                double dz = w.Z[target] - w.Kaiju.Z;
                closest = Math.Min(closest, Math.Sqrt(dx * dx + dz * dz));
            }
            foreach (ShiverFlag f in watch.Flags)
            {
                if (f.Actor != ShiverWatch.KaijuId) continue;
                kaijuFlags++;
                Console.WriteLine(ShiverWatch.Format(f));
            }
            return kaijuFlags;
        }

        // The bridge mouth from the owner's screenshots: the north-east end of the hill's ring wall, where the diagonal bridge from
        // the Crimson courtyard (B3 mirrored, (77, 20) to (68, 30)) lands. No cell of that bridge fits the Kaiju's body (wall
        // radius 1.1), so the last cell it fits in is (68, 29), and a fighter on the bridge at (71.5, 27.5) is 3.6 m from it.
        private const double KaijuX = 68.5, KaijuZ = 30.5, BridgeX = 71.5, BridgeZ = 27.5;

        [Test]
        public void ReproTheOldKaijuShiversAtTheBridgeMouthForTenSeconds()
        {
            // The pre-fix behaviour, kept reachable through the flag: it walks into the goal cell, steers straight at the fighter,
            // the collider pushes it back, the path steers it in again: direction flips every tick and it never arrives.
            World w = NewWorld(false);
            int target = Setup(w, KaijuX, KaijuZ);
            var watch = new ShiverWatch();
            int flags = RunPinned(w, target, BridgeX, BridgeZ, 300, watch, out double closest);
            Console.WriteLine("{\"item\":\"kaiju bridge mouth\",\"guard\":false,\"kaijuFlags\":" + flags + ",\"closest\":" + closest.ToString("F2") + "}");
            Assert.Greater(flags, 0, "the old path shivers at the bridge mouth (the bug this branch fixes)");
            Assert.Greater(closest, w.Config.Kaiju.MeleeRange, "and never gets in claw reach");
        }

        [Test]
        public void TheKaijuMakesAStableDecisionAtTheBridgeMouthFor600Ticks()
        {
            World w = NewWorld(true);
            int target = Setup(w, KaijuX, KaijuZ);
            var watch = new ShiverWatch();
            int flags = RunPinned(w, target, BridgeX, BridgeZ, 600, watch, out double closest);
            Console.WriteLine("{\"item\":\"kaiju bridge mouth\",\"guard\":true,\"kaijuFlags\":" + flags + ",\"closest\":" + closest.ToString("F2") + ",\"target\":" + w.Kaiju.TargetId + "}");
            Assert.AreEqual(0, flags, "no shiver, no facing flip-flop, not pressed into the bridge edge");
            // A fighter the body cannot get in claw reach of is not chased: the allied Kaiju goes on to the hill instead.
            Assert.AreNotEqual(target, w.Kaiju.TargetId, "an out-of-reach fighter on the bridge is not the target");
        }

        [Test]
        public void TheKaijuMakesNetProgressAroundTheRingWallEndToAFighterItCanReach()
        {
            // Inside the ring west of the hill, the fighter just outside the ring wall (x = 46) near the west gap.
            World w = NewWorld(true);
            int target = Setup(w, 50.5, 36.5);
            var watch = new ShiverWatch();
            int flags = RunPinned(w, target, 44.5, 36.5, 600, watch, out double closest);
            Console.WriteLine("{\"item\":\"kaiju ring wall end\",\"kaijuFlags\":" + flags + ",\"closest\":" + closest.ToString("F2") + "}");
            Assert.AreEqual(0, flags);
            Assert.LessOrEqual(closest, w.Config.Kaiju.MeleeRange, "walks round the wall end into claw reach");
        }

        /// <summary>
        /// The soak: whole 16v16 bot matches on fixed seeds (one with an early hatch so the Kaiju walks for most of it), every
        /// actor and the Kaiju under the detector. Before (both new flags off) is printed only; after must be zero.
        /// </summary>
        [Test, Category("Slow")]
        public void NoActorOrKaijuShiversInWholeBotMatches()
        {
            int before = 0;
            int after = 0;
            foreach (bool guard in new[] { false, true })
            {
                foreach (int seed in new[] { 1, 7, 1337 })
                {
                    foreach (double hatch in new[] { 240.0, 60.0 })
                    {
                        GameConfig cfg = GameConfig.CreateDefault();
                        cfg.Physics.KaijuNavGuard = guard;
                        cfg.Physics.BotWideLine = guard;
                        cfg.HatchSeconds = hatch;
                        World w = World.CreateWorld(new WorldOptions
                        {
                            Seed = seed,
                            TeamSize = 16,
                            HumanTeam = TeamId.Azure,
                            HumanClass = ClassId.Warrior,
                            Config = cfg,
                        });
                        var watch = new ShiverWatch();
                        InputFrame scratch = SimInput.EmptyInput();
                        for (int t = 0; t < 14400 && w.Match.Phase != MatchPhase.Ended; t++)
                        {
                            MatchTick.Advance(w, null, scratch);
                            watch.Observe(w);
                        }
                        int kaiju = 0;
                        foreach (ShiverFlag f in watch.Flags) if (f.Actor == ShiverWatch.KaijuId) kaiju++;
                        Console.WriteLine("{\"item\":\"shiver soak\",\"guard\":" + (guard ? "true" : "false") + ",\"seed\":" + seed +
                                          ",\"hatch\":" + hatch + ",\"flags\":" + watch.Count + ",\"dither\":" + watch.CountByKind[0] +
                                          ",\"reversals\":" + watch.CountByKind[1] + ",\"pressed\":" + watch.CountByKind[2] + ",\"kaijuKept\":" + kaiju + "}");
                        if (guard)
                        {
                            after += watch.Count;
                            int shown = 0;
                            foreach (ShiverFlag f in watch.Flags) if (shown++ < 40) Console.WriteLine(ShiverWatch.Format(f));
                        }
                        else before += watch.Count;
                    }
                }
            }
            Console.WriteLine("{\"item\":\"shiver soak total\",\"before\":" + before + ",\"after\":" + after + "}");
            Assert.AreEqual(0, after, "no actor or Kaiju may shiver, dither or sit pressed while wanting to move (lines above)");
        }

        [Test]
        public void TheDetectorFlagsABackAndForthActorAndNotAWalker()
        {
            World w = NewWorld(true);
            Setup(w, 56, 40);
            var watch = new ShiverWatch();
            int id = -1;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Alive) id = i;
            // A walker: 2 s straight east at 4 m/s. No flag.
            for (int t = 0; t < 60; t++)
            {
                w.PrevX[id] = w.X[id];
                w.PrevZ[id] = w.Z[id];
                w.X[id] += 4.0 / 30;
                watch.Observe(w);
            }
            Assert.AreEqual(0, watch.Count, "walking straight is not a shiver");
            // A shiver: 2 s of +0.1 / -0.1 every tick (covers one whole detector window whatever its phase).
            for (int t = 0; t < 60; t++)
            {
                w.PrevX[id] = w.X[id];
                w.PrevZ[id] = w.Z[id];
                w.X[id] += (t & 1) == 0 ? 0.1 : -0.1;
                watch.Observe(w);
            }
            Assert.Greater(watch.Count, 0, "back and forth every tick is flagged");
            Assert.AreEqual(id, watch.Flags[0].Actor);
            StringAssert.StartsWith("[CM.Shiver] actor ", ShiverWatch.Format(watch.Flags[0]));
        }
    }
}
