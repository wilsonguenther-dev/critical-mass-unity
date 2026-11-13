using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>A step's baseline and running observations (one object per runner, reused for every step).</summary>
    public sealed class TrialProbe
    {
        /// <summary>Actor 0's position at the step's start (Move).</summary>
        public double StartX, StartZ;
        public int Kills;
        public double Delivered;
        public double HillSeconds;
        /// <summary>The ability cooldown at the last observation (UseAbility completes on a rise).</summary>
        public double LastAbilityCd;
        /// <summary>JumpGap: actor 0 has been seen alive over a gap cell during this step.</summary>
        public bool SawOverGap;
        /// <summary>A target fixed at step start (Marker, Gap): HasFixed false = none could be found.</summary>
        public bool HasFixed;
        public double FixedX, FixedZ;
        /// <summary>Gap only: the centre of the walkable cell to take off from, on the gap's axis.</summary>
        public double FromX, FromZ;
        /// <summary>The enemy the arrow follows (-1 = none) and the tick it was picked.</summary>
        public int EnemyId = -1;
        public int EnemyPickedTick = int.MinValue;

        public void Clear()
        {
            StartX = StartZ = 0;
            Kills = 0;
            Delivered = 0;
            HillSeconds = 0;
            LastAbilityCd = 0;
            SawOverGap = false;
            HasFixed = false;
            FixedX = FixedZ = 0;
            FromX = FromZ = 0;
            EnemyId = -1;
            EnemyPickedTick = int.MinValue;
        }
    }

    /// <summary>
    /// The guided match's completion checks and arrow targets, all read from sim state (World), never written. Pure C#,
    /// allocation-free after the cell snapshot is sized, so EditMode tests drive each check on a real world.
    /// </summary>
    public static class TrialChecks
    {
        /// <summary>The human is always actor 0 (HudBinder.LocalActor).</summary>
        public const int Player = 0;
        /// <summary>Ticks between two picks of the enemy the arrow follows (no flicker between two close enemies).</summary>
        public const int EnemyRepickTicks = 15;
        /// <summary>Health weight of the "weakest enemy" score, in world units per full health bar.</summary>
        public const double WeakHealthWeight = 8;
        /// <summary>How far from the requested point a marker may be moved to find clear ground (cells).</summary>
        public const int MarkerSearchCells = 8;

        /// <summary>Records the step's baseline from the world as it is now. snapshot: map.Cells is copied into it.</summary>
        public static void Begin(World w, TrialStep s, TrialProbe p, byte[] snapshot)
        {
            p.Clear();
            Actor a = w.Actors[Player];
            p.StartX = w.X[Player];
            p.StartZ = w.Z[Player];
            p.Kills = a.Kills;
            p.Delivered = w.Match.Delivered[(int)a.Team].Biomass;
            p.HillSeconds = a.HillSeconds;
            p.LastAbilityCd = a.AbilityCd;
            if (snapshot != null && snapshot.Length == w.Map.Cells.Length) Buffer.BlockCopy(w.Map.Cells, 0, snapshot, 0, snapshot.Length);
            if (s.Target == TrialTarget.Marker)
                p.HasFixed = MarkerPoint(w, a.Team, s.MarkerFraction, out p.FixedX, out p.FixedZ);
            else if (s.Target == TrialTarget.Gap)
                p.HasFixed = NearestGap(w, w.X[Player], w.Z[Player], JumpReachCells(w), out p.FixedX, out p.FixedZ, out p.FromX, out p.FromZ);
        }

        /// <summary>Observes the world once (call once per sim tick or per frame) and returns true when the step is done.</summary>
        public static bool Observe(World w, TrialStep s, TrialProbe p, byte[] snapshot)
        {
            Actor a = w.Actors[Player];
            switch (s.Goal)
            {
                case TrialGoal.Move:
                    return a.Alive && Dist2(w.X[Player], w.Z[Player], p.StartX, p.StartZ) >= s.Param * s.Param;
                case TrialGoal.ReachMarker:
                    return a.Alive && p.HasFixed && Dist2(w.X[Player], w.Z[Player], p.FixedX, p.FixedZ) <= s.Param * s.Param;
                case TrialGoal.KnockOut:
                    return a.Kills > p.Kills;
                case TrialGoal.GatherBiomass:
                    return a.Alive && a.Carry == CarryKind.Biomass && a.CarryAmount > 0;
                case TrialGoal.DeliverBiomass:
                    return w.Match.Delivered[(int)a.Team].Biomass > p.Delivered + 1e-9;
                case TrialGoal.HoldHill:
                    return a.HillSeconds - p.HillSeconds >= s.Param - 1e-6;
                case TrialGoal.UseAbility:
                {
                    bool rose = a.AbilityCd > p.LastAbilityCd + 1e-9;
                    p.LastAbilityCd = a.AbilityCd;
                    return rose;
                }
                case TrialGoal.JumpGap:
                {
                    if (!a.Alive)
                    {
                        p.SawOverGap = false;
                        return false;
                    }
                    bool over = IsJumpGapCell(MapGrid.CellAt(w.Map, w.X[Player], w.Z[Player]));
                    if (over)
                    {
                        p.SawOverGap = true;
                        return false;
                    }
                    return p.SawOverGap && MapGrid.IsWalkableType(MapGrid.CellAt(w.Map, w.X[Player], w.Z[Player]));
                }
                case TrialGoal.Dig:
                    return a.Alive && ChangedNear(w, snapshot, s.Param, true);
                case TrialGoal.Place:
                    return a.Alive && ChangedNear(w, snapshot, s.Param, false);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Why a step cannot run in this world right now ("" = it can). Checked when the step starts; a non-empty reason
        /// skips it with a log line, the same way a missing mechanic does.
        /// </summary>
        public static string CannotRunReason(World w, TrialStep s, TrialProbe p)
        {
            Actor a = w.Actors[Player];
            if (s.OnlyClass.HasValue && a.Cls != s.OnlyClass.Value)
                return "it is for the " + s.OnlyClass.Value + " and the player is a " + a.Cls;
            if (s.Goal == TrialGoal.DeliverBiomass && (w.Match.Hatched || w.Kaiju.State != KaijuState.Egg))
                return "the egg has hatched, so it takes no more deliveries";
            if ((s.Target == TrialTarget.Marker || s.Target == TrialTarget.Gap) && !p.HasFixed)
                return s.Target == TrialTarget.Gap ? "no gap within jump reach was found on this map" : "no clear ground for the marker";
            if (s.Goal == TrialGoal.GatherBiomass && !HasBiomassNode(w)) return "no biomass node exists on this map";
            return "";
        }

        /// <summary>Where the step's arrow points now. False = nothing to point at this frame (arrow hidden).</summary>
        public static bool Target(World w, TrialStep s, TrialProbe p, out double x, out double z)
        {
            x = z = 0;
            switch (s.Target)
            {
                case TrialTarget.Marker:
                case TrialTarget.Gap:
                    x = p.FixedX;
                    z = p.FixedZ;
                    return p.HasFixed;
                case TrialTarget.WeakestEnemy:
                case TrialTarget.NearestEnemy:
                {
                    int id = TrackEnemy(w, p, s.Target == TrialTarget.WeakestEnemy);
                    if (id < 0) return false;
                    x = w.X[id];
                    z = w.Z[id];
                    return true;
                }
                case TrialTarget.BiomassNode:
                {
                    int ni = NearestBiomassNode(w, w.X[Player], w.Z[Player]);
                    if (ni < 0) return false;
                    x = w.Nodes[ni].X;
                    z = w.Nodes[ni].Z;
                    return true;
                }
                case TrialTarget.Shrine:
                    x = w.Map.Shrine.X;
                    z = w.Map.Shrine.Z;
                    return true;
                case TrialTarget.Hill:
                    x = w.Map.Hill.X;
                    z = w.Map.Hill.Z;
                    return true;
                case TrialTarget.Self:
                    x = w.X[Player];
                    z = w.Z[Player];
                    return w.Actors[Player].Alive;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- helpers

        public static double Dist2(double ax, double az, double bx, double bz)
        {
            double dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz;
        }

        /// <summary>Void and water are the gaps a jump crosses or a place fills.</summary>
        public static bool IsGapCell(int cellType) => cellType == Cell.Void || cellType == Cell.Water;

        /// <summary>A gap a jump can clear: in-map VOID only (CM-32: airborne actors pass over void, never over water).</summary>
        public static bool IsJumpGapCell(int cellType) => cellType == Cell.Void;

        /// <summary>The enemy the arrow follows, kept for EnemyRepickTicks unless it dies (-1 = no living enemy).</summary>
        public static int TrackEnemy(World w, TrialProbe p, bool weakest)
        {
            bool keep = p.EnemyId >= 0 && p.EnemyId < w.ActorCount && w.Actors[p.EnemyId].Alive &&
                w.Actors[p.EnemyId].Team != w.Actors[Player].Team && w.Tick - p.EnemyPickedTick < EnemyRepickTicks &&
                w.Tick >= p.EnemyPickedTick;
            if (keep) return p.EnemyId;
            p.EnemyId = PickEnemy(w, weakest);
            p.EnemyPickedTick = w.Tick;
            return p.EnemyId;
        }

        /// <summary>The living enemy with the lowest score: distance, plus WeakHealthWeight x health share when weakest.</summary>
        public static int PickEnemy(World w, bool weakest)
        {
            TeamId team = w.Actors[Player].Team;
            double px = w.X[Player], pz = w.Z[Player];
            int best = -1;
            double bestScore = double.PositiveInfinity;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor e = w.Actors[i];
                if (!e.Alive || e.Team == team) continue;
                double score = Math.Sqrt(Dist2(px, pz, w.X[i], w.Z[i]));
                if (weakest && e.MaxHp > 0) score += WeakHealthWeight * (e.Hp / e.MaxHp);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        public static bool HasBiomassNode(World w)
        {
            for (int i = 0; i < w.Nodes.Length; i++)
                if (w.Nodes[i].Kind == NodeKind.Biomass) return true;
            return false;
        }

        /// <summary>The nearest biomass node with charges left, else the nearest biomass node (it regrows), else -1.</summary>
        public static int NearestBiomassNode(World w, double x, double z)
        {
            int best = -1, bestEmpty = -1;
            double bd = double.PositiveInfinity, be = double.PositiveInfinity;
            for (int i = 0; i < w.Nodes.Length; i++)
            {
                ResourceNode n = w.Nodes[i];
                if (n.Kind != NodeKind.Biomass) continue;
                double d = Dist2(x, z, n.X, n.Z);
                if (n.Charges > 0)
                {
                    if (d < bd)
                    {
                        bd = d;
                        best = i;
                    }
                }
                else if (d < be)
                {
                    be = d;
                    bestEmpty = i;
                }
            }
            return best >= 0 ? best : bestEmpty;
        }

        /// <summary>
        /// A cell centre on clear walkable ground near pad + fraction x (hill - pad) of the team's pad; false when there is
        /// none within MarkerSearchCells.
        /// </summary>
        public static bool MarkerPoint(World w, TeamId team, double fraction, out double x, out double z)
        {
            MapDef map = w.Map;
            PadDef pad = map.Pads[(int)team];
            double tx = pad.X + (map.Hill.X - pad.X) * fraction;
            double tz = pad.Z + (map.Hill.Z - pad.Z) * fraction;
            return NearestClearCell(map, tx, tz, MarkerSearchCells, out x, out z);
        }

        /// <summary>The centre of the nearest cell to (tx, tz) with ClearanceOk at 0.5 units, searching rings outward.</summary>
        public static bool NearestClearCell(MapDef map, double tx, double tz, int maxRing, out double x, out double z)
        {
            int cx0 = (int)Math.Floor(tx), cz0 = (int)Math.Floor(tz);
            for (int r = 0; r <= maxRing; r++)
            {
                double best = double.PositiveInfinity;
                int bx = 0, bz = 0;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                        int cx = cx0 + dx, cz = cz0 + dz;
                        if (!MapGrid.ClearanceOk(map, cx, cz, 0.5)) continue;
                        double d = Dist2(cx + 0.5, cz + 0.5, tx, tz);
                        if (d < best)
                        {
                            best = d;
                            bx = cx;
                            bz = cz;
                        }
                    }
                }
                if (best < double.PositiveInfinity)
                {
                    x = bx + 0.5;
                    z = bz + 0.5;
                    return true;
                }
            }
            x = z = 0;
            return false;
        }

        /// <summary>Cells a full jump of the player's class covers: floor(airTicks / simHz x moveSpeed), at least 1.</summary>
        public static int JumpReachCells(World w)
        {
            GameConfig c = w.Config;
            ClassConfig cc = c.Class(w.Actors[Player].Cls);
            // A gap is cleared only when the footprint lands clear of it (the landing guard): the flight must cover the gap
            // plus a body width (2.28 u of flight for a Warrior clears 1 cell, not 2).
            double reach = c.Jump.AirTicks / c.SimHz * cc.MoveSpeed - 2 * cc.Radius;
            return Math.Max(1, (int)Math.Floor(reach));
        }

        static readonly PointXZ[] _reach = new PointXZ[1];
        static FlowField _reachField;

        static readonly int[] DirX = { 1, -1, 0, 0 };
        static readonly int[] DirZ = { 0, 0, 1, -1 };

        /// <summary>
        /// The middle of the nearest straight run of 1..maxSpan gap cells with walkable ground on both ends (a gap a jump
        /// clears), measured from (x, z). False when the map has none.
        /// </summary>
        public static bool NearestGap(World w, double x, double z, int maxSpan, out double gx, out double gz) =>
            NearestGap(w, x, z, maxSpan, out gx, out gz, out _, out _);

        /// <summary>NearestGap, also giving the takeoff cell's centre (fx, fz): the jump crosses along the gap's axis.</summary>
        public static bool NearestGap(World w, double x, double z, int maxSpan, out double gx, out double gz, out double fx, out double fz)
        {
            fx = fz = 0;
            MapDef map = w.Map;
            // Only takeoff cells the player can walk to (one flow field from the player, at step start only): the
            // nearest gap in a straight line may sit on ground reachable only across another gap.
            _reach[0] = new PointXZ(x, z);
            _reachField = FlowField.ComputeFlowField(map, _reach, _reachField);
            double[] dist = _reachField.Dist;
            double best = double.PositiveInfinity;
            gx = gz = 0;
            for (int cz = 0; cz < map.Height; cz++)
            {
                for (int cx = 0; cx < map.Width; cx++)
                {
                    if (!MapGrid.IsWalkable(map, cx, cz)) continue;
                    if (dist != null && double.IsPositiveInfinity(dist[cz * map.Width + cx])) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        for (int span = 1; span <= maxSpan; span++)
                        {
                            int ex = cx + DirX[d] * (span + 1), ez = cz + DirZ[d] * (span + 1);
                            if (!MapGrid.InBounds(map, ex, ez)) break;
                            int mx = cx + DirX[d] * span, mz = cz + DirZ[d] * span;
                            if (!IsJumpGapCell(map.Cells[mz * map.Width + mx])) break;
                            if (!MapGrid.IsWalkable(map, ex, ez)) continue;
                            double mxw = cx + 0.5 + DirX[d] * (span + 1) * 0.5, mzw = cz + 0.5 + DirZ[d] * (span + 1) * 0.5;
                            double dd = Dist2(x, z, mxw, mzw);
                            if (dd < best)
                            {
                                best = dd;
                                gx = mxw;
                                gz = mzw;
                                fx = cx + 0.5;
                                fz = cz + 0.5;
                            }
                            break;
                        }
                    }
                }
            }
            return best < double.PositiveInfinity;
        }

        /// <summary>
        /// True when a cell within radius of actor 0 changed since the snapshot: walkable to gap (dig = true) or gap to
        /// walkable (dig = false).
        /// </summary>
        public static bool ChangedNear(World w, byte[] snapshot, double radius, bool dig)
        {
            MapDef map = w.Map;
            if (snapshot == null || snapshot.Length != map.Cells.Length) return false;
            double px = w.X[Player], pz = w.Z[Player];
            int x0 = (int)Math.Floor(px - radius), x1 = (int)Math.Floor(px + radius);
            int z0 = (int)Math.Floor(pz - radius), z1 = (int)Math.Floor(pz + radius);
            double r2 = radius * radius;
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (!MapGrid.InBounds(map, cx, cz)) continue;
                    if (Dist2(cx + 0.5, cz + 0.5, px, pz) > r2) continue;
                    int i = cz * map.Width + cx;
                    byte was = snapshot[i], now = map.Cells[i];
                    if (was == now) continue;
                    if (dig && MapGrid.IsWalkableType(was) && IsGapCell(now)) return true;
                    if (!dig && IsGapCell(was) && MapGrid.IsWalkableType(now)) return true;
                }
            }
            return false;
        }
    }
}
