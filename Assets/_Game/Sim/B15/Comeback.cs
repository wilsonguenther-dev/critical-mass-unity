// b15 rule A (owner approved 2026-11-17): the comeback rule. Every number lives in Sim/Config/ComebackConfig.cs.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>b15 rule A state on the World (World.Rally). Fixed arrays, sized once; reset in place. Integers only.</summary>
    public sealed class ComebackState
    {
        /// <summary>Per team: RALLY credit banked this match, in thousandths of a tick (the cap is checked on this integer).</summary>
        public readonly int[] BonusMilli = new int[2];
        /// <summary>Per team: 1 = the team earned RALLY credit on the last tick (HUD).</summary>
        public readonly byte[] Earning = new byte[2];
        /// <summary>Per team: the tick the team first earned RALLY credit (-1 never; the announcer's one line).</summary>
        public readonly int[] FirstTick = { -1, -1 };

        public void Init(World w) => Clear();
        public void Reset(World w) => Clear();

        void Clear()
        {
            Array.Clear(BonusMilli, 0, 2);
            Array.Clear(Earning, 0, 2);
            FirstTick[0] = -1;
            FirstTick[1] = -1;
        }
    }

    /// <summary>
    /// b15 rule A, the comeback rule. RALLY: while a team trails on hill seconds by more than TrailThresholdSeconds, every tick
    /// it owns the hill counts x(1 + RallyPerMille / 1000), until BonusCapSeconds of extra credit are banked (Hill.HillSystem).
    /// The RESPAWN CURVE: the knock-out wait grows from RespawnStartSeconds to RespawnEndSeconds over the match (Damage, the
    /// one place a fighter goes down). Deterministic: integer bookkeeping, no RNG, no allocation.
    /// </summary>
    public static class Comeback
    {
        /// <summary>The rule is on (Full profile and ComebackConfig.Enabled).</summary>
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Comeback.Enabled);

        /// <summary>The RALLY cap in thousandths of a tick.</summary>
        public static int CapMilli(GameConfig c) => (int)Math.Floor(c.Depth.Comeback.BonusCapSeconds * c.SimHz + 0.5) * 1000;

        /// <summary>The hill multiplier while RALLY runs (1.25).</summary>
        public static double Multiplier(GameConfig c) => 1 + c.Depth.Comeback.RallyPerMille / 1000.0;

        /// <summary>The other team's hill seconds minus this team's (positive = behind).</summary>
        public static double Deficit(World w, TeamId team)
        {
            PerTeam h = w.Match.HillSeconds;
            return team == TeamId.Azure ? h.Crimson - h.Azure : h.Azure - h.Crimson;
        }

        /// <summary>The team trails by more than the threshold and still has cap left (RALLY would count if it held the hill).</summary>
        public static bool Trailing(World w, TeamId team) =>
            On(w) && Deficit(w, team) > w.Config.Depth.Comeback.TrailThresholdSeconds && w.Rally.BonusMilli[(int)team] < CapMilli(w.Config);

        /// <summary>The team earned RALLY credit on the last tick.</summary>
        public static bool Earning(World w, TeamId team) => On(w) && w.Rally.Earning[(int)team] != 0;

        /// <summary>Extra hill seconds RALLY has given the team this match.</summary>
        public static double BonusSeconds(World w, TeamId team) => On(w) ? w.Rally.BonusMilli[(int)team] / 1000.0 / w.Config.SimHz : 0;

        /// <summary>Extra hill seconds RALLY can still give the team.</summary>
        public static double CapLeftSeconds(World w, TeamId team) =>
            On(w) ? Math.Max(0, CapMilli(w.Config) - w.Rally.BonusMilli[(int)team]) / 1000.0 / w.Config.SimHz : 0;

        /// <summary>
        /// Hill.HillSystem, once the owner of this tick is known and BEFORE its seconds are added: the extra seconds RALLY gives
        /// the owner this tick (0 = none). The deficit is read before this tick's credit, so the bonus stops the tick the gap
        /// reaches the threshold and can never carry a team into the lead by itself.
        /// </summary>
        public static double RallyCredit(World w, HillState next, double dt)
        {
            ComebackState s = w.Rally;
            s.Earning[0] = 0;
            s.Earning[1] = 0;
            if (!On(w)) return 0;
            TeamId team;
            if (next == HillState.Azure) team = TeamId.Azure;
            else if (next == HillState.Crimson) team = TeamId.Crimson;
            else return 0;
            if (!(Deficit(w, team) > w.Config.Depth.Comeback.TrailThresholdSeconds)) return 0;
            int t = (int)team;
            int left = CapMilli(w.Config) - s.BonusMilli[t];
            int add = Math.Min(left, Math.Max(0, w.Config.Depth.Comeback.RallyPerMille));
            if (add <= 0) return 0;
            s.BonusMilli[t] += add;
            s.Earning[t] = 1;
            if (s.FirstTick[t] < 0) s.FirstTick[t] = w.Tick;
            return add / 1000.0 * dt;
        }

        /// <summary>Ticks of a time in seconds (rounded), as the sim counts them.</summary>
        static long Ticks(double seconds, double hz) => (long)Math.Floor(seconds * hz + 0.5);

        /// <summary>
        /// Damage.ApplyHit, the knock-out: the wait before a fighter of team comes back. With both b15 rules off it is exactly
        /// RespawnConfig.DelaySeconds. With the comeback rule on it follows the curve (integer ticks); a team with the hut's
        /// QUICK RETURN waits HutChoicesConfig.QuickReturnSeconds less, never under RespawnFloorSeconds.
        /// </summary>
        public static double RespawnDelay(World w, TeamId team)
        {
            GameConfig c = w.Config;
            bool curve = On(w);
            bool quick = HutChoices.Has(w, team, HutOption.QuickReturn);
            if (!curve && !quick) return c.Respawn.DelaySeconds;
            double hz = c.SimHz;
            long ticks;
            if (curve)
            {
                ComebackConfig k = c.Depth.Comeback;
                long a = Ticks(k.RespawnStartSeconds, hz), b = Ticks(k.RespawnEndSeconds, hz);
                long total = Math.Max(1L, Ticks(c.MatchSeconds, hz));
                long now = Math.Min(total, Math.Max(0L, (long)w.Tick));
                long num = now, den = total;
                if (k.CurvePower >= 2)
                {
                    num = now * now;
                    den = total * total;
                }
                ticks = a + (b - a) * num / den;
            }
            else ticks = Ticks(c.Respawn.DelaySeconds, hz);
            if (quick)
            {
                HutChoicesConfig h = c.Depth.HutChoices;
                ticks = Math.Max(Ticks(h.RespawnFloorSeconds, hz), ticks - Ticks(h.QuickReturnSeconds, hz));
            }
            return ticks / hz;
        }
    }

    /// <summary>b15 rule A on-screen words, in ONE place for the owner to rename. Data only.</summary>
    public static class ComebackNames
    {
        public const string Rally = "RALLY";
        public const string EnemyRally = "ENEMY RALLY";
        public const string Hint = "Behind: your hill time counts more while you hold it.";
    }
}
