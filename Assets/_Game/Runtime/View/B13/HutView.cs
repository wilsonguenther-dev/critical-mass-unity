using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 item H: world-space view of the Blacksmith hut rule. Every object is made once when a world binds, then only
    /// moved, scaled and toggled, so nothing allocates per frame. Reads World only (view-only).
    /// b13 r3 ART: the scripted Blacksmith art (ArtMapping.BlacksmithHut / OreVein / Fx, Art/Blacksmith/_source~):
    ///  * per team a forge hut on the base side of the pad, its front (forge mouth, anvil) turned to the pad: stone plinth,
    ///    timber-framed walls, team-cloth gable roof, chimney, glowing forge. Stage meshes _L1.._L5 switch on with the hut
    ///    level (bellows and ore bin, chimney stack, weapon rack, banners, brass crest and flames), each lighting one more
    ///    team gem on the ridge, so the level reads at a glance from the gameplay camera;
    ///  * each ore vein is a rock outcrop with bright gold ore facets (shrinking a little with the charges, swapped for the
    ///    depleted rubble at zero) that pulses while a Miner channels it;
    ///  * a level-up is a short contained gold ring (1.2 to 2.1 m, 0.55 s) at the hut and a spark burst from the newly lit
    ///    ridge gem (10 sparks, 0.7 s), pooled. Round 2's 6 m opaque team disc read as spilled paint over the base.
    /// The carried ore lump is drawn by Anim.CarryProp at the Miner's hands (one cargo drawer; round 2 drew a second lump here).
    /// Without the art catalog (never in a player build: Build.cs rebuilds it) a small primitive stand-in keeps every state.
    /// Budget: 2 huts x 6 stage renderers (4136 triangles a hut at level 5), veins 2 renderers each (200 triangles full),
    /// 2 x (1 ring + 10 sparks) only while pulsing; shared materials (SRP batcher).
    /// </summary>
    public sealed class HutView : MonoBehaviour
    {
        const float Behind = 3.5f;
        public const float PulseSeconds = 0.55f, RingStartR = 1.2f, RingEndR = 2.1f, SparkSeconds = 0.7f;
        public const int Sparks = 10;
        const float SparkSpeed = 3.2f, SparkGravity = 7.5f, SparkScale = 1.4f;
        /// <summary>Ridge gem n's spot in the hut's local space (Unity: Blender x is -x, Blender -y is +z): the burst origin.</summary>
        static readonly float[] GemX = { 1.0f, 0.55f, 0.1f, -0.35f, -0.8f };
        const float GemY = 2.97f, GemZ = -0.35f;
        const string StoneHex = "#9C9480", RoofDarkHex = "#1D6A8C", AnvilHex = "#4C5260", RockHex = "#7A7F8C", SpentHex = "#5A5D66",
            OreHex = "#FFC83D", FxHex = "#FFD66B";

        SimDriverBehaviour _driver;
        World _world;
        Transform _root;
        readonly Transform[] _hut = new Transform[2];
        readonly GameObject[,] _stage = new GameObject[2, 6];
        readonly int[] _shownLevel = { -1, -1 };
        readonly int[] _seenLevel = { 0, 0 };
        readonly float[] _pulseAt = { -10f, -10f };
        readonly Vector3[] _burstAt = new Vector3[2];
        readonly Transform[] _ring = new Transform[2];
        readonly Transform[,] _spark = new Transform[2, Sparks];
        readonly bool[] _fxOn = new bool[2];
        Transform[] _vein;
        GameObject[] _veinFull, _veinSpent;
        int[] _veinIndex;
        int[] _slotOfSource;
        float[] _veinPulse;
        /// <summary>Spark scale at birth: the art spark is 0.24 m tall (x SparkScale), the stand-in a unit cube.</summary>
        float _sparkUnit = SparkScale;

        /// <summary>Spark directions (unit, upward cone), fixed so every burst is the same shape.</summary>
        static readonly Vector3[] SparkDir = MakeSparkDirs();

        static Vector3[] MakeSparkDirs()
        {
            var d = new Vector3[Sparks];
            for (int k = 0; k < Sparks; k++)
            {
                float a = k * Mathf.PI * 2f / Sparks + (k % 2) * 0.3f, up = 0.75f + 0.25f * ((k * 7) % 5) / 4f;
                d[k] = new Vector3(Mathf.Cos(a) * (1.1f - up), up, Mathf.Sin(a) * (1.1f - up)).normalized;
            }
            return d;
        }

        void Start()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            if (_driver != null && HutShots.Requested && _driver.GetComponent<HutShots>() == null) _driver.gameObject.AddComponent<HutShots>();
        }

        static string Hex(Color32 c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static Transform Cube(Transform parent, string name, Material m, Vector3 local, Vector3 scale, Quaternion rot)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Object.Destroy(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = m;
            Transform t = g.transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            t.localScale = scale;
            t.localRotation = rot;
            return t;
        }

        /// <summary>
        /// b13 r2: where team pad's hut stands: the first spot with walkable ground under its whole footprint, trying behind the
        /// pad (3.5 u, then 3.0 and 2.5) and then beside it (3.5 u north / south). Round 1 always used 3.5 u behind, which on the
        /// base island's back edge, and placed it at world z = +sim z (the views use SimSpace.ToWorld: world z = -sim z), so the
        /// hut, veins and ore lumps were drawn off the map, floating against the sky. Returns WORLD space; HutShots frames it.
        /// </summary>
        public static Vector3 HutPosition(MapDef map, PadDef pad)
        {
            double side = pad.X < map.Width * 0.5 ? -1.0 : 1.0;
            for (int k = 0; k < SpotDx.Length; k++)
            {
                double x = pad.X + side * SpotDx[k], z = pad.Z + SpotDz[k];
                if (!Grounded(map, x, z)) continue;
                return SimSpace.ToWorld(x, GroundHeight.At(map, x, z), z);   // sim z is world -z
            }
            double fx = pad.X + side * Behind;
            return SimSpace.ToWorld(fx, GroundHeight.At(map, fx, pad.Z), pad.Z);
        }

        /// <summary>The hut's yaw (degrees): its front, the forge mouth and anvil, faces the team pad.</summary>
        public static float HutYaw(MapDef map, PadDef pad)
        {
            Vector3 hp = HutPosition(map, pad), pw = SimSpace.ToWorld(pad.X, 0, pad.Z);
            return Mathf.Atan2(pw.x - hp.x, pw.z - hp.z) * Mathf.Rad2Deg;
        }

        static readonly double[] SpotDx = { 3.5, 3.0, 2.5, 0, 0, 1.5, 1.5 };
        static readonly double[] SpotDz = { 0, 0, 0, 3.5, -3.5, 3.0, -3.0 };

        /// <summary>Walkable ground under a 2.4 x 2.4 u footprint centred on (x, z) (the hut base is 2.4 x 1.8 u).</summary>
        static bool Grounded(MapDef m, double x, double z)
        {
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (!MapGrid.IsWalkable(m, JsMath.FloorToInt(x + i * 1.2), JsMath.FloorToInt(z + j * 1.2))) return false;
            return true;
        }

        /// <summary>An instance of a catalogued art model on its team material, or null (no catalog entry / no material).</summary>
        static GameObject Art(string key, Transform parent, string name, TeamId team)
        {
            ArtCatalog cat = ArtCatalog.Load();
            ArtEntry e = cat != null ? cat.Find(key) : null;
            ModelSpec spec = ArtMapping.Spec(key);
            if (e == null || e.Model == null || spec == null) return null;
            Material m;
            try { m = ArtMaterials.ForTeam(spec, team); }
            catch (System.InvalidOperationException) { return null; }
            GameObject g = Object.Instantiate(e.Model, parent, false);
            g.name = name;
            foreach (Animator an in g.GetComponentsInChildren<Animator>(true)) Object.Destroy(an);
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
            return g;
        }

        static GameObject Child(GameObject root, string name)
        {
            if (root == null) return null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) if (r.name == name) return r.gameObject;
            return null;
        }

        /// <summary>The mesh of one Fx piece in the catalogued model (read from the prefab asset; nothing instantiated).</summary>
        static Mesh FxMesh(string piece)
        {
            ArtCatalog cat = ArtCatalog.Load();
            ArtEntry e = cat != null ? cat.Find(ArtMapping.BlacksmithFx) : null;
            if (e == null || e.Model == null) return null;
            foreach (MeshFilter f in e.Model.GetComponentsInChildren<MeshFilter>(true)) if (f.name == piece) return f.sharedMesh;
            return null;
        }

        static Transform FxPiece(Transform parent, string name, Mesh mesh, Material m)
        {
            var g = new GameObject(name);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.transform.SetParent(parent, false);
            g.SetActive(false);
            return g.transform;
        }

        static Mesh _fallbackRing;

        /// <summary>A flat 1 m ring (inner 0.93 m), the stand-in for BlacksmithFx_Ring; built once.</summary>
        static Mesh FallbackRing()
        {
            if (_fallbackRing != null) return _fallbackRing;
            const int n = 32;
            var v = new Vector3[n * 2];
            var nr = new Vector3[n * 2];
            var tri = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * 0.93f, 0.03f, s * 0.93f);
                v[i * 2 + 1] = new Vector3(c * 1.07f, 0.03f, s * 1.07f);
                nr[i * 2] = nr[i * 2 + 1] = Vector3.up;
                int j = (i + 1) % n;
                tri[i * 6] = i * 2; tri[i * 6 + 1] = j * 2; tri[i * 6 + 2] = i * 2 + 1;
                tri[i * 6 + 3] = i * 2 + 1; tri[i * 6 + 4] = j * 2; tri[i * 6 + 5] = j * 2 + 1;
            }
            _fallbackRing = new Mesh { name = "Hut Ring (stand-in)", vertices = v, normals = nr, triangles = tri };
            return _fallbackRing;
        }

        void Bind(World w)
        {
            if (_root != null) Destroy(_root.gameObject);
            _world = w;
            _seenLevel[0] = Blacksmith.Level(w, TeamId.Azure);
            _seenLevel[1] = Blacksmith.Level(w, TeamId.Crimson);
            _shownLevel[0] = _shownLevel[1] = -1;
            _pulseAt[0] = _pulseAt[1] = -10f;
            _root = new GameObject("Hut View").transform;
            _root.SetParent(transform, false);
            if (!Blacksmith.On(w)) return;
            MapDef map = w.Map;
            // Level-up effect pieces: the art's glow ring and spark (no hull), else stand-ins on an unlined toon material.
            Mesh ringMesh = FxMesh(ArtMapping.BlacksmithFxRing), sparkMesh = FxMesh(ArtMapping.BlacksmithFxSpark);
            Material fxMat = null;
            _sparkUnit = SparkScale;
            ModelSpec fxSpec = ArtMapping.Spec(ArtMapping.BlacksmithFx);
            if (ringMesh != null && sparkMesh != null && fxSpec != null)
            {
                try { fxMat = ArtMaterials.ForTeam(fxSpec, TeamId.Azure); }
                catch (System.InvalidOperationException) { fxMat = null; }
            }
            if (fxMat == null)
            {
                ringMesh = FallbackRing();
                sparkMesh = null;
                _sparkUnit = 0.12f;
                fxMat = ToonMaterials.Toon("fx", FxHex, FxHex, 0f, 0f, ToonMaterials.OutlineFromPivot);
            }
            for (int t = 0; t < 2; t++)
            {
                PadDef pad = map.Pads[t];
                Vector3 hp = HutPosition(map, pad);
                var hut = new GameObject("Hut " + (TeamId)t).transform;
                hut.SetParent(_root, false);
                hut.position = hp;
                hut.rotation = Quaternion.Euler(0f, HutYaw(map, pad), 0f);   // the forge mouth faces the pad
                GameObject art = Art(ArtMapping.BlacksmithHut, hut, "Blacksmith Hut", (TeamId)t);
                for (int s = 0; s < 6; s++) _stage[t, s] = Child(art, ArtMapping.BlacksmithHutStages[s]);
                if (art == null || _stage[t, 0] == null) StandInHut(hut, t);
                _hut[t] = hut;
                _ring[t] = FxPiece(_root, "Level Ring " + (TeamId)t, ringMesh, fxMat);
                for (int k = 0; k < Sparks; k++)
                {
                    Transform sp = sparkMesh != null
                        ? FxPiece(_root, "Level Spark " + k, sparkMesh, fxMat)
                        : Cube(_root, "Level Spark " + k, fxMat, Vector3.zero, Vector3.one * 0.1f, Quaternion.identity);
                    sp.gameObject.SetActive(false);
                    _spark[t, k] = sp;
                }
                _fxOn[t] = false;
            }
            // Ore veins.
            SourceNode[] list = w.Depth.Sources;
            int n = 0;
            for (int i = 0; i < list.Length; i++) if (list[i].Kind == BiomassType.Ore) n++;
            _vein = new Transform[n];
            _veinFull = new GameObject[n];
            _veinSpent = new GameObject[n];
            _veinIndex = new int[n];
            _veinPulse = new float[n];
            _slotOfSource = new int[list.Length];
            Material rock = null, ore = null, spent = null;
            int k2 = 0;
            for (int i = 0; i < list.Length; i++)
            {
                _slotOfSource[i] = -1;
                SourceNode s = list[i];
                if (s.Kind != BiomassType.Ore) continue;
                var v = new GameObject("Ore Vein " + i).transform;
                v.SetParent(_root, false);
                v.position = SimSpace.ToWorld(s.X, GroundHeight.At(map, s.X, s.Z), s.Z);   // b13 r2: sim z is world -z (round 1 drew veins off the map)
                v.rotation = Quaternion.Euler(0f, (i * 137.5f) % 360f, 0f);              // each vein turned its own way
                GameObject art = Art(ArtMapping.BlacksmithOreVein, v, "Blacksmith Ore Vein", TeamId.Azure);
                GameObject full = Child(art, ArtMapping.OreVeinFull), dep = Child(art, ArtMapping.OreVeinDepleted);
                if (full == null || dep == null)
                {
                    if (art != null) Destroy(art);
                    if (rock == null) { rock = ToonMaterials.Prop(RockHex); ore = ToonMaterials.Prop(OreHex); spent = ToonMaterials.Prop(SpentHex); }
                    full = new GameObject("Full");
                    full.transform.SetParent(v, false);
                    Cube(full.transform, "Rock A", rock, new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.6f, 0.7f), Quaternion.Euler(0f, 20f, 8f));
                    Cube(full.transform, "Rock B", rock, new Vector3(0.45f, 0.2f, 0.25f), new Vector3(0.5f, 0.4f, 0.5f), Quaternion.Euler(10f, 50f, 0f));
                    Cube(full.transform, "Ore A", ore, new Vector3(0.1f, 0.62f, 0.12f), new Vector3(0.26f, 0.22f, 0.24f), Quaternion.Euler(20f, 35f, 10f));
                    Cube(full.transform, "Ore B", ore, new Vector3(0.48f, 0.42f, 0.32f), new Vector3(0.2f, 0.18f, 0.2f), Quaternion.Euler(-15f, -15f, 20f));
                    dep = new GameObject("Depleted");
                    dep.transform.SetParent(v, false);
                    Cube(dep.transform, "Rubble", spent, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.24f, 0.7f), Quaternion.Euler(0f, 20f, 4f));
                }
                dep.SetActive(false);
                _vein[k2] = v;
                _veinFull[k2] = full;
                _veinSpent[k2] = dep;
                _veinIndex[k2] = i;
                _slotOfSource[i] = k2;
                k2++;
            }
        }

        /// <summary>Primitive stand-in (no art catalog): plinth, team roof, anvil; one ridge cube per level stage.</summary>
        void StandInHut(Transform hut, int t)
        {
            Material stone = ToonMaterials.Prop(StoneHex), roof = ToonMaterials.Prop(Hex(t == 0 ? FxScene.AzureC : FxScene.CrimsonC)),
                dark = ToonMaterials.Prop(RoofDarkHex), anvil = ToonMaterials.Prop(AnvilHex), gem = ToonMaterials.Prop(OreHex);
            var l0 = new GameObject("Stand-in L0").transform;
            l0.SetParent(hut, false);
            Cube(l0, "Base", stone, new Vector3(0f, 0.8f, 0f), new Vector3(2.4f, 1.6f, 1.8f), Quaternion.identity);
            Cube(l0, "Roof", roof, new Vector3(0f, 1.7f, 0f), new Vector3(1.75f, 1.75f, 1.9f), Quaternion.Euler(0f, 0f, 45f));
            Cube(l0, "Ridge", dark, new Vector3(0f, 2.95f, 0f), new Vector3(2.6f, 0.16f, 0.22f), Quaternion.identity);
            Cube(l0, "Anvil", anvil, new Vector3(0.6f, 0.3f, 1.3f), new Vector3(0.6f, 0.3f, 0.3f), Quaternion.identity);
            _stage[t, 0] = l0.gameObject;
            for (int s = 1; s < 6; s++)
                _stage[t, s] = Cube(hut, "Stand-in L" + s, gem, new Vector3(GemX[s - 1], GemY, GemZ + 0.35f), Vector3.one * 0.22f, Quaternion.Euler(45f, 0f, 45f)).gameObject;
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponentInParent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (w == null) return;
            if (!ReferenceEquals(w, _world)) Bind(w);
            if (!Blacksmith.On(w) || _vein == null) return;
            float now = Time.time;
            for (int t = 0; t < 2; t++)
            {
                // The level-up: a team's hut level rose since the last frame (b13 r2: read from state; views read world events
                // only through the bus, WorldReactionsTests).
                int lvl = Blacksmith.Level(w, (TeamId)t);
                if (lvl > _seenLevel[t])
                {
                    _pulseAt[t] = now;
                    int g = Mathf.Clamp(lvl, 1, 5) - 1;
                    _burstAt[t] = _hut[t].TransformPoint(new Vector3(GemX[g], GemY, GemZ));
                }
                _seenLevel[t] = lvl;
                if (lvl != _shownLevel[t])
                {
                    _shownLevel[t] = lvl;
                    for (int s = 0; s < 6; s++)
                    {
                        GameObject st = _stage[t, s];
                        bool on = s == 0 || s <= lvl;
                        if (st != null && st.activeSelf != on) st.SetActive(on);
                    }
                }
                Pulse(t, now - _pulseAt[t]);
            }
            SourceNode[] list = w.Depth.Sources;
            for (int k = 0; k < _veinPulse.Length; k++) _veinPulse[k] = 1f;
            // A Miner channelling: his vein pulses (one pass over the actors, the vein found by its source index).
            for (int i = 0; i < w.ActorCount; i++)
            {
                int cv = Blacksmith.ChannelVein(w, i);
                if (cv < 0 || cv >= _slotOfSource.Length || _slotOfSource[cv] < 0) continue;
                float ph = (float)Blacksmith.ChannelPhase(w, i);
                _veinPulse[_slotOfSource[cv]] = 1f + 0.06f * Mathf.Sin(ph * Mathf.PI * 2f);
            }
            for (int k = 0; k < _vein.Length; k++)
            {
                SourceNode s = list[_veinIndex[k]];
                bool left = s.Charges > 0;
                if (_veinFull[k].activeSelf != left) _veinFull[k].SetActive(left);
                if (_veinSpent[k].activeSelf == left) _veinSpent[k].SetActive(!left);
                float full = s.MaxCharges > 0 ? (float)s.Charges / s.MaxCharges : 0f;
                float sc = (left ? 0.72f + 0.28f * full : 1f) * _veinPulse[k];
                _vein[k].localScale = new Vector3(sc, sc, sc);
            }
        }

        /// <summary>The level-up effect at age seconds: ring out from the hut base, sparks out of the new ridge gem.</summary>
        void Pulse(int t, float age)
        {
            bool on = age >= 0f && age < SparkSeconds;
            if (!on)
            {
                if (_fxOn[t])
                {
                    _fxOn[t] = false;
                    _ring[t].gameObject.SetActive(false);
                    for (int k = 0; k < Sparks; k++) _spark[t, k].gameObject.SetActive(false);
                }
                return;
            }
            if (!_fxOn[t])
            {
                _fxOn[t] = true;
                for (int k = 0; k < Sparks; k++) _spark[t, k].gameObject.SetActive(true);
            }
            bool ringOn = age < PulseSeconds;
            if (_ring[t].gameObject.activeSelf != ringOn) _ring[t].gameObject.SetActive(ringOn);
            if (ringOn)
            {
                float u = age / PulseSeconds, e = 1f - (1f - u) * (1f - u) * (1f - u);
                float r = Mathf.Lerp(RingStartR, RingEndR, e), h = u < 0.6f ? 1f : Mathf.Lerp(1f, 0.2f, (u - 0.6f) / 0.4f);
                _ring[t].position = _hut[t].position + Vector3.up * 0.02f;
                _ring[t].localScale = new Vector3(r, h, r);
            }
            float life = 1f - age / SparkSeconds;
            Vector3 o = _burstAt[t];
            for (int k = 0; k < Sparks; k++)
            {
                Transform sp = _spark[t, k];
                Vector3 d = SparkDir[k];
                sp.position = new Vector3(o.x + d.x * SparkSpeed * age, o.y + d.y * SparkSpeed * age - 0.5f * SparkGravity * age * age, o.z + d.z * SparkSpeed * age);
                sp.rotation = Quaternion.Euler(age * 540f, k * 36f, age * 300f);
                float s = _sparkUnit * life;
                sp.localScale = new Vector3(s, s, s);
            }
        }
    }
}
