using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/clarity.test.ts describe "CM-28 barricade refusal reasons (ability event value &lt; 0)"
    /// (09e0904, lines 197-272).
    /// </summary>
    [TestFixture]
    public sealed class ClarityBarricadeTests
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

        private static void Place(World w, int id, double x, double z)
        {
            w.X[id] = x;
            w.Z[id] = z;
            w.PrevX[id] = x;
            w.PrevZ[id] = z;
            w.Actors[id].SpawnProtect = 0;
        }

        private static void ClearOthers(World w, params int[] keep)
        {
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (System.Array.IndexOf(keep, i) >= 0) continue;
                w.Actors[i].Alive = false;
                w.Actors[i].RespawnIn = 1e9;
            }
        }

        /// <summary>One step with ability held for actor 0, then release; returns a copy of its first Ability event of that step, or null.</summary>
        private static SimEvent Cast(World w, double ax, double az)
        {
            InputFrame f = SimInput.EmptyInput();
            f.AimX = ax;
            f.AimZ = az;
            f.Ability = true;
            SimInput.ApplyInput(w, 0, f);
            int head = w.EventHead;
            SimStep.Step(w, 1.0 / w.Config.SimHz);
            f.Ability = false;
            SimInput.ApplyInput(w, 0, f);
            for (int h = head; h < w.EventHead; h++)
            {
                SimEvent e = w.Events[h % World.EventCapacity];
                if (e.Type == SimEventType.Ability && e.A == 0)
                    return new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value };
            }
            return null;
        }

        [TestCase("hill", 56.5, 42.5, 56.5, 40.5, Barricades.RefusedHill, double.NaN, double.NaN)]
        [TestCase("pad", 12.5, 36.5, 10.5, 36.5, Barricades.RefusedOccupied, double.NaN, double.NaN)]
        [TestCase("node", 24.5, 18.5, 24.5, 16.5, Barricades.RefusedOccupied, double.NaN, double.NaN)]
        [TestCase("actor", 27.5, 16.5, 27.5, 18.5, Barricades.RefusedOccupied, 28.2, 18.9)]
        [TestCase("wall", 52.5, 32.5, 52.5, 30.5, Barricades.RefusedOccupied, double.NaN, double.NaN)]
        public void NameTheRefusedCastCarriesReasonCode(string name, double minerX, double minerZ, double aimX, double aimZ, int code, double occX, double occZ)
        {
            World w = MakeWorld(ClassId.Miner);
            ClearOthers(w, 0, 5);
            Place(w, 0, minerX, minerZ);
            // A NaN occupant means the vitest case has none.
            if (!double.IsNaN(occX)) Place(w, 5, occX, occZ);
            else Place(w, 5, 100, 36);
            Assert.AreEqual(code, Barricades.BarricadeRefusal(w, 0, aimX, aimZ), name);
            SimEvent e = Cast(w, aimX, aimZ);
            Assert.IsNotNull(e, name);
            Assert.AreEqual((double)code, e.Value, name);
            Assert.Less(e.Value, 0.0, name);
            Assert.AreEqual(0.0, w.Actors[0].AbilityCd, name);
        }

        [Test]
        public void ACellThatWouldCutABaseOffIsRefusedWithTheRouteReason()
        {
            World w = MakeWorld(ClassId.Miner);
            ClearOthers(w, 0);
            for (int cz = 30; cz <= 42; cz++)
            {
                for (int cx = 2; cx <= 14; cx++)
                {
                    bool edge = cz == 30 || cz == 42 || cx == 2 || cx == 14;
                    if (edge && !(cx == 14 && cz == 36)) MapGrid.SetCell(w, cx, cz, Cell.Wall);
                }
            }
            Place(w, 0, 16.5, 36.5);
            Assert.AreEqual(Barricades.RefusedRoute, Barricades.BarricadeRefusal(w, 0, 14.5, 36.5));
            SimEvent e = Cast(w, 14.5, 36.5);
            Assert.IsNotNull(e);
            Assert.AreEqual((double)Barricades.RefusedRoute, e.Value);
        }

        [Test]
        public void ATargetOffTheIslandVoidOrANonFiniteAimIsOutOfRangeAValidCellReturns0()
        {
            World w = MakeWorld(ClassId.Miner);
            ClearOthers(w, 0);
            // Find a floor cell whose neighbour 1.5 cells away is void: the miner stands on it and aims into the void.
            MapDef map = w.Map;
            bool found = false;
            for (int cz = 1; cz < map.Height - 1 && !found; cz++)
            {
                for (int cx = 1; cx < map.Width - 1 && !found; cx++)
                {
                    if (map.Cells[cz * map.Width + cx] != Cell.Floor) continue;
                    if (map.Cells[cz * map.Width + cx - 1] != Cell.Void) continue;
                    Place(w, 0, cx + 0.5, cz + 0.5);
                    Assert.AreEqual(Barricades.RefusedRange, Barricades.BarricadeRefusal(w, 0, cx - 0.5, cz + 0.5));
                    found = true;
                }
            }
            Assert.AreEqual(true, found);
            Assert.AreEqual(Barricades.RefusedRange, Barricades.BarricadeRefusal(w, 0, double.NaN, 3));
            Place(w, 0, 27.5, 16.5);
            Assert.AreEqual(0, Barricades.BarricadeRefusal(w, 0, 27.5, 18.5));
        }
    }
}
