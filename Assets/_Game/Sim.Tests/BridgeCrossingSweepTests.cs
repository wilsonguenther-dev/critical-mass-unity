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
    /// Creator acceptance, need 4 ("bridges can be crossed at 0, 30, 45 and 60 degrees with keys and with an analogue
    /// stick"), as a pure Sim sweep with the game's own rules (GameConfig.CreateDefault): every span of BridgeSpans.All
    /// (each Bridges row and its mirror), both directions, the stick from -60 to +60 degrees off the axis in 5 degree steps
    /// and every 8-way key direction within 60 degrees of the axis (at today's 45 degree camera both the screen keys and
    /// "keys follow the map" are the eight compass directions), several entry offsets across the deck width, from the near
    /// landing and from on the deck, at every class's radius and speed, at full stick, half stick and with Haste. One
    /// tick = one Movement.MovementSystem call on a lone human (no map events, no bots), like BridgeTests.
    /// <para>Pass: the body's along-axis value reaches the far side (one cell past the chasm, or the span end) within
    /// 1.25 x the straight-line time at the input's along speed plus 1 s, never stalls 0.1 s (the acceptance test's rule:
    /// under 0.01 u of along progress over 3 ticks after tick 6), never stands where Collide.ActorFits fails, never touches
    /// VOID off every deck, and never leaves the deck sideways over the chasm. The one exception is input that truly points
    /// off the deck: from the near landing, the straight line in the input direction leaves the deck band before it reaches
    /// the chasm (the body walks along the island beside the mouth, as it should); those runs must still never fall,
    /// touch VOID or stand where it may not, but need not cross.</para>
    /// <para>There is no carry speed in the Sim (carrying changes nothing in Movement), so "carry speed" is the walking
    /// speed; the slower and faster bodies are covered by half stick (0.5 x, also what Farmer roots give) and Haste.</para>
    /// <para>Finding (2026-11-13): this sweep passes on the unchanged Sim. The acceptance test's 4 failures ("B5 back", that
    /// is BridgeSpans.All[4] = map bridge B3, NW courtyard to central, crossed central to courtyard) were the U-068 map event
    /// BRIDGE COLLAPSE dropping that span's middle third during those four crossings of the 30 minute PlayMode sweep, which
    /// ran with map events on (TheAcceptanceFailureIsTheBridgeCollapseEventNotCollision reproduces it).</para>
    /// </summary>
    [TestFixture]
    public sealed class BridgeCrossingSweepTests
    {
        private const double HalfWidth = SkybreakBasinMap.BridgeHalfWidth;
        private const double TimeSlack = 1.25;

        private sealed class Opts
        {
            public ClassId[] Classes;
            public double[] Offsets;   // share of the band half width (HalfWidth - r), signed
            public int StickStep;      // degrees
            public double[] Mags;      // stick magnitudes; a negative value = full stick with Haste
        }

        private struct Run
        {
            public bool Arrived;
            public int Stall, Unfit, Void, Sideways, Ticks;
            public double X, Z;
        }

        private static World Lone(GameConfig cfg, ClassId cls)
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 1, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg,
            });
            for (int i = 1; i < w.ActorCount; i++) w.Actors[i].Alive = false;
            return w;
        }

        private static void Ready(World w, double x, double z, bool haste)
        {
            Actor a = w.Actors[0];
            a.Alive = true;
            a.StandUp = 0;
            a.SlowTimer = 0;
            a.AbilityActive = 0;
            a.AirLen = 0;
            a.UnsupportedTicks = 0;
            a.JumpBuffer = 0;
            a.HitStunTicks = 0;
            a.KVx = 0;
            a.KVz = 0;
            w.HasAim[0] = 0;
            w.Depth.PowerKind[0] = haste ? (byte)PickupKind.Haste : (byte)0;
            w.X[0] = w.PrevX[0] = x;
            w.Z[0] = w.PrevZ[0] = z;
            w.Vx[0] = 0;
            w.Vz[0] = 0;
        }

        /// <summary>The chasm part of a span: first and last along value (0.05 u samples) whose axis cell is BRIDGE or VOID.</summary>
        internal static bool Chasm(MapDef map, BridgeSpan sp, out double enter, out double exit)
        {
            enter = -1;
            exit = -1;
            for (int k = 0; k * 0.05 <= sp.Len; k++)
            {
                double a = k * 0.05;
                int c = MapGrid.CellAt(map, sp.X0 + sp.Ux * a, sp.Z0 + sp.Uz * a);
                if (c != Cell.Bridge && c != Cell.Void) continue;
                if (enter < 0) enter = a;
                exit = a;
            }
            return enter >= 0;
        }

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

        private static bool OnSomeDeck(double x, double z, double r)
        {
            for (int s = 0; s < BridgeSpans.All.Count; s++)
                if (BridgeSpans.Distance(BridgeSpans.All[s], x, z) <= HalfWidth - r + 0.02) return true;
            return false;
        }

        /// <summary>
        /// Holds world move (mx, mz) from (x, z) for up to budget ticks along direction (ux, uz) from origin (ox, oz).
        /// Over the chasm part (along en + r .. ex - r) the centre must stay within one cell of the drawn deck.
        /// </summary>
        private static Run Cross(World w, double x, double z, double mx, double mz, bool haste, double ox, double oz, double ux, double uz,
            BridgeSpan sp, double en, double ex, double goal, int budget)
        {
            Ready(w, x, z, haste);
            double r = Collide.ActorRadius(w, 0);
            double dt = SimStep.Dt(w.Config);
            var run = new Run { Stall = -1, Unfit = -1, Void = -1, Sideways = -1 };
            var along = new List<double>(budget + 1) { (x - ox) * ux + (z - oz) * uz };
            for (int t = 1; t <= budget; t++)
            {
                w.Inputs[0].MoveX = mx;
                w.Inputs[0].MoveZ = mz;
                Movement.MovementSystem(w, dt);
                double nx = w.X[0], nz = w.Z[0];
                run.X = nx;
                run.Z = nz;
                run.Ticks = t;
                double a = (nx - ox) * ux + (nz - oz) * uz;
                along.Add(a);
                if (!Collide.ActorFits(w, nx, nz, r) && run.Unfit < 0) run.Unfit = t;
                if (CircleTouchesVoid(w.Map, nx, nz, r) && !OnSomeDeck(nx, nz, r) && run.Void < 0) run.Void = t;
                if (a > en + r && a < ex - r && Math.Abs(BridgeSpans.Lateral(sp, nx, nz)) > HalfWidth + 0.5 && run.Sideways < 0) run.Sideways = t;
                if (t >= 6 && a - along[t - 3] < 0.01 && run.Stall < 0) run.Stall = t;
                if (a >= goal)
                {
                    run.Arrived = true;
                    return run;
                }
            }
            return run;
        }

        private static List<string> Sweep(Opts o, out int runs, out int excused)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            var bad = new List<string>();
            runs = 0;
            excused = 0;
            foreach (ClassId cls in o.Classes)
            {
                World w = Lone(cfg, cls);
                double r = cfg.Class(cls).Radius;
                double speed = cfg.Class(cls).MoveSpeed;
                double band = HalfWidth - r;
                for (int s = 0; s < BridgeSpans.All.Count; s++)
                {
                    BridgeSpan sp = BridgeSpans.All[s];
                    Assert.IsTrue(Chasm(w.Map, sp, out double enter, out double exit), BridgeSpans.Name(s) + " has a chasm part");
                    for (int dir = 1; dir >= -1; dir -= 2)
                    {
                        double ux = sp.Ux * dir, uz = sp.Uz * dir;
                        double ox = dir > 0 ? sp.X0 : sp.X1, oz = dir > 0 ? sp.Z0 : sp.Z1;
                        double en = dir > 0 ? enter : sp.Len - exit, ex = dir > 0 ? exit : sp.Len - enter;
                        double goal = Math.Min(sp.Len, ex + 1);
                        double axis = Math.Atan2(uz, ux);
                        // Lateral in this direction's frame: + = toward (-uz, ux).
                        var inputs = new List<(string name, double off)>();
                        for (int deg = -60; deg <= 60; deg += o.StickStep) inputs.Add(("stick " + deg, deg));
                        for (int q = 0; q < 8; q++)
                        {
                            double off = Math.IEEERemainder(q * Math.PI / 4 - axis, 2 * Math.PI) * 180 / Math.PI;
                            if (Math.Abs(off) <= 60.0001) inputs.Add(("key " + KeyName(q) + " (" + off.ToString("0") + " deg)", off));
                        }
                        foreach (double f in o.Offsets)
                        {
                            double l = f * band;
                            for (int family = 0; family < 2; family++)
                            {
                                // family 0: the near landing (one cell before the chasm, advanced 0.25 u until it fits); 1: on the deck.
                                double sa = -1, sx = 0, sz = 0;
                                double from = family == 0 ? Math.Max(0, en - 1) : en + 1.5;
                                for (double a = from; a <= from + 2; a += 0.25)
                                {
                                    double px = ox + ux * a - uz * l, pz = oz + uz * a + ux * l;
                                    if (Collide.ActorFits(w, px, pz, r)) { sa = a; sx = px; sz = pz; break; }
                                }
                                string where = BridgeSpans.Name(s) + (dir > 0 ? " forward" : " back") + (family == 0 ? " from the landing" : " from the deck") +
                                               " offset " + f.ToString("0.0") + " " + cls;
                                if (sa < 0)
                                {
                                    bad.Add(where + ": no fitting start");
                                    continue;
                                }
                                foreach (var inp in inputs)
                                {
                                    double th = axis + inp.off * Math.PI / 180;
                                    double cos = Math.Cos(inp.off * Math.PI / 180);
                                    // Truly off the deck: the straight line leaves the band before the chasm (landing starts only).
                                    bool pointsOff = false;
                                    if (family == 0 && en > sa)
                                    {
                                        double latAtChasm = l + (en - sa) * Math.Tan(inp.off * Math.PI / 180);
                                        pointsOff = Math.Abs(latAtChasm) > band + Collide.DeckPad;
                                    }
                                    foreach (double mag0 in o.Mags)
                                    {
                                        bool haste = mag0 < 0;
                                        double mag = Math.Abs(mag0);
                                        double v = speed * mag * (haste ? cfg.Depth.Pickups.HasteSpeed : 1) * cos;
                                        int budget = (int)Math.Ceiling(TimeSlack * (goal - sa) / v * cfg.SimHz) + (int)cfg.SimHz;
                                        Run run = Cross(w, sx, sz, mag * Math.Cos(th), mag * Math.Sin(th), haste, ox, oz, ux, uz, sp, en, ex, goal, budget);
                                        runs++;
                                        string tag = where + " " + inp.name + (haste ? " haste" : " x" + mag.ToString("0.0"));
                                        if (run.Unfit >= 0) bad.Add(tag + ": stands where ActorFits fails at tick " + run.Unfit);
                                        if (run.Void >= 0) bad.Add(tag + ": touches VOID off every deck at tick " + run.Void);
                                        if (run.Sideways >= 0) bad.Add(tag + ": left the deck sideways over the chasm at tick " + run.Sideways);
                                        if (pointsOff)
                                        {
                                            excused++;
                                            continue;
                                        }
                                        if (!run.Arrived) bad.Add(tag + $": not across in {budget} ticks (at {run.X:F2},{run.Z:F2})");
                                        if (run.Stall >= 0) bad.Add(tag + $": stalled 0.1 s at tick {run.Stall}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return bad;
        }

        private static string KeyName(int q)
        {
            // World compass direction q * 45 degrees (0 = +x, 90 = +z).
            string[] n = { "+x", "+x+z", "+z", "-x+z", "-x", "-x-z", "-z", "+x-z" };
            return n[q];
        }

        private static void NoneBad(List<string> bad, int runs, int excused)
        {
            Console.WriteLine($"bridge sweep: {runs} crossings, {excused} with input pointing off the deck (no-fall checks only), {bad.Count} failures");
            int show = Math.Min(bad.Count, 60);
            Assert.IsTrue(bad.Count == 0, bad.Count + " failure(s) in " + runs + " crossings:\n" + string.Join("\n", bad.GetRange(0, show)));
        }

        /// <summary>
        /// The acceptance test's "B5 back" failures, reproduced in the Sim: BridgeSpans.All[4] is map bridge B3 (NW
        /// courtyard to central). With U-068 map events on, a BRIDGE COLLAPSE of that span voids its middle third for 45 s;
        /// a body crossing it from the central island at 30 or 45 degrees then stops at the gap's edge, along about 4.0 u
        /// from the central end at tick 41 (30 deg) and 49 (45 deg), exactly the stall ticks CI run 43461496029 printed. The
        /// same crossings on the intact span arrive. So the failure was the map event, not deck collision.
        /// </summary>
        [Test]
        public void TheAcceptanceFailureIsTheBridgeCollapseEventNotCollision()
        {
            const int span = 4;
            Assert.AreEqual("B3", BridgeSpans.Name(span), "All[4] is the map's B3");
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.Bots.PassiveSeconds = 100000;
            cfg.MatchSeconds = 100000;
            cfg.HatchSeconds = 100000;
            World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 1, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            var frame = new InputFrame { AimX = double.NaN, AimZ = double.NaN };
            var scratch = new InputFrame();
            for (int k = 0; k < 64 && w.Depth.BrokenSpan != span; k++)
            {
                w.Depth.BridgeState = MapEvents.BridgeNone;
                MapEvents.Start(w, MapEvents.KindBridge, k);
            }
            Assert.AreEqual(span, w.Depth.BrokenSpan, "a collapse of B3 was scheduled");
            for (int t = 0; t < 400 && w.Depth.BridgeState != MapEvents.BridgeBroken; t++) Bots.MatchTick.Advance(w, frame, scratch);
            Assert.AreEqual(MapEvents.BridgeBroken, w.Depth.BridgeState, "the middle third fell");

            BridgeSpan sp = BridgeSpans.All[span];
            double r = Collide.ActorRadius(w, 0);
            double ux = -sp.Ux, uz = -sp.Uz, ox = sp.X1, oz = sp.Z1; // back: central -> courtyard
            Assert.IsTrue(Collide.ActorFits(w, ox, oz, r), "the acceptance start (the span's central end) fits");
            foreach (int deg in new[] { 30, -30, 45, -45 })
            {
                double th = Math.Atan2(uz, ux) + deg * Math.PI / 180;
                Ready(w, ox, oz, false);
                frame.MoveX = Math.Cos(th);
                frame.MoveZ = Math.Sin(th);
                var along = new List<double> { 0 };
                int stall = -1;
                for (int t = 1; t <= 120 && stall < 0; t++)
                {
                    Bots.MatchTick.Advance(w, frame, scratch);
                    along.Add((w.X[0] - ox) * ux + (w.Z[0] - oz) * uz);
                    if (t >= 6 && along[t] - along[t - 3] < 0.01) stall = t;
                }
                Assert.AreEqual(Math.Abs(deg) == 30 ? 41 : 49, stall, deg + " deg: the stall tick CI printed");
                Assert.AreEqual(4.0, along[along.Count - 1], 0.15, deg + " deg: stopped at the collapsed gap (the middle third starts at Len / 3 = 4.48 u)");
            }
            frame.MoveX = 0;
            frame.MoveZ = 0;

            // The same crossings on the intact span (a fresh world, no event): every one arrives without a stall.
            World intact = Lone(GameConfig.CreateDefault(), ClassId.Warrior);
            Assert.IsTrue(Chasm(intact.Map, sp, out double enter, out double exit));
            foreach (int deg in new[] { 30, -30, 45, -45 })
            {
                double th = Math.Atan2(uz, ux) + deg * Math.PI / 180;
                Run run = Cross(intact, ox, oz, Math.Cos(th), Math.Sin(th), false, ox, oz, ux, uz, sp, sp.Len - exit, sp.Len - enter, sp.Len, 400);
                Assert.IsTrue(run.Arrived && run.Stall < 0, deg + " deg on the intact span: arrived " + run.Arrived + ", stall " + run.Stall);
            }
        }

        /// <summary>The fast slice: the widest class (Warrior, 0.45) at full stick, 10 degree steps, three offsets, both starts.</summary>
        [Test]
        public void EveryBridgeBothWaysEveryAngleTo60DegreesWithTheStickAndEveryKeyFromTheLandingAndTheDeckCrossesWithNoStallNoFall()
        {
            var bad = Sweep(new Opts
            {
                Classes = new[] { ClassId.Warrior }, Offsets = new[] { -0.8, 0, 0.8 }, StickStep = 10, Mags = new[] { 1.0 },
            }, out int runs, out int excused);
            NoneBad(bad, runs, excused);
        }

        /// <summary>The full sweep: every class, 5 degree steps, five offsets, full stick, half stick and Haste.</summary>
        [Test, Category("Slow")]
        public void FullSweepEveryClassEverySpeedEvery5DegreesFiveOffsetsCrossesWithNoStallNoFall()
        {
            var bad = Sweep(new Opts
            {
                Classes = new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer },
                Offsets = new[] { -0.8, -0.4, 0, 0.4, 0.8 }, StickStep = 5, Mags = new[] { 1.0, 0.5, -1.0 },
            }, out int runs, out int excused);
            NoneBad(bad, runs, excused);
        }
    }
}
