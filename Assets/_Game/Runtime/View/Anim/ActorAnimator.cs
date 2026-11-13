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
        // Facing spring (DG-89): critically damped, capped at 45 degrees per frame at the default pace (60 fps, pace 2).
        public const double YawOmega = 40.0, MaxYawRate = 1350.0, BankMaxDeg = 8.0, BankGain = 0.011, PitchMaxDeg = 5.0, PitchGain = 0.2;
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
            double target = SimSpace.YawDeg(actor.Facing);
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
            }

            // ---- actions: AnimAction resets to 0 on the press tick of an attack or an ability
            double aa = actor.AnimAction;
            if (!corpse && aa + 1e-9 < _lastAnimAction && (actor.AttackCd > 0 || actor.AbilityCd > 0)) BeginAction(m, actor, cfg, simTime);
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
                idleW = PoseGround(m, actor, cfg, simTime, loopDt, air, ref scale);
            }

            // ---- upper body (DG-73, DG-74, DG-88, DG-71)
            float upperSum = 0f;
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
            Quaternion rot = Quaternion.Euler(0f, (float)_yaw + twist, 0f) * Quaternion.Euler(_pitch, 0f, _roll);
            if (hasFx) rot = fx.Lean * rot;
            m.SetFacing(rot);
            // fix/model-scale: scale above is the effect factor, rebuilt from 1 this frame; the one owner composes and clamps it.
            m.Transform.localScale = ModelScale.Compose(m.BaseScale, scale, m.AssignedName);

            bool fresh = _fresh > 0;
            if (_fresh > 0) _fresh--;
            bool evaluate = AnimLod.ShouldEvaluate(m.AlwaysAnimate, fresh, m.AnyVisible, basePos, _id);
            bool evaluated = m.Evaluate(evaluate);
            if (evaluated) ApplyLegWarp(m);
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
            state.ActionAge = _actClip != null ? simTime - _actStart : 1e9;
            return evaluated;
        }

        double _actStart;

        // ------------------------------------------------------------------ ground: locomotion, heavy hits, gather, idle

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
            for (int e = 0; e < _eCount; e++)
            {
                double l = m.ClipLength(_eClip[e]);
                stride += _eW[e] * ClipFacts.GroundSpeed(_eClip[e], IsRunClip(_eClip[e]) ? 4.57f : 0.86f) * l;
                len += _eW[e] * l;
                wsum += _eW[e];
            }
            if (wsum > 0) { stride /= wsum; len /= wsum; }
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
            if (_hasReact && _react.Layer == ReactionLayer.FullBody && m.HasClip(_react.Clip))
            {
                hitT = _hitAgeForPose;
                heavy = _react.Weight * HitReactions.Decay(hitT, m.ClipLength(_react.Clip));
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
            float refPlant = ClipFacts.LeftPlant(ArtMapping.Run, 0.203f);
            for (int e = 0; e < _eCount; e++)
            {
                string c = _eClip[e];
                double t = Locomotion.SampleTime(_phase, m.ClipLength(c), ClipFacts.LeftPlant(c, IsRunClip(c) ? 0.203f : 0.281f), refPlant, _eRev[e]);
                m.Pose(c, t, _eW[e] / (float)Math.Max(1e-6, wsum) * move * rest, true);
            }
            if (_gather > 0f) m.Pose(gatherClip, _gatherT, _gather * (1f - heavy) * (1f - land), true);
            if (heavy > 0f) m.Pose(_react.Clip, hitT, heavy, false);
            if (land > 0f) m.Pose(ArtMapping.JumpLand, 0.1 + since * 1.8, land * (1f - heavy), false);
            return idleW;
        }

        double _prevPhase;
        double _hitAgeForPose;

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
                    windup = AttackTiming.WindupTicks(cfg, actor.Cls) / hz;
                    double age = AttackTiming.SwingAge(actor, w.Tick, alpha, hz);
                    if (combat != null && hasFx) age = combat.AttackTime(i, age, fx); // the attacker freezes on contact during the hold
                    t = AttackTiming.ClipTime(age, ClipFacts.Contact(_actClip, (float)(len * 0.5)), len, windup);
                    _actAge = age;
                }
                else
                {
                    _actAge = actor.AnimAction + alpha / hz;
                    t = Math.Min(len, _actAge);
                }
                actW = AttackTiming.Envelope(_actAge, t, len, windup);
                if (_kind == 2 && actor.Cls == ClassId.Warrior) actW *= 1f - _holdW;
                if (actW <= 0f && t >= len - 1e-4) _actClip = null;
                else
                {
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
                float f = _prevW * (float)(1 - pf / AttackTiming.Retrigger);
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
                }
            }
            return sum;
        }

        double _actAge;

        void BeginAction(ModelView m, Actor actor, GameConfig cfg, double simTime)
        {
            ClassConfig cc = cfg.Class(actor.Cls);
            double aa = actor.AnimAction;
            bool attack = actor.AttackCd > 0 && Math.Abs(actor.AttackCd + aa - cc.AttackCooldown) < 1e-3;
            bool ability = actor.AbilityCd > 0 && Math.Abs(actor.AbilityCd + aa - cc.AbilityCooldown) < 1e-3;
            byte kind = attack ? (byte)1 : ability ? (byte)2 : actor.AttackCd > 0 ? (byte)1 : (byte)2;
            string clip = kind == 1 ? AttackTiming.AttackClip(m, actor.Cls) : AttackTiming.AbilityClip(m, actor.Cls);
            if (clip != null && !m.HasClip(clip)) clip = null;
            if (_actClip != null && _lastActW > 0f)
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
            _actStart = simTime - aa;
            _lastActW = 0f;
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

        void StepYaw(double target, double dt)
        {
            if (!_hasYaw) { _yaw = target; _yawVel = 0; _hasYaw = true; return; }
            if (dt <= 0) return;
            double x = Wrap(_yaw - target), v = _yawVel, e = Math.Exp(-YawOmega * dt);
            double nx = (x + (v + YawOmega * x) * dt) * e;
            double nv = (v - YawOmega * (v + YawOmega * x) * dt) * e;
            double maxStep = MaxYawRate * dt;
            if (Math.Abs(nx - x) > maxStep) { nx = x + Math.Sign(nx - x) * maxStep; nv = (nx - x) / dt; }
            _yaw = Wrap(target + nx);
            _yawVel = nv;
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
