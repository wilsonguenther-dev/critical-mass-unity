using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// What the view draws for one sim event of the new classes (DG-22 Demolitions, DG-24 Builder, DG-40 Healer) and of the
    /// physics lane (DG-69 / DG-70 / DG-85, looked up by name: they are not on every base). <see cref="Unmapped"/> means
    /// "nobody decided": the event-to-visual test fails on it. <see cref="None"/> is an explicit "drawn elsewhere or not
    /// at all" (each such row says where in <see cref="MechEvents.VisualFor(SimEventType, int)"/>).
    /// </summary>
    public enum MechVisual
    {
        Unmapped = -1,
        None = 0,
        MineMound,
        MineArmRing,
        MineTrigger,
        MineGone,
        TntArc,
        TntFuse,
        PieceBuild,
        PieceBlink,
        PieceGone,
        PieceBreak,
        PieceCarry,
        HealBeam,
        HealPulse,
        HealSparkle,
        PurifyColumn,
        WeaponGlint,
        GlintCancel,
        StunBadge,
        KnockStreak,
        DodgedTick,
        DropPop,
    }

    /// <summary>
    /// The event-to-visual table and the name lookups for sim events that may not exist on this base. Lookups run once
    /// (static readonly), so the per-tick path compares ints only and never boxes.
    /// </summary>
    public static class MechEvents
    {
        /// <summary>The SimEventType member called name as an int, or -1 when this build of the sim has no such event.</summary>
        public static int Code(string name)
        {
            try
            {
                return Enum.IsDefined(typeof(SimEventType), name) ? (int)(SimEventType)Enum.Parse(typeof(SimEventType), name) : -1;
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }

        /// <summary>The first of several candidate names that exists, or -1.</summary>
        public static int FirstCode(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                int c = Code(names[i]);
                if (c >= 0) return c;
            }
            return -1;
        }

        // ---- physics lane (feat/sim-physics, PR #49: DG-69 / DG-70 / DG-85 / DG-100). Names as PR #49 ships them first
        // (AttackContact, AttackCancel, HitStun: A target, B source, Value stun ticks; Knock: A target, B source; MeatPop:
        // A orb owner, X/Z where it lands), then the build list's earlier spellings. BodyShove and PillarCrush draw nothing
        // here (the force bus and the topple view own them).
        public static readonly int AttackContactCode = Code("AttackContact");
        public static readonly int AttackCancelCode = Code("AttackCancel");
        public static readonly int HitStunCode = FirstCode("HitStun", "Stagger", "Stun");
        public static readonly int KnockbackCode = FirstCode("Knock", "Knockback", "KnockBack");
        public static readonly int SlamDodgedCode = FirstCode("SlamDodged", "KaijuSlamDodged", "JumpDodge");
        public static readonly int DropPopCode = FirstCode("MeatPop", "DropPopped", "OrbPopped", "DropPop");

        /// <summary>Every event PR #37 (DG-22) and PR #41 (DG-24, DG-40) appended.</summary>
        public static readonly string[] ClassEventNames =
        {
            "MinePlaced", "MineArmed", "MineTriggered", "MineRemoved", "TntThrown", "TntLanded", "Explosion",
            "PiecePlaced", "PieceCrumbling", "PieceExpired", "PieceDestroyed", "PiecePickedUp", "HealCast", "HealLanded", "Purify",
        };

        /// <summary>Every candidate physics event name this view understands (any subset may exist).</summary>
        public static readonly string[] PhysicsEventNames =
        {
            "AttackContact", "AttackCancel", "HitStun", "Stagger", "Stun", "Knock", "Knockback", "KnockBack", "SlamDodged", "KaijuSlamDodged",
            "JumpDodge", "MeatPop", "DropPopped", "OrbPopped", "DropPop",
        };

        /// <summary>The visual for an event type known at compile time; b is the event's B field (the heal kind).</summary>
        public static MechVisual VisualFor(SimEventType t, int b)
        {
            switch (t)
            {
                case SimEventType.MinePlaced: return MechVisual.MineMound;
                case SimEventType.MineArmed: return MechVisual.MineArmRing;
                case SimEventType.MineTriggered: return MechVisual.MineTrigger;
                case SimEventType.MineRemoved: return MechVisual.MineGone;
                case SimEventType.TntThrown: return MechVisual.TntArc;
                case SimEventType.TntLanded: return MechVisual.TntFuse;
                // Explosion: the fireball, scorch and force ring are CombatFeed's (integration batch 3); the crater arrives as a
                // Terraform event plus a GridVersion bump, so the chunk mesh rebuild (the dig visuals) draws it.
                case SimEventType.Explosion: return MechVisual.None;
                case SimEventType.Terraform: return MechVisual.None;
                case SimEventType.PiecePlaced: return MechVisual.PieceBuild;
                case SimEventType.PieceCrumbling: return MechVisual.PieceBlink;
                case SimEventType.PieceExpired: return MechVisual.PieceGone;
                case SimEventType.PieceDestroyed: return MechVisual.PieceBreak;
                case SimEventType.PiecePickedUp: return MechVisual.PieceCarry;
                case SimEventType.HealCast: return b == Healer.KindPulse ? MechVisual.HealPulse : MechVisual.HealBeam;
                case SimEventType.HealLanded: return MechVisual.HealSparkle;
                case SimEventType.Purify: return MechVisual.PurifyColumn;
                case SimEventType.AttackStart: return MechVisual.WeaponGlint;
                case SimEventType.Hit: return MechVisual.KnockStreak;
                case SimEventType.KaijuSlam: return MechVisual.DodgedTick;
                default: return MechVisual.Unmapped;
            }
        }

        /// <summary>The visual for a raw event code, physics names first (they are not compile-time members on every base).</summary>
        public static MechVisual VisualFor(int code, int b)
        {
            if (code < 0) return MechVisual.Unmapped;
            if (code == AttackContactCode) return MechVisual.WeaponGlint;
            if (code == AttackCancelCode) return MechVisual.GlintCancel;
            if (code == HitStunCode) return MechVisual.StunBadge;
            if (code == KnockbackCode) return MechVisual.KnockStreak;
            if (code == SlamDodgedCode) return MechVisual.DodgedTick;
            if (code == DropPopCode) return MechVisual.DropPop;
            return VisualFor((SimEventType)code, b);
        }
    }

    /// <summary>
    /// The compile switch for the physics lane's FIELDS (PR #49 appends them to SimEvent and Actor; this base lacks them).
    /// Events are found by name and work either way; with the scripting define <c>CM_SIM_PHYSICS</c> the view also reads
    /// <c>SimEvent.DirX / DirZ / Impulse / Tier</c> for exact knock direction and length, and <c>Actor.HitStunTicks /
    /// HitStunLen</c> for the stun badge. The integrator flips it once PR #49 is merged (Player Settings scripting define,
    /// or <c>-define:CM_SIM_PHYSICS</c> in a csc.rsp next to CriticalMass.Runtime.asmdef).
    /// </summary>
    public static class MechPhysics
    {
#if CM_SIM_PHYSICS
        public const bool FieldsCompiled = true;
#else
        public const bool FieldsCompiled = false;
#endif

        /// <summary>The event's own push direction and travelled distance when compiled in and present; false otherwise.</summary>
        public static bool Push(SimEvent e, out double dx, out double dz, out double impulse)
        {
#if CM_SIM_PHYSICS
            dx = e.DirX;
            dz = e.DirZ;
            impulse = e.Impulse;
            return dx * dx + dz * dz > 1e-9;
#else
            dx = 0;
            dz = 0;
            impulse = 0;
            return false;
#endif
        }

        /// <summary>Stun ticks left and the stun's full length from the actor (compiled in), else false.</summary>
        public static bool Stun(Actor a, out double left, out double len)
        {
#if CM_SIM_PHYSICS
            left = a.HitStunTicks;
            len = a.HitStunLen;
            return true;
#else
            left = 0;
            len = 0;
            return false;
#endif
        }
    }

    /// <summary>Timing of a temporary ladder or bridge: life left, the 3 s warning and its blink (DG-24).</summary>
    public static class PieceTimer
    {
        /// <summary>Blink rate in the warning, Hz: 2 at its start rising to 4 in the last second (never above 4).</summary>
        public const double BlinkSlowHz = 2, BlinkFastHz = 4;

        /// <summary>Life left 0..1 (1 at the build, 0 at the expiry tick), smooth between ticks.</summary>
        public static float Left01(int tick, double alpha, int placedTick, int expireTick)
        {
            double life = expireTick - placedTick;
            if (life <= 0) return 0f;
            double left = expireTick - (tick + Clamp01(alpha));
            return (float)Clamp01(left / life);
        }

        /// <summary>True inside the last warnTicks of a piece's life.</summary>
        public static bool Warning(int tick, int expireTick, double warnTicks) => expireTick - tick <= warnTicks;

        /// <summary>Whole seconds left on screen for a countdown label (ceil, never below 0).</summary>
        public static int SecondsLeft(int tick, int expireTick, double simHz)
        {
            if (simHz <= 0) return 0;
            double s = (expireTick - tick) / simHz;
            return s <= 0 ? 0 : (int)Math.Ceiling(s - 1e-9);
        }

        /// <summary>
        /// Visibility multiplier of a warning piece: 1 = full, down to 0.35 on the off beat. wallInto = wall seconds since
        /// the warning began, wallLeft = wall seconds to the end. Reduce flashing: a steady 1 (the colour shift carries it).
        /// </summary>
        public static float Blink(double wallInto, double wallLeft, bool reduceFlashing)
        {
            if (reduceFlashing || wallInto < 0) return 1f;
            double hz = wallLeft <= 1.0 ? BlinkFastHz : BlinkSlowHz;
            double phase = wallInto * hz;
            phase -= Math.Floor(phase);
            return phase < 0.5 ? 1f : 0.35f;
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    /// <summary>The thrown TNT's flight: progress, height, shadow and the fuse (DG-22: FlightTicks touch-down, FuseTicks blast).</summary>
    public static class TntArc
    {
        public const float MinApex = 0.9f, ApexPerUnit = 0.3f, MaxApex = 3f;

        /// <summary>Flight progress 0..1 from the throw tick to the landing tick at a fractional sim time (ticks).</summary>
        public static float Progress(double nowTicks, int thrownTick, int landTick)
        {
            double span = landTick - thrownTick;
            if (span <= 0) return 1f;
            double p = (nowTicks - thrownTick) / span;
            return p <= 0 ? 0f : p >= 1 ? 1f : (float)p;
        }

        /// <summary>The apex height for a throw of distance d.</summary>
        public static float Apex(float distance) => Mathf.Clamp(MinApex + ApexPerUnit * distance, MinApex, MaxApex);

        /// <summary>Height above the ground at progress p (a parabola: 0 at both ends, the apex at 0.5).</summary>
        public static float Height(float p, float distance) => 4f * Apex(distance) * p * (1f - p);

        /// <summary>The ground shadow's scale for a height (1 on the ground, smaller and fainter higher up).</summary>
        public static float ShadowScale(float height) => 1f / (1f + 0.35f * Mathf.Max(0f, height));

        /// <summary>Fuse left 0..1 between landing and blast.</summary>
        public static float FuseLeft01(double nowTicks, int landTick, int blastTick)
        {
            double span = blastTick - landTick;
            if (span <= 0) return 0f;
            double f = (blastTick - nowTicks) / span;
            return f <= 0 ? 0f : f >= 1 ? 1f : (float)f;
        }
    }

    /// <summary>
    /// Who sees what of a mine (gap list section 6: "a loose-dirt mound, visible to everyone for fairness; armed, a brass
    /// pressure plate peeks out, and a dashed team-colour ring shows to the owner's team"). Enemies see the mound only, a
    /// touch fainter; the plate and the ring are the owner's team's.
    /// </summary>
    public static class MineSight
    {
        public const float EnemyMound = 0.8f;

        public static float MoundAlpha(TeamId mineTeam, TeamId viewer) => mineTeam == viewer ? 1f : EnemyMound;

        public static bool ShowPlate(TeamId mineTeam, TeamId viewer, bool armed) => armed && mineTeam == viewer;

        public static bool ShowRing(TeamId mineTeam, TeamId viewer, bool armed) => armed && mineTeam == viewer;

        /// <summary>Minimap: only the owner's team's mines are marked.</summary>
        public static bool OnMap(TeamId mineTeam, TeamId viewer) => mineTeam == viewer;
    }

    /// <summary>Knock-down and get-up timing (DG-70 stun, DG-88 knock-down), and the feed throw (A15), view only.</summary>
    public static class KnockPose
    {
        /// <summary>Wall seconds to fall and to get back up; a stun shorter than both plays a stagger lean only.</summary>
        public const double FallSeconds = 0.14, RiseSeconds = 0.36, MinKnockDown = 0.55;
        /// <summary>Largest tilt of a full knock-down and of a stagger, degrees.</summary>
        public const float DownDeg = 78f, StaggerDeg = 16f;

        /// <summary>Tilt 0..1 at wall age into a stun of wall length len (fall, hold, rise; 0 outside).</summary>
        public static float Tilt01(double age, double len)
        {
            if (age < 0 || age >= len || len <= 0) return 0f;
            double fall = Math.Min(FallSeconds, len * 0.3), rise = Math.Min(RiseSeconds, len * 0.5);
            if (age < fall) return (float)(age / fall);
            if (age > len - rise) return (float)((len - age) / rise);
            return 1f;
        }

        /// <summary>Degrees of tilt: a full knock-down for long stuns, a stagger lean for short ones.</summary>
        public static float TiltDeg(double age, double len) => Tilt01(age, len) * (len >= MinKnockDown ? DownDeg : StaggerDeg);

        /// <summary>The feed throw: wall seconds, and the arm swing 0..1..0 (fast up, slower follow-through).</summary>
        public const double ThrowSeconds = 0.45;

        public static float Throw01(double age)
        {
            if (age < 0 || age >= ThrowSeconds) return 0f;
            double up = ThrowSeconds * 0.35;
            return age < up ? (float)(age / up) : (float)(1 - (age - up) / (ThrowSeconds - up));
        }
    }

    /// <summary>The off-screen damage direction chevron (DG-101): where on the screen edge it sits and how it fades.</summary>
    public static class DamageDirection
    {
        public const int Slots = 6;
        public const float LifeSeconds = 1.3f, FadeInSeconds = 0.06f;

        /// <summary>True when a viewport point (Camera.WorldToViewportPoint) is behind the camera or outside the screen.</summary>
        public static bool OffScreen(Vector3 viewport) =>
            viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f;

        /// <summary>
        /// Screen direction (centre to source) of a viewport point; a point behind the camera is mirrored so the arrow still
        /// points the right way round.
        /// </summary>
        public static Vector2 ScreenDir(Vector3 viewport)
        {
            var d = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (viewport.z < 0f) d = -d;
            if (d.sqrMagnitude < 1e-8f) d = Vector2.down;
            return d.normalized;
        }

        /// <summary>UI rotation (degrees, counter-clockwise from up) for an arrow pointing along dir.</summary>
        public static float AngleDeg(Vector2 dir) => Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg;

        /// <summary>Where a ray from the centre along dir leaves a box of half size half, pulled in by inset.</summary>
        public static Vector2 EdgePoint(Vector2 dir, Vector2 half, float inset)
        {
            float hx = Mathf.Max(1f, half.x - inset), hy = Mathf.Max(1f, half.y - inset);
            float tx = Mathf.Abs(dir.x) > 1e-5f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 1e-5f ? hy / Mathf.Abs(dir.y) : float.MaxValue;
            return dir * Mathf.Min(tx, ty);
        }

        /// <summary>Alpha at wall age: a quick fade in, a hold, then out by LifeSeconds.</summary>
        public static float Alpha(float age)
        {
            if (age < 0f || age >= LifeSeconds) return 0f;
            if (age < FadeInSeconds) return age / FadeInSeconds;
            float outStart = LifeSeconds * 0.55f;
            return age < outStart ? 1f : 1f - (age - outStart) / (LifeSeconds - outStart);
        }

        /// <summary>Chevron size for a hit: bigger for harder hits (15 dmg = base, 45+ = 1.6x).</summary>
        public static float Scale(double damage) => 1f + 0.6f * Mathf.Clamp01((float)((damage - 15) / 30));
    }

    /// <summary>The "you were hit by" words (the death cause and the blow), shared by the recap strip and its tests.</summary>
    public static class HitRecapWords
    {
        public const int Rows = 3;

        /// <summary>The blow's name: the death cause first, then the attacker's class weapon.</summary>
        public static string Blow(DamageCause cause, ClassId attackerClass, bool attackerIsActor, int blastKind)
        {
            switch (cause)
            {
                case DamageCause.Explosion: return blastKind == Demolitions.ExplosionMine ? DemolitionsNames.Mine : DemolitionsNames.Tnt;
                case DamageCause.KaijuMelee: return "KAIJU CLAW";
                case DamageCause.KaijuSlam: return "KAIJU SLAM";
                case DamageCause.Fall: return "THE FALL";
            }
            if (!attackerIsActor) return "A HIT";
            switch (attackerClass)
            {
                case ClassId.Warrior: return "SWORD";
                case ClassId.Farmer: return "STAFF";
                case ClassId.Miner: return "PICKAXE";
                case ClassId.Builder: return "HAMMER";
                case ClassId.Demolitions: return DemolitionsNames.Tnt;
                default: return "A HIT";
            }
        }

        /// <summary>Who: "CRIMSON WARRIOR", "THE KAIJU", or "THE GROUND".</summary>
        public static string Who(DamageCause cause, int attacker, TeamId team, ClassId cls, string[] classNames)
        {
            if (cause == DamageCause.KaijuMelee || cause == DamageCause.KaijuSlam || attacker == Damage.KaijuSource) return "THE KAIJU";
            if (cause == DamageCause.Fall) return "THE GROUND";
            if (attacker < 0) return "SOMETHING";
            int c = (int)cls;
            string name = classNames != null && c >= 0 && c < classNames.Length ? classNames[c] : cls.ToString().ToUpperInvariant();
            return (team == TeamId.Azure ? "AZURE " : "CRIMSON ") + name;
        }

        /// <summary>The class names the recap uses (the sim's name tables for the new classes).</summary>
        public static readonly string[] ClassNames =
        {
            "WARRIOR", "FARMER", "MINER", DemolitionsNames.ClassName, BuilderNames.ClassName, HealerNames.ClassName,
        };
    }

    /// <summary>The Kaiju slam read (DG-85 falloff and jump dodge): inside, the safe edge, falloff shading and the dodge.</summary>
    public static class SlamRead
    {
        /// <summary>Damage share at a distance (DG-85: 45 at the centre, at most 22 at the edge). 1 at d = 0, 0.5 at d = r.</summary>
        public static float Falloff(double d, double r) => r <= 0 ? 0f : 1f - 0.5f * Mathf.Clamp01((float)(d / r));

        public static bool Inside(double px, double pz, double cx, double cz, double r)
        {
            double dx = px - cx, dz = pz - cz;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>Units the player must walk to reach the safe edge (0 when already outside).</summary>
        public static double ToSafeEdge(double px, double pz, double cx, double cz, double r)
        {
            double dx = px - cx, dz = pz - cz;
            double d = Math.Sqrt(dx * dx + dz * dz);
            return d >= r ? 0 : r - d;
        }

        /// <summary>A dodge: inside the slam when it landed, airborne, and no slam damage that tick.</summary>
        public static bool Dodged(bool insideAtSlam, bool airborne, bool tookSlamHit) => insideAtSlam && airborne && !tookSlamHit;
    }

    /// <summary>The emote wheel's four emotes (view only, never in the sim).</summary>
    public static class Emotes
    {
        public const int Count = 4;
        public const float Seconds = 1.6f, Cooldown = 1.0f;
        public static readonly string[] Names = { "CHEER", "WAVE", "TAUNT", "BOW" };
        public static readonly string[] Bubble = { "!!", "HI", "COME ON", "GG" };

        /// <summary>The emote a direction picks: 0 up, 1 right, 2 down, 3 left; -1 inside the dead zone.</summary>
        public static int Sector(Vector2 d, float deadZone)
        {
            if (d.sqrMagnitude < deadZone * deadZone) return -1;
            if (Mathf.Abs(d.y) >= Mathf.Abs(d.x)) return d.y >= 0 ? 0 : 2;
            return d.x >= 0 ? 1 : 3;
        }

        /// <summary>The emote's envelope 0..1 at wall age (a pop in, a hold, a fade).</summary>
        public static float Envelope(float age)
        {
            if (age < 0f || age >= Seconds) return 0f;
            if (age < 0.12f) return age / 0.12f;
            if (age > Seconds - 0.3f) return (Seconds - age) / 0.3f;
            return 1f;
        }
    }

    /// <summary>One row of the class kit panel (the second ability and charges of the three new classes).</summary>
    public struct KitRow
    {
        public string Name;
        /// <summary>Cooldown left 0..1 (0 = ready).</summary>
        public float Cd01;
        public float CdSeconds;
        /// <summary>Charges available and the most there can be (MaxCharges 0 = no charges counter).</summary>
        public int Charges, MaxCharges;
        /// <summary>False when the ability cannot be used here (the Healer away from the egg); greys the row.</summary>
        public bool Usable;
        /// <summary>The binding the row is pressed with ("Ability" or "DigPlace").</summary>
        public string Action;
        /// <summary>A short status word ("AT THE EGG", "BANK 3"), or null.</summary>
        public string Note;
    }

    /// <summary>Reads the class kit rows from sim state (read only; never calls a function that writes sim state).</summary>
    public static class ClassKit
    {
        public static int RowCount(ClassId c) => c == ClassId.Demolitions || c == ClassId.Builder || c == ClassId.Healer ? 2 : 0;

        public static int MinesOut(World w, int id)
        {
            int n = 0;
            Mine[] ms = w.Demolitions.Mines;
            for (int k = 0; k < ms.Length; k++) if (ms[k].Active && ms[k].Owner == id) n++;
            return n;
        }

        static float Cd(double left, double full) => full > 0 ? Mathf.Clamp01((float)(left / full)) : 0f;

        /// <summary>Fills row r (0 = the class ability, 1 = the second ability) for actor id. Returns false when there is no row.</summary>
        public static bool Read(World w, int id, int r, ref KitRow row, string[] numbers)
        {
            if (w == null || id < 0 || id >= w.ActorCount) return false;
            Actor a = w.Actors[id];
            GameConfig c = w.Config;
            double hz = c.SimHz;
            ClassConfig cc = c.Class(a.Cls);
            row.Note = null;
            row.Usable = a.Alive;
            switch (a.Cls)
            {
                case ClassId.Demolitions:
                    if (r == 0)
                    {
                        row.Name = DemolitionsNames.Mine;
                        row.Action = "Ability";
                        row.MaxCharges = (int)c.Abilities.DemolitionsMine.MaxActivePerOwner;
                        row.Charges = Math.Max(0, row.MaxCharges - MinesOut(w, id));
                        row.Cd01 = Cd(a.AbilityCd, cc.AbilityCooldown);
                        row.CdSeconds = (float)Math.Max(0, a.AbilityCd);
                        if (row.Charges == 0) row.Note = "OLDEST GOES";
                    }
                    else
                    {
                        row.Name = DemolitionsNames.Tnt;
                        row.Action = "DigPlace";
                        row.MaxCharges = 0;
                        row.Charges = 0;
                        int wait = Demolitions.TntWaitTicks(w, id);
                        row.Cd01 = Cd(wait, c.Abilities.DemolitionsTnt.CooldownTicks);
                        row.CdSeconds = (float)(wait / hz);
                    }
                    return true;
                case ClassId.Builder:
                    if (r == 0)
                    {
                        row.Name = BuilderNames.Build;
                        row.Action = "Ability";
                        row.MaxCharges = (int)c.Abilities.BuilderPieces.MaxActivePerBuilder;
                        row.Charges = Math.Max(0, row.MaxCharges - BuiltPieces.OwnedCount(w, id));
                        row.Cd01 = Cd(a.AbilityCd, cc.AbilityCooldown);
                        row.CdSeconds = (float)Math.Max(0, a.AbilityCd);
                        double bank = BuiltPieces.Bank(w, a.Team);
                        row.Usable = a.Alive && bank >= c.Abilities.BuilderPieces.LadderCost;
                        int b = (int)Math.Floor(Math.Max(0, bank));
                        row.Note = numbers != null && b < numbers.Length ? numbers[b] : null;
                    }
                    else
                    {
                        row.Name = BuilderNames.Carry;
                        row.Action = "Interact";
                        row.MaxCharges = 0;
                        row.Charges = 0;
                        row.Cd01 = 0f;
                        row.CdSeconds = 0f;
                        row.Usable = a.Alive && w.Builder.CarryPiece[id] < 0 && a.Carry == CarryKind.None;
                        if (w.Builder.CarryPiece[id] >= 0) { row.Usable = a.Alive; row.Note = "CARRYING"; }
                    }
                    return true;
                case ClassId.Healer:
                    if (r == 0)
                    {
                        row.Name = HealerNames.Pulse;
                        row.Action = "Ability";
                        row.MaxCharges = 0;
                        row.Charges = 0;
                        row.Cd01 = Cd(a.AbilityCd, cc.AbilityCooldown);
                        row.CdSeconds = (float)Math.Max(0, a.AbilityCd);
                    }
                    else
                    {
                        row.Name = HealerNames.Purify;
                        row.Action = "DigPlace";
                        row.MaxCharges = 0;
                        row.Charges = 0;
                        int wait = Healer.PurifyWaitTicks(w, id);
                        row.Cd01 = Cd(wait, c.Abilities.Healer.PurifyCooldownTicks);
                        row.CdSeconds = (float)(wait / hz);
                        int why = Healer.PurifyRefusal(w, id);
                        row.Usable = a.Alive && why == 0;
                        row.Note = why == Healer.PurifyRefusedRange ? "GO TO THE EGG" : why == 0 ? "AT THE EGG" : "EGG IS CLEAN";
                    }
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>The bank and pieces line for a Builder's team (shown to every player on a team with a Builder).</summary>
    public static class BuilderBank
    {
        public static bool TeamHasBuilder(World w, TeamId team)
        {
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == team && w.Actors[i].Cls == ClassId.Builder) return true;
            return false;
        }

        /// <summary>Whole biomass in the team bank (never below 0).</summary>
        public static int Whole(World w, TeamId team) => (int)Math.Floor(Math.Max(0, BuiltPieces.Bank(w, team)));
    }
}
