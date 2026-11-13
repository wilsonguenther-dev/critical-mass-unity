using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// Film strip of the story intro (windowless, in edit mode, no play mode): renders the motion comic's "intro" sequence
    /// through the same <see cref="MotionComicPlayer"/> the game uses, frame by frame on a fixed clock, at the owner's screen
    /// size (default 3024 x 1964, a 14-inch MacBook Pro full screen, aspect 1.54) and 6 frames a second, plus the dissolve into
    /// a stand-in title logo drawn exactly where the title screen draws its logo. The navy plate under the layers is painted
    /// magenta, so any pixel the art leaves uncovered shows. Writes:
    /// <list type="bullet">
    /// <item>frames/f0000.jpg ... (every frame, 756 px wide), full/f0000.jpg (every sixth frame, full size);</item>
    /// <item>frames.csv: frame, time, shot, local time, uncovered (magenta) pixels inside the film window, mean luma,
    /// visible titles and their screen boxes;</item>
    /// <item>aspects.csv: for 4:3, 1.54, 16:10, 16:9 and 21:9, each shot at its start, middle and end: uncovered pixels.</item>
    /// </list>
    /// Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.StoryFilmCapture.Run -filmOut DIR
    /// [-filmW 3024 -filmH 1964 -filmFps 6 -filmSeq intro] (needs a GPU: no -nographics). Exit 0 on success.
    /// </summary>
    public static class StoryFilmCapture
    {
        static readonly Color Hole = new Color(1f, 0f, 1f, 1f);
        public static readonly float[] Aspects = { 4f / 3f, 3024f / 1964f, 16f / 10f, 16f / 9f, 21f / 9f };
        const int SmallW = 756;
        const float OutroFrames = 6f;

        public static void Run()
        {
            int code = 1;
            var log = new List<string>();
            try
            {
                string outDir = Arg("-filmOut");
                if (string.IsNullOrEmpty(outDir)) throw new ArgumentException("-filmOut DIR is required");
                int W = ArgInt("-filmW", 3024), H = ArgInt("-filmH", 1964);
                float fps = ArgInt("-filmFps", 6);
                string seq = Arg("-filmSeq") ?? MotionComic.IntroSequence;
                Directory.CreateDirectory(Path.Combine(outDir, "frames"));
                Directory.CreateDirectory(Path.Combine(outDir, "full"));
                code = Capture(outDir, W, H, fps, seq, log);
            }
            catch (Exception e)
            {
                Debug.LogError("[StoryFilm] " + e);
                log.Add("FAILED: " + e);
            }
            try { File.WriteAllLines(Path.Combine(Arg("-filmOut") ?? ".", "film.log"), log); } catch (Exception) { }
            EditorApplication.Exit(code);
        }

        static int Capture(string outDir, int W, int H, float fps, string seq, List<string> log)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            McTable table = MotionComic.LoadTable();
            McShot[] shots = MotionComic.Shots(table, seq);
            if (shots == null) { log.Add("no shots for " + seq); return 1; }

            var camGo = new GameObject("Film Camera");
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << 5;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.orthographic = true;

            var host = new GameObject("Motion Comic", typeof(RectTransform));
            MotionComicPlayer player = host.AddComponent<MotionComicPlayer>();
            player.VoiceOn = false;
            bool ok = player.Prepare(table, shots);
            log.Add($"prepare {ok}, missing textures {player.MissingTextures}, shots {shots.Length}, seconds {MotionComic.TotalSeconds(shots):0.00}");
            player.Show();
            Image navy = FindImage(player.Canvas.transform, "Navy");
            if (navy != null) navy.color = Hole;

            // Stand-in title: the logo where TitleScreen draws it (same size, same canvas scale rule), under the film.
            var titleGo = new GameObject("Title Stand-in", typeof(RectTransform));
            titleGo.layer = 5;
            Canvas title = titleGo.AddComponent<Canvas>();
            title.renderMode = RenderMode.ScreenSpaceCamera;
            title.worldCamera = cam;
            title.planeDistance = 2f;
            title.sortingOrder = 20;
            var logo = new GameObject("Logo", typeof(RectTransform)).AddComponent<RawImage>();
            logo.gameObject.layer = 5;
            logo.transform.SetParent(titleGo.transform, false);
            logo.texture = Resources.Load<Texture2D>("UI/Logo/critical-mass-bible-logo@2x");
            logo.rectTransform.anchorMin = logo.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            logo.rectTransform.sizeDelta = TitleScreen.LogoSize;
            logo.rectTransform.anchoredPosition = new Vector2(0f, TitleScreen.LogoY);
            title.enabled = false;

            // ---- the film at the owner's size ----
            var rt = Target(W, H);
            Use(player, cam, title, rt, W, H);
            var half = new RenderTexture(W / 2, H / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            int sh = Mathf.RoundToInt(SmallW * (float)H / W);
            var small = new RenderTexture(SmallW, sh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var readSmall = new Texture2D(SmallW, sh, TextureFormat.RGB24, false);
            var readFull = new Texture2D(W, H, TextureFormat.RGB24, false);

            float total = MotionComic.TotalSeconds(shots);
            int n = Mathf.CeilToInt(total * fps) + (int)OutroFrames;
            var csv = new StringBuilder("frame,time,shot,id,local,uncovered,luma,titles\n");
            double[] starts = new double[shots.Length];
            double acc = 0;
            for (int i = 0; i < shots.Length; i++) { starts[i] = acc; acc += shots[i].seconds; }
            for (int f = 0; f < n; f++)
            {
                double T = f / (double)fps;
                int si = shots.Length - 1;
                for (int i = 0; i < shots.Length; i++) if (T < starts[i] + shots[i].seconds) { si = i; break; }
                float local = (float)Math.Min(shots[si].seconds, T - starts[si]);
                float fade = 1f;
                if (T >= total)
                {
                    float u = (float)((T - total) / Mathf.Max(0.05f, table.outroSeconds));
                    fade = 1f - MotionComic.Ease(McEase.InOutSine, u);
                    title.enabled = true;
                }
                else title.enabled = false;
                player.SetFade(fade);
                player.Render(si, local, T, false);
                Draw(cam, rt);
                Blit(rt, half, small);
                Read(small, readSmall);
                File.WriteAllBytes(Path.Combine(outDir, "frames", $"f{f:0000}.jpg"), readSmall.EncodeToJPG(82));
                if (f % 6 == 0)
                {
                    Read(rt, readFull);
                    File.WriteAllBytes(Path.Combine(outDir, "full", $"f{f:0000}.jpg"), readFull.EncodeToJPG(88));
                }
                Stats(readSmall, table, player, SmallW, sh, out int holes, out float luma);
                csv.Append(f).Append(',').Append(T.ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(si + 1).Append(',')
                   .Append(shots[si].id).Append(',').Append(local.ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(holes).Append(',')
                   .Append(luma.ToString("0.0", CultureInfo.InvariantCulture)).Append(',').Append(Titles(player, cam, W, H)).Append('\n');
            }
            File.WriteAllText(Path.Combine(outDir, "frames.csv"), csv.ToString());
            log.Add($"film: {n} frames at {W}x{H}, {fps} fps");

            // ---- coverage at five aspects (start, middle, end of every shot) ----
            var acsv = new StringBuilder("aspect,shot,id,when,local,uncovered\n");
            int bad = 0;
            title.enabled = false;
            player.SetFade(1f);
            foreach (float a in Aspects)
            {
                int ah = 400, aw = Mathf.RoundToInt(ah * a);
                var art = Target(aw, ah);
                Use(player, cam, title, art, aw, ah);
                var read = new Texture2D(aw, ah, TextureFormat.RGB24, false);
                for (int i = 0; i < shots.Length; i++)
                {
                    float[] when = { 0f, shots[i].seconds * 0.5f, shots[i].seconds };
                    string[] name = { "start", "middle", "end" };
                    for (int k = 0; k < 3; k++)
                    {
                        player.Render(i, when[k], starts[i] + when[k], false);
                        Draw(cam, art);
                        Read(art, read);
                        Stats(read, table, player, aw, ah, out int holes, out _);
                        if (holes > 0) bad++;
                        acsv.Append(a.ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(i + 1).Append(',').Append(shots[i].id).Append(',')
                            .Append(name[k]).Append(',').Append(when[k].ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(holes).Append('\n');
                    }
                }
                UnityEngine.Object.DestroyImmediate(read);
                art.Release();
            }
            File.WriteAllText(Path.Combine(outDir, "aspects.csv"), acsv.ToString());
            log.Add($"aspects: {bad} shot sample(s) with uncovered pixels");
            cam.targetTexture = null;
            player.Release();
            return 0;
        }

        static RenderTexture Target(int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            return rt;
        }

        /// <summary>Points the player's canvas (and the title stand-in) at the capture camera for a w x h target.</summary>
        static void Use(MotionComicPlayer player, UnityEngine.Camera cam, Canvas title, RenderTexture rt, int w, int h)
        {
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            player.UseCamera(cam, w, h);
            title.worldCamera = cam;
            title.scaleFactor = HudLayout.Scale(w, h);
        }

        static void Draw(UnityEngine.Camera cam, RenderTexture rt)
        {
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        static void Blit(RenderTexture src, RenderTexture half, RenderTexture small)
        {
            Graphics.Blit(src, half);
            Graphics.Blit(half, small);
        }

        static void Read(RenderTexture src, Texture2D into)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = src;
            into.ReadPixels(new Rect(0, 0, into.width, into.height), 0, 0);
            into.Apply();
            RenderTexture.active = prev;
        }

        /// <summary>Magenta pixels (the uncovered plate) inside the film window (between the bars), and mean luma.</summary>
        static void Stats(Texture2D tex, McTable table, MotionComicPlayer player, int w, int h, out int holes, out float luma)
        {
            Color32[] px = tex.GetPixels32();
            holes = 0;
            double sum = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 c = px[i];
                if (c.r > 200 && c.g < 60 && c.b > 200) holes++;
                sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
            }
            luma = (float)(sum / Math.Max(1, px.Length));
        }

        static string Titles(MotionComicPlayer player, UnityEngine.Camera cam, int W, int H)
        {
            var sb = new StringBuilder();
            var corners = new Vector3[4];
            foreach (Text t in player.Canvas.GetComponentsInChildren<Text>(false))
            {
                if (!t.enabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) continue;
                t.rectTransform.GetWorldCorners(corners);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                float pw = t.preferredWidth * t.rectTransform.localScale.x * player.Canvas.scaleFactor;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(t.gameObject.name).Append(" '").Append(t.text.Replace(',', ' ')).Append("' box ")
                  .Append(Mathf.RoundToInt(a.x)).Append(' ').Append(Mathf.RoundToInt(H - b.y)).Append(' ')
                  .Append(Mathf.RoundToInt(b.x - a.x)).Append(' ').Append(Mathf.RoundToInt(b.y - a.y)).Append(" textW ").Append(Mathf.RoundToInt(pw));
            }
            return sb.ToString();
        }

        static Image FindImage(Transform root, string name)
        {
            foreach (Image i in root.GetComponentsInChildren<Image>(true)) if (i.gameObject.name == name) return i;
            return null;
        }

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }

        static int ArgInt(string name, int fallback) =>
            int.TryParse(Arg(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : fallback;
    }
}
