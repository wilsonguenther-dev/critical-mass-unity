using System;
using UnityEngine;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// Gameplay sanity suite (test/gameplay-sanity): reads where a rigged model visibly points from its own bones, so a
    /// test can compare it with the sim's facing. Twin of CriticalMass.Runtime.Tests.SanityRig (the two test
    /// assemblies cannot reference each other); keep them identical.
    /// <para>Cues, in order: the KayKit 41-bone character rig (Warrior, Miner, Farmer) has toes.l / toes.r ahead of
    /// foot.l / foot.r (the ankles), which holds in every locomotion pose; the 20-bone Kaiju rig has its Head ahead of the
    /// tail tip (Tail3). The side cue (right hip minus left hip, crossed with up) is a second opinion for the messages.</para>
    /// </summary>
    internal static class SanityRig
    {
        /// <summary>The first transform named exactly name under root (depth first), or null.</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = Find(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The model's visual front as a horizontal world direction (unit), and which cue measured it.</summary>
        public static bool TryFront(Transform root, out Vector3 front, out string cue)
        {
            Transform tl = Find(root, "toes.l"), tr = Find(root, "toes.r"), fl = Find(root, "foot.l"), fr = Find(root, "foot.r");
            if (tl != null && tr != null && fl != null && fr != null)
            {
                // The planted foot only: in a run cycle the trailing foot kicks up behind with its sole to the sky, and its
                // toes then point backwards; summed with an airborne front foot that read a correct model as turned around
                // in about one sampled frame in nine (integration batch 1).
                // fix/facing-frame-timing: the planted foot is the one with the lower ANKLE, not the lower toe. Just after
                // toe-off the trailing foot's heel is up but its toes still hang lowest (5 mm below the landing foot's in the
                // GitHub reads), so the lower-toe rule read that foot, toes backwards: the Farmer "144 degrees, turned
                // around" at run phase 0.27 with its root, its hip line and its other foot on the facing. Forward kinematics
                // on the KayKit rig (tools/art/locomotion_blend_fk.py clips, 200 phases each): the lower-toe rule misreads
                // 1 to 6 % of Run reversed, the strafes reversed, Walk and Walking_Backwards, all as turned around; the
                // lower-ankle rule none. A model drawn turned around still reads turned around (its planted toes point back).
                Vector3 d = Flat(fl.position.y <= fr.position.y ? tl.position - fl.position : tr.position - fr.position);
                if (d.sqrMagnitude > 1e-6f)
                {
                    front = d.normalized;
                    cue = "toes ahead of the planted (lower) ankle";
                    return true;
                }
            }
            Transform head = Find(root, "Head");
            Transform tail = Find(root, "Tail3");
            if (tail == null) tail = Find(root, "Tail2");
            if (tail == null) tail = Find(root, "Tail1");
            if (head != null && tail != null)
            {
                Vector3 d = Flat(head.position - tail.position);
                if (d.sqrMagnitude > 1e-6f)
                {
                    front = d.normalized;
                    cue = "head ahead of the tail";
                    return true;
                }
            }
            front = Vector3.zero;
            cue = "no front cue (no toes/foot or Head/Tail bones)";
            return false;
        }

        /// <summary>Second opinion: (right hip - left hip) x up. False when the rig has no hip pair.</summary>
        public static bool TrySideFront(Transform root, out Vector3 front)
        {
            Transform l = Find(root, "upperleg.l"), r = Find(root, "upperleg.r");
            if (l == null || r == null)
            {
                l = Find(root, "Thigh_L");
                r = Find(root, "Thigh_R");
            }
            front = Vector3.zero;
            if (l == null || r == null) return false;
            Vector3 right = Flat(r.position - l.position);
            if (right.sqrMagnitude < 1e-6f) return false;
            front = Vector3.Cross(right.normalized, Vector3.up).normalized;
            return true;
        }

        /// <summary>Unsigned horizontal angle between two directions, degrees.</summary>
        public static float AngleDeg(Vector3 a, Vector3 b) => Vector3.Angle(Flat(a), Flat(b));

        /// <summary>A plain-English reading of a facing error.</summary>
        public static string Verdict(float angle) =>
            angle >= 135f ? "turned around: it moves BACKWARDS" : angle >= 45f ? "turned sideways" : "slightly off";

        /// <summary>The Unity world direction of a sim facing (radians, sim (cos f, sin f)), through SimSpace.</summary>
        public static Vector3 SimDir(double facing) => SimSpace.ToWorld(Math.Cos(facing), 0, Math.Sin(facing)).normalized;
    }
}
