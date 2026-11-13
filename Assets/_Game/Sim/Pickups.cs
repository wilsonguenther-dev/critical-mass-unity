using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>U-066: one thing lying on the ground to walk over (a slot of the fixed pool World.Depth.Pickups).</summary>
    public sealed class Pickup
    {
        public bool Active;
        public PickupKind Kind;
        public double X;
        public double Z;
        /// <summary>Coins in the pile, or for a power-up the ticks it will last once taken (0 = the kind's full time).</summary>
        public int Value;
        /// <summary>First World.Tick it can be taken (a spawned power-up is announced PowerUpArmTicks ahead).</summary>
        public int ReadyTick;
        /// <summary>World.Tick it vanishes (int.MaxValue = never: a map coin pile waits).</summary>
        public int EndTick;
        /// <summary>Actor it dropped from (-1 = the map spawner).</summary>
        public int Source;
        /// <summary>Coin spot index it sits on (-1 = none).</summary>
        public int Spot;
    }

    /// <summary>
    /// U-066 pickups and drops (DG-39; the owner: "the skill points, the coins collected, drop item, everything"). Our rule;
    /// the browser has none.
    /// <para>COINS. A knocked-out actor drops CoinsPerKnockOut coins where it fell (anyone may take them, either team); coin
    /// piles of CoinPileValue wait on the map's coin spots and come back CoinRespawnTicks after they are taken. Walking over
    /// a pile adds it to Actor.CoinsPicked (the progression screens read it).</para>
    /// <para>POWER-UPS, one at a time per actor, each with a counter. HASTE: move x HasteSpeed for HasteTicks; Farmer roots
    /// strip it. MIGHT: melee damage x MightDamage for MightTicks; a Warrior's shield still cuts it and the carrier DROPS it
    /// when knocked out. WARD: soaks the next WardHp damage for WardTicks; any explosion goes straight through and breaks it.
    /// A knocked-out carrier drops its power-up with the time it had left (if DropMinTicks or more) for anyone to take.</para>
    /// <para>SPAWNER. Deterministic from the match seed (a stateless hash, never the shared RNG): from FirstPowerUpTicks, every
    /// PowerUpPeriodTicks, one power-up appears on one of the centre-line spots (equally far from both bases), announced
    /// PowerUpArmTicks before it can be taken (PickupSpawned), gone PowerUpLifetimeTicks later if nobody takes it.</para>
    /// <para>Bots take what they walk over; a bot in transit or in a fight within BotGrabRange and in line of sight of a
    /// power-up goes for it (AiState.Grab). A tie between two actors on the same tick goes to the lowest id on even ticks
    /// and the highest on odd ticks, so neither team is favoured.</para>
    /// Allocation-free after CreateWorld.
    /// </summary>
    public static class Pickups
    {
        /// <summary>PowerUpEnded reasons (SimEvent.Value).</summary>
        public const int EndExpired = 0, EndBroken = 1, EndDropped = 2, EndStripped = 3, EndReplaced = 4;

        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Pickups.Enabled);

        // ---------------------------------------------------------------- spots

        private static bool Open(MapDef map, int cx, int cz)
        {
            return MapGrid.InBounds(map, cx, cz) && map.Cells[cz * map.Width + cx] == Cell.Floor;
        }

        /// <summary>Nearest floor cell to the anchor whose mirror cell is floor too (rings 0..8, ties by row then column).</summary>
        private static bool Snap(MapDef map, double ax, double az, out int outCx, out int outCz)
        {
            int acx = JsMath.FloorToInt(ax);
            int acz = JsMath.FloorToInt(az);
            outCx = -1;
            outCz = -1;
            for (int r = 0; r <= 8; r++)
            {
                double best = double.PositiveInfinity;
                for (int cz = acz - r; cz <= acz + r; cz++)
                {
                    for (int cx = acx - r; cx <= acx + r; cx++)
                    {
                        if (Math.Abs(cx - acx) != r && Math.Abs(cz - acz) != r) continue;
                        if (!Open(map, cx, cz) || !Open(map, map.Width - 1 - cx, cz)) continue;
                        double d2 = (cx + 0.5 - ax) * (cx + 0.5 - ax) + (cz + 0.5 - az) * (cz + 0.5 - az);
                        if (d2 >= best) continue;
                        best = d2;
                        outCx = cx;
                        outCz = cz;
                    }
                }
                if (outCx >= 0) return true;
            }
            return false;
        }

        /// <summary>Coin spots: each west-half anchor snapped to floor, then its mirror (World.CreateWorld, once).</summary>
        public static PointXZ[] PlaceCoinSpots(GameConfig cfg, MapDef map)
        {
            var list = new List<PointXZ>();
            double[] a = cfg.Depth.Pickups.CoinAnchors;
            for (int k = 0; k + 1 < a.Length; k += 2)
            {
                if (!Snap(map, a[k], a[k + 1], out int cx, out int cz)) continue;
                list.Add(new PointXZ(cx + 0.5, cz + 0.5));
                list.Add(new PointXZ(map.Width - (cx + 0.5), cz + 0.5));
            }
            return list.ToArray();
        }

        /// <summary>Centre-line spots (x = half the map width): each z of the list snapped to the nearest row where both centre cells are floor.</summary>
        public static PointXZ[] PlaceCentreSpots(MapDef map, double[] zs)
        {
            var list = new List<PointXZ>();
            int half = map.Width / 2;
            for (int k = 0; k < zs.Length; k++)
            {
                int az = JsMath.FloorToInt(zs[k]);
                for (int r = 0; r <= 8; r++)
                {
                    int found = -1;
                    if (Open(map, half - 1, az - r) && Open(map, half, az - r)) found = az - r;
                    else if (Open(map, half - 1, az + r) && Open(map, half, az + r)) found = az + r;
                    if (found < 0) continue;
                    list.Add(new PointXZ(half, found + 0.5));
                    break;
                }
            }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- pool and events

        private static void Push(World w, SimEventType type, int a, int b, double x, double z, double value, int slot)
        {
            World.PushEvent(w, type, a, b, x, z, value, null, DamageCause.None, w.Config.Depth.Pickups.PickupRange);
            w.Events[(w.EventHead - 1) % World.EventCapacity].Slot = slot;
        }

        /// <summary>
        /// Put a pickup on the ground: first free slot, else the dropped pickup that ends soonest is recycled (lowest slot on a
        /// tie; a map pile or announced power-up is never recycled). One PickupSpawned event: A = source actor (-1 the map),
        /// B = kind, (X, Z), Value = coins or (power-up) the tick it can be taken, Radius = pickup range, Slot = pool slot.
        /// Returns the slot, or -1 when the pool is full of things that may not be recycled.
        /// </summary>
        public static int Spawn(World w, PickupKind kind, double x, double z, int value, int readyTick, int endTick, int source, int spot)
        {
            Pickup[] pool = w.Depth.Pickups;
            int slot = -1;
            int soonest = int.MaxValue;
            int recycle = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                Pickup p = pool[i];
                if (!p.Active)
                {
                    slot = i;
                    break;
                }
                if (p.Source >= 0 && p.EndTick < soonest)
                {
                    soonest = p.EndTick;
                    recycle = i;
                }
            }
            if (slot < 0)
            {
                if (recycle < 0) return -1;
                slot = recycle;
                w.Depth.PickupOverflows++;
            }
            Pickup n = pool[slot];
            n.Active = true;
            n.Kind = kind;
            n.X = x;
            n.Z = z;
            n.Value = value;
            n.ReadyTick = readyTick;
            n.EndTick = endTick;
            n.Source = source;
            n.Spot = spot;
            Push(w, SimEventType.PickupSpawned, source, (int)kind, x, z, kind == PickupKind.Coin ? value : readyTick, slot);
            return slot;
        }

        // ---------------------------------------------------------------- power-ups on actors

        public static PickupKind PowerOf(World w, int id) => (PickupKind)w.Depth.PowerKind[id];

        /// <summary>Full duration of a power-up kind in ticks.</summary>
        public static int FullTicks(GameConfig c, PickupKind kind)
        {
            PickupsConfig P = c.Depth.Pickups;
            return Durations.T30(c, kind == PickupKind.Haste ? P.HasteTicks : kind == PickupKind.Might ? P.MightTicks : P.WardTicks);
        }

        /// <summary>Actor id's power-up ends: one PowerUpEnded event (A = actor, B = kind, at the actor, Value = an End* reason). Nothing when it has none.</summary>
        public static void End(World w, int id, int reason)
        {
            DepthState d = w.Depth;
            if (d.PowerKind[id] == 0) return;
            int kind = d.PowerKind[id];
            d.PowerKind[id] = 0;
            d.PowerEndTick[id] = 0;
            d.WardLeft[id] = 0;
            World.PushEvent(w, SimEventType.PowerUpEnded, id, kind, w.X[id], w.Z[id], reason);
        }

        /// <summary>Give actor id a power-up for ticks (0 = the kind's full time); one it already holds is replaced.</summary>
        public static void Give(World w, int id, PickupKind kind, int ticks)
        {
            if (kind == PickupKind.Coin) return;
            DepthState d = w.Depth;
            End(w, id, EndReplaced);
            d.PowerKind[id] = (byte)kind;
            d.PowerEndTick[id] = w.Tick + (ticks > 0 ? ticks : FullTicks(w.Config, kind));
            d.WardLeft[id] = kind == PickupKind.Ward ? w.Config.Depth.Pickups.WardHp : 0;
        }

        /// <summary>Movement: 1, or HasteSpeed while actor id holds Haste and is not rooted.</summary>
        public static double SpeedScale(World w, int id)
        {
            if (w.Config.Depth.Classic || w.Depth.PowerKind[id] != (byte)PickupKind.Haste || w.Actors[id].SlowTimer > 0) return 1;
            return w.Config.Depth.Pickups.HasteSpeed;
        }

        /// <summary>Combat: 1, or MightDamage while actor id holds Might.</summary>
        public static double DamageScale(World w, int id)
        {
            if (w.Config.Depth.Classic || w.Depth.PowerKind[id] != (byte)PickupKind.Might) return 1;
            return w.Config.Depth.Pickups.MightDamage;
        }

        /// <summary>
        /// Damage: what is left of dmg after target's Ward. An explosion is not soaked and breaks the ward. Soaked damage
        /// pushes one WardAbsorb event (A = target, B = attacker id or -1, at the target, Value = damage soaked).
        /// </summary>
        public static double Absorb(World w, int target, double dmg, DamageCause cause, int attacker)
        {
            if (w.Config.Depth.Classic) return dmg;
            DepthState d = w.Depth;
            if (d.PowerKind[target] != (byte)PickupKind.Ward) return dmg;
            if (cause == DamageCause.Explosion)
            {
                End(w, target, EndBroken);
                return dmg;
            }
            double soak = d.WardLeft[target] < dmg ? d.WardLeft[target] : dmg;
            d.WardLeft[target] -= soak;
            World.PushEvent(w, SimEventType.WardAbsorb, target, attacker, w.X[target], w.Z[target], soak);
            if (d.WardLeft[target] <= 1e-9) End(w, target, EndBroken);
            return dmg - soak;
        }

        // ---------------------------------------------------------------- the step

        private static void Take(World w, int slot, int id)
        {
            DepthState d = w.Depth;
            Pickup p = d.Pickups[slot];
            Actor a = w.Actors[id];
            p.Active = false;
            if (p.Kind == PickupKind.Coin)
            {
                a.CoinsPicked += p.Value;
                d.CoinsTaken += p.Value;
                if (p.Spot >= 0) d.CoinSpotNextTick[p.Spot] = w.Tick + Durations.T30(w.Config, w.Config.Depth.Pickups.CoinRespawnTicks);
                Push(w, SimEventType.PickupTaken, id, (int)p.Kind, p.X, p.Z, p.Value, slot);
                return;
            }
            Give(w, id, p.Kind, p.Value);
            d.PowerUpsTaken++;
            Push(w, SimEventType.PickupTaken, id, (int)p.Kind, p.X, p.Z, d.PowerEndTick[id] - w.Tick, slot);
        }

        /// <summary>One step of pickups (SimStep, after combat): power-up timers, drops from the knocked out, the spawners, expiry, then who takes what.</summary>
        public static void PickupsSystem(World w)
        {
            if (!On(w)) return;
            DepthState d = w.Depth;
            PickupsConfig P = w.Config.Depth.Pickups;
            int n = w.ActorCount;
            int tick = w.Tick;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (a.Alive && d.PowerKind[id] != 0)
                {
                    if (tick >= d.PowerEndTick[id]) End(w, id, EndExpired);
                    else if (d.PowerKind[id] == (byte)PickupKind.Haste && a.SlowTimer > 0) End(w, id, EndStripped);
                }
                if (!a.Alive && d.WasAlive[id] != 0)
                {
                    // Knocked out since last step: coins where it fell, and its power-up with the time it had left.
                    if (P.CoinsPerKnockOut > 0)
                        Spawn(w, PickupKind.Coin, w.X[id], w.Z[id], (int)P.CoinsPerKnockOut, tick, tick + Durations.T30(w.Config, P.CoinDropLifetimeTicks), id, -1);
                    if (d.PowerKind[id] != 0)
                    {
                        PickupKind kind = (PickupKind)d.PowerKind[id];
                        int left = d.PowerEndTick[id] - tick;
                        End(w, id, EndDropped);
                        if (left >= Durations.T30(w.Config, P.DropMinTicks))
                        {
                            Spawn(w, kind, w.X[id], w.Z[id], left, tick, tick + Durations.T30(w.Config, P.DropLifetimeTicks), id, -1);
                            d.PowerUpsDropped++;
                        }
                    }
                }
                d.WasAlive[id] = (byte)(a.Alive ? 1 : 0);
            }

            for (int s = 0; s < d.CoinSpots.Length; s++)
            {
                if (d.CoinSpotNextTick[s] > tick) continue;
                d.CoinSpotNextTick[s] = int.MaxValue;
                if (Spawn(w, PickupKind.Coin, d.CoinSpots[s].X, d.CoinSpots[s].Z, (int)P.CoinPileValue, tick, int.MaxValue, -1, s) < 0)
                    d.CoinSpotNextTick[s] = tick + 30;
            }
            if (d.PowerSpots.Length > 0 && tick >= d.NextPowerTick)
            {
                int k = d.PowerIndex++;
                d.NextPowerTick += Durations.T30(w.Config, P.PowerUpPeriodTicks);
                PointXZ spot = d.PowerSpots[DepthRules.HashInt(w.Opts.Seed, k, 11, d.PowerSpots.Length)];
                PickupKind kind = (PickupKind)(1 + DepthRules.HashInt(w.Opts.Seed, k, 12, 3));
                bool taken = false;
                for (int i = 0; i < d.Pickups.Length; i++)
                {
                    Pickup q = d.Pickups[i];
                    if (q.Active && q.Source < 0 && q.Kind != PickupKind.Coin && q.X == spot.X && q.Z == spot.Z) taken = true;
                }
                if (!taken)
                {
                    int ready = tick + Durations.T30(w.Config, P.PowerUpArmTicks);
                    Spawn(w, kind, spot.X, spot.Z, 0, ready, ready + Durations.T30(w.Config, P.PowerUpLifetimeTicks), -1, -1);
                }
            }

            Pickup[] pool = d.Pickups;
            bool up = (tick & 1) == 0;
            for (int i = 0; i < pool.Length; i++)
            {
                Pickup p = pool[i];
                if (!p.Active) continue;
                if (tick >= p.EndTick)
                {
                    p.Active = false;
                    Push(w, SimEventType.PickupExpired, -1, (int)p.Kind, p.X, p.Z, p.Value, i);
                    continue;
                }
                if (tick < p.ReadyTick) continue;
                for (int c = 0; c < n; c++)
                {
                    int id = up ? c : n - 1 - c;
                    Actor a = w.Actors[id];
                    if (!a.Alive || a.StandUp > 0) continue;
                    double reach = P.PickupRange + w.Config.Class(a.Cls).Radius;
                    double dx = w.X[id] - p.X;
                    double dz = w.Z[id] - p.Z;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    Take(w, i, id);
                    break;
                }
            }
        }

        /// <summary>
        /// Bots: the power-up bot id should go for, if any: it holds none, and an announced or ready power-up lies within
        /// BotGrabRange with a clear straight line to it; nearest first, lowest slot on a tie.
        /// </summary>
        public static bool BotGrabSpot(World w, int id, out double gx, out double gz)
        {
            gx = 0;
            gz = 0;
            DepthState d = w.Depth;
            if (d.PowerKind[id] != 0) return false;
            double r = w.Config.Depth.Pickups.BotGrabRange;
            double best = r * r;
            bool found = false;
            Pickup[] pool = d.Pickups;
            for (int i = 0; i < pool.Length; i++)
            {
                Pickup p = pool[i];
                if (!p.Active || p.Kind == PickupKind.Coin) continue;
                double dx = p.X - w.X[id];
                double dz = p.Z - w.Z[id];
                double d2 = dx * dx + dz * dz;
                if (d2 >= best) continue;
                if (Bots.BotSteer.FirstBlockedCell(w.Map, w.X[id], w.Z[id], p.X, p.Z) >= 0) continue;
                best = d2;
                gx = p.X;
                gz = p.Z;
                found = true;
            }
            return found;
        }
    }

    /// <summary>U-066 on-screen names, in ONE place for the owner to rename (index = (int)PickupKind). Data only.</summary>
    public static class PickupNames
    {
        public static readonly string[] Name = { "COINS", "HASTE", "MIGHT", "WARD" };
        public static readonly string[] Counter =
        {
            "",
            "Roots strip it.",
            "Shields cut it. Knock the carrier out to take it.",
            "Explosions go straight through.",
        };
    }
}
