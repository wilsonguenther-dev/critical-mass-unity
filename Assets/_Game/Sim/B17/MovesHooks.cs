// Class moves build (2026-11-22), lane 1: the ONE place shared sim files call into the class-moves lanes. Every line in a shared
// file that calls this class ends with "// mv-hook". Lane 1 owns this file and MovesState.cs; lanes 2-5 fill ONLY their own
// MovesHooks.<Lane>.cs (Defence, Movement, Offence, Bots) and never edit this one. Contract: LANE1-CONTRACT.md.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// The class-moves switch gate and the intent reader. World.Moves is null unless the match was built with the Full rules
    /// and at least one MovesConfig switch on (MovesState.Create), so with every switch off every dispatcher below returns
    /// its input before touching any lane: the simulation is bit-identical to the base branch (x * 1.0 == x in IEEE doubles).
    /// <para>Inside a lane, a rule acts only while its own switch is on: test it with <see cref="On(World, bool)"/>, e.g.
    /// <c>if (!MovesHooks.On(w, MovesHooks.Cfg(w).Guard)) return amount;</c>.</para>
    /// </summary>
    public static partial class MovesHooks
    {
        /// <summary>The class moves knobs and switches of this world (never null: DepthConfig.Moves).</summary>
        public static MovesConfig Cfg(World w) => w.Config.Depth.Moves;

        /// <summary>Any class-moves switch is live in this world (World.Moves allocated: Full rules and a switch on at CreateWorld).</summary>
        public static bool On(World w) => w.Moves != null;

        /// <summary>One switch is live: the world has class-moves state and the switch is true.</summary>
        public static bool On(World w, bool sw) => sw && w.Moves != null;

        // ================================================================== step (SimStep.cs)

        /// <summary>SimStep.Step, first thing after the tick counter and the Prev positions (before Abilities): reads this tick's
        /// intents from w.Inputs (MovesState: GuardPressed, AttackHeldTicks, ...), then each lane's per-tick step in lane
        /// order Defence, Movement, Offence, Bots.</summary>
        public static void BeginStep(World w, double dt)
        {
            if (w.Moves == null) return;
            ReadIntents(w);
            DefenceStep(w, dt);
            MovementStep(w, dt);
            OffenceStep(w, dt);
            BotsStep(w, dt);
        }

        /// <summary>SimStep.Step, before DepthState.EndStep: remembers this tick's held buttons for next tick's edges.</summary>
        public static void EndStep(World w)
        {
            MovesState s = w.Moves;
            if (s == null) return;
            for (int i = 0; i < w.ActorCount; i++) s.GuardPrev[i] = w.Inputs[i].Guard ? (byte)1 : (byte)0;
        }

        /// <summary>Lane 1: per actor, this tick's intents from its applied InputFrame (MovesState documents each field).
        /// A dead or getting-up actor holds nothing (counters reset; a button still held after the respawn counts from 0
        /// only once it is let go and pressed again, like controls-1's hold-off).</summary>
        static void ReadIntents(World w)
        {
            MovesState s = w.Moves;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                InputFrame f = w.Inputs[i];
                bool live = a.Alive && a.StandUp <= 0;
                bool guard = live && f.Guard;
                s.GuardPressed[i] = guard && s.GuardPrev[i] == 0 ? (byte)1 : (byte)0;
                if (s.GuardPressed[i] != 0) s.LastGuardPressTick[i] = w.Tick;
                s.GuardHeldTicks[i] = guard ? s.GuardHeldTicks[i] + 1 : 0;
                bool atk = live && f.Attack;
                s.AttackReleased[i] = !atk && s.AttackHeldTicks[i] > 0 ? (byte)1 : (byte)0;
                s.AttackReleasedAfter[i] = s.AttackReleased[i] != 0 ? s.AttackHeldTicks[i] : 0;
                s.AttackHeldTicks[i] = atk ? s.AttackHeldTicks[i] + 1 : 0;
                bool jump = live && f.JumpHeld;
                s.JumpHeldTicks[i] = jump ? s.JumpHeldTicks[i] + 1 : 0;
                if (live && f.Dodge) s.LastDodgePressTick[i] = w.Tick;
                bool move = live && f.MoveX * f.MoveX + f.MoveZ * f.MoveZ > 1e-8;
                s.MoveHeldTicks[i] = move ? s.MoveHeldTicks[i] + 1 : 0;
            }
        }

        // ================================================================== Combat.cs

        /// <summary>Combat.CombatSystem, right after the attack want is read (buffer included): lane 4 turns a hold into a charge
        /// (no auto-repeat), fires the heavy on release and takes a combo press. Returns the want (identity when off).</summary>
        public static bool WantsAttack(World w, int i, bool wants) => w.Moves == null ? wants : OffenceWantsAttack(w, i, wants);

        /// <summary>Combat.CombatSystem, before a swing may start: true = no swing this tick (guard held, landing recovery: lane 2;
        /// gliding, sliding: lane 3).</summary>
        public static bool AttackLocked(World w, int i) => w.Moves != null && (DefenceLocksAttack(w, i) || MovementLocksAttack(w, i));

        /// <summary>Combat.CombatSystem, when a swing starts (press tick), with its wind-up in ticks: lane 2 (LUNGE x0.75) then
        /// lane 4 (combo step x0.7, heavy release) may shorten it and record what this swing is. Returns the wind-up.</summary>
        public static int SwingWindup(World w, int i, int windup) => w.Moves == null ? windup : OffenceSwingWindup(w, i, DefenceSwingWindup(w, i, windup));

        /// <summary>Combat.ResolveSwing on the contact tick, after the class (or light weapon) reach, arc and damage are read and
        /// before any target is found: lane 4's heavy shape and charge / combo damage. Identity when off.</summary>
        public static void SwingShape(World w, int i, ref double range, ref double arcDeg, ref double dmg)
        {
            if (w.Moves == null) return;
            OffenceSwingShape(w, i, ref range, ref arcDeg, ref dmg);
        }

        /// <summary>Combat.ResolveSwing, after CombatFeel.AfterContact: hits = bodies hit plus 1 when a structure or the Kaiju
        /// answered (0 = a whiff). Lane 2 (lunge done), then lane 4 (combo advance / reset, whiff recovery).</summary>
        public static void AfterContact(World w, int i, int hits)
        {
            if (w.Moves == null) return;
            DefenceAfterContact(w, i, hits);
            OffenceAfterContact(w, i, hits);
        }

        // ================================================================== CombatFeel.cs (lane 2's file; these lines are lane 1's)

        /// <summary>CombatFeel.ApplyStrike, before the strike is applied: the knock distance and stagger ticks of this melee
        /// strike. Lane 4 (heavy / finisher knock, heavy stagger) then lane 2 (charge armour is lane 4's; guard break stagger).</summary>
        public static void StrikeKnock(World w, int source, int target, ref double knock, ref int stun)
        {
            if (w.Moves == null) return;
            OffenceStrikeKnock(w, source, target, ref knock, ref stun);
            DefenceStrikeKnock(w, source, target, ref knock, ref stun);
        }

        /// <summary>CombatFeel.TakeCancel, right after a FEINT (Feint Value 1) was pushed for actor i: lane 2 raises FeintBaited.</summary>
        public static void Feinted(World w, int i)
        {
            if (w.Moves == null) return;
            DefenceFeinted(w, i);
        }

        // ================================================================== Damage.cs

        /// <summary>Damage.ApplyHit, after spawn protection, team and the dodge's immunity, before any scale: true = this hit is
        /// negated (0 damage, no Hit event): lane 2's parry (BlockedParried) and jump evade (BlockedAir). report = the
        /// lane S2 reporting rule says HitBlocked may be raised.</summary>
        public static bool HitNegated(World w, int target, int source, double srcX, double srcZ, DamageCause cause, bool report) =>
            w.Moves != null && DefenceHitNegated(w, target, source, srcX, srcZ, cause, report);

        /// <summary>Damage.ApplyHit, after the Warrior's shield cut: the damage after lane 2's guard cut (meter drain, BlockedGuard,
        /// GuardBreak). Identity when off.</summary>
        public static double HitCut(World w, int target, int source, double srcX, double srcZ, DamageCause cause, double dmg, bool report) =>
            w.Moves == null ? dmg : DefenceHitCut(w, target, source, srcX, srcZ, cause, dmg, report);

        /// <summary>Damage.ApplyHit, once HP is taken (dmg &gt; 0, before the death check): every lane hears it (a hit ends travel
        /// and glide, blocks travel for 3 s for both sides, may interrupt a charge).</summary>
        public static void Damaged(World w, int target, int source, DamageCause cause, double dmg)
        {
            if (w.Moves == null) return;
            DefenceDamaged(w, target, source, cause, dmg);
            MovementDamaged(w, target, source, cause, dmg);
            OffenceDamaged(w, target, source, cause, dmg);
        }

        // ================================================================== Movement.cs and Jump.cs

        /// <summary>Movement.MovementSystem, for a living grounded actor, after the fall rule and BEFORE the dodge step: true = lane
        /// 3 moved it this tick (a SLIDE, which consumes the Dodge press while TRAVEL is lit) and its walk update is skipped.</summary>
        public static bool GroundStep(World w, int i, double dt) => w.Moves != null && MovementGroundStep(w, i, dt);

        /// <summary>Movement.MovementSystem, with the other walk-speed multipliers: travel (lane 3) x guard (lane 2) x charge (lane 4).
        /// Exactly 1 when off.</summary>
        public static double SpeedScale(World w, int i) =>
            w.Moves == null ? 1.0 : MovementSpeedScale(w, i) * DefenceSpeedScale(w, i) * OffenceSpeedScale(w, i);

        /// <summary>Movement.FlyTick, first thing of an airborne tick (before AirTick++): lane 3's glide (steering, the held apex).</summary>
        public static void AirTick(World w, int i, double dt)
        {
            if (w.Moves == null) return;
            MovementAirTick(w, i, dt);
        }

        /// <summary>Jump.StartJump, after the alive / standing / airborne checks: true = refuse the jump (lane 3's relock after an
        /// evading jump, the landing recovery).</summary>
        public static bool JumpLocked(World w, int id) => w.Moves != null && MovementJumpLocked(w, id);

        /// <summary>Jump.LandJump, first thing (the flight's last tick ended): lane 3 (landing recovery, glide end) then lane 2.</summary>
        public static void Landed(World w, int id)
        {
            if (w.Moves == null) return;
            MovementLanded(w, id);
            DefenceLanded(w, id);
        }

        // ================================================================== Abilities.cs

        /// <summary>Abilities.AbilitiesSystem, before a cast: true = no cast this tick (guard held: lane 2; gliding: lane 3).</summary>
        public static bool CastLocked(World w, int i) => w.Moves != null && (DefenceLocksCast(w, i) || MovementLocksCast(w, i));

        // ================================================================== Bots/BotBrain.cs

        /// <summary>BotBrain.FillBotInput, last (after BotReflex): lane 5 writes a bot's guard / parry / charge / step-out / TNT
        /// request into its frame. The frame's Guard and JumpHeld are already false.</summary>
        public static void BotsFillInput(World w, int id, InputFrame output)
        {
            if (w.Moves == null) return;
            BotsFill(w, id, output);
        }
    }
}
