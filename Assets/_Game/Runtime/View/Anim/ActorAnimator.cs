using System;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// One fighter's animation state and pose (animation pass, 2026-11-12). ActorViews calls Pose once per view update after
    /// placing the model; this turns sim state into clip weights and times on the two ModelView layers, the drawn rotation
    /// (yaw spring, bank, acceleration pitch, hit twist) and scale (squash), then evaluates the graph (or skips it under the
    /// animation level of detail). Reads the sim and CombatFx only; never writes the sim. No allocation per frame.
    /// <para>Layer 0 (full body), first match wins: end screen (Cheer for the winners, Sit_Floor for the losers) / respawn
    /// get-up (Lie_StandUp from RenderConfig.StandUpClipStart over Respawn.StandUpSeconds) / jump (Jump_Start compressed
    /// into the first 2 air ticks, Jump_Idle by flight phase, Jump_Land with a squash after touchdown) / locomotion
    /// (directional, speed matched, one phase: Locomotion) blended with heavy-hit staggers, gather and idle breaks.</para>
    /// <para>Layer 1 (spine up): the class's attack timed by AttackTiming (contact frame on the damage tick, follow-through
    /// past the cooldown, cross-fade on a re-trigger), the class's ability (Warrior Block then Blocking while the shield is
    /// up, Miner Use_Item, Farmer Spellcast_Shoot; never the chop), light and blocked hits, and the carry pose.</para>
    /// </summary>
    public sealed class ActorAnimator
    {
        // Facing spring (DG-89): critically damped, capped at 45 degrees per frame at 60 fps, pace 2 (TurningIsSmoothButFast:
        // no one-frame snap). b15 world look (WORLD-AUDIT "turns snap"): omega 40 -> 50 inside the same cap, so every turn
        // settles within 2 degrees in 0.10 s at Fast pace and a 30 degree re-aim in 0.10 s at Standard; a 180 at Standard
        // is held to 0.2 s by the cap itself (180 / 1350 = 0.133 s minimum). WorldLookMotionTests prints every case.
        public const double YawOmega = 50.0, MaxYawRate = 1350.0, BankMaxDeg = 8.0, BankGain = 0.011, PitchMaxDeg = 5.0, PitchGain = 0.2;
        public const double VelocityTau = 0.06, TeleportSpeed = 30.0;
        public const double IdleBreakMin = 4.0, IdleBreakSpread = 4.0, BreakFade = 0.2, GatherFade = 0.1, CarryFade = 0.15;
        public const double LandSeconds = 0.3, TakeoffTicks = 2.0;

        readonly int _id;
        readonly float[] _w6 = new float[Locomotion.DirCount];
        // Locomotion entries (clip, reversed, weight, length, stride, plant) merged per frame; max 12 (6 dirs x 2 bands).
        readonly string[] _eClip = new string[12];
        readonly bool[] _eRev = new bool[12];
        readonly float[] _eW = new float[12];
        int _eCount;

        double _phase, _lastX, _lastZ, _vx, _vz, _speed, _prevSpeed, _accel, _leftDeg;
        bool _hasLast;
        // Sideways sectors (fix/animation-reds): the strafe kept by the hysteresis, the leg warp applied after evaluation
        // (degrees, + left) and where the drawn lower body points relative to the drawn facing (tests).
        int _side = Locomotion.NoSide;
        double _warp;
        float _legYaw;
        /// <summary>The bone the leg warp turns (the KayKit rig's pelvis, parent of the legs and the spine).</summary>
        public const string HipsBone = "hips";
        double _yaw, _yawVel;
        bool _hasYaw;
        float _roll, _pitch;

        // Actions (attack or ability) and the cross-fade of the one before.
        double _lastAnimAction = double.PositiveInfinity;
        byte _kind;
        string _actClip;
        int _slot;
        string _prevClip;
        double _prevTime, _prevAt = double.NegativeInfinity;
        float _prevW;
        bool _prevLoop;
        double _lastActTime;
        float _lastActW;
        float _holdW;

        // px lane C (2026-11-22): the committed swing (legs commit standing still, whiff over-rotation, feint abort) and the
        // SwingCommit ledger value 2 frames after the press.
        float _legActW;
        double _legActT;
        float _whiffTwist, _whiffPitch;
        int _feintSeen = int.MinValue;
        bool _feinting;
        double _feintAt, _feintFromT;
        float _feintFromW;
        int _swingStartFrame = int.MinValue;
        bool _commitPending, _commitReady;
        float _commitW;

        /// <summary>px lane C: true once per swing, 2 rendered frames after it began, with the attack pose weight then (the
        /// SwingCommit ledger value; ActorViews reports it while the model is drawn).</summary>
        /// <summary>Fix round 2: the attack pose weight drawn this frame when a swing clip is playing (0 otherwise).</summary>
        public float SwingWeightNow => _kind == 1 && _actClip != null ? _lastActW : 0f;

        // fix round 5 (item 4): a local press shown before the sim's tick takes it (presentation only).
        bool _predicting;
        double _predWall;
        int _predSwingTick;
        float _predFloor;
        double _predFloorT;
        public bool Predicting => _predicting;
        // fix round 7: a cooldown press's acknowledgement (capped, never handed over); the press's wall clock for the floor
        bool _predRefused;
        double _predFloorWall = double.NaN;
        /// <summary>Fix round 7: the last predicted press ended without the sim's swing (refused, aborted or timed out).</summary>
        public bool LastPredictionEnded { get; private set; }
        /// <summary>Fix round 6: predicted wind-ups aborted (tests / logs).</summary>
        public int PredictAborts { get; private set; }

        /// <summary>Fix round 6 (item 1): the predicted clip becomes the fading previous clip (0.06 s re-trigger fade) and
        /// leaves the action slot free for whatever the sim started (its own pose) or for nothing.</summary>
        void AbortPrediction(double simTime)
        {
            _predicting = false;
            _predFloor = 0f;
            _predFloorWall = double.NaN;
            PredictAborts++;
            LastPredictionEnded = true;
            if (_kind == 1 && _actClip != null)
            {
                if (_lastActW > 0f)
                {
                    _prevClip = _actClip; _prevTime = _lastActTime; _prevW = _lastActW; _prevLoop = false; _prevAt = simTime; _slot = 1 - _slot;
                }
                _actClip = null;
                _lastActW = 0f;
                _commitPending = _commitReady = false;
            }
        }

        /// <summary>Fix round 5 (item 4): the local player pressed attack this frame and the sim would accept it next step:
        /// start the attack clip's wind-up now (the sim's AttackStart continues it without a crossfade).</summary>
        public void PredictSwing(ModelView m, Actor actor, GameConfig cfg, double simTime) => PredictSwing(m, actor, cfg, simTime, false);

        /// <summary>Fix round 7: refused = the sim will not take this press (the attack is cooling down): the same wind-up, capped
        /// at HitFeelTuning.RefusedPressWeight, that fades out by 0.12 s (press acknowledged, never a strike).</summary>
        public void PredictSwing(ModelView m, Actor actor, GameConfig cfg, double simTime, bool refused)
        {
            if (m == null || actor == null || _predicting || (_kind == 1 && _actClip != null && _lastActW > 0f)) return;
            string clip = AttackTiming.AttackClip(m, actor.Cls);
            if (clip == null || !m.HasClip(clip)) return;
            if (_actClip != null && _lastActW > 0f)
            {
                _prevClip = _actClip; _prevTime = _lastActTime; _prevW = _lastActW; _prevLoop = false; _prevAt = simTime; _slot = 1 - _slot;
            }
            _feinting = false;
            _whiffTwist = _whiffPitch = 0f;
            _kind = 1;
            _actClip = clip;
            if (!_reachMeasured || _tipModel != m || _vcClip != clip) MeasureReach(m, actor, clip);
            if (_exModel != m || _exClip != clip)
            {
                _exModel = m;
                _exClip = clip;
                _exTime = AttackTiming.ExtremeNow(m, clip, ViewContact(clip, ClipFacts.Contact(clip, m.ClipLength(clip) * 0.5f)));
            }
            _actExtreme = _exTime;
            _swingStartFrame = Time.frameCount; _commitPending = true; _commitReady = false;
            _lastActW = 0f;
            _predicting = true;
            _predWall = Time.unscaledTimeAsDouble;
            _predSwingTick = actor.SwingTick;
            _predFloor = 0f;
            _predFloorWall = double.NaN;
            _predRefused = refused;
            LastPredictionEnded = false;
            if (refused) { _commitPending = false; _commitReady = false; }   // no SwingCommit for a refused press
        }

        public bool TakeSwingCommit(out float weight)
        {
            weight = _commitW;
            if (!_commitReady) return false;
            _commitReady = false;
            return true;
        }

        // Hits.
        int _lastHitTick = int.MinValue;
        HitReaction _react;
        bool _hasReact;

        // Jump and land.
        bool _wasAir;
        double _landAt = double.NegativeInfinity;
        int _lastTick = -1;

        // Idle life, gather, carry.
        double _idleT, _nextBreak = -1, _breakAt = -1, _gatherT;
        int _breaks;
        string _breakClip;
        float _gather, _carry;

        // Death and the end screen.
        bool _wasCorpse;
        float _corpseYaw;
        string _deathClip;
        double _endT;
        bool _wasEnded;

        int _fresh;
        // Sim time of the last Pose (NaN after Reset): the motion step during a hit-stop hold (fix/animation-reds).
        double _lastPoseT = double.NaN;

        public ActorAnimator(int id) { _id = id; }

        /// <summary>The drawn yaw (degrees) and its rate; the rendered speed and velocity (Unity XZ).</summary>
        public float Yaw => (float)_yaw;
        public float YawRate => (float)_yawVel;
        public float Speed => (float)_speed;
        public Vector3 Velocity => new Vector3((float)_vx, 0f, (float)_vz);
        public double Phase => _phase;
        /// <summary>Velocity direction relative to the drawn facing, degrees, + left (what picks the locomotion clips).</summary>
        public float TravelLeftDeg => (float)_leftDeg;
        /// <summary>Leg warp applied this frame, degrees, + left (0 outside the sideways sectors).</summary>
        public float LegWarpDeg => (float)_warp;
        /// <summary>
        /// Where the drawn lower body points relative to the drawn facing, degrees, + left: the weighted clips' own leg yaw
        /// (Locomotion.LegYawDeg: 0 for Run and its reverse, +-60 for the strafes) plus the warp. 0 when not in locomotion.
        /// </summary>
        public float LegYawDeg => _legYaw;
        /// <summary>The strafe index the sideways hysteresis holds, or Locomotion.NoSide.</summary>
        public int StrafeSide => _side;
        public byte ActionKind => _kind;
        public string ActionClip => _actClip;
        public bool HasReaction => _hasReact;
        public HitReaction Reaction => _react;

        /// <summary>Called when the model is (re)taken from the pool: forget motion history, evaluate the first frames.</summary>
        public void Reset()
        {
            _lastCarried = -1; _climbW = 0f;
            _terraClip = null;
            _hasLast = false;
            _hasYaw = false;
            _vx = _vz = _speed = _prevSpeed = _accel = 0;
            _actClip = _prevClip = null;
            _kind = 0;
            _lastAnimAction = double.PositiveInfinity;
            _wasCorpse = false;
            _wasAir = false;
            _hasReact = false;
            _fresh = AnimLod.FreshFrames;
            _idleT = 0;
            _breakAt = -1;
            _side = Locomotion.NoSide;
            _warp = 0;
            _legYaw = 0f;
            _lastPoseT = double.NaN;
        }

        /// <summary>
        /// Builds and applies actor i's pose. simX/simZ: the interpolated sim point the model stands on (before nudges and
        /// jump lift); basePos: its Unity position. Sets the model's rotation and scale. Returns true when the bones were
        /// evaluated this frame. state is filled for the post-pose hook.
        /// </summary>
        public bool Pose(ModelView m, World w, GameConfig cfg, int i, double alpha, double simTime, double dSim, Vector3 basePos,
            CombatFx combat, bool hasFx, in ActorPoseFx fx, ref ActorPoseState state)
        {
            Actor actor = w.Actors[i];
            double hz = cfg.SimHz;
            bool corpse = hasFx && fx.Corpse;
            bool holding = hasFx && fx.Holding;
            CombatFeed feed = combat != null ? combat.Feed : null;

            // fix/animation-reds: ActorViews passes dSim = 0 during a hit-stop hold (loops must not advance), and the facing
            // spring and the velocity used it too, so a held fighter kept the yaw and velocity of its last unheld update
            // while the sim turned it: up to 0.22 sim s stale in a match, and in the bot facing test (one view update per
            // 15 ticks) half a second stale, read as turned around. Motion (velocity, facing, lean) follows sim time;
            // only loops (loopDt below) stop during the hold.
            double dMotion = holding && !double.IsNaN(_lastPoseT) ? Math.Max(0, simTime - _lastPoseT) : dSim;
            _lastPoseT = simTime;

            // ---- rendered velocity from the drawn displacement (DG-75: never the sim's intended velocity)
            if (_hasLast && dMotion > 0)
            {
                double dx = basePos.x - _lastX, dz = basePos.z - _lastZ;
                double inst = Math.Sqrt(dx * dx + dz * dz) / dMotion;
                if (inst > TeleportSpeed) { _vx = _vz = 0; _hasYaw = false; }
                else
                {
                    double k = 1 - Math.Exp(-dMotion / VelocityTau);
                    _vx += (dx / dMotion - _vx) * k;
                    _vz += (dz / dMotion - _vz) * k;
                }
            }
            _lastX = basePos.x;
            _lastZ = basePos.z;
            _hasLast = true;
            _speed = corpse ? 0 : Math.Sqrt(_vx * _vx + _vz * _vz);
            if (dMotion > 0)
            {
                double acc = (_speed - _prevSpeed) / dMotion;
                _accel += (acc - _accel) * (1 - Math.Exp(-dMotion / 0.1));
                _prevSpeed = _speed;
            }
            double loopDt = holding ? 0 : dSim; // the fake hit-stop: loops do not advance during the hold

            // ---- facing (DG-89)
            // feat/hit-feel-1: through a swing the body turns to the enemy it swings at (HitFeel.Facing; the sim's Facing otherwise).
            // fix/hit-feel-3: a fighter in a hit reaction (staggered) is never turned; it keeps the sim's facing.
            // fix/hit-feel-4: nor is a moving one (half the locomotion move weight or more): the legs run along the sim
            // facing, so turning the body to the target there only ever fights the movement facing.
            bool turnable = !_hasReact && Locomotion.MoveWeight(_speed) < 0.5f;
            double target = SimSpace.YawDeg(turnable ? HitFeel.Facing(w, i, alpha) : actor.Facing);
            if (corpse)
            {
                if (!_wasCorpse) LatchDeath(m, actor, feed, i, target);
                _yaw = _corpseYaw;
                _yawVel = 0;
            }
            else
            {
                _wasCorpse = false;
                StepYaw(target, dMotion);
                // b15: the swing's direction is exact on its contact frame (the spring may still be settling there).
                if (turnable && _actClip != null && _kind == 1)
                {
                    double age = AttackTiming.SwingAge(actor, w.Tick, alpha, hz), windup = AttackTiming.SwingWindupSeconds(actor, hz);
                    // never more than one capped frame of turn (no one-frame snap)
                    if (Math.Abs(Wrap(_yaw - target)) <= MaxYawRate * dMotion && ContactSnap(age, windup, dMotion, ref _snappedAt, _actStart)) { _yaw = target; _yawVel = 0; }
                }
            }

            // ---- actions: AnimAction resets to 0 on the press tick of an attack or an ability
            double aa = actor.AnimAction;
            // feat/skill-effects-1: TNT and Purify reset AnimAction without the cooldowns above, so they played no clip; the
            // skill lane's cue (Throw, Cast, set on the skill's event tick) starts one. Root Seeds' cue swaps Cast for Throw.
            string cue = CriticalMass.Runtime.View.Skills.SkillAnimCues.FreshClip(i, w.Tick);
            // fix round 6 (item 1): a predicted wind-up hands over to the sim's swing, or fades when anything else started
            if (_predicting)
            {
                bool other = (aa + 1e-9 < _lastAnimAction || cue != null) && actor.SwingTick == _predSwingTick;
                HitFeelTuning.PredictStep step = HitFeelTuning.StepPrediction(actor.SwingTick != _predSwingTick, other, Time.unscaledTimeAsDouble - _predWall);
                if (step == HitFeelTuning.PredictStep.Abort) AbortPrediction(simTime);
            }
            if (!corpse && aa + 1e-9 < _lastAnimAction && (actor.AttackCd > 0 || actor.AbilityCd > 0 || cue != null)) BeginAction(m, actor, cfg, simTime, cue, w.Tick);
            _lastAnimAction = aa;

            // ---- hits
            if (feed != null && i < feed.LastHitTick.Length && feed.LastHitTick[i] != _lastHitTick)
            {
                _lastHitTick = feed.LastHitTick[i];
                if (_lastHitTick > int.MinValue && _lastHitTick >= 0) LatchHit(actor, cfg, feed, i);
            }

            // ---- jump edges
            bool air = actor.AirLen > 0;
            bool newTick = w.Tick != _lastTick;
            _lastTick = w.Tick;
            if (newTick && !air && _wasAir)
            {
                _landAt = simTime;
                Footfalls.Emit(new Footfall { Tick = w.Tick, Actor = i, Kind = FootfallKind.Land, Position = basePos, Strength = Footfalls.LandStrength,
                    Radius = Footfalls.LandRadius, Team = actor.Team });
            }
            if (newTick) _wasAir = air;

            bool ended = w.Match != null && w.Match.Phase == MatchPhase.Ended;
            if (ended && !_wasEnded) _endT = 0;
            _wasEnded = ended;
            if (ended) _endT += dSim > 0 ? dSim : Time.unscaledDeltaTime;

            _hitAgeForPose = hasFx ? fx.HitT - (fx.Holding ? fx.HoldOffset : 0) : 1e9;
            m.BeginPose();
            Vector3 scale = Vector3.one * (hasFx ? fx.Scale : 1f);
            float idleW = 0f;
            float twist = 0f;
            bool upperAllowed = !corpse;
            _warp = 0;
            _legYaw = 0f;
            _heavyUpper = 0f;   // fix/hit-feel-4: PoseGround sets it; air, stand-up and the end screen do not

            if (corpse)
            {
                if (!m.Pose(_deathClip, fx.DeathT, 1f, false) && !m.Pose(ArtMapping.Death, fx.DeathT, 1f, false))
                    m.Pose(ArtMapping.Hit, fx.DeathT, 1f, false);
            }
            else if (ended)
            {
                upperAllowed = false;
                bool won = w.Match.Winner == (actor.Team == TeamId.Azure ? MatchResult.Azure : MatchResult.Crimson);
                bool lost = w.Match.Winner != MatchResult.Draw && w.Match.Winner != MatchResult.None && !won;
                if (won && m.Pose(ArtMapping.Cheer, _endT + (_id % 7) * 0.21, 1f, true)) { }
                else if (lost && m.HasClip(ArtMapping.SitDown))
                {
                    float sitLen = m.ClipLength(ArtMapping.SitDown);
                    if (_endT < sitLen || !m.HasClip(ArtMapping.SitIdle)) m.Pose(ArtMapping.SitDown, _endT, 1f, false);
                    else m.Pose(ArtMapping.SitIdle, _endT - sitLen, 1f, true);
                }
                else m.Pose(ArtMapping.Idle, _endT + _id * 0.37, 1f, true);
            }
            else if (actor.StandUp > 0 && m.HasClip(ArtMapping.GetUp))
            {
                // DG-90 / A10: lying to standing over the stand-up lock, from the configured clip window.
                upperAllowed = false;
                double len = m.ClipLength(ArtMapping.GetUp);
                double start = Math.Min(cfg.Render.StandUpClipStart, len - 0.05);
                double total = Math.Max(1e-3, cfg.Respawn.StandUpSeconds);
                double u = Math.Max(0, Math.Min(1, 1 - (actor.StandUp - alpha / hz) / total));
                m.Pose(ArtMapping.GetUp, start + u * (len - start), 1f, false);
            }
            else if (air && m.HasClip(ArtMapping.JumpAir))
            {
                double u = Jump.JumpPhase(actor, alpha);
                double ticksIn = Math.Max(0, u) * actor.AirLen;
                float startW = m.HasClip(ArtMapping.JumpStart) ? 1f - Locomotion.Smooth(TakeoffTicks * 0.5, TakeoffTicks * 1.25, ticksIn) : 0f;
                double sLen = m.ClipLength(ArtMapping.JumpStart);
                m.Pose(ArtMapping.JumpStart, sLen * 0.5 + sLen * 0.5 * Math.Min(1, ticksIn / TakeoffTicks), startW, false);
                m.Pose(ArtMapping.JumpAir, Math.Max(0, u) * m.ClipLength(ArtMapping.JumpAir), 1f - startW, true);
                if (ticksIn < TakeoffTicks) scale = Squash(scale, 1.08f); // take-off stretch
            }
            else
            {
                // px lane C: standing still, the hips and legs commit to the swing too (base layer); running keeps the legs.
                _legActW = 0f;
                if (_kind == 1 && _actClip != null && m.OnUpperLayer(_actClip) && SampleSwing(m, w, actor, i, alpha, simTime, combat, hasFx, fx, out double lt, out float lw, false))
                {
                    float still = Mathf.Clamp01(1f - Locomotion.MoveWeight(_speed) / 0.35f);
                    _legActW = lw * still * HitFeelTuning.LegCommit;
                    _legActT = lt;
                }
                idleW = PoseGround(m, actor, cfg, simTime, loopDt, air, ref scale);
            }

            // ---- upper body (DG-73, DG-74, DG-88, DG-71)
            float upperSum = 0f;
            if (_actClip == null || _kind != 1 || !upperAllowed) _whiffTwist = _whiffPitch = 0f;   // px lane C
            if (upperAllowed) upperSum = PoseUpper(m, w, actor, cfg, i, alpha, simTime, dSim, combat, hasFx, fx, ref twist);
            m.UpperWeight = Mathf.Clamp01(upperSum);

            // ---- rotation and scale
            if (!corpse)
            {
                double bank = Clamp(-_yawVel * _speed * BankGain, -BankMaxDeg, BankMaxDeg);
                double pitch = Clamp(_accel * PitchGain, -PitchMaxDeg, PitchMaxDeg);
                double kk = dMotion > 0 ? 1 - Math.Exp(-dMotion / 0.08) : 0;
                _roll += (float)((bank - _roll) * kk);
                _pitch += (float)((pitch - _pitch) * kk);
            }
            else { _roll = 0f; _pitch = 0f; }
            Quaternion rot = Quaternion.Euler(0f, (float)_yaw + twist + _whiffTwist, 0f) * Quaternion.Euler(_pitch + _whiffPitch, 0f, _roll);
            if (hasFx) rot = fx.Lean * rot;
            m.SetFacing(rot);
            // fix/hit-feel-5: a brief squash along the blow (80 ms, at most 7%), a little lower and wider across it; the
            // model scale owner below still composes and clamps it.
            if (hasFx && !corpse && feed != null && i < feed.HitDirX.Length)
            {
                float sq = HitPose.Squash(fx.HitT);
                if (sq > 0f)
                {
                    Vector3 hd = Quaternion.Inverse(rot) * SimSpace.ToWorld(feed.HitDirX[i], 0, feed.HitDirZ[i]);
                    float ax = Mathf.Abs(hd.x), az = Mathf.Abs(hd.z), n = Mathf.Max(1e-4f, ax + az);
                    ax /= n; az /= n;
                    scale = Vector3.Scale(scale, new Vector3(1f + sq * (0.5f - 1.5f * ax), 1f - sq * 0.4f, 1f + sq * (0.5f - 1.5f * az)));
                }
            }
            // fix/model-scale: scale above is the effect factor, rebuilt from 1 this frame; the one owner composes and clamps it.
            m.Transform.localScale = ModelScale.Compose(m.BaseScale, scale, m.AssignedName);

            bool fresh = _fresh > 0;
            if (_fresh > 0) _fresh--;
            bool evaluate = AnimLod.ShouldEvaluate(m.AlwaysAnimate, fresh, m.AnyVisible, basePos, _id);
            bool evaluated = m.Evaluate(evaluate);
            if (evaluated) ApplyLegWarp(m);
            if (evaluated && !corpse && !air) ApplyClimb(m, w, i, simTime);
            {
                // Breathing: a 2% chest swell at 0.3 Hz while idle. fix/model-scale: set from the chest's rest scale, never
                // multiplied in place (a clip without a chest scale curve, or a frozen simTime in a hit-stop, compounded it).
                // Finisher (batch 3, AnInterruptedEffectNeverSurvivesTheNextFrame: chest 2.7x): written EVERY frame, idle or not,
                // evaluated or skipped, so this is the chest's one final writer; the KayKit clips carry no chest scale curve, so a
                // moving fighter used to keep whatever an interrupted effect left there.
                Transform chest = m.Bone("chest");
                float breath = evaluated && idleW > 0.01f ? 1f + 0.02f * idleW * Mathf.Sin((float)(2 * Math.PI * 0.3 * simTime + _id * 1.7)) : 1f;
                if (chest != null) chest.localScale = ModelScale.FromRest(m.RestScale(chest), breath);
            }
            if (!corpse && !air && actor.StandUp <= 0) EmitSteps(m, w, actor, i, basePos, evaluated);

            state.Actor = i;
            state.Tick = w.Tick;
            state.Alpha = alpha;
            state.SimTime = simTime;
            state.DeltaSim = dSim;
            state.Team = actor.Team;
            state.Cls = actor.Cls;
            state.IsKaiju = false;
            state.Evaluated = evaluated;
            state.Corpse = corpse;
            state.Airborne = air;
            state.Velocity = Velocity;
            state.Speed = (float)_speed;
            state.Yaw = (float)_yaw;
            state.YawRate = (float)_yawVel;
            state.Carry = actor.Carry;
            state.CarryAmount = actor.CarryAmount;
            state.HitAge = hasFx ? fx.HitT : 1e9;
            state.HitTier = _hasReact ? _react.Tier : HitTier.Light;
            state.HitDir = feed != null && i < feed.HitDirX.Length ? SimSpace.ToWorld(feed.HitDirX[i], 0, feed.HitDirZ[i]) : Vector3.zero;
            state.ActionKind = _kind;
            state.StrikeLeanDeg = 0f;
            state.StrikeDir = Vector3.zero;
            if (!corpse && _kind == 1 && _actClip != null && HitFeel.StrikeLean(w, i, alpha, out double leanDeg, out double ldx, out double ldz))
            {
                state.StrikeLeanDeg = (float)leanDeg;
                state.StrikeDir = SimSpace.ToWorld(ldx, 0, ldz);
            }
            state.ActionAge = _actClip != null ? simTime - _actStart : 1e9;
            return evaluated;
        }

        double _actStart;

        // ------------------------------------------------------------------ ground: locomotion, heavy hits, gather, idle

        float _heavyUpper;   // fix/hit-feel-4: the moving share of a whole-body reaction, drawn on the upper body

        float PoseGround(ModelView m, Actor actor, GameConfig cfg, double simTime, double loopDt, bool air, ref Vector3 scale)
        {
            double speed = _speed;
            float move = Locomotion.MoveWeight(speed);
            float run = Locomotion.RunWeight(speed);
            if (speed > 0.2)
                _leftDeg = Locomotion.LeftAngleDeg(Math.Sin(_yaw * Math.PI / 180), Math.Cos(_yaw * Math.PI / 180), _vx, _vz);
            double warp = Locomotion.DirectionWeights(_leftDeg, _w6, ref _side);
            // The warp turns a real strafe; a model without the strafes plays the band's forward clip (AddEntry) unturned.
            if (_side >= 0 && !m.HasClip(Locomotion.RunBand[_side].Clip)) warp = 0;

            _eCount = 0;
            for (int k = 0; k < Locomotion.DirCount; k++)
            {
                if (_w6[k] <= 0f) continue;
                if (run < 1f) AddEntry(m, Locomotion.WalkBand[k], _w6[k] * (1f - run), k, false);
                if (run > 0f) AddEntry(m, Locomotion.RunBand[k], _w6[k] * run, k, true);
            }
            double stride = 0, len = 0, wsum = 0;
            // b15 world look: a class on its own measured clips uses their real ground speed (ClassStrides); the Knight
            // facts times StrideScale stay the fallback for any model without a measured row.
            string own = ClassStrides.SourceOf(m.Spec != null ? m.Spec.ClipSource : null);
            bool measured = ClassStrides.UseInAnimator && ClassStrides.Covers(own);
            for (int e = 0; e < _eCount; e++)
            {
                double l = m.ClipLength(_eClip[e]);
                stride += _eW[e] * StrideSpeed(own, measured, _eClip[e]) * l;
                len += _eW[e] * l;
                wsum += _eW[e];
            }
            if (wsum > 0) { stride /= wsum; len /= wsum; }
            if (!measured && m.Spec != null && m.Spec.StrideScale > 0f) stride *= m.Spec.StrideScale;   // per-class legs (HY swaps)
            _warp = warp * move;
            double legs = 0;
            for (int e = 0; e < _eCount && wsum > 0; e++)
            {
                ClipFact f = ClipFacts.Get(_eClip[e]);
                if (f != null) legs += _eW[e] / wsum * Locomotion.LegYawDeg(f.moveDeg);
            }
            _legYaw = (float)((legs + warp) * move);
            _prevPhase = _phase;
            if (!air && move > 0.01f && loopDt > 0) _phase = Locomotion.Advance(_phase, speed * loopDt, stride, len, loopDt);

            // Heavy-hit stagger on the full body (light and blocked hits are on the upper layer).
            float heavy = 0f;
            double hitT = 0;
            _heavyUpper = 0f;
            if (_hasReact && _react.Layer == ReactionLayer.FullBody && m.HasClip(_react.Clip))
            {
                hitT = _hitAgeForPose;
                heavy = _react.Weight * HitReactions.Decay(hitT, m.ClipLength(_react.Clip));
                // fix/hit-feel-4 (FacingSanityPlayTests, Healer: HitHeavy at 0.43 twisted a running fighter's hips 21
                // degrees and the toes 166 off): a whole-body reaction fades off the legs as the fighter moves; PoseUpper
                // plays the moving share on the arms and chest instead (_heavyUpper).
                _heavyUpper = heavy * move;
                heavy *= 1f - move;
                if (hitT < 0.067) scale = new Vector3(scale.x * 1.05f, scale.y, scale.z * 1.05f);
            }

            // Gather (per class clip and rate), blended in and out over GatherFade.
            ArtMapping.ClassClipSet set = ArtMapping.ClassClips(actor.Cls);
            string gatherClip = m.HasClip(set.Gather) ? set.Gather : ArtMapping.Gather;
            float gatherRate = m.HasClip(set.Gather) ? set.GatherRate : 1f;
            bool gathering = actor.GatherProgress > 0 && speed < 0.3;
            _gather = Approach(_gather, gathering ? 1f : 0f, loopDt, GatherFade);
            if (_gather > 0f) _gatherT += loopDt * gatherRate;
            else _gatherT = 0;

            // Landing.
            double since = simTime - _landAt;
            float land = 0f;
            if (since >= 0 && since < LandSeconds && m.HasClip(ArtMapping.JumpLand))
            {
                land = 1f - Locomotion.Smooth(LandSeconds * 0.5, LandSeconds, since);
                double y = since < 0.06 ? 1 - 0.15 * since / 0.06 : 0.85 + 0.15 * Math.Min(1, (since - 0.06) / 0.24);
                scale = Squash(scale, (float)y);
            }

            // Idle breaks (DG-107): after 4-8 s of standing, one look-around or stretch; deterministic by actor and count.
            bool idle = move < 0.05f && _gather < 0.05f && _actClip == null && actor.Carry == CarryKind.None && !air;
            float brk = 0f;
            if (idle)
            {
                _idleT += loopDt;
                if (_nextBreak < 0) _nextBreak = IdleBreakMin + IdleBreakSpread * Hash01(_id, 0);
                if (_breakAt < 0 && _idleT >= _nextBreak)
                {
                    _breakClip = (Hash(_id, _breaks + 1) & 1) == 0 ? ArtMapping.IdleLook : ArtMapping.UseItem;
                    if (!m.HasClip(_breakClip)) _breakClip = m.HasClip(ArtMapping.IdleLook) ? ArtMapping.IdleLook : null;
                    _breakAt = _breakClip != null ? _idleT : -1;
                    if (_breakClip == null) _nextBreak = _idleT + IdleBreakMin;
                }
                if (_breakAt >= 0)
                {
                    double bt = _idleT - _breakAt, bl = m.ClipLength(_breakClip);
                    if (bt >= bl) { _breaks++; _breakAt = -1; _nextBreak = _idleT + IdleBreakMin + IdleBreakSpread * Hash01(_id, _breaks + 1); }
                    else
                    {
                        brk = (float)Math.Min(1, Math.Min(bt / BreakFade, (bl - bt) / BreakFade));
                        m.Pose(_breakClip, bt, brk, false);
                    }
                }
            }
            else { _idleT = 0; _breakAt = -1; _nextBreak = -1; }

            float rest = (1f - heavy) * (1f - _gather) * (1f - land);
            float idleW = (1f - move) * (1f - brk) * rest;
            m.Pose(ArtMapping.Idle, simTime + _id * 0.37, idleW, true);
            float refPlant = Plant(own, ArtMapping.Run);
            for (int e = 0; e < _eCount; e++)
            {
                string c = _eClip[e];
                double t = Locomotion.SampleTime(_phase, m.ClipLength(c), Plant(own, c), refPlant, _eRev[e]);
                m.Pose(c, t, _eW[e] / (float)Math.Max(1e-6, wsum) * move * rest, true);
            }
            if (_gather > 0f) m.Pose(gatherClip, _gatherT, _gather * (1f - heavy) * (1f - land), true);
            if (heavy > 0f) m.Pose(_react.Clip, hitT, heavy, false);
            if (land > 0f) m.Pose(ArtMapping.JumpLand, 0.1 + since * 1.8, land * (1f - heavy), false);
            if (_legActW > 0f && _actClip != null) m.Pose(_actClip, _legActT, _legActW * (1f - heavy), false);   // px lane C: legs commit
            return idleW;
        }

        double _prevPhase;
        double _hitAgeForPose;

        /// <summary>b15: planted-toe ground speed of a clip for this class (u/s): its measured row, else the Knight's.</summary>
        public static double StrideSpeed(string source, bool measured, string clip) =>
            measured && ClassStrides.TryGet(source, clip, out ClassStrides.Row r) ? r.GroundSpeed : ClipFacts.GroundSpeed(clip, IsRunClip(clip) ? 4.57f : 0.86f);

        /// <summary>Left mid-stance phase of a clip (the sync marker blends line up on). b15: the Knight's markers stay in
        /// force for every class: the class clips are baked from the Knight's timing, and their stance-centre measure
        /// (ClassStrides.LeftPlant, 0.19 to 0.21 on the runs) is noisier than one shared marker (first CI run: per-class
        /// markers made the backward-diagonal blend slide 0.39 u/u). Only the ground speed comes from the class.</summary>
        public static float Plant(string source, string clip) => ClipFacts.LeftPlant(clip, IsRunClip(clip) ? 0.203f : 0.281f);

        static bool IsRunClip(string c) => c == ArtMapping.Run || c == ArtMapping.StrafeL || c == ArtMapping.StrafeR;

        /// <summary>Adds a locomotion clip, resolving clips this model lacks (the 8-clip set) and merging duplicates.</summary>
        void AddEntry(ModelView m, Locomotion.Dir d, float w, int k, bool runBand)
        {
            if (w <= 0f) return;
            string clip = d.Clip;
            bool rev = d.Reversed;
            if (!m.HasClip(clip))
            {
                // Without strafes or the backpedal: the band's forward clip, reversed for the back half (still no moonwalk
                // straight back; a sideways run then plays forward or back at the nearer side).
                string fwd = runBand ? ArtMapping.Run : ArtMapping.Walk;
                if (!m.HasClip(fwd)) fwd = m.HasClip(ArtMapping.Run) ? ArtMapping.Run : ArtMapping.Walk;
                if (!m.HasClip(fwd)) return;
                clip = fwd;
                rev = k >= 2 && k <= 4;
            }
            for (int e = 0; e < _eCount; e++)
                if (_eClip[e] == clip)
                {
                    // One playable plays one time: keep the direction with more weight.
                    if (_eRev[e] != rev && w > _eW[e]) _eRev[e] = rev;
                    _eW[e] += w;
                    return;
                }
            if (_eCount >= _eClip.Length) return;
            _eClip[_eCount] = clip;
            _eRev[_eCount] = rev;
            _eW[_eCount] = w;
            _eCount++;
        }

        // ------------------------------------------------------------------ upper body

        /// <summary>
        /// fix/hit-feel-4: the share of an action clip kept this frame: 1 on the upper layer; for a clip that would play on
        /// the full body, 1 - the locomotion move weight, so an action never turns or freezes a moving fighter's legs.
        /// </summary>
        float FullBodyActionKeep(ModelView m, string clip) =>
            clip == null || m.OnUpperLayer(clip) ? 1f : 1f - Locomotion.MoveWeight(_speed);

        float PoseUpper(ModelView m, World w, Actor actor, GameConfig cfg, int i, double alpha, double simTime, double dSim,
            CombatFx combat, bool hasFx, in ActorPoseFx fx, ref float twist)
        {
            float sum = 0f;
            float actW = 0f;
            double hz = cfg.SimHz;
            if (_actClip != null)
            {
                double len = m.ClipLength(_actClip);
                double t;
                double windup = 0;
                if (_kind == 1)
                {
                    SampleSwing(m, w, actor, i, alpha, simTime, combat, hasFx, fx, out t, out actW, true);
                }
                else
                {
                    _actAge = actor.AnimAction + alpha / hz;
                    if (_actClip == ArtMapping.Throw)
                    {
                        // fix round 7: the throw is over 8 frames after it leaves the hand (HitFeelTuning.ThrowPose)
                        HitFeelTuning.ThrowPose(_actAge, len, out t, out actW);
                    }
                    else
                    {
                        t = Math.Min(len, _actAge);
                        actW = AttackTiming.Envelope(_actAge, t, len, windup);
                    }
                }
                if (_kind == 1 && _commitPending && Time.frameCount - _swingStartFrame >= 2)
                {
                    _commitPending = false;
                    _commitReady = true;
                    _commitW = actW;
                }
                if (_kind == 2 && actor.Cls == ClassId.Warrior) actW *= 1f - _holdW;
                if ((actW <= 0f && t >= len - 1e-4) || (_kind == 1 && _feinting && actW <= 0f)) { _actClip = null; _feinting = false; }
                else
                {
                    // fix/hit-feel-4 (FacingSanityPlayTests, Demolitions): an action clip that is not on the upper layer
                    // (the mine-plant PickUp crouch) goes to the FULL body, at 3x weight, so on a moving fighter it took the
                    // legs off the stride: toes 93 degrees off the leg yaw. A moving fighter keeps the legs: the clip fades
                    // out with the move weight (full strength standing still, gone at a run).
                    actW *= FullBodyActionKeep(m, _actClip);
                    m.PoseUpper(_actClip, t, actW, false, _slot);
                    _lastActTime = t;
                    _lastActW = actW;
                    sum += actW;
                }
            }
            // The action before a re-trigger fades out from the pose it was in (no pop).
            double pf = simTime - _prevAt;
            if (_prevClip != null && pf >= 0 && pf < AttackTiming.Retrigger)
            {
                float f = _prevW * (float)(1 - pf / AttackTiming.Retrigger) * FullBodyActionKeep(m, _prevClip);
                m.PoseUpper(_prevClip, _prevLoop ? _prevTime + pf : Math.Min(_prevTime + pf, m.ClipLength(_prevClip)), f, _prevLoop, 1 - _slot);
                sum += f;
                actW = Math.Max(actW, f);
            }
            else _prevClip = null;

            // Warrior shield hold: Blocking while the shield is up.
            ArtMapping.ClassClipSet set = ArtMapping.ClassClips(actor.Cls);
            bool shield = set.AbilityHold != null && actor.AbilityActive > 0 && m.HasClip(set.AbilityHold);
            double sinceAbility = actor.AnimAction;
            _holdW = Approach(_holdW, shield && (_kind != 2 || sinceAbility > 0.2) ? 1f : 0f, dSim, 0.12);
            if (_holdW > 0f && set.AbilityHold != null)
            {
                m.PoseUpper(set.AbilityHold, simTime, _holdW, true, 0);
                sum += _holdW;
                actW = Math.Max(actW, _holdW);
            }

            // fix/kaiju-alive-1 (nothing-glides audit): a dig or a place had no pose at all (the block just appeared or vanished
            // while the Miner stood in Idle). The carried-block count changing is the sim's dig (+) or place (-): a chop for a
            // dig, Use_Item for a place, on the upper layer so the legs keep their state.
            int carried = w.Terraform != null && w.Terraform.Carried != null && i < w.Terraform.Carried.Length ? w.Terraform.Carried[i] : 0;
            if (_lastCarried >= 0 && carried != _lastCarried && actor.Alive)
            {
                _terraClip = carried > _lastCarried ? (m.HasClip(ArtMapping.Attack2HChop) ? ArtMapping.Attack2HChop : ArtMapping.UseItem) : ArtMapping.UseItem;
                if (!m.HasClip(_terraClip)) _terraClip = null;
                _terraAt = simTime;
            }
            _lastCarried = carried;
            if (_terraClip != null)
            {
                double tt = simTime - _terraAt, tl = m.ClipLength(_terraClip);
                if (tt >= tl || tt < 0) _terraClip = null;
                else
                {
                    float tw = (float)Math.Min(1, Math.Min(tt / 0.08, (tl - tt) / 0.2)) * (1f - Mathf.Clamp01(actW));
                    if (tw > 0f && m.PoseUpper(_terraClip, tt, tw, false, 0)) { sum += tw; actW = Math.Max(actW, tw); }
                }
            }

            // Carry pose (arms in) under everything else.
            bool carrying = actor.Carry != CarryKind.None && actor.CarryAmount > 0 && m.HasClip(ArtMapping.Carry);
            _carry = Approach(_carry, carrying ? 1f : 0f, dSim, CarryFade);
            if (_carry > 0f)
            {
                float cw = _carry * (1f - Mathf.Clamp01(actW));
                if (m.PoseUpper(ArtMapping.Carry, simTime, cw, true, 0)) sum += cw;
            }

            // Light and blocked hits flinch the arms and chest; heavy ones are on the full body (PoseGround).
            if (_hasReact && hasFx)
            {
                double ht = _hitAgeForPose;
                string clip = _react.Clip;
                if (_react.Layer == ReactionLayer.FullBody && !m.HasClip(clip)) clip = ArtMapping.Hit;
                if (!m.HasClip(clip)) clip = ArtMapping.Hit;
                float decay = HitReactions.Decay(ht, m.ClipLength(clip));
                if (decay <= 0f) _hasReact = fx.HitT < 0.05; // keep until the hit is visible at all
                else
                {
                    twist = _react.TwistDeg * decay;
                    if (_react.Layer == ReactionLayer.Upper || !m.HasClip(_react.Clip))
                    {
                        float hw = _react.Weight * decay * (1f - 0.5f * Mathf.Clamp01(actW));
                        if (m.PoseUpper(clip, ht, hw, false, 0)) sum += hw;
                    }
                    else if (_heavyUpper > 0f)
                    {
                        // fix/hit-feel-4: a moving fighter's whole-body reaction, on the arms and chest only (never the
                        // full-body fallback, which would put it back on the legs).
                        string uc = m.OnUpperLayer(_react.Clip) ? _react.Clip : m.OnUpperLayer(ArtMapping.Hit) ? ArtMapping.Hit : null;
                        float hw = _heavyUpper * (1f - 0.5f * Mathf.Clamp01(actW));
                        if (uc != null && m.PoseUpper(uc, ht, hw, false, 0)) sum += hw;
                    }
                }
            }
            return sum;
        }

        /// <summary>
        /// px lane C: the attack clip's time and weight now: the committed envelope (HitFeelTuning.CommitWeight, local
        /// faster), the craft wind-up (anticipation, hold, a 4-6 frame strike ending on the sim's contact tick read LIVE from
        /// SwingTick + SwingWindup), the follow-through held through the hit-stop on a hit (CombatFx.AttackTime) and played
        /// faster with an over-rotation on a whiff (AttackContact with nothing hit), and a feint that rewinds and fades the
        /// swing out (SimEventType.Feint). commit = true updates the per-swing state (whiff twist, feint latch).
        /// </summary>
        bool SampleSwing(ModelView m, World w, Actor actor, int i, double alpha, double simTime, CombatFx combat, bool hasFx, in ActorPoseFx fx,
            out double t, out float actW, bool commit)
        {
            t = 0; actW = 0f;
            if (_actClip == null) return false;
            double hz = w.Config.SimHz;
            double len = m.ClipLength(_actClip);
            double windup = AttackTiming.SwingWindupSeconds(actor, hz);   // the sim's own swing: contact on SwingTick + SwingWindup
            if (_predicting)
            {
                // fix round 5 (item 4): before the sim's tick: the wind-up from the press, held short of the strike
                double cfgWindup = (double)w.Dur.AttackWindup[(int)actor.Cls] / hz;
                if (actor.SwingTick == _predSwingTick && HitFeelTuning.PredictedSwing(Time.unscaledTimeAsDouble - _predWall, cfgWindup, out double pAge, out float pW))
                {
                    t = AttackTiming.ClipTime(pAge, ViewContact(_actClip, ClipFacts.Contact(_actClip, (float)(len * 0.5))), len, cfgWindup, _actExtreme);
                    actW = _predRefused ? Math.Min(pW, HitFeelTuning.RefusedPressWeight) : pW;
                    return true;
                }
                if (actor.SwingTick == _predSwingTick && commit)
                {
                    // the sim refused (cooldown, stun, TNT): fade it out like a feint
                    _predicting = false;
                    LastPredictionEnded = true;
                    _feinting = true;
                    _feintAt = simTime;
                    _feintFromT = _lastActTime;
                    _feintFromW = Math.Max(_lastActW, 0.01f);
                }
                else if (actor.SwingTick != _predSwingTick) _predicting = false;
            }
            double age = AttackTiming.SwingAge(actor, w.Tick, alpha, hz);
            double rawAge = age;
            if (combat != null && hasFx) age = combat.AttackTime(i, age, fx); // the attacker freezes on contact during the hold
            CombatFeed feed = combat != null ? combat.Feed : null;
            bool whiff = false;
            if (feed != null && i < feed.ContactTick.Length && feed.ContactTick[i] >= actor.SwingTick && actor.SwingTick > 0 && feed.ContactHits[i] == 0)
                whiff = true;
            double contactAge = age;
            if (whiff && age > windup) contactAge = windup + (age - windup) * HitFeelTuning.WhiffRate;   // swing through
            t = AttackTiming.ClipTime(contactAge, ViewContact(_actClip, ClipFacts.Contact(_actClip, (float)(len * 0.5))), len, windup, _actExtreme);
            actW = AttackTiming.Envelope(age, t, len, windup, i == LocalSeat.Actor);
            if (_predFloor > 0f)
            {
                // fix round 5 (item 4): after a predicted start the pose never drops below what the press frame drew, and
                // the clip never steps back before the predicted wind-up time, until the sim's own envelope passes them
                // fix round 7: the floor keeps rising on the press's clock (HitFeelTuning.RisingFloor), so the hand-over does
                // not hold the press frame's 0.56 while the sim's envelope restarts its own rise (pass 7: 0.56 at press + 2)
                float floor = double.IsNaN(_predFloorWall) ? _predFloor : Math.Max(_predFloor, HitFeelTuning.RisingFloor(Time.unscaledTimeAsDouble - _predFloorWall));
                if (actW >= floor && t >= _predFloorT) { _predFloor = 0f; _predFloorWall = double.NaN; }
                else { actW = Math.Max(actW, floor); t = Math.Max(t, _predFloorT); }
            }
            // feint: the sim dropped the wind-up (b16 CANCEL Value 1): rewind to the start and fade out, visibly aborted
            if (feed != null && i < feed.FeintTick.Length && feed.FeintTick[i] >= actor.SwingTick && actor.SwingTick > 0 && feed.FeintTick[i] != _feintSeen)
            {
                if (commit)
                {
                    _feintSeen = feed.FeintTick[i];
                    _feinting = true;
                    _feintAt = simTime;
                    _feintFromT = _lastActTime;
                    _feintFromW = Math.Max(_lastActW, 0.01f);
                }
            }
            if (_feinting)
            {
                double u = Math.Min(1, Math.Max(0, (simTime - _feintAt) / HitFeelTuning.FeintAbortSeconds));
                t = _feintFromT * (1 - u);
                actW = (float)(_feintFromW * (1 - u));
            }
            if (commit)
            {
                _actAge = age;
                double since = rawAge - windup;
                double ws = whiff && !_feinting ? HitFeelTuning.WhiffShape(since) : 0;
                float sign = actor.Cls == ClassId.Farmer ? -1f : 1f;
                _whiffTwist = (float)(sign * HitFeelTuning.WhiffOverRotateDeg * ws);
                _whiffPitch = (float)(HitFeelTuning.WhiffLeanDeg * ws);
            }
            return true;
        }

        double _actAge;
        // fix/hit-feel-5: the weapon tip (hand-local) and whether this fighter's contact reach has been measured.
        bool _reachMeasured;
        Vector3 _tipLocal;
        Transform _tipHand;
        ModelView _tipModel;

        // fix/hit-feel-7: the view contact (clip seconds the damage tick shows) per model and clip.
        string _vcClip;
        double _vcTime = -1, _vcFact = -1;
        string _endName = "a";

        /// <summary>
        /// fix/hit-feel-5/7: once per model and clip, before this frame's pose. Picks the weapon's STRIKING end (of the
        /// two far ends, the one further forward along the fighter's facing at the clip's contact pose: a two-hand tool's
        /// head, not its handle), then finds the clip time in [contact - 0.15, contact + 0.35] s where that end is furthest
        /// forward: the view contact, which the wind-up craft lands on the damage tick (the sim's hit time is unchanged).
        /// Its forward reach and its height above the spine feed HitFeel (step-in, strike lean).
        /// </summary>
        void MeasureReach(ModelView m, Actor actor, string clip)
        {
            _reachMeasured = true;
            _tipModel = m;
            float len = m.ClipLength(clip);
            _vcClip = clip;
            _vcFact = ClipFacts.Contact(clip, len * 0.5f);
            _vcTime = _vcFact;
            Vector3 endA = HitPose.Ends(m, out _tipHand, out Vector3 endB, out _);
            _tipLocal = endA;
            _endName = "a";
            Transform spine = m.Bone("spine");
            if (_tipHand == null || spine == null || len <= 0f) return;
            Vector3 fwd = SimSpace.ToWorld(Math.Cos(actor.Facing), 0, Math.Sin(actor.Facing));
            fwd.y = 0f;
            fwd.Normalize();
            if (endB.sqrMagnitude > 1e-6f && PoseAt(m, clip, _vcFact))
            {
                Vector3 root = m.Transform.position;
                if (Vector3.Dot(_tipHand.TransformPoint(endB) - root, fwd) > Vector3.Dot(_tipHand.TransformPoint(endA) - root, fwd))
                { _tipLocal = endB; _endName = "b (other end)"; }
            }
            double lo = Math.Max(0, _vcFact - 0.15), hi = Math.Max(_vcFact, Math.Min(len - AttackTiming.FadeOut - 0.03, _vcFact + 0.35)), best = double.NegativeInfinity;
            const int n = 20;
            for (int k = 0; k <= n; k++)
            {
                double t = lo + (hi - lo) * k / n;
                if (!PoseAt(m, clip, t)) return;
                double f = Vector3.Dot(_tipHand.TransformPoint(_tipLocal) - m.Transform.position, fwd);
                if (f > best + 1e-4) { best = f; _vcTime = t; }
            }
            if (!PoseAt(m, clip, _vcTime)) return;
            Vector3 tip = _tipHand.TransformPoint(_tipLocal);
            HitFeel.SetMeasured(actor.Cls, Vector3.Dot(tip - m.Transform.position, fwd), tip.y - spine.position.y);
        }

        static bool PoseAt(ModelView m, string clip, double t)
        {
            m.BeginPose();
            m.Pose(clip, t, 1f, false);
            return m.Evaluate(true);
        }

        /// <summary>The clip time this fighter's damage tick shows (the view contact; the fact table's until measured).</summary>
        public double ViewContact(string clip, double fact) => clip != null && clip == _vcClip && _vcTime >= 0 ? _vcTime : fact;
        public double FactContact => _vcFact;
        public double ViewContactNow => _vcTime;
        public string StrikingEnd => _endName;
        public Vector3 TipLocalNow => _tipLocal;
        public Transform TipHand => _tipHand;

        /// <summary>The weapon tip in world space now (capture; Vector3.zero before the first swing).</summary>
        public Vector3 TipWorld => _tipHand != null ? _tipHand.TransformPoint(_tipLocal) : Vector3.zero;
        public bool HasTip => _tipHand != null;
        double _actExtreme = -1;   // fix/hit-feel-3: the attack clip's measured wind-up extreme
        ModelView _exModel;
        string _exClip;
        double _exTime = -1;
        int _lastCarried = -1;
        string _terraClip;
        double _terraAt;

        void BeginAction(ModelView m, Actor actor, GameConfig cfg, double simTime, string cue = null, int tick = int.MinValue)
        {
            _feinting = false;
            _whiffTwist = _whiffPitch = 0f;
            ClassConfig cc = cfg.Class(actor.Cls);
            double aa = actor.AnimAction;
            // feat/light-weapons-1: a light-weapon swing sets its own cooldown; the Healer's lantern swing (not her mend) is
            // the press whose swing started on this action's tick, and plays the shared one-hand Attack clip instead of Cast.
            LightWeapon lw = LightWeapons.On(cfg) ? LightWeapons.Row(cfg, actor.Cls) : null;
            // b15 combat feel: the light weapons are off under it; the Healer's lantern and the pry bar are her class row's swing.
            double lightCdSec = lw != null ? lw.CooldownSeconds
                : CombatFeel.On(cfg) && (actor.Cls == ClassId.Healer || actor.Cls == ClassId.Demolitions) ? cc.AttackCooldown : -1;
            bool lightCd = lightCdSec > 0 && actor.AttackCd > 0 && Math.Abs(actor.AttackCd + aa - lightCdSec) < 1e-3;
            bool attack = actor.AttackCd > 0 && Math.Abs(actor.AttackCd + aa - cc.AttackCooldown) < 1e-3 || lightCd;
            bool lantern = lightCd && actor.Cls == ClassId.Healer && tick != int.MinValue
                && Math.Abs((tick - actor.SwingTick) / cfg.SimHz - aa) < 1.5 / cfg.SimHz;
            bool ability = actor.AbilityCd > 0 && Math.Abs(actor.AbilityCd + aa - cc.AbilityCooldown) < 1e-3;
            byte kind = attack ? (byte)1 : ability ? (byte)2 : actor.AttackCd > 0 ? (byte)1 : (byte)2;
            string clip = kind == 1 ? AttackTiming.AttackClip(m, actor.Cls) : AttackTiming.AbilityClip(m, actor.Cls);
            if (lantern && kind == 1 && m.HasClip(ArtMapping.Attack)) clip = ArtMapping.Attack;
            if (clip != null && !m.HasClip(clip)) clip = null;
            // a skill cue (not a melee press this tick) plays its own clip on the ability timing
            if (cue != null && !attack && m.HasClip(cue)) { clip = cue; kind = 2; }
            // fix round 5 (item 4): the sim took the predicted press: continue the same clip, no crossfade, the pose never dips
            bool fromPredict = _predicting && !_predRefused && kind == 1 && clip != null && clip == _actClip;
            if (_predicting && !fromPredict) LastPredictionEnded = true;
            _predicting = false;
            if (fromPredict) { _predFloor = Math.Max(_lastActW, 0.01f); _predFloorT = _lastActTime; _predFloorWall = _predWall; }
            else { _predFloor = 0f; _predFloorWall = double.NaN; }
            if (_actClip != null && _lastActW > 0f && !fromPredict)
            {
                _prevClip = _actClip;
                _prevTime = _lastActTime;
                _prevW = _lastActW;
                _prevLoop = false;
                _prevAt = simTime;
                _slot = 1 - _slot;
            }
            _kind = kind;
            _actClip = clip;
            // fix/hit-feel-3: measured once per clip, here, before this frame's pose is built.
            // fix/hit-feel-7: the view contact first (per model and clip), then the wind-up extreme before it (per model too).
            if (kind == 1 && clip != null && (!_reachMeasured || _tipModel != m || _vcClip != clip)) MeasureReach(m, actor, clip);
            if (kind == 1 && clip != null && (_exModel != m || _exClip != clip))
            {
                _exModel = m;
                _exClip = clip;
                _exTime = AttackTiming.ExtremeNow(m, clip, ViewContact(clip, ClipFacts.Contact(clip, m.ClipLength(clip) * 0.5f)));
            }
            _actExtreme = kind == 1 && clip != null ? _exTime : -1;
            if (kind == 1 && clip != null) { _swingStartFrame = Time.frameCount; _commitPending = true; _commitReady = false; }
            _actStart = simTime - aa;
            if (fromPredict) { _commitPending = false; _commitReady = false; }   // the press frame already committed
            _lastActW = fromPredict ? _predFloor : 0f;
            _breakAt = -1;
            _idleT = 0;
        }

        void LatchHit(Actor actor, GameConfig cfg, CombatFeed feed, int i)
        {
            Vector3 d = SimSpace.ToWorld(feed.HitDirX[i], 0, feed.HitDirZ[i]);
            double yr = _yaw * Math.PI / 180;
            HitSide side = HitReactions.Side(Math.Sin(yr), Math.Cos(yr), d.x, d.z);
            bool blocked = actor.Cls == ClassId.Warrior && actor.AbilityActive > 0 && side == HitSide.Front;
            _react = HitReactions.Pick(feed.LastHitDmg[i], !actor.Alive, blocked, side);
            // px lane C: the stagger pose at full weight when the local player is the attacker or the victim (own-hit emphasis).
            bool own = i == LocalSeat.Actor || (i < feed.LastHitBy.Length && feed.LastHitBy[i] == LocalSeat.Actor);
            if (own && !blocked && _react.Weight < HitFeelTuning.ReactionWeightOwn)
                _react = new HitReaction(_react.Clip, _react.Layer, HitFeelTuning.ReactionWeightOwn, _react.Tier, _react.Side, _react.TwistDeg);
            // lead's call 12: a critical hit on/by the local player is the full-body Hit_B at full weight, twisted harder.
            if (actor.Alive && !blocked && i < feed.CritTick.Length && feed.CritTick[i] == feed.LastHitTick[i])
                _react = new HitReaction(ArtMapping.HitHeavy, ReactionLayer.FullBody, 1f, HitTier.Critical, side, _react.TwistDeg * 1.4f);
            _hasReact = true;
        }

        void LatchDeath(ModelView m, Actor actor, CombatFeed feed, int i, double target)
        {
            _wasCorpse = true;
            int tick = feed != null && i < feed.DeathTick.Length ? feed.DeathTick[i] : 0;
            _deathClip = m.HasClip(ArtMapping.DeathB) && (Hash(_id, tick) & 1) == 1 ? ArtMapping.DeathB : ArtMapping.Death;
            _corpseYaw = (float)(_hasYaw ? _yaw : target);
            if (feed != null && i < feed.HitDirX.Length)
            {
                Vector3 d = SimSpace.ToWorld(feed.HitDirX[i], 0, feed.HitDirZ[i]);
                if (d.sqrMagnitude > 1e-6f)
                {
                    // Death_A falls backwards, Death_B forwards (measured): either way the body falls away from the killer.
                    Vector3 face = _deathClip == ArtMapping.DeathB ? d : -d;
                    _corpseYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                }
            }
            _actClip = _prevClip = null;
            _hasReact = false;
        }

        double _snappedAt = double.NaN;

        /// <summary>b15: true once per swing, on the first frame at or past the contact (age reaches the wind-up). Pure.</summary>
        public static bool ContactSnap(double age, double windup, double dt, ref double snappedFor, double swingStart)
        {
            if (snappedFor == swingStart) return false;
            if (age + 1e-9 < windup || age > windup + Math.Max(dt, 1.0 / 30.0)) return false;
            snappedFor = swingStart;
            return true;
        }

        void StepYaw(double target, double dt)
        {
            if (!_hasYaw) { _yaw = target; _yawVel = 0; _hasYaw = true; return; }
            StepFacing(ref _yaw, ref _yawVel, target, dt);
        }

        /// <summary>
        /// The facing spring (DG-89) over dt sim seconds: the exact critically damped step toward target (degrees), its
        /// travel capped at MaxYawRate. Pure, so EditMode tests can run it on any clock.
        /// </summary>
        public static void StepFacing(ref double yaw, ref double yawVel, double target, double dt)
        {
            if (dt <= 0) return;
            double x = Wrap(yaw - target), v = yawVel, e = Math.Exp(-YawOmega * dt);
            double nx = (x + (v + YawOmega * x) * dt) * e;
            double nv = (v - YawOmega * (v + YawOmega * x) * dt) * e;
            double maxStep = MaxYawRate * dt;
            if (Math.Abs(nx - x) > maxStep) { nx = x + Math.Sign(nx - x) * maxStep; nv = (nx - x) / dt; }
            yaw = Wrap(target + nx);
            yawVel = nv;
        }

        /// <summary>Footsteps on the measured touchdown phases of the dominant locomotion clip (DG-93), near the camera only.</summary>
        void EmitSteps(ModelView m, World w, Actor actor, int i, Vector3 basePos, bool evaluated)
        {
            if (_speed < 0.5 || _eCount == 0 || !AnimLod.Near(basePos)) return;
            int best = 0;
            for (int e = 1; e < _eCount; e++) if (_eW[e] > _eW[best]) best = e;
            ClipFact f = ClipFacts.Get(_eClip[best]);
            if (f == null || f.touchdown == null || f.touchdown.Length < 2) return;
            double plant = f.leftPlant >= 0 ? f.leftPlant : 0.2, refPlant = ClipFacts.LeftPlant(ArtMapping.Run, 0.203f);
            for (int foot = 0; foot < 2; foot++)
            {
                double td = f.touchdown[foot];
                if (td < 0) continue;
                double marker = _eRev[best] ? Locomotion.Frac(plant + refPlant - td) : Locomotion.Frac(td - plant + refPlant);
                if (!Locomotion.Crossed(_prevPhase, _phase, marker)) continue;
                Transform toe = evaluated ? m.Bone(foot == 0 ? "toes.l" : "toes.r") : null;
                Footfalls.Emit(new Footfall { Tick = w.Tick, Actor = i, Kind = FootfallKind.Step, Position = toe != null ? toe.position : basePos,
                    Strength = Footfalls.StepStrength, Radius = Footfalls.StepRadius, Left = foot == 0, Team = actor.Team });
            }
        }

        /// <summary>
        /// Orientation warping for the sideways sectors (fix/animation-reds): after the graph wrote the pose, turn the hips
        /// (and with them the legs) by the warp about the model's up axis, and the spine back by the same amount so the
        /// chest and arms keep facing the aim. Only after an evaluation: on a skipped frame the bones hold last frame's
        /// (already turned) pose, and the next evaluation rewrites hips and spine from the clips, so nothing compounds.
        /// </summary>
        /// <summary>
        /// fix/kaiju-alive-1 (nothing-glides audit): a fighter on a Builder LADDER cell (a Wall cell a ladder piece turned
        /// walkable) used to run up it with the run cycle. Cheap procedural climb on top of the clip: the arms reach up
        /// hand over hand (alternating, phase from the sim time and the actor id), the torso leans into the wall. View only.
        /// </summary>
        void ApplyClimb(ModelView m, World w, int i, double simTime)
        {
            // b14 ladder fix: the climb pose belongs on the rungs (between the feet and the lip), not on the wall top
            float target = CriticalMass.Runtime.View.Mechanics.LadderClimb.OnRungs(w, w.X[i], w.Z[i]) ? 1f : 0f;
            _climbW += (target - _climbW) * 0.25f;
            if (_climbW < 0.01f) { _climbW = 0f; return; }
            float ph = (float)(2 * Math.PI * 1.6 * simTime + i * 0.9);
            Reach(m.Bone("upperarm.l"), 150f + 25f * Mathf.Sin(ph));
            Reach(m.Bone("upperarm.r"), 150f - 25f * Mathf.Sin(ph));
            Transform spine = m.Bone("spine");
            if (spine != null) spine.localRotation = Quaternion.Slerp(spine.localRotation, spine.localRotation * Quaternion.Euler(18f, 0f, 0f), _climbW);

            void Reach(Transform arm, float deg)
            {
                if (arm == null || arm.parent == null) return;
                // Raise the arm about the model's sideways axis, expressed in the arm's parent space.
                Vector3 side = arm.parent.InverseTransformDirection(m.Transform.right);
                Quaternion up = Quaternion.AngleAxis(-deg, side) * arm.localRotation;
                arm.localRotation = Quaternion.Slerp(arm.localRotation, up, _climbW * 0.85f);
            }
        }

        float _climbW;

        /// <summary>True when actor i stands on a cell of an active ladder piece (BuiltPieces: the cell was a Wall).</summary>
        static bool OnLadder(World w, int i)
        {
            BuiltPiece[] ps = w.Builder != null ? w.Builder.Pieces : null;
            if (ps == null) return false;
            var map = w.Map;
            int cx = (int)Math.Floor(w.X[i]), cz = (int)Math.Floor(w.Z[i]);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            int cell = cz * map.Width + cx;
            for (int p = 0; p < ps.Length; p++)
            {
                BuiltPiece b = ps[p];
                if (b == null || !b.Active || b.Carried || b.Kind != BuiltPieces.Ladder || b.Cells == null) continue;
                for (int c = 0; c < b.Count; c++) if (b.Cells[c] == cell) return true;
            }
            return false;
        }

        void ApplyLegWarp(ModelView m)
        {
            if (Math.Abs(_warp) < 0.25) return;
            Transform hips = m.Bone(HipsBone);
            if (hips == null) return;
            // Unity is left-handed with Y up: a positive angle about up turns to the right, so + left is a negative angle.
            Quaternion q = Quaternion.AngleAxis(-(float)_warp, m.Transform.up);
            hips.rotation = q * hips.rotation;
            Transform spine = m.Bone(ModelView.UpperBodyRootBone);
            if (spine != null && spine.IsChildOf(hips)) spine.rotation = Quaternion.Inverse(q) * spine.rotation;
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Squash(Vector3 s, float y)
        {
            float xz = 1f / Mathf.Sqrt(Mathf.Max(0.1f, y)); // volume preserving
            return new Vector3(s.x * xz, s.y * y, s.z * xz);
        }

        static float Approach(float v, float target, double dt, double seconds)
        {
            if (dt <= 0) return v;
            float step = (float)(dt / seconds);
            return v < target ? Mathf.Min(target, v + step) : Mathf.Max(target, v - step);
        }

        static double Wrap(double deg)
        {
            deg %= 360.0;
            if (deg > 180) deg -= 360;
            else if (deg < -180) deg += 360;
            return deg;
        }

        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        /// <summary>Deterministic hash of (actor, n): idle-break timing and death variety never use UnityEngine.Random (C7).</summary>
        public static int Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        public static double Hash01(int a, int b) => (Hash(a, b) % 10007) / 10007.0;
    }
}
