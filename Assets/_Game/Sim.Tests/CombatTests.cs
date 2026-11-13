using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/combat.test.ts (09e0904), the 14 combat and damage cases (the 5 respawn cases are F-05d).
    /// 5v5, human azure warrior. Roster: azure 0 warrior (human), 1 warrior, 2 farmer, 3 farmer, 4 miner;
    /// crimson 5 warrior, 6 warrior, 7 farmer, 8 farmer, 9 miner. Fights are staged inside the hill ring around (56, 40).
    /// </summary>
    [TestFixture]
    public sealed class CombatTests
    {
        private static GameConfig Cfg => SimPhysicsTests.ClassicBrowser();

        private static ClassConfig W => Cfg.Class(ClassId.Warrior);

        private static World MakeWorld()
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 7,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = SimPhysicsTests.ClassicBrowser(),
            });
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

        /// <summary>Stand still, aim at (ax, az), optionally holding attack.</summary>
        private static void AimAt(World w, int id, double ax, double az, bool attack)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = ax;
            f.AimZ = az;
            f.Attack = attack;
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
                {
                    output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
                }
            }
            return output;
        }

        private static void StepN(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
        }

        private static double Close(int digits) => 0.5 * Math.Pow(10, -digits);

        // combat: cleave

        [Test]
        public void CleaveHitsAnEnemyInFrontWithinRangeAndMissesOneBehind()
        {
            World w = MakeWorld();
            Place(w, 0, 54, 40);
            Place(w, 5, 55.5, 40); // in front, 1.5 away
            Place(w, 6, 52.6, 40); // behind, 1.4 away
            AimAt(w, 0, 60, 40, true);
            SimStep.Step(w);
            Assert.AreEqual(W.MaxHp - W.AttackDamage, w.Actors[5].Hp, Close(5));
            Assert.AreEqual(W.MaxHp, w.Actors[6].Hp);
            List<SimEvent> hits = Events(w, SimEventType.Hit);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(5, hits[0].A);
            Assert.AreEqual(0, hits[0].B);
            Assert.AreEqual(W.AttackDamage, hits[0].Value, Close(5));
            Assert.AreEqual(0, w.Actors[5].LastAttacker);
            Assert.AreEqual(0, w.Actors[0].AnimAction);
            SimStep.Step(w);
            Assert.AreEqual(1 / Cfg.SimHz, w.Actors[0].AnimAction, Close(9));
        }

        [Test]
        public void CleaveMissesAnEnemyOutsideTheArc90DegreesOffFacingAndOneBeyondRangePlusRadius()
        {
            World w = MakeWorld();
            Place(w, 0, 54, 40);
            Place(w, 5, 54, 41.5); // perpendicular: outside the 120 degree arc
            Place(w, 6, 54 + W.AttackRange + W.Radius + 0.15, 40); // straight ahead but out of reach
            Place(w, 7, 54 + 1.2, 40 - 0.6); // ~26.6 degrees off facing, inside reach: hit
            AimAt(w, 0, 60, 40, true);
            SimStep.Step(w);
            Assert.AreEqual(W.MaxHp, w.Actors[5].Hp);
            Assert.AreEqual(W.MaxHp, w.Actors[6].Hp);
            Assert.AreEqual(Cfg.Class(ClassId.Farmer).MaxHp - W.AttackDamage, w.Actors[7].Hp, Close(5));
        }

        [Test]
        public void NoFriendlyFireATeammateStandingInTheArcTakesNoDamage()
        {
            World w = MakeWorld();
            Place(w, 0, 54, 40);
            Place(w, 1, 55.5, 40); // azure teammate right in front
            Place(w, 5, 55.2, 41); // enemy also in the arc, proves the swing happened
            AimAt(w, 0, 60, 40, true);
            SimStep.Step(w);
            Assert.AreEqual(W.MaxHp, w.Actors[1].Hp);
            Assert.AreEqual(W.MaxHp - W.AttackDamage, w.Actors[5].Hp, Close(5));
            // Direct call: same team gives 0 dealt, no event.
            int head = w.EventHead;
            Assert.AreEqual(0, Damage.ApplyDamage(w, 1, 50, 0, w.X[0], w.Z[0]));
            Assert.AreEqual(W.MaxHp, w.Actors[1].Hp);
            Assert.AreEqual(head, w.EventHead);
        }

        [Test]
        public void AttackCooldownPreventsASecondHitWithinAttackCooldown()
        {
            World w = MakeWorld();
            Place(w, 0, 54, 40);
            Place(w, 5, 55.5, 40);
            AimAt(w, 0, 60, 40, true); // attack held every step
            int cdSteps = (int)JsMath.Round(W.AttackCooldown * Cfg.SimHz); // 21
            SimStep.Step(w);
            Assert.AreEqual(1, Events(w, SimEventType.Hit).Count);
            StepN(w, cdSteps - 1); // still inside the cooldown window
            Assert.AreEqual(1, Events(w, SimEventType.Hit).Count);
            Assert.AreEqual(W.MaxHp - W.AttackDamage, w.Actors[5].Hp, Close(5));
            SimStep.Step(w); // cooldown elapsed exactly
            Assert.AreEqual(2, Events(w, SimEventType.Hit).Count);
            Assert.AreEqual(W.MaxHp - 2 * W.AttackDamage, w.Actors[5].Hp, Close(5));
        }

        [Test]
        public void DeadActorsCannotAttackAndAreNotTargets()
        {
            World w = MakeWorld();
            Place(w, 0, 54, 40);
            Place(w, 5, 55.5, 40);
            Place(w, 6, 52.5, 40);
            Damage.ApplyDamage(w, 5, 1000, -1, 56, 8); // the Kaiju kills 5
            Damage.ApplyDamage(w, 0, 1000, -1, 56, 8); // and 0
            Assert.AreEqual(false, w.Actors[5].Alive);
            Assert.AreEqual(false, w.Actors[0].Alive);
            int hitsBefore = Events(w, SimEventType.Hit).Count;
            AimAt(w, 0, 60, 40, true); // dead attacker swings
            Place(w, 1, 54, 40.2);
            AimAt(w, 1, 60, 40, true); // living teammate swings at the corpse of 5
            w.Actors[1].SpawnProtect = 0;
            SimStep.Step(w);
            Assert.AreEqual(0, w.Actors[5].Hp);
            Assert.AreEqual(W.MaxHp, w.Actors[6].Hp);
            Assert.AreEqual(hitsBefore, Events(w, SimEventType.Hit).Count);
            Assert.AreEqual(54, w.X[0]); // dead actors do not move
        }

        // combat: protection and shield

        [Test]
        public void DamageToASpawnProtectedTargetIs0DirectAndThroughACleaveAtTheEnemyPad()
        {
            World w = MakeWorld();
            Assert.Greater(w.Actors[5].SpawnProtect, 0);
            Assert.AreEqual(0, Damage.ApplyDamage(w, 5, 50, 0, w.X[5] - 1, w.Z[5]));
            Assert.AreEqual(W.MaxHp, w.Actors[5].Hp);

            // Attacker walks up to the crimson pad; target still standing at its spawn slot keeps protection.
            double tx = w.X[5];
            double tz = w.Z[5];
            Place(w, 0, tx - 1.2, tz);
            AimAt(w, 0, tx + 5, tz, true);
            SimStep.Step(w);
            Assert.Greater(w.Actors[5].SpawnProtect, 0);
            Assert.AreEqual(W.MaxHp, w.Actors[5].Hp);
            Assert.AreEqual(0, Events(w, SimEventType.Hit).Count);
        }

        [Test]
        public void SpawnProtectionEndsWhenTheActorIsFartherThanPadRPlus1FromItsPad()
        {
            World w = MakeWorld();
            PadDef pad = w.Map.Pads[(int)TeamId.Crimson];
            SimStep.Step(w);
            Assert.Greater(w.Actors[5].SpawnProtect, 0); // still on the pad
            w.X[5] = JsMath.F32(pad.X - (pad.R + 1.5));
            w.Z[5] = JsMath.F32(pad.Z);
            w.PrevX[5] = w.X[5];
            w.PrevZ[5] = w.Z[5];
            SimStep.Step(w);
            Assert.AreEqual(0, w.Actors[5].SpawnProtect);
        }

        [Test]
        public void SpawnProtectionTicksDownTo0AfterProtectionSecondsEvenOnThePad()
        {
            World w = MakeWorld();
            StepN(w, (int)JsMath.Round(Cfg.Respawn.ProtectionSeconds * Cfg.SimHz));
            for (int i = 0; i < w.ActorCount; i++) Assert.AreEqual(0, w.Actors[i].SpawnProtect);
        }

        [Test]
        public void ASpawnProtectedAttackerLosesItsProtectionWhenItAttacks()
        {
            World w = MakeWorld();
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            Place(w, 5, pad.X + 1.5, pad.Z + 1.5); // raider on the azure pad
            // Actor 0 is on its pad (protected) and swings.
            Assert.Greater(w.Actors[0].SpawnProtect, 0);
            AimAt(w, 0, pad.X + 10, pad.Z, true);
            SimStep.Step(w);
            Assert.AreEqual(0, w.Actors[0].SpawnProtect);
        }

        [Test]
        public void ShieldedWarriorFacingTheAttackerTakes40PercentDamageFullDamageFromBehind()
        {
            World w = MakeWorld();
            double reduction = Cfg.Abilities.WarriorShield.DamageReduction;
            Place(w, 5, 60, 40);
            w.Actors[5].Facing = Math.PI; // facing -x
            w.Actors[5].AbilityActive = 2;
            double front = Damage.ApplyDamage(w, 5, 22, 0, 58, 40);
            Assert.AreEqual(22 * (1 - reduction), front, Close(9));
            Assert.AreEqual(W.MaxHp - 22 * (1 - reduction), w.Actors[5].Hp, Close(9));
            // 50 degrees off facing: still inside +/-60.
            double a50 = Math.PI - 50 * Math.PI / 180;
            Assert.AreEqual(10 * (1 - reduction),
                Damage.ApplyDamage(w, 5, 10, 0, 60 + 2 * JsMath.Cos(a50), 40 + 2 * JsMath.Sin(a50)), Close(9));
            // 70 degrees off facing: outside.
            double a70 = Math.PI - 70 * Math.PI / 180;
            Assert.AreEqual(10,
                Damage.ApplyDamage(w, 5, 10, 0, 60 + 2 * JsMath.Cos(a70), 40 + 2 * JsMath.Sin(a70)), Close(9));
            double back = Damage.ApplyDamage(w, 5, 22, 0, 62, 40);
            Assert.AreEqual(22, back, Close(9));
            // Shield down: full damage from the front.
            w.Actors[5].AbilityActive = 0;
            Assert.AreEqual(22, Damage.ApplyDamage(w, 5, 22, 0, 58, 40), Close(9));
            // A non-warrior with abilityActive gets no shield.
            Place(w, 7, 60, 44);
            w.Actors[7].Facing = Math.PI;
            w.Actors[7].AbilityActive = 2;
            Assert.AreEqual(12, Damage.ApplyDamage(w, 7, 12, 0, 58, 44), Close(9));
        }

        [Test]
        public void ShieldAppliesInsideACleaveToo()
        {
            World w = MakeWorld();
            Place(w, 0, 54, 40);
            Place(w, 5, 55.5, 40);
            AimAt(w, 5, 50, 40, false); // 5 faces the attacker
            w.Actors[5].AbilityActive = 2;
            AimAt(w, 0, 60, 40, true);
            SimStep.Step(w);
            Assert.AreEqual(W.MaxHp - W.AttackDamage * (1 - Cfg.Abilities.WarriorShield.DamageReduction), w.Actors[5].Hp, Close(4));
        }

        // damage: death

        [Test]
        public void LethalDamageGivesAliveFalseRespawnIn5DeathEventKillsDeathsUpdatedCarriedCargoDropped()
        {
            World w = MakeWorld();
            Place(w, 5, 60, 40);
            Actor t = w.Actors[5];
            t.Hp = 10;
            t.Carry = CarryKind.Biomass;
            t.CarryAmount = 1;
            t.GatherProgress = 0.5;
            t.GatherNodeId = 2;
            double dealt = Damage.ApplyDamage(w, 5, W.AttackDamage, 0, 58.5, 40);
            Assert.AreEqual(W.AttackDamage, dealt, Close(9));
            Assert.AreEqual(false, t.Alive);
            Assert.AreEqual(0, t.Hp);
            Assert.AreEqual(Cfg.Respawn.DelaySeconds, t.RespawnIn);
            Assert.AreEqual(5, t.RespawnIn);
            Assert.AreEqual(1, t.Deaths);
            Assert.AreEqual(1, w.Actors[0].Kills);
            Assert.AreEqual(CarryKind.None, t.Carry);
            Assert.AreEqual(0, t.CarryAmount);
            Assert.AreEqual(0, t.GatherProgress);
            Assert.AreEqual(0, t.LastAttacker);
            List<SimEvent> deaths = Events(w, SimEventType.Death);
            Assert.AreEqual(1, deaths.Count);
            Assert.AreEqual(5, deaths[0].A);
            Assert.AreEqual(0, deaths[0].B);
            Assert.AreEqual(60, deaths[0].X, Close(5));
            // Already dead: further damage is ignored.
            Assert.AreEqual(0, Damage.ApplyDamage(w, 5, 50, 0, 58.5, 40));
            Assert.AreEqual(1, t.Deaths);
            Assert.AreEqual(1, w.Actors[0].Kills);
        }

        [Test]
        public void AKaijuKillSource1CountsADeathButNoActorKillDamageIsIgnoredOnceTheMatchEnded()
        {
            World w = MakeWorld();
            Place(w, 5, 60, 40);
            int killsBefore = SumKills(w);
            Damage.ApplyDamage(w, 5, 1000, -1, 56, 30);
            Assert.AreEqual(false, w.Actors[5].Alive);
            Assert.AreEqual(1, w.Actors[5].Deaths);
            Assert.AreEqual(-1, w.Actors[5].LastAttacker);
            Assert.AreEqual(killsBefore, SumKills(w));

            Place(w, 6, 62, 40);
            w.Match.Phase = MatchPhase.Ended;
            Assert.AreEqual(0, Damage.ApplyDamage(w, 6, 50, 0, 61, 40));
            Assert.AreEqual(W.MaxHp, w.Actors[6].Hp);
        }

        /// <summary>w.actors.reduce((s, a) => s + a.kills, 0).</summary>
        private static int SumKills(World w)
        {
            int s = 0;
            for (int i = 0; i < w.Actors.Length; i++) s += w.Actors[i].Kills;
            return s;
        }

        // combat: determinism and stress

        [Test]
        public void Case16v16BrawlAtTheHillFor1200StepsDeterministicHpIn0MaxHpDeadActorsStayPut()
        {
            bool sawDead = false;
            World Run()
            {
                World w = World.CreateWorld(new WorldOptions
                {
                    Seed = 99,
                    TeamSize = 16,
                    HumanTeam = TeamId.Crimson,
                    HumanClass = ClassId.Farmer,
                    Config = SimPhysicsTests.ClassicBrowser(),
                });
                InputFrame f = SimInput.EmptyInput();
                var deadBefore = new byte[w.ActorCount];
                for (int t = 0; t < 1200; t++)
                {
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        double dx = 56 - w.X[i];
                        double dz = 40 - w.Z[i];
                        // Math.hypot(dx, dz) || 1; only self-determinism is checked, so Math.Sqrt is enough here.
                        double d = Math.Sqrt(dx * dx + dz * dz);
                        if (d == 0 || double.IsNaN(d)) d = 1;
                        f.MoveX = dx / d;
                        f.MoveZ = dz / d;
                        f.AimX = 56;
                        f.AimZ = 40;
                        f.Attack = true;
                        f.ClassPick = (t + i) % 3;
                        SimInput.ApplyInput(w, i, f);
                        deadBefore[i] = (byte)(w.Actors[i].Alive ? 0 : 1);
                    }
                    SimStep.Step(w);
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        Actor a = w.Actors[i];
                        if (a.Hp < 0 || a.Hp > a.MaxHp) Assert.Fail($"hp out of range for {i}: {a.Hp}");
                        // Dead for the whole step: did not move.
                        if (deadBefore[i] == 1 && !a.Alive && (w.X[i] != w.PrevX[i] || w.Z[i] != w.PrevZ[i]))
                        {
                            Assert.Fail($"dead actor {i} moved");
                        }
                        if (!a.Alive) sawDead = true;
                    }
                }
                return w;
            }

            World ra = Run();
            World rb = Run();
            CollectionAssert.AreEqual(rb.X, ra.X);
            Assert.AreEqual(rb.Actors.Length, ra.Actors.Length);
            for (int i = 0; i < ra.Actors.Length; i++) Assert.AreEqual(rb.Actors[i].Hp, ra.Actors[i].Hp);
            int deaths = 0;
            int kills = 0;
            for (int i = 0; i < ra.Actors.Length; i++)
            {
                deaths += ra.Actors[i].Deaths;
                kills += ra.Actors[i].Kills;
            }
            Assert.Greater(deaths, 0);
            Assert.AreEqual(deaths, kills); // no Kaiju in this fight: every death has an actor killer
            Assert.AreEqual(true, sawDead);
        }
    }
}
