using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>DG-40 per-match Healer counters (the soak reports them).</summary>
    public sealed class HealerStats
    {
        public int Mends;
        public int Pulses;
        public int Purifies;
        /// <summary>HP actually restored by mends and pulses (after the max-HP cap).</summary>
        public double HealingDone;
        public double InstabilityRemoved;

        public void Clear()
        {
            Mends = 0;
            Pulses = 0;
            Purifies = 0;
            HealingDone = 0;
            InstabilityRemoved = 0;
        }
    }

    /// <summary>DG-40 Healer state, allocated once per World at capacity and reset in place.</summary>
    public sealed class HealerState
    {
        /// <summary>Per actor id: first World.Tick it may purify again.</summary>
        public int[] PurifyReadyTick;
        /// <summary>Per actor id: InputFrame.Terraform of the previous step (rising edge).</summary>
        public byte[] PrevPressed;
        /// <summary>Per actor id: a bot's queued purify press (BotFsm sets it, BotBrain.FillBotInput turns it into one press).</summary>
        public byte[] BotPurify;
        /// <summary>Corruption units purified this match (Cleanse.NetCorruptionUnits subtracts them).</summary>
        public double PurifiedUnits;
        public HealerStats Stats;

        public static HealerState Create(GameConfig cfg)
        {
            int cap = World.ActorCapacity(cfg);
            return new HealerState
            {
                PurifyReadyTick = new int[cap],
                PrevPressed = new byte[cap],
                BotPurify = new byte[cap],
                Stats = new HealerStats(),
            };
        }

        /// <summary>Back to the start of a match, in place (World.InitWorld).</summary>
        public void Reset()
        {
            Array.Clear(PurifyReadyTick, 0, PurifyReadyTick.Length);
            Array.Clear(PrevPressed, 0, PrevPressed.Length);
            Array.Clear(BotPurify, 0, BotPurify.Length);
            PurifiedUnits = 0;
            Stats.Clear();
        }
    }

    /// <summary>
    /// DG-40 on-screen names of the Healer and her abilities, in ONE place for the owner to rename (the HUD and class cards
    /// read these). Data only: the sim never shows text.
    /// </summary>
    public static class HealerNames
    {
        public const string ClassName = "HEALER";
        /// <summary>The attack binding: a quick heal on one hurt ally.</summary>
        public const string Mend = "MEND";
        /// <summary>The class ability (Space / the ability button): an area heal.</summary>
        public const string Pulse = "HEALING WINGS";
        /// <summary>The second ability (the DigPlace binding: F, Y on a controller) at the egg: remove corruption.</summary>
        public const string Purify = "PURIFY";
    }

    /// <summary>
    /// DG-40 Healer (U-061; the oracle has no Healer: its only corruption removal is the CM-36 shrine cleanse and its only
    /// heal the CM-37 fountain). Travis 2026-11-06: "Healer / Heal / Remove corruption". Art direction: an armoured battle
    /// healer with wings and no sword; she heals and supports first (feat/light-weapons-1: her lantern, LightWeapons in Combat.cs, swings only when no hurt ally can be mended).
    /// <para>MEND (attack press, class AttackCooldown 0.8 s): the hurt living ally (not herself) within MendRange of her that is
    /// nearest the aim point (no aim: nearest her), lowest id on a tie, gets MendHeal HP (capped at its max). No hurt ally in
    /// range: nothing happens and no cooldown is spent. Mending does not end her spawn protection (it is not an attack).</para>
    /// <para>HEALING PULSE (Space, class AbilityCooldown 9 s): at the aim pulled back to PulseCastRange (no aim: ahead), every
    /// living ally within PulseRadius of that point, her included, gets PulseHeal HP (capped).</para>
    /// <para>PURIFY (DigPlace rising edge, own PurifyCooldownTicks): within PurifyRange of the shrine, while a cleanse could run
    /// at all (Cleanse.CleanseOpen: egg unhatched, match playing, berserk not locked) and corruption is in the egg
    /// (Cleanse.NetCorruptionUnits &gt; 0), removes min(PurifyUnits, net) units x CorruptionInstabilityPerUnit instability at
    /// once (clamped at 0), the CM-36 cleanse's units-to-instability rule; the units count as cleansed. It does not use or
    /// touch the team cleanse cooldown. A refused press pushes an Ability event with a Purify* code (a press on cooldown is
    /// silent).</para>
    /// Events: HealCast per cast, HealLanded per actor healed, Purify, each with its position (and radius). Every loop is in
    /// id order, timing is World.Tick: deterministic. Allocation-free.
    /// </summary>
    public static class Healer
    {
        /// <summary>HealCast kinds (SimEvent.B).</summary>
        public const int KindMend = 1, KindPulse = 2;
        /// <summary>Ability event values: a pulse; purify refusals.</summary>
        public const int PulseCode = 1, PurifyRefusedRange = -1, PurifyRefusedClosed = -2;

        private static double PtX;
        private static double PtZ;

        private static double Restore(World w, int target, int healer, double amount)
        {
            Actor t = w.Actors[target];
            double room = t.MaxHp - t.Hp;
            double d = amount < room ? amount : room;
            if (!(d > 0)) return 0;
            t.Hp += d;
            w.Healer.Stats.HealingDone += d;
            World.PushEvent(w, SimEventType.HealLanded, target, healer, w.X[target], w.Z[target], d);
            return d;
        }

        /// <summary>The ally Healer id would mend for aim (aimX, aimZ) (NaN = none), or -1.</summary>
        public static int MendTarget(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            double range = w.Config.Abilities.Healer.MendRange;
            double hx = w.X[id];
            double hz = w.Z[id];
            double px = double.IsFinite(aimX) ? aimX : hx;
            double pz = double.IsFinite(aimZ) ? aimZ : hz;
            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == id) continue;
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team != a.Team || !(t.Hp < t.MaxHp)) continue;
                double dx = w.X[j] - hx;
                double dz = w.Z[j] - hz;
                if (dx * dx + dz * dz > range * range) continue;
                double ax = w.X[j] - px;
                double az = w.Z[j] - pz;
                double d = ax * ax + az * az;
                if (d < bestD)
                {
                    bestD = d;
                    best = j;
                }
            }
            return best;
        }

        /// <summary>The attack press of a living, standing Healer whose attack cooldown ran out (Combat.CombatSystem). True when a mend landed.</summary>
        public static bool TryMend(World w, int id)
        {
            bool hasAim = w.HasAim[id] == 1;
            int t = MendTarget(w, id, hasAim ? w.Inputs[id].AimX : double.NaN, hasAim ? w.Inputs[id].AimZ : double.NaN);
            if (t < 0) return false;
            Actor a = w.Actors[id];
            a.AttackCd = w.Config.Class(ClassId.Healer).AttackCooldown;
            a.AnimAction = 0;
            double d = Restore(w, t, id, w.Config.Abilities.Healer.MendHeal);
            w.Healer.Stats.Mends++;
            World.PushEvent(w, SimEventType.HealCast, id, KindMend, w.X[t], w.Z[t], d);
            return true;
        }

        /// <summary>Healer id pulses at aim (aimX, aimZ) (NaN = ahead). Returns the HP restored in all.</summary>
        public static double Pulse(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            HealerConfig cfg = w.Config.Abilities.Healer;
            Abilities.ClampToRange(w.X[id], w.Z[id], aimX, aimZ, a.Facing, cfg.PulseCastRange, out PtX, out PtZ);
            double cx = PtX;
            double cz = PtZ;
            double r2 = cfg.PulseRadius * cfg.PulseRadius;
            double total = 0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team != a.Team) continue;
                double dx = w.X[j] - cx;
                double dz = w.Z[j] - cz;
                if (dx * dx + dz * dz > r2) continue;
                total += Restore(w, j, id, cfg.PulseHeal);
            }
            a.AbilityCd = w.Config.Class(ClassId.Healer).AbilityCooldown;
            a.AnimAction = 0;
            w.Healer.Stats.Pulses++;
            World.PushEvent(w, SimEventType.Ability, id, -1, cx, cz, PulseCode);
            World.PushEvent(w, SimEventType.HealCast, id, KindPulse, cx, cz, total, null, DamageCause.None, cfg.PulseRadius);
            return total;
        }

        /// <summary>The Space press of a Healer (Abilities.AbilitiesSystem): a pulse at her aim, or ahead without one.</summary>
        public static void CastPulse(World w, int id)
        {
            bool hasAim = w.HasAim[id] == 1;
            Pulse(w, id, hasAim ? w.Inputs[id].AimX : double.NaN, hasAim ? w.Inputs[id].AimZ : double.NaN);
        }

        /// <summary>0 when Healer id may purify right now (cooldown aside), else a Purify* refusal code.</summary>
        public static int PurifyRefusal(World w, int id)
        {
            double r = w.Config.Abilities.Healer.PurifyRange;
            double dx = w.X[id] - w.Map.Shrine.X;
            double dz = w.Z[id] - w.Map.Shrine.Z;
            if (dx * dx + dz * dz > r * r) return PurifyRefusedRange;
            if (!Cleanse.CleanseOpen(w) || !(Cleanse.NetCorruptionUnits(w) > 0)) return PurifyRefusedClosed;
            return 0;
        }

        /// <summary>Would a purify press by actor id work now (class, alive, cooldown, range, open)? Bots ask first.</summary>
        public static bool CanPurify(World w, int id)
        {
            Actor a = w.Actors[id];
            return a != null && a.Alive && a.Cls == ClassId.Healer && w.Tick >= w.Healer.PurifyReadyTick[id] && PurifyRefusal(w, id) == 0;
        }

        /// <summary>Healer id purifies. Returns the instability removed (&gt;= 0), or a negative Purify* code (Ability event); -10 on cooldown (silent).</summary>
        public static double Purify(World w, int id)
        {
            HealerState s = w.Healer;
            if (w.Tick < s.PurifyReadyTick[id]) return -10;
            int why = PurifyRefusal(w, id);
            if (why != 0)
            {
                World.PushEvent(w, SimEventType.Ability, id, -1, w.X[id], w.Z[id], why);
                return why;
            }
            MatchState m = w.Match;
            double net = Cleanse.NetCorruptionUnits(w);
            double units = Math.Min(w.Config.Abilities.Healer.PurifyUnits, net);
            double amount = units * w.Config.KaijuFeed.CorruptionInstabilityPerUnit;
            double removed = amount < m.Instability ? amount : m.Instability > 0 ? m.Instability : 0;
            m.Instability -= removed;
            s.PurifiedUnits += units;
            s.PurifyReadyTick[id] = w.Tick + Durations.T30(w.Config, w.Config.Abilities.Healer.PurifyCooldownTicks);
            s.Stats.Purifies++;
            s.Stats.InstabilityRemoved += removed;
            w.Actors[id].AnimAction = 0;
            int teamIdx = w.Actors[id].Team == TeamId.Azure ? 0 : 1;
            World.PushEvent(w, SimEventType.Purify, id, teamIdx, w.Map.Shrine.X, w.Map.Shrine.Z, removed, null, DamageCause.None, w.Config.Abilities.Healer.PurifyRange);
            return removed;
        }

        /// <summary>Ticks until actor id may purify again (0 = ready).</summary>
        public static int PurifyWaitTicks(World w, int id)
        {
            int left = w.Healer.PurifyReadyTick[id] - w.Tick;
            return left > 0 ? left : 0;
        }

        /// <summary>Input half (end of Abilities.AbilitiesSystem): a living, standing Healer purifies on the RISING edge of InputFrame.Terraform (DigPlace).</summary>
        public static void HealerInputSystem(World w)
        {
            HealerState s = w.Healer;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                byte pressed = w.Inputs[i].Terraform ? (byte)1 : (byte)0;
                bool rising = pressed == 1 && s.PrevPressed[i] == 0;
                s.PrevPressed[i] = pressed;
                if (!rising || !a.Alive || a.Cls != ClassId.Healer || a.StandUp > 0) continue;
                Purify(w, i);
            }
        }

        /// <summary>Bots: queue one purify press for bot id (BotFsm).</summary>
        public static void RequestBotPurify(World w, int id)
        {
            w.Healer.BotPurify[id] = 1;
        }

        /// <summary>Bots: take bot id's queued purify into output (a DigPlace press); false when none is queued or it died.</summary>
        public static bool TakeBotPurify(World w, int id, InputFrame output)
        {
            HealerState s = w.Healer;
            if (s.BotPurify[id] != 1) return false;
            s.BotPurify[id] = 0;
            if (!w.Actors[id].Alive) return false;
            output.Terraform = true;
            return true;
        }
    }
}
