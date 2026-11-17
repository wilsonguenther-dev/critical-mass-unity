// CM b13 item K (five Kaiju types: inherent trait + fed power each, incl. the blinding SHROUD). RULE CHANGE, Full profile only.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM b13 item K: the Kaiju type state on the World (World.KaijuKind). Not in the state hash on purpose (the order): a type
    /// mismatch between peers shows through the Kaiju's HP and position, which are hashed. Fixed-size, no allocation after Init.
    /// </summary>
    public sealed class KaijuTypeState
    {
        /// <summary>The type the hatch picked (Elder before the hatch, with the rule off, on a tie or with nothing fed).</summary>
        public KaijuType Type;
        /// <summary>The fed power level 0..3 (0 = trait only, and always 0 for Elder).</summary>
        public int Level;
        /// <summary>Units of plant, meat, wood, water both teams fed before the hatch (snapshotted at the hatch, rule on or off).</summary>
        public readonly double[] Mix = new double[4];
        /// <summary>Each kind's score at the hatch (share / RefShare), for logs and tests.</summary>
        public readonly double[] Score = new double[4];
        /// <summary>The hatch ran this match (the snapshot is valid).</summary>
        public bool Snapped;
        /// <summary>Wood / water tallies at the last world reset (World.ResetWorld zeroes only plant and meat; subtracted at the hatch).</summary>
        public double BaseWood;
        public double BaseWater;
        /// <summary>Kaiju ticks since the last damage it took (GROVE's REGROWTH waits RegrowthDelayTicks).</summary>
        public int SinceHit;
        /// <summary>SHROUD: ticks left of the puff telegraph (0 = none).</summary>
        public int PuffIn;
        /// <summary>SHROUD: ticks left of the cloud (0 = no cloud).</summary>
        public int CloudIn;
        /// <summary>SHROUD: ticks left of the cooldown (it starts at the cloud's end).</summary>
        public int CooldownIn;
        /// <summary>SHROUD: the cloud's centre (follows the Kaiju) and radius.</summary>
        public double CloudX;
        public double CloudZ;
        public double CloudR;
        /// <summary>SHROUD: the cloud's full length in ticks (for the view's fade).</summary>
        public int CloudLen;
        /// <summary>SHROUD casts this match (puffs started).</summary>
        public int Casts;

        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) => Clear(w);

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w) => Clear(w);

        private void Clear(World w)
        {
            Type = KaijuType.Elder;
            Level = 0;
            Array.Clear(Mix, 0, 4);
            Array.Clear(Score, 0, 4);
            Snapped = false;
            BaseWood = 0;
            BaseWater = 0;
            if (w != null && w.Match != null && w.Match.Delivered != null && w.Match.Delivered.Length >= 2)
            {
                BaseWood = w.Match.Delivered[0].Wood + w.Match.Delivered[1].Wood;
                BaseWater = w.Match.Delivered[0].Water + w.Match.Delivered[1].Water;
            }
            SinceHit = 0;
            PuffIn = 0;
            CloudIn = 0;
            CooldownIn = 0;
            CloudX = 0;
            CloudZ = 0;
            CloudR = 0;
            CloudLen = 0;
            Casts = 0;
        }
    }

    /// <summary>
    /// CM b13 item K (RULE CHANGE, Travis): five Kaiju types. The SIDE is still allegiance; the TYPE comes from the biomass MIX
    /// at the hatch (KaijuTypesConfig). GROVE (plant): HP x1.10, speed x0.95, fed REGROWTH. BRUTE (meat): claw x1.15, speed
    /// x1.08, HP x0.95, fed FRENZY. TIMBER (wood): HP x1.05, slam radius +0.75, fed BARKSKIN. MIRE (water): slam cooldown x0.8,
    /// speed x0.92, fed SHROUD. ELDER: today's Kaiju exactly (every getter returns the config value itself, no multiply).
    /// </summary>
    public static class KaijuTypes
    {
        /// <summary>The rule is on (KaijuTypesConfig.On in the Full profile).</summary>
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.KaijuTypes.On);

        /// <summary>This match's Kaiju type (Elder = today's Kaiju).</summary>
        public static KaijuType Type(World w) => w.KaijuKind.Type;

        /// <summary>The fed power level, 0..3.</summary>
        public static int PowerLevel(World w) => w.KaijuKind.Level;

        // ------------------------------------------------------------------ the pure picker

        /// <summary>Kind index (0 plant, 1 meat, 2 wood, 3 water) to its type.</summary>
        public static KaijuType TypeOfKind(int kind) =>
            kind == 0 ? KaijuType.Grove : kind == 1 ? KaijuType.Brute : kind == 2 ? KaijuType.Timber : kind == 3 ? KaijuType.Mire : KaijuType.Elder;

        /// <summary>A type's kind index (-1 for Elder).</summary>
        public static int KindOf(KaijuType t) =>
            t == KaijuType.Grove ? 0 : t == KaijuType.Brute ? 1 : t == KaijuType.Timber ? 2 : t == KaijuType.Mire ? 3 : -1;

        /// <summary>
        /// Pure: the type a feed of (plant, meat, wood, water) units picks. score_k = share_k / RefShare_k; the largest wins;
        /// the two best within TieEps, a best score under ElderBelowScore (a balanced feed), nothing fed, or a bad tally = ELDER. Writes the four scores into <paramref name="score"/>
        /// when given (length 4). Negative or non-finite units count as 0.
        /// </summary>
        public static KaijuType Pick(double plant, double meat, double wood, double water, KaijuTypesConfig c, double[] score = null)
        {
            double p = Clean(plant), m = Clean(meat), d = Clean(wood), a = Clean(water);
            double total = p + m + d + a;
            if (score != null) for (int i = 0; i < 4; i++) score[i] = 0;
            if (!(total > 0)) return KaijuType.Elder;
            int best = -1;
            double bestS = double.NegativeInfinity, second = double.NegativeInfinity;
            for (int k = 0; k < 4; k++)
            {
                double u = k == 0 ? p : k == 1 ? m : k == 2 ? d : a;
                double r = c.RefShare[k];
                double s = r > 0 ? (u / total) / r : 0;
                if (score != null) score[k] = s;
                if (s > bestS) { second = bestS; bestS = s; best = k; }
                else if (s > second) second = s;
            }
            if (best < 0 || bestS - second <= c.TieEps) return KaijuType.Elder;
            // b13 r3: a balanced feed (no kind clearly over its reference share) hatches ELDER, today's Kaiju.
            if (bestS < c.ElderBelowScore) return KaijuType.Elder;
            return TypeOfKind(best);
        }

        /// <summary>Pure: the power level for units of the type's own kind (0 below LevelUnits[0], up to 3).</summary>
        public static int LevelFor(double units, KaijuTypesConfig c)
        {
            int lv = 0;
            for (int i = 0; i < c.LevelUnits.Length && i < 3; i++) if (units >= c.LevelUnits[i]) lv = i + 1;
            return lv;
        }

        /// <summary>Pure: the inherent max-HP factor of a type (1 for Elder).</summary>
        public static double HpScale(KaijuType t, KaijuTypesConfig c) =>
            t == KaijuType.Grove ? c.GroveHpScale : t == KaijuType.Brute ? c.BruteHpScale : t == KaijuType.Timber ? c.TimberHpScale : 1.0;

        /// <summary>Pure: the inherent move-speed factor of a type (1 for Elder and TIMBER).</summary>
        public static double SpeedFactor(KaijuType t, KaijuTypesConfig c) =>
            t == KaijuType.Grove ? c.GroveSpeedScale : t == KaijuType.Brute ? c.BruteSpeedScale : t == KaijuType.Mire ? c.MireSpeedScale : 1.0;

        private static double Clean(double v) => v > 0 && double.IsFinite(v) ? v : 0;

        // ------------------------------------------------------------------ the hatch

        /// <summary>
        /// Called on the hatch tick right before the Hatch event (Kaiju.Hatch, after Hp = MaxHp). Snapshots the mix both teams
        /// fed (rule on or off, so a control arm can print it), then with the rule on picks the type and level and applies the
        /// inherent max HP (MaxHp x factor, Hp = MaxHp). Rule off: ELDER and nothing changes.
        /// </summary>
        public static void OnHatch(World w)
        {
            KaijuTypeState s = w.KaijuKind;
            KaijuTypesConfig c = w.Config.Depth.KaijuTypes;
            DeliveredTally a = w.Match.Delivered[0], b = w.Match.Delivered[1];
            s.Mix[0] = a.Plant + b.Plant;
            s.Mix[1] = a.Meat + b.Meat;
            s.Mix[2] = Math.Max(0, a.Wood + b.Wood - s.BaseWood);
            s.Mix[3] = Math.Max(0, a.Water + b.Water - s.BaseWater);
            s.Snapped = true;
            s.SinceHit = 0;
            s.PuffIn = s.CloudIn = s.CooldownIn = s.CloudLen = 0;
            s.CloudR = 0;
            KaijuType t = Pick(s.Mix[0], s.Mix[1], s.Mix[2], s.Mix[3], c, s.Score);
            if (!On(w)) t = KaijuType.Elder;
            s.Type = t;
            s.Level = t == KaijuType.Elder ? 0 : LevelFor(s.Mix[KindOf(t)], c);
            if (t == KaijuType.Elder) return;
            Kaiju k = w.Kaiju;
            double f = HpScale(t, c);
            if (f != 1.0) k.MaxHp *= f;
            k.Hp = k.MaxHp;
        }

        // ------------------------------------------------------------------ traits and powers (read by Kaiju.Ai / Kaiju.Hatch)

        /// <summary>The Kaiju's move speed (KaijuConfig.MoveSpeed itself for ELDER and TIMBER: bit for bit).</summary>
        public static double MoveSpeed(World w)
        {
            double v = w.Config.Kaiju.MoveSpeed;
            KaijuType t = w.KaijuKind.Type;
            if (t == KaijuType.Elder || t == KaijuType.Timber) return v;
            return v * SpeedFactor(t, w.Config.Depth.KaijuTypes);
        }

        /// <summary>Claw (melee) damage: BRUTE x1.15, else the base itself.</summary>
        public static double MeleeDamage(World w, double baseDamage) =>
            w.KaijuKind.Type == KaijuType.Brute ? baseDamage * w.Config.Depth.KaijuTypes.BruteMeleeDamageScale : baseDamage;

        /// <summary>The claw cooldown to set: BRUTE's FRENZY (level 1+, below half HP) scales it, else the config value itself.</summary>
        public static double MeleeCooldown(World w)
        {
            double cd = w.Config.Kaiju.MeleeCooldown;
            KaijuTypeState s = w.KaijuKind;
            if (s.Type != KaijuType.Brute || s.Level < 1) return cd;
            KaijuTypesConfig c = w.Config.Depth.KaijuTypes;
            Kaiju k = w.Kaiju;
            if (!(k.Hp < k.MaxHp * c.FrenzyBelowHpShare)) return cd;
            return cd * c.FrenzyMeleeCooldownScale[s.Level - 1];
        }

        /// <summary>The slam cooldown to set: MIRE x0.8, else the config value itself.</summary>
        public static double SlamCooldown(World w) =>
            w.KaijuKind.Type == KaijuType.Mire ? w.Config.Kaiju.SlamCooldown * w.Config.Depth.KaijuTypes.MireSlamCooldownScale : w.Config.Kaiju.SlamCooldown;

        /// <summary>The slam radius: TIMBER +0.75 u, else the config value itself.</summary>
        public static double SlamRadius(World w) =>
            w.KaijuKind.Type == KaijuType.Timber ? w.Config.Kaiju.SlamRadius + w.Config.Depth.KaijuTypes.TimberSlamRadiusAdd : w.Config.Kaiju.SlamRadius;

        /// <summary>Damage taken by the Kaiju for a cause: TIMBER's BARKSKIN (level 1+) scales explosions, else the amount itself.</summary>
        public static double TakenScale(World w, double amount, DamageCause cause)
        {
            KaijuTypeState s = w.KaijuKind;
            if (cause != DamageCause.Explosion || s.Type != KaijuType.Timber || s.Level < 1) return amount;
            return amount * w.Config.Depth.KaijuTypes.BarkskinExplosionScale[s.Level - 1];
        }

        /// <summary>Kaiju.DamageKaiju, when damage landed: restarts the REGROWTH wait.</summary>
        public static void OnDamaged(World w) => w.KaijuKind.SinceHit = 0;

        /// <summary>
        /// Once per Kaiju step after the hatch (before the knock-out early return): REGROWTH (GROVE) and SHROUD (MIRE). No-op for
        /// ELDER, BRUTE and TIMBER; no allocation.
        /// </summary>
        public static void Step(World w, double dt)
        {
            KaijuTypeState s = w.KaijuKind;
            if (s.Type == KaijuType.Elder) return;
            Kaiju k = w.Kaiju;
            KaijuTypesConfig c = w.Config.Depth.KaijuTypes;
            if (s.SinceHit < int.MaxValue) s.SinceHit++;
            if (s.Type == KaijuType.Grove)
            {
                if (s.Level < 1 || k.KoIn > 0 || s.SinceHit <= c.RegrowthDelayTicks || !(k.Hp < k.MaxHp)) return;
                k.Hp = Math.Min(k.MaxHp, k.Hp + k.MaxHp * (c.RegrowthPctPerSecond[s.Level - 1] / 100.0) * dt);
                return;
            }
            if (s.Type == KaijuType.Mire && s.Level >= 1) ShroudStep(w, s, k, c);
        }

        private static void ShroudStep(World w, KaijuTypeState s, Kaiju k, KaijuTypesConfig c)
        {
            if (s.CloudIn > 0)
            {
                s.CloudX = k.X;
                s.CloudZ = k.Z;
                s.CloudIn--;
                if (s.CloudIn == 0) s.CooldownIn = c.ShroudCooldownTicks;
                return;
            }
            if (s.PuffIn > 0)
            {
                s.PuffIn--;
                if (s.PuffIn == 0)
                {
                    s.CloudLen = c.ShroudTicks[s.Level - 1];
                    s.CloudIn = s.CloudLen;
                    s.CloudR = c.ShroudRadius[s.Level - 1];
                    s.CloudX = k.X;
                    s.CloudZ = k.Z;
                }
                return;
            }
            if (s.CooldownIn > 0)
            {
                s.CooldownIn--;
                return;
            }
            if (k.KoIn > 0) return;
            if (AffectedWithin(w, k.X, k.Z, c.ShroudTriggerRange) >= c.ShroudTriggerCount) StartPuff(w, s, k, c);
        }

        private static void StartPuff(World w, KaijuTypeState s, Kaiju k, KaijuTypesConfig c)
        {
            s.PuffIn = Math.Max(1, c.ShroudPuffTicks);
            s.Casts++;
            World.PushEvent(w, SimEventType.KaijuPower, -1, (int)s.Type, k.X, k.Z, c.ShroudRadius[s.Level - 1]);
        }

        /// <summary>For tests and the capture tool: start a SHROUD puff now (a MIRE of level 1+ with no puff, cloud or cooldown).</summary>
        public static bool ForceShroud(World w)
        {
            KaijuTypeState s = w.KaijuKind;
            if (s.Type != KaijuType.Mire || s.Level < 1 || s.PuffIn > 0 || s.CloudIn > 0) return false;
            s.CooldownIn = 0;
            StartPuff(w, s, w.Kaiju, w.Config.Depth.KaijuTypes);
            return true;
        }

        /// <summary>Living affected fighters within r of (x, z), actor-id order.</summary>
        public static int AffectedWithin(World w, double x, double z, double r)
        {
            int n = 0;
            double r2 = r * r;
            for (int id = 0; id < w.ActorCount; id++)
            {
                if (!w.Actors[id].Alive || !Affected(w, id)) continue;
                double dx = w.X[id] - x, dz = w.Z[id] - z;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n;
        }

        /// <summary>
        /// Actor id is blinded by this Kaiju's cloud: an enemy of the owning team (KaijuBlindHitsOwnTeam false), everyone when
        /// the Kaiju is wild (Berserk) or neutral.
        /// </summary>
        public static bool Affected(World w, int id)
        {
            if (!(id >= 0 && id < w.ActorCount)) return false;
            if (KaijuTypesConfig.KaijuBlindHitsOwnTeam) return true;
            KaijuState st = w.Kaiju.State;
            TeamId team = w.Actors[id].Team;
            if (st == KaijuState.Azure) return team != TeamId.Azure;
            if (st == KaijuState.Crimson) return team != TeamId.Crimson;
            return true;
        }

        /// <summary>A team's fighters are affected by the cloud (the view's fog asks per team).</summary>
        public static bool AffectsTeam(World w, TeamId team)
        {
            if (KaijuTypesConfig.KaijuBlindHitsOwnTeam) return true;
            KaijuState st = w.Kaiju.State;
            if (st == KaijuState.Azure) return team != TeamId.Azure;
            if (st == KaijuState.Crimson) return team != TeamId.Crimson;
            return true;
        }

        private static bool InCloud(World w, KaijuTypeState s, int id)
        {
            // the cloud is centred on the Kaiju and follows it (its live position, not last tick's)
            double dx = w.X[id] - w.Kaiju.X, dz = w.Z[id] - w.Kaiju.Z;
            return dx * dx + dz * dz <= s.CloudR * s.CloudR;
        }

        /// <summary>
        /// True when viewer viewerId cannot see (or target) targetId through the SHROUD: the viewer is affected and inside the
        /// cloud and the target is farther than SightInCloud, or the viewer is affected, outside, and the target is inside.
        /// </summary>
        public static bool BlindBlocks(World w, int viewerId, int targetId)
        {
            KaijuTypeState s = w.KaijuKind;
            if (s.CloudIn <= 0) return false;
            if (!(viewerId >= 0 && viewerId < w.ActorCount) || !(targetId >= 0 && targetId < w.ActorCount)) return false;
            if (!Affected(w, viewerId)) return false;
            if (InCloud(w, s, viewerId))
            {
                double dx = w.X[targetId] - w.X[viewerId], dz = w.Z[targetId] - w.Z[viewerId];
                double sr = w.Config.Depth.KaijuTypes.SightInCloud;
                return dx * dx + dz * dz > sr * sr;
            }
            return InCloud(w, s, targetId);
        }

        /// <summary>The SHROUD cloud, when one is up (centre follows the Kaiju).</summary>
        public static bool CloudActive(World w, out double x, out double z, out double r)
        {
            KaijuTypeState s = w.KaijuKind;
            if (s.CloudIn <= 0)
            {
                x = 0; z = 0; r = 0;
                return false;
            }
            x = w.Kaiju.X;
            z = w.Kaiju.Z;
            r = s.CloudR;
            return true;
        }

        /// <summary>Viewer viewerId's sight radius given its normal one: SightInCloud for an affected viewer inside the cloud.</summary>
        public static double SightRadiusFor(World w, int viewerId, double normal)
        {
            KaijuTypeState s = w.KaijuKind;
            if (s.CloudIn <= 0 || !(viewerId >= 0 && viewerId < w.ActorCount) || !Affected(w, viewerId) || !InCloud(w, s, viewerId)) return normal;
            return Math.Min(normal, w.Config.Depth.KaijuTypes.SightInCloud);
        }

        /// <summary>The power's word ("REGROWTH", "FRENZY", "BARKSKIN", "SHROUD"; null for ELDER).</summary>
        public static string PowerWord(KaijuType t) =>
            t == KaijuType.Grove ? "REGROWTH" : t == KaijuType.Brute ? "FRENZY" : t == KaijuType.Timber ? "BARKSKIN" : t == KaijuType.Mire ? "SHROUD" : null;
    }
}
