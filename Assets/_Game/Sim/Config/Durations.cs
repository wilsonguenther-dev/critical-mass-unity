using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// DG-68 (S8, not in the browser): the one place sim durations become ticks. Three kinds of number exist:
    /// <para>Tick counts written at 30 Hz (every config field named *Ticks): T30 rescales them to SimHz, so changing SimHz
    /// never silently halves a window. At SimHz 30 they are unchanged (the world hash did not move at this step).</para>
    /// <para>Sim seconds (AttackCooldown, SlamTelegraphSeconds...): SimSeconds, ticks = round(s x SimHz); they scale with
    /// the match pace, as in the browser.</para>
    /// <para>Real seconds (wind-ups, hit-stun, the reaction floors of OD-1): RealSeconds, ticks = round(s x SimHz x pace),
    /// and Floor raises a window to at least a real-second minimum at the match pace (WorldOptions.Pace).</para>
    /// The derived per-world table (World.Dur) is filled once at CreateWorld / ResetWorld; nothing allocates per tick.
    /// </summary>
    public sealed class Durations
    {
        /// <summary>The rate the config's *Ticks fields are written at.</summary>
        public const double ReferenceHz = 30;

        /// <summary>The match pace this table was derived for.</summary>
        public double Pace;
        /// <summary>Kaiju claw wind-up (ticks): KaijuConfig.MeleeWindupTicks, floored from DG-84 by PhysicsConfig.KaijuClawMinRealSeconds.</summary>
        public int KaijuClawWindup;
        /// <summary>Kaiju slam telegraph (ticks): KaijuConfig.SlamTelegraphSeconds (sim), floored from DG-85 by KaijuSlamMinRealSeconds.</summary>
        public int SlamTelegraph;
        /// <summary>DG-69: melee wind-up per class (ticks; 0 = damage on the press tick).</summary>
        public int[] AttackWindup;
        /// <summary>DG-69: slowed follow-through after the contact tick per class (ticks).</summary>
        public int[] Recovery;
        /// <summary>DG-70: hit-stun lengths (ticks).</summary>
        public int HeavyStun;
        public int KaijuStun;
        public int SlamStun;
        public int BlastStun;
        public int CrushStun;

        /// <summary>A tick count written at 30 Hz, at this config's SimHz.</summary>
        public static int T30(GameConfig c, double ticksAt30) => (int)Math.Floor(ticksAt30 / ReferenceHz * c.SimHz + 0.5);

        /// <summary>Sim seconds (they scale with the pace) to ticks.</summary>
        public static int SimSeconds(GameConfig c, double seconds) => (int)Math.Floor(seconds * c.SimHz + 0.5);

        /// <summary>Real (on-screen) seconds at the pace to ticks.</summary>
        public static int RealSeconds(GameConfig c, double seconds, double pace) => (int)Math.Floor(seconds * c.SimHz * pace + 0.5);

        /// <summary>ticks raised to at least realMin on-screen seconds at the pace (ceil: never a frame short).</summary>
        public static int Floor(GameConfig c, int ticks, double realMin, double pace)
        {
            int min = (int)Math.Ceiling(realMin * c.SimHz * pace - 1e-9);
            return ticks > min ? ticks : min;
        }

        /// <summary>How long ticks last on screen at the pace (seconds).</summary>
        public static double OnScreen(GameConfig c, int ticks, double pace) => ticks / (c.SimHz * pace);

        /// <summary>The match pace of opts: WorldOptions.Pace, else the config's DefaultSpeed, else 1.</summary>
        public static double PaceOf(WorldOptions o, GameConfig c)
        {
            if (o != null && o.Pace > 0) return o.Pace;
            return c.DefaultSpeed > 0 ? c.DefaultSpeed : 1;
        }

        public static Durations Create(GameConfig c)
        {
            return new Durations { AttackWindup = new int[c.Classes.Length], Recovery = new int[c.Classes.Length] };
        }

        /// <summary>Fill the table for config c at pace (no allocation).</summary>
        public void Derive(GameConfig c, double pace)
        {
            Pace = pace;
            KaijuClawWindup = T30(c, c.Kaiju.MeleeWindupTicks);
            if (!c.Physics.Classic) KaijuClawWindup = Floor(c, KaijuClawWindup, c.Physics.KaijuClawMinRealSeconds, pace); // DG-84 / OD-7
            SlamTelegraph = SimSeconds(c, c.Kaiju.SlamTelegraphSeconds);
            bool windup = Phys.On(c, c.Physics.AttackWindup);
            for (int i = 0; i < AttackWindup.Length; i++)
            {
                AttackWindup[i] = windup ? RealSeconds(c, c.Classes[i].AttackWindupSeconds, pace) : 0;
                Recovery[i] = windup ? RealSeconds(c, c.Classes[i].RecoverySeconds, pace) : 0;
            }
            PhysicsConfig p = c.Physics;
            HeavyStun = RealSeconds(c, p.HeavyStunSeconds, pace);
            KaijuStun = RealSeconds(c, p.KaijuStunSeconds, pace);
            SlamStun = RealSeconds(c, p.SlamStunSeconds, pace);
            BlastStun = RealSeconds(c, p.BlastStunSeconds, pace);
            CrushStun = RealSeconds(c, p.CrushStunSeconds, pace);
        }
    }
}
