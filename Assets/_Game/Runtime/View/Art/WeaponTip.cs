using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// fix/hit-feel-5: where a character's held weapon ends, measured at import (ArtImportPostprocessor.MeasureWeaponTip)
    /// from the skinned vertices bound to the weapon hand: the farthest of them from the hand, in the hand bone's local
    /// space (Bone: "handslot.r" or "hand.r"). The character meshes import without a CPU copy, so the game cannot read the
    /// vertices itself; this is the one measured number it keeps. Distance 0: no weapon on that hand (the Healer).
    /// </summary>
    public sealed class WeaponTip : MonoBehaviour
    {
        public string Bone = "handslot.r";
        public Vector3 Local;
        public float Distance;
        public int Vertices;
        /// <summary>
        /// fix/hit-feel-7: the far point on the OTHER side of the hand (a two-hand tool's handle end or head). The game
        /// strikes with whichever end is further forward at the contact pose (ActorAnimator.MeasureReach). 0: none.
        /// </summary>
        public Vector3 OtherEnd;
        public float OtherDistance;
        /// <summary>
        /// b11-c: the same measure on the LEFT hand ("handslot.l" or "hand.l"): the Healer carries her lantern there, so her
        /// reach is read from it (HitPose.StrikeEnds). LeftDistance 0: nothing held in the left hand.
        /// </summary>
        public string LeftBone = "handslot.l";
        public Vector3 LeftLocal;
        public float LeftDistance;
        public int LeftVertices;
        public Vector3 LeftOtherEnd;
        public float LeftOtherDistance;
    }
}
