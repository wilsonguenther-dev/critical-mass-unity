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

        /// <summary>Share of the SLIDER shake offset applied (ground view sets 0.3: a 0.32 u shake 3 u from the pivot is a lot).
        /// px lane C: the hit kick below is NOT limited by it (it is sized per view already).</summary>
        public static float Limit = 1f;

        // ---- px lane C (2026-11-22): the directional HIT KICK, separate from the accessibility shake slider.
        Vector3 _kickVec;
        float _kickAt = -10f, _kickU;
        /// <summary>The kick offset applied this frame (tests, captures) and the size of the last kick requested (u).</summary>
        public static Vector3 KickApplied => _instance != null ? _instance._kickNow : Vector3.zero;
        public static float LastKickU => _instance != null ? _instance._kickU : 0f;
        Vector3 _kickNow;
        /// <summary>Kicks requested (also counted when reduce motion zeroes them: tests).</summary>
        public static int KickRequests;

        /// <summary>
        /// px lane C: a small directional camera impulse for a hit by me, on me, my kill and my death. Position only (the aim
        /// never moves: CameraShakeReset takes it off before input reads the camera), along the hit direction in the screen
        /// plane, out in 25 ms and back by 150 ms. Sized per view as a share of the screen height at the followed fighter
        /// (HitFeelTuning.KickU), scaled by the HIT KICK setting, zero under REDUCE MOTION, NOT scaled by the shake slider.
        /// Reports CameraKick to the PlayerFeedback ledger (actor = the victim). Returns the size in u (0 = none).
        /// </summary>
        public static float Kick(CriticalMass.Runtime.View.Anim.HitFeelTuning.KickKind kind, Vector3 worldDir, int victim)
        {
            KickRequests++;
            if (_instance == null || FeelGate.ReduceMotion) return 0f;
            float scale = CriticalMass.Runtime.Menu.GameSettings.HitKickScale;
            if (scale <= 0f) return 0f;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return 0f;
            double dist = FocusDistance();
            float u = CriticalMass.Runtime.View.Anim.HitFeelTuning.KickU(kind, dist, cam.fieldOfView, scale);
            float now = Time.unscaledTime;
            float left = _instance._kickU * CriticalMass.Runtime.View.Anim.HitFeelTuning.KickEnvelope(now - _instance._kickAt);
            if (u < left) return 0f;   // a bigger kick is still running
            Transform tr = cam.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(tr.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = tr.up;
            fwd.Normalize();
            float x = Vector3.Dot(worldDir, tr.right), y = Vector3.Dot(worldDir, fwd);
            Vector2 d = new Vector2(x, y);
            if (d.sqrMagnitude < 1e-6f) d = new Vector2(0f, -1f);
            d.Normalize();
            _instance._kickVec = new Vector3(d.x, d.y, 0f);
            _instance._kickU = u;
            _instance._kickAt = now;
            CriticalMass.Runtime.Game.PlayerFeedback.Report(CriticalMass.Runtime.Game.FeedbackChannel.CameraKick, victim, u, _instance);
            return u;
        }

        // ---- critical hits (lead's call 12): a +2 deg FOV punch, taken off again by Reset like the position offset
        float _punchAt = -10f, _punchDeg, _fovApplied;
        UnityEngine.Camera _fovCam;
        /// <summary>The FOV added this frame (tests, captures).</summary>
        public static float FovPunchApplied => _instance != null ? _instance._fovApplied : 0f;

        /// <summary>A critical hit's FOV punch (deg); zero under REDUCE MOTION or HIT KICK 0. Returns the degrees started.</summary>
        public static float FovPunch(float deg)
        {
            if (_instance == null || FeelGate.ReduceMotion || deg <= 0f) return 0f;
            float scale = CriticalMass.Runtime.Menu.GameSettings.HitKickScale;
            if (scale <= 0f) return 0f;
            _instance._punchDeg = deg * Mathf.Min(1f, scale);
            _instance._punchAt = Time.unscaledTime;
            return _instance._punchDeg;
        }

        /// <summary>The followed fighter's distance from the lens the kick is sized at: third person ThirdDistance, else the
        /// top-down rig's distance.</summary>
        public static double FocusDistance() => GroundView.PlayActive ? FollowCamera.ThirdDistance : GameplayCamera.Distance;

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

        /// <summary>feat/hit-feel-2: nudge strength before the player has touched the shake setting (a fresh install).</summary>
        public const float DefaultNudgeScale = 0.7f;

        /// <summary>
        /// feat/hit-feel-2: the scale a hit nudge uses: 0 with reduce motion; the shake setting once the player has set it
        /// (so "off" really is off); DefaultNudgeScale on a fresh install, where full shake stays off (pure; tests).
        /// </summary>
        public static float NudgeScale(bool reduceMotion, bool settingChosen, float shakeScale) =>
            reduceMotion ? 0f : settingChosen ? shakeScale : DefaultNudgeScale;

        /// <summary>feat/hit-feel-2: a small camera nudge for a hit on or by the local player (see NudgeScale).</summary>
        public static void Nudge(float amount)
        {
            if (_instance == null || amount <= 0f) return;
            bool chosen = PlayerPrefs.HasKey(CriticalMass.Runtime.Menu.GameSettings.ShakeScaleKey) || PlayerPrefs.HasKey(CriticalMass.Runtime.Menu.GameSettings.ShakeKey);
            float s = NudgeScale(FeelGate.ReduceMotion, chosen, FeelGate.ShakeScale);
            if (s <= 0f) return;
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
            float now = Time.unscaledTime;
            float ke = _kickU > 0f && !FeelGate.ReduceMotion ? CriticalMass.Runtime.View.Anim.HitFeelTuning.KickEnvelope(now - _kickAt) : 0f;
            if (_trauma > 0f && FeelGate.ShakeScale <= 0f) _trauma = 0f;
            _kickNow = Vector3.zero;
            float pe = _punchDeg > 0f && !FeelGate.ReduceMotion ? CriticalMass.Runtime.View.Anim.HitFeelTuning.FovPunchEnvelope(now - _punchAt) : 0f;
            if (_trauma <= 0f && ke <= 0f && pe <= 0f) return;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;
            if (pe > 0f)
            {
                _fovApplied = _punchDeg * pe;
                cam.fieldOfView += _fovApplied;
                _fovCam = cam;
            }
            float dt = Time.unscaledDeltaTime;
            Transform tr = cam.transform;
            Vector3 off = Vector3.zero;
            if (_trauma > 0f)
            {
                _t += dt;
                Vector2 o = Offset(_trauma, _t) * Limit;   // the slider shake: limited per view
                off += tr.right * o.x + tr.up * o.y;
                _trauma = Mathf.Max(0f, _trauma - dt / DecaySeconds);
            }
            if (ke > 0f)
            {
                // px lane C: the hit kick, its own size (never times Limit), along the hit in the screen plane.
                _kickNow = (tr.right * _kickVec.x + tr.up * _kickVec.y) * (_kickU * ke);
                off += _kickNow;
            }
            tr.position += off;
            _shaken = tr;
            _applied = off;
        }

        /// <summary>Takes this frame's offset off the camera (CameraShakeReset, start of the next frame).</summary>
        public static void Reset()
        {
            if (_instance == null) return;
            if (_instance._fovCam != null) { _instance._fovCam.fieldOfView -= _instance._fovApplied; _instance._fovCam = null; }
            _instance._fovApplied = 0f;
            if (_instance._shaken == null) return;
            _instance._shaken.position -= _instance._applied;
            _instance._shaken = null;
            _instance._applied = Vector3.zero;
        }
    }
}
