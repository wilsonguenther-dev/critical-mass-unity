using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Runtime;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor
{
    /// <summary>
    /// Gameplay-camera capture (art direction 2026-11-12, section 8 checklist renders). Loads the Boot scene, enters Play
    /// Mode, runs the 16v16 bot match (actor 0 on autopilot, GameConfig.CreateDefault seed 1337) for a fixed number of
    /// simulated seconds by StepTicks with the wall clock stopped, then frames four targets with the gameplay camera
    /// (CameraFollowTarget.SetOverride) and writes 1920x1080 PNGs plus capture.json to the output folder. Needs graphics
    /// (no -nographics). Run (tools/unity-capture.sh wraps it):
    /// Unity -batchmode -projectPath . -executeMethod CriticalMass.Editor.CaptureTool.Run -captureOut DIR [-captureSeconds 30] [-captureYaw DEG]
    /// Exit codes: 0 written, 1 exception, 2 timed out in frames, 64 bad arguments.
    /// <para>Targets: the Azure base, the hill and the shrine at the gameplay distance, and "wide": the centre of the land
    /// from the gameplay pitch and FOV, pulled back to <see cref="WideDistance"/>, the one distance that fits all the land
    /// (every non-Void cell) at every heading, so captures at different yaws share a scale. Past the URP shadow distance
    /// (60 u) the wide view has no cast shadows.</para>
    /// <para>-captureYaw (capture only; the game's yaw stays RenderConfig.CameraYawDeg = 45): sets the yaw in this match's
    /// GameConfig (the sim never reads RenderConfig, so the match is identical) and re-poses everything built from it: the
    /// CM Camera's rotation and follow offset (CameraRig) and the sun (GameBootstrap.SunRotation, so it stays at the
    /// screen's upper-left). Input is bypassed, not remapped: actor 0 is on autopilot (no human frame reaches the sim), the
    /// follow is on an override (no aim term, no screen-up offset), so HumanInputState's fixed 45-degree move basis is
    /// never used here; it would be wrong for a human playing at another yaw. The aim ray (Camera.ScreenPointToRay) would
    /// follow the camera by itself.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class CaptureTool
    {
        public const int Width = 1920;
        public const int Height = 1080;
        public const double DefaultSeconds = 30;
        private const int MaxFrames = 20000;
        private const int SettleFrames = 6;
        private const string ActiveKey = "cm.capture.active";
        private const string OutKey = "cm.capture.out";
        private const string SecondsKey = "cm.capture.seconds";
        private const string YawKey = "cm.capture.yaw";
        private const string HasYawKey = "cm.capture.hasYaw";
        /// <summary>Wide target: the map corners stay this far inside the frame edges (fraction of the half-extent).</summary>
        private const double WideMargin = 0.04;

        private static int _frames;
        private static int _phase;
        private static int _ticksDone;
        private static int _ticksWanted;
        private static int _target;
        private static int _settle;
        private static SimDriverBehaviour _driver;
        private static readonly List<string> Written = new List<string>();

        // Survives a domain reload on entering Play Mode should the project ever turn it back on.
        static CaptureTool()
        {
            if (SessionState.GetBool(ActiveKey, false)) Hook();
        }

        /// <summary>Batch entry point.</summary>
        public static void Run()
        {
            try
            {
                string outDir = Arg("-captureOut");
                if (string.IsNullOrEmpty(outDir))
                {
                    Debug.LogError("[CaptureTool] -captureOut DIR is required");
                    EditorApplication.Exit(64);
                    return;
                }
                string secs = Arg("-captureSeconds");
                double seconds = string.IsNullOrEmpty(secs) ? DefaultSeconds : double.Parse(secs, CultureInfo.InvariantCulture);
                string yawArg = Arg("-captureYaw");
                bool hasYaw = !string.IsNullOrEmpty(yawArg);
                double yaw = 0;
                if (hasYaw && (!double.TryParse(yawArg, NumberStyles.Float, CultureInfo.InvariantCulture, out yaw) ||
                               double.IsNaN(yaw) || double.IsInfinity(yaw)))
                {
                    Debug.LogError($"[CaptureTool] -captureYaw '{yawArg}' is not a number of degrees");
                    EditorApplication.Exit(64);
                    return;
                }
                Directory.CreateDirectory(outDir);
                SessionState.SetBool(HasYawKey, hasYaw);
                SessionState.SetFloat(YawKey, (float)yaw);
                SessionState.SetBool(ActiveKey, true);
                SessionState.SetString(OutKey, Path.GetFullPath(outDir));
                SessionState.SetFloat(SecondsKey, (float)seconds);
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                Hook();
                SimDriverBehaviour.CaptureMode = true;
                Debug.Log($"[CaptureTool] start out={outDir} seconds={seconds} yaw={(hasYaw ? yaw.ToString(CultureInfo.InvariantCulture) : "default")}");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            try
            {
                if (++_frames > MaxFrames)
                {
                    Debug.LogError($"[CaptureTool] timed out at phase {_phase} after {MaxFrames} frames");
                    Finish(2);
                    return;
                }
                if (!EditorApplication.isPlaying) return;
                switch (_phase)
                {
                    case 0: Begin(); break;
                    case 1: Simulate(); break;
                    case 2: Shoot(); break;
                    case 3: ShootHud(); break;
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private static void Begin()
        {
            _driver = UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) return;
            // SimDriverBehaviour.CaptureMode (set before Play) already started it on autopilot with the clock stopped, so
            // no frame-time tick ran; the tick check before each shot proves it.
            if (_driver.World.Tick != 0) throw new InvalidOperationException($"world tick {_driver.World.Tick} at start: frame-time ticks ran");
            double seconds = SessionState.GetFloat(SecondsKey, (float)DefaultSeconds);
            _ticksWanted = (int)Math.Round(seconds * _driver.Config.SimHz);
            _ticksDone = 0;
            if (SessionState.GetBool(HasYawKey, false))
            {
                if (!_driver.HumanAutopilot) throw new InvalidOperationException("yaw override needs actor 0 on autopilot (input is not remapped)");
                _driver.Config.Render.CameraYawDeg = SessionState.GetFloat(YawKey, 45f);
            }
            Debug.Log($"[CaptureTool] seed={_driver.Config.Seed} teamSize={_driver.Config.TeamSize} actors={_driver.World.ActorCount} ticks={_ticksWanted}");
            _phase = 1;
        }

        private static void Simulate()
        {
            int n = Math.Min(150, _ticksWanted - _ticksDone);
            if (n > 0)
            {
                _driver.StepTicks(n);
                _ticksDone += n;
                return;
            }
            _phase = 2;
            _target = 0;
            _settle = -1;
        }

        private static (string Name, double X, double Z, double Distance)[] Targets(World w)
        {
            MapDef map = w.Map;
            PadDef azure = map.Pads[(int)TeamId.Azure];
            List<Vector2> land = LandOutline(map, out double cx, out double cz);
            CloseTarget(w, out double closeX, out double closeZ);
            return new[]
            {
                ("base-azure", azure.X, azure.Z, GameplayCamera.Distance),
                ("base-crimson", map.Pads[(int)TeamId.Crimson].X, map.Pads[(int)TeamId.Crimson].Z, GameplayCamera.Distance),
                ("hill", map.Hill.X, map.Hill.Z, GameplayCamera.Distance),
                ("shrine", map.Shrine.X, map.Shrine.Z, GameplayCamera.Distance),
                ("wide", cx, cz, WideDistance(map, land, cx, cz)),
                ("close", closeX, closeZ, GameplayCamera.Distance),
            };
        }

        /// <summary>
        /// Art review frame (2026-11-12 art import): the centroid of the Kaiju, the living Warrior nearest it and the living
        /// Miner nearest it, at the gameplay camera distance. Read-only on the world.
        /// </summary>
        public static void CloseTarget(World w, out double x, out double z)
        {
            double kx = w.Kaiju.X, kz = w.Kaiju.Z;
            int warrior = Nearest(w, ClassId.Warrior, kx, kz), miner = Nearest(w, ClassId.Miner, kx, kz);
            double sx = kx, sz = kz;
            int n = 1;
            if (warrior >= 0) { sx += w.X[warrior]; sz += w.Z[warrior]; n++; }
            if (miner >= 0) { sx += w.X[miner]; sz += w.Z[miner]; n++; }
            x = sx / n;
            z = sz / n;
        }

        private static int Nearest(World w, ClassId cls, double x, double z)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Cls != cls) continue;
                double d = (w.X[i] - x) * (w.X[i] - x) + (w.Z[i] - z) * (w.Z[i] - z);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The outer corners of the land (every cell that is not Cell.Void): per row the first and last land cell, per
        /// column the first and last, in sim (x, z). (cx, cz) = the centre of the land's bounding box. A map with no land
        /// falls back to its bounds.
        /// </summary>
        public static List<Vector2> LandOutline(MapDef map, out double cx, out double cz)
        {
            var pts = new List<Vector2>();
            int w = map.Width, h = map.Height;
            int minX = int.MaxValue, maxX = -1, minZ = int.MaxValue, maxZ = -1;
            for (int z = 0; z < h; z++)
            {
                int first = -1, last = -1;
                for (int x = 0; x < w; x++)
                {
                    if (map.Cells[z * w + x] == Cell.Void) continue;
                    if (first < 0) first = x;
                    last = x;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
                if (first < 0) continue;
                pts.Add(new Vector2(first, z)); pts.Add(new Vector2(first, z + 1));
                pts.Add(new Vector2(last + 1, z)); pts.Add(new Vector2(last + 1, z + 1));
            }
            for (int x = 0; x < w; x++)
            {
                int first = -1, last = -1;
                for (int z = 0; z < h; z++)
                {
                    if (map.Cells[z * w + x] == Cell.Void) continue;
                    if (first < 0) first = z;
                    last = z;
                }
                if (first < 0) continue;
                pts.Add(new Vector2(x, first)); pts.Add(new Vector2(x + 1, first));
                pts.Add(new Vector2(x, last + 1)); pts.Add(new Vector2(x + 1, last + 1));
            }
            BoundsDef b = map.Bounds;
            if (pts.Count == 0)
            {
                pts.Add(new Vector2((float)b.MinX, (float)b.MinZ)); pts.Add(new Vector2((float)b.MaxX, (float)b.MinZ));
                pts.Add(new Vector2((float)b.MinX, (float)b.MaxZ)); pts.Add(new Vector2((float)b.MaxX, (float)b.MaxZ));
                cx = (b.MinX + b.MaxX) / 2;
                cz = (b.MinZ + b.MaxZ) / 2;
                return pts;
            }
            cx = (minX + maxX + 1) / 2.0;
            cz = (minZ + maxZ + 1) / 2.0;
            return pts;
        }

        /// <summary>
        /// The smallest look distance (gameplay pitch and FOV, 16:9, look target = (cx, cz) at its ground height) that keeps
        /// every land outline point inside the frame at every heading in 15-degree steps, so a wide capture at any yaw has
        /// the same scale.
        /// </summary>
        public static double WideDistance(MapDef map, List<Vector2> land, double cx, double cz)
        {
            Vector3 target = SimSpace.ToWorld(cx, GroundHeight.At(map, cx, cz), cz);
            var corners = new Vector3[land.Count];
            for (int i = 0; i < land.Count; i++)
                corners[i] = SimSpace.ToWorld(land[i].x, GroundHeight.At(map, land[i].x, land[i].y), land[i].y);
            double tv = Math.Tan(GameplayCamera.FovDeg * Math.PI / 360) * (1 - WideMargin);
            double th = tv * Width / Height;
            double lo = GameplayCamera.Distance, hi = 1000;
            for (int it = 0; it < 60; it++)
            {
                double d = (lo + hi) / 2;
                bool fits = true;
                for (int yaw = 0; yaw < 360 && fits; yaw += 15)
                {
                    Quaternion r = Quaternion.Euler((float)GameplayCamera.PitchDeg, 360f - yaw, 0f);
                    Quaternion inv = Quaternion.Inverse(r);
                    Vector3 cam = target - r * Vector3.forward * (float)d;
                    foreach (Vector3 p in corners)
                    {
                        Vector3 l = inv * (p - cam);
                        if (l.z <= 0 || Math.Abs(l.x / l.z) > th || Math.Abs(l.y / l.z) > tv) { fits = false; break; }
                    }
                }
                if (fits) hi = d; else lo = d;
            }
            return Math.Ceiling(hi * 10) / 10;
        }

        /// <summary>
        /// Pose the CM Camera (CameraRig) and the sun (GameBootstrap) for the config's yaw and the given look distance.
        /// At the default yaw and the gameplay distance this re-applies exactly what CameraRig and GameBootstrap built.
        /// </summary>
        private static void ApplyPose(double distance)
        {
            GameConfig cfg = _driver.Config;
            GameObject cmGo = GameObject.Find("CM Camera");
            if (cmGo == null) throw new InvalidOperationException("no CM Camera in the scene");
            var follow = cmGo.GetComponent<CinemachineFollow>();
            var cm = cmGo.GetComponent<CinemachineCamera>();
            if (follow == null || cm == null) throw new InvalidOperationException("CM Camera has no CinemachineFollow / CinemachineCamera");
            Quaternion rotation = GameplayCamera.Rotation(cfg);
            cmGo.transform.rotation = rotation;
            follow.FollowOffset = -(rotation * Vector3.forward) * (float)distance;
            cm.Lens.FarClipPlane = Math.Max(GameplayCamera.FarClip, (float)distance + 150f);
            GameObject sun = GameObject.Find("Directional Light");
            if (sun != null) sun.transform.rotation = GameBootstrap.SunRotation(cfg);
        }

        private static void Shoot()
        {
            var targets = Targets(_driver.World);
            if (_target >= targets.Length)
            {
                WriteManifest(targets);
                _phase = 3;
                _extra = 0;
                _settle = -1;
                return;
            }
            CameraFollowTarget follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (follow == null) throw new InvalidOperationException("no CameraFollowTarget in the scene");
            if (_settle < 0)
            {
                ApplyPose(targets[_target].Distance);
                follow.ClearOverride();
                follow.SetOverride(targets[_target].X, targets[_target].Z);
                _settle = SettleFrames;
                return;
            }
            if (--_settle > 0) return;

            UnityEngine.Camera main = UnityEngine.Camera.main;
            GameObject hillGo = GameObject.Find("Hill");
            Debug.Log($"[CaptureTool] {targets[_target].Name}: target={follow.transform.position} cam={main.transform.position} " +
                      $"euler={main.transform.eulerAngles} fov={main.fieldOfView} ortho={main.orthographic} " +
                      $"distance={targets[_target].Distance} yaw={_driver.Config.Render.CameraYawDeg} " +
                      $"hill={(hillGo != null ? hillGo.transform.position + " scale " + hillGo.transform.lossyScale + " active " + hillGo.activeInHierarchy + " mat " + hillGo.GetComponent<Renderer>().sharedMaterial.shader.name : "none")}");
            if (_driver.World.Tick != _ticksWanted)
                throw new InvalidOperationException($"world tick {_driver.World.Tick} != {_ticksWanted}: frame-time ticks leaked in, the capture is not deterministic");
            string path = Path.Combine(SessionState.GetString(OutKey, "."), "capture-" + targets[_target].Name + ".png");
            RenderTo(path);
            Written.Add(path);
            Debug.Log("[CaptureTool] wrote " + path);
            Perf.Add(MeasureFrame(targets[_target].Name));
            _target++;
            _settle = -1;
        }

        /// <summary>Frames timed per target by MeasureFrame.</summary>
        private const int PerfFrames = 40;
        private static readonly List<string> Perf = new List<string>();

        /// <summary>
        /// View frame time at the current pose, 1920x1080 MSAA 4, clock stopped: per frame the views' Sync (positions,
        /// materials, animation evaluation) and one render of the gameplay camera, then a 1-pixel read-back so the GPU work
        /// of that frame is finished inside the timing. Median and p95 over PerfFrames after 5 warm-up frames. This is the
        /// editor in batch mode, not a player build: compare before and after, do not read it as a budget.
        /// </summary>
        private static string MeasureFrame(string name)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            var views = _driver.GetComponent<CriticalMass.Runtime.View.ActorViews>();
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            rt.Create();
            var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
            RenderTexture prevActive = RenderTexture.active;
            cam.aspect = (float)Width / Height;
            var ms = new List<double>();
            // Draw calls, batches and SetPass calls of the measured frames (Render profiler counters; 0 when the counter
            // is not available in this editor build, reported as -1).
            var drawRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
            var batchRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
            var setPassRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
            long draws = -1, batches = -1, setPass = -1;
            try
            {
                for (int i = 0; i < PerfFrames + 5; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    if (views != null) views.Sync(1f / 60f);
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    RenderPipeline.SubmitRenderRequest(cam, req);
                    RenderTexture.active = rt;
                    px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    RenderTexture.active = prevActive;
                    sw.Stop();
                    if (i >= 5) ms.Add(sw.Elapsed.TotalMilliseconds);
                    if (i >= 5 && drawRec.Valid && drawRec.CurrentValue > 0) draws = Math.Max(draws, drawRec.CurrentValue);
                    if (i >= 5 && UnityStats.drawCalls > 0) draws = Math.Max(draws, UnityStats.drawCalls);
                    if (i >= 5 && UnityStats.batches > 0) batches = Math.Max(batches, UnityStats.batches);
                    if (i >= 5 && UnityStats.setPassCalls > 0) setPass = Math.Max(setPass, UnityStats.setPassCalls);
                    if (i >= 5 && batchRec.Valid && batchRec.CurrentValue > 0) batches = Math.Max(batches, batchRec.CurrentValue);
                    if (i >= 5 && setPassRec.Valid && setPassRec.CurrentValue > 0) setPass = Math.Max(setPass, setPassRec.CurrentValue);
                }
            }
            finally
            {
                drawRec.Dispose();
                batchRec.Dispose();
                setPassRec.Dispose();
                RenderTexture.active = prevActive;
                cam.ResetAspect();
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(px);
            }
            ms.Sort();
            double median = ms[ms.Count / 2], p95 = ms[(int)Math.Floor(ms.Count * 0.95)];
            int renderers = 0, skinned = 0, visible = 0;
            long tris = 0, visibleTris = 0, estDraws = 0;
            // Batch mode reports no Render profiler counters, so the frame's draws are also estimated from the camera:
            // each renderer whose bounds meet the frustum costs one forward draw per submesh, plus one hull draw per
            // submesh when its material keeps the outline pass, plus one shadow-caster draw when it casts (no batching).
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                renderers++;
                Mesh m = r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                if (r is SkinnedMeshRenderer) skinned++;
                if (m != null) for (int s = 0; s < m.subMeshCount; s++) tris += (long)m.GetIndexCount(s) / 3;
                if (m == null || !GeometryUtility.TestPlanesAABB(frustum, r.bounds)) continue;
                visible++;
                Material[] mats = r.sharedMaterials;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    visibleTris += (long)m.GetIndexCount(s) / 3;
                    Material mat = s < mats.Length ? mats[s] : null;
                    estDraws += 1;
                    if (mat != null && mat.FindPass("Outline") >= 0 && mat.GetShaderPassEnabled("SRPDefaultUnlit")) estDraws++;
                    if (r.shadowCastingMode != ShadowCastingMode.Off) estDraws++;
                }
            }
            string line = string.Format(CultureInfo.InvariantCulture,
                "{{\"name\": \"{0}\", \"frameMsMedian\": {1:0.00}, \"frameMsP95\": {2:0.00}, \"frames\": {3}, \"renderers\": {4}, \"skinned\": {5}, \"sceneTris\": {6}, \"drawCalls\": {7}, \"batches\": {8}, \"setPass\": {9}, \"visibleRenderers\": {10}, \"visibleTris\": {11}, \"estDrawCalls\": {12}}}",
                name, median, p95, ms.Count, renderers, skinned, tris, draws, batches, setPass, visible, visibleTris, estDraws);
            Debug.Log("[CaptureTool] perf " + line);
            return line;
        }

        private static void RenderTo(string path) => RenderTo(path, Width, Height, false, null);

        /// <summary>
        /// Renders the gameplay camera to a PNG of width x height. The HUD (GameHud) and the title screen are Screen Space -
        /// Camera canvases, so they are drawn in the same render: the camera targets the texture first and the canvases are
        /// re-scaled to its size. uiOnly: only the UI layer over a pure magenta clear (HUD area mask).
        /// </summary>
        private static void RenderTo(string path, int width, int height, bool uiOnly, TitleScreen title)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) throw new InvalidOperationException("no main camera");
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4,
            };
            rt.Create();
            RenderTexture prevActive = RenderTexture.active;
            int prevMask = cam.cullingMask;
            CameraClearFlags prevFlags = cam.clearFlags;
            Color prevBg = cam.backgroundColor;
            cam.targetTexture = rt;
            cam.aspect = (float)width / height;
            GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
            if (hud != null && _driver != null && _driver.World != null)
            {
                hud.UpdateScale();
                hud.Refresh(_driver.World, 0f);
            }
            if (title != null) title.UpdateScale();
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();   // the first pass re-sizes the canvas to the texture, the second lays out at that size
            if (hud != null)
            {
                var land = UnityEngine.Object.FindAnyObjectByType<CriticalMass.Runtime.Hud.Ui.MinimapLand>();
                Debug.Log($"[CaptureTool] hud canvas {hud.Root.rect.size} scale {hud.Canvas.scaleFactor} for {width}x{height}; " +
                          $"minimap quads {(land != null ? land.Quads : -1)} rect {(land != null ? land.rectTransform.rect.ToString() : "-")}");
            }
            if (uiOnly)
            {
                cam.cullingMask = 1 << 5;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
            }
            try
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                cam.cullingMask = prevMask;
                cam.clearFlags = prevFlags;
                cam.backgroundColor = prevBg;
                cam.ResetAspect();
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        // ---------------------------------------------------------------- HUD and title shots (2026-11-12, hud-and-logo)

        private static int _extra;

        /// <summary>HUD shots at the hill pose: three resolutions, the area mask, the state sheet frames, the title screen.</summary>
        private static readonly (string Name, int W, int H)[] HudShots =
        {
            ("hud-1080", 1920, 1080), ("hud-720", 1280, 720), ("hud-2160", 3840, 2160), ("hud-mask", 1920, 1080),
            ("state-hill-neutral", 1920, 1080), ("state-hill-azure", 1920, 1080), ("state-hill-contested", 1920, 1080),
            ("state-kaiju-neutral", 1920, 1080), ("state-kaiju-azure", 1920, 1080), ("state-kaiju-crimson", 1920, 1080),
            ("state-kaiju-berserk", 1920, 1080), ("state-death", 1920, 1080), ("title", 1920, 1080),
        };

        /// <summary>View-only HudState edits for the state sheet (GameHud.PreviewOverride; the Sim is untouched).</summary>
        private static Action<HudState> OverrideFor(string name)
        {
            switch (name)
            {
                case "state-hill-neutral": return s => { s.Hill = HillState.Neutral; s.OnHillAzure = 0; s.OnHillCrimson = 0; };
                case "state-hill-azure": return s => { s.Hill = HillState.Azure; s.OnHillAzure = 3; s.OnHillCrimson = 0; };
                case "state-hill-contested": return s => { s.Hill = HillState.Contested; s.OnHillAzure = 2; s.OnHillCrimson = 2; };
                case "state-kaiju-neutral": return s => { s.Hatched = true; s.Kaiju = KaijuState.Neutral; s.KaijuMeter01 = 0.85f; s.Instability01 = 0.2f; };
                case "state-kaiju-azure": return s => { s.Hatched = true; s.Kaiju = KaijuState.Azure; s.KaijuMeter01 = 0.6f; s.Instability01 = 0.4f; };
                case "state-kaiju-crimson": return s => { s.Hatched = true; s.Kaiju = KaijuState.Crimson; s.KaijuMeter01 = 0.45f; s.Instability01 = 0.6f; };
                case "state-kaiju-berserk": return s => { s.Hatched = true; s.Kaiju = KaijuState.Berserk; s.KaijuMeter01 = 0.9f; s.Instability01 = 1f; };
                case "state-death": return s => { s.Alive = false; s.Hp = 0f; s.RespawnIn = 4.2; };
                default: return null;
            }
        }

        private static void ShootHud()
        {
            if (_extra >= HudShots.Length)
            {
                GameHud.PreviewOverride = null;
                Finish(0);
                return;
            }
            CameraFollowTarget follow = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (follow == null) throw new InvalidOperationException("no CameraFollowTarget in the scene");
            if (_settle < 0)
            {
                ApplyPose(GameplayCamera.Distance);
                follow.ClearOverride();
                follow.SetOverride(_driver.World.Map.Hill.X, _driver.World.Map.Hill.Z);
                _settle = SettleFrames;
                return;
            }
            if (--_settle > 0) return;
            var shot = HudShots[_extra];
            GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
            if (hud == null) throw new InvalidOperationException("no GameHud in the scene");
            hud.SnapAnimations = true;
            GameHud.PreviewOverride = OverrideFor(shot.Name);
            TitleScreen title = shot.Name == "title" ? TitleScreen.Show(hud.gameObject, hud) : null;
            string path = Path.Combine(SessionState.GetString(OutKey, "."), shot.Name + ".png");
            RenderTo(path, shot.W, shot.H, shot.Name == "hud-mask", title);
            if (title != null) title.Hide();
            GameHud.PreviewOverride = null;
            Written.Add(path);
            Debug.Log("[CaptureTool] wrote " + path);
            _extra++;
            _settle = -1;
        }

        private static void WriteManifest((string Name, double X, double Z, double Distance)[] targets)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"seed\": {_driver.Config.Seed},\n  \"teamSize\": {_driver.Config.TeamSize},\n");
            sb.Append($"  \"ticks\": {_ticksDone},\n  \"simSeconds\": {(_ticksDone / _driver.Config.SimHz).ToString(CultureInfo.InvariantCulture)},\n");
            sb.Append($"  \"worldTick\": {_driver.World.Tick},\n  \"width\": {Width},\n  \"height\": {Height},\n");
            sb.Append($"  \"camera\": {{\"projection\": \"perspective\", \"pitchDeg\": {GameplayCamera.PitchDeg.ToString(CultureInfo.InvariantCulture)}, \"fovDeg\": {GameplayCamera.FovDeg.ToString(CultureInfo.InvariantCulture)}, \"distance\": {GameplayCamera.Distance.ToString(CultureInfo.InvariantCulture)}, \"yawDeg\": {_driver.Config.Render.CameraYawDeg.ToString(CultureInfo.InvariantCulture)}, \"yawOverride\": {(SessionState.GetBool(HasYawKey, false) ? "true" : "false")}, \"input\": \"bypassed: actor 0 on autopilot, follow on override\"}},\n");
            sb.Append("  \"targets\": [\n");
            for (int i = 0; i < targets.Length; i++)
            {
                sb.Append($"    {{\"name\": \"{targets[i].Name}\", \"simX\": {targets[i].X.ToString(CultureInfo.InvariantCulture)}, \"simZ\": {targets[i].Z.ToString(CultureInfo.InvariantCulture)}, \"distance\": {targets[i].Distance.ToString(CultureInfo.InvariantCulture)}, \"file\": \"{Path.GetFileName(Written[i])}\"}}");
                sb.Append(i + 1 < targets.Length ? ",\n" : "\n");
            }
            sb.Append("  ],\n  \"perf\": [\n    " + string.Join(",\n    ", Perf) + "\n  ]\n}\n");
            File.WriteAllText(Path.Combine(SessionState.GetString(OutKey, "."), "capture.json"), sb.ToString());
        }

        private static void Fail(Exception e)
        {
            Debug.LogException(e);
            Finish(1);
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Update;
            SimDriverBehaviour.CaptureMode = false;
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseBool(HasYawKey);
            Debug.Log($"[CaptureTool] exit {code}, {Written.Count} file(s)");
            EditorApplication.Exit(code);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
