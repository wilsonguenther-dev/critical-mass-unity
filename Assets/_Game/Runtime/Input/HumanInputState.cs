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
    /// <para>Easy movement (2026-11-12): Fill builds the move through WorldMove: keys (screen basis, or the map basis with
    /// KeysFollowMap), else pointer walking while the MoveToPointer button is held, then MoveAssist's lane assist (one key
    /// only) and corner slide when MovementAssist is on and BindWorld gave it the world. Fill runs once per tick right
    /// before the step, so the assists see the position the step starts from and add no latency.</para>
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

        // Easy movement "Keys follow the map": the grid axis nearest screen up (ties turn clockwise on screen, so at
        // today's yaw 45 W goes sim -Z, up-right on screen, and D goes sim +X, toward the Crimson base, down-right).
        static readonly int KeyAxis = NearestAxis(ScreenUpX, ScreenUpZ);
        static readonly double[] AxisX = { 1, 0, -1, 0 };
        static readonly double[] AxisZ = { 0, 1, 0, -1 };
        /// <summary>World XZ direction of the "up" key while KeysFollowMap is on (a sim grid axis).</summary>
        public static readonly double MapUpX = AxisX[KeyAxis];
        public static readonly double MapUpZ = AxisZ[KeyAxis];
        /// <summary>World XZ direction of the "right" key while KeysFollowMap is on.</summary>
        public static readonly double MapRightX = -MapUpZ;
        public static readonly double MapRightZ = MapUpX;

        static int NearestAxis(double x, double z)
        {
            double q = Math.Floor(Math.Atan2(z, x) / (Math.PI / 2) + 0.5 + 1e-9);
            return (((int)q % 4) + 4) % 4;
        }

        // Neutral frame copied while paused (no allocation per step); never handed out.
        readonly InputFrame _empty = SimInput.EmptyInput();

        bool _paused;
        bool _scoreboardHeld;
        bool _interactHeld;
        bool _attackHeld;
        // controls-1 (hygiene): a press held through a death is ignored after the respawn until it is let go.
        bool _attackHoldOff;
        bool _aliveSeen, _wasAlive;
        bool _attackLatch;
        bool _abilityLatch;
        bool _terraformLatch;
        bool _switchLatch;
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
        bool _moveFromKeys;
        bool _walkHeld;
        bool _hasWalkTarget;
        double _walkX;
        double _walkZ;
        World _world;

        /// <summary>
        /// Easy movement: lane assist for a single held key plus corner sliding (MoveAssist), applied in Fill. Setting
        /// "Movement assist", default on; needs BindWorld. HumanInputSource copies the setting in.
        /// </summary>
        public bool MovementAssist { get; set; } = true;

        /// <summary>
        /// Easy movement setting "Keys follow the map" (default off): keyboard directions use the sim grid axes (MapUp,
        /// MapRight) instead of the screen axes. The controller stick always stays screen-relative.
        /// </summary>
        public bool KeysFollowMap { get; set; }

        // feat/ground-camera: camera-relative movement for the experimental ground camera (GroundView). While set, the move
        // keys and the stick go through this basis instead of the fixed screen one; Fill still writes a world-space move.
        bool _basisOn;
        double _bUpX, _bUpZ, _bRightX, _bRightZ;

        /// <summary>
        /// The ground camera's facing as the move basis (sim XZ, unit vectors): up = the camera forward on the ground, right
        /// = its right. KeysFollowMap does not apply under it. The sim input is unchanged in kind: InputFrame.MoveX / MoveZ.
        /// </summary>
        public void SetCameraBasis(double upX, double upZ, double rightX, double rightZ)
        {
            _basisOn = true;
            _bUpX = upX;
            _bUpZ = upZ;
            _bRightX = rightX;
            _bRightZ = rightZ;
        }

        /// <summary>Back to the fixed screen basis (the high camera).</summary>
        public void ClearCameraBasis() => _basisOn = false;

        /// <summary>True while SetCameraBasis is in force.</summary>
        public bool CameraBasisActive => _basisOn;

        void Basis(out double rx, out double rz, out double ux, out double uz)
        {
            if (_basisOn)
            {
                rx = _bRightX; rz = _bRightZ; ux = _bUpX; uz = _bUpZ;
                return;
            }
            bool map = _moveFromKeys && KeysFollowMap;
            rx = map ? MapRightX : ScreenRightX; rz = map ? MapRightZ : ScreenRightZ;
            ux = map ? MapUpX : ScreenUpX; uz = map ? MapUpZ : ScreenUpZ;
        }

        /// <summary>The world whose actor 0 the assists read (position, collision); null = no assist, no pointer walk.</summary>
        public void BindWorld(World w) => _world = w;

        /// <summary>The walk-to-pointer button is held (rebindable action MoveToPointer).</summary>
        public void SetPointerWalk(bool held) => _walkHeld = held;

        /// <summary>The ground point under the cursor for pointer walking, in sim world space.</summary>
        public void SetWalkTarget(double worldX, double worldZ)
        {
            _hasWalkTarget = true;
            _walkX = worldX;
            _walkZ = worldZ;
        }

        /// <summary>No ground point under the cursor: pointer walking stands still.</summary>
        public void ClearWalkTarget() => _hasWalkTarget = false;

        /// <summary>The walk-to-pointer button is held.</summary>
        public bool PointerWalkHeld => _walkHeld;

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
            _attackHoldOff = false;
            _aliveSeen = false;
            _wasAlive = false;
            _attackLatch = false;
            _abilityLatch = false;
            _terraformLatch = false;
            _switchLatch = false;
            _jumpLatch = false;
            _classPick = -1;
            _moveRight = 0;
            _moveUp = 0;
            _hasAim = false;
            _aimX = 0;
            _aimZ = 0;
            _attackBuffer.Clear();
            _abilityBuffer.Clear();
            _moveFromKeys = false;
            _walkHeld = false;
            _hasWalkTarget = false;
            _walkX = 0;
            _walkZ = 0;
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
                    if (repeat && _attackHoldOff) break;
                    _attackHoldOff = false;
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

        /// <summary>
        /// DG-09 (U-053): the dig / place mode switch was pressed (mouse wheel notch, LB or RB; HumanInputSource edge-detects
        /// the SwitchDigPlace axis). Latched like a button: one InputFrame.TerraformSwitch per press, dropped while paused.
        /// </summary>
        public void SwitchDigPlace()
        {
            if (!_paused) _switchLatch = true;
        }

        /// <summary>A button went up. Latches stay set until Fill consumes them.</summary>
        public void ButtonUp(HumanButton b)
        {
            switch (b)
            {
                case HumanButton.Attack:
                    _attackHeld = false;
                    _attackHoldOff = false;
                    break;
                case HumanButton.Interact:
                    _interactHeld = false;
                    break;
                case HumanButton.Scoreboard:
                    _scoreboardHeld = false;
                    break;
            }
        }

        /// <summary>
        /// The move intent in screen axes (right, up), each -1..1 (WASD composite or the left stick). fromKeys = it came
        /// from direction keys (KeysFollowMap and lane assist apply to keys only).
        /// </summary>
        public void SetMove(double right, double up, bool fromKeys = false)
        {
            _moveRight = right;
            _moveUp = up;
            _moveFromKeys = fromKeys;
        }

        /// <summary>
        /// controls-1: the move intent as a sim XZ direction through the same basis Fill uses (screen, or the map grid for
        /// keys with KeysFollowMap), before lane assist and corner sliding; (0, 0) when nothing moves. Keyboard aim reads it so
        /// the fighter faces the way the keys send it. Read-only: never changes what Fill writes.
        /// </summary>
        public void MoveDirection(out double x, out double z)
        {
            Basis(out double rx, out double rz, out double ux, out double uz);
            x = _moveRight * rx + _moveUp * ux;
            z = _moveRight * rz + _moveUp * uz;
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
            _attackHoldOff = false;
            _attackLatch = false;
            _abilityLatch = false;
            _terraformLatch = false;
            _switchLatch = false;
            _jumpLatch = false;
            _moveRight = 0;
            _moveUp = 0;
            _attackBuffer.Clear();
            _abilityBuffer.Clear();
            _moveFromKeys = false;
            _walkHeld = false;
            _hasWalkTarget = false;
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

        /// <summary>
        /// controls-1 (hygiene, owner veteran pass): the actor came back to life. An attack held (or mashed) through the death
        /// does not swing on the first moment of the new life: the hold is dropped until the key is let go and pressed
        /// again, and every latch and buffered press queued while dead is dropped. Presentation only (the frames the sim
        /// receives), so the sim is unchanged. Called by Fill on the dead-to-alive edge of BufferActor.
        /// </summary>
        public void HoldOffAfterRespawn()
        {
            if (_attackHeld) _attackHoldOff = true;
            _attackHeld = false;
            _attackLatch = false;
            _abilityLatch = false;
            _terraformLatch = false;
            _switchLatch = false;
            _jumpLatch = false;
            _attackBuffer.Clear();
            _abilityBuffer.Clear();
        }

        /// <summary>True while a hold from before the respawn is being ignored.</summary>
        public bool AttackHeldOff => _attackHoldOff;

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
                _switchLatch = false;
                _jumpLatch = false;
                _classPick = -1;
                _attackBuffer.Clear();
                _abilityBuffer.Clear();
                View.Mechanics.DepthInputLatch.Clear(); // mechanics depth: a ping or purchase never fires on unpause
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

            WorldMove(out double moveX, out double moveZ);
            output.MoveX = moveX;
            output.MoveZ = moveZ;

            World bw = BufferWorld;
            bool canBuffer = bw != null && bw.Config != null;
            int early = canBuffer ? InputBuffer.MaxEarlyTicks(bw.Config.SimHz) : 0;
            if (bw != null && BufferActor >= 0 && BufferActor < bw.ActorCount)
            {
                bool alive = bw.Actors[BufferActor].Alive;
                if (_aliveSeen && alive && !_wasAlive) HoldOffAfterRespawn();
                _wasAlive = alive;
                _aliveSeen = true;
            }
            bool attackNow = _attackBuffer.Step(_attackLatch, canBuffer, canBuffer && InputReadiness.Attack(bw, BufferActor), early);
            output.Attack = _attackHeld || attackNow;
            _attackLatch = false;
            output.Ability = _abilityBuffer.Step(_abilityLatch, canBuffer, canBuffer && InputReadiness.Ability(bw, BufferActor), early);
            _abilityLatch = false;
            output.Interact = _interactHeld;
            output.Terraform = _terraformLatch;
            _terraformLatch = false;
            output.TerraformSwitch = _switchLatch;
            _switchLatch = false;
            output.Jump = _jumpLatch;
            _jumpLatch = false;
            output.ClassPick = _classPick;
            _classPick = -1;
            // Mechanics depth (U-065 / U-067): the station purchase and the ping, each for exactly one tick.
            View.Mechanics.DepthInputLatch.WriteInto(output);
        }

        /// <summary>
        /// This tick's move in sim world space (length at most 1): the keys or stick through the screen basis (or the map
        /// basis for keys with KeysFollowMap), else pointer walking while its button is held; then, with MovementAssist
        /// and a bound world, lane assist for a single key and corner sliding (MoveAssist).
        /// </summary>
        /// <summary>
        /// True within a bridge's deck band plus one unit, from 1.5 u before its start to 1.5 u after its end: where corner
        /// slide stays off (EasyMovementTests.CornerSlideIsOffNearBridges...: with it on there, 148 of 3504 Sim crossings
        /// stall; with it off, none).
        /// </summary>
        public static bool NearBridge(double x, double z)
        {
            var all = CriticalMass.Sim.Maps.BridgeSpans.All;
            double reach = CriticalMass.Sim.Maps.SkybreakBasinMap.BridgeHalfWidth + 1.0;
            for (int i = 0; i < all.Count; i++)
            {
                CriticalMass.Sim.Maps.BridgeSpan sp = all[i];
                double a = CriticalMass.Sim.Maps.BridgeSpans.Along(sp, x, z);
                if (a < -1.5 || a > sp.Len + 1.5) continue;
                if (Math.Abs(CriticalMass.Sim.Maps.BridgeSpans.Lateral(sp, x, z)) <= reach) return true;
            }
            return false;
        }

        void WorldMove(out double mx, out double mz)
        {
            mx = 0;
            mz = 0;
            World w = _world;
            bool human = w != null && w.ActorCount > 0 && w.Actors[0].Alive;
            if (_moveRight != 0 || _moveUp != 0)
            {
                Basis(out double rx, out double rz, out double ux, out double uz);
                mx = _moveRight * rx + _moveUp * ux;
                mz = _moveRight * rz + _moveUp * uz;
                double len2 = mx * mx + mz * mz;
                if (len2 > 1)
                {
                    double inv = 1 / Math.Sqrt(len2);
                    mx *= inv;
                    mz *= inv;
                }
                // One direction key alone (never two keys, never the stick): follow the lane it points along.
                bool singleKey = _moveFromKeys && (_moveRight != 0) != (_moveUp != 0);
                if (human && MovementAssist && singleKey && MoveAssist.LaneSteer(w.X[0], w.Z[0], mx, mz, out double lx, out double lz))
                {
                    double m = Math.Sqrt(mx * mx + mz * mz);
                    mx = lx * m;
                    mz = lz * m;
                }
            }
            else if (human && _walkHeld && _hasWalkTarget)
            {
                if (!MoveAssist.PointerMove(w.X[0], w.Z[0], _walkX, _walkZ, out mx, out mz)) return;
            }
            else return;
            // Creator acceptance (bridge travel): on and around a bridge the sim's own rail rule (Collide.RailFirst, DG-02)
            // carries a body walking up to 60 degrees off the axis; the input layer's corner slide must not turn the move
            // away from the span there first (the creator's "fighting the keys" on diagonal bridges).
            if (human && MovementAssist && !NearBridge(w.X[0], w.Z[0]) && MoveAssist.CornerSlide(w, 0, mx, mz, out double sx, out double sz))
            {
                mx = sx;
                mz = sz;
            }
        }
    }
}
