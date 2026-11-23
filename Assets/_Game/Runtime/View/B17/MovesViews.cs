// Class moves build (2026-11-22), lane 6: the world-space feedback of the new verbs and their words, created by DodgeView.Awake
// (one child of the B13 item views) so it lives and dies with the driver. View only: reads World and MovesRead, never writes.
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Player;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.View.B17
{
    /// <summary>
    /// Lane 6 (class moves). Draws, per fighter, only while that verb's sim state says so:
    /// GUARD meter arc under the fighter (team colour, empties toward red; only while the guard is held or refilling);
    /// CHARGE: a ground glow under the charger in three steps (a smooth ramp with reduce flashing on) and the heavy's real
    /// ground shape (reach x arc, 360 for the Builder's slam) along his facing, for an enemy's charge too (the telegraph);
    /// TRAVEL: a dust puff every 0.3 u while lit; SLIDE: a dust plume at the start and along the slide;
    /// GLIDE: a shrinking ring at the glider's feet (the glide time left) and a soft trail; LANDING: dust after an
    /// evading jump; GUARD BREAK: a crack burst; SECOND WIND: a green ring at the feet.
    /// Words (via PlayerHud.Current.Tags): PARRY (large, both sides; + the 80 ms pose hold on both bodies), GUARD BREAK,
    /// FEINT / BAITED, LUNGE, CHARGED, x2 / x3 / FINISHER on the target I hit, TRIPPED, SECOND WIND.
    /// Its own event cursor; one pass per frame; pooled objects made once in Awake; no per-frame allocation.
    /// With every switch off World.Moves is null and nothing here draws or says anything.
    /// </summary>
    [DefaultExecutionOrder(120)]   // after the sim step (driver Update) and before GameHud (200) ticks the words this frame
    public sealed class MovesViews : MonoBehaviour
    {
        public const int Arcs = 8, Shapes = 8, Glows = 8, Puffs = 32, Rings = 6;
        public const float ArcInner = 0.62f, ArcOuter = 0.74f, ArcY = 0.05f, ShapeY = 0.04f, GlowRadius = 0.7f;
        public const float DustEvery = 0.3f, PuffSeconds = 0.35f, PuffSize = 0.45f, RingSeconds = 0.5f;
        const int ArcSegments = 32, ShapeSegments = 24;

        /// <summary>The views of the running match (tests and the play-mode rig read their state).</summary>
        public static MovesViews Current { get; private set; }

        SimDriverBehaviour _driver;
        Material _unlit;
        readonly Piece[] _arcs = new Piece[Arcs], _shapes = new Piece[Shapes], _glows = new Piece[Glows], _puffs = new Piece[Puffs], _rings = new Piece[Rings];
        int _nextPuff, _nextRing;
        float[] _dustDist = new float[64];
        double[] _lastX = new double[64], _lastZ = new double[64];
        bool[] _wasLanding = new bool[64], _wasSliding = new bool[64];
        int _cursor = -1, _epoch = -1;
        World _world;
        readonly int[] _lastVictimOfMe = { -1 };
        // first-use hints (MovesHelp): shown this match, wall time of the last one, my airborne edge
        readonly bool[] _hinted = new bool[(int)MovesHelp.Verb.Count];
        float _hintAt = -100f;
        bool _wasAir;
        /// <summary>Tests: hints shown this match, by verb.</summary>
        public bool Hinted(MovesHelp.Verb v) => _hinted[(int)v];
        /// <summary>Tests and the reviewer: ignore the PlayerPrefs "seen twice" memory.</summary>
        public static bool HintsAlways;

        // ---- tests / reviewer: what is on screen this frame
        public int GuardArcsShown { get; private set; }
        public int ChargeShapesShown { get; private set; }
        public int GlideRingsShown { get; private set; }
        public int PuffsAlive { get; private set; }
        public int LastWordFrame { get; private set; } = -1;
        public string LastWord { get; private set; }

        // ---- readability round (Editor review 2026-11-23)
        /// <summary>Item 1: a local Guard press shows the guard (arc, shield, pose) on the press frame itself, before the sim's
        /// tick takes it; if the sim has not raised the guard GuardPredictWait after the press, it fades over GuardPredictFade.</summary>
        public const float GuardPredictWait = 0.10f, GuardPredictFade = 0.12f;
        /// <summary>Item 1: the guard SHIELD, a curved see-through band in front of the guarding body (reads from behind):
        /// radius, bottom, top (u), arc, its base alpha and the raise flash (0.18 s; none with reduce flashing).</summary>
        public const float ShieldR = 0.62f, ShieldY0 = 0.25f, ShieldY1 = 1.55f, ShieldArc = 130f, ShieldAlpha = 0.26f, ShieldFlash = 0.40f, ShieldFlashSeconds = 0.18f;
        /// <summary>The local guard's predicted strength this frame (1 = shown, fading to 0 when the sim refused).</summary>
        public float GuardPredicted01 { get; private set; }
        public int ShieldsShown { get; private set; }
        /// <summary>Item 5: the combo-window ring is on the victim (the next Attack press chains).</summary>
        public bool ChainRingShown { get; private set; }
        /// <summary>Item 2: the hint line (one fixed HUD row, the prompt stack's free row), its words and row, or null / -1.</summary>
        public string HintLineText => _hintChip != null && _hintChip.gameObject.activeSelf ? _hintNow : null;
        public int HintLineSlot { get; private set; } = -1;

        readonly Piece[] _shields = new Piece[Arcs];
        Piece _chainRing;
        float[] _guardShownAt = new float[64];
        bool[] _guardWas = new bool[64];
        CriticalMass.Runtime.Input.HumanInputSource _human;
        int _humanLook = int.MinValue;
        bool _guardPressWas, _wasChain;
        float _guardPressAt = -100f, _chainAt = -100f;
        Canvas _hintCanvas;
        RectTransform _hintChip;
        Text _hintText;
        CanvasGroup _hintGroup;
        string _hintNow;
        float _hintLeft, _hintWait;
        GameHud _hud;
        const float HintChipW = 720f, HintChipH = 46f;

        // ---- readability round 2
        /// <summary>Item 2: SPRINT (travel) speed lines: thin camera-facing streaks beside the body every StreakEvery u, and
        /// the SPRINT word over the fighter the first SprintWordShows times travel lights up (PlayerPrefs). Stop: a dust
        /// burst ahead of the feet. Item 4: the combo hint on my first landed hit while I have chained fewer than
        /// ChainedToLearn times ever.</summary>
        public const int Streaks = 24, SprintWordShows = 3, ChainedToLearn = 2;
        public const float StreakEvery = 0.35f, StreakSeconds = 0.28f, StreakLength = 1.1f, StreakWidth = 0.05f;
        public const string SprintWord = "SPRINT", SprintPref = "cm.moves.sprintword", ChainedPref = "cm.moves.chained";
        readonly Piece[] _streaks = new Piece[Streaks];
        int _nextStreak;
        float[] _streakDist = new float[64];
        int _streakSide;
        public int StreaksAlive { get; private set; }

        sealed class Piece
        {
            public GameObject Go;
            public Transform T;
            public MeshFilter Filter;
            public Mesh Mesh;
            public Material Mat;
            public float Born = -1f, Life;
            public float Key = float.NaN;   // the last share the mesh was built for (rebuild only on change)
            public Color Colour;
        }

        void Awake()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            Shader sh = CriticalMass.Runtime.View.UnlitKeep.Get();   // fix/px-rough-spots: kept in the build (was magenta)
            _unlit = sh != null ? new Material(sh) { name = "Moves Unlit" } : null;
            if (_unlit != null)
            {
                // transparent, so the meter and the telegraph never hide the ground they sit on
                if (_unlit.HasProperty("_Surface")) _unlit.SetFloat("_Surface", 1f);
                if (_unlit.HasProperty("_Blend")) _unlit.SetFloat("_Blend", 0f);
                _unlit.SetOverrideTag("RenderType", "Transparent");
                _unlit.renderQueue = 3000;
                _unlit.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _unlit.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _unlit.SetInt("_ZWrite", 0);
                _unlit.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            for (int i = 0; i < Arcs; i++) _arcs[i] = Make("Guard Meter " + i, true);
            for (int i = 0; i < Shapes; i++) _shapes[i] = Make("Heavy Telegraph " + i, true);
            for (int i = 0; i < Glows; i++) { _glows[i] = Make("Charge Glow " + i, true); Disc(_glows[i].Mesh, 0f, GlowRadius, 360f, ShapeSegments); }
            for (int i = 0; i < Puffs; i++) { _puffs[i] = Make("Moves Dust " + i, true); Disc(_puffs[i].Mesh, 0f, 0.5f, 360f, 12); }
            for (int i = 0; i < Rings; i++) _rings[i] = Make("Moves Ring " + i, true);
            for (int i = 0; i < Arcs; i++)
            {
                _shields[i] = Make("Guard Shield " + i, true);
                Band(_shields[i].Mesh, ShieldR, ShieldY0, ShieldY1, ShieldArc, 20);
                if (_shields[i].Mat != null && _shields[i].Mat.HasProperty("_Cull")) _shields[i].Mat.SetFloat("_Cull", 0f);
            }
            for (int i = 0; i < Streaks; i++) { _streaks[i] = Make("Sprint Streak " + i, true); Quad(_streaks[i].Mesh, StreakWidth, StreakLength); }
            _chainRing = Make("Combo Window Ring", true);
            Disc(_chainRing.Mesh, 0.80f, 0.94f, 360f, ArcSegments);
            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Destroy(_unlit);
            WorldWords.MovesCap = false;
            if (_hintCanvas != null) Destroy(_hintCanvas.gameObject);
            if (_chainRing != null) { if (_chainRing.Mat != null) Destroy(_chainRing.Mat); if (_chainRing.Mesh != null) Destroy(_chainRing.Mesh); }
            foreach (Piece[] set in new[] { _arcs, _shapes, _glows, _puffs, _rings, _shields, _streaks })
                foreach (Piece p in set)
                {
                    if (p == null) continue;
                    if (p.Mat != null) Destroy(p.Mat);
                    if (p.Mesh != null) Destroy(p.Mesh);
                }
            MovesPoses.ClearHolds();
        }

        Piece Make(string name, bool ownMaterial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var p = new Piece { Go = go, T = go.transform, Filter = go.AddComponent<MeshFilter>(), Mesh = new Mesh { name = name } };
            p.Mesh.MarkDynamic();
            p.Filter.sharedMesh = p.Mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (_unlit != null) { p.Mat = ownMaterial ? new Material(_unlit) : _unlit; r.sharedMaterial = p.Mat; }
            go.SetActive(false);
            return p;
        }

        static void Tint(Piece p, Color c)
        {
            if (p.Mat == null || p.Colour == c) return;
            p.Colour = c;
            if (p.Mat.HasProperty("_BaseColor")) p.Mat.SetColor("_BaseColor", c);
            if (p.Mat.HasProperty("_Color")) p.Mat.SetColor("_Color", c);
        }

        static void Show(Piece p, bool on) { if (p.Go.activeSelf != on) p.Go.SetActive(on); }

        // ------------------------------------------------------------------ meshes (flat, XZ plane, +Z = facing)

        /// <summary>A flat ring sector (inner 0 = a filled sector) centred on +Z, arcDeg wide, rebuilt in place.</summary>
        public static void Disc(Mesh mesh, float inner, float outer, float arcDeg, int segments)
        {
            segments = Mathf.Clamp(segments, 3, ArcSegments);
            arcDeg = Mathf.Clamp(arcDeg, 0f, 360f);
            float half = arcDeg * 0.5f * Mathf.Deg2Rad;
            int nv = (segments + 1) * 2;
            var verts = new Vector3[nv];   // warm-up only per size change: segments are fixed per piece kind
            for (int s = 0; s <= segments; s++)
            {
                float a = -half + 2f * half * s / segments;
                float sx = Mathf.Sin(a), cz = Mathf.Cos(a);
                verts[s * 2] = new Vector3(sx * inner, 0f, cz * inner);
                verts[s * 2 + 1] = new Vector3(sx * outer, 0f, cz * outer);
            }
            var tris = new int[segments * 6];
            for (int s = 0; s < segments; s++)
            {
                int b = s * 2;
                tris[s * 6] = b; tris[s * 6 + 1] = b + 1; tris[s * 6 + 2] = b + 3;
                tris[s * 6 + 3] = b; tris[s * 6 + 4] = b + 3; tris[s * 6 + 5] = b + 2;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>A flat double-sided quad in the XZ plane, width along X, length along Z, centred.</summary>
        public static void Quad(Mesh mesh, float width, float length)
        {
            float x = width * 0.5f, z = length * 0.5f;
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(-x, 0f, -z), new Vector3(x, 0f, -z), new Vector3(-x, 0f, z), new Vector3(x, 0f, z) };
            mesh.triangles = new[] { 0, 2, 3, 0, 3, 1, 0, 3, 2, 0, 1, 3 };
            mesh.RecalculateBounds();
        }

        /// <summary>A vertical band (part of a cylinder) centred on +Z, double-sided (both windings), rebuilt in place.</summary>
        public static void Band(Mesh mesh, float radius, float y0, float y1, float arcDeg, int segments)
        {
            segments = Mathf.Clamp(segments, 3, ArcSegments);
            float half = Mathf.Clamp(arcDeg, 0f, 360f) * 0.5f * Mathf.Deg2Rad;
            var verts = new Vector3[(segments + 1) * 2];
            for (int k = 0; k <= segments; k++)
            {
                float a = -half + 2f * half * k / segments;
                float sx = Mathf.Sin(a) * radius, cz = Mathf.Cos(a) * radius;
                verts[k * 2] = new Vector3(sx, y0, cz);
                verts[k * 2 + 1] = new Vector3(sx, y1, cz);
            }
            var tris = new int[segments * 12];
            for (int k = 0; k < segments; k++)
            {
                int b = k * 2, t = k * 12;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 3; tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
                tris[t + 6] = b; tris[t + 7] = b + 3; tris[t + 8] = b + 1; tris[t + 9] = b; tris[t + 10] = b + 2; tris[t + 11] = b + 3;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>Item 1 (pure): the predicted guard strength for a local press. pressed = the Guard button is down (or a
        /// tap latched), simHeld = the sim has the guard up, age = seconds since the press. 1 while waiting for the sim's tick
        /// (up to GuardPredictWait), then fading to 0 over GuardPredictFade when the sim refused; 1 once the sim holds it.</summary>
        public static float GuardPrediction(bool pressed, bool simHeld, float age)
        {
            if (simHeld) return 1f;
            if (!pressed) return 0f;
            if (age <= GuardPredictWait) return 1f;
            return Mathf.Clamp01(1f - (age - GuardPredictWait) / GuardPredictFade);
        }

        /// <summary>The meter arc colour: team colour full, red empty (linear by share).</summary>
        public static Color MeterColour(TeamId team, float share)
        {
            Color t = team == TeamId.Crimson ? new Color(1.00f, 0.42f, 0.36f, 0.9f) : new Color(0.36f, 0.72f, 1.00f, 0.9f);
            Color empty = new Color(0.95f, 0.12f, 0.10f, 0.95f);
            return Color.Lerp(empty, t, Mathf.Clamp01(share));
        }

        /// <summary>The charge glow alpha: three steps (1/3, 2/3, full) or, with reduce flashing, a gentler smooth ramp.</summary>
        public static float GlowAlpha(float share, bool reduceFlashing)
        {
            if (share <= 0f) return 0f;
            if (reduceFlashing) return 0.12f + 0.33f * Mathf.Clamp01(share);
            int step = MovesRead.ChargeStep(share);
            return step == 1 ? 0.22f : step == 2 ? 0.42f : 0.65f;
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponentInParent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            float now = Time.time;
            TickPuffs(now);
            TickRings(now);
            TickStreaks(now);
            GuardArcsShown = ChargeShapesShown = GlideRingsShown = 0;
            if (!MovesRead.On(w))
            {
                HideAll(_arcs);
                HideAll(_shapes);
                HideAll(_glows);
                HideAll(_shields);
                HideAll(_streaks);
                if (_chainRing != null) Show(_chainRing, false);
                WorldWords.MovesCap = false;
                WorldWords.Focus = -1;
                MovesPoses.PredictActor = -1;
                MovesPoses.PredictGuard = 0f;
                _hintLeft = 0f;
                TickHintLine(Time.unscaledDeltaTime);
                _world = null;
                return;
            }
            WorldWords.MovesCap = true;   // item 2: words per body by priority (readability-3: one)
            WorldWords.Focus = _focus >= 0 && _focus < w.ActorCount && w.Actors[_focus].Alive && Time.unscaledTime - _focusAt <= FocusSeconds ? _focus : -1;
            double alpha = _driver.Alpha;
            bool reduce = !CriticalMass.Runtime.View.Moments.FlashSafety.IntenseAllowed;
            int n = w.ActorCount;
            Grow(n);
            Events(w);
            int meG = HudBinder.LocalActor;
            float pred = PredictGuard(w, meG);
            int arc = 0, shape = 0, glow = 0, ring = 0, shield = 0;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive) { _wasSliding[i] = _wasLanding[i] = false; continue; }
                double x = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * alpha;
                double z = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * alpha;
                float gy = w.Map != null ? (float)GroundHeight.At(w.Map, x, z) : 0f;
                float yaw = SimSpace.YawDeg(a.Facing);

                // GUARD meter: while held, or while it is below full (refilling), for classes that have one
                if (arc < Arcs && MovesRead.HasGuardMeter(w, a.Cls))
                {
                    float share = MovesRead.GuardMeter01(w, i);
                    bool simHeld = MovesRead.GuardHeld(w, i);
                    float strength = simHeld ? 1f : i == meG ? pred : 0f;   // item 1: the local press shows on its own frame
                    bool held = strength > 0f;
                    if (held || share < 0.999f)
                    {
                        Piece p = _arcs[arc++];
                        float key = Mathf.Round(share * ArcSegments) / ArcSegments;
                        if (p.Key != key) { p.Key = key; Disc(p.Mesh, ArcInner, ArcOuter, 360f * Mathf.Max(0.02f, key), ArcSegments); }
                        Color c = MeterColour(a.Team, share);
                        if (!held) c.a *= 0.45f;   // refilling, guard down: dimmer
                        else c.a *= Mathf.Max(0.45f, strength);
                        Tint(p, c);
                        p.T.localPosition = SimSpace.ToWorld(x, gy + ArcY, z);
                        p.T.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Show(p, true);
                    }
                    // item 1: the SHIELD band in front of the guarding body (team colour, a short raise flash), seen from behind
                    if (held && !_guardWas[i]) _guardShownAt[i] = now;
                    _guardWas[i] = held;
                    if (held && shield < Arcs)
                    {
                        Piece sp = _shields[shield++];
                        float flash = reduce ? 0f : ShieldFlash * Mathf.Clamp01(1f - (now - _guardShownAt[i]) / ShieldFlashSeconds);
                        Color sc = MeterColour(a.Team, share);
                        sc.a = (ShieldAlpha + flash) * strength;
                        Tint(sp, sc);
                        sp.T.localPosition = SimSpace.ToWorld(x, gy, z);
                        sp.T.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Show(sp, true);
                    }
                }
                else if (i < _guardWas.Length) _guardWas[i] = false;

                // CHARGE: glow under the charger + the heavy's real ground shape along the facing (the telegraph)
                if (MovesRead.Charging(w, i, out float cs))
                {
                    if (glow < Glows)
                    {
                        Piece g = _glows[glow++];
                        Tint(g, new Color(1f, 0.78f, 0.35f, GlowAlpha(cs, reduce)));
                        g.T.localPosition = SimSpace.ToWorld(x, gy + ShapeY * 0.5f, z);
                        g.T.localRotation = Quaternion.identity;
                        Show(g, true);
                    }
                    if (shape < Shapes)
                    {
                        Piece s = _shapes[shape++];
                        MovesRead.HeavyShape(w, a.Cls, out float reach, out float arcDeg);
                        float key = a.Cls == ClassId.Builder ? -reach : reach * 1000f + arcDeg;
                        if (s.Key != key) { s.Key = key; Disc(s.Mesh, 0.35f, Mathf.Max(0.4f, reach), arcDeg, ShapeSegments); }
                        bool enemy = HudBinder.LocalActor >= 0 && HudBinder.LocalActor < n && w.Actors[HudBinder.LocalActor].Team != a.Team;
                        Color c = enemy ? new Color(1f, 0.30f, 0.25f, 0.18f + 0.30f * cs) : new Color(1f, 0.85f, 0.55f, 0.12f + 0.22f * cs);
                        Tint(s, c);
                        s.T.localPosition = SimSpace.ToWorld(x, gy + ShapeY, z);
                        s.T.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Show(s, true);
                    }
                }

                // TRAVEL dust every DustEvery u travelled; SLIDE plume
                double ddx = x - _lastX[i], ddz = z - _lastZ[i];
                float moved = (float)System.Math.Sqrt(ddx * ddx + ddz * ddz);
                _lastX[i] = x; _lastZ[i] = z;
                if (moved > 2f) moved = 0f;   // a respawn or a teleport
                bool sliding = MovesRead.Sliding(w, i);
                if (sliding && !_wasSliding[i]) for (int k = 0; k < 3; k++) Puff(x, gy, z, 0.6f + 0.15f * k, new Color(0.86f, 0.80f, 0.68f, 0.7f));
                _wasSliding[i] = sliding;
                bool travel = MovesRead.Travelling(w, i);
                if (travel && !sliding)
                {
                    // item 2: speed lines beside the sprinting body (camera-facing), one side then the other
                    _streakDist[i] += moved;
                    if (_streakDist[i] >= StreakEvery && moved > 0f)
                    {
                        _streakDist[i] = 0f;
                        Streak(x, gy, z, ddx / moved, ddz / moved, reduce);
                    }
                }
                else _streakDist[i] = 0f;
                if (sliding || travel)
                {
                    _dustDist[i] += moved;
                    float every = sliding ? DustEvery * 0.5f : DustEvery;
                    if (_dustDist[i] >= every)
                    {
                        _dustDist[i] = 0f;
                        Puff(x, gy, z, sliding ? 0.8f : 0.5f, new Color(0.84f, 0.78f, 0.66f, sliding ? 0.6f : 0.4f));
                    }
                }
                else _dustDist[i] = 0f;

                // GLIDE ring (time left) at the feet and a soft trail
                if (MovesRead.Gliding(w, i))
                {
                    float left = MovesRead.GlideLeft01(w, i);
                    float lift = JumpLift.Offset(w, w.Map, i, alpha, x, z).y + MovesPoses.GlideLift(w, i, alpha);
                    if (ring < Rings && GlideRing(ring, x, gy + lift + 0.05f, z, left)) { ring++; GlideRingsShown++; }
                    _dustDist[i] += moved;
                    if (_dustDist[i] >= DustEvery * 1.5f) { _dustDist[i] = 0f; Puff(x, gy + lift + 0.3f, z, 0.35f, new Color(0.70f, 1.00f, 0.78f, reduce ? 0.25f : 0.4f)); }
                }

                // LANDING dust after an evading jump (the 4-tick landing recovery begins)
                bool landing = MovesRead.LandingLocked(w, i);
                if (landing && !_wasLanding[i]) for (int k = 0; k < 4; k++) Puff(x + 0.3 * Mathf.Cos(k * 1.57f), gy, z + 0.3 * Mathf.Sin(k * 1.57f), 0.5f, new Color(0.84f, 0.78f, 0.66f, 0.55f));
                _wasLanding[i] = landing;
            }
            for (int k = arc; k < Arcs; k++) Show(_arcs[k], false);
            for (int k = shield; k < Arcs; k++) Show(_shields[k], false);
            ShieldsShown = shield;
            ComboWindow(w, alpha, reduce);
            TickHintLine(Time.unscaledDeltaTime);
            for (int k = shape; k < Shapes; k++) Show(_shapes[k], false);
            for (int k = glow; k < Glows; k++) Show(_glows[k], false);
            for (int k = ring; k < Rings; k++) if (_rings[k].Born < 0) Show(_rings[k], false);
            GuardArcsShown = arc;
            ChargeShapesShown = shape;
            int mine = HudBinder.LocalActor;
            if (mine >= 0 && mine < n && w.Actors[mine].Alive)
            {
                if (w.Actors[mine].Cls == ClassId.Demolitions) Hint(w, mine, MovesHelp.Verb.Tnt);
                bool air = w.Actors[mine].AirLen > 0;
                if (air && !_wasAir) Hint(w, mine, MovesHelp.Verb.Glide);
                _wasAir = air;
            }
        }

        /// <summary>Item 1: the local fighter's Guard press, read on the frame it happens (HumanInputSource's state, read only),
        /// handed to ActorAnimator through MovesPoses.PredictActor / PredictGuard.</summary>
        float PredictGuard(World w, int me)
        {
            // readability-3 item 6: computed once per frame by whoever asks first (ActorAnimator poses BEFORE this view runs,
            // so the press frame's pose used to see the previous frame's prediction: 0.00 on the press frame)
            float g = MovesPoses.LocalGuard(w, me);
            GuardPredicted01 = g;
            return g;
        }

        /// <summary>Item 5: while my next Attack press will chain (the sim's chain window), a ring on the fighter I hit; the
        /// first time the window opens in a match, the combo hint. The attack slot's pulse is GameHud's (MovesRead.ChainNext).</summary>
        void ComboWindow(World w, double alpha, bool reduce)
        {
            int me = HudBinder.LocalActor;
            int next = me >= 0 && me < w.ActorCount && w.Actors[me].Alive ? MovesRead.ChainNext(w, me) : 0;
            bool chain = next >= 2;
            float now = Time.unscaledTime;
            if (chain && !_wasChain) { _chainAt = now; Hint(w, me, MovesHelp.Verb.Combo); }
            _wasChain = chain;
            int v = _lastVictimOfMe[0];
            bool on = chain && v >= 0 && v < w.ActorCount && w.Actors[v].Alive;
            ChainRingShown = on;
            if (_chainRing == null) return;
            if (!on) { Show(_chainRing, false); return; }
            double x = w.PrevX[v] + (w.X[v] - w.PrevX[v]) * alpha, z = w.PrevZ[v] + (w.Z[v] - w.PrevZ[v]) * alpha;
            float gy = w.Map != null ? (float)GroundHeight.At(w.Map, x, z) : 0f;
            float k = Mathf.Clamp01((now - _chainAt) / 0.25f);
            Tint(_chainRing, new Color(1f, 0.86f, 0.30f, reduce ? 0.7f : 0.9f - 0.25f * k));
            _chainRing.T.localPosition = SimSpace.ToWorld(x, gy + ArcY + 0.01, z);
            _chainRing.T.localRotation = Quaternion.identity;
            _chainRing.T.localScale = Vector3.one * (reduce ? 1f : 1.25f - 0.25f * k);   // closes in once, never blinks
            Show(_chainRing, true);
        }

        // ------------------------------------------------------------------ item 2: the hint line (one fixed HUD row)

        void BuildHintLine()
        {
            var go = new GameObject("Moves Hint Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _hintCanvas = go.AddComponent<Canvas>();
            _hintCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hintCanvas.sortingOrder = 11;
            var root = (RectTransform)go.transform;
            _hintChip = MenuUi.Rt(root, "Hint Chip");
            _hintChip.anchorMin = _hintChip.anchorMax = new Vector2(0.5f, 0f);
            _hintChip.pivot = new Vector2(0.5f, 0f);
            _hintChip.sizeDelta = new Vector2(HintChipW, HintChipH);
            _hintGroup = _hintChip.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.interactable = _hintGroup.blocksRaycasts = false;
            VectorShape body = MenuUi.Shape(_hintChip, "Body", ShapeKind.RoundRect, 0f, 0f, HintChipW, HintChipH, CMTokens.PanelNavy.Color, -1f);
            body.OutlinePx = 3f;
            body.RimPx = 2f;
            body.SetRim(CMTokens.ToolBrass.Color);
            _hintText = MenuUi.Fit(MenuUi.Label(_hintChip, "Line", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                12f, 0f, HintChipW - 24f, HintChipH, "", 3f), 16);
            _hintChip.gameObject.SetActive(false);
        }

        void ShowHintLine(string text)
        {
            if (_hintCanvas == null) BuildHintLine();
            _hintNow = text;
            _hintLeft = MovesHelp.HintSeconds;
            _hintWait = 0f;
            HudText.Set(_hintText, text);
        }

        /// <summary>The lowest prompt row (PromptStack) no prompt holds this frame, or -1 (the hint waits, up to 4 s).</summary>
        public static int FreePromptRow()
        {
            int a = PromptStack.SlotOf(PromptStack.Source.Station), b = PromptStack.SlotOf(PromptStack.Source.Pill), c = PromptStack.SlotOf(PromptStack.Source.Strip);
            int visible = PxFight.Active ? 1 : PromptStack.Visible(HudZones.Blocked(HudZone.Lower, HudPriority.Guide));
            for (int r = 0; r < visible; r++) if (r != a && r != b && r != c) return r;
            return -1;
        }

        void TickHintLine(float dt)
        {
            HintLineSlot = -1;
            if (_hintChip == null) return;
            if (_hintLeft <= 0f) { if (_hintChip.gameObject.activeSelf) _hintChip.gameObject.SetActive(false); return; }
            if (_hud == null) _hud = FindAnyObjectByType<GameHud>();
            bool hudOn = _hud != null && _hud.Canvas != null && _hud.Canvas.enabled && !CriticalMass.Runtime.Menus.MenuGate.AnyOpen;
            int row = hudOn ? FreePromptRow() : -1;
            if (row < 0)
            {
                if (_hintChip.gameObject.activeSelf) _hintChip.gameObject.SetActive(false);
                _hintWait += dt;
                if (_hintWait > 4f) _hintLeft = 0f;   // never queued for long: a stale hint is worse than none
                return;
            }
            float w = Screen.width, h = Screen.height;
            float s = HudLayout.Scale(w, h);
            if (s > 0f && Mathf.Abs(_hintCanvas.scaleFactor - s) > 1e-4f) _hintCanvas.scaleFactor = s;
            _hintLeft -= dt;
            HintLineSlot = row;
            var p = new Vector2(0f, PromptStack.Bottom(row, HintChipH));
            if ((_hintChip.anchoredPosition - p).sqrMagnitude > 0.25f) _hintChip.anchoredPosition = p;
            _hintGroup.alpha = Mathf.Clamp01(_hintLeft / 0.25f);
            if (!_hintChip.gameObject.activeSelf) _hintChip.gameObject.SetActive(true);
        }

        /// <summary>A first-use hint over my fighter: once per verb per match, at most MovesHelp.HintShows times ever, one
        /// every 4 s at most, only while that verb's switch is on.</summary>
        void Hint(World w, int me, MovesHelp.Verb v)
        {
            if (me < 0 || me >= w.ActorCount || _hinted[(int)v]) return;
            if (!MovesHelp.SwitchOn(w, v, w.Actors[me].Cls)) return;
            if (Time.unscaledTime - _hintAt < 4f) return;
            string pref = MovesHelp.PrefKey(v);
            int seen = PlayerPrefs.GetInt(pref, 0);
            _hinted[(int)v] = true;
            if (seen >= MovesHelp.HintShows && !HintsAlways) return;
            string key = CriticalMass.Runtime.Input.InputGlyphs.Words(MovesHelp.HintAction(v));
            string text = MovesHelp.HintText(v, key);
            ShowHintLine(text);   // item 2: the fixed hint row, never in world space over a fighter
            _hintAt = Time.unscaledTime;
            PlayerPrefs.SetInt(pref, seen + 1);
            Said(text);
        }

        void Grow(int n)
        {
            if (_dustDist.Length >= n) return;
            System.Array.Resize(ref _dustDist, n);
            System.Array.Resize(ref _lastX, n);
            System.Array.Resize(ref _lastZ, n);
            System.Array.Resize(ref _wasLanding, n);
            System.Array.Resize(ref _wasSliding, n);
            System.Array.Resize(ref _guardShownAt, n);
            System.Array.Resize(ref _streakDist, n);
            System.Array.Resize(ref _guardWas, n);
        }

        static void HideAll(Piece[] set) { for (int i = 0; i < set.Length; i++) Show(set[i], false); }

        bool GlideRing(int slot, double x, float y, double z, float left)
        {
            Piece p = _rings[slot];
            if (p.Born >= 0) return false;   // the slot is a second-wind ring right now
            float key = Mathf.Round(left * ArcSegments) / ArcSegments;
            if (p.Key != key) { p.Key = key; Disc(p.Mesh, 0.5f, 0.62f, 360f * Mathf.Max(0.02f, key), ArcSegments); }
            Tint(p, new Color(0.62f, 1.00f, 0.74f, 0.85f));
            p.T.localPosition = new Vector3((float)x, y, (float)-z);
            p.T.localRotation = Quaternion.identity;
            Show(p, true);
            return true;
        }

        void Puff(double x, float gy, double z, float size, Color c)
        {
            Piece p = _puffs[_nextPuff];
            _nextPuff = (_nextPuff + 1) % Puffs;
            p.Born = Time.time;
            p.Life = PuffSeconds;
            p.Key = size;
            p.Colour = Color.clear;   // TickPuffs fades the material directly: always re-tint a reused puff
            Tint(p, c);
            p.T.localPosition = SimSpace.ToWorld(x, gy + 0.07, z);
            p.T.localScale = Vector3.one * size * 0.4f;
            Show(p, true);
            PuffsAlive++;   // counted the frame it is raised (TickPuffs recounts next frame)
        }

        void TickPuffs(float now)
        {
            int alive = 0;
            for (int i = 0; i < Puffs; i++)
            {
                Piece p = _puffs[i];
                if (p.Born < 0) continue;
                float age = now - p.Born;
                if (age >= p.Life) { p.Born = -1f; Show(p, false); continue; }
                alive++;
                float k = age / p.Life;
                p.T.localScale = Vector3.one * p.Key * (0.4f + 0.9f * k);
                Color c = p.Colour;
                if (p.Mat != null && p.Mat.HasProperty("_BaseColor")) p.Mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, c.a * (1f - k)));
            }
            PuffsAlive = alive;
        }

        /// <summary>Item 2: one speed line beside a sprinting body, along its motion (dx, dz unit, sim), facing the camera.</summary>
        void Streak(double x, float gy, double z, double dx, double dz, bool reduce)
        {
            Piece p = _streaks[_nextStreak];
            _nextStreak = (_nextStreak + 1) % Streaks;
            _streakSide = -_streakSide == 0 ? 1 : -_streakSide;
            float side = _streakSide * 0.42f, lift = _streakSide > 0 ? 0.55f : 0.95f;
            // behind the body, out to one side (sim space: the side is the motion turned 90 degrees)
            double px = x - dx * 0.55 - dz * side, pz = z - dz * 0.55 + dx * side;
            Vector3 pos = SimSpace.ToWorld(px, gy + lift, pz);
            Vector3 along = SimSpace.ToWorld(dx, 0, dz) - SimSpace.ToWorld(0, 0, 0);
            if (along.sqrMagnitude < 1e-6f) return;
            along.Normalize();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 toCam = cam != null ? (cam.transform.position - pos).normalized : Vector3.up;
            Vector3 n = toCam - Vector3.Dot(toCam, along) * along;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            p.Born = Time.time;
            p.Life = StreakSeconds;
            p.Key = reduce ? 0.35f : 0.6f;   // the start alpha
            p.Colour = Color.clear;
            Tint(p, new Color(1f, 1f, 0.95f, p.Key));
            p.T.localPosition = pos;
            p.T.localRotation = Quaternion.LookRotation(along, n.normalized);
            p.T.localScale = Vector3.one;
            Show(p, true);
        }

        void TickStreaks(float now)
        {
            int alive = 0;
            for (int i = 0; i < Streaks; i++)
            {
                Piece p = _streaks[i];
                if (p == null || p.Born < 0) continue;
                float age = now - p.Born;
                if (age >= p.Life) { p.Born = -1f; Show(p, false); continue; }
                alive++;
                float k = age / p.Life;
                p.T.localScale = new Vector3(1f, 1f, 1f + 0.6f * k);   // stretches out as it fades
                Color c = p.Colour;
                if (p.Mat != null && p.Mat.HasProperty("_BaseColor")) p.Mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, p.Key * (1f - k)));
            }
            StreaksAlive = alive;
        }

        void TickRings(float now)
        {
            for (int i = 0; i < Rings; i++)
            {
                Piece p = _rings[i];
                if (p.Born < 0) continue;
                float age = now - p.Born;
                if (age >= p.Life) { p.Born = -1f; p.Key = float.NaN; Show(p, false); continue; }
                float k = age / p.Life;
                p.T.localScale = Vector3.one * (0.8f + 1.2f * k);
            }
        }

        /// <summary>A one-shot expanding ring at the feet (second wind green, guard break crack).</summary>
        void Burst(World w, int i, Color c)
        {
            if (i < 0 || i >= w.ActorCount) return;
            Piece p = _rings[_nextRing];
            _nextRing = (_nextRing + 1) % Rings;
            p.Born = Time.time;
            p.Life = RingSeconds;
            p.Key = float.NaN;
            Disc(p.Mesh, 0.55f, 0.75f, 360f, ArcSegments);
            Tint(p, c);
            double x = w.X[i], z = w.Z[i];
            float gy = w.Map != null ? (float)GroundHeight.At(w.Map, x, z) : 0f;
            p.T.localPosition = SimSpace.ToWorld(x, gy + 0.08, z);
            p.T.localRotation = Quaternion.identity;
            p.T.localScale = Vector3.one * 0.8f;
            Show(p, true);
        }

        // ------------------------------------------------------------------ events -> words, holds, bursts

        void Events(World w)
        {
            int head = w.EventHead;
            if (!ReferenceEquals(w, _world) || w.Epoch != _epoch || _cursor < 0 || _cursor > head)
            {
                _world = w;
                _epoch = w.Epoch;
                _cursor = head;
                _lastVictimOfMe[0] = -1;
                for (int k = 0; k < _hinted.Length; k++) _hinted[k] = false;
                return;
            }
            if (head - _cursor > World.EventCapacity) _cursor = head - World.EventCapacity;
            if (_refundsSeen > w.Moves.Defence.Refunds) _refundsSeen = w.Moves.Defence.Refunds;   // a new match
            int me = HudBinder.LocalActor;
            ActionTags tags = PlayerHud.Current != null ? PlayerHud.Current.Tags : null;
            bool reduce = !CriticalMass.Runtime.View.Moments.FlashSafety.IntenseAllowed;
            double wall = Time.unscaledTimeAsDouble;
            for (; _cursor < head; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                switch (e.Type)
                {
                    case SimEventType.Hit:
                        if (e.A == me && e.B >= 0 && e.B != me) { _focus = e.B; _focusAt = Time.unscaledTime; }   // he hit me
                        if (e.B == me && e.A != me)
                        {
                            _lastVictimOfMe[0] = e.A;
                            _focus = e.A; _focusAt = Time.unscaledTime;   // readability-3 item 1: my opponent
                            // readability-2 item 4: the combo hint on my first landed hit, until I have chained twice ever
                            if (PlayerPrefs.GetInt(ChainedPref, 0) < ChainedToLearn) Hint(w, me, MovesHelp.Verb.Combo);
                        }
                        break;
                    case SimEventType.AttackStart:
                        if (e.A == me && e.B == Combat.KindAttack) Hint(w, me, MovesHelp.Verb.Heavy);
                        else if (me >= 0 && me < w.ActorCount && e.A >= 0 && e.A < w.ActorCount && e.B == Combat.KindAttack && w.Actors[e.A].Team != w.Actors[me].Team)
                        {
                            double dx = w.X[e.A] - w.X[me], dz = w.Z[e.A] - w.Z[me];
                            double reach = w.Config.Class(w.Actors[e.A].Cls).AttackRange + 1.0;
                            bool guardHint = !_hinted[(int)MovesHelp.Verb.Guard] && MovesHelp.SwitchOn(w, MovesHelp.Verb.Guard, w.Actors[me].Cls);   // item 3
                            if (dx * dx + dz * dz <= reach * reach) Hint(w, me, guardHint ? MovesHelp.Verb.Guard : MovesHelp.Verb.JumpEvade);
                        }
                        break;
                    case SimEventType.TravelChanged:
                        if (e.A == me && e.Value > 0.5)
                        {
                            // item 2: SPRINT over me the first few times travel lights up (then the speed lines say it)
                            int seen = PlayerPrefs.GetInt(SprintPref, 0);
                            if (tags != null && (seen < SprintWordShows || HintsAlways)
                                && tags.OnMovesWord(w, SprintWord, 5800, me, true, false, PxPalette.Cream, PxPalette.Brass, 0.9f, 900 + (int)e.Type) != null)   // readability-3 item 7: callout size
                            {
                                PlayerPrefs.SetInt(SprintPref, seen + 1);
                                Said(SprintWord);
                            }
                            Hint(w, me, MovesHelp.Verb.Slide);
                        }
                        else if (e.Value <= 0.5 && e.A >= 0 && e.A < w.ActorCount && w.Actors[e.A].Alive)
                        {
                            // item 2: the stop: a dust burst ahead of the feet (the body brakes out of the sprint)
                            double fx = System.Math.Cos(w.Actors[e.A].Facing), fz = System.Math.Sin(w.Actors[e.A].Facing);
                            float sy = w.Map != null ? (float)GroundHeight.At(w.Map, e.X, e.Z) : 0f;
                            for (int k = 0; k < 3; k++) Puff(e.X + fx * (0.35 + 0.15 * k) + fz * 0.2 * (k - 1), sy, e.Z + fz * (0.35 + 0.15 * k) - fx * 0.2 * (k - 1), 0.7f, new Color(0.86f, 0.80f, 0.68f, 0.75f));
                        }
                        break;
                    case SimEventType.HitBlocked:
                        if ((int)System.Math.Round(e.Value) == PlayerReasons.BlockedDodge && e.A == me)
                        {
                            // readability-3 item 7: my dodge avoided a swing; lane 2's T1-3 counts a refund (Defence.Refunds) on the
                            // same tick, so a rise in the count with my dodge is MY refund (the chip's ready-tick watch is the
                            // second witness: MechanicsHud reads DodgeBackFrame too)
                            int refunds = w.Moves.Defence.Refunds;
                            if (refunds > _refundsSeen) { DodgeBackAt = Time.unscaledTime; DodgeBackFrame = Time.frameCount; }
                        }
                        if ((int)System.Math.Round(e.Value) == PlayerReasons.BlockedParried)
                        {
                            // PARRY: 80 ms pose hold on the parrier (A) and the attacker (B); the words come through
                            // PlayerHud's HitBlocked path (ActionTags: PARRY large on both sides)
                            MovesPoses.Hold(e.A, wall, MovesPoses.ParryHoldSeconds);
                            MovesPoses.Hold(e.B, wall, MovesPoses.ParryHoldSeconds);
                            if (e.A == me || e.B == me) Said(ActionTags.ParryWord);
                        }
                        break;
                    case SimEventType.GuardBreak:
                        Burst(w, e.A, new Color(0.95f, 0.85f, 0.70f, reduce ? 0.5f : 0.8f));
                        if (tags != null && (e.A == me || e.B == me)) { tags.OnGuardBreak(w, e.A, e.B, me); Said(e.A == me ? ActionTags.GuardBrokenWord : ActionTags.GuardBreakWord); }
                        break;
                    case SimEventType.FeintBaited:
                        if (tags != null && (e.A == me || e.B == me)) { tags.OnFeintBaited(w, e.A, e.B, me); Said(ActionTags.BaitedWord); }
                        break;
                    case SimEventType.Lunge:
                        if (tags != null && e.A == me && tags.OnMovesWord(w, ActionTags.LungeWord, 5200, me, false, false, PxPalette.Cream, PxPalette.Brass, 0.7f, 900 + (int)e.Type) != null) Said(ActionTags.LungeWord);
                        break;
                    case SimEventType.HeavyRelease:
                        if (tags != null && e.A == me && e.Value >= 0.999 && tags.OnMovesWord(w, ActionTags.ChargedWord, 5300, me, true, false, PxPalette.Cream, PxPalette.Brass, 0.8f, 900 + (int)e.Type) != null) Said(ActionTags.ChargedWord);
                        break;
                    case SimEventType.ComboStep:
                        if (e.A == me && e.Value >= 2 && !HintsAlways) PlayerPrefs.SetInt(ChainedPref, PlayerPrefs.GetInt(ChainedPref, 0) + 1);   // item 4
                        if (tags != null && e.A == me)
                        {
                            string word = ActionTags.MovesWords(e.Type, e.Value, e.Tier, true, false);
                            int on = _lastVictimOfMe[0] >= 0 ? _lastVictimOfMe[0] : me;
                            if (tags.OnMovesWord(w, word, 5400 + (on & 63), on, true, e.Tier >= 1, PxPalette.StrikeInk, PxPalette.Strike, 0.7f, 900 + (int)e.Type) != null) Said(word);
                        }
                        break;
                    case SimEventType.Slide:
                        if (e.B >= 0)   // readability-3 item 8: a trip is the slide event WITH a victim (B), whatever its damage
                        {
                            for (int k = 0; k < 2; k++) Puff(e.X, w.Map != null ? (float)GroundHeight.At(w.Map, e.X, e.Z) : 0f, e.Z, 0.9f, new Color(0.86f, 0.80f, 0.68f, 0.7f));
                            if (tags != null && (e.A == me || e.B == me) && tags.OnMovesWord(w, ActionTags.TrippedWord, 5500 + (e.B & 63), e.B, true, true, PxPalette.StrikeInk, PxPalette.Strike, 0.9f, 900 + (int)e.Type) != null) Said(ActionTags.TrippedWord);
                        }
                        break;
                    case SimEventType.SecondWind:
                        Burst(w, e.A, new Color(0.36f, 0.89f, 0.54f, reduce ? 0.45f : 0.85f));
                        if (tags != null && e.A == me && tags.OnMovesWord(w, ActionTags.SecondWindWord, 5600, me, true, true, PxPalette.Cream, PxPalette.Heal, 1.3f, 900 + (int)e.Type) != null) Said(ActionTags.SecondWindWord);
                        break;
                }
            }
            _refundsSeen = w.Moves.Defence.Refunds;
        }

        /// <summary>Readability-3 item 1: the local player's opponent (the last fighter he hit or who hit him) for this long.</summary>
        public const float FocusSeconds = 3f;
        int _focus = -1;
        float _focusAt = -100f;

        /// <summary>Readability-3 item 7: my last dodge refund (wall time and frame), for the dodge chip's DODGE BACK.</summary>
        public static float DodgeBackAt = -9f;
        public static int DodgeBackFrame = -1;
        int _refundsSeen;

        void Said(string word)
        {
            LastWord = word;
            LastWordFrame = Time.frameCount;
        }
    }
}
