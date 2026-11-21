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
            sbyte[] lift = map.Lift;   // b15 terrain height: a cell 2+ levels above the one before is not a straight walk
            int prev = cx >= 0 && cz >= 0 && cx < W && cz < H ? cz * W + cx : -1;
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
                if (lift != null && prev >= 0 && lift[i] - lift[prev] >= 2 && map.Cells[i] != Cell.Bridge) return i;
                prev = i;
            }
            return -1;
        }

        /// <summary>
        /// b13 r3: bot id stands where field `field` gives no step (cut off, e.g. on a floor pocket TNT craters left). Steps
        /// toward the walkable cell within 2 cells with the lowest finite field distance (nearest first on a tie, scan order),
        /// and jumps when the straight line there crosses a cell it cannot walk (an airborne actor passes over void). False when
        /// there is no such cell. No allocation, no RNG.
        /// </summary>
        private static bool HopOut(World w, BotsState st, int id, int field, InputFrame output)
        {
            MapDef map = w.Map;
            double x = w.X[id], z = w.Z[id];
            int cx = JsMath.FloorToInt(x), cz = JsMath.FloorToInt(z);
            int bx = 0, bz = 0;
            double best = double.PositiveInfinity, bestD2 = double.PositiveInfinity;
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (!MapGrid.IsWalkable(map, nx, nz)) continue;
                    double d = BotFields.FieldDistAt(st.Fields, field, nx + 0.5, nz + 0.5);
                    if (!(d < double.PositiveInfinity)) continue;
                    double d2 = dx * dx + dz * dz;
                    if (d < best - 1e-9 || (Math.Abs(d - best) <= 1e-9 && d2 < bestD2))
                    {
                        best = d;
                        bestD2 = d2;
                        bx = nx;
                        bz = nz;
                    }
                }
            if (!(best < double.PositiveInfinity)) return false;
            double gx = bx + 0.5, gz = bz + 0.5, ex = gx - x, ez = gz - z, len = Math.Sqrt(ex * ex + ez * ez);
            if (len < 1e-6) return false;
            Mv.X = ex / len;
            Mv.Z = ez / len;
            bool gap = false;
            int n = (int)Math.Ceiling(len / 0.25);
            for (int k = 1; k <= n && !gap; k++)
            {
                double t = (double)k / n;
                if (!MapGrid.IsWalkable(map, JsMath.FloorToInt(x + ex * t), JsMath.FloorToInt(z + ez * t))) gap = true;
            }
            if (gap && w.Actors[id].AirLen == 0) output.Jump = true;
            return true;
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
            // b15 combat feel (audit stuck hot spots 75,52 and 65,45): in a narrow spot the raw next-cell step, not the blend,
            // which steered into void corners at diagonal bridge ends and into the hill rock ring.
            bool raw = ok && CombatFeel.On(w) && w.Config.Depth.CombatFeel.BotNarrowRawStep && CombatFeel.Narrow(w.Map, w.X[id], w.Z[id]);
            if (ok && !raw) BlendFlow(st, w, field, w.X[id], w.Z[id]);
            return ok;
        }

        /// <summary>
        /// fix/movement-shiver-1: the centre line is clear and so are the two shoulder lines (offset by r either side), the
        /// shoulders only up to `reach` short of the target (a target hugging a wall still counts). Allocation-free.
        /// </summary>
        internal static bool WideLineClear(MapDef map, double x0, double z0, double x1, double z1, double r, double reach)
        {
            if (FirstBlockedCell(map, x0, z0, x1, z1) >= 0) return false;
            double dx = x1 - x0;
            double dz = z1 - z0;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len <= reach || len < 1e-9) return true;
            double f = (len - reach) / len;
            double ox = -dz / len * r;
            double oz = dx / len * r;
            double ex = x0 + dx * f;
            double ez = z0 + dz * f;
            return FirstBlockedCell(map, x0 + ox, z0 + oz, ex + ox, ez + oz) < 0 && FirstBlockedCell(map, x0 - ox, z0 - oz, ex - ox, ez - oz) < 0;
        }

        /// <summary>
        /// fix/movement-shiver-2: hold the hill post. Settles inside an arrival radius that grows by BotPostArrivePerAlly for each
        /// ally within SeparationRadius (capped at BotPostArriveMax), and walks back only once pushed BotPostRelease past it.
        /// The old fixed 0.5 stop with the separation push on top made a crowded holder step out, walk back, step out.
        /// </summary>
        private static void HoldPost(World w, BotsState st, int id, double x, double z, int[] mates)
        {
            PhysicsConfig pc = w.Config.Physics;
            TeamId team = w.Actors[id].Team;
            int cnt = SpatialHash.QueryCircle(w.Hash, x, z, SeparationRadius, mates);
            int allies = 0;
            for (int k = 0; k < cnt; k++)
            {
                int j = mates[k];
                if (j == id || j >= w.ActorCount || !w.Actors[j].Alive || w.Actors[j].Team != team) continue;
                double ax = w.X[j] - x;
                double az = w.Z[j] - z;
                if (ax * ax + az * az <= SeparationRadius * SeparationRadius) allies++;
            }
            double arrive = Math.Min(pc.BotPostArriveMax, pc.BotPostArrive + pc.BotPostArrivePerAlly * allies);
            double dx = st.PostX[id] - x;
            double dz = st.PostZ[id] - z;
            double d2 = dx * dx + dz * dz;
            double release = arrive + pc.BotPostRelease;
            if (st.Settled[id] != 0 && d2 > release * release)
            {
                // fix/movement-shiver-3: pushed off the post by the crowd but still well inside the hill: the hold is about being
                // on the hill, so the post moves to where it stands now instead of a walk back into the same crowd (out, back,
                // out: the HoldHill Dither the gate still saw). Outside that it walks back as before.
                HillDef hill = w.Map.Hill;
                double hx = x - hill.X;
                double hz = z - hill.Z;
                double inner = hill.R - 1.5;
                if (inner > 0 && hx * hx + hz * hz <= inner * inner)
                {
                    st.PostX[id] = JsMath.F32(x);
                    st.PostZ[id] = JsMath.F32(z);
                }
                else st.Settled[id] = 0;
            }
            else if (st.Settled[id] == 0 && d2 <= arrive * arrive) st.Settled[id] = 1;
            if (st.Settled[id] != 0)
            {
                Mv.X = 0;
                Mv.Z = 0;
            }
            else Toward(w, id, st.PostX[id], st.PostZ[id], 0);
        }

        /// <summary>
        /// fix/movement-shiver-2: fight steering with hysteresis. (1) Straight line or route: straight needs the whole body to fit
        /// along the line (WideLineClear) to start, and is left only when the centre line itself is blocked; either choice is held
        /// BotModeHoldSeconds. (2) The circle-strafe band is entered at reach with a clear line of hit and left only beyond reach +
        /// BotReachSlack. (3) The strafe is steered from the target's real position; the wind-up lead (tx, tz) stays on the aim and
        /// the straight chase. Same play style: same strafe side by id parity, same 0.7 x reach keep distance, same pull.
        /// </summary>
        private static void FightCalm(World w, BotsState st, int id, int t, double tx, double tz, double reach, InputFrame output)
        {
            GameConfig cfg = w.Config;
            PhysicsConfig pc = cfg.Physics;
            MapDef map = w.Map;
            ClassConfig cc = cfg.Class(w.Actors[id].Cls);
            double x = w.X[id];
            double z = w.Z[id];
            double rx = t == BotsState.TargetKaiju ? w.Kaiju.X : w.X[t];
            double rz = t == BotsState.TargetKaiju ? w.Kaiju.Z : w.Z[t];
            double now = w.Match.Time;
            byte mode = st.FightMode[id];
            if (mode == 0 || now >= st.FightModeUntil[id])
            {
                bool straight = mode == 1 ? FirstBlockedCell(map, x, z, rx, rz) < 0 : WideLineClear(map, x, z, rx, rz, cc.Radius * 0.9, reach);
                byte next = straight ? (byte)1 : (byte)2;
                if (next != mode)
                {
                    st.FightMode[id] = next;
                    st.FightModeUntil[id] = now + pc.BotModeHoldSeconds;
                    mode = next;
                }
            }
            bool hitLine = !Phys.On(w, pc.WallsBlockHits) || SegmentClear.Clear(map, x, z, rx, rz);
            double dx = rx - x;
            double dz = rz - z;
            double d2 = dx * dx + dz * dz;
            double band = st.InReach[id] != 0 ? reach + pc.BotReachSlack : reach;
            bool inBand = d2 <= band * band && (st.InReach[id] != 0 || hitLine);
            st.InReach[id] = inBand ? (byte)1 : (byte)0;
            if (inBand)
            {
                output.Attack = hitLine && InArc(w, id, tx, tz);
                double d = Math.Sqrt(d2);
                if (d == 0 || double.IsNaN(d)) d = 1;
                double side = (id & 1) == 0 ? 1 : -1;
                double want = reach * 0.7;
                double pull = (d - want) / reach;
                Mv.X = (-dz / d) * side * 0.6 + (dx / d) * pull;
                Mv.Z = (dx / d) * side * 0.6 + (dz / d) * pull;
            }
            else if (mode == 1) Toward(w, id, tx, tz, 0);
            else if (!Follow(w, st, id, st.Role[id] == BotsState.RoleKaiju ? BotFields.FieldShrine : BotFields.FieldHill))
            {
                // fix/movement-shiver-3: the route is the hill (or shrine) field; standing in its goal disc it gives no step.
                // Round 2 left the bot standing there for good while its target stood behind a wall (34.9 s in BotsMatchTests).
                // Go straight at the target instead: the wall slides it along, the stuck detector sees a bot that wants to move,
                // and its unstick walk and the FSM's next think take it on.
                Toward(w, id, tx, tz, 0);
            }
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

        /// <summary>
        /// Sim-physics lane: a telegraphed Kaiju attack covers this bot: move straight away from its origin (claw cone or slam
        /// ring), drop the swing, and press jump when the slam lands inside the jump's air time. True when it dodged.
        /// </summary>
        private static bool DodgeKaiju(World w, int id, InputFrame output)
        {
            Kaiju k = w.Kaiju;
            if (k.State == KaijuState.Egg || k.KoIn > 0 || !Kaiju.KaijuHostileTo(w, id)) return false;
            GameConfig cfg = w.Config;
            double ox;
            double oz;
            if (k.TelegraphIn > 0 && Phys.On(w, cfg.Physics.SlamShockwave))
            {
                ox = k.TelegraphX;
                oz = k.TelegraphZ;
                double ddx = w.X[id] - ox;
                double ddz = w.Z[id] - oz;
                if (ddx * ddx + ddz * ddz > (KaijuTypes.SlamRadius(w) + 0.5) * (KaijuTypes.SlamRadius(w) + 0.5)) return false;
                int left = (int)Math.Ceiling(k.TelegraphIn * cfg.SimHz - 1e-9);
                if (Phys.On(w, cfg.Physics.AirDodgesSlam) && w.Actors[id].AirLen == 0 && left >= 1 && left <= Jump.AirTicks(cfg) - 2) output.Jump = true;
            }
            else if (k.MeleeWindupIn > 0 && Phys.On(w, cfg.Physics.KaijuClawCone) && Kaiju.InClawCone(w, id))
            {
                ox = k.ClawX;
                oz = k.ClawZ;
            }
            else return false;
            double dx = w.X[id] - ox;
            double dz = w.Z[id] - oz;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6)
            {
                dx = JsMath.Cos(id * 2.39996);
                dz = JsMath.Sin(id * 2.39996);
                d = 1;
            }
            output.MoveX = dx / d;
            output.MoveZ = dz / d;
            output.Attack = false;
            return true;
        }

        /// <summary>b15 unjam: the pressed window (ticks at 30 Hz) and the side-step length (ticks).</summary>
        public const int JamWindowTicks = 15, SideStepTicks = 12;

        /// <summary>
        /// b15 combat feel, the steering-level stuck detector (deterministic, no RNG): a bot whose own steer pressed at least
        /// half speed for a whole JamWindowTicks window and covered under a quarter of the walk it asked for (a wall corner, a
        /// bridge end, the hill crowd) side-steps for SideStepTicks along the perpendicular with the clear line and fewer bodies
        /// (a tie: by id parity), slightly forward. The FSM's slower unstick (StuckSeconds) still runs behind it. True while it
        /// steers the output.
        /// </summary>
        private static bool Unjam(World w, BotsState st, int id, ClassConfig cc, InputFrame output)
        {
            Actor a = w.Actors[id];
            double x = w.X[id], z = w.Z[id];
            if (w.Tick < st.SideUntil[id])
            {
                output.MoveX = st.SideX[id];
                output.MoveZ = st.SideZ[id];
                return true;
            }
            double mx = output.MoveX, mz = output.MoveZ;
            double m2 = mx * mx + mz * mz;
            // A fighter at reach (circle-strafing, its strafe pressed into the scrum) is fighting, not stuck: no side-step there
            // (it read as Reversals in the shiver gate). Pass 4: no fighter at all. A chase into a moving scrum covers little of
            // its walk without being stuck, and its side-step and the chase back read as Reversals (2-minute shiver gate, run
            // 44837670714: 12 hard flags, every one a Crimson fighter chasing on the hill).
            if (m2 < 0.25 || a.HitStunTicks > 0 || a.AirLen > 0 || a.StandUp > 0 || a.KVx != 0 || a.KVz != 0 ||
                a.AiState == AiState.Fight)
            {
                st.JamX[id] = x;
                st.JamZ[id] = z;
                st.JamTick[id] = w.Tick;
                return false;
            }
            int span = w.Tick - st.JamTick[id];
            if (span < JamWindowTicks) return false;
            double ex = x - st.JamX[id], ez = z - st.JamZ[id];
            double want = cc.MoveSpeed * CombatFeel.SpeedScale(w, id) * span / w.Config.SimHz * 0.25;
            st.JamX[id] = x;
            st.JamZ[id] = z;
            st.JamTick[id] = w.Tick;
            if (ex * ex + ez * ez >= want * want) return false;
            // b15 pass 3: no side-step against the Kaiju or on the hill disc. Side-stepping there walked the hill crowd off the disc,
            // dragged the Kaiju's chase off the hill (KaijuHill on-disc share 0.03) and re-asked the route guard (RouteProofs).
            CombatFeelConfig fc = w.Config.Depth.CombatFeel;
            if (fc.BotUnjamKaijuClear > 0 && w.Match.Hatched && w.Kaiju.State != KaijuState.Egg)
            {
                double kx = w.Kaiju.X - x, kz = w.Kaiju.Z - z;
                if (kx * kx + kz * kz <= fc.BotUnjamKaijuClear * fc.BotUnjamKaijuClear) return false;
            }
            if (fc.BotUnjamHillClear >= 0)
            {
                HillDef h = w.Map.Hill;
                double hx = h.X - x, hz = h.Z - z, hr = h.R + fc.BotUnjamHillClear;
                if (hx * hx + hz * hz <= hr * hr) return false;
            }
            if (fc.BotUnjamHillZone > 0 && w.Match.Hatched && w.Kaiju.State != KaijuState.Egg)
            {
                HillDef h = w.Map.Hill;
                double hx = h.X - x, hz = h.Z - z;
                if (hx * hx + hz * hz <= fc.BotUnjamHillZone * fc.BotUnjamHillZone) return false;
            }
            // Pass 4: bounded. A bot that has used its budget in the current window is left to the FSM's own unstick.
            if (fc.BotUnjamBudget > 0)
            {
                int win = CombatFeel.Ticks(w, fc.BotUnjamBudgetSeconds);
                if (w.Tick - st.UnjamWindowStart[id] >= win)
                {
                    st.UnjamWindowStart[id] = w.Tick;
                    st.UnjamCount[id] = 0;
                }
                if (st.UnjamCount[id] >= fc.BotUnjamBudget) return false;
                st.UnjamCount[id]++;
            }
            double inv = 1 / Math.Sqrt(m2);
            mx *= inv;
            mz *= inv;
            double best = double.NegativeInfinity, bx = 0, bz = 0;
            // Pass 4: the tie-break is mirror-fair. It reads the team-local slot, and Crimson (the mirrored half of the map) turns
            // the other way, so both teams make the mirror image of the same choice. The raw id parity turned both teams to the
            // same side of their walk, which is a different side on a mirrored map: 117 of 200 5v5 wins for the team with an idle
            // player (RulesCombined idle floor, limit under 100; 96 with no side-step), run 44834829109.
            int slot = World.TeamSlot(w, id);
            double mirror = a.Team == TeamId.Crimson ? -1 : 1;
            for (int s = 0; s < 2; s++)
            {
                double side = (((slot + s) & 1) == 0 ? 1 : -1) * mirror;
                double px = -mz * side * 0.9 + mx * 0.3, pz = mx * side * 0.9 + mz * 0.3;
                double score = (slot + s) & 1;   // the parity tie-break, under every real difference
                // Sliding along a wall: keep the slide's side (a side-step against it would read as a reversal).
                if (px * w.Vx[id] + pz * w.Vz[id] > 0.05) score += 5;
                // Jammed again soon after a side-step: the same side as that one (the other way is a reversal).
                if (w.Tick - st.SideUntil[id] <= 2 * JamWindowTicks && px * st.SideX[id] + pz * st.SideZ[id] > 0) score += 20;
                if (FirstBlockedCell(w.Map, x, z, x + px * 1.2, z + pz * 1.2) >= 0) score -= 100;
                int cnt = SpatialHash.QueryCircle(w.Hash, x + px * 0.9, z + pz * 0.9, 0.8, Mates);
                for (int k = 0; k < cnt; k++)
                {
                    int j = Mates[k];
                    if (j == id || j >= w.ActorCount || !w.Actors[j].Alive) continue;
                    double qx = w.X[j] - (x + px * 0.9), qz = w.Z[j] - (z + pz * 0.9);
                    if (qx * qx + qz * qz <= 0.64) score -= 10;
                }
                if (score > best)
                {
                    best = score;
                    bx = px;
                    bz = pz;
                }
            }
            double l = Math.Sqrt(bx * bx + bz * bz);
            st.SideX[id] = bx / l;
            st.SideZ[id] = bz / l;
            st.SideUntil[id] = w.Tick + SideStepTicks;
            st.Unjams++;
            output.MoveX = st.SideX[id];
            output.MoveZ = st.SideZ[id];
            return true;
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

            // fix/movement-shiver-2 (BotCalmSteer): the hysteresis latches belong to one state; leaving it clears them.
            bool calm = Phys.On(w, cfg.Physics.BotCalmSteer);
            if (a.AiState != AiState.HoldHill) st.Settled[id] = 0;
            if (a.AiState != AiState.Fight)
            {
                st.FightMode[id] = 0;
                st.InReach[id] = 0;
            }
            switch (a.AiState)
            {
                case AiState.Unstick:
                    Toward(w, id, st.UnstickX[id], st.UnstickZ[id], 0.2);
                    break;
                case AiState.HoldHill:
                    if (TeamBrain.On(w) && TeamBrain.OffHillJob(st.Job[id]))   // b16: escort or intercept
                    {
                        Escort(w, st, id, a);   // b15 bot brain: KAIJU HOLDS NOTHING, walk our Kaiju off the disc
                        break;
                    }
                    if (calm) HoldPost(w, st, id, x, z, mates);
                    else Toward(w, id, st.PostX[id], st.PostZ[id], 0.5);
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
                        reach = CombatFeel.BotReach(w, a.Cls, cc.AttackRange) + AttackSlack; // b15: the Healer keeps her stand-off
                        // DG-69: with a wind-up the bot swings at where the target will be on the contact tick.
                        int windup = w.Dur.AttackWindup[(int)a.Cls];
                        if (windup > 0)
                        {
                            // b15 combat feel: the lead is capped at BotLeadSeconds. A 0.40 s wind-up led the whole way swung the
                            // chase point across a knocked or strafing target, so the bot walked back and forth (shiver gate run
                            // 44232708547: 18 hard flags, 1 with the shipped wind-ups); a heavy that misses a moving target is fair.
                            double lead = (CombatFeel.On(w) ? Math.Min(windup, CombatFeel.Ticks(w, cfg.Depth.CombatFeel.BotLeadSeconds)) : windup) / cfg.SimHz;
                            tx += w.Vx[t] * lead;
                            tz += w.Vz[t] * lead;
                        }
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
                    // fix/movement-shiver-1 (BotWideLine): no strafing and swinging at a target inside reach but behind a wall,
                    // and the straight line counts only when the whole body fits along it (no route / line flip at a wall end).
                    bool wide = Phys.On(w, cfg.Physics.BotWideLine);
                    bool seen = !wide || !Phys.On(w, cfg.Physics.WallsBlockHits) || SegmentClear.Clear(map, x, z, tx, tz);
                    // b15 bot brain (a): a kiter strikes the Kaiju from its side away from the hill, so the Kaiju's chase walks it
                    // off the disc. Only while the straight line to that point is clear; else the plain fight steering below.
                    if (t == BotsState.TargetKaiju && TeamBrain.On(w) && st.Job[id] == TeamBrain.JobKite)
                    {
                        TeamBrain.KitePoint(w, id, reach, out double kpx, out double kpz);
                        if (wide ? WideLineClear(map, x, z, kpx, kpz, cc.Radius * 0.9, 0.5) : FirstBlockedCell(map, x, z, kpx, kpz) < 0)
                        {
                            if (d2 <= reach * reach && seen) output.Attack = InArc(w, id, tx, tz);
                            Toward(w, id, kpx, kpz, 0.3);
                            break;
                        }
                    }
                    if (calm)
                    {
                        FightCalm(w, st, id, t, tx, tz, reach, output);
                        break;
                    }
                    if (d2 <= reach * reach && seen)
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
                    else if (wide ? WideLineClear(map, x, z, tx, tz, cc.Radius * 0.9, reach) : FirstBlockedCell(map, x, z, tx, tz) < 0)
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
                    // b13 r3 (PhysicsSoak16v16 seed 101, Healer bot 14 still in Retreat at (65.06, 42.58) from tick 13860): TNT
                    // craters (Cell.Void) cut the floor pocket she stood on off the pad field (her cell and every neighbour
                    // Infinity but one diagonal past a void corner, which FlowStepAt refuses), so Follow found no step, she wanted
                    // no move (WantMove 0: the stuck and progress detectors never fire) and stood there for good. Now she hops out.
                    if (!Follow(w, st, id, TeamPadField(w, id)) && DepthRules.On(w, true)) HopOut(w, st, id, TeamPadField(w, id), output);   // Full only: Classic / PhysicsOnly rows unchanged
                    break;
                case AiState.Order:
                {
                    // U-067: to the ping (or the caller): straight when the line is clear, else the ping's route.
                    if (!PingRules.HasOrder(w, id)) break;
                    PingRules.OrderGoal(w, id, out double gx, out double gz, out double stop);
                    FlowField route = PingRules.OrderRoute(w, id);
                    // fix/movement-shiver-1 (BotWideLine): the straight line only when the whole body fits along it.
                    bool lineOk = Phys.On(w, cfg.Physics.BotWideLine) ? WideLineClear(map, x, z, gx, gz, cc.Radius * 0.9, stop) : FirstBlockedCell(map, x, z, gx, gz) < 0;
                    if (lineOk || route == null) Toward(w, id, gx, gz, stop);
                    else if (FlowField.SampleFlowDir(route, map, x, z, out double odx, out double odz))
                    {
                        Mv.X = odx;
                        Mv.Z = odz;
                    }
                    else Toward(w, id, gx, gz, stop);
                    break;
                }
                case AiState.Task:   // b13-hook
                {
                    // CM b13: an item task (BotsState.TaskKind): to (TaskX, TaskZ) like an Order to its point (straight when the
                    // body fits along the line, else Toward with the barricade probe below); within 1.2 u the owning item acts.
                    if (w.Match.Time >= st.TaskUntil[id])   // b13-hook: a task past TaskUntil ends
                    {
                        st.TaskKind[id] = 0;   // b13-hook
                        a.AiState = AiState.None;   // b13-hook: ThinkBot picks a state next tick
                        break;
                    }
                    double gx = st.TaskX[id];
                    double gz = st.TaskZ[id];
                    double tdx = gx - x;
                    double tdz = gz - z;
                    if (tdx * tdx + tdz * tdz <= 1.2 * 1.2)
                    {
                        aimX = gx;
                        aimZ = gz;
                        separate = false;
                        B13Hooks.BotTaskAct(w, st, id, output);   // b13-hook
                        // b13 r3 (MovementShiver Kaiju hill seed 1337): a task that walks (BotMine.Act writes the step straight into
                        // the frame) is a wish to move, so the stuck and progress detectors see it. Before, WantMove stayed 0: Crimson
                        // Miner bot 26 pressed into the void edge of the diagonal bridge at (70, 28.5) for over 300 s, never unstuck,
                        // and the owned Kaiju behind him on the deck (it cannot push a wedged fighter) stood at (70.6, 27.0) all match.
                        if (output.MoveX * output.MoveX + output.MoveZ * output.MoveZ > 1e-8) st.WantMove[id] = 1;
                    }
                    else Toward(w, id, gx, gz, 0);
                    break;
                }
                case AiState.Grab:
                    Toward(w, id, st.GrabX[id], st.GrabZ[id], 0.2); // U-066: straight at the power-up (it was in line of sight)
                    break;
                case AiState.Supply:
                {
                    // U-064: to the source and gather; with no source (full, or none left) home to the base and bank.
                    int si = st.SupplySource[id];
                    SourceNode[] srcs = w.Depth.Sources;
                    if (si >= 0 && si < srcs.Length)
                    {
                        SourceNode src = srcs[si];
                        aimX = src.X;
                        aimZ = src.Z;
                        double reach = cfg.Resources.NodeInteractRange + cc.Radius - InteractMargin;
                        double dx = src.X - x;
                        double dz = src.Z - z;
                        if (dx * dx + dz * dz <= reach * reach)
                        {
                            output.Interact = true;
                            separate = false;
                        }
                        else if (FlowField.SampleFlowDir(Sources.EnsureField(w, si), map, x, z, out double fdx, out double fdz))
                        {
                            Mv.X = fdx;
                            Mv.Z = fdz;
                        }
                        else Toward(w, id, src.X, src.Z, 0);
                    }
                    else
                    {
                        PadDef pad = map.Pads[(int)a.Team];
                        double r = cfg.Depth.Sources.BankRange - 1;
                        double dx = pad.X - x;
                        double dz = pad.Z - z;
                        if (dx * dx + dz * dz <= r * r) output.Interact = true;
                        else if (!Follow(w, st, id, TeamPadField(w, id))) Toward(w, id, pad.X, pad.Z, 0);
                    }
                    break;
                }
                case AiState.Escort:
                {
                    // U-063: walk with the carrier (straight at it when the line is clear, else the shrine route it is on).
                    int t = st.EscortOf[id];
                    if (t >= 0 && t < w.ActorCount && w.Actors[t].Alive && (Phys.On(w, cfg.Physics.BotWideLine)
                            ? WideLineClear(map, x, z, w.X[t], w.Z[t], cc.Radius * 0.9, cfg.Depth.Roles.EscortKeepDistance)
                            : FirstBlockedCell(map, x, z, w.X[t], w.Z[t]) < 0))
                        Toward(w, id, w.X[t], w.Z[t], cfg.Depth.Roles.EscortKeepDistance);
                    else if (!Follow(w, st, id, BotFields.FieldShrine)) Toward(w, id, map.Shrine.X, map.Shrine.Z, 3);
                    break;
                }
                default:
                {
                    // b15 bot brain (b): a respawned attacker waits at the staging ring until its group goes in together.
                    if (a.AiState == AiState.ToHill && TeamBrain.On(w) && TeamBrain.HoldsAtStage(w, st, id))
                    {
                        aimX = map.Hill.X;
                        aimZ = map.Hill.Z;
                        break;
                    }
                    if (TeamBrain.On(w) && TeamBrain.OffHillJob(st.Job[id]))   // b16: escort or intercept
                    {
                        Escort(w, st, id, a);
                        break;
                    }
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
                if (hit >= 0 && hit < map.Cells.Length && (map.Cells[hit] == Cell.Barricade || map.Cells[hit] == Cell.LowWall))   // b13-hook
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

            // fix/movement-shiver-2: did the bot want to go somewhere, before the separation push is added?
            bool intent = Mv.X * Mv.X + Mv.Z * Mv.Z > 1e-8;
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
            // fix/movement-shiver-4: the calm push rule is for the two places it was made for, a settled hill holder and a
            // fighter at its stop. Every other bot (a gatherer at its node, a carrier, an escort, an idle bot in a gap) keeps the
            // old full-step separation, so it gets out of the way of a walker the way it did before batch 10.
            bool calmPush = calm && !intent &&
                            ((a.AiState == AiState.HoldHill && st.Settled[id] != 0) || a.AiState == AiState.Fight);
            if (calmPush)
            {
                // fix/movement-shiver-2: a separation-only push (a settled holder, a bot at its stop) keeps its size: the old
                // code blew any push over 0.01 up to a full-speed step, so two allies nudging each other stepped back and forth
                // at full speed every tick. Under BotMoveDeadzone it is dropped. Not a wish to move (WantMove stays 0), so the
                // stuck detector does not send a jostled holder on an unstick walk.
                // fix/movement-shiver-3: the dead zone is for a settled hill holder only. Anywhere else a small push is kept: a bot
                // with no step of its own must never be frozen in place by the dead zone (BotsMatchTests no-sit-still rule).
                double dzn = a.AiState == AiState.HoldHill && st.Settled[id] != 0 ? cfg.Physics.BotMoveDeadzone : 0.01;
                if (ml >= dzn * dzn)
                {
                    double m = Math.Sqrt(ml);
                    double sc = m > 1 ? 1 / m : 1;
                    output.MoveX = Mv.X * sc;
                    output.MoveZ = Mv.Z * sc;
                }
            }
            else if (ml > 1e-4)
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

            // feat/light-weapons-1: a Demolitions or Healer bot swings its light weapon only at close range (a Healer's press
            // that would mend is kept); every other class, and the rule off, is unchanged.
            if (output.Attack && !LightWeapons.BotKeepsPress(w, id, aimX, aimZ)) output.Attack = false;
            // DG-21 hesitation: an Easy bot holds its swing in SwingHesitation of 1/3-second windows (never for Normal).
            if (output.Attack && BotSkill.HoldsSwing(w, id)) output.Attack = false;
            // Sim-physics lane (DG-84 / DG-85): a Hard bot steps out of a telegraphed claw cone or slam ring, and jumps a slam.
            BotSkillConfig dsk = BotSkill.Profile(w, id);
            // b15 bot brain: a kiter uses the dodge step and the slam jump at every skill level.
            bool kiter = TeamBrain.On(w) && st.Job[id] == TeamBrain.JobKite;
            if (((dsk != null && dsk.DodgeKaiju) || kiter) && Phys.On(w, true) && DodgeKaiju(w, id, output)) st.WantMove[id] = 1;
            // b13 r3 [K]: a bot the SHROUD would blind leaves the cloud and skirts it, unless it is contesting the hill.
            if (BotKaijuFeed.AvoidCloud(w, id, output)) st.WantMove[id] = 1;
            // b15 combat feel: a bot that has pressed for JamSeconds and covered under a quarter of its walk side-steps.
            if (CombatFeel.On(w) && cfg.Depth.CombatFeel.BotUnjam && Unjam(w, st, id, cc, output)) st.WantMove[id] = 1;
            if (TeamBrain.On(w)) TeamBrain.TerrainVerbsHook(w, st, id, output);   // b15 bot brain (d): TerrainHeight hook (PR #211), a no-op here

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

        /// <summary>b15 bot brain, KAIJU HOLDS NOTHING escort: toward the enemy pad (its flow field), so the fights it meets
        /// pull our Kaiju off the hill disc; straight at the Kaiju's far side if the field has no route.</summary>
        private static void Escort(World w, BotsState st, int id, Actor a)
        {
            int field = a.Team == TeamId.Azure ? BotFields.FieldPadCrimson : BotFields.FieldPadAzure;
            if (st.Job[id] == TeamBrain.JobIntercept && Intercept(w, st, id, field)) return;   // b16-hook
            if (!Follow(w, st, id, field)) Toward(w, id, w.Kaiju.X, w.Kaiju.Z, 3);
        }

        /// <summary>Intercept band (u of path): inside it the bot stands; past its far edge it walks back toward the hill.</summary>
        const double InterceptBand = 3;
        /// <summary>b16: the intercept stand-off tiers (path units apart, how many).</summary>
        const double InterceptTierStep = 4;
        const int InterceptTiers = 3;

        /// <summary>
        /// b16 the hill cap (TeamBrain.HillCapJobs): stand on the enemy's approach, InterceptAlong u of path from the hill toward
        /// the enemy pad (the enemy pad's flow field: the route its reinforcements walk), and fight whoever comes (the FSM plays
        /// it cocky). A 3 u band of path holds still, so it never shivers on an edge. False when the field gives no distance.
        /// </summary>
        private static bool Intercept(World w, BotsState st, int id, int field)
        {
            double dHill = BotFields.FieldDistAt(st.Fields, field, w.Map.Hill.X, w.Map.Hill.Z);
            double d = BotFields.FieldDistAt(st.Fields, field, w.X[id], w.Z[id]);
            if (!(dHill < 1e5) || !(d < 1e5)) return false;
            // three stand-off tiers by id (12, 16, 20 u of path at the defaults): the surplus spreads along the approach instead of
            // packing one band of the corridor (r3 tick cost: one band made a dense crowd, 80 us a tick vs 54 us)
            double target = Math.Max(1, dHill - w.Config.Depth.Mech.InterceptAlong - InterceptTierStep * (id % InterceptTiers));
            if (d > target) return Follow(w, st, id, field);
            if (d < target - InterceptBand) return Follow(w, st, id, BotFields.FieldHill);
            Mv.X = 0;
            Mv.Z = 0;
            return true;
        }
    }
}
