using CriticalMass.Runtime.View.Effects;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// The camera shake the "Camera shake" setting controls (physics audit M11, DG-94; the setting was a dead control).
    /// Trauma model: each impulse adds trauma 0..1 times FeelGate.ShakeScale (the setting 0..1, 0 by default and with
    /// reduce motion), the offset is trauma squared times MaxOffset along the camera's right and up, from smooth noise, and
    /// trauma decays to 0 over DecaySeconds of wall time. Runs after CinemachineBrain (LateUpdate order 100) so the brain's
    /// pose for this frame is shaken, and before the HUD (order 200) so world marks follow the shaken view.
    /// CameraShakeReset takes the offset off again at the start of the next frame, so input aim never sees it.
    /// View only: never touches the sim, aim or the follow target.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class CameraShake : MonoBehaviour
    {
        public const float MaxOffset = 0.32f;
        public const float DecaySeconds = 0.4f;
        const float NoiseHz = 22f;

        static CameraShake _instance;
        float _trauma, _t;
        Transform _shaken;
        Vector3 _applied;

        /// <summary>Trauma now (tests) and the offset applied this frame (tests, captures).</summary>
        public static float Trauma => _instance != null ? _instance._trauma : 0f;
        public static Vector3 Applied => _instance != null ? _instance._applied : Vector3.zero;

        /// <summary>Share of the shake offset applied (ground view sets 0.3: a 0.32 u shake 3 u from the pivot is a lot).</summary>
        public static float Limit = 1f;

        void OnEnable() => _instance = this;

        void OnDisable()
        {
            Reset();
            if (_instance == this) _instance = null;
        }

        /// <summary>Adds trauma (0..1 before the setting's scale); nothing when shake is off or reduce motion is on.</summary>
        public static void Impulse(float amount)
        {
            if (_instance == null) return;
            float s = FeelGate.ShakeScale;
            if (s <= 0f || amount <= 0f) return;
            _instance._trauma = Mathf.Min(1f, _instance._trauma + amount * s);
        }

        /// <summary>Offset for a trauma and a noise time (pure; tests): length at most trauma squared x MaxOffset x sqrt 2.</summary>
        public static Vector2 Offset(float trauma, float t)
        {
            float amp = trauma * trauma * MaxOffset;
            float x = (Mathf.PerlinNoise(t * NoiseHz, 0.37f) * 2f - 1f) * amp;
            float y = (Mathf.PerlinNoise(0.71f, t * NoiseHz) * 2f - 1f) * amp;
            return new Vector2(x, y);
        }

        void LateUpdate()
        {
            if (_trauma <= 0f) return;
            if (FeelGate.ShakeScale <= 0f) { _trauma = 0f; return; }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            Vector2 o = Offset(_trauma, _t) * Limit;
            Transform tr = cam.transform;
            Vector3 off = tr.right * o.x + tr.up * o.y;
            tr.position += off;
            _shaken = tr;
            _applied = off;
            _trauma = Mathf.Max(0f, _trauma - dt / DecaySeconds);
        }

        /// <summary>Takes this frame's offset off the camera (CameraShakeReset, start of the next frame).</summary>
        public static void Reset()
        {
            if (_instance == null || _instance._shaken == null) return;
            _instance._shaken.position -= _instance._applied;
            _instance._shaken = null;
            _instance._applied = Vector3.zero;
        }
    }
}
