using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/kaiju.test.ts (09e0904): the "hatch" and "kaiju HP and KO" describes and the CM-26 melee-tell
    /// describe. 5v5, human azure warrior; roster: azure 0 warrior (human), 1 warrior, 2 farmer, 3 farmer, 4 miner;
    /// crimson 5 warrior, 6 warrior, 7 farmer, 8 farmer, 9 miner.
    /// </summary>
    [TestFixture]
    public sealed class KaijuHatchTests
    {
        private static readonly GameConfig Cfg = SimPhysicsTests.ClassicBrowser();
        private static readonly double Hz = Cfg.SimHz;
        private static readonly KaijuConfig K = Cfg.Kaiju;
        private static readonly KaijuFeedConfig Feed = Cfg.KaijuFeed;
        private const double ShrineZ = 8;

        private static WorldOptions Opts(int teamSize = 5)
        {
            return new WorldOptions
            {
                Seed = 7,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = SimPhysicsTests.ClassicBrowser(),
            };
        }

        private static World MakeWorld(int teamSize = 5)
        {
            return World.CreateWorld(Opts(teamSize));
        }

        /// <summary>Teleport an actor (no interpolation streak) and drop its spawn protection.</summary>
        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = JsMath.F32(x);
            w.PrevZ[id] = JsMath.F32(z);
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

        private static SimEvent Copy(SimEvent e)
        {
            return new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value };
        }

        private static List<SimEvent> Events(World w, SimEventType type)
        {
            var output = new List<SimEvent>();
            int from = Math.Max(0, w.EventHead - World.EventCapacity);
            for (int i = from; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) output.Add(Copy(e));
            }
            return output;
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

        private static MatchState SetMatch(MatchState m, double az, double cr, bool lockBerserk)
        {
            m.Allegiance.Azure = az;
            m.Allegiance.Crimson = cr;
            m.BerserkLocked = lockBerserk;
            return m;
        }

        [Test]
        public void ResolveHatchAzureGtCrimsonToAzureTieToNeutralBerserkLockedOverridesAllegiance()
        {
            MatchState m = World.CreateWorld(Opts()).Match;
            Assert.AreEqual(KaijuState.Azure, Kaiju.ResolveHatch(SetMatch(m, 30, 10, false)));
            Assert.AreEqual(KaijuState.Crimson, Kaiju.ResolveHatch(SetMatch(m, 10, 30, false)));
            Assert.AreEqual(KaijuState.Neutral, Kaiju.ResolveHatch(SetMatch(m, 20, 20, false)));
            Assert.AreEqual(KaijuState.Neutral, Kaiju.ResolveHatch(SetMatch(m, 0, 0, false)));
            Assert.AreEqual(KaijuState.Berserk, Kaiju.ResolveHatch(SetMatch(m, 90, 10, true)));
        }

        [Test]
        public void OnceInstabilityReaches100BeforeHatchBerserkLockedStaysTrueAndInstabilityNeverDecreases()
        {
            World w = MakeWorld();
            // CM-34: ceil(100 / 9) = 12 corruption units lock berserk (was 10 at 10 per unit).
            double units = Math.Ceiling(Feed.BerserkThreshold / Feed.CorruptionInstabilityPerUnit);
            Assert.AreEqual(12.0, units);
            for (int i = 0; i < units - 1; i++) SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1);
            Assert.AreEqual((units - 1) * Feed.CorruptionInstabilityPerUnit, w.Match.Instability);
            Assert.Less(w.Match.Instability, Feed.BerserkThreshold);
            Assert.AreEqual(false, w.Match.BerserkLocked);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 1);
            Assert.AreEqual(units * Feed.CorruptionInstabilityPerUnit, w.Match.Instability);
            Assert.GreaterOrEqual(w.Match.Instability, Feed.BerserkThreshold);
            Assert.AreEqual(true, w.Match.BerserkLocked);
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 50);
            double last = w.Match.Instability;
            for (int i = 0; i < 300; i++)
            {
                SimStep.Step(w);
                Assert.AreEqual(true, w.Match.BerserkLocked);
                Assert.GreaterOrEqual(w.Match.Instability, last);
                last = w.Match.Instability;
            }
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State);
            Assert.AreEqual(3.0, Events(w, SimEventType.Hatch)[0].Value);
        }

        private static void FeedHistory(World w)
        {
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 3);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Biomass, 1);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Corruption, 2);
        }

        [Test]
        public void ForcedHatchAtT10ProducesTheSameStateAsNaturalHatchAtT240ForIdenticalFeedHistoryTwoWorlds()
        {
            World natural = MakeWorld();
            World forced = MakeWorld();
            FeedHistory(natural);
            FeedHistory(forced);

            int steps = 0;
            while (!natural.Match.Hatched && steps < 300 * Hz)
            {
                SimStep.Step(natural);
                steps++;
            }
            Assert.AreEqual(true, natural.Match.Hatched);
            Assert.Greater(natural.Match.Time, Cfg.HatchSeconds - 0.05);
            Assert.Less(natural.Match.Time, Cfg.HatchSeconds + 0.05);

            StepN(forced, 10 * Hz);
            Assert.AreEqual(false, forced.Match.Hatched);
            Assert.AreEqual(KaijuState.Egg, forced.Kaiju.State);
            ForceHatch(forced);

            foreach (World w in new[] { natural, forced })
            {
                Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
                Assert.AreEqual(1, Events(w, SimEventType.Hatch).Count);
                Assert.AreEqual(1.0, Events(w, SimEventType.Hatch)[0].Value);
            }
            Assert.AreEqual(natural.Kaiju.State, forced.Kaiju.State);
            Assert.AreEqual(natural.Kaiju.Hp, forced.Kaiju.Hp);
            Assert.AreEqual(natural.Kaiju.MaxHp, forced.Kaiju.MaxHp);
            Assert.AreEqual(natural.Kaiju.X, forced.Kaiju.X, 1e-4);
            Assert.AreEqual(natural.Kaiju.Z, forced.Kaiju.Z, 1e-4);
            Assert.AreEqual(natural.Kaiju.KoIn, forced.Kaiju.KoIn);
            Assert.AreEqual(natural.Match.BerserkLocked, forced.Match.BerserkLocked);
        }

        [Test]
        public void TheHatchCellPassesClearanceOkAtKaijuGridRadiusAndSitsOutsideTheShrineYardWall()
        {
            World w = MakeWorld();
            ForceHatch(w);
            int cx = JsMath.FloorToInt(w.Kaiju.X);
            int cz = JsMath.FloorToInt(w.Kaiju.Z);
            Assert.AreEqual(true, MapGrid.ClearanceOk(w.Map, cx, cz, K.GridRadius));
            Assert.Greater(w.Kaiju.Z, ShrineZ + 5); // south of the yard wall (outer radius 5)
            Assert.AreEqual(w.Kaiju.MaxHp, w.Kaiju.Hp);
            Assert.AreEqual(KaijuState.Neutral, w.Kaiju.State);
        }

        [Test]
        public void MaxHPHpPerTeamMemberXTeamSizeTimeToKOUnderFullHostileCleavePressureIsGe14SAt5v5And16v16()
        {
            foreach (int teamSize in new[] { 5, 16 })
            {
                World w = MakeWorld(teamSize);
                Assert.AreEqual(K.HpPerTeamMember * teamSize, w.Kaiju.MaxHp);
                SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1); // allied azure: the crimson team is hostile
                ForceHatch(w);
                Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
                // Class mix from the config's bot class mix (the roster builder), every hostile actor swinging every attackCooldown.
                var roster = new ClassId[teamSize];
                for (int i = 0; i < teamSize; i++) roster[i] = ClassId.Warrior;
                World.BuildRoster(w.Config, teamSize, null, roster);
                var crimsonIds = new List<int>();
                for (int id = 0; id < w.ActorCount; id++)
                    if (w.Actors[id].Team == TeamId.Crimson) crimsonIds.Add(id);
                Assert.AreEqual(teamSize, crimsonIds.Count);
                var nextSwing = new double[teamSize];
                double t = 0;
                double koAt = -1;
                double dt = 1 / Hz;
                for (int s = 0; s < 120 * Hz && koAt < 0; s++)
                {
                    for (int k = 0; k < teamSize; k++)
                    {
                        if (t + 1e-9 < nextSwing[k]) continue;
                        ClassConfig cc = w.Config.Class(roster[k]);
                        Kaiju.DamageKaiju(w, crimsonIds[k], cc.AttackDamage);
                        nextSwing[k] = t + cc.AttackCooldown;
                        if (w.Kaiju.KoIn > 0)
                        {
                            koAt = t;
                            break;
                        }
                    }
                    t += dt;
                }
                Assert.GreaterOrEqual(koAt, 14);
            }
        }

        [Test]
        public void KODriveKaijuHpTo0ToKoIn60NoAttacksDuringKORecoversWithFullHpAndTheSameStateAfter60S()
        {
            World w = MakeWorld();
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 2);
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
            // An allied azure Kaiju cannot be hurt by its own team.
            Assert.AreEqual(0.0, Kaiju.DamageKaiju(w, 1, 50));
            Assert.AreEqual(w.Kaiju.MaxHp, w.Kaiju.Hp);
            Kaiju.DamageKaiju(w, 5, w.Kaiju.MaxHp + 10);
            Assert.AreEqual(0.0, w.Kaiju.Hp);
            Assert.AreEqual(K.KoSeconds, w.Kaiju.KoIn);
            Assert.AreEqual(1, Events(w, SimEventType.KaijuKO).Count);
            Assert.AreEqual(0.0, Kaiju.DamageKaiju(w, 5, 10)); // no damage while KO

            // Two crimson actors right next to it the whole time: no melee, no slam.
            for (int id = 5; id < 10; id++) Bench(w, id);
            w.Actors[5].Alive = true;
            w.Actors[5].Hp = w.Actors[5].MaxHp;
            w.Actors[6].Alive = true;
            w.Actors[6].Hp = w.Actors[6].MaxHp;
            Place(w, 5, w.Kaiju.X + 1.2, w.Kaiju.Z);
            Place(w, 6, w.Kaiju.X - 1.2, w.Kaiju.Z);
            double kx = w.Kaiju.X;
            double kz = w.Kaiju.Z;
            double ko = JsMath.Round(K.KoSeconds * Hz);
            for (int s = 0; s < ko - 1; s++)
            {
                SimStep.Step(w);
                w.Actors[5].SpawnProtect = 0;
                w.Actors[6].SpawnProtect = 0;
                Assert.AreEqual(w.Actors[5].MaxHp, w.Actors[5].Hp);
                Assert.AreEqual(w.Actors[6].MaxHp, w.Actors[6].Hp);
                Assert.AreEqual(kx, w.Kaiju.X, 1e-4);
                Assert.AreEqual(kz, w.Kaiju.Z, 1e-4);
                Assert.AreEqual(0.0, w.Kaiju.TelegraphIn);
            }
            Assert.Greater(w.Kaiju.KoIn, 0);
            Assert.AreEqual(0, Events(w, SimEventType.KaijuRecover).Count);
            SimStep.Step(w);
            Assert.AreEqual(0.0, w.Kaiju.KoIn);
            Assert.AreEqual(w.Kaiju.MaxHp, w.Kaiju.Hp);
            Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
            Assert.AreEqual(20.0, w.Match.Allegiance.Azure);
            Assert.AreEqual(1, Events(w, SimEventType.KaijuRecover).Count);
            // Awake again: it attacks the crimson pair.
            StepN(w, 3 * Hz);
            bool hitPair = false;
            foreach (SimEvent e in Events(w, SimEventType.Hit))
                if (e.B == Damage.KaijuSource && (e.A == 5 || e.A == 6)) hitPair = true;
            Assert.AreEqual(true, hitPair);
        }

        // CM-26 (4): the Kaiju melee tell. An 11-tick wind-up announced by a 'kaijuMeleeWindup' event at the target, then
        // the hit if the target is still within meleeRange + 0.4; the hit credits KAIJU_SOURCE (-2).

        private static double Wind => K.MeleeWindupTicks;

        private static double Leash => K.MeleeRange + 0.4;

        /// <summary>Azure Kaiju, only crimson warrior 5 in play, standing <paramref name="d"/> east of the Kaiju (inside the chase stop: no walking).</summary>
        private static World Duel(double d)
        {
            World w = MakeWorld();
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            ForceHatch(w);
            Assert.AreEqual(KaijuState.Azure, w.Kaiju.State);
            for (int id = 6; id < 10; id++) Bench(w, id);
            Place(w, 5, w.Kaiju.X + d, w.Kaiju.Z);
            return w;
        }

        /// <summary>Step until the first wind-up event; returns world.Tick of that step.</summary>
        private static int UntilWindup(World w)
        {
            for (int s = 0; s < 60; s++)
            {
                int head = w.EventHead;
                SimStep.Step(w);
                for (int i = head; i < w.EventHead; i++)
                    if (w.Events[i % World.EventCapacity].Type == SimEventType.KaijuMeleeWindup) return w.Tick;
            }
            throw new InvalidOperationException("no kaijuMeleeWindup within 60 steps");
        }

        /// <summary>Kaiju melee hits (B == KaijuSource) pushed by one step.</summary>
        private static List<SimEvent> StepHits(World w)
        {
            int head = w.EventHead;
            SimStep.Step(w);
            var output = new List<SimEvent>();
            for (int i = head; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == SimEventType.Hit && e.B == Damage.KaijuSource) output.Add(Copy(e));
            }
            return output;
        }

        [Test]
        public void MeleeWindupTicksIs110367SAWholeTickCount()
        {
            Assert.AreEqual(11.0, Wind);
            Assert.AreEqual(true, double.IsFinite(Wind) && Math.Floor(Wind) == Wind);
            Assert.AreEqual(-2, Damage.KaijuSource);
        }

        [Test]
        public void Case11TickWindUpThenTheHitTheEventMarksTheTargetNothingLandsFor10TicksTheHitLandsOnTickPlus11AndCreditsKAIJUSOURCE()
        {
            World w = Duel(2.0);
            double hp0 = w.Actors[5].Hp;
            int t0 = UntilWindup(w);
            List<SimEvent> wind = Events(w, SimEventType.KaijuMeleeWindup);
            Assert.AreEqual(1, wind.Count);
            Assert.AreEqual(5, wind[0].A);
            Assert.AreEqual(w.X[5], wind[0].X, 1e-4);
            Assert.AreEqual(w.Z[5], wind[0].Z, 1e-4);
            Assert.AreEqual(Wind, (double)w.Kaiju.MeleeWindupIn);
            Assert.AreEqual(5, w.Kaiju.MeleeTargetId);
            double kx = w.Kaiju.X;
            double kz = w.Kaiju.Z;
            // The target backs off to just inside the leash (outside meleeRange): still hit.
            Place(w, 5, kx + Leash - 0.05, kz);
            for (int i = 1; i < Wind; i++)
            {
                Assert.AreEqual(0, StepHits(w).Count);
                Assert.AreEqual(hp0, w.Actors[5].Hp);
                Assert.AreEqual(kx, w.Kaiju.X, 1e-4); // stands still while winding up
                Assert.AreEqual(kz, w.Kaiju.Z, 1e-4);
                Assert.AreEqual(Wind - i, (double)w.Kaiju.MeleeWindupIn);
            }
            List<SimEvent> hits = StepHits(w);
            Assert.AreEqual(t0 + Wind, (double)w.Tick);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(5, hits[0].A);
            Assert.AreEqual(K.MeleeDamage, hits[0].Value);
            Assert.AreEqual(hp0 - K.MeleeDamage, w.Actors[5].Hp);
            Assert.AreEqual(Damage.KaijuSource, w.Actors[5].LastAttacker);
            Assert.AreEqual(0, w.Kaiju.MeleeWindupIn);
        }

        [Test]
        public void WhiffATargetThatLeavesMeleeRangePlus04DuringTheWindUpTakesNothingAndTheCooldownIsStillSpent()
        {
            World w = Duel(2.0);
            double hp0 = w.Actors[5].Hp;
            UntilWindup(w);
            Place(w, 5, w.Kaiju.X + Leash + 0.3, w.Kaiju.Z);
            for (int i = 0; i < Wind; i++) Assert.AreEqual(0, StepHits(w).Count);
            Assert.AreEqual(hp0, w.Actors[5].Hp);
            Assert.AreEqual(0, w.Kaiju.MeleeWindupIn);
            Assert.Greater(w.Kaiju.MeleeCd, 0);
            // No second wind-up before the cooldown runs out.
            double cdTicks = Math.Ceiling(w.Kaiju.MeleeCd * Hz) - 1;
            for (int i = 0; i < cdTicks; i++) SimStep.Step(w);
            Assert.AreEqual(1, Events(w, SimEventType.KaijuMeleeWindup).Count);
        }

        [Test]
        public void AKOCancelsAWindUpInProgress()
        {
            World w = Duel(2.0);
            double hp0 = w.Actors[5].Hp;
            UntilWindup(w);
            Kaiju.DamageKaiju(w, 5, w.Kaiju.MaxHp + 1);
            Assert.Greater(w.Kaiju.KoIn, 0);
            Assert.AreEqual(0, w.Kaiju.MeleeWindupIn);
            for (int i = 0; i < Wind + 2; i++) Assert.AreEqual(0, StepHits(w).Count);
            Assert.AreEqual(hp0, w.Actors[5].Hp);
        }

        private static int TotalKills(World w)
        {
            int s = 0;
            foreach (Actor a in w.Actors) s += a.Kills;
            return s;
        }

        private static SimEvent LastOf(List<SimEvent> list)
        {
            return list[list.Count - 1];
        }

        [Test]
        public void KAIJUSOURCESurvivesApplyDamageIntoLastAttackerAndTheHitDeathEventsActorAnd1SourcesAreUnchanged()
        {
            World w = MakeWorld();
            Place(w, 5, 60, 40);
            int kills0 = TotalKills(w);
            Assert.AreEqual(10.0, Damage.ApplyDamage(w, 5, 10, Damage.KaijuSource, 58, 40));
            Assert.AreEqual(Damage.KaijuSource, w.Actors[5].LastAttacker);
            SimEvent hit = LastOf(Events(w, SimEventType.Hit));
            Assert.AreEqual(5, hit.A);
            Assert.AreEqual(Damage.KaijuSource, hit.B);
            // An actor hit after it overwrites the id; a -1 hit (slam) still reads -1.
            Damage.ApplyDamage(w, 5, 5, 0, 58, 40);
            Assert.AreEqual(0, w.Actors[5].LastAttacker);
            Damage.ApplyDamage(w, 5, 5, -1, 58, 40);
            Assert.AreEqual(-1, w.Actors[5].LastAttacker);
            Assert.AreEqual(-1, LastOf(Events(w, SimEventType.Hit)).B);
            // Lethal Kaiju melee: the death event carries KAIJU_SOURCE, nobody gets the kill.
            Damage.ApplyDamage(w, 5, 1e6, Damage.KaijuSource, 58, 40);
            Assert.AreEqual(false, w.Actors[5].Alive);
            Assert.AreEqual(Damage.KaijuSource, w.Actors[5].LastAttacker);
            Assert.AreEqual(Damage.KaijuSource, LastOf(Events(w, SimEventType.Death)).B);
            Assert.AreEqual(kills0, TotalKills(w));
            // Friendly fire stays off for actor sources.
            Place(w, 1, 30, 40);
            Assert.AreEqual(0.0, Damage.ApplyDamage(w, 1, 10, 0, 30, 40));
        }

        [Test]
        public void SLAMUnchangedNoWindUpWhileTheSlamTelegraphsTheSlamHitsCredit1ForSlamDamage()
        {
            World w = MakeWorld();
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            ForceHatch(w);
            for (int id = 7; id < 10; id++) Bench(w, id);
            double kx = w.Kaiju.X;
            double kz = w.Kaiju.Z;
            Place(w, 5, kx + 2, kz);
            Place(w, 6, kx - 2, kz);
            SimStep.Step(w);
            Assert.Greater(w.Kaiju.TelegraphIn, 0);
            double hp5 = w.Actors[5].Hp;
            double wind = JsMath.Round(K.SlamTelegraphSeconds * Hz);
            for (int i = 0; i < wind; i++) SimStep.Step(w);
            Assert.AreEqual(1, Events(w, SimEventType.KaijuSlam).Count);
            Assert.AreEqual(0, Events(w, SimEventType.KaijuMeleeWindup).Count);
            var slamHits = new List<SimEvent>();
            foreach (SimEvent e in Events(w, SimEventType.Hit))
                if (e.B == -1) slamHits.Add(e);
            Assert.AreEqual(2, slamHits.Count);
            foreach (SimEvent e in slamHits) Assert.AreEqual(K.SlamDamage, e.Value);
            Assert.AreEqual(K.SlamDamage, hp5 - w.Actors[5].Hp);
            Assert.AreEqual(-1, w.Actors[5].LastAttacker);
        }
    }
}
