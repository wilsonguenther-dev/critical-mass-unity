using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using UnityEngine;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// PR #44's animation leftovers that need this lane's events, through the C3 post-pose hook (no edit to ActorViews or
    /// ModelView): the knock-down and get-up on a stun (DG-70 HitStun when the sim sends it; a short stagger lean on a slam
    /// hit before that), the feed throw on a delivery, and the local player's four emotes. Root tilts are re-applied after
    /// SetFacing every frame (ActorAnimator rewrites the rotation each frame, so nothing accumulates); bone turns are applied
    /// only on frames the animator evaluated (it rewrites the bones only then). View only: the sim never sees any of it.
    /// </summary>
    public sealed class MechanicsPoses : MonoBehaviour, IPostPoseListener
    {
        /// <summary>The emote the local player is playing (-1 none) and when it started (realtime seconds); set by EmoteWheel.</summary>
        public static int Emote = -1;
        public static float EmoteAt = -99f;

        /// <summary>Starts emote k (0..3) on the local player now.</summary>
        public static void Play(int k)
        {
            if (k < 0 || k >= Emotes.Count) return;
            Emote = k;
            EmoteAt = Time.realtimeSinceStartup;
        }

        /// <summary>The playing emote's envelope 0..1 (0 when none).</summary>
        public static float EmoteEnvelope() => Emote < 0 ? 0f : Emotes.Envelope(Time.realtimeSinceStartup - EmoteAt);

        MechanicsWorldView _view;

        void OnEnable() => PostPoseHooks.Register(this);
        void OnDisable() => PostPoseHooks.Unregister(this);

        public void OnPostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            if (actor < 0 || s.IsKaiju || s.Corpse || mv == null) return;
            if (_view == null) _view = MechanicsWorldView.Active;
            if (_view == null) return;
            MechFeed f = _view.Feed;
            double now = _view.SimNow;
            Transform root = mv.Transform;
            Vector3 fwd = Quaternion.Euler(0f, s.Yaw, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // knock-down / stagger: fall away from the blow, hold, get up
            float tilt = f.StunTiltDeg(actor, now);
            if (tilt > 0.01f && actor < f.StunDirX.Length)
            {
                Vector3 dir = SimSpace.ToWorld(f.StunDirX[actor], 0, f.StunDirZ[actor]);
                Vector3 axis = Vector3.Cross(Vector3.up, dir.sqrMagnitude > 1e-6f ? dir.normalized : -fwd);
                root.rotation = Quaternion.AngleAxis(tilt, axis) * root.rotation;
                return;   // a downed body plays nothing else
            }

            // the feed throw: lean back then lunge, and the right arm swings up and forward
            if (actor < f.ThrowAt.Length)
            {
                float k = KnockPose.Throw01(f.Wall(now - f.ThrowAt[actor]));
                if (k > 0f)
                {
                    root.rotation = Quaternion.AngleAxis(10f * k, right) * root.rotation;
                    if (s.Evaluated) TurnBone(mv, "upperarm.r", "UpperArm.R", right, -150f * k);
                }
            }

            // emotes: the local player only
            if (actor != _view.Feed.LocalActor || Emote < 0) return;
            float env = EmoteEnvelope();
            if (env <= 0f) { Emote = -1; return; }
            float t = Time.realtimeSinceStartup - EmoteAt;
            switch (Emote)
            {
                case 0:   // cheer: both arms up
                    if (s.Evaluated)
                    {
                        TurnBone(mv, "upperarm.r", "UpperArm.R", right, -165f * env);
                        TurnBone(mv, "upperarm.l", "UpperArm.L", right, -165f * env);
                    }
                    break;
                case 1:   // wave: the right arm up, waving side to side
                    if (s.Evaluated)
                    {
                        TurnBone(mv, "upperarm.r", "UpperArm.R", right, -150f * env);
                        TurnBone(mv, "lowerarm.r", "LowerArm.R", fwd, 28f * env * Mathf.Sin(t * Mathf.PI * 2f * 2.5f));
                    }
                    break;
                case 2:   // taunt: a forward chest pump
                    root.rotation = Quaternion.AngleAxis(12f * env * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 2f)), right) * root.rotation;
                    break;
                case 3:   // bow
                    root.rotation = Quaternion.AngleAxis(32f * env, right) * root.rotation;
                    break;
            }
        }

        static void TurnBone(ModelView mv, string name, string alt, Vector3 axis, float deg)
        {
            Transform b = mv.Bone(name) ?? mv.Bone(alt);
            if (b == null || Mathf.Abs(deg) < 0.01f) return;
            b.rotation = Quaternion.AngleAxis(deg, axis) * b.rotation;
        }
    }
}
