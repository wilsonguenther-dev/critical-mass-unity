using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Respawn system (CM-06, port of src/sim/respawn.ts on 09e0904): dead actors count down RespawnIn, then come back at a free spawn slot around their own pad with full hp and spawn protection; class re-pick arrives only through InputFrame.ClassPick.</summary>
    public static class Respawn
    {
        /// <summary>CM-84 / U-065: ClassSwitch event outcome codes (SimEvent.B), CM-54's values.</summary>
        public const int ClassSwitchAccepted = 0, ClassSwitchCooldown = 1, ClassSwitchAlive = 2;

        /// <summary>Two living actors closer than this (centre to centre) make a spawn slot "taken". 2 x the largest class radius.</summary>
        private static double SlotClearance(GameConfig c)
        {
            return 2 * Math.Max(Math.Max(c.Class(ClassId.Warrior).Radius, c.Class(ClassId.Farmer).Radius), c.Class(ClassId.Miner).Radius);
        }

        /// <summary>Outward cell-range tolerance, same rule as collide.ts (D-012).</summary>
        private const double RangeEps = 1e-6;

        /// <summary>SpawnPoint output scratch of ChooseSpawn.</summary>
        private static double SlotScratchX;
        private static double SlotScratchZ;

        /// <summary>Queue a class change for the next respawn; returns whether it is (now) queued. Refused while the actor is alive.</summary>
        public static bool RequestClassChange(World w, int actorId, ClassId cls)
        {
            if (!(actorId >= 0 && actorId < w.ActorCount)) return false;
            int ci = (int)cls;
            if (ci < 0 || ci >= ClassIds.All.Count) return false;
            Actor a = w.Actors[actorId];
            if (a.Alive)
            {
                // CM-84 / U-065: with the station rule on a living actor switches at its base station (else a ClassSwitch
                // Alive refusal, CM-54); with it off a living pick is dropped silently, as before.
                if (Stations.On(w)) return Stations.TrySwitchAlive(w, actorId, cls);
                return false;
            }
            if ((a.PendingClass ?? a.Cls) == cls) return true;
            bool station = Stations.On(w); // CM-84 / U-065: CM-54's cooldown, counter and event ride with the station rule
            if (station && a.ClassSwitchCd > 0)
            {
                World.PushEvent(w, SimEventType.ClassSwitch, actorId, ClassSwitchCooldown, w.X[actorId], w.Z[actorId], ci);
                return false;
            }
            a.PendingClass = cls;
            if (station)
            {
                a.ClassSwitchCd = (int)w.Config.Respawn.ClassSwitchCooldownTicks;
                a.ClassSwitches++;
                World.PushEvent(w, SimEventType.ClassSwitch, actorId, ClassSwitchAccepted, w.X[actorId], w.Z[actorId], ci);
            }
            return true;
        }

        /// <summary>CM-84 / U-065 (CM-54 teamClassSwitches): accepted switches by the non-human actors of team.</summary>
        public static int TeamClassSwitches(World w, TeamId team)
        {
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++) if (!w.Actors[i].IsHuman && w.Actors[i].Team == team) n += w.Actors[i].ClassSwitches;
            return n;
        }

        /// <summary>The actor's bounding square (side 2r) at the Float32 position touches only walkable cells (collide.ts invariant).</summary>
        private static bool SquareClear(World w, double x, double z, double r)
        {
            int x0 = JsMath.FloorToInt(x - r - RangeEps);
            int x1 = JsMath.FloorToInt(x + r + RangeEps);
            int z0 = JsMath.FloorToInt(z - r - RangeEps);
            int z1 = JsMath.FloorToInt(z + r + RangeEps);
            for (int cz = z0; cz <= z1; cz++) for (int cx = x0; cx <= x1; cx++) if (!MapGrid.IsWalkable(w.Map, cx, cz)) return false;
            return true;
        }

        /// <summary>No living actor other than self stands within SlotClearance of (x, z).</summary>
        private static bool SlotFree(World w, int self, double x, double z)
        {
            double slotClearance = SlotClearance(w.Config);
            double lim2 = slotClearance * slotClearance;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == self || !w.Actors[j].Alive) continue;
                double dx = w.X[j] - x;
                double dz = w.Z[j] - z;
                if (dx * dx + dz * dz < lim2) return false;
            }
            return true;
        }

        /// <summary>Spawn point for id: the CreateWorld spiral around its pad from its own team slot, first slot clear of walls and living actors; else the first wall-clear slot; else the pad centre.</summary>
        private static void ChooseSpawn(World w, int id, double r, out double outX, out double outZ)
        {
            TeamId team = w.Actors[id].Team;
            int start = World.TeamSlot(w, id);
            int actorCapacity = World.ActorCapacity(w.Config);
            double fallbackX = double.NaN;
            double fallbackZ = double.NaN;
            for (int k = 0; k < actorCapacity; k++)
            {
                World.SpawnPoint(w.Map, team, (start + k) % actorCapacity, out SlotScratchX, out SlotScratchZ);
                double x = JsMath.F32(SlotScratchX);
                double z = JsMath.F32(SlotScratchZ);
                if (!SquareClear(w, x, z, r)) continue;
                if (double.IsNaN(fallbackX))
                {
                    fallbackX = x;
                    fallbackZ = z;
                }
                if (SlotFree(w, id, x, z))
                {
                    outX = x;
                    outZ = z;
                    return;
                }
            }
            if (!double.IsNaN(fallbackX))
            {
                outX = fallbackX;
                outZ = fallbackZ;
                return;
            }
            // Every slot blocked by walls (cannot happen on Skybreak Basin): use the pad centre.
            PadDef pad = w.Map.Pads[(int)team];
            outX = pad.X;
            outZ = pad.Z;
        }

        /// <summary>ChooseSpawn output scratch of RespawnSystem.</summary>
        private static double SpawnScratchX;
        private static double SpawnScratchZ;

        /// <summary>Consume every ClassPick, tick the post-respawn StandUp of living actors, and bring back every dead actor whose RespawnIn ran out.</summary>
        public static void RespawnSystem(World w, double dt)
        {
            GameConfig cfg = w.Config;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                InputFrame inp = w.Inputs[i];
                if (a.ClassSwitchCd > 0) a.ClassSwitchCd--; // CM-84 / U-065 (CM-54 R2): before this step's pick
                int pick = inp.ClassPick;
                if (pick >= 0)
                {
                    if (pick < ClassIds.All.Count) RequestClassChange(w, i, ClassIds.All[pick]);
                    inp.ClassPick = -1; // one-shot: a pick sent while alive is dropped, never replayed at the next death
                }
                if (a.Alive)
                {
                    // CM-20: the get-up runs out at the end of the step, so the next StandUpSeconds * SimHz steps see StandUp > 0.
                    if (a.StandUp > 0) a.StandUp = Combat.TickDown(a.StandUp, dt);
                    continue;
                }

                a.RespawnIn -= dt;
                if (a.RespawnIn > Combat.TimerEps) continue;

                if (a.PendingClass.HasValue)
                {
                    a.Cls = a.PendingClass.Value;
                    a.PendingClass = null;
                }
                ClassConfig cc = cfg.Class(a.Cls);
                a.MaxHp = Stations.MaxHp(w, a.Team, a.Cls); // U-065: the team's upgrade level of the class (the class row with the rule off)
                a.Hp = a.MaxHp;
                a.Alive = true;
                a.RespawnIn = 0;
                a.SpawnProtect = cfg.Respawn.ProtectionSeconds;
                a.StandUp = cfg.Respawn.StandUpSeconds;
                a.Carry = CarryKind.None;
                a.CarryAmount = 0;
                a.GatherProgress = 0;
                a.GatherNodeId = -1;
                a.AttackCd = 0;
                a.AbilityActive = 0;
                a.SlowTimer = 0;
                a.LastAttacker = -1;
                a.LastCause = DamageCause.None;
                a.LostCargo.Kind = CarryKind.None;
                a.LostCargo.Amount = 0;
                a.Moving = false;
                a.AiState = AiState.None;

                ChooseSpawn(w, i, cc.Radius, out SpawnScratchX, out SpawnScratchZ);
                w.X[i] = JsMath.F32(SpawnScratchX);
                w.Z[i] = JsMath.F32(SpawnScratchZ);
                w.PrevX[i] = w.X[i]; // no interpolation streak from the death spot
                w.PrevZ[i] = w.Z[i];
                HillDef hill = w.Map.Hill;
                a.Facing = JsMath.Atan2(hill.Z - w.Z[i], hill.X - w.X[i]);
                // Like a fresh spawn: no aim until the next input frame provides one (D-011).
                w.HasAim[i] = 0;
                inp.AimX = w.X[i];
                inp.AimZ = w.Z[i];
                World.PushEvent(w, SimEventType.Respawn, i, -1, w.X[i], w.Z[i], (int)a.Cls);
            }
        }
    }
}
