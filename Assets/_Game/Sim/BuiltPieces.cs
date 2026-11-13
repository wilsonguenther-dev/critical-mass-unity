using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>DG-24: one ladder or rope bridge slot (fixed pool BuilderState.Pieces). Inactive slots have Active = false.</summary>
    public sealed class BuiltPiece
    {
        /// <summary>The slot is in use: on the map, or carried.</summary>
        public bool Active;
        /// <summary>Lifted by Carrier (its cells are back to what they were); false while it lies on the map.</summary>
        public bool Carried;
        public int Carrier;
        /// <summary>BuiltPieces.Ladder or BuiltPieces.Bridge.</summary>
        public int Kind;
        /// <summary>The Builder that built it (its limit counts the piece wherever it is).</summary>
        public int Owner;
        public TeamId Team;
        /// <summary>The cardinal step along the span (cells): (1,0) east, (-1,0) west, (0,1) south, (0,-1) north.</summary>
        public int Dx;
        public int Dz;
        /// <summary>Cells along the centre lane (the length), 1..max for the kind.</summary>
        public int Length;
        /// <summary>Cell indices the piece turned into Bridge (Cells[0..Count-1]) and the types they had (Prev).</summary>
        public int Count;
        public int[] Cells;
        public byte[] Prev;
        /// <summary>The span's centre (world units) and half its length (units, the event radius).</summary>
        public double X;
        public double Z;
        public double HalfLen;
        public int PlacedTick;
        /// <summary>World.Tick the piece crumbles (carried time counts).</summary>
        public int ExpireTick;
        /// <summary>The PieceCrumbling warning went out.</summary>
        public bool Warned;
        /// <summary>A cell was dug or blown out; the rest is taken away as soon as it is safe.</summary>
        public bool Broken;
    }

    /// <summary>DG-24 per-match counters (the soak reports them).</summary>
    public sealed class BuilderStats
    {
        public int Ladders;
        public int Bridges;
        public int Refused;
        public int PickedUp;
        public int PutDown;
        public int Expired;
        public int Dropped;
        public int Destroyed;
        /// <summary>Removals put off for a tick because an actor stood on the piece, the world gap had not run out, or a barricade sat on it.</summary>
        public int Deferred;
        /// <summary>Removals put off because they would have cut a pad from the hill (or newly from the shrine).</summary>
        public int RouteDeferred;
        public double BiomassSpent;

        public void Clear()
        {
            Ladders = 0;
            Bridges = 0;
            Refused = 0;
            PickedUp = 0;
            PutDown = 0;
            Expired = 0;
            Dropped = 0;
            Destroyed = 0;
            Deferred = 0;
            RouteDeferred = 0;
            BiomassSpent = 0;
        }
    }

    /// <summary>DG-24 Builder state: the piece pool, each team's spent biomass and the carry edge, allocated once per World.</summary>
    public sealed class BuilderState
    {
        public BuiltPiece[] Pieces;
        /// <summary>Biomass each team has spent on pieces this match, indexed by (int)TeamId.</summary>
        public double[] Spent;
        /// <summary>Per actor id: the slot of the piece it carries, -1 = none.</summary>
        public int[] CarryPiece;
        /// <summary>Per actor id: InputFrame.Interact of the previous step (rising edge).</summary>
        public byte[] PrevInteract;
        public BuilderStats Stats;

        public static BuilderState Create(GameConfig cfg)
        {
            int cap = World.ActorCapacity(cfg);
            BuilderPiecesConfig b = cfg.Abilities.BuilderPieces;
            int perPiece = Math.Max(1, (int)b.WidthCells) * Math.Max(1, (int)Math.Max(b.LadderMaxCells, b.BridgeMaxCells));
            var s = new BuilderState
            {
                Pieces = new BuiltPiece[cap * Math.Max(1, (int)b.MaxActivePerBuilder)],
                Spent = new double[2],
                CarryPiece = new int[cap],
                PrevInteract = new byte[cap],
                Stats = new BuilderStats(),
            };
            for (int i = 0; i < s.Pieces.Length; i++) s.Pieces[i] = new BuiltPiece { Owner = -1, Carrier = -1, Cells = new int[perPiece], Prev = new byte[perPiece] };
            Array.Fill(s.CarryPiece, -1);
            return s;
        }

        /// <summary>Back to the start of a match, in place (World.InitWorld; the map is repainted by the caller).</summary>
        public void Reset()
        {
            for (int i = 0; i < Pieces.Length; i++)
            {
                BuiltPiece p = Pieces[i];
                p.Active = false;
                p.Carried = false;
                p.Carrier = -1;
                p.Owner = -1;
                p.Count = 0;
                p.Warned = false;
                p.Broken = false;
            }
            Spent[0] = 0;
            Spent[1] = 0;
            Array.Fill(CarryPiece, -1);
            Array.Clear(PrevInteract, 0, PrevInteract.Length);
            Stats.Clear();
        }
    }

    /// <summary>
    /// DG-24 on-screen names of the Builder and its pieces, in ONE place for the owner to rename (the HUD and class cards
    /// read these). Data only: the sim never shows text.
    /// </summary>
    public static class BuilderNames
    {
        public const string ClassName = "BUILDER";
        /// <summary>The class ability (Space / the ability button): build a ladder or a bridge, whichever the spot needs.</summary>
        public const string Build = "BUILD";
        public const string Ladder = "LADDER";
        public const string Bridge = "ROPE BRIDGE";
        /// <summary>The Interact binding (E, the controller's interact button) next to a piece: any class lifts it, and puts it down again.</summary>
        public const string Carry = "CARRY";
    }

    /// <summary>
    /// DG-24 Builder ladders and rope bridges (U-060; the oracle has none: its CM-39 Builder only upgrades classes). Travis
    /// 2026-11-06: "Build temporary ladders/bridges with biomass / Ladders/bridges movable by all classes"; Nov 4 call: "He can
    /// build ladders to get from different elevation levels. He can build rope bridges".
    /// <para>BUILD (Space, a living standing Builder, class AbilityCooldown 12 s): it looks along the cardinal axis nearest
    /// its aim (no aim: its facing). Within ReachCells ahead of its own (walkable) cell the first blocked cell decides the
    /// piece: a Wall (cliff, rock) run makes a LADDER (up to LadderMaxCells, cost LadderCost), a Void or Water run makes a
    /// ROPE BRIDGE (up to BridgeMaxCells, cost BridgeCost); the cell right after the run must be walkable ground (the landing).
    /// The piece is WidthCells lanes wide, centred on that line: every cell of the kind's type in the rectangle becomes
    /// Cell.Bridge through MapGrid.SetCell (walkable; WallVersion bumps so bot fields rebuild). Refused: no run, a run too long,
    /// a breakable or barricade in the way, a cell in the shrine keep-out or on the map edge, the Builder's MaxActivePerBuilder
    /// pieces already out, the team's bank short, or the CM-30 world gap not run out. A refusal pushes an Ability event with a
    /// Refused* code and locks Space for RefuseLockSeconds; nothing else changes.</para>
    /// <para>BANK: StartBiomass + the team's delivered biomass (MatchState.Delivered[team].Biomass) - what it spent. Spending
    /// never touches allegiance, instability or the hatch.</para>
    /// <para>TEMPORARY: a piece lasts LifetimeTicks from the build (carried time included). PieceCrumbling warns WarnTicks
    /// before. At the end, and as soon as one of its cells is no longer Bridge (dug by a miner or cratered by TNT: PieceDestroyed),
    /// its cells still Bridge go back to the types they had, but only when no living actor (or the hatched Kaiju) overlaps them,
    /// no barricade sits on them, the CM-30 world gap has run out and the ROUTE GUARD passes (Terraform.CellsKeepRoutes: a
    /// removal never cuts a pad from the hill); otherwise it waits and tries again next tick (PieceExpired when it goes).</para>
    /// <para>CARRY (any class, InputFrame.Interact rising edge, empty-handed, not inside a node's or the shrine's interact
    /// range, where E gathers and delivers): within PickupRange of a cell of a piece of its own team it lifts it (the same
    /// safety checks as a removal; PiecePickedUp). With a piece in hand, the next press puts it down on the spot in front by
    /// the same rule as a build of that kind (no cost, no cooldown; PiecePlaced with the carrier as A). A carrier that dies
    /// drops it: it is gone (PieceExpired EndDropped).</para>
    /// Every loop is in index order and all timing is World.Tick: deterministic. Allocation-free (fixed pool World.Builder).
    /// </summary>
    public static class BuiltPieces
    {
        /// <summary>Piece kinds (BuiltPiece.Kind).</summary>
        public const int Ladder = 1, Bridge = 2;
        /// <summary>Ability event values of a successful build (A = the Builder, (X, Z) = the span's centre).</summary>
        public const int BuiltLadderCode = 1, BuiltBridgeCode = 2;
        /// <summary>Refusals (Ability event values; for a carried piece B is its slot, else -1).</summary>
        public const int RefusedTarget = -1, RefusedLength = -2, RefusedOccupied = -3, RefusedRoute = -4, RefusedRate = -5,
            RefusedLimit = -6, RefusedBiomass = -7, RefusedClass = -8;
        /// <summary>PieceExpired reasons (SimEvent.Value).</summary>
        public const int EndExpired = 0, EndDropped = 1, EndBroken = 2;

        // ------------------------------------------------------------------------------------------------ scratch

        private static int[] PlanCells = new int[16];
        private static byte[] PlanPrev = new byte[16];
        private static int PlanCount;
        private static int PlanKind;
        private static int PlanLength;
        private static int PlanStart;
        private static double PlanX;
        private static double PlanZ;
        private static int[] RouteIdx = new int[16];
        private static byte[] RouteNext = new byte[16];

        private static void EnsurePlan(int n)
        {
            if (PlanCells.Length >= n) return;
            PlanCells = new int[n];
            PlanPrev = new byte[n];
            RouteIdx = new int[n];
            RouteNext = new byte[n];
        }

        private static bool KindType(int kind, byte t)
        {
            return kind == Ladder ? t == Cell.Wall : t == Cell.Void || t == Cell.Water;
        }

        /// <summary>Spendable biomass of team (StartBiomass + delivered biomass - spent).</summary>
        public static double Bank(World w, TeamId team)
        {
            return w.Config.Abilities.BuilderPieces.StartBiomass + w.Match.Delivered[(int)team].Biomass - w.Builder.Spent[(int)team];
        }

        /// <summary>Biomass a piece of kind costs.</summary>
        public static double Cost(GameConfig c, int kind)
        {
            return kind == Ladder ? c.Abilities.BuilderPieces.LadderCost : c.Abilities.BuilderPieces.BridgeCost;
        }

        /// <summary>Pieces Builder id owns now (on the map or carried by anyone).</summary>
        public static int OwnedCount(World w, int id)
        {
            int n = 0;
            BuiltPiece[] ps = w.Builder.Pieces;
            for (int k = 0; k < ps.Length; k++) if (ps[k].Active && ps[k].Owner == id) n++;
            return n;
        }

        /// <summary>The cardinal step nearest the direction from actor id to (aimX, aimZ) (no aim: its facing). Ties go to x.</summary>
        public static void AxisToward(World w, int id, double aimX, double aimZ, out int dx, out int dz)
        {
            double vx;
            double vz;
            if (double.IsFinite(aimX) && double.IsFinite(aimZ) && (aimX != w.X[id] || aimZ != w.Z[id]))
            {
                vx = aimX - w.X[id];
                vz = aimZ - w.Z[id];
            }
            else
            {
                vx = JsMath.Cos(w.Actors[id].Facing);
                vz = JsMath.Sin(w.Actors[id].Facing);
            }
            if (Math.Abs(vx) >= Math.Abs(vz))
            {
                dx = vx >= 0 ? 1 : -1;
                dz = 0;
            }
            else
            {
                dx = 0;
                dz = vz >= 0 ? 1 : -1;
            }
        }

        private static bool OnEdge(MapDef map, int cx, int cz)
        {
            return cx <= 0 || cz <= 0 || cx >= map.Width - 1 || cz >= map.Height - 1;
        }

        /// <summary>
        /// The piece actor id would make looking along (dx, dz): 0 and the plan in the static scratch (kind, cells, centre),
        /// else a Refused* code. kindWanted 0 = whatever the spot needs; Ladder / Bridge (a carried piece) refuses the other.
        /// Reads the grid only (no cost, limit, gap or cooldown).
        /// </summary>
        public static int Plan(World w, int id, int dx, int dz, int kindWanted)
        {
            MapDef map = w.Map;
            BuilderPiecesConfig cfg = w.Config.Abilities.BuilderPieces;
            int ax = JsMath.FloorToInt(w.X[id]);
            int az = JsMath.FloorToInt(w.Z[id]);
            PlanCount = 0;
            if (!MapGrid.IsWalkable(map, ax, az)) return RefusedTarget;
            int reach = Math.Max(1, (int)cfg.ReachCells);
            int s0 = -1;
            for (int s = 1; s <= reach; s++)
            {
                if (!MapGrid.InBounds(map, ax + dx * s, az + dz * s)) return RefusedTarget;
                if (MapGrid.IsWalkable(map, ax + dx * s, az + dz * s)) continue;
                s0 = s;
                break;
            }
            if (s0 < 0) return RefusedTarget;
            byte t0 = map.Cells[(az + dz * s0) * map.Width + ax + dx * s0];
            int kind = t0 == Cell.Wall ? Ladder : t0 == Cell.Void || t0 == Cell.Water ? Bridge : 0;
            if (kind == 0 || (kindWanted != 0 && kind != kindWanted)) return RefusedTarget;
            int max = Math.Max(1, (int)(kind == Ladder ? cfg.LadderMaxCells : cfg.BridgeMaxCells));
            int len = 0;
            while (true)
            {
                int cx = ax + dx * (s0 + len);
                int cz = az + dz * (s0 + len);
                if (!MapGrid.InBounds(map, cx, cz)) return RefusedLength;
                byte t = map.Cells[cz * map.Width + cx];
                if (MapGrid.IsWalkableType(t)) break;
                if (!KindType(kind, t) || OnEdge(map, cx, cz)) return RefusedTarget;
                len++;
                if (len > max) return RefusedLength;
            }
            int half = Math.Max(0, ((int)cfg.WidthCells - 1) / 2);
            EnsurePlan((2 * half + 1) * len);
            int px = -dz;
            int pz = dx;
            ShrineDef sh = map.Shrine;
            double sk2 = Barricades.ShrineKeepout * Barricades.ShrineKeepout;
            for (int o = -half; o <= half; o++)
            {
                for (int s = s0; s < s0 + len; s++)
                {
                    int cx = ax + dx * s + px * o;
                    int cz = az + dz * s + pz * o;
                    if (!MapGrid.InBounds(map, cx, cz) || OnEdge(map, cx, cz)) continue;
                    int i = cz * map.Width + cx;
                    byte t = map.Cells[i];
                    if (!KindType(kind, t)) continue;
                    double sx = cx + 0.5 - sh.X;
                    double sz = cz + 0.5 - sh.Z;
                    if (sx * sx + sz * sz <= sk2) return RefusedOccupied;
                    PlanCells[PlanCount] = i;
                    PlanPrev[PlanCount] = t;
                    PlanCount++;
                }
            }
            PlanKind = kind;
            PlanLength = len;
            PlanStart = s0;
            PlanX = ax + 0.5 + dx * (s0 + (len - 1) * 0.5);
            PlanZ = az + 0.5 + dz * (s0 + (len - 1) * 0.5);
            return 0;
        }

        /// <summary>The landing cell centre of the last successful Plan (the first walkable cell after the run) and its distance in cells from the actor.</summary>
        public static void LastPlanLanding(World w, int id, int dx, int dz, out double x, out double z, out int cells)
        {
            int ax = JsMath.FloorToInt(w.X[id]);
            int az = JsMath.FloorToInt(w.Z[id]);
            cells = PlanStart + PlanLength;
            x = ax + 0.5 + dx * cells;
            z = az + 0.5 + dz * cells;
        }

        /// <summary>Kind of the last successful Plan.</summary>
        public static int LastPlanKind => PlanKind;

        private static bool GapOk(World w)
        {
            return w.Tick - w.WallChangeTick >= Durations.T30(w.Config, w.Config.Abilities.MinerTerraform.MinGapTicks);
        }

        /// <summary>0 when Builder id may build looking along (dx, dz) right now (cooldown aside), else a Refused* code. Leaves the plan in the scratch.</summary>
        public static int BuildRefusal(World w, int id, int dx, int dz)
        {
            Actor a = w.Actors[id];
            if (a == null || !a.Alive || a.Cls != ClassId.Builder) return RefusedClass;
            if (OwnedCount(w, id) >= w.Config.Abilities.BuilderPieces.MaxActivePerBuilder) return RefusedLimit;
            int why = Plan(w, id, dx, dz, 0);
            if (why != 0) return why;
            if (!(Bank(w, a.Team) >= Cost(w.Config, PlanKind))) return RefusedBiomass;
            if (!GapOk(w)) return RefusedRate;
            return 0;
        }

        /// <summary>Would Builder id's build toward (aimX, aimZ) be accepted (cooldown included)? Bots ask first.</summary>
        public static bool CanBuild(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            if (a == null || a.AbilityCd > 0) return false;
            AxisToward(w, id, aimX, aimZ, out int dx, out int dz);
            return BuildRefusal(w, id, dx, dz) == 0;
        }

        private static int FreeSlot(World w)
        {
            BuiltPiece[] ps = w.Builder.Pieces;
            for (int k = 0; k < ps.Length; k++) if (!ps[k].Active) return k;
            return -1;
        }

        /// <summary>Write the scratch plan's cells to the map as Bridge into piece p and push PiecePlaced (A = actor).</summary>
        private static void Lay(World w, int slot, BuiltPiece p, int actor, int dx, int dz)
        {
            p.Kind = PlanKind;
            p.Dx = dx;
            p.Dz = dz;
            p.Length = PlanLength;
            p.Count = PlanCount;
            for (int k = 0; k < PlanCount; k++)
            {
                p.Cells[k] = PlanCells[k];
                p.Prev[k] = PlanPrev[k];
            }
            int width = w.Map.Width;
            for (int k = 0; k < PlanCount; k++) MapGrid.SetCell(w, PlanCells[k] % width, PlanCells[k] / width, Cell.Bridge);
            p.X = PlanX;
            p.Z = PlanZ;
            p.HalfLen = PlanLength * 0.5;
            p.Carried = false;
            p.Carrier = -1;
            p.Broken = false;
            World.PushEvent(w, SimEventType.PiecePlaced, actor, slot, p.X, p.Z, p.ExpireTick, null, DamageCause.None, p.HalfLen);
        }

        /// <summary>Builder id builds toward (aimX, aimZ). Returns BuiltLadderCode / BuiltBridgeCode or a Refused* code (Space locked briefly).</summary>
        public static int Build(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            AxisToward(w, id, aimX, aimZ, out int dx, out int dz);
            int why = BuildRefusal(w, id, dx, dz);
            int slot = why == 0 ? FreeSlot(w) : -1;
            if (why == 0 && slot < 0) why = RefusedLimit; // unreachable: the pool is ActorCapacity x MaxActivePerBuilder
            if (why != 0)
            {
                w.Builder.Stats.Refused++;
                if (a != null && a.Alive) a.AbilityCd = w.Config.Abilities.BuilderPieces.RefuseLockSeconds;
                World.PushEvent(w, SimEventType.Ability, id, -1, w.X[id] + dx, w.Z[id] + dz, why);
                return why;
            }
            BuilderState s = w.Builder;
            BuiltPiece p = s.Pieces[slot];
            double cost = Cost(w.Config, PlanKind);
            s.Spent[(int)a.Team] += cost;
            s.Stats.BiomassSpent += cost;
            if (PlanKind == Ladder) s.Stats.Ladders++;
            else s.Stats.Bridges++;
            p.Active = true;
            p.Owner = id;
            p.Team = a.Team;
            p.PlacedTick = w.Tick;
            p.ExpireTick = w.Tick + Durations.T30(w.Config, w.Config.Abilities.BuilderPieces.LifetimeTicks);
            p.Warned = false;
            a.AbilityCd = w.Config.Class(ClassId.Builder).AbilityCooldown;
            a.AnimAction = 0;
            int code = PlanKind == Ladder ? BuiltLadderCode : BuiltBridgeCode;
            Lay(w, slot, p, id, dx, dz);
            World.PushEvent(w, SimEventType.Ability, id, -1, p.X, p.Z, code);
            return code;
        }

        /// <summary>The Space press of a Builder (Abilities.AbilitiesSystem): build toward its aim, or straight ahead without one.</summary>
        public static void CastBuild(World w, int id)
        {
            bool hasAim = w.HasAim[id] == 1;
            Build(w, id, hasAim ? w.Inputs[id].AimX : double.NaN, hasAim ? w.Inputs[id].AimZ : double.NaN);
        }

        // ------------------------------------------------------------------------------------------ taking away

        /// <summary>
        /// 0 when piece p's cells still Bridge may go back to their old types right now, else RefusedOccupied (an actor, the
        /// Kaiju or a barricade on it), RefusedRate (world gap) or RefusedRoute (it would cut a route).
        /// </summary>
        private static int LiftRefusal(World w, BuiltPiece p)
        {
            MapDef map = w.Map;
            EnsurePlan(p.Count);
            int n = 0;
            for (int k = 0; k < p.Count; k++)
            {
                int i = p.Cells[k];
                byte t = map.Cells[i];
                if (t == Cell.Barricade) return RefusedOccupied;
                if (t != Cell.Bridge) continue;
                if (Terraform.CellOccupied(w, i % map.Width, i / map.Width)) return RefusedOccupied;
                RouteIdx[n] = i;
                RouteNext[n] = p.Prev[k];
                n++;
            }
            if (n == 0) return 0;
            if (!GapOk(w)) return RefusedRate;
            return Terraform.CellsKeepRoutes(w, RouteIdx, RouteNext, n) ? 0 : RefusedRoute;
        }

        /// <summary>Put piece p's cells still Bridge back to their old types (LiftRefusal passed).</summary>
        private static void Unlay(World w, BuiltPiece p)
        {
            MapDef map = w.Map;
            for (int k = 0; k < p.Count; k++)
            {
                int i = p.Cells[k];
                if (map.Cells[i] == Cell.Bridge) MapGrid.SetCell(w, i % map.Width, i / map.Width, p.Prev[k]);
            }
        }

        private static void End(World w, int slot, BuiltPiece p, int reason, double x, double z)
        {
            BuilderState s = w.Builder;
            if (p.Carried && p.Carrier >= 0 && p.Carrier < s.CarryPiece.Length && s.CarryPiece[p.Carrier] == slot) s.CarryPiece[p.Carrier] = -1;
            p.Active = false;
            p.Carried = false;
            p.Carrier = -1;
            if (reason == EndDropped) s.Stats.Dropped++;
            else if (reason == EndExpired) s.Stats.Expired++;
            World.PushEvent(w, SimEventType.PieceExpired, p.Owner, slot, x, z, reason, null, DamageCause.None, p.HalfLen);
        }

        /// <summary>
        /// Per step (after Combat and Topple, so this step's digs and craters are seen): carried pieces whose carrier died are
        /// dropped, and run out like the rest; a piece on the map with a cell that is no longer Bridge breaks (PieceDestroyed);
        /// the crumble warning; and the taking away of broken or expired pieces, retried every tick until it is safe.
        /// </summary>
        public static void PiecesSystem(World w)
        {
            BuilderState s = w.Builder;
            BuilderPiecesConfig cfg = w.Config.Abilities.BuilderPieces;
            MapDef map = w.Map;
            BuiltPiece[] ps = s.Pieces;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece p = ps[k];
                if (!p.Active) continue;
                if (p.Carried)
                {
                    int c = p.Carrier;
                    bool lost = c < 0 || c >= w.ActorCount || !w.Actors[c].Alive;
                    if (lost) End(w, k, p, EndDropped, c >= 0 && c < w.ActorCount ? w.X[c] : p.X, c >= 0 && c < w.ActorCount ? w.Z[c] : p.Z);
                    else if (w.Tick >= p.ExpireTick) End(w, k, p, EndExpired, w.X[c], w.Z[c]);
                    continue;
                }
                if (!p.Broken)
                {
                    for (int q = 0; q < p.Count; q++)
                    {
                        byte t = map.Cells[p.Cells[q]];
                        if (t == Cell.Bridge || t == Cell.Barricade) continue;
                        p.Broken = true;
                        s.Stats.Destroyed++;
                        int i = p.Cells[q];
                        World.PushEvent(w, SimEventType.PieceDestroyed, p.Owner, k, i % map.Width + 0.5, i / map.Width + 0.5, p.Count, null, DamageCause.None, p.HalfLen);
                        break;
                    }
                }
                if (!p.Broken && !p.Warned && w.Tick >= p.ExpireTick - Durations.T30(w.Config, cfg.WarnTicks))
                {
                    p.Warned = true;
                    World.PushEvent(w, SimEventType.PieceCrumbling, p.Owner, k, p.X, p.Z, Math.Max(0, p.ExpireTick - w.Tick), null, DamageCause.None, p.HalfLen);
                }
                if (!p.Broken && w.Tick < p.ExpireTick) continue;
                int why = LiftRefusal(w, p);
                if (why != 0)
                {
                    if (why == RefusedRoute) s.Stats.RouteDeferred++;
                    else s.Stats.Deferred++;
                    continue;
                }
                Unlay(w, p);
                End(w, k, p, p.Broken ? EndBroken : EndExpired, p.X, p.Z);
            }
        }

        // ------------------------------------------------------------------------------------------------- carry

        /// <summary>True when (x, z) is where Interact means gather or deliver: a node with charges within its interact range, or the shrine's.</summary>
        private static bool InteractBusy(World w, double x, double z)
        {
            double nr = w.Config.Resources.NodeInteractRange;
            for (int k = 0; k < w.Nodes.Length; k++)
            {
                ResourceNode nd = w.Nodes[k];
                if (nd.Charges <= 0) continue;
                double dx = x - nd.X;
                double dz = z - nd.Z;
                if (dx * dx + dz * dz <= nr * nr) return true;
            }
            double sr = w.Config.KaijuFeed.ShrineInteractRange;
            double sx = x - w.Map.Shrine.X;
            double sz = z - w.Map.Shrine.Z;
            return sx * sx + sz * sz <= sr * sr;
        }

        /// <summary>The slot of the nearest piece of actor id's team lying on the map within PickupRange (+ its radius) of one of its cells, else -1.</summary>
        public static int PieceInReach(World w, int id)
        {
            MapDef map = w.Map;
            Actor a = w.Actors[id];
            double r = w.Config.Abilities.BuilderPieces.PickupRange + w.Config.Class(a.Cls).Radius;
            double x = w.X[id];
            double z = w.Z[id];
            int best = -1;
            double bestD = double.PositiveInfinity;
            BuiltPiece[] ps = w.Builder.Pieces;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece p = ps[k];
                if (!p.Active || p.Carried || p.Broken || p.Team != a.Team) continue;
                for (int q = 0; q < p.Count; q++)
                {
                    int i = p.Cells[q];
                    int cx = i % map.Width;
                    int cz = i / map.Width;
                    double nx = x < cx ? cx : x > cx + 1 ? cx + 1 : x;
                    double nz = z < cz ? cz : z > cz + 1 ? cz + 1 : z;
                    double d = (x - nx) * (x - nx) + (z - nz) * (z - nz);
                    if (d <= r * r && d < bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
            }
            return best;
        }

        /// <summary>Actor id lifts piece slot. Returns 0 or a Refused* code (Ability event, B = slot).</summary>
        public static int PickUp(World w, int id, int slot)
        {
            BuilderState s = w.Builder;
            BuiltPiece p = s.Pieces[slot];
            int why = LiftRefusal(w, p);
            if (why != 0)
            {
                World.PushEvent(w, SimEventType.Ability, id, slot, p.X, p.Z, why);
                return why;
            }
            Unlay(w, p);
            p.Carried = true;
            p.Carrier = id;
            p.Warned = false;
            s.CarryPiece[id] = slot;
            s.Stats.PickedUp++;
            World.PushEvent(w, SimEventType.PiecePickedUp, id, slot, p.X, p.Z, Math.Max(0, p.ExpireTick - w.Tick), null, DamageCause.None, p.HalfLen);
            return 0;
        }

        /// <summary>Actor id puts its carried piece down toward (aimX, aimZ) by the build rule of its kind. Returns 0 or a Refused* code (Ability event, B = slot).</summary>
        public static int PutDown(World w, int id, double aimX, double aimZ)
        {
            BuilderState s = w.Builder;
            int slot = s.CarryPiece[id];
            if (slot < 0) return RefusedTarget;
            BuiltPiece p = s.Pieces[slot];
            AxisToward(w, id, aimX, aimZ, out int dx, out int dz);
            int why = Plan(w, id, dx, dz, p.Kind);
            if (why == 0 && !GapOk(w)) why = RefusedRate;
            if (why != 0)
            {
                World.PushEvent(w, SimEventType.Ability, id, slot, w.X[id] + dx, w.Z[id] + dz, why);
                return why;
            }
            s.CarryPiece[id] = -1;
            s.Stats.PutDown++;
            p.Warned = false;
            Lay(w, slot, p, id, dx, dz);
            return 0;
        }

        /// <summary>
        /// Input half (end of Abilities.AbilitiesSystem, before movement): every actor's Interact edge; a living, standing,
        /// empty-handed actor away from nodes and the shrine lifts the nearest piece of its team in reach, or puts down the one
        /// it carries. A carrier is never inside a node's or the shrine's range check: putting down is always allowed.
        /// </summary>
        public static void CarryInputSystem(World w)
        {
            BuilderState s = w.Builder;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                byte pressed = w.Inputs[i].Interact ? (byte)1 : (byte)0;
                bool rising = pressed == 1 && s.PrevInteract[i] == 0;
                s.PrevInteract[i] = pressed;
                if (!rising || !a.Alive || a.StandUp > 0) continue;
                bool hasAim = w.HasAim[i] == 1;
                double ax = hasAim ? w.Inputs[i].AimX : double.NaN;
                double az = hasAim ? w.Inputs[i].AimZ : double.NaN;
                if (s.CarryPiece[i] >= 0)
                {
                    PutDown(w, i, ax, az);
                    continue;
                }
                if (a.CarryAmount > 0 || a.CarryMeat > 0 || InteractBusy(w, w.X[i], w.Z[i])) continue;
                int slot = PieceInReach(w, i);
                if (slot >= 0) PickUp(w, i, slot);
            }
        }
    }
}
