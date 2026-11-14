using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// feat/light-weapons-2: view-side reads of the light weapons (Sim LightWeapons, DepthConfig.LightWeapons). The swing
    /// trail, the swing effect and the push-back dust use these so a light weapon draws its own short arc. No numbers here.
    /// </summary>
    public static class LightWeaponView
    {
        static LightWeapon On(GameConfig c, ClassId cls) => c != null && LightWeapons.On(c) ? LightWeapons.Row(c, cls) : null;

        /// <summary>The arc a swing of cls draws (degrees): the light weapon's when the rule is on, else the class row's.</summary>
        public static double ArcDeg(GameConfig c, ClassId cls)
        {
            LightWeapon lw = On(c, cls);
            return lw != null ? lw.ArcDeg : c.Class(cls).AttackArcDeg;
        }

        /// <summary>The reach a swing of cls draws (u).</summary>
        public static double Range(GameConfig c, ClassId cls)
        {
            LightWeapon lw = On(c, cls);
            return lw != null ? lw.Range : c.Class(cls).AttackRange;
        }

        /// <summary>True when a hit by an attacker of attCls shoves (so the hit shows the push-back dust).</summary>
        public static bool Shoves(GameConfig c, bool hasAttacker, ClassId attCls)
        {
            if (!hasAttacker) return false;
            LightWeapon lw = On(c, attCls);
            return lw != null && lw.Shove > 0;
        }
    }

    /// <summary>What a class model holds in its weapon hand.</summary>
    public enum HeldWeapon : byte { None, Mallet, Lantern, PryBarStandIn }

    /// <summary>
    /// feat/light-weapons-2: the three light weapons SEEN in the hand. A post-pose listener (contract C3) that, once per
    /// model, parents a prop to the weapon hand slot ("handslot.r", the KayKit weapon socket; "hand.r" as a fallback), so
    /// the prop follows the hand at rest and through the shared Attack swing with no per-frame work afterwards.
    /// <para>What is attached (Plan): a model whose spec says HeldWeaponInMesh already carries its weapon (today's Builder
    /// with his mallet on handslot.r, today's Healer with her lantern in the left hand) and gets nothing, so nothing is
    /// doubled. A new Builder model gets the MALLET, a new Healer model the LANTERN (the authored Healer_Lantern_Prop from
    /// HealerProps.fbx, on the team material, so its team codes swap like the body), and every Demolitions model the
    /// STAND-IN PRY BAR.</para>
    /// <para>STAND-IN: no pry-bar model exists. The Demolitions Expert borrows the mallet shape tinted dark iron until one is
    /// modelled; replace MakeStandInPryBar when it lands. The mallet shape is one small mesh built once from Unity's
    /// primitive meshes (a handle and a head, under 100 triangles) and shared by every holder; colours are neutral wood and
    /// iron (team-safe: never a team hue), one cached toon material each, one draw call per prop.</para>
    /// </summary>
    public sealed class WeaponProps : IPostPoseListener
    {
        public const string HandBone = "handslot.r", HandBoneFallback = "hand.r";
        /// <summary>World sizes (u): the mallet and stand-in are this long; the lantern prop is fitted to this height.</summary>
        public const float MalletLength = 0.62f, LanternHeight = 0.42f;
        /// <summary>Grip: the handle runs along the socket's +Y (the KayKit weapon convention), held this far from its end.</summary>
        public static readonly Vector3 GripEuler = Vector3.zero;
        public const float GripFromEnd = 0.12f;
        /// <summary>Team-safe neutrals: weathered wood (mallet) and dark iron (stand-in pry bar).</summary>
        public const string MalletWoodHex = "#8A5E3B", PryBarIronHex = "#3B4048";
        public const string PropName = "Light Weapon";

        readonly ArtCatalog _catalog;
        readonly Dictionary<ModelView, Transform> _held = new Dictionary<ModelView, Transform>();
        static Mesh _malletMesh;

        public WeaponProps(ArtCatalog catalog) { _catalog = catalog; }

        /// <summary>Props attached so far (tests, capture log).</summary>
        public int Attached { get; private set; }

        /// <summary>The weapon a model of class cls with this spec needs attached (None: it has its own, or no weapon).</summary>
        public static HeldWeapon Plan(ClassId cls, ModelSpec spec)
        {
            bool own = spec != null && spec.HeldWeaponInMesh;
            switch (cls)
            {
                case ClassId.Builder: return own ? HeldWeapon.None : HeldWeapon.Mallet;
                case ClassId.Healer: return own ? HeldWeapon.None : HeldWeapon.Lantern;
                case ClassId.Demolitions: return own ? HeldWeapon.None : HeldWeapon.PryBarStandIn;
                default: return HeldWeapon.None;
            }
        }

        public void OnPostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            if (s.IsKaiju || mv == null || _held.ContainsKey(mv)) return;
            HeldWeapon kind = Plan(s.Cls, mv.Spec);
            Transform hand = kind == HeldWeapon.None ? null : mv.Bone(HandBone) ?? mv.Bone(HandBoneFallback);
            Transform prop = hand != null ? Attach(hand, kind, s.Team, _catalog) : null;
            _held[mv] = prop; // once per model, null included: no lookups on later frames
            if (prop != null) Attached++;
        }

        /// <summary>Parent the prop for kind to hand at the grip, sized in world units. Null for None.</summary>
        public static Transform Attach(Transform hand, HeldWeapon kind, TeamId team, ArtCatalog catalog)
        {
            if (hand == null || kind == HeldWeapon.None) return null;
            GameObject go = kind == HeldWeapon.Lantern ? MakeLantern(catalog, team) : kind == HeldWeapon.PryBarStandIn ? MakeStandInPryBar() : MakeMallet();
            Transform t = go.transform;
            t.SetParent(hand, false);
            t.localRotation = Quaternion.Euler(GripEuler);
            // World size regardless of the rig's bone scale (the prop then scales with the character's own squash).
            Vector3 ls = hand.lossyScale;
            float k = 1f / Mathf.Max(1e-4f, Mathf.Abs(ls.y));
            t.localScale = Vector3.one * k;
            t.localPosition = kind == HeldWeapon.Lantern ? Vector3.zero : Vector3.down * (GripFromEnd * k);
            return t;
        }

        /// <summary>The shared mallet-shape mesh: handle along +Y from 0 to MalletLength, head across the top (built once).</summary>
        public static Mesh MalletMesh()
        {
            if (_malletMesh != null) return _malletMesh;
            Mesh cyl = PrimitiveMesh(PrimitiveType.Cylinder), cube = PrimitiveMesh(PrimitiveType.Cube);
            float L = MalletLength;
            var parts = new CombineInstance[2];
            // Unity's cylinder is 2 tall and 1 wide, centred: a 0.045 wide handle from 0 to 0.8 L.
            parts[0] = new CombineInstance { mesh = cyl, transform = Matrix4x4.TRS(new Vector3(0f, L * 0.4f, 0f), Quaternion.identity, new Vector3(0.045f, L * 0.4f, 0.045f)) };
            // The head: a 0.26 x 0.14 x 0.14 block across the handle's top.
            parts[1] = new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(0f, L * 0.88f, 0f), Quaternion.identity, new Vector3(0.14f, 0.14f, 0.26f)) };
            var m = new Mesh { name = "LightWeapon_MalletShape" };
            m.CombineMeshes(parts, true, true);
            m.RecalculateBounds();
            _malletMesh = m;
            return m;
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject tmp = GameObject.CreatePrimitive(type);
            Mesh mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(tmp); else Object.DestroyImmediate(tmp);
            return mesh;
        }

        static GameObject MeshObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static GameObject MakeMallet() => MeshObject(PropName + " (Mallet)", MalletMesh(), ToonMaterials.Prop(MalletWoodHex, false));

        /// <summary>STAND-IN until a pry-bar model exists: the mallet shape in dark iron.</summary>
        static GameObject MakeStandInPryBar() => MeshObject(PropName + " (Pry Bar STAND-IN)", MalletMesh(), ToonMaterials.Prop(PryBarIronHex, false));

        /// <summary>The authored Healer_Lantern_Prop on the team material, hung from the hand; a small brass block if the catalog lacks it.</summary>
        static GameObject MakeLantern(ArtCatalog catalog, TeamId team)
        {
            ArtEntry e = catalog != null ? catalog.Find(ArtMapping.HealerProps) : null;
            Mesh mesh = null;
            if (e != null)
            {
                foreach (MeshFilter f in e.Model.GetComponentsInChildren<MeshFilter>(true))
                    if (f.name == ArtMapping.HealerLantern) { mesh = f.sharedMesh; break; }
                if (mesh == null)
                    foreach (SkinnedMeshRenderer f in e.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (f.name == ArtMapping.HealerLantern) { mesh = f.sharedMesh; break; }
            }
            var holder = new GameObject(PropName + " (Lantern)");
            if (mesh == null)
            {
                // Catalog without the authored prop: a small brass block hung from the hand (the holder keeps Attach's scale).
                GameObject box = MeshObject(PropName + " (Lantern fallback)", PrimitiveMesh(PrimitiveType.Cube), ToonMaterials.Prop(Palette.Brass));
                float b0 = LanternHeight * 0.6f;
                box.transform.SetParent(holder.transform, false);
                box.transform.localScale = Vector3.one * b0;
                box.transform.localPosition = Vector3.down * (b0 * 0.5f);
                return holder;
            }
            ModelSpec spec = ArtMapping.Spec(ArtMapping.HealerProps);
            GameObject inner = MeshObject(ArtMapping.HealerLantern, mesh, ArtMaterials.ForTeam(spec, team));
            // Fit the authored prop to LanternHeight and hang it so its top (the ring) sits at the grip.
            Bounds b = mesh.bounds;
            float s = b.size.y > 1e-4f ? LanternHeight / b.size.y : 1f;
            inner.transform.SetParent(holder.transform, false);
            inner.transform.localScale = Vector3.one * s;
            inner.transform.localPosition = new Vector3(-b.center.x * s, -b.max.y * s, -b.center.z * s);
            return holder;
        }
    }
}
