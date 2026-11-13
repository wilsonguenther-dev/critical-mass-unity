using System;
using CriticalMass.Runtime.Game;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// perf-title-start: the idle title no longer simulates and draws a live 16v16 match behind the logo (49.7% of a core on
    /// the 8 GB M1 Air at 20 fps; the budget is 35%). Once the backdrop has built and settled, the main camera renders it ONE
    /// more time into a texture (title canvas off for that render, so the logo is not baked in), the still is shown as the
    /// title canvas's back layer with a slow drift (scale and pan, a Ken Burns move, so the screen stays alive), the camera
    /// then draws only the UI layer, and the backdrop's sim is paused (TimeScale 0; its fighters are not visible to any camera,
    /// so their poses stop evaluating too). Anything that is not the idle title (attract tour, story film, the title
    /// dismissed or destroyed, a new screen size) puts the live scene back exactly as it was.
    /// <para>Lives on the title canvas's GameObject: destroying the canvas (Hide) restores the camera and the sim.</para>
    /// </summary>
    public sealed class TitleBackdrop : MonoBehaviour
    {
        /// <summary>Global switch (tests and captures that want the live backdrop turn it off).</summary>
        public static bool Enabled = true;
        /// <summary>Title seconds before the still is taken: the backdrop's world builds over its first 1 to 2 s.</summary>
        public const float SettleSeconds = 3f;
        /// <summary>Drift: the still is drawn this much larger than the screen and pans within the margin.</summary>
        public const float DriftScale = 1.06f;
        public const float DriftPeriodSeconds = 48f;
        public const float DriftFraction = 0.02f;   // of the screen size, per axis (inside the 6% margin)

        /// <summary>true while the still is up (perf report, tests).</summary>
        public static bool Frozen { get; private set; }
        /// <summary>Stills taken this session (tests, the perf log).</summary>
        public static int Stills { get; private set; }

        public Canvas Canvas;
        RenderTexture _rt;
        RawImage _still;
        UnityEngine.Camera _cam;
        int _savedMask;
        SimDriverBehaviour _driver;
        double _savedScale;
        float _age, _driftT;
        bool _failed;

        /// <summary>Whether the backdrop should be a still now (pure; tests).</summary>
        public static bool ShouldFreeze(bool enabled, bool idleTitle, bool hidden, float titleSeconds, bool worldBuilding, bool failed) =>
            enabled && idleTitle && !hidden && !failed && !worldBuilding && titleSeconds >= SettleSeconds;

        /// <summary>The still's pan at time t, as a fraction of the screen per axis (pure; tests): a slow Lissajous inside
        /// the DriftScale margin, 0 at t = 0, so the still starts exactly where the live picture was.</summary>
        public static Vector2 Drift(float t)
        {
            float w = 2f * Mathf.PI * t / DriftPeriodSeconds;
            return new Vector2(DriftFraction * Mathf.Sin(w), DriftFraction * 0.6f * Mathf.Sin(0.5f * w));
        }

        void Update()
        {
            _age += Time.unscaledDeltaTime;
            bool hidden = Canvas == null || !Canvas.isActiveAndEnabled;
            bool want = ShouldFreeze(Enabled, FrameGovernor.IdleTitle, hidden, _age, View.Dressing.WorldView.Building, _failed);
            if (Frozen && _cam != null && _rt != null && (_cam.pixelWidth != _rt.width || _cam.pixelHeight != _rt.height)) Thaw();
            if (want && !Frozen) Freeze();
            else if (!want && Frozen) Thaw();
            if (!Frozen || _still == null) return;
            _driftT += Time.unscaledDeltaTime;
            Vector2 d = Drift(_driftT);
            RectTransform rt = _still.rectTransform;
            Vector2 size = ((RectTransform)Canvas.transform).rect.size;
            rt.anchoredPosition = new Vector2(d.x * size.x, d.y * size.y);
        }

        void Freeze()
        {
            _cam = Canvas != null ? Canvas.worldCamera : null;
            if (_cam == null) _cam = UnityEngine.Camera.main;
            if (_cam == null || _cam.pixelWidth < 16 || _cam.pixelHeight < 16) return;
            try
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest();
                if (!RenderPipeline.SupportsRenderRequest(_cam, req))
                {
                    _failed = true;
                    Debug.Log("[CM.Title] backdrop still not supported by the pipeline; the live backdrop stays");
                    return;
                }
                _rt = new RenderTexture(_cam.pixelWidth, _cam.pixelHeight, 24, RenderTextureFormat.Default) { name = "Title Backdrop Still" };
                _rt.Create();
                req.destination = _rt;
                bool canvasOn = Canvas.enabled;
                Canvas.enabled = false;   // the logo and menu are not baked into the still
                try { RenderPipeline.SubmitRenderRequest(_cam, req); }
                finally { Canvas.enabled = canvasOn; }
            }
            catch (Exception e)
            {
                _failed = true;
                ReleaseTexture();
                Debug.LogWarning("[CM.Title] backdrop still failed (" + e.GetType().Name + ": " + e.Message + "); the live backdrop stays");
                return;
            }

            if (_still == null)
            {
                var go = new GameObject("Backdrop Still", typeof(RectTransform));
                go.layer = Canvas.gameObject.layer;
                go.transform.SetParent(Canvas.transform, false);
                _still = go.AddComponent<RawImage>();
                _still.raycastTarget = false;
                RectTransform r = _still.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.sizeDelta = Vector2.zero;
                r.localScale = new Vector3(DriftScale, DriftScale, 1f);
            }
            _still.texture = _rt;
            _still.transform.SetAsFirstSibling();   // behind the wash, the logo and the menu
            _still.gameObject.SetActive(true);
            _driftT = 0f;
            _savedMask = _cam.cullingMask;
            _cam.cullingMask = 1 << Canvas.gameObject.layer;   // the camera draws only the UI from now on
            _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver != null)
            {
                _savedScale = _driver.TimeScale;
                _driver.TimeScale = 0;   // the backdrop match waits behind the still
            }
            Frozen = true;
            Stills++;
            Debug.Log($"[CM.Title] backdrop frozen to a {_rt.width}x{_rt.height} still (camera draws the UI only, backdrop sim paused)");
        }

        void Thaw()
        {
            if (!Frozen) return;
            Frozen = false;
            if (_cam != null) _cam.cullingMask = _savedMask;
            if (_driver != null && _driver.TimeScale == 0) _driver.TimeScale = _savedScale;
            _driver = null;
            if (_still != null) _still.gameObject.SetActive(false);
            ReleaseTexture();
            _age = Mathf.Min(_age, SettleSeconds - 1f);   // back to idle later: settle one second before the next still
        }

        void ReleaseTexture()
        {
            if (_still != null) _still.texture = null;
            if (_rt == null) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        void OnDisable() => Thaw();

        void OnDestroy()
        {
            Thaw();
            ReleaseTexture();
        }
    }
}
