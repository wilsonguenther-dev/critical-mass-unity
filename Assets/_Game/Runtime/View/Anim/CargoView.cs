using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// Carried cargo on the carrier (audit finding 3, DG-71's cargo item; un-skips CarriedCargoIsDrawnOnTheCarrier).
    /// Attached through contract C3: after the pose, a carrier's model gets a pooled bundle on its back, parented under the
    /// model and placed behind the chest joint against the actor's facing: biomass plants or corruption growths from the
    /// node props at 0.35 scale (grey-box cubes in the palette colours when the art catalog is missing), 1 to 3 pieces by
    /// CarryAmount, a 0.15 s pop (0 to 1.15 to 1) when the load grows and a vertical spring (k 180, damping 0.35) on the
    /// carrier's bounce. Gameplay information, so every quality tier draws it. Pooled per actor slot: no allocation after
    /// the first carry. Reads World only.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public sealed class CargoView : MonoBehaviour, IPostPoseRootListener, IPostPoseListener
    {
        public const float PropScale = 0.35f, BackOffset = 0.28f, PopSeconds = 0.15f;
        private const int Stack = 3;

        private sealed class Slot
        {
            public GameObject Root;
            public GameObject[] Biomass, Corruption;
            public int Amount, Seen = -10;
            public float PopAt = -10f;
            public LagSpring1 Bob;
            public float PrevY;
            public bool HasY;
        }

        private SimDriverBehaviour _driver;
        private Slot[] _slots;
        private readonly Dictionary<Transform, Transform> _chest = new Dictionary<Transform, Transform>();

        /// <summary>Cargo bundles shown this frame (tests, captures).</summary>
        public int Shown { get; private set; }
        private int _shownFrame;

        private void OnEnable() => PostPoseHub.Register(this);
        private void OnDisable() => PostPoseHub.Unregister(this);

        public void PostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            if (mv != null) PostPoseRoot(actor, mv.Transform, mv.Entry != null ? mv.Entry.Key : null);
        }

        public void PostPoseRoot(int actor, Transform root, string modelKey)
        {
            if (actor < 0 || root == null) return;
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (w == null || actor >= w.ActorCount) return;
            if (_slots == null || _slots.Length < World.ActorCapacity(_driver.Config)) _slots = new Slot[World.ActorCapacity(_driver.Config)];
            if (actor >= _slots.Length) return;
            Actor a = w.Actors[actor];
            Slot s = _slots[actor];
            bool carrying = a.Alive && a.Carry != CarryKind.None && a.CarryAmount > 0;
            if (!carrying)
            {
                if (s != null && s.Root != null && s.Root.activeSelf) s.Root.SetActive(false);
                if (s != null) { s.Amount = 0; s.HasY = false; }
                return;
            }
            if (s == null) _slots[actor] = s = Make(actor);
            if (s.Root.transform.parent != root) s.Root.transform.SetParent(root, false);
            if (!s.Root.activeSelf) s.Root.SetActive(true);
            s.Seen = Time.frameCount;
            int pieces = Mathf.Clamp(a.CarryAmount, 1, Stack);
            if (a.CarryAmount > s.Amount) s.PopAt = Time.time;
            s.Amount = a.CarryAmount;
            bool bio = a.Carry == CarryKind.Biomass;
            for (int k = 0; k < Stack; k++)
            {
                SetActive(s.Biomass[k], bio && k < pieces);
                SetActive(s.Corruption[k], !bio && k < pieces);
            }

            if (!_chest.TryGetValue(root, out Transform chest))
            {
                chest = PostPoseHub.FindJoint(root, "chest");
                if (chest == null) chest = PostPoseHub.FindJoint(root, "spine");
                _chest[root] = chest;
            }
            Vector3 facing = Quaternion.Euler(0f, SimSpace.YawDeg(a.Facing), 0f) * Vector3.forward;
            Vector3 anchor = chest != null ? chest.position : root.position + Vector3.up * 1.0f;

            // Bounce: a vertical spring driven by the carrier's own vertical motion (landings, jumps, strides).
            float dt = Time.deltaTime;
            float y = root.position.y;
            float push = s.HasY && dt > 1e-5f ? Mathf.Clamp((y - s.PrevY) / dt, -6f, 6f) * -0.02f : 0f;
            s.PrevY = y;
            s.HasY = true;
            s.Bob.V += push;
            s.Bob.Step(0f, Mathf.Sqrt(180f), 0.35f, dt);
            s.Bob.X = Mathf.Clamp(s.Bob.X, -0.12f, 0.12f);

            float age = Time.time - s.PopAt;
            float pop = age < 0f || age >= PopSeconds ? 1f : age < PopSeconds * 0.6f ? 1.15f * (age / (PopSeconds * 0.6f)) : 1.15f - 0.15f * ((age - PopSeconds * 0.6f) / (PopSeconds * 0.4f));
            Transform t = s.Root.transform;
            t.position = anchor - facing * BackOffset + Vector3.up * (0.04f + s.Bob.X);
            t.rotation = Quaternion.LookRotation(facing, Vector3.up);
            t.localScale = Vector3.one * Mathf.Max(0.01f, pop) / Mathf.Max(1e-3f, root.lossyScale.x);
            if (_shownFrame != Time.frameCount) { _shownFrame = Time.frameCount; Shown = 0; }
            Shown++;
        }

        /// <summary>Hide the cargo of actors whose model was not posed this frame (pooled models move between actors).</summary>
        private void LateUpdate()
        {
            if (_slots == null) return;
            int f = Time.frameCount;
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot s = _slots[i];
                if (s == null || s.Root == null || !s.Root.activeSelf) continue;
                if (s.Seen < f) s.Root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_slots == null) return;
            foreach (Slot s in _slots) if (s != null && s.Root != null) Destroy(s.Root);
        }

        private static void SetActive(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) g.SetActive(on);
        }

        private Slot Make(int actor)
        {
            var s = new Slot { Root = new GameObject("Cargo " + actor), Biomass = new GameObject[Stack], Corruption = new GameObject[Stack] };
            s.Root.transform.SetParent(transform, false);
            s.Root.SetActive(false);
            for (int k = 0; k < Stack; k++)
            {
                Vector3 off = new Vector3((k - 1) * 0.12f, k * 0.14f, -0.02f * k);
                s.Biomass[k] = Piece(s.Root.transform, "Cargo Biomass " + k, ArtMapping.BiomassS, Palette.Biomass, off);
                s.Corruption[k] = Piece(s.Root.transform, "Cargo Corruption " + k, ArtMapping.CorruptionS, Palette.Corruption, off);
            }
            return s;
        }

        private static GameObject Piece(Transform parent, string name, string key, string hex, Vector3 offset)
        {
            ArtCatalog cat = ArtCatalog.Load();
            ArtEntry e = cat != null ? cat.Find(key) : null;
            ModelSpec spec = e != null ? ArtMapping.Spec(key) : null;
            GameObject g;
            if (e != null && e.Model != null)
            {
                g = Object.Instantiate(e.Model, parent, false);
                foreach (Animator an in g.GetComponentsInChildren<Animator>(true)) Object.Destroy(an);
                Material m = null;
                if (spec != null)
                {
                    try { m = ArtMaterials.ForTeam(spec, TeamId.Azure); }
                    catch (System.InvalidOperationException) { m = null; }
                }
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
                {
                    if (m != null)
                    {
                        Material[] mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) mats[i] = m;
                        r.sharedMaterials = mats;
                    }
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.name = name;
                }
                g.transform.localScale = Vector3.one * PropScale;
            }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Collider c = g.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
                g.transform.SetParent(parent, false);
                g.transform.localScale = Vector3.one * 0.22f;
                Renderer r = g.GetComponent<Renderer>();
                r.sharedMaterial = ToonMaterials.Prop(hex);
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            g.name = name;
            g.transform.localPosition = offset;
            return g;
        }
    }
}
