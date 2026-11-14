using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// feat/character-turntable-1: the -characterShots capture director (its own file; it boots itself, no other file
    /// changes). For every class model, the Kaiju adult and the hatchling, in both team colours, it stands the model on
    /// open flat ground far from the map (a neutral grey backdrop, a light floor with stride marks every 0.5 u) and draws
    /// it with its own camera into an off-screen texture, so the HUD is never in the picture. Per model and team it writes:
    ///   shot-char-&lt;key&gt;-&lt;team&gt;-stills.png  front, right side, back, left side, three-quarter; top row close range
    ///                                       (Hero mesh, face height), bottom row gameplay distance and pitch (LOD0)
    ///   shot-char-&lt;key&gt;-&lt;team&gt;-walk-side.png / -walk-front.png / -run-side.png / -run-front.png
    ///                                       8 frames evenly over one clip cycle, in place, low camera, feet and ground line in frame
    ///   shot-char-&lt;key&gt;-&lt;team&gt;-idle-attack.png  4 Idle frames then 4 frames of the class's attack clip, three-quarter view
    /// plus shot-char-lineup-&lt;team&gt;.png: every model side by side, facing the camera, for relative size.
    /// The poses are the game's own path: ModelView (the same prefab, clips and ArtYawDeg facing as ActorViews), the
    /// team material from ArtMaterials.ForTeam (Kaiju: ForGlow with the team's allegiance glow), the imported scale
    /// (BaseScale). Not applied here: ActorAnimator's lean, bank, leg warp and spring bones, and cosmetics.
    /// The log ([CM.CharacterShots]) states per model which FBX it drew, which clip pack it plays (the shared KayKit pack,
    /// checked by clip object identity against the KayKit_Clips entry, or its own baked pack), the clip lengths and the
    /// measured world height, so the pictures can be trusted.
    /// Options: -characterShotsOnly &lt;Class&gt; (Warrior, Farmer, Miner, Demolitions, Builder, Healer, Kaiju, Hatchling;
    /// comma list allowed; the line-up still shows every model). The sim is paused while it shoots; quits when done
    /// unless -characterShotsStay is given.
    /// Cost: capture only. One 1920x960 strip texture and one tile render texture, both freed at the end; nothing runs
    /// unless the flag is on the command line.
    /// </summary>
    public sealed class CharacterShotsCapture : MonoBehaviour
    {
        public const string Flag = "-characterShots", OnlyFlag = "-characterShotsOnly", StayFlag = "-characterShotsStay";
        public const string Prefix = "shot-char-";
        public const int TileW = 384, TileH = 480, StripTileW = 240, StripTileH = 300, StripFrames = 8;
        /// <summary>The stage: far from every map so the world never shows; y 0 keeps the shader's height fog off.</summary>
        static readonly Vector3 Stage = new Vector3(4000f, 0f, 4000f);
        static readonly Color Backdrop = new Color(0.66f, 0.68f, 0.70f, 1f);
        static readonly Color Floor = new Color(0.80f, 0.79f, 0.75f, 1f);
        static readonly Color Mark = new Color(0.35f, 0.34f, 0.32f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, Flag) < 0 && Array.IndexOf(args, OnlyFlag) < 0) return;
            if (FindAnyObjectByType<CharacterShotsCapture>() != null) return;
            var go = new GameObject("CharacterShots Director");
            DontDestroyOnLoad(go);
            go.AddComponent<CharacterShotsCapture>();
        }

        static void Log(string s) => Debug.Log("[CM.CharacterShots] " + s);

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        /// <summary>One subject: a mapping key, its display name, the class (null for the Kaiju models).</summary>
        struct Subject
        {
            public string Key, Name;
            public ClassId? Cls;
        }

        static readonly TeamId[] Teams = { TeamId.Azure, TeamId.Crimson };

        UnityEngine.Camera _cam;
        RenderTexture _tile, _stripTile;
        Texture2D _sheet;
        GameObject _floor;
        readonly List<GameObject> _marks = new List<GameObject>();
        SimDriverBehaviour _driver;
        double _savedScale = double.NaN;
        float _gamePitch = 55f, _gameDist = 22f, _gameFov = 35f;

        void Start() => StartCoroutine(Run());

        static List<Subject> Subjects()
        {
            var list = new List<Subject>();
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer })
                list.Add(new Subject { Key = ArtMapping.ClassModel(c), Name = c.ToString(), Cls = c });
            list.Add(new Subject { Key = ArtMapping.KaijuHatchling, Name = "Hatchling" });
            list.Add(new Subject { Key = ArtMapping.KaijuAdult, Name = "Kaiju" });
            return list;
        }

        static bool Wanted(Subject s, string only)
        {
            if (string.IsNullOrEmpty(only)) return true;
            foreach (string part in only.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                if (string.Equals(p, s.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(p, s.Key, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            // Wait for the match scene (the sun, the gameplay camera and the catalog are then all up), at most 90 s.
            while (Time.realtimeSinceStartup - t0 < 90f)
            {
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
                var w = _driver != null ? _driver.World : null;
                if (w != null && w.Match.Phase == MatchPhase.Playing && w.Match.Time >= 2 && UnityEngine.Camera.main != null) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            ArtCatalog catalog = ArtCatalog.Load();
            if (catalog == null) { Log("no art catalog in Resources; nothing to shoot"); yield return Finish(); yield break; }
            if (_driver != null) { _savedScale = _driver.TimeScale; _driver.TimeScale = 0; }
            ReadGameplayCamera();
            BuildStage();

            string only = Arg(OnlyFlag);
            List<Subject> all = Subjects();
            ArtEntry kay = catalog.Find(ArtMapping.KaykitClips);
            Log($"start: {all.Count} models, only={(only ?? "all")}, gameplay camera pitch {_gamePitch:F1} deg, distance {_gameDist:F1} u, fov {_gameFov:F1}; " +
                $"KayKit shared pack in catalog: {(kay != null && kay.Clips != null ? kay.Clips.Length + " clips" : "MISSING")}");

            foreach (Subject s in all)
            {
                if (!Wanted(s, only)) continue;
                ArtEntry e = catalog.Find(s.Key);
                if (e == null || e.Model == null || ArtMapping.Spec(s.Key) == null) { Log($"{s.Name}: no model in the catalog (grey-box capsule in game); skipped"); continue; }
                foreach (TeamId team in Teams) yield return ShootSubject(s, e, kay, team);
            }
            foreach (TeamId team in Teams) yield return Lineup(catalog, all, team);
            yield return Finish();
        }

        IEnumerator Finish()
        {
            if (_driver != null && !double.IsNaN(_savedScale)) _driver.TimeScale = _savedScale;
            Cleanup();
            Log("done");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), StayFlag) >= 0) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        /// <summary>The gameplay camera's pitch, field of view and distance to the ground point it looks at (y 0).</summary>
        void ReadGameplayCamera()
        {
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main == null) { Log("no gameplay camera; gameplay-distance stills use pitch 55, 22 u, fov 35"); return; }
            Vector3 f = main.transform.forward;
            float pitch = Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (pitch > 5f)
            {
                _gamePitch = pitch;
                float h = main.transform.position.y;
                if (h > 1f) _gameDist = Mathf.Clamp(h / Mathf.Sin(pitch * Mathf.Deg2Rad), 6f, 80f);
            }
            _gameFov = main.fieldOfView;
        }

        void BuildStage()
        {
            var go = new GameObject("CharacterShots Camera");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<UnityEngine.Camera>();
            UnityEngine.Camera main = UnityEngine.Camera.main;
            if (main != null) _cam.CopyFrom(main);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Backdrop;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 250f;
            _cam.depth = -50f;
            _cam.allowMSAA = false;
            _tile = new RenderTexture(TileW, TileH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1, name = "CharacterShots Tile" };
            _stripTile = new RenderTexture(StripTileW, StripTileH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1, name = "CharacterShots Strip Tile" };
            _cam.targetTexture = _tile;

            _floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floor.name = "CharacterShots Floor";
            Kill(_floor.GetComponent<Collider>());
            _floor.transform.SetParent(transform, false);
            _floor.transform.position = Stage;
            _floor.transform.localScale = new Vector3(12f, 1f, 12f);   // 120 x 120 u
            Tint(_floor, Floor);
            // Stride marks along X and Z every 0.5 u (thin, flat): a slipping or floating foot shows against them.
            for (int i = -6; i <= 6; i++)
            {
                _marks.Add(MarkBar(new Vector3(i * 0.5f, 0.002f, 1.6f), new Vector3(0.02f, 0.004f, 0.5f)));
                _marks.Add(MarkBar(new Vector3(-1.6f, 0.002f, i * 0.5f), new Vector3(0.5f, 0.004f, 0.02f)));
            }
            _marks.Add(MarkBar(new Vector3(0f, 0.002f, 1.6f), new Vector3(6.5f, 0.004f, 0.02f)));
        }

        GameObject MarkBar(Vector3 local, Vector3 size)
        {
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m.name = "CharacterShots Mark";
            Kill(m.GetComponent<Collider>());
            m.transform.SetParent(transform, false);
            m.transform.position = Stage + local;
            m.transform.localScale = size;
            Tint(m, Mark);
            return m;
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        ModelView Spawn(ArtEntry e, TeamId team, string name, Vector3 at)
        {
            var m = new ModelView(e, transform, name);
            ModelSpec spec = m.Spec;
            Material mat = spec.Kind == ArtKind.Kaiju
                ? ArtMaterials.ForGlow(spec, ArtMapping.KaijuGlow(team == TeamId.Azure ? KaijuState.Azure : KaijuState.Crimson))
                : ArtMaterials.ForTeam(spec, team);
            m.SetMaterial(mat);
            m.SetActive(true);
            m.Transform.position = at;
            m.Transform.localScale = m.BaseScale;
            m.SetFacing(Quaternion.identity);   // the model's face toward world +Z (the art yaw is added by the game's own rule)
            Animator a = m.Root.GetComponent<Animator>();
            if (a != null) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (Renderer r in m.Renderers) if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            return m;
        }

        static void PoseAt(ModelView m, string clip, float seconds, bool loop)
        {
            m.BeginPose();
            if (clip != null) m.Pose(clip, seconds, 1f, loop);
            m.Evaluate();
        }

        /// <summary>World bounds of the drawn renderers (the shadow proxy excluded).</summary>
        static Bounds Measure(ModelView m)
        {
            bool any = false;
            var b = new Bounds(m.Transform.position, Vector3.zero);
            foreach (Renderer r in m.Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.name.EndsWith("_Shadow", StringComparison.Ordinal)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static string ClipReport(ArtEntry e, ArtEntry kay, ModelSpec spec)
        {
            var sb = new StringBuilder();
            int n = e.Clips != null ? e.Clips.Length : 0;
            int shared = 0;
            if (kay != null && e.Clips != null)
                foreach (AnimationClip c in e.Clips)
                    if (c != null && ReferenceEquals(kay.Clip(c.name), c)) shared++;
            string source = n == 0 ? "NO CLIPS (static pose)"
                : spec.Kind == ArtKind.Kaiju ? "its own clips (Kaiju FBX)"
                : shared == n ? "the SHARED KayKit pack (same clip objects as KayKit_Clips)"
                : shared == 0 ? "its OWN baked pack (no clip object shared with KayKit_Clips)"
                : $"MIXED: {shared} of {n} clips are the shared KayKit objects";
            sb.Append("clips ").Append(n).Append(", source: ").Append(source)
              .Append(", ModelSpec.ClipSource=").Append(spec.ClipSource ?? "(own FBX)");
            foreach (string c in new[] { ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run })
            {
                AnimationClip k = e.Clip(c);
                sb.Append(", ").Append(c).Append(k != null ? $" {k.length:F3}s" : " missing");
            }
            return sb.ToString();
        }

        static string AttackClip(Subject s, ModelView m)
        {
            if (s.Cls.HasValue)
            {
                string a = ArtMapping.ClassClips(s.Cls.Value).Attack;
                if (m.HasClip(a)) return a;
                return m.HasClip(ArtMapping.Attack) ? ArtMapping.Attack : null;
            }
            if (m.HasClip(ArtMapping.Swipe)) return ArtMapping.Swipe;
            return m.HasClip(ArtMapping.Slam) ? ArtMapping.Slam : null;
        }

        IEnumerator ShootSubject(Subject s, ArtEntry e, ArtEntry kay, TeamId team)
        {
            string teamName = team == TeamId.Azure ? "azure" : "crimson";
            string baseName = Prefix + s.Name.ToLowerInvariant() + "-" + teamName;
            ModelView m = Spawn(e, team, "CharacterShots " + s.Name, Stage);
            ModelSpec spec = m.Spec;
            PoseAt(m, ArtMapping.Idle, 0f, true);
            yield return null;
            m.SetHero(true);
            Bounds hb = Measure(m);
            m.SetHero(false);
            m.SetLod(0);
            Bounds lb = Measure(m);
            float h = Mathf.Max(0.5f, hb.max.y - Stage.y);
            Log($"{s.Name} {teamName}: model {spec.AssetPath} (prefab {e.Model.name}), face decal {(e.Face != null ? e.Face.name : spec.VertexColourFace ? "none (vertex-colour face)" : "none")}, " +
                $"scale {m.BaseScale.y:F3}, art yaw {spec.ArtYawDeg:F0}, idle height Hero {h:F3} u / LOD0 {(lb.max.y - Stage.y):F3} u, width {hb.size.x:F2} u; " + ClipReport(e, kay, spec));

            // ---- stills: front, right, back, left, three-quarter; close (Hero) on top, gameplay (LOD0) below
            string[] views = { "front", "right", "back", "left", "threequarter" };
            float[] yaws = { 0f, 90f, 180f, 270f, 45f };
            EnsureSheet(TileW * views.Length, TileH * 2);
            _cam.targetTexture = _tile;
            for (int row = 0; row < 2; row++)
            {
                bool close = row == 0;
                m.SetHero(close);
                if (!close) m.SetLod(0);
                for (int v = 0; v < views.Length; v++)
                {
                    PoseAt(m, ArtMapping.Idle, 0f, true);
                    if (close) FrameClose(yaws[v], h, 1.0f);
                    else FrameGameplay(yaws[v], h);
                    yield return Grab(_tile, v * TileW, (1 - row) * TileH);
                }
            }
            Save(baseName + "-stills.png");

            // ---- walk and run strips, side and front, 8 frames over one cycle, LOD0 (what the match draws)
            m.SetHero(false);
            m.SetLod(0);
            _cam.targetTexture = _stripTile;
            foreach (string clip in new[] { ArtMapping.Walk, ArtMapping.Run })
            {
                if (!m.HasClip(clip)) { Log($"{s.Name} {teamName}: no {clip} clip; strip skipped"); continue; }
                float len = m.ClipLength(clip);
                foreach (var side in new[] { ("side", 90f), ("front", 0f) })
                {
                    EnsureSheet(StripTileW * StripFrames, StripTileH);
                    for (int f = 0; f < StripFrames; f++)
                    {
                        PoseAt(m, clip, len * f / StripFrames, true);
                        FrameFeet(side.Item2, h);
                        yield return Grab(_stripTile, f * StripTileW, 0);
                    }
                    Save(baseName + "-" + clip.ToLowerInvariant() + "-" + side.Item1 + ".png");
                }
            }

            // ---- idle then attack, three-quarter, close (Hero)
            string attack = AttackClip(s, m);
            m.SetHero(true);
            EnsureSheet(StripTileW * StripFrames, StripTileH);
            float idleLen = m.ClipLength(ArtMapping.Idle), atkLen = attack != null ? m.ClipLength(attack) : 0f;
            for (int f = 0; f < StripFrames; f++)
            {
                if (f < 4 || attack == null) PoseAt(m, ArtMapping.Idle, idleLen * (f % 4) / 4f, true);
                else PoseAt(m, attack, atkLen * (f - 4) / 3f, false);
                FrameClose(45f, h, 1.25f);
                yield return Grab(_stripTile, f * StripTileW, 0);
            }
            Save(baseName + "-idle-attack.png");
            Log($"{s.Name} {teamName}: idle {idleLen:F3}s, attack clip {(attack ?? "none")} {atkLen:F3}s");
            m.Dispose();
            yield return null;
        }

        IEnumerator Lineup(ArtCatalog catalog, List<Subject> all, TeamId team)
        {
            string teamName = team == TeamId.Azure ? "azure" : "crimson";
            var models = new List<ModelView>();
            var names = new List<string>();
            float x = 0f, top = 0f;
            const float gap = 0.6f;
            foreach (Subject s in all)
            {
                ArtEntry e = catalog.Find(s.Key);
                if (e == null || e.Model == null || ArtMapping.Spec(s.Key) == null) continue;
                ModelView m = Spawn(e, team, "CharacterShots Lineup " + s.Name, Stage);
                PoseAt(m, ArtMapping.Idle, 0f, true);
                m.SetHero(false);
                m.SetLod(0);
                models.Add(m);
                names.Add(s.Name);
            }
            if (models.Count == 0) yield break;
            yield return null;
            var widths = new float[models.Count];
            var heights = new float[models.Count];
            for (int i = 0; i < models.Count; i++)
            {
                Bounds b = Measure(models[i]);
                widths[i] = Mathf.Max(0.6f, b.size.x);
                heights[i] = b.max.y - Stage.y;
                top = Mathf.Max(top, heights[i]);
            }
            float total = gap * (models.Count - 1);
            foreach (float w in widths) total += w;
            var sb = new StringBuilder();
            x = -total * 0.5f;
            for (int i = 0; i < models.Count; i++)
            {
                x += widths[i] * 0.5f;
                models[i].Transform.position = Stage + new Vector3(-x, 0f, 0f);   // the camera looks down -Z: world -X is screen right
                PoseAt(models[i], ArtMapping.Idle, 0f, true);
                sb.Append(names[i]).Append(' ').Append(heights[i].ToString("F2")).Append(" u; ");
                x += widths[i] * 0.5f + gap;
            }
            Log($"lineup {teamName} (left to right, idle height in world units): {sb}");
            // Front, slightly above eye level, wide enough for the whole row.
            var rt = new RenderTexture(1920, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1, name = "CharacterShots Lineup" };
            _cam.targetTexture = rt;
            _cam.fieldOfView = 30f;
            float halfW = total * 0.5f + 0.8f;
            float halfH = top * 0.5f + 0.6f;
            float aspect = 1920f / 720f;
            float dist = Mathf.Max(halfH / Mathf.Tan(15f * Mathf.Deg2Rad), halfW / (Mathf.Tan(15f * Mathf.Deg2Rad) * aspect));
            Vector3 look = Stage + new Vector3(0f, top * 0.5f, 0f);
            Quaternion rot = Quaternion.Euler(6f, 180f, 0f);
            _cam.transform.SetPositionAndRotation(look - rot * Vector3.forward * dist, rot);
            EnsureSheet(1920, 720);
            yield return Grab(rt, 0, 0);
            Save(Prefix + "lineup-" + teamName + ".png");
            _cam.targetTexture = _tile;
            rt.Release();
            Kill(rt);
            foreach (ModelView m in models) m.Dispose();
            yield return null;
        }

        /// <summary>Close framing: whole body, low pitch, the camera on the model's side given by yaw (0 = in front).</summary>
        void FrameClose(float yaw, float h, float margin)
        {
            _cam.fieldOfView = 30f;
            float half = h * 0.5f * 1.12f * margin;
            float dist = half / Mathf.Tan(15f * Mathf.Deg2Rad) / Mathf.Min(1f, (float)_cam.targetTexture.width / _cam.targetTexture.height);
            Vector3 look = Stage + new Vector3(0f, h * 0.52f, 0f);
            Place(look, yaw, 8f, dist);
        }

        /// <summary>Gameplay framing: the gameplay camera's pitch, field of view and distance, orbiting to the yaw.</summary>
        void FrameGameplay(float yaw, float h)
        {
            _cam.fieldOfView = _gameFov;
            Vector3 look = Stage + new Vector3(0f, h * 0.4f, 0f);
            Place(look, yaw, _gamePitch, _gameDist);
        }

        /// <summary>Strip framing: knee-height camera close to the ground so both feet and the ground line read.</summary>
        void FrameFeet(float yaw, float h)
        {
            _cam.fieldOfView = 30f;
            float half = h * 0.5f * 1.15f;
            float dist = half / Mathf.Tan(15f * Mathf.Deg2Rad) / Mathf.Min(1f, (float)_cam.targetTexture.width / _cam.targetTexture.height);
            Vector3 look = Stage + new Vector3(0f, h * 0.48f, 0f);
            Place(look, yaw, 4f, dist);
        }

        /// <summary>Camera at yaw degrees around the model (0: in front, on +Z, looking at the face), pitched down.</summary>
        void Place(Vector3 look, float yaw, float pitch, float dist)
        {
            Quaternion rot = Quaternion.Euler(pitch, 180f + yaw, 0f);
            _cam.transform.SetPositionAndRotation(look - rot * Vector3.forward * dist, rot);
        }

        void EnsureSheet(int w, int h)
        {
            if (_sheet != null && _sheet.width == w && _sheet.height == h) return;
            if (_sheet != null) Kill(_sheet);
            _sheet = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = "CharacterShots Sheet" };
        }

        /// <summary>Waits for this frame's render (the pose set this frame is skinned before the cameras draw), then copies the tile.</summary>
        IEnumerator Grab(RenderTexture rt, int x, int y)
        {
            yield return new WaitForEndOfFrame();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            _sheet.ReadPixels(new Rect(0, 0, rt.width, rt.height), x, y, false);
            RenderTexture.active = prev;
        }

        void Save(string file)
        {
            _sheet.Apply(false);
            string path = Path.Combine(GamePaths.DataDir, file);
            try
            {
                File.WriteAllBytes(path, _sheet.EncodeToPNG());
                Log("shot " + file);
            }
            catch (Exception ex) { Log("could not write " + path + ": " + ex.Message); }
        }

        void Cleanup()
        {
            if (_cam != null) { _cam.targetTexture = null; Kill(_cam.gameObject); }
            if (_tile != null) { _tile.Release(); Kill(_tile); }
            if (_stripTile != null) { _stripTile.Release(); Kill(_stripTile); }
            if (_sheet != null) Kill(_sheet);
            if (_floor != null) Kill(_floor);
            foreach (GameObject g in _marks) if (g != null) Kill(g);
            _marks.Clear();
        }

        void OnDestroy() => Cleanup();

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
