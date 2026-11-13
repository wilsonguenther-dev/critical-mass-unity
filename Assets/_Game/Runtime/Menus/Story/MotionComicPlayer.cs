using System.Collections.Generic;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Plays motion-comic shots (the story intro, the title's attract loop, Codex lore vignettes) on its own overlay
    /// canvas: multi-layer parallax under an eased camera (lens zoom about a point, depth-weighted dolly and pan), focus
    /// pulls between sharp and offline-blurred twins, stepped frame swaps (the spreading crack, the splitting egg),
    /// flickering speed lines, pulsing rim glows, pooled particles, flashes, shakes, kinetic titles with animated tracking,
    /// letterbox bars, subtitles, narration and named sound cues. The caller owns the clock: <see cref="Render"/> draws one
    /// frame for (shot index, seconds into the shot). Everything is built and loaded in <see cref="Prepare"/>; a frame
    /// allocates nothing. View only; never touches the sim.
    /// </summary>
    public sealed class MotionComicPlayer : MonoBehaviour
    {
        public const int SortingOrder = 500;
        public const int ParticlePool = 64;
        public const int SfxVoices = 4;
        const float TitleIn = 0.5f, TitleOut = 0.3f;

        /// <summary>Reduced motion: poses sit at their settled key, the camera holds its last framing, no shake, jitter,
        /// speed lines or particles, titles fade without scale or tracking moves.</summary>
        public bool Reduced;
        /// <summary>Flashes capped at 20% and slowed.</summary>
        public bool ReduceFlashing;
        /// <summary>Narration on (the attract loop runs with it off).</summary>
        public bool VoiceOn = true;
        public float ShakeScale = 1f;

        public Canvas Canvas { get; private set; }
        public int MissingTextures { get; private set; }
        public int ShownShot => _shown;
        public McShot[] Shots => _shots;

        sealed class LayerView
        {
            public McLayer L;
            public RawImage Img, Blur;
            public Texture2D Tex, BlurTex;
            public Texture2D[] Frames;
            public float LastKeyT;
        }

        sealed class ShotView
        {
            public McShot S;
            public RectTransform Root;
            public LayerView[] Layers;
            public AudioClip[] Vo;
        }

        McTable _table;
        McShot[] _shots;
        ShotView[] _views;
        readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, AudioClip[]> _cueClips = new Dictionary<string, AudioClip[]>();
        readonly List<string> _names = new List<string>();

        bool _built;
        CanvasGroup _group;
        RectTransform _root, _stage, _ui, _particleRoot;
        Image _barTop, _barBottom, _flash;
        RawImage[] _parts;
        readonly Text[] _titles = new Text[MotionComic.MaxTitles];
        readonly TextTracking[] _tracks = new TextTracking[MotionComic.MaxTitles];
        readonly TextOutline[] _titleOutlines = new TextOutline[MotionComic.MaxTitles];
        Text _sub, _skip;
        AudioSource _voice;
        readonly AudioSource[] _sfx = new AudioSource[SfxVoices];
        int _sfxNext;
        int _shown = -1;
        float _prevLocal = -1f;
        static readonly Rect FullUv = new Rect(0f, 0f, 1f, 1f);

        // ---------------------------------------------------------------- lifecycle

        /// <summary>Builds the views of these shots and loads every texture and clip they use (once per play, before the
        /// first frame, so playback never hitches). False when nothing could be shown.</summary>
        public bool Prepare(McTable table, McShot[] shots)
        {
            Build();
            ReleaseViews();
            _table = table;
            _shots = shots;
            if (table == null || shots == null || shots.Length == 0) return false;
            _names.Clear();
            foreach (McShot s in shots) MotionComic.CollectTextures(s, _names);
            MissingTextures = 0;
            foreach (string n in _names)
            {
                if (_tex.ContainsKey(n)) continue;
                Texture2D t = Resources.Load<Texture2D>(MotionComic.TexturePath(n));
                if (t == null)
                {
                    MissingTextures++;
                    if (Application.isEditor) Debug.LogWarning("[MotionComic] missing layer texture Resources/" + MotionComic.TexturePath(n));
                }
                _tex[n] = t;
            }
            foreach (McCue c in table.cues)
            {
                if (c == null || string.IsNullOrEmpty(c.id) || _cueClips.ContainsKey(c.id) || c.files == null) continue;
                var clips = new List<AudioClip>(c.files.Length);
                foreach (string f in c.files)
                {
                    AudioClip a = Resources.Load<AudioClip>(MotionComic.SfxRoot + f);
                    if (a != null) clips.Add(a);
                }
                _cueClips[c.id] = clips.ToArray();
            }
            _views = new ShotView[shots.Length];
            for (int i = 0; i < shots.Length; i++) _views[i] = BuildShot(shots[i], i);
            _particleRoot.SetAsLastSibling();
            _shown = -1;
            return MissingTextures < _names.Count;
        }

        public void Show()
        {
            Build();
            Canvas.enabled = true;
            _group.alpha = 1f;
            _shown = -1;
            _prevLocal = -1f;
        }

        /// <summary>Whole-canvas opacity (the outro into the title screen).</summary>
        public void SetFade(float a)
        {
            if (_group != null) _group.alpha = a;
        }

        public void SetSkipText(string s)
        {
            if (_skip != null) HudText.Set(_skip, s);
        }

        public void StopVoice()
        {
            if (_voice != null) _voice.Stop();
        }

        public void Hide()
        {
            if (!_built) return;
            Canvas.enabled = false;
            StopVoice();
        }

        /// <summary>Destroys the shot views and unloads the story layers (shared textures such as the logo are left alone).</summary>
        public void Release()
        {
            ReleaseViews();
            foreach (KeyValuePair<string, Texture2D> kv in _tex)
                if (Application.isPlaying && kv.Value != null && kv.Key.IndexOf('/') < 0) Resources.UnloadAsset(kv.Value);
            _tex.Clear();
            _shots = null;
        }

        void OnDestroy() => ReleaseViews();

        void ReleaseViews()
        {
            if (_views == null) return;
            foreach (ShotView v in _views)
            {
                if (v == null || v.Root == null) continue;
                if (Application.isPlaying) Destroy(v.Root.gameObject);
                else DestroyImmediate(v.Root.gameObject);   // EditMode tests
            }
            _views = null;
            _shown = -1;
        }

        // ---------------------------------------------------------------- frame

        /// <summary>Draws shot <paramref name="index"/> at <paramref name="t"/> seconds into it; <paramref name="total"/> is the
        /// time since the play began (skip prompt). Cues whose time was crossed since the last frame fire when audio is on.</summary>
        public void Render(int index, float t, double total, bool audio)
        {
            if (_views == null || index < 0 || index >= _views.Length) return;
            ShotView v = _views[index];
            McShot s = v.S;
            if (index != _shown) EnterShot(index);
            UpdateStageScale();

            MotionComic.Cam cam = MotionComic.SampleCam(s.cam, Reduced ? s.seconds : t);
            Vector2 shake = Reduced ? Vector2.zero : Shake(s, t) * ShakeScale;
            for (int i = 0; i < v.Layers.Length; i++) RenderLayer(v.Layers[i], cam, shake, t);
            RenderParticles(s, cam, shake, t);

            float fa = 0f;
            Color fc = Color.white;
            foreach (McFlash f in s.flash)
            {
                float dur = ReduceFlashing ? f.dur * 1.6f : f.dur;
                if (dur <= 0f || t < f.t || t > f.t + dur) continue;
                float peak = ReduceFlashing ? Mathf.Min(f.peak, 0.2f) : f.peak;
                float a = peak * (1f - MotionComic.Ease(McEase.OutCubic, (t - f.t) / dur));
                if (a > fa) { fa = a; fc = f.Color; }
            }
            _flash.enabled = fa > 0.003f;
            if (_flash.enabled) _flash.color = new Color(fc.r, fc.g, fc.b, fa);

            float bh = _table.letterbox * _table.stageH * Mathf.Clamp01(MotionComic.SampleBars(s.bars, t));
            _barTop.rectTransform.sizeDelta = new Vector2(0f, bh);
            _barBottom.rectTransform.sizeDelta = new Vector2(0f, bh);

            for (int k = 0; k < MotionComic.MaxTitles; k++)
            {
                if (k < s.titles.Length) RenderTitle(k, s.titles[k], t);
                else if (_titles[k].enabled) _titles[k].enabled = false;
            }
            bool subOn = !string.IsNullOrEmpty(s.sub.text) && t >= s.sub.at && t <= s.sub.@out;
            if (_sub.enabled != subOn) _sub.enabled = subOn;

            float sk = MotionComic.SkipPromptAlpha(total, _table.skipPromptAfter);
            _skip.enabled = sk > 0.003f;
            if (_skip.enabled) _skip.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.85f * sk);

            if (audio) FireCues(v, _prevLocal, t);
            _prevLocal = t;
        }

        void EnterShot(int index)
        {
            if (_shown >= 0 && _shown < _views.Length) _views[_shown].Root.gameObject.SetActive(false);
            _shown = index;
            ShotView v = _views[index];
            v.Root.gameObject.SetActive(true);
            McShot s = v.S;
            for (int k = 0; k < MotionComic.MaxTitles; k++)
            {
                if (k >= s.titles.Length) continue;
                McTitle ti = s.titles[k];
                Text tx = _titles[k];
                tx.font = CMTokens.Font(ti.font == "ui" ? CMTokens.Ui : CMTokens.Display);
                tx.fontSize = Mathf.Clamp(Mathf.RoundToInt(ti.size), 12, 200);
                HudText.Set(tx, ti.text ?? string.Empty);
                tx.rectTransform.sizeDelta = new Vector2(1900f, ti.size * 1.7f);
                _titleOutlines[k].Radius = Mathf.Max(3f, ti.size / 16f);
                _tracks[k].Spacing = Reduced ? ti.track1 : ti.track0;
                tx.enabled = false;
            }
            HudText.Set(_sub, s.sub.text ?? string.Empty);
            _sub.enabled = false;
            _prevLocal = -1f;
        }

        void RenderLayer(LayerView lv, in MotionComic.Cam cam, Vector2 shake, float t)
        {
            McLayer l = lv.L;
            bool vis = t >= l.@from && t <= l.to;
            float x, y, s, a, r, b;
            if (Reduced)
            {
                MotionComic.Sample(l.keys, lv.LastKeyT, out x, out y, out s, out _, out r, out b);
                a = MotionComic.SampleAlpha(l.keys, t);
                if (l.flicker) vis = false;
            }
            else MotionComic.Sample(l.keys, t, out x, out y, out s, out a, out r, out b);

            bool solid = l.tex == MotionComic.Solid;
            Texture2D tex = lv.Tex;
            if (lv.Frames.Length > 0)
            {
                int f = (int)(Mathf.Max(0f, t - l.frameAt) * l.fps);
                tex = lv.Frames[Mathf.Min(f, lv.Frames.Length - 1)];
            }
            if (!vis || a <= 0.002f || (tex == null && !solid))
            {
                if (lv.Img.enabled) lv.Img.enabled = false;
                if (lv.Blur != null && lv.Blur.enabled) lv.Blur.enabled = false;
                return;
            }
            if (l.jitter > 0f && !Reduced)
            {
                float amp = l.jitter * Smooth(l.jitterFrom, l.jitterTo, t);
                x += amp * (Mathf.Sin(t * 71f) + Mathf.Sin(t * 113f + 1.3f)) * 0.5f;
                y += amp * (Mathf.Sin(t * 89f + 0.7f) + Mathf.Sin(t * 57f + 2.1f)) * 0.5f;
            }
            if (l.pulseHz > 0f) a *= l.pulseLo + (1f - l.pulseLo) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * l.pulseHz * t));

            Vector2 p = MotionComic.ToScreen(cam, l.depth, x, y, out float z) + shake * (0.4f + 0.6f * l.depth);
            float aspect = tex != null ? (float)tex.height / tex.width : 1f;
            float w = l.w * s * z;
            float h = (l.h > 0f ? l.h : l.w * aspect) * s * z;
            float rot = r + cam.Rot * l.depth;
            Rect uv = Uv(l, t);
            if (lv.Blur != null)
            {
                if (lv.BlurTex != null && b > 0.001f)
                {
                    Place(lv.Blur, lv.BlurTex, p, w, h, rot, uv, l.Tint, a);
                    a *= 1f - Mathf.Clamp01(b);
                }
                else if (lv.Blur.enabled) lv.Blur.enabled = false;
            }
            Place(lv.Img, tex, p, w, h, rot, uv, l.Tint, a);
        }

        static Rect Uv(McLayer l, float t)
        {
            bool fx = l.flip, fy = false;
            if (l.flicker)
            {
                int step = (int)(t * l.fps);
                fx ^= (step & 1) == 1;
                fy = (step & 2) != 0;
            }
            if (!fx && !fy) return FullUv;
            return new Rect(fx ? 1f : 0f, fy ? 1f : 0f, fx ? -1f : 1f, fy ? -1f : 1f);
        }

        static void Place(RawImage img, Texture tex, Vector2 p, float w, float h, float rot, Rect uv, Color tint, float a)
        {
            if (img.texture != tex) img.texture = tex;
            if (!img.enabled) img.enabled = true;
            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = p;
            rt.sizeDelta = new Vector2(w, h);
            rt.localEulerAngles = new Vector3(0f, 0f, rot);
            img.uvRect = uv;
            img.color = new Color(tint.r, tint.g, tint.b, tint.a * Mathf.Clamp01(a));
        }

        void RenderParticles(McShot s, in MotionComic.Cam cam, Vector2 shake, float t)
        {
            int used = 0;
            if (!Reduced)
            {
                for (int k = 0; k < s.particles.Length; k++)
                {
                    McParticles P = s.particles[k];
                    if (t < P.t0 || t > Mathf.Max(P.t1, P.t0 + P.life)) continue;
                    if (!_tex.TryGetValue(P.tex ?? string.Empty, out Texture2D pt) || pt == null) continue;
                    for (int i = 0; i < P.count && used < ParticlePool; i++)
                    {
                        float h0 = MotionComic.Hash01(i, 0, k), h1 = MotionComic.Hash01(i, 1, k), h2 = MotionComic.Hash01(i, 2, k);
                        float h3 = MotionComic.Hash01(i, 3, k), h4 = MotionComic.Hash01(i, 4, k);
                        float age;
                        if (P.loop)
                        {
                            if (t > P.t1) continue;
                            age = Mathf.Repeat((t - P.t0) - h0 * P.life, P.life);
                        }
                        else
                        {
                            age = t - (P.t0 + (P.t1 - P.t0) * h0);
                            if (age < 0f || age > P.life) continue;
                        }
                        float x = P.x + P.sx * (h1 - 0.5f) + (P.vx + P.jx * (h3 - 0.5f)) * age;
                        float y = P.y + P.sy * (h2 - 0.5f) + (P.vy + P.jy * (h4 - 0.5f)) * age + 0.5f * P.g * age * age;
                        Vector2 sp = MotionComic.ToScreen(cam, P.depth, x, y, out float z) + shake * (0.4f + 0.6f * P.depth);
                        float size = P.size * (0.6f + 0.8f * h3) * z;
                        Place(_parts[used++], pt, sp, size, size, 0f, FullUv, P.Color, Mathf.Sin(Mathf.PI * age / P.life));
                    }
                }
            }
            for (int i = used; i < ParticlePool; i++)
                if (_parts[i].enabled) _parts[i].enabled = false;
        }

        void RenderTitle(int k, McTitle ti, float t)
        {
            Text tx = _titles[k];
            bool on = t >= ti.at && t <= ti.@out;
            if (!on)
            {
                if (tx.enabled) tx.enabled = false;
                return;
            }
            if (!tx.enabled) tx.enabled = true;
            float uin = (t - ti.at) / TitleIn;
            float uout = Mathf.Clamp01((ti.@out - t) / TitleOut);
            float a = MotionComic.Ease(McEase.OutCubic, (t - ti.at) / 0.25f) * MotionComic.Ease(McEase.OutCubic, uout);
            float sc = 1f;
            if (!Reduced)
            {
                sc = Mathf.LerpUnclamped(1.22f, 1f, MotionComic.Ease(McEase.OutBack, uin));
                if (uout < 1f) sc *= Mathf.Lerp(1.06f, 1f, MotionComic.Ease(McEase.OutCubic, uout));
                _tracks[k].Spacing = Mathf.Lerp(ti.track0, ti.track1, MotionComic.Ease(McEase.OutCubic, uin));
            }
            RectTransform rt = tx.rectTransform;
            rt.anchoredPosition = new Vector2(ti.x, ti.y);
            rt.localScale = new Vector3(sc, sc, 1f);
            tx.color = new Color(ti.Color.r, ti.Color.g, ti.Color.b, a);
        }

        static Vector2 Shake(McShot s, float t)
        {
            Vector2 sum = Vector2.zero;
            foreach (McShake sh in s.shake)
            {
                if (sh.dur <= 0f || t < sh.t || t > sh.t + sh.dur) continue;
                float u = (t - sh.t) / sh.dur;
                float amp = sh.amp * (1f - u) * (1f - u);
                sum.x += amp * (Mathf.Sin(t * 53.1f) + Mathf.Sin(t * 31.7f + 2.1f)) * 0.5f;
                sum.y += amp * (Mathf.Sin(t * 47.3f + 1.1f) + Mathf.Sin(t * 27.9f + 0.4f)) * 0.5f;
            }
            return sum;
        }

        static float Smooth(float e0, float e1, float x)
        {
            if (e1 <= e0) return x >= e0 ? 1f : 0f;
            float u = Mathf.Clamp01((x - e0) / (e1 - e0));
            return u * u * (3f - 2f * u);
        }

        void FireCues(ShotView v, float prev, float t)
        {
            McShot s = v.S;
            if (VoiceOn)
            {
                for (int k = 0; k < s.vo.Length; k++)
                {
                    float at = s.vo[k].at;
                    if (at <= prev || at > t || v.Vo[k] == null) continue;
                    _voice.Stop();
                    _voice.clip = v.Vo[k];
                    _voice.Play();
                    MusicDirector.Duck(v.Vo[k].length + 0.4f);
                }
            }
            for (int k = 0; k < s.sfx.Length; k++)
            {
                float at = s.sfx[k].at;
                if (at <= prev || at > t) continue;
                PlayCue(s.sfx[k].cue, k);
            }
        }

        /// <summary>Plays a named cue from the table's cue list (CMSfx clips); a missing cue or clip is a silent no-op.</summary>
        public void PlayCue(string id, int salt)
        {
            if (string.IsNullOrEmpty(id) || _table == null) return;
            if (!_cueClips.TryGetValue(id, out AudioClip[] clips) || clips.Length == 0) return;
            McCue c = MotionComic.Cue(_table, id);
            if (c == null) return;
            AudioSource src = _sfx[_sfxNext];
            _sfxNext = (_sfxNext + 1) % SfxVoices;
            src.Stop();
            src.clip = clips[Mathf.Abs(salt + _shown) % clips.Length];
            src.pitch = Mathf.Clamp(c.pitch, 0.1f, 3f);
            src.volume = Mathf.Clamp01(c.gain);
            src.Play();
        }

        /// <summary>Capture and tests: draw through this camera into a w x h pixel target instead of the screen overlay (the
        /// canvas stays 1080 units tall, as on screen).</summary>
        public void UseCamera(UnityEngine.Camera cam, int pixelW, int pixelH)
        {
            Build();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = 1f;
            CanvasScaler cs = Canvas.GetComponent<CanvasScaler>();
            if (cs != null) cs.enabled = false;
            Canvas.scaleFactor = pixelH / 1080f;
            _viewOverride = new Vector2(1080f * pixelW / Mathf.Max(1f, pixelH), 1080f);
        }

        Vector2 _viewOverride;

        /// <summary>The canvas size in canvas units (1080 tall).</summary>
        public Vector2 ViewSize => _viewOverride.x > 0f ? _viewOverride : (_root != null ? _root.rect.size : new Vector2(1920f, 1080f));

        void UpdateStageScale()
        {
            Rect r = new Rect(Vector2.zero, ViewSize);
            if (r.width < 1f || r.height < 1f) return;
            float s = Mathf.Max(r.width / _table.stageW, r.height / _table.stageH);
            if (Mathf.Abs(_stage.localScale.x - s) > 1e-4f) _stage.localScale = new Vector3(s, s, 1f);
        }

        // ---------------------------------------------------------------- build

        ShotView BuildShot(McShot s, int index)
        {
            var v = new ShotView { S = s, Layers = new LayerView[s.layers.Length], Vo = new AudioClip[s.vo.Length] };
            v.Root = NewRect(_stage, "Shot " + (index + 1) + " " + s.id);
            v.Root.sizeDelta = new Vector2(_table.stageW, _table.stageH);
            for (int i = 0; i < s.layers.Length; i++)
            {
                McLayer l = s.layers[i];
                var lv = new LayerView { L = l, LastKeyT = l.keys[l.keys.Length - 1].t };
                if (!string.IsNullOrEmpty(l.blur))
                {
                    lv.Blur = NewRaw(v.Root, l.tex + " blur");
                    lv.BlurTex = Get(l.blur);
                }
                lv.Img = NewRaw(v.Root, l.tex);
                lv.Tex = l.tex == MotionComic.Solid ? null : Get(l.tex);
                lv.Frames = new Texture2D[l.frames.Length];
                for (int f = 0; f < l.frames.Length; f++) lv.Frames[f] = Get(l.frames[f]);
                v.Layers[i] = lv;
            }
            for (int k = 0; k < s.vo.Length; k++)
                v.Vo[k] = string.IsNullOrEmpty(s.vo[k].clip) ? null : Resources.Load<AudioClip>(s.vo[k].clip);
            v.Root.gameObject.SetActive(false);
            return v;
        }

        Texture2D Get(string name) => !string.IsNullOrEmpty(name) && _tex.TryGetValue(name, out Texture2D t) ? t : null;

        void Build()
        {
            if (_built) return;
            _built = true;
            var go = new GameObject("Motion Comic Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = SortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // the canvas is always 1080 units tall; the stage covers any width
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _root = (RectTransform)go.transform;

            Image navy = NewRect(_root, "Navy").gameObject.AddComponent<Image>();
            Stretch(navy.rectTransform);
            navy.color = CMTokens.VoidNavy.Color;
            navy.raycastTarget = false;

            _stage = NewRect(_root, "Stage");
            _stage.sizeDelta = new Vector2(1920f, 1080f);
            _particleRoot = NewRect(_stage, "Particles");
            _particleRoot.sizeDelta = new Vector2(1920f, 1080f);
            _parts = new RawImage[ParticlePool];
            for (int i = 0; i < ParticlePool; i++)
            {
                _parts[i] = NewRaw(_particleRoot, "P");
                _parts[i].enabled = false;
            }

            _ui = NewRect(_root, "UI");
            Stretch(_ui);
            _flash = NewRect(_ui, "Flash").gameObject.AddComponent<Image>();
            Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
            _flash.enabled = false;
            _barTop = Bar("Top Bar", 1f);
            _barBottom = Bar("Bottom Bar", 0f);

            for (int k = 0; k < MotionComic.MaxTitles; k++)
            {
                Text tx = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(_ui, "Title " + (k + 1), CMTokens.Display, UiScale.TypeHero, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 0f), 96);
                RectTransform rt = tx.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                _tracks[k] = tx.gameObject.AddComponent<TextTracking>();      // before the outline: the outline copies spaced glyphs
                _titleOutlines[k] = tx.gameObject.AddComponent<TextOutline>();
                tx.enabled = false;
                _titles[k] = tx;
            }

            _sub = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(_ui, "Subtitle", CMTokens.Ui, UiScale.TypeLead, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2f), 34);
            RectTransform srt = _sub.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = new Vector2(0f, 58f);
            srt.sizeDelta = new Vector2(1500f, 60f);
            _sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            _sub.enabled = false;

            _skip = HudText.Make(_ui, "Skip", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleRight, 2f);
            RectTransform krt = _skip.rectTransform;
            krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(1f, 0f);
            krt.anchoredPosition = new Vector2(-48f, 24f);
            krt.sizeDelta = new Vector2(600f, 40f);
            _skip.enabled = false;

            _voice = gameObject.AddComponent<AudioSource>();
            Setup(_voice);
            for (int i = 0; i < SfxVoices; i++)
            {
                _sfx[i] = gameObject.AddComponent<AudioSource>();
                Setup(_sfx[i]);
            }
            Canvas.enabled = false;
        }

        static void Setup(AudioSource a)
        {
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 0f;
            a.ignoreListenerPause = true;   // the Codex plays it from the pause menu
            a.outputAudioMixerGroup = GameSettings.EffectsGroup;
        }

        Image Bar(string name, float anchorY)
        {
            Image bar = NewRect(_ui, name).gameObject.AddComponent<Image>();
            RectTransform rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0f, anchorY);
            rt.anchorMax = new Vector2(1f, anchorY);
            rt.pivot = new Vector2(0.5f, anchorY);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 0f);
            bar.color = CMTokens.VoidNavy.Color;
            bar.raycastTarget = false;
            return bar;
        }

        static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        static RawImage NewRaw(Transform parent, string name)
        {
            RawImage img = NewRect(parent, name).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
