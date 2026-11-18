using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>
    /// b15 world look, item 3 (WORLD-AUDIT "no post volume"): a restrained URP post volume made in code (no asset), per
    /// quality level (WorldLookBudgets.For): a colour grade per time of day (small: the value structure lives in the
    /// shaders' time-of-day grade) and, on HDR levels only, a mild bloom whose threshold sits above every lit surface so
    /// only self-lit glows (crystals, braziers, the egg seam at night) bloom (art direction 8.20: bloom no wider than 4 px;
    /// no blur, no vignette, no tone mapping change). Very Low and Low: the camera keeps post off (zero cost, as before).
    /// The grade eases toward the target over the director's blend time. Visual only.
    /// </summary>
    [DefaultExecutionOrder(910)]
    [GameOverlay(Order = 78, Scope = OverlayScope.Always)]
    public sealed class WorldPost : MonoBehaviour
    {
        /// <summary>One time of day's grade (URP ColorAdjustments units).</summary>
        public readonly struct Grade
        {
            public readonly float Exposure, Contrast, Saturation;
            public readonly Color Filter;
            public Grade(float exposure, float contrast, float saturation, Color filter) { Exposure = exposure; Contrast = contrast; Saturation = saturation; Filter = filter; }

            public static Grade Lerp(in Grade a, in Grade b, float t) =>
                new Grade(Mathf.Lerp(a.Exposure, b.Exposure, t), Mathf.Lerp(a.Contrast, b.Contrast, t), Mathf.Lerp(a.Saturation, b.Saturation, t), Color.Lerp(a.Filter, b.Filter, t));
        }

        /// <summary>Day: a touch of contrast and colour. Dusk: warm. Night: cool, a little more contrast, less colour in the
        /// world (the fighters' own lift keeps them saturated).</summary>
        public static Grade For(TimeOfDay t) =>
            t == TimeOfDay.Night ? new Grade(0f, 8f, -6f, new Color(0.93f, 0.97f, 1.06f, 1f))
            : t == TimeOfDay.Dusk ? new Grade(0f, 6f, 4f, new Color(1.04f, 0.98f, 0.93f, 1f))
            : new Grade(0f, 6f, 6f, Color.white);

        public static WorldPost Current { get; private set; }
        public bool PostOn { get; private set; }
        public bool BloomOn { get; private set; }

        Volume _volume;
        VolumeProfile _profile;
        ColorAdjustments _colour;
        Bloom _bloom;
        Grade _shown;
        bool _hasShown;
        UnityEngine.Camera _cam;

        void OnEnable()
        {
            Current = this;
            var go = new GameObject("World Post Volume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "World Post (runtime)";
            _colour = _profile.Add<ColorAdjustments>(true);
            _bloom = _profile.Add<Bloom>(true);
            _bloom.threshold.Override(WorldLookBudgets.BloomThreshold);
            _bloom.intensity.Override(WorldLookBudgets.BloomIntensity);
            _bloom.scatter.Override(WorldLookBudgets.BloomScatter);
            _bloom.highQualityFiltering.Override(false);
            // Nothing else overridden: no vignette, no tone mapping, no blur (art direction 8.20).
            _volume.sharedProfile = _profile;
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
            if (_cam != null) SetCameraPost(_cam, false);
            if (_volume != null) Destroy(_volume.gameObject);
            if (_profile != null) Destroy(_profile);
            _volume = null;
            _profile = null;
        }

        static void SetCameraPost(UnityEngine.Camera cam, bool on)
        {
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null && data.renderPostProcessing != on) data.renderPostProcessing = on;
        }

        void LateUpdate()
        {
            WorldLookBudgets.AirTier tier = WorldLookBudgets.For(RenderTier.Level);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != _cam && _cam != null) SetCameraPost(_cam, false);
            _cam = cam;
            bool hdr = cam != null && cam.allowHDR && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u && u.supportsHDR;
            PostOn = tier.Post && cam != null;
            BloomOn = PostOn && tier.Bloom && hdr;
            if (cam != null) SetCameraPost(cam, PostOn);
            if (_volume == null) return;
            _volume.enabled = PostOn;
            if (!PostOn) return;
            _bloom.active = BloomOn;
            TimeOfDayDirector dir = TimeOfDayDirector.Current;
            Grade target = For(dir != null ? dir.Target : TimeOfDay.Day);
            float k = _hasShown ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / (TimeOfDayDirector.BlendSeconds * 0.4f)) : 1f;
            _shown = Grade.Lerp(_shown, target, k);
            _hasShown = true;
            _colour.postExposure.Override(_shown.Exposure);
            _colour.contrast.Override(_shown.Contrast);
            _colour.saturation.Override(_shown.Saturation);
            _colour.colorFilter.Override(_shown.Filter);
        }
    }
}
