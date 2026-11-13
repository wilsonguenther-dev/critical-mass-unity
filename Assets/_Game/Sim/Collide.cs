using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Collision (browser collide.ts): actor squares against the grid, then soft actor-actor separation.
    /// <para>CM-32: an AIRBORNE actor (Actor.AirLen &gt; 0, Jump.cs) also passes over in-map VOID cells (a gap), never over
    /// walls, water, intact breakables, barricades or the map edge. Landing is guarded by Jump.cs with FootprintClear.</para>
    /// <para>DG-02 / CM-33 (browser branch cm/CM-33 at 003c970, D-069): DIAGONAL bridges are walked against a straight
    /// rail, not the cell staircase. The cell raster is unchanged. A grounded actor may also stand where its circle lies
    /// within BridgeHalfWidth - r (+ DeckPad) of a diagonal span's centre segment, the axis cells under it are walkable and
    /// every blocked cell its square touches is in-map VOID (the staircase notches under the drawn deck). Near a diagonal
    /// span a step the box rule would block is accepted when it lands on that deck, else slid along the rail. Off every
    /// rail capture zone, and for any step the box rule does not block from a box-legal start, the result is bit-identical
    /// to the plain push-out.</para>
    /// </summary>
    public static class Collide
    {
        /// <summary>Push-out margin so a resting actor sits strictly clear of blocked cells (far above the Float32 ulp).</summary>
        private const double Skin = 1e-3;

        /// <summary>Cell-range tolerance, applied outward: an edge within RangeEps of a cell boundary touches the next cell.</summary>
        private const double RangeEps = 1e-6;

        /// <summary>Max distance per collision sub-step (well under one cell, so no tunnelling).</summary>
        private const double MaxSubstep = 0.4;

        /// <summary>
        /// CM-33: deck band pad beyond BridgeHalfWidth - r. DERIVED: much more than the Float32 ulp at x &lt; 128 (7.6e-6),
        /// so a rail target set exactly on the band edge always tests as on the deck; much less than the view's deck margin.
        /// </summary>
        public const double DeckPad = 0.01;

        /// <summary>CM-33: lateral reach beyond BridgeHalfWidth within which a blocked step is slid along a diagonal rail. [guess]</summary>
        public const double RailCapture = 1.0;

        /// <summary>Browser MAX_RADIUS: the largest of the warrior, farmer and miner radii, read from the world's config.</summary>
        private static double MaxRadius(GameConfig c)
        {
            return Math.Max(c.Class(ClassId.Warrior).Radius, Math.Max(c.Class(ClassId.Farmer).Radius, c.Class(ClassId.Miner).Radius));
        }

        private static int[] Neighbours = new int[64];

        /// <summary>Collision radius of actor id (its class radius).</summary>
        public static double ActorRadius(World w, int id)
        {
            return w.Config.Class(w.Actors[id].Cls).Radius;
        }

        /// <summary>True when cell (cx, cz) blocks the actor: any non-walkable cell.</summary>
        private static bool CellBlocks(World w, int cx, int cz, bool air)
        {
            MapDef map = w.Map;
            if (MapGrid.IsWalkable(map, cx, cz)) return false;
            // CM-32: airborne actors pass over in-map VOID.
            return !(air && MapGrid.InBounds(map, cx, cz) && map.Cells[cz * map.Width + cx] == Cell.Void);
        }

        private static bool RowBlocked(World w, int cx, double z, double r, bool air)
        {
            int z0 = JsMath.FloorToInt(z - r - RangeEps);
            int z1 = JsMath.FloorToInt(z + r + RangeEps);
            for (int cz = z0; cz <= z1; cz++) if (CellBlocks(w, cx, cz, air)) return true;
            return false;
        }

        private static bool ColBlocked(World w, int cz, double x, double r, bool air)
        {
            int x0 = JsMath.FloorToInt(x - r - RangeEps);
            int x1 = JsMath.FloorToInt(x + r + RangeEps);
            for (int cx = x0; cx <= x1; cx++) if (CellBlocks(w, cx, cz, air)) return true;
            return false;
        }

        /// <summary>
        /// CM-32: true when actor id may stand centred at (x, z): the box rule (its square touches only walkable cells) or
        /// a diagonal deck (CM-33). Jump.cs lands an actor only where this holds; DG-02 coyote time reads it for support.
        /// </summary>
        public static bool FootprintClear(World w, int id, double x, double z)
        {
            return ActorFits(w, x, z, ActorRadius(w, id));
        }

        /// <summary>The plain box rule: the square of half side r centred at (x, z) touches only walkable cells.</summary>
        private static bool BoxFits(World w, double x, double z, double r)
        {
            int x0 = JsMath.FloorToInt(x - r - RangeEps);
            int x1 = JsMath.FloorToInt(x + r + RangeEps);
            int z0 = JsMath.FloorToInt(z - r - RangeEps);
            int z1 = JsMath.FloorToInt(z + r + RangeEps);
            for (int cz = z0; cz <= z1; cz++) for (int cx = x0; cx <= x1; cx++) if (!MapGrid.IsWalkable(w.Map, cx, cz)) return false;
            return true;
        }

        /// <summary>CM-33: every non-walkable cell the square touches is in-map VOID (no wall, water, breakable, barricade or edge).</summary>
        private static bool OnlyVoidBlocks(World w, double x, double z, double r)
        {
            MapDef map = w.Map;
            int x0 = JsMath.FloorToInt(x - r - RangeEps);
            int x1 = JsMath.FloorToInt(x + r + RangeEps);
            int z0 = JsMath.FloorToInt(z - r - RangeEps);
            int z1 = JsMath.FloorToInt(z + r + RangeEps);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (MapGrid.IsWalkable(map, cx, cz)) continue;
                    if (!MapGrid.InBounds(map, cx, cz) || map.Cells[cz * map.Width + cx] != Cell.Void) return false;
                }
            }
            return true;
        }

        /// <summary>CM-33: the axis cells under the body (along a - r, a, a + r, clamped to the span) are walkable: the deck is intact.</summary>
        private static bool AxisSupported(World w, BridgeSpan s, double a, double r)
        {
            for (int k = -1; k <= 1; k++)
            {
                double t = a + k * r;
                if (t < 0) t = 0;
                else if (t > s.Len) t = s.Len;
                if (!MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(s.X0 + s.Ux * t), JsMath.FloorToInt(s.Z0 + s.Uz * t))) return false;
            }
            return true;
        }

        /// <summary>
        /// CM-33: the diagonal span whose deck carries a circle of radius r at (x, z), else null: the centre within
        /// BridgeHalfWidth - r + DeckPad of the span's centre segment, the deck intact under it and only VOID among the
        /// blocked cells its square touches. First match in table order (deterministic).
        /// </summary>
        public static BridgeSpan DeckSpanAt(World w, double x, double z, double r)
        {
            double band = SkybreakBasinMap.BridgeHalfWidth - r + DeckPad;
            bool voidChecked = false;
            IReadOnlyList<BridgeSpan> spans = BridgeSpans.Diagonal;
            for (int k = 0; k < spans.Count; k++)
            {
                BridgeSpan s = spans[k];
                if (BridgeSpans.Distance(s, x, z) > band) continue;
                if (!AxisSupported(w, s, BridgeSpans.Along(s, x, z), r)) continue;
                if (!voidChecked)
                {
                    if (!OnlyVoidBlocks(w, x, z, r)) return null;
                    voidChecked = true;
                }
                return s;
            }
            return null;
        }

        /// <summary>CM-33: true when a grounded circle of radius r may stand at (x, z): the box rule, or on a diagonal deck.</summary>
        public static bool ActorFits(World w, double x, double z, double r)
        {
            return BoxFits(w, x, z, r) || DeckSpanAt(w, x, z, r) != null;
        }

        /// <summary>
        /// CM-33: the diagonal span whose rail captures (x, z): along within [-BridgeHalfWidth, Len + BridgeHalfWidth] and
        /// |lateral| at most BridgeHalfWidth + RailCapture; the smallest |lateral| wins, ties to the first in table order.
        /// </summary>
        private static BridgeSpan RailSpanAt(double x, double z)
        {
            double hw = SkybreakBasinMap.BridgeHalfWidth;
            BridgeSpan best = null;
            double bestL = hw + RailCapture;
            IReadOnlyList<BridgeSpan> spans = BridgeSpans.Diagonal;
            for (int k = 0; k < spans.Count; k++)
            {
                BridgeSpan s = spans[k];
                double a = BridgeSpans.Along(s, x, z);
                if (a < -hw || a > s.Len + hw) continue;
                double l = Math.Abs(BridgeSpans.Lateral(s, x, z));
                if (l < bestL || (best == null && l <= bestL))
                {
                    best = s;
                    bestL = l;
                }
            }
            return best;
        }

        /// <summary>
        /// DG-02 (not in the browser; fixes the browser branch's own failing "-60..+60 deg" case): a blocked step from a
        /// box-legal start, heading along the span (|along part| at least RailAlongFraction of the step), tries the rail
        /// before the plain slide (lx, lz) when the start is OFF the deck band, or when the plain slide keeps less than half
        /// of the step's along part. Without it the plain slide drags a body walking 15 to 60 degrees off the axis down the
        /// staircase beside the deck into a riser corner, where it oscillates (rail pull up, plain slide down) and never
        /// crosses, or slides it backwards along the island edge at a bridge mouth. A step across the axis keeps the plain slide.
        /// </summary>
        private static bool RailFirst(BridgeSpan s, double x, double z, double lx, double lz, double sx, double sz, double r)
        {
            double aStep = sx * s.Ux + sz * s.Uz;
            if (Math.Abs(aStep) < RailAlongFraction * BridgeSpans.Hypot(sx, sz)) return false;
            double band = SkybreakBasinMap.BridgeHalfWidth - r;
            if (Math.Abs(BridgeSpans.Lateral(s, x, z)) > band + DeckPad) return true;
            double plainAlong = (lx - x) * s.Ux + (lz - z) * s.Uz;
            return (aStep > 0 ? plainAlong : -plainAlong) < 0.5 * Math.Abs(aStep);
        }

        /// <summary>
        /// DG-02 (not in the browser): a free step heading along the span (RailFirst's along test) from the band edge or
        /// beyond, over the chasm part (axis cell BRIDGE), whose end would sit further from the axis (the staircase beside a diagonal deck),
        /// keeps its along part and no outward lateral part: the body rides the rail line instead of drifting down the
        /// staircase into a riser corner. False (use the free step) anywhere else, or when that end does not fit.
        /// </summary>
        private static bool NoOutwardDrift(World w, BridgeSpan s, double x, double z, double tx, double tz, double sx, double sz, double r, out double ox, out double oz)
        {
            ox = tx;
            oz = tz;
            double aStep = sx * s.Ux + sz * s.Uz;
            if (Math.Abs(aStep) < RailAlongFraction * BridgeSpans.Hypot(sx, sz)) return false;
            double band = SkybreakBasinMap.BridgeHalfWidth - r;
            double l0 = BridgeSpans.Lateral(s, x, z);
            double lt = BridgeSpans.Lateral(s, tx, tz);
            if (Math.Abs(lt) <= band + DeckPad || Math.Abs(lt) <= Math.Abs(l0) || Math.Abs(l0) < band) return false;
            // Over the chasm part only (the axis cell under the body is BRIDGE), never on a landing.
            double a0 = BridgeSpans.Along(s, x, z);
            if (MapGrid.CellAt(w.Map, s.X0 + s.Ux * a0, s.Z0 + s.Uz * a0) != Cell.Bridge) return false;
            double qx = JsMath.F32(x + s.Ux * aStep);
            double qz = JsMath.F32(z + s.Uz * aStep);
            if (!ActorFits(w, qx, qz, r)) return false;
            ox = qx;
            oz = qz;
            return true;
        }

        /// <summary>DG-02: the share of a blocked step the off-band rail pull may use while keeping the along part (0.6 leaves 0.8). [guess]</summary>
        public const double RailPullShare = 0.6;

        /// <summary>DG-02: cos 65 degrees rounded down; steps within about 65 degrees of the axis count as heading along it. [guess]</summary>
        public const double RailAlongFraction = 0.42;

        /// <summary>
        /// CM-33: the rail's answer to a blocked sub-step (sx, sz) from (x, z) on span s (false = no motion). On the band
        /// the step keeps its along-axis part and its lateral end is clamped to the band edge. Outside the band the centre is
        /// pulled toward it by at most the step's length, keeping as much of the along-axis part as the rest of that length
        /// allows (keepAlong false: the pull alone).
        /// </summary>
        private static bool RailTarget(BridgeSpan s, double x, double z, double sx, double sz, double r, bool keepAlong, out double ox, out double oz)
        {
            ox = x;
            oz = z;
            double band = SkybreakBasinMap.BridgeHalfWidth - r;
            double l0 = BridgeSpans.Lateral(s, x, z);
            double aStep = sx * s.Ux + sz * s.Uz;
            double lStep = sz * s.Ux - sx * s.Uz;
            double da;
            double dl;
            if (Math.Abs(l0) <= band + DeckPad)
            {
                double lt = l0 + lStep;
                if (lt > band) lt = band;
                else if (lt < -band) lt = -band;
                dl = lt - l0;
                da = aStep;
            }
            else
            {
                double len = BridgeSpans.Hypot(sx, sz);
                // DG-02: with keepAlong the pull takes at most RailPullShare of the step, so the along part survives (the
                // browser pulled by the whole step first, which left 3-tick stalls beside the deck at 30 degrees).
                double pull = Math.Min(Math.Abs(l0) - band, keepAlong ? RailPullShare * len : len);
                dl = l0 > 0 ? -pull : pull;
                double room = Math.Sqrt(Math.Max(0, len * len - pull * pull));
                da = !keepAlong ? 0 : aStep > room ? room : aStep < -room ? -room : aStep;
            }
            if (dl == 0 && da == 0) return false;
            ox = JsMath.F32(x + s.Ux * da - s.Uz * dl);
            oz = JsMath.F32(z + s.Uz * da + s.Ux * dl);
            return true;
        }

        /// <summary>
        /// DG-97 (S10): an axis step into blocked line `line` (a column when alongX, else a row) that clips only the end cell
        /// of the body's span by at most CornerNudgeMax: shift the cross coordinate `other` off that cell by the overlap (at
        /// most `stepLen`) when the shifted body clears the line, so the body slides round the corner. False = still blocked.
        /// </summary>
        private static bool CornerNudge(World w, int line, bool alongX, double r, double stepLen, double along, ref double other)
        {
            double max = w.Config.Physics.CornerNudgeMax;
            int lo = JsMath.FloorToInt(other - r - RangeEps);
            int hi = JsMath.FloorToInt(other + r + RangeEps);
            bool loB = alongX ? CellBlocks(w, line, lo, false) : CellBlocks(w, lo, line, false);
            bool hiB = alongX ? CellBlocks(w, line, hi, false) : CellBlocks(w, hi, line, false);
            for (int c = lo + 1; c < hi; c++) if (alongX ? CellBlocks(w, line, c, false) : CellBlocks(w, c, line, false)) return false;
            double shift;
            if (loB && !hiB) shift = (lo + 1) - (other - r) + Skin;
            else if (hiB && !loB) shift = -((other + r) - hi + Skin);
            else return false;
            if (Math.Abs(shift) > max + Skin + 1e-6 || Math.Abs(shift) > stepLen + Skin) return false; // 1e-6: Float32 positions
            double moved = JsMath.F32(other + shift);
            if (!(alongX ? BoxFits(w, along, moved, r) : BoxFits(w, moved, along, r))) return false;
            other = moved;
            return true;
        }

        /// <summary>Move actor id by (dx, dz) against the grid, sliding along walls. Does not touch other actors.</summary>
        public static void MoveActor(World w, int id, double dx, double dz, bool air = false)
        {
            if (!(dx == dx) || !(dz == dz)) return; // NaN guard
            SweepActor(w, id, w.X[id], w.Z[id], dx, dz, air, out double x, out double z);
            w.X[id] = JsMath.F32(x);
            w.Z[id] = JsMath.F32(z);
        }

        /// <summary>The pure core of MoveActor: sweep actor id's square from (x0, z0) by (dx, dz); touches no world state.</summary>
        public static void SweepActor(World w, int id, double x0, double z0, double dx, double dz, bool air, out double x, out double z)
        {
            x = x0;
            z = z0;
            if (!(dx == dx) || !(dz == dz)) return; // NaN guard
            double r = ActorRadius(w, id);
            double big = Math.Max(Math.Abs(dx), Math.Abs(dz));
            int steps = big > MaxSubstep ? (int)Math.Ceiling(big / MaxSubstep) : 1;
            double sx = dx / steps;
            double sz = dz / steps;
            double px = x0;
            double pz = z0;
            for (int s = 0; s < steps; s++)
            {
                // The plain axis-separated push-out (resolve X, then Z): bit-identical to the pre-CM-33 sweep.
                double lx = px;
                double lz = pz;
                bool corners = !air && Phys.On(w, w.Config.Physics.CornerRounding); // DG-97 (S10): slide round convex corners
                if (sx != 0)
                {
                    lx = JsMath.F32(lx + sx);
                    if (sx > 0)
                    {
                        int cx = JsMath.FloorToInt(lx + r + RangeEps);
                        if (RowBlocked(w, cx, lz, r, air) && !(corners && CornerNudge(w, cx, true, r, Math.Abs(sx), lx, ref lz))) lx = JsMath.F32(cx - r - Skin);
                    }
                    else
                    {
                        int cx = JsMath.FloorToInt(lx - r - RangeEps);
                        if (RowBlocked(w, cx, lz, r, air) && !(corners && CornerNudge(w, cx, true, r, Math.Abs(sx), lx, ref lz))) lx = JsMath.F32(cx + 1 + r + Skin);
                    }
                }
                if (sz != 0)
                {
                    lz = JsMath.F32(lz + sz);
                    if (sz > 0)
                    {
                        int cz = JsMath.FloorToInt(lz + r + RangeEps);
                        if (ColBlocked(w, cz, lx, r, air) && !(corners && CornerNudge(w, cz, false, r, Math.Abs(sz), lz, ref lx))) lz = JsMath.F32(cz - r - Skin);
                    }
                    else
                    {
                        int cz = JsMath.FloorToInt(lz - r - RangeEps);
                        if (ColBlocked(w, cz, lx, r, air) && !(corners && CornerNudge(w, cz, false, r, Math.Abs(sz), lz, ref lx))) lz = JsMath.F32(cz + 1 + r + Skin);
                    }
                }
                // CM-33: grounded, inside a diagonal rail's capture zone: the deck and the rail (see the class summary).
                // DG-02: the person-steered actor only (the human, not on autopilot). Bots path on the flow field over the
                // unchanged raster; the browser branch gave every actor the rail and its own bot test then failed (hill
                // presence 0.49 of the 0.70 floor, also 0.49 in this port), so bots keep the plain push-out, as on main.
                BridgeSpan rs = air || !w.Actors[id].IsHuman || Bots.BotBrain.ActorZeroOnAutopilot(w) ? null : RailSpanAt(px, pz);
                if (rs == null)
                {
                    if (!air && Phys.On(w, true) && (lx != JsMath.F32(px + sx) || lz != JsMath.F32(pz + sz))
                        && !BoxFits(w, px, pz, r) && DeckSpanAt(w, px, pz, r) != null)
                    {
                        // A bot standing on a diagonal deck (it landed a jump there): the plain push-out would snap it a
                        // whole cell into the VOID beside the deck. Step only to where it may stand (deck or box rule).
                        double fx = JsMath.F32(px + sx), fz = JsMath.F32(pz + sz);
                        if (ActorFits(w, fx, fz, r)) { px = fx; pz = fz; }
                        else if (ActorFits(w, fx, pz, r)) px = fx;
                        else if (ActorFits(w, px, fz, r)) pz = fz;
                        continue;
                    }
                    px = lx;
                    pz = lz;
                    continue;
                }
                bool startBox = BoxFits(w, px, pz, r);
                if (!startBox && DeckSpanAt(w, px, pz, r) == null)
                {
                    // The grid changed under a deck-standing actor (a removed span): the plain rule. The browser logs this
                    // once per episode; the Sim has no logger (conventions 11).
                    px = lx;
                    pz = lz;
                    continue;
                }
                double tx = JsMath.F32(px + sx);
                double tz = JsMath.F32(pz + sz);
                bool pushed = lx != tx || lz != tz;
                if (!pushed && (startBox || ActorFits(w, tx, tz, r)))
                {
                    if (NoOutwardDrift(w, rs, px, pz, tx, tz, sx, sz, r, out double qx, out double qz))
                    {
                        px = qx;
                        pz = qz;
                        continue;
                    }
                    px = tx;
                    pz = tz;
                    continue;
                }
                if (pushed && DeckSpanAt(w, tx, tz, r) != null)
                {
                    px = tx;
                    pz = tz;
                    continue;
                }
                if (pushed && startBox && (lx != px || lz != pz) && !RailFirst(rs, px, pz, lx, lz, sx, sz, r))
                {
                    // A box-legal start the plain rule still moves: keep the plain slide (walls, island edges).
                    px = lx;
                    pz = lz;
                    continue;
                }
                if (RailTarget(rs, px, pz, sx, sz, r, true, out double rx, out double rz) && ActorFits(w, rx, rz, r))
                {
                    px = rx;
                    pz = rz;
                    continue;
                }
                if (RailTarget(rs, px, pz, sx, sz, r, false, out rx, out rz) && ActorFits(w, rx, rz, r))
                {
                    px = rx;
                    pz = rz;
                    continue;
                }
                if (startBox)
                {
                    px = lx;
                    pz = lz;
                }
                // else: a deck-standing actor whose step and rail slide are both blocked (a wall or barricade on the deck) stays.
            }
            x = px;
            z = pz;
        }

        /// <summary>
        /// DG-86 (S5, OD-5): the hatched Kaiju is an immovable disc of PhysicsConfig.KaijuBodyRadius. Every living, grounded
        /// actor overlapping it is pushed straight out through MoveActor (walls stop it); one pinned against a wall slides
        /// round the body instead. No trample damage. One BodyShove event per tick that shoved anyone.
        /// </summary>
        public static void KaijuBody(World w)
        {
            Kaiju k = w.Kaiju;
            if (k.State == KaijuState.Egg || !Phys.On(w, w.Config.Physics.SolidKaiju)) return;
            double body = w.Config.Physics.KaijuBodyRadius;
            int shoved = 0;
            double most = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.AirLen > 0) continue;
                double rr = body + ActorRadius(w, i);
                double dx = w.X[i] - k.X;
                double dz = w.Z[i] - k.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 >= rr * rr) continue;
                double d = Math.Sqrt(d2);
                double ux;
                double uz;
                if (d < 1e-6)
                {
                    ux = -JsMath.Cos(k.Facing);
                    uz = -JsMath.Sin(k.Facing);
                }
                else
                {
                    ux = dx / d;
                    uz = dz / d;
                }
                double push = rr - d;
                double x0 = w.X[i];
                double z0 = w.Z[i];
                MoveActor(w, i, ux * push, uz * push);
                double ex = w.X[i] - k.X;
                double ez = w.Z[i] - k.Z;
                if (ex * ex + ez * ez < (rr - 0.05) * (rr - 0.05))
                {
                    // Pinned against a wall: slide round the body (the tangent closer to its own motion, else counter-clockwise).
                    double side = w.Vx[i] * -uz + w.Vz[i] * ux >= 0 ? 1 : -1;
                    MoveActor(w, i, -uz * side * push * 1.5, ux * side * push * 1.5);
                    ex = w.X[i] - k.X;
                    ez = w.Z[i] - k.Z;
                    double e2 = ex * ex + ez * ez;
                    if (e2 < (rr - 0.05) * (rr - 0.05))
                    {
                        // Still wedged (a wall on both sides): the body gives way by the rest, through its own wall collider.
                        double e = Math.Sqrt(e2);
                        double vx = e > 1e-6 ? ex / e : ux;
                        double vz = e > 1e-6 ? ez / e : uz;
                        Kaiju.ShoveKaiju(w, -vx * (rr - e), -vz * (rr - e));
                    }
                }
                double moved = Math.Sqrt((w.X[i] - x0) * (w.X[i] - x0) + (w.Z[i] - z0) * (w.Z[i] - z0));
                if (moved > most) most = moved;
                shoved++;
            }
            if (shoved > 0)
                World.PushEvent(w, SimEventType.BodyShove, Damage.KaijuSource, -1, k.X, k.Z, shoved, null, DamageCause.None, body,
                    JsMath.Cos(k.Facing), JsMath.Sin(k.Facing), most, 0, 0, 0);
        }

        /// <summary>Soft actor-actor separation after all moves: each overlapping living pair is pushed apart through MoveActor.</summary>
        public static void SeparateActors(World w)
        {
            SeparatePairs(w);
            KaijuBody(w); // DG-86
        }

        private static double[] SepX = new double[0];
        private static double[] SepZ = new double[0];
        private static double[] SepMoved = new double[0];

        /// <summary>
        /// DG-97 (S9, OD-9): order-free relaxed (Jacobi) separation. Each of SeparationIterations passes accumulates every
        /// overlapping pair's push into scratch (split by ClassConfig.Mass; teammates AllyPushShare as hard; a getting-up actor
        /// immovable), scaled by SeparationRelax, then applies them through MoveActor, each actor capped at SeparationMaxStep
        /// per tick over all passes. No id order decides who moves.
        /// </summary>
        private static void SeparateRelaxed(World w)
        {
            int n = w.ActorCount;
            PhysicsConfig p = w.Config.Physics;
            if (SepX.Length < n)
            {
                SepX = new double[n];
                SepZ = new double[n];
                SepMoved = new double[n];
            }
            if (Neighbours.Length < w.Hash.Capacity) Neighbours = new int[w.Hash.Capacity];
            double reach = 2 * MaxRadius(w.Config);
            for (int i = 0; i < n; i++) SepMoved[i] = 0;
            for (int iter = 0; iter < p.SeparationIterations; iter++)
            {
                SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, n);
                for (int i = 0; i < n; i++)
                {
                    SepX[i] = 0;
                    SepZ[i] = 0;
                }
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    Actor ai = w.Actors[i];
                    if (!ai.Alive || ai.AirLen > 0) continue;
                    double ri = ActorRadius(w, i);
                    int cnt = SpatialHash.QueryCircle(w.Hash, w.X[i], w.Z[i], reach, Neighbours);
                    for (int k = 0; k < cnt; k++)
                    {
                        int j = Neighbours[k];
                        Actor aj = w.Actors[j];
                        if (j <= i || !aj.Alive || aj.AirLen > 0) continue;
                        double rr = ri + ActorRadius(w, j);
                        double ddx = w.X[j] - w.X[i];
                        double ddz = w.Z[j] - w.Z[i];
                        double d2 = ddx * ddx + ddz * ddz;
                        if (d2 >= rr * rr) continue;
                        bool iFixed = ai.StandUp > 0;
                        bool jFixed = aj.StandUp > 0;
                        if (iFixed && jFixed) continue;
                        double d = Math.Sqrt(d2);
                        if (d < 1e-6)
                        {
                            double a = (Math.Min(i, j) * 31 + Math.Max(i, j) * 17) * 2.39996;
                            ddx = JsMath.Cos(a);
                            ddz = JsMath.Sin(a);
                            d = 0;
                        }
                        else
                        {
                            ddx /= d;
                            ddz /= d;
                        }
                        double overlap = (rr - d) * p.SeparationRelax;
                        if (ai.Team == aj.Team) overlap *= p.AllyPushShare;
                        double mi = p.SeparationByMass ? w.Config.Class(ai.Cls).Mass : 1;
                        double mj = p.SeparationByMass ? w.Config.Class(aj.Cls).Mass : 1;
                        double pushI = jFixed ? overlap : iFixed ? 0 : overlap * mj / (mi + mj);
                        double pushJ = iFixed ? overlap : jFixed ? 0 : overlap - pushI;
                        SepX[i] -= ddx * pushI;
                        SepZ[i] -= ddz * pushI;
                        SepX[j] += ddx * pushJ;
                        SepZ[j] += ddz * pushJ;
                        any = true;
                    }
                }
                if (!any) break;
                for (int i = 0; i < n; i++)
                {
                    double sx = SepX[i];
                    double sz = SepZ[i];
                    if (sx == 0 && sz == 0) continue;
                    double len = Math.Sqrt(sx * sx + sz * sz);
                    double room = p.SeparationMaxStep - SepMoved[i];
                    if (room <= 0) continue;
                    if (len > room)
                    {
                        sx *= room / len;
                        sz *= room / len;
                        len = room;
                    }
                    SepMoved[i] += len;
                    MoveActor(w, i, sx, sz);
                }
            }
        }

        private static void SeparatePairs(World w)
        {
            if (Phys.On(w, w.Config.Physics.RelaxedSeparation))
            {
                SeparateRelaxed(w);
                return;
            }
            int n = w.ActorCount;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, n);
            if (Neighbours.Length < w.Hash.Capacity) Neighbours = new int[w.Hash.Capacity];
            double reach = 2 * MaxRadius(w.Config);
            for (int i = 0; i < n; i++)
            {
                // CM-32: an airborne actor is over everyone's head: it neither pushes nor is pushed until it lands.
                if (!w.Actors[i].Alive || w.Actors[i].AirLen > 0) continue;
                double ri = ActorRadius(w, i);
                int cnt = SpatialHash.QueryCircle(w.Hash, w.X[i], w.Z[i], reach, Neighbours);
                for (int k = 0; k < cnt; k++)
                {
                    int j = Neighbours[k];
                    if (j <= i || !w.Actors[j].Alive || w.Actors[j].AirLen > 0) continue;
                    double rr = ri + ActorRadius(w, j);
                    double ddx = w.X[j] - w.X[i];
                    double ddz = w.Z[j] - w.Z[i];
                    double d2 = ddx * ddx + ddz * ddz;
                    if (d2 >= rr * rr) continue;
                    double d = Math.Sqrt(d2);
                    if (d < 1e-6)
                    {
                        double a = (i * 31 + j * 17) * 2.39996;
                        ddx = JsMath.Cos(a);
                        ddz = JsMath.Sin(a);
                        d = 0;
                    }
                    else
                    {
                        ddx /= d;
                        ddz /= d;
                    }
                    // CM-20: an actor still getting up (StandUp > 0) is immovable; the other actor takes the full push.
                    bool iFixed = w.Actors[i].StandUp > 0;
                    bool jFixed = w.Actors[j].StandUp > 0;
                    if (iFixed && jFixed) continue;
                    double overlap = rr - d;
                    double pushI = jFixed ? overlap : iFixed ? 0 : overlap * 0.5;
                    double pushJ = overlap - pushI;
                    if (pushI > 0) MoveActor(w, i, -ddx * pushI, -ddz * pushI);
                    if (pushJ > 0) MoveActor(w, j, ddx * pushJ, ddz * pushJ);
                }
            }
        }
    }
}
