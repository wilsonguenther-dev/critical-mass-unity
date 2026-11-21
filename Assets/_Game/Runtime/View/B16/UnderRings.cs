using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.B16
{
    /// <summary>
    /// b16 [9] (physics audit item 9: the Kaiju's body collides at 1.3 u but its canopy is drawn about 3 u wide, so a fighter
    /// at contact stands under it and is hidden) and [11] (the refused-jump edge highlight).
    /// <para>Choice: an X-RAY MARKER, not a canopy cut. Fading or cutting the canopy over every fighter under it would shred
    /// the Kaiju's silhouette in exactly the moments it matters (a hill brawl puts six or more fighters under it) and would
    /// hide its own claw and slam wind-ups, which are drawn on the body. Instead, with the same CM/UnitRing shader and depth
    /// test off (ZTest Always), this draws over the Kaiju: (1) the Kaiju's TRUE footprint, a dashed ring of
    /// PhysicsConfig.KaijuBodyRadius on the ground (what actually blocks you; the canopy overhang does not), and (2) a copy
    /// of the ground ring of every fighter within CanopyReach of the Kaiju (your brass ring with its ticks, allies solid,
    /// enemies dashed), so you always see where you and the others stand under it. The Kaiju stays whole.</para>
    /// <para>One dynamic mesh (ActorCapacity + 2 quads, made once), one material, one draw call; no per-frame allocation.
    /// Reads the sim only. Installed next to UnitRings by UnitRingsInstaller.EnsureOn.</para>
    /// </summary>
    public sealed class UnderRings : MonoBehaviour
    {
        /// <summary>Fighters whose centre is within this of the Kaiju's centre get the x-ray ring (u: the drawn canopy, about
        /// 1.5 u, plus a fighter's radius).</summary>
        public const float CanopyReach = 2.0f;
        public const float FootprintAlpha = 0.6f, XrayAlpha = 0.85f, EdgeScale = 0.8f;

        SimDriverBehaviour _driver;
        World _world;
        Mesh _mesh;
        Material _material;
        GameObject _drawObject;
        Vector3[] _vertices;
        Vector4[] _uv0, _uv1;
        int _visible;

        /// <summary>Quads drawn after the last Sync (tests).</summary>
        public int VisibleCount => _visible;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null) { enabled = false; return; }
            Bind(_driver.World);
        }

        void OnDestroy()
        {
            if (_drawObject != null) { if (Application.isPlaying) Destroy(_drawObject); else DestroyImmediate(_drawObject); }
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
            if (_material != null) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
        }

        /// <summary>Builds the pool for a world (called by Start, or by tests).</summary>
        public void Bind(World world)
        {
            _world = world;
            int n = World.ActorCapacity(world.Config) + 2;
            _vertices = new Vector3[n * 4];
            _uv0 = new Vector4[n * 4];
            _uv1 = new Vector4[n * 4];
            var indices = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int v = i * 4, t = i * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
                float q = UnitRings.QuadHalf;
                _uv0[v] = new Vector4(-q, -q, 0, 0);
                _uv0[v + 1] = new Vector4(-q, q, 0, 0);
                _uv0[v + 2] = new Vector4(q, q, 0, 0);
                _uv0[v + 3] = new Vector4(q, -q, 0, 0);
            }
            if (_mesh == null) { _mesh = new Mesh { name = "Under Rings (b16)" }; _mesh.MarkDynamic(); }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            MapDef map = world.Map;
            float w = map != null ? map.Width : 256f, h = map != null ? map.Height : 256f;
            Vector3 lo = SimSpace.ToWorld(-4, -4, h + 4), hi = SimSpace.ToWorld(w + 4, 16, -4);
            var b = new Bounds();
            b.SetMinMax(Vector3.Min(lo, hi), Vector3.Max(lo, hi));
            _mesh.bounds = b;
            if (_material == null)
            {
                var shader = Resources.Load<Shader>(UnitRings.ShaderResource);
                if (shader != null)
                {
                    _material = new Material(shader) { name = "CM UnderRing (x-ray)" };
                    Color c;
                    if (ColorUtility.TryParseHtmlString(Palette.Brass, out c)) _material.SetColor("_SelfColor", c);
                    if (ColorUtility.TryParseHtmlString(Palette.VoidNavy, out c)) _material.SetColor("_OutlineColor", c);
                    _material.SetColor("_AzureColor", CriticalMass.Runtime.Hud.Style.CMTokens.Team(TeamId.Azure));
                    _material.SetColor("_CrimsonColor", CriticalMass.Runtime.Hud.Style.CMTokens.Team(TeamId.Crimson));
                    _material.SetFloat("_Radius", UnitRings.Radius);
                    _material.SetFloat("_SelfWidth", UnitRings.SelfWidth);
                    _material.SetFloat("_TeamWidth", UnitRings.TeamWidth);
                    _material.SetFloat("_TickLength", UnitRings.TickLength);
                    _material.SetFloat("_TickWidth", UnitRings.TickWidth);
                    _material.SetFloat("_Dashes", UnitRings.EnemyDashes);
                    _material.SetFloat("_DashFill", UnitRings.DashFill);
                    _material.SetFloat("_ZTest", (float)CompareFunction.Always);
                    _material.renderQueue = (int)RenderQueue.Transparent - 30;   // after the Kaiju and the normal rings
                }
            }
            if (_drawObject == null)
            {
                _drawObject = new GameObject("Under Rings (b16)");
                _drawObject.transform.SetParent(transform, false);
                _drawObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var r = _drawObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
            }
            _drawObject.transform.position = Vector3.zero;
            _drawObject.transform.rotation = Quaternion.identity;
            _drawObject.transform.localScale = Vector3.one;
        }

        void LateUpdate()
        {
            if (_world == null) return;
            if (_driver != null && _driver.World != _world) Bind(_driver.World);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            Sync(_driver != null ? _driver.Alpha : 1.0, fwd);
        }

        void Quad(int slot, double x, double z, float scale, float style, float team, float tick, float alpha)
        {
            MapDef map = _world.Map;
            double gh = map != null ? GroundHeight.At(map, x, z) : 0;
            Vector3 c = SimSpace.ToWorld(x, gh + UnitRings.Lift, z);
            float q = UnitRings.QuadHalf * scale;
            int v = slot * 4;
            _vertices[v] = new Vector3(c.x - q, c.y, c.z - q);
            _vertices[v + 1] = new Vector3(c.x - q, c.y, c.z + q);
            _vertices[v + 2] = new Vector3(c.x + q, c.y, c.z + q);
            _vertices[v + 3] = new Vector3(c.x + q, c.y, c.z - q);
            var info = new Vector4(style, team, tick, alpha);
            _uv1[v] = _uv1[v + 1] = _uv1[v + 2] = _uv1[v + 3] = info;
            _visible++;
        }

        void Hide(int slot)
        {
            int v = slot * 4;
            _vertices[v] = _vertices[v + 1] = _vertices[v + 2] = _vertices[v + 3] = Vector3.zero;
        }

        /// <summary>One view update (allocation-free).</summary>
        public void Sync(double alpha, Vector3 cameraForward)
        {
            World w = _world;
            _visible = 0;
            int slots = _vertices.Length / 4;
            for (int s = 0; s < slots; s++) Hide(s);
            Vector2 up = new Vector2(cameraForward.x, cameraForward.z);
            float tickAngle = up.sqrMagnitude > 1e-8f ? Mathf.Atan2(up.y, up.x) : 0f;
            Kaiju k = w.Kaiju;
            bool kaijuOut = w.Match.Hatched && k.State != KaijuState.Egg && k.Hp > 0;
            if (kaijuOut)
            {
                double kx = k.PrevX + (k.X - k.PrevX) * alpha, kz = k.PrevZ + (k.Z - k.PrevZ) * alpha;
                // (1) the true footprint: a dashed ring of the body radius (shader radius 0.55 u, so the quad scales by body / 0.55)
                Quad(0, kx, kz, (float)(w.Config.Physics.KaijuBodyRadius / UnitRings.Radius), 2f, 1f, 0f, FootprintAlpha);
                // (2) fighters under the canopy: their own ring, drawn over the Kaiju
                TeamId selfTeam = w.ActorCount > UnitRings.SelfId ? w.Actors[UnitRings.SelfId].Team : TeamId.Azure;
                for (int i = 0; i < w.ActorCount && i + 1 < slots - 1; i++)
                {
                    Actor a = w.Actors[i];
                    if (a == null || !a.Alive || CriticalMass.Runtime.View.Fog.FogOfWar.Hides(i)) continue;
                    double x = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * alpha, z = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * alpha;
                    double dx = x - kx, dz = z - kz;
                    if (dx * dx + dz * dz > CanopyReach * CanopyReach) continue;
                    float style = i == UnitRings.SelfId ? 0f : a.Team == selfTeam ? 1f : 2f;
                    Quad(i + 1, x, z, 1f, style, a.Team == TeamId.Azure ? 0f : 1f, tickAngle, XrayAlpha);
                }
            }
            // (3) the local player's refused-jump edge highlight (b16 [11])
            if (JumpRefusedView.Edge(w, UnitRings.SelfId, alpha, out double ex, out double ez, out float fade))
                Quad(slots - 1, ex, ez, EdgeScale, 0f, 0f, tickAngle, fade);
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.SetVertices(_vertices, 0, _vertices.Length, flags);
            _mesh.SetUVs(1, _uv1, 0, _uv1.Length, flags);
        }
    }
}
