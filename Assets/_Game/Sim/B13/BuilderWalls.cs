// CM b13 item B (Builder walls and blocks, Jeisil: "similar to Fortnite and easy"; owner: terrain belongs to the Builder and
// the Demolitionist). Every number lives in Sim/Config/BuildConfig.cs. Allocation-free after World creation.
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM b13 item B: the Builder walls state on the World (World.Walls). Per actor: the selected piece and tall flag, the build
    /// lock, the bot's queued press. Per World.Barricades slot: whether the slot is a wall cell (short / tall), its piece and the
    /// cell it was built on. Per piece: owner, first cell, cell count, cells still standing, build order.
    /// </summary>
    public sealed class WallsState
    {
        // ---- per actor
        /// <summary>(byte)BuildPiece the Builder has selected.</summary>
        public byte[] Piece;
        /// <summary>1 = he wants the tall piece (used only while his team's tier is 1 or more). Default 1: the tallest he has.</summary>
        public byte[] Tall;
        /// <summary>World.Tick from which he may build again (BuildConfig.LockTicks after each build).</summary>
        public int[] ReadyTick;
        /// <summary>The last Ghost() verdict for the view (0 = will build, else a BuilderWalls.Refused* code) and whether it was a fill.</summary>
        public int[] LastWhy;
        public byte[] LastFill;
        /// <summary>Bots: one queued F press (BotBuild), its aim and the piece it wants; the last rule 1 / rule 2 attempt ticks.</summary>
        public byte[] BotReq;
        public double[] BotX;
        public double[] BotZ;
        public byte[] BotPiece;
        public byte[] BotTall;
        public int[] BotHillTick;
        public int[] BotGuardTick;
        /// <summary>Pieces (walls and blocks, fills not counted) each actor built this match.</summary>
        public int[] Built;
        /// <summary>
        /// b13 r3 key layout: 0 = the DIG / PLACE tool is selected (F digs or places through Terraform, the switch steps DIG ->
        /// PLACE -> WALL), 1 = a build piece is selected (F builds it). The dig tool exists only while the Builder owns terrain
        /// (Terraform.OwnerClass); otherwise 0 reads as a build piece.
        /// </summary>
        public byte[] Tool;
        /// <summary>Bots: the BotBuild rule behind the queued press (1 hill edge, 2 guard, 3 crater fill; 0 = a player's press).</summary>
        public byte[] BotRule;
        /// <summary>Stats (tests print them): pieces and fills committed per team x rule (index team * 4 + rule).</summary>
        public readonly int[] BuiltByRule = new int[8];
        /// <summary>Stats: plans per team whose aim x sat exactly on a cell boundary (BuilderWalls.MirrorCellX).</summary>
        public readonly int[] BoundaryX = new int[2];

        // ---- per World.Barricades slot
        /// <summary>0 = not a wall cell (free or a Miner barricade), 1 = short (Cell.LowWall), 2 = tall (Cell.Barricade).</summary>
        public byte[] Kind;
        public int[] PieceOf;
        public int[] SlotCell;
        public int[] SlotOwner;
        public double[] MaxHp;
        /// <summary>World.Barricades length before this item grew the pool (Miner slots come first).</summary>
        public int BaseSlots;

        // ---- per piece (capacity = pool length)
        public int[] POwner;
        public int[] PFirst;
        public int[] PCells;
        public int[] PAlive;
        public int[] PSeq;
        public int Seq;

        /// <summary>Per team: the StartWood credit left (spent before the Sources wood bank).</summary>
        public readonly int[] Credit = new int[2];

        // ---- stats (tests print them)
        public int Builds;
        public int Fills;
        public int Refusals;
        public readonly int[] RefusedBy = new int[16];

        // ---- route memo (small ring: the ghost asks every frame, a bot every think)
        internal readonly long[] MemoKey = new long[16];
        internal readonly int[] MemoVersion = new int[16];
        internal readonly byte[] MemoOk = new byte[16];
        internal int MemoNext;

        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w)
        {
            int n = w.Actors.Length;
            Piece = new byte[n];
            Tall = new byte[n];
            ReadyTick = new int[n];
            LastWhy = new int[n];
            LastFill = new byte[n];
            BotReq = new byte[n];
            BotX = new double[n];
            BotZ = new double[n];
            BotPiece = new byte[n];
            BotTall = new byte[n];
            BotHillTick = new int[n];
            BotGuardTick = new int[n];
            Built = new int[n];
            Tool = new byte[n];
            BotRule = new byte[n];
            BaseSlots = w.Barricades.Length;
            if (BuilderWalls.On(w))
            {
                // Grow the fixed pool once (World.cs is frozen): 15 cells per Builder x PoolBuildersPerTeam x 2 teams.
                BuildConfig b = w.Config.Depth.Build;
                int extra = (int)b.MaxCellsPerBuilder * (int)b.PoolBuildersPerTeam * 2;
                var grown = new Barricade[BaseSlots + extra];
                Array.Copy(w.Barricades, grown, BaseSlots);
                for (int i = BaseSlots; i < grown.Length; i++)
                    grown[i] = new Barricade { Active = false, Owner = -1, Team = TeamId.Azure, Cx = -1, Cz = -1, Hp = 0, Ttl = 0, PrevCell = 0 };
                w.Barricades = grown;
            }
            int m = w.Barricades.Length;
            Kind = new byte[m];
            PieceOf = new int[m];
            SlotCell = new int[m];
            SlotOwner = new int[m];
            MaxHp = new double[m];
            POwner = new int[m];
            PFirst = new int[m];
            PCells = new int[m];
            PAlive = new int[m];
            PSeq = new int[m];
            Reset(w);
        }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w)
        {
            if (Piece == null) return;
            for (int i = 0; i < Piece.Length; i++)
            {
                Piece[i] = (byte)BuildPiece.Wall;
                Tall[i] = 1;
                ReadyTick[i] = 0;
                LastWhy[i] = 0;
                LastFill[i] = 0;
                BotReq[i] = 0;
                BotX[i] = 0;
                BotZ[i] = 0;
                BotPiece[i] = 0;
                BotTall[i] = 0;
                BotHillTick[i] = int.MinValue / 2;
                BotGuardTick[i] = int.MinValue / 2;
                Built[i] = 0;
                Tool[i] = 0;
                BotRule[i] = 0;
            }
            Array.Clear(BuiltByRule, 0, BuiltByRule.Length);
            Array.Clear(BoundaryX, 0, BoundaryX.Length);
            for (int s = 0; s < Kind.Length; s++)
            {
                Kind[s] = 0;
                PieceOf[s] = -1;
                SlotCell[s] = -1;
                SlotOwner[s] = -1;
                MaxHp[s] = 0;
                POwner[s] = -1;
                PFirst[s] = -1;
                PCells[s] = 0;
                PAlive[s] = 0;
                PSeq[s] = 0;
            }
            Seq = 0;
            int start = BuilderWalls.On(w) ? (int)w.Config.Depth.Build.StartWood : 0;
            Credit[0] = start;
            Credit[1] = start;
            Builds = 0;
            Fills = 0;
            Refusals = 0;
            Array.Clear(RefusedBy, 0, RefusedBy.Length);
            for (int k = 0; k < MemoKey.Length; k++)
            {
                MemoKey[k] = -1;
                MemoVersion[k] = -1;
                MemoOk[k] = 0;
            }
            MemoNext = 0;
        }
    }

    /// <summary>
    /// CM b13 item B: the Builder builds walls and blocks at a ghost. F (InputFrame.Terraform, rising edge) builds the selected
    /// piece; the switch (InputFrame.TerraformSwitch: wheel, LB / RB) cycles WALL SHORT -> WALL TALL -> BLOCK (TALL skipped while
    /// the team's tier is 0). The ghost: the aim point clamped to BuildConfig.Reach, the cell under it is the centre; a wall runs
    /// perpendicular to the aim direction (his facing when the aim is under AimAxisMin) snapped to the nearer axis, 3 cells (a
    /// tall wall at tier 2: 5). A BLOCK aimed at a Void cell fills it to Floor (Terraform's place checks and team rate cap).
    /// Piece cells reuse World.Barricades slots (owner, team, HP, life), so swings, TNT and the Kaiju break them like barricades.
    /// </summary>
    public static class BuilderWalls
    {
        /// <summary>Build outcomes (also the Value of a refused WallBuilt event, always lt 0).</summary>
        public const int Built = 1, Filled = 2;
        public const int RefusedOutside = -1, RefusedGround = -2, RefusedKeepOut = -3, RefusedOccupied = -4, RefusedWood = -5,
            RefusedLock = -6, RefusedRoute = -7, RefusedRate = -8, RefusedFull = -9, RefusedDown = -10;

        /// <summary>Slot kinds.</summary>
        public const byte KindShort = 1, KindTall = 2;

        private static readonly int[] ProofIdx = new int[8];
        private static readonly byte[] ProofNext = new byte[8];

        /// <summary>The rule is on.</summary>
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Build.On);

        /// <summary>The team's wall tier, 0..2, from its hut level (BuildConfig.TierAtHutLevel).</summary>
        public static int Tier(World w, TeamId team)
        {
            if (!On(w)) return 0;
            int[] t = w.Config.Depth.Build.TierAtHutLevel;
            if (t == null || t.Length == 0) return 0;
            int lvl = Blacksmith.Level(w, team);
            if (lvl < 0) lvl = 0;
            if (lvl >= t.Length) lvl = t.Length - 1;
            int tier = t[lvl];
            return tier < 0 ? 0 : tier > 2 ? 2 : tier;
        }

        private static bool Valid(World w, int id) => id >= 0 && id < w.ActorCount && w.Walls.Piece != null && id < w.Walls.Piece.Length;

        /// <summary>The piece Builder id has selected.</summary>
        public static BuildPiece Piece(World w, int id)
        {
            if (!On(w) || !Valid(w, id)) return BuildPiece.Wall;
            return (BuildPiece)w.Walls.Piece[id];
        }

        /// <summary>The tall flag in effect for Builder id (his choice and a tier of 1 or more).</summary>
        public static bool TallSelected(World w, int id)
        {
            if (!On(w) || !Valid(w, id)) return false;
            return w.Walls.Tall[id] == 1 && Tier(w, w.Actors[id].Team) >= 1;
        }

        /// <summary>Wood the team may spend on walls now (its StartWood credit plus its Sources wood bank).</summary>
        public static int Wood(World w, TeamId team)
        {
            return w.Walls.Credit[(int)team] + Sources.Stock(w, team, Sources.StockWood);
        }

        private static void Spend(World w, TeamId team, double cost)
        {
            int c = cost > 0 ? (int)Math.Floor(cost) : 0;
            int[] credit = w.Walls.Credit;
            int take = credit[(int)team] < c ? credit[(int)team] : c;
            credit[(int)team] -= take;
            c -= take;
            if (c > 0) Sources.SpendUpTo(w, team, Sources.StockWood, c);
        }

        /// <summary>Wood a piece costs.</summary>
        public static double Cost(BuildConfig b, BuildPiece piece, bool tall, bool fill)
        {
            if (fill) return b.CostFill;
            if (piece == BuildPiece.Block) return b.CostBlock;
            return tall ? b.CostTallWall : b.CostShortWall;
        }

        /// <summary>The one word the view shows for a verdict.</summary>
        public static string Word(int why)
        {
            switch (why)
            {
                case 0: return "BUILD";
                case Built: return "BUILD";
                case Filled: return "FILL";
                case RefusedOutside: return "OUT OF REACH";
                case RefusedGround: return "NO GROUND";
                case RefusedKeepOut: return "TOO CLOSE";
                case RefusedOccupied: return "BLOCKED";
                case RefusedWood: return "NO WOOD";
                case RefusedLock: return "WAIT";
                case RefusedRoute: return "ROUTE";
                case RefusedRate: return "WAIT";
                case RefusedFull: return "FULL";
                case RefusedDown: return "WAIT";
                default: return "";
            }
        }

        /// <summary>A Builder's F press: true = taken (build), so Terraform skips it.</summary>
        public static bool TakeBuilderPress(World w, int id)
        {
            if (!On(w) || !Valid(w, id)) return false;
            Actor a = w.Actors[id];
            if (!a.Alive || a.Cls != ClassId.Builder) return false;
            if (TerrainTool(w, id)) return false;   // b13 r3: the DIG / PLACE tool is selected, Terraform digs or places
            if (a.StandUp > 0) return true;
            bool hasAim = w.HasAim[id] == 1;
            InputFrame inp = w.Inputs[id];
            Build(w, id, hasAim ? inp.AimX : double.NaN, hasAim ? inp.AimZ : double.NaN);
            return true;
        }

        /// <summary>A Builder's switch press: true = taken (switch piece), so Terraform skips it.</summary>
        public static bool TakeBuilderSwitch(World w, int id)
        {
            if (!On(w) || !Valid(w, id)) return false;
            Actor a = w.Actors[id];
            if (!a.Alive || a.Cls != ClassId.Builder) return false;
            WallsState s = w.Walls;
            bool owns = OwnsTerrain(w);
            bool tierTall = Tier(w, a.Team) >= 1;
            bool tallEff = s.Tall[id] == 1 && tierTall;
            TerraformState t = w.Terraform;
            if (owns && s.Tool[id] == 0)
            {
                // b13 r3 key layout: DIG -> PLACE -> WALL (the switch keeps the old dig / place flip, then walks into the pieces).
                if (t.Mode[id] != (byte)TerraformMode.Place) t.Mode[id] = (byte)TerraformMode.Place;
                else
                {
                    s.Tool[id] = 1;
                    s.Piece[id] = (byte)BuildPiece.Wall;
                    s.Tall[id] = 0;
                }
                return true;
            }
            if (s.Piece[id] == (byte)BuildPiece.Wall)
            {
                if (!tallEff && tierTall) s.Tall[id] = 1;           // WALL SHORT -> WALL TALL
                else s.Piece[id] = (byte)BuildPiece.Block;          // WALL TALL (or SHORT at tier 0) -> BLOCK
            }
            else if (owns)
            {
                s.Tool[id] = 0;                                     // BLOCK -> DIG (the cycle starts again)
                t.Mode[id] = (byte)TerraformMode.Dig;
            }
            else
            {
                s.Piece[id] = (byte)BuildPiece.Wall;                // BLOCK -> WALL SHORT
                s.Tall[id] = 0;
            }
            return true;
        }

        /// <summary>b13 r3: the Builder owns the dig / place terraform (the hut rule moved it from the Miner).</summary>
        public static bool OwnsTerrain(World w) => Terraform.OwnerClass(w) == ClassId.Builder;

        /// <summary>b13 r3: Builder id has the DIG / PLACE tool selected (F goes to Terraform, no wall ghost).</summary>
        public static bool TerrainTool(World w, int id) => On(w) && Valid(w, id) && w.Walls.Tool[id] == 0 && OwnsTerrain(w);

        /// <summary>Per tick, after Barricades.BarricadesSystem: a wall cell whose slot broke, expired or was reused is released (WallBroken once per piece).</summary>
        public static void WallsSystem(World w)
        {
            if (!On(w)) return;
            Sweep(w);
        }

        /// <summary>The build ghost for Builder id: cell, direction, length, tall, will build. False = no ghost (rule off, not a living Builder).</summary>
        public static bool Ghost(World w, int id, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool ok)
        {
            cx = 0; cz = 0; dx = 0; dz = 0; len = 0; tall = false; ok = false;
            if (!On(w) || !Valid(w, id)) return false;
            Actor a = w.Actors[id];
            if (!a.Alive || a.Cls != ClassId.Builder) return false;
            if (TerrainTool(w, id)) return false;   // b13 r3: the dig / place affordance shows instead
            bool hasAim = w.HasAim[id] == 1;
            InputFrame inp = w.Inputs[id];
            int why = Plan(w, id, hasAim ? inp.AimX : double.NaN, hasAim ? inp.AimZ : double.NaN, out cx, out cz, out dx, out dz, out len, out tall, out bool fill);
            if (why == 0 && a.StandUp > 0) why = RefusedDown;
            w.Walls.LastWhy[id] = why;
            w.Walls.LastFill[id] = fill ? (byte)1 : (byte)0;
            ok = why == 0;
            return true;
        }

        /// <summary>The verdict of the last Ghost() call for id (0 = will build) and whether it is a crater fill.</summary>
        public static int GhostWhy(World w, int id) => Valid(w, id) ? w.Walls.LastWhy[id] : 0;
        public static bool GhostFill(World w, int id) => Valid(w, id) && w.Walls.LastFill[id] == 1;

        /// <summary>The verdict for Builder id aiming at (aimX, aimZ) now (NaN = straight ahead), without building.</summary>
        public static int Refusal(World w, int id, double aimX, double aimZ)
        {
            if (!On(w) || !Valid(w, id)) return RefusedDown;
            return Plan(w, id, aimX, aimZ, out _, out _, out _, out _, out _, out _, out _);
        }

        // -----------------------------------------------------------------------------------------------------------
        // Placement

        /// <summary>Every per-cell rule: 0 = a piece cell may stand on (cx, cz), else a Refused* code.</summary>
        public static int CellRefusal(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (!MapGrid.InBounds(map, cx, cz)) return RefusedOutside;
            byte t = map.Cells[cz * map.Width + cx];
            if (t != Cell.Floor && t != Cell.BreakableDestroyed) return RefusedGround;
            double x = cx + 0.5;
            double z = cz + 0.5;
            BuildConfig b = w.Config.Depth.Build;
            SourcesConfig S = w.Config.Depth.Sources;
            for (int k = 0; k < map.Pads.Length; k++)
            {
                double r = map.Pads[k].R + S.PadKeepOut;
                if (Near(x, z, map.Pads[k].X, map.Pads[k].Z, r)) return RefusedKeepOut;
            }
            if (Near(x, z, map.Shrine.X, map.Shrine.Z, S.ShrineKeepOut)) return RefusedKeepOut;
            if (Near(x, z, map.Hill.X, map.Hill.Z, b.HillKeepout)) return RefusedKeepOut;
            for (int k = 0; k < w.Nodes.Length; k++)
            {
                ResourceNode nd = w.Nodes[k];
                if (Near(x, z, nd.X, nd.Z, S.NodeKeepOut)) return RefusedKeepOut;
            }
            SourceNode[] src = w.Depth != null ? w.Depth.Sources : null;
            if (src != null)
            {
                for (int k = 0; k < src.Length; k++)
                {
                    if (src[k] != null && Near(x, z, src[k].X, src[k].Z, S.NodeKeepOut)) return RefusedKeepOut;
                }
            }
            GameConfig cfg = w.Config;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor a = w.Actors[j];
                if (!a.Alive) continue;
                if (Near(w.X[j], w.Z[j], x, z, b.ActorClearance + cfg.Class(a.Cls).Radius)) return RefusedOccupied;
            }
            Kaiju kj = w.Kaiju;
            if (kj.State != KaijuState.Egg && Near(kj.X, kj.Z, x, z, b.ActorClearance + cfg.Kaiju.Radius)) return RefusedOccupied;
            return 0;
        }

        private static bool Near(double x, double z, double px, double pz, double r)
        {
            double dx = x - px;
            double dz = z - pz;
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>
        /// The piece Builder id would build for the aim point and its verdict (0 = ok). Shared by the press, the ghost and the
        /// bots. Order: cells (outside, ground, keep-outs, actors), wood, the build lock, then the route guard (the costly one).
        /// </summary>
        private static int Plan(World w, int id, double aimX, double aimZ, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool fill)
        {
            WallsState s = w.Walls;
            BuildConfig b = w.Config.Depth.Build;
            Actor a = w.Actors[id];
            double ox = w.X[id];
            double oz = w.Z[id];
            Abilities.ClampToRange(ox, oz, aimX, aimZ, a.Facing, b.Reach, out double tx, out double tz);
            cx = MirrorCellX(w, tx, ox, a.Team);
            cz = JsMath.FloorToInt(tz);
            double vx = tx - ox;
            double vz = tz - oz;
            if (vx * vx + vz * vz < b.AimAxisMin * b.AimAxisMin)
            {
                vx = JsMath.Cos(a.Facing);
                vz = JsMath.Sin(a.Facing);
            }
            // He faces along the nearer axis; the wall runs across it (ties go to x, like BuiltPieces.AxisToward).
            if (Math.Abs(vx) >= Math.Abs(vz)) { dx = 0; dz = 1; }
            else { dx = 1; dz = 0; }
            int tier = Tier(w, a.Team);
            BuildPiece piece = (BuildPiece)s.Piece[id];
            tall = s.Tall[id] == 1 && tier >= 1;
            fill = false;
            MapDef map = w.Map;
            if (!MapGrid.InBounds(map, cx, cz)) { len = 0; return RefusedOutside; }
            if (piece == BuildPiece.Block)
            {
                len = 1;
                if (map.Cells[cz * map.Width + cx] == Cell.Void)
                {
                    // The old Miner place, now the Builder's: a permanent fill under the shared terrain rate cap.
                    fill = true;
                    tall = false;
                    if (Terraform.PlaceRefusal(w, cx, cz) != 0) return RefusedGround;
                    if (Wood(w, a.Team) < b.CostFill) return RefusedWood;
                    if (w.Tick < s.ReadyTick[id]) return RefusedLock;
                    if (Terraform.WaitTicks(w, a.Team) > 0) return RefusedRate;
                    return 0;
                }
            }
            else
            {
                len = tall && tier >= 2 ? (int)b.WallCellsTier2 : (int)b.WallCells;
                if (len < 1) len = 1;
                if (len > ProofIdx.Length) len = ProofIdx.Length;
            }
            int half = len / 2;
            // b13 r3: every cell is checked and the most basic refusal wins (outside, ground, keep-out, then occupied), so a
            // mirrored piece gets the same word for both teams (first-found depended on which end the scan started from).
            int worst = 0;
            for (int k = -half; k < len - half; k++)
            {
                int why = CellRefusal(w, cx + k * dx, cz + k * dz);
                if (why != 0 && (worst == 0 || why > worst)) worst = why;
            }
            if (worst != 0) return worst;
            if (Wood(w, a.Team) < Cost(b, piece, tall, false)) return RefusedWood;
            if (w.Tick < s.ReadyTick[id]) return RefusedLock;
            if (!RouteOk(w, cx, cz, dx, dz, len)) return RefusedRoute;
            return 0;
        }

        /// <summary>
        /// b13 r3 (the round 1 tilt check): the cell column for an aim x that is mirror-fair between the teams. The map is a mirror
        /// image across x = MirrorSum / 2 (cell cx mirrors to Width - 1 - cx), but FloorToInt puts a point exactly on a cell
        /// boundary into the cell on its +x side for BOTH teams, so the same aim mirrored landed one cell off for Crimson
        /// (MirroredBuildersGetMirroredPieces: 221 of 875 mirrored aims). On an exact boundary the piece now goes to the cell on
        /// the Builder's side; when he stands on the boundary line too, to the cell on the side of the mirror line he aims from
        /// (on the mirror line itself, each team's own half). Off a boundary this is FloorToInt, unchanged.
        /// </summary>
        public static int MirrorCellX(World w, double tx, double ox, TeamId team)
        {
            int c = JsMath.FloorToInt(tx);
            if (tx != c) return c;
            w.Walls.BoundaryX[(int)team]++;
            double mid = w.Map.Width * 0.5;
            if (ox < tx) return c - 1;
            if (ox > tx) return c;
            if (tx > mid) return c - 1;
            if (tx < mid) return c;
            return team == TeamId.Azure ? c - 1 : c;
        }

        /// <summary>
        /// The route guard for a whole piece at once (Terraform.CellsKeepRoutes: both pads still reach the hill and a pad that
        /// reached the shrine still does), memoised by (centre cell, axis, length, World.GridVersion).
        /// </summary>
        public static bool RouteOk(World w, int cx, int cz, int dx, int dz, int len)
        {
            WallsState s = w.Walls;
            MapDef map = w.Map;
            long key = ((long)(cz * map.Width + cx) * 2 + (dx != 0 ? 1 : 0)) * 16 + len;
            for (int k = 0; k < s.MemoKey.Length; k++)
            {
                if (s.MemoKey[k] == key && s.MemoVersion[k] == w.GridVersion) return s.MemoOk[k] == 1;
            }
            int half = len / 2;
            int n = 0;
            for (int k = -half; k < len - half; k++)
            {
                ProofIdx[n] = (cz + k * dz) * map.Width + (cx + k * dx);
                ProofNext[n] = Cell.Barricade;
                n++;
            }
            bool ok = Terraform.CellsKeepRoutes(w, ProofIdx, ProofNext, n);
            int slot = s.MemoNext;
            s.MemoNext = (slot + 1) % s.MemoKey.Length;
            s.MemoKey[slot] = key;
            s.MemoVersion[slot] = w.GridVersion;
            s.MemoOk[slot] = ok ? (byte)1 : (byte)0;
            return ok;
        }

        /// <summary>
        /// Builder id builds the selected piece for the aim point (NaN = straight ahead). Returns Built, Filled or a Refused* code;
        /// a refusal pushes a WallBuilt event with that code as its Value and changes nothing.
        /// </summary>
        public static int Build(World w, int id, double aimX, double aimZ)
        {
            if (!On(w) || !Valid(w, id)) return RefusedDown;
            Actor a = w.Actors[id];
            WallsState s = w.Walls;
            Sweep(w);
            int why;
            if (!a.Alive || a.Cls != ClassId.Builder || a.StandUp > 0)
            {
                why = RefusedDown;
            }
            else
            {
                why = Plan(w, id, aimX, aimZ, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool fill);
                if (why == 0) why = Commit(w, id, cx, cz, dx, dz, len, tall, fill);
            }
            if (why < 0)
            {
                s.Refusals++;
                if (-why < s.RefusedBy.Length) s.RefusedBy[-why]++;
                double rx = double.IsFinite(aimX) ? aimX : w.X[id];
                double rz = double.IsFinite(aimZ) ? aimZ : w.Z[id];
                World.PushEvent(w, SimEventType.WallBuilt, id, -1, rx, rz, why);
            }
            return why;
        }

        private static int Commit(World w, int id, int cx, int cz, int dx, int dz, int len, bool tall, bool fill)
        {
            WallsState s = w.Walls;
            BuildConfig b = w.Config.Depth.Build;
            Actor a = w.Actors[id];
            MapDef map = w.Map;
            BuildPiece piece = (BuildPiece)s.Piece[id];
            if (fill)
            {
                Spend(w, a.Team, b.CostFill);
                Terraform.CommitFill(w, id, cx, cz);
                s.ReadyTick[id] = w.Tick + Durations.T30(w.Config, b.LockTicks);
                s.Fills++;
                s.BuiltByRule[(int)a.Team * 4 + (s.BotRule[id] & 3)]++;
                s.BotRule[id] = 0;
                return Filled;
            }

            // The cap: building past MaxCellsPerBuilder crumbles his oldest pieces; first prove the pool can take the piece.
            int max = (int)b.MaxCellsPerBuilder;
            int owned = OwnedCells(w, id);
            int freed = 0;
            int after = -1;
            while (owned - freed + len > max)
            {
                int p = OldestPiece(w, id, after);
                if (p < 0) break;
                freed += s.PAlive[p];
                after = s.PSeq[p];
            }
            Barricade[] list = w.Barricades;
            int free = 0;
            for (int k = s.BaseSlots; k < list.Length; k++) if (!list[k].Active) free++;
            if (free + freed < len) return RefusedFull;
            int pieceSlot = -1;
            for (int p = 0; p < s.PAlive.Length; p++) if (s.PAlive[p] == 0) { pieceSlot = p; break; }
            if (pieceSlot < 0) return RefusedFull;
            while (owned + len > max)
            {
                int p = OldestPiece(w, id, -1);
                if (p < 0) break;
                owned -= s.PAlive[p];
                CrumblePiece(w, p);
            }
            Spend(w, a.Team, Cost(b, piece, tall, false));
            byte cell = tall ? Cell.Barricade : Cell.LowWall;
            double hp = !tall ? b.ShortHp : Tier(w, a.Team) >= 2 ? b.TallHp2 : b.TallHp;
            double life = Durations.T30(w.Config, tall ? b.TallLifeTicks : b.ShortLifeTicks) / w.Config.SimHz;
            double tough = HutChoices.WallScale(w, a.Team);   // b15-hook: TOUGH WALLS (exactly 1 with the rule off)
            if (tough != 1.0) { hp *= tough; life *= tough; }
            int half = len / 2;
            int first = (cz - half * dz) * map.Width + (cx - half * dx);
            s.POwner[pieceSlot] = id;
            s.PFirst[pieceSlot] = first;
            s.PCells[pieceSlot] = len;
            s.PAlive[pieceSlot] = 0;
            s.PSeq[pieceSlot] = ++s.Seq;
            int search = list.Length - 1;
            for (int k = -half; k < len - half; k++)
            {
                int x = cx + k * dx;
                int z = cz + k * dz;
                while (search >= s.BaseSlots && list[search].Active) search--;
                if (search < s.BaseSlots) break;
                int slot = search;
                if (s.Kind[slot] != 0) Release(w, slot);
                Barricade bs = list[slot];
                bs.Active = true;
                bs.Owner = id;
                bs.Team = a.Team;
                bs.Cx = x;
                bs.Cz = z;
                bs.Hp = hp;
                bs.Ttl = life;
                bs.PrevCell = map.Cells[z * map.Width + x];
                MapGrid.SetCell(w, x, z, cell);
                s.Kind[slot] = tall ? KindTall : KindShort;
                s.PieceOf[slot] = pieceSlot;
                s.SlotCell[slot] = z * map.Width + x;
                s.SlotOwner[slot] = id;
                s.MaxHp[slot] = hp;
                s.PAlive[pieceSlot]++;
            }
            s.ReadyTick[id] = w.Tick + Durations.T30(w.Config, b.LockTicks);
            s.Builds++;
            s.Built[id]++;
            s.BuiltByRule[(int)a.Team * 4 + (s.BotRule[id] & 3)]++;
            s.BotRule[id] = 0;
            World.PushEvent(w, SimEventType.WallBuilt, id, first, cx + 0.5, cz + 0.5, len);
            return Built;
        }

        /// <summary>Wall cells Builder id has standing.</summary>
        public static int OwnedCells(World w, int id)
        {
            WallsState s = w.Walls;
            int n = 0;
            for (int p = 0; p < s.PAlive.Length; p++) if (s.PAlive[p] > 0 && s.POwner[p] == id) n += s.PAlive[p];
            return n;
        }

        /// <summary>His standing piece with the lowest build order above `after` (-1 = none).</summary>
        private static int OldestPiece(World w, int id, int after)
        {
            WallsState s = w.Walls;
            int best = -1;
            for (int p = 0; p < s.PAlive.Length; p++)
            {
                if (s.PAlive[p] <= 0 || s.POwner[p] != id || s.PSeq[p] <= after) continue;
                if (best < 0 || s.PSeq[p] < s.PSeq[best]) best = p;
            }
            return best;
        }

        private static void CrumblePiece(World w, int p)
        {
            WallsState s = w.Walls;
            Barricade[] list = w.Barricades;
            for (int slot = 0; slot < list.Length && s.PAlive[p] > 0; slot++)
            {
                if (s.Kind[slot] == 0 || s.PieceOf[slot] != p) continue;
                if (list[slot].Active) Barricades.RemoveBarricade(w, slot, Barricades.BarricadeReplaced);
                Release(w, slot);
            }
        }

        /// <summary>True when barricade slot s is a standing wall cell of this rule (same owner and cell it was built with).</summary>
        public static bool IsWallSlot(World w, int slot)
        {
            WallsState s = w.Walls;
            if (s.Kind == null || slot < 0 || slot >= s.Kind.Length || s.Kind[slot] == 0) return false;
            Barricade b = w.Barricades[slot];
            return b.Active && b.Owner == s.SlotOwner[slot] && b.Cz * w.Map.Width + b.Cx == s.SlotCell[slot];
        }

        /// <summary>Slot kind for the view: 0 none, KindShort, KindTall (standing wall cells only).</summary>
        public static byte SlotKind(World w, int slot) => IsWallSlot(w, slot) ? w.Walls.Kind[slot] : (byte)0;

        /// <summary>Max HP of wall slot `slot` (0 when it is not one).</summary>
        public static double SlotMaxHp(World w, int slot) => IsWallSlot(w, slot) ? w.Walls.MaxHp[slot] : 0;

        private static void Release(World w, int slot)
        {
            WallsState s = w.Walls;
            int p = s.PieceOf[slot];
            s.Kind[slot] = 0;
            s.PieceOf[slot] = -1;
            s.SlotCell[slot] = -1;
            s.SlotOwner[slot] = -1;
            s.MaxHp[slot] = 0;
            if (p < 0 || s.PAlive[p] <= 0) return;
            s.PAlive[p]--;
            if (s.PAlive[p] > 0) return;
            MapDef map = w.Map;
            int first = s.PFirst[p];
            World.PushEvent(w, SimEventType.WallBroken, s.POwner[p], first, first % map.Width + 0.5, first / map.Width + 0.5, s.PCells[p]);
        }

        /// <summary>Release every wall record whose slot broke, expired or was taken over since the last look.</summary>
        private static void Sweep(World w)
        {
            WallsState s = w.Walls;
            if (s.Kind == null) return;
            for (int slot = s.BaseSlots; slot < s.Kind.Length; slot++)
            {
                if (s.Kind[slot] != 0 && !IsWallSlot(w, slot)) Release(w, slot);
            }
        }

        /// <summary>A TNT blast of `team` at (x, z): every ENEMY wall cell whose centre is within r loses BuildConfig.TntDamage.</summary>
        public static void BlastWalls(World w, TeamId team, double x, double z, double r)
        {
            if (!On(w)) return;
            double dmg = w.Config.Depth.Build.TntDamage;
            Barricade[] list = w.Barricades;
            for (int slot = 0; slot < list.Length; slot++)
            {
                if (!IsWallSlot(w, slot)) continue;
                Barricade b = list[slot];
                if (b.Team == team || b.Hp <= 0) continue;
                double dx = b.Cx + 0.5 - x;
                double dz = b.Cz + 0.5 - z;
                if (dx * dx + dz * dz <= r * r) b.Hp -= dmg;
            }
        }

        // -----------------------------------------------------------------------------------------------------------
        // Bots

        /// <summary>BotBuild: queue one F press for Builder bot id aimed at (x, z) with the given piece, if the sim would accept it now.</summary>
        public static bool QueueBotPress(World w, int id, double x, double z, BuildPiece piece, bool tall, int rule = 0)
        {
            if (!On(w) || !Valid(w, id)) return false;
            WallsState s = w.Walls;
            byte oldPiece = s.Piece[id];
            byte oldTall = s.Tall[id];
            s.Piece[id] = (byte)piece;
            s.Tall[id] = tall ? (byte)1 : (byte)0;
            int why = Plan(w, id, x, z, out _, out _, out _, out _, out _, out _, out _);
            s.Piece[id] = oldPiece;
            s.Tall[id] = oldTall;
            if (why != 0) return false;
            s.BotReq[id] = 1;
            s.BotX[id] = JsMath.F32(x);
            s.BotZ[id] = JsMath.F32(z);
            s.BotPiece[id] = (byte)piece;
            s.BotTall[id] = tall ? (byte)1 : (byte)0;
            s.BotRule[id] = (byte)(rule & 3);
            return true;
        }

        /// <summary>BotBrain.FillBotInput (through Demolitions.TakeBotTnt): a queued Builder press becomes this frame's F press, aimed.</summary>
        public static bool TakeBotPress(World w, int id, InputFrame output)
        {
            if (!On(w) || !Valid(w, id)) return false;
            WallsState s = w.Walls;
            if (s.BotReq[id] != 1) return false;
            s.BotReq[id] = 0;
            if (!w.Actors[id].Alive || w.Actors[id].Cls != ClassId.Builder) return false;
            s.Tool[id] = 1;   // b13 r3: a bot's build press always uses a piece (bots never dig)
            s.Piece[id] = s.BotPiece[id];
            s.Tall[id] = s.BotTall[id];
            output.AimX = s.BotX[id];
            output.AimZ = s.BotZ[id];
            output.Terraform = true;
            return true;
        }
    }
}
