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
    /// DG-16 additions asked for by the view builders (not in the browser): the damage cause on Hit / Death events and
    /// Actor.LastCause, Damage.KaijuSlamSource, Bots.PassiveSeconds, the AttackStart and KaijuDamaged events, and a
    /// same-seed state hash over everything DG-16a / DG-16b added.
    /// </summary>
    [TestFixture]
    public sealed class Dg16ExtrasTests
    {
        private static World MakeWorld(GameConfig cfg = null, int seed = 7, int teamSize = 5)
        {
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg ?? GameConfig.CreateBrowser5v5() });
        }

        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Actors[id].SpawnProtect = 0;
        }

        private static SimEvent Last(World w, SimEventType type)
        {
            for (int i = w.EventHead - 1; i >= Math.Max(0, w.EventHead - World.EventCapacity); i--)
                if (w.Events[i % World.EventCapacity].Type == type) return w.Events[i % World.EventCapacity];
            return null;
        }

        private static int Count(World w, SimEventType type, int a)
        {
            int c = 0;
            for (int i = Math.Max(0, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type && (a < -9 || e.A == a)) c++;
            }
            return c;
        }

        [Test]
        public void HitAndDeathEventsCarryTheCauseAndExistingFieldsAreUnchanged()
        {
            World w = MakeWorld();
            Place(w, 5, 30, 30);
            Damage.ApplyDamage(w, 5, 1, 1, 29, 30);
            Assert.AreEqual(DamageCause.Melee, Last(w, SimEventType.Hit).Cause);
            Assert.AreEqual(1, Last(w, SimEventType.Hit).B);
            Damage.ApplyDamage(w, 5, 1, Damage.KaijuSource, 29, 30);
            Assert.AreEqual(DamageCause.KaijuMelee, Last(w, SimEventType.Hit).Cause);
            Assert.AreEqual(Damage.KaijuSource, Last(w, SimEventType.Hit).B);
            Damage.ApplyDamage(w, 5, 1, -1, 29, 30);
            Assert.AreEqual(DamageCause.None, Last(w, SimEventType.Hit).Cause);
            // The slam: a distinct source id, but B and LastAttacker still read -1 (ported tests pin it); the cause tells it apart.
            Damage.ApplyDamage(w, 5, 1e6, Damage.KaijuSlamSource, 29, 30);
            SimEvent death = Last(w, SimEventType.Death);
            Assert.AreEqual(-1, death.B);
            Assert.AreEqual(DamageCause.KaijuSlam, death.Cause);
            Assert.AreEqual(-1, w.Actors[5].LastAttacker);
            Assert.AreEqual(DamageCause.KaijuSlam, w.Actors[5].LastCause);
            Assert.AreEqual(-3, Damage.KaijuSlamSource);
            // Every other event writes None and null.
            World.PushEvent(w, SimEventType.Gather, 1, 0, 0, 0, 1);
            Assert.AreEqual(DamageCause.None, Last(w, SimEventType.Gather).Cause);
            Assert.IsNull(Last(w, SimEventType.Gather).Bio);
        }

        [Test]
        public void ARealMeleeKillIsMeleeAndRespawnClearsTheCause()
        {
            World w = MakeWorld();
            Place(w, 5, 30, 30);
            Damage.ApplyDamage(w, 5, 1e6, 1, 29, 30);
            Assert.AreEqual(DamageCause.Melee, w.Actors[5].LastCause);
            for (int t = 0; t < (w.Config.Respawn.DelaySeconds + 1) * w.Config.SimHz && !w.Actors[5].Alive; t++) SimStep.Step(w);
            Assert.IsTrue(w.Actors[5].Alive);
            Assert.AreEqual(DamageCause.None, w.Actors[5].LastCause);
        }

        [Test]
        public void ASwingPushesOneAttackStartAtItsReachPointAndAKaijuHitPushesKaijuDamagedAtTheContactPoint()
        {
            World w = MakeWorld();
            Place(w, 1, 30, 30);
            w.Actors[1].Facing = 0;
            InputFrame f = SimInput.EmptyInput();
            f.Attack = true;
            f.AimX = 40;
            f.AimZ = 30;
            SimInput.ApplyInput(w, 1, f);
            SimStep.Step(w);
            SimEvent e = Last(w, SimEventType.AttackStart);
            Assert.IsNotNull(e);
            Assert.AreEqual(1, e.A);
            double range = w.Config.Class(w.Actors[1].Cls).AttackRange;
            double fx = Math.Cos(w.Actors[1].Facing), fz = Math.Sin(w.Actors[1].Facing);
            Assert.AreEqual(w.X[1] + fx * range, e.X, 0.05);
            Assert.AreEqual(w.Z[1] + fz * range, e.Z, 0.05);
            Assert.AreEqual(1, Count(w, SimEventType.AttackStart, 1));

            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 1);
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.IsTrue(w.Match.Hatched);
            int src = w.Kaiju.State == KaijuState.Azure ? 5 : 1;
            Place(w, src, w.Kaiju.X + 2, w.Kaiju.Z);
            double dealt = Kaiju.DamageKaiju(w, src, 7);
            Assert.AreEqual(7, dealt);
            SimEvent kd = Last(w, SimEventType.KaijuDamaged);
            Assert.IsNotNull(kd);
            Assert.AreEqual(src, kd.A);
            Assert.AreEqual(7, kd.Value);
            double dx = kd.X - w.Kaiju.X, dz = kd.Z - w.Kaiju.Z;
            Assert.AreEqual(w.Config.Kaiju.Radius, Math.Sqrt(dx * dx + dz * dz), 1e-6);
            Assert.Greater(kd.X, w.Kaiju.X);
        }

        [Test]
        public void PassiveSecondsDefaultsTo0AndKeepsBotsOffTheHumanForTheWindowThenLetsThemIn()
        {
            Assert.AreEqual(0, GameConfig.CreateBrowser5v5().Bots.PassiveSeconds);
            Assert.AreEqual(0, GameConfig.CreateDefault().Bots.PassiveSeconds);
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.Bots.PassiveSeconds = 2;
            World w = MakeWorld(cfg);
            Place(w, 0, 56, 40);
            Place(w, 5, 57, 40); // crimson warrior bot right next to the human, on the hill
            w.Actors[5].Facing = Math.PI;
            InputFrame idle = SimInput.EmptyInput();
            InputFrame scratch = SimInput.EmptyInput();
            int hitsOnHuman = 0;
            int seen = w.EventHead;
            double hp0 = w.Actors[0].Hp;
            // The bot's own swing input, every tick, aimed at the human: no damage inside the window.
            for (int t = 0; t < 2 * cfg.SimHz - 2; t++)
            {
                InputFrame atk = SimInput.EmptyInput();
                atk.Attack = true;
                atk.AimX = w.X[0];
                atk.AimZ = w.Z[0];
                SimInput.ApplyInput(w, 5, atk);
                SimInput.ApplyInput(w, 0, idle);
                SimStep.Step(w);
            }
            for (; seen < w.EventHead; seen++)
            {
                SimEvent e = w.Events[seen % World.EventCapacity];
                if (e.Type == SimEventType.Hit && e.A == 0) hitsOnHuman++;
            }
            Assert.AreEqual(0, hitsOnHuman);
            Assert.Greater(Count(w, SimEventType.AttackStart, 5), 0, "the bot never swung: the check would be vacuous");
            Assert.AreEqual(hp0, w.Actors[0].Hp);
            Assert.IsTrue(BotFsm.PassiveTarget(w, 0));
            Assert.IsFalse(BotFsm.PassiveTarget(w, 1));
            // A thinking bot does not pick the human as its fight target inside the window.
            BotsState st = BotBrain.GetBotsState(w);
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            BotFsm.ThinkBot(w, st, 5);
            Assert.IsFalse(w.Actors[5].AiState == AiState.Fight && st.Target[5] == 0);
            // After the window the same swing lands.
            for (int t = 0; t < cfg.SimHz && w.Actors[0].Hp == hp0; t++)
            {
                InputFrame atk = SimInput.EmptyInput();
                atk.Attack = true;
                atk.AimX = w.X[0];
                atk.AimZ = w.Z[0];
                SimInput.ApplyInput(w, 5, atk);
                SimInput.ApplyInput(w, 0, idle);
                SimStep.Step(w);
            }
            Assert.Less(w.Actors[0].Hp, hp0);
            Assert.GreaterOrEqual(w.Match.Time, cfg.Bots.PassiveSeconds);
            Assert.IsNotNull(scratch);
        }

        [Test]
        public void ABotMatchWithAPassiveWindowNeverHitsAnIdleHumanInsideIt()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.Bots.PassiveSeconds = 30;
            World w = MakeWorld(cfg, 3);
            InputFrame idle = SimInput.EmptyInput();
            InputFrame scratch = SimInput.EmptyInput();
            int seen = w.EventHead;
            int hits = 0;
            for (int t = 0; t < 40 * cfg.SimHz; t++)
            {
                idle.AimX = w.X[0];
                idle.AimZ = w.Z[0];
                MatchTick.Advance(w, idle, scratch);
                for (; seen < w.EventHead; seen++)
                {
                    SimEvent e = w.Events[seen % World.EventCapacity];
                    if (e.Type == SimEventType.Hit && e.A == 0 && w.Match.Time < cfg.Bots.PassiveSeconds) hits++;
                }
            }
            Assert.AreEqual(0, hits);
        }

        private static ulong Hash(World w)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
            void MixD(double d) => Mix(BitConverter.DoubleToInt64Bits(d));
            Mix(w.Tick);
            Mix(w.EventHead);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                MixD(w.X[i]);
                MixD(w.Z[i]);
                MixD(a.Hp);
                Mix(a.CarryAmount);
                Mix(a.CarryMeat);
                Mix(a.DeliverTicks);
                Mix((long)a.LastCause);
                Mix(a.Alive ? 1 : 0);
            }
            foreach (LooseOrb o in w.Orbs)
            {
                Mix(o.Active ? 1 : 0);
                Mix(o.Units);
                Mix(o.Ttl);
            }
            MixD(w.Match.Instability);
            MixD(w.Match.Intercepted.Azure);
            MixD(w.Match.Intercepted.Crimson);
            MixD(w.Match.Delivered[0].Meat + w.Match.Delivered[1].Meat);
            Mix(w.Cleanse.Count);
            MixD(w.Cleanse.Removed);
            MixD(w.Match.HillSeconds.Azure);
            MixD(w.Match.HillSeconds.Crimson);
            return h;
        }

        private static ulong Run(int seed)
        {
            World w = MakeWorld(null, seed, 16);
            InputFrame f = SimInput.EmptyInput();
            for (int t = 0; t < 2400; t++) MatchTick.Advance(w, null, f);
            return Hash(w);
        }

        [Test]
        public void DG16SameSeedSameStateHashIn16v16WithMeatHealDeliveryWindupAndCleanseLive()
        {
            Assert.AreEqual(Run(1337), Run(1337));
            Assert.AreNotEqual(Run(1337), Run(1338));
        }
    }
}
