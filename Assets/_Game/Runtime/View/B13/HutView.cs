using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 item H: world-space view of the Blacksmith hut rule (primitives and shared toon materials only; every object is
    /// made once when a world binds, then only moved, scaled and toggled, so nothing allocates per frame).
    /// Per team a hut prop 3.5 u behind the pad on the base side: stone base, gable roof, anvil, and a brass level plate with
    /// five pips lit in the team colour up to the level. Each ore vein is a dark rock cluster with copper flecks, sized by the
    /// charges left (the flecks pulse while a Miner channels it). A Miner carrying ore wears a copper lump on his back, sized by
    /// the load. HutLevel pushes a team-coloured ring pulse at the hut.
    /// Budget: huts 2 x 10 cubes, veins 6 x 5 cubes, 2 pulse discs (only while pulsing), lumps 1 cube per carrier: about 52
    /// renderers in the steady state and 12 triangles per cube (about 600 triangles), 11 shared materials (SRP batcher batches).
    /// </summary>
    public sealed class HutView : MonoBehaviour
    {
        const float Behind = 3.5f, PulseSeconds = 0.8f, PulseMaxR = 6f;
        const string StoneHex = "#6B6259", RoofHex = "#3A3F55", AnvilHex = "#2E3138", BrassHex = "#C9B27A", PipOffHex = "#3A3D48", RockHex = "#2B2A30", CopperHex = "#C0703A";

        SimDriverBehaviour _driver;
        World _world;
        Transform _root;
        readonly Transform[] _hut = new Transform[2];
        readonly Renderer[,] _pips = new Renderer[2, 5];
        readonly Transform[] _pulse = new Transform[2];
        readonly float[] _pulseAt = { -10f, -10f };
        Material _pipOff;
        readonly Material[] _pipOn = new Material[2];
        Transform[] _vein;
        Transform[] _flecks;
        int[] _veinIndex;
        Transform[] _lump;
        readonly int[] _seenLevel = { 0, 0 };

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

        void Bind(World w)
        {
            if (_root != null) Destroy(_root.gameObject);
            _world = w;
            _seenLevel[0] = Blacksmith.Level(w, TeamId.Azure);
            _seenLevel[1] = Blacksmith.Level(w, TeamId.Crimson);
            _root = new GameObject("Hut View").transform;
            _root.SetParent(transform, false);
            if (!Blacksmith.On(w)) return;
            MapDef map = w.Map;
            Material stone = ToonMaterials.Prop(StoneHex), roof = ToonMaterials.Prop(RoofHex), anvil = ToonMaterials.Prop(AnvilHex), brass = ToonMaterials.Prop(BrassHex);
            _pipOff = ToonMaterials.Prop(PipOffHex);
            for (int t = 0; t < 2; t++)
            {
                string team = Hex(t == 0 ? FxScene.AzureC : FxScene.CrimsonC);
                _pipOn[t] = ToonMaterials.Prop(team);
                PadDef pad = map.Pads[t];
                float side = pad.X < map.Width * 0.5 ? -1f : 1f;
                float x = (float)pad.X + side * Behind, z = (float)pad.Z;
                var hut = new GameObject("Hut " + (TeamId)t).transform;
                hut.SetParent(_root, false);
                hut.position = new Vector3(x, (float)GroundHeight.At(map, x, z), z);
                hut.rotation = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f); // the plate faces the pad
                Cube(hut, "Base", stone, new Vector3(0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 1.8f), Quaternion.identity);
                Cube(hut, "Roof", roof, new Vector3(0f, 1.25f, 0f), new Vector3(1.75f, 1.75f, 1.9f), Quaternion.Euler(0f, 0f, 45f));
                Cube(hut, "Roof Cap", _pipOn[t], new Vector3(0f, 2.48f, 0f), new Vector3(0.18f, 0.12f, 2.0f), Quaternion.identity);
                Cube(hut, "Anvil", anvil, new Vector3(0.7f, 0.25f, 1.25f), new Vector3(0.55f, 0.5f, 0.32f), Quaternion.identity);
                Cube(hut, "Anvil Face", anvil, new Vector3(0.7f, 0.56f, 1.25f), new Vector3(0.8f, 0.14f, 0.38f), Quaternion.identity);
                Transform plate = Cube(hut, "Level Plate", brass, new Vector3(-0.45f, 0.75f, 0.93f), new Vector3(1.2f, 0.36f, 0.06f), Quaternion.identity);
                for (int p = 0; p < 5; p++)
                {
                    Transform pip = Cube(hut, "Pip " + (p + 1), _pipOff, new Vector3(-0.85f + p * 0.2f, 0.75f, 0.97f), new Vector3(0.12f, 0.12f, 0.05f), Quaternion.identity);
                    _pips[t, p] = pip.GetComponent<Renderer>();
                }
                _hut[t] = hut;
                GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "Level Pulse " + (TeamId)t;
                Object.Destroy(ring.GetComponent<Collider>());
                ring.GetComponent<Renderer>().sharedMaterial = _pipOn[t];
                ring.transform.SetParent(_root, false);
                ring.transform.position = hut.position + Vector3.up * 0.05f;
                ring.SetActive(false);
                _pulse[t] = ring.transform;
                _ = plate;
            }
            // Ore veins.
            SourceNode[] list = w.Depth.Sources;
            int n = 0;
            for (int i = 0; i < list.Length; i++) if (list[i].Kind == BiomassType.Ore) n++;
            _vein = new Transform[n];
            _flecks = new Transform[n];
            _veinIndex = new int[n];
            Material rock = ToonMaterials.Prop(RockHex), copper = ToonMaterials.Prop(CopperHex);
            int k = 0;
            for (int i = 0; i < list.Length; i++)
            {
                SourceNode s = list[i];
                if (s.Kind != BiomassType.Ore) continue;
                var v = new GameObject("Ore Vein " + i).transform;
                v.SetParent(_root, false);
                v.position = new Vector3((float)s.X, (float)GroundHeight.At(map, s.X, s.Z), (float)s.Z);
                Cube(v, "Rock A", rock, new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.6f, 0.7f), Quaternion.Euler(0f, 20f, 8f));
                Cube(v, "Rock B", rock, new Vector3(0.45f, 0.2f, 0.25f), new Vector3(0.5f, 0.4f, 0.5f), Quaternion.Euler(10f, 50f, 0f));
                Cube(v, "Rock C", rock, new Vector3(-0.35f, 0.18f, -0.3f), new Vector3(0.45f, 0.36f, 0.42f), Quaternion.Euler(0f, -30f, 12f));
                var f = new GameObject("Flecks").transform;
                f.SetParent(v, false);
                Cube(f, "Fleck A", copper, new Vector3(0.1f, 0.58f, 0.15f), new Vector3(0.18f, 0.1f, 0.16f), Quaternion.Euler(0f, 35f, 0f));
                Cube(f, "Fleck B", copper, new Vector3(0.5f, 0.38f, 0.35f), new Vector3(0.14f, 0.08f, 0.12f), Quaternion.Euler(0f, -15f, 0f));
                _vein[k] = v;
                _flecks[k] = f;
                _veinIndex[k] = i;
                k++;
            }
            // Carried-ore lumps, one per actor slot.
            _lump = new Transform[w.ActorCount];
            for (int i = 0; i < _lump.Length; i++)
            {
                _lump[i] = Cube(_root, "Ore Lump " + i, copper, Vector3.zero, Vector3.one * 0.3f, Quaternion.Euler(15f, 30f, 10f));
                _lump[i].gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponentInParent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (w == null) return;
            if (!ReferenceEquals(w, _world)) Bind(w);
            if (!Blacksmith.On(w) || _vein == null) return;
            float now = Time.time;
            // The level pulse: a team's hut level rose since the last frame (b13 r2: read from state; views read world events
            // only through the bus, WorldReactionsTests).
            for (int tm = 0; tm < 2; tm++)
            {
                int lv = Blacksmith.Level(w, (TeamId)tm);
                if (lv > _seenLevel[tm]) _pulseAt[tm] = now;
                _seenLevel[tm] = lv;
            }
            for (int t = 0; t < 2; t++)
            {
                int lvl = Blacksmith.Level(w, (TeamId)t);
                for (int p = 0; p < 5; p++)
                {
                    Material m = p < lvl ? _pipOn[t] : _pipOff;
                    if (_pips[t, p].sharedMaterial != m) _pips[t, p].sharedMaterial = m;
                }
                float age = now - _pulseAt[t];
                bool on = age >= 0f && age < PulseSeconds;
                if (_pulse[t].gameObject.activeSelf != on) _pulse[t].gameObject.SetActive(on);
                if (on)
                {
                    float r = Mathf.Lerp(1f, PulseMaxR, age / PulseSeconds);
                    _pulse[t].localScale = new Vector3(r * 2f, 0.02f, r * 2f);
                }
            }
            SourceNode[] list = w.Depth.Sources;
            for (int k = 0; k < _vein.Length; k++)
            {
                SourceNode s = list[_veinIndex[k]];
                float full = s.MaxCharges > 0 ? (float)s.Charges / s.MaxCharges : 0f;
                float sc = 0.35f + 0.65f * full;
                _vein[k].localScale = new Vector3(sc, sc, sc);
                if (_flecks[k].gameObject.activeSelf != (s.Charges > 0)) _flecks[k].gameObject.SetActive(s.Charges > 0);
            }
            for (int i = 0; i < w.ActorCount && i < _lump.Length; i++)
            {
                Actor a = w.Actors[i];
                int ore = a.Alive ? Blacksmith.OreOf(a) : 0;
                // A Miner channelling: his vein's flecks pulse.
                int cv = Blacksmith.ChannelVein(w, i);
                if (cv >= 0)
                    for (int k = 0; k < _vein.Length; k++)
                        if (_veinIndex[k] == cv)
                        {
                            float ph = (float)Blacksmith.ChannelPhase(w, i);
                            float b = 1f + 0.25f * Mathf.Sin(ph * Mathf.PI * 2f);
                            _flecks[k].localScale = new Vector3(b, b, b);
                        }
                Transform l = _lump[i];
                if (l.gameObject.activeSelf != (ore > 0)) l.gameObject.SetActive(ore > 0);
                if (ore <= 0) continue;
                float fx = Mathf.Cos((float)a.Facing), fz = Mathf.Sin((float)a.Facing);
                float x = (float)w.X[i] - fx * 0.32f, z = (float)w.Z[i] - fz * 0.32f;
                l.position = new Vector3(x, (float)GroundHeight.At(w.Map, x, z) + 1.05f, z);
                float s1 = 0.2f + 0.07f * ore;
                l.localScale = new Vector3(s1, s1, s1);
            }
        }
    }
}
