using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// px S1 [2] (Full rules only, CombatFeel.On): bots fight by human limits. A bot's melee aim reads its target where it was
    /// BotMeleeAimDelayTicks ago (an average human reacts in about 0.2 s), leads it by the velocity it showed then (capped at
    /// BotLeadSeconds, as before), and turns by a small per-window aim error (BotMeleeAimErrorDeg). Positions come from a short
    /// ring of every actor's position recorded once per tick in BotsThink (BotsState.AimHist*). Deterministic: plain reads in
    /// id order, the stateless BotSkill.Roll, no allocation per tick.
    /// </summary>
    public static class BotAim
    {
        /// <summary>Ring length (ticks); a delay longer than Ring - 3 is clamped.</summary>
        public const int Ring = 32;
        /// <summary>BotSkill.Roll salt of the melee aim error.</summary>
        public const int SaltMeleeAim = 5;

        public static bool On(World w) => CombatFeel.On(w) && w.Config.Depth.CombatFeel.BotHumanLimits;

        /// <summary>px DM (lead's call 18): actor id t is a human seat (a person's seat, autopilot or not). Human-limit rules
        /// whose HumansOnly switch is on apply only in an exchange with one.</summary>
        public static bool IsHumanSeat(World w, int t) => t >= 0 && t < w.ActorCount && w.Actors[t].IsHuman;

        /// <summary>BotsThink, once per tick: every actor's position into the ring slot of this tick.</summary>
        public static void Record(World w, BotsState st)
        {
            int slot = w.Tick & (Ring - 1);
            int n = w.ActorCount;
            int b = slot * st.AimHistCap;
            for (int i = 0; i < n; i++)
            {
                st.AimHistX[b + i] = w.X[i];
                st.AimHistZ[b + i] = w.Z[i];
            }
            st.AimHistTick[slot] = w.Tick;
        }

        /// <summary>The delay (ticks at this SimHz) of bot id's difficulty.</summary>
        public static int Delay(World w, int id)
        {
            int[] d = w.Config.Depth.CombatFeel.BotMeleeAimDelayTicks;
            int k = (int)BotSkill.Of(w, id);
            int t30 = d != null && k < d.Length ? d[k] : 0;
            if (t30 <= 0) return 0;
            int t = CombatFeel.Ticks(w, t30 / 30.0);
            return t > Ring - 3 ? Ring - 3 : t;
        }

        private static bool At(World w, BotsState st, int target, int ago, out double x, out double z)
        {
            int tick = w.Tick - ago;
            int slot = tick & (Ring - 1);
            if (ago <= 0 || st.AimHistTick[slot] != tick) { x = w.X[target]; z = w.Z[target]; return ago <= 0; }
            int b = slot * st.AimHistCap;
            x = st.AimHistX[b + target];
            z = st.AimHistZ[b + target];
            return true;
        }

        /// <summary>
        /// SteerBot: bot id's perceived aim point at enemy actor target for a swing of windup ticks (the lead is capped like
        /// the b15 lead), with this window's aim error turned in around the bot.
        /// </summary>
        public static void Perceive(World w, BotsState st, int id, int target, int windup, out double tx, out double tz)
        {
            GameConfig cfg = w.Config;
            CombatFeelConfig f = cfg.Depth.CombatFeel;
            int delay = Delay(w, id);
            At(w, st, target, delay, out tx, out tz);
            double vx = w.Vx[target], vz = w.Vz[target];
            if (delay > 0)
            {
                const int span = 2;
                if (At(w, st, target, delay + span, out double ox, out double oz) && At(w, st, target, delay, out double nx, out double nz))
                {
                    vx = (nx - ox) * cfg.SimHz / span;
                    vz = (nz - oz) * cfg.SimHz / span;
                }
            }
            if (windup > 0)
            {
                double lead = Math.Min(windup, CombatFeel.Ticks(w, f.BotLeadSeconds)) / cfg.SimHz;
                tx += vx * lead;
                tz += vz * lead;
            }
            double[] errs = f.BotMeleeAimErrorDeg;
            int k = (int)BotSkill.Of(w, id);
            double err = errs != null && k < errs.Length ? errs[k] : 0;
            if (err > 0)
            {
                double r = BotSkill.Roll(w, id, SaltMeleeAim, w.Tick / BotSkill.SwingWindowTicks) * 2 - 1;
                double ang = r * err * Math.PI / 180;
                double dx = tx - w.X[id], dz = tz - w.Z[id];
                double c = JsMath.Cos(ang), s = JsMath.Sin(ang);
                tx = w.X[id] + dx * c - dz * s;
                tz = w.Z[id] + dx * s + dz * c;
            }
        }

        /// <summary>The cosine of the press cone half-angle of bot id: its class arc (px S1 [2]); the b15 flat 60 degrees off.</summary>
        public static double PressCos(World w, int id, double flatCos)
        {
            if (!On(w) || !w.Config.Depth.CombatFeel.BotPressConeIsClassArc) return flatCos;
            return JsMath.Cos(w.Config.Class(w.Actors[id].Cls).AttackArcDeg / 2 * Math.PI / 180);
        }
    }
}
