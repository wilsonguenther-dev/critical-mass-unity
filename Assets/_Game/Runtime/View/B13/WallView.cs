using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 item B: world-space view of built walls, blocks and the build ghost. Read only on the Sim; one pooled
    /// FxMeshBuilder (CM/FxUnlit, premultiplied), so the whole view is ONE draw call and allocation-free per frame.
    /// <list type="bullet">
    /// <item>the GHOST while the local player is a living Builder and the rule is on: translucent cells (green = will build,
    /// red = refused; the reason word is TerrainPrompt's), knee high for short pieces, 1.6 u for tall ones, a flat pad for a
    /// crater fill;</item>
    /// <item>short walls (Cell.LowWall; b14: the bevelled stone block under it is TerrainEditMesh's, top exactly ShortTop
    /// above the ground, so the cap sits on its flat top inside the 0.07 u bevel): a team-coloured top, cracks by HP;</item>
    /// <item>every wall cell in its last WarnTicks (BuilderPieces' crumble warning, 3 s): a flickering outline box.</item>
    /// </list>
    /// Tall walls are Cell.Barricade cells in barricade slots, so ActorViews' palisade model (team material, damage stages)
    /// draws them. Budget: 4096 vertices; per frame at most 5 ghost cells x 20 vertices plus 12 per wall cell.
    /// </summary>
    public sealed class WallView : MonoBehaviour
    {
        public const float ShortTop = 0.6f, TallTop = 1.6f;
        /// <summary>Inset of a short wall's team-colour inlay from the cell edge (b14 review: was 0.08, a lid over the whole block).</summary>
        public const float CapInset = 0.2f;
        const int MaxVerts = 4096, MaxIdx = 8192;

        static readonly Color32 GhostOk = Pm(72, 220, 120, 110), GhostBad = Pm(240, 64, 56, 110), GhostEdge = Pm(255, 255, 255, 160);
        // b14 review: a deeper team shade (was 70,140,250 / 226,62,58): the inlay is unlit (FxUnlit), so at full brightness
        // it glowed like a UI tile on the stone, worst at night. CapLight() also dims it with the sun.
        static readonly Color32 AzureTop = new Color32(48, 108, 200, 255), CrimsonTop = new Color32(190, 54, 50, 255);
        /// <summary>Lowest share of the cap colour kept at night.</summary>
        const float CapNightFloor = 0.55f;

        /// <summary>b14 review: the scene's main light as a 0..1 share (sun intensity times its grey level), floored, so the
        /// unlit team inlay follows day and night like the lit stone round it.</summary>
        static float CapLight()
        {
            Light sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled) return 1f;
            return Mathf.Clamp(sun.intensity * sun.color.grayscale, CapNightFloor, 1f);
        }

        static Color32 Scale(Color32 c, float k) => new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a);
        static readonly Color32 Crack = new Color32(34, 30, 40, 255), Warn = Pm(255, 236, 160, 150);

        SimDriverBehaviour _driver;
        FxMeshBuilder _mesh;
        GameObject _layer;

        /// <summary>Stats of the last frame (WallShots prints them).</summary>
        public static int GhostCells { get; private set; }
        public static bool GhostOkShown { get; private set; }
        public static int GhostWhyShown { get; private set; }
        /// <summary>b15: the level a BLOCK ghost would make on a lift cell (0 when the ghost is not a stack).</summary>
        public static int StackGhostLevel { get; private set; }
        public static int CapsDrawn { get; private set; }
        public static int CracksDrawn { get; private set; }
        public static int WarnDrawn { get; private set; }

        static Color32 Pm(byte r, byte g, byte b, byte a) => new Color32((byte)(r * a / 255), (byte)(g * a / 255), (byte)(b * a / 255), a);

        void Start()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            Shader sh = UnityEngine.Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (_driver == null || sh == null) { enabled = false; return; }
            if (WallShots.Wanted && _driver.GetComponent<WallShots>() == null) _driver.gameObject.AddComponent<WallShots>();
            _mesh = new FxMeshBuilder("Builder Walls", MaxVerts, MaxIdx);
            var m = new Material(sh) { name = "Builder Walls", renderQueue = 3010 };
            m.SetFloat("_Cull", 0);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_OffsetUnits", -1);
            _layer = new GameObject("Builder Walls");
            _layer.transform.SetParent(transform, false);
            _layer.AddComponent<MeshFilter>().sharedMesh = _mesh.Mesh;
            var r = _layer.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        void LateUpdate()
        {
            if (_mesh == null) return;
            _mesh.Clear();
            GhostCells = CapsDrawn = CracksDrawn = WarnDrawn = 0;
            KeepoutRingShown = false;
            GhostOkShown = false;
            GhostWhyShown = 0;
            StackGhostLevel = 0;
            World w = _driver != null ? _driver.World : null;
            if (w != null && w.Map != null && BuilderWalls.On(w))
            {
                DrawWalls(w);
                DrawGhost(w);
            }
            _mesh.Upload();
        }

        void DrawGhost(World w)
        {
            int me = CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.LocalActor;
            if (me < 0 || me >= w.ActorCount || w.Match.Phase != MatchPhase.Playing) return;
            // b14 (#209 routed): while the wheel holds LADDER or BRIDGE the sim still has a wall piece selected; the
            // terrain view draws the climb ghost then, so the wall ghost stays hidden.
            var belt = CriticalMass.Runtime.Input.BuilderToolbelt.Local;
            if (belt != null && (belt.Actor < 0 || belt.Actor == me) && CriticalMass.Runtime.Input.BuilderToolbelt.IsClimb(belt.Selected)) return;
            if (!BuilderWalls.Ghost(w, me, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool ok)) return;
            bool fill = BuilderWalls.GhostFill(w, me);
            GhostOkShown = ok;
            GhostWhyShown = BuilderWalls.GhostWhy(w, me);
            DrawHillKeepout(w, cx, cz);
            Color32 c = ok ? GhostOk : GhostBad;
            float top = fill ? 0.05f : tall ? TallTop : ShortTop;
            // b15 terrain height: a BLOCK on a lift cell is one more level: the ghost is that course, from today's top
            // (GroundHeight.At includes the lift) up one step, so it shows the height the stack will have.
            bool stack = !fill && w.Walls.Piece[me] == (byte)BuildPiece.Block && Terraform.LiftDig(w, cx, cz);
            if (stack) top = (float)w.Map.LiftStepU;
            StackGhostLevel = stack ? w.Map.Lift[cz * w.Map.Width + cx] + 1 : 0;
            int half = len / 2;
            for (int k = -half; k < len - half; k++)
            {
                int x = cx + k * dx, z = cz + k * dz;
                float h = (float)GroundHeight.At(w.Map, x + 0.5, z + 0.5);
                if (fill) h = (float)GroundHeight.At(w.Map, w.X[me], w.Z[me]);
                Box(x, z, h + 0.02f, h + top, 0.04f, c, GhostEdge);
                GhostCells++;
            }
        }

        /// <summary>b16: the hill keep-out ring was drawn this frame (tests, WallShots).</summary>
        public static bool KeepoutRingShown { get; private set; }
        static readonly Color32 KeepoutInk = Pm(240, 64, 56, 90);
        /// <summary>b16: the ghost within this many units of the keep-out edge shows the ring.</summary>
        public const float KeepoutHintReach = 5f;

        /// <summary>b16 (audit: walls refused only inside 3 u of the 6 u hill): while a Builder aims a piece near the hill, a
        /// thin red ground ring shows the no-build circle (BuildConfig.HillKeepout, 7 u), so "can't build on the hill" has a
        /// place on the ground. Steady (no blink): nothing for the reduced-flashing setting to calm.</summary>
        void DrawHillKeepout(World w, int cx, int cz)
        {
            float r = (float)w.Config.Depth.Build.HillKeepout;
            float hx = (float)w.Map.Hill.X, hz = (float)w.Map.Hill.Z;
            float ddx = cx + 0.5f - hx, ddz = cz + 0.5f - hz;
            float d = Mathf.Sqrt(ddx * ddx + ddz * ddz);
            if (r <= 0f || d > r + KeepoutHintReach) return;
            float y = (float)GroundHeight.At(w.Map, hx, hz) + 0.04f;
            Vector3 c = SimSpace.ToWorld(hx, y, hz);
            Vector3 right = SimSpace.ToWorld(hx + 1f, y, hz) - c, up = SimSpace.ToWorld(hx, y, hz + 1f) - c;
            _mesh.Ring(c, right, up, r - 0.12f, r + 0.12f, 0f, Mathf.PI * 2f, 64, KeepoutInk);
            KeepoutRingShown = true;
        }

        void DrawWalls(World w)
        {
            Barricade[] list = w.Barricades;
            double warn = w.Config.Abilities.BuilderPieces.WarnTicks / CriticalMass.Sim.Config.Durations.ReferenceHz;
            bool blink = (Mathf.FloorToInt(Time.unscaledTime * 8f) & 1) == 0;
            float capK = CapLight();
            for (int s = 0; s < list.Length; s++)
            {
                byte kind = BuilderWalls.SlotKind(w, s);
                if (kind == 0) continue;
                Barricade b = list[s];
                float h = (float)GroundHeight.At(w.Map, b.Cx + 0.5, b.Cz + 0.5);
                float top = kind == BuilderWalls.KindTall ? TallTop : ShortTop;
                if (kind == BuilderWalls.KindShort)
                {
                    if (!_mesh.Room(4, 6)) return;
                    Color32 tc = Scale(b.Team == TeamId.Azure ? AzureTop : CrimsonTop, capK);
                    // b14 review: the cap is an inlay on the stone top (CapInset), not a lid over the whole block: the bevelled
                    // stone shows round it, so a row of blocks reads as stones carrying team colour instead of a blue brick.
                    Quad(b.Cx + CapInset, b.Cz + CapInset, b.Cx + 1f - CapInset, b.Cz + 1f - CapInset, h + top + 0.015f, tc);
                    CapsDrawn++;
                    double max = BuilderWalls.SlotMaxHp(w, s);
                    double share = max > 0 ? b.Hp / max : 1;
                    int cracks = share < 0.34 ? 2 : share < 0.67 ? 1 : 0;
                    for (int k = 0; k < cracks && _mesh.Room(4, 6); k++)
                    {
                        float o = 0.25f + 0.4f * k;
                        Quad(b.Cx + o, b.Cz + 0.15f, b.Cx + o + 0.07f, b.Cz + 0.85f, h + top + 0.03f, Crack);
                        CracksDrawn++;
                    }
                }
                if (b.Ttl <= warn && blink)
                {
                    Box(b.Cx, b.Cz, h, h + top + 0.05f, -0.03f, Warn, Warn);
                    WarnDrawn++;
                }
            }
        }

        /// <summary>A flat quad over sim cell rectangle [x0, x1] x [z0, z1] at height y.</summary>
        void Quad(float x0, float z0, float x1, float z1, float y, Color32 c)
        {
            if (!_mesh.Room(4, 6)) return;
            int a = _mesh.V(SimSpace.ToWorld(x0, y, z0), c);
            int b = _mesh.V(SimSpace.ToWorld(x1, y, z0), c);
            int d = _mesh.V(SimSpace.ToWorld(x1, y, z1), c);
            int e = _mesh.V(SimSpace.ToWorld(x0, y, z1), c);
            _mesh.T(a, b, d);
            _mesh.T(a, d, e);
        }

        /// <summary>Top and four sides of cell (cx, cz) from y0 to y1, inset by `inset` (negative = a little larger).</summary>
        void Box(int cx, int cz, float y0, float y1, float inset, Color32 side, Color32 top)
        {
            if (!_mesh.Room(20, 30)) return;
            float x0 = cx + inset, x1 = cx + 1 - inset, z0 = cz + inset, z1 = cz + 1 - inset;
            Quad(x0, z0, x1, z1, y1, top);
            Side(x0, z0, x1, z0, y0, y1, side);
            Side(x1, z0, x1, z1, y0, y1, side);
            Side(x1, z1, x0, z1, y0, y1, side);
            Side(x0, z1, x0, z0, y0, y1, side);
        }

        void Side(float xa, float za, float xb, float zb, float y0, float y1, Color32 c)
        {
            int a = _mesh.V(SimSpace.ToWorld(xa, y0, za), c);
            int b = _mesh.V(SimSpace.ToWorld(xb, y0, zb), c);
            int d = _mesh.V(SimSpace.ToWorld(xb, y1, zb), c);
            int e = _mesh.V(SimSpace.ToWorld(xa, y1, za), c);
            _mesh.T(a, b, d);
            _mesh.T(a, d, e);
        }

        void OnDestroy()
        {
            if (_layer != null) Destroy(_layer);
        }
    }
}
