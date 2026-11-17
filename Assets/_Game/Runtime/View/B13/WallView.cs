using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
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
    /// <item>short walls (Cell.LowWall, the 0.6 u stone box is GridChunkMeshView's): a team-coloured top, cracks by HP;</item>
    /// <item>every wall cell in its last WarnTicks (BuilderPieces' crumble warning, 3 s): a flickering outline box.</item>
    /// </list>
    /// Tall walls are Cell.Barricade cells in barricade slots, so ActorViews' palisade model (team material, damage stages)
    /// draws them. Budget: 4096 vertices; per frame at most 5 ghost cells x 20 vertices plus 12 per wall cell.
    /// </summary>
    public sealed class WallView : MonoBehaviour
    {
        public const float ShortTop = 0.6f, TallTop = 1.6f;
        const int MaxVerts = 4096, MaxIdx = 8192;

        static readonly Color32 GhostOk = Pm(72, 220, 120, 110), GhostBad = Pm(240, 64, 56, 110), GhostEdge = Pm(255, 255, 255, 160);
        static readonly Color32 AzureTop = new Color32(70, 140, 250, 255), CrimsonTop = new Color32(226, 62, 58, 255);
        static readonly Color32 Crack = new Color32(34, 30, 40, 255), Warn = Pm(255, 236, 160, 150);

        SimDriverBehaviour _driver;
        FxMeshBuilder _mesh;
        GameObject _layer;

        /// <summary>Stats of the last frame (WallShots prints them).</summary>
        public static int GhostCells { get; private set; }
        public static bool GhostOkShown { get; private set; }
        public static int GhostWhyShown { get; private set; }
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
            GhostOkShown = false;
            GhostWhyShown = 0;
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
            if (!BuilderWalls.Ghost(w, me, out int cx, out int cz, out int dx, out int dz, out int len, out bool tall, out bool ok)) return;
            bool fill = BuilderWalls.GhostFill(w, me);
            GhostOkShown = ok;
            GhostWhyShown = BuilderWalls.GhostWhy(w, me);
            Color32 c = ok ? GhostOk : GhostBad;
            float top = fill ? 0.05f : tall ? TallTop : ShortTop;
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

        void DrawWalls(World w)
        {
            Barricade[] list = w.Barricades;
            double warn = w.Config.Abilities.BuilderPieces.WarnTicks / CriticalMass.Sim.Config.Durations.ReferenceHz;
            bool blink = (Mathf.FloorToInt(Time.unscaledTime * 8f) & 1) == 0;
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
                    Color32 tc = b.Team == TeamId.Azure ? AzureTop : CrimsonTop;
                    Quad(b.Cx + 0.08f, b.Cz + 0.08f, b.Cx + 0.92f, b.Cz + 0.92f, h + top + 0.015f, tc);
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
