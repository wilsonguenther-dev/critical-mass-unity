using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    // The motion-comic shot table (Resources "Story/motion-comic", written by Art/Story/_source~/make_shots.py and
    // editable by hand). Field names match the JSON; every field is written by the generator, the initialisers are only
    // a safety net for hand edits. Units: seconds and stage units on a 1920 x 1080 stage, origin at the centre, y up.

    /// <summary>Easing curves; every move in the comic eases (Linear is kept for holds).</summary>
    public enum McEase { InOutSine, OutSine, InSine, OutCubic, InCubic, InOutCubic, OutExpo, InExpo, OutBack, InBack, Step, Linear }

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
    }

    [Serializable] public sealed class McCue { public string id; public string[] files; public float pitch = 1f, gain = 1f; }
    [Serializable] public sealed class McSequence { public string id; public string[] shots; }

    [Serializable]
    public sealed class McTable
    {
        public string schema;
        public float stageW = 1920f, stageH = 1080f, letterbox = 0.11f, skipPromptAfter = 1f, outroSeconds = 0.5f;
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
                l.Tint = Hex(l.tint, Color.white);
            }
            foreach (McParticles p in s.particles) { p.Color = Hex(p.color, Color.white); if (p.life <= 0f) p.life = 1f; }
            foreach (McTitle ti in s.titles) ti.Color = Hex(ti.color, Color.white);
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
                default: return McEase.InOutSine;
            }
        }

        public static bool IsKnownEase(string name) =>
            name == "inOutSine" || name == "outSine" || name == "inSine" || name == "outCubic" || name == "inCubic" || name == "inOutCubic" ||
            name == "outExpo" || name == "inExpo" || name == "outBack" || name == "inBack" || name == "step" || name == "linear";

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

        public static float TotalSeconds(McShot[] shots)
        {
            float sum = 0f;
            if (shots != null) foreach (McShot s in shots) sum += s.seconds;
            return sum;
        }
    }
}
