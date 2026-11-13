using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>DG-40 Healer (U-061; not in the browser): mend on attack, healing pulse on Space, purify at the egg on DigPlace. Sim ticks only.</summary>
    [TestFixture]
    public sealed class HealerTests
    {
        private const int H = 0;
        private static GameConfig Cfg => GameConfig.CreateBrowser5v5();
        private static HealerConfig HC => Cfg.Abilities.Healer;

        private static List<SimEvent> Since(World w, int head, SimEventType type)
        {
            var list = new List<SimEvent>();
            for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) list.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Radius = e.Radius });
            }
            return list;
        }

        private static void Put(World w, int j, double x, double z, double hp)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = hp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            w.X[j] = x;
            w.Z[j] = z;
        }

        private static int Ally(World w, int k) => 1 + k;
        private static int Enemy(World w, int k) => w.ActorCount / 2 + k;

        private static World Field()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 40, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Healer, Config = Cfg });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            Put(w, H, 56, 40, w.Actors[H].MaxHp);
            w.Tick = 100;
            Assert.AreEqual(ClassId.Healer, w.Actors[H].Cls);
            Assert.AreEqual(TeamId.Azure, w.Actors[Ally(w, 0)].Team);
            Assert.AreEqual(TeamId.Crimson, w.Actors[Enemy(w, 0)].Team);
            return w;
        }

        [Test]
        public void DG40TheAttackPressMendsTheHurtAllyNearestTheAimNeverAnEnemyNeverDamagesAndSpendsNoCooldownWithoutATarget()
        {
            World w = Field();
            int a0 = Ally(w, 0), a1 = Ally(w, 1), e = Enemy(w, 0);
            Put(w, a0, 59, 40, 50);
            Put(w, a1, 56, 44, 50);
            Put(w, e, 57, 40, 50);
            w.Inputs[H].Attack = true;
            w.HasAim[H] = 1;
            w.Inputs[H].AimX = 56;
            w.Inputs[H].AimZ = 45;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            int head = w.EventHead;
            Combat.CombatSystem(w, SimStep.Dt(w.Config));
            Assert.AreEqual(50 + HC.MendHeal, w.Actors[a1].Hp, 1e-9, "the ally nearest the aim");
            Assert.AreEqual(50, w.Actors[a0].Hp, 1e-9);
            Assert.AreEqual(50, w.Actors[e].Hp, 1e-9, "never an enemy, never a hit");
            Assert.AreEqual(w.Config.Class(ClassId.Healer).AttackCooldown, w.Actors[H].AttackCd, 1e-9);
            Assert.IsEmpty(Since(w, head, SimEventType.Hit));
            Assert.IsEmpty(Since(w, head, SimEventType.AttackStart));
            SimEvent cast = Since(w, head, SimEventType.HealCast).Single();
            Assert.AreEqual(Healer.KindMend, cast.B);
            Assert.AreEqual(56, cast.X, 1e-9);
            Assert.AreEqual(44, cast.Z, 1e-9);
            SimEvent land = Since(w, head, SimEventType.HealLanded).Single();
            Assert.AreEqual(a1, land.A);
            Assert.AreEqual(H, land.B);
            Assert.AreEqual(HC.MendHeal, land.Value, 1e-9);
            // Nobody hurt in range (she is hurt herself: no self-mend): no cooldown, no event.
            w.Actors[H].AttackCd = 0;
            w.Actors[H].Hp = 10;
            Put(w, a0, 59, 40, w.Actors[a0].MaxHp);
            Put(w, a1, 70, 40, 10);
            head = w.EventHead;
            Combat.CombatSystem(w, SimStep.Dt(w.Config));
            Assert.AreEqual(0, w.Actors[H].AttackCd);
            Assert.AreEqual(10, w.Actors[H].Hp, 1e-9);
            Assert.IsEmpty(Since(w, head, SimEventType.HealCast));
        }

        [Test]
        public void DG40SpaceIsAHealingPulseOnEveryAllyInItsRadiusHerselfIncludedCappedAtMaxHpWithItsCooldown()
        {
            World w = Field();
            int a0 = Ally(w, 0), a1 = Ally(w, 1), a2 = Ally(w, 2), e = Enemy(w, 0);
            Put(w, H, 56, 40, 50);
            Put(w, a0, 60, 40, 10);
            Put(w, a1, 61, 41, w.Actors[a1].MaxHp - 5);
            Put(w, a2, 66, 40, 10);  // outside the radius
            Put(w, e, 60, 41, 10);
            w.HasAim[H] = 1;
            w.Inputs[H].AimX = 59;
            w.Inputs[H].AimZ = 40;
            w.Inputs[H].Ability = true;
            int head = w.EventHead;
            Abilities.AbilitiesSystem(w, SimStep.Dt(w.Config));
            Assert.AreEqual(50 + HC.PulseHeal, w.Actors[H].Hp, 1e-9, "herself (3 from the centre)");
            Assert.AreEqual(10 + HC.PulseHeal, w.Actors[a0].Hp, 1e-9);
            Assert.AreEqual(w.Actors[a1].MaxHp, w.Actors[a1].Hp, 1e-9, "capped");
            Assert.AreEqual(10, w.Actors[a2].Hp, 1e-9);
            Assert.AreEqual(10, w.Actors[e].Hp, 1e-9);
            Assert.AreEqual(w.Config.Class(ClassId.Healer).AbilityCooldown, w.Actors[H].AbilityCd, 1e-9);
            SimEvent cast = Since(w, head, SimEventType.HealCast).Single();
            Assert.AreEqual(Healer.KindPulse, cast.B);
            Assert.AreEqual(59, cast.X, 1e-9);
            Assert.AreEqual(HC.PulseRadius, cast.Radius, 1e-9);
            Assert.AreEqual(2 * HC.PulseHeal + 5, cast.Value, 1e-9);
            Assert.AreEqual(3, Since(w, head, SimEventType.HealLanded).Count);
        }

        [Test]
        public void DG40PurifyAtTheEggRemovesOneCorruptionUnitOfInstabilityOnItsOwnCooldownAndOnlyWhileACleanseCouldRun()
        {
            World w = Field();
            ShrineDef s = w.Map.Shrine;
            w.Match.Delivered[1].Corruption = 3;
            w.Match.Instability = 50;
            Put(w, H, s.X, s.Z + 3, w.Actors[H].MaxHp);
            w.Inputs[H].Terraform = true;
            int head = w.EventHead;
            Healer.HealerInputSystem(w);
            double per = w.Config.KaijuFeed.CorruptionInstabilityPerUnit;
            Assert.AreEqual(50 - per, w.Match.Instability, 1e-9);
            SimEvent p = Since(w, head, SimEventType.Purify).Single();
            Assert.AreEqual(per, p.Value, 1e-9);
            Assert.AreEqual(s.X, p.X, 1e-9);
            Assert.AreEqual(HC.PurifyRange, p.Radius, 1e-9);
            Assert.AreEqual(0, p.B, "Azure");
            Assert.AreEqual(2, Cleanse.NetCorruptionUnits(w), 1e-9, "the unit counts as cleansed");
            Assert.AreEqual(0, w.Cleanse.Cooldown[0], "the team cleanse cooldown is not touched");
            // Held key: nothing. A fresh press on cooldown: silent, nothing.
            Healer.HealerInputSystem(w);
            w.Inputs[H].Terraform = false;
            Healer.HealerInputSystem(w);
            w.Inputs[H].Terraform = true;
            head = w.EventHead;
            Healer.HealerInputSystem(w);
            Assert.AreEqual(50 - per, w.Match.Instability, 1e-9);
            Assert.AreEqual(0, w.EventHead - head);
            // Off cooldown but too far, then closed by the berserk lock.
            w.Tick += (int)HC.PurifyCooldownTicks;
            w.X[H] = s.X + 10;
            Assert.AreEqual(Healer.PurifyRefusedRange, Healer.Purify(w, H));
            w.X[H] = s.X;
            w.Match.BerserkLocked = true;
            Assert.AreEqual(Healer.PurifyRefusedClosed, Healer.Purify(w, H));
            Assert.IsFalse(Healer.CanPurify(w, H));
        }

        [Test]
        public void DG40ANonHealerPressingDigPlaceAtTheEggPurifiesNothing()
        {
            World w = Field();
            w.Actors[H].Cls = ClassId.Warrior;
            w.Match.Delivered[1].Corruption = 3;
            w.Match.Instability = 50;
            Put(w, H, w.Map.Shrine.X, w.Map.Shrine.Z + 3, 100);
            w.Inputs[H].Terraform = true;
            Healer.HealerInputSystem(w);
            Assert.AreEqual(50, w.Match.Instability, 1e-9);
        }

        [Test]
        public void DG40ResetClearsPurifyStateAndNoHealerMeansNoHealerEvent()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = Cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int head = w.EventHead;
            for (int t = 0; t < 1800; t++) MatchTick.Advance(w, null, scratch);
            Assert.AreEqual(0, w.Healer.Stats.Mends + w.Healer.Stats.Pulses + w.Healer.Stats.Purifies);
            w.Healer.PurifiedUnits = 2;
            w.Healer.PurifyReadyTick[0] = 99;
            World.ResetWorld(w, w.Opts);
            Assert.AreEqual(0, w.Healer.PurifiedUnits);
            Assert.AreEqual(0, w.Healer.PurifyReadyTick[0]);
        }

        [Test]
        public void DG40HealerBotsUseTheirAbilitiesAndTheSameSeedGivesTheSameHash()
        {
            Action<GameConfig> mix = c => c.Bots.ClassMix = new[] { 0.3, 0.2, 0.2, 0, 0, 0.3 };
            ulong a = MatchHash.Run(4040, 16, 2700, ClassId.Healer, null, mix);
            Assert.AreEqual(a, MatchHash.Run(4040, 16, 2700, ClassId.Healer, null, mix));
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            mix(cfg);
            World w = World.CreateWorld(new WorldOptions { Seed = 4040, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < 2700; t++) MatchTick.Advance(w, null, scratch);
            Assert.Greater(w.Healer.Stats.Mends, 0, "bots mend");
            Assert.Greater(w.Healer.Stats.Pulses, 0, "bots pulse");
        }
    }
}
