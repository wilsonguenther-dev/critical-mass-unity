using UnityEngine;

namespace CriticalMass.Runtime.Hud.Mechanics
{
    /// <summary>
    /// Fix round 6 (lane C): the name tags' late position pass. DepthHud decides which tags show at the default order; this
    /// re-projects their positions after CameraShake (150) has added the hit kick and after GameHud (200) has placed the
    /// health bars, so a name never parts from its bar on an impact frame. View only.
    /// </summary>
    [DefaultExecutionOrder(DepthHud.LateOrder)]
    public sealed class DepthHudTagsLate : MonoBehaviour
    {
        public DepthHud Hud;

        void LateUpdate()
        {
            if (Hud != null && Hud.isActiveAndEnabled) Hud.ReprojectTags();
        }
    }
}
