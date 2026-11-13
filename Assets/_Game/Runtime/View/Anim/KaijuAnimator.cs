using System;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// The hatched Kaiju's pose (DG-92, DG-93, A13, A14): KO, slam telegraph, slam (its impact frame now lands on the tick
    /// the slam resolves and is held a beat), claw wind-up (SwipeWindup when the Kaiju art has it, else Roar, stretched over
    /// MeleeWindupTicks), the strike after it (Swipe, else the slam's tail from its impact frame, so the arm is at speed on
    /// the damage tick instead of dropping straight to Walk), berserk, and Idle/Walk at the MEASURED stride. A flinch when
    /// its HP drops (Flinch clip, else a short backwards tip), a scale tween instead of a pop from hatchling to adult, and
    /// a footfall on each measured foot contact. The stride and contacts are measured once per model on its own Walk clip
    /// (Foot_L / Foot_R), because the Kaiju art is not in clip_facts.json. View only.
    /// </summary>
    public sealed class KaijuAnimator
    {
        public const double GrowSeconds = 0.5, SlamHold = 0.3, StrikeSeconds = 0.6, FlinchSeconds = 0.25, FlinchDeg = 5.0;
        public const double FallbackWalkSpeed = 1.5;

        sealed class Measured
        {
            public double WalkSpeed = FallbackWalkSpeed;
            public double[] Contacts = new double[0];
            public double SlamContact = -1, SwipeContact = -1;
        }

        readonly Dictionary<ModelView, Measured> _measured = new Dictionary<ModelView, Measured>();
        double _idleT, _walkT, _prevWalkT;
        double _strikeAt = double.NegativeInfinity, _flinchAt = double.NegativeInfinity, _growAt = double.NegativeInfinity;
        int _lastWindup;
        double _lastHp = double.NaN;
        ModelView _lastModel;
        int _lastTick = -1;

        /// <summary>Measured walk ground speed of a model (u/s at rate 1), for tests and the capture report.</summary>
        public double WalkSpeedOf(ModelView m) => m != null && _measured.TryGetValue(m, out Measured r) ? r.WalkSpeed : FallbackWalkSpeed;

        /// <summary>Footfalls raised by the Kaiju since start.</summary>
        public int Steps { get; private set; }

        /// <summary>The scale the model is drawn at this frame (1 except during the hatchling-to-adult tween).</summary>
        public float Scale { get; private set; } = 1f;

        /// <summary>
        /// Poses the hatchling or adult model m (already placed and faced by ActorViews with `facing`). slamAt / berserkAt:
        /// the sim times ActorViews latched for the slam resolving and berserk starting.
        /// </summary>
        public void Pose(ModelView m, ModelView hatchling, ModelView adult, World w, GameConfig cfg, Quaternion facing, double simTime,
            double dSim, double speed, bool telegraph, double slamAt, double berserkAt)
        {
            Kaiju k = w.Kaiju;
            Measured me = Measure(m);
            bool newTick = w.Tick != _lastTick;
            _lastTick = w.Tick;

            // Edges, once per sim tick.
            if (newTick)
            {
                if (_lastWindup > 0 && k.MeleeWindupIn <= 0 && k.KoIn <= 0) _strikeAt = simTime;
                _lastWindup = k.MeleeWindupIn;
                if (!double.IsNaN(_lastHp) && k.Hp < _lastHp - 1e-9) _flinchAt = simTime;
                _lastHp = k.Hp;
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
            m.Transform.localScale = Vector3.one * s;

            // Flinch: a tip backwards (the Flinch clip blends on top when the art has it).
            double fl = simTime - _flinchAt;
            float flinch = fl >= 0 && fl < FlinchSeconds ? (float)(1 - fl / FlinchSeconds) : 0f;
            m.SetFacing(flinch > 0f ? facing * Quaternion.Euler((float)(-FlinchDeg * flinch), 0f, 0f) : facing);

            _idleT += dSim;
            double rate = Math.Max(0.4, Math.Min(2.2, speed / Math.Max(1e-3, me.WalkSpeed * s)));
            _prevWalkT = _walkT;
            _walkT += dSim * rate;

            m.BeginPose();
            float walkW = 0f;
            double since = simTime - slamAt, strike = simTime - _strikeAt;
            float slamLen = m.ClipLength(ArtMapping.Slam), berserkLen = m.ClipLength(ArtMapping.Berserk);
            const double lead = 2.0 / 30.0;
            if (k.KoIn > 0) m.Pose(ArtMapping.KO, cfg.Kaiju.KoSeconds - k.KoIn, 1f, false);
            else if (telegraph)
                m.Pose(ArtMapping.SlamWindup, (cfg.Kaiju.SlamTelegraphSeconds - k.TelegraphIn) / cfg.Kaiju.SlamTelegraphSeconds * m.ClipLength(ArtMapping.SlamWindup), 1f, false);
            else if (since >= 0 && SlamTime(since, me.SlamContact, lead) < slamLen - 1e-3)
                m.Pose(ArtMapping.Slam, SlamTime(since, me.SlamContact, lead), 1f, false);
            else if (k.MeleeWindupIn > 0)
            {
                string wind = m.HasClip(ArtMapping.SwipeWindup) ? ArtMapping.SwipeWindup : ArtMapping.Roar;
                double u = (cfg.Kaiju.MeleeWindupTicks - k.MeleeWindupIn) / Math.Max(1, cfg.Kaiju.MeleeWindupTicks);
                m.Pose(wind, u * m.ClipLength(wind), 1f, false);
            }
            else if (strike >= 0 && strike < StrikeSeconds && StrikeClip(m, me, out string sc, out double c))
            {
                double t = Math.Max(0, c - lead) + strike;
                float wgt = (float)Math.Min(1, (StrikeSeconds - strike) / 0.15);
                m.Pose(sc, Math.Min(t, m.ClipLength(sc) - 1e-3), wgt, false);
                walkW = Locomotion.Smooth(0.15, 1.0, speed) * (1f - wgt);
                m.Pose(ArtMapping.Idle, _idleT, 1f - wgt - walkW, true);
                m.Pose(ArtMapping.Walk, _walkT, walkW, true);
            }
            else if (simTime - berserkAt < berserkLen) m.Pose(ArtMapping.Berserk, simTime - berserkAt, 1f, false);
            else
            {
                walkW = Locomotion.Smooth(0.15, 1.0, speed);
                m.Pose(ArtMapping.Idle, _idleT, 1f - walkW, true);
                m.Pose(ArtMapping.Walk, _walkT, walkW, true);
            }
            if (flinch > 0f && m.HasClip(ArtMapping.Flinch)) m.Pose(ArtMapping.Flinch, fl, flinch * 0.6f, false);
            m.Evaluate();

            // Footfalls on the measured contacts of the Walk clip.
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
            Transform fl = m.Bone("Foot_L"), fr = m.Bone("Foot_R");
            float len = m.ClipLength(ArtMapping.Walk);
            if (fl != null && fr != null && len > 0f)
            {
                const int n = 48;
                var y = new float[2, n + 1];
                var p = new Vector3[2, n + 1];
                for (int k = 0; k <= n; k++)
                {
                    m.BeginPose();
                    m.Pose(ArtMapping.Walk, len * k / n, 1f, true);
                    if (!m.Evaluate(true)) { _measured.Remove(m); return r; }
                    p[0, k] = m.Transform.InverseTransformPoint(fl.position);
                    p[1, k] = m.Transform.InverseTransformPoint(fr.position);
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
                    float min = float.PositiveInfinity;
                    for (int k = 0; k <= n; k++) min = Mathf.Min(min, y[f, k]);
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
                double sp = speedN > 0 ? speedSum / speedN : 0;
                if (sp > 0.2 && sp < 10) r.WalkSpeed = sp;
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
            Transform hand = m.Bone("Forearm_R") ?? m.Bone("Arm_R");
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
                    if (v > best) { best = v; bestT = len * k / n; }
                }
                prev = q;
            }
            return bestT;
        }
    }
}
