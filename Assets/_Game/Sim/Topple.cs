using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Knock-down decorations (CM-31, port of src/sim/topple.ts on 09e0904). Travis (2026-11-12, "What Demo needs"): "I have
    /// an idea for some decoration structures to be able to knock down. Knock it down one direction, ramp to another area.
    /// Knock it down a different direction, path block." The sim has no height, so (panel F4, D-065):
    /// <para>RAMP: every Void cell of the footprint becomes Bridge, a walkable strip across the void joining two islands.</para>
    /// <para>BLOCK: every walkable cell of the footprint becomes Wall, a strip across a path.</para>
    /// <para>The footprint (Footprint) is Topple.LengthCells cells beyond the base cell along the fall direction,
    /// Topple.WidthCells wide, centred on the base cell. Each piece (MapDef.Decorations) authors its two directions.</para>
    /// <para>Trigger: Combat calls ToppleInArc for every melee swing (any class, bots included). A standing piece whose base
    /// cell centre is within attackRange + Topple.Reach of the attacker and inside the swing arc loses one hit; the swing
    /// that empties it knocks it over AWAY from the attacker, along whichever authored direction is closer to that push
    /// (ModeFor). So a player chooses ramp or block by the side he hits it from.</para>
    /// <para>Every topple goes through the CM-30 route guard (Terraform.CellsKeepRoutes). A ramp only adds walkable ground;
    /// a block that would cut the last route is refused. A block is also refused while a living actor or the hatched Kaiju
    /// overlaps a cell it would wall in (nobody is ever crushed or walled in), and any topple waits MinerTerraform.MinGapTicks
    /// after the last field-invalidating change. A refused piece keeps one hit left: the next swing tries again.</para>
    /// <para>Events: every attempt to fall pushes one Topple event (A = attacker, -1 = none; B = decoration; (X, Z) = base
    /// cell centre; Value = Ramp / Block or a Refused* code). DG-09 adds ToppleHit per counted swing and ToppleDown when the
    /// fall animation time (Topple.FallTicks) has passed. Timing is in sim ticks. Allocation-free after the first call.</para>
    /// </summary>
    public static class Topple
    {
        /// <summary>ToppleState.Fallen values (and the success values of a Topple event).</summary>
        public const int Standing = 0;
        public const int Ramp = 1;
        public const int Block = 2;

        /// <summary>No such decoration, or it is already down.</summary>
        public const int RefusedFallen = -1;
        /// <summary>The fall would disconnect a team pad from the hill (or newly from the shrine).</summary>
        public const int RefusedRoute = -2;
        /// <summary>A living actor or the hatched Kaiju overlaps a cell the block would wall in.</summary>
        public const int RefusedOccupied = -3;
        /// <summary>Inside MinerTerraform.MinGapTicks of the last field-invalidating grid change.</summary>
        public const int RefusedRate = -4;

        private static int[] FootCells = new int[12];
        private static byte[] FootNew = new byte[12];

        private static void EnsureScratch(GameConfig c)
        {
            int n = FootprintMax(c);
            if (FootCells.Length >= n) return;
            FootCells = new int[n];
            FootNew = new byte[n];
        }

        /// <summary>Most cells a footprint can hold: LengthCells x WidthCells.</summary>
        public static int FootprintMax(GameConfig c)
        {
            return (int)c.Abilities.Topple.LengthCells * (int)c.Abilities.Topple.WidthCells;
        }

        /// <summary>The fall direction of mode (Ramp / Block) for decoration d.</summary>
        public static CellDir Dir(DecorationDef d, int mode)
        {
            return mode == Ramp ? d.Ramp : d.Block;
        }

        /// <summary>
        /// Footprint of d falling along dir: in-bounds cell indices written into output (row by row from the base outward,
        /// across the width in increasing perpendicular offset). Returns the count (at most LengthCells x WidthCells).
        /// </summary>
        public static int Footprint(GameConfig c, MapDef map, DecorationDef d, CellDir dir, int[] output)
        {
            int length = (int)c.Abilities.Topple.LengthCells;
            int width = (int)c.Abilities.Topple.WidthCells;
            int px = -dir.Dz;
            int pz = dir.Dx;
            int lo = -((width - 1) / 2);
            int hi = lo + width - 1;
            int n = 0;
            for (int k = 1; k <= length; k++)
            {
                for (int s = lo; s <= hi; s++)
                {
                    int cx = d.Cx + dir.Dx * k + px * s;
                    int cz = d.Cz + dir.Dz * k + pz * s;
                    if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) continue;
                    if (n < output.Length) output[n++] = cz * map.Width + cx;
                }
            }
            return n;
        }

        /// <summary>The cell type a footprint cell of type t becomes for mode (unchanged types return t).</summary>
        public static byte ToppledType(byte t, int mode)
        {
            if (mode == Ramp) return t == Cell.Void ? Cell.Bridge : t;
            return MapGrid.IsWalkableType(t) ? Cell.Wall : t;
        }

        /// <summary>
        /// Which way a push from (fromX, fromZ) knocks d over: the authored direction with the larger dot product against
        /// the push (base centre minus the pusher). Ties go to the ramp.
        /// </summary>
        public static int ModeFor(DecorationDef d, double fromX, double fromZ)
        {
            double vx = d.Cx + 0.5 - fromX;
            double vz = d.Cz + 0.5 - fromZ;
            double r = vx * d.Ramp.Dx + vz * d.Ramp.Dz;
            double b = vx * d.Block.Dx + vz * d.Block.Dz;
            return r >= b ? Ramp : Block;
        }

        /// <summary>
        /// Knock decoration k over as mode (Ramp / Block) on behalf of attacker (-1 = none). Returns Ramp / Block on success
        /// (cells written through MapGrid.SetCell, World.Topple updated) or a Refused* code (grid and piece unchanged).
        /// Pushes one Topple event either way. Checks fallen / rate / occupancy / route guard in that order.
        /// </summary>
        public static int ToppleDecoration(World w, int k, int mode, int attacker)
        {
            MapDef map = w.Map;
            DecorationDef[] decs = map.Decorations;
            if (decs == null || k < 0 || k >= decs.Length)
            {
                World.PushEvent(w, SimEventType.Topple, attacker, k, 0, 0, RefusedFallen);
                return RefusedFallen;
            }
            DecorationDef d = decs[k];
            double x = d.Cx + 0.5;
            double z = d.Cz + 0.5;
            ToppleState t = w.Topple;
            if (t.Fallen[k] != Standing)
            {
                World.PushEvent(w, SimEventType.Topple, attacker, k, x, z, RefusedFallen);
                return RefusedFallen;
            }
            if (w.Tick < w.WallChangeTick + (int)w.Config.Abilities.MinerTerraform.MinGapTicks)
            {
                World.PushEvent(w, SimEventType.Topple, attacker, k, x, z, RefusedRate);
                return RefusedRate;
            }
            EnsureScratch(w.Config);
            int n = Footprint(w.Config, map, d, Dir(d, mode), FootCells);
            int changes = 0;
            for (int j = 0; j < n; j++)
            {
                byte old = map.Cells[FootCells[j]];
                FootNew[j] = ToppledType(old, mode);
                if (FootNew[j] != old) changes++;
            }
            if (mode == Block)
            {
                for (int j = 0; j < n; j++)
                {
                    int i = FootCells[j];
                    if (FootNew[j] == map.Cells[i]) continue;
                    int cx = i % map.Width;
                    int cz = (i - cx) / map.Width;
                    if (Terraform.CellOccupied(w, cx, cz))
                    {
                        World.PushEvent(w, SimEventType.Topple, attacker, k, x, z, RefusedOccupied);
                        return RefusedOccupied;
                    }
                }
            }
            if (changes > 0 && !Terraform.CellsKeepRoutes(w, FootCells, FootNew, n))
            {
                World.PushEvent(w, SimEventType.Topple, attacker, k, x, z, RefusedRoute);
                return RefusedRoute;
            }
            double baseH = map.Height01[d.Cz * map.Width + d.Cx];
            for (int j = 0; j < n; j++)
            {
                int i = FootCells[j];
                if (FootNew[j] == map.Cells[i]) continue;
                int cx = i % map.Width;
                int cz = (i - cx) / map.Width;
                if (mode == Ramp) map.Height01[i] = baseH; // visual only: the ramp sits at the rim it fell from
                MapGrid.SetCell(w, cx, cz, FootNew[j]);
            }
            t.Fallen[k] = (byte)mode;
            t.Hp[k] = 0;
            t.FallTick[k] = w.Tick;
            World.PushEvent(w, SimEventType.Topple, attacker, k, x, z, mode);
            return mode;
        }

        /// <summary>
        /// One melee swing by actor i at (ax, az) facing (fx, fz) with reach range and arc cosine cosHalf (Combat passes the
        /// values it uses for actors). Each standing piece whose base cell centre is within range + Topple.Reach and inside
        /// the arc loses one hit (DG-09: one ToppleHit event); at its last hit it falls away from the attacker (ModeFor). A
        /// refused fall leaves it one hit from falling.
        /// </summary>
        public static void ToppleInArc(World w, int i, double ax, double az, double fx, double fz, double range, double cosHalf)
        {
            DecorationDef[] decs = w.Map.Decorations;
            if (decs == null) return;
            ToppleState t = w.Topple;
            double reach = range + w.Config.Abilities.Topple.Reach;
            for (int k = 0; k < decs.Length; k++)
            {
                if (t.Fallen[k] != Standing) continue;
                DecorationDef d = decs[k];
                double dx = d.Cx + 0.5 - ax;
                double dz = d.Cz + 0.5 - az;
                double d2 = dx * dx + dz * dz;
                if (d2 > reach * reach) continue;
                if (d2 > 1e-12 && (dx * fx + dz * fz) / Math.Sqrt(d2) < cosHalf - 1e-9) continue;
                if (t.Hp[k] > 1)
                {
                    t.Hp[k]--;
                    World.PushEvent(w, SimEventType.ToppleHit, i, k, d.Cx + 0.5, d.Cz + 0.5, t.Hp[k]);
                    continue;
                }
                t.Hp[k] = 1;
                World.PushEvent(w, SimEventType.ToppleHit, i, k, d.Cx + 0.5, d.Cz + 0.5, 0);
                ToppleDecoration(w, k, ModeFor(d, ax, az), i);
            }
        }

        /// <summary>DG-09 view helper: 0..1 through the fall of piece k at render interpolation alpha; -1 standing.</summary>
        public static double FallPhase(World w, int k, double alpha)
        {
            ToppleState t = w.Topple;
            if (k < 0 || k >= t.Fallen.Length || t.Fallen[k] == Standing) return -1;
            double ticks = w.Config.Abilities.Topple.FallTicks;
            if (!(ticks > 0)) return 1;
            double u = (w.Tick - t.FallTick[k] - 1 + alpha) / ticks;
            return u < 0 ? 0 : u > 1 ? 1 : u;
        }

        /// <summary>
        /// DG-09, per step after Combat: every piece whose fall animation has just ended (World.Tick = FallTick + FallTicks)
        /// pushes one ToppleDown event at the far end of its footprint (where the view lands the dust). Reads ticks only.
        /// </summary>
        public static void ToppleSystem(World w)
        {
            DecorationDef[] decs = w.Map.Decorations;
            if (decs == null) return;
            ToppleState t = w.Topple;
            int fall = (int)w.Config.Abilities.Topple.FallTicks;
            int length = (int)w.Config.Abilities.Topple.LengthCells;
            for (int k = 0; k < decs.Length; k++)
            {
                int mode = t.Fallen[k];
                if (mode == Standing || w.Tick != t.FallTick[k] + fall) continue;
                DecorationDef d = decs[k];
                CellDir dir = Dir(d, mode);
                World.PushEvent(w, SimEventType.ToppleDown, -1, k, d.Cx + 0.5 + dir.Dx * length, d.Cz + 0.5 + dir.Dz * length, mode);
            }
        }
    }
}
