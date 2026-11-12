using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// Phase 0 placeholder views: one capsule per actor slot, one kaiju cube, one cube per resource node, one flat hill
    /// cube. Every Sim-to-Unity position goes through SimSpace (O-13). No Unity physics: primitives lose their colliders.
    /// Reads the Sim only; never writes it. Barricades are grid cells (GridChunkMeshView, F-08b).
    /// </summary>
    public sealed class ActorViews : MonoBehaviour
    {
        /// <summary>The human's capsule is this much wider in X and Z.</summary>
        private const float HumanScale = 1.15f;
        /// <summary>Resource node cube footprint.</summary>
        private const float NodeSize = 0.6f;
        /// <summary>Hill plate thickness.</summary>
        private const float HillThickness = 0.05f;

        private SimDriverBehaviour _driver;
        private Transform[] _actors;
        private Renderer[] _actorRenderers;
        private Transform _kaiju;
        private Renderer _kaijuRenderer;
        private Transform[] _nodes;
        private Renderer[] _nodeRenderers;
        private Transform _hill;

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null || _driver.World == null)
            {
                Debug.LogError("ActorViews needs a SimDriverBehaviour on the same GameObject");
                enabled = false;
                return;
            }
            World w = _driver.World;
            GameConfig cfg = _driver.Config;

            int capacity = World.ActorCapacity(cfg);
            _actors = new Transform[capacity];
            _actorRenderers = new Renderer[capacity];
            for (int i = 0; i < capacity; i++)
            {
                GameObject go = CreatePrimitive(PrimitiveType.Capsule, "Actor " + i);
                _actors[i] = go.transform;
                _actorRenderers[i] = go.GetComponent<Renderer>();
                go.SetActive(false);
            }

            GameObject kaiju = CreatePrimitive(PrimitiveType.Cube, "Kaiju");
            _kaiju = kaiju.transform;
            _kaijuRenderer = kaiju.GetComponent<Renderer>();

            int nodeCount = w.Nodes.Length;
            _nodes = new Transform[nodeCount];
            _nodeRenderers = new Renderer[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                GameObject go = CreatePrimitive(PrimitiveType.Cube, "Node " + i);
                _nodes[i] = go.transform;
                _nodeRenderers[i] = go.GetComponent<Renderer>();
            }

            GameObject hill = CreatePrimitive(PrimitiveType.Cube, "Hill");
            _hill = hill.transform;
            hill.GetComponent<Renderer>().sharedMaterial = PlaceholderMaterials.Get(Palette.Brass);
        }

        private GameObject CreatePrimitive(PrimitiveType type, string objectName)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            return go;
        }

        private void LateUpdate()
        {
            World w = _driver.World;
            GameConfig cfg = _driver.Config;
            MapDef map = w.Map;
            double a = _driver.Alpha;
            double charHeight = cfg.Render.CharacterHeight;

            // Actors.
            for (int i = 0; i < _actors.Length; i++)
            {
                Transform t = _actors[i];
                bool active = i < w.ActorCount && w.Actors[i].Alive;
                if (t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
                if (!active) continue;

                Actor actor = w.Actors[i];
                double x = Lerp(w.PrevX[i], w.X[i], a);
                double z = Lerp(w.PrevZ[i], w.Z[i], a);
                double h = GroundHeight(map, x, z);
                t.localPosition = SimSpace.ToWorld(x, h + charHeight / 2, z);
                t.localRotation = Quaternion.Euler(0f, SimSpace.YawDeg(actor.Facing), 0f);

                float d = (float)(2 * cfg.Class(actor.Cls).Radius);
                if (i == 0) d *= HumanScale;
                t.localScale = new Vector3(d, (float)(charHeight / 2), d);

                SetMaterial(_actorRenderers[i], actor.Team == TeamId.Azure ? Palette.Azure : Palette.Crimson);
            }

            // Kaiju: shown in every state.
            {
                Kaiju k = w.Kaiju;
                double x = Lerp(k.PrevX, k.X, a);
                double z = Lerp(k.PrevZ, k.Z, a);
                double h = GroundHeight(map, x, z);
                double kh = cfg.Render.KaijuHeight;
                float d = (float)(2 * cfg.Kaiju.Radius);
                _kaiju.localPosition = SimSpace.ToWorld(x, h + kh / 2, z);
                _kaiju.localRotation = Quaternion.Euler(0f, SimSpace.YawDeg(k.Facing), 0f);
                _kaiju.localScale = new Vector3(d, (float)kh, d);
                SetMaterial(_kaijuRenderer, KaijuHex(k.State));
            }

            // Resource nodes.
            int nodeCount = Math.Min(_nodes.Length, w.Nodes.Length);
            for (int i = 0; i < nodeCount; i++)
            {
                ResourceNode n = w.Nodes[i];
                float sy = 0.3f + 0.1f * n.Charges;
                double h = GroundHeight(map, n.X, n.Z);
                Transform t = _nodes[i];
                t.localPosition = SimSpace.ToWorld(n.X, h + sy / 2.0, n.Z);
                t.localRotation = Quaternion.identity;
                t.localScale = new Vector3(NodeSize, sy, NodeSize);
                SetMaterial(_nodeRenderers[i], n.Kind == NodeKind.Biomass ? Palette.Biomass : Palette.Corruption);
            }

            // Hill plate.
            {
                HillDef hill = map.Hill;
                double h = GroundHeight(map, hill.X, hill.Z);
                float d = (float)(2 * hill.R);
                _hill.localPosition = SimSpace.ToWorld(hill.X, h + HillThickness / 2.0, hill.Z);
                _hill.localRotation = Quaternion.identity;
                _hill.localScale = new Vector3(d, HillThickness, d);
            }
        }

        private static double Lerp(double from, double to, double t) => from + (to - from) * t;

        /// <summary>Map.Height01 of the cell under a sim point; 0 outside the map.</summary>
        private static double GroundHeight(MapDef map, double x, double z)
        {
            int idx = MapGrid.CellIndex(map, JsMath.FloorToInt(x), JsMath.FloorToInt(z));
            return idx < 0 ? 0 : map.Height01[idx];
        }

        private static string KaijuHex(KaijuState s)
        {
            switch (s)
            {
                case KaijuState.Egg: return Palette.Cream;
                case KaijuState.Neutral: return Palette.Stone;
                case KaijuState.Azure: return Palette.Azure;
                case KaijuState.Crimson: return Palette.Crimson;
                case KaijuState.Berserk: return Palette.Corruption;
                default: return Palette.Stone;
            }
        }

        private static void SetMaterial(Renderer r, string hex)
        {
            Material m = PlaceholderMaterials.Get(hex);
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }
    }
}
