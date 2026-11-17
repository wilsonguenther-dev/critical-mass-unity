using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Input
{
    /// <summary>b14 (owner: "the F control doesn't work for building or building straight ladders"): the Builder's tools, in
    /// wheel order. Number keys 1 to 7 pick them in this order.</summary>
    public enum BuilderTool : byte { Dig = 0, Place = 1, Ladder = 2, Bridge = 3, ShortWall = 4, TallWall = 5, Block = 6 }

    /// <summary>
    /// b14: ONE tool wheel for the Builder, in the human input layer (view side; the sim is untouched). One key uses the
    /// selected tool on the aimed cell (F, or the controller's Y; the class-ability buttons, right click, K / X, LT / B, are
    /// aliases on the Builder), one key cycles (the wheel, R / I, the left bumper), number keys pick directly.
    /// <para>How it reaches the sim with the sim's own inputs only: DIG, PLACE and the three wall pieces are the sim's own
    /// selection (BuilderWalls.TakeBuilderSwitch / Terraform), reached by sending one InputFrame.TerraformSwitch per tick
    /// until the predicted sim selection equals the wanted one (the use press waits for the last switch, so a switch and a
    /// press never share a frame); the use press is then InputFrame.Terraform. LADDER and BRIDGE are the Builder's ability
    /// in the sim (BuiltPieces.CastBuild): the use press becomes InputFrame.Ability with the aim point moved onto the chosen
    /// grid axis, so the sim's AxisToward lays the piece exactly where the ghost showed. While a ladder or bridge is
    /// selected the sim selection parks on PLACE, so the wall ghost (WallView) stays hidden.</para>
    /// <para>Direction (owner: ladders "not straight"): the camera looks down a diagonal (yaw 225), so the grid axis nearest the
    /// aim flips on the diagonal. ChooseAxis keeps the current axis until the aim is more than 45 + HysteresisDeg degrees
    /// from it; for a ladder or bridge an axis that has a valid piece of that kind (a cliff face for a ladder, a gap for a
    /// bridge) wins among the axes within that cone, so the ladder lies flat against the face the player is looking at.</para>
    /// Every input here is a plain InputFrame field, so replays and lockstep see exactly what the player pressed.
    /// </summary>
    public sealed class BuilderToolbelt
    {
        public const int Count = 7;
        /// <summary>Degrees past the 45 degree midpoint the aim must swing before the axis changes.</summary>
        public const double HysteresisDeg = 15;
        /// <summary>cos(45 + 15 degrees): keep the current axis while the aim is within 60 degrees of it.</summary>
        public const double KeepCos = 0.5;
        /// <summary>Sim ticks without a sent switch before the predicted selection is checked against the sim's.</summary>
        public const int QuietTicks = 45;
        /// <summary>Ticks a use press may wait for the selection to settle before it is dropped.</summary>
        public const int HoldTicks = 20;
        /// <summary>World units along the axis for the climb aim point (well clear of input-delay drift).</summary>
        public const double AimLead = 3.0;

        public static readonly string[] Words = { "DIG", "PLACE", "LADDER", "BRIDGE", "SHORT WALL", "TALL WALL", "BLOCK" };

        /// <summary>The local human's belt (HumanInputSource sets it); the HUD and the terrain view read it.</summary>
        public static BuilderToolbelt Local;

        /// <summary>The actor this belt drives (HumanInputSource writes LocalSeat.Actor; -1 = the input buffer's actor).</summary>
        public int Actor = -1;

        BuilderTool _target = BuilderTool.Ladder;
        bool _has;
        int _epoch = int.MinValue;
        BuilderTool _pred;
        bool _predKnown;
        int _lastSwitchTick = int.MinValue / 2;
        int _resyncs;
        bool _heldUse;
        int _heldSince;
        int _dx = 1, _dz;
        bool _axisSet;

        /// <summary>The selected tool (the player's choice, shown on the HUD at once).</summary>
        public BuilderTool Selected => _target;
        /// <summary>Goes up by one on every selection change (the terrain view starts its reach outline fade on it).</summary>
        public int Serial { get; private set; }
        /// <summary>The grid axis a ladder or bridge goes along now: (1,0) east, (-1,0) west, (0,1), (0,-1).</summary>
        public int Dx => _dx;
        public int Dz => _dz;
        /// <summary>Switch presses still to send before the sim's selection matches (tests).</summary>
        public bool Settling { get; private set; }

        public static bool IsClimb(BuilderTool t) => t == BuilderTool.Ladder || t == BuilderTool.Bridge;
        public static bool IsWallPiece(BuilderTool t) => t == BuilderTool.ShortWall || t == BuilderTool.TallWall || t == BuilderTool.Block;

        /// <summary>The belt drives actor id: a Builder (alive or not).</summary>
        public static bool Applies(World w, int id) =>
            w != null && w.Actors != null && id >= 0 && id < w.ActorCount && w.Actors[id] != null && w.Actors[id].Cls == ClassId.Builder;

        /// <summary>What the rules allow now: the wall pieces (walls rule), dig / place (the Builder owns terrain), tall walls (tier 1+).</summary>
        public static void Rules(World w, int id, out bool walls, out bool terrain, out bool tall)
        {
            walls = BuilderWalls.On(w);
            terrain = Terraform.OwnerClass(w) == ClassId.Builder;
            tall = walls && BuilderWalls.Tier(w, w.Actors[id].Team) >= 1;
        }

        public static bool Available(BuilderTool t, bool walls, bool terrain, bool tall)
        {
            switch (t)
            {
                case BuilderTool.Dig:
                case BuilderTool.Place: return terrain;
                case BuilderTool.Ladder:
                case BuilderTool.Bridge: return true;
                case BuilderTool.TallWall: return walls && tall;
                default: return walls;
            }
        }

        /// <summary>The sim's own selection for actor id (false: the sim has none, Classic with no walls and no terrain).</summary>
        public static bool SimTool(World w, int id, out BuilderTool t)
        {
            Rules(w, id, out bool walls, out bool terrain, out _);
            if (terrain && (!walls || w.Walls.Tool[id] == 0))
            {
                t = Terraform.ModeOf(w, id) == TerraformMode.Place ? BuilderTool.Place : BuilderTool.Dig;
                return true;
            }
            if (walls)
            {
                t = BuilderWalls.Piece(w, id) == BuildPiece.Block ? BuilderTool.Block : BuilderWalls.TallSelected(w, id) ? BuilderTool.TallWall : BuilderTool.ShortWall;
                return true;
            }
            t = BuilderTool.Ladder;
            return false;
        }

        /// <summary>The sim selection after one switch press (mirrors BuilderWalls.TakeBuilderSwitch and Terraform's flip).</summary>
        public static BuilderTool SimStep(BuilderTool from, bool walls, bool terrain, bool tall)
        {
            if (walls)
            {
                switch (from)
                {
                    case BuilderTool.Dig: return terrain ? BuilderTool.Place : BuilderTool.ShortWall;
                    case BuilderTool.Place: return BuilderTool.ShortWall;
                    case BuilderTool.ShortWall: return tall ? BuilderTool.TallWall : BuilderTool.Block;
                    case BuilderTool.TallWall: return BuilderTool.Block;
                    case BuilderTool.Block: return terrain ? BuilderTool.Dig : BuilderTool.ShortWall;
                    default: return from;
                }
            }
            if (terrain) return from == BuilderTool.Dig ? BuilderTool.Place : BuilderTool.Dig;
            return from;
        }

        /// <summary>Switch presses from one sim selection to another (-1 = unreachable).</summary>
        public static int StepsTo(BuilderTool from, BuilderTool to, bool walls, bool terrain, bool tall)
        {
            BuilderTool s = from;
            for (int n = 0; n <= 6; n++)
            {
                if (s == to) return n;
                s = SimStep(s, walls, terrain, tall);
            }
            return -1;
        }

        /// <summary>The sim selection a tool needs (false = any): itself for dig, place and pieces; PLACE for a climb tool
        /// while the walls rule shows a ghost for pieces and the Builder owns terrain.</summary>
        public static bool SimTarget(BuilderTool t, bool walls, bool terrain, out BuilderTool sim)
        {
            sim = t;
            if (!IsClimb(t)) return true;
            sim = BuilderTool.Place;
            return walls && terrain;
        }

        /// <summary>The next available tool in wheel order (dir +1 forward, -1 back).</summary>
        public static BuilderTool Next(BuilderTool cur, int dir, bool walls, bool terrain, bool tall)
        {
            int step = dir < 0 ? Count - 1 : 1;
            int k = (int)cur;
            for (int n = 0; n < Count; n++)
            {
                k = (k + step) % Count;
                if (Available((BuilderTool)k, walls, terrain, tall)) return (BuilderTool)k;
            }
            return cur;
        }

        /// <summary>The HUD word of a tool (a tall wall at tier 2 is five cells long).</summary>
        public static string Word(BuilderTool t, int tier) => t == BuilderTool.TallWall && tier >= 2 ? "TALL WALL x5" : Words[(int)t];

        /// <summary>Team wood one use of the tool costs (dig and place cost nothing; a block aimed at a hole fills it for CostFill).</summary>
        public static int Cost(World w, BuilderTool t)
        {
            double c;
            switch (t)
            {
                case BuilderTool.Ladder: c = BuiltPieces.Cost(w.Config, BuiltPieces.Ladder); break;
                case BuilderTool.Bridge: c = BuiltPieces.Cost(w.Config, BuiltPieces.Bridge); break;
                case BuilderTool.ShortWall: c = w.Config.Depth.Build.CostShortWall; break;
                case BuilderTool.TallWall: c = w.Config.Depth.Build.CostTallWall; break;
                case BuilderTool.Block: c = w.Config.Depth.Build.CostBlock; break;
                default: c = 0; break;
            }
            return c > 0 ? (int)Math.Ceiling(c - 1e-9) : 0;
        }

        /// <summary>
        /// The grid axis for a look direction (vx, vz) with hysteresis: the current axis (pdx, pdz; (0,0) = none) is kept while
        /// the look is within 60 degrees of it; otherwise the nearest axis (ties to x, like BuiltPieces.AxisToward).
        /// </summary>
        public static void ChooseAxis(double vx, double vz, int pdx, int pdz, out int dx, out int dz)
        {
            double len = Math.Sqrt(vx * vx + vz * vz);
            bool prev = pdx != 0 || pdz != 0;
            if (!(len > 1e-9))
            {
                dx = prev ? pdx : 1;
                dz = prev ? pdz : 0;
                return;
            }
            if (prev && (vx * pdx + vz * pdz) / len >= KeepCos - 1e-12)
            {
                dx = pdx;
                dz = pdz;
                return;
            }
            Nearest(vx, vz, out dx, out dz);
        }

        static void Nearest(double vx, double vz, out int dx, out int dz)
        {
            if (Math.Abs(vx) >= Math.Abs(vz)) { dx = vx >= 0 ? 1 : -1; dz = 0; }
            else { dx = 0; dz = vz >= 0 ? 1 : -1; }
        }

        /// <summary>
        /// The axis for a ladder (kind BuiltPieces.Ladder) or bridge: among the kept axis, the nearest and the second nearest
        /// (each within 60 degrees of the look), the first whose BuiltPieces.Plan of that kind succeeds; none: ChooseAxis.
        /// Reads the grid only (Plan's scratch, which every sim build recomputes first).
        /// </summary>
        public static void ChooseClimbAxis(World w, int id, int kind, double vx, double vz, int pdx, int pdz, out int dx, out int dz)
        {
            ChooseAxis(vx, vz, pdx, pdz, out dx, out dz);
            double len = Math.Sqrt(vx * vx + vz * vz);
            if (!(len > 1e-9) || w == null || w.Map == null) return;
            if (BuiltPieces.Plan(w, id, dx, dz, kind) == 0) return;
            Nearest(vx, vz, out int nx, out int nz);
            // second nearest: the other axis on the side the look leans to
            int sx, sz;
            if (nx != 0) { sx = 0; sz = vz >= 0 ? 1 : -1; }
            else { sx = vx >= 0 ? 1 : -1; sz = 0; }
            if ((nx != dx || nz != dz) && (vx * nx + vz * nz) / len >= KeepCos - 1e-12 && BuiltPieces.Plan(w, id, nx, nz, kind) == 0) { dx = nx; dz = nz; return; }
            if ((sx != dx || sz != dz) && (vx * sx + vz * sz) / len >= KeepCos - 1e-12 && BuiltPieces.Plan(w, id, sx, sz, kind) == 0) { dx = sx; dz = sz; }
        }

        /// <summary>Updates the ladder / bridge axis from the look (the aim point, or the facing without one).</summary>
        public void UpdateAxis(World w, int id, bool hasAim, double aimX, double aimZ)
        {
            if (!Applies(w, id)) return;
            double vx, vz;
            if (hasAim && double.IsFinite(aimX) && double.IsFinite(aimZ) && (aimX != w.X[id] || aimZ != w.Z[id]))
            {
                vx = aimX - w.X[id];
                vz = aimZ - w.Z[id];
            }
            else
            {
                vx = Math.Cos(w.Actors[id].Facing);
                vz = Math.Sin(w.Actors[id].Facing);
            }
            int pdx = _axisSet ? _dx : 0, pdz = _axisSet ? _dz : 0;
            int dx, dz;
            if (IsClimb(_target)) ChooseClimbAxis(w, id, _target == BuilderTool.Ladder ? BuiltPieces.Ladder : BuiltPieces.Bridge, vx, vz, pdx, pdz, out dx, out dz);
            else ChooseAxis(vx, vz, pdx, pdz, out dx, out dz);
            _dx = dx;
            _dz = dz;
            _axisSet = true;
        }

        void Select(BuilderTool t)
        {
            if (t == _target) return;
            _target = t;
            Serial++;
            _resyncs = 0;
        }

        /// <summary>Selects a tool directly (number keys, tests). Unavailable tools are ignored (false).</summary>
        public bool Pick(World w, int id, BuilderTool t)
        {
            if (!Applies(w, id)) return false;
            Rules(w, id, out bool walls, out bool terrain, out bool tall);
            if (!Available(t, walls, terrain, tall)) return false;
            Adopt(w, id);
            Select(t);
            return true;
        }

        void Adopt(World w, int id)
        {
            if (_has && _epoch == w.Epoch) return;
            _has = true;
            _epoch = w.Epoch;
            _target = SimTool(w, id, out BuilderTool s) ? s : BuilderTool.Ladder;
            _predKnown = false;
            _heldUse = false;
            _axisSet = false;
            Serial++;
        }

        /// <summary>Forget everything (a new match or a reset input state).</summary>
        public void Reset()
        {
            _has = false;
            _predKnown = false;
            _heldUse = false;
            _axisSet = false;
            Settling = false;
        }

        /// <summary>
        /// One sim tick of the Builder's input (HumanInputState.Fill): applies a pick (0..6, -1 none) or a switch press
        /// (dir +1 / -1), sends at most one sim switch toward the wanted selection, and turns a use press into the sim's
        /// Terraform or Ability press. Writes o.Terraform, o.Ability, o.TerraformSwitch (and the aim for a climb build).
        /// </summary>
        public void Step(World w, int id, bool switchPress, int switchDir, int pick, bool usePress, bool hasAim, double aimX, double aimZ, InputFrame o)
        {
            o.Terraform = false;
            o.Ability = false;
            o.TerraformSwitch = false;
            Adopt(w, id);
            Actor a = w.Actors[id];
            Rules(w, id, out bool walls, out bool terrain, out bool tall);
            bool simHas = SimTool(w, id, out BuilderTool sim);
            if (!Available(_target, walls, terrain, tall)) Select(simHas ? sim : BuilderTool.Ladder);
            if (a.Alive)
            {
                if (pick >= 0 && pick < Count && Available((BuilderTool)pick, walls, terrain, tall)) Select((BuilderTool)pick);
                else if (switchPress) Select(Next(_target, switchDir, walls, terrain, tall));
            }
            UpdateAxis(w, id, hasAim, aimX, aimZ);

            // Converge the sim's selection (one switch per tick, predicted; checked against the sim after a quiet spell).
            bool wantSim = SimTarget(_target, walls, terrain, out BuilderTool want) && simHas;
            if (_predKnown && w.Tick - _lastSwitchTick >= QuietTicks && _pred == sim) _resyncs = 0;
            if (!_predKnown || (w.Tick - _lastSwitchTick >= QuietTicks && _pred != sim))
            {
                if (_predKnown && wantSim && _pred != sim && ++_resyncs > 2)
                {
                    // the model and the sim disagree twice in a row: show the sim's truth instead of pressing forever
                    _target = sim;
                    Serial++;
                    wantSim = SimTarget(_target, walls, terrain, out want);
                    _resyncs = 0;
                }
                _pred = sim;
                _predKnown = true;
            }
            bool switched = false;
            if (wantSim && a.Alive && _pred != want && StepsTo(_pred, want, walls, terrain, tall) > 0)
            {
                o.TerraformSwitch = true;
                _pred = SimStep(_pred, walls, terrain, tall);
                _lastSwitchTick = w.Tick;
                switched = true;
            }
            Settling = wantSim && _pred != want;

            if (usePress && a.Alive)
            {
                _heldUse = true;
                _heldSince = w.Tick;
            }
            if (!_heldUse) return;
            if (!a.Alive || w.Tick - _heldSince > HoldTicks)
            {
                _heldUse = false;
                return;
            }
            if (switched || Settling) return;   // the press waits for the last switch (never the same frame)
            _heldUse = false;
            if (IsClimb(_target))
            {
                o.Ability = true;
                o.AimX = w.X[id] + _dx * AimLead;
                o.AimZ = w.Z[id] + _dz * AimLead;
            }
            else o.Terraform = true;
        }
    }
}
