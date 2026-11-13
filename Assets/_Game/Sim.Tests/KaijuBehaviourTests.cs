using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/kaiju.test.ts describe "kaiju behaviour" (09e0904, lines 441-689).</summary>
    [TestFixture]
    public sealed class KaijuBehaviourTests
    {
        /*
         * 5v5, human azure warrior. Roster (CM-01 largest remainder):
         *   azure   0 warrior (human), 1 warrior, 2 farmer, 3 farmer, 4 miner
         *   crimson 5 warrior,         6 warrior, 7 farmer, 8 farmer, 9 miner
         */
        private const double ShrineX = 56;
        private const double ShrineZ = 8;

        private static World NewWorld(int teamSize = 5)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 7,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = SimPhysicsTests.ClassicBrowser(),
            });
        }

        /// <summary>CM-34: corruption units that lock berserk before the hatch, derived from the rate.</summary>
        private static int BerserkUnits(GameConfig c)
        {
            return (int)Math.Ceiling(c.KaijuFeed.BerserkThreshold / c.KaijuFeed.CorruptionInstabilityPerUnit);
        }

        /// <summary>Teleport an actor (no interpolation streak) and drop its spawn protection.</summary>
        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = x;
            w.Z[id] = z;
            w.PrevX[id] = x;
            w.PrevZ[id] = z;
            w.Actors[id].SpawnProtect = 0;
        }

        /// <summary>Remove an actor from play for the rest of the test (dead, never respawns).</summary>
        private static void Bench(World w, int id)
        {
            Actor a = w.Actors[id];
            a.Alive = false;
            a.Hp = 0;
            a.RespawnIn = 1e9;
            a.Moving = false;
        }

        private static void Hold(World w, int id, bool interact, bool attack = false, double aimX = double.NaN, double aimZ = double.NaN)
        {
            InputFrame f = SimInput.EmptyInput();
            f.Interact = interact;
            f.Attack = attack;
            f.AimX = aimX;
            f.AimZ = aimZ;
            SimInput.ApplyInput(w, id, f);
        }

        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            int from = Math.Max(0, w.EventHead - World.EventCapacity);
            for (int i = from; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type)
                    output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return output;
        }

        private static int CountKaijuHits(World w)
        {
            int c = 0;
            foreach (SimEvent e in Events(w, SimEventType.Hit)) if (e.B < 0) c++;
            return c;
        }

        private static void StepN(World w, double n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        /// <summary>Force-hatch now (same code path as the natural hatch) and return after the hatching step.</summary>
        private static void ForceHatch(World w)
        {
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.AreEqual(true, w.Match.Hatched);
        }

        /// <summary>Deterministic test-local LCG (the sim's own rng is never touched by the test).</summary>
        private static Func<double> Lcg(uint seed)
        {
            uint s = seed;
            return () =>
            {
                unchecked
                {
                    s = s * 1664525u + 1013904223u;
                }
                return s / 4294967296.0;
            };
        }

        /// <summary>Two-argument Math.hypot as V8 computes it (scale by the max, compensated sum).</summary>
        private static double Hypot(double a, double b)
        {
            double x = Math.Abs(a);
            double z = Math.Abs(b);
            if (double.IsInfinity(x) || double.IsInfinity(z)) return double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(z)) return double.NaN;
            double max = Math.Max(x, z);
            if (max == 0) return 0;
            double sum = 0;
            double compensation = 0;
            double n0 = x / max;
            double summand0 = n0 * n0 - compensation;
            double preliminary0 = sum + summand0;
            compensation = (preliminary0 - sum) - summand0;
            sum = preliminary0;
            double n1 = z / max;
            double summand1 = n1 * n1 - compensation;
            double preliminary1 = sum + summand1;
            sum = preliminary1;
            return Math.Sqrt(sum) * max;
        }

        private static bool NearOwnPad(World w, int id)
        {
            Actor a = w.Actors[id];
            PadDef pad = w.Map.Pads[(int)a.Team];
            double lim = pad.R + 2;
            double dx = w.X[id] - pad.X;
            double dz = w.Z[id] - pad.Z;
            return dx * dx + dz * dz <= lim * lim;
        }

        [Test]
        public void NeutralKaijuDoesNotAttackANearbyActorUntilThatActorDamagesIt()
        {
            World w = NewWorld();
            double hz = w.Config.SimHz;
            KaijuConfig kc = w.Config.Kaiju;
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Neutral, w.Kaiju.State);
            double homeX = w.Kaiju.X;
            double homeZ = w.Kaiju.Z;
            for (int id = 2; id < 10; id++) Bench(w, id);
            Place(w, 1, homeX + 1.5, homeZ);
            Hold(w, 1, false, false, homeX, homeZ);
            for (int s = 0; s < 5 * hz; s++)
            {
                SimStep.Step(w);
                Assert.AreEqual(w.Actors[1].MaxHp, w.Actors[1].Hp);
            }
            Assert.AreEqual(-1, w.Kaiju.TargetId);
            Assert.AreEqual(0, CountKaijuHits(w));

            // Actor 1 cleaves the Kaiju once.
            Hold(w, 1, false, true, w.Kaiju.X, w.Kaiju.Z);
            SimStep.Step(w);
            Hold(w, 1, false, false, w.Kaiju.X, w.Kaiju.Z);
            Assert.AreEqual(w.Kaiju.MaxHp - w.Config.Class(ClassId.Warrior).AttackDamage, w.Kaiju.Hp);
            Assert.Greater(w.Kaiju.Threat[1], kc.NeutralThreatMemorySeconds - 0.1);
            StepN(w, 3 * hz);
            Assert.Less(w.Actors[1].Hp, w.Actors[1].MaxHp);
            bool hitByKaiju = false;
            foreach (SimEvent e in Events(w, SimEventType.Hit)) if (e.A == 1 && e.B == Damage.KaijuSource) hitByKaiju = true;
            Assert.AreEqual(true, hitByKaiju);
        }

        [Test]
        public void SlamTelegraphs12SWhen2PlusHostilesAreWithinSlamRadiusStandsStillThenHitsThoseStillInside()
        {
            World w = NewWorld();
            double hz = w.Config.SimHz;
            KaijuConfig kc = w.Config.Kaiju;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            ForceHatch(w);
            for (int id = 7; id < 10; id++) Bench(w, id);
            double kx = w.Kaiju.X;
            double kz = w.Kaiju.Z;
            Place(w, 5, kx + 3, kz);
            Place(w, 6, kx - 3, kz);
            int s = 0;
            while (w.Kaiju.TelegraphIn <= 0 && s < 30)
            {
                SimStep.Step(w);
                s++;
            }
            Assert.Greater(w.Kaiju.TelegraphIn, 0);
            Assert.AreEqual(1, Events(w, SimEventType.KaijuTelegraph).Count);
            double tx = w.Kaiju.TelegraphX;
            double tz = w.Kaiju.TelegraphZ;
            // Actor 6 dodges out of the circle; 5 stays.
            Place(w, 6, tx - kc.SlamRadius - 2, tz);
            double hp5 = w.Actors[5].Hp;
            double hp6 = w.Actors[6].Hp;
            double wind = JsMath.Round(kc.SlamTelegraphSeconds * hz);
            for (int i = 0; i < wind - 1; i++)
            {
                SimStep.Step(w);
                Assert.AreEqual(tx, w.Kaiju.X, 1e-4);
                Assert.AreEqual(tz, w.Kaiju.Z, 1e-4);
            }
            Assert.AreEqual(0, Events(w, SimEventType.KaijuSlam).Count);
            SimStep.Step(w);
            Assert.AreEqual(1, Events(w, SimEventType.KaijuSlam).Count);
            Assert.AreEqual(kc.SlamCooldown, w.Kaiju.SlamCd, 0.5 * Math.Pow(10, -5));
            Assert.GreaterOrEqual(hp5 - w.Actors[5].Hp, kc.SlamDamage);
            Assert.AreEqual(hp6, w.Actors[6].Hp);
        }

        [Test]
        public void KaijuReachesHill()
        {
            // P1 (a): allied azure Kaiju force-hatched at t=10 with no living crimson actors comes within hill.r + 2 of the
            // hill centre within 1200 steps.
            World w = NewWorld();
            double hz = w.Config.SimHz;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            StepN(w, 10 * hz);
            for (int id = 5; id < 10; id++) Bench(w, id);
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
            HillDef hill = w.Map.Hill;
            double lim = hill.R + 2;
            int reached = -1;
            for (int s = 0; s < 1200; s++)
            {
                SimStep.Step(w);
                double d = Hypot(w.Kaiju.X - hill.X, w.Kaiju.Z - hill.Z);
                if (d <= lim)
                {
                    reached = s;
                    break;
                }
            }
            Assert.GreaterOrEqual(reached, 0);
        }

        [Test]
        public void KaijuBerserkTravel()
        {
            // P1 (b): berserk Kaiju whose only living target stands on the Azure pad island travels >= 30 units from the
            // shrine within 1800 steps.
            World w = NewWorld();
            int units = BerserkUnits(w.Config);
            for (int i = 0; i < units; i++) SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1);
            Assert.AreEqual(true, w.Match.BerserkLocked);
            for (int id = 0; id < 10; id++) if (id != 1) Bench(w, id);
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            Place(w, 1, pad.X + pad.R + 4, pad.Z); // on the pad island, beyond pad.r + 2
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
            double far = 0;
            for (int s = 0; s < 1800; s++)
            {
                SimStep.Step(w);
                w.Actors[1].SpawnProtect = 0;
                far = Math.Max(far, Hypot(w.Kaiju.X - ShrineX, w.Kaiju.Z - ShrineZ));
                if (far >= 30) break;
            }
            Assert.GreaterOrEqual(far, 30);
        }

        [Test]
        public void KaijuNotStuck()
        {
            // P1 (d): while it has a target, the Kaiju position changes by > 0.5 u in every 5 s window. The single target
            // jumps to the far side of the map whenever the Kaiju closes in, so it always has somewhere to walk.
            World w = NewWorld();
            double hz = w.Config.SimHz;
            int units = BerserkUnits(w.Config);
            for (int i = 0; i < units; i++) SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Corruption, 1);
            for (int id = 0; id < 10; id++) if (id != 2) Bench(w, id);
            PointXZ[] spots =
            {
                new PointXZ(56, 40),
                new PointXZ(28, 56),
                new PointXZ(28, 18),
                new PointXZ(84, 18),
                new PointXZ(84, 56),
                new PointXZ(56, 65),
                new PointXZ(20, 36),
                new PointXZ(92, 36),
            };
            Place(w, 2, spots[1].X, spots[1].Z);
            ForceHatch(w);
            double window = 5 * hz;
            var hx = new List<double>();
            var hzs = new List<double>();
            var had = new List<bool>();
            int relocations = 0;
            for (int s = 0; s < 3000; s++)
            {
                SimStep.Step(w);
                Actor t = w.Actors[2];
                t.Hp = t.MaxHp;
                t.SpawnProtect = 0;
                if (Hypot(w.Kaiju.X - w.X[2], w.Kaiju.Z - w.Z[2]) < 8)
                {
                    PointXZ best = spots[0];
                    double bd = -1;
                    foreach (PointXZ p in spots)
                    {
                        double d = Hypot(p.X - w.Kaiju.X, p.Z - w.Kaiju.Z);
                        if (d > bd)
                        {
                            bd = d;
                            best = p;
                        }
                    }
                    Place(w, 2, best.X, best.Z);
                    relocations++;
                }
                hx.Add(w.Kaiju.X);
                hzs.Add(w.Kaiju.Z);
                had.Add(w.Kaiju.TargetId >= 0);
                Assert.GreaterOrEqual(w.Kaiju.X, w.Map.Bounds.MinX);
                Assert.LessOrEqual(w.Kaiju.X, w.Map.Bounds.MaxX);
                Assert.GreaterOrEqual(w.Kaiju.Z, w.Map.Bounds.MinZ);
                Assert.LessOrEqual(w.Kaiju.Z, w.Map.Bounds.MaxZ);
            }
            int windows = 0;
            for (int s = 0; s + window < hx.Count; s++)
            {
                bool all = true;
                for (int i = s; i <= s + window; i++) if (!had[i]) all = false;
                if (!all) continue;
                windows++;
                double moved = 0;
                for (int i = s + 1; i <= s + window; i++) moved = Math.Max(moved, Hypot(hx[i] - hx[s], hzs[i] - hzs[s]));
                Assert.Greater(moved, 0.5, $"window starting at step {s}");
            }
            Assert.Greater(windows, 2000);
            Assert.Greater(relocations, 0);
        }

        [Test]
        public void BerserkScenarioWith10ActorsNeverTargetsTheDeadNeverLeavesMapBoundsNeverPicksATargetWithinPadRPlus2OfItsOwnPad3000StepsEveryStep()
        {
            World w = NewWorld();
            int units = BerserkUnits(w.Config);
            for (int i = 0; i < units; i++) SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1);
            // Spread the 10 actors around the central island and the shrine bridge.
            Func<double> rnd = Lcg(99);
            for (int id = 0; id < 10; id++) Place(w, id, 50 + 12 * rnd(), 30 + 14 * rnd());
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
            BoundsDef b = w.Map.Bounds;
            var dirX = new double[10];
            var dirZ = new double[10];
            int targeted = 0;
            int kaijuHits = 0;
            int seen = w.EventHead;
            for (int s = 0; s < 3000; s++)
            {
                if (s % 30 == 0)
                {
                    for (int id = 0; id < 10; id++)
                    {
                        // Drift toward the Kaiju with some wander, swinging all the time.
                        double a = rnd() * Math.PI * 2;
                        double tx = w.Kaiju.X - w.X[id];
                        double tz = w.Kaiju.Z - w.Z[id];
                        double tl = Hypot(tx, tz);
                        if (tl == 0 || double.IsNaN(tl)) tl = 1;
                        dirX[id] = 0.6 * (tx / tl) + 0.8 * JsMath.Cos(a);
                        dirZ[id] = 0.6 * (tz / tl) + 0.8 * JsMath.Sin(a);
                    }
                }
                for (int id = 0; id < 10; id++)
                {
                    InputFrame f = SimInput.EmptyInput();
                    f.MoveX = dirX[id];
                    f.MoveZ = dirZ[id];
                    f.AimX = w.Kaiju.X;
                    f.AimZ = w.Kaiju.Z;
                    f.Attack = true;
                    SimInput.ApplyInput(w, id, f);
                }
                SimStep.Step(w);
                Kaiju k = w.Kaiju;
                Assert.GreaterOrEqual(k.X, b.MinX);
                Assert.LessOrEqual(k.X, b.MaxX);
                Assert.GreaterOrEqual(k.Z, b.MinZ);
                Assert.LessOrEqual(k.Z, b.MaxZ);
                if (k.TargetId >= 0)
                {
                    targeted++;
                    Assert.AreEqual(true, w.Actors[k.TargetId].Alive);
                    Assert.AreEqual(false, NearOwnPad(w, k.TargetId));
                    Assert.AreEqual(0.0, w.Actors[k.TargetId].SpawnProtect);
                }
                for (; seen < w.EventHead; seen++)
                {
                    SimEvent e = w.Events[seen % World.EventCapacity];
                    if (e.Type == SimEventType.Hit && e.B < 0) kaijuHits++;
                }
            }
            Assert.Greater(targeted, 300);
            Assert.Greater(kaijuHits, 0);
        }

        [Test]
        public void AfterPhaseEndedNoKaijuHitEventBLt0IsPushed()
        {
            World w = NewWorld();
            int units = BerserkUnits(w.Config);
            for (int i = 0; i < units; i++) SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1);
            ForceHatch(w);
            for (int id = 0; id < 10; id++)
            {
                Place(w, id, w.Kaiju.X + (id % 2 == 0 ? 1.5 : -1.5), w.Kaiju.Z + 0.1 * id);
                w.Actors[id].MaxHp = 1e6;
                w.Actors[id].Hp = 1e6;
            }
            w.Match.Time = w.Config.MatchSeconds - 2;
            int guard = 0;
            while (w.Match.Phase != MatchPhase.Ended && guard++ < 200) SimStep.Step(w);
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase);
            Assert.AreEqual(true, CountKaijuHits(w) > 0); // it was fighting before the whistle
            int head = w.EventHead;
            int before = CountKaijuHits(w);
            for (int s = 0; s < 120; s++) SimStep.Step(w);
            Assert.AreEqual(head, w.EventHead);
            Assert.AreEqual(before, CountKaijuHits(w));
            // damageKaiju / applyDamage are inert too.
            Assert.AreEqual(0.0, Kaiju.DamageKaiju(w, 0, 100));
        }
    }
}
