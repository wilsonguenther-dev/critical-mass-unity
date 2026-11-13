using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// The game side of the character LOD contract (PHYSICS-BUILD-LIST contract C4, DG-82; runtime seat B1). Every character
    /// FBX exports one rig with four meshes, all skinned to the same armature:
    /// <list type="bullet">
    /// <item><c>&lt;Class&gt;_Hero</c>: 15,000 to 25,000 triangles. Close cameras only (class select, story, results,
    /// Barracks, photo mode). Never drawn in a match: <see cref="ModelView.SetHero"/> shows it and hides the LODs.</item>
    /// <item><c>&lt;Class&gt;_LOD0</c>: at most 4,500 triangles (target 4,000), derived from the Hero.</item>
    /// <item><c>&lt;Class&gt;_LOD1</c>: at most 1,500 triangles.</item>
    /// <item><c>&lt;Class&gt;_Shadow</c>: at most 800 triangles (Kaiju 1,500), shadow only: it is the only caster.</item>
    /// </list>
    /// How the game picks (per fighter, per frame, <see cref="AnimLod.PickLod"/>): your own character always LOD0; at Low
    /// everyone else LOD1; at Medium and High LOD0 within 25 u of the camera target, LOD1 beyond. Outline hull on LOD1 at
    /// High only. Shadows: only <c>_Shadow</c> casts (Medium, High); at Low nothing casts and a ground blob is drawn.
    /// <para>Graceful fallback: a model that has none of the suffixed meshes (every model today) draws its current meshes
    /// as LOD0 at every distance, and they cast the shadow themselves (Medium, High). A model with LOD0 but no LOD1 stays
    /// on LOD0. A model with no _Shadow mesh lets LOD0 cast. Weapons and props that are separate objects without a
    /// suffix (Warrior_Cleaver) count as part of every LOD.</para>
    /// <para>The renderers are always switched directly. If the model importer built a LODGroup from the _LOD0/_LOD1
    /// names, ModelView disables it (fix/fighters-always-visible, 2026-11-13): an enabled group culls by screen height on
    /// its own and fought the game's pick, so near fighters drew nothing (FighterVisibilityPlayTests).</para>
    /// </summary>
    public static class FighterLod
    {
        public const string HeroSuffix = "_Hero", Lod0Suffix = "_LOD0", Lod1Suffix = "_LOD1", ShadowSuffix = "_Shadow";
        /// <summary>Triangle budgets of the contract (the art builder's FBX scan and ArtImportTests read these).</summary>
        public const int HeroMinTriangles = 15000, HeroMaxTriangles = 25000, Lod0Triangles = 4500, Lod0TargetTriangles = 4000,
            Lod1Triangles = 1500, ShadowTriangles = 800, KaijuLod0Triangles = 12000, KaijuShadowTriangles = 1500;
        /// <summary>The URP LightMode tag of CMToon's inverted-hull outline pass.</summary>
        public const string OutlinePass = "SRPDefaultUnlit";

        /// <summary>Shared: unsuffixed objects (weapons, today's single meshes) that belong to every in-match LOD.</summary>
        public enum Role { Shared, Hero, Lod0, Lod1, Shadow }

        /// <summary>The role of a renderer by its object name (pure; tests).</summary>
        public static Role RoleOf(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return Role.Shared;
            if (objectName.EndsWith(Lod0Suffix, StringComparison.Ordinal)) return Role.Lod0;
            if (objectName.EndsWith(Lod1Suffix, StringComparison.Ordinal)) return Role.Lod1;
            if (objectName.EndsWith(ShadowSuffix, StringComparison.Ordinal)) return Role.Shadow;
            if (objectName.EndsWith(HeroSuffix, StringComparison.Ordinal)) return Role.Hero;
            return Role.Shared;
        }

        /// <summary>The object name without its LOD suffix (Warrior_LOD1 to Warrior); unsuffixed names come back unchanged.</summary>
        public static string BaseName(string objectName)
        {
            switch (RoleOf(objectName))
            {
                case Role.Lod0: return objectName.Substring(0, objectName.Length - Lod0Suffix.Length);
                case Role.Lod1: return objectName.Substring(0, objectName.Length - Lod1Suffix.Length);
                case Role.Shadow: return objectName.Substring(0, objectName.Length - ShadowSuffix.Length);
                case Role.Hero: return objectName.Substring(0, objectName.Length - HeroSuffix.Length);
                default: return objectName;
            }
        }

        /// <summary>
        /// Whether a renderer draws (pure; tests). hero: a close camera asked for the Hero mesh. lod: 0 or 1 from PickLod.
        /// hasLod1: the model has a _LOD1 mesh (without one LOD0 stays). hasLod0: the model has a _LOD0 mesh.
        /// </summary>
        public static bool Draws(Role role, bool hero, int lod, bool hasLod0, bool hasLod1)
        {
            if (hero) return role == Role.Hero || role == Role.Shared;
            switch (role)
            {
                case Role.Hero: return !hasLod0 && !hasLod1;   // a Hero-only export still shows something in a match
                case Role.Lod0: return lod == 0 || !hasLod1;
                case Role.Lod1: return lod == 1;
                case Role.Shadow: return false;                // the proxy is switched by ShadowMode, never drawn lit
                default: return true;
            }
        }

        /// <summary>The shadow casting mode of a renderer for a role and a quality row (pure; tests).</summary>
        public static ShadowCastingMode ShadowMode(Role role, FighterShadow policy, bool hasProxy)
        {
            if (policy == FighterShadow.Blob) return ShadowCastingMode.Off;
            if (role == Role.Shadow) return ShadowCastingMode.ShadowsOnly;
            if (role == Role.Hero || role == Role.Lod1) return ShadowCastingMode.Off;
            if (policy == FighterShadow.Proxy && hasProxy) return ShadowCastingMode.Off;
            return ShadowCastingMode.On;   // LOD0 and shared parts cast when there is no proxy (or the policy is Mesh)
        }

        /// <summary>Whether the shadow proxy renderer is enabled for a policy (pure).</summary>
        public static bool ProxyEnabled(FighterShadow policy) => policy != FighterShadow.Blob;

        static readonly Dictionary<Material, Material> NoOutline = new Dictionary<Material, Material>();

        /// <summary>A copy of a CMToon material without the outline pass (LOD1 at Low and Medium), cached per source.</summary>
        public static Material WithoutOutline(Material source)
        {
            if (source == null) return null;
            if (NoOutline.TryGetValue(source, out Material m) && m != null) return m;
            m = new Material(source) { name = source.name + " (No Outline)" };
            m.SetShaderPassEnabled(OutlinePass, false);
            NoOutline[source] = m;
            return m;
        }

        /// <summary>No-outline copies made so far (bounded by class models x teams x flash and tint variants).</summary>
        public static int NoOutlineCount => NoOutline.Count;
    }
}
