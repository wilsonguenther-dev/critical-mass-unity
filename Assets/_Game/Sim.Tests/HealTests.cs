using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/sim/heal.test.ts (09e0904, CM-37): the base healing fountain. Time is counted in sim ticks.</summary>
    [TestFixture]
    public sealed class HealTests
    {
        private static World MakeWorld(GameConfig cfg = null)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 11,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = cfg ?? GameConfig.CreateBrowser5v5(),
            });
        }

        private static List<SimEvent> HealEvents(World w, int head)
        {
            var output = new List<SimEvent>();
            for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == SimEventType.Heal) output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return output;
        }

        private static void Place(World w, int id, TeamId team, double hp, double dx = 0, double dz = 0)
        {
            PadDef pad = w.Map.Pads[(int)team];
            w.X[id] = pad.X + dx;
            w.Z[id] = pad.Z + dz;
            w.PrevX[id] = w.X[id];
            w.PrevZ[id] = w.Z[id];
            w.Actors[id].Hp = hp;
        }

        private static int FirstOfTeam(World w, TeamId team, int skip = -1)
        {
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == team && i != skip) return i;
            throw new InvalidOperationException("no actor of " + team);
        }

        [Test]
        public void HpPerTickPositiveTheZoneCoversEveryPadGoalCellAndStaysInsideTheYardWall()
        {
            World w = MakeWorld();
            HealConfig h = w.Config.Heal;
            Assert.Greater(h.HpPerTick, 0);
            foreach (PadDef pad in w.Map.Pads)
            {
                Assert.GreaterOrEqual(h.ZoneRadius, pad.R + Math.Sqrt(0.5));
                Assert.Less(h.ZoneRadius, 4); // map YARD_INNER
            }
            Assert.Greater(h.SeekHpShare, 0);
            Assert.Less(h.SeekHpShare, 1);
            double refillTicks = Math.Ceiling(w.Config.Class(ClassId.Warrior).MaxHp * (1 - h.SeekHpShare) / h.HpPerTick);
            Assert.Less(refillTicks, w.Config.Respawn.DelaySeconds * w.Config.SimHz);
        }

        [Test]
        public void AnActorInsideItsOwnZoneHealsHpPerTickUpToMaxHpOneEventPerHealCarryingTheAmount()
        {
            World w = MakeWorld();
            double per = w.Config.Heal.HpPerTick;
            Actor a = w.Actors[0];
            double start = a.MaxHp - 3.5 * per;
            Place(w, 0, a.Team, start, 1, -1);
            Assert.IsTrue(Heal.InFountainZone(w, a.Team, w.X[0], w.Z[0]));
            int head = w.EventHead;
            for (int t = 1; t <= 3; t++)
            {
                Heal.HealSystem(w);
                Assert.AreEqual(start + t * per, a.Hp, 1e-9);
            }
            Heal.HealSystem(w);
            Assert.AreEqual(a.MaxHp, a.Hp);
            for (int t = 0; t < 5; t++) Heal.HealSystem(w);
            Assert.AreEqual(a.MaxHp, a.Hp);
            List<SimEvent> ev = HealEvents(w, head).FindAll(e => e.A == 0);
            Assert.AreEqual(4, ev.Count);
            for (int i = 0; i < 3; i++) Assert.AreEqual(per, ev[i].Value);
            Assert.AreEqual(0.5 * per, ev[3].Value, 1e-9);
            double sum = 0;
            foreach (SimEvent e in ev)
            {
                sum += e.Value;
                Assert.AreEqual(-1, e.B);
                Assert.AreEqual(w.X[0], e.X);
                Assert.AreEqual(w.Z[0], e.Z);
            }
            Assert.AreEqual(a.MaxHp - start, sum, 1e-9);
        }

        [Test]
        public void EnemiesDoNotHealInTheOtherTeamsFountainAndNobodyHealsOutsideAZone()
        {
            World w = MakeWorld();
            double per = w.Config.Heal.HpPerTick;
            int foe = FirstOfTeam(w, TeamId.Crimson);
            int ally = FirstOfTeam(w, TeamId.Azure);
            double foeHp = w.Actors[foe].MaxHp - 20;
            double allyHp = w.Actors[ally].MaxHp - 20;
            Place(w, foe, TeamId.Azure, foeHp);
            Place(w, ally, TeamId.Azure, allyHp);
            int outside = FirstOfTeam(w, TeamId.Azure, ally);
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            Place(w, outside, TeamId.Azure, w.Actors[outside].MaxHp - 20, w.Config.Heal.ZoneRadius + 0.5, 0);
            double outHp = w.Actors[outside].Hp;
            int head = w.EventHead;
            Heal.HealSystem(w);
            Assert.AreEqual(foeHp, w.Actors[foe].Hp);
            Assert.AreEqual(allyHp + per, w.Actors[ally].Hp);
            Assert.AreEqual(outHp, w.Actors[outside].Hp);
            Assert.IsFalse(Heal.InFountainZone(w, TeamId.Azure, pad.X + w.Config.Heal.ZoneRadius + 0.5, pad.Z));
            List<SimEvent> ev = HealEvents(w, head);
            Assert.IsFalse(ev.Exists(e => e.A == foe));
            Assert.IsFalse(ev.Exists(e => e.A == outside));
            Assert.AreEqual(1, ev.FindAll(e => e.A == ally).Count);
        }

        [Test]
        public void TheDeadNeverHeal()
        {
            World w = MakeWorld();
            int id = FirstOfTeam(w, TeamId.Crimson);
            Place(w, id, TeamId.Crimson, 0);
            w.Actors[id].Alive = false;
            int head = w.EventHead;
            Heal.HealSystem(w);
            Assert.AreEqual(0, w.Actors[id].Hp);
            Assert.AreEqual(0, HealEvents(w, head).Count);
        }

        [Test]
        public void HpPerTick0SwitchesTheFountainOff()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.Heal.HpPerTick = 0;
            World w = MakeWorld(cfg);
            Place(w, 0, w.Actors[0].Team, 10);
            int head = w.EventHead;
            Assert.AreEqual(0, Heal.HealSystem(w));
            Assert.AreEqual(10, w.Actors[0].Hp);
            Assert.AreEqual(0, HealEvents(w, head).Count);
        }

        [Test]
        public void StepRunsTheFountainOncePerTick5TicksAre5TimesHpPerTick()
        {
            World w = MakeWorld();
            double per = w.Config.Heal.HpPerTick;
            Actor a = w.Actors[0];
            double start = a.MaxHp - 50 * per;
            Place(w, 0, a.Team, start);
            int head = w.EventHead;
            for (int t = 0; t < 5; t++) SimStep.Step(w);
            Assert.AreEqual(start + 5 * per, a.Hp, 1e-9);
            Assert.AreEqual(5, HealEvents(w, head).FindAll(e => e.A == 0).Count);
        }

        [Test]
        public void FsmLowHpIsTheConfiguredShareSoTheDefaultReplaysMainsBotDecisions()
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(c.Heal.SeekHpShare, BotFsm.LowHp(c));
            Assert.AreEqual(0.3, c.Heal.SeekHpShare);
        }

        /// <summary>A hill-role crimson bot two units off its own pad centre with one azure enemy 4 units away.</summary>
        private static World Setup(double hpShare, out int me)
        {
            World w = MakeWorld();
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Place(w, i, a.Team, a.MaxHp);
                a.AiState = AiState.None;
                a.SpawnProtect = 0;
            }
            me = FirstOfTeam(w, TeamId.Crimson);
            int foe = FirstOfTeam(w, TeamId.Azure);
            Place(w, me, TeamId.Crimson, w.Actors[me].MaxHp * hpShare, 2, 0);
            PadDef pad = w.Map.Pads[(int)TeamId.Crimson];
            w.X[foe] = pad.X + 2;
            w.Z[foe] = pad.Z + 4;
            w.PrevX[foe] = w.X[foe];
            w.PrevZ[foe] = w.Z[foe];
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            BotsState st = BotBrain.GetBotsState(w);
            st.Role[me] = BotsState.RoleHill;
            st.Corrupt[me] = 0;
            return w;
        }

        [Test]
        public void BelowTheShareItChoosesRetreatThePadFieldLeadsIntoTheZoneAndThenHealsThere()
        {
            World w = Setup(w0Share(), out int me);
            BotFsm.ThinkBot(w, BotBrain.GetBotsState(w), me);
            Assert.AreEqual(AiState.Retreat, w.Actors[me].AiState);
            Assert.IsTrue(Heal.InFountainZone(w, TeamId.Crimson, w.X[me], w.Z[me]));
            int head = w.EventHead;
            double hp = w.Actors[me].Hp;
            Heal.HealSystem(w);
            Assert.AreEqual(hp + w.Config.Heal.HpPerTick, w.Actors[me].Hp);
            Assert.AreEqual(1, HealEvents(w, head).FindAll(e => e.A == me).Count);
        }

        private static double w0Share() => GameConfig.CreateBrowser5v5().Heal.SeekHpShare * 0.5;

        [Test]
        public void AboveTheShareTheSameBotDoesNotRetreat()
        {
            World w = Setup(Math.Min(1, GameConfig.CreateBrowser5v5().Heal.SeekHpShare + 0.1), out int me);
            BotFsm.ThinkBot(w, BotBrain.GetBotsState(w), me);
            Assert.AreNotEqual(AiState.Retreat, w.Actors[me].AiState);
        }
    }
}
