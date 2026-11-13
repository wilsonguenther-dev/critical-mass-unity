using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Reactions;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>A damped 2D spring (semi-implicit Euler in fixed 1/120 s substeps, so any frame rate is stable).</summary>
    public struct LagSpring2
    {
        public Vector2 X, V;

        public void Step(Vector2 target, float omega, float zeta, float dt)
        {
            if (dt <= 0f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 16);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                V += (-2f * zeta * omega * V - omega * omega * (X - target)) * h;
                X += V * h;
            }
        }
    }

    /// <summary>A damped 1D spring (same integrator as LagSpring2).</summary>
    public struct LagSpring1
    {
        public float X, V;

        public void Step(float target, float omega, float zeta, float dt)
        {
            if (dt <= 0f) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 16);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                V += (-2f * zeta * omega * V - omega * omega * (X - target)) * h;
                X += V * h;
            }
        }
    }

    /// <summary>
    /// Fighter body lag (audit finding 4): a critically damped spring on the root's horizontal acceleration. Output is a
    /// tilt in degrees as a world xz vector (the top of the body leans toward it): it leans back on a start, forward on a
    /// stop, and settles in about 0.2 s. Spine, chest and head take 0.8, 0.6 and 1.0 of it, capped at 8, 6 and 10 degrees.
    /// </summary>
    public sealed class FighterLag
    {
        public const float Omega = 22f, Zeta = 1f, GainDegPerAccel = 0.2f, MaxDeg = 10f;
        private Vector3 _prevPos, _prevVel;
        private bool _has, _hasVel;
        public LagSpring2 Tilt;

        public void Reset() { _has = false; _hasVel = false; Tilt = default; }

        /// <summary>One frame: the root's world position after the pose; returns the tilt (deg, world xz).</summary>
        public Vector2 Step(Vector3 rootPos, float dt)
        {
            if (dt <= 1e-5f) return Tilt.X;
            Vector2 target = Vector2.zero;
            if (_has)
            {
                Vector3 vel = (rootPos - _prevPos) / dt;
                vel.y = 0f;
                if (vel.sqrMagnitude > 400f) { _hasVel = false; vel = Vector3.zero; } // a teleport (respawn, pool reuse)
                if (_hasVel)
                {
                    Vector3 acc = (vel - _prevVel) / dt;
                    target = new Vector2(-acc.x, -acc.z) * GainDegPerAccel;
                    if (target.magnitude > MaxDeg) target = target.normalized * MaxDeg;
                }
                _prevVel = vel;
                _hasVel = true;
            }
            _prevPos = rootPos;
            _has = true;
            Tilt.Step(target, Omega, Zeta, dt);
            if (Tilt.X.magnitude > MaxDeg) Tilt.X = Tilt.X.normalized * MaxDeg;
            return Tilt.X;
        }
    }

    /// <summary>
    /// Kaiju tail (audit finding 4): Tail1-3 trail the body's yaw rate, each a spring about the up axis with stiffness
    /// falling down the chain (k 60, 42, 25: omega = sqrt(k)), lag 0.04 / 0.08 / 0.12 s of yaw rate, 35 degrees each at
    /// most. The offsets add down the chain (a child inherits its parent), so Tail3's trail is the sum. Neck and head lag
    /// 0.03 s and 0.05 s of yaw rate the other way round (the head leads less than the body turns).
    /// </summary>
    public sealed class KaijuTail
    {
        public static readonly float[] K = { 60f, 42f, 25f, 90f, 70f };
        public static readonly float[] Lag = { 0.04f, 0.08f, 0.12f, 0.03f, 0.05f };
        public const float Zeta = 0.7f, MaxDeg = 35f;
        public readonly LagSpring1[] Joint = new LagSpring1[5];
        private float _prevYaw;
        private bool _has;

        public void Reset() { _has = false; for (int i = 0; i < Joint.Length; i++) Joint[i] = default; }

        /// <summary>One frame with the body's yaw (degrees); fills offsets (deg about up) for Tail1-3, Neck, Head.</summary>
        public void Step(float yawDeg, float dt, float[] offsets)
        {
            float rate = 0f;
            if (_has && dt > 1e-5f) rate = Mathf.DeltaAngle(_prevYaw, yawDeg) / dt;
            if (Mathf.Abs(rate) > 2000f) rate = 0f; // a snap (spawn), not a turn
            _prevYaw = yawDeg;
            _has = true;
            for (int i = 0; i < Joint.Length; i++)
            {
                float target = Mathf.Clamp(-rate * Lag[i], -MaxDeg, MaxDeg);
                Joint[i].Step(target, Mathf.Sqrt(K[i]), Zeta, dt);
                Joint[i].X = Mathf.Clamp(Joint[i].X, -MaxDeg, MaxDeg);
                offsets[i] = Joint[i].X;
            }
        }

        /// <summary>How far Tail3 trails the body now (the chain's summed offsets, degrees).</summary>
        public float Tail3Trail => Mathf.Abs(Joint[0].X + Joint[1].X + Joint[2].X);
    }

    /// <summary>
    /// Procedural spring bones applied after the pose (DG-79), attached through contract C3 (PostPoseHub; DG-71's
    /// IPostPoseListener too). Fighters: FighterLag on spine, chest and head (Medium: on screen only; High: all; Low:
    /// none). Kaiju: KaijuTail on Tail1-3, Neck and Head (every tier). Each offset is an extra world rotation on top of
    /// the clip's pose; a joint the clips do not animate gets its own rest pose back first, so nothing accumulates.
    /// Reduced motion quarters the angles. Allocation only on the first sight of a model (its joint lookup).
    /// Hair, scarf and tabard have no joints on the KayKit rig: CMToon's UV4.y wobble covers them once art paints the weight.
    /// </summary>
    [DefaultExecutionOrder(52)]
    public sealed class CharacterSprings : MonoBehaviour, IPostPoseRootListener, IPostPoseListener
    {
        private static readonly string[] FighterJoints = { "spine", "chest", "head" };
        private static readonly float[] FighterShare = { 0.8f, 0.6f, 1.0f };
        private static readonly float[] FighterMax = { 8f, 6f, 10f };
        private static readonly string[] KaijuJoints = { "Tail1", "Tail2", "Tail3", "Neck", "Head" };

        private sealed class Rig
        {
            public Transform[] Joints;
            public Quaternion[] Rest, Written;
            public bool Kaiju;
            public FighterLag Lag;
            public KaijuTail Tail;
            public Renderer Renderer;
            public int Frame;
        }

        private readonly Dictionary<Transform, Rig> _rigs = new Dictionary<Transform, Rig>();
        private readonly float[] _offsets = new float[5];
        private ReactionBudget _budget;
        private int _budgetFrame = -1000;

        // Integration batch 3: driven by the animation pass's real hook (ActorViews.PostPose -> PostPoseHooks, contract C3).
        private void OnEnable() => PostPoseHooks.Register(this);
        private void OnDisable() => PostPoseHooks.Unregister(this);

        public void OnPostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            // A frame the animation level of detail skipped keeps last frame's bones (springs already on them): never stack.
            if (!s.Evaluated) return;
            if (mv != null) PostPoseRoot(actor, mv.Transform, mv.Entry != null ? mv.Entry.Key : null);
        }

        public void PostPoseRoot(int actor, Transform root, string modelKey)
        {
            if (root == null) return;
            if (Time.frameCount - _budgetFrame > 120) { _budget = ReactionBudget.Current; _budgetFrame = Time.frameCount; }
            bool kaiju = actor == PostPoseHub.KaijuActor || modelKey == ArtMapping.KaijuAdult || modelKey == ArtMapping.KaijuHatchling;
            if (!kaiju && !_budget.FighterSprings) return;
            if (!_rigs.TryGetValue(root, out Rig rig))
            {
                rig = Build(root, kaiju);
                _rigs[root] = rig;
            }
            if (rig == null) return;
            if (!kaiju && !_budget.OffscreenSprings && rig.Renderer != null && !rig.Renderer.isVisible) return;
            float dt = Time.deltaTime;
            if (rig.Frame != Time.frameCount - 1) { rig.Lag?.Reset(); rig.Tail?.Reset(); } // first frame back in use
            rig.Frame = Time.frameCount;
            float scale = ReactionBudget.MotionScale;
            for (int i = 0; i < rig.Joints.Length; i++)
            {
                Transform j = rig.Joints[i];
                if (j == null) continue;
                // Not animated by any clip this frame (still what we wrote): back to its rest pose before the new offset.
                if (j.localRotation == rig.Written[i]) j.localRotation = rig.Rest[i];
                else rig.Rest[i] = j.localRotation;
            }
            if (kaiju)
            {
                rig.Tail.Step(root.eulerAngles.y, dt, _offsets);
                for (int i = 0; i < rig.Joints.Length; i++)
                {
                    Transform j = rig.Joints[i];
                    if (j == null) continue;
                    j.rotation = Quaternion.AngleAxis(_offsets[i] * scale, Vector3.up) * j.rotation;
                    rig.Written[i] = j.localRotation;
                }
                return;
            }
            Vector2 tilt = rig.Lag.Step(root.position, dt) * scale;
            float mag = tilt.magnitude;
            if (mag < 1e-3f)
            {
                for (int i = 0; i < rig.Joints.Length; i++) if (rig.Joints[i] != null) rig.Written[i] = rig.Joints[i].localRotation;
                return;
            }
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(tilt.x / mag, 0f, tilt.y / mag));
            for (int i = 0; i < rig.Joints.Length; i++)
            {
                Transform j = rig.Joints[i];
                if (j == null) continue;
                float a = Mathf.Min(mag * FighterShare[i], FighterMax[i]);
                j.rotation = Quaternion.AngleAxis(a, axis) * j.rotation;
                rig.Written[i] = j.localRotation;
            }
        }

        private static Rig Build(Transform root, bool kaiju)
        {
            string[] names = kaiju ? KaijuJoints : FighterJoints;
            var rig = new Rig
            {
                Kaiju = kaiju,
                Joints = new Transform[names.Length],
                Rest = new Quaternion[names.Length],
                Written = new Quaternion[names.Length],
                Lag = kaiju ? null : new FighterLag(),
                Tail = kaiju ? new KaijuTail() : null,
                Renderer = root.GetComponentInChildren<Renderer>(true),
                Frame = -10,
            };
            int found = 0;
            for (int i = 0; i < names.Length; i++)
            {
                rig.Joints[i] = PostPoseHub.FindJoint(root, names[i]);
                if (rig.Joints[i] == null) continue;
                found++;
                rig.Rest[i] = rig.Joints[i].localRotation;
                rig.Written[i] = new Quaternion(0f, 0f, 0f, 0f); // never equal to a real rotation
            }
            return found > 0 ? rig : null;
        }
    }
}
