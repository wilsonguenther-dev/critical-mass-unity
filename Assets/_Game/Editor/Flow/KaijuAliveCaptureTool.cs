using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// fix/kaiju-alive-1 evidence: frame strips at 5 frames per second for 4 sim seconds (20 frames, one every 6 ticks at
    /// 30 Hz) from a close camera at the gameplay angle, as contact sheets (JPEG, 1400 px wide). The Kaiju: the hatchling
    /// just after the hatch (its roar), and the adult idle, walking, roaring on choosing a side, clawing and slamming; one
    /// sheet per class: a bot running (top half) and the same class attacking (bottom half). The capture stages the Kaiju's
    /// states through its sim fields (this is a capture, not the game); everything else is the real 16v16 bot match.
    /// capture.json records, per strip, the local rotation range of the tracked bones (the proof that bones move).
    /// Windowless: batchmode with the GPU (no -nographics), never a visible window.
    ///   Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.KaijuAliveCaptureTool.Run -captureOut DIR
    /// </summary>
    public static class KaijuAliveCaptureTool
    {
        const string ActiveKey = "CM.KaijuAliveCapture.Active", OutKey = "CM.KaijuAliveCapture.Out";
        const int Frames = 20, TicksPerFrame = 6, MaxEditorFrames = 60000;
        const int KaijuCell = 280, ClassCell = 175;

        static int _frames, _phase;
        static SimDriverBehaviour _driver;
        static ActorViews _views;
        static Camera _cam;
        static readonly StringBuilder Json = new StringBuilder();
        static readonly List<string> Written = new List<string>();

        static KaijuAliveCaptureTool()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        public static void Run()
        {
            try
            {
                string outDir = Arg("-captureOut");
                if (string.IsNullOrEmpty(outDir)) { Debug.LogError("[KaijuAlive] -captureOut DIR is required"); EditorApplication.Exit(64); return; }
                Directory.CreateDirectory(outDir);
                SessionState.SetBool(ActiveKey, true);
                SessionState.SetString(OutKey, Path.GetFullPath(outDir));
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                Hook();
                SimDriverBehaviour.CaptureMode = true;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Fail(e); }
        }

        static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static IEnumerator<bool> _script;

        static void Update()
        {
            try
            {
                if (++_frames > MaxEditorFrames) { Debug.LogError("[KaijuAlive] timed out"); Finish(2); return; }
                if (!EditorApplication.isPlaying) return;
                if (_phase == 0)
                {
                    _driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
                    if (_driver == null || _driver.World == null) return;
                    _views = _driver.GetComponent<ActorViews>();
                    _driver.HumanAutopilot = true;
                    _script = Script();
                    _phase = 1;
                }
                // One step of the script per editor frame (it renders inside the step).
                if (!_script.MoveNext()) { WriteJson(); Finish(0); }
            }
            catch (Exception e) { Fail(e); }
        }

        // ------------------------------------------------------------------ the script

        static IEnumerator<bool> Script()
        {
            World w = _driver.World;
            Json.Append("{\"strips\":[");
            Tick(90);
            yield return true;
            w.ForceHatch = true;
            for (int i = 0; i < 300 && w.Kaiju.State == KaijuState.Egg; i++) Tick(1);
            // The hatchling: the hatch roar and its first steps.
            foreach (bool b in Strip("kaiju-hatchling", KaijuCell, 5, () => KaijuPos(), 9f, null, KaijuBones())) yield return b;
            Tick(30 * 5);   // past the hatchling (ArtMapping.HatchlingSeconds 8)
            Calm(w);
            Tick(30);
            foreach (bool b in Strip("kaiju-idle", KaijuCell, 5, () => KaijuPos(), 13f, () => Calm(w), KaijuBones())) yield return b;
            int far = FarActor(w);
            foreach (bool b in Strip("kaiju-walk", KaijuCell, 5, () => KaijuPos(), 13f, () => { w.Kaiju.MeleeCd = w.Kaiju.SlamCd = 1000; if (far >= 0) w.Kaiju.Threat[far] = 8f; }, KaijuBones())) yield return b;
            Calm(w);
            w.Kaiju.State = KaijuState.Azure;   // choosing a side: the roar
            foreach (bool b in Strip("kaiju-roar", KaijuCell, 5, () => KaijuPos(), 13f, () => { w.Kaiju.MeleeCd = w.Kaiju.SlamCd = 1000; w.Kaiju.TargetId = -1; }, KaijuBones())) yield return b;
            w.Kaiju.State = KaijuState.Neutral;
            Calm(w);
            Tick(20);
            foreach (bool b in Strip("kaiju-claw", KaijuCell, 5, () => KaijuPos(), 13f, ClawOnce(w), KaijuBones())) yield return b;
            Calm(w);
            Tick(20);
            foreach (bool b in Strip("kaiju-slam", KaijuCell, 5, () => KaijuPos(), 15f, SlamOnce(w), KaijuBones())) yield return b;

            foreach (ClassId c in ClassIds.All)
            {
                int id = FindMoving(w, c);
                for (int tries = 0; id < 0 && tries < 20; tries++) { Tick(15); id = FindMoving(w, c); }
                if (id < 0) { Debug.LogWarning("[KaijuAlive] no moving " + c); continue; }
                int who = id;
                var cells = new List<Texture2D>();
                foreach (bool b in StripInto(cells, "class-" + c + "-run", ClassCell, () => ActorPos(w, who), 7f, null, ActorBones(who))) yield return b;
                // Wait (up to 20 s) for this class to start an attack, then the attack strip on that fighter.
                int attacker = -1;
                var cd = new double[w.ActorCount];
                for (int i = 0; i < w.ActorCount; i++) cd[i] = w.Actors[i].AttackCd;
                for (int t = 0; t < 600 && attacker < 0; t++)
                {
                    Tick(1);
                    for (int i = 1; i < w.ActorCount; i++)
                    {
                        if (w.Actors[i].Cls == c && w.Actors[i].Alive && w.Actors[i].AttackCd > cd[i] + 1e-6) { attacker = i; break; }
                        cd[i] = w.Actors[i].AttackCd;
                    }
                    if (t % 60 == 0) yield return true;
                }
                if (attacker >= 0)
                {
                    int a = attacker;
                    foreach (bool b in StripInto(cells, "class-" + c + "-attack", ClassCell, () => ActorPos(w, a), 7f, null, ActorBones(a), 0)) yield return b;
                }
                Sheet(cells, ClassCell, 8, "class-" + c);
                yield return true;
            }
            Json.Append("]}");
        }

        static void Calm(World w)
        {
            Kaiju k = w.Kaiju;
            k.MeleeCd = k.SlamCd = 1000;
            k.TargetId = -1;
            for (int i = 0; i < k.Threat.Length; i++) k.Threat[i] = 0;
        }

        static Action ClawOnce(World w)
        {
            bool done = false;
            return () =>
            {
                if (done) { w.Kaiju.MeleeCd = w.Kaiju.SlamCd = 1000; return; }
                done = true;
                Kaiju k = w.Kaiju;
                int t = NearestFighter(w);
                if (t < 0) return;
                // A fighter 2 u in front, the claw locked on it (DG-84 fields, as the sim's own wind-up sets them).
                double f = k.Facing;
                w.X[t] = w.PrevX[t] = JsMath.F32(k.X + 2.0 * Math.Cos(f));
                w.Z[t] = w.PrevZ[t] = JsMath.F32(k.Z + 2.0 * Math.Sin(f));
                k.MeleeWindupIn = k.MeleeWindupLen = w.Dur.KaijuClawWindup;
                k.MeleeTargetId = t;
                k.ClawX = k.X; k.ClawZ = k.Z; k.ClawFacing = f;
                k.MeleeCd = 1000;
            };
        }

        static Action SlamOnce(World w)
        {
            bool done = false;
            return () =>
            {
                if (done) { w.Kaiju.MeleeCd = 1000; return; }
                done = true;
                Kaiju k = w.Kaiju;
                k.TelegraphX = k.X;
                k.TelegraphZ = k.Z;
                k.TelegraphIn = Telegraphs.SlamTotalSim(w);
                k.MeleeCd = 1000;
            };
        }

        static int NearestFighter(World w)
        {
            int best = -1; double bd = double.PositiveInfinity;
            for (int i = 1; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double dx = w.X[i] - w.Kaiju.X, dz = w.Z[i] - w.Kaiju.Z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static int FarActor(World w)
        {
            int best = -1; double bd = 0;
            for (int i = 1; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive || Kaiju.NearOwnPad(w, i)) continue;
                double dx = w.X[i] - w.Kaiju.X, dz = w.Z[i] - w.Kaiju.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d > 10 && d < 25 && (best < 0 || d < bd)) { bd = d; best = i; }
            }
            return best;
        }

        static int FindMoving(World w, ClassId c)
        {
            for (int i = 1; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Cls != c || !w.Actors[i].Alive) continue;
                double s = Math.Sqrt(w.Vx[i] * w.Vx[i] + w.Vz[i] * w.Vz[i]);
                if (s > 2.0) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ strips

        static IEnumerable<bool> Strip(string name, int cell, int cols, Func<Vector3> target, float dist, Action perTick, Transform[] bones)
        {
            var cells = new List<Texture2D>();
            foreach (bool b in StripInto(cells, name, cell, target, dist, perTick, bones)) yield return b;
            Sheet(cells, cell, cols, name);
        }

        static IEnumerable<bool> StripInto(List<Texture2D> cells, string name, int cell, Func<Vector3> target, float dist, Action perTick, Transform[] bones, int unused = 0)
        {
            var first = new Quaternion[bones.Length];
            var range = new float[bones.Length];
            for (int f = 0; f < Frames; f++)
            {
                for (int t = 0; t < TicksPerFrame; t++) { perTick?.Invoke(); Tick(1); }
                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] == null) continue;
                    if (f == 0) first[b] = bones[b].localRotation;
                    else range[b] = Mathf.Max(range[b], Quaternion.Angle(first[b], bones[b].localRotation));
                }
                cells.Add(Render(target(), dist, cell));
                yield return true;
            }
            if (Json[Json.Length - 1] == '}') Json.Append(',');
            Json.Append("{\"strip\":\"").Append(name).Append("\",\"boneTurnDeg\":{");
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] == null) continue;
                if (Json[Json.Length - 1] != '{') Json.Append(',');
                Json.Append('"').Append(bones[b].name).Append("\":").Append(range[b].ToString("0.0", CultureInfo.InvariantCulture));
            }
            var ka = _views.KaijuAnimator;
            Json.Append("},\"kaiju\":{\"dominant\":\"").Append(ka.Dominant).Append("\",\"roars\":").Append(ka.Roars)
                .Append(",\"steps\":").Append(ka.Steps).Append(",\"walkRate\":").Append(ka.WalkRate.ToString("0.00", CultureInfo.InvariantCulture)).Append("}}");
        }

        static Vector3 KaijuPos()
        {
            Transform t = KaijuModel();
            return t != null ? t.position + Vector3.up * 1.6f : Vector3.zero;
        }

        static Vector3 ActorPos(World w, int i)
        {
            Transform t = _views.transform.Find("Actor " + i + " " + Runtime.View.Art.ArtMapping.ClassModel(w.Actors[i].Cls));
            if (t == null) foreach (Transform c in _views.GetComponentsInChildren<Transform>(false)) if (c.name.StartsWith("Actor " + i + " ")) { t = c; break; }
            return t != null ? t.position + Vector3.up * 0.9f : Runtime.SimSpace.ToWorld(w.X[i], 1, w.Z[i]);
        }

        static Transform KaijuModel()
        {
            foreach (Transform c in _views.GetComponentsInChildren<Transform>(false))
                if (c.name == "Kaiju Adult" || c.name == "Kaiju Hatchling") return c;
            return null;
        }

        static Transform[] KaijuBones()
        {
            Transform m = KaijuModel();
            var list = new List<Transform>();
            if (m != null)
                foreach (string n in new[] { "Hips", "Chest", "Head", "Jaw", "Arm_R", "Arm_L", "Thigh_L", "Thigh_R", "Tail1", "Tail3" })
                    list.Add(FindDeep(m, n));
            return list.ToArray();
        }

        static Transform[] ActorBones(int i)
        {
            foreach (Transform c in _views.GetComponentsInChildren<Transform>(false))
                if (c.name.StartsWith("Actor " + i + " "))
                {
                    var list = new List<Transform>();
                    foreach (string n in new[] { "hips", "spine", "chest", "head", "upperarm.l", "upperarm.r", "upperleg.l", "upperleg.r" })
                        list.Add(FindDeep(c, n));
                    return list.ToArray();
                }
            return new Transform[0];
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static void Tick(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _driver.StepTicks(1);
                _views.Sync(0f);
                foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                    if (a.cullingMode != AnimatorCullingMode.AlwaysAnimate) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            _views.Sync(0f);
        }

        // ------------------------------------------------------------------ rendering

        static Texture2D Render(Vector3 target, float dist, int size)
        {
            if (_cam == null)
            {
                var go = new GameObject("KaijuAlive Capture Camera");
                _cam = go.AddComponent<Camera>();
                _cam.fieldOfView = 30f;
                _cam.nearClipPlane = 0.3f;
                _cam.farClipPlane = 400f;
                _cam.enabled = false;
                Camera main = Camera.main;
                if (main != null) { _cam.clearFlags = main.clearFlags; _cam.backgroundColor = main.backgroundColor; _cam.cullingMask = main.cullingMask & ~(1 << 5); }
            }
            Quaternion rot = Quaternion.Euler(52f, 45f, 0f);
            _cam.transform.rotation = rot;
            _cam.transform.position = target - rot * Vector3.forward * dist;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            _cam.targetTexture = rt;
            _cam.aspect = 1f;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req)) RenderPipeline.SubmitRenderRequest(_cam, req);
            else _cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            _cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        static void Sheet(List<Texture2D> cells, int cell, int cols, string name)
        {
            if (cells.Count == 0) return;
            int rows = (cells.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(20, 20, 24, 255);
            sheet.SetPixels32(fill);
            for (int i = 0; i < cells.Count; i++)
            {
                int cx = i % cols, cy = rows - 1 - i / cols;   // first frame top-left
                sheet.SetPixels(cx * cell, cy * cell, cell, cell, cells[i].GetPixels());
                // a 2 px gutter so the frames read as a strip
                for (int y = 0; y < cell; y++) { sheet.SetPixel(cx * cell, cy * cell + y, Color.black); sheet.SetPixel(cx * cell + 1, cy * cell + y, Color.black); }
                Object.DestroyImmediate(cells[i]);
            }
            sheet.Apply();
            string dir = SessionState.GetString(OutKey, "");
            int quality = 82;
            byte[] jpg = sheet.EncodeToJPG(quality);
            while (jpg.Length > 400 * 1024 && quality > 40) { quality -= 8; jpg = sheet.EncodeToJPG(quality); }
            string path = Path.Combine(dir, name + ".jpg");
            File.WriteAllBytes(path, jpg);
            Written.Add(path);
            Debug.Log($"[KaijuAlive] wrote {path} ({jpg.Length / 1024} KB, {sheet.width}x{sheet.height}, q{quality})");
            Object.DestroyImmediate(sheet);
            cells.Clear();
        }

        static void WriteJson()
        {
            string dir = SessionState.GetString(OutKey, "");
            File.WriteAllText(Path.Combine(dir, "capture.json"), Json.ToString());
        }

        static void Finish(int code)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Update;
            Debug.Log($"[KaijuAlive] done code={code}, {Written.Count} sheets");
            EditorApplication.Exit(code);
        }

        static void Fail(Exception e)
        {
            Debug.LogError("[KaijuAlive] " + e);
            try { WriteJson(); } catch { }
            Finish(1);
        }

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
