using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/cleanse.test.ts (browser branch cm/CM-36, 1c6ce00): the shrine cleanse. Every time is sim ticks.
    /// The browser read the HUD copy (writeHudSnapshot); here the same values are read from the sim state the HUD reads
    /// (Match.Instability, World.Cleanse). With CM-64 merged, the delivering press in the hold case lasts the wind-up.
    /// </summary>
    [TestFixture]
    public sealed class CleanseTests
    {
        private static World Mk(int seed = 1)
        {
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateBrowser5v5() });
        }

        private static CleanseConfig CL(World w) => w.Config.KaijuFeed.Cleanse;

        private static double Amount(World w) => CL(w).Units * w.Config.KaijuFeed.CorruptionInstabilityPerUnit;

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

        private static void Steps(World w, double n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        private static bool PlaceAtShrine(World w, int id)
        {
            double sx = w.Map.Shrine.X;
            double sz = w.Map.Shrine.Z;
            double lim = w.Config.KaijuFeed.ShrineInteractRange - 0.5;
            for (int ring = 1; ring <= 3; ring++)
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        double x = Math.Floor(sx + dx) + 0.5;
                        double z = Math.Floor(sz + dz) + 0.5;
                        double ex = x - sx;
                        double ez = z - sz;
                        if (ex * ex + ez * ez > lim * lim || !MapGrid.IsWalkable(w.Map, (int)Math.Floor(x), (int)Math.Floor(z))) continue;
                        w.X[id] = x;
                        w.Z[id] = z;
                        w.PrevX[id] = x;
                        w.PrevZ[id] = z;
                        return true;
                    }
            return false;
        }

        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private static void StepHuman(World w, bool interact)
        {
            Frame.MoveX = 0;
            Frame.MoveZ = 0;
            Frame.Attack = false;
            Frame.Ability = false;
            Frame.Interact = interact;
            Frame.AimX = w.X[0];
            Frame.AimZ = w.Z[0];
            SimInput.ApplyInput(w, 0, Frame);
            SimStep.Step(w);
        }

        [Test]
        public void DrainsAWholeNumberOfCorruptionUnitsWorthOverTicksWithATickCooldownAndAShortHold()
        {
            World w = Mk();
            CleanseConfig cl = CL(w);
            Assert.AreEqual(Amount(w), Cleanse.CleanseAmount(w.Config));
            Assert.AreEqual(18, Amount(w));
            Assert.IsTrue(cl.Ticks == Math.Floor(cl.Ticks) && cl.Ticks > 0);
            Assert.IsTrue(cl.CooldownTicks == Math.Floor(cl.CooldownTicks) && cl.CooldownTicks > cl.Ticks);
            Assert.IsTrue(cl.HoldTicks == Math.Floor(cl.HoldTicks) && cl.HoldTicks > 0);
            Assert.Less(Amount(w), w.Config.KaijuFeed.BerserkThreshold);
            Assert.Less(BotFsm.CleanseUtility, 0.9);
            Assert.Greater(BotFsm.CleanseUtility, 0.8);
        }

        [Test]
        public void LowersInstabilityByTheConfiguredAmountOverTheConfiguredTicksAndEmitsTheCue()
        {
            World w = Mk();
            CleanseConfig cl = CL(w);
            double amount = Amount(w);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 6);
            double start = 6 * w.Config.KaijuFeed.CorruptionInstabilityPerUnit;
            Assert.AreEqual(start, w.Match.Instability);

            int head = w.EventHead;
            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 0));
            List<SimEvent> cues = Events(w, SimEventType.Cleanse);
            Assert.AreEqual(head + 1, w.EventHead);
            Assert.AreEqual(1, cues.Count);
            Assert.AreEqual(-1, cues[0].A);
            Assert.AreEqual(0, cues[0].B);
            Assert.AreEqual(w.Map.Shrine.X, cues[0].X);
            Assert.AreEqual(w.Map.Shrine.Z, cues[0].Z);
            Assert.AreEqual(amount, cues[0].Value, 1e-9);
            Assert.AreEqual(cl.Ticks, w.Cleanse.ActiveTicks);

            int half = (int)Math.Floor(cl.Ticks / 2);
            Steps(w, half);
            Assert.AreEqual(start - amount * half / cl.Ticks, w.Match.Instability, 1e-6);
            Assert.AreEqual(cl.Ticks - half, w.Cleanse.ActiveTicks);
            Assert.AreEqual(0, Events(w, SimEventType.CleanseEnd).Count);

            Steps(w, cl.Ticks - half - 1);
            Assert.Greater(w.Match.Instability, start - amount + 1e-9);
            Assert.AreEqual(1, w.Cleanse.ActiveTicks);

            Steps(w, 1);
            Assert.AreEqual(start - amount, w.Match.Instability, 1e-6);
            Assert.AreEqual(0, w.Cleanse.ActiveTicks);
            List<SimEvent> ends = Events(w, SimEventType.CleanseEnd);
            Assert.AreEqual(1, ends.Count);
            Assert.AreEqual(0, ends[0].B);
            Assert.AreEqual(amount, ends[0].Value, 1e-6);
            Assert.AreEqual(amount, w.Cleanse.Removed, 1e-6);

            Steps(w, 30);
            Assert.AreEqual(start - amount, w.Match.Instability, 1e-6);
        }

        [Test]
        public void ClampsAt0AndRecordsOnlyWhatItDrained()
        {
            World w = Mk(2);
            double per = w.Config.KaijuFeed.CorruptionInstabilityPerUnit;
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Corruption, 1);
            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 1));
            Steps(w, CL(w).Ticks);
            Assert.AreEqual(0, w.Match.Instability);
            Assert.AreEqual(per, w.Cleanse.Removed, 1e-6);
            Assert.AreEqual(per, Events(w, SimEventType.CleanseEnd)[0].Value, 1e-6);
        }

        [Test]
        public void TheCleansingTeamWaitsCooldownTicksFromTheStartTheOtherTeamHasItsOwnTimerOneCleanseAtATime()
        {
            World w = Mk(3);
            CleanseConfig cl = CL(w);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 8);
            Assert.IsTrue(Cleanse.CleanseReady(w, 0));
            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 0));
            Assert.AreEqual(cl.CooldownTicks, w.Cleanse.Cooldown[0]);
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 1));
            Steps(w, cl.Ticks);
            Assert.AreEqual(0, w.Cleanse.ActiveTicks);
            Assert.AreEqual(cl.CooldownTicks - cl.Ticks, w.Cleanse.Cooldown[0]);
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 0));
            Assert.IsFalse(Cleanse.CleanseReady(w, 0));

            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 1));
            Steps(w, cl.Ticks);

            Steps(w, cl.CooldownTicks - 2 * cl.Ticks - 1);
            Assert.AreEqual(1, w.Cleanse.Cooldown[0]);
            Assert.IsFalse(Cleanse.CleanseReady(w, 0));
            Steps(w, 1);
            Assert.AreEqual(0, w.Cleanse.Cooldown[0]);
            Assert.AreEqual(8 - 2 * cl.Units, Cleanse.NetCorruptionUnits(w));
            Assert.IsTrue(Cleanse.CleanseReady(w, 0));
            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 0));
            Assert.AreEqual(3, Events(w, SimEventType.Cleanse).Count);
        }

        [Test]
        public void AnEmptyHandedActorHoldingInteractForHoldTicksStartsItThePressThatDeliversNeverDoes()
        {
            World w = Mk(4);
            CleanseConfig cl = CL(w);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 6);
            Assert.IsTrue(PlaceAtShrine(w, 0));
            Actor a = w.Actors[0];
            Assert.IsTrue(a.Alive);
            a.StandUp = 0;

            // Cargo in hand: the held press delivers it (after the CM-64 wind-up), then keeps being held: no cleanse.
            a.Carry = CarryKind.Biomass;
            a.CarryAmount = 1;
            for (int t = 0; t < Delivery.WindupTicks(w.Config) + cl.HoldTicks + 5; t++) StepHuman(w, true);
            Assert.AreEqual(1, w.Match.Delivered[(int)TeamId.Azure].Biomass);
            Assert.AreEqual(0, a.CarryAmount);
            Assert.AreEqual(0, Events(w, SimEventType.Cleanse).Count);

            StepHuman(w, false);
            for (int t = 0; t < cl.HoldTicks - 1; t++) StepHuman(w, true);
            Assert.AreEqual(0, Events(w, SimEventType.Cleanse).Count);
            StepHuman(w, true);
            List<SimEvent> cues = Events(w, SimEventType.Cleanse);
            Assert.AreEqual(1, cues.Count);
            Assert.AreEqual(0, cues[0].A);
            Assert.AreEqual(0, cues[0].B);
            Assert.AreEqual(cl.Ticks - 1, w.Cleanse.ActiveTicks);

            for (int t = 0; t < cl.HoldTicks * 2; t++) StepHuman(w, true);
            Assert.AreEqual(1, Events(w, SimEventType.Cleanse).Count);
        }

        [Test]
        public void OutsideShrineRangeTheHoldNeverCounts()
        {
            World w = Mk(5);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 6);
            double far = w.Config.KaijuFeed.ShrineInteractRange + 4;
            w.X[0] = w.Map.Shrine.X + far;
            w.Z[0] = w.Map.Shrine.Z + far;
            StepHuman(w, false);
            for (int t = 0; t < CL(w).HoldTicks * 3; t++) StepHuman(w, true);
            Assert.AreEqual(0, Events(w, SimEventType.Cleanse).Count);
        }

        [Test]
        public void NeedsCorruptionInTheEgg()
        {
            World w = Mk(6);
            Assert.AreEqual(0, Cleanse.NetCorruptionUnits(w));
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 0));
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 3);
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 0));
        }

        [Test]
        public void NeverAfterTheBerserkLock()
        {
            World w = Mk(7);
            KaijuFeedConfig feed = w.Config.KaijuFeed;
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, Math.Ceiling(feed.BerserkThreshold / feed.CorruptionInstabilityPerUnit));
            Assert.IsTrue(w.Match.BerserkLocked);
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 0));
        }

        [Test]
        public void TheHatchStopsACleanseInFlightWithACleanseEndAndNoneStartsAfterIt()
        {
            World w = Mk(8);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 5);
            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 1));
            w.ForceHatch = true;
            Steps(w, 2);
            Assert.IsTrue(w.Match.Hatched);
            Assert.AreEqual(0, w.Cleanse.ActiveTicks);
            List<SimEvent> ends = Events(w, SimEventType.CleanseEnd);
            Assert.AreEqual(1, ends.Count);
            Assert.AreEqual(Amount(w) / CL(w).Ticks, ends[0].Value, 1e-6);
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 0));
        }

        [Test]
        public void AnInvalidTeamIndexIsRefusedNeverThrown()
        {
            World w = Mk(9);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 5);
            int head = w.EventHead;
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, 2));
            Assert.IsFalse(Cleanse.StartCleanse(w, -1, -1));
            Assert.AreEqual(head, w.EventHead);
            Assert.AreEqual(0, w.Cleanse.Count);
        }

        [Test]
        public void ResetWorldClearsEveryCleanseTimer()
        {
            World w = Mk(10);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 5);
            Assert.IsTrue(Cleanse.StartCleanse(w, -1, 0));
            Steps(w, 3);
            World.ResetWorld(w, w.Opts);
            Assert.AreEqual(0, w.Cleanse.ActiveTicks);
            Assert.AreEqual(0, w.Cleanse.Cooldown[0]);
            Assert.AreEqual(0, w.Cleanse.Cooldown[1]);
            Assert.AreEqual(0, w.Cleanse.Count);
            Assert.AreEqual(0, w.Cleanse.Removed);
        }

        private static World SetUp(TeamId lead)
        {
            World w = Mk(11);
            SimResources.ApplyDelivery(w, lead, CarryKind.Biomass, 4);
            SimResources.ApplyDelivery(w, lead == TeamId.Azure ? TeamId.Crimson : TeamId.Azure, CarryKind.Biomass, 1);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, CL(w).BotNetUnits);
            Assert.AreEqual(CL(w).BotNetUnits, Cleanse.NetCorruptionUnits(w));
            Assert.IsFalse(w.Match.BerserkLocked);
            return w;
        }

        [Test]
        public void PureBotsOfTheBiomassLeaderNeverGoCleanseTheHumanSlotOnAutopilotDoes()
        {
            World w = SetUp(TeamId.Azure);
            Assert.IsTrue(Cleanse.CleanseReady(w, 0));
            BotsState st = BotBrain.GetBotsState(w);
            int pure = 0;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (a.Team != TeamId.Azure || a.IsHuman) continue;
                st.Role[id] = BotsState.RoleKaiju;
                st.Corrupt[id] = 0;
                BotFsm.ThinkBot(w, st, id);
                Assert.AreNotEqual(AiState.Cleanse, a.AiState, "bot " + id);
                pure++;
            }
            Assert.Greater(pure, 0);
            Assert.IsTrue(w.Actors[0].IsHuman);
            st.Role[0] = BotsState.RoleHill;
            st.Corrupt[0] = 0;
            BotFsm.ThinkBot(w, st, 0);
            Assert.AreEqual(AiState.Cleanse, w.Actors[0].AiState);
        }

        [Test]
        public void TheAutopilotNeverCleansesForATeamThatTrailsBiomass()
        {
            World w = SetUp(TeamId.Crimson);
            BotsState st = BotBrain.GetBotsState(w);
            st.Role[0] = BotsState.RoleHill;
            st.Corrupt[0] = 0;
            BotFsm.ThinkBot(w, st, 0);
            Assert.AreNotEqual(AiState.Cleanse, w.Actors[0].AiState);
        }
    }
}
