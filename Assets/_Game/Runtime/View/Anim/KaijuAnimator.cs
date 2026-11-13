using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// The hatched Kaiju's pose (DG-92, DG-93, A13, A14; fix/kaiju-alive-1). Clip layer: KO (and a get-up when it
    /// recovers), slam telegraph (SlamWindup stretched over the sim's real telegraph length), slam (its impact frame lands
    /// on the tick the slam resolves and is held a beat), claw wind-up (SwipeWindup when the art has it, else the Swipe
    /// clip's own anticipation up to its contact frame, stretched over the sim's real wind-up ticks), the strike after it
    /// (Swipe from its contact frame, so the arm is at speed on the damage tick), roars, berserk, and Idle/Walk at the
    /// MEASURED stride (playback rate = ground speed / the Walk clip's planted-foot speed, so the feet do not slide).
    /// Procedural layer on top, after the graph wrote the bones: the drawn yaw follows the sim facing through a fast spring
    /// (no snap turns), the body leans into turns, the tail lags behind them, the neck and head look at the target, and a
    /// hit jerks the head back. Roars on hatch, on choosing a side and on going berserk (clip, plus the existing
    /// kaiju_roar cue, pitched per stage). A footfall on each measured foot contact (the force bus turns it into the
    /// ground shake the camera allows, dust, footprints and the deep step thud). Hatchling: quicker, lighter, higher.
    /// View only: nothing here writes the sim.
    /// </summary>
    public sealed class KaijuAnimator
    {
        public const double GrowSeconds = 0.5, SlamHold = 0.3, StrikeSeconds = 0.6, FlinchSeconds = 0.25, FlinchDeg = 5.0;
        public const double FallbackWalkSpeed = 1.5;
        /// <summary>Roar: delay after the hatch (the shell falls first), the moving share of the Roar clip (legs keep the
        /// stride), and the get-up after a knock-out.</summary>
        /// <summary>Walk playback = ground speed / the clip's planted-foot speed, clamped; below WalkFrom u/s the Walk blends
        /// to Idle, full Walk from WalkFull (a slow creep plays the Walk slowly, not a half-weight shuffle that slides).</summary>
        public const double MinWalkRate = 0.25, MaxWalkRate = 3.2, WalkFrom = 0.08, WalkFull = 0.3;
        public const double HatchRoarDelay = 0.35, GetUpSeconds = 1.0;
        public const float RoarWhileMoving = 0.4f;
        /// <summary>Procedural limits (degrees) and the yaw spring (1/s). The adult turns at most TurnRateDeg per second.</summary>
        public const float LookMaxDeg = 40f, LeanMaxDeg = 7f, TailMaxDeg = 28f, HitHeadDeg = 12f;
        public const double YawOmega = 18.0, TurnRateDeg = 360.0, HatchlingTurnRateDeg = 540.0;

        sealed class Measured
        {
            public double WalkSpeed = FallbackWalkSpeed;
            public double[] Contacts = new double[0];
            public double SlamContact = -1, SwipeContact = -1;
            public bool StrideMeasured;
        }

        readonly Dictionary<ModelView, Measured> _measured = new Dictionary<ModelView, Measured>();
        double _idleT, _walkT, _prevWalkT;
        double _strikeAt = double.NegativeInfinity, _flinchAt = double.NegativeInfinity, _growAt = double.NegativeInfinity;
        double _roarAt = double.NegativeInfinity, _getUpAt = double.NegativeInfinity, _hatchAt = double.NegativeInfinity;
        double _teleTotal, _lastTeleIn;
        int _lastWindup, _windupLen;
        double _lastHp = double.NaN, _lastKo;
        KaijuState _lastState = KaijuState.Egg;
        bool _roarPending, _roarSoundPending;
        float _roarPitch = 1f, _roarGain = 1f;
        ModelView _lastModel;
        int _lastTick = -1;
        // procedural state
        double _yaw = double.NaN, _yawRate, _lean, _tail, _look;

        /// <summary>Measured walk ground speed of a model (u/s at rate 1), for tests and the capture report.</summary>
        public double WalkSpeedOf(ModelView m) => m != null && _measured.TryGetValue(m, out Measured r) ? r.WalkSpeed : FallbackWalkSpeed;

        /// <summary>True when the model's Walk clip had planted-foot contacts to measure (a static clip has none).</summary>
        public bool StrideMeasuredOf(ModelView m) => m != null && _measured.TryGetValue(m, out Measured r) && r.StrideMeasured;

        /// <summary>Sim time of the last claw strike (wind-up ended) and of the last health loss (the flinch); -infinity before
        /// any. ActorViews reads them for the Kaiju face (angry while striking, hurt after a hit).</summary>
        public double LastStrikeAt => _strikeAt;
        public double LastFlinchAt => _flinchAt;
        /// <summary>Sim time of the last roar (hatch, side, berserk); -infinity before any.</summary>
        public double LastRoarAt => _roarAt;

        /// <summary>Footfalls raised by the Kaiju since start, and roars started.</summary>
        public int Steps { get; private set; }
        public int Roars { get; private set; }

        /// <summary>The clip with the most weight in the last pose ("Walk", "Swipe", "SlamWindup", ...), for tests.</summary>
        public string Dominant { get; private set; } = "";
        /// <summary>The Walk clip's playback rate in the last pose (ground speed / measured stride speed).</summary>
        public double WalkRate { get; private set; }
        /// <summary>The planted-foot speed of the drawn model's Walk at Scale 1 (world u/s), as measured.</summary>
        public double LastWalkSpeed { get; private set; }
        /// <summary>Metres the body travels per Walk cycle at Scale 1 (planted-foot speed x cycle length).</summary>
        public double LastStrideMetres { get; private set; }
        /// <summary>The drawn yaw (degrees, view space) and the procedural lean, tail and look angles of the last pose.</summary>
        public float DrawnYaw => (float)_yaw;
        public float LeanDeg => (float)_lean;
        public float TailDeg => (float)_tail;
        public float LookDeg => (float)_look;

        /// <summary>The scale the model is drawn at this frame (1 except during the hatchling-to-adult tween).</summary>
        public float Scale { get; private set; } = 1f;

        /// <summary>
        /// Poses the hatchling or adult model m (already placed by ActorViews; `facing` is the sim facing as a view
        /// rotation). slamAt / berserkAt: the sim times ActorViews latched for the slam resolving and berserk starting.
        /// </summary>
        public void Pose(ModelView m, ModelView hatchling, ModelView adult, World w, GameConfig cfg, Quaternion facing, double simTime,
            double dSim, double speed, bool telegraph, double slamAt, double berserkAt)
        {
            Kaiju k = w.Kaiju;
            Measured me = Measure(m);
            if (w.Tick < _lastTick) Reset();   // a new match (or a reset world): forget the last one's edges
            bool newTick = w.Tick != _lastTick;
            _lastTick = w.Tick;
            bool isHatchling = m == hatchling && hatchling != null;

            // Edges, once per sim tick.
            if (newTick)
            {
                if (_lastWindup > 0 && k.MeleeWindupIn <= 0 && k.KoIn <= 0) _strikeAt = simTime;
                if (k.MeleeWindupIn > 0 && _lastWindup <= 0) _windupLen = Math.Max(k.MeleeWindupLen, k.MeleeWindupIn);
                _lastWindup = k.MeleeWindupIn;
                if (!double.IsNaN(_lastHp) && k.Hp < _lastHp - 1e-9) _flinchAt = simTime;
                _lastHp = k.Hp;
                // The slam telegraph's real length (the sim floors it to KaijuSlamMinRealSeconds at the match pace).
                if (k.TelegraphIn > 0 && (_lastTeleIn <= 0 || k.TelegraphIn > _lastTeleIn)) _teleTotal = k.TelegraphIn;
                _lastTeleIn = k.TelegraphIn;
                if (_lastKo > 0 && k.KoIn <= 0) _getUpAt = simTime;
                _lastKo = k.KoIn;
                // Roars: the hatch, a side chosen (or switched), berserk.
                if (k.State != _lastState)
                {
                    if (_lastState == KaijuState.Egg) { _hatchAt = simTime; _roarPending = true; }
                    else if (k.State == KaijuState.Berserk) StartRoar(simTime, 0.8f, 1.1f, false);   // the Berserk clip is the roar
                    else if (k.State == KaijuState.Azure || k.State == KaijuState.Crimson) StartRoar(simTime, isHatchling ? 1.3f : 1f, 1f, true);
                    _lastState = k.State;
                }
                if (_roarPending && simTime - _hatchAt >= HatchRoarDelay)
                {
                    _roarPending = false;
                    StartRoar(simTime, isHatchling ? 1.35f : 1f, isHatchling ? 0.75f : 1f, true);
                }
            }
            if (_lastModel == hatchling && hatchling != null && m == adult) _growAt = simTime;
            _lastModel = m;

            // Hatchling to adult: the adult starts at the hatchling's height and grows (never more than ~8% a frame).
            float s = 1f;
            double g = simTime - _growAt;
            if (m == adult && g >= 0 && g < GrowSeconds)
            {
                ModelSpec hs = ArtMapping.Spec(ArtMapping.KaijuHatchling), asp = ArtMapping.Spec(ArtMapping.KaijuAdult);
                float from = hs != null && asp != null && asp.Height > 0 ? hs.Height / asp.Height : 0.45f;
                s = Mathf.Lerp(from, 1f, Locomotion.Smooth(0, 1, g / GrowSeconds));
            }
            Scale = s;
            m.Transform.localScale = m.BaseScale * s;   // fix/model-scale: set from the stored base, the Kaiju's own models only

            // Drawn yaw: a fast spring toward the sim facing, rate-limited (the sim snaps its facing toward a target).
            double target = facing.eulerAngles.y;
            if (double.IsNaN(_yaw)) { _yaw = target; _yawRate = 0; }
            double yawStep = 0;
            if (dSim > 0)
            {
                double err = Locomotion.Wrap180(target - _yaw);
                double maxStep = (isHatchling ? HatchlingTurnRateDeg : TurnRateDeg) * dSim;
                yawStep = Math.Max(-maxStep, Math.Min(maxStep, err * (1 - Math.Exp(-YawOmega * dSim))));
                if (Math.Abs(err) > 150 && k.KoIn > 0) yawStep = err;   // knocked out: no spin
                _yaw = _yaw + yawStep;
                double rate = yawStep / dSim;
                _yawRate += (rate - _yawRate) * (1 - Math.Exp(-12 * dSim));
            }
            Quaternion drawn = Quaternion.Euler(0f, (float)_yaw, 0f);

            // Flinch: a tip backwards (the Flinch clip blends on top when the art has it). Lean into the turn.
            double fl = simTime - _flinchAt;
            float flinch = fl >= 0 && fl < FlinchSeconds ? (float)(1 - fl / FlinchSeconds) : 0f;
            double moving = Math.Min(1.0, speed / Math.Max(0.3, me.WalkSpeed));
            double leanTarget = k.KoIn > 0 ? 0 : -Math.Max(-LeanMaxDeg, Math.Min(LeanMaxDeg, _yawRate * 0.035 * (0.35 + 0.65 * moving)));
            if (dSim > 0) _lean += (leanTarget - _lean) * (1 - Math.Exp(-8 * dSim));
            m.SetFacing(drawn * Quaternion.Euler((float)(-FlinchDeg * flinch), 0f, (float)_lean));

            // Stride: turning in place steps too (a 1 u stance swings at |yaw rate| x 1 u).
            double turnSpeed = Math.Abs(_yawRate) * Math.PI / 180.0 * (isHatchling ? 0.5 : 1.0);
            // Turning steps only while (nearly) standing: on the move the stride follows the ground speed. CI run 43491982024:
            // the walk sat at its 3.2 cap while the Kaiju moved ~1.3 u/s (rate needed ~1.4), because a chasing Kaiju turns
            // about 180 deg/s and the turn term (|yaw rate| x 1 u = ~3 u/s) won the max; the planted feet ran backwards at
            // ~2.3x the body speed (slide 1.27 u/u). Pivot steps also use the half stance (0.5 u), not 1 u.
            double stepSpeed = speed >= WalkFull ? speed : Math.Max(speed, 0.5 * turnSpeed);
            _idleT += dSim * (isHatchling ? 1.25 : 1.0);
            // Cap 3.2: the adult's Walk plants at 0.91 u/s at full size (CI run 4) and it moves at up to 2.6 u/s; a 2.2 cap slid.
            double walkRate = Math.Max(MinWalkRate, Math.Min(MaxWalkRate, stepSpeed / Math.Max(1e-3, me.WalkSpeed * s)));
            WalkRate = walkRate;
            LastWalkSpeed = me.WalkSpeed;
            LastStrideMetres = me.WalkSpeed * m.ClipLength(ArtMapping.Walk);
            _prevWalkT = _walkT;
            _walkT += dSim * walkRate;

            m.BeginPose();
            float walkW = 0f;
            double since = simTime - slamAt, strike = simTime - _strikeAt, roar = simTime - _roarAt, getUp = simTime - _getUpAt;
            float slamLen = m.ClipLength(ArtMapping.Slam), berserkLen = m.ClipLength(ArtMapping.Berserk), roarLen = m.ClipLength(ArtMapping.Roar);
            float koLen = m.ClipLength(ArtMapping.KO);
            const double lead = 2.0 / 30.0;
            string dom;
            if (k.KoIn > 0) { m.Pose(ArtMapping.KO, cfg.Kaiju.KoSeconds - k.KoIn, 1f, false); dom = ArtMapping.KO; }
            else if (telegraph)
            {
                double total = _teleTotal > 0 ? _teleTotal : cfg.Kaiju.SlamTelegraphSeconds;
                double u = Math.Max(0, Math.Min(1, (total - k.TelegraphIn) / Math.Max(1e-3, total)));
                m.Pose(ArtMapping.SlamWindup, u * m.ClipLength(ArtMapping.SlamWindup), 1f, false);
                dom = ArtMapping.SlamWindup;
            }
            else if (k.MeleeWindupIn > 0)
            {
                // The wind-up runs over the sim's real wind-up (MeleeWindupLen ticks, floored to 0.45 real s), not the
                // config's 11: with 11 the pose sat at frame 0 for most of a 27-tick wind-up and then snapped.
                int len = Math.Max(1, _windupLen > 0 ? _windupLen : (int)Math.Round(cfg.Kaiju.MeleeWindupTicks));
                double u = Math.Max(0, Math.Min(1, (len - k.MeleeWindupIn) / (double)len));
                if (m.HasClip(ArtMapping.SwipeWindup)) { m.Pose(ArtMapping.SwipeWindup, u * m.ClipLength(ArtMapping.SwipeWindup), 1f, false); dom = ArtMapping.SwipeWindup; }
                else if (m.HasClip(ArtMapping.Swipe))
                {
                    double c = me.SwipeContact >= 0 ? me.SwipeContact : 0.1;
                    m.Pose(ArtMapping.Swipe, u * Math.Max(0, c - lead), 1f, false);
                    dom = ArtMapping.Swipe;
                }
                else { m.Pose(ArtMapping.Roar, u * m.ClipLength(ArtMapping.Roar), 1f, false); dom = ArtMapping.Roar; }
            }
            else if (since >= 0 && SlamTime(since, me.SlamContact, lead) < slamLen - 1e-3)
            {
                m.Pose(ArtMapping.Slam, SlamTime(since, me.SlamContact, lead), 1f, false);
                dom = ArtMapping.Slam;
            }
            else if (strike >= 0 && strike < StrikeSeconds && StrikeClip(m, me, out string sc, out double sct))
            {
                double t = Math.Max(0, sct - lead) + strike;
                float wgt = (float)Math.Min(1, (StrikeSeconds - strike) / 0.15);
                walkW = Locomotion.Smooth(WalkFrom, WalkFull, stepSpeed);
                if (walkW > 0f && m.HasUpperLayer && m.PoseUpper(sc, Math.Min(t, m.ClipLength(sc) - 1e-3), 1f, false))
                {
                    // Moving again after the swing: the legs keep the stride, the swing finishes on the torso and arms.
                    m.UpperWeight = wgt;
                    m.Pose(ArtMapping.Idle, _idleT, 1f - walkW, true);
                    m.Pose(ArtMapping.Walk, _walkT, walkW, true);
                }
                else
                {
                    m.Pose(sc, Math.Min(t, m.ClipLength(sc) - 1e-3), wgt, false);
                    walkW *= 1f - wgt;
                    m.Pose(ArtMapping.Idle, _idleT, 1f - wgt - walkW, true);
                    m.Pose(ArtMapping.Walk, _walkT, walkW, true);
                }
                dom = sc;
            }
            else if (getUp >= 0 && getUp < GetUpSeconds && koLen > 0f)
            {
                // Get up: the knock-out fall played backwards, blending into the idle.
                double u = getUp / GetUpSeconds;
                m.Pose(ArtMapping.KO, koLen * (1 - u), (float)(1 - u * 0.5), false);
                m.Pose(ArtMapping.Idle, _idleT, (float)(u * 0.5), true);
                dom = ArtMapping.KO;
            }
            else if (roar >= 0 && roar < roarLen)
            {
                walkW = Locomotion.Smooth(WalkFrom, WalkFull, stepSpeed);
                float fade = (float)Math.Min(1, (roarLen - roar) / 0.2);
                if (walkW > 0f && m.HasUpperLayer && m.PoseUpper(ArtMapping.Roar, roar, 1f, false))
                {
                    // Roaring on the move: full roar on the torso, head and jaw over an unbroken stride.
                    m.UpperWeight = fade;
                    m.Pose(ArtMapping.Idle, _idleT, 1f - walkW, true);
                    m.Pose(ArtMapping.Walk, _walkT, walkW, true);
                    dom = ArtMapping.Roar;
                }
                else
                {
                    float rw = (walkW > 0.3f ? RoarWhileMoving : 1f) * fade;
                    m.Pose(ArtMapping.Roar, roar, rw, false);
                    m.Pose(ArtMapping.Idle, _idleT, (1f - rw) * (1f - walkW), true);
                    m.Pose(ArtMapping.Walk, _walkT, (1f - rw) * walkW, true);
                    walkW *= 1f - rw;
                    dom = rw >= 0.5f ? ArtMapping.Roar : walkW > 0.5f ? ArtMapping.Walk : ArtMapping.Roar;
                }
            }
            else if (simTime - berserkAt < berserkLen) { m.Pose(ArtMapping.Berserk, simTime - berserkAt, 1f, false); dom = ArtMapping.Berserk; }
            else
            {
                walkW = Locomotion.Smooth(WalkFrom, WalkFull, stepSpeed);
                m.Pose(ArtMapping.Idle, _idleT, 1f - walkW, true);
                m.Pose(ArtMapping.Walk, _walkT, walkW, true);
                dom = walkW >= 0.5f ? ArtMapping.Walk : ArtMapping.Idle;
            }
            if (flinch > 0f && m.HasClip(ArtMapping.Flinch)) m.Pose(ArtMapping.Flinch, fl, flinch * 0.6f, false);
            Dominant = dom;
            bool wrote = m.Evaluate(true);

            if (_roarSoundPending)
            {
                _roarSoundPending = false;
                SoundDirector.PlayCueAt("kaiju_roar", k.X, k.Z, _roarGain, _roarPitch);
            }

            if (wrote) Procedural(m, w, k, dSim, flinch, isHatchling, drawn);

            // Footfalls on the measured contacts of the Walk clip (the force bus makes the shake, dust and thud).
            float walkLen = m.ClipLength(ArtMapping.Walk);
            if (walkW > 0.5f && walkLen > 0f && me.Contacts.Length > 0 && dSim > 0)
            {
                double p0 = Locomotion.Frac(_prevWalkT / walkLen), p1 = Locomotion.Frac(_walkT / walkLen);
                for (int i = 0; i < me.Contacts.Length; i++)
                {
                    if (!Locomotion.Crossed(p0, p1, me.Contacts[i])) continue;
                    Transform foot = m.Bone((i & 1) == 0 ? "Foot_L" : "Foot_R");
                    bool adultStep = m == adult;
                    Steps++;
                    Footfalls.Emit(new Footfall { Tick = w.Tick, Actor = -2, Kind = FootfallKind.KaijuStep,
                        Position = foot != null ? foot.position : m.Transform.position,
                        Strength = Footfalls.KaijuStepStrength * (adultStep ? 1f : 0.5f), Radius = Footfalls.KaijuStepRadius * (adultStep ? 1f : 0.5f),
                        Left = (i & 1) == 0, Team = TeamId.Azure });
                }
            }
        }

        /// <summary>Forget every edge and spring (a new match): the next hatch roars again.</summary>
        public void Reset()
        {
            _strikeAt = _flinchAt = _growAt = _roarAt = _getUpAt = _hatchAt = double.NegativeInfinity;
            _teleTotal = _lastTeleIn = 0;
            _lastWindup = _windupLen = 0;
            _lastHp = double.NaN;
            _lastKo = 0;
            _lastState = KaijuState.Egg;
            _roarPending = _roarSoundPending = false;
            _yaw = double.NaN;
            _yawRate = _lean = _tail = _look = 0;
            _lastTick = -1;
        }

        void StartRoar(double simTime, float pitch, float gain, bool animate)
        {
            if (animate) _roarAt = simTime;
            _roarPitch = pitch;
            _roarGain = gain;
            _roarSoundPending = true;
            Roars++;
        }

        /// <summary>
        /// Secondary motion on the evaluated pose (the graph rewrites every bone each frame, so nothing accumulates): the
        /// tail lags the turn, the neck and head look toward the target, a hit jerks the head back.
        /// </summary>
        void Procedural(ModelView m, World w, Kaiju k, double dSim, float flinch, bool hatchling, Quaternion drawn)
        {
            Quaternion parent = m.Transform.parent != null ? m.Transform.parent.rotation : Quaternion.identity;
            Vector3 up = parent * Vector3.up;
            double tailTarget = k.KoIn > 0 ? 0 : -Math.Max(-TailMaxDeg, Math.Min(TailMaxDeg, _yawRate * 0.12));
            double lookTarget = 0;
            int t = k.MeleeWindupIn > 0 ? k.MeleeTargetId : k.TargetId;
            if (k.KoIn <= 0 && t >= 0 && t < w.ActorCount)
            {
                double dx = w.X[t] - k.X, dz = w.Z[t] - k.Z;
                if (dx * dx + dz * dz > 0.25)
                {
                    double want = SimSpace.YawDeg(Math.Atan2(dz, dx));
                    lookTarget = Math.Max(-LookMaxDeg, Math.Min(LookMaxDeg, Locomotion.Wrap180(want - _yaw)));
                }
            }
            if (dSim > 0)
            {
                _tail += (tailTarget - _tail) * (1 - Math.Exp(-(hatchling ? 9 : 6) * dSim));
                _look += (lookTarget - _look) * (1 - Math.Exp(-(hatchling ? 10 : 6) * dSim));
            }
            Turn(m.Bone("Tail1"), up, _tail * 0.3);
            Turn(m.Bone("Tail2"), up, _tail * 0.35);
            Turn(m.Bone("Tail3"), up, _tail * 0.35);
            Turn(m.Bone("Neck"), up, _look * 0.4);
            Turn(m.Bone("Head"), up, _look * 0.6);
            if (flinch > 0f)
            {
                Transform head = m.Bone("Head");
                if (head != null) head.rotation = Quaternion.AngleAxis(-HitHeadDeg * flinch, parent * (drawn * Vector3.right)) * head.rotation;
            }
        }

        static void Turn(Transform bone, Vector3 axis, double deg)
        {
            if (bone == null || Math.Abs(deg) < 1e-3) return;
            bone.rotation = Quaternion.AngleAxis((float)deg, axis) * bone.rotation;
        }

        /// <summary>Slam clip time `since` the slam resolved: 2 frames before impact, the impact held SlamHold, then the tail.</summary>
        public static double SlamTime(double since, double contact, double lead)
        {
            if (contact < 0) return since;
            double t0 = Math.Max(0, contact - lead);
            if (since < contact - t0) return t0 + since;
            if (since < contact - t0 + SlamHold) return contact;
            return contact + (since - (contact - t0) - SlamHold);
        }

        static bool StrikeClip(ModelView m, Measured me, out string clip, out double contact)
        {
            if (m.HasClip(ArtMapping.Swipe)) { clip = ArtMapping.Swipe; contact = me.SwipeContact >= 0 ? me.SwipeContact : 0.1; return true; }
            if (m.HasClip(ArtMapping.Slam) && me.SlamContact >= 0) { clip = ArtMapping.Slam; contact = me.SlamContact; return true; }
            clip = null;
            contact = 0;
            return false;
        }

        /// <summary>Once per model: walk ground speed and foot contacts (Foot_L/Foot_R), slam and swipe hand-speed peaks.</summary>
        Measured Measure(ModelView m)
        {
            if (_measured.TryGetValue(m, out Measured r)) return r;
            r = new Measured();
            _measured[m] = r;
            // The Kaiju always animates (one actor, always relevant): with CullCompletely the first pose after the model is
            // shown (no visibility result yet, or a -nographics test) wrote no bones, so this measurement read a static
            // Walk and cached the fallback stride with no foot contacts for the rest of the match.
            Animator an = m.Root.GetComponent<Animator>();
            if (an != null && an.cullingMode != AnimatorCullingMode.AlwaysAnimate) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform fl = m.Bone("Foot_L"), fr = m.Bone("Foot_R");
            float len = m.ClipLength(ArtMapping.Walk);
            if (fl != null && fr != null && len > 0f)
            {
                const int n = 48;
                // Measure in WORLD units at the grown size (Scale 1): the root at its imported BaseScale for the measure, so the
                // stride carries every scale above and below the root (CI run 2 read a stride ~2x short in model-local units,
                // the Walk ran at its 2.2 cap and the planted feet slid 1.2 u per u travelled).
                Vector3 keepScale = m.Transform.localScale;
                m.Transform.localScale = m.BaseScale;
                var y = new float[2, n + 1];
                var p = new Vector3[2, n + 1];
                for (int k = 0; k <= n; k++)
                {
                    m.BeginPose();
                    m.Pose(ArtMapping.Walk, len * k / n, 1f, true);
                    if (!m.Evaluate(true)) { m.Transform.localScale = keepScale; _measured.Remove(m); return r; }
                    p[0, k] = fl.position - m.Transform.position;
                    p[1, k] = fr.position - m.Transform.position;
                    y[0, k] = p[0, k].y;
                    y[1, k] = p[1, k].y;
                }
                ModelSpec spec = m.Spec;
                float tol = 0.015f * (spec != null && spec.Height > 0 ? spec.Height : 2f);
                var contacts = new List<double>();
                double speedSum = 0;
                int speedN = 0;
                for (int f = 0; f < 2; f++)
                {
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    for (int k = 0; k <= n; k++) { min = Mathf.Min(min, y[f, k]); max = Mathf.Max(max, y[f, k]); }
                    if (max - min < tol) continue;   // a foot that never lifts: no contacts (a static clip)
                    for (int k = 0; k < n; k++)
                    {
                        bool a = y[f, k] < min + tol, b = y[f, k + 1] < min + tol;
                        if (a && b)
                        {
                            Vector3 d = p[f, k + 1] - p[f, k];
                            speedSum += new Vector2(d.x, d.z).magnitude / (len / n);
                            speedN++;
                        }
                        if (!a && b) contacts.Add((double)(k + 1) / n);
                    }
                }
                m.Transform.localScale = keepScale;
                double sp = speedN > 0 ? speedSum / speedN : 0;   // world units per second at Scale 1
                if (sp > 0.2 && sp < 10) { r.WalkSpeed = sp; r.StrideMeasured = contacts.Count > 0; }
                contacts.Sort();
                r.Contacts = contacts.ToArray();
            }
            r.SlamContact = PeakTime(m, ArtMapping.Slam);
            r.SwipeContact = PeakTime(m, ArtMapping.Swipe);
            return r;
        }

        static double PeakTime(ModelView m, string clip)
        {
            float len = m.ClipLength(clip);
            Transform hand = m.Bone("Fist_R") ?? m.Bone("Forearm_R") ?? m.Bone("Arm_R");
            if (len <= 0f || hand == null) return -1;
            const int n = 40;
            Vector3 prev = Vector3.zero;
            double best = -1, bestT = -1;
            for (int k = 0; k <= n; k++)
            {
                m.BeginPose();
                m.Pose(clip, len * k / n, 1f, false);
                if (!m.Evaluate(true)) return -1;
                Vector3 q = m.Transform.InverseTransformPoint(hand.position);
                if (k > 0)
                {
                    double v = (q - prev).magnitude;
                    if (v > best + 1e-6) { best = v; bestT = len * k / n; }
                }
                prev = q;
            }
            return best > 1e-4 ? bestT : -1;
        }
    }
}
