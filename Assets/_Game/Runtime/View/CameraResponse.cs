using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View.Reactions
{
    /// <summary>
    /// The camera-shake replacement (DG-94, audit finding 8, owner decision OD-6: shake OFF by default). A WorldForceBus
    /// listener that never moves the view. For the local player (actor 0) it turns each force's Shock, falling off with
    /// distance (full within radius + 3 u, nothing at 30 u), into:
    /// <list type="bullet">
    /// <item>a screen-edge flash on the side the force came from (felt above 0.3; at most 3 per second, WCAG 2.3.1;
    /// fades in 0.25 s; reduced motion quarters it);</item>
    /// <item>controller rumble, low motor by felt, high motor by sharpness, 60 to 180 ms (Input System Gamepad);</item>
    /// <item>a low-frequency hook for the audio owner (LowFrequency event, felt above 0.25) so big hits get a bass layer.</item>
    /// </list>
    /// The grass shock ring needs nothing here: the same force reaches ForceField. View only; no allocation per force.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public sealed class CameraResponse : MonoBehaviour, IWorldForceListener
    {
        public const float FlashThreshold = 0.3f, RumbleThreshold = 0.15f, BassThreshold = 0.25f;
        public const float MinFlashGap = 0.34f, FlashLife = 0.25f;

        /// <summary>Bass hook: intensity 0..1 and the force's world position (the audio owner plays a sub layer).</summary>
        public static event Action<float, Vector3> LowFrequency;

        private SimDriverBehaviour _driver;
        private WorldForceBus _subscribed;
        private Canvas _canvas;
        private readonly RawImage[] _edges = new RawImage[4];
        private readonly float[] _edgePeak = new float[4];
        private float _flashAt = -10f, _rumbleUntil = -1f;
        private bool _rumbling;

        /// <summary>Rumble requests made (also counted with no controller connected) and flashes started (tests).</summary>
        public int RumbleRequests { get; private set; }
        public int Flashes { get; private set; }

        /// <summary>The strongest edge alpha now (tests and captures).</summary>
        public float FlashAlpha
        {
            get
            {
                float a = 0f;
                for (int i = 0; i < 4; i++) if (_edges[i] != null && _edges[i].enabled) a = Mathf.Max(a, _edges[i].color.a);
                return a;
            }
        }

        /// <summary>Felt intensity 0..1 of a force for a listener at sim (lx, lz).</summary>
        public static float Felt(in WorldForce f, double lx, double lz)
        {
            double dx = f.X - lx, dz = f.Z - lz;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            float near = f.Radius + 3f, far = 30f;
            float fall = d <= near ? 1f : d >= far ? 0f : 1f - (d - near) / (far - near);
            return Mathf.Clamp01(f.Shock * fall);
        }

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            BuildEdges();
        }

        private void OnDestroy()
        {
            if (_subscribed != null) _subscribed.Unsubscribe(this);
            StopRumble();
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        private void OnDisable() => StopRumble();

        private void Update()
        {
            WorldForceBus bus = WorldForceBus.Active;
            if (bus != _subscribed)
            {
                if (_subscribed != null) _subscribed.Unsubscribe(this);
                _subscribed = bus;
                bus?.Subscribe(this);
            }
            float now = Time.unscaledTime;
            if (_rumbling && now >= _rumbleUntil) StopRumble();
            float t = now - _flashAt;
            for (int i = 0; i < 4; i++)
            {
                if (_edges[i] == null) continue;
                float a = t >= 0f && t < FlashLife ? _edgePeak[i] * (1f - t / FlashLife) : 0f;
                bool on = a > 0.01f;
                if (_edges[i].enabled != on) _edges[i].enabled = on;
                if (on) _edges[i].color = new Color(1f, 0.95f, 0.84f, a);
            }
        }

        public void OnForce(ref WorldForce f)
        {
            World w = _driver != null ? _driver.World : null;
            if (w == null || w.ActorCount == 0 || !w.Actors[0].Alive) return;
            float felt = Felt(in f, w.X[0], w.Z[0]);
            if (felt <= 0f) return;
            Vector3 src = SimSpace.ToWorld(f.X, f.Y, f.Z);
            if (felt >= RumbleThreshold) Rumble(felt, f.Source == ForceSource.KaijuSlam || f.Source == ForceSource.Explosion);
            if (felt >= BassThreshold) LowFrequency?.Invoke(felt, src);
            if (felt >= FlashThreshold) Flash(felt, src, w);
        }

        private void Flash(float felt, Vector3 src, World w)
        {
            float now = Time.unscaledTime;
            if (now - _flashAt < MinFlashGap) return; // never 3 or more a second
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector2 dir = Vector2.zero;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(src);
                dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (vp.z < 0f) dir = -dir;
            }
            else
            {
                Vector3 me = SimSpace.ToWorld(w.X[0], 0, w.Z[0]);
                dir = new Vector2(src.x - me.x, src.z - me.z);
            }
            float scale = 0.55f * felt * ReactionBudget.MotionScale;
            if (dir.sqrMagnitude < 1e-4f)
            {
                for (int i = 0; i < 4; i++) _edgePeak[i] = scale * 0.6f; // on top of us: every edge, softer
            }
            else
            {
                dir.Normalize();
                // 0 top, 1 bottom, 2 left, 3 right.
                _edgePeak[0] = scale * Mathf.Clamp01(dir.y);
                _edgePeak[1] = scale * Mathf.Clamp01(-dir.y);
                _edgePeak[2] = scale * Mathf.Clamp01(-dir.x);
                _edgePeak[3] = scale * Mathf.Clamp01(dir.x);
            }
            _flashAt = now;
            Flashes++;
            Update();
        }

        private void Rumble(float felt, bool big)
        {
            RumbleRequests++;
            float ms = Mathf.Lerp(60f, 180f, felt);
            _rumbleUntil = Mathf.Max(_rumbleUntil, Time.unscaledTime + ms / 1000f);
            Gamepad pad = Gamepad.current;
            if (pad == null) return;
            float scale = ReactionBudget.MotionScale < 1f ? 0.5f : 1f;
            pad.SetMotorSpeeds(Mathf.Clamp01(felt) * scale, (big ? 0.35f : 0.6f) * felt * scale);
            _rumbling = true;
        }

        private void StopRumble()
        {
            if (!_rumbling) return;
            _rumbling = false;
            Gamepad pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
        }

        /// <summary>Four screen edges, each a hard two-step gradient (toon) toward the middle; hidden until a flash.</summary>
        private void BuildEdges()
        {
            var go = new GameObject("Impact Edges");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = -40;
            Texture2D tex = EdgeTexture();
            for (int i = 0; i < 4; i++)
            {
                var e = new GameObject("Edge " + i);
                e.transform.SetParent(go.transform, false);
                RawImage img = e.AddComponent<RawImage>();
                img.texture = tex;
                img.raycastTarget = false;
                RectTransform rt = img.rectTransform;
                const float depth = 0.16f;
                switch (i)
                {
                    case 0: rt.anchorMin = new Vector2(0, 1 - depth); rt.anchorMax = Vector2.one; img.uvRect = new Rect(0, 0, 1, 1); break;
                    case 1: rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, depth); img.uvRect = new Rect(0, 1, 1, -1); break;
                    case 2: rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(depth, 1); rt.localRotation = Quaternion.identity; break;
                    default: rt.anchorMin = new Vector2(1 - depth, 0); rt.anchorMax = Vector2.one; break;
                }
                if (i >= 2) img.texture = SideTexture(i == 3);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                img.color = new Color(1, 1, 1, 0);
                img.enabled = false;
                _edges[i] = img;
            }
        }

        private static Texture2D EdgeTexture()
        {
            // Vertical: opaque band at the top row, a lighter step, then clear.
            var t = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Impact Edge" };
            for (int y = 0; y < 16; y++) t.SetPixel(0, y, new Color(1, 1, 1, y >= 12 ? 1f : y >= 7 ? 0.45f : 0f));
            t.Apply(false, true);
            return t;
        }

        private static Texture2D SideTexture(bool right)
        {
            var t = new Texture2D(16, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Impact Side" };
            for (int x = 0; x < 16; x++)
            {
                int k = right ? x : 15 - x;
                t.SetPixel(x, 0, new Color(1, 1, 1, k >= 12 ? 1f : k >= 7 ? 0.45f : 0f));
            }
            t.Apply(false, true);
            return t;
        }
    }
}
