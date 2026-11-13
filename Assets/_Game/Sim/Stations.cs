using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// U-065 base stations (fills the CM-84 markers). Each team's STATION is its base zone (StationsConfig.Range of the pad).
    /// <para>CLASS SWITCH (ported: CM-54's cooldown, counters and 'classSwitch' event codes; ours: switching while ALIVE at
    /// the station). A class pick (InputFrame.ClassPick) by a living, grounded actor inside its station changes its class at
    /// once: its load is banked (U-064) or dropped, it keeps its health share, its ability is locked SwitchAbilityLockSeconds,
    /// and Respawn.ClassSwitchCooldownTicks (5 s) must pass before its next switch. Away from the station a living pick is
    /// refused (ClassSwitch code Alive), as in CM-54. A dead actor's pick still queues for the respawn, now with the cooldown.</para>
    /// <para>CLASS UPGRADES (ported: cm/CM-39 upgrade.ts linear ladder, +15% then +30% max HP, 3 then 5 to buy; ours: bought
    /// at the station for a whole class, paid in wood and water from the team stock). Travis: "Builder / Build class upgrades
    /// with biomass". A living BUILDER inside its station sends InputFrame.StationBuy = class index + 1: the team's level of
    /// that class rises by one for the rest of the match; every living member gains the max HP at once (healed by the gain)
    /// and every later respawn of that class starts with it. Refusals push an Upgrade event with a negative code.</para>
    /// <para>Bots: a Builder bot standing in its station buys the affordable level for the class with the most living
    /// teammates (lowest class index on a tie). Bots do not switch class.</para>
    /// Deterministic, allocation-free.
    /// </summary>
    public static class Stations
    {
        /// <summary>Upgrade event codes (SimEvent.Value; a success writes the new level, always >= 2). -2 and -3 are CM-39's.</summary>
        public const int RefusedMaxLevel = -2, RefusedNoStock = -3, RefusedNotBuilder = -4, RefusedNotAtStation = -5;

        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Stations.Enabled);

        /// <summary>Is living actor id inside its own team's station zone?</summary>
        public static bool InStation(World w, int id)
        {
            Actor a = w.Actors[id];
            PadDef pad = w.Map.Pads[(int)a.Team];
            double r = w.Config.Depth.Stations.Range;
            double dx = w.X[id] - pad.X;
            double dz = w.Z[id] - pad.Z;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>The top of the ladder (level 1 is the spawn level): the shortest of the cost and bonus rows + 1.</summary>
        public static int MaxLevel(GameConfig c)
        {
            StationsConfig S = c.Depth.Stations;
            int steps = Math.Min(S.UpgradeWood.Length, Math.Min(S.UpgradeWater.Length, S.MaxHpBonus.Length));
            return steps + 1;
        }

        /// <summary>The team's level of a class (1 with the rule off).</summary>
        public static int Level(World w, TeamId team, ClassId cls)
        {
            if (!On(w)) return 1;
            int l = w.Depth.ClassLevel[(int)team * ClassIds.All.Count + (int)cls];
            return l < 1 ? 1 : l;
        }

        /// <summary>Max HP of a class at a ladder level: the class row at level 1, else round(row x (1 + MaxHpBonus[level - 2])) (CM-39 classMaxHp).</summary>
        public static double MaxHpAt(GameConfig c, ClassId cls, int level)
        {
            double b = c.Class(cls).MaxHp;
            int top = MaxLevel(c);
            int l = level < 1 ? 1 : level > top ? top : level;
            if (l <= 1) return b;
            return Math.Floor(b * (1 + c.Depth.Stations.MaxHpBonus[l - 2]) + 0.5);
        }

        /// <summary>Max HP a member of team's class has right now (the class row with the rule off).</summary>
        public static double MaxHp(World w, TeamId team, ClassId cls)
        {
            return On(w) ? MaxHpAt(w.Config, cls, Level(w, team, cls)) : w.Config.Class(cls).MaxHp;
        }

        /// <summary>Wood and water the next level of a class costs the team; false at the top.</summary>
        public static bool NextCost(World w, TeamId team, ClassId cls, out int wood, out int water)
        {
            int l = Level(w, team, cls);
            wood = 0;
            water = 0;
            if (l >= MaxLevel(w.Config)) return false;
            wood = (int)w.Config.Depth.Stations.UpgradeWood[l - 1];
            water = (int)w.Config.Depth.Stations.UpgradeWater[l - 1];
            return true;
        }

        /// <summary>0 when actor id may buy the next level of class index ci right now, else a Refused* code.</summary>
        public static int UpgradeRefusal(World w, int id, int ci)
        {
            Actor a = w.Actors[id];
            if (!a.Alive || a.Cls != ClassId.Builder) return RefusedNotBuilder;
            if (!InStation(w, id)) return RefusedNotAtStation;
            ClassId cls = ClassIds.All[ci];
            if (!NextCost(w, a.Team, cls, out int wood, out int water)) return RefusedMaxLevel;
            if (Sources.Stock(w, a.Team, Sources.StockWood) < wood || Sources.Stock(w, a.Team, Sources.StockWater) < water) return RefusedNoStock;
            return 0;
        }

        /// <summary>
        /// One purchase attempt by actor id for class index ci. Exactly one Upgrade event: A = the buyer, B = class index,
        /// (X, Z) = the pad, Value = the new level (>= 2) or a Refused* code. Returns that value.
        /// </summary>
        public static int TryUpgrade(World w, int id, int ci)
        {
            if (ci < 0 || ci >= ClassIds.All.Count || id < 0 || id >= w.ActorCount) return RefusedNotBuilder;
            Actor a = w.Actors[id];
            PadDef pad = w.Map.Pads[(int)a.Team];
            int code = UpgradeRefusal(w, id, ci);
            if (code != 0)
            {
                World.PushEvent(w, SimEventType.Upgrade, id, ci, pad.X, pad.Z, code);
                return code;
            }
            ClassId cls = ClassIds.All[ci];
            NextCost(w, a.Team, cls, out int wood, out int water);
            Sources.TrySpend(w, a.Team, wood, water);
            int slot = (int)a.Team * ClassIds.All.Count + ci;
            int level = Level(w, a.Team, cls) + 1;
            w.Depth.ClassLevel[slot] = (byte)level;
            w.Depth.UpgradesBought++;
            double mh = MaxHpAt(w.Config, cls, level);
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (t.Team != a.Team || t.Cls != cls || !t.Alive) continue;
                double gain = mh - t.MaxHp;
                t.MaxHp = mh;
                if (gain > 0) t.Hp = Math.Min(mh, t.Hp + gain);
            }
            World.PushEvent(w, SimEventType.Upgrade, id, ci, pad.X, pad.Z, level);
            return level;
        }

        /// <summary>Consume every StationBuy press (SimStep, before the respawns).</summary>
        public static void StationSystem(World w)
        {
            bool on = On(w);
            for (int i = 0; i < w.ActorCount; i++)
            {
                InputFrame inp = w.Inputs[i];
                int buy = inp.StationBuy;
                if (buy == 0) continue;
                inp.StationBuy = 0; // one-shot
                if (on && w.Actors[i].Alive && w.Actors[i].StandUp <= 0) TryUpgrade(w, i, buy - 1);
            }
        }

        /// <summary>
        /// A class pick by a LIVING actor (Respawn.RequestClassChange, rule on). True when it now is that class. One
        /// ClassSwitch event unless it already is that class: A = actor, B = Respawn.ClassSwitchAccepted / Cooldown / Alive
        /// (alive and not at the station, or in the air), (X, Z) = the actor, Value = class index.
        /// </summary>
        public static bool TrySwitchAlive(World w, int id, ClassId cls)
        {
            Actor a = w.Actors[id];
            int ci = (int)cls;
            double x = w.X[id];
            double z = w.Z[id];
            if (a.Cls == cls) return true;
            if (!InStation(w, id) || a.AirLen > 0 || a.StandUp > 0)
            {
                World.PushEvent(w, SimEventType.ClassSwitch, id, Respawn.ClassSwitchAlive, x, z, ci);
                return false;
            }
            if (a.ClassSwitchCd > 0)
            {
                World.PushEvent(w, SimEventType.ClassSwitch, id, Respawn.ClassSwitchCooldown, x, z, ci);
                return false;
            }
            // The load: banked when the stock exists, else dropped. Corruption is always dropped.
            if (Sources.On(w) && (a.CarryMeat > 0 || (a.CarryAmount > 0 && a.Carry != CarryKind.Corruption))) Sources.Bank(w, id);
            a.Carry = CarryKind.None;
            a.CarryAmount = 0;
            a.CarryMeat = 0;
            a.GatherProgress = 0;
            a.GatherNodeId = -1;
            Delivery.CancelDelivery(a);
            double share = a.MaxHp > 0 ? a.Hp / a.MaxHp : 1;
            a.Cls = cls;
            a.MaxHp = MaxHp(w, a.Team, cls);
            a.Hp = a.MaxHp * share;
            a.AbilityActive = 0;
            a.AbilityCd = Math.Max(a.AbilityCd, w.Config.Depth.Stations.SwitchAbilityLockSeconds);
            a.AttackCd = Math.Max(a.AttackCd, w.Config.Class(cls).AttackCooldown);
            a.PendingClass = null;
            w.Terraform.Carried[id] = 0;
            w.Terraform.Mode[id] = 0;
            a.ClassSwitchCd = Durations.T30(w.Config, w.Config.Respawn.ClassSwitchCooldownTicks);
            a.ClassSwitches++;
            World.PushEvent(w, SimEventType.ClassSwitch, id, Respawn.ClassSwitchAccepted, x, z, ci);
            return true;
        }

        /// <summary>
        /// Bots (BotBrain.FillBotInput): a Builder bot in its station, once per BotBuyPeriodTicks, buys the level it can pay
        /// for the class with the most living teammates (lowest class index on a tie). Always clears output.StationBuy first.
        /// </summary>
        public static void TakeBotBuy(World w, int id, InputFrame output)
        {
            output.StationBuy = 0;
            if (!On(w)) return;
            Actor a = w.Actors[id];
            if (!a.Alive || a.Cls != ClassId.Builder) return;
            int period = Durations.T30(w.Config, w.Config.Depth.Stations.BotBuyPeriodTicks);
            if (period < 1) period = 1;
            if ((w.Tick + id) % period != 0 || !InStation(w, id)) return;
            int best = -1;
            int bestCount = 0;
            for (int ci = 0; ci < ClassIds.All.Count; ci++)
            {
                if (UpgradeRefusal(w, id, ci) != 0) continue;
                int c = 0;
                for (int j = 0; j < w.ActorCount; j++) if (w.Actors[j].Team == a.Team && w.Actors[j].Alive && (int)w.Actors[j].Cls == ci) c++;
                if (c > bestCount)
                {
                    bestCount = c;
                    best = ci;
                }
            }
            if (best >= 0) output.StationBuy = best + 1;
        }
    }

    /// <summary>U-065 on-screen names, in ONE place for the owner to rename. Data only.</summary>
    public static class StationNames
    {
        public const string Station = "BASE STATION", SwitchClass = "SWITCH CLASS", Upgrade = "UPGRADE", Level = "LEVEL";
        public const string OnlyBuilders = "Only a Builder can build upgrades. Switch to Builder here.";
        public const string NeedStock = "Not enough wood or water in the team stock.";
    }
}
