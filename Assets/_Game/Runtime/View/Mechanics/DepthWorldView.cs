using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// The world side of the mechanics depth (U-063 to U-068), drawn from sim state and the DepthFeed into three fixed-budget
    /// meshes (the MechanicsWorldView pattern; placeholder visuals built in code until art lands): groves and springs with a
    /// stage per charge and a regrow ring, the base station prop and floor ring, pickups that bob and glow (coins, and power-ups
    /// with their arming beam and countdown), Haste / Might / Ward auras outside both team hues, wood and water cargo over
    /// heads, hand-off arcs, bank and withdraw flights, gather pops, ward ripples, upgrade ring pulses, the supply drop (claim
    /// ring, crate, contest fill), the exposed egg, the bridge warning hatch over the exact cells, falling planks and the
    /// Builder's repair ring, and the line from a bot to the ping it answered. Reads the sim only. Reduced motion: no bob,
    /// no shake, no spin; reduce flashing: steady colours.
    /// </summary>
    public sealed class DepthWorldView : MonoBehaviour
    {
        public const int GroundVerts = 12000, SolidVerts = 22000, AirVerts = 6000;

        public static DepthWorldView Active { get; private set; }
        public readonly DepthFeed Feed = new DepthFeed();
        public World World => _world;
        public double SimNow { get; private set; }

        SimDriverBehaviour _driver;
        World _world;
        FxMeshBuilder _ground, _solid, _air;
        Material _groundMat, _solidMat, _airMat;
        GameObject _groundGo, _solidGo, _airGo;

        static readonly Color32 Navy = new Color32(24, 35, 59, 255);
        static readonly Color32 Bark = new Color32(122, 84, 50, 255), BarkDark = new Color32(84, 56, 34, 255), BarkLight = new Color32(160, 116, 72, 255);
        static readonly Color32 Leaf = new Color32(92, 166, 84, 255), LeafDark = new Color32(56, 112, 60, 255), LeafLight = new Color32(140, 204, 112, 255);
        static readonly Color32 WaterC = new Color32(96, 176, 232, 255), WaterDark = new Color32(52, 112, 176, 255), WaterLight = new Color32(186, 228, 255, 255);
        static readonly Color32 DryBasin = new Color32(140, 112, 84, 255), Stone = CMTokens.Stone.Color, Cream = CMTokens.WarmCream.Color;
        static readonly Color32 Brass = CMTokens.ToolBrass.Color, BrassDark = new Color32(150, 112, 40, 255), Danger = CMTokens.Crimson.Color;
        static readonly Color32 Gold = new Color32(255, 206, 84, 255), GoldDark = new Color32(196, 140, 40, 255), Seam = CMTokens.EggSeam.Color;
        static readonly Color32 Crate = new Color32(186, 140, 84, 255), CrateDark = new Color32(122, 88, 52, 255), Hatch = new Color32(236, 72, 56, 255);

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) { enabled = false; return; }
            Shader sh = UnityEngine.Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (sh == null) { enabled = false; return; }
            _ground = new FxMeshBuilder("Depth Ground", GroundVerts, GroundVerts * 3);
            _solid = new FxMeshBuilder("Depth Solid", SolidVerts, SolidVerts * 2);
            _air = new FxMeshBuilder("Depth Air", AirVerts, AirVerts * 3);
            _groundMat = Mat(sh, "Depth Ground", 0, 0, -2, 2986);
            _solidMat = Mat(sh, "Depth Solid", 2, 1, 0, 2456);
            _airMat = Mat(sh, "Depth Air", 0, 0, 0, 3096);
            _groundGo = Layer("Depth Ground", _ground, _groundMat);
            _solidGo = Layer("Depth Solid", _solid, _solidMat);
            _airGo = Layer("Depth Air", _air, _airMat);
            _world = _driver.World;
            Feed.Reset();
            _driver.Ticked -= OnTicked;
            _driver.Ticked += OnTicked;
            Active = this;
        }

        void OnDestroy()
        {
            if (_driver != null) _driver.Ticked -= OnTicked;
            if (Active == this) Active = null;
            Release(_groundGo); Release(_solidGo); Release(_airGo);
            Release(_groundMat); Release(_solidMat); Release(_airMat);
            if (_ground != null) Release(_ground.Mesh);
            if (_solid != null) Release(_solid.Mesh);
            if (_air != null) Release(_air.Mesh);
        }

        static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static Material Mat(Shader sh, string name, int cull, int zwrite, int offset, int queue)
        {
            var m = new Material(sh) { name = name, renderQueue = queue };
            m.SetFloat("_Cull", cull);
            m.SetFloat("_ZWrite", zwrite);
            m.SetFloat("_OffsetUnits", offset);
            return m;
        }

        GameObject Layer(string n, FxMeshBuilder b, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        void OnTicked(World w)
        {
            if (w != _world) { _world = w; Feed.Reset(); }
            Feed.Pace = _driver != null && _driver.TimeScale > 0 ? _driver.TimeScale : 2;
            Feed.Read(w);
        }

        void LateUpdate()
        {
            if (_driver == null || _driver.World == null) return;
            if (_driver.World != _world) { _world = _driver.World; Feed.Reset(); }
            World w = _world;
            double alpha = _driver.Alpha;
            SimNow = (w.Tick + alpha) / w.Config.SimHz;
            Feed.Pace = _driver.TimeScale > 0 ? _driver.TimeScale : 2;
            _ground.Clear();
            _solid.Clear();
            _air.Clear();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.down;
            bool still = FeelGate.ReduceMotion, steady = FeelGate.ReduceFlashing;
            float wall = (float)Feed.Wall(SimNow);
            int me = Feed.LocalActor;
            TeamId viewer = me >= 0 && me < w.ActorCount ? w.Actors[me].Team : TeamId.Azure;
            if (DepthRead.On(w))
            {
                DrawSources(w, wall, still);
                DrawStations(w, viewer);
                DrawPickups(w, alpha, wall, still, camFwd);
                DrawAuras(w, alpha, wall, still, camFwd);
                DrawEvents(w, alpha, wall, still, steady, me, viewer, camFwd);
            }
            DrawCargo(w, alpha, wall, still);
            DrawFeed(w, alpha, wall, camFwd, viewer);
            _ground.Upload();
            _solid.Upload();
            _air.Upload();
        }

        // ------------------------------------------------------------------ helpers

        static Color32 A(Color32 c, float a) { c.a = (byte)Mathf.Clamp(a * 255f, 0, 255); return c; }
        static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
        float H(double x, double z) => (float)GroundHeight.At(_world.Map, x, z);
        Vector3 P(double x, double z, float lift) => SimSpace.ToWorld(x, H(x, z) + lift, z);

        Vector3 ActorPos(World w, int id, double alpha, float lift)
        {
            double x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha, z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
            return P(x, z, lift);
        }

        void GroundRing(Vector3 c, float r0, float r1, float a0, float a1, int seg, Color32 col) =>
            _ground.Ring(c, Vector3.right, Vector3.forward, r0, r1, a0, a1, seg, col);

        void Countdown(Vector3 c, float r, float wdt, float frac, Color32 col, float alpha)
        {
            const float top = Mathf.PI * 0.5f;
            GroundRing(c, r - wdt - 0.03f, r + 0.03f, 0f, Mathf.PI * 2f, 28, A(Navy, 0.7f * alpha));
            if (frac > 0.001f) GroundRing(c, r - wdt, r, top - Mathf.PI * 2f * frac, top, Mathf.Max(3, (int)(28 * frac)), A(col, alpha));
        }

        static Vector3 Right(Vector3 camFwd)
        {
            Vector3 r = Vector3.Cross(Vector3.up, camFwd);
            return r.sqrMagnitude > 1e-6f ? r.normalized : Vector3.right;
        }

        // ------------------------------------------------------------------ groves and springs (U-064)

        void DrawSources(World w, float wall, bool still)
        {
            int n = DepthRead.SourceCount(w);
            for (int i = 0; i < n; i++)
            {
                if (!DepthRead.Source(w, i, out bool water, out double x, out double z, out int ch, out int max, out int regrow)) continue;
                Vector3 c = P(x, z, 0.02f);
                float full = max > 0 ? Mathf.Clamp01((float)ch / max) : 0f;
                if (water) Spring(c, full, ch, wall, still, i);
                else Grove(c, ch, max, i);
                if (ch <= 0)
                {
                    // the regrow ring: a slow turning dashed ring (the full regrow time is a rule number; the ring just says "coming back")
                    float rot = still ? 0f : wall * 0.6f;
                    for (int k = 0; k < 10; k++)
                    {
                        float a0 = rot + k * Mathf.PI * 0.2f;
                        GroundRing(c, 1.35f, 1.48f, a0, a0 + Mathf.PI * 0.12f, 3, A(water ? WaterC : Leaf, 0.7f));
                    }
                }
            }
        }

        void Grove(Vector3 c, int ch, int max, int seed)
        {
            // up to four trees; one tree per charge stands, the rest are stumps
            int trees = Mathf.Clamp(max > 0 ? max : 4, 1, 4);
            _ground.Blob(c, Vector3.right, Vector3.forward, 1.25f, null, 14, 0f, A(LeafDark, 0.35f));
            for (int t = 0; t < trees; t++)
            {
                float ang = (seed * 1.7f + t * 1.5708f + 0.4f);
                Vector3 at = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (t == 0 ? 0.25f : 0.75f);
                bool standing = t < ch;
                Quaternion q = Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f);
                if (!standing)
                {
                    _solid.Chunk(at + Vector3.up * 0.1f, q, new Vector3(0.13f, 0.1f, 0.13f), 0.02f, BarkLight, Bark, BarkDark, Navy);
                    continue;
                }
                float hgt = 0.9f + 0.15f * ((seed + t) % 3);
                _solid.Chunk(at + Vector3.up * (hgt * 0.5f), q, new Vector3(0.1f, hgt * 0.5f, 0.1f), 0.025f, BarkLight, Bark, BarkDark, Navy);
                _solid.Chunk(at + Vector3.up * (hgt + 0.25f), q, new Vector3(0.42f, 0.3f, 0.42f), 0.03f, LeafLight, Leaf, LeafDark, Navy);
                _solid.Chunk(at + Vector3.up * (hgt + 0.62f), q * Quaternion.Euler(0f, 45f, 0f), new Vector3(0.27f, 0.2f, 0.27f), 0.03f, LeafLight, Leaf, LeafDark, Navy);
            }
        }

        void Spring(Vector3 c, float full, int ch, float wall, bool still, int seed)
        {
            // a stone rim, the pool (smaller per charge used), and a light column while it holds water
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f + seed;
                _solid.Chunk(c + new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a)) * 1.0f, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f),
                    new Vector3(0.2f, 0.09f, 0.14f), 0.02f, Mix(Stone, Cream, 0.3f), Stone, Mix(Stone, Navy, 0.4f), Navy);
            }
            if (ch <= 0)
            {
                _ground.Blob(c, Vector3.right, Vector3.forward, 0.85f, null, 16, 0f, A(DryBasin, 0.9f));
                return;
            }
            float r = 0.4f + 0.45f * full;
            float ripple = still ? 0f : 0.04f * Mathf.Sin(wall * 2.2f + seed);
            _ground.Blob(c + Vector3.up * 0.04f, Vector3.right, Vector3.forward, r + ripple, null, 18, 0f, A(WaterC, 0.95f));
            GroundRing(c + Vector3.up * 0.05f, r * 0.55f, r * 0.62f, 0f, Mathf.PI * 2f, 18, A(WaterLight, 0.7f));
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 right = Right(cam != null ? cam.transform.forward : Vector3.down);
            _air.Add = 180;
            _air.Quad(c + Vector3.up * 1.4f, right * (0.18f + 0.1f * full), Vector3.up * 1.4f, A(WaterLight, 0.55f), A(WaterLight, 0f));
            _air.Add = 0;
        }

        // ------------------------------------------------------------------ base stations (U-065)

        void DrawStations(World w, TeamId viewer)
        {
            if (!DepthRead.StationsOn(w) || w.Map.Pads == null) return;
            float r = (float)DepthRead.StationRange(w);
            for (int t = 0; t < 2 && t < w.Map.Pads.Length; t++)
            {
                CriticalMass.Sim.Grid.PadDef pad = w.Map.Pads[t];
                Color32 team = CMTokens.Team((TeamId)t);
                Vector3 c = P(pad.X, pad.Z, 0.03f);
                GroundRing(c, r - 0.12f, r, 0f, Mathf.PI * 2f, 48, A(team, (TeamId)t == viewer ? 0.85f : 0.45f));
                // prop: a bench, a banner pole with the team flag, and six class racks with level pips
                Vector3 prop = c + new Vector3(r * 0.55f, 0f, 0f);
                _solid.Chunk(prop + Vector3.up * 0.3f, Quaternion.identity, new Vector3(0.6f, 0.06f, 0.25f), 0.03f, Crate, Mix(Crate, CrateDark, 0.5f), CrateDark, Navy);
                _solid.Chunk(prop + new Vector3(-0.5f, 0.15f, 0f), Quaternion.identity, new Vector3(0.06f, 0.15f, 0.2f), 0.02f, Crate, CrateDark, CrateDark, Navy);
                _solid.Chunk(prop + new Vector3(0.5f, 0.15f, 0f), Quaternion.identity, new Vector3(0.06f, 0.15f, 0.2f), 0.02f, Crate, CrateDark, CrateDark, Navy);
                Vector3 pole = c + new Vector3(r * 0.55f, 0f, 0.7f);
                _solid.Chunk(pole + Vector3.up * 1.0f, Quaternion.identity, new Vector3(0.05f, 1.0f, 0.05f), 0.02f, Brass, Brass, BrassDark, Navy);
                _solid.Chunk(pole + new Vector3(0.32f, 1.7f, 0f), Quaternion.identity, new Vector3(0.3f, 0.22f, 0.02f), 0.02f, team, team, Mix(team, Navy, 0.4f), Navy);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 rack = c + new Vector3(r * 0.55f - 0.75f + k * 0.3f, 0f, -0.6f);
                    int lvl = DepthRead.Level(w, (TeamId)t, (ClassId)k);
                    _solid.Chunk(rack + Vector3.up * 0.28f, Quaternion.identity, new Vector3(0.05f, 0.28f, 0.05f), 0.015f, BarkLight, Bark, BarkDark, Navy);
                    for (int p = 1; p < lvl && p < 3; p++)
                        _solid.Chunk(rack + Vector3.up * (0.4f + p * 0.12f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.06f, 0.04f, 0.06f), 0.012f, Brass, Brass, BrassDark, Navy);
                }
            }
        }

        // ------------------------------------------------------------------ pickups (U-066)

        void DrawPickups(World w, double alpha, float wall, bool still, Vector3 camFwd)
        {
            int cap = Math.Min(DepthRead.PickupCapacity(w), DepthFeed.FadeSlots);
            Vector3 right = Right(camFwd);
            for (int s = 0; s < cap; s++)
            {
                if (!DepthRead.PickupAt(w, s, out int kind, out double x, out double z, out int value, out int ready, out int end))
                {
                    // fading out an expired one (0.4 s)
                    DepthFeed.Fade f = Feed.FadeRing[s];
                    float fa = (float)Feed.Wall(SimNow - f.T);
                    if (fa >= 0f && fa < 0.4f) _ground.Blob(P(f.X, f.Z, 0.04f), Vector3.right, Vector3.forward, 0.4f * (1f - fa / 0.4f), null, 12, 0f, A(PowerTimers.Colour[Mathf.Clamp(f.Kind, 0, 3)], 0.5f * (1f - fa / 0.4f)));
                    continue;
                }
                Color32 col = PowerTimers.Colour[Mathf.Clamp(kind, 0, 3)];
                float bob = still ? 0f : 0.08f * Mathf.Sin(wall * 3.1f + s * 1.3f);
                Vector3 c = P(x, z, 0.03f);
                if (kind == DepthRead.KindCoin)
                {
                    int coins = Mathf.Clamp(value / 2 + 1, 1, 4);
                    _ground.Blob(c, Vector3.right, Vector3.forward, 0.32f, null, 10, 0f, A(Gold, 0.25f));
                    for (int k = 0; k < coins; k++)
                    {
                        Quaternion q = Quaternion.Euler(0f, (still ? 0f : wall * 90f) + k * 40f, 0f);
                        _solid.Chunk(c + new Vector3(0.12f * (k - coins * 0.5f), 0.22f + bob + 0.04f * k, 0f), q, new Vector3(0.11f, 0.11f, 0.025f), 0.015f, Gold, Gold, GoldDark, Navy);
                    }
                    continue;
                }
                bool armed = w.Tick >= ready;
                if (!armed)
                {
                    // arming: a beam from the sky and a countdown ring until the ready tick
                    double spawn = Feed.SpawnedAt[s];
                    int span = Feed.SpawnedReady[s] > 0 && spawn > -998 ? Feed.SpawnedReady[s] - (int)Math.Round(spawn * w.Config.SimHz) : 150;
                    float frac = span > 0 ? Mathf.Clamp01((float)((ready - (w.Tick + alpha)) / span)) : 0f;
                    Countdown(c, 0.7f, 0.1f, frac, col, 0.9f);
                    _air.Add = 200;
                    _air.Quad(c + Vector3.up * 3f, right * 0.16f, Vector3.up * 3f, A(col, 0.7f), A(col, 0f));
                    _air.Add = 0;
                    continue;
                }
                _ground.Blob(c, Vector3.right, Vector3.forward, 0.55f, null, 14, 0f, A(col, 0.3f));
                GroundRing(c, 0.5f, 0.6f, 0f, Mathf.PI * 2f, 20, A(col, 0.85f));
                Quaternion spin = Quaternion.Euler(0f, still ? 30f : wall * 70f, 0f);
                Vector3 at = c + Vector3.up * (0.55f + bob);
                PowerIcon(at, spin, kind, col);
                if (end > 0 && end - w.Tick < 3 * w.Config.SimHz && !FeelGate.ReduceFlashing)
                    GroundRing(c, 0.62f, 0.68f, 0f, Mathf.PI * 2f, 20, A(Cream, 0.5f + 0.5f * Mathf.Sin(wall * 12f)));
            }
        }

        void PowerIcon(Vector3 at, Quaternion q, int kind, Color32 col)
        {
            Color32 dark = Mix(col, Navy, 0.45f), light = Mix(col, Cream, 0.4f);
            switch (kind)
            {
                case DepthRead.KindHaste: // a chevron of two slabs
                    _solid.Chunk(at + q * new Vector3(-0.08f, 0f, 0.08f), q * Quaternion.Euler(0f, 45f, 0f), new Vector3(0.2f, 0.06f, 0.05f), 0.02f, light, col, dark, Navy);
                    _solid.Chunk(at + q * new Vector3(-0.08f, 0f, -0.08f), q * Quaternion.Euler(0f, -45f, 0f), new Vector3(0.2f, 0.06f, 0.05f), 0.02f, light, col, dark, Navy);
                    break;
                case DepthRead.KindMight: // a fist block
                    _solid.Chunk(at, q * Quaternion.Euler(20f, 0f, 20f), new Vector3(0.17f, 0.17f, 0.17f), 0.025f, light, col, dark, Navy);
                    break;
                default: // Ward: a shield slab
                    _solid.Chunk(at, q, new Vector3(0.2f, 0.24f, 0.05f), 0.025f, light, col, dark, Navy);
                    _solid.Chunk(at + Vector3.up * -0.2f, q * Quaternion.Euler(0f, 0f, 45f), new Vector3(0.1f, 0.1f, 0.05f), 0.02f, light, col, dark, Navy);
                    break;
            }
        }

        void DrawAuras(World w, double alpha, float wall, bool still, Vector3 camFwd)
        {
            int n = w.ActorCount;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive) continue;
                int kind = DepthRead.PowerKind(w, id);
                if (!PowerTimers.Active(kind, w.Tick, DepthRead.PowerEndTick(w, id))) continue;
                Vector3 body = ActorPos(w, id, alpha, 0f);
                Color32 col = PowerTimers.Colour[kind];
                if (kind == DepthRead.KindHaste)
                {
                    // speed lines and a white trail behind the motion
                    double vx = w.X[id] - w.PrevX[id], vz = w.Z[id] - w.PrevZ[id];
                    Vector3 dir = SimSpace.ToWorld(vx, 0, vz);
                    if (dir.sqrMagnitude < 1e-6f) dir = SimSpace.ToWorld(Math.Cos(a.Facing), 0, Math.Sin(a.Facing));
                    dir.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    for (int j = -1; j <= 1; j++)
                    {
                        float phase = still ? 0.5f : Mathf.Repeat(wall * 3f + j * 0.33f, 1f);
                        Vector3 c = body - dir * (0.5f + 0.5f * phase) + side * (0.25f * j) + Vector3.up * (0.6f + 0.25f * j);
                        _air.Quad(c, dir * 0.3f, Vector3.up * 0.02f, A(col, 0f), A(col, 0.85f * (1f - phase)));
                    }
                    _ground.Quad(body - dir * 0.7f + Vector3.up * 0.04f, side * 0.18f, dir * 0.6f, A(col, 0f), A(col, 0.5f));
                }
                else if (kind == DepthRead.KindMight)
                {
                    Vector3 face = SimSpace.ToWorld(Math.Cos(a.Facing), 0, Math.Sin(a.Facing));
                    float beat = still || FeelGate.ReduceFlashing ? 0.8f : 0.7f + 0.3f * Mathf.Sin(wall * 6f);
                    _ground.Blob(body + Vector3.up * 0.04f, Vector3.right, Vector3.forward, 0.75f, null, 16, 0f, A(col, 0.22f * beat));
                    _air.Add = 255;
                    _air.Star(body + face * 0.55f + Vector3.up * 1.0f, Right(camFwd), Vector3.up, 0.22f * beat, 0.07f, 6, still ? 0f : wall * 3f, A(col, 0.9f));
                    _air.Add = 0;
                }
                else
                {
                    // Ward: a pale blue shell that thins as it soaks damage
                    float shell = PowerTimers.WardShell01(DepthRead.WardLeft(w, id), DepthRead.WardFull(w));
                    float thick = 0.04f + 0.12f * shell;
                    for (int band = 0; band < 3; band++)
                        _air.Ring(body + Vector3.up * (0.35f + band * 0.45f), Right(camFwd), Vector3.up, 0.72f - band * 0.08f, 0.72f - band * 0.08f + thick, 0f, Mathf.PI * 2f, 20, A(col, 0.35f + 0.4f * shell));
                }
            }
        }

        // ------------------------------------------------------------------ carried wood and water (spec 1: four cargo looks)

        void DrawCargo(World w, double alpha, float wall, bool still)
        {
            if (DepthEvents.CarryWood < 0 && DepthEvents.CarryWater < 0) return;
            int n = w.ActorCount;
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || a.CarryAmount <= 0) continue;
                int k = (int)a.Carry;
                bool wood = k == DepthEvents.CarryWood, water = k == DepthEvents.CarryWater;
                if (!wood && !water) continue;
                Vector3 at = ActorPos(w, id, alpha, 2.2f + (still ? 0f : 0.04f * Mathf.Sin(wall * 4f + id)));
                int stack = Mathf.Clamp(a.CarryAmount, 1, 3);
                for (int s = 0; s < stack; s++)
                {
                    if (wood) _solid.Chunk(at + Vector3.up * (s * 0.13f), Quaternion.Euler(0f, 25f + s * 30f, 0f), new Vector3(0.24f, 0.055f, 0.055f), 0.02f, BarkLight, Bark, BarkDark, Navy);
                    else _solid.Chunk(at + Vector3.up * (s * 0.16f), Quaternion.Euler(0f, 45f, 45f), new Vector3(0.08f, 0.08f, 0.08f), 0.02f, WaterLight, WaterC, WaterDark, Navy);
                }
            }
        }

        // ------------------------------------------------------------------ map events (U-068)

        void DrawEvents(World w, double alpha, float wall, bool still, bool steady, int me, TeamId viewer, Vector3 camFwd)
        {
            if (!DepthRead.EventsOn(w)) return;
            // supply drop
            int ds = DepthRead.DropState(w, out double dx, out double dz, out int dtick);
            if (ds != DepthRead.DropNone)
            {
                float r = (float)DepthRead.DropRange(w);
                Vector3 c = P(dx, dz, 0.04f);
                if (ds == DepthRead.DropIncoming)
                {
                    double warn = w.Config.SimHz * 8;
                    float fall = Mathf.Clamp01((float)(1.0 - (dtick - (w.Tick + alpha)) / Math.Max(1, warn)));
                    Countdown(c, r, 0.14f, 1f - fall, Brass, 0.9f);
                    _ground.Blob(c, Vector3.right, Vector3.forward, 0.45f + 0.5f * fall, null, 14, 0f, A(Navy, 0.25f + 0.3f * fall));
                    float hgt = Mathf.Lerp(14f, 0.5f, fall * fall);
                    CrateAt(c + Vector3.up * hgt, still ? 0f : wall * 40f);
                }
                else
                {
                    CrateAt(c + Vector3.up * 0.42f, 0f);
                    int contest = DepthRead.DropContest(w);
                    float pa = DepthRead.DropProgress01(w, 0), pc = DepthRead.DropProgress01(w, 1);
                    GroundRing(c, r - 0.08f, r, 0f, Mathf.PI * 2f, 40, A(Cream, 0.7f));
                    if (contest == DepthRead.ContestBoth)
                    {
                        float beat = steady ? 0.7f : 0.45f + 0.45f * Mathf.Abs(Mathf.Sin(wall * 5f));
                        GroundRing(c, r - 0.3f, r - 0.1f, 0f, Mathf.PI * 2f, 40, A(Cream, beat));
                    }
                    else
                    {
                        if (pa > 0.001f) GroundRing(c, r - 0.32f, r - 0.1f, Mathf.PI * 0.5f - Mathf.PI * 2f * pa, Mathf.PI * 0.5f, Mathf.Max(3, (int)(40 * pa)), A(CMTokens.Azure.Color, 0.95f));
                        if (pc > 0.001f) GroundRing(c, r - 0.56f, r - 0.34f, Mathf.PI * 0.5f - Mathf.PI * 2f * pc, Mathf.PI * 0.5f, Mathf.Max(3, (int)(40 * pc)), A(CMTokens.Crimson.Color, 0.95f));
                    }
                }
            }
            // the egg exposed: an open seam glow and a turning ring at the shrine
            int until = DepthRead.EggExposedUntil(w);
            if (until > w.Tick && w.Map.Shrine != null)
            {
                Vector3 c = P(w.Map.Shrine.X, w.Map.Shrine.Z, 0.05f);
                float left = Mathf.Clamp01((float)(until - w.Tick) / Math.Max(1, DepthRead.EggWindowTicks(w)));
                Countdown(c, 3.2f, 0.16f, left, Seam, 0.9f);
                _air.Add = 220;
                _air.Quad(c + Vector3.up * 2.4f, Right(camFwd) * 0.9f, Vector3.up * 2.4f, A(Seam, steady ? 0.45f : 0.35f + 0.2f * Mathf.Sin(wall * 3f)), A(Seam, 0f));
                _air.Add = 0;
            }
            // the bridge: a red hatch over exactly the falling or fallen cells, dust and shake while warned
            int bs = DepthRead.BridgeState(w, out int btick, out int bn);
            if (bs != DepthRead.BridgeNone && bn > 0)
            {
                int width = w.Map.Width;
                for (int k = 0; k < bn; k++)
                {
                    int cell = DepthRead.BrokenCell(w, k);
                    if (cell < 0 || width <= 0) continue;
                    double cx = cell % width + 0.5, cz = cell / width + 0.5;
                    if (bs == DepthRead.BridgeWarned)
                    {
                        float shake = still ? 0f : 0.04f * Mathf.Sin(wall * 37f + k);
                        Vector3 c = P(cx, cz, 0.06f) + new Vector3(shake, 0f, 0f);
                        _ground.Quad(c, new Vector3(0.45f, 0f, 0f), new Vector3(0f, 0f, 0.45f), A(Hatch, 0.28f), A(Hatch, 0.28f));
                        _ground.Quad(c, new Vector3(0.45f, 0f, 0.45f) * 0.7f, new Vector3(-0.06f, 0f, 0.06f), A(Hatch, 0.9f), A(Hatch, 0.9f));
                        if (!still && k % 3 == 0) _air.Quad(c + Vector3.up * (0.2f + 0.3f * Mathf.Repeat(wall * 0.8f + k * 0.17f, 1f)), Right(camFwd) * 0.12f, Vector3.up * 0.12f, A(Stone, 0.5f), A(Stone, 0.2f));
                    }
                    else _ground.Quad(P(cx, cz, 0.02f), new Vector3(0.42f, 0f, 0f), new Vector3(0f, 0f, 0.42f), A(Hatch, 0.16f), A(Hatch, 0.16f));
                }
                if (bs == DepthRead.BridgeBroken && DepthRead.BridgeEnds(w, out double ax, out double az, out double bx, out double bz))
                {
                    bool builder = me >= 0 && me < w.ActorCount && w.Actors[me].Cls == ClassId.Builder;
                    float prog = DepthRead.RepairProgress01(w);
                    Color32 col = builder ? Brass : A(Brass, 0.5f);
                    float rr = (float)DepthRead.RepairRange(w);
                    Countdown(P(ax, az, 0.05f), rr, 0.1f, prog, col, builder ? 0.95f : 0.5f);
                    Countdown(P(bx, bz, 0.05f), rr, 0.1f, prog, col, builder ? 0.95f : 0.5f);
                }
            }
        }

        void CrateAt(Vector3 at, float spinDeg)
        {
            Quaternion q = Quaternion.Euler(0f, spinDeg, 0f);
            _solid.Chunk(at, q, new Vector3(0.42f, 0.42f, 0.42f), 0.035f, Crate, Mix(Crate, CrateDark, 0.4f), CrateDark, Navy);
            _solid.Chunk(at + Vector3.up * 0.43f, q, new Vector3(0.44f, 0.04f, 0.1f), 0.02f, Brass, Brass, BrassDark, Navy);
            _solid.Chunk(at + Vector3.up * 0.43f, q, new Vector3(0.1f, 0.04f, 0.44f), 0.02f, Brass, Brass, BrassDark, Navy);
        }

        // ------------------------------------------------------------------ feed-driven moments

        void DrawFeed(World w, double alpha, float wall, Vector3 camFwd, TeamId viewer)
        {
            DepthFeed f = Feed;
            double now = SimNow;
            int n = w.ActorCount;
            Vector3 right = Right(camFwd);
            // arcs: hand-off (0.45 s), bank and withdraw flights (0.6 s)
            for (int i = 0; i < DepthFeed.Arcs; i++)
            {
                DepthFeed.Arc a = f.ArcRing[i];
                float life = a.Kind == DepthFeed.ArcHandoff ? 0.45f : 0.6f;
                float age = (float)f.Wall(now - a.T);
                if (age < 0f || age > life) continue;
                float t = age / life;
                Vector3 p0 = a.From >= 0 && a.From < n ? ActorPos(w, a.From, alpha, 1.6f) : P(a.FromX, a.FromZ, 0.8f);
                Vector3 p1 = a.To >= 0 && a.To < n ? ActorPos(w, a.To, alpha, 1.6f) : P(a.ToX, a.ToZ, 0.8f);
                Vector3 at = Vector3.Lerp(p0, p1, t) + Vector3.up * (4f * 1.2f * t * (1f - t));
                Color32 col = a.Cargo == DepthEvents.BioWater && a.Cargo >= 0 ? WaterC : a.Cargo == DepthEvents.BioWood && a.Cargo >= 0 ? Bark : CMTokens.Biomass.Color;
                _solid.Chunk(at, Quaternion.Euler(0f, t * 360f, 45f), new Vector3(0.12f, 0.12f, 0.12f), 0.02f, Mix(col, Cream, 0.3f), col, Mix(col, Navy, 0.4f), Navy);
            }
            // pops
            for (int i = 0; i < DepthFeed.Pops; i++)
            {
                DepthFeed.Pop p = f.PopRing[i];
                float age = (float)f.Wall(now - p.T);
                if (age < 0f || age > 0.9f) continue;
                Vector3 c = P(p.X, p.Z, 0.06f);
                float R = (float)p.R;
                switch (p.Kind)
                {
                    case DepthFeed.PopChop:
                        if (age > 0.35f) break;
                        _air.Star(c + Vector3.up * (0.9f + age), right, Vector3.up, 0.3f * (1f - age / 0.35f), 0.08f, 5, age * 6f, A(BarkLight, 0.95f));
                        break;
                    case DepthFeed.PopSplash:
                    case DepthFeed.PopCool:
                        {
                            float life = p.Kind == DepthFeed.PopCool ? 0.6f : 0.4f;
                            if (age > life) break;
                            float t = age / life;
                            GroundRing(c, R * t, R * t + 0.14f, 0f, Mathf.PI * 2f, 24, A(p.Kind == DepthFeed.PopCool ? WaterLight : WaterC, 0.9f * (1f - t)));
                            break;
                        }
                    case DepthFeed.PopCoins:
                        if (age > 0.3f) break;
                        _air.Add = 255;
                        _air.Star(c + Vector3.up * 0.4f, right, Vector3.up, 0.45f * (age / 0.3f), 0.1f, 6, 0f, A(Gold, 1f - age / 0.3f));
                        _air.Add = 0;
                        break;
                    case DepthFeed.PopTaken:
                    case DepthFeed.PopAuraEnd:
                        {
                            if (age > 0.4f) break;
                            int kind = p.Kind == DepthFeed.PopTaken ? p.Value : p.Value / 8;
                            float t = age / 0.4f;
                            GroundRing(c, R * t, R * t + 0.12f, 0f, Mathf.PI * 2f, 24, A(PowerTimers.Colour[Mathf.Clamp(kind, 0, 3)], 0.9f * (1f - t)));
                            break;
                        }
                    case DepthFeed.PopWard:
                        {
                            if (age > 0.3f || p.Actor < 0 || p.Actor >= n) break;
                            float t = age / 0.3f;
                            _air.Ring(ActorPos(w, p.Actor, alpha, 0.9f), right, Vector3.up, 0.5f + 0.4f * t, 0.6f + 0.4f * t, 0f, Mathf.PI * 2f, 20, A(PowerTimers.Colour[3], 0.9f * (1f - t)));
                            break;
                        }
                    case DepthFeed.PopShatter:
                        if (age > 0.45f) break;
                        _air.Add = 200;
                        _air.Star(c + Vector3.up * 0.9f, right, Vector3.up, 0.5f + 0.6f * age, 0.15f, 7, age * 4f, A(PowerTimers.Colour[3], 1f - age / 0.45f));
                        _air.Add = 0;
                        break;
                    case DepthFeed.PopSwitch:
                        if (age > 0.4f) break;
                        _air.Add = 220;
                        _air.Quad(c + Vector3.up * 1.2f, right * (0.5f * (1f - age / 0.4f) + 0.1f), Vector3.up * 1.2f, A(Cream, 0.9f * (1f - age / 0.4f)), A(Cream, 0f));
                        _air.Add = 0;
                        break;
                    case DepthFeed.PopDust:
                        if (age > 0.6f) break;
                        GroundRing(c, R * age / 0.6f, R * age / 0.6f + 0.3f, 0f, Mathf.PI * 2f, 28, A(Stone, 0.6f * (1f - age / 0.6f)));
                        break;
                    case DepthFeed.PopClaim:
                        if (age > 0.5f) break;
                        _air.Add = 255;
                        _air.Star(c + Vector3.up * 0.8f, right, Vector3.up, 1.2f * (age / 0.5f), 0.25f, 8, 0f, A(Gold, 1f - age / 0.5f));
                        _air.Add = 0;
                        break;
                    case DepthFeed.PopRepair:
                        if (age > 0.5f) break;
                        GroundRing(c, R * (age / 0.5f), R * (age / 0.5f) + 0.15f, 0f, Mathf.PI * 2f, 28, A(Brass, 0.9f * (1f - age / 0.5f)));
                        break;
                    case DepthFeed.PopPlanks:
                        {
                            if (age > 0.8f) break;
                            float drop = 4.9f * age * age * 4f;
                            for (int k = 0; k < 5; k++)
                            {
                                Vector3 o = new Vector3((k - 2) * R * 0.3f, 0.2f - drop, (k % 2) * 0.3f);
                                _solid.Chunk(c + o, Quaternion.Euler(age * 200f * (k - 2), 20f * k, age * 90f), new Vector3(0.3f, 0.04f, 0.1f), 0.02f, BarkLight, Bark, BarkDark, Navy);
                            }
                            break;
                        }
                }
            }
            // upgrade pulses: a ring under every member of the upgraded class
            for (int id = 0; id < n; id++)
            {
                Actor a = w.Actors[id];
                if (!a.Alive || !f.Pulsing((int)a.Team, (int)a.Cls, now, out float age)) continue;
                float t = age / 0.8f;
                GroundRing(ActorPos(w, id, alpha, 0.05f), 0.4f + 0.7f * t, 0.52f + 0.7f * t, 0f, Mathf.PI * 2f, 24, A(CMTokens.Team(a.Team), 0.95f * (1f - t)));
            }
            // bot acknowledgements: a line from the bot to the ping for 1 s
            for (int i = 0; i < DepthFeed.Acks; i++)
            {
                DepthFeed.Ack k = f.AckRing[i];
                float age = (float)f.Wall(now - k.T);
                if (age < 0f || age > 1f || k.Bot < 0 || k.Bot >= n) continue;
                Vector3 a0 = ActorPos(w, k.Bot, alpha, 0.1f), a1 = P(k.X, k.Z, 0.1f);
                Vector3 mid = (a0 + a1) * 0.5f, half = (a1 - a0) * 0.5f;
                Vector3 side = Vector3.Cross(Vector3.up, half);
                if (side.sqrMagnitude < 1e-6f) continue;
                Color col = Hud.Interact.PingWords.Colour((Hud.Interact.PingKind)Mathf.Clamp(k.Call, 0, 3));
                _ground.Quad(mid, half, side.normalized * 0.05f, A(col, 0.85f * (1f - age)), A(col, 0.85f * (1f - age)));
            }
        }
    }
}
