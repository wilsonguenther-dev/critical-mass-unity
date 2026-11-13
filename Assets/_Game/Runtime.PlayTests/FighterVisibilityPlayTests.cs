using System.Collections;
using System.Collections.Generic;
using System.Text;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// fix/fighters-always-visible (owner report 2026-11-13, "I can't see the characters walking. Is that a code
    /// degradation?"): in a running match, at every quality tier and at a small and a large screen, every living fighter in
    /// the camera's view is drawn as a character. Per fighter: its model is active, at least one lit renderer (not the
    /// _Shadow proxy, not shadows-only, not the grey-box capsule) is enabled with a real character mesh (triangles at or above
    /// MinTriangles), its on-screen height is at least MinScreenPx, and no LODGroup on the model can take the decision
    /// away from the game (the cause of the 2026-11-13 defect: the importer's LODGroup picked LOD1 at gameplay distance
    /// while ModelView had LOD0 on, so neither drew). With a GPU (local runs; the CI gate runs -nographics) the camera
    /// renders into a target of that size and each fighter must also have a renderer the camera actually drew (isVisible).
    /// </summary>
    public sealed class FighterVisibilityPlayTests
    {
        /// <summary>The smallest LOD1 is about 1,000 triangles; the shadow proxy is at most 800; a capsule is 832 but is caught by name.</summary>
        public const int MinTriangles = 400;
        /// <summary>A fighter about 2 m tall at the gameplay camera is about 13% of the screen height; 16 px is a floor even at 1280x720.</summary>
        public const float MinScreenPx = 16f;

        static readonly Vector2Int[] Screens = { new Vector2Int(1510, 890), new Vector2Int(1920, 1080), new Vector2Int(3024, 1964) };
        static readonly int[] Tiers = { FramePolicy.LevelLow, FramePolicy.LevelMedium, FramePolicy.LevelHigh };

        SanityWorld _w;
        RenderTexture _rt;
        int _quality;

        [SetUp]
        public void SetUp() => _quality = QualitySettings.GetQualityLevel();

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null) { cam.targetTexture = null; cam.ResetAspect(); }
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); _rt = null; }
            QualitySettings.SetQualityLevel(_quality, true);
            RenderTier.Set(FramePolicy.LevelHigh);
            SanityWorld.Reset();
            _w = null;
        }

        static bool HasGpu => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        [UnityTest]
        public IEnumerator EveryFighterInViewIsDrawnAsACharacterAtEveryTierAndScreen()
        {
            var bad = new List<string>();
            int checkedFighters = 0;
            foreach (int tier in Tiers)
            {
                QualitySettings.SetQualityLevel(tier, true);
                RenderTier.Set(tier);
                _w = new SanityWorld();
                // Autopilot for everyone and 12 s of sim: the crowd spreads from the pad, so near (LOD0) and far (LOD1)
                // fighters are both on screen. The input reader stays on (manualInput false): this test sends no input, and
                // a reader switched off for over 2 s trips HumanControlGuard's lockout watchdog (an error log fails the test).
                yield return _w.Load(autopilot: true, manualInput: false, settleSeconds: 12);
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                Assert.IsNotNull(cam, "no gameplay camera");
                foreach (Vector2Int s in Screens)
                {
                    if (_rt != null) { cam.targetTexture = null; _rt.Release(); Object.Destroy(_rt); }
                    _rt = new RenderTexture(s.x, s.y, 24);
                    cam.targetTexture = _rt;
                    cam.aspect = (float)s.x / s.y;
                    for (int f = 0; f < 4; f++) { _w.Sync(); yield return null; }
                    _w.Sync();
                    if (HasGpu) cam.Render();
                    checkedFighters += CheckFrame(cam, s, tier, bad);
                }
                SanityWorld.Reset();
            }
            Debug.Log($"[FighterVisibility] checked {checkedFighters} fighter views, gpu={HasGpu}, problems={bad.Count}");
            Assert.That(checkedFighters, Is.GreaterThan(Tiers.Length * Screens.Length), "too few fighters in view to judge");
            Assert.IsEmpty(bad, "fighters not drawn as characters:\n" + string.Join("\n", bad.GetRange(0, Mathf.Min(bad.Count, 40))));
        }

        /// <summary>Checks every living fighter in view; returns how many were checked.</summary>
        int CheckFrame(UnityEngine.Camera cam, Vector2Int s, int tier, List<string> bad)
        {
            int n = 0;
            var w = _w.W;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                Vector3 feet = _w.WorldPos(i);
                Vector3 vp = cam.WorldToViewportPoint(feet + Vector3.up);
                if (vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.03f || vp.y > 0.97f) continue;
                n++;
                string where = $"tier {tier} {s.x}x{s.y} actor {i} ({w.Actors[i].Cls})";
                string why = Problem(_w, cam, i, s);
                if (why != null) bad.Add(where + ": " + why);
            }
            return n;
        }

        /// <summary>Why actor i is not drawn as a character in this frame, or null when it is.</summary>
        internal static string Problem(SanityWorld world, UnityEngine.Camera cam, int i, Vector2Int screen)
        {
            if (world.ActorCapsule(i) != null) return "drawn as the grey-box capsule (no model)";
            Transform model = world.ActorModel(i);
            if (model == null) return "no active model";
            if (!model.gameObject.activeInHierarchy) return "model inactive";
            LODGroup lg = model.GetComponentInChildren<LODGroup>(false);
            if (lg != null && lg.enabled) return "an enabled LODGroup on the model can cull the renderer the game chose";
            float bestTris = 0f, bestPx = 0f;
            bool anyVisible = false;
            string seen = "";
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if (FighterLod.RoleOf(r.name) == FighterLod.Role.Shadow) continue;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                int tris = TriangleCount(mesh);
                float px = ScreenHeightPx(cam, r.bounds);
                seen += $" {r.name}(tris {tris}, {px:F0}px, visible {r.isVisible})";
                if (tris < MinTriangles) continue;
                bestTris = Mathf.Max(bestTris, tris);
                bestPx = Mathf.Max(bestPx, px);
                anyVisible |= r.isVisible;
            }
            if (bestTris <= 0f) return "no enabled lit renderer with a character mesh;" + seen;
            if (bestPx < MinScreenPx) return $"only {bestPx:F0} px tall on screen (floor {MinScreenPx});" + seen;
            if (HasGpu && !anyVisible) return "the camera drew none of its character renderers (culled);" + seen;
            return null;
        }

        static int TriangleCount(Mesh mesh)
        {
            long t = 0;
            for (int k = 0; k < mesh.subMeshCount; k++) t += mesh.GetIndexCount(k) / 3;
            return (int)t;
        }

        /// <summary>Height in pixels of a world bounds box projected by the camera (its target's size).</summary>
        static float ScreenHeightPx(UnityEngine.Camera cam, Bounds b)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            Vector3 c = b.center, e = b.extents;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = c + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                lo = Mathf.Min(lo, sp.y);
                hi = Mathf.Max(hi, sp.y);
            }
            return hi > lo ? hi - lo : 0f;
        }

        /// <summary>
        /// The model's own LOD switch survives the pool: a fighter's model is deactivated when it goes back to the pool and
        /// re-activated on respawn; afterwards exactly the renderers the game picked still draw, and no LODGroup decides.
        /// </summary>
        [UnityTest]
        public IEnumerator ALodChosenBeforeThePoolStillDrawsAfterRespawn()
        {
            _w = new SanityWorld();
            yield return _w.Load(autopilot: false, manualInput: true, settleSeconds: 1);
            Transform model = _w.ActorModel(0);
            Assert.IsNotNull(model, "actor 0 has no model");
            model.gameObject.SetActive(false);
            yield return null;
            model.gameObject.SetActive(true);
            _w.Sync();
            yield return null;
            _w.Sync();
            LODGroup lg = model.GetComponentInChildren<LODGroup>(false);
            Assert.IsTrue(lg == null || !lg.enabled, "an enabled LODGroup came back on the reactivated model");
            int lod0 = 0;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && FighterLod.RoleOf(r.name) == FighterLod.Role.Lod0) lod0++;
            ModelSpec spec = ArtMapping.Spec(_w.ActorModelKey(0));
            bool hasChain = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) hasChain |= FighterLod.RoleOf(r.name) == FighterLod.Role.Lod0;
            if (hasChain) Assert.AreEqual(1, lod0, "your own fighter must draw exactly its LOD0 after a respawn (" + spec?.Key + ")");
        }
    }
}
