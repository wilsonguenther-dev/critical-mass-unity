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
    }
}
