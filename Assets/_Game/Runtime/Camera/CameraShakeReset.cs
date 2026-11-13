using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// Removes last frame's camera shake before anything reads the camera this frame (input aim at order -100, the sim
    /// driver at 0), so the shake is only ever seen in the rendered picture.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CameraShakeReset : MonoBehaviour
    {
        void Update() => CameraShake.Reset();
    }
}
