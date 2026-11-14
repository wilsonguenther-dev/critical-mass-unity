using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>What a terrain marker or the live target offers (one per Sim rule the class can use).</summary>
    public enum AffAction : byte
    {
        None, Dig, PlaceBlock, Barricade, BreakWall, Build, PutDown, Carry, Repair, Gather, GatherSource, Deliver, Bank, Mine, Tnt, Crater,
    }

    /// <summary>The marker icon (atlas cell) of an action family: pick (mine), sprout (farm), hammer (build), block (place), blast (blow).</summary>
    public enum AffIcon : byte { Pick = 0, Sprout = 1, Hammer = 2, Block = 3, Blast = 4 }

    /// <summary>One usable thing near the local player (a cell, or a point for nodes, pieces and bridge ends).</summary>
    public struct AffMarker
    {
        public AffAction Action;
        /// <summary>Cell (-1 when the marker is a point: node, source, piece, bridge end, shrine, base).</summary>
        public int Cx, Cz;
        public double X, Z;
        /// <summary>The action reaches it from where the player stands now (brighter marker).</summary>
        public bool InReach;
    }

    /// <summary>
    /// The thing one press (or hold) of one action would act on RIGHT NOW, with the Sim's verdict. Filled by
    /// AffordanceScan from the Sim's own target pickers and refusal checks; never written back.
    /// </summary>
    public sealed class AffTarget
    {
        public const int GhostMax = 96;

        public bool Has;
        public AffAction Action;
        /// <summary>Player-map action whose binding the prompt names: "DigPlace", "Ability", "Interact", "Attack".</summary>
        public string Button = "";
        /// <summary>The action needs the button held (gather, repair).</summary>
        public bool Hold;
        /// <summary>Short verb ("DIG", "PLACE BLOCK", "BUILD LADDER").</summary>
        public string Verb = "";
        public double X, Z;
        public int Cx = -1, Cz = -1;
        /// <summary>The Sim would accept the action now.</summary>
        public bool Valid;
        /// <summary>Why not, in two or three words ("" when valid).</summary>
        public string Why = "";
        /// <summary>A count to show in brackets (blocks carried, team bank), -1 = none.</summary>
        public int Count = -1;
        /// <summary>Seconds until a cooldown or rate cap runs out (0 = none): the "wait" reason.</summary>
        public double WaitSeconds;
        /// <summary>Ghost of what will be placed: 0 none, 1 one block cell, 2 barricade cell, 3 piece cells, 4 mine cell, 5 blast circle.</summary>
        public int Ghost;
        public int GhostCount;
        public readonly int[] GhostCells = new int[GhostMax];
        public double GhostRadius;

        public void Clear()
        {
            Has = false;
            Action = AffAction.None;
            Button = "";
            Hold = false;
            Verb = "";
            X = Z = 0;
            Cx = Cz = -1;
            Valid = false;
            Why = "";
            Count = -1;
            WaitSeconds = 0;
            Ghost = 0;
            GhostCount = 0;
            GhostRadius = 0;
        }

        public void AddGhostCell(int index)
        {
            if (GhostCount < GhostMax) GhostCells[GhostCount++] = index;
        }

        /// <summary>A value that changes whenever anything the prompt says changes (the view rebuilds text only then).</summary>
        public int TextKey()
        {
            if (!Has) return 0;
            int h = 17;
            h = h * 31 + (int)Action;
            h = h * 31 + (Valid ? 1 : 0);
            h = h * 31 + (Why != null ? Why.GetHashCode() : 0);
            h = h * 31 + Count;
            h = h * 31 + (int)Math.Ceiling(WaitSeconds);
            h = h * 31 + (Verb != null ? Verb.GetHashCode() : 0);
            return h == 0 ? 1 : h;
        }
    }

    /// <summary>Short reason words (two or three words; one place to reword).</summary>
    public static class AffWords
    {
        public const string TooFar = "too far", HandsFull = "hands full", NoBlocks = "no blocks", Blocked = "blocked",
            CutsPath = "cuts the path", NotGround = "not ground", NotAHole = "not a hole", NoEdge = "no edge", OffMap = "off the map",
            Wait = "wait", BankEmpty = "bank empty", Limit = "piece limit", TooLong = "gap too long", ShrineGround = "shrine ground",
            NothingToCross = "nothing to cross", GettingUp = "getting up", NoWood = "no wood", EnemyGround = "enemy ground",
            Occupied = "occupied", Hill = "hill ground", Water = "no ground", NoRoom = "hands full", Busy = "busy here",
            NotFeeder = "cannot feed";
    }

    /// <summary>
    /// Terrain affordances (feat/terrain-affordances): for the local player's class, the usable cells and objects within a
    /// modest radius and the one target each action would hit right now, decided ONLY by the Sim's own pickers and checks
    /// (Terraform.TargetCell / DigRefusal / PlaceRefusal, Barricades.BarricadeRefusal, BuiltPieces.Plan / BuildRefusal /
    /// PieceInReach, SimResources.GatherNode / NodeTakes, Sources.SourceInReach / SourceTakes, Demolitions.MineCellRefusal,
    /// Abilities.ClampToRange). The route guard of a dig is a flow-field proof that depends on the grid alone, so it is
    /// cached per cell and World.GridVersion and at most RouteBudget new proofs run per Scan (cells not yet proven are
    /// not marked: never a marker the Sim would refuse). Read-only on the Sim; allocation-free after construction.
    /// </summary>
    public sealed class AffordanceScan
    {
        public const int MaxMarkers = 64;
        /// <summary>Marker radius around the player (world units), and while the show-all key is held.</summary>
        public const double Radius = 5.5, RadiusAll = 9.5;
        /// <summary>New route proofs per Scan (each is one or two flow fields over the map).</summary>
        public int RouteBudget = 4;

        public readonly AffMarker[] Markers = new AffMarker[MaxMarkers];
        public int MarkerCount;
        /// <summary>The primary action's target (the class's terrain key) and a second one (Space / interact).</summary>
        public readonly AffTarget Primary = new AffTarget();
        public readonly AffTarget Secondary = new AffTarget();
        /// <summary>The scan stopped adding markers at MaxMarkers.</summary>
        public bool Capped;

        int[] _routeStamp = new int[0];
        byte[] _routeOk = new byte[0];
        int _budgetLeft;
        int _epoch = int.MinValue;
        World _world;

        // ------------------------------------------------------------------ route cache

        void EnsureCache(World w)
        {
            int n = w.Map.Width * w.Map.Height;
            if (_routeStamp.Length != n)
            {
                _routeStamp = new int[n];
                _routeOk = new byte[n];
            }
            if (!ReferenceEquals(_world, w) || _epoch != w.Epoch)
            {
                _world = w;
                _epoch = w.Epoch;
                Array.Clear(_routeStamp, 0, _routeStamp.Length);
            }
        }

        /// <summary>The route verdict of digging cell index i (Terraform.DigKeepsRoutes), cached per GridVersion. force = ignore the budget.</summary>
        public bool RouteKnown(World w, int i, bool force, out bool ok)
        {
            int stamp = w.GridVersion + 1;
            if (stamp == 0) stamp = 1;
            if (_routeStamp[i] == stamp)
            {
                ok = _routeOk[i] == 1;
                return true;
            }
            if (!force && _budgetLeft <= 0)
            {
                ok = false;
                return false;
            }
            _budgetLeft--;
            ok = Terraform.DigKeepsRoutes(w, i);
            _routeStamp[i] = stamp;
            _routeOk[i] = ok ? (byte)1 : (byte)0;
            return true;
        }

        /// <summary>Terraform.DigRefusal(w, cx, cz) exactly, with the route proof from the cache (forced). Out of map: RefusedOutside.</summary>
        public int DigVerdict(World w, int cx, int cz)
        {
            int why = Terraform.DigRefusalBeforeRoute(w, cx, cz);
            if (why != 0) return why;
            RouteKnown(w, cz * w.Map.Width + cx, true, out bool ok);
            return ok ? 0 : Terraform.RefusedRoute;
        }

        /// <summary>Is cell (cx, cz) diggable now by the Sim's rule, proven within the budget? (known = false: not proven yet).</summary>
        bool DigOk(World w, int cx, int cz, out bool known)
        {
            known = true;
            if (Terraform.DigRefusalBeforeRoute(w, cx, cz) != 0) return false;
            known = RouteKnown(w, cz * w.Map.Width + cx, false, out bool ok);
            return known && ok;
        }

        // ------------------------------------------------------------------ the scan

        /// <summary>
        /// One scan for actor id with the aim the next InputFrame will carry (hasAim false = no aim: the Sim uses the facing).
        /// all = the show-all key is held (wider radius). Returns false when nothing applies (dead, not a terrain class).
        /// </summary>
        public bool Scan(World w, int id, bool hasAim, double aimX, double aimZ, bool all)
        {
            MarkerCount = 0;
            Capped = false;
            Primary.Clear();
            Secondary.Clear();
            if (w == null || w.Map == null || id < 0 || id >= w.ActorCount) return false;
            Actor a = w.Actors[id];
            if (!a.Alive) return false;
            EnsureCache(w);
            _budgetLeft = RouteBudget;
            double ax = hasAim ? aimX : double.NaN, az = hasAim ? aimZ : double.NaN;
            double r = all ? RadiusAll : Radius;
            switch (a.Cls)
            {
                case ClassId.Miner: ScanMiner(w, id, a, ax, az, r); return true;
                case ClassId.Builder: ScanBuilder(w, id, a, ax, az, r); return true;
                case ClassId.Farmer: ScanFarmer(w, id, a, r); return true;
                case ClassId.Demolitions: ScanDemolitions(w, id, a, ax, az, r); return true;
                default: return false;
            }
        }

        /// <summary>The terrain classes (the ones this feature marks for).</summary>
        public static bool IsTerrainClass(ClassId c) => c == ClassId.Miner || c == ClassId.Builder || c == ClassId.Farmer || c == ClassId.Demolitions;

        public static AffIcon IconOf(AffAction act)
        {
            switch (act)
            {
                case AffAction.Dig:
                case AffAction.BreakWall: return AffIcon.Pick;
                case AffAction.PlaceBlock:
                case AffAction.Barricade: return AffIcon.Block;
                case AffAction.Build:
                case AffAction.PutDown:
                case AffAction.Carry:
                case AffAction.Repair: return AffIcon.Hammer;
                case AffAction.Gather:
                case AffAction.GatherSource:
                case AffAction.Deliver:
                case AffAction.Bank: return AffIcon.Sprout;
                default: return AffIcon.Blast;
            }
        }

        bool Add(AffAction act, int cx, int cz, double x, double z, bool inReach)
        {
            if (MarkerCount >= MaxMarkers)
            {
                Capped = true;
                return false;
            }
            Markers[MarkerCount++] = new AffMarker { Action = act, Cx = cx, Cz = cz, X = x, Z = z, InReach = inReach };
            return true;
        }

        static double D2(double ax, double az, double bx, double bz) => (ax - bx) * (ax - bx) + (az - bz) * (az - bz);

        static void Box(World w, int id, double r, out int x0, out int x1, out int z0, out int z1)
        {
            MapDef m = w.Map;
            double ox = w.X[id], oz = w.Z[id];
            x0 = Math.Max(0, JsMath.FloorToInt(ox - r));
            x1 = Math.Min(m.Width - 1, JsMath.FloorToInt(ox + r));
            z0 = Math.Max(0, JsMath.FloorToInt(oz - r));
            z1 = Math.Min(m.Height - 1, JsMath.FloorToInt(oz + r));
        }

        static double WaitS(World w, int ticks) => ticks > 0 ? ticks / w.Config.SimHz : 0;

        // ------------------------------------------------------------------ Miner

        void ScanMiner(World w, int id, Actor a, double ax, double az, double r)
        {
            GameConfig c = w.Config;
            MapDef m = w.Map;
            double reach = c.Abilities.MinerTerraform.Reach;
            int carried = w.Terraform.Carried[id], max = Terraform.CarryMax(c);
            bool place = Terraform.ModeOf(w, id) == TerraformMode.Place;
            double ox = w.X[id], oz = w.Z[id];
            Box(w, id, r, out int x0, out int x1, out int z0, out int z1);
            double r2 = r * r;
            // Nearest cells first would need a sort; the ring order below (inside out) keeps the cap fair.
            for (int ring = 0; ring <= (int)Math.Ceiling(r) + 1; ring++)
            {
                int ccx = JsMath.FloorToInt(ox), ccz = JsMath.FloorToInt(oz);
                for (int cz = ccz - ring; cz <= ccz + ring; cz++)
                {
                    for (int cx = ccx - ring; cx <= ccx + ring; cx++)
                    {
                        if (Math.Abs(cx - ccx) != ring && Math.Abs(cz - ccz) != ring) continue;
                        if (cx < x0 || cx > x1 || cz < z0 || cz > z1) continue;
                        if (D2(cx + 0.5, cz + 0.5, ox, oz) > r2) continue;
                        bool inReach = Terraform.CircleHitsCell(ox, oz, reach, cx, cz);
                        byte t = m.Cells[cz * m.Width + cx];
                        if (t == Cell.BreakableIntact || t == Cell.BreakableCracked)
                        {
                            Add(AffAction.BreakWall, cx, cz, cx + 0.5, cz + 0.5, D2(cx + 0.5, cz + 0.5, ox, oz) <= c.Abilities.MinerWallBreak.Reach * c.Abilities.MinerWallBreak.Reach);
                            continue;
                        }
                        if (place)
                        {
                            if (carried > 0 && Terraform.PlaceRefusal(w, cx, cz) == 0) Add(AffAction.PlaceBlock, cx, cz, cx + 0.5, cz + 0.5, inReach);
                        }
                        else if (carried < max && DigOk(w, cx, cz, out _)) Add(AffAction.Dig, cx, cz, cx + 0.5, cz + 0.5, inReach);
                    }
                }
            }

            // Primary: the dig / place key (Terraform.Dig / Place, checks in the Sim's order).
            AffTarget p = Primary;
            p.Has = true;
            p.Button = "DigPlace";
            p.Action = place ? AffAction.PlaceBlock : AffAction.Dig;
            p.Verb = place ? "PLACE BLOCK" : "DIG";
            p.Count = place ? carried : -1;
            bool inMap = Terraform.TargetCell(w, id, ax, az, out int tx, out int tz);
            p.Cx = tx;
            p.Cz = tz;
            p.X = tx + 0.5;
            p.Z = tz + 0.5;
            if (place) p.Ghost = 1;
            if (inMap && place) p.AddGhostCell(tz * m.Width + tx);
            int wait = Terraform.WaitTicks(w, a.Team);
            if (a.StandUp > 0) Refuse(p, AffWords.GettingUp);
            else if (!place && carried >= max) Refuse(p, AffWords.HandsFull);
            else if (place && carried == 0) Refuse(p, AffWords.NoBlocks);
            else if (!inMap) Refuse(p, AffWords.OffMap);
            else if (wait > 0) { Refuse(p, AffWords.Wait); p.WaitSeconds = WaitS(w, wait); }
            else
            {
                int why = place ? Terraform.PlaceRefusal(w, tx, tz) : DigVerdict(w, tx, tz);
                if (why == 0) p.Valid = true;
                else Refuse(p, MinerWhy(w, why, place, tx, tz));
            }

            // Secondary: the barricade on Space (Abilities.CastMinerBarricade's aim rule, Barricades.BarricadeRefusal).
            AffTarget s = Secondary;
            s.Has = true;
            s.Button = "Ability";
            s.Action = AffAction.Barricade;
            s.Verb = "BARRICADE";
            double bx = ax, bz = az;
            if (!(double.IsFinite(ax) && double.IsFinite(az)))
                Abilities.ClampToRange(ox, oz, double.NaN, double.NaN, a.Facing, c.Abilities.MinerBarricade.PlaceRange, out bx, out bz);
            bool cell = Barricades.BarricadeTargetCell(w, id, bx, bz, out int bcx, out int bcz);
            s.Cx = bcx;
            s.Cz = bcz;
            s.X = bcx + 0.5;
            s.Z = bcz + 0.5;
            s.Ghost = place ? 0 : 2;   // one ghost at a time: the block ghost wins in place mode
            if (cell && s.Ghost != 0) s.AddGhostCell(bcz * m.Width + bcx);
            if (a.StandUp > 0) Refuse(s, AffWords.GettingUp);
            else if (a.AbilityCd > 0) { Refuse(s, AffWords.Wait); s.WaitSeconds = a.AbilityCd; }
            else
            {
                int why = Barricades.BarricadeRefusal(w, id, bx, bz);
                if (why == 0) s.Valid = true;
                else Refuse(s, why == Barricades.RefusedHill ? AffWords.Hill : why == Barricades.RefusedRoute ? AffWords.CutsPath
                    : why == Barricades.RefusedRange ? AffWords.Water : AffWords.Blocked);
            }
        }

        static void Refuse(AffTarget t, string why)
        {
            t.Valid = false;
            t.Why = why;
        }

        static string MinerWhy(World w, int code, bool place, int cx, int cz)
        {
            switch (code)
            {
                case Terraform.RefusedOutside: return AffWords.OffMap;
                case Terraform.RefusedRoute: return AffWords.CutsPath;
                case Terraform.RefusedRate: return AffWords.Wait;
                case Terraform.RefusedOccupied:
                    return EnemyGround(w, cx, cz) ? AffWords.EnemyGround : AffWords.Blocked;
                default:
                    if (!place) return AffWords.NotGround;
                    byte t = w.Map.Cells[cz * w.Map.Width + cx];
                    return t == Cell.Void || t == Cell.Water ? AffWords.NoEdge : AffWords.NotAHole;
            }
        }

        /// <summary>Inside a team pad's keep-out (Barricades.PadKeepout): the base ground no terrain tool may change.</summary>
        static bool EnemyGround(World w, int cx, int cz)
        {
            double x = cx + 0.5, z = cz + 0.5;
            for (int t = 0; t < 2; t++)
            {
                PadDef p = w.Map.Pads[t];
                if (D2(x, z, p.X, p.Z) <= Barricades.PadKeepout * Barricades.PadKeepout) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Builder

        static readonly int[] Dxs = { 1, -1, 0, 0 };
        static readonly int[] Dzs = { 0, 0, 1, -1 };

        void ScanBuilder(World w, int id, Actor a, double ax, double az, double r)
        {
            GameConfig c = w.Config;
            MapDef m = w.Map;
            BuilderPiecesConfig bp = c.Abilities.BuilderPieces;
            double ox = w.X[id], oz = w.Z[id];
            int carrySlot = w.Builder.CarryPiece[id];

            // Markers: the cells every build the Sim would accept from here would lay (the four cardinal plans).
            if (carrySlot < 0)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (BuiltPieces.BuildRefusal(w, id, Dxs[k], Dzs[k]) != 0) continue;
                    int n = BuiltPieces.LastPlanCount;
                    for (int q = 0; q < n; q++)
                    {
                        int i = BuiltPieces.LastPlanCell(q);
                        Add(AffAction.Build, i % m.Width, i / m.Width, i % m.Width + 0.5, i / m.Width + 0.5, true);
                    }
                }
            }
            // The team's pieces lying on the map (CARRY on interact) and the broken bridge ends (REPAIR).
            double pr = bp.PickupRange + c.Class(a.Cls).Radius;
            BuiltPiece[] ps = w.Builder.Pieces;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece pc = ps[k];
                if (!pc.Active || pc.Carried || pc.Broken || pc.Team != a.Team) continue;
                if (D2(pc.X, pc.Z, ox, oz) > (r + pc.HalfLen) * (r + pc.HalfLen)) continue;
                Add(AffAction.Carry, -1, -1, pc.X, pc.Z, BuiltPieces.PieceInReach(w, id) == k);
            }
            DepthState d = w.Depth;
            MapEventsConfig E = c.Depth.Events;
            bool broken = d != null && d.BridgeState == MapEvents.BridgeBroken;
            if (broken)
            {
                double rr = E.BridgeRepairRange;
                if (D2(d.BridgeAx, d.BridgeAz, ox, oz) <= (r + rr) * (r + rr)) Add(AffAction.Repair, -1, -1, d.BridgeAx, d.BridgeAz, D2(d.BridgeAx, d.BridgeAz, ox, oz) <= rr * rr);
                if (D2(d.BridgeBx, d.BridgeBz, ox, oz) <= (r + rr) * (r + rr)) Add(AffAction.Repair, -1, -1, d.BridgeBx, d.BridgeBz, D2(d.BridgeBx, d.BridgeBz, ox, oz) <= rr * rr);
            }

            // Primary: Space builds along the axis nearest the aim (BuiltPieces.Build), or E puts the carried piece down.
            BuiltPieces.AxisToward(w, id, ax, az, out int dx, out int dz);
            AffTarget p = Primary;
            p.Has = true;
            p.Ghost = 3;
            if (carrySlot >= 0)
            {
                BuiltPiece pc = ps[carrySlot];
                p.Button = "Interact";
                p.Action = AffAction.PutDown;
                p.Verb = pc.Kind == BuiltPieces.Ladder ? "PUT DOWN LADDER" : "PUT DOWN BRIDGE";
                int why = BuiltPieces.Plan(w, id, dx, dz, pc.Kind);
                Ghost(w, id, p, why == 0, dx, dz);
                if (why == 0 && w.Tick - w.WallChangeTick < Durations.T30(c, c.Abilities.MinerTerraform.MinGapTicks)) why = BuiltPieces.RefusedRate;
                if (why == 0) p.Valid = true;
                else Refuse(p, BuilderWhy(why));
            }
            else
            {
                p.Button = "Ability";
                p.Action = AffAction.Build;
                int why = BuiltPieces.BuildRefusal(w, id, dx, dz);
                int plan = BuiltPieces.Plan(w, id, dx, dz, 0);
                Ghost(w, id, p, plan == 0, dx, dz);
                int kind = plan == 0 ? BuiltPieces.LastPlanKind : 0;
                p.Verb = kind == BuiltPieces.Ladder ? "BUILD LADDER" : kind == BuiltPieces.Bridge ? "BUILD BRIDGE" : "BUILD";
                // first-minutes-1 (walk 1 #6): no count here. The team wood number (BuilderBank.Whole) is shown on the TEAM WOOD
                // line and the class card; "BUILD BRIDGE (2)" read as a price and was a fourth wood count.
                p.Count = -1;
                if (a.StandUp > 0) Refuse(p, AffWords.GettingUp);
                else if (why != 0) Refuse(p, BuilderWhy(why));
                else if (a.AbilityCd > 0) { Refuse(p, AffWords.Wait); p.WaitSeconds = a.AbilityCd; }
                else p.Valid = true;
            }

            // Secondary: interact. Lift the piece in reach (BuiltPieces.CarryInputSystem's rule) or hold to repair the bridge.
            AffTarget s = Secondary;
            if (carrySlot < 0)
            {
                int slot = a.CarryAmount > 0 || a.CarryMeat > 0 || BuiltPieces.InteractBusy(w, ox, oz) ? -1 : BuiltPieces.PieceInReach(w, id);
                if (slot >= 0)
                {
                    BuiltPiece pc = ps[slot];
                    s.Has = true;
                    s.Button = "Interact";
                    s.Action = AffAction.Carry;
                    s.Verb = pc.Kind == BuiltPieces.Ladder ? "CARRY LADDER" : "CARRY BRIDGE";
                    s.X = pc.X;
                    s.Z = pc.Z;
                    s.Valid = a.StandUp <= 0;
                    if (!s.Valid) s.Why = AffWords.GettingUp;
                }
                else if (broken)
                {
                    double rr = E.BridgeRepairRange;
                    bool nearA = D2(d.BridgeAx, d.BridgeAz, ox, oz) <= rr * rr, nearB = D2(d.BridgeBx, d.BridgeBz, ox, oz) <= rr * rr;
                    double ex = nearB && !nearA ? d.BridgeBx : d.BridgeAx, ez = nearB && !nearA ? d.BridgeBz : d.BridgeAz;
                    s.Has = true;
                    s.Button = "Interact";
                    s.Hold = true;
                    s.Action = AffAction.Repair;
                    s.Verb = "REPAIR BRIDGE";
                    s.X = ex;
                    s.Z = ez;
                    s.Count = (int)E.BridgeRepairWood;
                    if (!nearA && !nearB) Refuse(s, AffWords.TooFar);
                    else if (Sources.Stock(w, a.Team, Sources.StockWood) < (int)E.BridgeRepairWood) Refuse(s, AffWords.NoWood);
                    else s.Valid = a.StandUp <= 0;
                }
            }
        }

        static void Ghost(World w, int id, AffTarget p, bool planned, int dx, int dz)
        {
            MapDef m = w.Map;
            if (planned)
            {
                int n = BuiltPieces.LastPlanCount;
                for (int q = 0; q < n; q++) p.AddGhostCell(BuiltPieces.LastPlanCell(q));
            }
            int cx = JsMath.FloorToInt(w.X[id]) + dx, cz = JsMath.FloorToInt(w.Z[id]) + dz;
            if (p.GhostCount > 0)
            {
                int first = p.GhostCells[0];
                cx = first % m.Width;
                cz = first / m.Width;
            }
            p.Cx = cx;
            p.Cz = cz;
            p.X = cx + 0.5;
            p.Z = cz + 0.5;
            if (!planned && MapGrid.InBounds(m, cx, cz)) p.AddGhostCell(cz * m.Width + cx);
        }

        static string BuilderWhy(int code)
        {
            switch (code)
            {
                case BuiltPieces.RefusedBiomass: return AffWords.BankEmpty;
                case BuiltPieces.RefusedLimit: return AffWords.Limit;
                case BuiltPieces.RefusedLength: return AffWords.TooLong;
                case BuiltPieces.RefusedOccupied: return AffWords.ShrineGround;
                case BuiltPieces.RefusedRate: return AffWords.Wait;
                default: return AffWords.NothingToCross;
            }
        }

        // ------------------------------------------------------------------ Farmer

        void ScanFarmer(World w, int id, Actor a, double r)
        {
            GameConfig c = w.Config;
            double ox = w.X[id], oz = w.Z[id];
            double reach = c.Resources.NodeInteractRange + Collide.ActorRadius(w, id);
            bool room = a.CarryAmount < c.Class(a.Cls).CarryCapacity;
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            bool loaded = a.CarryAmount > 0 || a.CarryMeat > 0;
            double rr = (r + reach) * (r + reach);
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode n = nodes[i];
                if (n.Kind != NodeKind.Biomass && !Roles.MayCorrupt(w, a.Cls)) continue;
                if (!room || !SimResources.NodeTakes(w, a, n)) continue;
                double d2 = D2(n.X, n.Z, ox, oz);
                if (d2 > rr) continue;
                Add(AffAction.Gather, -1, -1, n.X, n.Z, d2 <= reach * reach);
            }
            SourceNode[] src = w.Depth != null && Sources.On(w) ? w.Depth.Sources : null;
            if (src != null)
            {
                for (int i = 0; i < src.Length; i++)
                {
                    SourceNode s = src[i];
                    if (!room || !Sources.SourceTakes(a, s)) continue;
                    double d2 = D2(s.X, s.Z, ox, oz);
                    if (d2 > rr) continue;
                    Add(AffAction.GatherSource, -1, -1, s.X, s.Z, d2 <= reach * reach);
                }
            }
            ShrineDef sh = w.Map.Shrine;
            double shr = c.KaijuFeed.ShrineInteractRange;
            bool deliverable = loaded && egg && ((a.CarryAmount > 0 && Roles.MayDeliver(w, a, a.Carry)) || (a.CarryMeat > 0 && Roles.MayFeed(w, a.Cls)));
            if (deliverable && D2(sh.X, sh.Z, ox, oz) <= (r + shr) * (r + shr)) Add(AffAction.Deliver, -1, -1, sh.X, sh.Z, D2(sh.X, sh.Z, ox, oz) <= shr * shr);

            // Primary: interact held, in the Sim's order: deliver at the shrine, gather the node, bank at the base, draw from a source.
            AffTarget p = Primary;
            p.Has = true;
            p.Button = "Interact";
            p.Hold = true;
            p.Count = a.CarryAmount + a.CarryMeat;
            bool atShrine = D2(ox, oz, sh.X, sh.Z) <= shr * shr;
            int ni = SimResources.GatherNode(w, id);
            int si = src != null ? Sources.SourceInReach(w, id) : -1;
            bool bank = Sources.On(w) && Sources.InBase(w, a.Team, ox, oz) &&
                        ((a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Corruption) || a.CarryMeat > 0);
            if (loaded && egg && atShrine)
            {
                p.Action = AffAction.Deliver;
                p.Verb = "FEED THE EGG";
                p.X = sh.X;
                p.Z = sh.Z;
                if (deliverable) p.Valid = true;
                else Refuse(p, AffWords.NotFeeder);
            }
            else if (ni >= 0)
            {
                p.Action = AffAction.Gather;
                p.Verb = "GATHER";
                p.X = nodes[ni].X;
                p.Z = nodes[ni].Z;
                p.Valid = a.StandUp <= 0;
                if (!p.Valid) p.Why = AffWords.GettingUp;
            }
            else if (bank)
            {
                p.Action = AffAction.Bank;
                p.Verb = "BANK IT";
                p.X = w.Map.Pads[(int)a.Team].X;
                p.Z = w.Map.Pads[(int)a.Team].Z;
                p.Valid = true;
            }
            else if (si >= 0)
            {
                p.Action = AffAction.GatherSource;
                p.Verb = src[si].Kind == BiomassType.Water ? "DRAW WATER" : "CHOP WOOD";
                p.X = src[si].X;
                p.Z = src[si].Z;
                p.Valid = a.StandUp <= 0;
                if (!p.Valid) p.Why = AffWords.GettingUp;
            }
            else
            {
                // Nothing in reach: the nearest marker, refused "too far" (or "hands full" when the cargo is full).
                int best = -1;
                double bd = double.PositiveInfinity;
                for (int k = 0; k < MarkerCount; k++)
                {
                    double d2 = D2(Markers[k].X, Markers[k].Z, ox, oz);
                    if (d2 < bd) { bd = d2; best = k; }
                }
                if (best < 0 && !room)
                {
                    p.Action = AffAction.Gather;
                    p.Verb = "GATHER";
                    p.X = ox;
                    p.Z = oz;
                    Refuse(p, AffWords.HandsFull);
                }
                else if (best < 0) p.Has = false;
                else
                {
                    AffMarker mk = Markers[best];
                    p.Action = mk.Action;
                    p.Verb = mk.Action == AffAction.Deliver ? "FEED THE EGG" : mk.Action == AffAction.GatherSource ? "GATHER" : "GATHER";
                    p.X = mk.X;
                    p.Z = mk.Z;
                    Refuse(p, AffWords.TooFar);
                }
            }
        }

        // ------------------------------------------------------------------ Demolitions

        void ScanDemolitions(World w, int id, Actor a, double ax, double az, double r)
        {
            GameConfig c = w.Config;
            MapDef m = w.Map;
            double ox = w.X[id], oz = w.Z[id];
            Box(w, id, r, out int x0, out int x1, out int z0, out int z1);
            double r2 = r * r;
            double blast = c.Abilities.DemolitionsTnt.BlastRadius, carve = c.Abilities.DemolitionsTnt.CarveRadius;
            double throwR = c.Abilities.DemolitionsTnt.ThrowRange;
            // Markers: what a blast can remove near him: breakable walls and enemy barricades.
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    byte t = m.Cells[cz * m.Width + cx];
                    if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) continue;
                    double d2 = D2(cx + 0.5, cz + 0.5, ox, oz);
                    if (d2 > r2) continue;
                    Add(AffAction.Tnt, cx, cz, cx + 0.5, cz + 0.5, d2 <= (throwR + blast) * (throwR + blast));
                }
            }
            Barricade[] bs = w.Barricades;
            for (int k = 0; k < bs.Length; k++)
            {
                Barricade b = bs[k];
                if (!b.Active || b.Team == a.Team) continue;
                double d2 = D2(b.Cx + 0.5, b.Cz + 0.5, ox, oz);
                if (d2 > r2) continue;
                Add(AffAction.Tnt, b.Cx, b.Cz, b.Cx + 0.5, b.Cz + 0.5, d2 <= (throwR + blast) * (throwR + blast));
            }

            // Primary: the TNT throw on the dig / place key (Demolitions.ThrowTnt: aim pulled back to ThrowRange).
            AffTarget p = Primary;
            p.Has = true;
            p.Button = "DigPlace";
            p.Action = AffAction.Tnt;
            p.Verb = "THROW TNT";
            Abilities.ClampToRange(ox, oz, ax, az, a.Facing, throwR, out double tx, out double tz);
            p.X = tx;
            p.Z = tz;
            p.Cx = JsMath.FloorToInt(tx);
            p.Cz = JsMath.FloorToInt(tz);
            p.Ghost = 5;
            p.GhostRadius = blast;
            // The crater forecast: cells within CarveRadius the CM-30 dig rules pass now (the blast re-checks at its tick).
            int cx0 = Math.Max(0, JsMath.FloorToInt(tx - carve)), cx1 = Math.Min(m.Width - 1, JsMath.FloorToInt(tx + carve));
            int cz0 = Math.Max(0, JsMath.FloorToInt(tz - carve)), cz1 = Math.Min(m.Height - 1, JsMath.FloorToInt(tz + carve));
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    byte t = m.Cells[cz * m.Width + cx];
                    if (t != Cell.Floor && t != Cell.Bridge) continue;
                    if (D2(cx + 0.5, cz + 0.5, tx, tz) > carve * carve + 1e-9) continue;
                    if (!DigOk(w, cx, cz, out _)) continue;
                    p.AddGhostCell(cz * m.Width + cx);
                    Add(AffAction.Crater, cx, cz, cx + 0.5, cz + 0.5, true);
                }
            }
            int tw = Demolitions.TntWaitTicks(w, id);
            if (a.StandUp > 0) Refuse(p, AffWords.GettingUp);
            else if (tw > 0) { Refuse(p, AffWords.Wait); p.WaitSeconds = WaitS(w, tw); }
            else p.Valid = true;

            // Secondary: the land mine on Space (Demolitions.PlaceMine: aim pulled back to PlaceRange, MineCellRefusal).
            AffTarget s = Secondary;
            s.Has = true;
            s.Button = "Ability";
            s.Action = AffAction.Mine;
            s.Verb = "LAY MINE";
            Abilities.ClampToRange(ox, oz, ax, az, a.Facing, c.Abilities.DemolitionsMine.PlaceRange, out double mx, out double mz);
            int mcx = JsMath.FloorToInt(mx), mcz = JsMath.FloorToInt(mz);
            s.Cx = mcx;
            s.Cz = mcz;
            s.X = mcx + 0.5;
            s.Z = mcz + 0.5;
            s.Ghost = 4;
            if (MapGrid.InBounds(m, mcx, mcz)) s.AddGhostCell(mcz * m.Width + mcx);
            int mw = Demolitions.MineCellRefusal(w, mcx, mcz);
            if (a.StandUp > 0) Refuse(s, AffWords.GettingUp);
            else if (mw != 0) Refuse(s, mw == Demolitions.MineRefusedRange ? AffWords.Water : EnemyGround(w, mcx, mcz) ? AffWords.EnemyGround : AffWords.Occupied);
            else if (a.AbilityCd > 0) { Refuse(s, AffWords.Wait); s.WaitSeconds = a.AbilityCd; }
            else s.Valid = true;
        }
    }
}
