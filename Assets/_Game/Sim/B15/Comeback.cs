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
        /// <summary>Per team: ticks of unbroken hill ownership (HOLD FATIGUE).</summary>
        public readonly int[] HoldTicks = new int[2];
        /// <summary>The team (0 Azure, 1 Crimson) that knocked a sided Kaiju down and gets it at the get-up; -1 none (TURNCOAT).</summary>
        public int KoTeam = -1;
        /// <summary>Times the Kaiju turned this match (TURNCOAT; tests and the HUD).</summary>
        public int Turns;

        public void Init(World w) => Clear();
        public void Reset(World w) => Clear();

        void Clear()
        {
            Array.Clear(BonusMilli, 0, 2);
            Array.Clear(Earning, 0, 2);
            FirstTick[0] = -1;
            FirstTick[1] = -1;
            HoldTicks[0] = 0;
            HoldTicks[1] = 0;
            KoTeam = -1;
            Turns = 0;
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
            On(w) && w.Config.Depth.Comeback.RallyOn && Deficit(w, team) > w.Config.Depth.Comeback.TrailThresholdSeconds && w.Rally.BonusMilli[(int)team] < CapMilli(w.Config);

        /// <summary>FORWARD RETURN is on for the team right now (it trails by more than the threshold).</summary>
        public static bool ForwardActive(World w, TeamId team) =>
            On(w) && w.Config.Depth.Comeback.ForwardReturnOn && (Deficit(w, team) > w.Config.Depth.Comeback.TrailThresholdSeconds
                || (w.Config.Depth.Comeback.ForwardWhenHillLost && w.Match.HillState == (team == TeamId.Azure ? HillState.Crimson : HillState.Azure)));

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
            if (!On(w) || !w.Config.Depth.Comeback.RallyOn) return 0;
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
            bool on = On(w);
            bool curve = on && c.Depth.Comeback.RespawnCurveOn;
            bool wave = on && c.Depth.Comeback.WaveSeconds > 0;
            bool quick = HutChoices.Has(w, team, HutOption.QuickReturn);
            if (!curve && !quick && !wave) return c.Respawn.DelaySeconds;
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
            if (wave)
            {
                // WAVES: come back on the next beat of the team clock at or after the own wait (same beat for both teams)
                long beat = Math.Max(1L, Ticks(c.Depth.Comeback.WaveSeconds, hz));
                long now = Math.Max(0L, (long)w.Tick);
                long at = now + ticks;
                long up = (at + beat - 1) / beat * beat;
                ticks = up - now;
            }
            return ticks / hz;
        }

        // ------------------------------------------------------------------ b15 pass 2 levers

        /// <summary>
        /// Hill.HillSystem, the owner by bodies (next) before scoring: MAJORITY turns a narrow lead of bodies into Contested
        /// (nobody scores) when both teams stand on the hill. A team alone on the hill always owns it.
        /// </summary>
        public static HillState Majority(World w, HillState next, int azure, int crimson)
        {
            if (!On(w)) return next;
            int margin = w.Config.Depth.Comeback.MajorityMargin;
            if (margin <= 1 || azure == 0 || crimson == 0) return next;
            if (next != HillState.Azure && next != HillState.Crimson) return next;
            int diff = azure > crimson ? azure - crimson : crimson - azure;
            return diff >= margin ? next : HillState.Contested;
        }

        /// <summary>
        /// Hill.HillSystem, once per tick with this tick's owner: HOLD FATIGUE's share of a second the owner scores (1 = full),
        /// from the owner's unbroken hold. Integer ticks and per mille; the streak resets for a team that does not own the hill.
        /// </summary>
        public static double HoldRate(World w, HillState next)
        {
            if (!On(w) || !w.Config.Depth.Comeback.HoldFatigueOn) return 1.0;
            ComebackState s = w.Rally;
            int owner = next == HillState.Azure ? 0 : next == HillState.Crimson ? 1 : -1;
            for (int t = 0; t < 2; t++) s.HoldTicks[t] = t == owner ? s.HoldTicks[t] + 1 : 0;
            if (owner < 0) return 1.0;
            ComebackConfig k = w.Config.Depth.Comeback;
            double hz = w.Config.SimHz;
            long grace = Ticks(k.HoldGraceSeconds, hz), span = Math.Max(1L, Ticks(k.HoldSpanSeconds, hz));
            long over = Math.Min(span, Math.Max(0L, s.HoldTicks[owner] - grace));
            int min = Math.Max(0, Math.Min(1000, k.HoldMinPerMille));
            long milli = 1000 - (1000 - min) * over / span;
            return milli / 1000.0;
        }

        /// <summary>The owner's HOLD FATIGUE share right now (HUD; 1 when off or not tiring).</summary>
        public static double HoldShare(World w, TeamId team)
        {
            if (!On(w) || !w.Config.Depth.Comeback.HoldFatigueOn) return 1.0;
            ComebackConfig k = w.Config.Depth.Comeback;
            double hz = w.Config.SimHz;
            long grace = Ticks(k.HoldGraceSeconds, hz), span = Math.Max(1L, Ticks(k.HoldSpanSeconds, hz));
            long over = Math.Min(span, Math.Max(0L, w.Rally.HoldTicks[(int)team] - grace));
            int min = Math.Max(0, Math.Min(1000, k.HoldMinPerMille));
            return (1000 - (1000 - min) * over / span) / 1000.0;
        }

        /// <summary>Kaiju KO (Kaiju.Hatch DamageKaiju): TURNCOAT remembers the team that knocked a sided Kaiju down.</summary>
        public static void OnKaijuKo(World w, int sourceId)
        {
            if (!On(w) || !w.Config.Depth.Comeback.KaijuTurncoat) return;
            KaijuState st = w.Kaiju.State;
            if (st != KaijuState.Azure && st != KaijuState.Crimson) return;
            if (sourceId < 0 || sourceId >= w.ActorCount) return;
            TeamId by = w.Actors[sourceId].Team;
            if ((by == TeamId.Azure && st == KaijuState.Azure) || (by == TeamId.Crimson && st == KaijuState.Crimson)) return;
            w.Rally.KoTeam = (int)by;
        }

        /// <summary>Kaiju get-up (Kaiju.Ai): TURNCOAT turns the Kaiju to the team that knocked it down.</summary>
        public static void OnKaijuRecover(World w)
        {
            ComebackState s = w.Rally;
            if (s.KoTeam < 0) return;
            int t = s.KoTeam;
            s.KoTeam = -1;
            if (!On(w) || !w.Config.Depth.Comeback.KaijuTurncoat) return;
            KaijuState st = w.Kaiju.State;
            if (st != KaijuState.Azure && st != KaijuState.Crimson) return;
            KaijuState to = t == 0 ? KaijuState.Azure : KaijuState.Crimson;
            if (to == st) return;
            w.Kaiju.State = to;
            w.Kaiju.TargetId = -1;
            s.Turns++;
        }

        /// <summary>
        /// Respawn.RespawnSystem, after the pad slot is chosen: FORWARD RETURN moves a trailing team's respawn ForwardShare of the
        /// way toward the hill, keeping the slot's offset from the pad so teammates do not stack; only onto open floor.
        /// </summary>
        public static void ForwardSpawn(World w, int id, double radius, ref double x, ref double z)
        {
            if (!On(w) || !w.Config.Depth.Comeback.ForwardReturnOn) return;
            TeamId team = w.Actors[id].Team;
            if (!ForwardActive(w, team)) return;
            CriticalMass.Sim.Grid.PadDef pad = w.Map.Pads[(int)team];
            CriticalMass.Sim.Grid.MapDef m = w.Map;
            int r = (int)Math.Ceiling(radius) + 1;
            // the furthest clear point on the pad-to-hill line at or under ForwardShare, in twentieths (integer steps)
            int steps = (int)Math.Floor(Math.Max(0, Math.Min(0.9, w.Config.Depth.Comeback.ForwardShare)) * 20 + 1e-9);
            for (int k = steps; k >= 2; k--)
            {
                double f = k / 20.0;
                double nx = x + (w.Map.Hill.X - pad.X) * f, nz = z + (w.Map.Hill.Z - pad.Z) * f;
                if (!Clear(m, nx, nz, r)) continue;
                x = nx;
                z = nz;
                return;
            }
        }

        static bool Clear(CriticalMass.Sim.Grid.MapDef m, double x, double z, int r)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                if (gx < 0 || gz < 0 || gx >= m.Width || gz >= m.Height) return false;
                byte c = m.Cells[gz * m.Width + gx];
                if (c != Cell.Floor && c != Cell.BreakableDestroyed && c != Cell.Bridge) return false;
            }
            return true;
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
