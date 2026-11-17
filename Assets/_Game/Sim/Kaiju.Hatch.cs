using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Port of src/sim/kaiju.ts lines 1-262 (09e0904): hatch, allegiance resolution, damage taken, the 60 s KO, the
    /// clearance lane and target choice. The behaviour system (lines 263-543) is in Kaiju.Ai.cs (F-05j).
    /// </summary>
    public sealed partial class Kaiju
    {
        /// <summary>Seconds between target selections.</summary>
        public const double KaijuRetargetSeconds = 0.5;

        /// <summary>Minimum seconds between flow-field rebuilds for a new goal cell (wall changes rebuild at once).</summary>
        public const double KaijuRepathSeconds = 1;

        /// <summary>Hatch search starts this far south of the shrine centre (outside the yard wall, outer radius 5).</summary>
        internal const double HatchOffsetZ = 6;

        internal const int HatchSearchRings = 16;

        /// <summary>A goal standing on a non-clearance cell snaps to the nearest clearance cell within this many rings.</summary>
        internal const int GoalSnapRings = 4;

        /// <summary>Berserk targeting: score = distance - BigFeederBias when the target's team delivered more biomass.</summary>
        internal const double BigFeederBias = 4;

        /// <summary>Never target an actor within pad.r + PadMargin of its own pad (never camps a spawn).</summary>
        internal const double PadMargin = 2;

        /// <summary>Chasing stops at meleeRange * this, so the target stays inside reach.</summary>
        internal const double ChaseStop = 0.8;

        /// <summary>With no target, an allied / berserk Kaiju stops this close to the hill centre; a neutral one this close to home.</summary>
        internal const double HillStop = 1.5;

        internal const double HomeStop = 1;

        internal const double Skin = 1e-3;

        internal const double MaxSubstep = 0.25;

        /// <summary>fix/movement-shiver-1: holding on the goal cell when the body is this close to its centre.</summary>
        internal const double HoldEps = 0.05;

        /// <summary>CM-26: a wound-up melee still lands on a target within meleeRange + this (it stepped back, not away).</summary>
        public const double MeleeLeash = 0.4;

        /// <summary>Natural hatch tolerance on the accumulated float clock.</summary>
        internal const double HatchTimeEps = 1e-6;

        /// <summary>Neighbour offsets: 4 orthogonal then 4 diagonal (same order as flowfield.ts).</summary>
        internal static readonly int[] Ndx = { 1, -1, 0, 0, 1, 1, -1, -1 };

        internal static readonly int[] Ndz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Module-scope scratch (never allocated in a step).</summary>
        internal static readonly PointXZ[] GoalScratch = new PointXZ[1];

        /// <summary>The Kaiju state a hatch produces right now.</summary>
        public static KaijuState ResolveHatch(MatchState m)
        {
            if (m.BerserkLocked) return KaijuState.Berserk;
            if (m.Allegiance.Azure > m.Allegiance.Crimson) return KaijuState.Azure;
            if (m.Allegiance.Crimson > m.Allegiance.Azure) return KaijuState.Crimson;
            return KaijuState.Neutral;
        }

        /// <summary>'hatch' event value: 0 neutral, 1 azure, 2 crimson, 3 berserk.</summary>
        public static int HatchCode(KaijuState s)
        {
            return s == KaijuState.Azure ? 1 : s == KaijuState.Crimson ? 2 : s == KaijuState.Berserk ? 3 : 0;
        }

        /// <summary>True when the Kaiju, in its current state, attacks actor <paramref name="id"/> (ignores alive / protection / pad).</summary>
        public static bool KaijuHostileTo(World w, int id)
        {
            Kaiju k = w.Kaiju;
            TeamId team = w.Actors[id].Team;
            switch (k.State)
            {
                case KaijuState.Azure:
                    return team == TeamId.Crimson;
                case KaijuState.Crimson:
                    return team == TeamId.Azure;
                case KaijuState.Berserk:
                    return true;
                case KaijuState.Neutral:
                    return k.Threat[id] > 0;
                default:
                    return false;
            }
        }

        /// <summary>True when actor <paramref name="id"/> stands within pad.r + 2 of its own team pad.</summary>
        public static bool NearOwnPad(World w, int id)
        {
            PadDef pad = w.Map.Pads[(int)w.Actors[id].Team];
            double lim = pad.R + PadMargin;
            double dx = w.X[id] - pad.X;
            double dz = w.Z[id] - pad.Z;
            return dx * dx + dz * dz <= lim * lim;
        }

        /// <summary>A valid Kaiju target: alive, not spawn-protected, away from its own pad, and hostile.</summary>
        public static bool KaijuCanTarget(World w, int id)
        {
            if (!(id >= 0 && id < w.ActorCount)) return false;
            Actor a = w.Actors[id];
            if (!a.Alive || a.SpawnProtect > 0) return false;
            if (NearOwnPad(w, id)) return false;
            return KaijuHostileTo(w, id);
        }

        /// <summary>Damage the Kaiju from actor <paramref name="sourceId"/>; at 0 hp a KO for koSeconds ('kaijuKO'). Returns damage dealt.</summary>
        public static double DamageKaiju(World w, int sourceId, double amount) => DamageKaiju(w, sourceId, amount, DamageCause.None);

        /// <summary>b13-k: as above with the damage cause (DamageCause.Explosion for TNT: TIMBER's BARKSKIN reads it).</summary>
        public static double DamageKaiju(World w, int sourceId, double amount, DamageCause cause)
        {
            MatchState m = w.Match;
            Kaiju k = w.Kaiju;
            if (m.Phase == MatchPhase.Ended || !m.Hatched || k.State == KaijuState.Egg) return 0;
            if (k.KoIn > 0) return 0;
            if (!(amount > 0) || !double.IsFinite(amount)) return 0;
            if (!(sourceId >= 0 && sourceId < w.ActorCount)) return 0;
            Actor src = w.Actors[sourceId];
            if (!src.Alive) return 0;
            amount *= B13Hooks.DamageScale(w, sourceId, -1, DamageCause.None);   // b13-hook: target -1 = the Kaiju
            amount = KaijuTypes.TakenScale(w, amount, cause); // b13-k: BARKSKIN (the amount itself unless a fed TIMBER takes an explosion)
            if (k.State == KaijuState.Azure && src.Team != TeamId.Crimson) return 0;
            if (k.State == KaijuState.Crimson && src.Team != TeamId.Azure) return 0;
            if (k.State == KaijuState.Neutral) k.Threat[sourceId] = JsMath.F32(w.Config.Kaiju.NeutralThreatMemorySeconds);
            else if (Phys.On(w, w.Config.Physics.KaijuHillSiege)) w.KaijuNav.HitBy[sourceId] = w.Config.Physics.KaijuHitMemorySeconds; // fix/kaiju-hill-1
            k.Hp -= amount;
            KaijuTypes.OnDamaged(w); // b13-k: a hit restarts the REGROWTH wait
            // DG-16 (view request): who hit the Kaiju and where, for effects: the point on its hit radius toward the attacker.
            double hx = w.X[sourceId] - k.X;
            double hz = w.Z[sourceId] - k.Z;
            double hd = Math.Sqrt(hx * hx + hz * hz);
            double cr = w.Config.Kaiju.Radius;
            double cx = hd > 1e-9 ? k.X + hx / hd * cr : k.X;
            double cz = hd > 1e-9 ? k.Z + hz / hd * cr : k.Z;
            World.PushEvent(w, SimEventType.KaijuDamaged, sourceId, -1, cx, cz, amount);
            if (k.Hp <= 0)
            {
                k.Hp = 0;
                k.KoIn = w.Config.Kaiju.KoSeconds;
                k.TelegraphIn = 0;
                k.MeleeWindupIn = 0;
                k.MeleeTargetId = -1;
                k.TargetId = -1;
                World.PushEvent(w, SimEventType.KaijuKO, -1, sourceId, k.X, k.Z, 0);
            }
            return amount;
        }

        /// <summary>feat/kaiju-bridges-1: the bridge squeeze applies (it needs the DG-86 solid collider).</summary>
        internal static bool SqueezeOn(World w) =>
            Phys.On(w, w.Config.Physics.KaijuBridgeSqueeze) && Phys.On(w, w.Config.Physics.SolidKaiju);

        /// <summary>feat/kaiju-bridges-1: a Bridge cell lies within `halo` cells (Chebyshev) of (cx, cz).</summary>
        private static bool NearBridge(MapDef map, int cx, int cz, int halo)
        {
            for (int dz = -halo; dz <= halo; dz++)
                for (int dx = -halo; dx <= halo; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (MapGrid.InBounds(map, x, z) && map.Cells[z * map.Width + x] == Cell.Bridge) return true;
                }
            return false;
        }

        /// <summary>
        /// fix/kaiju-hill-1 round 3: MapGrid.ClearanceOk with barricade cells counted as walkable (the Kaiju crushes them; see
        /// Kaiju.CrushBarricades), so its plan goes through a barricade instead of treating the far side as another region.
        /// </summary>
        private static bool ClearanceThroughBarricades(MapDef map, int cx, int cz, double radius)
        {
            if (!PassableForKaiju(map, cx, cz)) return false;
            double x = cx + 0.5;
            double z = cz + 0.5;
            double r2 = radius * radius;
            for (int gz = JsMath.FloorToInt(z - radius); gz <= JsMath.FloorToInt(z + radius); gz++)
                for (int gx = JsMath.FloorToInt(x - radius); gx <= JsMath.FloorToInt(x + radius); gx++)
                {
                    if (PassableForKaiju(map, gx, gz)) continue;
                    double nx = x < gx ? gx : x > gx + 1 ? gx + 1 : x;
                    double nz = z < gz ? gz : z > gz + 1 ? gz + 1 : z;
                    if ((x - nx) * (x - nx) + (z - nz) * (z - nz) < r2) return false;
                }
            return true;
        }

        private static bool PassableForKaiju(MapDef map, int cx, int cz) =>
            MapGrid.IsWalkable(map, cx, cz) || (MapGrid.InBounds(map, cx, cz) && (map.Cells[cz * map.Width + cx] == Cell.Barricade || map.Cells[cz * map.Width + cx] == Cell.LowWall));   // b13-hook

        /// <summary>
        /// b11-dg: what the shadow grid was last built from (per World, allocated once, like Barricades' memo). Built = the real
        /// cells at that build; the rest are the inputs of ClearCellAt. Never part of the Sim state: it only lets a grid change
        /// recompute the cells near the changed ones instead of every cell.
        /// </summary>
        private sealed class ClearBuild
        {
            public byte[] Built;
            public int[] Changed;
            public int[] Mark;
            public int Stamp;
            public MapDef Map;
            public double R, Sr;
            public int Halo;
            public bool Squeeze, Crush, Guard, Valid;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, ClearBuild> ClearBuilds =
            new System.Runtime.CompilerServices.ConditionalWeakTable<World, ClearBuild>();

        /// <summary>b11-dg: more changed cells than this since the last build = one full rebuild (cheaper than many windows).</summary>
        internal const int ClearPatchMaxCells = 48;

        private static ClearBuild ClearBuildFor(World w)
        {
            int n = w.Map.Width * w.Map.Height;
            if (!ClearBuilds.TryGetValue(w, out ClearBuild b) || b.Built.Length != n)
            {
                if (b != null) ClearBuilds.Remove(w);
                b = new ClearBuild { Built = new byte[n], Changed = new int[ClearPatchMaxCells], Mark = new int[n] };
                ClearBuilds.Add(w, b);
            }
            return b;
        }

        /// <summary>
        /// One shadow cell (and its squeeze flag) from the real grid: the whole rule, shared by the full build and the patch so
        /// both give the same bytes. Returns true when the shadow cell's walkable value changed.
        /// </summary>
        private static bool ClearCellAt(World w, MapDef map, int cx, int cz, double r, bool crush, bool squeeze, double sr, int halo,
                                        byte[] cells, byte[] sq)
        {
            int i = cz * map.Width + cx;
            bool ok = crush ? ClearanceThroughBarricades(map, cx, cz, r) : MapGrid.ClearanceOk(map, cx, cz, r);
            // feat/kaiju-bridges-1: near a bridge, a cell its squeezed collider fits in is walkable for it too.
            // feat/kaiju-bridges-2: or the cell centre is on a diagonal bridge's deck for the squeezed body (the CM-33
            // deck rule the actors use): the raster stair-steps of the shallow bridges (base to hill, south island to the
            // far islands) leave no cell the 0.7 m circle fits by the box rule.
            bool sqz = !ok && squeeze && NearBridge(map, cx, cz, halo) &&
                       (MapGrid.ClearanceOk(map, cx, cz, sr) || Collide.DeckSpanAt(w, cx + 0.5, cz + 0.5, sr) != null);
            sq[i] = sqz ? (byte)1 : (byte)0;
            byte v = ok || sqz ? Cell.Floor : Cell.Wall;
            if (cells[i] == v) return false;
            cells[i] = v;
            return true;
        }

        /// <summary>
        /// b11-dg: how far (Chebyshev cells) a real-cell change can reach into ClearCellAt: the collider box (r), the bridge halo,
        /// the squeezed box (sr) and the diagonal-deck test (axis cells within BridgeHalfWidth + DeckPad + sr of the centre), each
        /// plus the half cell from the centre, plus 1 spare. A cell farther than this from every changed cell keeps its value.
        /// </summary>
        internal static int ClearReach(double r, double sr, int halo)
        {
            int box = (int)Math.Ceiling(Math.Max(r, sr) + 0.5);
            int deck = (int)Math.Ceiling(CriticalMass.Sim.Maps.SkybreakBasinMap.BridgeHalfWidth + Collide.DeckPad + sr + 0.5);
            return Math.Max(Math.Max(box, deck), halo) + 1;
        }

        /// <summary>
        /// Rebuild the shadow clearance grid when the real grid changed. b11-dg (frame cost): a change of a few cells (a barricade
        /// placed, crushed or expired) recomputes only the cells within ClearReach of them, and the regions only when a shadow
        /// cell actually changed (under the crush rule a barricade swap never changes one away from a bridge). Every shadow
        /// cell, squeeze flag and region label comes out byte-identical to a full rebuild (each is a pure function of the real
        /// grid within that reach, and BuildRegions a pure function of the shadow cells); the flow field is still invalidated on
        /// every grid change exactly as before, so no Sim decision or timer moves. SimProfile.ForceFullClearance = the old path.
        /// </summary>
        internal static void RefreshClearance(World w)
        {
            KaijuNav nav = w.KaijuNav;
            if (nav.ClearGridVersion == w.GridVersion) return;
            SimProfile.Begin?.Invoke(SimProfile.KaijuClearance);
            MapDef map = w.Map;
            byte[] cells = nav.Clear.Cells;
            byte[] real = map.Cells;
            int width = map.Width;
            int height = map.Height;
            double r = GridR(w);
            bool squeeze = SqueezeOn(w);
            bool guard = Phys.On(w, w.Config.Physics.KaijuNavGuard);
            bool crush = guard && Phys.On(w, w.Config.Physics.KaijuHillSiege);
            double sr = w.Config.Physics.KaijuSqueezeRadius;
            int halo = w.Config.Physics.KaijuSqueezeHalo;
            byte[] sq = nav.Squeeze;
            ClearBuild b = ClearBuildFor(w);
            bool same = b.Valid && nav.ClearGridVersion >= 0 && !SimProfile.ForceFullClearance && ReferenceEquals(b.Map, map) &&
                        b.R == r && b.Sr == sr && b.Halo == halo && b.Squeeze == squeeze && b.Crush == crush && b.Guard == guard;
            int changed = 0;
            if (same)
            {
                int n = width * height;
                byte[] built = b.Built;
                for (int i = 0; i < n; i++)
                {
                    if (built[i] == real[i]) continue;
                    if (changed == ClearPatchMaxCells)
                    {
                        same = false;
                        break;
                    }
                    b.Changed[changed++] = i;
                }
            }
            bool regions;
            if (same)
            {
                SimProfile.ClearancePatches++;
                if (b.Stamp == int.MaxValue)
                {
                    Array.Fill(b.Mark, 0);
                    b.Stamp = 0;
                }
                int stamp = ++b.Stamp;
                int[] mark = b.Mark;
                int reach = ClearReach(r, sr, halo);
                bool any = false;
                for (int c = 0; c < changed; c++)
                {
                    int ci = b.Changed[c];
                    int ccx = ci % width;
                    int ccz = ci / width;
                    int z0 = Math.Max(0, ccz - reach), z1 = Math.Min(height - 1, ccz + reach);
                    int x0 = Math.Max(0, ccx - reach), x1 = Math.Min(width - 1, ccx + reach);
                    for (int cz = z0; cz <= z1; cz++)
                        for (int cx = x0; cx <= x1; cx++)
                        {
                            int i = cz * width + cx;
                            if (mark[i] == stamp) continue;
                            mark[i] = stamp;
                            if (ClearCellAt(w, map, cx, cz, r, crush, squeeze, sr, halo, cells, sq)) any = true;
                        }
                    b.Built[ci] = real[ci];
                }
                regions = any;
            }
            else
            {
                SimProfile.ClearanceFullRebuilds++;
                for (int cz = 0; cz < height; cz++)
                    for (int cx = 0; cx < width; cx++)
                        ClearCellAt(w, map, cx, cz, r, crush, squeeze, sr, halo, cells, sq);
                Array.Copy(real, b.Built, real.Length);
                b.Map = map;
                b.R = r;
                b.Sr = sr;
                b.Halo = halo;
                b.Squeeze = squeeze;
                b.Crush = crush;
                b.Guard = guard;
                b.Valid = true;
                regions = true;
            }
            nav.ClearGridVersion = w.GridVersion;
            // The field was built over the old shadow grid: force a rebuild (on every grid change, as before b11-dg: the
            // rebuild resets RepathIn, so skipping it would move the Sim).
            nav.FieldWallVersion = -1;
            if (guard && regions)
            {
                SimProfile.ClearanceRegionBuilds++;
                BuildRegions(w);
            }
            SimProfile.End?.Invoke(SimProfile.KaijuClearance);
        }

        /// <summary>
        /// fix/movement-shiver-1: label the clearance cells by region (8-connected with the flow field's no-corner-cutting
        /// rule), so target choice can skip a fighter the body can never walk to (inside the shrine yard, on a diagonal bridge
        /// far from its mouth). Breadth-first over the preallocated queue; no allocation.
        /// </summary>
        internal static void BuildRegions(World w)
        {
            KaijuNav nav = w.KaijuNav;
            MapDef map = w.Map;
            int W = map.Width;
            int H = map.Height;
            int[] reg = nav.Region;
            int[] q = nav.RegionQueue;
            Array.Fill(reg, 0);
            int label = 0;
            for (int start = 0; start < W * H; start++)
            {
                if (reg[start] != 0 || !IsClear(w, start % W, start / W)) continue;
                label++;
                int head = 0;
                int tail = 0;
                q[tail++] = start;
                reg[start] = label;
                while (head < tail)
                {
                    int c = q[head++];
                    int cx = c % W;
                    int cz = c / W;
                    for (int i = 0; i < 8; i++)
                    {
                        int nx = cx + Ndx[i];
                        int nz = cz + Ndz[i];
                        if (!IsClear(w, nx, nz)) continue;
                        int ni = nz * W + nx;
                        if (reg[ni] != 0) continue;
                        if (i >= 4 && (!IsClear(w, nx, cz) || !IsClear(w, cx, nz))) continue;
                        reg[ni] = label;
                        q[tail++] = ni;
                    }
                }
            }
        }

        /// <summary>fix/movement-shiver-1: region label of the clearance cell nearest (x, z) within GoalSnapRings (0 = none).</summary>
        internal static int RegionAt(World w, double x, double z, out int cx, out int cz)
        {
            if (!NearestClearCell(w, x, z, GoalSnapRings, out cx, out cz)) return 0;
            return w.KaijuNav.Region[cz * w.Map.Width + cx];
        }

        /// <summary>
        /// fix/movement-shiver-1: true when the body can get to a cell from which actor id is in claw reach: the clearance cell
        /// nearest the actor is in the Kaiju's own region, its centre is within MeleeRange of the actor, and (with walls
        /// blocking hits) nothing blocks the line from it. A fighter on a narrow bridge or behind a wall end fails this.
        /// </summary>
        internal static bool BodyCanReach(World w, int id, int kaijuRegion, bool current = false)
        {
            int r = RegionAt(w, w.X[id], w.Z[id], out int cx, out int cz);
            if (r == 0 || (kaijuRegion != 0 && r != kaijuRegion)) return false;
            double ex = cx + 0.5 - w.X[id];
            double ez = cz + 0.5 - w.Z[id];
            double reach = w.Config.Kaiju.MeleeRange + (current ? 1 : 0); // the current target keeps a 1 u margin (no flicker)
            if (ex * ex + ez * ez > reach * reach) return false;
            return !Phys.On(w, w.Config.Physics.WallsBlockHits) || SegmentClear.Clear(w.Map, cx + 0.5, cz + 0.5, w.X[id], w.Z[id]);
        }

        internal static bool IsClear(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            return MapGrid.IsWalkableType(w.KaijuNav.Clear.Cells[cz * map.Width + cx]);
        }

        /// <summary>Nearest clearance cell to (x, z) by cell-centre distance over square rings 0..maxRings (first minimum wins); false when none.</summary>
        internal static bool NearestClearCell(World w, double x, double z, int maxRings, out int cx, out int cz)
        {
            cx = 0;
            cz = 0;
            int ox = JsMath.FloorToInt(x);
            int oz = JsMath.FloorToInt(z);
            for (int ring = 0; ring <= maxRings; ring++)
            {
                double best = double.PositiveInfinity;
                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                        int ccx = ox + dx;
                        int ccz = oz + dz;
                        if (!IsClear(w, ccx, ccz)) continue;
                        double ex = ccx + 0.5 - x;
                        double ez = ccz + 0.5 - z;
                        double d2 = ex * ex + ez * ez;
                        if (d2 < best)
                        {
                            best = d2;
                            cx = ccx;
                            cz = ccz;
                        }
                    }
                }
                if (best < double.PositiveInfinity) return true;
            }
            return false;
        }

        /// <summary>The one hatch code path (natural at hatchSeconds, or world.ForceHatch).</summary>
        internal static void Hatch(World w)
        {
            MatchState m = w.Match;
            Kaiju k = w.Kaiju;
            MapDef map = w.Map;
            RefreshClearance(w);
            KaijuState state = Corruption.AtHatch(w, ResolveHatch(m));   // b13-hook
            k.State = state;
            m.Hatched = true;
            w.ForceHatch = false;
            k.Hp = k.MaxHp;
            k.KoIn = 0;
            k.MeleeCd = 0;
            k.SlamCd = 0;
            k.TelegraphIn = 0;
            k.MeleeWindupIn = 0;
            k.MeleeTargetId = -1;
            k.TargetId = -1;
            Array.Fill(k.Threat, JsMath.F32(0));
            double x = map.Shrine.X;
            double z = map.Shrine.Z + HatchOffsetZ;
            if (NearestClearCell(w, x, z, HatchSearchRings, out int cx, out int cz))
            {
                x = cx + 0.5;
                z = cz + 0.5;
            }
            k.X = x;
            k.Z = z;
            k.PrevX = x;
            k.PrevZ = z;
            k.Facing = Math.PI / 2; // south, toward the hill
            KaijuNav nav = w.KaijuNav;
            nav.HomeX = x;
            nav.HomeZ = z;
            nav.RetargetIn = 0;
            nav.RepathIn = 0;
            nav.GoalCx = -1;
            nav.GoalCz = -1;
            nav.ProgressX = x;
            nav.ProgressZ = z;
            nav.NoProgress = 0;
            Array.Fill(nav.Ignore, 0.0);
            nav.SiegeCx = nav.SiegeCz = nav.StandCx = nav.StandCz = -1;
            nav.SiegeIn = 0;
            Array.Fill(nav.HitBy, 0.0);
            KaijuTypes.OnHatch(w);   // b13-hook
            World.PushEvent(w, SimEventType.Hatch, -1, -1, x, z, HatchCode(state));
        }

        /// <summary>The nearest valid target (berserk: biased toward the bigger biomass feeder); -1 when none.</summary>
        internal static int SelectTarget(World w)
        {
            Kaiju k = w.Kaiju;
            MatchState m = w.Match;
            int bigFeeder = -1; // 0 azure, 1 crimson, -1 none
            if (k.State == KaijuState.Berserk)
            {
                if (m.Delivered[(int)TeamId.Azure].Biomass > m.Delivered[(int)TeamId.Crimson].Biomass) bigFeeder = 0;
                else if (m.Delivered[(int)TeamId.Crimson].Biomass > m.Delivered[(int)TeamId.Azure].Biomass) bigFeeder = 1;
                bigFeeder = Corruption.WildTargetTeam(w, bigFeeder);   // b13-hook
            }
            int best = -1;
            double bestScore = double.PositiveInfinity;
            int n = w.ActorCount;
            // fix/movement-shiver-1: skip given-up targets and ones the body cannot get in claw reach of, prefer what it can
            // see, and keep the current target unless another is KaijuRetargetMargin closer (no flip-flop between two fighters).
            PhysicsConfig p = w.Config.Physics;
            bool guard = Phys.On(w, p.KaijuNavGuard);
            bool walls = guard && Phys.On(w, p.WallsBlockHits);
            int kReg = 0;
            if (guard)
            {
                RefreshClearance(w);
                kReg = RegionAt(w, k.X, k.Z, out _, out _);
            }
            bool hillRule = guard && Phys.On(w, p.KaijuHillSiege) && k.State != KaijuState.Berserk && k.State != KaijuState.Neutral;
            HillDef hill = w.Map.Hill;
            for (int id = 0; id < n; id++)
            {
                if (!KaijuCanTarget(w, id)) continue;
                if (guard && (w.KaijuNav.Ignore[id] > 0 || !BodyCanReach(w, id, kReg, id == k.TargetId))) continue;
                if (hillRule)
                {
                    // fix/kaiju-hill-1 round 3: fight on the hill; an attacker only within the leash.
                    double hx = w.X[id] - hill.X;
                    double hz = w.Z[id] - hill.Z;
                    double hd2 = hx * hx + hz * hz;
                    bool onHill = hd2 <= p.KaijuHillFightRadius * p.KaijuHillFightRadius;
                    bool attacker = w.KaijuNav.HitBy[id] > 0 && hd2 <= p.KaijuHillLeash * p.KaijuHillLeash;
                    if (!onHill && !attacker) continue;
                }
                double dx = w.X[id] - k.X;
                double dz = w.Z[id] - k.Z;
                double score = Math.Sqrt(dx * dx + dz * dz);
                if (bigFeeder >= 0 && (w.Actors[id].Team == TeamId.Azure ? 0 : 1) == bigFeeder) score -= BigFeederBias;
                if (walls && !SegmentClear.Clear(w.Map, k.X, k.Z, w.X[id], w.Z[id])) score += p.KaijuHiddenPenalty;
                if (guard && id == k.TargetId) score -= p.KaijuRetargetMargin;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = id;
                }
            }
            return best;
        }
    }
}
