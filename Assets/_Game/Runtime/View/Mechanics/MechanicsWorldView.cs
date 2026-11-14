using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// The world side of the new classes and the physics lane, drawn from sim state and the MechFeed every frame into three
    /// fixed-budget meshes (the CombatFx pattern: ground decals, solid toon chunks, air billboards; three draw calls):
    /// ladders and rope bridges with a countdown ring and the 3 s warning blink, TNT in flight with its arc shadow and a
    /// landing marker, lying TNT with its fuse ring, mines (mound for everyone, plate and dashed ring for the owner's team),
    /// heal beams, pulse rings, heal sparkles, the Healer's purify column at the egg, wind-up glints, knockback streaks,
    /// the slam's falloff and safe edge, and the dodge pop. Craters are the chunk mesh's (Terraform event, GridVersion).
    /// Reads the sim only; installs itself on the driver (MechanicsWorldViewInstaller), so no shared file changes.
    /// </summary>
    public sealed class MechanicsWorldView : MonoBehaviour
    {
        public const int GroundVerts = 14000, SolidVerts = 26000, AirVerts = 6000;

        /// <summary>The running view (null outside a match). The HUD reads its feed.</summary>
        public static MechanicsWorldView Active { get; private set; }
        public static event Action<MechanicsWorldView> Installed;

        public readonly MechFeed Feed = new MechFeed();
        public World World => _world;
        public double SimNow { get; private set; }
        public double Pace => Feed.Pace;

        SimDriverBehaviour _driver;
        World _world;
        FxMeshBuilder _ground, _solid, _air;
        Material _groundMat, _solidMat, _airMat;
        GameObject _groundGo, _solidGo, _airGo;
        StagedBuildView _staged;

        static readonly Color32 Navy = new Color32(24, 35, 59, 255);
        static readonly Color32 WoodLight = new Color32(214, 168, 108, 255), WoodMid = new Color32(170, 122, 70, 255), WoodDark = new Color32(112, 78, 44, 255);
        static readonly Color32 Dirt = new Color32(107, 79, 53, 255), DirtLight = new Color32(140, 106, 72, 255), DirtDark = new Color32(76, 55, 37, 255);
        static readonly Color32 Brass = CMTokens.ToolBrass.Color, BrassDark = new Color32(150, 112, 40, 255);
        static readonly Color32 TntRed = new Color32(200, 53, 46, 255), TntDark = new Color32(132, 30, 28, 255), TntLight = new Color32(232, 96, 80, 255);
        static readonly Color32 Danger = CMTokens.MeterOrange.Color, Cream = CMTokens.WarmCream.Color, Spark = new Color32(255, 214, 72, 255);
        static readonly Color32 Heal = CMTokens.Biomass.Color, Seam = CMTokens.EggSeam.Color, Hurt = CMTokens.Crimson.Color;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) { enabled = false; return; }
            _ground = new FxMeshBuilder("Mechanics Ground", GroundVerts, GroundVerts * 3);
            _solid = new FxMeshBuilder("Mechanics Solid", SolidVerts, SolidVerts * 2);
            _air = new FxMeshBuilder("Mechanics Air", AirVerts, AirVerts * 3);
            Shader sh = UnityEngine.Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (sh == null) { enabled = false; return; }
            _groundMat = Mat(sh, "Mechanics Ground", 0, 0, -2, 2985);
            _solidMat = Mat(sh, "Mechanics Solid", 2, 1, 0, 2455);
            _airMat = Mat(sh, "Mechanics Air", 0, 0, 0, 3095);
            _groundGo = Layer("Mechanics Ground", _ground, _groundMat);
            _solidGo = Layer("Mechanics Solid", _solid, _solidMat);
            _airGo = Layer("Mechanics Air", _air, _airMat);
            Bind(_driver.World);
            _driver.Ticked -= OnTicked;
            _driver.Ticked += OnTicked;
            Active = this;
            Installed?.Invoke(this);
        }

        void Bind(World w)
        {
            _world = w;
            Feed.Reset();
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
            if (w != _world) Bind(w);
            Feed.Pace = _driver != null && _driver.TimeScale > 0 ? _driver.TimeScale : 2;
            Feed.Read(w);
        }

        void LateUpdate() { var c = CriticalMass.Runtime.Game.FrameCost.Enter(); LateUpdateBody(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.MechanicsView, c); }

        void LateUpdateBody()
        {
            if (_world == null || _driver == null) return;
            if (_driver.World != _world) Bind(_driver.World);
            World w = _world;
            Feed.Pace = _driver.TimeScale > 0 ? _driver.TimeScale : 2;
            double alpha = _driver.Alpha;
            SimNow = MechFeed.Now(w, alpha);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.down;
            _ground.Clear();
            _solid.Clear();
            _air.Clear();
            int me = Feed.LocalActor;
            TeamId viewer = me >= 0 && me < w.ActorCount ? w.Actors[me].Team : TeamId.Azure;
            bool reduceFlash = FeelGate.ReduceFlashing;
            if (_staged == null) _staged = GetComponent<StagedBuildView>();
            DrawPieces(w, alpha, reduceFlash);
            DrawBuildExtras(camFwd);
            DrawMines(w, viewer, reduceFlash);
            DrawBombs(w, alpha);
            DrawSlam(w, alpha, me, reduceFlash);
            DrawFeed(w, alpha, camFwd);
            _ground.Upload();
            _solid.Upload();
            _air.Upload();
        }

        // ------------------------------------------------------------------ helpers

        static Color32 A(Color32 c, float a)
        {
            c.a = (byte)Mathf.Clamp(a * 255f, 0, 255);
            return c;
        }

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

        /// <summary>A countdown ring: a navy track and a coloured arc clockwise from 12 o'clock for frac of a turn.</summary>
        void Countdown(Vector3 c, float r, float w, float frac, Color32 col, float alpha)
        {
            const float top = Mathf.PI * 0.5f;
            GroundRing(c, r - w - 0.03f, r + 0.03f, 0f, Mathf.PI * 2f, 28, A(Navy, 0.7f * alpha));
            if (frac > 0.001f) GroundRing(c, r - w, r, top - Mathf.PI * 2f * frac, top, Mathf.Max(3, (int)(28 * frac)), A(col, alpha));
        }

        void Dashed(Vector3 c, float r0, float r1, int dashes, float fill, float rot, Color32 col)
        {
            float step = Mathf.PI * 2f / dashes;
            for (int k = 0; k < dashes; k++)
            {
                float a0 = rot + k * step;
                GroundRing(c, r0, r1, a0, a0 + step * fill, 4, col);
            }
        }

        // ------------------------------------------------------------------ ladders and rope bridges (DG-24)

        void DrawPieces(World w, double alpha, bool reduceFlash)
        {
            BuiltPiece[] ps = w.Builder != null ? w.Builder.Pieces : null;
            if (ps == null) return;
            BuilderPiecesConfig pc = w.Config.Abilities.BuilderPieces;
            double hz = w.Config.SimHz;
            for (int k = 0; k < ps.Length; k++)
            {
                BuiltPiece p = ps[k];
                if (!p.Active) continue;
                float left = PieceTimer.Left01(w.Tick, alpha, p.PlacedTick, p.ExpireTick);
                bool warn = PieceTimer.Warning(w.Tick, p.ExpireTick, pc.WarnTicks);
                double wallLeft = Feed.Wall((p.ExpireTick - w.Tick - alpha) / hz);
                double wallInto = Feed.Wall((w.Tick + alpha - (p.ExpireTick - pc.WarnTicks)) / hz);
                float blink = warn ? PieceTimer.Blink(wallInto, wallLeft, reduceFlash) : 1f;
                Color32 team = CMTokens.Team(p.Team);
                Color32 ringCol = warn ? Danger : team;
                if (p.Carried)
                {
                    if (p.Carrier < 0 || p.Carrier >= w.ActorCount || !w.Actors[p.Carrier].Alive) continue;
                    Vector3 at = ActorPos(w, p.Carrier, alpha, 0f);
                    // feat/staged-building-1: the authored carried ladder or bridge when the art is there
                    if (_staged != null && _staged.DrawCarried(p, at, (float)w.Actors[p.Carrier].Facing, warn && blink < 0.5f && !reduceFlash))
                    {
                        Countdown(at + Vector3.up * 0.05f, 0.95f, 0.08f, left, ringCol, blink);
                        continue;
                    }
                    // a lashed bundle of planks over the carrier's head, and the life ring at the feet
                    Quaternion rot = Quaternion.Euler(0f, 30f, 0f);
                    for (int b = 0; b < 3; b++)
                        _solid.Chunk(at + new Vector3(0f, 2.05f + b * 0.13f, 0f), rot, new Vector3(0.5f, 0.05f, 0.11f), 0.03f,
                            Warm(WoodLight, warn, blink), Warm(WoodMid, warn, blink), Warm(WoodDark, warn, blink), Navy);
                    _solid.Chunk(at + new Vector3(0f, 2.18f, 0f), rot, new Vector3(0.06f, 0.2f, 0.13f), 0.02f, team, team, Mix(team, Navy, 0.4f), Navy);
                    Countdown(at + Vector3.up * 0.05f, 0.95f, 0.08f, left, ringCol, blink);
                    continue;
                }
                double ax = p.Dx, az = p.Dz;
                Vector3 along = SimSpace.ToWorld(ax, 0, az);
                if (along.sqrMagnitude < 1e-6f) along = Vector3.right;
                along.Normalize();
                Vector3 across = Vector3.Cross(Vector3.up, along);
                Vector3 c = P(p.X, p.Z, 0.02f);
                float len = Mathf.Max(0.5f, (float)p.HalfLen * 2f);
                float half = (float)(pc.WidthCells * 0.5) - 0.12f;
                // feat/staged-building-1: the authored ladder or rope bridge model, with its construction sequence; the
                // procedural planks below stay as the fallback when the art is missing.
                if (_staged != null && _staged.DrawPiece(w, k, p, c, along, len, (float)pc.WidthCells, warn && blink < 0.5f && !reduceFlash,
                        out float buildProgress, out bool building))
                {
                    if (!building) Countdown(c + Vector3.up * 0.75f, 0.42f, 0.08f, left, ringCol, blink);
                    else if (WorldFeedbackSettings.Progress) Countdown(c + Vector3.up * 0.75f, 0.42f, 0.05f, buildProgress, team, 1f);
                    continue;
                }
                double popWall = Feed.Wall((w.Tick + alpha - p.PlacedTick) / hz);
                float pop = popWall < 0.18 ? Mathf.Lerp(0.6f, 1f, (float)(popWall / 0.18)) : 1f;
                Quaternion q = Quaternion.LookRotation(along, Vector3.up);
                Color32 l = Warm(WoodLight, warn, blink), m = Warm(WoodMid, warn, blink), d = Warm(WoodDark, warn, blink);
                if (p.Kind == BuiltPieces.Bridge)
                {
                    int n = Mathf.Clamp(Mathf.RoundToInt(len / 0.55f), 2, 16);
                    for (int i = 0; i < n; i++)
                    {
                        float t = -len * 0.5f + (i + 0.5f) * len / n;
                        float sag = -0.12f * (1f - Mathf.Pow(2f * t / len, 2f));
                        _solid.Chunk(c + along * t + Vector3.up * (0.06f + sag), q, new Vector3(half * pop, 0.045f, 0.19f), 0.025f, l, m, d, Navy);
                    }
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 side = across * (half + 0.08f) * s;
                        _solid.Chunk(c + side + Vector3.up * 0.55f, q, new Vector3(0.035f, 0.035f, len * 0.5f * pop), 0.02f, team, team, Mix(team, Navy, 0.4f), Navy);
                        for (int e = -1; e <= 1; e += 2)
                            _solid.Chunk(c + side + along * (len * 0.5f * e) + Vector3.up * 0.32f, q, new Vector3(0.07f, 0.32f, 0.07f), 0.025f, l, m, d, Navy);
                    }
                }
                else
                {
                    // ladder: two rails and rungs every 0.4 u, raised over the span (it climbs the wall it crosses)
                    float lift = 0.35f;
                    int n = Mathf.Clamp(Mathf.RoundToInt(len / 0.4f), 2, 16);
                    for (int s = -1; s <= 1; s += 2)
                        _solid.Chunk(c + across * (half * 0.85f) * s + Vector3.up * lift, q, new Vector3(0.07f, 0.07f, len * 0.5f * pop), 0.025f, l, m, d, Navy);
                    for (int i = 0; i < n; i++)
                    {
                        float t = -len * 0.5f + (i + 0.5f) * len / n;
                        _solid.Chunk(c + along * t + Vector3.up * lift, q, new Vector3(half * 0.85f * pop, 0.04f, 0.05f), 0.02f, l, m, d, Navy);
                    }
                    _solid.Chunk(c + Vector3.up * (lift + 0.09f), q, new Vector3(0.1f, 0.03f, 0.1f), 0.02f, team, team, Mix(team, Navy, 0.4f), Navy);
                }
                Countdown(c + Vector3.up * 0.75f, 0.42f, 0.08f, left, ringCol, blink);
            }
        }

        /// <summary>
        /// feat/staged-building-1: the construction progress ring at a barricade being assembled (the HUD ring style: navy
        /// track, team arc filling from 12 o'clock) and the dust puffs the staged build drops where pieces land.
        /// </summary>
        void DrawBuildExtras(Vector3 camFwd)
        {
            StagedBuildView sv = _staged;
            if (sv == null) return;
            if (WorldFeedbackSettings.Progress)
                for (int i = 0; i < sv.Tracker.Barricades.Length; i++)
                    if (sv.BarricadeProgress(i, out float prog, out Vector3 simAt, out TeamId team))
                        Countdown(P(simAt.x, simAt.z, 0.03f), 0.62f, 0.05f, prog, CMTokens.Team(team), 1f);
            Vector3 right = Vector3.Cross(Vector3.up, camFwd);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(camFwd, right).normalized;
            bool particles = WorldFeedbackSettings.Particles;
            for (int i = 0; i < StagedBuildView.PuffCount; i++)
            {
                float age = sv.PuffAge(i);
                if (age < 0f || age > StagedBuildView.PuffLife) continue;
                float t = age / StagedBuildView.PuffLife, k = 1f - t;
                StagedBuildView.Puff pf = sv.Puffs[i];
                float r = pf.R * (0.5f + 0.8f * FeedEase(t));
                _ground.Blob(pf.P + Vector3.up * 0.03f, Vector3.right, Vector3.forward, r, null, 14, 0f, A(DirtLight, 0.35f * k));
                if (!particles) continue;
                for (int j = 0; j < 3; j++)
                {
                    float ang = j * 2.094f + i;
                    Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (r * 0.7f) + Vector3.up * (0.12f + 0.35f * t);
                    float sz = pf.R * 0.35f * (0.6f + 0.6f * t);
                    _air.Quad(pf.P + off, right * sz, up * sz, A(Cream, 0.55f * k), A(Cream, 0.55f * k));
                }
            }
        }

        /// <summary>Wood colour in the warning: alternates toward a burnt tone on the off beat (steady tint with reduce flashing).</summary>
        static Color32 Warm(Color32 wood, bool warn, float blink)
        {
            if (!warn) return wood;
            Color32 burnt = new Color32(150, 70, 50, 255);
            return Mix(wood, burnt, blink < 1f ? 0.75f : 0.3f);
        }

        // ------------------------------------------------------------------ mines (DG-22)

        void DrawMines(World w, TeamId viewer, bool reduceFlash)
        {
            Mine[] ms = w.Demolitions != null ? w.Demolitions.Mines : null;
            if (ms == null) return;
            float radius = (float)w.Config.Abilities.DemolitionsMine.BlastRadius;
            double hz = w.Config.SimHz;
            for (int k = 0; k < ms.Length; k++)
            {
                Mine mi = ms[k];
                if (!mi.Active) continue;
                double x = mi.Cx + 0.5, z = mi.Cz + 0.5;
                Vector3 c = P(x, z, 0.02f);
                float moundA = MineSight.MoundAlpha(mi.Team, viewer);
                _ground.Blob(c, Vector3.right, Vector3.forward, 0.44f, null, 12, 0f, A(DirtDark, 0.55f * moundA));
                Color32 dl = Mix(DirtLight, Navy, 1f - moundA), dm = Mix(Dirt, Navy, 1f - moundA);
                _solid.Chunk(c + Vector3.up * 0.05f, Quaternion.Euler(0f, 45f, 0f), new Vector3(0.26f, 0.06f, 0.26f), 0.02f, dl, dm, DirtDark, Navy);
                _solid.Chunk(c + Vector3.up * 0.1f, Quaternion.Euler(0f, 12f, 0f), new Vector3(0.15f, 0.05f, 0.15f), 0.02f, dl, dm, DirtDark, Navy);
                if (mi.Team != viewer) continue;
                if (MineSight.ShowPlate(mi.Team, viewer, mi.Armed))
                    _solid.Chunk(c + Vector3.up * 0.16f, Quaternion.identity, new Vector3(0.12f, 0.02f, 0.12f), 0.015f, Brass, Brass, BrassDark, Navy);
                Color32 team = CMTokens.Team(mi.Team);
                if (MineSight.ShowRing(mi.Team, viewer, mi.Armed))
                {
                    float pulse = reduceFlash ? 0.75f : 0.6f + 0.25f * Mathf.Sin((float)(Feed.Wall(SimNow) * Mathf.PI * 2f * 1.2f));
                    Dashed(c, radius - 0.05f, radius, 8, 0.6f, (float)(Feed.Wall(SimNow) * 0.4), A(team, pulse));
                }
                else
                {
                    // arming: the ring fills from 12 o'clock while the fuse is set
                    double span = mi.ArmTick - mi.PlacedTick;
                    float f = span > 0 ? Mathf.Clamp01((float)((w.Tick - mi.PlacedTick) / span)) : 1f;
                    Countdown(c, radius, 0.05f, f, team, 0.6f);
                }
            }
        }

        // ------------------------------------------------------------------ TNT (DG-22)

        void DrawBombs(World w, double alpha)
        {
            Bomb[] bs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            if (bs == null) return;
            float radius = (float)w.Config.Abilities.DemolitionsTnt.BlastRadius;
            double nowTicks = w.Tick + alpha;
            float wallNow = (float)Feed.Wall(SimNow);
            for (int k = 0; k < bs.Length; k++)
            {
                Bomb b = bs[k];
                if (!b.Active) continue;
                Vector3 land = P(b.X, b.Z, 0.03f);
                if (!b.Landed)
                {
                    float p = TntArc.Progress(nowTicks, b.ThrownTick, b.LandTick);
                    double gx = b.FromX + (b.X - b.FromX) * p, gz = b.FromZ + (b.Z - b.FromZ) * p;
                    float dist = (float)Math.Sqrt((b.X - b.FromX) * (b.X - b.FromX) + (b.Z - b.FromZ) * (b.Z - b.FromZ));
                    float h = TntArc.Height(p, dist);
                    Vector3 g = P(gx, gz, 0.03f);
                    float sh = TntArc.ShadowScale(h);
                    _ground.Blob(g, Vector3.right, Vector3.forward, 0.32f * sh, null, 10, 0f, A(Navy, 0.45f * sh));
                    Vector3 dir = SimSpace.ToWorld(b.X - b.FromX, 0, b.Z - b.FromZ);
                    Vector3 axis = Vector3.Cross(Vector3.up, dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward);
                    Quaternion spin = Quaternion.AngleAxis(p * 540f, axis) * Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward);
                    Vector3 at = g + Vector3.up * (h + 0.3f);
                    Stick(at, spin);
                    _air.Add = 255;
                    _air.Star(at + spin * new Vector3(0f, 0f, 0.26f), Vector3.right, Vector3.up, 0.12f, 0.05f, 5, wallNow * 9f, A(Spark, 0.9f));
                    _air.Add = 0;
                    // landing marker: the blast circle, filling as the stick comes down, and a centre cross
                    GroundRing(land, radius - 0.07f, radius, 0f, Mathf.PI * 2f, 32, A(Danger, 0.5f + 0.4f * p));
                    _ground.Blob(land, Vector3.right, Vector3.forward, radius * p, null, 24, 0f, A(Danger, 0.12f));
                    _ground.Quad(land, new Vector3(0.22f, 0f, 0f), new Vector3(0f, 0f, 0.04f), A(Danger, 0.9f), A(Danger, 0.9f));
                    _ground.Quad(land, new Vector3(0.04f, 0f, 0f), new Vector3(0f, 0f, 0.22f), A(Danger, 0.9f), A(Danger, 0.9f));
                }
                else
                {
                    float fuse = TntArc.FuseLeft01(nowTicks, b.LandTick, b.BlastTick);
                    Stick(land + Vector3.up * 0.1f, Quaternion.Euler(90f, 20f + k * 37f, 0f));
                    float flick = 0.7f + 0.3f * Mathf.Sin(wallNow * 47f + k);
                    _air.Add = 255;
                    _air.Star(land + new Vector3(0f, 0.32f, 0f), Vector3.right, Vector3.forward, 0.16f * flick, 0.06f, 6, wallNow * 12f, A(Spark, 0.95f));
                    _air.Add = 0;
                    Color32 col = Mix(Danger, Hurt, 1f - fuse);
                    Countdown(land, radius, 0.09f, fuse, col, 0.9f);
                    _ground.Blob(land, Vector3.right, Vector3.forward, radius, null, 28, 0f, A(Hurt, 0.08f + 0.18f * (1f - fuse)));
                }
            }
        }

        void Stick(Vector3 at, Quaternion rot)
        {
            _solid.Chunk(at, rot, new Vector3(0.09f, 0.09f, 0.22f), 0.025f, TntLight, TntRed, TntDark, Navy);
            _solid.Chunk(at, rot, new Vector3(0.1f, 0.1f, 0.03f), 0.015f, Cream, Cream, Mix(Cream, Navy, 0.3f), Navy);
        }

        // ------------------------------------------------------------------ the slam's falloff and safe edge (DG-85)

        void DrawSlam(World w, double alpha, int me, bool reduceFlash)
        {
            Kaiju k = w.Kaiju;
            if (k == null || k.State == KaijuState.Egg || k.TelegraphIn <= 0) return;
            double r = w.Config.Kaiju.SlamRadius;
            Vector3 c = P(k.TelegraphX, k.TelegraphZ, 0.04f);
            // falloff: five bands, strongest at the centre (DG-85: 45 at the centre, at most 22 at the edge)
            const int bands = 5;
            for (int i = 0; i < bands; i++)
            {
                float r0 = (float)(r * i / bands), r1 = (float)(r * (i + 1) / bands);
                float f = SlamRead.Falloff((r0 + r1) * 0.5, r);
                GroundRing(c, r0, r1, 0f, Mathf.PI * 2f, 36, A(Hurt, 0.06f + 0.16f * (f - 0.5f) * 2f));
            }
            // the safe edge: a bright cream line just outside the radius, slowly turning
            float wall = (float)Feed.Wall(SimNow);
            Dashed(c, (float)r + 0.06f, (float)r + 0.2f, 18, 0.62f, wall * 0.5f, A(Cream, 0.9f));
            if (me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return;
            double px = w.PrevX[me] + (w.X[me] - w.PrevX[me]) * alpha, pz = w.PrevZ[me] + (w.Z[me] - w.PrevZ[me]) * alpha;
            if (!SlamRead.Inside(px, pz, k.TelegraphX, k.TelegraphZ, r)) return;
            // inside: an arrow from the player to the nearest safe edge point
            double dx = px - k.TelegraphX, dz = pz - k.TelegraphZ, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-4) { dx = 1; dz = 0; d = 1; }
            Vector3 dir = SimSpace.ToWorld(dx / d, 0, dz / d);
            Vector3 from = P(px, pz, 0.05f);
            float to = (float)SlamRead.ToSafeEdge(px, pz, k.TelegraphX, k.TelegraphZ, r);
            float beat = reduceFlash ? 1f : 0.75f + 0.25f * Mathf.Sin(wall * Mathf.PI * 2f * 2f);
            Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.07f;
            Vector3 mid = from + dir * (0.5f + to * 0.5f);
            _ground.Quad(mid, side, dir * Mathf.Max(0.05f, to * 0.5f), A(Cream, 0.2f * beat), A(Cream, 0.85f * beat));
            Vector3 tip = from + dir * (0.5f + to + 0.2f);
            _ground.Blob(tip, Vector3.Cross(Vector3.up, dir), dir, 0.24f, null, 3, Mathf.PI * 0.5f, A(Cream, 0.9f * beat));
        }

        // ------------------------------------------------------------------ feed-driven moments

        void DrawFeed(World w, double alpha, Vector3 camFwd)
        {
            MechFeed f = Feed;
            double now = SimNow;
            int n = w.ActorCount;
            // heal beams (0.35 s): a billboard ribbon from the Healer to the mended ally
            for (int i = 0; i < MechFeed.Beams; i++)
            {
                MechFeed.Beam b = f.BeamRing[i];
                float age = (float)f.Wall(now - b.T);
                if (age < 0f || age > 0.35f || b.From >= n || b.To >= n || b.From < 0 || b.To < 0) continue;
                Vector3 a0 = ActorPos(w, b.From, alpha, 1.0f), a1 = ActorPos(w, b.To, alpha, 0.9f);
                Vector3 mid = (a0 + a1) * 0.5f, half = (a1 - a0) * 0.5f;
                Vector3 wide = Vector3.Cross(half, camFwd);
                if (wide.sqrMagnitude < 1e-6f) continue;
                float k = 1f - age / 0.35f;
                _air.Add = 160;
                _air.Quad(mid, half, wide.normalized * (0.1f * k + 0.03f), A(Heal, 0.9f * k), A(Cream, 0.6f * k));
                _air.Add = 0;
            }
            // bursts: pulse ring, purify column, drop pop, dodge pop, mine trigger flash
            for (int i = 0; i < MechFeed.Bursts; i++)
            {
                MechFeed.Burst b = f.BurstRing[i];
                float age = (float)f.Wall(now - b.T);
                if (age < 0f || age > 0.9f) continue;
                Vector3 c = P(b.X, b.Z, 0.05f);
                float R = (float)b.R;
                switch (b.Kind)
                {
                    case MechFeed.BurstPulse:
                        if (age > 0.45f) break;
                        {
                            float t = age / 0.45f, r = Mathf.Lerp(0.3f, R, FeedEase(t));
                            GroundRing(c, Mathf.Max(0f, r - 0.22f), r, 0f, Mathf.PI * 2f, 36, A(Heal, 0.85f * (1f - t)));
                            _ground.Blob(c, Vector3.right, Vector3.forward, r, null, 28, 0f, A(Heal, 0.14f * (1f - t)));
                        }
                        break;
                    case MechFeed.BurstPurify:
                        {
                            float t = age / 0.9f, k = 1f - t;
                            GroundRing(c, Mathf.Max(0f, R * t - 0.25f), R * t + 0.01f, 0f, Mathf.PI * 2f, 40, A(Seam, 0.8f * k));
                            Vector3 wide = Vector3.Cross(Vector3.up, camFwd);
                            if (wide.sqrMagnitude > 1e-6f)
                            {
                                _air.Add = 200;
                                _air.Quad(c + Vector3.up * 2.2f, wide.normalized * (0.5f * k + 0.1f), Vector3.up * 2.2f, A(Seam, 0.85f * k), A(Cream, 0f));
                                _air.Add = 0;
                            }
                        }
                        break;
                    case MechFeed.BurstDrop:
                        if (age > 0.3f) break;
                        GroundRing(c, Mathf.Max(0f, R * age / 0.3f - 0.1f), R * age / 0.3f + 0.02f, 0f, Mathf.PI * 2f, 20, A(Heal, 0.8f * (1f - age / 0.3f)));
                        break;
                    case MechFeed.BurstDodge:
                        if (age > 0.4f) break;
                        {
                            float t = age / 0.4f;
                            GroundRing(c, R * t, R * t + 0.12f, 0f, Mathf.PI * 2f, 28, A(Brass, 0.95f * (1f - t)));
                        }
                        break;
                    case MechFeed.BurstMine:
                        if (age > 0.18f) break;
                        GroundRing(c, Mathf.Max(0f, R - 0.15f), R + 0.05f, 0f, Mathf.PI * 2f, 28, A(Hurt, 0.95f));
                        break;
                }
            }
            // heal sparkles (0.6 s): a plus sign rising over the healed actor
            for (int i = 0; i < MechFeed.Sparkles; i++)
            {
                MechFeed.Sparkle s = f.SparkleRing[i];
                float age = (float)f.Wall(now - s.T);
                if (age < 0f || age > 0.6f || s.Actor < 0 || s.Actor >= n) continue;
                float t = age / 0.6f, k = 1f - t;
                Vector3 at = ActorPos(w, s.Actor, alpha, 1.8f + 0.6f * t);
                Vector3 right = Vector3.Cross(Vector3.up, camFwd);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                right.Normalize();
                Vector3 up = Vector3.Cross(camFwd, right).normalized;
                float sz = 0.16f + 0.06f * Mathf.Clamp01((float)(s.Amount / 30));
                _air.Quad(at, right * sz, up * sz * 0.3f, A(Heal, 0.95f * k), A(Heal, 0.95f * k));
                _air.Quad(at, right * sz * 0.3f, up * sz, A(Heal, 0.95f * k), A(Heal, 0.95f * k));
            }
            // wind-up glints (0.16 s) and contact flashes (0.08 s)
            for (int i = 0; i < MechFeed.Glints; i++)
            {
                MechFeed.Glint g = f.GlintRing[i];
                float life = g.Contact ? 0.08f : 0.16f;
                float age = (float)f.Wall(now - g.T);
                if (age < 0f || age > life || g.Cancelled || g.Actor < 0 || g.Actor >= n || !w.Actors[g.Actor].Alive) continue;
                float t = age / life, env = t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f;
                Vector3 body = ActorPos(w, g.Actor, alpha, 0f);
                Vector3 face = SimSpace.ToWorld(Math.Cos(g.Facing), 0, Math.Sin(g.Facing));
                Vector3 at = body + face * 0.65f + Vector3.up * 1.15f;
                Color32 team = CMTokens.Team(w.Actors[g.Actor].Team);
                _air.Add = 255;
                _air.Star(at, Vector3.Cross(Vector3.up, camFwd).normalized, Vector3.up, (g.Contact ? 0.38f : 0.26f) * env, 0.05f, 4, 0.3f, A(g.Contact ? Cream : Spark, env));
                _air.Add = 0;
                if (!g.Contact)
                {
                    float yaw = Mathf.Atan2(face.z, face.x);
                    GroundRing(body + Vector3.up * 0.06f, 0.75f, 0.86f, yaw - 0.7f, yaw + 0.7f, 10, A(team, 0.6f * env));
                }
            }
            // knockback streaks (0.25 s): three speed lines trailing the shove
            for (int i = 0; i < MechFeed.Streaks; i++)
            {
                MechFeed.Streak s = f.StreakRing[i];
                float age = (float)f.Wall(now - s.T);
                if (age < 0f || age > 0.25f || s.Victim < 0 || s.Victim >= n) continue;
                float k = 1f - age / 0.25f;
                Vector3 dir = SimSpace.ToWorld(s.Dx, 0, s.Dz);
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Vector3 body = ActorPos(w, s.Victim, alpha, 0.6f);
                float len = 0.35f + 0.6f * Mathf.Clamp01((float)(s.Dmg / 40));
                for (int j = -1; j <= 1; j++)
                {
                    Vector3 c = body - dir * (0.45f + len * 0.5f + 0.15f * Mathf.Abs(j)) + side * (0.22f * j) + Vector3.up * (0.25f * j);
                    _air.Quad(c, dir * len * 0.5f * k, Vector3.up * 0.025f, A(Cream, 0f), A(Cream, 0.8f * k));
                }
            }
        }

        static float FeedEase(float t) => 1f - (1f - t) * (1f - t);
    }

    /// <summary>Puts a MechanicsWorldView on the sim driver of every match (the UnitRings installer pattern).</summary>
    public static class MechanicsWorldViewInstaller
    {
        static MechanicsWatcher _watcher;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_watcher != null) return;   // domain reload is off: a watcher may survive between play sessions
            var go = new GameObject("Mechanics View Installer") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _watcher = go.AddComponent<MechanicsWatcher>();
        }

        /// <summary>Adds the view (and the poses listener) to the driver's GameObject unless it has them; returns the view.</summary>
        public static MechanicsWorldView EnsureOn(SimDriverBehaviour driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            MechanicsWorldView v = driver.GetComponent<MechanicsWorldView>();
            if (v == null) v = driver.gameObject.AddComponent<MechanicsWorldView>();
            if (driver.GetComponent<MechanicsPoses>() == null) driver.gameObject.AddComponent<MechanicsPoses>();
            if (driver.GetComponent<StagedBuildView>() == null) driver.gameObject.AddComponent<StagedBuildView>();
            if (driver.GetComponent<CriticalMass.Runtime.Hud.Mechanics.MechanicsHud>() == null) driver.gameObject.AddComponent<CriticalMass.Runtime.Hud.Mechanics.MechanicsHud>();
            if (driver.GetComponent<CriticalMass.Runtime.Hud.Mechanics.EmoteWheel>() == null) driver.gameObject.AddComponent<CriticalMass.Runtime.Hud.Mechanics.EmoteWheel>();
            // feat/mechanics-depth-view: jobs, sources, stations, pickups, pings, named bots, map events (U-063 to U-068).
            if (driver.GetComponent<DepthWorldView>() == null) driver.gameObject.AddComponent<DepthWorldView>();
            if (driver.GetComponent<CriticalMass.Runtime.Hud.Mechanics.DepthHud>() == null) driver.gameObject.AddComponent<CriticalMass.Runtime.Hud.Mechanics.DepthHud>();
            if (driver.GetComponent<CriticalMass.Runtime.Hud.Mechanics.StationPanel>() == null) driver.gameObject.AddComponent<CriticalMass.Runtime.Hud.Mechanics.StationPanel>();
            return v;
        }
    }

    /// <summary>The installer's watcher: finds the driver every 15 frames until it exists.</summary>
    public sealed class MechanicsWatcher : MonoBehaviour
    {
        const int SearchEveryFrames = 15;
        SimDriverBehaviour _driver;
        int _wait;

        void Update()
        {
            if (_driver != null) return;
            if (--_wait > 0) return;
            _wait = SearchEveryFrames;
            _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver != null) MechanicsWorldViewInstaller.EnsureOn(_driver);
        }
    }
}
