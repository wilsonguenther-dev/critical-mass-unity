using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// Carried cargo (DG-71, W3, A15 carry): a fighter carrying biomass or corruption holds one to three bundles (by
    /// CarryAmount) between its hands, in front of the chest, while the upper-body layer plays the Carry pose (arms in).
    /// The stack bobs on a small vertical spring. Drawn as prop_carried_bundle when the catalog has it, else as a toon box
    /// in the cargo colour, so it shows before the catalog is rebuilt. Each bundle is a child of the carrier's model, so it
    /// is pooled and hidden with it. A post-pose listener (contract C3): it only reads bones.
    /// b13 r3 ART: ore (the Miner's Blacksmith haul) is drawn as 1 to 3 gold ore lumps (BlacksmithOreLump, a heap between
    /// the hands, not a team bundle), else a gold toon box. Lumps and bundles are separate pooled sets, toggled by cargo kind.
    /// </summary>
    public sealed class CarryProp : IPostPoseListener
    {
        public const int MaxStack = 3;
        /// <summary>Bundle scale (the prop is 0.55 u tall) and stack spacing, u.</summary>
        public const float BundleScale = 0.55f, StackStep = 0.24f, ForwardOffset = 0.12f;
        /// <summary>Ore lump heap (lump art 0.5 u tall at BundleScale): side and up offsets per lump, u.</summary>
        static readonly float[] OreSide = { 0f, 0.11f, -0.1f }, OreUp = { 0f, 0.08f, 0.17f };
        const string OreHex = "#FFC83D";
        const float SpringOmega = 18f, SpringZeta = 0.45f, MaxBob = 0.08f;

        sealed class Carrier
        {
            public readonly Transform[] Bundles = new Transform[MaxStack];
            public readonly Renderer[] Renderers = new Renderer[MaxStack];
            public ModelView[] Views = new ModelView[MaxStack];
            public readonly Transform[] Ore = new Transform[MaxStack];
            public float Y, Vy;
            public bool HasY;
            public CarryKind Kind = CarryKind.None;
        }

        readonly Dictionary<ModelView, Carrier> _carriers = new Dictionary<ModelView, Carrier>();
        readonly ArtCatalog _catalog;

        public CarryProp(ArtCatalog catalog) { _catalog = catalog; }

        /// <summary>Bundles currently shown (tests).</summary>
        public int Shown { get; private set; }
        int _shownFrame = -1;

        public void OnPostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            if (s.IsKaiju || mv == null) return;
            if (Time.frameCount != _shownFrame) { _shownFrame = Time.frameCount; Shown = 0; }
            int n = s.Corpse || s.Carry == CarryKind.None ? 0 : Mathf.Clamp(s.CarryAmount, 1, MaxStack);
            if (n == 0 && !_carriers.ContainsKey(mv)) return;
            if (!_carriers.TryGetValue(mv, out Carrier c)) _carriers[mv] = c = new Carrier();
            bool ore = s.Carry == CarryKind.Ore;
            Transform[] set = ore ? c.Ore : c.Bundles, other = ore ? c.Bundles : c.Ore;
            for (int k = 0; k < MaxStack; k++)
            {
                bool on = k < n;
                if (on && set[k] == null) { if (ore) MakeOre(c, k, mv, s.Team); else Make(c, k, mv, s.Carry); }
                if (set[k] != null && set[k].gameObject.activeSelf != on) set[k].gameObject.SetActive(on);
                if (other[k] != null && other[k].gameObject.activeSelf) other[k].gameObject.SetActive(false);
            }
            if (n == 0) { c.HasY = false; return; }
            if (!ore && c.Kind != s.Carry) Recolour(c, s.Carry, s.Team);

            Transform l = mv.Bone("handslot.l") ?? mv.Bone("hand.l"), r = mv.Bone("handslot.r") ?? mv.Bone("hand.r");
            Transform chest = mv.Bone("chest");
            Vector3 fwd = Quaternion.Euler(0f, s.Yaw, 0f) * Vector3.forward;
            Vector3 anchor = l != null && r != null ? (l.position + r.position) * 0.5f
                : (chest != null ? chest.position : mv.Transform.position + Vector3.up * 1.0f) + fwd * 0.35f;
            anchor += fwd * ForwardOffset;
            // Vertical spring: the stack lags the hands a little on steps, starts and landings.
            float dt = (float)System.Math.Min(0.1, s.DeltaSim);
            if (!c.HasY) { c.Y = anchor.y; c.Vy = 0f; c.HasY = true; }
            else if (dt > 0f)
            {
                float a = SpringOmega * SpringOmega * (anchor.y - c.Y) - 2f * SpringZeta * SpringOmega * c.Vy;
                c.Vy += a * dt;
                c.Y += c.Vy * dt;
                c.Y = Mathf.Clamp(c.Y, anchor.y - MaxBob, anchor.y + MaxBob);
            }
            Quaternion rot = Quaternion.Euler(0f, s.Yaw, 0f);
            if (ore)
            {
                // A heap of gold lumps cradled between the hands: the first centred, the next beside and on top of it.
                Vector3 right = rot * Vector3.right;
                for (int k = 0; k < n; k++)
                {
                    Transform b = c.Ore[k];
                    if (b == null) continue;
                    b.position = new Vector3(anchor.x, c.Y - BundleScale * 0.5f * 0.5f + OreUp[k], anchor.z) + right * OreSide[k];
                    b.rotation = rot * Quaternion.Euler(0f, 40f + k * 75f, k * 12f);
                    Shown++;
                }
                return;
            }
            for (int k = 0; k < n; k++)
            {
                Transform b = c.Bundles[k];
                if (b == null) continue;
                // The first bundle hangs between the hands (centred), the next sit on top of it.
                b.position = new Vector3(anchor.x, c.Y - BundleScale * 0.55f * 0.5f + k * StackStep, anchor.z);
                b.rotation = rot * Quaternion.Euler(0f, k * 37f, 0f);
                Shown++;
            }
        }

        void Make(Carrier c, int k, ModelView owner, CarryKind kind)
        {
            ArtEntry e = _catalog != null ? _catalog.Find(ArtMapping.CarriedBundle) : null;
            Transform t;
            if (e != null && ArtMapping.Spec(ArtMapping.CarriedBundle) != null)
            {
                var v = new ModelView(e, owner.Transform, "Carry Bundle " + k);
                c.Views[k] = v;
                t = v.Transform;
                c.Renderers[k] = v.Renderers.Length > 0 ? v.Renderers[0] : null;
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Carry Bundle " + k;
                Collider col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                go.transform.SetParent(owner.Transform, false);
                go.transform.localScale = Vector3.one * 0.6f;
                t = go.transform;
                c.Renderers[k] = go.GetComponent<Renderer>();
            }
            if (c.Views[k] != null) t.localScale = Vector3.one * BundleScale;
            c.Bundles[k] = t;
            c.Kind = CarryKind.None; // force a recolour
        }

        /// <summary>One carried ore lump: BlacksmithOreLump when catalogued, else a gold toon box (it has no team colour).</summary>
        void MakeOre(Carrier c, int k, ModelView owner, TeamId team)
        {
            ArtEntry e = _catalog != null ? _catalog.Find(ArtMapping.BlacksmithOreLump) : null;
            ModelSpec spec = ArtMapping.Spec(ArtMapping.BlacksmithOreLump);
            Transform t;
            if (e != null && spec != null)
            {
                var v = new ModelView(e, owner.Transform, "Carry Ore " + k);
                v.SetMaterial(ArtMaterials.ForTeam(spec, team));
                t = v.Transform;
                t.localScale = Vector3.one * BundleScale;
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Carry Ore " + k;
                Collider col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                go.transform.SetParent(owner.Transform, false);
                go.transform.localScale = Vector3.one * 0.26f;
                go.GetComponent<Renderer>().sharedMaterial = ToonMaterials.Prop(OreHex);
                t = go.transform;
            }
            c.Ore[k] = t;
        }

        void Recolour(Carrier c, CarryKind kind, TeamId team)
        {
            c.Kind = kind;
            for (int k = 0; k < MaxStack; k++)
            {
                if (c.Views[k] != null) c.Views[k].SetMaterial(ArtMaterials.ForTeam(c.Views[k].Spec, team));
                else if (c.Renderers[k] != null)
                    c.Renderers[k].sharedMaterial = ToonMaterials.Prop(kind == CarryKind.Corruption ? Palette.Corruption : Palette.Biomass);
            }
        }
    }
}
