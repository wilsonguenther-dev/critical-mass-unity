// Class moves build (2026-11-22), lane 1: the class-moves state on the World (World.Moves). Lane 1 owns this file; each lane
// keeps its own state in its own class, declared in its MovesHooks.<Lane>.cs (DefenceMovesState, MovementMovesState,
// OffenceMovesState, BotsMovesState), and adds fields there. Every field is sim state: public, in a sealed class, arrays of
// primitives sized once in Init (no per-tick allocation). A lane that adds a field regenerates Net/StateHash.Generated.cs
// at the combine (StateHashCoverageTests writes Temp/ci/StateHash.Generated.cs).
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// World.Moves: null unless the world was created with the Full rules and at least one MovesConfig switch on, so with every
    /// switch off nothing is allocated and nothing is fingerprinted (StateHash mixes World.Moves only when it is not null).
    /// <para>Intents (written by MovesHooks.ReadIntents at the start of every tick from the applied InputFrame; read-only for
    /// lanes 2-5): see each field.</para>
    /// </summary>
    public sealed class MovesState
    {
        /// <summary>1 on the tick InputFrame.Guard went from released to held (the PARRY press edge), else 0.</summary>
        public byte[] GuardPressed = new byte[0];
        /// <summary>Consecutive ticks Guard has been held including this one (1 on the press tick), 0 = released.</summary>
        public int[] GuardHeldTicks = new int[0];
        /// <summary>Guard as it was at the end of last tick (edge bookkeeping, MovesHooks.EndStep).</summary>
        public byte[] GuardPrev = new byte[0];
        /// <summary>Consecutive ticks Attack has been held including this one (1 on the press tick), 0 = released.</summary>
        public int[] AttackHeldTicks = new int[0];
        /// <summary>1 on the tick Attack was let go (held last tick, not this one), else 0.</summary>
        public byte[] AttackReleased = new byte[0];
        /// <summary>On a release tick: how many ticks Attack had been held (the heavy's charge length); else 0.</summary>
        public int[] AttackReleasedAfter = new int[0];
        /// <summary>Consecutive ticks InputFrame.JumpHeld has been true including this one, 0 = released (the glide).</summary>
        public int[] JumpHeldTicks = new int[0];
        /// <summary>Consecutive ticks with a move intent (|move| &gt; 1e-4) including this one, 0 = standing (travel).</summary>
        public int[] MoveHeldTicks = new int[0];
        /// <summary>World.Tick of the last Guard press edge, -1 never (FeintBaited).</summary>
        public int[] LastGuardPressTick = new int[0];
        /// <summary>World.Tick of the last tick InputFrame.Dodge was set, -1 never (FeintBaited).</summary>
        public int[] LastDodgePressTick = new int[0];

        /// <summary>Lane 2 (T1-2, T1-3, T1-4a, T2-1, T2-7 hit test, T2-8): MovesHooks.Defence.cs.</summary>
        public DefenceMovesState Defence = new DefenceMovesState();
        /// <summary>Lane 3 (T2-4, T2-5, T2-6, T2-7 air window and landing): MovesHooks.Movement.cs.</summary>
        public MovementMovesState Movement = new MovementMovesState();
        /// <summary>Lane 4 (T1-1, T2-2, T2-3): MovesHooks.Offence.cs.</summary>
        public OffenceMovesState Offence = new OffenceMovesState();
        /// <summary>Lane 5 (T1-4b, T1-5, bot use of the verbs): MovesHooks.Bots.cs.</summary>
        public BotsMovesState Bots = new BotsMovesState();

        /// <summary>The world's class-moves state, or null when no switch is on or the rules are not Full (the base game).</summary>
        public static MovesState Create(GameConfig cfg)
        {
            MovesConfig m = cfg.Depth.Moves;
            if (m == null || !m.AnyOn) return null;
            if (cfg.Depth.Classic || cfg.Physics.Classic || !CombatFeel.On(cfg)) return null;
            return new MovesState();
        }

        /// <summary>World.CreateWorld, once the actor arrays exist: every array sized to the actor capacity.</summary>
        public void Init(World w)
        {
            int n = World.ActorCapacity(w.Config);
            GuardPressed = new byte[n];
            GuardHeldTicks = new int[n];
            GuardPrev = new byte[n];
            AttackHeldTicks = new int[n];
            AttackReleased = new byte[n];
            AttackReleasedAfter = new int[n];
            JumpHeldTicks = new int[n];
            MoveHeldTicks = new int[n];
            LastGuardPressTick = new int[n];
            LastDodgePressTick = new int[n];
            Defence.Init(w);
            Movement.Init(w);
            Offence.Init(w);
            Bots.Init(w);
            Reset(w);
        }

        /// <summary>World.InitWorld (CreateWorld and ResetWorld): back to the match start.</summary>
        public void Reset(World w)
        {
            System.Array.Clear(GuardPressed, 0, GuardPressed.Length);
            System.Array.Clear(GuardHeldTicks, 0, GuardHeldTicks.Length);
            System.Array.Clear(GuardPrev, 0, GuardPrev.Length);
            System.Array.Clear(AttackHeldTicks, 0, AttackHeldTicks.Length);
            System.Array.Clear(AttackReleased, 0, AttackReleased.Length);
            System.Array.Clear(AttackReleasedAfter, 0, AttackReleasedAfter.Length);
            System.Array.Clear(JumpHeldTicks, 0, JumpHeldTicks.Length);
            System.Array.Clear(MoveHeldTicks, 0, MoveHeldTicks.Length);
            for (int i = 0; i < LastGuardPressTick.Length; i++) LastGuardPressTick[i] = -1;
            for (int i = 0; i < LastDodgePressTick.Length; i++) LastDodgePressTick[i] = -1;
            Defence.Reset(w);
            Movement.Reset(w);
            Offence.Reset(w);
            Bots.Reset(w);
        }
    }
}
