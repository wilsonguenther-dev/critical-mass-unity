using System;
using System.IO;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CriticalMass.Runtime.Menus.Story
{
    /// <summary>
    /// Story intro 3 (plan intro-film-1): the intro is one pre-rendered film, picture and sound locked in a single file
    /// (StreamingAssets/Story/intro-film-1.mp4, 2560x1440, 30 fps, a fixed black 2.0:1 frame baked in), played by Unity's
    /// VideoPlayer. The engine draws only what must follow the player's screen and settings: the subtitles (in the black
    /// under the picture, keyed to the video clock), the title logo (pixel-locked to the title screen's logo) and the skip
    /// prompt. The shot table the film was rendered from ships as Resources/Story/intro-film.json; tests read it.
    /// If the film is missing or the platform cannot decode it, <see cref="StoryIntro"/> falls back to the motion comic.
    /// </summary>
    public static class IntroFilm
    {
        public const string FileName = "Story/intro-film-1.mp4";
        public const string TableResource = "Story/intro-film";
        public const float Seconds = 1420f / 30f;              // 47.333 s
        public const float HandoffStart = 46.50f;               // background dissolve to the title starts here
        public const float HandoffSeconds = Seconds - HandoffStart;
        public const float SkipPictureFade = 0.3f, SkipAudioFade = 0.4f;
        public const float OpenBlackSeconds = 0.35f;            // black while the first frame prepares
        public const float PrepareTimeout = 4f;                 // not ready by then: the motion comic plays instead
        public const float WorldGainRampSeconds = 1.5f;         // world sounds return over this after the film
        public const float VideoAspect = 16f / 9f;              // the container; the picture inside it is 2.0:1
        public const float PictureAspect = 2f;
        public const float SubtitleFade = 0.15f;
        /// <summary>music-title.ogg file offset that the film's own crossfade reaches on its last frame; MusicDirector
        /// continues the title loop from exactly here, so the hand-off has no seam (tools/film/build_soundtrack.py).</summary>
        public const float TitleMusicOffsetAtEnd = 28f + (Seconds - 45.80f);
        public const float LogoIn = 43.17f, LogoLand = 43.50f, LogoSettle = 0.5f, LogoFrom = 1.6f, LogoLandScale = 1.04f;

        public static string Url => Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>H.264 in an .mp4 decodes through the OS on macOS (AVFoundation) and Windows (Media Foundation). The
        /// Linux editor and player have no H.264 decoder (Unity: VP8/WebM only), so there the motion comic plays the intro
        /// without ever asking VideoPlayer (which would log an error per attempt).</summary>
        public static bool PlatformDecodes(RuntimePlatform p) =>
            p == RuntimePlatform.OSXEditor || p == RuntimePlatform.OSXPlayer ||
            p == RuntimePlatform.WindowsEditor || p == RuntimePlatform.WindowsPlayer;

        /// <summary>True when the film file is present and this platform can decode it (desktop builds and the editor
        /// read StreamingAssets directly).</summary>
        public static bool Available
        {
            get
            {
                if (!PlatformDecodes(Application.platform)) return false;
                try { return File.Exists(Url); }
                catch (Exception) { return false; }
            }
        }

        [Serializable] public sealed class Sub { public string text; public float t_in, t_out; public string clip; }
        [Serializable] public sealed class Title { public string text; public float t_in, t_out, size, x, y; public string style; }
        [Serializable] public sealed class Light { public float t, peak; public int up, down; public string src; }
        [Serializable] public sealed class Shake { public float t, px, dur; }
        [Serializable] public sealed class Shot { public string id, @out; public float t0, t1, dur; }
        [Serializable] public sealed class Pic { public int width, height, bar; public float aspect; }

        [Serializable]
        public sealed class Table
        {
            public string schema;
            public int fps, frames, width, height, bpm;
            public float seconds, beat, bar_seconds, skip_lock_seconds, handoff_dissolve;
            public Pic picture;
            public float[] downbeats, cuts, hard_cuts;
            public Shot[] shots;
            public Light[] light_events;
            public Shake[] shakes;
            public Title[] titles;
            public Sub[] subtitles;
        }

        static Table _table;

        public static Table LoadTable()
        {
            if (_table != null) return _table;
            var ta = Resources.Load<TextAsset>(TableResource);
            if (ta == null) return null;
            _table = JsonUtility.FromJson<Table>(ta.text);
            return _table;
        }

        /// <summary>The subtitle line and its alpha at film time t (0.15 s fades), or null.</summary>
        public static string SubtitleAt(Table t, double time, out float alpha)
        {
            alpha = 0f;
            if (t?.subtitles == null) return null;
            foreach (Sub s in t.subtitles)
            {
                if (time < s.t_in || time > s.t_out) continue;
                alpha = Mathf.Clamp01((float)(time - s.t_in) / SubtitleFade) * Mathf.Clamp01((float)(s.t_out - time) / SubtitleFade);
                return s.text;
            }
            return null;
        }

        /// <summary>The title logo's scale and alpha at film time t: flies in 43.17-43.50, lands on the downbeat at 1.04,
        /// settles to 1.00 over 0.5 s. Never above 1.6, never a flash.</summary>
        public static void LogoPose(double time, out float scale, out float alpha)
        {
            float t = (float)time;
            if (t < LogoIn) { scale = LogoFrom; alpha = 0f; return; }
            if (t < LogoLand)
            {
                float u = (t - LogoIn) / (LogoLand - LogoIn);
                scale = LogoFrom + (LogoLandScale - LogoFrom) * u * u * u;
                alpha = 1f - (1f - u) * (1f - u) * (1f - u);
                return;
            }
            float v = Mathf.Clamp01((t - LogoLand) / LogoSettle);
            scale = LogoLandScale + (1f - LogoLandScale) * (1f - (1f - v) * (1f - v) * (1f - v));
            alpha = 1f;
        }

        /// <summary>Picture alpha of the film over the title during the hand-off (1 until 46.50, then 0 at the end).</summary>
        public static float HandoffAlpha(double time)
        {
            float u = Mathf.Clamp01((float)(time - HandoffStart) / HandoffSeconds);
            return 1f - (u * u * (3f - 2f * u));
        }

        /// <summary>World-sound gain t seconds after the film ended (a linear ramp over 1.5 s).</summary>
        public static float WorldGainAfter(float t) => Mathf.Clamp01(t / WorldGainRampSeconds);

        /// <summary>Where the title screen's logo sits in a film canvas of size view (1080 units tall, width follows the
        /// screen): centre offset from the canvas centre and size. Title canvas units map to film units by min(w/1920, 1),
        /// the same mapping the motion comic used to land its logo on the title's.</summary>
        public static void LogoRect(Vector2 view, float titleLogoY, out Vector2 centre, out Vector2 size)
        {
            float k = Mathf.Min(view.x / 1920f, 1f);
            size = Hud.TitleScreen.LogoSize * k;
            centre = new Vector2(0f, titleLogoY * k);
        }

        /// <summary>The video rect for a screen (FitInside: the whole 16:9 container visible, black around it) and the
        /// 2.0:1 picture inside it, in pixels from the bottom-left.</summary>
        public static void FitInside(float screenW, float screenH, out Rect video, out Rect picture)
        {
            float w = screenW, h = screenW / VideoAspect;
            if (h > screenH) { h = screenH; w = screenH * VideoAspect; }
            video = new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
            float ph = w / PictureAspect;
            picture = new Rect(video.x, video.y + (h - ph) * 0.5f, w, ph);
        }
    }

    /// <summary>Plays the intro film (see <see cref="IntroFilm"/>). Owned and driven by StoryIntro.</summary>
    public sealed class IntroFilmPlayer : MonoBehaviour
    {
        public enum FilmState { Idle, Preparing, Playing, Failed, Done }

        public FilmState State { get; private set; } = FilmState.Idle;
        public double Time => _vp != null && State == FilmState.Playing ? _vp.time : 0.0;
        public bool Subtitles = true;
        /// <summary>The video reached its last frame.</summary>
        public bool Ended { get; private set; }
        /// <summary>Height of the title screen's logo centre in title canvas units (its bob included when a title is
        /// frozen under the film); the film's logo lands exactly on it, as the motion comic's did.</summary>
        public Func<float> TitleLogoY = () => Hud.TitleScreen.LogoY;

        public const int SortingOrder = 520;
        VideoPlayer _vp;
        AudioSource _audio;
        RenderTexture _rt;
        Canvas _canvas;
        CanvasGroup _group, _logoGroup;
        RectTransform _root, _videoRect;
        RawImage _video, _logo, _logoGlow;
        RectTransform _logoRoot;
        Image _black;
        Text _sub, _skip;
        float _prepareT, _fadeT = -1f, _audioGain = 1f;
        Action _fadeDone;

        public void Begin(string url)
        {
            Build();
            Release();
            State = FilmState.Preparing;
            _prepareT = 0f;
            _fadeT = -1f;
            _audioGain = 1f;
            _canvas.enabled = true;
            _group.alpha = 1f;
            _video.enabled = false;
            _video.color = Color.white;
            _logoGroup.alpha = 0f;
            _sub.enabled = false;
            _skip.enabled = false;
            _vp = gameObject.AddComponent<VideoPlayer>();
            _vp.playOnAwake = false;
            _vp.source = VideoSource.Url;
            _vp.url = url;
            _vp.isLooping = false;
            _vp.skipOnDrop = true;
            _vp.waitForFirstFrame = true;
            _vp.renderMode = VideoRenderMode.RenderTexture;
            _vp.aspectRatio = VideoAspectRatio.FitInside;
            _rt = new RenderTexture(2560, 1440, 0, RenderTextureFormat.ARGB32) { name = "Intro Film" };
            _vp.targetTexture = _rt;
            _video.texture = _rt;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.priority = 0;
            _audio.volume = 1f;
            _audio.outputAudioMixerGroup = GameSettings.MusicGroup;
            _vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _vp.controlledAudioTrackCount = 1;
            _vp.EnableAudioTrack(0, true);
            _vp.SetTargetAudioSource(0, _audio);
            Ended = false;
            _vp.errorReceived += OnError;
            _vp.prepareCompleted += OnPrepared;
            _vp.loopPointReached += OnEnd;
            _vp.Prepare();
        }

        void OnError(VideoPlayer vp, string message)
        {
            Debug.LogWarning("[intro film] " + message);
            if (State == FilmState.Preparing || State == FilmState.Playing) State = FilmState.Failed;
        }

        void OnEnd(VideoPlayer vp) => Ended = true;

        void OnPrepared(VideoPlayer vp)
        {
            if (State != FilmState.Preparing) return;
            _vp.Play();
            _video.enabled = true;
            State = FilmState.Playing;
        }

        /// <summary>Per frame while playing. Returns false once the film has failed (caller falls back).</summary>
        public void Tick(float dt)
        {
            if (State == FilmState.Preparing)
            {
                _prepareT += dt;
                if (_prepareT > IntroFilm.PrepareTimeout) State = FilmState.Failed;
            }
            if (_fadeT >= 0f)
            {
                _fadeT += dt;
                _group.alpha = 1f - Mathf.Clamp01(_fadeT / IntroFilm.SkipPictureFade);
                _logoGroup.alpha = Mathf.Min(_logoGroup.alpha, _group.alpha);
                float a = 1f - Mathf.Clamp01(_fadeT / IntroFilm.SkipAudioFade);
                if (_audio != null) _audio.volume = _audioGain * a * a;
                if (_fadeT >= Mathf.Max(IntroFilm.SkipPictureFade, IntroFilm.SkipAudioFade))
                {
                    Action d = _fadeDone;
                    _fadeDone = null;
                    _fadeT = -1f;
                    Stop();
                    d?.Invoke();
                }
                return;
            }
            if (State != FilmState.Playing) return;
            double t = _vp.time;
            float sa = 0f;
            string line = Subtitles ? IntroFilm.SubtitleAt(IntroFilm.LoadTable(), t, out sa) : null;
            if (line != null)
            {
                _sub.enabled = true;
                HudText.Set(_sub, line);
                Color c = _sub.color;
                c.a = sa;
                _sub.color = c;
            }
            else _sub.enabled = false;
            IntroFilm.LogoPose(t, out float ls, out float la);
            _logoGroup.alpha = la;
            PlaceLogo(ls);
            _group.alpha = IntroFilm.HandoffAlpha(t);
        }

        void PlaceLogo(float scale)
        {
            if (_logoGroup.alpha <= 0f) return;
            Vector2 view = _root.rect.size;                  // film canvas units (always 1080 tall)
            IntroFilm.LogoRect(view, TitleLogoY != null ? TitleLogoY() : Hud.TitleScreen.LogoY, out Vector2 centre, out Vector2 size);
            _logoRoot.anchorMin = _logoRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _logoRoot.pivot = new Vector2(0.5f, 0.5f);
            _logoRoot.sizeDelta = size;
            _logoRoot.anchoredPosition = centre;
            _logoRoot.localScale = new Vector3(scale, scale, 1f);
        }

        public void SetSkip(string text, float alpha)
        {
            if (_skip == null) return;
            _skip.enabled = alpha > 0.001f && State == FilmState.Playing;
            if (!_skip.enabled) return;
            HudText.Set(_skip, text);
            Color c = _skip.color;
            c.a = alpha;
            _skip.color = c;
        }

        /// <summary>Skip: a 0.3 s picture dissolve and a 0.4 s audio fade, then stop (no click when the voice stops).</summary>
        public void FadeOut(Action done)
        {
            if (_fadeT >= 0f) return;
            _audioGain = _audio != null ? _audio.volume : 1f;
            _fadeT = 0f;
            _fadeDone = done;
            if (_skip != null) _skip.enabled = false;
        }

        public bool Fading => _fadeT >= 0f;

        public void Stop()
        {
            if (_vp != null) _vp.Stop();
            if (_audio != null) _audio.Stop();
            if (_canvas != null) _canvas.enabled = false;
            State = State == FilmState.Failed ? FilmState.Failed : FilmState.Done;
            Release();
        }

        public void Release()
        {
            if (_vp != null)
            {
                _vp.errorReceived -= OnError;
                _vp.prepareCompleted -= OnPrepared;
                _vp.loopPointReached -= OnEnd;
                _vp.targetTexture = null;
                Destroy(_vp);
                _vp = null;
            }
            if (_audio != null)
            {
                Destroy(_audio);
                _audio = null;
            }
            if (_video != null) _video.texture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        void OnDestroy() => Release();

        void Build()
        {
            if (_canvas != null) return;
            var go = new GameObject("Intro Film Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            // Everything that dissolves at the hand-off and on skip lives under one group; the logo has its own.
            var fade = Rect(_root, "Film");
            Stretch(fade);
            _group = fade.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = true;   // a click to skip never reaches a button under the film
            _black = fade.gameObject.AddComponent<Image>();
            _black.color = Color.black;     // pure #000 around the video on any screen shape, same as the baked bars
            _black.raycastTarget = true;

            _videoRect = Rect(fade, "Video");
            _videoRect.anchorMin = Vector2.zero;
            _videoRect.anchorMax = Vector2.one;
            _videoRect.sizeDelta = Vector2.zero;
            var fit = _videoRect.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = IntroFilm.VideoAspect;
            _video = _videoRect.gameObject.AddComponent<RawImage>();
            _video.raycastTarget = false;
            _video.color = Color.white;

            // Subtitle: in the black under the picture, anchored to the picture's bottom edge (80/1440 of the video).
            _sub = HudText.Make(_videoRect, "Subtitle", CMTokens.Ui, 30, CMTokens.WarmCream.Color, TextAnchor.UpperCenter, 0f);
            RectTransform srt = _sub.rectTransform;
            srt.anchorMin = new Vector2(0.5f, 80f / 1440f);
            srt.anchorMax = new Vector2(0.5f, 80f / 1440f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -8f);
            srt.sizeDelta = new Vector2(1500f, 48f);
            _sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            _sub.raycastTarget = false;
            _sub.enabled = false;

            _skip = HudText.Make(fade, "Skip", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.LowerRight, 2f);
            RectTransform krt = _skip.rectTransform;
            krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(1f, 0f);
            krt.anchoredPosition = new Vector2(-40f, 24f);
            krt.sizeDelta = new Vector2(500f, 40f);
            _skip.raycastTarget = false;
            _skip.enabled = false;

            var lg = Rect(_root, "Logo Group");
            Stretch(lg);
            _logoGroup = lg.gameObject.AddComponent<CanvasGroup>();
            _logoGroup.ignoreParentGroups = true;
            _logoGroup.interactable = false;
            _logoGroup.blocksRaycasts = false;
            _logoGroup.alpha = 0f;
            _logoRoot = Rect(lg, "Logo Root");
            _logoGlow = Rect(_logoRoot, "Logo Glow").gameObject.AddComponent<RawImage>();
            _logo = Rect(_logoRoot, "Logo").gameObject.AddComponent<RawImage>();
            Stretch(_logoGlow.rectTransform);
            Stretch(_logo.rectTransform);
            _logo.raycastTarget = _logoGlow.raycastTarget = false;
            string tier = Screen.height > 1440 ? "4x" : Screen.height > 720 ? "2x" : "1x";
            _logo.texture = Resources.Load<Texture2D>("UI/Logo/critical-mass-bible-logo@" + tier);
            _logoGlow.texture = Resources.Load<Texture2D>("UI/Logo/critical-mass-bible-logo-glow@" + tier);
            _logoGlow.color = new Color(1f, 1f, 1f, 0.6f);
            _canvas.enabled = false;
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.layer = 5;
            g.transform.SetParent(parent, false);
            return (RectTransform)g.transform;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero;
            r.anchoredPosition = Vector2.zero;
        }
    }
}
