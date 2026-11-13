using System.Collections.Generic;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Plays motion-comic shots (the story intro, the title's attract loop, Codex lore vignettes) on its own overlay
    /// canvas: multi-layer parallax under an eased camera (lens zoom about a point, depth-weighted dolly and pan) with a slow
    /// procedural drift, procedural life on every layer (breathing, bob, sway, cloud drift, light flicker), focus pulls
    /// between sharp and offline-blurred twins, stepped frame swaps (the spreading crack, the splitting egg), flickering
    /// speed lines, pooled particles, flashes, shakes, kinetic titles (pop, slam, rise), dissolves between shots, letterbox
    /// bars that keep one film window on every screen shape, subtitles, narration and named sound cues (clips or procedural
    /// recipes). The stage covers any aspect without stretching. The caller owns the clock: <see cref="Render"/> draws one
    /// frame for (shot index, seconds into the shot). Textures load either all at once (<see cref="Prepare"/>: tests and the
    /// capture tool) or streamed in shot order (<see cref="BeginStream"/>, then <see cref="Pump"/> every frame), so the first
    /// frame never waits for 60 images. A frame allocates nothing. View only; never touches the sim.
    /// </summary>
    public sealed class MotionComicPlayer : MonoBehaviour
    {
        public const int SortingOrder = 500;
        public const int ParticlePool = 64;
        public const int SfxVoices = 6;
        /// <summary>Shot views built per <see cref="Pump"/> call while streaming (spreads the object creation).</summary>
        public const int BuildsPerPump = 2;

        /// <summary>Reduced motion: poses sit at their settled key, the camera holds its last framing, no shake, jitter, life,
        /// speed lines or particles, titles fade without scale or tracking moves.</summary>
        public bool Reduced;
        /// <summary>Flashes capped at 20% and slowed.</summary>
        public bool ReduceFlashing;
        /// <summary>Narration on (the attract loop runs with it off).</summary>
        public bool VoiceOn = true;
        public float ShakeScale = 1f;
        /// <summary>Height of the title screen's logo centre in title canvas units (TitleScreen.LogoY plus its bob): a layer
        /// anchored "titleLogo" lands exactly on it.</summary>
        public float TitleLogoY = CriticalMass.Runtime.Hud.TitleScreen.LogoY;

        public Canvas Canvas { get; private set; }
        public int MissingTextures { get; private set; }
        public int ShownShot => _shown;
        public McShot[] Shots => _shots;
        public McTable Table => _table;
        /// <summary>Streaming: share of textures loaded (1 when prepared at once).</summary>
        public float StreamProgress => _names.Count == 0 ? 1f : 1f - _pending.Count / (float)_names.Count;
        public bool Streaming => _pending.Count > 0 || (_views != null && _built < _views.Length);

        sealed class LayerView
        {
            public McLayer L;
            public int Index;
            public RawImage Img, Blur;
            public Texture2D Tex, BlurTex;
            public Texture2D[] Frames;
            public float LastKeyT;
        }

        sealed class ShotView
        {
            public McShot S;
            public RectTransform Root;
            public CanvasGroup Group;
            public LayerView[] Layers;
            public AudioClip[] Vo;
        }

        struct Pending
        {
            public string Name;
            public ResourceRequest Req;
        }

        McTable _table;
        McShot[] _shots;
        ShotView[] _views;
        int _built;
        readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, AudioClip[]> _cueClips = new Dictionary<string, AudioClip[]>();
        readonly List<string> _names = new List<string>();
        readonly List<Pending> _pending = new List<Pending>();
        readonly List<string> _shotNames = new List<string>();

        bool _canvasBuilt;
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
        int _shown = -1, _under = -1;
        float _prevLocal = -1f;
        float _skipAlpha;
        Vector2 _viewOverride;
        static readonly Rect FullUv = new Rect(0f, 0f, 1f, 1f);

        // ---------------------------------------------------------------- lifecycle

        /// <summary>Builds the views of these shots and loads every texture and clip they use at once (tests, the capture tool).
        /// False when nothing could be shown.</summary>
        public bool Prepare(McTable table, McShot[] shots)
        {
            if (!Begin(table, shots)) return false;
            foreach (string n in _names)
            {
                if (_tex.ContainsKey(n)) continue;
                Resolve(n, Resources.Load<Texture2D>(MotionComic.TexturePath(n)));
            }
            LoadCues();
            for (int i = 0; i < shots.Length; i++) _views[i] = BuildShot(shots[i], i);
            _built = shots.Length;
            _particleRoot.SetAsLastSibling();
            return MissingTextures < _names.Count;
        }

        /// <summary>
        /// Starts loading these shots' textures in the background, in the order the film first needs them, and returns at
        /// once. Call <see cref="Pump"/> every frame; <see cref="ShotReady"/> says when a shot can be drawn. False when the
        /// table or the shots are empty.
        /// </summary>
        public bool BeginStream(McTable table, McShot[] shots)
        {
            if (!Begin(table, shots)) return false;
            foreach (string n in _names)
            {
                if (_tex.ContainsKey(n)) continue;
                ResourceRequest r = Resources.LoadAsync<Texture2D>(MotionComic.TexturePath(n));
                if (r == null) Resolve(n, null);
                else _pending.Add(new Pending { Name = n, Req = r });
            }
            LoadCues();
            return true;
        }

        bool Begin(McTable table, McShot[] shots)
        {
            Build();
            ReleaseViews();
            _table = table;
            _shots = shots;
            _pending.Clear();
            _names.Clear();
            if (table == null || shots == null || shots.Length == 0) return false;
            foreach (McShot s in shots) MotionComic.CollectTextures(s, _names);   // shot order: the first shot loads first
            MissingTextures = 0;
            _views = new ShotView[shots.Length];
            _built = 0;
            _shown = _under = -1;
            return true;
        }

        void Resolve(string n, Texture2D t)
        {
            if (t == null)
            {
                MissingTextures++;
                if (Application.isEditor) Debug.LogWarning("[MotionComic] missing layer texture Resources/" + MotionComic.TexturePath(n));
            }
            _tex[n] = t;
        }

        void LoadCues()
        {
            foreach (McCue c in _table.cues)
            {
                if (c == null || string.IsNullOrEmpty(c.id) || _cueClips.ContainsKey(c.id)) continue;
                var clips = new List<AudioClip>(4);
                if (!string.IsNullOrEmpty(c.synth))
                {
                    // Procedural recipes are rendered once per process and cached by SoundSynth.
                    for (int v = 0; v < Mathf.Clamp(c.variants, 1, 4); v++)
                    {
                        AudioClip a = SoundSynth.Clip(c.synth, v);
                        if (a != null) clips.Add(a);
                    }
                }
                if (clips.Count == 0 && c.files != null)
                    foreach (string f in c.files)
                    {
                        AudioClip a = Resources.Load<AudioClip>(MotionComic.SfxRoot + f);
                        if (a != null) clips.Add(a);
                    }
                _cueClips[c.id] = clips.ToArray();
            }
        }

        /// <summary>Streaming: takes finished loads and builds the views of shots whose textures are all in. True when done.</summary>
        public bool Pump()
        {
            if (_views == null) return true;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (!p.Req.isDone) continue;
                Resolve(p.Name, p.Req.asset as Texture2D);
                _pending.RemoveAt(i);
            }
            int builds = 0;
            while (_built < _views.Length && builds < BuildsPerPump)
            {
                _shotNames.Clear();
                MotionComic.CollectTextures(_shots[_built], _shotNames);
                foreach (string n in _shotNames) if (!_tex.ContainsKey(n)) return false;
                _views[_built] = BuildShot(_shots[_built], _built);
                _built++;
                builds++;
            }
            return _built >= _views.Length && _pending.Count == 0;
        }

        public bool ShotReady(int i) => _views != null && i >= 0 && i < _views.Length && _views[i] != null;

        public void Show()
        {
            Build();
            Canvas.enabled = true;
            _group.alpha = 1f;
            _prevLocal = -1f;
        }

        /// <summary>Whole-canvas opacity (the open from navy and the dissolve into the title screen).</summary>
        public void SetFade(float a)
        {
            if (_group != null) _group.alpha = a;
        }

        public void SetSkipText(string s)
        {
            if (_skip != null) HudText.Set(_skip, s);
        }

        /// <summary>Skip prompt opacity 0..1 (the intro shows it once skipping works; the attract loop never does).</summary>
        public void SetSkipAlpha(float a) => _skipAlpha = Mathf.Clamp01(a);

        public void StopVoice()
        {
            if (_voice != null) _voice.Stop();
        }

        public void Hide()
        {
            if (!_canvasBuilt) return;
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
            // Loads still in flight are unloaded as they land.
            foreach (Pending p in _pending)
            {
                string name = p.Name;
                p.Req.completed += op =>
                {
                    if (Application.isPlaying && name.IndexOf('/') < 0 && op is ResourceRequest rr && rr.asset is Texture2D t) Resources.UnloadAsset(t);
                };
            }
            _pending.Clear();
            _names.Clear();
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
            _built = 0;
            _shown = _under = -1;
        }

        // ---------------------------------------------------------------- frame

        /// <summary>Draws shot <paramref name="index"/> at <paramref name="t"/> seconds into it; <paramref name="total"/> is the
        /// time since the film began. Cues whose time was crossed since the last frame fire when audio is on. A shot that has
        /// not streamed in yet draws nothing new (the last frame stays).</summary>
        public void Render(int index, float t, double total, bool audio)
        {
            if (_views == null || index < 0 || index >= _views.Length || _views[index] == null) return;
            ShotView v = _views[index];
            McShot s = v.S;
            if (index != _shown) EnterShot(index);
            UpdateStageScale();
            Vector2 view = ViewSize;

            // A dissolve: the previous shot holds its last frame (still living) under this one while this one fades in.
            bool dissolve = s.trans == "dissolve" && s.transDur > 0f && t < s.transDur && index > 0 && _views[index - 1] != null;
            int under = dissolve ? index - 1 : -1;
            if (under != _under)
            {
                if (_under >= 0 && _under != _shown && _under < _views.Length && _views[_under] != null) _views[_under].Root.gameObject.SetActive(false);
                _under = under;
                if (under >= 0) _views[under].Root.gameObject.SetActive(true);
            }
            if (dissolve)
            {
                ShotView u = _views[under];
                float ue = u.S.seconds;
                MotionComic.Cam ucam = MotionComic.ShotCam(u.S, ue + t, Reduced);
                for (int i = 0; i < u.Layers.Length; i++) RenderLayer(u.Layers[i], ucam, Vector2.zero, ue, ue + t, view);
                u.Group.alpha = 1f;
                v.Group.alpha = MotionComic.Ease(McEase.InOutSine, t / s.transDur);
            }
            else v.Group.alpha = 1f;

            MotionComic.Cam cam = MotionComic.ShotCam(s, t, Reduced);
            Vector2 shake = Reduced ? Vector2.zero : MotionComic.Shake(s, t) * ShakeScale;
            for (int i = 0; i < v.Layers.Length; i++) RenderLayer(v.Layers[i], cam, shake, t, t, view);
            RenderParticles(s, cam, shake, t);

            float fa = MotionComic.Flash(s, t, ReduceFlashing, out Color fc);
            _flash.enabled = fa > 0.003f;
            if (_flash.enabled) _flash.color = new Color(fc.r, fc.g, fc.b, fa);

            float bh = MotionComic.BarHeight(_table, view, MotionComic.SampleBars(s.bars, t));
            _barTop.rectTransform.sizeDelta = new Vector2(0f, bh);
            _barBottom.rectTransform.sizeDelta = new Vector2(0f, bh);

            for (int k = 0; k < MotionComic.MaxTitles; k++)
            {
                if (k < s.titles.Length) RenderTitle(k, s.titles[k], t, shake);
                else if (_titles[k].enabled) _titles[k].enabled = false;
            }
            bool subOn = !string.IsNullOrEmpty(s.sub.text) && t >= s.sub.at && t <= s.sub.@out;
            if (_sub.enabled != subOn) _sub.enabled = subOn;
            if (subOn)
            {
                float sa = Mathf.Clamp01((t - s.sub.at) / 0.15f) * Mathf.Clamp01((s.sub.@out - t) / 0.15f);
                _sub.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, sa);
            }

            _skip.enabled = _skipAlpha > 0.003f;
            if (_skip.enabled) _skip.color = CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.85f * _skipAlpha);

            if (audio) FireCues(v, _prevLocal, t);
            _prevLocal = t;
        }

        void EnterShot(int index)
        {
            McShot ns = _views[index].S;
            bool keepPrev = ns.trans == "dissolve" && ns.transDur > 0f && _shown == index - 1;
            if (_under >= 0 && _under != index && _under < _views.Length && _views[_under] != null) _views[_under].Root.gameObject.SetActive(false);
            _under = -1;
            if (_shown >= 0 && _shown != index && _shown < _views.Length && _views[_shown] != null)
            {
                if (keepPrev) _under = _shown;   // Render keeps it under the dissolve, then hides it
                else _views[_shown].Root.gameObject.SetActive(false);
            }
            _shown = index;
            ShotView v = _views[index];
            v.Root.gameObject.SetActive(true);
            for (int k = 0; k < MotionComic.MaxTitles; k++)
            {
                if (k >= ns.titles.Length) continue;
                McTitle ti = ns.titles[k];
                Text tx = _titles[k];
                tx.font = CMTokens.Font(ti.font == "ui" ? CMTokens.Ui : CMTokens.Display);
                tx.fontSize = Mathf.Clamp(Mathf.RoundToInt(ti.size), 12, 200);
                HudText.Set(tx, ti.text ?? string.Empty);
                tx.rectTransform.sizeDelta = new Vector2(1900f, ti.size * 1.7f);
                _titleOutlines[k].Radius = Mathf.Max(3f, ti.size / 16f);
                _tracks[k].Spacing = Reduced ? ti.track1 : ti.track0;
                tx.enabled = false;
            }
            HudText.Set(_sub, ns.sub.text ?? string.Empty);
            _sub.enabled = false;
            _prevLocal = -1f;
        }

        void RenderLayer(LayerView lv, in MotionComic.Cam cam, Vector2 shake, float t, float lifeT, Vector2 view)
        {
            McLayer l = lv.L;
            Texture2D tex = lv.Tex;
            if (lv.Frames.Length > 0)
            {
                int f = (int)(Mathf.Max(0f, t - l.frameAt) * l.fps);
                tex = lv.Frames[Mathf.Min(f, lv.Frames.Length - 1)];
            }
            bool solid = l.tex == MotionComic.Solid;
            float aspect = tex != null ? (float)tex.height / tex.width : 1f;
            MotionComic.Pose p = MotionComic.LayerPose(_table, l, lv.Index, cam, shake, t, lifeT, Reduced, lv.LastKeyT, aspect, view, TitleLogoY);
            if (!p.On || p.A <= 0.002f || (tex == null && !solid))
            {
                if (lv.Img.enabled) lv.Img.enabled = false;
                if (lv.Blur != null && lv.Blur.enabled) lv.Blur.enabled = false;
                return;
            }
            float a = p.A;
            Rect uv = Uv(l, t);
            if (lv.Blur != null)
            {
                if (lv.BlurTex != null && p.Blur > 0.001f)
                {
                    Place(lv.Blur, lv.BlurTex, p.P, p.W, p.H, p.Rot, uv, l.Tint, a);
                    a *= 1f - Mathf.Clamp01(p.Blur);
                }
                else if (lv.Blur.enabled) lv.Blur.enabled = false;
            }
            Place(lv.Img, tex, p.P, p.W, p.H, p.Rot, uv, l.Tint, a);
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
                        x += 9f * Mathf.Sin(age * 1.7f + h2 * 6.28f);   // motes wander instead of rising on rails
                        Vector2 sp = MotionComic.ToScreen(cam, P.depth, x, y, out float z) + shake * (0.4f + 0.6f * P.depth);
                        float size = P.size * (0.6f + 0.8f * h3) * z;
                        Place(_parts[used++], pt, sp, size, size, age * 40f * (h4 - 0.5f), FullUv, P.Color, Mathf.Sin(Mathf.PI * age / P.life));
                    }
                }
            }
            for (int i = used; i < ParticlePool; i++)
                if (_parts[i].enabled) _parts[i].enabled = false;
        }

        void RenderTitle(int k, McTitle ti, float t, Vector2 shake)
        {
            Text tx = _titles[k];
            bool on = t >= ti.at && t <= ti.@out;
            if (!on)
            {
                if (tx.enabled) tx.enabled = false;
                return;
            }
            if (!tx.enabled) tx.enabled = true;
            float since = t - ti.at, left = ti.@out - t;
            float outU = Mathf.Clamp01(left / 0.22f);
            float a, sc, dy = 0f;
            switch (ti.style)
            {
                case "slam":
                {
                    // Hits from big in 0.16 s, a small rebound, then a slow push while it holds.
                    a = Mathf.Clamp01(since / 0.06f);
                    float u = since / 0.16f;
                    sc = u < 1f ? Mathf.Lerp(1.55f, 0.96f, MotionComic.Ease(McEase.InCubic, u))
                                : Mathf.Lerp(0.96f, 1f, MotionComic.Ease(McEase.OutBackSoft, (since - 0.16f) / 0.22f));
                    sc *= 1f + 0.02f * Mathf.Clamp01((since - 0.38f) / 2.5f);
                    break;
                }
                case "rise":
                    a = MotionComic.Ease(McEase.OutCubic, since / 0.6f);
                    dy = -26f * (1f - MotionComic.Ease(McEase.OutQuint, since / 0.9f));
                    sc = 1f + 0.015f * Mathf.Clamp01(since / 3f);
                    break;
                default:
                    a = MotionComic.Ease(McEase.OutCubic, since / 0.25f);
                    sc = Mathf.LerpUnclamped(1.18f, 1f, MotionComic.Ease(McEase.OutBackSoft, since / 0.45f));
                    sc *= 1f + 0.015f * Mathf.Clamp01((since - 0.45f) / 2.5f);
                    break;
            }
            a *= MotionComic.Ease(McEase.OutCubic, outU);
            dy += 14f * (1f - outU);   // leaves upward
            if (Reduced)
            {
                sc = 1f;
                dy = 0f;
            }
            else _tracks[k].Spacing = Mathf.Lerp(ti.track0, ti.track1, MotionComic.Ease(McEase.OutQuint, since / 0.7f));
            // Titles are placed in stage units and scale with the stage, so they keep their place on the art at any aspect.
            float s = _stage != null ? _stage.localScale.x : 1f;
            RectTransform rt = tx.rectTransform;
            rt.anchoredPosition = (new Vector2(ti.x, ti.y + dy) + shake * 0.25f) * s;
            rt.localScale = new Vector3(sc * s, sc * s, 1f);
            tx.color = new Color(ti.Color.r, ti.Color.g, ti.Color.b, a);
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
                    MusicDirector.Duck(v.Vo[k].length + 0.3f);
                }
            }
            for (int k = 0; k < s.sfx.Length; k++)
            {
                float at = s.sfx[k].at;
                if (at <= prev || at > t) continue;
                PlayCue(s.sfx[k].cue, k);
            }
        }

        /// <summary>Plays a named cue from the table's cue list; a missing cue or clip is a silent no-op.</summary>
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

        /// <summary>The canvas size in canvas units (always 1080 tall; the width follows the screen's aspect).</summary>
        public Vector2 ViewSize
        {
            get
            {
                if (_viewOverride.x > 0f) return _viewOverride;
                Rect r = _root != null ? _root.rect : default;
                if (r.width >= 1f && r.height >= 1f) return r.size;
                return new Vector2(1080f * Screen.width / Mathf.Max(1f, Screen.height), 1080f);
            }
        }

        void UpdateStageScale()
        {
            Vector2 v = ViewSize;
            if (v.x < 1f || v.y < 1f) return;
            float s = MotionComic.StageScale(_table, v);
            if (Mathf.Abs(_stage.localScale.x - s) > 1e-4f) _stage.localScale = new Vector3(s, s, 1f);
        }

        // ---------------------------------------------------------------- build

        ShotView BuildShot(McShot s, int index)
        {
            var v = new ShotView { S = s, Layers = new LayerView[s.layers.Length], Vo = new AudioClip[s.vo.Length] };
            v.Root = NewRect(_stage, "Shot " + (index + 1) + " " + s.id);
            v.Root.sizeDelta = new Vector2(_table.stageW, _table.stageH);
            v.Group = v.Root.gameObject.AddComponent<CanvasGroup>();
            v.Group.interactable = false;
            v.Group.blocksRaycasts = false;
            for (int i = 0; i < s.layers.Length; i++)
            {
                McLayer l = s.layers[i];
                var lv = new LayerView { L = l, Index = i, LastKeyT = l.keys[l.keys.Length - 1].t };
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
            _particleRoot.SetAsLastSibling();   // particles draw over every shot, however late it was built
            return v;
        }

        Texture2D Get(string name) => !string.IsNullOrEmpty(name) && _tex.TryGetValue(name, out Texture2D t) ? t : null;

        void Build()
        {
            if (_canvasBuilt) return;
            _canvasBuilt = true;
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
            _group.blocksRaycasts = true;   // a click to skip never reaches a button under the film
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            Image navy = NewRect(_root, "Navy").gameObject.AddComponent<Image>();
            Stretch(navy.rectTransform);
            navy.color = CMTokens.VoidNavy.Color;
            navy.raycastTarget = true;

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

            RectTransform titleSpace = NewRect(_ui, "Titles");
            for (int k = 0; k < MotionComic.MaxTitles; k++)
            {
                Text tx = CriticalMass.Runtime.Menu.MenuUi.Fit(HudText.Make(titleSpace, "Title " + (k + 1), CMTokens.Display, UiScale.TypeHero, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 0f), 96);
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
            srt.anchoredPosition = new Vector2(0f, 40f);
            srt.sizeDelta = new Vector2(1400f, 60f);
            _sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            _sub.enabled = false;

            _skip = HudText.Make(_ui, "Skip", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleRight, 2f);
            RectTransform krt = _skip.rectTransform;
            krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(1f, 0f);
            krt.anchoredPosition = new Vector2(-48f, 50f);
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
