using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Atmosphere;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CriticalMass.Editor.LookProbes
{
    /// <summary>
    /// b15 world look: the reviewer's measuring scripts (Editor, Play mode, one Editor instance). Menu "CM/World Look/...".
    /// Every probe prints one plain line per case (what, value, limit) to the Console and appends it to
    /// ~/cm-editor-eyes/&lt;UTC stamp&gt;-worldlook/probe.txt with its PNGs.
    /// <list type="number">
    /// <item>Value: the gameplay camera (pitch 55, FOV 35, 23.4 u, yaw from the config) over the hill, both pads and the
    /// local fighter, for Day, Dusk and Night. Two renders, fighters on and off, with every fighter's shadow casting OFF in
    /// both and the unit rings hidden in both, so the mask is the bodies and their ink only. BODY luma = the mask eroded
    /// 3 px (no ink line); GROUND = the 12 px ring around the mask in the fighters-off render. Bar: body - ground
    /// &gt;= +0.12 (Day, Dusk), &gt;= +0.15 (Night). Also prints the audit's own number (mask with ink, shadows on) and
    /// the ground luma spread.</item>
    /// <item>Frame: 600 frames of ProfilerRecorder counters (SetPass, draw calls, batches, triangles, shadow casters, GC
    /// alloc per frame, main thread ms) with the quality level, MSAA, HDR and post state. Bars: SetPass p50 &lt; 150
    /// (expected after this branch: see the PR), no frame &gt; 33 ms after the hatch, GC 0 B per frame in steady fight
    /// frames once the world is warm (the HUD's own allocations are reported separately when present).</item>
    /// <item>Feet: AnimLod off for the run, every ON-SCREEN fighter's foot and hip bones per frame for 6 s: share of
    /// walking frames with a foot under 25% of body speed (planted), per class. Bar: &gt;= 30% planted frames per class
    /// (a planted run foot is down about 30 to 45% of the cycle). The audit's 0-3% included off-screen fighters, whose
    /// graphs AnimLod does not evaluate by design (their pose holds and the foot rides with the body).</item>
    /// </list>
    /// </summary>
    public static class WorldLookProbes
    {
        static string _dir;

        static string Dir()
        {
            if (_dir != null) return _dir;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            _dir = Path.Combine(home, "cm-editor-eyes", DateTime.UtcNow.ToString("yyyyMMdd'T'HHmm'Z'") + "-worldlook");
            Directory.CreateDirectory(_dir);
            return _dir;
        }

        static void Out(string line)
        {
            Debug.Log("[b15 probe] " + line);
            File.AppendAllText(Path.Combine(Dir(), "probe.txt"), line + "\n");
        }

        static SimDriverBehaviour Driver() => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();

        // ------------------------------------------------------------------------------------------- value
        [MenuItem("CM/World Look/Value probe (Day, Dusk, Night)")]
        public static void ValueProbe()
        {
            SimDriverBehaviour d = Driver();
            if (!Application.isPlaying || d == null || d.World == null) { Debug.LogWarning("[b15 probe] enter Play mode in a match first"); return; }
            World w = d.World;
            MapDef map = w.Map;
            var spots = new List<(string name, double x, double z)> { ("hill", map.Hill.X + 2.0, map.Hill.Z) };
            if (map.Pads != null) for (int i = 0; i < map.Pads.Length; i++) spots.Add(("pad" + i, map.Pads[i].X, map.Pads[i].Z));
            if (w.ActorCount > 0) spots.Add(("local fighter", w.X[0], w.Z[0]));
            TimeOfDay? before = TimeOfDayDirector.Override;
            try
            {
                foreach (TimeOfDay t in new[] { TimeOfDay.Day, TimeOfDay.Dusk, TimeOfDay.Night })
                {
                    TimeOfDayDirector.Override = t;
                    if (TimeOfDayDirector.Current != null) TimeOfDayDirector.Current.SnapToTarget();
                    float bar = t == TimeOfDay.Night ? 0.15f : 0.12f;
                    foreach (var s in spots) Out(Value(d, t, s.name, s.x, s.z, bar));
                }
            }
            finally
            {
                TimeOfDayDirector.Override = before;
                if (TimeOfDayDirector.Current != null) TimeOfDayDirector.Current.SnapToTarget();
            }
        }

        static string Value(SimDriverBehaviour d, TimeOfDay t, string spot, double sx, double sz, float bar)
        {
            const int W = 1920, H = 1080;
            MapDef map = d.World.Map;
            var go = new GameObject("b15 value probe camera");
            UnityEngine.Camera c = go.AddComponent<UnityEngine.Camera>();
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main != null) c.CopyFrom(main);
            c.fieldOfView = 35f;
            c.targetTexture = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            float yaw = (float)(d.Config != null ? d.Config.Render.CameraYawDeg : 45.0);
            Quaternion q = Quaternion.Euler(55f, yaw + 180f, 0f);
            float gy = (float)GroundHeight.At(map, sx, sz);
            c.transform.rotation = q;
            c.transform.position = SimSpace.ToWorld(sx, gy, sz) - q * Vector3.forward * 23.4f;
            ActorViews av = UnityEngine.Object.FindAnyObjectByType<ActorViews>();
            var actors = new List<Renderer>();
            if (av != null) actors.AddRange(av.GetComponentsInChildren<Renderer>(false));
            var rings = new List<Renderer>();
            GameObject ringRoot = GameObject.Find("Unit Rings");
            if (ringRoot != null) rings.AddRange(ringRoot.GetComponentsInChildren<Renderer>(false));
            var casting = new Dictionary<Renderer, ShadowCastingMode>();
            Color32[] withShadow = Grab(c), a, b;
            foreach (Renderer r in actors) { casting[r] = r.shadowCastingMode; if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) r.shadowCastingMode = ShadowCastingMode.Off; }
            var ringOn = new Dictionary<Renderer, bool>();
            foreach (Renderer r in rings) { ringOn[r] = r.enabled; r.enabled = false; }
            var shadowOnly = new List<Renderer>();
            foreach (Renderer r in actors) if (casting[r] == ShadowCastingMode.ShadowsOnly && r.enabled) { r.enabled = false; shadowOnly.Add(r); }
            try
            {
                a = Grab(c);
                var on = new List<Renderer>();
                foreach (Renderer r in actors) if (r.enabled) { r.enabled = false; on.Add(r); }
                try { b = Grab(c); }
                finally { foreach (Renderer r in on) r.enabled = true; }
            }
            finally
            {
                foreach (var kv in casting) kv.Key.shadowCastingMode = kv.Value;
                foreach (var kv in ringOn) kv.Key.enabled = kv.Value;
                foreach (Renderer r in shadowOnly) r.enabled = true;
            }
            int n = W * H;
            var mask = new bool[n];
            for (int i = 0; i < n; i++) mask[i] = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 40;
            bool[] body = Erode(mask, W, H, 3);
            var near = new bool[n];
            for (int y = 0; y < H; y += 2)
                for (int x = 0; x < W; x += 2)
                {
                    if (!mask[y * W + x]) continue;
                    for (int oy = -12; oy <= 12; oy += 4)
                        for (int ox = -12; ox <= 12; ox += 4)
                        {
                            int yy = y + oy, xx = x + ox;
                            if (yy >= 0 && yy < H && xx >= 0 && xx < W && !mask[yy * W + xx]) near[yy * W + xx] = true;
                        }
                }
            double bl = 0, ml = 0, gl = 0, g2 = 0, wl = 0;
            int bn = 0, mn = 0, gn = 0, wn = 0;
            for (int i = 0; i < n; i++)
            {
                if (body[i]) { bl += L(a[i]); bn++; }
                if (mask[i]) { ml += L(a[i]); mn++; }
                if (near[i]) { double l = L(b[i]); gl += l; g2 += l * l; gn++; }
                if (mask[i] || Math.Abs(withShadow[i].r - b[i].r) + Math.Abs(withShadow[i].g - b[i].g) + Math.Abs(withShadow[i].b - b[i].b) > 40) { wl += L(withShadow[i]); wn++; }
            }
            double body01 = bn > 0 ? bl / bn : 0, ground = gn > 0 ? gl / gn : 0, sd = gn > 0 ? Math.Sqrt(Math.Max(0, g2 / gn - ground * ground)) : 0;
            string file = Path.Combine(Dir(), $"value-{t}-{spot.Replace(' ', '-')}.png");
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels32(a);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture rt = c.targetTexture;
            c.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
            double delta = body01 - ground;
            return $"VALUE {t} {spot}: body luma {body01:0.000} (px {bn}) ground behind {ground:0.000} delta {delta:+0.000;-0.000} limit >= +{bar:0.00} " +
                   $"{(delta >= bar ? "PASS" : "FAIL")} | audit-style (ink + shadows) {(wn > 0 ? wl / wn : 0):0.000} | mask with ink {(mn > 0 ? ml / mn : 0):0.000} | ground luma s.d. {sd:0.000}";
        }

        static bool[] Erode(bool[] m, int w, int h, int r)
        {
            bool[] cur = m;
            for (int k = 0; k < r; k++)
            {
                var nx = new bool[cur.Length];
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = y * w + x;
                        nx[i] = cur[i] && cur[i - 1] && cur[i + 1] && cur[i - w] && cur[i + w];
                    }
                cur = nx;
            }
            return cur;
        }

        static double L(Color32 c) => (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0;

        static Color32[] Grab(UnityEngine.Camera c)
        {
            RenderTexture rt = c.targetTexture;
            c.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Color32[] px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        // ------------------------------------------------------------------------------------------- frame
        static FrameRecorder _frame;

        [MenuItem("CM/World Look/Frame probe (600 frames)")]
        public static void FrameProbe()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[b15 probe] enter Play mode in a match first"); return; }
            if (_frame != null) return;
            var go = new GameObject("b15 frame probe") { hideFlags = HideFlags.DontSave };
            _frame = go.AddComponent<FrameRecorder>();
            _frame.Done = lines => { foreach (string l in lines) Out(l); _frame = null; };
        }

        sealed class FrameRecorder : MonoBehaviour
        {
            public Action<List<string>> Done;
            ProfilerRecorder _setPass, _draws, _batches, _tris, _casters, _gc, _main;
            readonly List<double> _ms = new List<double>(), _sp = new List<double>(), _dc = new List<double>(), _gcb = new List<double>(), _sc = new List<double>(), _tr = new List<double>();
            int _left = 600;
            int _hatchFrame = -1;
            int _over33AfterHatch;

            void OnEnable()
            {
                _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
                _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            }

            void OnDisable()
            {
                _setPass.Dispose(); _draws.Dispose(); _batches.Dispose(); _tris.Dispose(); _casters.Dispose(); _gc.Dispose(); _main.Dispose();
            }

            void LateUpdate()
            {
                SimDriverBehaviour d = Driver();
                bool hatched = d != null && d.World != null && d.World.Kaiju != null && d.World.Kaiju.State != KaijuState.Egg;
                if (hatched && _hatchFrame < 0) _hatchFrame = Time.frameCount;
                double ms = Time.unscaledDeltaTime * 1000.0;
                _ms.Add(ms);
                _sp.Add(_setPass.LastValue); _dc.Add(_draws.LastValue); _gcb.Add(_gc.LastValue); _sc.Add(_casters.LastValue); _tr.Add(_tris.LastValue);
                if (_hatchFrame >= 0 && Time.frameCount > _hatchFrame && ms > 33.0) _over33AfterHatch++;
                if (--_left > 0) return;
                var cam = UnityEngine.Camera.main;
                var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var lines = new List<string>
                {
                    $"FRAME quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} MSAA {(urp != null ? urp.msaaSampleCount : 0)} HDR {(urp != null && urp.supportsHDR)} post {(data != null && data.renderPostProcessing)} time {(TimeOfDayDirector.Current != null ? TimeOfDayDirector.Current.Target.ToString() : "-")} (Editor numbers: measure the build for ms)",
                    $"FRAME ms p50 {P(_ms, 50):0.00} p95 {P(_ms, 95):0.00} p99 {P(_ms, 99):0.00} max {P(_ms, 100):0.0}; frames > 33 ms after the hatch {_over33AfterHatch} (limit 0; hatch seen {_hatchFrame >= 0})",
                    $"FRAME SetPass p50 {P(_sp, 50):0} max {P(_sp, 100):0} (limit p50 < 150); draws p50 {P(_dc, 50):0} max {P(_dc, 100):0}; tris p50 {P(_tr, 50):0} (limit < 1,000,000); shadow casters p50 {P(_sc, 50):0} max {P(_sc, 100):0}",
                    $"FRAME GC alloc per frame p50 {P(_gcb, 50):0} B p95 {P(_gcb, 95):0} B max {P(_gcb, 100):0} B (limit: 0 B steady view; hatch frame <= {CriticalMass.Runtime.View.WorldLookBudgets.HatchFrameAllocBytes} B)",
                };
                Done?.Invoke(lines);
                Destroy(gameObject);
            }

            static double P(List<double> v, int pct)
            {
                if (v.Count == 0) return 0;
                var s = new List<double>(v);
                s.Sort();
                int i = Mathf.Clamp((int)Math.Round((s.Count - 1) * pct / 100.0), 0, s.Count - 1);
                return s[i];
            }
        }

        // ------------------------------------------------------------------------------------------- feet
        static FeetRecorder _feet;

        [MenuItem("CM/World Look/Feet probe (6 s, on-screen fighters)")]
        public static void FeetProbe()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[b15 probe] enter Play mode in a match first"); return; }
            if (_feet != null) return;
            var go = new GameObject("b15 feet probe") { hideFlags = HideFlags.DontSave };
            _feet = go.AddComponent<FeetRecorder>();
            _feet.Done = lines => { foreach (string l in lines) Out(l); _feet = null; };
        }

        sealed class FeetRecorder : MonoBehaviour
        {
            public Action<List<string>> Done;
            float _t;
            bool _lodWas;
            readonly Dictionary<Transform, (Vector3 fl, Vector3 fr, Vector3 root)> _last = new Dictionary<Transform, (Vector3, Vector3, Vector3)>();
            readonly Dictionary<string, (int walk, int planted)> _byClass = new Dictionary<string, (int, int)>();

            void OnEnable() { _lodWas = CriticalMass.Runtime.View.Anim.AnimLod.Enabled; CriticalMass.Runtime.View.Anim.AnimLod.Enabled = false; }
            void OnDisable() { CriticalMass.Runtime.View.Anim.AnimLod.Enabled = _lodWas; }

            void LateUpdate()
            {
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                ActorViews av = UnityEngine.Object.FindAnyObjectByType<ActorViews>();
                float dt = Time.deltaTime;
                if (cam != null && av != null && dt > 0f)
                {
                    foreach (Animator an in av.GetComponentsInChildren<Animator>(false))
                    {
                        Transform root = an.transform, fl = null, fr = null;
                        foreach (Transform b in root.GetComponentsInChildren<Transform>(false))
                        {
                            if (b.name == "foot.l") fl = b;
                            else if (b.name == "foot.r") fr = b;
                        }
                        if (fl == null || fr == null) continue;
                        Vector3 vp = cam.WorldToViewportPoint(root.position);
                        bool onScreen = vp.z > 0f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f;
                        if (onScreen && _last.TryGetValue(root, out var prev))
                        {
                            float body = Flat(root.position - prev.root) / dt;
                            if (body > 1.0f)
                            {
                                float a = Flat(fl.position - prev.fl) / dt, b = Flat(fr.position - prev.fr) / dt;
                                string cls = ClassOf(root.name);
                                _byClass.TryGetValue(cls, out var s);
                                s.walk++;
                                if (Mathf.Min(a, b) < 0.25f * body) s.planted++;
                                _byClass[cls] = s;
                            }
                        }
                        _last[root] = (fl.position, fr.position, root.position);
                    }
                }
                _t += Time.unscaledDeltaTime;
                if (_t < 6f) return;
                var lines = new List<string>();
                foreach (var kv in _byClass)
                {
                    float share = kv.Value.walk > 0 ? (float)kv.Value.planted / kv.Value.walk : 0f;
                    lines.Add($"FEET {kv.Key}: planted-foot share of walking on-screen frames {share:0%} (n {kv.Value.walk}) limit >= 30% {(share >= 0.3f ? "PASS" : "FAIL")}");
                }
                if (lines.Count == 0) lines.Add("FEET no on-screen walking fighter seen in 6 s (move the camera to a fight and run again)");
                Done?.Invoke(lines);
                Destroy(gameObject);
            }

            static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

            static string ClassOf(string name)
            {
                foreach (string c in new[] { "Warrior", "Farmer", "Miner", "Demolitions", "Builder", "Healer" })
                    if (name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0) return c;
                return name;
            }
        }
    }
}
