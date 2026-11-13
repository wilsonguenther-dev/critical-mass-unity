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
    /// </summary>
    public sealed class CarryProp : IPostPoseListener
    {
        public const int MaxStack = 3;
        /// <summary>Bundle scale (the prop is 0.55 u tall) and stack spacing, u.</summary>
        public const float BundleScale = 0.55f, StackStep = 0.24f, ForwardOffset = 0.12f;
        const float SpringOmega = 18f, SpringZeta = 0.45f, MaxBob = 0.08f;

        sealed class Carrier
        {
            public readonly Transform[] Bundles = new Transform[MaxStack];
            public readonly Renderer[] Renderers = new Renderer[MaxStack];
            public ModelView[] Views = new ModelView[MaxStack];
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
            for (int k = 0; k < MaxStack; k++)
            {
                bool on = k < n;
                if (on && c.Bundles[k] == null) Make(c, k, mv, s.Carry);
                if (c.Bundles[k] != null && c.Bundles[k].gameObject.activeSelf != on) c.Bundles[k].gameObject.SetActive(on);
            }
            if (n == 0) { c.HasY = false; return; }
            if (c.Kind != s.Carry) Recolour(c, s.Carry, s.Team);

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
