// CM b13 item H (Miner and the Blacksmith hut; Jeisil's play-test review). Every number lives in Sim/Config/HutConfig.cs.
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>CM b13 item H: the Blacksmith hut state on the World (World.Hut). Fixed arrays, sized once; reset in place.</summary>
    public sealed class HutState
    {
        /// <summary>Per team: hut level 0..5, ore in the stock toward the next level, ore banked in all, ore mined in all.</summary>
        public int[] Level = new int[2], Ore = new int[2], Banked = new int[2], Mined = new int[2];
        /// <summary>Per actor: the vein being channelled (-1 none), channel ticks done, HP last tick (hit test), HAUL ticks left, ore mined.</summary>
        public int[] ChannelVein, ChannelTicks, HaulLeft, MinedBy;
        public double[] PrevHp;
        /// <summary>Bot routes to each team's hut (the pad): built on first use, rebuilt when walls changed (one per tick).</summary>
        public FlowField[] PadField = new FlowField[2];
        public int[] PadFieldVersion = { -1, -1 };
        public int FieldBuildTick = -1;
        public readonly PointXZ[] Goal = new PointXZ[1];
        /// <summary>The ore-vein routes were built (first HutSystem tick: no allocation later).</summary>
        public bool Warm;

        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w)
        {
            int cap = World.ActorCapacity(w.Config);
            ChannelVein = new int[cap];
            ChannelTicks = new int[cap];
            HaulLeft = new int[cap];
            MinedBy = new int[cap];
            PrevHp = new double[cap];
            Clear();
        }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) => Clear();

        void Clear()
        {
            Array.Clear(Level, 0, 2);
            Array.Clear(Ore, 0, 2);
            Array.Clear(Banked, 0, 2);
            Array.Clear(Mined, 0, 2);
            if (ChannelVein == null) return;
            Array.Fill(ChannelVein, -1);
            Array.Clear(ChannelTicks, 0, ChannelTicks.Length);
            Array.Clear(HaulLeft, 0, HaulLeft.Length);
            Array.Clear(MinedBy, 0, MinedBy.Length);
            Array.Clear(PrevHp, 0, PrevHp.Length);
            PadFieldVersion[0] = -1;
            PadFieldVersion[1] = -1;
            FieldBuildTick = -1;
        }
    }

    /// <summary>
    /// CM b13 item H: the Miner only mines, and each team's Blacksmith hut levels 0 to 5 on the ore its Miners bank.
    /// <para>MINE (F, B13Hooks.TakeTerraformPress): the nearest ore vein (a SourceNode of Kind Ore) within MinerTerraform.Reach,
    /// toward his aim when two are in reach. One press starts a MineTicks channel; holding F keeps channelling unit after
    /// unit; a hit restarts the channel (as Sources' gather progress). Each channel moves one charge into his cargo
    /// (CarryKind.Ore, up to OreCarry). Every Miner F press is taken while the rule is on, so Terraform never digs or places
    /// for him; a refused press pushes OreMined with a negative Refused* code (the Terraform / Ability refusal style).</para>
    /// <para>HAUL (Space, B13Hooks.TakeAbilityPress): HaulTicks of HaulSpeed and no carry slowdown, on his class AbilityCooldown.
    /// Barricades never see a Miner press while the rule is on.</para>
    /// <para>THE HUT is the team's station zone (Stations.InStation). E there with ore banks his whole load (Sources' bank
    /// press, its edge); while the stock covers the next LevelCost the hut levels (HutLevel event). Each level: +StatPerLevel
    /// max HP (Stations.MaxHp, at once for living teammates and at every spawn), damage dealt and healing (B13Hooks).
    /// Deaths never change the level. The station's class-level ladder is off while this rule is on.</para>
    /// Deterministic (actor-id order, no RNG), allocation-free after the first tick.
    /// </summary>
    public static class Blacksmith
    {
        /// <summary>OreMined refusal codes (Value &lt; 0): no vein with ore in reach, cargo full, other cargo, hut at max, getting up.</summary>
        public const int RefusedNoVein = -1, RefusedFull = -2, RefusedOtherCargo = -3, RefusedHutMax = -4, RefusedBusy = -5;

        /// <summary>The rule is on (Full profile and HutConfig.On).</summary>
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Hut.On);

        /// <summary>Levels the hut can reach (the LevelCost row length: 5).</summary>
        public static int MaxLevel(GameConfig c) => c.Depth.Hut.LevelCost.Length;

        /// <summary>The team's hut level, 0..5.</summary>
        public static int Level(World w, TeamId team) => On(w) ? w.Hut.Level[(int)team] : 0;

        /// <summary>Ore in the team's hut stock toward the next level.</summary>
        public static int OreBanked(World w, TeamId team) => On(w) ? w.Hut.Ore[(int)team] : 0;

        /// <summary>Ore the next level costs (0 at max).</summary>
        public static int NextCost(World w, TeamId team)
        {
            if (!On(w)) return 0;
            int l = w.Hut.Level[(int)team];
            double[] cost = w.Config.Depth.Hut.LevelCost;
            return l < cost.Length ? (int)cost[l] : 0;
        }

        /// <summary>1 + StatPerLevel x Level: max HP, damage dealt and healing of every class of the team.</summary>
        public static double StatScale(World w, TeamId team)
        {
            if (!On(w)) return 1.0;
            int l = w.Hut.Level[(int)team];
            return l <= 0 ? 1.0 : 1 + w.Config.Depth.Hut.StatPerLevel * l;
        }

        /// <summary>Max HP of a class row at a hut level: the row at level 0, else round(row x StatScale) (as Stations rounds).</summary>
        public static double MaxHpAt(GameConfig c, ClassId cls, int level)
        {
            double b = c.Class(cls).MaxHp;
            if (level <= 0) return b;
            return Math.Floor(b * (1 + c.Depth.Hut.StatPerLevel * level) + 0.5);
        }

        /// <summary>The Miner's HAUL speed multiplier for actor id (1 = none). Cancels a carry slowdown (C's crystal) while it runs.</summary>
        public static double SpeedScale(World w, int id)
        {
            if (!On(w) || id < 0 || id >= w.ActorCount || w.Hut.HaulLeft[id] <= 0) return 1.0;
            double s = w.Config.Depth.Hut.HaulSpeed;
            double carry = Corruption.SpeedScale(w, id);
            return carry > 0 && carry < 1 ? s / carry : s;
        }

        /// <summary>HAUL ticks left for actor id (views and HUD; 0 = none).</summary>
        public static int HaulTicksLeft(World w, int id) => On(w) && id >= 0 && id < w.ActorCount ? w.Hut.HaulLeft[id] : 0;

        /// <summary>The vein actor id is channelling (-1 none).</summary>
        public static int ChannelVein(World w, int id) => On(w) && id >= 0 && id < w.ActorCount ? w.Hut.ChannelVein[id] : -1;

        /// <summary>The channel's progress 0..1 for actor id, -1 when not channelling (views).</summary>
        public static double ChannelPhase(World w, int id)
        {
            if (ChannelVein(w, id) < 0) return -1;
            return Math.Min(1.0, (double)w.Hut.ChannelTicks[id] / MineTicks(w.Config));
        }

        public static int MineTicks(GameConfig c) => Math.Max(1, Durations.T30(c, c.Depth.Hut.MineTicks));

        /// <summary>Ore actor id carries (0 when its cargo is another kind).</summary>
        public static int OreOf(Actor a) => a.Carry == CarryKind.Ore ? a.CarryAmount : 0;

        static bool Reaches(World w, int id, SourceNode s)
        {
            double r = w.Config.Abilities.MinerTerraform.Reach;
            double dx = s.X - w.X[id];
            double dz = s.Z - w.Z[id];
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>The ore vein with charges actor id would mine now: in reach, toward his aim (facing with no aim) if two are; -1 none.</summary>
        public static int VeinInReach(World w, int id)
        {
            SourceNode[] list = w.Depth.Sources;
            double ax, az;
            if (w.HasAim[id] == 1 && double.IsFinite(w.Inputs[id].AimX) && double.IsFinite(w.Inputs[id].AimZ))
            {
                ax = w.Inputs[id].AimX - w.X[id];
                az = w.Inputs[id].AimZ - w.Z[id];
            }
            else
            {
                ax = JsMath.Cos(w.Actors[id].Facing);
                az = JsMath.Sin(w.Actors[id].Facing);
            }
            double al = Math.Sqrt(ax * ax + az * az);
            if (al > 1e-9) { ax /= al; az /= al; }
            int best = -1;
            double bestDot = double.NegativeInfinity;
            for (int i = 0; i < list.Length; i++)
            {
                SourceNode s = list[i];
                if (s.Kind != BiomassType.Ore || s.Charges <= 0 || !Reaches(w, id, s)) continue;
                double dx = s.X - w.X[id];
                double dz = s.Z - w.Z[id];
                double d = Math.Sqrt(dx * dx + dz * dz);
                double dot = d > 1e-9 ? (dx * ax + dz * az) / d : 1;
                if (dot > bestDot + 1e-9)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>0 when Miner id may start mining vein v (or -1 = look one up), else a Refused* code.</summary>
        static int Refusal(World w, int id, out int vein)
        {
            Actor a = w.Actors[id];
            vein = -1;
            if (!a.Alive || a.StandUp > 0 || a.AirLen > 0) return RefusedBusy;
            if (a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Ore) return RefusedOtherCargo;
            if (OreOf(a) >= (int)w.Config.Depth.Hut.OreCarry) return RefusedFull;
            if (w.Hut.Level[(int)a.Team] >= MaxLevel(w.Config)) return RefusedHutMax;
            vein = VeinInReach(w, id);
            return vein < 0 ? RefusedNoVein : 0;
        }

        static void Refuse(World w, int id, int code)
        {
            World.PushEvent(w, SimEventType.OreMined, id, -1, w.X[id], w.Z[id], code, BiomassType.Ore, DamageCause.None);
        }

        /// <summary>A Miner's F press (rising edge): true = taken by the hut rule (every press while on), so Terraform skips it.</summary>
        public static bool TakeMinerPress(World w, int id)
        {
            if (!On(w) || id < 0 || id >= w.ActorCount || w.Actors[id].Cls != ClassId.Miner) return false;
            HutState h = w.Hut;
            int code = Refusal(w, id, out int vein);
            if (code != 0)
            {
                h.ChannelVein[id] = -1;
                h.ChannelTicks[id] = 0;
                Refuse(w, id, code);
                return true;
            }
            h.ChannelVein[id] = vein;
            h.ChannelTicks[id] = 0;
            return true;
        }

        /// <summary>A Miner's ability press (cooldown spent): HAUL. True = taken (every press while on), so Abilities skips it.</summary>
        public static bool TakeMinerAbility(World w, int id)
        {
            if (!On(w) || id < 0 || id >= w.ActorCount || w.Actors[id].Cls != ClassId.Miner) return false;
            Actor a = w.Actors[id];
            w.Hut.HaulLeft[id] = Math.Max(1, Durations.T30(w.Config, w.Config.Depth.Hut.HaulTicks));
            a.AbilityCd = w.Config.Class(ClassId.Miner).AbilityCooldown;
            return true;
        }

        /// <summary>
        /// One bank press of Miner id inside its station with ore (Sources.SourcesSystem, on the interact edge): the whole load
        /// goes into the hut stock (BankDeposit: A = actor, B = team, (X, Z) = the pad, Value = units, Bio = Ore), then the hut
        /// levels while the stock covers the next cost. True when anything moved.
        /// </summary>
        public static bool BankOre(World w, int id)
        {
            if (!On(w)) return false;
            Actor a = w.Actors[id];
            int units = OreOf(a);
            if (units <= 0) return false;
            HutState h = w.Hut;
            int t = (int)a.Team;
            PadDef pad = w.Map.Pads[t];
            h.Ore[t] += units;
            h.Banked[t] += units;
            a.Carry = CarryKind.None;
            a.CarryAmount = 0;
            a.GatherProgress = 0;
            h.ChannelVein[id] = -1;
            h.ChannelTicks[id] = 0;
            World.PushEvent(w, SimEventType.BankDeposit, id, t, pad.X, pad.Z, units, BiomassType.Ore, DamageCause.None);
            LevelWhileStock(w, a.Team, id);
            return true;
        }

        /// <summary>Level the team's hut while its stock covers the next cost (tests and BankOre). Returns the level.</summary>
        public static int LevelWhileStock(World w, TeamId team, int by = -1)
        {
            HutState h = w.Hut;
            int t = (int)team;
            double[] cost = w.Config.Depth.Hut.LevelCost;
            while (h.Level[t] < cost.Length && h.Ore[t] >= (int)cost[h.Level[t]])
            {
                h.Ore[t] -= (int)cost[h.Level[t]];
                h.Level[t]++;
                ApplyLevel(w, team);
                PadDef pad = w.Map.Pads[t];
                World.PushEvent(w, SimEventType.HutLevel, t, by, pad.X, pad.Z, h.Level[t]);
            }
            return h.Level[t];
        }

        /// <summary>Every LIVING teammate's MaxHp follows the level; Hp rises by the same gain (as Stations.TryUpgrade).</summary>
        static void ApplyLevel(World w, TeamId team)
        {
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (t.Team != team || !t.Alive) continue;
                double mh = Stations.MaxHp(w, team, t.Cls);
                double gain = mh - t.MaxHp;
                t.MaxHp = mh;
                if (gain > 0) t.Hp = Math.Min(mh, t.Hp + gain);
            }
        }

        /// <summary>Bot route to team's hut (the pad centre): built on first use, rebuilt when walls changed (one per tick).</summary>
        public static FlowField PadField(World w, TeamId team)
        {
            HutState h = w.Hut;
            int t = (int)team;
            bool fresh = h.PadField[t] == null;
            if (!fresh && h.PadFieldVersion[t] == w.WallVersion) return h.PadField[t];
            if (!fresh && h.FieldBuildTick == w.Tick) return h.PadField[t];
            PadDef pad = w.Map.Pads[t];
            h.Goal[0] = new PointXZ(pad.X, pad.Z);
            h.PadField[t] = FlowField.ComputeFlowField(w.Map, h.Goal, h.PadField[t], w.WallVersion);
            h.PadFieldVersion[t] = w.WallVersion;
            if (!fresh) h.FieldBuildTick = w.Tick;
            return h.PadField[t];
        }

        static void Cancel(HutState h, int id)
        {
            h.ChannelVein[id] = -1;
            h.ChannelTicks[id] = 0;
        }

        /// <summary>Per tick, after Sources.SourcesSystem: HAUL timers, the mining channels.</summary>
        public static void HutSystem(World w)
        {
            if (!On(w)) return;
            HutState h = w.Hut;
            SourceNode[] list = w.Depth.Sources;
            if (!h.Warm)
            {
                h.Warm = true;
                for (int i = 0; i < list.Length; i++) if (list[i].Kind == BiomassType.Ore) Sources.EnsureField(w, i);
                PadField(w, TeamId.Azure);
                PadField(w, TeamId.Crimson);
            }
            int need = MineTicks(w.Config);
            int cap = (int)w.Config.Depth.Hut.OreCarry;
            int top = MaxLevel(w.Config);
            int n = w.ActorCount;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                bool hit = a.Hp < h.PrevHp[id];
                h.PrevHp[id] = a.Hp;
                if (h.HaulLeft[id] > 0)
                {
                    if (!a.Alive || a.Cls != ClassId.Miner) h.HaulLeft[id] = 0;
                    else h.HaulLeft[id]--;
                }
                // Ore never feeds the egg: a delivery wind-up with ore in the cargo never completes.
                if (a.Carry == CarryKind.Ore && a.DeliverTicks > 0) Delivery.CancelDelivery(a);
                int v = h.ChannelVein[id];
                if (v < 0) continue;
                if (!a.Alive || a.Cls != ClassId.Miner || !w.Inputs[id].Terraform || a.StandUp > 0 || v >= list.Length)
                {
                    Cancel(h, id);
                    continue;
                }
                SourceNode s = list[v];
                if (s.Charges <= 0 || !Reaches(w, id, s) || h.Level[(int)a.Team] >= top
                    || (a.CarryAmount > 0 && a.Carry != CarryKind.None && a.Carry != CarryKind.Ore))
                {
                    Cancel(h, id);
                    continue;
                }
                if (hit)
                {
                    h.ChannelTicks[id] = 0; // a hit restarts the channel (Sources' gather rule)
                    continue;
                }
                if (++h.ChannelTicks[id] < need) continue;
                s.Charges -= 1;
                a.Carry = CarryKind.Ore;
                a.CarryAmount += 1;
                h.ChannelTicks[id] = 0;
                h.MinedBy[id]++;
                h.Mined[(int)a.Team]++;
                World.PushEvent(w, SimEventType.OreMined, id, v, s.X, s.Z, a.CarryAmount, BiomassType.Ore, DamageCause.None);
                if (a.CarryAmount >= cap || s.Charges <= 0) Cancel(h, id);
            }
        }
    }

    /// <summary>CM b13 item H on-screen words, in ONE place for the owner to rename (V's HUD may read these). Data only.</summary>
    public static class HutNames
    {
        public const string Hut = "BLACKSMITH HUT", Level = "LEVEL", Ore = "ORE", Mine = "MINE", Haul = "HAUL", Vein = "ORE VEIN";
        public const string Hint = "Miners upgrade the hut with ore.";
        public const string Bonus = "max HP, damage and healing";
        public const string Max = "MAX LEVEL";
    }
}
