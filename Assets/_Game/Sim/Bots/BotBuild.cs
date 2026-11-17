// CM b13 item B (Builder bots build walls and blocks). Numbers in Sim/Config/BuildConfig.cs.
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// CM b13 item B: Builder bots build walls and blocks. Three rules, checked at think time, each queuing ONE F press that the
    /// sim would accept now (BuilderWalls.QueueBotPress; BotBrain.FillBotInput presses it through Demolitions.TakeBotTnt):
    /// (2) own HP under BotGuardHpShare and an enemy within BotGuardRange: a SHORT wall BotGuardDistance toward it (once per
    /// 90 ticks); (1) the team holds the hill and BotHillEnemies+ living enemies are within BotHillEnemyRange of its centre: a
    /// wall (tall when the tier allows) on the hill edge (radius + BotHillEdgeExtra) toward the enemy centroid, at most once per
    /// BotHillEveryTicks (a refused try retries after 30 ticks); (3) the bot is in Unstick beside a Void cell: a BLOCK into it
    /// (the cell that leads toward the hill). False lets today's ladder / bridge logic run.
    /// </summary>
    public static class BotBuild
    {
        /// <summary>Ticks between two rule 2 (guard) tries.</summary>
        public const int GuardEveryTicks = 90;
        /// <summary>Ticks before a refused rule 1 try may run again.</summary>
        public const int HillRetryTicks = 30;

        /// <summary>The Builder's decision; true = a build press was queued (BotFsm.DecideBuilder skips the rest).</summary>
        public static bool DecideBuilder(World w, BotsState st, int id, AiState s)
        {
            if (!BuilderWalls.On(w)) return false;
            Actor a = w.Actors[id];
            if (!a.Alive || a.StandUp > 0 || a.Cls != ClassId.Builder) return false;
            WallsState ws = w.Walls;
            if (ws.BotReq[id] == 1 || w.Tick < ws.ReadyTick[id]) return false;
            BuildConfig b = w.Config.Depth.Build;
            if (s == AiState.Retreat && !(a.Hp < b.BotGuardHpShare * a.MaxHp)) return false;
            TeamId team = a.Team;
            int tier = BuilderWalls.Tier(w, team);
            double x = w.X[id];
            double z = w.Z[id];

            // (2) Hurt and pressed: a short wall between him and the nearest enemy.
            if (a.Hp < b.BotGuardHpShare * a.MaxHp && w.Tick - ws.BotGuardTick[id] >= GuardEveryTicks)
            {
                int e = -1;
                double best = b.BotGuardRange * b.BotGuardRange;
                for (int j = 0; j < w.ActorCount; j++)
                {
                    Actor o = w.Actors[j];
                    if (!o.Alive || o.Team == team) continue;
                    double dx = w.X[j] - x, dz = w.Z[j] - z, d2 = dx * dx + dz * dz;
                    if (d2 < best) { best = d2; e = j; }
                }
                if (e >= 0)
                {
                    ws.BotGuardTick[id] = w.Tick;
                    double d = Math.Sqrt(best);
                    if (d > 1e-6)
                    {
                        double tx = x + (w.X[e] - x) / d * b.BotGuardDistance;
                        double tz = z + (w.Z[e] - z) / d * b.BotGuardDistance;
                        if (BuilderWalls.QueueBotPress(w, id, tx, tz, BuildPiece.Wall, false)) return true;
                    }
                }
            }

            // (1) Holding the hill against a crowd: a wall on the hill edge facing them.
            HillState own = team == TeamId.Azure ? HillState.Azure : HillState.Crimson;
            if (w.Match.HillState == own && w.Tick - ws.BotHillTick[id] >= Durations.T30(w.Config, b.BotHillEveryTicks))
            {
                HillDef h = w.Map.Hill;
                double r2 = b.BotHillEnemyRange * b.BotHillEnemyRange;
                int count = 0;
                double sx = 0, sz = 0;
                for (int j = 0; j < w.ActorCount; j++)
                {
                    Actor o = w.Actors[j];
                    if (!o.Alive || o.Team == team) continue;
                    double dx = w.X[j] - h.X, dz = w.Z[j] - h.Z;
                    if (dx * dx + dz * dz > r2) continue;
                    count++;
                    sx += w.X[j];
                    sz += w.Z[j];
                }
                if (count >= b.BotHillEnemies)
                {
                    double cx = sx / count - h.X, cz = sz / count - h.Z;
                    double d = Math.Sqrt(cx * cx + cz * cz);
                    if (d > 1e-6)
                    {
                        double edge = w.Config.Hill.Radius + b.BotHillEdgeExtra;
                        double tx = h.X + cx / d * edge;
                        double tz = h.Z + cz / d * edge;
                        double ox = tx - x, oz = tz - z;
                        if (ox * ox + oz * oz <= (b.Reach + 0.25) * (b.Reach + 0.25))
                        {
                            if (BuilderWalls.QueueBotPress(w, id, tx, tz, BuildPiece.Wall, tier >= 1))
                            {
                                ws.BotHillTick[id] = w.Tick;
                                return true;
                            }
                            ws.BotHillTick[id] = w.Tick - Durations.T30(w.Config, b.BotHillEveryTicks) + HillRetryTicks;
                        }
                    }
                }
            }

            // (3) Stuck beside a hole: fill the one toward the hill.
            if (s == AiState.Unstick)
            {
                MapDef map = w.Map;
                int bx = JsMath.FloorToInt(x);
                int bz = JsMath.FloorToInt(z);
                double gx = map.Hill.X - x, gz = map.Hill.Z - z;
                int pick = -1;
                double bestDot = double.NegativeInfinity;
                for (int k = 0; k < 4; k++)
                {
                    int dx = k == 0 ? 1 : k == 1 ? -1 : 0;
                    int dz = k == 2 ? 1 : k == 3 ? -1 : 0;
                    int cx = bx + dx, cz = bz + dz;
                    if (!MapGrid.InBounds(map, cx, cz) || map.Cells[cz * map.Width + cx] != Cell.Void) continue;
                    double dot = dx * gx + dz * gz;
                    if (dot > bestDot) { bestDot = dot; pick = k; }
                }
                if (pick >= 0)
                {
                    int dx = pick == 0 ? 1 : pick == 1 ? -1 : 0;
                    int dz = pick == 2 ? 1 : pick == 3 ? -1 : 0;
                    if (BuilderWalls.QueueBotPress(w, id, bx + dx + 0.5, bz + dz + 0.5, BuildPiece.Block, false)) return true;
                }
            }
            return false;
        }
    }
}
