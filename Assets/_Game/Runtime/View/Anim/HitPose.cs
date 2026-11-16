using System;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// fix/hit-feel-5: the strike and the reaction on the UPPER body only (spine, chest, head; CharacterSprings writes
    /// them with its lag, after the pose). Roots and legs never move here, so the placement (0.35 u), upright (25 degrees
    /// on the model's up), ring and facing tests are untouched; the head stays far above the hips.
    /// <para>Strike: the spine and chest lean toward the target around the damage tick, so the weapon tip reaches the body
    /// surface where the step-in (capped 0.25 u) is not enough. Reaction: the upper body recoils along the blow and the
    /// head snaps, fast out (50 ms) and eased back (by 0.3 s), for every class and every tier, damage threshold or not.</para>
    /// </summary>
    public static class HitPose
    {
        public const double MaxStrikeLeanDeg = 18, MinLever = 0.25;
        public const float SpineShare = 0.45f, ChestShare = 0.55f, StrikeSpineShare = 0.6f, StrikeChestShare = 0.4f;
        public const double RecoilOut = 0.05, RecoilBack = 0.3, SquashSeconds = 0.08, SquashMax = 0.07;

        /// <summary>Lean (degrees) that carries a tip `lever` u above the spine pivot `gap` u further forward (0..18).</summary>
        public static double StrikeLeanDeg(double gap, double lever)
        {
            if (!(gap > 0)) return 0;
            double s = Math.Min(1, gap / Math.Max(MinLever, lever));
            return Math.Min(MaxStrikeLeanDeg, Math.Asin(s) * 180 / Math.PI);
        }

        /// <summary>Forward gain (u) of a lean of deg degrees for a tip `lever` u above the pivot (pure; capture and tests).</summary>
        public static double LeanGain(double deg, double lever) => Math.Max(MinLever, lever) * Math.Sin(deg * Math.PI / 180);

        /// <summary>Upper-body recoil (degrees, spine plus chest) per hit tier.</summary>
        public static float RecoilDeg(HitTier tier) => tier == HitTier.Critical ? 24f : tier == HitTier.Heavy ? 19f : 13f;

        /// <summary>Head snap on top of the recoil (degrees) per hit tier.</summary>
        public static float HeadSnapDeg(HitTier tier) => tier == HitTier.Critical ? 26f : tier == HitTier.Heavy ? 20f : 14f;

        /// <summary>Recoil envelope by seconds since the hit: out over RecoilOut, eased back by RecoilBack (0..1).</summary>
        public static float RecoilEnv(double t)
        {
            if (!(t >= 0) || t >= RecoilBack) return 0f;
            if (t < RecoilOut) { double s = t / RecoilOut; return (float)(s * (2 - s)); }
            double u = (t - RecoilOut) / (RecoilBack - RecoilOut);
            return (float)(1 - u * u * (3 - 2 * u));
        }

        /// <summary>Squash share along the blow by seconds since the hit (0 to SquashMax and back over SquashSeconds).</summary>
        public static float Squash(double t) =>
            !(t >= 0) || t >= SquashSeconds ? 0f : (float)(SquashMax * Math.Sin(Math.PI * t / SquashSeconds));

        /// <summary>
        /// Extra world rotations for spine, chest and head this frame (identity when nothing applies). scale: the motion
        /// scale (reduced motion quarters it).
        /// </summary>
        public static void Extra(in ActorPoseState s, float scale, out Quaternion spine, out Quaternion chest, out Quaternion head)
        {
            spine = chest = head = Quaternion.identity;
            if (s.Corpse || s.IsKaiju) return;
            Vector3 d = new Vector3(s.HitDir.x, 0f, s.HitDir.z);
            float env = RecoilEnv(s.HitAge);
            if (env > 0f && d.sqrMagnitude > 1e-6f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, d.normalized);   // tips the top along the blow
                float r = RecoilDeg(s.HitTier) * env * scale;
                spine = Quaternion.AngleAxis(r * SpineShare, axis);
                chest = Quaternion.AngleAxis(r * ChestShare, axis);
                head = Quaternion.AngleAxis(HeadSnapDeg(s.HitTier) * env * scale, axis);
            }
            Vector3 f = new Vector3(s.StrikeDir.x, 0f, s.StrikeDir.z);
            if (s.StrikeLeanDeg > 0.01f && f.sqrMagnitude > 1e-6f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, f.normalized);
                float l = s.StrikeLeanDeg * scale;
                spine = Quaternion.AngleAxis(l * StrikeSpineShare, axis) * spine;
                chest = Quaternion.AngleAxis(l * StrikeChestShare, axis) * chest;
            }
        }

        /// <summary>
        /// The weapon tip in the weapon hand's local space: a light-weapon prop under the hand (its own readable mesh), else
        /// the WeaponTip measured at import, else the hand itself (no weapon). Allocates (vertex read): once per fighter.
        /// </summary>
        public static Vector3 TipLocal(ModelView m, out Transform hand, out string source) => Ends(m, out hand, out _, out source);

        /// <summary>
        /// b11-c: the ends of the weapon class cls really strikes with. Every class: Ends (the weapon hand, handslot.r). The
        /// Healer: a lantern PROP on the weapon hand when one is attached (a new Healer model); otherwise her own lantern in
        /// the LEFT hand (handslot.l / hand.l, measured at import: WeaponTip.Left*), else that bare left hand. Allocates
        /// (vertex read, strings): once per fighter and clip, like Ends.
        /// </summary>
        public static Vector3 StrikeEnds(ModelView m, ClassId cls, out Transform hand, out Vector3 other, out string source)
        {
            Vector3 r = Ends(m, out hand, out other, out source);
            if (cls != ClassId.Healer || m == null || source.StartsWith("prop", StringComparison.Ordinal)) return r;
            Transform left = m.Bone("handslot.l") ?? m.Bone("hand.l");
            if (left == null) return r;
            hand = left;
            other = Vector3.zero;
            source = "left hand " + left.name + " (no lantern measured)";
            WeaponTip tip = m.Root != null ? m.Root.GetComponentInChildren<WeaponTip>(true) : null;
            if (tip != null && tip.LeftDistance > 0f && !string.IsNullOrEmpty(tip.LeftBone))
            {
                Transform b = m.Bone(tip.LeftBone);
                if (b != null)
                {
                    source = "import (" + tip.LeftVertices + " lantern vertices on " + tip.LeftBone + ")";
                    if (tip.LeftOtherDistance > 0f) other = left.InverseTransformPoint(b.TransformPoint(tip.LeftOtherEnd));
                    return left.InverseTransformPoint(b.TransformPoint(tip.LeftLocal));
                }
            }
            return Vector3.zero;
        }

        /// <summary>
        /// fix/hit-feel-7: both far ends of the held weapon in the hand's local space: the return value is the farthest
        /// point from the hand, `other` the farthest on the opposite side of the hand (Vector3.zero: none). Which one
        /// strikes is decided on the contact pose (the one further forward: ActorAnimator.MeasureReach).
        /// </summary>
        public static Vector3 Ends(ModelView m, out Transform hand, out Vector3 other, out string source)
        {
            other = Vector3.zero;
            source = "hand (no weapon measured)";
            hand = m != null ? (m.Bone(WeaponProps.HandBone) ?? m.Bone(WeaponProps.HandBoneFallback)) : null;
            if (hand == null) return Vector3.zero;
            Vector3 best = Vector3.zero;
            float bestD = -1f;
            for (int c = 0; c < hand.childCount; c++)
            {
                Transform prop = hand.GetChild(c);
                if (!prop.gameObject.activeInHierarchy || !prop.name.StartsWith(WeaponProps.PropName, StringComparison.Ordinal)) continue;
                foreach (MeshFilter mf in prop.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    Vector3[] v = mesh.vertices;
                    for (int k = 0; k < v.Length; k++)
                    {
                        Vector3 p = hand.InverseTransformPoint(mf.transform.TransformPoint(v[k]));
                        float dd = p.sqrMagnitude;
                        if (dd > bestD) { bestD = dd; best = p; }
                    }
                }
                if (bestD >= 0f)
                {
                    source = "prop " + prop.name;
                    float od = 0.04f;   // a prop's other end counts past 0.2 u from the hand
                    Vector3 ax = best.normalized;
                    foreach (MeshFilter mf in prop.GetComponentsInChildren<MeshFilter>())
                    {
                        Mesh mesh = mf.sharedMesh;
                        if (mesh == null || !mesh.isReadable) continue;
                        Vector3[] v = mesh.vertices;
                        for (int k = 0; k < v.Length; k++)
                        {
                            Vector3 p = hand.InverseTransformPoint(mf.transform.TransformPoint(v[k]));
                            if (Vector3.Dot(p, ax) < 0f && p.sqrMagnitude > od) { od = p.sqrMagnitude; other = p; }
                        }
                    }
                    return best;
                }
            }
            WeaponTip tip = m.Root != null ? m.Root.GetComponentInChildren<WeaponTip>(true) : null;
            if (tip != null && tip.Distance > 0f)
            {
                Transform b = m.Bone(tip.Bone);
                if (b != null)
                {
                    source = "import (" + tip.Vertices + " weapon vertices on " + tip.Bone + ")";
                    if (tip.OtherDistance > 0f) other = hand.InverseTransformPoint(b.TransformPoint(tip.OtherEnd));
                    return hand.InverseTransformPoint(b.TransformPoint(tip.Local));
                }
            }
            return Vector3.zero;
        }
    }
}
