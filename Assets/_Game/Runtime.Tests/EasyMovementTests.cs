using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Maps;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Easy movement (2026-11-12): move to pointer, lane assist, corner slide, keys follow the map (MoveAssist and
    /// HumanInputState.Fill). Pure C# on a real World of the default map: no UnityEngine type is touched, so this fixture
    /// also runs outside the editor with the Sim (the no-Unity runner of rules-dg-02, plus the two Input files).
    /// </summary>
    [TestFixture]
    public sealed class EasyMovementTests
    {
        private static readonly GameConfig Cfg = GameConfig.CreateDefault();
        private static readonly double Dt = SimStep.Dt(Cfg);
        private const double Eps = 1e-9;

        private static World Lone()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 7,
                TeamSize = Cfg.TeamSize,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateDefault(),
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

        private static void Place(World w, double x, double z)
        {
            w.X[0] = x;
            w.Z[0] = z;
            w.PrevX[0] = x;
            w.PrevZ[0] = z;
            w.Vx[0] = 0;
            w.Vz[0] = 0;
        }

        private static double Deg(double x, double z) => Math.Atan2(z, x) * 180 / Math.PI;

        private static double AngleBetween(double ax, double az, double bx, double bz)
        {
            double d = Math.Abs(Deg(ax, az) - Deg(bx, bz)) % 360;
            return d > 180 ? 360 - d : d;
        }

        private static readonly (string Name, double Right, double Up)[] Keys = { ("W", 0, 1), ("S", 0, -1), ("A", -1, 0), ("D", 1, 0) };
        private static readonly (string Name, double Right, double Up)[] Keys8 =
        {
            ("W", 0, 1), ("S", 0, -1), ("A", -1, 0), ("D", 1, 0), ("W+D", 1, 1), ("W+A", -1, 1), ("S+D", 1, -1), ("S+A", -1, -1),
        };

        private static void KeyWorld(double right, double up, out double x, out double z)
        {
            x = right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX;
            z = right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ;
        }

        // ---- move to pointer ----

        [Test]
        public void PointerMoveStopsInsideTheDeadZoneEasesOverTheRampAndIsFullSpeedBeyond()
        {
            Assert.IsFalse(MoveAssist.PointerMove(10, 10, 10.2, 10.2, out _, out _));
            Assert.IsFalse(MoveAssist.PointerMove(0, 0, MoveAssist.PointerDeadZone, 0, out _, out _));
            Assert.IsTrue(MoveAssist.PointerMove(10, 10, 10 + MoveAssist.PointerDeadZone + MoveAssist.PointerRamp / 2, 10, out double hx, out double hz));
            Assert.AreEqual(0.5, hx, 1e-9);
            Assert.AreEqual(0, hz, Eps);
            Assert.IsTrue(MoveAssist.PointerMove(10, 10, 30, 10, out double fx, out _));
            Assert.AreEqual(1, fx, Eps);
            Assert.IsFalse(MoveAssist.PointerMove(10, 10, double.NaN, 3, out _, out _));
            Assert.IsFalse(MoveAssist.PointerMove(10, 10, double.PositiveInfinity, 3, out _, out _));
        }

        [Test]
        public void PointerMoveIsSmooth360DegreesNotSnappedTo8Directions()
        {
            for (int deg = 0; deg < 360; deg += 7)
            {
                double a = deg * Math.PI / 180;
                Assert.IsTrue(MoveAssist.PointerMove(0, 0, 5 * Math.Cos(a), 5 * Math.Sin(a), out double mx, out double mz));
                Assert.AreEqual(1, Math.Sqrt(mx * mx + mz * mz), 1e-9, "full speed at " + deg);
                Assert.Less(AngleBetween(mx, mz, Math.Cos(a), Math.Sin(a)), 1e-6, "direction at " + deg);
            }
        }

        [Test]
        public void HoldingTheWalkButtonWalksToTheCursorKeysWinAndReleasingStops()
        {
            World w = Lone();
            var s = new HumanInputState();
            s.BindWorld(w);
            Place(w, 56, 36); // the central island
            var f = SimInput.EmptyInput();
            s.SetPointerWalk(true);
            s.SetWalkTarget(56 + 3, 36 + 4);
            s.Fill(f);
            Assert.AreEqual(0.6, f.MoveX, 1e-9);
            Assert.AreEqual(0.8, f.MoveZ, 1e-9);
            // A direction key beats the pointer (never two intents at once).
            s.SetMove(0, 1, true);
            s.Fill(f);
            Assert.AreEqual(HumanInputState.ScreenUpX, f.MoveX, 1e-9);
            s.SetMove(0, 0);
            s.SetPointerWalk(false);
            s.Fill(f);
            Assert.AreEqual(0, f.MoveX);
            Assert.AreEqual(0, f.MoveZ);
            // No ground point under the cursor: stand still.
            s.SetPointerWalk(true);
            s.ClearWalkTarget();
            s.Fill(f);
            Assert.AreEqual(0, f.MoveX);
            // Focus lost drops the held walk button.
            s.SetWalkTarget(70, 36);
            s.Blur();
            s.Fill(f);
            Assert.AreEqual(0, f.MoveX);
        }

        // ---- controller stick ----

        [Test]
        public void TheStickStaysAnalogAndScreenRelativeEvenWithAssistAndKeysFollowTheMapOn()
        {
            World w = Lone();
            var s = new HumanInputState { MovementAssist = true, KeysFollowMap = true };
            s.BindWorld(w);
            Place(w, 56, 36);
            var f = SimInput.EmptyInput();
            for (int deg = 0; deg < 360; deg += 11)
            {
                double a = deg * Math.PI / 180;
                double right = 0.6 * Math.Cos(a), up = 0.6 * Math.Sin(a);
                s.SetMove(right, up, false);
                s.Fill(f);
                KeyWorld(right, up, out double ex, out double ez);
                Assert.AreEqual(ex, f.MoveX, 1e-9, "x at " + deg);
                Assert.AreEqual(ez, f.MoveZ, 1e-9, "z at " + deg);
            }
        }

        // ---- keys follow the map ----

        [Test]
        public void KeysFollowTheMapTurnsTheFourKeysOntoTheGridAxes()
        {
            Assert.AreEqual(0, HumanInputState.MapUpX, Eps);
            Assert.AreEqual(-1, HumanInputState.MapUpZ, Eps);
            Assert.AreEqual(1, HumanInputState.MapRightX, Eps);
            Assert.AreEqual(0, HumanInputState.MapRightZ, Eps);
            var s = new HumanInputState { KeysFollowMap = true, MovementAssist = false };
            var f = SimInput.EmptyInput();
            s.SetMove(0, 1, true);
            s.Fill(f);
            Assert.AreEqual(0, f.MoveX, Eps);
            Assert.AreEqual(-1, f.MoveZ, Eps);
            s.SetMove(1, 0, true);
            s.Fill(f);
            Assert.AreEqual(1, f.MoveX, Eps);
            Assert.AreEqual(0, f.MoveZ, Eps);
            s.KeysFollowMap = false;
            s.SetMove(0, 1, true);
            s.Fill(f);
            Assert.AreEqual(HumanInputState.ScreenUpX, f.MoveX, Eps);
            Assert.AreEqual(HumanInputState.ScreenUpZ, f.MoveZ, Eps);
        }

        // ---- lane assist ----

        [Test]
        public void EveryBridgeIsWithin50DegreesOfOneOfTheFourKeysAtTodaysCamera()
        {
            for (int i = 0; i < BridgeSpans.All.Count; i++)
            {
                BridgeSpan sp = BridgeSpans.All[i];
                double best = 180;
                foreach (var k in Keys)
                {
                    KeyWorld(k.Right, k.Up, out double kx, out double kz);
                    best = Math.Min(best, Math.Min(AngleBetween(kx, kz, sp.Ux, sp.Uz), AngleBetween(kx, kz, -sp.Ux, -sp.Uz)));
                }
                Assert.LessOrEqual(best, MoveAssist.LaneMaxAngleDeg, "span " + i);
            }
        }

        [Test]
        public void LaneSteerFollowsTheAxisOnTheDeckPullsToTheCentreAndIgnoresKeysTooFarOff()
        {
            BridgeSpan b1 = BridgeSpans.All[0]; // B1, the long base -> central bridge, about 37 degrees off every key
            double mx0 = b1.X0 + b1.Ux * b1.Len / 2, mz0 = b1.Z0 + b1.Uz * b1.Len / 2;
            // The key nearest the axis direction.
            double bkx = 0, bkz = 0, bestDot = -2;
            foreach (var k in Keys)
            {
                KeyWorld(k.Right, k.Up, out double kx, out double kz);
                double d = kx * b1.Ux + kz * b1.Uz;
                if (d > bestDot)
                {
                    bestDot = d;
                    bkx = kx;
                    bkz = kz;
                }
            }
            Assert.IsTrue(MoveAssist.LaneSteer(mx0, mz0, bkx, bkz, out double sx, out double sz));
            Assert.AreEqual(b1.Ux, sx, 1e-9);
            Assert.AreEqual(b1.Uz, sz, 1e-9);
            // Off the centre line: the steer leans back toward it (lateral decreases), never more than LaneCentreMax.
            double ox = mx0 - b1.Uz * 0.8, oz = mz0 + b1.Ux * 0.8; // lateral +0.8
            Assert.IsTrue(MoveAssist.LaneSteer(ox, oz, bkx, bkz, out sx, out sz));
            double lateralRate = sz * b1.Ux - sx * b1.Uz;
            Assert.Less(lateralRate, 0);
            Assert.Greater(sx * b1.Ux + sz * b1.Uz, 0.8);
            // A key 60+ degrees off the axis is not steered; nor is a spot far from every bridge.
            double far = 60 * Math.PI / 180;
            double rx = b1.Ux * Math.Cos(far) - b1.Uz * Math.Sin(far), rz = b1.Ux * Math.Sin(far) + b1.Uz * Math.Cos(far);
            Assert.IsFalse(MoveAssist.LaneSteer(mx0, mz0, rx, rz, out _, out _));
            Assert.IsFalse(MoveAssist.LaneSteer(8, 36, bkx, bkz, out _, out _)); // the Azure base pad
        }

        [Test]
        public void LaneAssistNeverTouchesTwoKeysOrTheStick()
        {
            World w = Lone();
            BridgeSpan b1 = BridgeSpans.All[0];
            var s = new HumanInputState { MovementAssist = true };
            s.BindWorld(w);
            Place(w, b1.X0 + b1.Ux * b1.Len / 2, b1.Z0 + b1.Uz * b1.Len / 2);
            var f = SimInput.EmptyInput();
            // Two keys (S+D) on the deck centre: exactly the screen diagonal (sim +X here), not the axis.
            s.SetMove(1, -1, true);
            s.Fill(f);
            KeyWorld(1, -1, out double ex, out double ez);
            double el = Math.Sqrt(ex * ex + ez * ez);
            Assert.AreEqual(ex / el, f.MoveX, 1e-9);
            Assert.AreEqual(ez / el, f.MoveZ, 1e-9);
            // The same single direction from the stick: unchanged.
            s.SetMove(1, 0, false);
            s.Fill(f);
            Assert.AreEqual(HumanInputState.ScreenRightX, f.MoveX, 1e-9);
            Assert.AreEqual(HumanInputState.ScreenRightZ, f.MoveZ, 1e-9);
            // Assist off: a single key is untouched too.
            s.MovementAssist = false;
            s.SetMove(1, 0, true);
            s.Fill(f);
            Assert.AreEqual(HumanInputState.ScreenRightX, f.MoveX, 1e-9);
        }

        // ---- corner slide ----

        [Test]
        public void CornerSlideLeavesOpenGroundAndHeadOnWallsAloneAndFreesAStaircaseNotch()
        {
            World w = Lone();
            double r = Collide.ActorRadius(w, 0);
            // Open ground on the central island: nothing to slide around.
            Place(w, 56, 36);
            Assert.IsFalse(MoveAssist.CornerSlide(w, 0, 1, 0, out _, out _));
            // B7 (axis-aligned, cells x 54..57): pushing +X head-on into its east edge over the chasm: still a stop.
            Assert.IsTrue(Collide.ActorFits(w, 57.5, 21.5, r), "precondition: the east lane of B7 fits");
            Assert.IsFalse(Collide.ActorFits(w, 58.6, 21.5, r), "precondition: chasm east of B7");
            Place(w, 58 - r - 0.002, 21.5);
            Assert.IsFalse(MoveAssist.CornerSlide(w, 0, 1, 0, out _, out _));
            // A single key pressed into that same edge at 45 degrees already slides (the plain push-out): no turn needed.
            KeyWorld(1, 0, out double dx, out double dz);
            Assert.IsFalse(MoveAssist.CornerSlide(w, 0, dx, dz, out _, out _));
        }

        [Test]
        public void CornerSlideTurnsAMoveThatWouldStopAtASnagAndStillAdvances()
        {
            // Real snags of the default map: hold a keyboard direction with no assist until the body stands still,
            // then ask CornerSlide. Where it turns the move, the turn is at most 60 degrees, keeps the speed and advances.
            World w = Lone();
            double r = Collide.ActorRadius(w, 0);
            double probe = Cfg.Class(ClassId.Warrior).MoveSpeed * Dt * MoveAssist.SlideProbeTicks;
            int notches = 0;
            for (double x0 = 6.25; x0 < 106 && notches < 40; x0 += 2)
            {
                for (double z0 = 4.25; z0 < 68 && notches < 40; z0 += 2)
                {
                    if (!Collide.ActorFits(w, x0, z0, r)) continue;
                    foreach (var k in Keys8)
                    {
                        KeyWorld(k.Right, k.Up, out double kx, out double kz);
                        double kl = Math.Sqrt(kx * kx + kz * kz);
                        kx /= kl;
                        kz /= kl;
                        Place(w, x0, z0);
                        double lx = x0, lz = z0;
                        bool stopped = false;
                        for (int t = 1; t <= 30 && !stopped; t++)
                        {
                            w.Inputs[0].MoveX = kx;
                            w.Inputs[0].MoveZ = kz;
                            Movement.MovementSystem(w, Dt);
                            if (t % 5 == 0)
                            {
                                stopped = t >= 10 && BridgeSpans.Hypot(w.X[0] - lx, w.Z[0] - lz) < 0.01;
                                lx = w.X[0];
                                lz = w.Z[0];
                            }
                        }
                        if (!stopped) continue;
                        double x = w.X[0], z = w.Z[0];
                        Place(w, x, z);
                        double qx, qz;
                        if (!MoveAssist.CornerSlide(w, 0, kx, kz, out double sx, out double sz)) continue;
                        notches++;
                        Assert.LessOrEqual(AngleBetween(sx, sz, kx, kz), MoveAssist.SlideMaxAngleDeg + 1e-6);
                        Assert.AreEqual(1, Math.Sqrt(sx * sx + sz * sz), 1e-9, "same speed");
                        Collide.SweepActor(w, 0, x, z, sx * probe, sz * probe, false, out qx, out qz);
                        Assert.GreaterOrEqual((qx - x) * kx + (qz - z) * kz, MoveAssist.SlideMinForward * probe - 1e-9,
                            $"forward progress at ({x},{z}) key {k.Name}");
                    }
                }
            }
            Assert.Greater(notches, 0, "no snag found: the search is broken");
        }

        // ---- one key crosses every bridge ----

        private struct Crossing
        {
            public bool Arrived;
            public int Ticks;
            public int StallTick;
            public int UnfitTick;
            public double X, Z;
        }

        private static Crossing Cross(BridgeSpan sp, double sign, double right, double up, bool assist, int maxTicks)
        {
            World w = Lone();
            double r = Collide.ActorRadius(w, 0);
            var s = new HumanInputState { MovementAssist = assist };
            s.BindWorld(w);
            double ex = sign > 0 ? sp.X0 : sp.X1, ez = sign > 0 ? sp.Z0 : sp.Z1;
            double dx = sign * sp.Ux, dz = sign * sp.Uz;
            // Two units back on the entry landing (stepping toward the mouth until it fits).
            double back = 2.0;
            while (back > 0 && !Collide.ActorFits(w, ex - dx * back, ez - dz * back, r)) back -= 0.25;
            Place(w, ex - dx * back, ez - dz * back);
            double goal = sp.Len + 2.0;
            var output = new Crossing { StallTick = -1, UnfitTick = -1 };
            var travelled = new List<double>();
            double Travel() => sign > 0 ? BridgeSpans.Along(sp, w.X[0], w.Z[0]) : sp.Len - BridgeSpans.Along(sp, w.X[0], w.Z[0]);
            travelled.Add(Travel());
            for (int t = 1; t <= maxTicks; t++)
            {
                s.SetMove(right, up, true);
                s.Fill(w.Inputs[0]);
                Movement.MovementSystem(w, Dt);
                output.X = w.X[0];
                output.Z = w.Z[0];
                if (output.UnfitTick < 0 && !Collide.ActorFits(w, w.X[0], w.Z[0], r)) output.UnfitTick = t;
                double p = Travel();
                travelled.Add(p);
                if (t >= 6 && p - travelled[t - 3] < 0.01 && output.StallTick < 0) output.StallTick = t;
                if (p >= goal)
                {
                    output.Arrived = true;
                    output.Ticks = t;
                    return output;
                }
            }
            output.Ticks = maxTicks;
            return output;
        }

        [Test]
        public void HoldingOneKeyCrossesEveryBridgeEndToEndBothWaysWithoutStopping()
        {
            var bad = new List<string>();
            double speed = Cfg.Class(ClassId.Warrior).MoveSpeed;
            for (int i = 0; i < BridgeSpans.All.Count; i++)
            {
                BridgeSpan sp = BridgeSpans.All[i];
                for (int dir = 0; dir < 2; dir++)
                {
                    double sign = dir == 0 ? 1 : -1;
                    // The single key nearest the walking direction.
                    var key = Keys[0];
                    double bestDot = -2;
                    foreach (var k in Keys)
                    {
                        KeyWorld(k.Right, k.Up, out double kx, out double kz);
                        double d = sign * (kx * sp.Ux + kz * sp.Uz);
                        if (d > bestDot)
                        {
                            bestDot = d;
                            key = k;
                        }
                    }
                    // Budget: straight-line time at full speed, x 1.4 slack, plus the 0.2 s start.
                    int budget = (int)Math.Ceiling((sp.Len + 4.0) / speed * Cfg.SimHz * 1.4) + 6;
                    Crossing c = Cross(sp, sign, key.Right, key.Up, true, budget);
                    string tag = $"span {i} ({sp.X0},{sp.Z0})->({sp.X1},{sp.Z1}) {(dir == 0 ? "forward" : "back")} key {key.Name}";
                    if (!c.Arrived) bad.Add($"{tag}: not across in {budget} ticks, at ({c.X:F2},{c.Z:F2})");
                    if (c.StallTick >= 0) bad.Add($"{tag}: stalled 0.1 s ending at tick {c.StallTick}");
                    if (c.UnfitTick >= 0) bad.Add($"{tag}: stands where ActorFits fails at tick {c.UnfitTick}");
                }
            }
            Assert.IsTrue(bad.Count == 0, bad.Count + " failure(s):\n" + string.Join("\n", bad));
        }

        /// <summary>
        /// Creator acceptance (bridge travel), 2026-11-13: corner slide must stay off near bridges (HumanInputState.NearBridge)
        /// for the stick AND the keys. On a deck, or on the landing at a mouth, the straight move 45 to 60 degrees off the
        /// axis is "blocked" by the VOID beside the deck, and corner slide turns it toward the most progress along the input
        /// direction, which is along the island edge or into a riser corner, away from the span; the sim's own rail
        /// (Collide.RailFirst) carries the unturned move across. Every span, both ways, the stick at -60..+60 in 15 degree
        /// steps and every 8-way key within 60 degrees (one key with lane assist and two keys, for both key bases), three
        /// offsets, from the landing and from the deck, Warrior (widest): the game's assist (slide off near bridges) crosses
        /// everything the bare sim crosses, and the control (slide everywhere) shows the stalls the gate removes.
        /// </summary>
        [Test]
        public void CornerSlideIsOffNearBridgesSoTheStickAndTheKeysCrossEveryBridgeAsTheSimAloneDoes()
        {
            int gated = BridgeSweepWithAssist(true, out string example, out int runs);
            Assert.AreEqual(0, gated, gated + " of " + runs + " crossings stalled or did not arrive with the game's assist, e.g. " + example);
            int ungated = BridgeSweepWithAssist(false, out string ungatedExample, out _);
            Assert.Greater(ungated, 0, "control: corner slide everywhere should stall some bridge crossings (else the gate is moot)");
        }

        private static int BridgeSweepWithAssist(bool gate, out string example, out int runs)
        {
            example = "";
            runs = 0;
            int fails = 0;
            World w = Lone();
            double r = Cfg.Class(ClassId.Warrior).Radius, speed = Cfg.Class(ClassId.Warrior).MoveSpeed;
            double band = SkybreakBasinMap.BridgeHalfWidth - r;
            for (int s = 0; s < BridgeSpans.All.Count; s++)
            {
                BridgeSpan sp = BridgeSpans.All[s];
                double enter = -1, exit = -1;
                for (int k = 0; k * 0.05 <= sp.Len; k++)
                {
                    int c = CriticalMass.Sim.Grid.MapGrid.CellAt(w.Map, sp.X0 + sp.Ux * k * 0.05, sp.Z0 + sp.Uz * k * 0.05);
                    if (c != Cell.Bridge && c != Cell.Void) continue;
                    if (enter < 0) enter = k * 0.05;
                    exit = k * 0.05;
                }
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    double ux = sp.Ux * dir, uz = sp.Uz * dir, ox = dir > 0 ? sp.X0 : sp.X1, oz = dir > 0 ? sp.Z0 : sp.Z1;
                    double en = dir > 0 ? enter : sp.Len - exit, ex = dir > 0 ? exit : sp.Len - enter;
                    double goal = Math.Min(sp.Len, ex + 1), axis = Math.Atan2(uz, ux);
                    var inputs = new List<(double off, int keys)>(); // keys: 0 stick, 1 one key, 2 two keys
                    for (int deg = -60; deg <= 60; deg += 15) inputs.Add((deg, 0));
                    for (int q = 0; q < 8; q++)
                    {
                        double off = Math.IEEERemainder(q * Math.PI / 4 - axis, 2 * Math.PI) * 180 / Math.PI;
                        if (Math.Abs(off) > 60.0001) continue;
                        inputs.Add((off, 1)); // one key in one key basis
                        inputs.Add((off, 2)); // two keys in the other
                    }
                    foreach (double f in new[] { -0.8, 0, 0.8 })
                    {
                        for (int fam = 0; fam < 2; fam++)
                        {
                            double l = f * band, sa = -1, sx = 0, sz = 0, from = fam == 0 ? Math.Max(0, en - 1) : en + 1.5;
                            for (double a = from; a <= from + 2; a += 0.25)
                            {
                                double px = ox + ux * a - uz * l, pz = oz + uz * a + ux * l;
                                if (Collide.ActorFits(w, px, pz, r)) { sa = a; sx = px; sz = pz; break; }
                            }
                            Assert.GreaterOrEqual(sa, 0, BridgeSpans.Name(s) + ": a fitting start");
                            foreach (var inp in inputs)
                            {
                                // Input that leaves the band before the chasm truly points off the deck: not a crossing.
                                if (fam == 0 && en > sa && Math.Abs(l + (en - sa) * Math.Tan(inp.off * Math.PI / 180)) > band + Collide.DeckPad) continue;
                                double th = axis + inp.off * Math.PI / 180, kx = Math.Cos(th), kz = Math.Sin(th);
                                int budget = (int)Math.Ceiling(1.25 * (goal - sa) / (speed * Math.Cos(inp.off * Math.PI / 180)) * Cfg.SimHz) + (int)Cfg.SimHz;
                                Place(w, sx, sz);
                                var along = new List<double> { sa };
                                bool arrived = false;
                                int stall = -1;
                                for (int t = 1; t <= budget && !arrived; t++)
                                {
                                    double mx = kx, mz = kz; // HumanInputState.WorldMove's order: lane assist (one key), then corner slide
                                    if (inp.keys == 1 && MoveAssist.LaneSteer(w.X[0], w.Z[0], mx, mz, out double lx, out double lz)) { mx = lx; mz = lz; }
                                    if (!(gate && HumanInputState.NearBridge(w.X[0], w.Z[0])) && MoveAssist.CornerSlide(w, 0, mx, mz, out double qx, out double qz)) { mx = qx; mz = qz; }
                                    w.Inputs[0].MoveX = mx;
                                    w.Inputs[0].MoveZ = mz;
                                    Movement.MovementSystem(w, Dt);
                                    double a = (w.X[0] - ox) * ux + (w.Z[0] - oz) * uz;
                                    along.Add(a);
                                    if (t >= 6 && a - along[t - 3] < 0.01 && stall < 0) stall = t;
                                    arrived = a >= goal;
                                }
                                runs++;
                                if (arrived && stall < 0) continue;
                                fails++;
                                if (example.Length == 0)
                                    example = $"{BridgeSpans.Name(s)} {(dir > 0 ? "fwd" : "back")} offset {f} {(fam == 0 ? "landing" : "deck")} {inp.off:0} deg ({(inp.keys == 0 ? "stick" : inp.keys + " key(s)")}): arrived {arrived}, stall tick {stall}";
                            }
                        }
                    }
                }
            }
            return fails;
        }
    }
}
