using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Threading;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Game
{
    /// <summary>The three highlight moments of a match (DG-23).</summary>
    public enum MomentKind { FirstKnockOut = 0, Hatch = 1, MultiKnockOut = 2 }

    /// <summary>One recorded moment: when it happened (sim tick and match time, read the frame it happened), where, and who.</summary>
    public struct Moment
    {
        public bool Valid;
        public MomentKind Kind;
        /// <summary>World.Tick when the recorder read the event (at most one frame's ticks after it).</summary>
        public int Tick;
        public double MatchTime;
        /// <summary>Knock-outs in the chain (MultiKnockOut), else 1.</summary>
        public int Count;
        /// <summary>The knocking-out actor (-1 for the hatch).</summary>
        public int Actor;
        public double X, Z;
        /// <summary>Hatch: the Kaiju.HatchCode value (0 neutral, 1 Azure, 2 Crimson, 3 berserk).</summary>
        public int Code;
    }

    /// <summary>
    /// The moment rules, pure and allocation-free after the first poll: the first knock-out of the match, the hatch, and
    /// the biggest multi-knock-out (one actor knocking out 2 or more within MultiWindowSeconds of sim time between kills;
    /// a bigger chain replaces a smaller one). Reads the World event ring with its own cursor; never writes the sim.
    /// </summary>
    public sealed class MomentLog
    {
        public const double MultiWindowSeconds = 4;
        readonly Moment[] _moments = new Moment[3];
        double[] _lastKill = new double[0];
        int[] _chain = new int[0];
        int _cursor = -1;

        public bool Has(MomentKind k) => _moments[(int)k].Valid;
        public Moment Get(MomentKind k) => _moments[(int)k];

        public void Reset(World w)
        {
            for (int i = 0; i < _moments.Length; i++) _moments[i] = default;
            _cursor = w != null ? w.EventHead : -1;
            Array.Clear(_lastKill, 0, _lastKill.Length);
            Array.Clear(_chain, 0, _chain.Length);
        }

        /// <summary>Reads new events; returns a bit mask (1 &lt;&lt; kind) of the moments recorded or replaced by this call.</summary>
        public int Poll(World w)
        {
            if (w == null) return 0;
            if (_lastKill.Length < w.ActorCount)
            {
                _lastKill = new double[w.ActorCount];
                _chain = new int[w.ActorCount];
            }
            if (_cursor < 0 || _cursor > w.EventHead) _cursor = w.EventHead;
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            int changed = 0;
            double t = w.Match.Time;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e.Type == SimEventType.Hatch)
                {
                    if (Has(MomentKind.Hatch)) continue;
                    _moments[(int)MomentKind.Hatch] = Make(MomentKind.Hatch, w, t, 1, -1, e.X, e.Z, (int)e.Value);
                    changed |= 1 << (int)MomentKind.Hatch;
                }
                else if (e.Type == SimEventType.Death)
                {
                    int killer = e.B;
                    if (!Has(MomentKind.FirstKnockOut))
                    {
                        _moments[(int)MomentKind.FirstKnockOut] = Make(MomentKind.FirstKnockOut, w, t, 1, killer, e.X, e.Z, 0);
                        changed |= 1 << (int)MomentKind.FirstKnockOut;
                    }
                    if (killer < 0 || killer >= _chain.Length || killer == e.A) continue;
                    _chain[killer] = _chain[killer] > 0 && t - _lastKill[killer] <= MultiWindowSeconds ? _chain[killer] + 1 : 1;
                    _lastKill[killer] = t;
                    int n = _chain[killer];
                    Moment best = _moments[(int)MomentKind.MultiKnockOut];
                    if (n >= 2 && (!best.Valid || n > best.Count))
                    {
                        _moments[(int)MomentKind.MultiKnockOut] = Make(MomentKind.MultiKnockOut, w, t, n, killer, e.X, e.Z, 0);
                        changed |= 1 << (int)MomentKind.MultiKnockOut;
                    }
                }
            }
            return changed;
        }

        static Moment Make(MomentKind k, World w, double t, int count, int actor, double x, double z, int code) => new Moment
        {
            Valid = true, Kind = k, Tick = w.Tick, MatchTime = t, Count = count, Actor = actor, X = x, Z = z, Code = code,
        };
    }

    /// <summary>
    /// Highlights and photo (DG-23). Records the three moments (MomentLog) and captures a still of each at the moment,
    /// without a hitch: the frame is copied on the GPU at the end of the frame (ScreenCapture into a pooled render
    /// texture), cropped and scaled to StillWidth x StillHeight, and read back asynchronously into one of MaxStills pooled
    /// textures; at most one capture per MinCaptureInterval seconds (a moment that waits longer than StaleSeconds is
    /// dropped). F12 saves a framed PNG of the screen with the logo (or, on the results highlights, the three-still card);
    /// the frame is composed, encoded and written on a worker thread, atomically (temp file, then rename) under
    /// persistentDataPath/Photos.
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 56, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]   // installed once by GameBootstrap through the overlay registry
    public sealed class MomentRecorder : MonoBehaviour
    {
        public const int MaxStills = 6;
        public const int StillWidth = 640, StillHeight = 360;
        public const float MinCaptureInterval = 1f;
        public const float StaleSeconds = 1.5f;
        public const string PhotoFolder = "Photos";
        public const string LogoResource = "UI/Logo/critical-mass-logo@2x";

        /// <summary>
        /// The screen copy comes out upside down where the graphics API's texture origin is the top (Metal, Direct3D);
        /// the recorder flips it there. Exposed so the integrator can correct it in one place if a capture reads wrong.
        /// </summary>
        public static bool FlipScreenCapture = SystemInfo.graphicsUVStartsAtTop;

        /// <summary>The recorder of the running game (null when none).</summary>
        public static MomentRecorder Current { get; private set; }
        /// <summary>Raised on the main thread with the saved file's full path.</summary>
        public static event Action<string> PhotoSaved;

        readonly MomentLog _log = new MomentLog();
        readonly Texture2D[] _pool = new Texture2D[MaxStills];
        readonly bool[] _used = new bool[MaxStills];
        readonly int[] _stillOf = { -1, -1, -1 };
        readonly float[] _pendingSince = new float[3];
        int _pendingMask;
        bool _inFlight;
        int _inFlightSlot = -1, _inFlightGen;
        MomentKind _inFlightKind;
        int _generation;
        float _lastCapture = -100f;
        RenderTexture _screenRt, _smallRt, _photoRt, _logoRt;
        static readonly WaitForEndOfFrame Eof = new WaitForEndOfFrame();
        Action<AsyncGPUReadbackRequest> _onStill, _onPhoto, _onLogo;

        SimDriverBehaviour _driver;
        PauseMenu _menu;
        World _world;
        int _epoch = -1;

        byte[] _logo;
        int _logoW, _logoH;
        bool _logoRequested;
        byte[] _photoBuf;
        volatile bool _photoBusy;
        string _photoDone, _photoError;
        readonly object _photoLock = new object();
        string _photoDir;

        public MomentLog Log => _log;
        public bool Has(MomentKind k) => _log.Has(k);
        public Moment Get(MomentKind k) => _log.Get(k);
        /// <summary>The still of a moment (null until its capture is read back, or where captures are unsupported).</summary>
        public Texture2D Still(MomentKind k) => _stillOf[(int)k] >= 0 ? _pool[_stillOf[(int)k]] : null;
        /// <summary>Pooled stills holding a picture right now (never above MaxStills).</summary>
        public int StillsInMemory
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxStills; i++) if (_used[i]) n++;
                return n;
            }
        }
        public bool PhotoBusy => _photoBusy;

        /// <summary>True where stills can be captured: a real graphics device with asynchronous readback, in play.</summary>
        public static bool CanCapture => Application.isPlaying && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null &&
                                         SystemInfo.supportsAsyncGPUReadback;

        void Awake()
        {
            _onStill = OnStill;
            _onPhoto = OnPhoto;
            _onLogo = OnLogo;
            _photoDir = Path.Combine(Application.persistentDataPath, PhotoFolder);
        }

        // The logo's pixels are read back once, early, so the first photo already carries it.
        void Start() => RequestLogo();

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        void OnDestroy()
        {
            for (int i = 0; i < MaxStills; i++) if (_pool[i] != null) Destroy(_pool[i]);
            Release(ref _screenRt);
            Release(ref _smallRt);
            Release(ref _photoRt);
            Release(ref _logoRt);
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_menu == null) _menu = GetComponent<PauseMenu>();
            if (_driver != null && _driver.World != null)
            {
                World w = _driver.World;
                if (!ReferenceEquals(w, _world) || w.Epoch != _epoch) NewMatch(w);
                int changed = _log.Poll(w);
                if (changed != 0)
                {
                    for (int k = 0; k < 3; k++)
                        if ((changed & (1 << k)) != 0) { _pendingMask |= 1 << k; _pendingSince[k] = Time.unscaledTime; }
                }
                TryStartCapture();
            }

            Keyboard kb = Keyboard.current;
            bool menuOpen = _menu != null && _menu.IsOpen;
            if (kb != null && kb.f12Key.wasPressedThisFrame && !menuOpen)
            {
                if (ResultsHighlights.IsOpen) SaveHighlightsCard();
                else SavePhoto();
            }
            FlushPhotoResult();
        }

        void NewMatch(World w)
        {
            _world = w;
            _epoch = w.Epoch;
            _log.Reset(w);
            _generation++;
            _pendingMask = 0;
            for (int i = 0; i < 3; i++) _stillOf[i] = -1;
            for (int i = 0; i < MaxStills; i++) _used[i] = false;
            if (_inFlightSlot >= 0) _used[_inFlightSlot] = true;   // still owned by the readback; freed in its callback
        }

        // ---------------------------------------------------------------- stills

        void TryStartCapture()
        {
            if (_pendingMask == 0 || _inFlight) return;
            float now = Time.unscaledTime;
            if (now - _lastCapture < MinCaptureInterval) return;
            for (int k = 0; k < 3; k++)
            {
                if ((_pendingMask & (1 << k)) == 0) continue;
                _pendingMask &= ~(1 << k);
                if (now - _pendingSince[k] > StaleSeconds) continue;
                if (!CanCapture) continue;
                int slot = FreeSlot();
                if (slot < 0) continue;   // the hard cap: no slot, no capture
                _used[slot] = true;
                _inFlight = true;
                _inFlightSlot = slot;
                _inFlightKind = (MomentKind)k;
                _inFlightGen = _generation;
                _lastCapture = now;
                StartCoroutine(CaptureStill());
                return;
            }
        }

        int FreeSlot()
        {
            for (int i = 0; i < MaxStills; i++) if (!_used[i]) return i;
            return -1;
        }

        IEnumerator CaptureStill()
        {
            yield return Eof;
            if (!EnsureRt(ref _screenRt, Screen.width, Screen.height) || !EnsureRt(ref _smallRt, StillWidth, StillHeight))
            {
                AbortStill();
                yield break;
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenRt);
            // Crop the centre to 16:9 and scale down in one blit (flipped where the copy comes out upside down).
            float aspect = (float)_screenRt.width / _screenRt.height, want = (float)StillWidth / StillHeight;
            float sx = 1f, sy = 1f;
            if (aspect > want) sx = want / aspect;
            else sy = aspect / want;
            float ox = (1f - sx) * 0.5f, oy = (1f - sy) * 0.5f;
            if (FlipScreenCapture) Graphics.Blit(_screenRt, _smallRt, new Vector2(sx, -sy), new Vector2(ox, oy + sy));
            else Graphics.Blit(_screenRt, _smallRt, new Vector2(sx, sy), new Vector2(ox, oy));
            AsyncGPUReadback.Request(_smallRt, 0, TextureFormat.RGBA32, _onStill);
        }

        void AbortStill()
        {
            if (_inFlightSlot >= 0) _used[_inFlightSlot] = false;
            _inFlightSlot = -1;
            _inFlight = false;
        }

        void OnStill(AsyncGPUReadbackRequest req)
        {
            int slot = _inFlightSlot;
            _inFlightSlot = -1;
            _inFlight = false;
            if (slot < 0) return;
            if (this == null || req.hasError || _inFlightGen != _generation)
            {
                _used[slot] = false;
                return;
            }
            Texture2D tex = _pool[slot];
            if (tex == null)
            {
                tex = new Texture2D(StillWidth, StillHeight, TextureFormat.RGBA32, false, false) { name = "Highlight Still " + slot };
                tex.wrapMode = TextureWrapMode.Clamp;
                _pool[slot] = tex;
            }
            tex.SetPixelData(req.GetData<byte>(), 0);
            tex.Apply(false, false);
            int k = (int)_inFlightKind;
            if (_stillOf[k] >= 0 && _stillOf[k] != slot) _used[_stillOf[k]] = false;   // a bigger multi-knock-out replaces the old still
            _stillOf[k] = slot;
        }

        static bool EnsureRt(ref RenderTexture rt, int w, int h)
        {
            if (w < 8 || h < 8) return false;
            if (rt != null && rt.width == w && rt.height == h) return true;
            Release(ref rt);
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Moment Capture " + w + "x" + h };
            return rt.Create();
        }

        static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        // ---------------------------------------------------------------- photo (F12) and the highlights card

        /// <summary>Saves a framed PNG of the current screen with the logo. False when a save is already running or captures are unsupported.</summary>
        public bool SavePhoto()
        {
            if (_photoBusy || !CanCapture) return false;
            _photoBusy = true;
            RequestLogo();
            StartCoroutine(CapturePhoto());
            return true;
        }

        IEnumerator CapturePhoto()
        {
            yield return Eof;
            if (!EnsureRt(ref _screenRt, Screen.width, Screen.height) || !EnsureRt(ref _photoRt, Screen.width, Screen.height))
            {
                _photoBusy = false;
                yield break;
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_screenRt);
            if (FlipScreenCapture) Graphics.Blit(_screenRt, _photoRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_screenRt, _photoRt);
            AsyncGPUReadback.Request(_photoRt, 0, TextureFormat.RGBA32, _onPhoto);
        }

        void OnPhoto(AsyncGPUReadbackRequest req)
        {
            if (this == null || req.hasError)
            {
                _photoBusy = false;
                return;
            }
            int w = req.width, h = req.height, n = w * h * 4;
            if (_photoBuf == null || _photoBuf.Length != n) _photoBuf = new byte[n];
            req.GetData<byte>().CopyTo(_photoBuf);
            var tile = new PhotoFrame.Tile { Rgba = _photoBuf, W = w, H = h, BottomUp = true };
            PhotoFrame.Layout frame = PhotoFrame.ForPhoto(tile);
            StartSave(frame, "CriticalMass-Photo");
        }

        /// <summary>Saves the results card: the three highlight stills side by side, framed, with the logo (the Share button).</summary>
        public bool SaveHighlightsCard()
        {
            if (_photoBusy) return false;
            var tiles = new PhotoFrame.Tile[3];
            for (int k = 0; k < 3; k++)
            {
                Texture2D t = Still((MomentKind)k);
                tiles[k] = t != null
                    ? new PhotoFrame.Tile { Rgba = t.GetPixelData<byte>(0).ToArray(), W = t.width, H = t.height, BottomUp = true }
                    : new PhotoFrame.Tile { Rgba = null, W = StillWidth, H = StillHeight };
            }
            _photoBusy = true;
            RequestLogo();
            StartSave(PhotoFrame.ForCard(tiles), "CriticalMass-Highlights");
            return true;
        }

        void StartSave(PhotoFrame.Layout frame, string prefix)
        {
            frame.Logo = _logo;
            frame.LogoW = _logoW;
            frame.LogoH = _logoH;
            string dir = _photoDir ?? Path.Combine(Application.persistentDataPath, PhotoFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] rgba = PhotoFrame.Compose(frame);
                    byte[] png = PngWriter.Encode(rgba, frame.Width, frame.Height);
                    string path = PngWriter.WriteAtomic(dir, prefix + "-" + stamp, png);
                    lock (_photoLock) _photoDone = path;
                }
                catch (Exception e)
                {
                    lock (_photoLock) _photoError = e.Message;
                }
                finally
                {
                    _photoBusy = false;
                }
            });
        }

        void FlushPhotoResult()
        {
            string done, error;
            lock (_photoLock)
            {
                done = _photoDone;
                error = _photoError;
                _photoDone = null;
                _photoError = null;
            }
            if (done != null)
            {
                CaptionBar.Show("PHOTO SAVED   ·   " + Path.GetFileName(done), 3f);
                PhotoSaved?.Invoke(done);
            }
            if (error != null)
            {
                Debug.LogWarning("[MomentRecorder] photo not saved: " + error);
                CaptionBar.Show("THE PHOTO COULD NOT BE SAVED", 3f);
            }
        }

        void RequestLogo()
        {
            if (_logoRequested || !CanCapture) return;
            _logoRequested = true;
            Texture2D logo = Resources.Load<Texture2D>(LogoResource);
            if (logo == null) return;
            if (!EnsureRt(ref _logoRt, logo.width, logo.height)) return;
            Graphics.Blit(logo, _logoRt);
            AsyncGPUReadback.Request(_logoRt, 0, TextureFormat.RGBA32, _onLogo);
        }

        void OnLogo(AsyncGPUReadbackRequest req)
        {
            if (this == null) return;
            if (!req.hasError)
            {
                _logoW = req.width;
                _logoH = req.height;
                _logo = req.GetData<byte>().ToArray();
            }
            Release(ref _logoRt);
        }

        // ---------------------------------------------------------------- install

        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        /// <summary>Adds the recorder and the results highlights to the game object; null when there is no game.</summary>
        public static MomentRecorder Attach(Scene scene)
        {
            PauseMenu menu = MenuInstaller.Attach(scene);
            if (menu == null) return null;
            GameObject go = menu.gameObject;
            MomentRecorder r = go.GetComponent<MomentRecorder>();
            if (r == null) r = go.AddComponent<MomentRecorder>();
            if (go.GetComponent<ResultsHighlights>() == null) go.AddComponent<ResultsHighlights>();
            return r;
        }
    }

    /// <summary>
    /// Builds a framed picture in memory (worker-thread safe: plain arrays, no Unity calls): a Void Navy frame around the
    /// tiles, a Tool Brass rule above a bottom band, and the logo in the band. Output rows run top to bottom.
    /// </summary>
    public static class PhotoFrame
    {
        public struct Tile
        {
            /// <summary>RGBA32 pixels; null draws an empty Panel Navy plate.</summary>
            public byte[] Rgba;
            public int W, H;
            /// <summary>Rows bottom to top (Unity texture order).</summary>
            public bool BottomUp;
            /// <summary>Destination rect, top-left origin.</summary>
            public int X, Y, DW, DH;
        }

        public sealed class Layout
        {
            public int Width, Height;
            public Tile[] Tiles;
            public int RuleY, RuleH;
            public int BandY, BandH;
            public int Margin;
            public byte[] Logo;
            public int LogoW, LogoH;
        }

        static readonly Color32 Navy = Hex(Palette.VoidNavy), Brass = Hex(Palette.Brass), Plate = new Color32(0x1B, 0x31, 0x4D, 255);

        static Color32 Hex(string h)
        {
            int v = Convert.ToInt32(h.Substring(1), 16);
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        }

        public static Layout ForPhoto(Tile shot)
        {
            int b = Math.Max(16, shot.H / 40), band = Math.Max(72, shot.H / 9);
            shot.X = b;
            shot.Y = b;
            shot.DW = shot.W;
            shot.DH = shot.H;
            return Finish(new Layout { Width = shot.W + 2 * b, Tiles = new[] { shot }, Margin = b }, b + shot.H, band, shot.H);
        }

        public static Layout ForCard(Tile[] stills)
        {
            const int b = 32, gap = 24, band = 140;
            int w = stills.Length > 0 ? stills[0].W : 640, h = stills.Length > 0 ? stills[0].H : 360;
            for (int i = 0; i < stills.Length; i++)
            {
                stills[i].X = b + i * (w + gap);
                stills[i].Y = b;
                stills[i].DW = w;
                stills[i].DH = h;
            }
            return Finish(new Layout { Width = 2 * b + stills.Length * w + Math.Max(0, stills.Length - 1) * gap, Tiles = stills, Margin = b },
                b + h, band, h);
        }

        static Layout Finish(Layout l, int contentBottom, int band, int imageH)
        {
            l.RuleH = Math.Max(3, imageH / 270);
            l.RuleY = contentBottom + l.Margin / 2;
            l.BandY = l.RuleY + l.RuleH;
            l.BandH = band;
            l.Height = l.BandY + band;
            return l;
        }

        public static byte[] Compose(Layout l)
        {
            int W = l.Width, H = l.Height;
            var o = new byte[W * H * 4];
            for (int i = 0; i < o.Length; i += 4)
            {
                o[i] = Navy.r; o[i + 1] = Navy.g; o[i + 2] = Navy.b; o[i + 3] = 255;
            }
            foreach (Tile t in l.Tiles) DrawTile(o, W, H, t);
            Fill(o, W, H, l.Margin, l.RuleY, W - 2 * l.Margin, l.RuleH, Brass);
            if (l.Logo != null && l.LogoW > 0 && l.LogoH > 0)
            {
                int lh = (int)(l.BandH * 0.78f), lw = (int)((long)lh * l.LogoW / l.LogoH);
                DrawImage(o, W, H, l.Logo, l.LogoW, l.LogoH, true, l.Margin, l.BandY + (l.BandH - lh) / 2, lw, lh, true);
            }
            return o;
        }

        static void DrawTile(byte[] o, int W, int H, Tile t)
        {
            if (t.Rgba == null)
            {
                Fill(o, W, H, t.X, t.Y, t.DW, t.DH, Plate);
                return;
            }
            DrawImage(o, W, H, t.Rgba, t.W, t.H, t.BottomUp, t.X, t.Y, t.DW, t.DH, false);
        }

        static void Fill(byte[] o, int W, int H, int x, int y, int w, int h, Color32 c)
        {
            for (int j = Math.Max(0, y); j < Math.Min(H, y + h); j++)
            {
                for (int i = Math.Max(0, x); i < Math.Min(W, x + w); i++)
                {
                    int p = (j * W + i) * 4;
                    o[p] = c.r; o[p + 1] = c.g; o[p + 2] = c.b; o[p + 3] = 255;
                }
            }
        }

        /// <summary>Bilinear scale of src into the dest rect; alpha-blends when blend is set, else copies opaque.</summary>
        static void DrawImage(byte[] o, int W, int H, byte[] src, int sw, int sh, bool bottomUp, int x, int y, int dw, int dh, bool blend)
        {
            if (dw <= 0 || dh <= 0) return;
            bool same = dw == sw && dh == sh;
            for (int j = 0; j < dh; j++)
            {
                int oy = y + j;
                if (oy < 0 || oy >= H) continue;
                float fy = same ? j : (j + 0.5f) * sh / dh - 0.5f;
                for (int i = 0; i < dw; i++)
                {
                    int ox = x + i;
                    if (ox < 0 || ox >= W) continue;
                    float r, g, b, a;
                    if (same)
                    {
                        int s = Row(bottomUp, sh, j) * sw + i;
                        r = src[s * 4]; g = src[s * 4 + 1]; b = src[s * 4 + 2]; a = src[s * 4 + 3];
                    }
                    else
                    {
                        float fx = (i + 0.5f) * sw / dw - 0.5f;
                        int x0 = Clamp((int)Math.Floor(fx), sw), y0 = Clamp((int)Math.Floor(fy), sh);
                        int x1 = Clamp(x0 + 1, sw), y1 = Clamp(y0 + 1, sh);
                        float tx = Math.Max(0f, Math.Min(1f, fx - x0)), ty = Math.Max(0f, Math.Min(1f, fy - y0));
                        int p00 = (Row(bottomUp, sh, y0) * sw + x0) * 4, p10 = (Row(bottomUp, sh, y0) * sw + x1) * 4;
                        int p01 = (Row(bottomUp, sh, y1) * sw + x0) * 4, p11 = (Row(bottomUp, sh, y1) * sw + x1) * 4;
                        r = Lerp2(src, p00, p10, p01, p11, 0, tx, ty);
                        g = Lerp2(src, p00, p10, p01, p11, 1, tx, ty);
                        b = Lerp2(src, p00, p10, p01, p11, 2, tx, ty);
                        a = Lerp2(src, p00, p10, p01, p11, 3, tx, ty);
                    }
                    int q = (oy * W + ox) * 4;
                    if (blend)
                    {
                        float k = a / 255f;
                        o[q] = (byte)(o[q] + (r - o[q]) * k);
                        o[q + 1] = (byte)(o[q + 1] + (g - o[q + 1]) * k);
                        o[q + 2] = (byte)(o[q + 2] + (b - o[q + 2]) * k);
                    }
                    else
                    {
                        o[q] = (byte)r; o[q + 1] = (byte)g; o[q + 2] = (byte)b;
                    }
                    o[q + 3] = 255;
                }
            }
        }

        static int Row(bool bottomUp, int h, int topDownRow) => bottomUp ? h - 1 - topDownRow : topDownRow;
        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        static float Lerp2(byte[] s, int p00, int p10, int p01, int p11, int c, float tx, float ty)
        {
            float top = s[p00 + c] + (s[p10 + c] - s[p00 + c]) * tx;
            float bottom = s[p01 + c] + (s[p11 + c] - s[p01 + c]) * tx;
            return top + (bottom - top) * ty;
        }
    }

    /// <summary>A small PNG encoder (8-bit RGBA, no filter, zlib via DeflateStream) and an atomic file write; thread safe.</summary>
    public static class PngWriter
    {
        static readonly uint[] Crc = BuildCrc();

        /// <summary>PNG bytes of a top-to-bottom RGBA32 image.</summary>
        public static byte[] Encode(byte[] rgba, int w, int h)
        {
            if (rgba == null || rgba.Length < w * h * 4) throw new ArgumentException("pixel buffer too small");
            using (var png = new MemoryStream())
            {
                png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                Be(ihdr, 0, (uint)w);
                Be(ihdr, 4, (uint)h);
                ihdr[8] = 8;   // bit depth
                ihdr[9] = 6;   // colour type RGBA
                Chunk(png, "IHDR", ihdr, ihdr.Length);
                byte[] z = Zlib(rgba, w, h);
                Chunk(png, "IDAT", z, z.Length);
                Chunk(png, "IEND", new byte[0], 0);
                return png.ToArray();
            }
        }

        static byte[] Zlib(byte[] rgba, int w, int h)
        {
            int stride = w * 4;
            uint a = 1, b = 0;
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x01);
                using (var d = new DeflateStream(ms, CompressionLevel.Fastest, true))
                {
                    var filter = new byte[1];
                    for (int y = 0; y < h; y++)
                    {
                        d.Write(filter, 0, 1);
                        a = (a + 0) % 65521;
                        b = (b + a) % 65521;
                        d.Write(rgba, y * stride, stride);
                        for (int i = y * stride, end = i + stride; i < end; i++)
                        {
                            a += rgba[i];
                            if (a >= 65521) a -= 65521;
                            b += a;
                            if (b >= 65521) b %= 65521;
                        }
                    }
                }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        static void Chunk(Stream s, string type, byte[] data, int len)
        {
            var head = new byte[8];
            Be(head, 0, (uint)len);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            if (len > 0) s.Write(data, 0, len);
            uint c = 0xFFFFFFFFu;
            for (int i = 4; i < 8; i++) c = Crc[(c ^ head[i]) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < len; i++) c = Crc[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            var tail = new byte[4];
            Be(tail, 0, c ^ 0xFFFFFFFFu);
            s.Write(tail, 0, 4);
        }

        static void Be(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        static uint[] BuildCrc()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        /// <summary>Writes <paramref name="bytes"/> to dir/name.png through a temp file and a rename (never a half-written PNG); returns the path.</summary>
        public static string WriteAtomic(string dir, string name, byte[] bytes)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, name + "-" + n + ".png");
            string tmp = path + ".tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(bytes, 0, bytes.Length);
                f.Flush(true);
            }
            File.Move(tmp, path);
            return path;
        }
    }
}
