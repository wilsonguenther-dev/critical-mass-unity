// CM b13 item H (Miner bots mine ore and bank it at the hut). Numbers: Sim/Config/HutConfig.cs.
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// CM b13 item H: Miner bots mine ore and bank it at the hut while their hut is below its top level. Task kinds (BotsState
    /// TaskKind, H owns 1-9): 1 = MineOre (TaskX/Z the vein), 2 = Hut (TaskX/Z the pad). Where the straight line to the goal is
    /// not walkable for the body (StraightWalk), TaskX/Z is a point about a cell down the goal's flow field (BotSteer walks a Task straight), moved on every
    /// tick by Act, so the bot follows the route. Deterministic, allocation-free.
    /// </summary>
    public static class BotMine
    {
        public const byte TaskMineOre = 1, TaskHut = 2;
        /// <summary>How far down the route the steering point sits (u, inside BotSteer's 1.2 u act radius).</summary>
        const double Carrot = 0.9;

        static bool Mining(World w, BotsState st, int id)
        {
            if (!Blacksmith.On(w)) return false;
            Actor a = w.Actors[id];
            return a.Alive && a.Cls == ClassId.Miner && BotsState.IsBot(st, w, id);
        }

        static bool OwnHalf(World w, TeamId team, double x)
        {
            double mid = w.Map.Width * 0.5;
            return (w.Map.Pads[(int)team].X < mid) == (x < mid);
        }

        /// <summary>The vein with charges bot id should walk to: nearest by walking distance on its own half, else anywhere; -1 none.</summary>
        public static int BestVein(World w, int id, out bool ownHalf)
        {
            SourceNode[] list = w.Depth.Sources;
            TeamId team = w.Actors[id].Team;
            int best = -1, bestOwn = -1;
            double bestD = double.PositiveInfinity, bestOwnD = double.PositiveInfinity;
            for (int i = 0; i < list.Length; i++)
            {
                SourceNode s = list[i];
                if (s.Kind != BiomassType.Ore || s.Charges <= 0) continue;
                double d = Sources.FieldDist(w, i, w.X[id], w.Z[id]);
                if (!double.IsFinite(d)) continue;
                if (d < bestD) { bestD = d; best = i; }
                if (OwnHalf(w, team, s.X) && d < bestOwnD) { bestOwnD = d; bestOwn = i; }
            }
            ownHalf = bestOwn >= 0;
            return bestOwn >= 0 ? bestOwn : best;
        }

        /// <summary>How far ahead StraightWalk checks the line (u) and its sample step (u).</summary>
        const double WalkLookahead = 3, WalkStep = 0.4;

        /// <summary>
        /// b13 r2: the straight line from (x, z) toward (gx, gz) is walkable ground for the whole body over the next
        /// WalkLookahead u: no hit blocker on the centre line (SegmentClear) and every sample of the centre line and of both
        /// edge lines one class radius to each side stands on a walkable cell. SegmentClear alone is a HIT line (a hole, a
        /// cliff edge or water does not stop it), so it sent Miners straight at the base island edge (Pressed flags).
        /// Allocation-free; at most 8 samples x 3 cell reads.
        /// </summary>
        static bool StraightWalk(World w, int id, double x, double z, double gx, double gz)
        {
            double dx = gx - x, dz = gz - z, l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-9) return true;
            double look = l < WalkLookahead ? l : WalkLookahead;
            double ux = dx / l, uz = dz / l;
            double ex = x + ux * look, ez = z + uz * look;
            if (!SegmentClear.Clear(w.Map, x, z, ex, ez)) return false;
            double r = w.Config.Class(w.Actors[id].Cls).Radius;
            double px = -uz * r, pz = ux * r;
            MapDef m = w.Map;
            int n = (int)Math.Ceiling(look / WalkStep);
            for (int k = 1; k <= n; k++)
            {
                double t = k * look / n, sx = x + ux * t, sz = z + uz * t;
                if (!MapGrid.IsWalkable(m, JsMath.FloorToInt(sx), JsMath.FloorToInt(sz))) return false;
                if (!MapGrid.IsWalkable(m, JsMath.FloorToInt(sx + px), JsMath.FloorToInt(sz + pz))) return false;
                if (!MapGrid.IsWalkable(m, JsMath.FloorToInt(sx - px), JsMath.FloorToInt(sz - pz))) return false;
            }
            return true;
        }

        /// <summary>The steering point toward (gx, gz): the goal when the line is clear, else a point down the route.</summary>
        static void Aim(World w, int id, FlowField route, double gx, double gz, out double tx, out double tz)
        {
            double x = w.X[id], z = w.Z[id];
            tx = gx;
            tz = gz;
            // b13 r2: straight while the next stretch of the line is WALKABLE for the whole body (StraightWalk), else the route.
            // Round 1 followed the route whenever there was one (a hit line over a hole or a cliff edge pressed Miners into
            // the base edge), which halved the ore rate (21 ore per Miner bot against 44, 0 of 6 16v16 seeds at L5).
            if (route == null || StraightWalk(w, id, x, z, gx, gz)) return;
            if (route != null && FlowField.SampleFlowDir(route, w.Map, x, z, out double dx, out double dz))
            {
                tx = x + dx * Carrot;
                tz = z + dz * Carrot;
            }
        }

        /// <summary>Offers the Miner's task utilities (BotFsm.ConsiderExternal) before the state is chosen.</summary>
        public static void Consider(World w, BotsState st, int id)
        {
            if (!Mining(w, st, id)) return;
            Actor a = w.Actors[id];
            HutConfig H = w.Config.Depth.Hut;
            if (st.Corrupt[id] != 0 || a.Carry == CarryKind.Corruption) return; // a corruption runner on a run keeps it
            if (a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Ore) return; // other cargo: today's logic
            int ore = Blacksmith.OreOf(a);
            bool top = Blacksmith.Level(w, a.Team) >= Blacksmith.MaxLevel(w.Config);
            if (top && ore <= 0) return; // at L5 today's behaviour returns
            // Ore never goes to the egg: a Miner that walked into Deliver with ore is sent to the hut at once.
            if (ore > 0 && a.AiState == AiState.Deliver)
            {
                PadDef hp = w.Map.Pads[(int)a.Team];
                Aim(w, id, Blacksmith.PadField(w, a.Team), hp.X, hp.Z, out double ex, out double ez);
                Offer(w, st, id, TaskHut, ex, ez, H.BotEscapeUtility);
                return;
            }
            // Empty-handed with an enemy close: fight as today (the Miner still holds his share of the fights).
            if (ore <= 0 && EnemyWithin(w, id, H.BotFightRange)) return;
            // b13 integrator tuning: with ore in hand he still fights back at close range (a pure miner, not a pure target).
            if (ore > 0 && EnemyWithin(w, id, H.BotFightWithOreRange)) return;
            byte kind;
            double gx, gz;
            FlowField route;
            if (ore >= (int)H.OreCarry || top)
            {
                kind = TaskHut;
            }
            else
            {
                int v = BestVein(w, id, out bool own);
                if (v < 0 && ore <= 0) return;
                kind = v >= 0 && (own || ore <= 0) ? TaskMineOre : TaskHut;
                if (kind == TaskMineOre)
                {
                    SourceNode s = w.Depth.Sources[v];
                    route = Sources.EnsureField(w, v);
                    Aim(w, id, route, s.X, s.Z, out gx, out gz);
                    Offer(w, st, id, kind, gx, gz, ore > 0 ? H.BotCarryUtility : H.BotMineUtility);
                    return;
                }
            }
            PadDef pad = w.Map.Pads[(int)a.Team];
            route = Blacksmith.PadField(w, a.Team);
            Aim(w, id, route, pad.X, pad.Z, out gx, out gz);
            Offer(w, st, id, kind, gx, gz, H.BotCarryUtility);
        }

        static bool EnemyWithin(World w, int id, double r)
        {
            double r2 = r * r;
            TeamId team = w.Actors[id].Team;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor e = w.Actors[j];
                if (!e.Alive || e.Team == team) continue;
                double dx = w.X[j] - w.X[id], dz = w.Z[j] - w.Z[id];
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        static void Offer(World w, BotsState st, int id, byte kind, double gx, double gz, double u)
        {
            st.TaskKind[id] = kind;
            st.TaskX[id] = gx;
            st.TaskZ[id] = gz;
            st.TaskUntil[id] = w.Match.Time + w.Config.Depth.Hut.BotTaskSeconds;
            BotFsm.ConsiderExternal(w, AiState.Task, u);
        }

        /// <summary>The Miner's ability decision; true = decided here (BotFsm skips the barricade logic) for every Miner while on.</summary>
        public static bool DecideAbility(World w, BotsState st, int id, AiState s)
        {
            if (!Mining(w, st, id)) return false;
            Actor a = w.Actors[id];
            HutConfig H = w.Config.Depth.Hut;
            int ore = Blacksmith.OreOf(a);
            if (a.AbilityCd > 0 || ore <= 0 || Blacksmith.HaulTicksLeft(w, id) > 0) return true;
            bool home = s == AiState.Task && st.TaskKind[id] == TaskHut && ore >= (int)H.BotHaulHomeOre;
            bool threat = false;
            double r2 = H.BotHaulEnemyRange * H.BotHaulEnemyRange;
            for (int j = 0; j < w.ActorCount && !threat && !home; j++)
            {
                Actor e = w.Actors[j];
                if (!e.Alive || e.Team == a.Team) continue;
                double dx = w.X[j] - w.X[id], dz = w.Z[j] - w.Z[id];
                if (dx * dx + dz * dz <= r2) threat = true;
            }
            if (!home && !threat) return true;
            st.AbilityReq[id] = 1;
            st.AbilityX[id] = w.X[id];
            st.AbilityZ[id] = w.Z[id];
            return true;
        }

        /// <summary>Acts at the task point (TaskKind 1-9); true = the frame was written.</summary>
        public static bool Act(World w, BotsState st, int id, InputFrame o)
        {
            if (!Mining(w, st, id)) return false;
            byte kind = st.TaskKind[id];
            if (kind != TaskMineOre && kind != TaskHut) return false;
            Actor a = w.Actors[id];
            double x = w.X[id], z = w.Z[id];
            FlowField route;
            double gx, gz;
            if (kind == TaskMineOre)
            {
                // Nothing to mine for (full, other cargo, hut at the top): no press, so no refusal every other tick.
                if (Blacksmith.OreOf(a) >= (int)w.Config.Depth.Hut.OreCarry || (a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Ore)
                    || Blacksmith.Level(w, a.Team) >= Blacksmith.MaxLevel(w.Config)) return false;
                int v = Blacksmith.ChannelVein(w, id);
                if (v < 0) v = Blacksmith.VeinInReach(w, id);
                if (v >= 0)
                {
                    SourceNode s = w.Depth.Sources[v];
                    // b13 integrator (NoShiver fix): the steering point stays 0.3 u from the bot toward the vein, so BotSteer keeps
                    // calling Act (within its 1.2 u) instead of walking him into the vein's body from up to 1.6 u away (Pressed flags).
                    double vx = s.X - x, vz = s.Z - z, vl = Math.Sqrt(vx * vx + vz * vz);
                    st.TaskX[id] = vl > 1e-9 ? x + vx / vl * 0.3 : x;
                    st.TaskZ[id] = vl > 1e-9 ? z + vz / vl * 0.3 : z;
                    o.MoveX = 0;
                    o.MoveZ = 0;
                    o.AimX = s.X;
                    o.AimZ = s.Z;
                    // Hold F while a channel runs; release one tick to press again when it ended (full, empty, refused).
                    o.Terraform = Blacksmith.ChannelVein(w, id) >= 0 || w.Terraform.PrevPressed[id] == 0;
                    return true;
                }
                int best = BestVein(w, id, out _);
                if (best < 0) return false;
                SourceNode b = w.Depth.Sources[best];
                route = Sources.EnsureField(w, best);
                gx = b.X;
                gz = b.Z;
            }
            else
            {
                if (Stations.InStation(w, id))
                {
                    // The bank press on its edge (Sources' interact edge).
                    o.Interact = w.Depth.PrevInteract[id] == 0;
                    st.TaskX[id] = x;   // b13 integrator: stand and bank (no push into the pad edge)
                    st.TaskZ[id] = z;
                    o.MoveX = 0;
                    o.MoveZ = 0;
                    return true;
                }
                PadDef pad = w.Map.Pads[(int)a.Team];
                route = Blacksmith.PadField(w, a.Team);
                gx = pad.X;
                gz = pad.Z;
            }
            // Walk on: the route's step (or straight at a clear goal), and the steering point moves with the bot.
            double mx = gx - x, mz = gz - z;
            if (route != null && !StraightWalk(w, id, x, z, gx, gz) && FlowField.SampleFlowDir(route, w.Map, x, z, out double fx, out double fz))
            {
                mx = fx;
                mz = fz;
            }
            double ml = Math.Sqrt(mx * mx + mz * mz);
            if (ml < 1e-9) return false;
            mx /= ml;
            mz /= ml;
            o.MoveX = mx;
            o.MoveZ = mz;
            st.TaskX[id] = x + mx * Carrot;
            st.TaskZ[id] = z + mz * Carrot;
            return true;
        }
    }
}
