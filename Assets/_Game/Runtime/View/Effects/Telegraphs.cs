using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Telegraph timing (physics audit M6 view half, DG-87; owner rule: pace stays 2x by default, but every reaction window
    /// gets a guaranteed minimum time on screen in real seconds: Kaiju claw 0.45 s, slam 0.8 s). Pure; EditMode tests.
    /// <para>The sim decides when a wind-up starts and how long it lasts (GameConfig Kaiju.MeleeWindupTicks = 11, 0.18 s on
    /// screen at pace 2; SlamTelegraphSeconds = 1.2, 0.6 s on screen). Lengthening those is the sim lane's DG-84/DG-85. Until
    /// then the view adds a PRE-WARNING stage read from sim state alone: when the Kaiju is about to be able to swing (melee
    /// cooldown nearly spent, its target already in reach) or to slam (slam cooldown nearly spent, two or more hostiles in
    /// the slam radius), a faint shape appears early enough that pre-warning plus wind-up lasts at least the minimum. The
    /// lead is computed from the config and the pace, so it shrinks to zero by itself once the sim wind-ups are long
    /// enough. The pre-warning can show for a swing that does not come (the target steps out): it never misses one that
    /// does, because a swing needs exactly the state it watches.</para>
    /// </summary>
    public static class Telegraphs
    {
        /// <summary>Owner minimums, wall seconds on screen.</summary>
        public const double ClawMinSeconds = 0.45, SlamMinSeconds = 0.8;
        /// <summary>The telegraph-audit floor for every wind-up (DG-87 acceptance).</summary>
        public const double AuditFloorSeconds = 0.4;

        /// <summary>Wall seconds on screen for a span of sim ticks at a pace (sim seconds per wall second).</summary>
        public static double OnScreenSeconds(double ticks, double simHz, double pace) =>
            simHz > 0 && pace > 0 ? ticks / simHz / pace : 0;

        /// <summary>The melee wind-up's own on-screen length (sim only, no pre-warning).</summary>
        public static double ClawWindupSeconds(GameConfig cfg, double pace) => OnScreenSeconds(cfg.Kaiju.MeleeWindupTicks, cfg.SimHz, pace);

        /// <summary>The slam telegraph's own on-screen length.</summary>
        public static double SlamWindupSeconds(GameConfig cfg, double pace) => pace > 0 ? cfg.Kaiju.SlamTelegraphSeconds / pace : 0;

        /// <summary>Sim seconds of pre-warning before the claw wind-up can start (0 when the wind-up alone is long enough).</summary>
        public static double ClawLeadSim(GameConfig cfg, double pace) =>
            Math.Max(0, ClawMinSeconds * pace - cfg.Kaiju.MeleeWindupTicks / cfg.SimHz);

        /// <summary>Sim seconds of pre-warning before the slam telegraph can start.</summary>
        public static double SlamLeadSim(GameConfig cfg, double pace) =>
            Math.Max(0, SlamMinSeconds * pace - cfg.Kaiju.SlamTelegraphSeconds);

        /// <summary>Total wall seconds of warning (pre-warning plus wind-up) the view gives for the claw and the slam.</summary>
        public static double ClawWarningSeconds(GameConfig cfg, double pace) => ClawWindupSeconds(cfg, pace) + (pace > 0 ? ClawLeadSim(cfg, pace) / pace : 0);
        public static double SlamWarningSeconds(GameConfig cfg, double pace) => SlamWindupSeconds(cfg, pace) + (pace > 0 ? SlamLeadSim(cfg, pace) / pace : 0);

        /// <summary>fix/kaiju-alive-1: the slam telegraph's length in sim seconds exactly as Kaiju.KaijuSystem sets TelegraphIn
        /// (World.Dur.SlamTelegraph, floored to the real-time minimum, with the sim-physics lane on; the config's seconds in
        /// Classic).</summary>
        public static double SlamTotalSim(World w) =>
            Phys.On(w, true) ? Math.Max(1e-3, w.Dur.SlamTelegraph / w.Config.SimHz) : Math.Max(1e-3, w.Config.Kaiju.SlamTelegraphSeconds);

        /// <summary>The claw wind-up's fill 0..1, smooth between ticks (windupIn counts down to 0 on the resolve tick).</summary>
        public static float MeleeProgress(int windupIn, double windupTicks, double alpha)
        {
            if (windupTicks <= 0) return 1f;
            double p = (windupTicks - windupIn + Math.Max(0, Math.Min(1, alpha))) / windupTicks;
            return (float)Math.Max(0, Math.Min(1, p));
        }

        static bool Awake(Kaiju k) => k.State != KaijuState.Egg && k.KoIn <= 0;

        /// <summary>
        /// The claw pre-warning target, or -1: the Kaiju is not winding up or slamming, its melee cooldown is spent within
        /// the lead, and its current target is alive and within MeleeRange (the exact start condition of Kaiju.Ai).
        /// </summary>
        public static int ClawPreWarning(World w, double pace)
        {
            Kaiju k = w.Kaiju;
            GameConfig cfg = w.Config;
            if (!Awake(k) || k.MeleeWindupIn > 0 || k.TelegraphIn > 0) return -1;
            double lead = ClawLeadSim(cfg, pace);
            if (lead <= 0 || k.MeleeCd > lead) return -1;
            int t = k.TargetId;
            if (t < 0 || t >= w.ActorCount || !w.Actors[t].Alive) return -1;
            double dx = w.X[t] - k.X, dz = w.Z[t] - k.Z;
            double r = cfg.Kaiju.MeleeRange;
            return dx * dx + dz * dz <= r * r ? t : -1;
        }

        /// <summary>The slam pre-warning: slam cooldown spent within the lead and two or more possible victims in the radius.</summary>
        public static bool SlamPreWarning(World w, double pace)
        {
            Kaiju k = w.Kaiju;
            GameConfig cfg = w.Config;
            if (!Awake(k) || k.TelegraphIn > 0 || k.MeleeWindupIn > 0) return false;
            double lead = SlamLeadSim(cfg, pace);
            if (lead <= 0 || k.SlamCd > lead) return false;
            double r2 = cfg.Kaiju.SlamRadius * cfg.Kaiju.SlamRadius;
            int n = 0;
            for (int i = 0; i < w.ActorCount && n < 2; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || !Hostile(k.State, a.Team)) continue;
                double dx = w.X[i] - k.X, dz = w.Z[i] - k.Z;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n >= 2;
        }

        /// <summary>View-side guess of who the Kaiju may hit: the other team when it is allied, anyone when wild or berserk.</summary>
        static bool Hostile(KaijuState s, TeamId team) =>
            s == KaijuState.Azure ? team != TeamId.Azure : s == KaijuState.Crimson ? team != TeamId.Crimson : true;
    }
}
