using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// Port of src/bots/steer.ts (09e0904), CM-09 steering: turns a bot's FSM state into one InputFrame per sim step.
    /// Move: flow-field step on the state's field (a chase uses the direct vector when a grid DDA line of sight is clear),
    /// plus separation from teammates within SeparationRadius; normalised. Barricades: a DDA probe BarricadeProbe cells
    /// ahead; the bot sidesteps (+/-45, +/-90 degrees, side by id parity) and chips an enemy barricade in reach.
    /// Aim: the target, or AimAhead units ahead along the move; NaN (no aim) when idle. Attack: target within class
    /// attackRange + AttackSlack (+ the Kaiju radius) and within +/-60 degrees of facing; a miner on the miner-hill field
    /// swings at a BREAKABLE next path cell within MinerReach. CM-21 (2): bilinear blend of the 4 surrounding cells' flow
    /// directions, and a turn-rate limit of Bots.TurnRateDegPerSec from the sim velocity heading while the bot follows a
    /// field at >= TurnLimitFraction of its moveSpeed. Allocation-free: module-scope scratch only.
    /// SteerBot never writes Jump or Terraform (fix-cm3-1: BotBrain.FillBotInput clears both).
    /// </summary>
    public static class BotSteer
    {
        public const double SeparationRadius = 1.2, SeparationWeight = 0.5, AttackSlack = 0.3, BarricadeProbe = 2, TurnLimitFraction = 0.25;

        /// <summary>+/-60 degrees (browser ATTACK_COS).</summary>
        public static readonly double AttackCos = JsMath.Cos(Math.PI / 3);

        /// <summary>Browser MINER_REACH.</summary>
        public static double MinerReach(GameConfig c) => c.Abilities.MinerWallBreak.Reach;

        /// <summary>Largest turn per sim step (radians): turnRateDegPerSec / simHz = 10 degrees at 300 deg/s and 30 Hz (browser TURN_STEP).</summary>
        public static double TurnStep(GameConfig c) => (c.Bots.TurnRateDegPerSec / c.SimHz) * Math.PI / 180;

        private const double InteractMargin = 0.35;
        private const double AimAhead = 3;
        /// <summary>A blend shorter than this (of the summed weight) means the 4 directions cancel out: keep the plain step.</summary>
        private const double BlendMin = 0.25;

        private struct MoveVec
        {
            public double X;
            public double Z;
        }

        private static FlowStep Fs;
        /// <summary>Scratch for the per-cell steps of the bilinear blend.</summary>
        private static FlowStep FsCell;
        /// <summary>
        /// True when this SteerBot call's move came from a flow-field step that found a next cell (the turn-rate limit
        /// applies to those only: the goal fallbacks and fights steer unlimited).
        /// </summary>
        private static bool Followed;
        /// <summary>Module-scope scratch of QueryCircle results (browser Int32Array(ACTOR_CAPACITY)).</summary>
        private static int[] Mates = new int[0];
        private static MoveVec Mv;
        private static readonly double[] RotCos = { Math.Sqrt(0.5), Math.Sqrt(0.5), 0, 0 }; // Math.SQRT1_2
        private static readonly double[] RotSin = { Math.Sqrt(0.5), -Math.Sqrt(0.5), 1, -1 };

        /// <summary>Browser TURN_COS (computed from config at the use site).</summary>
        private static double TurnCos(GameConfig c) => JsMath.Cos(TurnStep(c));

        /// <summary>Browser TURN_SIN (computed from config at the use site).</summary>
        private static double TurnSin(GameConfig c) => JsMath.Sin(TurnStep(c));

        /// <summary>
        /// DDA grid walk from (x0, z0) toward (x1, z1). Returns the index of the first non-walkable cell crossed, or -1
        /// when every cell up to the end point is walkable. The start cell is not tested.
        /// </summary>
        public static int FirstBlockedCell(MapDef map, double x0, double z0, double x1, double z1)
        {
            int W = map.Width;
            int H = map.Height;
            int cx = JsMath.FloorToInt(x0);
            int cz = JsMath.FloorToInt(z0);
            int tx = JsMath.FloorToInt(x1);
            int tz = JsMath.FloorToInt(z1);
            double dx = x1 - x0;
            double dz = z1 - z0;
            int sx = dx > 0 ? 1 : -1;
            int sz = dz > 0 ? 1 : -1;
            double tdx = dx != 0 ? Math.Abs(1 / dx) : double.PositiveInfinity;
            double tdz = dz != 0 ? Math.Abs(1 / dz) : double.PositiveInfinity;
            double tmx = dx > 0 ? (cx + 1 - x0) * tdx : dx < 0 ? (x0 - cx) * tdx : double.PositiveInfinity;
            double tmz = dz > 0 ? (cz + 1 - z0) * tdz : dz < 0 ? (z0 - cz) * tdz : double.PositiveInfinity;
            for (int guard = 0; guard < 512; guard++)
            {
                if (cx == tx && cz == tz) return -1;
                if (tmx < tmz)
                {
                    cx += sx;
                    tmx += tdx;
                }
                else
                {
                    cz += sz;
                    tmz += tdz;
                }
                if (cx < 0 || cz < 0 || cx >= W || cz >= H) return cz * W + cx;
                int i = cz * W + cx;
                if (!MapGrid.IsWalkableType(map.Cells[i])) return i;
            }
            return -1;
        }

        private static int TeamPadField(World w, int id)
        {
            return w.Actors[id].Team == TeamId.Azure ? BotFields.FieldPadAzure : BotFields.FieldPadCrimson;
        }

        /// <summary>
        /// Every steer path starts from a neutral frame. The browser also clears jump here (fix-cm3-1); in the port
        /// BotBrain.FillBotInput clears Jump and Terraform, so SteerBot writes neither.
        /// </summary>
        private static void Blank(InputFrame output)
        {
            output.MoveX = 0;
            output.MoveZ = 0;
            output.AimX = double.NaN;
            output.AimZ = double.NaN;
            output.Attack = false;
            output.Ability = false;
            output.Interact = false;
            output.ClassPick = -1;
        }

        /// <summary>
        /// CM-21 (2): bilinear blend of the flow directions of the 4 cells whose centres surround (x, z), into Mv. A cell
        /// counts when the field reaches it (finite distance) and it is not a goal cell; its direction is the plain flow
        /// step from its centre. The weights of the cells that count are renormalised. Returns false (Mv untouched) when
        /// no cell counts or the directions cancel out. Allocation-free.
        /// </summary>
        private static bool BlendFlow(BotsState st, World w, int field, double x, double z)
        {
            BotFields f = st.Fields;
            int W = f.Width;
            int H = f.Height;
            double[] dist = f.Dist[field];
            double gx = x - 0.5;
            double gz = z - 0.5;
            int cx0 = JsMath.FloorToInt(gx);
            int cz0 = JsMath.FloorToInt(gz);
            double fx = gx - cx0;
            double fz = gz - cz0;
            double bx = 0;
            double bz = 0;
            double wsum = 0;
            for (int k = 0; k < 4; k++)
            {
                int cx = cx0 + (k & 1);
                int cz = cz0 + (k >> 1);
                if (cx < 0 || cz < 0 || cx >= W || cz >= H) continue;
                double d = dist[cz * W + cx];
                if (!(d > 0 && d < double.PositiveInfinity)) continue;
                double wt = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fz : 1 - fz);
                if (wt <= 0) continue;
                if (!BotFields.FlowStepAt(f, w, field, cx + 0.5, cz + 0.5, out FsCell)) continue;
                bx += FsCell.X * wt;
                bz += FsCell.Z * wt;
                wsum += wt;
            }
            if (wsum < 1e-6) return false;
            double len = Math.Sqrt(bx * bx + bz * bz);
            if (len < BlendMin * wsum) return false;
            Mv.X = bx / len;
            Mv.Z = bz / len;
            return true;
        }

        /// <summary>
        /// Follow field `field` from the bot's position into Mv; returns false at the goal. Sets Fs.Cell (the plain step's
        /// next cell, which the miner's breakable-wall check reads). The direction is the bilinear blend (CM-21 (2)) when
        /// it exists, else the plain step toward the next cell's centre.
        /// </summary>
        private static bool Follow(World w, BotsState st, int id, int field)
        {
            bool ok = BotFields.FlowStepAt(st.Fields, w, field, w.X[id], w.Z[id], out Fs);
            Followed = ok;
            Mv.X = Fs.X;
            Mv.Z = Fs.Z;
            if (ok) BlendFlow(st, w, field, w.X[id], w.Z[id]);
            return ok;
        }

        private static bool Toward(World w, int id, double tx, double tz, double stopDist)
        {
            double dx = tx - w.X[id];
            double dz = tz - w.Z[id];
            double d2 = dx * dx + dz * dz;
            if (d2 <= stopDist * stopDist || d2 < 1e-12)
            {
                Mv.X = 0;
                Mv.Z = 0;
                return false;
            }
            double inv = 1 / Math.Sqrt(d2);
            Mv.X = dx * inv;
            Mv.Z = dz * inv;
            return true;
        }

        private static bool InArc(World w, int id, double tx, double tz)
        {
            double dx = tx - w.X[id];
            double dz = tz - w.Z[id];
            double d2 = dx * dx + dz * dz;
            if (d2 < 1e-12) return true;
            double f = w.Actors[id].Facing;
            return (dx * JsMath.Cos(f) + dz * JsMath.Sin(f)) / Math.Sqrt(d2) >= AttackCos - 1e-9;
        }

        /// <summary>Steer bot `id` (state in actor.AiState and st) into `output`.</summary>
        public static void SteerBot(World w, BotsState st, int id, InputFrame output)
        {
            Blank(output);
            st.WantMove[id] = 0;
            Fs.Cell = -1;
            Followed = false;
            if (!(id >= 0 && id < w.ActorCount)) return;
            Actor a = w.Actors[id];
            if (!a.Alive || w.Match.Phase == MatchPhase.Ended) return;
            GameConfig cfg = w.Config;
            MapDef map = w.Map;
            double x = w.X[id];
            double z = w.Z[id];
            ClassConfig cc = cfg.Class(a.Cls);
            Mv.X = 0;
            Mv.Z = 0;
            double aimX = double.NaN;
            double aimZ = double.NaN;
            bool separate = true;
            bool minerField = false;
            int capacity = World.ActorCapacity(cfg);
            if (Mates.Length < capacity) Mates = new int[capacity];
            int[] mates = Mates;

            switch (a.AiState)
            {
                case AiState.Unstick:
                    Toward(w, id, st.UnstickX[id], st.UnstickZ[id], 0.2);
                    break;
                case AiState.HoldHill:
                    Toward(w, id, st.PostX[id], st.PostZ[id], 0.5);
                    break;
                case AiState.Fight:
                {
                    int t = st.Target[id];
                    double tx = 0;
                    double tz = 0;
                    double reach = 0;
                    bool valid = false;
                    if (t == BotsState.TargetKaiju)
                    {
                        Kaiju k = w.Kaiju;
                        valid = k.State != KaijuState.Egg && k.KoIn <= 0;
                        tx = k.X;
                        tz = k.Z;
                        reach = cc.AttackRange + cfg.Kaiju.Radius + AttackSlack;
                    }
                    else if (t >= 0 && t < w.ActorCount && w.Actors[t].Alive && !BotFsm.PassiveTarget(w, t))
                    {
                        valid = true;
                        tx = w.X[t];
                        tz = w.Z[t];
                        reach = cc.AttackRange + AttackSlack;
                    }
                    if (!valid)
                    {
                        minerField = a.Cls == ClassId.Miner;
                        Follow(w, st, id, minerField ? BotFields.FieldMinerHill : BotFields.FieldHill);
                        break;
                    }
                    aimX = tx;
                    aimZ = tz;
                    double dx = tx - x;
                    double dz = tz - z;
                    double d2 = dx * dx + dz * dz;
                    if (d2 <= reach * reach)
                    {
                        output.Attack = InArc(w, id, tx, tz);
                        // Inside reach: circle-strafe (side by id parity) with a small pull to keep the distance.
                        double d = Math.Sqrt(d2);
                        if (d == 0 || double.IsNaN(d)) d = 1; // browser `Math.sqrt(d2) || 1`
                        double side = (id & 1) == 0 ? 1 : -1;
                        double want = reach * 0.7;
                        double pull = (d - want) / reach;
                        Mv.X = (-dz / d) * side * 0.6 + (dx / d) * pull;
                        Mv.Z = (dx / d) * side * 0.6 + (dz / d) * pull;
                    }
                    else if (FirstBlockedCell(map, x, z, tx, tz) < 0)
                    {
                        Toward(w, id, tx, tz, 0);
                    }
                    else
                    {
                        Follow(w, st, id, st.Role[id] == BotsState.RoleKaiju ? BotFields.FieldShrine : BotFields.FieldHill);
                    }
                    break;
                }
                case AiState.Gather:
                case AiState.Corrupt:
                {
                    int ni = st.TargetNode[id];
                    if (ni < 0 || ni >= w.Nodes.Length) break;
                    ResourceNode node = w.Nodes[ni];
                    aimX = node.X;
                    aimZ = node.Z;
                    double reach = cfg.Resources.NodeInteractRange + cc.Radius - InteractMargin;
                    double dx = node.X - x;
                    double dz = node.Z - z;
                    if (dx * dx + dz * dz <= reach * reach)
                    {
                        output.Interact = true;
                        separate = false;
                    }
                    else
                    {
                        int field = st.Fields.FieldOfNode[ni];
                        if (!Follow(w, st, id, field < 0 ? BotFields.FieldCorruption : field)) Toward(w, id, node.X, node.Z, 0);
                    }
                    break;
                }
                case AiState.Deliver:
                {
                    ShrineDef sh = map.Shrine;
                    aimX = sh.X;
                    aimZ = sh.Z;
                    double reach = cfg.KaijuFeed.ShrineInteractRange - InteractMargin;
                    double dx = sh.X - x;
                    double dz = sh.Z - z;
                    if (dx * dx + dz * dz <= reach * reach)
                    {
                        output.Interact = true;
                        separate = false;
                    }
                    else if (!Follow(w, st, id, BotFields.FieldShrine))
                    {
                        Toward(w, id, sh.X, sh.Z, 0);
                    }
                    break;
                }
                case AiState.Cleanse:
                {
                    // CM-36: walk to the shrine empty-handed, then hold interact; let go for a tick whenever the sim says this
                    // bot is not armed (a press still held from a delivery does not count).
                    ShrineDef sh = map.Shrine;
                    aimX = sh.X;
                    aimZ = sh.Z;
                    double reach = cfg.KaijuFeed.ShrineInteractRange - InteractMargin;
                    double dx = sh.X - x;
                    double dz = sh.Z - z;
                    if (dx * dx + dz * dz <= reach * reach)
                    {
                        output.Interact = w.Cleanse.Armed[id] == 1;
                        separate = false;
                    }
                    else if (!Follow(w, st, id, BotFields.FieldShrine))
                    {
                        Toward(w, id, sh.X, sh.Z, 0);
                    }
                    break;
                }
                case AiState.Retreat:
                    Follow(w, st, id, TeamPadField(w, id));
                    break;
                default:
                {
                    // toHill (and 'none' before the first think).
                    minerField = a.Cls == ClassId.Miner;
                    if (!Follow(w, st, id, minerField ? BotFields.FieldMinerHill : BotFields.FieldHill)) Toward(w, id, map.Hill.X, map.Hill.Z, 1);
                    break;
                }
            }

            // Miner: the next path cell is a breakable wall within reach -> swing at it.
            if (minerField && Fs.Cell >= 0)
            {
                byte t = map.Cells[Fs.Cell];
                if (t == Cell.BreakableIntact || t == Cell.BreakableCracked)
                {
                    int W = map.Width;
                    double bx = (Fs.Cell % W) + 0.5;
                    double bz = JsMath.FloorToInt((double)Fs.Cell / W) + 0.5;
                    double dx = bx - x;
                    double dz = bz - z;
                    double minerReach = MinerReach(cfg);
                    if (dx * dx + dz * dz <= minerReach * minerReach)
                    {
                        aimX = bx;
                        aimZ = bz;
                        output.Attack = true;
                    }
                }
            }

            // Opportunistic swing outside 'fight': any enemy inside reach gets aimed at and hit (bots defend while they
            // hold, walk or gather; the FSM only decides whether to CHASE).
            if (!output.Attack && a.AiState != AiState.Fight)
            {
                double reach = cc.AttackRange + AttackSlack;
                int cnt = SpatialHash.QueryCircle(w.Hash, x, z, reach, mates);
                int best = -1;
                double bestD2 = double.PositiveInfinity;
                for (int k = 0; k < cnt; k++)
                {
                    int j = mates[k];
                    if (j >= w.ActorCount) continue;
                    Actor t = w.Actors[j];
                    if (!t.Alive || t.Team == a.Team || t.SpawnProtect > 0) continue;
                    if (BotFsm.PassiveTarget(w, j)) continue; // DG-16 passive window
                    double dx = w.X[j] - x;
                    double dz = w.Z[j] - z;
                    double d2 = dx * dx + dz * dz;
                    if (d2 < bestD2 || (d2 == bestD2 && j < best))
                    {
                        bestD2 = d2;
                        best = j;
                    }
                }
                if (best >= 0)
                {
                    aimX = w.X[best];
                    aimZ = w.Z[best];
                    output.Attack = InArc(w, id, aimX, aimZ);
                }
                else
                {
                    // CM-19: a hostile, awake Kaiju inside reach gets hit back too (bots on the hill no longer stand in its melee).
                    Kaiju k = w.Kaiju;
                    bool hostile = k.State == KaijuState.Berserk
                        || (k.State != KaijuState.Neutral && k.State != KaijuState.Egg && k.State != SimEnumMap.ToKaijuState(a.Team));
                    if (hostile && k.KoIn <= 0)
                    {
                        double kr = cc.AttackRange + cfg.Kaiju.Radius + AttackSlack;
                        double dx = k.X - x;
                        double dz = k.Z - z;
                        if (dx * dx + dz * dz <= kr * kr)
                        {
                            aimX = k.X;
                            aimZ = k.Z;
                            output.Attack = InArc(w, id, aimX, aimZ);
                        }
                    }
                }
            }

            double ml = Mv.X * Mv.X + Mv.Z * Mv.Z;
            if (ml > 1e-8)
            {
                // Barricade avoidance: probe ahead; sidestep around a BARRICADE, chip an enemy one in reach.
                int W = map.Width;
                int hit = FirstBlockedCell(map, x, z, x + Mv.X * BarricadeProbe, z + Mv.Z * BarricadeProbe);
                if (hit >= 0 && hit < map.Cells.Length && map.Cells[hit] == Cell.Barricade)
                {
                    double bx = (hit % W) + 0.5;
                    double bz = JsMath.FloorToInt((double)hit / W) + 0.5;
                    double bdx = bx - x;
                    double bdz = bz - z;
                    double reach = cc.AttackRange + 0.5;
                    if (bdx * bdx + bdz * bdz <= reach * reach && !output.Attack)
                    {
                        Barricade[] list = w.Barricades;
                        for (int s = 0; s < list.Length; s++)
                        {
                            Barricade b = list[s];
                            if (b.Active && b.Cx + 0.5 == bx && b.Cz + 0.5 == bz && b.Team != a.Team)
                            {
                                aimX = bx;
                                aimZ = bz;
                                output.Attack = true;
                                break;
                            }
                        }
                    }
                    int side = (id & 1) == 0 ? 0 : 1;
                    double ox = Mv.X;
                    double oz = Mv.Z;
                    for (int r = 0; r < 4; r++)
                    {
                        int k = r ^ side; // alternate which side is tried first by id parity
                        double c = RotCos[k];
                        double sn = RotSin[k];
                        double rx = ox * c - oz * sn;
                        double rz = ox * sn + oz * c;
                        if (FirstBlockedCell(map, x, z, x + rx * BarricadeProbe, z + rz * BarricadeProbe) < 0)
                        {
                            Mv.X = rx;
                            Mv.Z = rz;
                            break;
                        }
                    }
                }
            }

            if (separate)
            {
                int cnt = SpatialHash.QueryCircle(w.Hash, x, z, SeparationRadius, mates);
                double sx = 0;
                double sz = 0;
                for (int k = 0; k < cnt; k++)
                {
                    int j = mates[k];
                    if (j == id || j >= w.ActorCount) continue;
                    Actor t = w.Actors[j];
                    if (!t.Alive || t.Team != a.Team) continue;
                    double dx = x - w.X[j];
                    double dz = z - w.Z[j];
                    double d2 = dx * dx + dz * dz;
                    if (d2 > SeparationRadius * SeparationRadius) continue;
                    if (d2 < 1e-10)
                    {
                        // Coincident: push apart along an id-derived direction.
                        sx += id < j ? 1 : -1;
                        continue;
                    }
                    double d = Math.Sqrt(d2);
                    double wt = 1 - d / SeparationRadius;
                    sx += (dx / d) * wt;
                    sz += (dz / d) * wt;
                }
                Mv.X += sx * SeparationWeight;
                Mv.Z += sz * SeparationWeight;
            }

            ml = Mv.X * Mv.X + Mv.Z * Mv.Z;
            if (ml > 1e-4)
            {
                double inv = 1 / Math.Sqrt(ml);
                double ox = Mv.X * inv;
                double oz = Mv.Z * inv;
                // CM-21 (2): turn toward the wanted direction at most TurnStep per step, from the sim velocity heading.
                double vx = w.Vx[id];
                double vz = w.Vz[id];
                double vs = Math.Sqrt(vx * vx + vz * vz);
                if (Followed && a.AiState != AiState.Fight && vs >= TurnLimitFraction * cc.MoveSpeed)
                {
                    double hx = vx / vs;
                    double hz = vz / vs;
                    double turnCos = TurnCos(cfg);
                    if (hx * ox + hz * oz < turnCos)
                    {
                        double turnSin = TurnSin(cfg);
                        double sn = hx * oz - hz * ox >= 0 ? turnSin : -turnSin;
                        ox = hx * turnCos - hz * sn;
                        oz = hx * sn + hz * turnCos;
                    }
                }
                output.MoveX = ox;
                output.MoveZ = oz;
                st.WantMove[id] = 1;
            }

            // Ability press decided at think time.
            if (st.AbilityReq[id] == 1)
            {
                if (a.AbilityCd <= 0)
                {
                    output.Ability = true;
                    aimX = st.AbilityX[id];
                    aimZ = st.AbilityZ[id];
                }
                st.AbilityReq[id] = 0;
            }

            if (double.IsFinite(aimX))
            {
                output.AimX = aimX;
                output.AimZ = aimZ;
            }
            else if (st.WantMove[id] == 1)
            {
                output.AimX = x + output.MoveX * AimAhead;
                output.AimZ = z + output.MoveZ * AimAhead;
            }
        }
    }
}
