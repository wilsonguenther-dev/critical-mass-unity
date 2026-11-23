// Class moves build (2026-11-22), lane 6: the world-space feedback of the new verbs and their words, created by DodgeView.Awake
// (one child of the B13 item views) so it lives and dies with the driver. View only: reads World and MovesRead, never writes.
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Player;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

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

        // ---- tests / reviewer: what is on screen this frame
        public int GuardArcsShown { get; private set; }
        public int ChargeShapesShown { get; private set; }
        public int GlideRingsShown { get; private set; }
        public int PuffsAlive { get; private set; }
        public int LastWordFrame { get; private set; } = -1;
        public string LastWord { get; private set; }

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
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
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
            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            Destroy(_unlit);
            foreach (Piece[] set in new[] { _arcs, _shapes, _glows, _puffs, _rings })
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
            GuardArcsShown = ChargeShapesShown = GlideRingsShown = 0;
            if (!MovesRead.On(w))
            {
                HideAll(_arcs);
                HideAll(_shapes);
                HideAll(_glows);
                _world = null;
                return;
            }
            double alpha = _driver.Alpha;
            bool reduce = !CriticalMass.Runtime.View.Moments.FlashSafety.IntenseAllowed;
            int n = w.ActorCount;
            Grow(n);
            Events(w);
            int arc = 0, shape = 0, glow = 0, ring = 0;
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
                    bool held = MovesRead.GuardHeld(w, i);
                    if (held || share < 0.999f)
                    {
                        Piece p = _arcs[arc++];
                        float key = Mathf.Round(share * ArcSegments) / ArcSegments;
                        if (p.Key != key) { p.Key = key; Disc(p.Mesh, ArcInner, ArcOuter, 360f * Mathf.Max(0.02f, key), ArcSegments); }
                        Color c = MeterColour(a.Team, share);
                        if (!held) c.a *= 0.45f;   // refilling, guard down: dimmer
                        Tint(p, c);
                        p.T.localPosition = SimSpace.ToWorld(x, gy + ArcY, z);
                        p.T.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Show(p, true);
                    }
                }

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
                if (sliding || MovesRead.Travelling(w, i))
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
            for (int k = shape; k < Shapes; k++) Show(_shapes[k], false);
            for (int k = glow; k < Glows; k++) Show(_glows[k], false);
            for (int k = ring; k < Rings; k++) if (_rings[k].Born < 0) Show(_rings[k], false);
            GuardArcsShown = arc;
            ChargeShapesShown = shape;
        }

        void Grow(int n)
        {
            if (_dustDist.Length >= n) return;
            System.Array.Resize(ref _dustDist, n);
            System.Array.Resize(ref _lastX, n);
            System.Array.Resize(ref _lastZ, n);
            System.Array.Resize(ref _wasLanding, n);
            System.Array.Resize(ref _wasSliding, n);
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
                return;
            }
            if (head - _cursor > World.EventCapacity) _cursor = head - World.EventCapacity;
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
                        if (e.B == me && e.A != me) _lastVictimOfMe[0] = e.A;
                        break;
                    case SimEventType.HitBlocked:
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
                        if (tags != null && e.A == me)
                        {
                            string word = ActionTags.MovesWords(e.Type, e.Value, e.Tier, true, false);
                            int on = _lastVictimOfMe[0] >= 0 ? _lastVictimOfMe[0] : me;
                            if (tags.OnMovesWord(w, word, 5400 + (on & 63), on, true, e.Tier >= 1, PxPalette.StrikeInk, PxPalette.Strike, 0.7f, 900 + (int)e.Type) != null) Said(word);
                        }
                        break;
                    case SimEventType.Slide:
                        if (e.B >= 0 && e.Value > 0)
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
        }

        void Said(string word)
        {
            LastWord = word;
            LastWordFrame = Time.frameCount;
        }
    }
}
