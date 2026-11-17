using System;
using System.Collections.Generic;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM b13 item C (Travis): crystal veins, carriers, the pool, the capped sqrt chance, the seeded roll at the hatch and at a
    /// knock-out get-up, the wild Kaiju (leader-biased, holds the hill for neither team, never re-rolled), the old instability
    /// line untouched outside the rule, zero allocation, and the balance run against its same-seed control (Corruption.On
    /// false, everything else equal). Every test prints one line per measured case.
    /// </summary>
    [TestFixture]
    public sealed class CorruptionRollTests
    {
        static World Make(int seed, int size, Action<GameConfig> tweak = null, GameConfig baseCfg = null)
        {
            GameConfig cfg = baseCfg ?? GameConfig.CreateDefault();
            cfg.TeamSize = size;
            tweak?.Invoke(cfg);
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = size, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
        }

        static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Actors[id].SpawnProtect = 0;
        }

        static void Hold(World w, int id, bool interact)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = w.X[id];
            f.AimZ = w.Z[id];
            f.Interact = interact;
            SimInput.ApplyInput(w, id, f);
        }

        static void Hatch(World w)
        {
            w.ForceHatch = true;
            for (int i = 0; i < 4000 && !w.Match.Hatched; i++) SimStep.Step(w);
            w.ForceHatch = false;
            Assert.IsTrue(w.Match.Hatched, "hatched");
        }

        /// <summary>First living actor of team that may carry crystals, is not the human slot and not a Healer.</summary>
        static int Raider(World w, TeamId team, int skip = -1)
        {
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (i == skip || a.IsHuman || !a.Alive || a.Team != team || a.Cls == ClassId.Healer || a.Cls == ClassId.Miner) continue;
                if (Roles.MayCorrupt(w, a.Cls)) return i;
            }
            return -1;
        }

        static void GetUpNextTick(World w)
        {
            w.Kaiju.Hp = 0;
            w.Kaiju.KoIn = 0.5 / w.Config.SimHz;
            SimStep.Step(w);
        }

        [Test]
        public void ChanceIsTheCappedSqrtAndNeverOne()
        {
            CorruptionConfig c = new CorruptionConfig();
            double[] pools = { 0, 1, 4, 5, 9, 9.8, 20, 100 };
            double[] expect = { 0, Math.Sqrt(1 / 20.0), Math.Sqrt(4 / 20.0), 0.5, Math.Sqrt(9 / 20.0), 0.70, 0.70, 0.70 };
            for (int i = 0; i < pools.Length; i++)
            {
                double p = Corruption.ChanceOf(pools[i], c);
                Console.WriteLine($"[b13-c chance] pool {pools[i]}: chance {p:0.0000} (expect {expect[i]:0.0000}; limit <= 0.70, < 1)");
                Assert.AreEqual(expect[i], p, 1e-9, "pool " + pools[i]);
                Assert.LessOrEqual(p, 0.70);
                Assert.Less(p, 1.0);
            }
        }

        [Test]
        public void RollValuesAreSeededPerRollIndexAndRepeat()
        {
            CorruptionConfig c = new CorruptionConfig();
            foreach (int seed in new[] { 1, 1337, 4242 })
            {
                for (int r = 0; r < 4; r++)
                {
                    double u = Corruption.RollU(seed, r, c);
                    double again = Corruption.RollU(seed, r, c);
                    double expect;
                    unchecked { expect = SimResources.SeedUniform(seed * 7919 + 104729 * (r + 1)); }
                    Console.WriteLine($"[b13-c roll u] seed {seed} rollIndex {r}: u {u:0.000000} again {again:0.000000} (limit: equal, in [0, 1))");
                    Assert.AreEqual(u, again);
                    Assert.AreEqual(expect, u);
                    Assert.GreaterOrEqual(u, 0.0);
                    Assert.Less(u, 1.0);
                }
            }
            Assert.AreNotEqual(Corruption.RollU(1337, 0, c), Corruption.RollU(1337, 1, c));
        }

        [Test]
        public void CrystalsAtTheEggGiveNoAllegianceNoInstabilityAndTheTrailingTeamCountsOneAndAHalf()
        {
            World w = Make(7, 5);
            Assert.IsTrue(Corruption.On(w));
            w.Match.HillSeconds.Crimson = 20; // azure behind by 20 s (margin 15)
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Corruption, 1, -1);
            double afterAzure = Corruption.Pool(w);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1, -1);
            double afterBoth = Corruption.Pool(w);
            Console.WriteLine($"[b13-c trailing] azure behind 20 s feeds 1: pool {afterAzure} (expect 1.5); crimson feeds 1: pool {afterBoth} (expect 2.5); allegiance {w.Match.Allegiance.Azure}/{w.Match.Allegiance.Crimson} instability {w.Match.Instability} (expect 0) berserkLine {SimResources.BerserkLine(w)} (expect infinity)");
            Assert.AreEqual(1.5, afterAzure, 1e-12);
            Assert.AreEqual(2.5, afterBoth, 1e-12);
            Assert.AreEqual(0, w.Match.Allegiance.Azure);
            Assert.AreEqual(0, w.Match.Allegiance.Crimson);
            Assert.AreEqual(0, w.Match.Instability);
            Assert.AreEqual(2, w.Match.Delivered[0].Corruption + w.Match.Delivered[1].Corruption);
            Assert.IsTrue(double.IsPositiveInfinity(SimResources.BerserkLine(w)));
            Assert.AreEqual(Corruption.ChanceOf(2.5, w.Config.Depth.Corruption), Corruption.Chance(w), 1e-12);
        }

        [Test]
        public void VeinsHaveTwoChargesGatherTakesThreeSecondsTheMinerOneAndCarriersHoldOne()
        {
            World w = Make(7, 5);
            SimStep.Step(w);
            int node = -1;
            for (int i = 0; i < w.Nodes.Length; i++) if (w.Nodes[i].Kind == NodeKind.Corruption) { node = i; break; }
            Assert.GreaterOrEqual(node, 0);
            ResourceNode nd = w.Nodes[node];
            Console.WriteLine($"[b13-c veins] node {node} at ({nd.X},{nd.Z}): charges {nd.Charges}/{nd.MaxCharges} (expect 2/2)");
            Assert.AreEqual(2, nd.MaxCharges);
            Assert.AreEqual(2, nd.Charges);
            int id = Raider(w, TeamId.Azure);
            Assert.GreaterOrEqual(id, 0);
            Place(w, id, nd.X + 0.6, nd.Z);
            int ticks = 0, head = w.EventHead, mined = 0;
            while (ticks < 400 && w.Actors[id].CarryAmount == 0)
            {
                Place(w, id, nd.X + 0.6, nd.Z);
                Hold(w, id, true);
                SimStep.Step(w);
                ticks++;
            }
            for (int e = head; e < w.EventHead; e++) if (w.Events[e % World.EventCapacity].Type == SimEventType.CrystalMined) mined++;
            int expectTicks = (int)Math.Round(3.0 * w.Config.SimHz);
            for (int t = 0; t < (int)(4 * w.Config.SimHz); t++) { Place(w, id, nd.X + 0.6, nd.Z); Hold(w, id, true); SimStep.Step(w); }
            Console.WriteLine($"[b13-c gather] {w.Actors[id].Cls} crystal after {ticks} ticks (expect {expectTicks} +-2), CrystalMined events {mined} (expect 1), still carrying {w.Actors[id].CarryAmount} after 4 s more (expect 1: cap 1), speedScale {Corruption.SpeedScale(w, id)} (expect 0.85)");
            Assert.AreEqual(expectTicks, ticks, 2);
            Assert.AreEqual(1, mined);
            Assert.AreEqual(1, w.Actors[id].CarryAmount);
            Assert.AreEqual(0.85, Corruption.SpeedScale(w, id));
            Assert.AreEqual(0.85, B13Hooks.SpeedScale(w, id));
            Assert.AreEqual(1.0, Corruption.GatherSeconds(w, ClassId.Miner), 1e-12);
            Assert.AreEqual(2, Corruption.CrystalCap(w, ClassId.Miner));
            Assert.AreEqual(1, Corruption.CrystalCap(w, ClassId.Warrior));
        }

        [Test]
        public void FeedingNeedsAKnockedOutKaijuANonOwnerTeamAndAHitInterruptsIt()
        {
            World w = Make(7, 5);
            Hatch(w);
            Kaiju k = w.Kaiju;
            k.State = KaijuState.Crimson;
            int az = Raider(w, TeamId.Azure);
            int cr = Raider(w, TeamId.Crimson);
            double fx = k.X, fz = k.Z + w.Config.Kaiju.Radius + 1.0;
            foreach (int id in new[] { az, cr })
            {
                w.Actors[id].Carry = CarryKind.Corruption;
                w.Actors[id].CarryAmount = 1;
                Place(w, id, fx, fz);
            }
            // standing Kaiju: refused
            k.KoIn = 0;
            bool any = false;
            for (int t = 0; t < 60; t++) any |= Corruption.StepFeed(w, az);
            Console.WriteLine($"[b13-c feed] standing Kaiju, azure holds E 60 passes: fed {any}, pool {Corruption.Pool(w)} (expect false, 0)");
            Assert.IsFalse(any);
            // down Kaiju, owner team: refused
            k.Hp = 0;
            k.KoIn = w.Config.Kaiju.KoSeconds;
            any = false;
            for (int t = 0; t < 60; t++) any |= Corruption.StepFeed(w, cr);
            Console.WriteLine($"[b13-c feed] down Kaiju owned by crimson, crimson holds E 60 passes: fed {any}, pool {Corruption.Pool(w)} (expect false, 0)");
            Assert.IsFalse(any);
            Assert.AreEqual(0, Corruption.Pool(w));
            // down Kaiju, azure: a hit at 10 ticks interrupts; a full fresh wind-up then feeds
            int need = Durations.T30(w.Config, w.Config.Depth.Corruption.FeedTicks);
            for (int t = 0; t < 10; t++) { Place(w, az, fx, fz); Hold(w, az, true); SimStep.Step(w); }
            int reached = w.Actors[az].DeliverTicks;
            bool interrupted = Delivery.InterruptDelivery(w, az, cr);
            int ticks = 0;
            while (ticks < 200 && w.Actors[az].CarryAmount > 0) { Place(w, az, fx, fz); Hold(w, az, true); SimStep.Step(w); ticks++; }
            Console.WriteLine($"[b13-c feed] azure into the down crimson Kaiju: wind-up {reached} at the hit, interrupted {interrupted}, then fed after {ticks} ticks (expect {need + 1}: re-arm + full wind-up), pool {Corruption.Pool(w)} (expect 1)");
            Assert.IsTrue(interrupted);
            Assert.AreEqual(need + 1, ticks);
            Assert.AreEqual(1, Corruption.Pool(w), 1e-12);
            k.State = KaijuState.Neutral;
            Console.WriteLine($"[b13-c feed] neutral Kaiju: azure may {Corruption.MayFeedTeam(w, 0)} crimson may {Corruption.MayFeedTeam(w, 1)} (expect true, true)");
            Assert.IsTrue(Corruption.MayFeedTeam(w, 0) && Corruption.MayFeedTeam(w, 1));
        }

        /// <summary>A seed whose first roll u0 &gt; lo0 and second roll u1 &lt; hi1 (deterministic scan).</summary>
        static int SeedWith(double lo0, double hi1)
        {
            CorruptionConfig c = new CorruptionConfig();
            for (int s = 1; s < 100000; s++) if (Corruption.RollU(s, 0, c) > lo0 && Corruption.RollU(s, 1, c) < hi1) return s;
            return -1;
        }

        static double PoolFor(double chance, CorruptionConfig c) => chance * chance * c.Full;

        [Test]
        public void HatchRollFailsGetUpRollSucceedsThenWildForTheRestOfTheMatchAndNeverRerolled()
        {
            int seed = SeedWith(0.3, 0.5);
            Assert.Greater(seed, 0);
            World w = Make(seed, 5);
            CorruptionConfig c = w.Config.Depth.Corruption;
            double u0 = Corruption.RollU(seed, 0, c), u1 = Corruption.RollU(seed, 1, c);
            w.Corrupt.PreFed = PoolFor(u0 * 0.5, c);
            int head = w.EventHead;
            Hatch(w);
            int rollEvents = 0;
            for (int e = head; e < w.EventHead; e++) if (w.Events[e % World.EventCapacity].Type == SimEventType.CorruptionRoll) rollEvents++;
            Console.WriteLine($"[b13-c roll] seed {seed} hatch: u0 {u0:0.0000} chance {w.Corrupt.LastChance:0.0000} success {w.Corrupt.LastSuccess} rolls {w.Corrupt.RollIndex} events {rollEvents} state {w.Kaiju.State} pool after {Corruption.Pool(w)} (expect fail, 1 roll, pool 0)");
            Assert.AreEqual(1, w.Corrupt.RollIndex);
            Assert.AreEqual(1, rollEvents);
            Assert.IsFalse(w.Corrupt.LastSuccess);
            Assert.AreNotEqual(KaijuState.Berserk, w.Kaiju.State);
            Assert.AreEqual(0, Corruption.Pool(w));

            w.Kaiju.State = KaijuState.Crimson;
            w.Corrupt.PostPool = PoolFor(u1 + (0.70 - u1) * 0.5, c);
            Assert.Greater(Corruption.NextRollTick(w), -2);
            GetUpNextTick(w);
            Console.WriteLine($"[b13-c roll] get-up: u1 {u1:0.0000} chance {w.Corrupt.LastChance:0.0000} success {w.Corrupt.LastSuccess} rolls {w.Corrupt.RollIndex} state {w.Kaiju.State} wild {Corruption.Wild(w)} pool {Corruption.Pool(w)} nextRollTick {Corruption.NextRollTick(w)} (expect success, 2 rolls, Berserk, pool 0, -1)");
            Assert.AreEqual(2, w.Corrupt.RollIndex);
            Assert.IsTrue(w.Corrupt.LastSuccess);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
            Assert.IsTrue(Corruption.Wild(w));
            Assert.AreEqual(0, Corruption.Pool(w));
            Assert.AreEqual(-1, Corruption.NextRollTick(w));
            Assert.IsFalse(Corruption.MayFeedTeam(w, 0) || Corruption.MayFeedTeam(w, 1));

            for (int round = 0; round < 3; round++)
            {
                w.Corrupt.PostPool = 5;
                GetUpNextTick(w);
                for (int t = 0; t < 30; t++) SimStep.Step(w);
            }
            Console.WriteLine($"[b13-c roll] wild, three more get-ups with a forced pool of 5: rolls {w.Corrupt.RollIndex} state {w.Kaiju.State} (expect 2, Berserk)");
            Assert.AreEqual(2, w.Corrupt.RollIndex);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
        }

        [Test]
        public void HatchRollSuccessHatchesWildAndAGetUpFailSpendsThePool()
        {
            CorruptionConfig c0 = new CorruptionConfig();
            int seedOk = -1;
            for (int s = 1; s < 100000 && seedOk < 0; s++) if (Corruption.RollU(s, 0, c0) < 0.5) seedOk = s;
            World w = Make(seedOk, 5);
            double u0 = Corruption.RollU(seedOk, 0, w.Config.Depth.Corruption);
            w.Corrupt.PreFed = PoolFor(u0 + (0.70 - u0) * 0.5, w.Config.Depth.Corruption);
            Hatch(w);
            Console.WriteLine($"[b13-c roll] seed {seedOk} hatch success: u0 {u0:0.0000} chance {w.Corrupt.LastChance:0.0000} state {w.Kaiju.State} wild {Corruption.Wild(w)} (expect Berserk, true)");
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
            Assert.IsTrue(Corruption.Wild(w));

            int seedFail = SeedWith(0.05, 1.0);
            World f = Make(seedFail, 5);
            Hatch(f);
            Assert.AreEqual(0, f.Corrupt.RollIndex, "no roll at a hatch with an empty pool");
            f.Kaiju.State = KaijuState.Azure;
            double uf = Corruption.RollU(seedFail, 0, f.Config.Depth.Corruption);
            f.Corrupt.PostPool = PoolFor(uf * 0.5, f.Config.Depth.Corruption);
            GetUpNextTick(f);
            Console.WriteLine($"[b13-c roll] seed {seedFail} empty hatch: rolls 0; get-up fail: u {uf:0.0000} chance {f.Corrupt.LastChance:0.0000} success {f.Corrupt.LastSuccess} pool {Corruption.Pool(f)} state {f.Kaiju.State} (expect fail, pool 0, Azure)");
            Assert.AreEqual(1, f.Corrupt.RollIndex);
            Assert.IsFalse(f.Corrupt.LastSuccess);
            Assert.AreEqual(0, Corruption.Pool(f));
            Assert.AreEqual(KaijuState.Azure, f.Kaiju.State);
            f.Corrupt.PostPool = 0;
            GetUpNextTick(f);
            Assert.AreEqual(1, f.Corrupt.RollIndex, "no roll at a get-up with an empty pool");
        }

        [Test]
        public void WildKaijuPrefersTheHillLeaderAndHoldsTheHillForNeither()
        {
            CorruptionConfig c0 = new CorruptionConfig();
            int seed = -1;
            for (int s = 1; s < 100000 && seed < 0; s++) if (Corruption.RollU(s, 0, c0) < 0.5) seed = s;
            World w = Make(seed, 5);
            World tame = Make(seed, 5);
            Hatch(tame);
            w.Corrupt.PreFed = PoolFor(0.69, w.Config.Depth.Corruption);
            Hatch(w);
            Assert.IsTrue(Corruption.Wild(w));
            w.Match.HillSeconds.Azure = 30;
            w.Match.HillSeconds.Crimson = 10;
            int lead = Corruption.WildTargetTeam(w, 1);
            w.Match.HillSeconds.Crimson = 30;
            int level = Corruption.WildTargetTeam(w, 1);
            w.Match.HillSeconds.Crimson = 44;
            int near = Corruption.WildTargetTeam(w, 1);
            w.Match.HillSeconds.Crimson = 45;
            int flipped = Corruption.WildTargetTeam(w, 1);
            Console.WriteLine($"[b13-c wild target] azure leads 30-10: team {lead} (expect 0, bigFeeder 1 ignored); level 30-30: {level} (expect 0, sticky); crimson +14: {near} (expect 0, sticky); crimson +15: {flipped} (expect 1, WildSwitchMargin 15); bias {Corruption.TargetBias(w, 4)} (expect 6); tame Kaiju: team {Corruption.WildTargetTeam(tame, 1)} bias {Corruption.TargetBias(tame, 4)} (expect 1, 4)");
            Assert.AreEqual(0, lead);
            Assert.AreEqual(0, level);
            Assert.AreEqual(0, near);
            Assert.AreEqual(1, flipped);
            Assert.AreEqual(6, Corruption.TargetBias(w, 4));
            Assert.AreEqual(1, Corruption.WildTargetTeam(tame, 1));
            Assert.AreEqual(4, Corruption.TargetBias(tame, 4));

            double dt = SimStep.Dt(w.Config);
            HillDef hill = w.Map.Hill;
            int az = Raider(w, TeamId.Azure);
            for (int i = 0; i < w.ActorCount; i++) if (i != az) Place(w, i, w.Map.Pads[(int)w.Actors[i].Team].X, w.Map.Pads[(int)w.Actors[i].Team].Z);
            Place(w, az, hill.X + 1, hill.Z);
            w.Kaiju.X = hill.X - 1;
            w.Kaiju.Z = hill.Z;
            w.Kaiju.KoIn = 0;
            double before = w.Match.HillSeconds.Azure;
            for (int t = 0; t < 30; t++) Hill.HillSystem(w, dt);
            double held = w.Match.HillSeconds.Azure - before;
            HillState stHeld = w.Match.HillState;
            w.Kaiju.X = hill.X + 40;
            before = w.Match.HillSeconds.Azure;
            for (int t = 0; t < 30; t++) Hill.HillSystem(w, dt);
            double free = w.Match.HillSeconds.Azure - before;
            Console.WriteLine($"[b13-c wild hill] azure alone on the hill with the wild Kaiju: +{held:0.000} s, state {stHeld} (expect 0, Contested); Kaiju off the hill: +{free:0.000} s, state {w.Match.HillState} (expect {30 * dt:0.000}, Azure)");
            Assert.AreEqual(0, held);
            Assert.AreEqual(HillState.Contested, stHeld);
            Assert.AreEqual(30 * dt, free, 1e-9);
        }

        [Test]
        public void CarrierDropsACrystalOnAKnockOutAndAHandsFreeRaiderPicksItUp()
        {
            World w = Make(7, 5);
            double dt = SimStep.Dt(w.Config);
            int id = Raider(w, TeamId.Azure);
            int j = Raider(w, TeamId.Crimson);
            Actor a = w.Actors[id];
            a.Carry = CarryKind.Corruption;
            a.CarryAmount = 1;
            Console.WriteLine($"[b13-c carrier] speedScale carrier {Corruption.SpeedScale(w, id)} non-carrier {Corruption.SpeedScale(w, j)} (expect 0.85, 1)");
            Assert.AreEqual(0.85, Corruption.SpeedScale(w, id));
            Assert.AreEqual(1.0, Corruption.SpeedScale(w, j));
            Corruption.CorruptionSystem(w, dt);
            double x = w.X[id], z = w.Z[id];
            a.Alive = false; // the knock-out (Damage empties the cargo)
            a.Carry = CarryKind.None;
            a.CarryAmount = 0;
            Corruption.CorruptionSystem(w, dt);
            int slot = Array.IndexOf(w.Corrupt.DropActive, true);
            Console.WriteLine($"[b13-c drop] knock-out at ({x:0.0},{z:0.0}): loose crystal slot {slot} units {(slot >= 0 ? w.Corrupt.DropUnits[slot] : 0)} ttl {(slot >= 0 ? w.Corrupt.DropTtl[slot] : 0)} (expect 1 unit, about {Durations.T30(w.Config, 600)} ticks)");
            Assert.GreaterOrEqual(slot, 0);
            Assert.AreEqual(1, w.Corrupt.DropUnits[slot]);
            Place(w, j, x + 0.5, z);
            Corruption.CorruptionSystem(w, dt);
            Console.WriteLine($"[b13-c pickup] crimson raider 0.5 u away: carrying {w.Actors[j].CarryAmount} {w.Actors[j].Carry}, slot active {w.Corrupt.DropActive[slot]} (expect 1 Corruption, false)");
            Assert.AreEqual(1, w.Actors[j].CarryAmount);
            Assert.AreEqual(CarryKind.Corruption, w.Actors[j].Carry);
            Assert.IsFalse(w.Corrupt.DropActive[slot]);
        }

        [Test]
        public void ClassicPhysicsOnlyAndRuleOffKeepTodaysInstabilityLine()
        {
            var worlds = new (string name, World w)[]
            {
                ("Classic", Make(7, 5, null, GameConfig.CreateClassic())),
                ("PhysicsOnly", Make(7, 5, null, GameConfig.CreateDefault().ApplyRules(RulesProfile.PhysicsOnly))),
                ("Full, Corruption.On false", Make(7, 5, c => c.Depth.Corruption.On = false)),
            };
            foreach (var (name, w) in worlds)
            {
                KaijuFeedConfig feed = w.Config.KaijuFeed;
                SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Corruption, 1, -1);
                double line = SimResources.BerserkLine(w);
                Console.WriteLine($"[b13-c old rule] {name}: rule on {Corruption.On(w)}, 1 corruption unit -> instability {w.Match.Instability} (expect {feed.CorruptionInstabilityPerUnit}), allegiance {w.Match.Allegiance.Azure:0.###} (expect > 0), berserk line {line} (finite), pool {Corruption.Pool(w)} (expect 0)");
                Assert.IsFalse(Corruption.On(w));
                Assert.AreEqual(feed.CorruptionInstabilityPerUnit, w.Match.Instability);
                Assert.Greater(w.Match.Allegiance.Azure, 0);
                Assert.IsFalse(double.IsInfinity(line));
                Assert.AreEqual(0, Corruption.Pool(w));
                Assert.AreEqual(0, Corruption.Chance(w));
                Assert.AreEqual(-1, Corruption.NextRollTick(w));
                Assert.AreEqual(KaijuState.Azure, Corruption.AtHatch(w, KaijuState.Azure));
            }
        }

        [Test]
        public void ZeroAllocationOver300TicksWithCarriersAndSimCostOnVsOff()
        {
            double onUs = 0, offUs = 0;
            long onBytes = 0, offBytes = 0;
            foreach (bool on in new[] { true, false })
            {
                World w = Make(4242, 16, c => c.Depth.Corruption.On = on);
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 600; t++)
                {
                    if (t == 30) w.ForceHatch = true;   // b13 r3: measure after the hatch, where the base sim allocates nothing (K's window)
                    if (t == 40) w.ForceHatch = false;
                    if (t % 100 == 0) GiveCrystals(w);
                    if (t == 300 && w.Match.Hatched) { w.Kaiju.Hp = 0; w.Kaiju.KoIn = 12; }   // a downed Kaiju: feed, guard and purify paths run once
                    MatchTick.Advance(w, null, scratch);
                }
                GiveCrystals(w);
                for (int t = 0; t < 30; t++) MatchTick.Advance(w, null, scratch);
                GiveCrystals(w);
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch sw = Stopwatch.StartNew();
                for (int t = 0; t < 300; t++) MatchTick.Advance(w, null, scratch);
                sw.Stop();
                long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                double us = sw.Elapsed.TotalMilliseconds * 1000.0 / 300;
                if (on) { onUs = us; onBytes = bytes; } else { offUs = us; offBytes = bytes; }
                Console.WriteLine($"[b13-c alloc] 16v16 seed 4242 Full, rule {(on ? "on" : "off")}: 300 ticks after 600 warm-up with carriers: {bytes} B allocated, {us:0.0} us per tick (limit: 0 B)");
            }
            Console.WriteLine($"[b13-c cost] mean sim us per tick on {onUs:0.0} vs off {offUs:0.0}: {(offUs > 0 ? (onUs / offUs - 1) * 100 : 0):+0.0;-0.0}% (limit +10%; printed, timing is not asserted on shared runners)");
            Console.WriteLine($"[b13-c alloc] rule's own allocation (on - off): {onBytes - offBytes} B (limit 0 B)");
            Assert.LessOrEqual(onBytes, offBytes, "the corruption rule adds no allocation over the same-seed control");
            Assert.AreEqual(0, onBytes, "0 B in the post-hatch window");
        }

        /// <summary>
        /// b13 r3 correctness probe (round 1: SimPhysicsSoakTests seed 101 "actor inside the Kaiju" at Normal and Hard with the rule
        /// on): the same soak config and seed, rule on and off, printing every grounded living actor whose centre is inside the
        /// hatched Kaiju's body (what it was doing, where the Kaiju and the actor came from, blocked cells around it).
        /// </summary>
        [Test, Category("Slow")]
        public void NobodyEndsInsideTheKaijuInTheSoakSeedWithTheRuleOnAndOff()
        {
            int onCount = 0;
            foreach (BotDifficulty d in new[] { BotDifficulty.Normal, BotDifficulty.Hard })
            foreach (bool on in new[] { true, false })
            {
                GameConfig cfg = SimPhysicsSoakTests.SixClassMix(false);
                cfg.Depth.Corruption.On = on;
                World w = World.CreateWorld(new WorldOptions
                {
                    Seed = 101, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
                    BotDifficulty = d, AllyBotDifficulty = d,
                });
                InputFrame scratch = SimInput.EmptyInput();
                int n = w.ActorCount;
                var px = new double[n];
                var pz = new double[n];
                double kpx = w.Kaiju.X, kpz = w.Kaiju.Z;
                double body = cfg.Physics.KaijuBodyRadius;
                int max = (int)(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz, count = 0, printed = 0;
                for (int s = 0; s < max && w.Match.Phase != MatchPhase.Ended; s++)
                {
                    for (int id = 0; id < n; id++) { px[id] = w.X[id]; pz[id] = w.Z[id]; }
                    kpx = w.Kaiju.X;
                    kpz = w.Kaiju.Z;
                    MatchTick.Advance(w, null, scratch);
                    Kaiju k = w.Kaiju;
                    if (k.State == KaijuState.Egg) continue;
                    for (int id = 0; id < n; id++)
                    {
                        Actor a = w.Actors[id];
                        if (!a.Alive || a.AirLen > 0) continue;
                        double dx = w.X[id] - k.X, dz = w.Z[id] - k.Z;
                        if (dx * dx + dz * dz >= body * body) continue;
                        count++;
                        if (printed >= 20) continue;
                        printed++;
                        int blocked = 0;
                        for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                            if ((ox != 0 || oz != 0) && !Terraform.WalkableAt(w.Map, w.X[id] + ox, w.Z[id] + oz)) blocked++;
                        double pdx = px[id] - kpx, pdz = pz[id] - kpz;
                        Console.WriteLine($"[b13-c inside] rule {(on ? "on" : "off")} {d} tick {w.Tick}: actor {id} {a.Cls} {a.Team} state {a.AiState} carry {a.Carry} x{a.CarryAmount} standUp {a.StandUp:0.00} at ({w.X[id]:0.00}, {w.Z[id]:0.00}) dist {Math.Sqrt(dx * dx + dz * dz):0.000} (last tick {Math.Sqrt(pdx * pdx + pdz * pdz):0.000}); Kaiju {k.State} koIn {k.KoIn:0.00} wild {Corruption.Wild(w)} target {k.TargetId} at ({k.X:0.00}, {k.Z:0.00}) moved {Math.Sqrt((k.X - kpx) * (k.X - kpx) + (k.Z - kpz) * (k.Z - kpz)):0.000}; actor moved {Math.Sqrt((w.X[id] - px[id]) * (w.X[id] - px[id]) + (w.Z[id] - pz[id]) * (w.Z[id] - pz[id])):0.000}; blocked cells around {blocked}/8");
                    }
                }
                Console.WriteLine($"[b13-c inside] rule {(on ? "on" : "off")} {d} seed 101 16v16: {count} actor-ticks inside the Kaiju body (limit 0 with the rule on); wild {Corruption.Wild(w)} rolls {w.Corrupt.RollIndex}");
                if (on) onCount += count;
            }
            Assert.AreEqual(0, onCount, "with the corruption rule on nobody ends a tick inside the Kaiju");
        }

        static void GiveCrystals(World w)
        {
            int given = 0;
            for (int i = 0; i < w.ActorCount && given < 6; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Cls == ClassId.Healer || a.Carry != CarryKind.None || a.CarryAmount > 0 || a.CarryMeat > 0) continue;
                a.Carry = CarryKind.Corruption;
                a.CarryAmount = 1;
                given++;
            }
        }

        // ------------------------------------------------------------------------------------------------ balance

        sealed class Arm
        {
            public string Name;
            public int Matches, AzureWins, CrimsonWins, Rolls, Successes, SuccessMatches;
            public double MaxChance;
            public int WildDecided, WildBehindWins;
            public int TrailMatches, TrailWins;
            public int[] Mined = new int[2];
            public readonly List<double> FirstKaijuKo = new List<double>();
            public readonly List<double> FirstPlayerKo = new List<double>();
            public double Kills, Minutes;
            public double[] ClassKills = new double[6];
            public int LeadChanges;
            public readonly List<string> RollLines = new List<string>();
        }

        static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
        }

        static void RunMatch(Arm arm, int seed, int size, bool on, out bool success)
        {
            World w = Make(seed, size, c => c.Depth.Corruption.On = on);
            InputFrame scratch = SimInput.EmptyInput();
            int head = w.EventHead;
            int maxSteps = (int)Math.Ceiling(w.Config.MatchSeconds * w.Config.SimHz) + (int)w.Config.SimHz;
            double firstPlayerKo = -1, firstKaijuKo = -1;
            int trailingAt4 = -2, behindAtWild = -2, lastSign = 0, hz = (int)w.Config.SimHz, lc0 = arm.LeadChanges;
            bool wasKo = false;
            success = false;
            for (int s = 0; s < maxSteps && w.Match.Phase != MatchPhase.Ended; s++)
            {
                MatchTick.Advance(w, null, scratch);
                MatchState m = w.Match;
                int first = Math.Max(head, w.EventHead - World.EventCapacity);
                for (int e = first; e < w.EventHead; e++)
                {
                    SimEvent ev = w.Events[e % World.EventCapacity];
                    if (ev.Type == SimEventType.Death && firstPlayerKo < 0) firstPlayerKo = m.Time;
                    else if (ev.Type == SimEventType.Gather && ev.B >= 0 && ev.B < w.Nodes.Length && w.Nodes[ev.B].Kind == NodeKind.Corruption && ev.A >= 0)
                        arm.Mined[(int)w.Actors[ev.A].Team]++;
                    else if (ev.Type == SimEventType.CorruptionRoll)
                    {
                        arm.Rolls++;
                        if (ev.Value > arm.MaxChance) arm.MaxChance = ev.Value;
                        int lead = Corruption.LeadingTeam(w);
                        int behind = lead < 0 ? -1 : 1 - lead;
                        if (ev.B == 1) { arm.Successes++; success = true; behindAtWild = behind; }
                        arm.RollLines.Add($"seed {seed} {size}v{size} t {m.Time:0} chance {ev.Value:0.000} {(ev.B == 1 ? "WILD" : "fail")} behind {(behind == 0 ? "azure" : behind == 1 ? "crimson" : "level")}");
                    }
                }
                head = w.EventHead;
                bool ko = m.Hatched && w.Kaiju.KoIn > 0;
                if (ko && !wasKo && firstKaijuKo < 0) firstKaijuKo = m.Time;
                wasKo = ko;
                if (w.Tick % hz == 0)
                {
                    double d = m.HillSeconds.Azure - m.HillSeconds.Crimson;
                    int sign = d > 0 ? 1 : d < 0 ? -1 : 0;
                    if (sign != 0 && lastSign != 0 && sign != lastSign) arm.LeadChanges++;
                    if (sign != 0) lastSign = sign;
                    if (trailingAt4 == -2 && m.Time >= 240) trailingAt4 = sign > 0 ? 1 : sign < 0 ? 0 : -1;
                }
            }
            MatchState fm = w.Match;
            arm.Matches++;
            int winner = fm.Winner == MatchResult.Azure ? 0 : fm.Winner == MatchResult.Crimson ? 1 : -1;
            if (winner == 0) arm.AzureWins++;
            if (winner == 1) arm.CrimsonWins++;
            if (success) arm.SuccessMatches++;
            if (success && behindAtWild >= 0) { arm.WildDecided++; if (winner == behindAtWild) arm.WildBehindWins++; }
            if (trailingAt4 >= 0) { arm.TrailMatches++; if (winner == trailingAt4) arm.TrailWins++; }
            if (firstPlayerKo >= 0) arm.FirstPlayerKo.Add(firstPlayerKo);
            if (firstKaijuKo >= 0) arm.FirstKaijuKo.Add(firstKaijuKo);
            for (int i = 0; i < w.ActorCount; i++)
            {
                arm.Kills += w.Actors[i].Kills;
                int cls = (int)w.Actors[i].Cls;
                if (cls >= 0 && cls < 6) arm.ClassKills[cls] += w.Actors[i].Kills;
            }
            arm.Minutes += fm.Time / 60.0;
            arm.RollLines.Add($"match {size}v{size} seed {seed}: winner {(winner == 0 ? "azure" : winner == 1 ? "crimson" : "draw")}, behind at 4:00 {(trailingAt4 == 0 ? "azure" : trailingAt4 == 1 ? "crimson" : "level")}, lead changes {arm.LeadChanges - lc0}, wild {(success ? "yes" : "no")}, crystals mined {w.Corrupt.Mined[0]}/{w.Corrupt.Fed[0]} fed azure, {w.Corrupt.Mined[1]}/{w.Corrupt.Fed[1]} fed crimson, hill {fm.HillSeconds.Azure:0}/{fm.HillSeconds.Crimson:0}");
        }

        [Test]
        [Category("Balance")]
        public void BalanceAgainstTheSameSeedControlOver40Seeds()
        {
            var on = new Arm { Name = "on" };
            var off = new Arm { Name = "control" };
            var table = new List<string>();
            foreach (int size in new[] { 5, 16 })
            {
                for (int seed = 1; seed <= 20; seed++)
                {
                    RunMatch(on, seed, size, true, out bool okOn);
                    RunMatch(off, seed, size, false, out _);
                    table.Add($"{size}v{size} seed {seed}: {(okOn ? "WILD" : "-")}");
                }
            }
            foreach (Arm a in new[] { on, off })
            {
                double total = Math.Max(1, a.Kills);
                string cls = "";
                for (int c = 0; c < 6; c++) cls += $" {(ClassId)c} {a.ClassKills[c] / total:0.000}";
                Console.WriteLine($"[b13-c balance] {a.Name}: matches {a.Matches} azure wins {a.AzureWins} crimson wins {a.CrimsonWins}; rolls {a.Rolls} successes {a.Successes} matches wild {a.SuccessMatches}; max chance {a.MaxChance:0.000}; wild decided {a.WildDecided} behind-at-roll wins {a.WildBehindWins}; trailing at 4:00 {a.TrailMatches} wins {a.TrailWins}; crystals mined azure {a.Mined[0]} crimson {a.Mined[1]}; median first Kaiju KO {Median(a.FirstKaijuKo):0.0} s ({a.FirstKaijuKo.Count} matches); median first player KO {Median(a.FirstPlayerKo):0.0} s; kills/min {a.Kills / Math.Max(1e-9, a.Minutes):0.00}; hill lead changes {a.LeadChanges}; class kill shares{cls}");
            }
            foreach (string l in on.RollLines) Console.WriteLine("[b13-c balance roll] " + l);
            Console.WriteLine("[b13-c balance success table] " + string.Join(" | ", table));

            double successShare = on.SuccessMatches / 40.0;
            double wildBehind = on.WildDecided > 0 ? (double)on.WildBehindWins / on.WildDecided : double.NaN;
            double trailOn = on.TrailMatches > 0 ? (double)on.TrailWins / on.TrailMatches : 0;
            double trailOff = off.TrailMatches > 0 ? (double)off.TrailWins / off.TrailMatches : 0;
            double winOn = on.AzureWins / 40.0, winOff = off.AzureWins / 40.0;
            double koOn = Median(on.FirstPlayerKo), koOff = Median(off.FirstPlayerKo);
            double kpmOn = on.Kills / Math.Max(1e-9, on.Minutes), kpmOff = off.Kills / Math.Max(1e-9, off.Minutes);
            double lcRatio = off.LeadChanges > 0 ? (double)on.LeadChanges / off.LeadChanges : 1;
            double worstClass = 0;
            for (int c = 0; c < 6; c++)
                worstClass = Math.Max(worstClass, Math.Abs(on.ClassKills[c] / Math.Max(1, on.Kills) - off.ClassKills[c] / Math.Max(1, off.Kills)));

            var fails = new List<string>();
            void Limit(string what, bool pass)
            {
                Console.WriteLine($"[b13-c limit] {what}: {(pass ? "PASS" : "FAIL")}");
                if (!pass) fails.Add(what);
            }
            Limit($"matches with a successful roll {on.SuccessMatches}/40 = {successShare:0.000} (limit 0.10..0.35)", successShare >= 0.10 && successShare <= 0.35);
            Limit($"highest chance rolled {on.MaxChance:0.000} (limit <= 0.70)", on.MaxChance <= 0.70 + 1e-12);
            Limit($"wild matches: the team behind at the roll wins {on.WildBehindWins}/{on.WildDecided} = {wildBehind:0.000} (limit 0.25..0.55)", on.WildDecided > 0 && wildBehind >= 0.25 && wildBehind <= 0.55);
            Limit($"trailing team (behind at 4:00) win share on {trailOn:0.000} vs control {trailOff:0.000}: {trailOn - trailOff:+0.000;-0.000} (limit >= +0.03)", trailOn - trailOff >= 0.03 - 1e-12);
            Limit($"azure win share on {winOn:0.000} vs control {winOff:0.000} (limit within 0.15)", Math.Abs(winOn - winOff) <= 0.15 + 1e-12);
            Limit($"median first player knock-out on {koOn:0.0} s vs control {koOff:0.0} s (limit -10% / +20%)", koOn >= koOff * 0.90 && koOn <= koOff * 1.20);
            Limit($"kills per minute on {kpmOn:0.00} vs control {kpmOff:0.00} (limit -20% / +15%)", kpmOn >= kpmOff * 0.80 && kpmOn <= kpmOff * 1.15);
            Limit($"worst class kill share change {worstClass:0.000} (limit 0.08)", worstClass <= 0.08 + 1e-12);
            Limit($"hill lead changes on {on.LeadChanges} vs control {off.LeadChanges} (limit +-30%)", lcRatio >= 0.70 && lcRatio <= 1.30);
            Console.WriteLine("[b13-c balance] lesson 7 (MovementShiverTests Kaiju hill seed 1337 near share, NoShiverInTwoMinutes 16v16) print in the same suite run.");
            Assert.IsEmpty(fails, "b13-c balance limits failed: " + string.Join("; ", fails));
        }
    }
}
