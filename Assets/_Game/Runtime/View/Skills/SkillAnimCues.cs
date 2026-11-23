using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using UnityEngine;

namespace CriticalMass.Runtime.View.Skills
{
    /// <summary>
    /// Per-actor skill animation cues (view only). The sim resets AnimAction for TNT and Purify without the cooldown
    /// ActorAnimator watches, so those skills played no clip (audit 1, Part 1). SkillFxCore notes a cue on the skill's
    /// event tick; ActorAnimator starts the cue's clip on the upper layer; SkillPoseOverlay adds the procedural arm.
    /// Fixed arrays, no allocation.
    /// </summary>
    public static class SkillAnimCues
    {
        public const int MaxActors = 128;
        /// <summary>A cue is fresh for this many ticks after its event (the animator sees it on the next drawn frame).</summary>
        public const int FreshTicks = 6;
        public const byte ProcNone = 0, ProcThrow = 1, ProcRaise = 2;
        /// <summary>Sim seconds the procedural overlay runs.</summary>
        public const double ProcSeconds = 0.75;

        static readonly int[] _tick = NewTicks();
        static readonly double[] _t0 = new double[MaxActors];
        static readonly string[] _clip = new string[MaxActors];
        static readonly byte[] _proc = new byte[MaxActors];

        static int[] NewTicks()
        {
            var t = new int[MaxActors];
            for (int i = 0; i < t.Length; i++) t[i] = int.MinValue;
            return t;
        }

        public static void Note(int actor, int tick, double simTime, string clip, byte proc)
        {
            if (actor < 0 || actor >= MaxActors) return;
            _tick[actor] = tick;
            _t0[actor] = simTime;
            _clip[actor] = clip;
            _proc[actor] = proc;
        }

        /// <summary>The cue clip for actor at tick, or null when there is no fresh cue.</summary>
        public static string FreshClip(int actor, int tick)
        {
            if (actor < 0 || actor >= MaxActors) return null;
            int d = tick - _tick[actor];
            return d >= 0 && d <= FreshTicks ? _clip[actor] : null;
        }

        /// <summary>The running procedural overlay for actor at sim time now: its kind and age in sim seconds.</summary>
        public static bool Proc(int actor, double now, out byte kind, out float age)
        {
            kind = ProcNone;
            age = 0f;
            if (actor < 0 || actor >= MaxActors || _proc[actor] == ProcNone || _tick[actor] == int.MinValue) return false;
            double a = now - _t0[actor];
            // fix/px-view-2: the TNT / Root Seeds throw arm is released on the throw timing (HitFeelTuning.ThrowPose: full 3
            // frames, gone by 8), not held for the 0.75 s of the raise overlay
            if (a < 0 || a > (_proc[actor] == ProcThrow ? HitFeelTuning.ThrowPoseSeconds : ProcSeconds)) return false;
            kind = _proc[actor];
            age = (float)a;
            return true;
        }

        public static void Clear()
        {
            for (int i = 0; i < MaxActors; i++) { _tick[i] = int.MinValue; _clip[i] = null; _proc[i] = ProcNone; }
        }

        /// <summary>0..1 weight of the overlay: up in 0.12 s, held, down by ProcSeconds.</summary>
        public static float Envelope(float age)
        {
            if (age < 0f) return 0f;
            if (age < 0.12f) return age / 0.12f;
            if (age < 0.45f) return 1f;
            return Mathf.Clamp01(1f - (age - 0.45f) / (float)(ProcSeconds - 0.45));
        }
    }

    /// <summary>
    /// The procedural arm on the existing KayKit rig (C3 post-pose hook, after the clips): a throw swing on the right upper
    /// arm for Root Seeds and TNT when the model has no Throw clip, and the right arm raised overhead for Purify (on top
    /// of Cast, which is a forward shot, not a raised arm). Rotates about the model's own right axis, so it does not depend
    /// on the bone's local axes. One cached bone per actor; FindJoint runs once per model.
    /// </summary>
    public sealed class SkillPoseOverlay : IPostPoseListener
    {
        public const string ArmBone = "upperarm.r";
        readonly Transform[] _root = new Transform[SkillAnimCues.MaxActors];
        readonly Transform[] _arm = new Transform[SkillAnimCues.MaxActors];

        /// <summary>fix/px-view-2 (tests): the weight of the last procedural throw arm drawn.</summary>
        public static float LastThrowWeight;
        public static int LastThrowFrame = -1;

        /// <summary>Overlays applied since start (capture log).</summary>
        public int Applied { get; private set; }

        public void OnPostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            if (mv == null || actor < 0 || actor >= SkillAnimCues.MaxActors) return;
            if (!SkillAnimCues.Proc(actor, s.SimTime, out byte kind, out float age)) return;
            if (kind == SkillAnimCues.ProcThrow && mv.HasClip(ArtMapping.Throw)) return;   // the real clip already throws
            Transform root = mv.Transform;
            if (root == null) return;
            if (_root[actor] != root)
            {
                _root[actor] = root;
                _arm[actor] = PostPoseHub.FindJoint(root, ArmBone);
            }
            Transform arm = _arm[actor];
            if (arm == null) return;
            float w = SkillAnimCues.Envelope(age);
            float angle;
            if (kind == SkillAnimCues.ProcRaise) angle = -140f * w;
            else
            {
                // fix/px-view-2: the cue is noted when the TNT leaves the hand, so the overlay is the release: the arm sweeps
                // forward 110 degrees over the hold (3 frames) and eases back to the clip pose by 8 frames (ThrowPose)
                HitFeelTuning.ThrowPose(age, 1.0, out _, out w);
                float k = Mathf.Clamp01(age / (float)HitFeelTuning.ThrowHoldSeconds);
                angle = Mathf.Lerp(35f, -110f, k) * w;
            }
            if (kind == SkillAnimCues.ProcThrow) { LastThrowWeight = w; LastThrowFrame = Time.frameCount; }
            arm.rotation = Quaternion.AngleAxis(angle, root.right) * arm.rotation;
            Applied++;
        }
    }
}
