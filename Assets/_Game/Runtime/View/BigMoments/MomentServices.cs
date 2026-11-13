using System;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Moments
{
    /// <summary>
    /// Clean placeholder sheets for every effect image the brief lists, generated once (white on transparent, tintable),
    /// used only when Resources/UI/BigMoments has no file of that name. Small sizes: they are soft shapes, not art.
    /// </summary>
    public static class ProceduralSheets
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>The delivered sheet, else a generated one (never null for a name in MomentArt.Sheets).</summary>
        public static Texture2D SheetOrFallback(string name)
        {
            Texture2D t = MomentArt.Load(name);
            return t != null ? t : Get(name);
        }

        public static Texture2D Get(string name)
        {
            if (Cache.TryGetValue(name, out Texture2D t) && t != null) return t;
            int w = 128, h = 128;
            if (name == MomentArt.SpeedRadial) w = h = 256;
            else if (name == MomentArt.SpeedBand || name == MomentArt.Band) { w = 256; h = 64; }
            else if (name == MomentArt.Spark) w = h = 32;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float a = Alpha(name, u, v, x, y);
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "BM " + name + " (generated)",
                hideFlags = HideFlags.DontSave,
                wrapMode = name == MomentArt.SpeedBand || name == MomentArt.Band ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            t.SetPixels32(px);
            t.Apply(false, true);
            Cache[name] = t;
            return t;
        }

        static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }

        static float Alpha(string name, float u, float v, int x, int y)
        {
            float cx = u - 0.5f, cy = v - 0.5f, r = Mathf.Sqrt(cx * cx + cy * cy) * 2f, ang = Mathf.Atan2(cy, cx);
            float a01 = (ang + Mathf.PI) / (2f * Mathf.PI);
            switch (name)
            {
                case MomentArt.SpeedRadial:
                {
                    float k = a01 * 110f;
                    int i = Mathf.FloorToInt(k);
                    float f = k - i, width = 0.12f + 0.3f * Hash(i);
                    float line = Mathf.Abs(f - 0.5f) < width * 0.5f ? 1f : 0f;
                    return line * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f + 0.2f * Hash(i + 999), 0.9f, r));
                }
                case MomentArt.SpeedBand:
                {
                    int row = y / 3;
                    float on = Hash(row) > 0.6f ? 1f : 0f;
                    float seg = Mathf.Repeat(u * (1f + 2f * Hash(row + 7)) + Hash(row + 3), 1f);
                    return on * (seg < 0.55f ? Mathf.Sin(seg / 0.55f * Mathf.PI) : 0f) * 0.9f;
                }
                case MomentArt.Burst:
                {
                    float spikes = 0.62f + 0.38f * Mathf.Abs(Mathf.Cos(ang * 8.5f)) * (0.8f + 0.2f * Hash(Mathf.FloorToInt(a01 * 17f)));
                    return r < spikes ? Mathf.Lerp(1f, 0.6f, r / spikes) : 0f;
                }
                case MomentArt.Band:
                {
                    float edge = Mathf.Min(v, 1f - v) * 64f;
                    float rough = 2f + 3f * Hash(x / 4);
                    return edge > rough ? 0.85f + 0.15f * Hash(x * 31 + y) : 0f;
                }
                case MomentArt.CracksCorner:
                {
                    // three jagged cracks from the top-left corner (v = 1 is the top)
                    float best = 1f;
                    for (int c = 0; c < 3; c++)
                    {
                        float dir = 0.25f + 0.25f * c;   // fraction of a quarter turn
                        float lx = u, ly = 1f - v;
                        float along = lx * Mathf.Cos(dir * Mathf.PI * 0.5f) + ly * Mathf.Sin(dir * Mathf.PI * 0.5f);
                        if (along > 0.7f) continue;
                        float across = -lx * Mathf.Sin(dir * Mathf.PI * 0.5f) + ly * Mathf.Cos(dir * Mathf.PI * 0.5f);
                        float jag = 0.025f * Mathf.Sin(along * 40f + c * 2f);
                        float thick = Mathf.Lerp(0.02f, 0.003f, along / 0.7f);
                        best = Mathf.Min(best, Mathf.Abs(across - jag) / thick);
                    }
                    return best < 1f ? 1f : 0f;
                }
                case MomentArt.AuraRing:
                    return Mathf.Clamp01(1f - Mathf.Abs(r - 0.88f) / 0.07f);
                case MomentArt.Spark:
                {
                    float d = Mathf.Abs(cx) + Mathf.Abs(cy);
                    return Mathf.Clamp01(1f - d / 0.5f) * Mathf.Clamp01(1f - d / 0.5f);
                }
                case MomentArt.EdgeGlow:
                {
                    float e = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    return 1f - Mathf.SmoothStep(0f, 1f, e / 0.3f);
                }
                default:
                    return 1f;
            }
        }
    }

    /// <summary>
    /// The missing-portrait fallback that ships today: the actor's (or the Kaiju's) real 3D model, posed in an attack
    /// frame, rendered by a small off-screen camera into a temporary RenderTexture for the cut-in band. One render at a
    /// time; the RenderTexture is released and the camera disabled when the takeover ends; model instances are pooled
    /// (hidden) for reuse. The stage sits far outside the map (StageOrigin) so the gameplay camera never sees it. View only.
    /// </summary>
    public sealed class ModelPortrait
    {
        public static readonly Vector3 StageOrigin = new Vector3(5000f, 0f, 5000f);
        public const int Size = 768;
        public const float FieldOfView = 28f;
        /// <summary>Faces screen right, three-quarter on (Unity yaw 135: forward = (+x, -z)).</summary>
        public const float StageYaw = 135f;

        private readonly ArtCatalog _catalog;
        private readonly GameObject _stage;
        private readonly UnityEngine.Camera _camera;
        private readonly Dictionary<string, ModelView> _pool = new Dictionary<string, ModelView>();
        private ModelView _active;
        private string _clip;
        private RenderTexture _rt;

        public bool Rendering => _rt != null;
        public RenderTexture Texture => _rt;

        public ModelPortrait(Transform parent)
        {
            _catalog = ArtCatalog.Load();
            _stage = new GameObject("Big Moments Stage");
            _stage.transform.SetParent(parent, false);
            _stage.transform.position = StageOrigin;
            var camGo = new GameObject("Big Moments Model Camera");
            camGo.transform.SetParent(_stage.transform, false);
            _camera = camGo.AddComponent<UnityEngine.Camera>();
            _camera.enabled = false;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = BackdropFor(CMTokens.VoidNavy.Color);   // opaque: see BackdropFor
            _camera.fieldOfView = FieldOfView;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 60f;
            _camera.depth = -20f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_camera);
            data.renderPostProcessing = false;
            data.renderShadows = false;
        }

        public static string ClassKey(ClassId c) => ArtMapping.ClassModel(c);

        /// <summary>True when the catalog has a model for key (class model or ArtMapping.KaijuAdult).</summary>
        public bool CanRender(string key)
        {
            if (string.IsNullOrEmpty(key) || _catalog == null || ArtMapping.Spec(key) == null) return false;
            // integration batch 3: no off-screen render without a GPU (batch tests, -nographics); the HUD icon stands in
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;
            ArtEntry e = _catalog.Find(key);
            return e != null && e.Model != null;
        }

        /// <summary>Starts rendering a class model in its team colours; null when it has no model.</summary>
        public RenderTexture BeginClass(ClassId c, TeamId t)
        {
            string key = ClassKey(c);
            if (!CanRender(key)) return null;
            return Begin(key, ArtMaterials.ForTeam(ArtMapping.Spec(key), t), ArtMapping.Attack, BackdropFor(CMTokens.Team(t)));
        }

        /// <summary>
        /// fix/model-scale (the "giant Azure Warrior" report, 2026-11-12): the cut-in is drawn on an OPAQUE card. With the old
        /// transparent clear the live render was a bare fighter cut-out 660 reference px tall composited straight over the
        /// battlefield next to the real crowd, which read as one fighter inflated to about 3x. The card makes it a portrait.
        /// </summary>
        public static Color BackdropFor(Color accent)
        {
            Color c = Color.Lerp(CMTokens.VoidNavy.Color, accent, 0.35f);
            c.a = 1f;
            return c;
        }

        /// <summary>The live render's background this takeover (always opaque).</summary>
        public Color Backdrop => _camera.backgroundColor;

        /// <summary>Starts rendering the adult Kaiju in its allegiance glow; null when it has no model.</summary>
        public RenderTexture BeginKaiju(KaijuState s)
        {
            string key = ArtMapping.KaijuAdult;
            if (!CanRender(key)) return null;
            Color accent = s == KaijuState.Azure ? CMTokens.Azure.Color : s == KaijuState.Crimson ? CMTokens.Crimson.Color
                : s == KaijuState.Berserk ? CMTokens.Corruption.Color : CMTokens.VoidNavy.Color;
            return Begin(key, ArtMaterials.ForGlow(ArtMapping.Spec(key), ArtMapping.KaijuGlow(s)), ArtMapping.Roar, BackdropFor(accent));
        }

        private RenderTexture Begin(string key, Material material, string clip, Color backdrop)
        {
            End();
            _camera.backgroundColor = backdrop;
            if (!_pool.TryGetValue(key, out ModelView m) || m == null || m.Root == null)
            {
                m = new ModelView(_catalog.Find(key), _stage.transform, "BM Model " + key);
                foreach (Renderer r in m.Renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // posed by hand every frame while shown: never culled away before the portrait camera first sees it
                Animator anim = m.Root.GetComponent<Animator>();
                if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _pool[key] = m;
            }
            _active = m;
            m.SetActive(true);
            m.Transform.localPosition = Vector3.zero;
            m.SetFacing(Quaternion.Euler(0f, StageYaw, 0f));
            if (material != null) m.SetMaterial(material);
            _clip = m.HasClip(clip) ? clip : m.HasClip(ArtMapping.Attack) ? ArtMapping.Attack : null;

            float h = m.Spec != null && m.Spec.Height > 0.1f ? m.Spec.Height : 2f;
            Vector3 target = new Vector3(0f, h * 0.6f, 0f);
            float dist = h * 0.8f * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            _camera.transform.localPosition = target + new Vector3(0f, h * 0.08f, -dist);
            _camera.transform.LookAt(_stage.transform.TransformPoint(target));
            _camera.farClipPlane = dist + h * 3f;

            _rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            _rt.name = "BM Portrait";
            _camera.targetTexture = _rt;
            _camera.enabled = true;   // URP renders an enabled camera with a target texture before the gameplay camera
            Pose(0f, true);
            return _rt;
        }

        /// <summary>Poses the attack frame for progress 0..1 (motion false: one fixed frame).</summary>
        public void Pose(float progress, bool motion)
        {
            if (_active == null) return;
            _active.BeginPose();
            if (_clip != null)
            {
                float len = _active.ClipLength(_clip);
                float f = motion ? 0.3f + 0.35f * Mathf.Clamp01(progress) : 0.45f;
                _active.Pose(_clip, len * f, 1f, false);
            }
            _active.Evaluate();
        }

        /// <summary>Stops rendering: camera off, RenderTexture released, the model hidden in the pool.</summary>
        public void End()
        {
            _camera.enabled = false;
            _camera.targetTexture = null;
            if (_rt != null)
            {
                RenderTexture.ReleaseTemporary(_rt);
                _rt = null;
            }
            if (_active != null) _active.SetActive(false);
            _active = null;
        }

        public void Dispose()
        {
            End();
            foreach (KeyValuePair<string, ModelView> kv in _pool) kv.Value?.Dispose();
            _pool.Clear();
            if (_stage != null) UnityEngine.Object.Destroy(_stage);
        }
    }

    /// <summary>Sound for big moments. The default bridge is a no-op until the audio item (DG-18/31, PR #26) is on the base.</summary>
    public interface IBigMomentsAudio
    {
        void OnTakeover(MomentId id, float seconds, bool playerOwned);
        void OnStinger(MomentId id);
    }

    /// <summary>The sink the director calls (replace it to route through another audio system).</summary>
    public static class BigMomentsAudio
    {
        public static IBigMomentsAudio Sink = new AudioBridge();
    }

    /// <summary>
    /// Finds DG-31's MusicDirector.Duck(float) and UiSound.Play(UiCue) by name (reflection, scanned once), so this compiles
    /// on main and lights up when PR #26 merges: a takeover ducks the music for its length; a stinger plays the UI confirm
    /// cue. The announcer (also PR #26) already voices first knock-out, multi knock-outs, hatch and berserk on its own,
    /// so the bridge never asks it to speak (no double lines). Optional one-shot clips Resources/Audio/BigMoments/bm-takeover
    /// and bm-stinger play through the director's AudioSource when present.
    /// </summary>
    public sealed class AudioBridge : IBigMomentsAudio
    {
        private bool _scanned;
        private MethodInfo _duck, _uiPlay;
        private object _confirmCue;
        private readonly object[] _one = new object[1];

        public bool HasMusic { get { Scan(); return _duck != null; } }

        private void Scan()
        {
            if (_scanned) return;
            _scanned = true;
            Assembly asm = typeof(AudioBridge).Assembly;
            Type music = asm.GetType("CriticalMass.Runtime.Audio.MusicDirector");
            if (music != null) _duck = music.GetMethod("Duck", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float) }, null);
            Type ui = asm.GetType("CriticalMass.Runtime.Audio.UiSound");
            Type cue = asm.GetType("CriticalMass.Runtime.Audio.UiCue");
            if (ui != null && cue != null && Enum.IsDefined(cue, "Confirm"))
            {
                _uiPlay = ui.GetMethod("Play", BindingFlags.Public | BindingFlags.Static, null, new[] { cue }, null);
                _confirmCue = Enum.Parse(cue, "Confirm");
            }
        }

        public void OnTakeover(MomentId id, float seconds, bool playerOwned)
        {
            Scan();
            if (_duck == null) return;
            try
            {
                _one[0] = seconds;
                _duck.Invoke(null, _one);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _duck = null;
            }
        }

        public void OnStinger(MomentId id)
        {
            Scan();
            if (_uiPlay == null) return;
            try
            {
                _one[0] = _confirmCue;
                _uiPlay.Invoke(null, _one);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _uiPlay = null;
            }
        }
    }
}
