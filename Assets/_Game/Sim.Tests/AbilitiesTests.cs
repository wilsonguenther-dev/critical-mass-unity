using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/abilities.test.ts (09e0904): describes "warrior shield", "farmer roots" and "miner wall-break"
    /// (8 cases). The 8 "miner barricade placement" cases are F-05e's BarricadeTests.
    /// 5v5, human azure, seed 11. Rosters (CM-01 largest remainder, human class first):
    ///   human warrior: azure 0 W, 1 W, 2 F, 3 F, 4 M      crimson 5 W, 6 W, 7 F, 8 F, 9 M
    ///   human farmer:  azure 0 F, 1 W, 2 W, 3 F, 4 M      crimson 5 W, 6 W, 7 F, 8 F, 9 M
    ///   human miner:   azure 0 M, 1 W, 2 W, 3 F, 4 F      crimson 5 W, 6 W, 7 F, 8 F, 9 M
    /// </summary>
    [TestFixture]
    public sealed class AbilitiesTests
    {
        private static readonly GameConfig Cfg = GameConfig.CreateBrowser5v5();
        private static readonly double Dt = 1.0 / Cfg.SimHz;
        private static readonly AbilitiesConfig AB = Cfg.Abilities;

        private static World MakeWorld(ClassId cls)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 11,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = cls,
                Config = GameConfig.CreateBrowser5v5(),
            });
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

        /// <summary>Every actor except `keep` leaves the board (dead, never respawning).</summary>
        private static void ClearOthers(World w, params int[] keep)
        {
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (Array.IndexOf(keep, i) >= 0) continue;
                w.Actors[i].Alive = false;
                w.Actors[i].RespawnIn = 1e9;
            }
        }

        private static void Input(World w, int id, double ax, double az, bool ability = false, bool attack = false)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = ax;
            f.AimZ = az;
            f.Ability = ability;
            f.Attack = attack;
            SimInput.ApplyInput(w, id, f);
        }

        /// <summary>One step with ability held, then release (inputs persist across steps).</summary>
        private static void Cast(World w, int id, double ax, double az)
        {
            Input(w, id, ax, az, ability: true);
            SimStep.Step(w);
            Input(w, id, ax, az);
        }

        private static void StepN(World w, int n)
        {
            for (int i = 0; i < n; i++) SimStep.Step(w);
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

        private static byte CellOf(World w, int cx, int cz)
        {
            return w.Map.Cells[cz * w.Map.Width + cx];
        }

        /// <summary>toBeCloseTo(v, d) tolerance.</summary>
        private static double Close(int digits)
        {
            return 0.5 * Math.Pow(10, -digits);
        }

        // ---- warrior shield ----

        [Test]
        public void SetsAbilityActiveAndTheClassCooldownOncePerCooldown()
        {
            World w = MakeWorld(ClassId.Warrior);
            Place(w, 0, 54, 40);
            Cast(w, 0, 60, 40);
            // Cast inside the step, then this step's timers tick once (combat).
            Assert.AreEqual(AB.WarriorShield.DurationSeconds - Dt, w.Actors[0].AbilityActive, Close(6));
            Assert.AreEqual(Cfg.Class(ClassId.Warrior).AbilityCooldown - Dt, w.Actors[0].AbilityCd, Close(6));
            List<SimEvent> ab = Events(w, SimEventType.Ability);
            Assert.AreEqual(1, ab.Count);
            Assert.AreEqual(0, ab[0].A);
            Assert.AreEqual(1.0, ab[0].Value);
            // Held through the cooldown: no recast, the shield runs out after exactly 2.5 s.
            Input(w, 0, 60, 40, ability: true);
            StepN(w, (int)JsMath.Round(AB.WarriorShield.DurationSeconds * Cfg.SimHz) - 1);
            Assert.AreEqual(0.0, w.Actors[0].AbilityActive);
            Assert.AreEqual(1, Events(w, SimEventType.Ability).Count);
        }

        [Test]
        public void IsReadByDamageTsFrontalReductionAndMovementTsSpeedMultiplier()
        {
            World w = MakeWorld(ClassId.Warrior);
            ClearOthers(w, 0, 5);
            Place(w, 0, 54, 40);
            Place(w, 5, 55.5, 40);
            w.Actors[0].Facing = 0;
            Cast(w, 0, 60, 40);
            // Enemy warrior 5 faces west toward 0 and swings into the raised shield.
            Input(w, 5, 50, 40, attack: true);
            SimStep.Step(w);
            ClassConfig wc = Cfg.Class(ClassId.Warrior);
            Assert.AreEqual(wc.MaxHp - wc.AttackDamage * (1 - AB.WarriorShield.DamageReduction), w.Actors[0].Hp, Close(5));

            // Speed: shielded warrior covers moveSpeedMultiplier of the unshielded distance in the same steps.
            World a = MakeWorld(ClassId.Warrior);
            World b = MakeWorld(ClassId.Warrior);
            foreach (World v in new[] { a, b })
            {
                ClearOthers(v, 0);
                Place(v, 0, 52, 38);
            }
            Cast(a, 0, 60, 38);
            InputFrame f = SimInput.EmptyInput();
            f.MoveX = 1;
            f.AimX = double.NaN;
            f.AimZ = double.NaN;
            SimInput.ApplyInput(a, 0, f);
            SimInput.ApplyInput(b, 0, f);
            SimStep.Step(b); // b's first step is the one a spent casting
            // CM-21: both reach their top speed (acceleration takes 0.12 s) before the distances are compared.
            StepN(a, 6);
            StepN(b, 6);
            double ax0 = a.X[0];
            double bx0 = b.X[0];
            StepN(a, 10);
            StepN(b, 10);
            Assert.AreEqual(AB.WarriorShield.MoveSpeedMultiplier, (a.X[0] - ax0) / (b.X[0] - bx0), Close(3));
        }

        // ---- farmer roots ----

        [Test]
        public void SlowOnlyEnemiesInsideTheRadiusAroundTheClampedAimPointNeverAlliesCooldownAndEvent()
        {
            World w = MakeWorld(ClassId.Farmer);
            Place(w, 0, 54, 40);
            Place(w, 5, 58.5, 40); // enemy, 0.5 from the target point
            Place(w, 6, 62, 40); // enemy, 4 from the target point (outside 2.5)
            Place(w, 1, 58, 41); // ally, inside the radius
            Place(w, 7, 56.5, 38.5); // enemy, 2.12 from the target point (inside)
            Cast(w, 0, 58, 40);
            double s = AB.FarmerRoots.SlowSeconds - Dt;
            Assert.AreEqual(s, w.Actors[5].SlowTimer, Close(6));
            Assert.AreEqual(s, w.Actors[7].SlowTimer, Close(6));
            Assert.AreEqual(0.0, w.Actors[6].SlowTimer);
            Assert.AreEqual(0.0, w.Actors[1].SlowTimer);
            Assert.AreEqual(0.0, w.Actors[0].SlowTimer);
            Assert.AreEqual(Cfg.Class(ClassId.Farmer).AbilityCooldown - Dt, w.Actors[0].AbilityCd, Close(6));
            List<SimEvent> ab = Events(w, SimEventType.Ability);
            Assert.AreEqual(1, ab.Count);
            Assert.AreEqual(0, ab[0].A);
            Assert.AreEqual(1.0, ab[0].Value);
            Assert.AreEqual(58, ab[0].X, Close(5));
            Assert.AreEqual(40, ab[0].Z, Close(5));
        }

        [Test]
        public void ClampsTheSeedToCastRangeFromTheFarmer()
        {
            World w = MakeWorld(ClassId.Farmer);
            Place(w, 0, 50, 40);
            Place(w, 5, 56.2, 40); // 0.2 past the clamped point (56, 40)
            Place(w, 6, 64, 40); // at the unclamped aim point
            Cast(w, 0, 64, 40);
            SimEvent ev = Events(w, SimEventType.Ability)[0];
            Assert.AreEqual(50 + AB.FarmerRoots.CastRange, ev.X, Close(5));
            Assert.Greater(w.Actors[5].SlowTimer, 0.0);
            Assert.AreEqual(0.0, w.Actors[6].SlowTimer);
        }

        [Test]
        public void FarmerSBiggerCarryAndFasterGatheringAreConfigValuesThatDifferFromTheOtherClasses()
        {
            ClassConfig farmer = Cfg.Class(ClassId.Farmer);
            ClassConfig warrior = Cfg.Class(ClassId.Warrior);
            ClassConfig miner = Cfg.Class(ClassId.Miner);
            Assert.AreEqual(3.0, farmer.CarryCapacity);
            Assert.Greater(farmer.CarryCapacity, warrior.CarryCapacity);
            Assert.Greater(farmer.CarryCapacity, miner.CarryCapacity);
            Assert.AreEqual(1.0, farmer.GatherSeconds);
            Assert.Less(farmer.GatherSeconds, warrior.GatherSeconds);
            Assert.Less(farmer.GatherSeconds, miner.GatherSeconds);
        }

        // ---- miner wall-break ----

        private static World BreakableFixture(ClassId cls, out int i)
        {
            World w = MakeWorld(cls);
            ClearOthers(w, 0);
            MapGrid.SetCell(w, 27, 18, Cell.BreakableIntact);
            i = MapGrid.CellIndex(w.Map, 27, 18);
            w.Map.WallHp[i] = (byte)(2 * AB.MinerWallBreak.HitsPerState);
            Place(w, 0, 27.5, 17.3); // 1.2 north of the cell centre
            return w;
        }

        private static void SwingOnce(World w)
        {
            Input(w, 0, 27.5, 30, attack: true);
            SimStep.Step(w);
            Input(w, 0, 27.5, 30);
            while (w.Actors[0].AttackCd > 0) SimStep.Step(w);
        }

        [Test]
        public void AMinerBreaksAWallIn2HitsINTACTToCRACKEDToDESTROYEDAndTheRubbleIsWalkable()
        {
            World w = BreakableFixture(ClassId.Miner, out _);
            SwingOnce(w);
            Assert.AreEqual(Cell.BreakableCracked, CellOf(w, 27, 18));
            Assert.AreEqual(false, MapGrid.IsWalkable(w.Map, 27, 18));
            SwingOnce(w);
            Assert.AreEqual(Cell.BreakableDestroyed, CellOf(w, 27, 18));
            Assert.AreEqual(true, MapGrid.IsWalkable(w.Map, 27, 18));
            List<SimEvent> wb = Events(w, SimEventType.WallBreak);
            Assert.AreEqual(2, wb.Count);
            Assert.AreEqual((double)Cell.BreakableCracked, wb[0].Value);
            Assert.AreEqual((double)Cell.BreakableDestroyed, wb[1].Value);
            Assert.AreEqual(0, wb[0].A);
        }

        [Test]
        public void AWarriorSAttackNeverChangesABreakableWallAndNobodyDamagesWALL()
        {
            World w = BreakableFixture(ClassId.Warrior, out _);
            for (int k = 0; k < 6; k++) SwingOnce(w);
            Assert.AreEqual(Cell.BreakableIntact, CellOf(w, 27, 18));
            Assert.AreEqual(0, Events(w, SimEventType.WallBreak).Count);

            World m = MakeWorld(ClassId.Miner);
            ClearOthers(m, 0);
            MapGrid.SetCell(m, 27, 18, Cell.Wall);
            Place(m, 0, 27.5, 17.3);
            for (int k = 0; k < 4; k++) SwingOnce(m);
            Assert.AreEqual(Cell.Wall, CellOf(m, 27, 18));
            Assert.AreEqual(0, Events(m, SimEventType.WallBreak).Count);
        }

        [Test]
        public void ABreakableBehindTheMinerOutsidePlusMinus45DegreesIsNotHit()
        {
            World w = BreakableFixture(ClassId.Miner, out _);
            Input(w, 0, 27.5, 0, attack: true); // facing north, away from the wall
            SimStep.Step(w);
            Assert.AreEqual(Cell.BreakableIntact, CellOf(w, 27, 18));
        }
    }
}
