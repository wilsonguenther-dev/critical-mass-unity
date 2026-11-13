using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    // The motion-comic shot table (Resources "Story/motion-comic", written by Art/Story/_source~/make_shots.py and
    // editable by hand). Field names match the JSON; every field is written by the generator, the initialisers are only
    // a safety net for hand edits. Units: seconds and stage units on a 1920 x 1080 stage, origin at the centre, y up.

    /// <summary>Easing curves; every move in the comic eases (Linear is kept for holds).</summary>
    public enum McEase { InOutSine, OutSine, InSine, OutCubic, InCubic, InOutCubic, OutExpo, InExpo, OutBack, InBack, Step, Linear,
        /// <summary>Quint curves: a long, soft settle (camera moves, big slides).</summary>
        OutQuint, InOutQuint,
        /// <summary>A small overshoot (about 4%) that settles: entrances that land without bouncing like a flat card.</summary>
        OutBackSoft,
        /// <summary>Anticipation and settle in one move: a small pull back, the move, a small overshoot.</summary>
        InOutBack }

    [Serializable] public sealed class McKey { public float t, x, y, s = 1f, a = 1f, r, b; public string ease = "inOutSine"; [NonSerialized] public McEase E; }
    [Serializable] public sealed class McCamKey { public float t, zoom = 1f, zx, zy, dolly = 1f, x, y, rot; public string ease = "inOutSine"; [NonSerialized] public McEase E; }
    [Serializable] public sealed class McBarKey { public float t, v = 1f; public string ease = "linear"; [NonSerialized] public McEase E; }

    [Serializable]
    public sealed class McLayer
    {
        public string tex;
        public float depth, w, h;
        public bool flip;
        public string tint = "#FFFFFF";
        public string blur;
        public float @from;
        public float to = 999f;
        public string[] frames;
        public float frameAt, fps = 12f;
        public bool flicker;
        public float pulseHz, pulseLo = 1f, jitter, jitterFrom, jitterTo;
        /// <summary>An opaque plate that must cover the film window (the coverage test checks these alone).</summary>
        public bool cover;
        /// <summary>A subject (a character, the egg, the Kaiju, the logo): its settled box must stay inside the subject-safe box.</summary>
        public bool subject;
        /// <summary>Procedural life, always on top of the keys (off with reduced motion): breathing scale (fraction) and its
        /// rate, a slow bob (stage units), a sway (degrees), a constant drift (stage units per second: clouds) and a light
        /// flicker on the layer's opacity (0..1: glows, seams, the egg).</summary>
        public float breathe, breatheHz = 0.3f, bob, sway, driftX, driftY, glint;
        /// <summary>"titleLogo": keys are relative to where the title screen draws its logo on this screen (x, y offsets and a
        /// scale), so the film's last frame lands exactly on the title's logo.</summary>
        public string anchor;
        public McKey[] keys;
        [NonSerialized] public Color Tint = Color.white;
    }

    [Serializable]
    public sealed class McParticles
    {
        public string tex, color = "#FFFFFF";
        public float t0, t1;
        public int count;
        public float x, y, sx, sy, vx, vy, jx, jy, g, life = 3f, size = 14f;
        public bool loop = true;
        public float depth = 0.6f;
        [NonSerialized] public Color Color = Color.white;
    }

    [Serializable]
    public sealed class McTitle
    {
        public string text;
        public float at, @out, size = 96f;
        public string color = "#F7F0E5";
        public float x, y;
        public string font = "display";
        public float track0, track1;
        /// <summary>"pop" (scale down with a small overshoot), "slam" (fast hit from big, a settle and a kick: impact words),
        /// "rise" (drifts up into place: quiet words).</summary>
        public string style = "pop";
        [NonSerialized] public Color Color = Color.white;
    }

    [Serializable] public sealed class McSub { public string text; public float at, @out; }
    [Serializable] public sealed class McVo { public string clip; public float at; }
    [Serializable] public sealed class McSfx { public string cue; public float at; }
    [Serializable] public sealed class McFlash { public float t, dur, peak; public string color = "#FFFFFF"; [NonSerialized] public Color Color = Color.white; }
    [Serializable] public sealed class McShake { public float t, dur, amp; }

    [Serializable]
    public sealed class McShot
    {
        public string id;
        public float seconds;
        public string beat;
        public float boardAt;
        public McLayer[] layers;
        public McCamKey[] cam;
        public McTitle[] titles;
        public McSub sub;
        public McVo[] vo;
        public McSfx[] sfx;
        public McFlash[] flash;
        public McShake[] shake;
        public McParticles[] particles;
        public McBarKey[] bars;
        /// <summary>How the shot comes in: "cut" (default) or "dissolve" over transDur seconds (the previous shot holds its last
        /// frame underneath).</summary>
        public string trans = "cut";
        public float transDur;
        /// <summary>Slow procedural camera drift (stage units): a held shot is never frozen.</summary>
        public float camDrift = 5f;
    }

    [Serializable] public sealed class McCue
    {
        public string id; public string[] files; public float pitch = 1f, gain = 1f;
        /// <summary>A procedural recipe (SoundSynth) used instead of, or when missing, the files: heavy whooshes, blasts, the
        /// hatch, the roar.</summary>
        public string synth;
        public int variants = 1;
    }
    [Serializable] public sealed class McSequence { public string id; public string[] shots; }

    [Serializable]
    public sealed class McTable
    {
        public string schema;
        public float stageW = 1920f, stageH = 1080f, letterbox = 0.11f, skipPromptAfter = 1.5f, outroSeconds = 0.5f;
        public McCue[] cues;
        public McSequence[] sequences;
        public McSequence[] codex;
        public McShot[] shots;
    }

    /// <summary>
    /// Loading, validation and the pure maths of the motion comic (no Unity objects beyond Resources, so EditMode tests can
    /// check the table): easing, eased key sampling, the parallax camera and the deterministic particle hash. The
    /// storyboard renderer (Art/Story/_source~/make_sheets.py) implements the same rules, so its stills match the game.
    /// </summary>
    public static class MotionComic
    {
        public const string TableResource = "Story/motion-comic";
        public const string LayerRoot = "Story/";
        public const string SfxRoot = "CMSfx/";
        public const string Solid = "@solid";
        public const string IntroSequence = "intro";
        public const string AttractSequence = "attract";
        public const int MaxTitles = 2;

        /// <summary>The table from Resources, normalised; null when missing or unreadable.</summary>
        public static McTable LoadTable()
        {
            TextAsset ta = Resources.Load<TextAsset>(TableResource);
            return ta != null ? Parse(ta.text) : null;
        }

        /// <summary>Parses and normalises (null arrays become empty, eases and colours resolved once). Null on bad JSON.</summary>
        public static McTable Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            McTable t;
            try { t = JsonUtility.FromJson<McTable>(json); }
            catch (ArgumentException) { return null; }
            if (t == null || t.shots == null || t.shots.Length == 0) return null;
            if (t.cues == null) t.cues = new McCue[0];
            if (t.sequences == null) t.sequences = new McSequence[0];
            if (t.codex == null) t.codex = new McSequence[0];
            foreach (McShot s in t.shots) Normalise(s);
            return t;
        }

        static void Normalise(McShot s)
        {
            if (s.layers == null) s.layers = new McLayer[0];
            if (s.cam == null || s.cam.Length == 0) s.cam = new[] { new McCamKey() };
            if (s.titles == null) s.titles = new McTitle[0];
            if (s.sub == null) s.sub = new McSub();
            if (s.vo == null) s.vo = new McVo[0];
            if (s.sfx == null) s.sfx = new McSfx[0];
            if (s.flash == null) s.flash = new McFlash[0];
            if (s.shake == null) s.shake = new McShake[0];
            if (s.particles == null) s.particles = new McParticles[0];
            if (s.bars == null || s.bars.Length == 0) s.bars = new[] { new McBarKey() };
            foreach (McCamKey k in s.cam) { k.E = ParseEase(k.ease); if (k.zoom <= 0f) k.zoom = 1f; if (k.dolly <= 0f) k.dolly = 1f; }
            foreach (McBarKey k in s.bars) k.E = ParseEase(k.ease);
            foreach (McLayer l in s.layers)
            {
                if (l.keys == null || l.keys.Length == 0) l.keys = new[] { new McKey() };
                foreach (McKey k in l.keys) { k.E = ParseEase(k.ease); if (k.s <= 0f) k.s = 1f; }
                if (l.frames == null) l.frames = new string[0];
                if (l.fps <= 0f) l.fps = 12f;
                if (l.breatheHz <= 0f) l.breatheHz = 0.3f;
                l.Tint = Hex(l.tint, Color.white);
            }
            foreach (McParticles p in s.particles) { p.Color = Hex(p.color, Color.white); if (p.life <= 0f) p.life = 1f; }
            foreach (McTitle ti in s.titles) { ti.Color = Hex(ti.color, Color.white); if (string.IsNullOrEmpty(ti.style)) ti.style = "pop"; }
            if (string.IsNullOrEmpty(s.trans)) s.trans = "cut";
            if (s.camDrift < 0f) s.camDrift = 0f;
            foreach (McFlash f in s.flash) f.Color = Hex(f.color, Color.white);
        }

        public static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }

        public static McEase ParseEase(string name)
        {
            switch (name)
            {
                case "inOutSine": return McEase.InOutSine;
                case "outSine": return McEase.OutSine;
                case "inSine": return McEase.InSine;
                case "outCubic": return McEase.OutCubic;
                case "inCubic": return McEase.InCubic;
                case "inOutCubic": return McEase.InOutCubic;
                case "outExpo": return McEase.OutExpo;
                case "inExpo": return McEase.InExpo;
                case "outBack": return McEase.OutBack;
                case "inBack": return McEase.InBack;
                case "step": return McEase.Step;
                case "linear": return McEase.Linear;
                case "outQuint": return McEase.OutQuint;
                case "inOutQuint": return McEase.InOutQuint;
                case "outBackSoft": return McEase.OutBackSoft;
                case "inOutBack": return McEase.InOutBack;
                default: return McEase.InOutSine;
            }
        }

        public static bool IsKnownEase(string name) =>
            name == "inOutSine" || name == "outSine" || name == "inSine" || name == "outCubic" || name == "inCubic" || name == "inOutCubic" ||
            name == "outExpo" || name == "inExpo" || name == "outBack" || name == "inBack" || name == "step" || name == "linear" ||
            name == "outQuint" || name == "inOutQuint" || name == "outBackSoft" || name == "inOutBack";

        /// <summary>Eased 0..1 (OutBack overshoots above 1 before settling; InBack dips below 0).</summary>
        public static float Ease(McEase e, float t)
        {
            t = Mathf.Clamp01(t);
            switch (e)
            {
                case McEase.Linear: return t;
                case McEase.OutSine: return Mathf.Sin(t * Mathf.PI * 0.5f);
                case McEase.InSine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case McEase.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case McEase.InCubic: return t * t * t;
                case McEase.InOutCubic: { if (t < 0.5f) return 4f * t * t * t; float u = -2f * t + 2f; return 1f - u * u * u * 0.5f; }
                case McEase.OutExpo: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case McEase.InExpo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case McEase.OutBack: { const float c1 = 1.70158f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
                case McEase.InBack: { const float c1 = 1.70158f, c3 = c1 + 1f; return c3 * t * t * t - c1 * t * t; }
                case McEase.Step: return t >= 1f ? 1f : 0f;
                case McEase.OutQuint: { float u = 1f - t; return 1f - u * u * u * u * u; }
                case McEase.InOutQuint: { if (t < 0.5f) return 16f * t * t * t * t * t; float u = -2f * t + 2f; return 1f - u * u * u * u * u * 0.5f; }
                case McEase.OutBackSoft: { const float c1 = 0.9f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
                case McEase.InOutBack:
                {
                    const float c2 = 1.70158f * 0.8f;
                    if (t < 0.5f) return (2f * t) * (2f * t) * ((c2 + 1f) * 2f * t - c2) * 0.5f;
                    float u = 2f * t - 2f;
                    return (u * u * ((c2 + 1f) * u + c2) + 2f) * 0.5f;
                }
                default: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
            }
        }

        /// <summary>Layer key at time t (seconds into the shot): each key eases in from the one before it with its own curve.</summary>
        public static void Sample(McKey[] keys, float t, out float x, out float y, out float s, out float a, out float r, out float b)
        {
            McKey k0 = keys[0];
            if (t <= k0.t || keys.Length == 1) { x = k0.x; y = k0.y; s = k0.s; a = k0.a; r = k0.r; b = k0.b; return; }
            for (int i = 1; i < keys.Length; i++)
            {
                McKey p = keys[i - 1], q = keys[i];
                if (t > q.t) continue;
                float u = Ease(q.E, (t - p.t) / Mathf.Max(1e-5f, q.t - p.t));
                x = p.x + (q.x - p.x) * u; y = p.y + (q.y - p.y) * u; s = p.s + (q.s - p.s) * u;
                a = p.a + (q.a - p.a) * u; r = p.r + (q.r - p.r) * u; b = p.b + (q.b - p.b) * u;
                return;
            }
            McKey e = keys[keys.Length - 1];
            x = e.x; y = e.y; s = e.s; a = e.a; r = e.r; b = e.b;
        }

        /// <summary>Alpha alone at time t (reduced motion keeps timed appearances while poses sit at their last key).</summary>
        public static float SampleAlpha(McKey[] keys, float t)
        {
            Sample(keys, t, out _, out _, out _, out float a, out _, out _);
            return a;
        }

        public struct Cam { public float Zoom, Zx, Zy, Dolly, X, Y, Rot; }

        public static Cam SampleCam(McCamKey[] keys, float t)
        {
            McCamKey k0 = keys[0];
            if (t <= k0.t || keys.Length == 1) return new Cam { Zoom = k0.zoom, Zx = k0.zx, Zy = k0.zy, Dolly = k0.dolly, X = k0.x, Y = k0.y, Rot = k0.rot };
            for (int i = 1; i < keys.Length; i++)
            {
                McCamKey p = keys[i - 1], q = keys[i];
                if (t > q.t) continue;
                float u = Ease(q.E, (t - p.t) / Mathf.Max(1e-5f, q.t - p.t));
                return new Cam
                {
                    Zoom = p.zoom + (q.zoom - p.zoom) * u, Zx = p.zx + (q.zx - p.zx) * u, Zy = p.zy + (q.zy - p.zy) * u,
                    Dolly = p.dolly + (q.dolly - p.dolly) * u, X = p.x + (q.x - p.x) * u, Y = p.y + (q.y - p.y) * u, Rot = p.rot + (q.rot - p.rot) * u,
                };
            }
            McCamKey e = keys[keys.Length - 1];
            return new Cam { Zoom = e.zoom, Zx = e.zx, Zy = e.zy, Dolly = e.dolly, X = e.x, Y = e.y, Rot = e.rot };
        }

        public static float SampleBars(McBarKey[] keys, float t)
        {
            if (t <= keys[0].t || keys.Length == 1) return keys[0].v;
            for (int i = 1; i < keys.Length; i++)
            {
                if (t > keys[i].t) continue;
                float u = Ease(keys[i].E, (t - keys[i - 1].t) / Mathf.Max(1e-5f, keys[i].t - keys[i - 1].t));
                return keys[i - 1].v + (keys[i].v - keys[i - 1].v) * u;
            }
            return keys[keys.Length - 1].v;
        }

        /// <summary>
        /// Parallax camera: a layer at depth d (0 pinned to the screen, 1 moves fully with the camera) is panned by
        /// (x, y) * d and dollied by 1 + (dolly - 1) * d; then the lens zoom scales everything about (zx, zy).
        /// Returns the stage position and the layer's size factor.
        /// </summary>
        public static Vector2 ToScreen(in Cam c, float depth, float x, float y, out float scale)
        {
            float zd = 1f + (c.Dolly - 1f) * depth;
            float px = (x - c.X * depth) * zd, py = (y - c.Y * depth) * zd;
            scale = zd * c.Zoom;
            return new Vector2((px - c.Zx) * c.Zoom + c.Zx, (py - c.Zy) * c.Zoom + c.Zy);
        }

        /// <summary>Deterministic 0..1 hash for particle i, channel j, system k (the storyboard uses the same formula).</summary>
        public static float Hash01(int i, int j, int k)
        {
            double v = Math.Sin(i * 12.9898 + j * 78.233 + k * 37.719) * 43758.5453;
            return (float)(v - Math.Floor(v));
        }

        /// <summary>Skip prompt opacity: hidden for the first <paramref name="after"/> seconds, then eases in over 0.3 s.</summary>
        public static float SkipPromptAlpha(double time, float after) =>
            Ease(McEase.OutCubic, (float)((time - after) / 0.3));

        /// <summary>The shots of a sequence ("intro", "attract") or of a Codex entry ("relic-02"), in order; null if unknown.</summary>
        public static McShot[] Shots(McTable t, string id)
        {
            if (t == null || string.IsNullOrEmpty(id)) return null;
            McSequence seq = Find(t.sequences, id) ?? Find(t.codex, id);
            if (seq == null || seq.shots == null) return null;
            var list = new List<McShot>(seq.shots.Length);
            foreach (string sid in seq.shots)
            {
                McShot s = Shot(t, sid);
                if (s != null) list.Add(s);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        static McSequence Find(McSequence[] arr, string id)
        {
            if (arr == null) return null;
            foreach (McSequence s in arr) if (s != null && s.id == id) return s;
            return null;
        }

        public static McShot Shot(McTable t, string id)
        {
            foreach (McShot s in t.shots) if (s.id == id) return s;
            return null;
        }

        public static McCue Cue(McTable t, string id)
        {
            foreach (McCue c in t.cues) if (c.id == id) return c;
            return null;
        }

        /// <summary>Resources path of a layer texture: names with a slash are full Resources paths (the logo), others live in Story/.</summary>
        public static string TexturePath(string tex) => tex.IndexOf('/') >= 0 ? tex : LayerRoot + tex;

        /// <summary>Every texture name a shot uses (layers, blur twins, frames, particles), without duplicates or the solid fill.</summary>
        public static void CollectTextures(McShot s, List<string> into)
        {
            foreach (McLayer l in s.layers)
            {
                Add(into, l.tex);
                Add(into, l.blur);
                foreach (string f in l.frames) Add(into, f);
            }
            foreach (McParticles p in s.particles) Add(into, p.tex);
        }

        static void Add(List<string> into, string tex)
        {
            if (string.IsNullOrEmpty(tex) || tex == Solid || into.Contains(tex)) return;
            into.Add(tex);
        }

        // ---------------------------------------------------------------- framing (any aspect)

        /// <summary>Title-safe box, half extents in stage units: inside it on every screen from 4:3 to 21:9 (titles, the
        /// notch's band at the top centre of a 14-inch MacBook Pro excluded).</summary>
        public const float SafeHalfW = 680f, SafeHalfH = 380f;
        /// <summary>Subject-safe box, half extents in stage units: a subject's settled box stays fully on screen at 1.54
        /// (the owner's MacBook Pro) and loses at most a sliver at 4:3.</summary>
        public const float SubjectHalfW = 830f, SubjectHalfH = 420f;

        /// <summary>The stage (stageW x stageH) is scaled to cover the canvas, never stretched: canvas units per stage unit.</summary>
        public static float StageScale(McTable t, Vector2 view) => Mathf.Max(view.x / t.stageW, view.y / t.stageH);

        /// <summary>Height of the film window between full bars, in stage units (the 16:9 composition: 1080 less two bars).</summary>
        public static float WindowH(McTable t) => t.stageH * (1f - 2f * Mathf.Clamp01(t.letterbox));

        /// <summary>One letterbox bar's height in canvas units at bar level v: the bars keep the same film window on every
        /// screen, so a 21:9 screen (already that shape) gets none and nothing is cropped twice.</summary>
        public static float BarHeight(McTable t, Vector2 view, float v)
        {
            float s = StageScale(t, view);
            return Mathf.Max(0f, (view.y - WindowH(t) * s) * 0.5f) * Mathf.Clamp01(v);
        }

        /// <summary>The part of the stage that is on screen and not under a bar, in stage units (centred on the origin).</summary>
        public static Rect VisibleStage(McTable t, Vector2 view, float barV)
        {
            float s = StageScale(t, view);
            float hw = view.x * 0.5f / s, hh = (view.y * 0.5f - BarHeight(t, view, barV)) / s;
            return new Rect(-hw, -hh, 2f * hw, 2f * hh);
        }

        /// <summary>
        /// Where the title screen's logo sits, in stage units, on a canvas of this size: its centre height and the size
        /// factor that turns TitleScreen.LogoSize into stage units. The title canvas scales by min(w / 1920, h / 1080) while
        /// the film's canvas is 1080 tall and its stage covers, so the two differ on any screen narrower or wider than 16:9.
        /// </summary>
        public static void TitleLogo(McTable t, Vector2 view, float titleLogoY, out float y, out float scale)
        {
            float k = Mathf.Min(view.x / 1920f, 1f);   // title canvas units -> film canvas units
            float s = StageScale(t, view);
            scale = k / s;
            y = titleLogoY * scale;
        }

        // ---------------------------------------------------------------- layer pose (player and tests share it)

        public struct Pose { public Vector2 P; public float W, H, Rot, A, Blur; public bool On; }

        /// <summary>
        /// A layer's box at time t (seconds into its shot): keys, procedural life, jitter, pulse, the parallax camera and the
        /// shake. texAspect is the texture's height / width. lifeT drives the life (a dissolving shot keeps living under the
        /// next one). The logo anchor needs the canvas size and the title logo's height in title canvas units.
        /// </summary>
        public static Pose LayerPose(McTable table, McLayer l, int index, in Cam cam, Vector2 shake, float t, float lifeT, bool reduced,
            float lastKeyT, float texAspect, Vector2 view, float titleLogoY)
        {
            var o = new Pose { On = t >= l.@from && t <= l.to };
            float x, y, s, a, r, b;
            if (reduced)
            {
                Sample(l.keys, lastKeyT, out x, out y, out s, out _, out r, out b);
                a = SampleAlpha(l.keys, t);
                if (l.flicker) o.On = false;
            }
            else
            {
                Sample(l.keys, t, out x, out y, out s, out a, out r, out b);
                Life(l, index, lifeT, out float lx, out float ly, out float ls, out float lr, out float la);
                x += lx; y += ly; s *= 1f + ls; r += lr; a *= la;
                if (l.jitter > 0f)
                {
                    float amp = l.jitter * Smooth01(l.jitterFrom, l.jitterTo, t);
                    x += amp * (Mathf.Sin(t * 71f) + Mathf.Sin(t * 113f + 1.3f)) * 0.5f;
                    y += amp * (Mathf.Sin(t * 89f + 0.7f) + Mathf.Sin(t * 57f + 2.1f)) * 0.5f;
                }
            }
            if (l.pulseHz > 0f) a *= l.pulseLo + (1f - l.pulseLo) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * l.pulseHz * t));
            float h0 = l.h > 0f ? l.h : l.w * texAspect;
            if (l.anchor == "titleLogo")
            {
                TitleLogo(table, view, titleLogoY, out float ly0, out float k);
                o.P = new Vector2(x * k, ly0 + y * k) + shake * 0.4f;
                o.W = l.w * s * k;
                o.H = h0 * s * k;
                o.Rot = r;
            }
            else
            {
                o.P = ToScreen(cam, l.depth, x, y, out float z) + shake * (0.4f + 0.6f * l.depth);
                o.W = l.w * s * z;
                o.H = h0 * s * z;
                o.Rot = r + cam.Rot * l.depth;
            }
            o.A = a;
            o.Blur = b;
            return o;
        }

        /// <summary>Procedural life of a layer (see McLayer.breathe ...): offsets, a scale fraction, degrees and an opacity factor.</summary>
        public static void Life(McLayer l, int index, float t, out float dx, out float dy, out float ds, out float dr, out float da)
        {
            float ph = Hash01(index, 7, 3) * 6.2831853f;
            dx = l.driftX * t + l.bob * 0.35f * Mathf.Sin(t * 0.9f + ph * 1.7f);
            dy = l.driftY * t + l.bob * Mathf.Sin(2f * Mathf.PI * 0.21f * t + ph);
            ds = l.breathe * Mathf.Sin(2f * Mathf.PI * l.breatheHz * t + ph);
            dr = l.sway * Mathf.Sin(2f * Mathf.PI * 0.27f * t + ph * 0.5f);
            if (l.glint > 0f)
            {
                float n = 0.5f * Mathf.Sin(7.3f * t + ph) + 0.3f * Mathf.Sin(13.1f * t + 2f * ph) + 0.2f * Mathf.Sin(2.9f * t + 0.5f);
                da = 1f - l.glint * (0.5f + 0.5f * n);
            }
            else da = 1f;
        }

        /// <summary>The shot's camera at time t with its slow procedural drift (none with reduced motion: the last framing).</summary>
        public static Cam ShotCam(McShot s, float t, bool reduced)
        {
            Cam c = SampleCam(s.cam, reduced ? s.seconds : t);
            if (reduced || s.camDrift <= 0f) return c;
            float d = s.camDrift;
            c.X += d * (Mathf.Sin(2f * Mathf.PI * 0.11f * t + 0.4f) + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.23f * t + 1.9f));
            c.Y += d * 0.6f * (Mathf.Sin(2f * Mathf.PI * 0.13f * t + 2.2f) + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.29f * t + 0.7f));
            c.Rot += d * 0.02f * Mathf.Sin(2f * Mathf.PI * 0.07f * t + 1.1f);
            return c;
        }

        /// <summary>Decaying camera shake (stage units) at time t: a fast, irregular rattle that dies out quadratically.</summary>
        public static Vector2 Shake(McShot s, float t)
        {
            Vector2 sum = Vector2.zero;
            foreach (McShake sh in s.shake)
            {
                if (sh.dur <= 0f || t < sh.t || t > sh.t + sh.dur) continue;
                float u = (t - sh.t) / sh.dur;
                float amp = sh.amp * (1f - u) * (1f - u);
                float w = 2f * Mathf.PI * t;
                sum.x += amp * (0.6f * Mathf.Sin(w * 17f + 1.3f) + 0.4f * Mathf.Sin(w * 23.3f + 0.2f));
                sum.y += amp * (0.6f * Mathf.Sin(w * 19.1f + 2.4f) + 0.4f * Mathf.Sin(w * 26.7f + 0.9f));
            }
            return sum;
        }

        /// <summary>Flash opacity and colour at time t: a 40 ms rise, then an eased fall; reduce flashing caps it at 20% and slows it.</summary>
        public static float Flash(McShot s, float t, bool reduceFlashing, out Color color)
        {
            float fa = 0f;
            color = Color.white;
            foreach (McFlash f in s.flash)
            {
                float dur = reduceFlashing ? f.dur * 1.6f : f.dur;
                if (dur <= 0f || t < f.t || t > f.t + dur) continue;
                float peak = reduceFlashing ? Mathf.Min(f.peak, 0.2f) : f.peak;
                float u = t - f.t;
                const float rise = 0.04f;
                float a = u < rise ? peak * u / rise : peak * (1f - Ease(McEase.OutCubic, (u - rise) / Mathf.Max(1e-3f, dur - rise)));
                if (a > fa) { fa = a; color = f.Color; }
            }
            return fa;
        }

        static float Smooth01(float e0, float e1, float x)
        {
            if (e1 <= e0) return x >= e0 ? 1f : 0f;
            float u = Mathf.Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }

        /// <summary>True when the point (stage units) is inside the layer's rotated box.</summary>
        public static bool Contains(in Pose p, Vector2 q)
        {
            Vector2 d = q - p.P;
            float rad = -p.Rot * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            float lx = d.x * c - d.y * sn, ly = d.x * sn + d.y * c;
            return Mathf.Abs(lx) <= p.W * 0.5f && Mathf.Abs(ly) <= p.H * 0.5f;
        }

        public static float TotalSeconds(McShot[] shots)
        {
            float sum = 0f;
            if (shots != null) foreach (McShot s in shots) sum += s.seconds;
            return sum;
        }
    }

    /// <summary>
    /// The intro's skip rule, free of input devices so tests can drive it: nothing skips during the first
    /// <see cref="Lock"/> seconds the film is on screen; after that, one deliberate press of a skip key does. "Deliberate"
    /// means the key goes down after the lock: a key already held when the film began (the Enter that chose WATCH INTRO) or
    /// held through the lock has to be released and pressed again. Mouse movement is never a skip key.
    /// </summary>
    public sealed class SkipGate
    {
        public const float LockSeconds = 1.5f;
        public readonly float Lock;
        bool _wasUp;

        public SkipGate(float lockSeconds = LockSeconds) { Lock = Mathf.Max(0f, lockSeconds); }

        /// <summary>Feed one frame: seconds the film has been on screen, and whether a skip key is down this frame. True: skip.</summary>
        public bool Update(double time, bool down)
        {
            bool press = down && _wasUp && time >= Lock;
            _wasUp = !down;
            return press;
        }

        /// <summary>The skip prompt shows once skipping works.</summary>
        public bool PromptOn(double time) => time >= Lock;
    }
}
