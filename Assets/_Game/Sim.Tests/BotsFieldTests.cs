using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/bots/bots.test.ts describe "bots (e): breakable walls and flow fields" (3 cases).</summary>
    [TestFixture]
    public sealed class BotsFieldTests
    {
        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private static World MakeWorld(int seed, int teamSize)
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = teamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
        }

        /// <summary>One SimDriver step with every actor (human included) on bots.</summary>
        private static void TickOnce(World w)
        {
            double dt = SimStep.Dt(w.Config);
            BotBrain.FillBotInput(w, 0, Frame);
            SimInput.ApplyInput(w, 0, Frame);
            BotBrain.BotsThink(w, dt);
            for (int id = 1; id < w.ActorCount; id++)
            {
                BotBrain.FillBotInput(w, id, Frame);
                SimInput.ApplyInput(w, id, Frame);
            }
            SimStep.Step(w, dt);
        }

        private static void Run(World w, double seconds)
        {
            int steps = (int)JsMath.Round(seconds * w.Config.SimHz);
            for (int s = 0; s < steps; s++) TickOnce(w);
        }

        /// <summary>JS Math.max of two numbers (NaN if either is NaN).</summary>
        private static double JsMax(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            return Math.Max(a, b);
        }

        private static double At(double[] d, PointXZ p, int W)
        {
            return d[JsMath.FloorToInt(p.Z) * W + JsMath.FloorToInt(p.X)];
        }

        [Test]
        public void EP2DestroyingSomeBREAKABLECellShortensANodePadRouteToTheHillCentreOrShrineRingByGe2()
        {
            World w = MakeWorld(1, 5);
            MapDef m = w.Map;
            int W = m.Width;
            PointXZ[] hillGoal = { new PointXZ(m.Hill.X, m.Hill.Z) };
            List<PointXZ> shrineGoal = new List<PointXZ>();
            double sr = w.Config.KaijuFeed.ShrineInteractRange;
            for (int cz = 0; cz < m.Height; cz++)
                for (int cx = 0; cx < W; cx++)
                {
                    double dx = cx + 0.5 - m.Shrine.X;
                    double dz = cz + 0.5 - m.Shrine.Z;
                    if (dx * dx + dz * dz <= sr * sr) shrineGoal.Add(new PointXZ(cx + 0.5, cz + 0.5));
                }
            // Sources: the walkable cell touching each node (nodes sit on cell corners) and the pad centres.
            List<PointXZ> sources = new List<PointXZ>();
            foreach (PointXZ p in m.BiomassNodes) sources.Add(new PointXZ(p.X - 0.5, p.Z - 0.5));
            foreach (PointXZ p in m.CorruptionNodes) sources.Add(new PointXZ(p.X - 0.5, p.Z - 0.5));
            PadDef azure = m.Pads[(int)TeamId.Azure];
            PadDef crimson = m.Pads[(int)TeamId.Crimson];
            sources.Add(new PointXZ(azure.X, azure.Z));
            sources.Add(new PointXZ(crimson.X, crimson.Z));
            double[] baseH = (double[])FlowField.ComputeFlowField(m, hillGoal).Dist.Clone();
            double[] baseS = (double[])FlowField.ComputeFlowField(m, shrineGoal).Dist.Clone();
            List<string> report = new List<string>();
            int qualifying = 0;
            int breakables = 0;
            for (int i = 0; i < m.Cells.Length; i++)
            {
                byte t = m.Cells[i];
                if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) continue;
                breakables++;
                int cx = i % W;
                int cz = (int)((double)(i - cx) / W);
                byte hp = m.WallHp[i];
                MapGrid.SetCell(w, cx, cz, Cell.BreakableDestroyed);
                double[] h = FlowField.ComputeFlowField(m, hillGoal).Dist;
                double best = 0;
                foreach (PointXZ p in sources) best = JsMax(best, At(baseH, p, W) - At(h, p, W));
                double[] s = FlowField.ComputeFlowField(m, shrineGoal).Dist;
                foreach (PointXZ p in sources) best = JsMax(best, At(baseS, p, W) - At(s, p, W));
                MapGrid.SetCell(w, cx, cz, t);
                m.WallHp[i] = hp;
                report.Add("(" + cx + "," + cz + ") " + best.ToString("F2", CultureInfo.InvariantCulture));
                if (best >= 2) qualifying++;
            }
            Assert.Greater(breakables, 0);
            // NEEDS_MAP when this fails: the numbers name every breakable cell and its best route saving.
            Assert.Greater(qualifying, 0, "best saving per breakable cell: " + string.Join(" | ", report));
        }

        [Test]
        public void EAWallBreakRebuildsTheBotsFieldsLe1PerStepTheMinerHillFieldNeverGetsLongerBotsKeepMoving()
        {
            World w = MakeWorld(5, 5);
            Run(w, 3);
            BotsState st = BotBrain.GetBotsState(w);
            PadDef pad = w.Map.Pads[(int)TeamId.Azure];
            double before = BotFields.FieldDistAt(st.Fields, BotFields.FieldMinerHill, pad.X, pad.Z);
            Assert.AreEqual(true, double.IsFinite(before));
            // Azure-side (west) ring breakable cells.
            int W = w.Map.Width;
            int broken = 0;
            for (int i = 0; i < w.Map.Cells.Length; i++)
            {
                int cx = i % W;
                if (w.Map.Cells[i] == Cell.BreakableIntact && cx < w.Map.Hill.X
                    && Math.Abs(JsMath.FloorToInt((double)i / W) + 0.5 - w.Map.Hill.Z) < 12)
                {
                    MapGrid.SetCell(w, cx, JsMath.FloorToInt((double)i / W), Cell.BreakableDestroyed);
                    broken++;
                }
            }
            Assert.Greater(broken, 0);
            Assert.AreEqual(st.Fields.Count, BotFields.StaleFieldCount(st.Fields, w));
            double[] xs = new double[w.ActorCount];
            double[] zs = new double[w.ActorCount];
            Array.Copy(w.X, xs, w.ActorCount);
            Array.Copy(w.Z, zs, w.ActorCount);
            for (int s = 0; s < st.Fields.Count; s++)
            {
                int b0 = st.Fields.Builds;
                TickOnce(w);
                Assert.LessOrEqual(st.Fields.Builds - b0, 1);
            }
            Assert.AreEqual(0, BotFields.StaleFieldCount(st.Fields, w));
            Assert.LessOrEqual(BotFields.FieldDistAt(st.Fields, BotFields.FieldMinerHill, pad.X, pad.Z), before);
            Run(w, 3);
            int moved = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                double dx = w.X[i] - xs[i];
                double dz = w.Z[i] - zs[i];
                if (Math.Sqrt(dx * dx + dz * dz) > 1) moved++;
            }
            Assert.Greater(moved, (double)w.ActorCount / 2);
        }

        [Test]
        public void P1BarricadesPlacedOrExpiringNeverTriggerAFieldRebuild()
        {
            World w = MakeWorld(6, 5);
            Run(w, 1);
            BotsState st = BotBrain.GetBotsState(w);
            int builds = st.Fields.Builds;
            int wv = w.WallVersion;
            // A bridge cell on B1 (Azure base -> hill).
            int cx = 28;
            int cz = 38;
            Assert.AreEqual(Cell.Bridge, w.Map.Cells[cz * w.Map.Width + cx]);
            MapGrid.SetCell(w, cx, cz, Cell.Barricade);
            Run(w, 1);
            MapGrid.SetCell(w, cx, cz, Cell.Bridge);
            Run(w, 1);
            Assert.AreEqual(wv, w.WallVersion);
            Assert.AreEqual(builds, st.Fields.Builds);
        }
    }
}
