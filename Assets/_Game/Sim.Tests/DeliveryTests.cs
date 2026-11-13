using System;
using System.Collections.Generic;
using System.Text;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/delivery.test.ts (browser branch cm/CM-64, e056d8e): contestable deliveries. Every time is sim ticks.
    /// An unhit carrier delivers on exactly the DeliverWindupTicks-th step; an enemy hit during the wind-up loses that
    /// delivery (one DeliverIntercepted, MatchState.Intercepted, a full fresh wind-up); friendly hits, hits outside a wind-up
    /// and letting go log nothing; defender bots' intercept target; the director keeps a dead feeder's duty.
    /// </summary>
    [TestFixture]
    public sealed class DeliveryTests
    {
        private static World MakeWorld()
        {
            return World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateBrowser5v5() });
        }

        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Actors[id].SpawnProtect = 0;
        }

        private static void Hold(World w, int id, bool interact)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = w.X[id];
            f.AimZ = w.Z[id];
            f.Interact = interact;
            SimInput.ApplyInput(w, id, f);
        }

        private static void StepN(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            for (int i = Math.Max(0, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return output;
        }

        private static int BotOf(World w, TeamId team, int not = -1)
        {
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team == team && i != not) return i;
            throw new InvalidOperationException("no " + team + " bot");
        }

        private static int CarrierAtShrine(World w)
        {
            int id = BotOf(w, TeamId.Azure);
            Actor a = w.Actors[id];
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 3;
            Place(w, id, w.Map.Shrine.X, w.Map.Shrine.Z + 2.3);
            Hold(w, id, true);
            return id;
        }

        private static int EnemyNear(World w, int carrier)
        {
            int e = BotOf(w, TeamId.Crimson);
            Place(w, e, w.X[carrier] + 1, w.Z[carrier]);
            return e;
        }

        [Test]
        public void TheWindupIsTheConfiguredWholeNumberOfTicksAndIsShorterThanTheFastestSwingU056()
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            int windup = Delivery.WindupTicks(c);
            Assert.AreEqual(c.KaijuFeed.DeliverWindupTicks, windup);
            Assert.Greater(windup, 1);
            double fastest = double.PositiveInfinity;
            foreach (ClassConfig cc in c.Classes) fastest = Math.Min(fastest, JsMath.Round(cc.AttackCooldown * c.SimHz));
            // U-056: the browser's 30 outlasted every swing, so one defender could pin a carrier at the egg until it died (the
            // DG-09 soak's 'stationary carrier'). Shorter than the fastest swing: a lone defender delays, it never pins.
            Assert.Less(windup, fastest);
        }

        [Test]
        public void AnUnhitCarrierDeliversAfterExactlyDeliverWindupTicksStepsInTheZoneWithEHeld()
        {
            World w = MakeWorld();
            int W = Delivery.WindupTicks(w.Config);
            int id = CarrierAtShrine(w);
            Actor a = w.Actors[id];
            StepN(w, W - 1);
            Assert.AreEqual(W - 1, a.DeliverTicks);
            Assert.AreEqual(3, a.CarryAmount);
            Assert.AreEqual(0, w.Match.Allegiance.Azure);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
            SimStep.Step(w);
            List<SimEvent> del = Events(w, SimEventType.DeliverBiomass);
            Assert.AreEqual(1, del.Count);
            Assert.AreEqual(id, del[0].A);
            Assert.AreEqual(3, del[0].Value);
            Assert.AreEqual(3 * w.Config.KaijuFeed.BiomassAllegiancePerUnit, w.Match.Allegiance.Azure);
            Assert.AreEqual(3, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(CarryKind.None, a.Carry);
            Assert.AreEqual(0, a.DeliverTicks);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverIntercepted).Count);
            Assert.AreEqual(0, w.Match.Intercepted.Azure);
        }

        [Test]
        public void LettingGoOfECancelsTheWindupQuietlyAndTheNextPressStartsItFromZero()
        {
            World w = MakeWorld();
            int W = Delivery.WindupTicks(w.Config);
            int id = CarrierAtShrine(w);
            Actor a = w.Actors[id];
            StepN(w, W - 1);
            Hold(w, id, false);
            SimStep.Step(w);
            Assert.AreEqual(0, a.DeliverTicks);
            Assert.AreEqual(3, a.CarryAmount);
            Hold(w, id, true);
            StepN(w, W - 1);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
            SimStep.Step(w);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverBiomass).Count);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverIntercepted).Count);
        }

        [Test]
        public void ACarrierOutsideTheZoneNeverWindsUp()
        {
            World w = MakeWorld();
            int W = Delivery.WindupTicks(w.Config);
            int id = CarrierAtShrine(w);
            Place(w, id, w.Map.Shrine.X, w.Map.Shrine.Z + w.Config.KaijuFeed.ShrineInteractRange + 0.5);
            StepN(w, W * 2);
            Assert.AreEqual(0, w.Actors[id].DeliverTicks);
            Assert.AreEqual(3, w.Actors[id].CarryAmount);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
        }

        [Test]
        public void AnEnemyHitDuringTheWindupLosesTheDeliveryAndAFullFreshWindupIsNeeded()
        {
            World w = MakeWorld();
            int W = Delivery.WindupTicks(w.Config);
            int id = CarrierAtShrine(w);
            Actor a = w.Actors[id];
            int enemy = EnemyNear(w, id);
            StepN(w, W - 1);
            Assert.Greater(Damage.ApplyDamage(w, id, 1, enemy, w.X[enemy], w.Z[enemy]), 0);
            Assert.IsTrue(a.Alive);
            Assert.AreEqual(Delivery.Interrupted, a.DeliverTicks);
            List<SimEvent> ev = Events(w, SimEventType.DeliverIntercepted);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(id, ev[0].A);
            Assert.AreEqual(enemy, ev[0].B);
            Assert.AreEqual(W - 1, ev[0].Value);
            Assert.AreEqual(w.X[id], ev[0].X);
            Assert.AreEqual(w.Z[id], ev[0].Z);
            Assert.AreEqual(1, w.Match.Intercepted.Azure);
            Assert.AreEqual(0, w.Match.Intercepted.Crimson);

            SimStep.Step(w);
            Assert.AreEqual(0, a.DeliverTicks);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
            Assert.AreEqual(0, w.Match.Allegiance.Azure);
            Assert.AreEqual(3, a.CarryAmount);

            StepN(w, W - 1);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
            SimStep.Step(w);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverBiomass).Count);
            Assert.AreEqual(3 * w.Config.KaijuFeed.BiomassAllegiancePerUnit, w.Match.Allegiance.Azure);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverIntercepted).Count);
        }

        [Test]
        public void AKillingBlowDuringTheWindupIsAnInterceptionAndTheLoadDiesWithTheCarrier()
        {
            World w = MakeWorld();
            int W = Delivery.WindupTicks(w.Config);
            int id = CarrierAtShrine(w);
            int enemy = EnemyNear(w, id);
            StepN(w, 10);
            Damage.ApplyDamage(w, id, 1e6, enemy, w.X[enemy], w.Z[enemy]);
            Assert.IsFalse(w.Actors[id].Alive);
            Assert.AreEqual(1, Events(w, SimEventType.DeliverIntercepted).Count);
            Assert.AreEqual(10, Events(w, SimEventType.DeliverIntercepted)[0].Value);
            Assert.AreEqual(1, w.Match.Intercepted.Azure);
            StepN(w, W * 2);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverBiomass).Count);
            Assert.AreEqual(0, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(0, w.Actors[id].DeliverTicks);
        }

        [Test]
        public void ATeammatesHitAHitBeforeTheWindupStartsAndASecondHitOnTheSameTickLogNothingMore()
        {
            World w = MakeWorld();
            int id = CarrierAtShrine(w);
            int mate = BotOf(w, TeamId.Azure, id);
            int enemy = EnemyNear(w, id);
            Damage.ApplyDamage(w, id, 1, enemy, w.X[enemy], w.Z[enemy]);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverIntercepted).Count);
            StepN(w, 5);
            Assert.AreEqual(0, Damage.ApplyDamage(w, id, 5, mate, w.X[id], w.Z[id]));
            Assert.AreEqual(5, w.Actors[id].DeliverTicks);
            Assert.AreEqual(0, Events(w, SimEventType.DeliverIntercepted).Count);
            Damage.ApplyDamage(w, id, 1, enemy, w.X[enemy], w.Z[enemy]);
            Assert.IsFalse(Delivery.InterruptDelivery(w, id, enemy));
            Assert.AreEqual(1, Events(w, SimEventType.DeliverIntercepted).Count);
            Assert.AreEqual(1, w.Match.Intercepted.Azure);
        }

        [Test]
        public void IsDeterministicTwoWorldsWithTheSameInputsLogTheSameInterceptionsOnTheSameTicks()
        {
            string Run()
            {
                World w = MakeWorld();
                int W = Delivery.WindupTicks(w.Config);
                int id = CarrierAtShrine(w);
                int enemy = EnemyNear(w, id);
                var log = new StringBuilder();
                for (int t = 0; t < W * 3; t++)
                {
                    if (t % 17 == 16) Damage.ApplyDamage(w, id, 1, enemy, w.X[enemy], w.Z[enemy]);
                    SimStep.Step(w);
                    log.Append(w.Tick).Append(':').Append(w.Actors[id].DeliverTicks).Append(':').Append(w.Match.Intercepted.Azure).Append(':').Append(w.Actors[id].CarryAmount).Append(',');
                }
                return log.ToString();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TargetsAnEnemyCarrierInsideInterceptShrineRadiusNotAnEmptyHandedEnemyNotFromFarAwayOrWhileCarrying()
        {
            World w = MakeWorld();
            int carrier = CarrierAtShrine(w);
            int defender = BotOf(w, TeamId.Crimson);
            double sx = w.Map.Shrine.X;
            double sz = w.Map.Shrine.Z;
            Place(w, defender, sx + 4, sz + 4);
            Assert.AreEqual(carrier, BotFsm.InterceptTarget(w, defender));

            Place(w, carrier, sx, sz + w.Config.Bots.InterceptShrineRadius + 1);
            Assert.AreEqual(-1, BotFsm.InterceptTarget(w, defender));
            Place(w, carrier, sx, sz + 2.3);

            w.Actors[carrier].CarryAmount = 0;
            w.Actors[carrier].Carry = CarryKind.None;
            Assert.AreEqual(-1, BotFsm.InterceptTarget(w, defender));
            w.Actors[carrier].CarryMeat = 1;
            Assert.AreEqual(carrier, BotFsm.InterceptTarget(w, defender));

            w.Actors[defender].Carry = CarryKind.Biomass;
            w.Actors[defender].CarryAmount = 1;
            Assert.AreEqual(-1, BotFsm.InterceptTarget(w, defender));
            w.Actors[defender].Carry = CarryKind.None;
            w.Actors[defender].CarryAmount = 0;

            Place(w, defender, w.Map.Hill.X, w.Map.Hill.Z);
            Assert.AreEqual(-1, BotFsm.InterceptTarget(w, defender));
        }

        [Test]
        public void AKaijuDutyBotMayInterceptTheHillCrewNeverLeavesTheHillForIt()
        {
            Assert.IsTrue(BotFsm.InterceptorRole(BotsState.RoleKaiju));
            Assert.IsFalse(BotFsm.InterceptorRole(BotsState.RoleHill));
        }

        private static void BotTicks(World w, int ticks)
        {
            InputFrame f = SimInput.EmptyInput();
            double dt = SimStep.Dt(w.Config);
            for (int t = 0; t < ticks; t++)
            {
                BotBrain.BotsThink(w, dt);
                for (int id = 1; id < w.ActorCount; id++)
                {
                    BotBrain.FillBotInput(w, id, f);
                    SimInput.ApplyInput(w, id, f);
                }
                SimStep.Step(w);
            }
        }

        [Test]
        public void AFeederKilledBeforeTheHatchIsNotReplacedByAHillBotWhileItRespawnsAndGetsItsDutyBack()
        {
            World w = MakeWorld();
            int hz = (int)w.Config.SimHz;
            BotTicks(w, 7 * hz);
            BotsState st = BotBrain.GetBotsState(w);
            var crimson = new List<int>();
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team == TeamId.Crimson) crimson.Add(i);
            List<int> feeders = crimson.FindAll(i => w.Actors[i].Alive && st.Role[i] == BotsState.RoleKaiju);
            Assert.AreEqual(1, feeders.Count);
            int feeder = feeders[0];
            Assert.AreEqual(0, BotDirector.DeadKaijuDuty(w, st, TeamId.Crimson));

            int killer = BotOf(w, TeamId.Azure);
            w.Actors[feeder].SpawnProtect = 0;
            Damage.ApplyDamage(w, feeder, 1e6, killer, w.X[killer], w.Z[killer]);
            Assert.IsFalse(w.Actors[feeder].Alive);

            BotTicks(w, 4 * hz);
            Assert.IsFalse(w.Actors[feeder].Alive);
            Assert.Greater(w.Config.Respawn.DelaySeconds, 4);
            Assert.AreEqual(1, BotDirector.DeadKaijuDuty(w, st, TeamId.Crimson));
            Assert.AreEqual(BotsState.RoleKaiju, st.Role[feeder]);
            foreach (int i in crimson) if (i != feeder && w.Actors[i].Alive) Assert.AreEqual(BotsState.RoleHill, st.Role[i], "bot " + i);

            BotTicks(w, 3 * hz);
            Assert.IsTrue(w.Actors[feeder].Alive);
            Assert.AreEqual(BotsState.RoleKaiju, st.Role[feeder]);
            Assert.AreEqual(0, BotDirector.DeadKaijuDuty(w, st, TeamId.Crimson));
        }
    }
}
