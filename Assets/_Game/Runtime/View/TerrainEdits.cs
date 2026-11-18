using System.Collections.Generic;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// b14 terrain look (view only; reads the Sim, never writes it): which cells a dig, a crater, a fill or a place has
    /// changed since the match's map was painted, so the views draw them as what they are instead of as island edges.
    /// <para>The owner's complaint (2026-11-17 play): a dug cell was re-planned by the world dresser as a new island edge
    /// (lips and cliff skirts down to the island's underside), and the ground chunk left the cell out, so the player looked
    /// through the island at the sky; paving, grass and props whose pivot sat on the cell still drew over it. A placed cell
    /// was a flat ground tile with no sides. Now:</para>
    /// <list type="bullet">
    /// <item>PIT: a cell that was land when the map was painted and is Void now (a dig or a TNT crater). The dresser plans
    /// the edges from the PRISTINE cell (no new rim, no cliff into the sky), drops dressing on it, and TerrainEditMesh draws a
    /// pit (earth and rock walls, a floor, a spoil pile at the rim).</item>
    /// <item>FILLED: a pit filled back to Floor (it was land, became Void, is land again): a block flush with the ground.</item>
    /// <item>PLACED: a cell that was Void (or water) when the map was painted and is land now: an earth block hanging off the
    /// island edge, top at the cell's height, sides and bottom where they show.</item>
    /// </list>
    /// "Touched" is sticky per match: it is set the first time a cell changes between land and not-land and cleared only
    /// when the map is repainted (World.Epoch moves) or a new map arrives. Walls built on floor (LowWall, Barricade) are
    /// land to land, so they never touch a cell. The registry is one static per process, keyed by the MapDef object and the
    /// world epoch; Sync is idempotent per GridVersion and allocation-free after the first capture of a map.
    /// </summary>
    public static class TerrainEdits
    {
        /// <summary>What a changed cell is drawn as.</summary>
        public enum Kind : byte { None = 0, Pit = 1, Filled = 2, Placed = 3, Raised = 4 }

        static MapDef _map;
        static int _epoch = int.MinValue;
        static byte[] _pristine;
        static bool[] _touched;
        static byte[] _seen;
        static sbyte[] _seenLift;   // b15 terrain height
        static int _seenVersion = int.MinValue;

        /// <summary>Bumped every time a map is captured (match start, a repaint): views rebuild every chunk.</summary>
        public static int Captures { get; private set; }

        /// <summary>Bumped every time the touched mask grows.</summary>
        public static int Version { get; private set; }

        /// <summary>Cells touched in the current map (tests, captures).</summary>
        public static int TouchedCount { get; private set; }

        /// <summary>Land as the dresser and the ground chunks see it (WorldDresser.Solid).</summary>
        public static bool Land(byte t) => WorldDresser.Solid(t);

        /// <summary>
        /// What cell (pristine, current, touched) is drawn as. Pure: the views and the tests share it.
        /// </summary>
        public static Kind Classify(byte pristine, byte current, bool touched)
        {
            bool wasLand = Land(pristine);
            if (current == Cell.Void) return wasLand ? Kind.Pit : Kind.None;
            if (!Land(current)) return Kind.None;
            if (!wasLand) return Kind.Placed;            // land where the painted map had void or water
            return touched ? Kind.Filled : Kind.None;    // land again after it was dug
        }

        /// <summary>
        /// b15 terrain height: as Classify, then a Floor / BreakableDestroyed cell with a lift below 0 is a PIT (a walkable dug
        /// pit at true depth) and above 0 is RAISED (stacked blocks). lift 0 = the old rule exactly.
        /// </summary>
        public static Kind Classify(byte pristine, byte current, bool touched, sbyte lift)
        {
            if (lift != 0 && CriticalMass.Sim.Core.TerrainHeight.IsLiftType(current)) return lift < 0 ? Kind.Pit : Kind.Raised;
            if (lift > 0 && current == Cell.Bridge) return Kind.Raised;   // a height ladder on a stack: the stack still stands under it
            return Classify(pristine, current, touched);
        }

        /// <summary>Follow the world: capture a new map or a repaint, then mark cells whose land-ness changed.</summary>
        public static void Sync(World w)
        {
            if (w == null || w.Map == null || w.Map.Cells == null) return;
            byte[] cells = w.Map.Cells;
            if (w.Map != _map || w.Epoch != _epoch || _pristine == null || _pristine.Length != cells.Length)
            {
                _map = w.Map;
                _epoch = w.Epoch;
                _pristine = (byte[])cells.Clone();
                _seen = (byte[])cells.Clone();
                _seenLift = w.Map.Lift != null ? (sbyte[])w.Map.Lift.Clone() : null;
                _touched = new bool[cells.Length];
                _seenVersion = w.GridVersion;
                TouchedCount = 0;
                Captures++;
                Version++;
                return;
            }
            if (w.GridVersion == _seenVersion) return;
            _seenVersion = w.GridVersion;
            bool grew = false;
            for (int i = 0; i < cells.Length; i++)
            {
                byte now = cells[i];
                byte was = _seen[i];
                if (now == was) continue;
                _seen[i] = now;
                if (_touched[i] || Land(now) == Land(was)) continue;
                _touched[i] = true;
                TouchedCount++;
                grew = true;
            }
            // b15 terrain height: a cell whose level changed is touched too (the dresser drops its ground art there).
            sbyte[] lift = w.Map.Lift;
            if (lift != null && _seenLift != null && _seenLift.Length == lift.Length)
            {
                for (int i = 0; i < lift.Length; i++)
                {
                    if (lift[i] == _seenLift[i]) continue;
                    _seenLift[i] = lift[i];
                    if (_touched[i]) continue;
                    _touched[i] = true;
                    TouchedCount++;
                    grew = true;
                }
            }
            if (grew) Version++;
        }

        /// <summary>True when the registry follows this map.</summary>
        public static bool Follows(MapDef map) => map != null && map == _map && _pristine != null && _pristine.Length == map.Cells.Length;

        /// <summary>The cell's type when the map was painted (the current type when the registry does not follow the map).</summary>
        public static byte Pristine(MapDef map, int i) => Follows(map) ? _pristine[i] : map.Cells[i];

        public static bool Touched(MapDef map, int i) => Follows(map) && _touched[i];

        /// <summary>Classify cell i of a followed map (None for a map the registry does not follow).</summary>
        public static Kind KindAt(MapDef map, int i) => Follows(map) ? Classify(_pristine[i], map.Cells[i], _touched[i], map.Lift != null ? map.Lift[i] : (sbyte)0) : Kind.None;

        /// <summary>The registry's arrays for a followed map (null otherwise). Read only: never write into them.</summary>
        public static bool Arrays(MapDef map, out byte[] pristine, out bool[] touched)
        {
            bool ok = Follows(map);
            pristine = ok ? _pristine : null;
            touched = ok ? _touched : null;
            return ok;
        }

        /// <summary>
        /// The cells the dresser plans from: each touched cell as it was painted (so a pit or a placed cell never moves the
        /// island's rim, lips or cliffs), every other cell as it is now (breakables, built walls). Pure; allocates the array.
        /// </summary>
        public static byte[] PlanningCells(byte[] current, byte[] pristine, bool[] touched)
        {
            var c = (byte[])current.Clone();
            if (pristine == null || touched == null || pristine.Length != c.Length) return c;
            for (int i = 0; i < c.Length; i++) if (touched[i]) c[i] = pristine[i];
            return c;
        }

        /// <summary>PlanningCells for a map through the registry, plus a copy of the edited mask (safe for a worker thread).</summary>
        public static byte[] PlanningCells(MapDef map, out bool[] edited)
        {
            if (!Arrays(map, out byte[] p, out bool[] t))
            {
                edited = new bool[map.Cells.Length];
                return (byte[])map.Cells.Clone();
            }
            edited = (bool[])t.Clone();
            // A placed cell's land-ness differs from the painted map even when the registry missed the change (a view that
            // started mid-match): mark every cell whose land-ness differs from pristine as edited too.
            byte[] cur = map.Cells;
            for (int i = 0; i < cur.Length; i++) if (!edited[i] && Land(cur[i]) != Land(p[i])) edited[i] = true;
            return PlanningCells(cur, p, edited);
        }

        /// <summary>Placement categories kept on an edited cell: the island's own edge (planned from the pristine cell), the
        /// sky, bridges and water. Ground tiles, fill triangles, grass, flowers, pebbles, props, trees, crystals, paving and
        /// every bases piece whose pivot is on the cell go.</summary>
        static readonly HashSet<string> KeptCats = new HashSet<string> { "cliff", "edge", "sky", "bridge", "water" };

        /// <summary>True when a placement of category `cat` stays on an edited cell.</summary>
        public static bool KeptOnEdited(string cat) => cat != null && KeptCats.Contains(cat);

        /// <summary>
        /// Remove every placement whose pivot is on an edited cell (except KeptOnEdited categories) and mark the edited cells
        /// undressed, so the grey-box view (GridChunkMeshView + TerrainEditMesh) draws them. Returns how many were dropped.
        /// </summary>
        public static int FilterPlan(WorldPlan plan, bool[] edited)
        {
            if (plan == null || edited == null) return 0;
            int w = plan.Width, h = plan.Height;
            int dropped = plan.Placements.RemoveAll(p =>
            {
                if (KeptOnEdited(p.Cat)) return false;
                int cx = (int)System.Math.Floor(p.SimX), cz = (int)System.Math.Floor(p.SimZ);
                if (cx < 0 || cz < 0 || cx >= w || cz >= h) return false;
                return edited[cz * w + cx];
            });
            if (plan.Dressed != null)
                for (int i = 0; i < plan.Dressed.Length && i < edited.Length; i++) if (edited[i]) plan.Dressed[i] = false;
            return dropped;
        }
    }
}
