using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/bridges.test.ts on the browser branch cm/CM-33 (003c970; the fix never reached the frozen 09e0904),
    /// CM-33 "cross bridges naturally from any angle" (Travis, 2026-11-06), plus the DG-02 angle check. All timing in sim
    /// ticks (one MovementSystem call = one tick). Every Bridges row and its mirror (16 spans) is measured with the sim's
    /// own standing test (Collide.ActorFits). The cell raster is unchanged by CM-33.
    /// </summary>
    [TestFixture]
    public sealed class BridgeTests
    {
        private static readonly GameConfig Cfg = GameConfig.CreateBrowser5v5();
        private static readonly double Dt = SimStep.Dt(Cfg);
        private static readonly double R = Cfg.Class(ClassId.Warrior).Radius; // 0.45, the widest class
        private static readonly double SimHz = Cfg.SimHz;
        private static readonly double MoveSpeed = Cfg.Class(ClassId.Warrior).MoveSpeed;
        private const double HalfWidth = SkybreakBasinMap.BridgeHalfWidth;
        /// <summary>DERIVED: 2 x BridgeHalfWidth - 2 x R = 2.1 u.</summary>
        private static readonly double MinBand = 2 * HalfWidth - 2 * R;
        /// <summary>[guess] slack over the straight-line crossing time at full speed (the item's 1.25).</summary>
        private const double TimeSlack = 1.25;
        private static readonly double[] Offsets = { -0.8, -0.4, 0, 0.4, 0.8 };
        /// <summary>How far onto each landing a crossing starts and ends (world units, one cell).</summary>
        private const double Landing = 1;
        private static readonly double OnDeck = HalfWidth - R + 0.02;

        private struct P
        {
            public double X;
            public double Z;
        }

        private struct Run
        {
            public bool Arrived;
            public int Ticks;
            public int StuckTick;
            public int VoidTick;
            public int UnfitTick;
            /// <summary>DG-02: first tick ending a 3-tick (0.1 s) window with under 0.01 u of along-axis progress, else -1.</summary>
            public int StallTick;
            public double X;
            public double Z;
        }

        private static string Label(int s)
        {
            BridgeSpan sp = BridgeSpans.All[s];
            return "span " + s + " (" + sp.X0 + "," + sp.Z0 + ")->(" + sp.X1 + "," + sp.Z1 + ")";
        }

        private static P At(BridgeSpan sp, double a, double l)
        {
            return new P { X = sp.X0 + sp.Ux * a - sp.Uz * l, Z = sp.Z0 + sp.Uz * a + sp.Ux * l };
        }

        private static World Lone()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 33,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
            for (int i = 1; i < w.ActorCount; i++) w.Actors[i].Alive = false;
            Actor a = w.Actors[0];
            a.Alive = true;
            a.StandUp = 0;
            a.SlowTimer = 0;
            a.AbilityActive = 0;
            w.HasAim[0] = 0;
            return w;
        }

        /// <summary>The chasm part of a span: first and last along value (0.05 u samples) whose axis cell is BRIDGE or VOID.</summary>
        private static bool Chasm(MapDef map, BridgeSpan sp, out double enter, out double exit)
        {
            enter = -1;
            exit = -1;
            for (int k = 0; k * 0.05 <= sp.Len; k++)
            {
                P p = At(sp, k * 0.05, 0);
                int c = MapGrid.CellAt(map, p.X, p.Z);
                if (c != Cell.Bridge && c != Cell.Void) continue;
                if (enter < 0) enter = k * 0.05;
                exit = k * 0.05;
            }
            return enter >= 0;
        }

        /// <summary>True when the circle (x, z, r) overlaps an in-map VOID cell or leaves the map.</summary>
        private static bool CircleTouchesVoid(MapDef map, double x, double z, double r)
        {
            for (int cz = (int)Math.Floor(z - r); cz <= (int)Math.Floor(z + r); cz++)
            {
                for (int cx = (int)Math.Floor(x - r); cx <= (int)Math.Floor(x + r); cx++)
                {
                    double nx = x < cx ? cx : x > cx + 1 ? cx + 1 : x;
                    double nz = z < cz ? cz : z > cz + 1 ? cz + 1 : z;
                    if (BridgeSpans.Hypot(x - nx, z - nz) >= r) continue;
                    if (!MapGrid.InBounds(map, cx, cz) || map.Cells[cz * map.Width + cx] == Cell.Void) return true;
                }
            }
            return false;
        }

        private static bool OnSomeDeck(double x, double z)
        {
            for (int s = 0; s < BridgeSpans.All.Count; s++) if (BridgeSpans.Distance(BridgeSpans.All[s], x, z) <= OnDeck) return true;
            return false;
        }

        /// <summary>Holds (mx, mz) from rest at (x, z) for up to maxTicks ticks; arrival = the centre's along value reaches goal.</summary>
        private static Run Cross(BridgeSpan sp, double x, double z, double mx, double mz, double goal, int maxTicks)
        {
            World w = Lone();
            double r = Cfg.Class(w.Actors[0].Cls).Radius;
            w.X[0] = x;
            w.Z[0] = z;
            w.Vx[0] = 0;
            w.Vz[0] = 0;
            var output = new Run { StuckTick = -1, VoidTick = -1, UnfitTick = -1, StallTick = -1 };
            var along = new List<double> { BridgeSpans.Along(sp, x, z) };
            for (int t = 1; t <= maxTicks; t++)
            {
                double px = w.X[0];
                double pz = w.Z[0];
                w.Inputs[0].MoveX = mx;
                w.Inputs[0].MoveZ = mz;
                Movement.MovementSystem(w, Dt);
                double nx = w.X[0];
                double nz = w.Z[0];
                output.X = nx;
                output.Z = nz;
                if (nx == px && nz == pz && output.StuckTick < 0) output.StuckTick = t;
                if (CircleTouchesVoid(w.Map, nx, nz, r) && !OnSomeDeck(nx, nz) && output.VoidTick < 0) output.VoidTick = t;
                if (!Collide.ActorFits(w, nx, nz, r) && output.UnfitTick < 0) output.UnfitTick = t;
                double a = BridgeSpans.Along(sp, nx, nz);
                along.Add(a);
                // After the 0.2 s ramp from rest, every 3-tick window must advance along the axis.
                if (t >= 6 && a - along[t - 3] < 0.01 && output.StallTick < 0) output.StallTick = t;
                if (a >= goal)
                {
                    output.Arrived = true;
                    output.Ticks = t;
                    return output;
                }
            }
            output.Ticks = maxTicks;
            return output;
        }

        /// <summary>The single 8-way key (components in {-1, 0, 1}) nearest the span's axis direction.</summary>
        private static void NearestKey(BridgeSpan sp, out double kx, out double kz)
        {
            double oct = JsMath.Round(JsMath.Atan2(sp.Uz, sp.Ux) / (Math.PI / 4));
            kx = JsMath.Round(JsMath.Cos(oct * Math.PI / 4));
            kz = JsMath.Round(JsMath.Sin(oct * Math.PI / 4));
            if (kx == 0) kx = 0; // -0 to 0
            if (kz == 0) kz = 0;
        }

        private static void Report(List<string> bad, string tag, Run run, int budget, bool stall)
        {
            if (!run.Arrived) bad.Add(tag + ": not across in " + budget + " ticks");
            if (run.StuckTick >= 0) bad.Add(tag + ": zero displacement at tick " + run.StuckTick);
            if (run.VoidTick >= 0) bad.Add(tag + ": body over VOID off every deck at tick " + run.VoidTick);
            if (run.UnfitTick >= 0) bad.Add(tag + ": stands where ActorFits fails at tick " + run.UnfitTick);
            if (stall && run.StallTick >= 0) bad.Add(tag + ": stalled over 0.1 s ending at tick " + run.StallTick);
        }

        /// <summary>Near-landing start on span sp at lateral l: one cell before the chasm, advanced 0.25 u at a time until it fits.</summary>
        private static bool StartAt(World w, BridgeSpan sp, double enter, double l, out P p)
        {
            for (double a = Math.Max(0, enter - Landing); a <= enter + Landing; a += 0.25)
            {
                p = At(sp, a, l);
                if (Collide.ActorFits(w, p.X, p.Z, R)) return true;
            }
            p = default;
            return false;
        }

        private static readonly World World0 = Lone();

        [Test]
        public void BridgeSpansIsEveryBridgesRowAndItsMirror16SpansAndEverySpanHasAChasmPart()
        {
            int n = 0;
            for (int k = 0; k < SkybreakBasinMap.Bridges.Count; k++) n += SkybreakBasinMap.Bridges[k].Mirror ? 2 : 1;
            Assert.AreEqual(16, n);
            Assert.AreEqual(n, BridgeSpans.All.Count);
            var bad = new List<string>();
            for (int s = 0; s < BridgeSpans.All.Count; s++) if (!Chasm(World0.Map, BridgeSpans.All[s], out _, out _)) bad.Add(Label(s));
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void FreeLateralBandForRadius045IsGe21UOverTheMiddle70PercentOfEverySpanAndItsMirror()
        {
            const double Step = 0.005;
            var bad = new List<string>();
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                double worst = double.PositiveInfinity;
                for (int k = 0; k <= 70; k++)
                {
                    double a = sp.Len * (0.15 + 0.7 * k / 70);
                    Func<int, bool> fits = j =>
                    {
                        P p = At(sp, a, j * Step);
                        return Collide.ActorFits(World0, p.X, p.Z, R);
                    };
                    double band = 0;
                    if (fits(0))
                    {
                        int hi = 0;
                        while (hi < 600 && fits(hi + 1)) hi++;
                        int lo = 0;
                        while (lo > -600 && fits(lo - 1)) lo--;
                        band = (hi - lo) * Step;
                    }
                    if (band < worst) worst = band;
                }
                if (!(worst >= MinBand - 1e-9)) bad.Add(Label(s) + " band " + worst.ToString("F3"));
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void HoldingTheSingle8WayKeyNearestTheAxisFrom5LateralOffsetsReachesTheFarLandingInTimeNoStallTickNoVoid()
        {
            var bad = new List<string>();
            double edge = HalfWidth - R;
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                if (!Chasm(World0.Map, sp, out double enter, out double exit))
                {
                    bad.Add(Label(s) + ": no chasm part");
                    continue;
                }
                int budget = (int)Math.Ceiling(TimeSlack * sp.Len / MoveSpeed * SimHz);
                double goal = Math.Min(sp.Len, exit + Landing);
                NearestKey(sp, out double kx, out double kz);
                foreach (double f in Offsets)
                {
                    if (!StartAt(World0, sp, enter, f * edge, out P p))
                    {
                        bad.Add(Label(s) + " offset " + f + ": no fitting start on the near landing");
                        continue;
                    }
                    Report(bad, Label(s) + " key (" + kx + "," + kz + ") offset " + f, Cross(sp, p.X, p.Z, kx, kz, goal, budget), budget, false);
                }
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void ConstantInputTowardTheFarEndFromMinus60ToPlus60DegOffTheAxis15DegStepsAlsoArrivesNoStallTickNoVoid()
        {
            var bad = new List<string>();
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                if (!Chasm(World0.Map, sp, out double enter, out double exit)) continue; // reported by the first test
                double axis = JsMath.Atan2(sp.Uz, sp.Ux);
                if (!StartAt(World0, sp, enter, 0, out P p))
                {
                    bad.Add(Label(s) + ": no fitting start on the near landing");
                    continue;
                }
                double goal = Math.Min(sp.Len, exit + Landing);
                double travel = goal - BridgeSpans.Along(sp, p.X, p.Z);
                for (int deg = -60; deg <= 60; deg += 15)
                {
                    double th = deg * Math.PI / 180;
                    int budget = (int)Math.Ceiling(TimeSlack * travel / (MoveSpeed * JsMath.Cos(th)) * SimHz) + (int)SimHz;
                    Run run = Cross(sp, p.X, p.Z, JsMath.Cos(axis + th), JsMath.Sin(axis + th), goal, budget);
                    Report(bad, Label(s) + " heading " + deg, run, budget, false);
                }
            }
            CollectionAssert.IsEmpty(bad);
        }

        [Test]
        public void TheWalkIsDeterministicTheSameCrossingTwiceLandsOnTheSameFloat32PositionAndTick()
        {
            BridgeSpan sp = BridgeSpans.All[0];
            NearestKey(sp, out double kx, out double kz);
            P p = At(sp, 0, 0.8 * (HalfWidth - R));
            Run a = Cross(sp, p.X, p.Z, kx, kz, sp.Len, 600);
            Run b = Cross(sp, p.X, p.Z, kx, kz, sp.Len, 600);
            Assert.AreEqual(a.Ticks, b.Ticks);
            Assert.AreEqual(a.X, b.X);
            Assert.AreEqual(a.Z, b.Z);
            Assert.AreEqual(a.Arrived, b.Arrived);
            Assert.AreEqual(a.StuckTick, b.StuckTick);
        }

        [Test]
        public void TheDeckHeightOverTheBandLiesBetweenTheSpansEndHeights()
        {
            var bad = new List<string>();
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                P p = At(sp, sp.Len / 2, 0.9 * (HalfWidth - R));
                double h = BridgeSpans.DeckHeightAt(p.X, p.Z);
                double lo = Math.Min(sp.H0, sp.H1) - 1e-9;
                double hi = Math.Max(sp.H0, sp.H1) + 1e-9;
                if (!(h >= lo && h <= hi)) bad.Add(Label(s) + " deck height " + h);
            }
            CollectionAssert.IsEmpty(bad);
            Assert.IsTrue(double.IsNaN(BridgeSpans.DeckHeightAt(-50, -50)));
        }

        // ---- DG-02 addition (no vitest twin) ----

        [Test]
        public void DG02EveryBridgeIsCrossedAt0And30And45And60DegreesEitherSideWithNoStallOver01SAndNoFall()
        {
            var bad = new List<string>();
            int[] angles = { 0, 30, -30, 45, -45, 60, -60 };
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                if (!Chasm(World0.Map, sp, out double enter, out double exit)) continue;
                double axis = JsMath.Atan2(sp.Uz, sp.Ux);
                if (!StartAt(World0, sp, enter, 0, out P p))
                {
                    bad.Add(Label(s) + ": no fitting start on the near landing");
                    continue;
                }
                double goal = Math.Min(sp.Len, exit + Landing);
                double travel = goal - BridgeSpans.Along(sp, p.X, p.Z);
                foreach (int deg in angles)
                {
                    double th = deg * Math.PI / 180;
                    int budget = (int)Math.Ceiling(TimeSlack * travel / (MoveSpeed * JsMath.Cos(th)) * SimHz) + (int)SimHz;
                    Run run = Cross(sp, p.X, p.Z, JsMath.Cos(axis + th), JsMath.Sin(axis + th), goal, budget);
                    Report(bad, Label(s) + " at " + deg + " deg", run, budget, true);
                }
            }
            CollectionAssert.IsEmpty(bad);
        }
    }
}
