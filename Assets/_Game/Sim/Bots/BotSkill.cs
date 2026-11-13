using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// DG-21 bot difficulty (U-058, not in the browser). WorldOptions.BotDifficulty sets the bots of the team the human does
    /// not play, WorldOptions.AllyBotDifficulty the bots of the human's team (actor 0 on autopilot included). Normal returns
    /// no profile, and every call site keeps its browser code path for it, so a Normal match is the browser match bit for bit.
    /// Easy and Hard read Bots.Easy / Bots.Hard (BotSkillConfig). Rolls are a stateless integer hash of seed, actor, a salt and
    /// a time bucket: deterministic, allocation-free, and they never draw from the shared bot RNG.
    /// </summary>
    public static class BotSkill
    {
        /// <summary>Roll salts (one per decision kind, so two decisions on one tick are independent).</summary>
        public const int SaltSwing = 1, SaltAimAngle = 2, SaltAimRadius = 3, SaltAbility = 4;

        /// <summary>Ticks per hesitation window (1/3 s at 30 Hz).</summary>
        public const int SwingWindowTicks = 10;

        /// <summary>The difficulty of actor id's brain.</summary>
        public static BotDifficulty Of(World w, int id)
        {
            return w.Actors[id].Team == w.Opts.HumanTeam ? w.Opts.AllyBotDifficulty : w.Opts.BotDifficulty;
        }

        /// <summary>The skill profile of actor id's brain; null for Normal (the browser bots).</summary>
        public static BotSkillConfig Profile(World w, int id)
        {
            BotDifficulty d = Of(w, id);
            if (d == BotDifficulty.Easy) return w.Config.Bots.Easy;
            if (d == BotDifficulty.Hard) return w.Config.Bots.Hard;
            return null;
        }

        /// <summary>Think period in ticks for actor id: the Normal period scaled by the profile (at least 1).</summary>
        public static int ThinkPeriod(World w, int id, int normalPeriod)
        {
            BotSkillConfig sk = Profile(w, id);
            if (sk == null) return normalPeriod;
            return Math.Max(1, (int)JsMath.Round(normalPeriod * sk.ThinkPeriodScale));
        }

        /// <summary>A uniform roll in [0, 1) from (seed, id, salt, bucket); the same arguments always give the same value.</summary>
        public static double Roll(World w, int id, int salt, int bucket)
        {
            unchecked
            {
                uint h = (uint)w.Opts.Seed * 0x9E3779B1u;
                h ^= (uint)(id + 1) * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)salt * 0xC2B2AE3Du;
                h = (h << 11) | (h >> 21);
                h ^= (uint)bucket * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h >> 8) / 16777216.0;
            }
        }

        /// <summary>True when actor id holds its melee swing this tick (Easy hesitation); false always for Normal and Hard.</summary>
        public static bool HoldsSwing(World w, int id)
        {
            BotSkillConfig sk = Profile(w, id);
            if (sk == null || !(sk.SwingHesitation > 0)) return false;
            return Roll(w, id, SaltSwing, w.Tick / SwingWindowTicks) < sk.SwingHesitation;
        }

        /// <summary>False when this think's ability decision is held back (AbilityChance); true always for Normal.</summary>
        public static bool UsesAbilityNow(World w, int id, BotSkillConfig sk)
        {
            if (sk == null || sk.AbilityChance >= 1) return true;
            return Roll(w, id, SaltAbility, w.Tick) < sk.AbilityChance;
        }

        /// <summary>Moves (x, z) by the profile's aim error (uniform over a disc of AimErrorUnits); no change for Normal or 0.</summary>
        public static void Jitter(World w, int id, BotSkillConfig sk, ref double x, ref double z)
        {
            if (sk == null || !(sk.AimErrorUnits > 0)) return;
            double ang = Roll(w, id, SaltAimAngle, w.Tick) * Math.PI * 2;
            double r = Math.Sqrt(Roll(w, id, SaltAimRadius, w.Tick)) * sk.AimErrorUnits;
            x = JsMath.F32(x + JsMath.Cos(ang) * r);
            z = JsMath.F32(z + JsMath.Sin(ang) * r);
        }

        /// <summary>
        /// Target-choice weight of enemy j for a focusing (Hard) bot: distance squared x CarrierWeight when j carries cargo or
        /// meat, x (0.5 + 0.5 x its HP share) otherwise too, so a hurt carrier is the first pick. Lower is preferred.
        /// </summary>
        public static double FocusScore(World w, BotSkillConfig sk, int j, double d2)
        {
            Actor t = w.Actors[j];
            double s = d2;
            if (t.CarryAmount > 0 || t.CarryMeat > 0) s *= sk.CarrierWeight;
            double hp = t.MaxHp > 0 ? t.Hp / t.MaxHp : 1;
            return s * (0.5 + 0.5 * hp);
        }
    }
}
