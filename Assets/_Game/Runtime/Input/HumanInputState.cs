using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// The human's input state between two sim steps (browser src/game/humanInput.ts module state). Pure C#: the
    /// HumanInputSource MonoBehaviour (F-08a) reads InputSystem.actions into it, and Fill turns it into actor 0's
    /// InputFrame once per sim step.
    /// <para>Edge latch (P1): rendering and the sim run at different rates, so Jump, Ability, DigPlace and Attack presses
    /// are latched on the down and cleared only when Fill consumes them (one true frame per press; repeat downs never
    /// re-latch). A held Attack stays true every step (the sim's cooldown gates repeats). Interact is held.</para>
    /// <para>Pause toggles Paused; while paused every down except Pause is dropped (never replayed) and Fill writes a
    /// neutral frame with no aim.</para>
    /// <para>Input buffer (DG-14): with BufferWorld set (InputBufferBinder does it), an Attack or Ability press the sim
    /// would refuse on its step (cooldown, getting up) is also delivered once on the first step, up to
    /// InputBuffer.MaxEarlyTicks later (0.15 s), on which the sim accepts it. The press step itself is unchanged, so the
    /// only difference the sim can see is that press arriving on its first ready step. Counted in sim steps (one per
    /// Fill), so the same presses on the same ticks always give the same frames. BufferWorld null = the plain latch.</para>
    /// </summary>
    public sealed class HumanInputState
    {
        /// <summary>The bound game buttons (U-006 Controls table).</summary>
        public enum HumanButton { Jump, Attack, Ability, Interact, DigPlace, Pause, Scoreboard }

        // Fixed screen -> world basis for the camera yaw (45 degrees: up = (-1, -1)/sqrt2, right = (1, -1)/sqrt2).
        static readonly double Yaw = new RenderConfig().CameraYawDeg * Math.PI / 180;

        /// <summary>World XZ direction of "screen up": the camera forward projected on the ground.</summary>
        public static readonly double ScreenUpX = -Math.Sin(Yaw);
        public static readonly double ScreenUpZ = -Math.Cos(Yaw);
        /// <summary>World XZ direction of "screen right": forward x world up.</summary>
        public static readonly double ScreenRightX = -ScreenUpZ;
        public static readonly double ScreenRightZ = ScreenUpX;

        // Neutral frame copied while paused (no allocation per step); never handed out.
        readonly InputFrame _empty = SimInput.EmptyInput();

        bool _paused;
        bool _scoreboardHeld;
        bool _interactHeld;
        bool _attackHeld;
        bool _attackLatch;
        bool _abilityLatch;
        bool _terraformLatch;
        bool _jumpLatch;
        int _classPick = -1;
        double _moveRight;
        double _moveUp;
        bool _hasAim;
        double _aimX;
        double _aimZ;
        readonly InputBuffer _attackBuffer = new InputBuffer();
        readonly InputBuffer _abilityBuffer = new InputBuffer();

        /// <summary>The world the buffer reads readiness from (read-only); null = no buffering.</summary>
        public World BufferWorld;

        /// <summary>The actor this input drives (actor 0, HudBinder.LocalActor).</summary>
        public int BufferActor;

        /// <summary>An Attack press is waiting for a ready step.</summary>
        public bool AttackBuffered => _attackBuffer.Pending;

        /// <summary>An Ability press is waiting for a ready step.</summary>
        public bool AbilityBuffered => _abilityBuffer.Pending;

        /// <summary>The pause menu is open (browser ui.paused).</summary>
        public bool Paused => _paused;

        /// <summary>The scoreboard key is held (Tab).</summary>
        public bool ScoreboardHeld => _scoreboardHeld;

        /// <summary>Back to "nothing pressed, no move, no aim, no queued pick, not paused".</summary>
        public void Reset()
        {
            _paused = false;
            _scoreboardHeld = false;
            _interactHeld = false;
            _attackHeld = false;
            _attackLatch = false;
            _abilityLatch = false;
            _terraformLatch = false;
            _jumpLatch = false;
            _classPick = -1;
            _moveRight = 0;
            _moveUp = 0;
            _hasAim = false;
            _aimX = 0;
            _aimZ = 0;
            _attackBuffer.Clear();
            _abilityBuffer.Clear();
        }

        /// <summary>A button went down. repeat = an OS auto-repeat down while held (never re-latches, never re-toggles).</summary>
        public void ButtonDown(HumanButton b, bool repeat = false)
        {
            if (b == HumanButton.Pause)
            {
                if (!repeat) _paused = !_paused;
                return;
            }
            if (_paused) return;
            switch (b)
            {
                case HumanButton.Jump:
                    if (!repeat) _jumpLatch = true;
                    break;
                case HumanButton.Attack:
                    _attackHeld = true;
                    if (!repeat) _attackLatch = true;
                    break;
                case HumanButton.Ability:
                    if (!repeat) _abilityLatch = true;
                    break;
                case HumanButton.Interact:
                    _interactHeld = true;
                    break;
                case HumanButton.DigPlace:
                    if (!repeat) _terraformLatch = true;
                    break;
                case HumanButton.Scoreboard:
                    _scoreboardHeld = true;
                    break;
            }
        }

        /// <summary>A button went up. Latches stay set until Fill consumes them.</summary>
        public void ButtonUp(HumanButton b)
        {
            switch (b)
            {
                case HumanButton.Attack:
                    _attackHeld = false;
                    break;
                case HumanButton.Interact:
                    _interactHeld = false;
                    break;
                case HumanButton.Scoreboard:
                    _scoreboardHeld = false;
                    break;
            }
        }

        /// <summary>The move intent in screen axes (right, up), each -1..1 (WASD composite or the left stick).</summary>
        public void SetMove(double right, double up)
        {
            _moveRight = right;
            _moveUp = up;
        }

        /// <summary>The pointer's aim point on the ground in sim world space (the raycast is HumanInputSource's).</summary>
        public void SetAim(double worldX, double worldZ)
        {
            _hasAim = true;
            _aimX = worldX;
            _aimZ = worldZ;
        }

        /// <summary>True while the pointer has a ground aim point (SetAim since the last ClearAim).</summary>
        public bool HasAim => _hasAim;
        /// <summary>The last aim point (sim x), valid while HasAim.</summary>
        public double AimX => _aimX;
        /// <summary>The last aim point (sim z), valid while HasAim.</summary>
        public double AimZ => _aimZ;

        /// <summary>No pointer over the game view, or the ray missed: "no aim" (non-finite, D-011).</summary>
        public void ClearAim()
        {
            _hasAim = false;
            _aimX = 0;
            _aimZ = 0;
        }

        /// <summary>Focus lost: releases will never arrive, so nothing may stay held, moving or latched.</summary>
        public void Blur()
        {
            _scoreboardHeld = false;
            _interactHeld = false;
            _attackHeld = false;
            _attackLatch = false;
            _abilityLatch = false;
            _terraformLatch = false;
            _jumpLatch = false;
            _moveRight = 0;
            _moveUp = 0;
            _attackBuffer.Clear();
            _abilityBuffer.Clear();
        }

        /// <summary>Respawn class re-pick from the UI (CM-10): written into InputFrame.ClassPick once by the next Fill.</summary>
        public void QueueClassPick(ClassId cls)
        {
            for (int k = 0; k < ClassIds.All.Count; k++)
            {
                if (ClassIds.All[k] == cls)
                {
                    _classPick = k;
                    return;
                }
            }
        }

        /// <summary>Fill actor 0's frame for one sim step and consume the latches and the queued class pick.</summary>
        public void Fill(InputFrame output)
        {
            if (_paused)
            {
                output.CopyFrom(_empty);
                output.AimX = double.NaN;
                output.AimZ = double.NaN;
                _attackLatch = false;
                _abilityLatch = false;
                _terraformLatch = false;
                _jumpLatch = false;
                _classPick = -1;
                _attackBuffer.Clear();
                _abilityBuffer.Clear();
                return;
            }

            if (_hasAim)
            {
                output.AimX = _aimX;
                output.AimZ = _aimZ;
            }
            else
            {
                output.AimX = double.NaN;
                output.AimZ = double.NaN;
            }

            if (_moveRight == 0 && _moveUp == 0)
            {
                output.MoveX = 0;
                output.MoveZ = 0;
            }
            else
            {
                double mx = _moveRight * ScreenRightX + _moveUp * ScreenUpX;
                double mz = _moveRight * ScreenRightZ + _moveUp * ScreenUpZ;
                double len2 = mx * mx + mz * mz;
                if (len2 > 1)
                {
                    double inv = 1 / Math.Sqrt(len2);
                    mx *= inv;
                    mz *= inv;
                }
                output.MoveX = mx;
                output.MoveZ = mz;
            }

            World bw = BufferWorld;
            bool canBuffer = bw != null && bw.Config != null;
            int early = canBuffer ? InputBuffer.MaxEarlyTicks(bw.Config.SimHz) : 0;
            bool attackNow = _attackBuffer.Step(_attackLatch, canBuffer, canBuffer && InputReadiness.Attack(bw, BufferActor), early);
            output.Attack = _attackHeld || attackNow;
            _attackLatch = false;
            output.Ability = _abilityBuffer.Step(_abilityLatch, canBuffer, canBuffer && InputReadiness.Ability(bw, BufferActor), early);
            _abilityLatch = false;
            output.Interact = _interactHeld;
            output.Terraform = _terraformLatch;
            _terraformLatch = false;
            output.Jump = _jumpLatch;
            _jumpLatch = false;
            output.ClassPick = _classPick;
            _classPick = -1;
        }
    }
}
