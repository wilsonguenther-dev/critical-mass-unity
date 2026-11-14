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
        public static double DamageKaiju(World w, int sourceId, double amount)
        {
            MatchState m = w.Match;
            Kaiju k = w.Kaiju;
            if (m.Phase == MatchPhase.Ended || !m.Hatched || k.State == KaijuState.Egg) return 0;
            if (k.KoIn > 0) return 0;
            if (!(amount > 0) || !double.IsFinite(amount)) return 0;
            if (!(sourceId >= 0 && sourceId < w.ActorCount)) return 0;
            Actor src = w.Actors[sourceId];
            if (!src.Alive) return 0;
            if (k.State == KaijuState.Azure && src.Team != TeamId.Crimson) return 0;
            if (k.State == KaijuState.Crimson && src.Team != TeamId.Azure) return 0;
            if (k.State == KaijuState.Neutral) k.Threat[sourceId] = JsMath.F32(w.Config.Kaiju.NeutralThreatMemorySeconds);
            k.Hp -= amount;
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

        /// <summary>Rebuild the shadow clearance grid when the real grid changed.</summary>
        internal static void RefreshClearance(World w)
        {
            KaijuNav nav = w.KaijuNav;
            if (nav.ClearGridVersion == w.GridVersion) return;
            MapDef map = w.Map;
            byte[] cells = nav.Clear.Cells;
            int width = map.Width;
            int height = map.Height;
            double r = GridR(w);
            bool squeeze = SqueezeOn(w);
            double sr = w.Config.Physics.KaijuSqueezeRadius;
            int halo = w.Config.Physics.KaijuSqueezeHalo;
            byte[] sq = nav.Squeeze;
            for (int cz = 0; cz < height; cz++)
                for (int cx = 0; cx < width; cx++)
                {
                    int i = cz * width + cx;
                    bool ok = MapGrid.ClearanceOk(map, cx, cz, r);
                    // feat/kaiju-bridges-1: near a bridge, a cell its squeezed collider fits in is walkable for it too.
                    // feat/kaiju-bridges-2: or the cell centre is on a diagonal bridge's deck for the squeezed body (the CM-33
                    // deck rule the actors use): the raster stair-steps of the shallow bridges (base to hill, south island to the
                    // far islands) leave no cell the 0.7 m circle fits by the box rule.
                    bool sqz = !ok && squeeze && NearBridge(map, cx, cz, halo) &&
                               (MapGrid.ClearanceOk(map, cx, cz, sr) || Collide.DeckSpanAt(w, cx + 0.5, cz + 0.5, sr) != null);
                    sq[i] = sqz ? (byte)1 : (byte)0;
                    cells[i] = ok || sqz ? Cell.Floor : Cell.Wall;
                }
            nav.ClearGridVersion = w.GridVersion;
            // The field was built over the old shadow grid: force a rebuild.
            nav.FieldWallVersion = -1;
            if (Phys.On(w, w.Config.Physics.KaijuNavGuard)) BuildRegions(w);
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
            KaijuState state = ResolveHatch(m);
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
            for (int id = 0; id < n; id++)
            {
                if (!KaijuCanTarget(w, id)) continue;
                if (guard && (w.KaijuNav.Ignore[id] > 0 || !BodyCanReach(w, id, kReg, id == k.TargetId))) continue;
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
