using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/abilities.test.ts describe "miner barricade placement" (09e0904, lines 193-359). 5v5, human
    /// azure miner: azure 0 M, 1 W, 2 W, 3 F, 4 F; crimson 5 W, 6 W, 7 F, 8 F, 9 M. Placement fixtures live in the NW
    /// courtyard (28, 18).
    /// </summary>
    [TestFixture]
    public sealed class BarricadeTests
    {
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

        private static double Dt(World w)
        {
            return 1.0 / w.Config.SimHz;
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

        /// <summary>Every actor except keep leaves the board (dead, never respawning).</summary>
        private static void ClearOthers(World w, params int[] keep)
        {
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (System.Array.IndexOf(keep, i) >= 0) continue;
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
            int from = System.Math.Max(0, w.EventHead - World.EventCapacity);
            for (int i = from; i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) output.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value });
            }
            return output;
        }

        private static byte CellOf(World w, int cx, int cz)
        {
            return w.Map.Cells[cz * w.Map.Width + cx];
        }

        private static List<(int cx, int cz)> ActiveBarricades(World w, int owner)
        {
            var output = new List<(int cx, int cz)>();
            foreach (Barricade b in w.Barricades)
                if (b.Active && b.Owner == owner) output.Add((b.Cx, b.Cz));
            return output;
        }

        private static Barricade FirstActive(World w)
        {
            foreach (Barricade b in w.Barricades)
                if (b.Active) return b;
            return null;
        }

        [Test]
        public void PlacesOnValidFLOORBARRICADECellSlotHpTtlCooldownEvent()
        {
            World w = MakeWorld(ClassId.Miner);
            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            ClearOthers(w, 0);
            Place(w, 0, 27.5, 16.5);
            Assert.AreEqual(true, Barricades.CanPlaceBarricade(w, 0, 27.5, 18.5));
            int gv = w.GridVersion;
            Cast(w, 0, 27.5, 18.5);
            Assert.AreEqual(Cell.Barricade, CellOf(w, 27, 18));
            Assert.AreEqual(gv + 1, w.GridVersion);
            Barricade b = FirstActive(w);
            Assert.IsNotNull(b);
            Assert.AreEqual(0, b.Owner);
            Assert.AreEqual(TeamId.Azure, b.Team);
            Assert.AreEqual(27, b.Cx);
            Assert.AreEqual(18, b.Cz);
            Assert.AreEqual(mb.MaxHp, b.Hp);
            Assert.AreEqual(mb.LifetimeSeconds - Dt(w), b.Ttl, 0.5e-6);
            Assert.AreEqual(w.Config.Class(ClassId.Miner).AbilityCooldown - Dt(w), w.Actors[0].AbilityCd, 0.5e-6);
            Assert.AreEqual(1, Events(w, SimEventType.BarricadePlaced).Count);
        }

        [Test]
        public void ClampsTheTargetToPlaceRangeFromTheMiner()
        {
            World w = MakeWorld(ClassId.Miner);
            ClearOthers(w, 0);
            Place(w, 0, 27.5, 14.5);
            Cast(w, 0, 27.5, 24.5); // clamped to (27.5, 16.5)
            Assert.AreEqual(Cell.Barricade, CellOf(w, 27, 16));
        }

        private sealed class RejectCase
        {
            public string Name;
            public double MinerX, MinerZ, AimX, AimZ;
            public bool HasOccupant;
            public double OccX, OccZ;
        }

        private static RejectCase Rc(string name, double mx, double mz, double ax, double az)
        {
            return new RejectCase { Name = name, MinerX = mx, MinerZ = mz, AimX = ax, AimZ = az };
        }

        [Test]
        public void IsRejectedOnAPadTheHillTheShrineANodeAndAnOccupiedCellNoCooldownAbilityEventValueLt0()
        {
            RejectCase occupied = Rc("occupied", 27.5, 16.5, 27.5, 18.5);
            occupied.HasOccupant = true;
            occupied.OccX = 28.2;
            occupied.OccZ = 18.9;
            RejectCase[] cases =
            {
                Rc("pad", 12.5, 36.5, 10.5, 36.5),
                Rc("hill", 56.5, 42.5, 56.5, 40.5),
                Rc("shrine", 58.5, 10.5, 56.5, 10.5),
                Rc("node", 24.5, 18.5, 24.5, 16.5),
                occupied,
                Rc("blocked (hill-ring breakable)", 52.5, 32.5, 52.5, 30.5),
            };
            foreach (RejectCase c in cases)
            {
                World w = MakeWorld(ClassId.Miner);
                ClearOthers(w, 0, 5);
                Place(w, 0, c.MinerX, c.MinerZ);
                if (c.HasOccupant) Place(w, 5, c.OccX, c.OccZ);
                else Place(w, 5, 100, 36);
                int cx = JsMath.FloorToInt(c.AimX);
                int cz = JsMath.FloorToInt(c.AimZ);
                byte before = CellOf(w, cx, cz);
                Assert.AreEqual(false, Barricades.CanPlaceBarricade(w, 0, c.AimX, c.AimZ), c.Name);
                Cast(w, 0, c.AimX, c.AimZ);
                Assert.AreEqual(before, CellOf(w, cx, cz), c.Name);
                Assert.AreEqual(0.0, w.Actors[0].AbilityCd, c.Name);
                Assert.AreEqual(0, ActiveBarricades(w, 0).Count, c.Name);
                List<SimEvent> ab = Events(w, SimEventType.Ability);
                Assert.AreEqual(1, ab.Count, c.Name);
                // CM-28: the value is the refusal reason, always lt 0 (-1 occupied, -2 hill; Barricades.Refused*).
                Assert.AreEqual(c.Name == "hill" ? -2.0 : -1.0, ab[0].Value, c.Name);
                Assert.AreEqual(0, Events(w, SimEventType.BarricadePlaced).Count, c.Name);
            }
        }

        [Test]
        public void IsRejectedWhenItWouldDisconnectABaseFromTheHillOrTheShrineTestCorridorBuiltWithSetCell()
        {
            World w = MakeWorld(ClassId.Miner);
            ClearOthers(w, 0);
            // Wall the Azure pad in at Chebyshev distance 6 from its centre cell (8, 36), leaving the one cell (14, 36) open.
            for (int cz = 30; cz <= 42; cz++)
            {
                for (int cx = 2; cx <= 14; cx++)
                {
                    bool edge = cz == 30 || cz == 42 || cx == 2 || cx == 14;
                    if (edge && !(cx == 14 && cz == 36)) MapGrid.SetCell(w, cx, cz, Cell.Wall);
                }
            }
            Assert.AreEqual(true, MapGrid.IsWalkable(w.Map, 14, 36));
            Place(w, 0, 16.5, 36.5);
            // The corridor cell is the base's only way out.
            Assert.AreEqual(false, Barricades.CanPlaceBarricade(w, 0, 14.5, 36.5));
            Cast(w, 0, 14.5, 36.5);
            Assert.AreEqual(Cell.Floor, CellOf(w, 14, 36));
            // CM-28: the route reason (Barricades.RefusedRoute), still lt 0.
            Assert.AreEqual(-3.0, Events(w, SimEventType.Ability)[0].Value);
            // A cell beside the corridor (outside the wall) is fine.
            Assert.AreEqual(true, Barricades.CanPlaceBarricade(w, 0, 16.5, 34.5));
        }

        [Test]
        public void AThirdBarricadeRemovesTheOldestNeverMoreThan2ActivePerMiner()
        {
            World w = MakeWorld(ClassId.Miner);
            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            ClearOthers(w, 0);
            Place(w, 0, 27.5, 18.5);
            double[][] targets =
            {
                new[] { 25.5, 18.5 },
                new[] { 29.5, 18.5 },
                new[] { 27.5, 16.5 },
            };
            foreach (double[] t in targets)
            {
                w.Actors[0].AbilityCd = 0;
                Cast(w, 0, t[0], t[1]);
                StepN(w, 3);
                Assert.LessOrEqual(ActiveBarricades(w, 0).Count, mb.MaxActivePerMiner);
            }
            List<(int cx, int cz)> act = ActiveBarricades(w, 0);
            Assert.AreEqual(2, act.Count);
            CollectionAssert.Contains(act, (29, 18));
            CollectionAssert.Contains(act, (27, 16));
            Assert.AreEqual(Cell.Floor, CellOf(w, 25, 18));
            Assert.AreEqual(Cell.Barricade, CellOf(w, 29, 18));
            Assert.AreEqual(Cell.Barricade, CellOf(w, 27, 16));
            Assert.AreEqual(3, Events(w, SimEventType.BarricadePlaced).Count);
            Assert.AreEqual(1, Events(w, SimEventType.BarricadeBroken).Count);
        }

        [Test]
        public void ExpiresAfterLifetimeSeconds20SAndRestoresThePreviousCellTypeBRIDGEStaysBRIDGE()
        {
            World w = MakeWorld(ClassId.Miner);
            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            ClearOthers(w, 0);
            // A bridge cell on B1 (base -> central), far from both pads and the hill.
            int bx = -1;
            for (int cx = 24; cx < 34 && bx < 0; cx++) if (CellOf(w, cx, 38) == Cell.Bridge) bx = cx;
            Assert.Greater(bx, 0);
            // Bridges are 3 cells wide; blocking one cell keeps every route open.
            Place(w, 0, bx - 1.5, 38.5);
            Cast(w, 0, bx + 0.5, 38.5);
            Assert.AreEqual(Cell.Barricade, CellOf(w, bx, 38));
            int steps = (int)JsMath.Round(mb.LifetimeSeconds * w.Config.SimHz); // 600 including the placing step
            StepN(w, steps - 2);
            Assert.AreEqual(Cell.Barricade, CellOf(w, bx, 38));
            SimStep.Step(w);
            Assert.AreEqual(Cell.Bridge, CellOf(w, bx, 38));
            Assert.AreEqual(0, ActiveBarricades(w, 0).Count);
            List<SimEvent> br = Events(w, SimEventType.BarricadeBroken);
            Assert.AreEqual(1, br.Count);
            Assert.AreEqual(0, br[0].A);
        }

        [Test]
        public void EnemyCleavesDamageABarricadeAllySwingsDoNotAndItBreaksAt0Hp()
        {
            World w = MakeWorld(ClassId.Miner);
            MinerBarricadeConfig mb = w.Config.Abilities.MinerBarricade;
            ClassConfig warrior = w.Config.Class(ClassId.Warrior);
            ClearOthers(w, 0, 1, 5);
            Place(w, 0, 27.5, 16.5);
            Cast(w, 0, 27.5, 18.5);
            Barricade b = FirstActive(w);
            Assert.IsNotNull(b);
            // Ally warrior 1 swings at it from the west: no damage.
            Place(w, 1, 26.2, 18.5);
            Input(w, 1, 30, 18.5, attack: true);
            SimStep.Step(w);
            Input(w, 1, 30, 18.5);
            Assert.AreEqual(mb.MaxHp, b.Hp);
            // Enemy warrior 5 from the east.
            Place(w, 5, 28.8, 18.5);
            Input(w, 5, 20, 18.5, attack: true);
            SimStep.Step(w);
            Assert.AreEqual(mb.MaxHp - warrior.AttackDamage, b.Hp);
            // Keep swinging: 120 / 22 -> 6 swings.
            StepN(w, (int)JsMath.Round(warrior.AttackCooldown * w.Config.SimHz) * 6);
            Assert.AreEqual(false, b.Active);
            Assert.AreEqual(Cell.Floor, CellOf(w, 27, 18));
            Assert.AreEqual(1, Events(w, SimEventType.BarricadeBroken).Count);
        }

        [Test]
        public void CanPlaceBarricadeMemoisesItsConnectivityProofByCellGridVersion1000CallsToAtMost2FlowFields()
        {
            World w = MakeWorld(ClassId.Miner);
            ClearOthers(w, 0);
            Place(w, 0, 27.5, 16.5);
            // The vitest spies on computeFlowField; FlowField.ComputeCalls counts every call.
            int start = FlowField.ComputeCalls;
            for (int i = 0; i < 1000; i++) Assert.AreEqual(true, Barricades.CanPlaceBarricade(w, 0, 27.5, 18.5));
            int calls = FlowField.ComputeCalls - start;
            Assert.Greater(calls, 0);
            Assert.LessOrEqual(calls, 2);
            // A grid change invalidates the proof.
            MapGrid.SetCell(w, 0, 0, Cell.Wall);
            int before = FlowField.ComputeCalls;
            Assert.AreEqual(true, Barricades.CanPlaceBarricade(w, 0, 27.5, 18.5));
            Assert.Greater(FlowField.ComputeCalls, before);
            // The proof never leaves a trace in the grid.
            Assert.AreEqual(Cell.Floor, CellOf(w, 27, 18));
        }
    }
}
