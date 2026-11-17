// CM b13 item C (crystal runners, feeders, guards and purifiers).
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// CM b13 item C: corruption bots, offered through BotFsm.ConsiderExternal as AiState.Task (BotSteer walks to TaskX / TaskZ
    /// and calls Act within 1.2 u). TaskKind 20 = to the nearest crystal vein with charges (runner of the trailing team, at most
    /// MaxRunners per team), 21 = feed the knocked-out Kaiju (any carrier of a team that may feed it), 22 = guard the downed
    /// Kaiju against an enemy carrier (owner team, at most MaxGuards), 23 = the owner Healer purifies the pool. Before the hatch
    /// a carrier hands its crystals in through the existing Deliver / Corrupt states (the shrine wind-up). Identity with the
    /// rule off. Stateless apart from BotsState's task fields: no allocation.
    /// </summary>
    public static class BotCorrupt
    {
        public const byte TaskVein = 20, TaskFeed = 21, TaskGuard = 22, TaskPurify = 23;

        /// <summary>Offers the corruption task utilities (BotFsm.ConsiderExternal) before the state is chosen.</summary>
        public static void Consider(World w, BotsState st, int id)
        {
            if (!Corruption.On(w)) return;
            Actor a = w.Actors[id];
            if (!a.Alive || a.StandUp > 0) return;
            // H's Miner bots mine ore until the hut is at level 5: never take a Miner off H's task.
            if (a.Cls == ClassId.Miner && Blacksmith.On(w) && Blacksmith.Level(w, a.Team) < 5) return;
            CorruptionConfig c = w.Config.Depth.Corruption;
            MatchState m = w.Match;
            Kaiju k = w.Kaiju;
            int team = a.Team == TeamId.Azure ? 0 : 1;
            bool egg = !m.Hatched && k.State == KaijuState.Egg;
            bool down = m.Hatched && k.KoIn > 0;
            bool carrying = a.Carry == CarryKind.Corruption && a.CarryAmount > 0;

            if (carrying)
            {
                if (down && Corruption.MayFeedTeam(w, team))
                {
                    RingPoint(w, id, out double fx, out double fz);
                    Offer(w, st, id, TaskFeed, fx, fz, c.FeedUtility);
                }
                return;
            }

            if (down && Corruption.OwnerTeam(w) == team)
            {
                int carrier = EnemyCarrierNear(w, team, k.X, k.Z, c.GuardRadius);
                if (a.Cls == ClassId.Healer)
                {
                    if (carrier >= 0 || Corruption.Pool(w) > 0)
                    {
                        RingPoint(w, id, out double hx, out double hz);
                        Offer(w, st, id, TaskPurify, hx, hz, c.PurifyUtility);
                    }
                    return;
                }
                if (carrier >= 0 && (IsOnTask(w, st, id, TaskGuard) || CountTask(w, st, a.Team, id, TaskGuard, TaskGuard) < c.MaxGuards))
                    Offer(w, st, id, TaskGuard, w.X[carrier], w.Z[carrier], c.GuardUtility);
                return;
            }

            // Runners: the trailing team's runner classes fetch crystals (before the hatch: behind on allegiance).
            if (!Roles.IsRunner(w, a.Cls) || !Roles.MayCorrupt(w, a.Cls) || a.CarryAmount > 0 || a.CarryMeat > 0) return;
            if (st.Corrupt[id] != 0) return; // already on the director's pre-hatch corruption run
            bool trailing;
            if (egg)
            {
                TeamId other = a.Team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
                trailing = m.Allegiance[other] - m.Allegiance[a.Team] >= w.Config.Bots.CorruptWhenBehindBy;
            }
            else
            {
                trailing = Corruption.TrailingTeam(w) == team && Corruption.MayFeedTeam(w, team);
            }
            if (!trailing) return;
            if (!IsOnTask(w, st, id, TaskVein) && CountTask(w, st, a.Team, id, TaskVein, TaskFeed) >= c.MaxRunners) return;
            int vein = NearestVein(w, id);
            if (vein < 0) return;
            ResourceNode nd = w.Nodes[vein];
            Offer(w, st, id, TaskVein, nd.X, nd.Z, c.RunnerUtility);
        }

        /// <summary>Acts at the task point (TaskKind 20-29); true = the frame was written.</summary>
        public static bool Act(World w, BotsState st, int id, InputFrame o)
        {
            if (!Corruption.On(w)) return false;
            int kind = st.TaskKind[id];
            Actor a = w.Actors[id];
            switch (kind)
            {
                case TaskVein:
                    if (a.Carry == CarryKind.Corruption && a.CarryAmount >= Corruption.CrystalCap(w, a.Cls))
                    {
                        st.TaskUntil[id] = 0; // full: the task ends, ThinkBot offers the feed next
                        return false;
                    }
                    Hold(w, id, o);
                    return true;
                case TaskFeed:
                case TaskPurify:
                    Hold(w, id, o);
                    return true;
                case TaskGuard:
                {
                    int team = a.Team == TeamId.Azure ? 0 : 1;
                    int carrier = EnemyCarrierNear(w, team, w.X[id], w.Z[id], 3.0);
                    if (carrier < 0) return false;
                    o.AimX = w.X[carrier];
                    o.AimZ = w.Z[carrier];
                    o.Attack = true;
                    return true;
                }
            }
            return false;
        }

        private static void Hold(World w, int id, InputFrame o)
        {
            o.MoveX = 0;
            o.MoveZ = 0;
            o.Interact = true;
        }

        private static void Offer(World w, BotsState st, int id, byte kind, double x, double z, double u)
        {
            st.TaskKind[id] = kind;
            st.TaskX[id] = x;
            st.TaskZ[id] = z;
            st.TaskUntil[id] = w.Match.Time + w.Config.Depth.Corruption.TaskSeconds;
            BotFsm.ConsiderExternal(w, AiState.Task, u);
        }

        private static bool IsOnTask(World w, BotsState st, int id, byte kind) =>
            w.Actors[id].AiState == AiState.Task && st.TaskKind[id] == kind;

        /// <summary>Teammates of team (not self) on a corruption task of kind lo..hi, or flagged for the director's corruption run when lo is the vein task.</summary>
        private static int CountTask(World w, BotsState st, TeamId team, int self, byte lo, byte hi)
        {
            int count = 0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == self) continue;
                Actor b = w.Actors[j];
                if (!b.Alive || b.Team != team) continue;
                bool on = b.AiState == AiState.Task && st.TaskKind[j] >= lo && st.TaskKind[j] <= hi;
                if (on || (lo == TaskVein && st.Corrupt[j] != 0)) count++;
            }
            return count;
        }

        /// <summary>The nearest crystal vein with charges (straight-line distance, node order on ties), or -1.</summary>
        private static int NearestVein(World w, int id)
        {
            int best = -1;
            double bestD2 = double.PositiveInfinity;
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode nd = nodes[i];
                if (nd.Kind != NodeKind.Corruption || nd.Charges <= 0) continue;
                double dx = nd.X - w.X[id];
                double dz = nd.Z - w.Z[id];
                double d2 = dx * dx + dz * dz;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The nearest living enemy crystal carrier of team teamIdx within r of (x, z), or -1.</summary>
        private static int EnemyCarrierNear(World w, int teamIdx, double x, double z, double r)
        {
            int best = -1;
            double bestD2 = r * r;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor b = w.Actors[j];
                if (!b.Alive || (b.Team == TeamId.Azure ? 0 : 1) == teamIdx) continue;
                if (b.Carry != CarryKind.Corruption || b.CarryAmount <= 0) continue;
                double dx = w.X[j] - x;
                double dz = w.Z[j] - z;
                double d2 = dx * dx + dz * dz;
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    best = j;
                }
            }
            return best;
        }

        /// <summary>A point 1 u outside the Kaiju's body on the side of actor id (inside the feed and purify reach when within 1.2 u of it).</summary>
        private static void RingPoint(World w, int id, out double x, out double z)
        {
            Kaiju k = w.Kaiju;
            double dx = w.X[id] - k.X;
            double dz = w.Z[id] - k.Z;
            double d = System.Math.Sqrt(dx * dx + dz * dz);
            double r = w.Config.Kaiju.Radius + 1.0;
            if (d < 1e-6)
            {
                dx = 0;
                dz = 1;
                d = 1;
            }
            x = k.X + dx / d * r;
            z = k.Z + dz / d * r;
        }
    }
}
