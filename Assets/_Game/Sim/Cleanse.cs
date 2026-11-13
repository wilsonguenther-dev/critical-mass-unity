using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM-36: the shrine cleanse, the one simple way to remove corruption (browser branch cm/CM-36 src/sim/cleanse.ts; Travis
    /// 2026-11-06 "A way to remove corruption (healer special or something else)"). All timing in sim ticks
    /// (KaijuFeed.Cleanse):
    ///  - Open only while the egg is unhatched, the match is playing and berserk is not locked.
    ///  - A team may start one when its cooldown is 0, no cleanse is in flight, and net corruption units (delivered by both
    ///    teams minus units already cleansed) are above 0. None of these read instability.
    ///  - An actor starts it by holding interact, empty-handed, inside ShrineInteractRange for HoldTicks. The press must be
    ///    fresh: carrying a load disarms the actor until interact is released, so the press that delivers never cleanses.
    ///    U-054: a meat load counts as a load here (the oracle's cleanse predates meat riding outside the cargo).
    ///  - It drains Units x CorruptionInstabilityPerUnit instability evenly over Ticks from the start tick, clamped at 0;
    ///    the cleansing team waits CooldownTicks from the start.
    ///  - The hatch, a berserk lock or the match end stops a cleanse in flight (CleanseEnd with what it drained).
    /// Events: Cleanse at the start, CleanseEnd at the end. Allocation-free.
    /// </summary>
    public static class Cleanse
    {
        /// <summary>Instability one cleanse drains: Cleanse.Units x CorruptionInstabilityPerUnit, read live (18 at 9 per unit).</summary>
        public static double CleanseAmount(GameConfig c)
        {
            return c.KaijuFeed.Cleanse.Units * c.KaijuFeed.CorruptionInstabilityPerUnit;
        }

        /// <summary>Corruption units still counted in the egg: delivered by both teams minus units already cleansed (never negative).</summary>
        public static double NetCorruptionUnits(World w)
        {
            DeliveredTally[] d = w.Match.Delivered;
            // DG-40: units a Healer purified count as cleansed too (0 without Healers, so the CM-36 rule is unchanged).
            double net = d[(int)TeamId.Azure].Corruption + d[(int)TeamId.Crimson].Corruption - w.Cleanse.Count * w.Config.KaijuFeed.Cleanse.Units - w.Healer.PurifiedUnits;
            return net > 0 ? net : 0;
        }

        /// <summary>True while a cleanse may run at all: egg unhatched, match playing, berserk not locked.</summary>
        public static bool CleanseOpen(World w)
        {
            MatchState m = w.Match;
            return m.Phase == MatchPhase.Playing && !m.Hatched && w.Kaiju.State == KaijuState.Egg && !m.BerserkLocked;
        }

        /// <summary>True when team teamIdx (0 azure, 1 crimson) may start a cleanse right now.</summary>
        public static bool CleanseReady(World w, int teamIdx)
        {
            if (teamIdx != 0 && teamIdx != 1) return false;
            CleanseState c = w.Cleanse;
            return CleanseOpen(w) && c.ActiveTicks == 0 && c.Cooldown[teamIdx] == 0 && NetCorruptionUnits(w) > 0;
        }

        /// <summary>Start a cleanse for team teamIdx (actorId = the cleanser, -1 scripted). False (nothing changes) when the team may not cleanse now or the index is invalid.</summary>
        public static bool StartCleanse(World w, int actorId, int teamIdx)
        {
            if (teamIdx != 0 && teamIdx != 1) return false;
            if (!CleanseReady(w, teamIdx)) return false;
            CleanseConfig cfg = w.Config.KaijuFeed.Cleanse;
            CleanseState c = w.Cleanse;
            double amount = CleanseAmount(w.Config);
            int ticks = cfg.Ticks > 0 ? (int)System.Math.Floor(cfg.Ticks) : 1;
            c.ActiveTicks = ticks;
            c.Team = teamIdx;
            c.Actor = actorId;
            c.PerTick = amount / ticks;
            c.Drained = 0;
            c.Cooldown[teamIdx] = (int)cfg.CooldownTicks;
            c.Count++;
            World.PushEvent(w, SimEventType.Cleanse, actorId, teamIdx, w.Map.Shrine.X, w.Map.Shrine.Z, amount);
            return true;
        }

        private static void FinishCleanse(World w)
        {
            CleanseState c = w.Cleanse;
            World.PushEvent(w, SimEventType.CleanseEnd, c.Actor, c.Team, w.Map.Shrine.X, w.Map.Shrine.Z, c.Drained);
            c.ActiveTicks = 0;
            c.Team = -1;
            c.Actor = -1;
            c.PerTick = 0;
            c.Drained = 0;
        }

        /// <summary>One tick: cooldowns, hold-to-cleanse for every actor, then the drain of the cleanse in flight. Called by KaijuSystem first on every step of a playing match.</summary>
        public static void CleanseSystem(World w)
        {
            MatchState m = w.Match;
            CleanseState c = w.Cleanse;
            CleanseConfig cfg = w.Config.KaijuFeed.Cleanse;
            if (c.Cooldown[0] > 0) c.Cooldown[0]--;
            if (c.Cooldown[1] > 0) c.Cooldown[1]--;
            bool open = CleanseOpen(w);

            int n = w.ActorCount;
            double sx = w.Map.Shrine.X;
            double sz = w.Map.Shrine.Z;
            double r = w.Config.KaijuFeed.ShrineInteractRange;
            double r2 = r * r;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || a.CarryAmount > 0 || a.CarryMeat > 0)
                {
                    c.Armed[id] = 0;
                    c.Hold[id] = 0;
                    continue;
                }
                if (!w.Inputs[id].Interact)
                {
                    c.Armed[id] = 1;
                    c.Hold[id] = 0;
                    continue;
                }
                int teamIdx = a.Team == TeamId.Azure ? 0 : 1;
                double dx = w.X[id] - sx;
                double dz = w.Z[id] - sz;
                if (!open || c.Armed[id] == 0 || a.StandUp > 0 || dx * dx + dz * dz > r2 || !CleanseReady(w, teamIdx))
                {
                    c.Hold[id] = 0;
                    continue;
                }
                if (c.Hold[id] < ushort.MaxValue) c.Hold[id]++;
                if (c.Hold[id] >= cfg.HoldTicks)
                {
                    c.Hold[id] = 0;
                    c.Armed[id] = 0;
                    StartCleanse(w, id, teamIdx);
                }
            }

            if (c.ActiveTicks <= 0) return;
            if (!open)
            {
                FinishCleanse(w);
                return;
            }
            double d = c.PerTick < m.Instability ? c.PerTick : m.Instability > 0 ? m.Instability : 0;
            m.Instability -= d;
            c.Drained += d;
            c.Removed += d;
            c.ActiveTicks--;
            if (c.ActiveTicks == 0) FinishCleanse(w);
        }
    }
}
