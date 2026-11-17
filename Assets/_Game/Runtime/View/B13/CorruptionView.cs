using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 item C: world-space view of the corruption rule (read-only, no sim writes). Crystal veins as violet-black spikes
    /// sized by charges, a glowing crystal over each carrier's head (team-tinted outline) and on the ground for a loose crystal,
    /// a violet pulse ring on the downed Kaiju while someone feeds it, a short column of light on each roll (violet success,
    /// grey fail) and a violet rim ring around a wild Kaiju. Two pooled FxMeshBuilder layers (2 draw calls, about 700 triangles
    /// at most at 16v16), rebuilt in LateUpdate into fixed vertex buffers: no per-frame allocation, no new textures or models.
    /// The meter, countdown and banner are V's HUD. Also installs CorruptionShots on the driver when <c>-corruptionShots</c> is set.
    /// </summary>
    public sealed class CorruptionView : MonoBehaviour
    {
        const int SolidVerts = 4096, GlowVerts = 4096;
        static readonly Color32 VeinDark = new Color32(46, 22, 70, 255), VeinMid = new Color32(98, 44, 150, 255), VeinLight = new Color32(178, 110, 236, 255);
        static readonly Color32 Glow = new Color32(196, 120, 255, 255), GlowSoft = new Color32(150, 80, 230, 0), Grey = new Color32(170, 170, 178, 255), GreySoft = new Color32(150, 150, 160, 0);
        static readonly Color32 AzureTint = new Color32(64, 150, 255, 255), CrimsonTint = new Color32(236, 64, 72, 255);

        SimDriverBehaviour _driver;
        FxMeshBuilder _solid, _glow;
        Material _solidMat, _glowMat;
        GameObject _solidGo, _glowGo;

        void Start()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            if (CorruptionShots.Requested && _driver.GetComponent<CorruptionShots>() == null) _driver.gameObject.AddComponent<CorruptionShots>();
            Shader sh = UnityEngine.Resources.Load<Shader>("CMFxUnlit");
            if (sh == null) sh = Shader.Find("CM/FxUnlit");
            if (sh == null) { enabled = false; return; }
            _solid = new FxMeshBuilder("Corruption Solid", SolidVerts, SolidVerts * 2);
            _glow = new FxMeshBuilder("Corruption Glow", GlowVerts, GlowVerts * 2);
            _solidMat = Mat(sh, "Corruption Solid", 2, 1, 2460);
            _glowMat = Mat(sh, "Corruption Glow", 0, 0, 3100);
            _solidGo = Layer("Corruption Solid", _solid, _solidMat);
            _glowGo = Layer("Corruption Glow", _glow, _glowMat);
        }

        void OnDestroy()
        {
            Release(_solidGo); Release(_glowGo); Release(_solidMat); Release(_glowMat);
            if (_solid != null) Release(_solid.Mesh);
            if (_glow != null) Release(_glow.Mesh);
        }

        static void Release(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static Material Mat(Shader sh, string name, int cull, int zwrite, int queue)
        {
            var m = new Material(sh) { name = name, renderQueue = queue };
            m.SetFloat("_Cull", cull);
            m.SetFloat("_ZWrite", zwrite);
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

        void LateUpdate()
        {
            if (_solid == null || _driver == null) return;
            _solid.Clear();
            _glow.Clear();
            World w = _driver.World;
            if (w != null && !CriticalMass.Runtime.Flow.GameFlow.MenuIdle && Corruption.On(w)) Build(w, _driver.Alpha);
            _solid.Upload();
            _glow.Upload();
        }

        static Vector3 At(World w, double x, double z, float lift)
        {
            return SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z), z) + Vector3.up * lift;
        }

        void Build(World w, double alpha)
        {
            float now = Time.time;
            CorruptionState s = w.Corrupt;
            // veins: up to VeinCharges spikes each, size by charges
            ResourceNode[] nodes = w.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                ResourceNode nd = nodes[i];
                if (nd.Kind != NodeKind.Corruption) continue;
                Vector3 b = At(w, nd.X, nd.Z, 0f);
                float full = nd.MaxCharges > 0 ? (float)nd.Charges / nd.MaxCharges : 0f;
                Spike(_solid, b, 0.55f, 0.35f + 1.1f * full, VeinDark, VeinMid);
                if (nd.Charges >= 1) Spike(_solid, b + new Vector3(0.45f, 0f, 0.2f), 0.3f, 0.9f, VeinMid, VeinLight);
                if (nd.Charges >= 2) Spike(_solid, b + new Vector3(-0.35f, 0f, -0.35f), 0.28f, 0.75f, VeinMid, VeinLight);
                if (nd.Charges > 0) _glow.Ring(b + Vector3.up * 0.05f, Vector3.right, Vector3.forward, 0.6f, 1.2f, 0f, 2f * Mathf.PI, 20, Glow, GlowSoft);
            }
            // carriers: a glowing crystal over the head, team-tinted outline
            float bob = 0.08f * Mathf.Sin(now * 3f);
            for (int id = 0; id < w.ActorCount; id++)
            {
                if (!Corruption.Carrying(w, id)) continue;
                double x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha;
                double z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
                Vector3 p = At(w, x, z, 2.35f + bob);
                Color32 tint = w.Actors[id].Team == TeamId.Azure ? AzureTint : CrimsonTint;
                Gem(_solid, p, 0.36f, tint, tint);
                Gem(_solid, p, 0.28f, VeinMid, VeinLight);
                _glow.Ring(p, Vector3.right, Vector3.forward, 0.0f, 0.55f, 0f, 2f * Mathf.PI, 12, Glow, GlowSoft);
            }
            // loose crystals on the ground
            for (int i = 0; i < s.DropActive.Length; i++)
            {
                if (!s.DropActive[i]) continue;
                Vector3 p = At(w, s.DropX[i], s.DropZ[i], 0.35f + bob);
                Gem(_solid, p, 0.3f, VeinMid, VeinLight);
                _glow.Ring(At(w, s.DropX[i], s.DropZ[i], 0.05f), Vector3.right, Vector3.forward, 0.2f, 0.8f, 0f, 2f * Mathf.PI, 16, Glow, GlowSoft);
            }
            Kaiju k = w.Kaiju;
            float kr = (float)w.Config.Kaiju.Radius;
            // feeding a downed Kaiju: a violet pulse ring
            if (w.Match.Hatched && k.KoIn > 0 && s.FeedTick >= 0 && w.Tick - s.FeedTick <= 2)
            {
                float ph = (now * 1.5f) % 1f;
                _glow.Ring(At(w, k.X, k.Z, 0.08f), Vector3.right, Vector3.forward, kr + 0.4f + ph * 2.2f, kr + 0.8f + ph * 2.2f, 0f, 2f * Mathf.PI, 40, Glow, GlowSoft);
            }
            // the roll: a short column of light for 1.5 s, violet on success, grey on fail
            if (s.LastRollTick >= 0)
            {
                double since = (w.Tick - s.LastRollTick + alpha) / w.Config.SimHz;
                if (since >= 0 && since < 1.5)
                {
                    float f = 1f - (float)(since / 1.5);
                    Color32 top = s.LastSuccess ? GlowSoft : GreySoft;
                    Color32 bot = s.LastSuccess ? Glow : Grey;
                    bot.a = (byte)(255 * f);
                    Vector3 c = At(w, w.Match.Hatched ? k.X : w.Map.Shrine.X, w.Match.Hatched ? k.Z : w.Map.Shrine.Z, 4f);
                    _glow.Quad(c, Vector3.right * 0.9f, Vector3.up * 4f, bot, top);
                    _glow.Quad(c, Vector3.forward * 0.9f, Vector3.up * 4f, bot, top);
                }
            }
            // a wild Kaiju: a violet rim ring and halo
            if (Corruption.Wild(w))
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * 4f);
                Color32 rim = Glow;
                rim.a = (byte)(160 + 95 * pulse);
                _glow.Ring(At(w, k.X, k.Z, 0.1f), Vector3.right, Vector3.forward, kr + 0.1f, kr + 0.55f, 0f, 2f * Mathf.PI, 48, rim, GlowSoft);
                _glow.Ring(At(w, k.X, k.Z, 2.2f), Vector3.right, Vector3.forward, kr - 0.1f, kr + 0.25f, 0f, 2f * Mathf.PI, 32, rim, GlowSoft);
            }
        }

        /// <summary>A four-sided spike (pyramid) on the ground: 5 vertices, 4 triangles.</summary>
        static void Spike(FxMeshBuilder b, Vector3 basePos, float r, float h, Color32 low, Color32 high)
        {
            if (!b.Room(5, 12)) return;
            int t = b.V(basePos + Vector3.up * h, high);
            int a0 = b.V(basePos + new Vector3(r, 0f, 0f), low);
            int a1 = b.V(basePos + new Vector3(0f, 0f, r), low);
            int a2 = b.V(basePos + new Vector3(-r, 0f, 0f), low);
            int a3 = b.V(basePos + new Vector3(0f, 0f, -r), low);
            b.T(t, a1, a0); b.T(t, a2, a1); b.T(t, a3, a2); b.T(t, a0, a3);
        }

        /// <summary>An octahedron gem: 6 vertices, 8 triangles.</summary>
        static void Gem(FxMeshBuilder b, Vector3 c, float r, Color32 low, Color32 high)
        {
            if (!b.Room(6, 24)) return;
            int top = b.V(c + Vector3.up * r * 1.4f, high);
            int bot = b.V(c - Vector3.up * r * 1.4f, low);
            int e0 = b.V(c + new Vector3(r, 0f, 0f), high);
            int e1 = b.V(c + new Vector3(0f, 0f, r), low);
            int e2 = b.V(c + new Vector3(-r, 0f, 0f), high);
            int e3 = b.V(c + new Vector3(0f, 0f, -r), low);
            b.T(top, e1, e0); b.T(top, e2, e1); b.T(top, e3, e2); b.T(top, e0, e3);
            b.T(bot, e0, e1); b.T(bot, e1, e2); b.T(bot, e2, e3); b.T(bot, e3, e0);
        }
    }
}
