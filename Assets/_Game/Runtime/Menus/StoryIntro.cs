using System;
using System.Globalization;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>One story card as the intro plays it (text, length, narration and its three parallax layers).</summary>
    public sealed class StoryCard
    {
        /// <summary>1..5.</summary>
        public int Number;
        /// <summary>"card-1" .. "card-5", the words pack id.</summary>
        public string Id;
        /// <summary>The subtitle (the narrated line).</summary>
        public string Caption;
        /// <summary>How long the card stays up.</summary>
        public float Seconds;
        /// <summary>Narration clip, or null (the card then runs its default length in silence).</summary>
        public AudioClip Narration;
        /// <summary>Layers back (opaque, moves least), mid and front (transparent, moves most); any may be null.</summary>
        public readonly Texture2D[] Layers = new Texture2D[StoryContent.LayerCount];
        /// <summary>True when any layer came from a "-placeholder" file.</summary>
        public bool PlaceholderArt;
    }

    /// <summary>
    /// Where the story intro's words, narration and art come from (DG-19). Everything has a built-in fallback, so the intro
    /// runs end to end with none of it present.
    /// <para><b>Words</b>: the words pack's story.json (DG-04, cards read by id "card-1".."card-5", caption and ticks at
    /// simHz). Read from Resources "Lore/story" when the pack is mirrored into a Resources folder, else (editor only) from
    /// Resources "Lore/story" (Assets/_Game/Resources/Lore/story.json), else the built-in lines below (the pack's own text of 2026-11-12).</para>
    /// <para><b>Narration</b>: one clip per card. Player builds load Resources "Narration/card-&lt;n&gt;" (for example
    /// Assets/_Game/Audio/Resources/Narration/card-&lt;n&gt;.ogg); the editor also accepts
    /// Assets/_Game/Audio/Narration/card-&lt;n&gt;.ogg. A card with a clip lasts the clip plus a short tail; without one, its
    /// words-pack ticks (default 150, 135, 150, 165, 90 ticks at 30 Hz = 690 frames = 23 s).</para>
    /// <para><b>Art</b>: Assets/_Game/Art/Story/Resources/StoryCards/card-&lt;n&gt;/{back,mid,front}.png (Resources
    /// "StoryCards/card-&lt;n&gt;/back" ...). The real stills replace the shipped "-placeholder" files simply by being
    /// present; the placeholder is used only when the real file is missing.</para>
    /// </summary>
    public static class StoryContent
    {
        public const int CardCount = 5;
        public const int DefaultSimHz = 30;
        public const int LayerCount = 3;
        public static readonly string[] LayerNames = { "back", "mid", "front" };
        public const string PlaceholderSuffix = "-placeholder";

        public const string LoreResource = "Lore/story";
        public const string LoreAssetPath = "Assets/_Game/Resources/Lore/story.json";
        public const string ArtResourceRoot = "StoryCards";
        public const string NarrationResourceRoot = "Narration";
        public const string NarrationAssetPathFormat = "Assets/_Game/Audio/Narration/card-{0}.ogg";
        /// <summary>Silence after a narration clip before the next card.</summary>
        public const float NarrationTailSeconds = 0.5f;
        /// <summary>No card is shorter than this, whatever its clip.</summary>
        public const float MinCardSeconds = 2f;

        /// <summary>Default card lengths in sim ticks (sum 690 = 23 s at 30 Hz), the words pack's numbers.</summary>
        public static readonly int[] DefaultTicks = { 150, 135, 150, 165, 90 };

        /// <summary>Built-in subtitles, used when the words pack is missing or a card's line is unusable.</summary>
        public static readonly string[] DefaultCaptions =
        {
            "The sky cracked. And the land rose to meet it.",
            "Two banners. One hill. Neither has learned to share.",
            "Far away, a Kaiju egg sleeps. It remembers every meal.",
            "Feed it, and it fights for you. Corrupt it, and it fights everyone.",
            "Hold the hill. Raise the Kaiju.",
        };

        [Serializable] sealed class CardJson { public string id; public int ticks; public string caption; }
        [Serializable] sealed class StoryJson { public string schema; public int simHz; public CardJson[] cards; }

        /// <summary>"card-3" -> 3; 0 when the id is not a card id.</summary>
        public static int CardNumber(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith("card-", StringComparison.Ordinal)) return 0;
            return int.TryParse(id.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= CardCount ? n : 0;
        }

        /// <summary>The words pack's story.json text, or null.</summary>
        public static string ReadLoreJson()
        {
            TextAsset ta = Resources.Load<TextAsset>(LoreResource);
            if (ta != null) return ta.text;
#if UNITY_EDITOR
            string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), LoreAssetPath);
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
#endif
            return null;
        }

        /// <summary>
        /// Fills captions and ticks (length CardCount, pre-filled by the caller with the defaults) from story.json by card
        /// id. A line that is empty or still holds a {token} keeps its default. Returns the file's simHz (default 30).
        /// </summary>
        public static int Parse(string json, string[] captions, int[] ticks)
        {
            if (string.IsNullOrEmpty(json)) return DefaultSimHz;
            StoryJson s;
            try { s = JsonUtility.FromJson<StoryJson>(json); }
            catch (ArgumentException) { return DefaultSimHz; }
            if (s == null) return DefaultSimHz;
            if (s.cards != null)
            {
                for (int i = 0; i < s.cards.Length; i++)
                {
                    CardJson c = s.cards[i];
                    if (c == null) continue;
                    int n = CardNumber(c.id);
                    if (n == 0) continue;
                    if (!string.IsNullOrWhiteSpace(c.caption) && c.caption.IndexOf('{') < 0) captions[n - 1] = c.caption.Trim();
                    if (c.ticks > 0) ticks[n - 1] = c.ticks;
                }
            }
            return s.simHz > 0 ? s.simHz : DefaultSimHz;
        }

        /// <summary>Card length: the clip plus its tail when there is a clip, else ticks / simHz.</summary>
        public static float SecondsFor(int ticks, int simHz, AudioClip clip)
        {
            if (clip != null && clip.length > 0f) return Mathf.Max(MinCardSeconds, clip.length + NarrationTailSeconds);
            return ticks / (float)Mathf.Max(1, simHz);
        }

        /// <summary>The narration clip of card n (1-based), or null.</summary>
        public static AudioClip LoadNarration(int n)
        {
            string id = "card-" + n.ToString(CultureInfo.InvariantCulture);
            AudioClip c = Resources.Load<AudioClip>(NarrationResourceRoot + "/" + id);
            // Integration batch 2: the narrator lines baked with the announcer (DG-18) live in Resources "CMVoice".
            if (c == null) c = Resources.Load<AudioClip>("CMVoice/story-" + id);
#if UNITY_EDITOR
            if (c == null) c = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(string.Format(CultureInfo.InvariantCulture, NarrationAssetPathFormat, n));
#endif
            return c;
        }

        /// <summary>One layer of card n: the real still if present, else its placeholder, else null.</summary>
        public static Texture2D LoadLayer(int n, int layer, out bool placeholder)
        {
            string stem = ArtResourceRoot + "/card-" + n.ToString(CultureInfo.InvariantCulture) + "/" + LayerNames[layer];
            placeholder = false;
            Texture2D t = Resources.Load<Texture2D>(stem);
            if (t != null) return t;
            t = Resources.Load<Texture2D>(stem + PlaceholderSuffix);
            placeholder = t != null;
            return t;
        }

        /// <summary>Text and timing only (no assets): what the timeline would play.</summary>
        public static void LoadText(string[] captions, int[] ticks, out int simHz)
        {
            for (int i = 0; i < CardCount; i++)
            {
                captions[i] = DefaultCaptions[i];
                ticks[i] = DefaultTicks[i];
            }
            simHz = Parse(ReadLoreJson(), captions, ticks);
        }

        /// <summary>All five cards with their clips and layers (called once per play, never during a match).</summary>
        public static StoryCard[] Load(bool withAssets = true)
        {
            var captions = new string[CardCount];
            var ticks = new int[CardCount];
            LoadText(captions, ticks, out int hz);
            var cards = new StoryCard[CardCount];
            for (int i = 0; i < CardCount; i++)
            {
                int n = i + 1;
                var c = new StoryCard { Number = n, Id = "card-" + n.ToString(CultureInfo.InvariantCulture), Caption = captions[i] };
                if (withAssets)
                {
                    c.Narration = LoadNarration(n);
                    for (int l = 0; l < LayerCount; l++)
                    {
                        c.Layers[l] = LoadLayer(n, l, out bool ph);
                        c.PlaceholderArt |= ph;
                    }
                }
                c.Seconds = SecondsFor(ticks[i], hz, c.Narration);
                cards[i] = c;
            }
            return cards;
        }
    }

    /// <summary>
    /// The intro's clock, free of Unity objects so tests can drive it frame by frame: card start times, the current card,
    /// progress through it and the short fade each card makes over the one before. Time is unscaled real time (the intro
    /// is view only and plays outside the sim).
    /// </summary>
    public sealed class StoryTimeline
    {
        /// <summary>Each card fades in over the previous one in this time (view transitions stay under 250 ms).</summary>
        public const double FadeSeconds = 0.24;
        const double Eps = 1e-6;

        readonly double[] _start;
        readonly double[] _len;

        public int CardCount => _len.Length;
        public double Total { get; }
        public double Time { get; private set; }
        public bool Done { get; private set; }
        public bool Skipped { get; private set; }
        /// <summary>Index of the card on screen (the last card once done).</summary>
        public int Index { get; private set; }

        public StoryTimeline(float[] seconds)
        {
            if (seconds == null || seconds.Length == 0) throw new ArgumentException("at least one card", nameof(seconds));
            _start = new double[seconds.Length];
            _len = new double[seconds.Length];
            double t = 0;
            for (int i = 0; i < seconds.Length; i++)
            {
                _start[i] = t;
                _len[i] = Math.Max(0.05, seconds[i]);
                t += _len[i];
            }
            Total = t;
        }

        public double StartOf(int i) => _start[i];
        public double LengthOf(int i) => _len[i];

        /// <summary>Moves the clock on; returns true when the card on screen changed (or the intro ended).</summary>
        public bool Advance(double dt)
        {
            if (Done) return false;
            Time += Math.Max(0, dt);
            int before = Index;
            if (Time >= Total - Eps)
            {
                Time = Total;
                Done = true;
                return true;
            }
            while (Index < _len.Length - 1 && Time >= _start[Index + 1] - Eps) Index++;
            return Index != before;
        }

        /// <summary>Ends at once (Esc, Space, A): works on frame 0.</summary>
        public void Skip()
        {
            if (Done) return;
            Done = true;
            Skipped = true;
        }

        /// <summary>Progress 0..1 through card i.</summary>
        public float Local01(int i) => (float)Math.Min(1, Math.Max(0, (Time - _start[i]) / _len[i]));

        /// <summary>Opacity of card i: the current card fades in over FadeSeconds (card 0 is at once), the one before it
        /// stays under it until that fade is over, every other card is hidden.</summary>
        public float Alpha(int i)
        {
            if (i == Index) return i == 0 ? 1f : (float)Math.Min(1, Math.Max(0, (Time - _start[i]) / FadeSeconds));
            if (i == Index - 1) return Time - _start[Index] < FadeSeconds ? 1f : 0f;
            return 0f;
        }
    }

    /// <summary>
    /// DG-19 story intro: five narrated cards (about 23 s), skippable from the first frame with Esc, Space, Enter or A (B and
    /// Start too), subtitles, and a slow three-layer parallax per card that stands still when reduced motion is on
    /// (PlayerPrefs "cm.reducedMotion" = 1). Plays once on first launch in player builds (PlayerPrefs "cm.story.seen"), and
    /// whenever the game-flow menu calls <see cref="Play"/> ("Watch the story"). View only: it never touches the sim.
    /// <para>Since the motion-comic rebuild (2026-11-12) the intro plays the shot table Resources "Story/motion-comic" through
    /// <see cref="MotionComicPlayer"/> (12 shots, about 58 s, the bible's own art; skippable with any key, "skip" prompt
    /// after 1 s, a short fade into the title at the end). The five-card path below stays as the fallback when the table
    /// or its layers are missing. The same player runs the title's attract loop (<see cref="PlayAttract"/>, narration off,
    /// any input returns to the title) and Codex lore vignettes (<see cref="PlayLore"/>).</para>
    /// <para>Public API for the game-flow item (DG-01): <see cref="Play"/>, <see cref="Skip"/>, <see cref="IsPlaying"/>,
    /// <see cref="Started"/>, <see cref="Finished"/> (argument: skipped), <see cref="PlayOnFirstLaunch"/> (set false if the
    /// flow plays it itself), <see cref="SeenKey"/>, <see cref="ReducedMotionKey"/>.</para>
    /// </summary>
    [Flow.GameOverlay(Order = 40)]   // installed by GameBootstrap through the DG-01 overlay registry
    public sealed class StoryIntro : MonoBehaviour
    {
        public const string SeenKey = "cm.story.seen";
        /// <summary>Reduced motion (int, 1 = on, default 0). Stored by the settings work; read here and by AttractMode.</summary>
        public const string ReducedMotionKey = "cm.reducedMotion";

        public static bool ReducedMotion => PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0;

        /// <summary>Play automatically on the first launch of a player build. The game-flow item may set this false.</summary>
        public static bool PlayOnFirstLaunch = true;

        public static StoryIntro Instance { get; private set; }
        public static bool IsPlaying => Instance != null && Instance._playing;
        /// <summary>True while the motion comic runs as the title's attract loop.</summary>
        public static bool IsAttract => IsPlaying && Instance._kind == PlayKind.Attract;
        /// <summary>The motion comic is playing (false while the five-card fallback plays).</summary>
        public bool ComicActive => _playing && _comicMode;
        public MotionComicPlayer Comic => _comic;
        public static event Action Started;
        /// <summary>Raised when the intro ends; true when it was skipped.</summary>
        public static event Action<bool> Finished;

        /// <summary>The cards of the current or last play.</summary>
        public StoryCard[] Cards { get; private set; }
        public StoryTimeline Timeline { get; private set; }
        public Canvas Canvas { get; private set; }

        const float ParallaxBackPx = 18f, ParallaxMidPx = 44f, ParallaxFrontPx = 86f;
        const float LayerOverscan = 1.1f;
        const float OpenFadeSeconds = 0.2f;
        const double MaxFrameDt = 0.1;

        static readonly float[] ParallaxPx = { ParallaxBackPx, ParallaxMidPx, ParallaxFrontPx };
        static readonly string SkipKeyboard = "SPACE   SKIP";
        static readonly string SkipPad = "A   SKIP";

        enum PlayKind { Intro, Attract, Lore }

        bool _built, _playing;
        PlayKind _kind;
        bool _comicMode, _outro;
        float _outroT;
        MotionComicPlayer _comic;
        McTable _table;
        static readonly string SkipAnyKeyboard = "ANY KEY   SKIP";
        static readonly string SkipAnyPad = "ANY BUTTON   SKIP";
        CanvasGroup _group;
        readonly RectTransform[] _cardRoots = new RectTransform[StoryContent.CardCount];
        readonly CanvasGroup[] _cardGroups = new CanvasGroup[StoryContent.CardCount];
        readonly RawImage[,] _layers = new RawImage[StoryContent.CardCount, StoryContent.LayerCount];
        readonly VectorShape[] _pips = new VectorShape[StoryContent.CardCount];
        Text _caption, _skip;
        AudioSource _voice;
        float _openT;
        int _shownIndex = -1;
        bool _reduced;
        ControlDevice _hintDevice = (ControlDevice)(-1);

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (_playing) EndPlay(false, false);
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // Sequenced by the game flow (DG-01): only on the first boot's title (before the title, before the first-time
            // card, which waits for the title to go), never on a reload into a match, results or the menu, never in autoplay.
            if (PlayOnFirstLaunch && TitleScreen.ShowOnBoot && GameFlow.State == FlowState.Title && !GameFlow.Autoplay &&
                PlayerPrefs.GetInt(SeenKey, 0) == 0) Play();
        }

        // ---- static API ----

        /// <summary>Starts the intro from card 1 (restarts it if playing). False when no game is loaded.</summary>
        public static bool Play()
        {
            StoryIntro s = Instance != null ? Instance : StoryAttractCreditsInstaller.AttachStory();
            if (s == null) return false;
            s.Begin(PlayKind.Intro, MotionComic.IntroSequence);
            return true;
        }

        /// <summary>The motion comic as the title's attract loop: loops with narration off until any input. False when no
        /// game is loaded or the shot table is missing (the caller then runs its camera tour).</summary>
        public static bool PlayAttract()
        {
            StoryIntro s = Instance != null ? Instance : StoryAttractCreditsInstaller.AttachStory();
            if (s == null) return false;
            return s.Begin(PlayKind.Attract, MotionComic.AttractSequence);
        }

        /// <summary>The Codex vignette of a lore entry (shot table "codex" ids, for example "relic-02"); false if it has none.</summary>
        public static bool PlayLore(string entryId)
        {
            StoryIntro s = Instance != null ? Instance : StoryAttractCreditsInstaller.AttachStory();
            if (s == null) return false;
            return s.Begin(PlayKind.Lore, entryId);
        }

        /// <summary>Ends the intro now, as the skip keys do.</summary>
        public static void Skip()
        {
            if (Instance != null && Instance._playing) Instance.SkipNow();
        }

        // ---- play ----

        bool Begin(PlayKind kind, string sequence)
        {
            if (_playing) EndPlay(false, false);
            if (kind != PlayKind.Attract) AttractMode.End();
            Credits.Hide();
            _kind = kind;
            _outro = false;
            if (BeginComic(sequence)) return true;
            if (kind != PlayKind.Intro) return false;
            Build();
            Cards = StoryContent.Load();
            var secs = new float[Cards.Length];
            for (int i = 0; i < Cards.Length; i++) secs[i] = Cards[i].Seconds;
            Timeline = new StoryTimeline(secs);
            _reduced = ReducedMotion;
            for (int i = 0; i < StoryContent.CardCount; i++)
            {
                for (int l = 0; l < StoryContent.LayerCount; l++)
                {
                    RawImage img = _layers[i, l];
                    Texture2D tex = Cards[i].Layers[l];
                    img.texture = tex;
                    img.enabled = tex != null;
                    img.rectTransform.localScale = Vector3.one;
                    img.rectTransform.anchoredPosition = Vector2.zero;
                }
            }
            _playing = true;
            _openT = 0f;
            _shownIndex = -1;
            OverlayGuard.Begin(gameObject, ConsumePause);
            Canvas.enabled = true;
            _group.alpha = 0f;
            ShowCard(0);
            Apply();
            Started?.Invoke();
            return true;
        }

        /// <summary>Starts the motion comic for a sequence or Codex id; false (nothing changed) when the table, the id or
        /// its layers are missing.</summary>
        bool BeginComic(string sequence)
        {
            _comicMode = false;
            if (_table == null) _table = MotionComic.LoadTable();
            McShot[] shots = MotionComic.Shots(_table, sequence);
            if (shots == null) return false;
            if (_comic == null)
            {
                var host = new GameObject("Motion Comic", typeof(RectTransform));
                host.transform.SetParent(transform, false);
                _comic = host.AddComponent<MotionComicPlayer>();
            }
            _comic.Reduced = ReducedMotion;
            _comic.ReduceFlashing = GameSettings.ReduceFlashing;
            _comic.ShakeScale = PlayerPrefs.HasKey(GameSettings.ShakeScaleKey) ? GameSettings.ShakeScale : 1f;
            _comic.VoiceOn = _kind != PlayKind.Attract;
            if (!_comic.Prepare(_table, shots))
            {
                _comic.Release();
                return false;
            }
            var secs = new float[shots.Length];
            for (int i = 0; i < shots.Length; i++) secs[i] = Mathf.Max(0.05f, shots[i].seconds);
            Timeline = new StoryTimeline(secs);
            _comicMode = true;
            _playing = true;
            _hintDevice = (ControlDevice)(-1);
            OverlayGuard.Begin(gameObject, ConsumePause);
            _comic.Show();
            UpdateComicHint();
            _comic.Render(0, 0f, 0.0, true);
            if (_kind == PlayKind.Intro) Started?.Invoke();
            return true;
        }

        void UpdateComicHint()
        {
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d == _hintDevice) return;
            _hintDevice = d;
            _comic.SetSkipText(d == ControlDevice.Gamepad ? SkipAnyPad : SkipAnyKeyboard);
        }

        void UpdateComic()
        {
            float dt = Mathf.Min((float)MaxFrameDt, Time.unscaledDeltaTime);
            if (_outro)
            {
                // The film has finished: a key here only shortens the fade (the title reads keys from now on as well).
                _outroT += dt;
                float len = Mathf.Max(0.05f, _table.outroSeconds);
                _comic.SetFade(1f - MotionComic.Ease(McEase.InOutSine, _outroT / len));
                if (_outroT >= len || AnyKeyPressed()) EndPlay(false, true);
                return;
            }
            bool input = _kind == PlayKind.Attract ? AttractMode.AnyInput() : AnyKeyPressed();
            if (input)
            {
                if (_kind == PlayKind.Attract) EndPlay(false, false);
                else SkipNow();
                return;
            }
            UpdateComicHint();
            Timeline.Advance(dt);
            if (Timeline.Done)
            {
                if (_kind == PlayKind.Attract)
                {
                    var secs = new float[Timeline.CardCount];
                    for (int i = 0; i < secs.Length; i++) secs[i] = (float)Timeline.LengthOf(i);
                    Timeline = new StoryTimeline(secs);   // loop (one small allocation per 58 s loop, on the title only)
                    _comic.Render(0, 0f, 0.0, false);
                    return;
                }
                int last = Timeline.CardCount - 1;
                _comic.Render(last, (float)Timeline.LengthOf(last), Timeline.Total, false);
                if (_kind == PlayKind.Intro)
                {
                    // The hero frame lands on the logo, then fades into the title screen, which is shown again now.
                    _outro = true;
                    _outroT = 0f;
                    _comic.StopVoice();
                    OverlayGuard.End();
                    return;
                }
                EndPlay(false, true);
                return;
            }
            int idx = Timeline.Index;
            _comic.Render(idx, (float)(Timeline.Time - Timeline.StartOf(idx)), Timeline.Time, true);
        }

        /// <summary>Any keyboard key, mouse button or controller button pressed this frame (mouse movement does not skip).</summary>
        public static bool AnyKeyPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            Mouse m = Mouse.current;
            if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)) return true;
            Gamepad p = Gamepad.current;
            if (p == null) return false;
            return p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame ||
                   p.buttonNorth.wasPressedThisFrame || p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame ||
                   p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame;
        }

        bool ConsumePause()
        {
            if (_playing) SkipNow();
            return true;
        }

        void SkipNow()
        {
            if (!_playing) return;
            Timeline.Skip();
            EndPlay(true, true);
        }

        void EndPlay(bool skipped, bool raise)
        {
            _playing = false;
            _outro = false;
            if (_voice != null) _voice.Stop();
            if (Canvas != null) Canvas.enabled = false;
            OverlayGuard.End();
            bool intro = _kind == PlayKind.Intro;
            if (intro)
            {
                PlayerPrefs.SetInt(SeenKey, 1);
                PlayerPrefs.Save();
            }
            ReleaseAssets();
            if (_comicMode && _comic != null)
            {
                _comic.Hide();
                _comic.Release();
            }
            _comicMode = false;
            if (raise && intro) Finished?.Invoke(skipped);
        }

        void ReleaseAssets()
        {
            if (Cards == null) return;
            for (int i = 0; i < Cards.Length; i++)
            {
                for (int l = 0; l < StoryContent.LayerCount; l++)
                {
                    if (_layers[i, l] != null) _layers[i, l].texture = null;
                    Texture2D t = Cards[i].Layers[l];
                    if (t != null) Resources.UnloadAsset(t);
                    Cards[i].Layers[l] = null;
                }
            }
        }

        void Update()
        {
            OverlayGuard.Tick();
            if (!_playing) return;
            if (_comicMode)
            {
                UpdateComic();
                return;
            }
            if (SkipPressed())
            {
                SkipNow();
                return;
            }
            UpdateScale();
            UpdateHint();
            _openT += Time.unscaledDeltaTime;
            bool changed = Timeline.Advance(Math.Min(MaxFrameDt, Time.unscaledDeltaTime));
            if (Timeline.Done)
            {
                EndPlay(false, true);
                return;
            }
            if (changed) ShowCard(Timeline.Index);
            Apply();
        }

        /// <summary>Esc, Space, Enter; controller A, B or Start. Read every frame from the first.</summary>
        public static bool SkipPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                return true;
            Gamepad pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        void ShowCard(int i)
        {
            if (i == _shownIndex) return;
            _shownIndex = i;
            HudText.Set(_caption, Cards[i].Caption);
            for (int p = 0; p < _pips.Length; p++)
            {
                _pips[p].SetFill(p == i ? CMTokens.ToolBrass.Color : p < i ? CMTokens.WarmCream.Color : CMTokens.WithAlpha(CMTokens.WarmCream.Color, 0.35f));
            }
            if (_voice != null)
            {
                _voice.Stop();
                AudioClip clip = Cards[i].Narration;
                if (clip != null)
                {
                    _voice.clip = clip;
                    _voice.Play();
                }
            }
        }

        /// <summary>Card opacity and the parallax for this frame (no allocation).</summary>
        void Apply()
        {
            _group.alpha = Mathf.Clamp01(_openT / OpenFadeSeconds);
            for (int i = 0; i < StoryContent.CardCount; i++)
            {
                float a = Timeline.Alpha(i);
                bool on = a > 0f;
                if (_cardRoots[i].gameObject.activeSelf != on) _cardRoots[i].gameObject.SetActive(on);
                if (!on) continue;
                _cardGroups[i].alpha = a;
                float u = Timeline.Local01(i);
                for (int l = 0; l < StoryContent.LayerCount; l++)
                {
                    RectTransform rt = _layers[i, l].rectTransform;
                    if (_reduced)
                    {
                        rt.anchoredPosition = Vector2.zero;
                        rt.localScale = Vector3.one;
                        continue;
                    }
                    // Slow drift right-to-left, deeper layers less; alternate cards drift the other way.
                    float dir = (i & 1) == 0 ? 1f : -1f;
                    float x = Mathf.Lerp(0.5f, -0.5f, u) * ParallaxPx[l] * dir;
                    rt.anchoredPosition = new Vector2(x, 0f);
                    float s = 1f + 0.025f * u * (l + 1) / StoryContent.LayerCount;
                    rt.localScale = new Vector3(s, s, 1f);
                }
            }
        }

        void UpdateHint()
        {
            ControlBindings.Sample();
            ControlDevice d = ControlBindings.ActiveDevice;
            if (d == _hintDevice) return;
            _hintDevice = d;
            HudText.Set(_skip, d == ControlDevice.Gamepad ? SkipPad : SkipKeyboard);
        }

        void UpdateScale()
        {
            UnityEngine.Camera cam = Canvas.worldCamera;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam != null) Canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(Canvas.scaleFactor - s) > 1e-4f) Canvas.scaleFactor = s;
        }

        // ---- build (once) ----

        void Build()
        {
            if (_built) return;
            _built = true;

            var go = new GameObject("Story Intro Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 60;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = cam != null ? Mathf.Max(0.36f, cam.nearClipPlane * 1.2f) : 0.36f;
            _group = go.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            var root = (RectTransform)go.transform;

            // Void Navy under everything: a missing layer shows navy, never the match.
            Image under = MenuUi.Rt(root, "Navy").gameObject.AddComponent<Image>();
            MenuUi.Stretch(under.rectTransform);
            under.color = CMTokens.VoidNavy.Color;
            under.raycastTarget = false;

            RectTransform space = MenuUi.Rt(root, "Space");
            space.anchorMin = space.anchorMax = new Vector2(0.5f, 0.5f);
            space.pivot = new Vector2(0.5f, 0.5f);
            space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            for (int i = 0; i < StoryContent.CardCount; i++)
            {
                RectTransform card = MenuUi.Rt(space, "Card " + (i + 1));
                MenuUi.Stretch(card);
                _cardRoots[i] = card;
                _cardGroups[i] = card.gameObject.AddComponent<CanvasGroup>();
                for (int l = 0; l < StoryContent.LayerCount; l++)
                {
                    RawImage img = MenuUi.Rt(card, StoryContent.LayerNames[l]).gameObject.AddComponent<RawImage>();
                    RectTransform rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(HudLayout.RefW * LayerOverscan, HudLayout.RefH * LayerOverscan);
                    img.raycastTarget = false;
                    img.enabled = false;
                    _layers[i, l] = img;
                }
                card.gameObject.SetActive(false);
            }

            // Letterbox bars (Void Navy), wider than the layout so wide screens stay covered.
            const float topBar = 76f, bottomBar = 168f;
            MakeBar(space, "Top Bar", 0f, topBar);
            MakeBar(space, "Bottom Bar", HudLayout.RefH - bottomBar, bottomBar);

            _caption = MenuUi.Label(space, "Subtitle", CMTokens.Ui, UiScale.TypeLead, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                260f, HudLayout.RefH - bottomBar + 14f, HudLayout.RefW - 520f, bottomBar - 28f, "", CMTokens.OutlinePx);
            _caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            _caption.lineSpacing = 1.05f;

            _skip = MenuUi.Label(space, "Skip", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleRight,
                HudLayout.RefW - 560f, 18f, 500f, 40f, SkipKeyboard, 2f);

            float pipsW = StoryContent.CardCount * 34f;
            for (int p = 0; p < StoryContent.CardCount; p++)
            {
                VectorShape pip = MenuUi.Shape(space, "Pip " + (p + 1), ShapeKind.Circle,
                    (HudLayout.RefW - pipsW) * 0.5f + p * 34f + 6f, 28f, 20f, 20f, CMTokens.WarmCream.Color);
                pip.OutlinePx = 3f;
                pip.RimPx = 0f;
                _pips[p] = pip;
            }

            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.loop = false;
            _voice.spatialBlend = 0f;
            _voice.outputAudioMixerGroup = GameSettings.EffectsGroup;

            Canvas.enabled = false;
            UpdateScale();
        }

        static void MakeBar(RectTransform space, string name, float y, float h)
        {
            Image bar = MenuUi.Rt(space, name).gameObject.AddComponent<Image>();
            MenuUi.Place(bar.rectTransform, -HudLayout.RefW, y, HudLayout.RefW * 3f, h);
            bar.color = CMTokens.VoidNavy.Color;
            bar.raycastTarget = false;
        }
    }

    /// <summary>
    /// Shared by the story intro, the attract tour and the credits (one at a time): while one is up, the title screen does
    /// not react (its component is disabled, so the key that ends the overlay cannot also dismiss the title) and the Pause
    /// press goes to the overlay instead of the pause menu. The title comes back one frame after the overlay ends, so the
    /// same key press is never read twice.
    /// </summary>
    internal static class OverlayGuard
    {
        static TitleScreen _title;
        static GameObject _titleCanvas;
        static HumanInputSource _input;
        static Func<bool> _savedPause;
        static Func<bool> _ours;
        static bool _active;
        static int _restoreFrame = -1;

        public static bool Active => _active;

        public static void ResetStatics()
        {
            _title = null;
            _titleCanvas = null;
            _input = null;
            _savedPause = null;
            _ours = null;
            _active = false;
            _restoreFrame = -1;
        }

        /// <summary>Freezes the title (if any) and routes the Pause press to handler. Ends any earlier overlay's hold.</summary>
        public static void Begin(GameObject host, Func<bool> pauseHandler)
        {
            if (_active) End();
            _restoreFrame = -1;   // a pending title restore is cancelled: the title stays frozen for this overlay
            _active = true;
            TitleScreen t = host != null ? host.GetComponent<TitleScreen>() : null;
            if (t != null)
            {
                _title = t;
                t.enabled = false;
                Transform c = host.transform.Find("Title Canvas");
                _titleCanvas = c != null ? c.gameObject : null;
                if (_titleCanvas != null) _titleCanvas.SetActive(false);
            }
            _input = host != null ? host.GetComponent<HumanInputSource>() : null;
            if (_input != null)
            {
                _savedPause = _input.PauseHandler;
                _ours = pauseHandler;
                _input.PauseHandler = pauseHandler;
            }
        }

        /// <summary>Gives the Pause press back now; shows the title now and lets it read keys from the next frame.</summary>
        public static void End()
        {
            if (!_active) return;
            _active = false;
            if (_input != null && _input.PauseHandler == _ours) _input.PauseHandler = _savedPause;
            _ours = null;
            _savedPause = null;
            if (_titleCanvas != null) _titleCanvas.SetActive(true);
            _titleCanvas = null;
            if (_title != null) _restoreFrame = Time.frameCount + 1;
        }

        /// <summary>Called every frame by each overlay component.</summary>
        public static void Tick()
        {
            if (_restoreFrame < 0 || Time.frameCount < _restoreFrame) return;
            _restoreFrame = -1;
            if (_title != null && !_active) _title.enabled = true;
            _title = null;
        }
    }

    /// <summary>
    /// Attaches StoryIntro, AttractMode and Credits to the game's HumanInputSource object on boot and after every scene
    /// load, without editing GameBootstrap (the pause menu's MenuInstaller pattern). The game-flow overlay registry
    /// (feat/dg-01-game-flow) was not on origin when this was written; when it lands it may call these components'
    /// static APIs instead.
    /// </summary>
    public static class StoryAttractCreditsInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            OverlayGuard.ResetStatics();
            // Installed by the DG-01 overlay registry ([GameOverlay] on each component); this only resets statics.
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds the three components to the game object; returns it (null: no game in this scene).</summary>
        public static GameObject Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            GameObject go = input.gameObject;
            if (go.GetComponent<StoryIntro>() == null) go.AddComponent<StoryIntro>();
            if (go.GetComponent<AttractMode>() == null) go.AddComponent<AttractMode>();
            if (go.GetComponent<Credits>() == null) go.AddComponent<Credits>();
            return go;
        }

        internal static StoryIntro AttachStory()
        {
            GameObject go = Attach(SceneManager.GetActiveScene());
            return go != null ? go.GetComponent<StoryIntro>() : null;
        }

        internal static Credits AttachCredits()
        {
            GameObject go = Attach(SceneManager.GetActiveScene());
            return go != null ? go.GetComponent<Credits>() : null;
        }
    }
}
