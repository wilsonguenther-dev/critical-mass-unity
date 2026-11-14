using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using CriticalMass.Runtime.View.Dressing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CriticalMass.Editor
{
    /// <summary>
    /// One-shot, idempotent project configuration. Run with:
    /// Unity -batchmode -quit -projectPath . -executeMethod CriticalMass.Editor.ProjectSetup.Apply
    /// </summary>
    public static class ProjectSetup
    {
        const string SettingsDir = "Assets/_Game/Settings";
        const string UrpAssetPath = SettingsDir + "/CriticalMass_URP.asset";
        const string UrpRendererPath = SettingsDir + "/CriticalMass_URP_Renderer.asset";
        // DG-50 quality ladder: Very Low and Low use Low, Medium uses Medium, High and above use the original asset.
        const string UrpLowPath = SettingsDir + "/CriticalMass_URP_Low.asset";
        const string UrpMediumPath = SettingsDir + "/CriticalMass_URP_Medium.asset";
        // Physics audit DG-80 (R-m7): Low and Medium draw with plain Forward (one directional light; CMToon has no Forward+
        // variants, so Forward+ only paid for light binning). High keeps the original Forward+ renderer.
        const string UrpForwardRendererPath = SettingsDir + "/CriticalMass_URP_Renderer_Forward.asset";
        /// <summary>DG-80 (R-m3): the audio DSP buffer, "Good latency" (was 1024, about 45 to 90 ms behind the hit flash).</summary>
        public const int DspBufferSize = 512;
        const int DefaultStandaloneQuality = 3;   // High; never Ultra
        const string BootScenePath = "Assets/_Game/Scenes/Boot.unity";
        const string ControlsPath = SettingsDir + "/Controls.inputactions";
        const string PlaceholderDir = "Assets/_Game/Resources/Placeholder";
        const string PlaceholderMaterialPath = PlaceholderDir + "/PlaceholderLit.mat";
        const string DefaultPostProcessDataPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        public static void Apply()
        {
            try
            {
                ApplyPlayerSettings();
                ApplyEditorSettings();
                ApplyRenderPipeline();
                ApplyBootScene();
                ApplyInputActions();
                ApplyPlaceholderMaterial();
                ApplyToonMaterials();
                ApplyWorldMaterials();
                AssetDatabase.SaveAssets();
                Art.ArtCatalogBuilder.Build();
                MoveIntoSettings("Assets/UniversalRenderPipelineGlobalSettings.asset");
                MoveIntoSettings("Assets/DefaultVolumeProfile.asset");
                AssetDatabase.SaveAssets();
                Debug.Log("[CriticalMass.Setup] done");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>The view's compile switches for the merged sim lanes (PR #50 / #59: physics fields, mechanics depth).</summary>
        public static readonly string[] SimDefines = { "CM_SIM_PHYSICS", "CM_SIM_MECHANICS" };

        static void ApplyPlayerSettings()
        {
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.companyName = "Drivia";
            PlayerSettings.productName = "Critical Mass";
            PlayerSettings.SetApplicationIdentifier(standalone, "com.drivia.criticalmass");
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            // DG-52: borderless fullscreen-window by default (a windowed option is in Settings); never exclusive
            // fullscreen and never a display-mode change.
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.forceSingleInstance = true;      // Windows: a second launch focuses the first (macOS: instance.lock)
            // DG-51: keep running when unfocused (FrameGovernor keeps the normal cap; sound stays on).
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            // DG-54: FrameTimingManager CPU and GPU frame times in release players too.
            PlayerSettings.enableFrameTimingStats = true;
            // polish-audit-1: no engine splash. It put the engine vendor's logo on the first screen a player sees (the
            // project rule: no vendor or stack names on player-facing screens) and held the cold launch for its animation
            // (boot to title measured 6.9 s process / 2.5 s engine at a14ce59 against a 5 s budget). Optional on Unity
            // Personal since Unity 6. The game's own intro and title follow at once.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            // DG-80 (R-M1): skinning on the GPU through compute shaders, batched (6000.3 docs: PlayerSettings.meshDeformation,
            // MeshDeformation.GPUBatched, needs a graphics API with compute shaders: Metal and Direct3D 11 both have them).
            if (PlayerSettings.meshDeformation != MeshDeformation.GPUBatched) PlayerSettings.meshDeformation = MeshDeformation.GPUBatched;
            // DG-56: Windows pinned to Direct3D 11 first, 12 as the fallback.
            var win = new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 };
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)) PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            if (!PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64).SequenceEqual(win)) PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, win);
            PlayerSettings.SetApiCompatibilityLevel(standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Integration batch 4: the merged sim (#61) carries the physics and mechanics-depth fields; the view reads them
            // only behind these two defines (MechPhysics, DepthRead). Kept in order, added once, other symbols untouched.
            var defines = PlayerSettings.GetScriptingDefineSymbols(standalone).Split(';').Where(d => d.Length > 0).ToList();
            foreach (string d in SimDefines) if (!defines.Contains(d)) defines.Add(d);
            string joined = string.Join(";", defines);
            if (joined != PlayerSettings.GetScriptingDefineSymbols(standalone)) PlayerSettings.SetScriptingDefineSymbols(standalone, joined);

            // Active Input Handling: 0 = Input Manager (old), 1 = Input System (new), 2 = both.
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")
                .FirstOrDefault(o => o is PlayerSettings);
            if (playerSettings == null) throw new InvalidOperationException("PlayerSettings asset not found");
            var so = new SerializedObject(playerSettings);
            var handler = so.FindProperty("activeInputHandler");
            if (handler == null) throw new InvalidOperationException("activeInputHandler property not found");
            handler.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ApplyEditorSettings()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";
#pragma warning disable 618
            EditorSettings.enterPlayModeOptionsEnabled = true;
#pragma warning restore 618
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        static void ApplyRenderPipeline()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(DefaultPostProcessDataPath);
                AssetDatabase.CreateAsset(renderer, UrpRendererPath);
            }
            renderer.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
            }

            // Art direction 2.2: hard-edged sun shadows, map 2048, one cascade over the view (the perspective view reaches
            // about 40 u from the camera at the top edge).
            asset.mainLightShadowmapResolution = 2048;
            asset.shadowCascadeCount = 1;
            // DG-80 (R-M8): 35 m at High covers the farthest ground the gameplay camera sees (about 31.5 m); it was 60 m on a
            // 112 x 72 map, so the shadow pass drew almost every caster on the map at half the texel density.
            asset.shadowDistance = CriticalMass.Runtime.Game.QualityTable.HighShadowMetres;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;

            // DG-50: three real pipeline assets. Low and Medium start as copies of the High asset (same renderer, new
            // GUIDs) and only the listed fields differ; every write is skipped when the value is already right, so a
            // second Apply leaves git clean.
            var low = EnsureTier(UrpLowPath, so =>
            {
                bool d = false;
                d |= SetInt(so, "m_SupportsHDR", 0);
                d |= SetInt(so, "m_MSAA", 1);                        // 1 = anti-aliasing off
                d |= SetFloat(so, "m_RenderScale", 0.75f);
                // DG-80: no shadow map at Low (blob shadows under fighters instead, FxScene.BlobShadows).
                d |= SetInt(so, "m_MainLightShadowsSupported", 0);
                d |= SetInt(so, "m_MainLightShadowmapResolution", 1024);
                d |= SetInt(so, "m_AdditionalLightShadowsSupported", 0);
                d |= SetInt(so, "m_AdditionalLightsPerObjectLimit", 2);
                d |= SetFloat(so, "m_ShadowDistance", 0f);
                return d;
            });
            var medium = EnsureTier(UrpMediumPath, so =>
            {
                bool d = false;
                d |= SetInt(so, "m_SupportsHDR", 0);
                d |= SetInt(so, "m_MainLightShadowmapResolution", 1024);
                d |= SetInt(so, "m_AdditionalLightShadowsSupported", 0);
                d |= SetFloat(so, "m_ShadowDistance", CriticalMass.Runtime.Game.QualityTable.MediumShadowMetres);   // DG-80: 30 m
                return d;
            });
            UniversalRendererData forward = EnsureForwardRenderer();
            if (forward != null)
            {
                SetFirstRenderer(low, forward);
                SetFirstRenderer(medium, forward);
            }
            ApplyAudioAndPhysics();

            var tiers = new RenderPipelineAsset[] { low, medium, asset };
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                RenderPipelineAsset want = tiers[i <= 1 ? 0 : i == 2 ? 1 : 2];
                if (QualitySettings.renderPipeline != want) QualitySettings.renderPipeline = want;
                if (QualitySettings.vSyncCount != 1) QualitySettings.vSyncCount = 1;   // DG-49: never uncapped, editor and tests too
            }
            QualitySettings.SetQualityLevel(current == 5 || current == 4 ? DefaultStandaloneQuality : current, false);
            SetStandaloneDefaultQuality(DefaultStandaloneQuality);
        }

        /// <summary>DG-80: a copy of the renderer in plain Forward mode for Low and Medium (created once, then only checked).</summary>
        static UniversalRendererData EnsureForwardRenderer()
        {
            var r = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpForwardRendererPath);
            if (r == null)
            {
                if (!AssetDatabase.CopyAsset(UrpRendererPath, UrpForwardRendererPath))
                {
                    Debug.LogWarning("[CriticalMass.Setup] could not copy the renderer for Forward; Low and Medium keep Forward+");
                    return null;
                }
                r = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpForwardRendererPath);
                if (r == null) return null;
            }
            if (r.renderingMode != RenderingMode.Forward)
            {
                r.renderingMode = RenderingMode.Forward;
                EditorUtility.SetDirty(r);
            }
            // perf/fullscreen-cost-1: no forced intermediate colour target (and its full-screen copy to the screen every
            // frame) at native scale; URP still makes one whenever something needs it (render scale, a colour copy).
            if (r.intermediateTextureMode != IntermediateTextureMode.Auto)
            {
                r.intermediateTextureMode = IntermediateTextureMode.Auto;
                EditorUtility.SetDirty(r);
            }
            return r;
        }

        /// <summary>Points a pipeline asset's first renderer at rendererData (skipped when it already does).</summary>
        static void SetFirstRenderer(UniversalRenderPipelineAsset a, ScriptableRendererData rendererData)
        {
            if (a == null || rendererData == null) return;
            var so = new SerializedObject(a);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray || list.arraySize == 0)
            {
                Debug.LogWarning("[CriticalMass.Setup] m_RendererDataList not found on " + a.name);
                return;
            }
            SerializedProperty first = list.GetArrayElementAtIndex(0);
            if (first.objectReferenceValue == rendererData) return;
            first.objectReferenceValue = rendererData;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(a);
        }

        /// <summary>
        /// DG-80: DSP buffer 512 (R-m3) and physics stepped only by script (R-m11: nothing in the game uses Unity physics;
        /// view physics, if added, steps its own local scene). Both through SerializedObject on the project settings files;
        /// each write is skipped when the value is already right, so a second Apply leaves git clean.
        /// </summary>
        static void ApplyAudioAndPhysics()
        {
            WriteSetting("ProjectSettings/AudioManager.asset", "m_DSPBufferSize", DspBufferSize);
            WriteSetting("ProjectSettings/DynamicsManager.asset", "m_SimulationMode", 2);   // SimulationMode.Script
        }

        static void WriteSetting(string path, string property, int value)
        {
            UnityEngine.Object o = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault();
            if (o == null) { Debug.LogWarning("[CriticalMass.Setup] " + path + " not found"); return; }
            var so = new SerializedObject(o);
            SerializedProperty p = so.FindProperty(property);
            if (p == null) { Debug.LogWarning("[CriticalMass.Setup] " + property + " not found in " + path); return; }
            if (p.intValue == value) return;
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(o);   // integration batch 3: AudioManager kept 1024 without this (project settings save only when dirty)
        }

        static UniversalRenderPipelineAsset EnsureTier(string path, Func<SerializedObject, bool> configure)
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (a == null)
            {
                if (!AssetDatabase.CopyAsset(UrpAssetPath, path)) throw new InvalidOperationException("could not copy " + UrpAssetPath + " to " + path);
                a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (a == null) throw new InvalidOperationException(path + " did not import as a URP asset");
            }
            var so = new SerializedObject(a);
            if (configure(so))
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
            }
            return a;
        }

        static bool SetInt(SerializedObject so, string name, int v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("[CriticalMass.Setup] URP field " + name + " not found"); return false; }
            if (p.propertyType == SerializedPropertyType.Boolean)
            {
                if (p.boolValue == (v != 0)) return false;
                p.boolValue = v != 0;
                return true;
            }
            if (p.propertyType == SerializedPropertyType.Enum)
            {
                if (p.intValue == v) return false;
                p.intValue = v;
                return true;
            }
            if (p.intValue == v) return false;
            p.intValue = v;
            return true;
        }

        static bool SetFloat(SerializedObject so, string name, float v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("[CriticalMass.Setup] URP field " + name + " not found"); return false; }
            if (Mathf.Abs(p.floatValue - v) < 1e-5f) return false;
            p.floatValue = v;
            return true;
        }

        // DG-50: first launch defaults to High (the per-platform default lives only in QualitySettings.asset's
        // m_PerPlatformDefaultQuality map; there is no public setter, so it is written through SerializedObject).
        static void SetStandaloneDefaultQuality(int level)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (qs == null) { Debug.LogWarning("[CriticalMass.Setup] QualitySettings asset not found"); return; }
            var so = new SerializedObject(qs);
            SerializedProperty map = so.FindProperty("m_PerPlatformDefaultQuality");
            if (map == null || !map.isArray) { Debug.LogWarning("[CriticalMass.Setup] m_PerPlatformDefaultQuality not found; set Standalone to High by hand"); return; }
            for (int i = 0; i < map.arraySize; i++)
            {
                SerializedProperty e = map.GetArrayElementAtIndex(i);
                SerializedProperty k = e.FindPropertyRelative("first"), v = e.FindPropertyRelative("second");
                if (k == null || v == null || k.stringValue != "Standalone") continue;
                if (v.intValue == level) return;
                v.intValue = level;
                so.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
            Debug.LogWarning("[CriticalMass.Setup] no Standalone entry in m_PerPlatformDefaultQuality");
        }

        // URP auto-creates its global settings and default volume profile at the Assets root; keep them in _Game/Settings.
        static void MoveIntoSettings(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) return;
            string error = AssetDatabase.MoveAsset(path, SettingsDir + "/" + System.IO.Path.GetFileName(path));
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }

        static void ApplyBootScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, BootScenePath))
                    throw new InvalidOperationException("could not save " + BootScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }

        // F-07: the project-wide actions (InputSystem.actions). In the editor the setter's validation hook stores the asset
        // in EditorBuildSettings under com.unity.input.settings.actions, which the player build includes.
        static void ApplyInputActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(ControlsPath);
            if (asset == null) throw new InvalidOperationException("Controls.inputactions not imported");
            UnityEngine.InputSystem.InputSystem.actions = asset;
        }

        // F-07: the lit placeholder material F-08a loads with Resources.Load. URP's defaultMaterial is editor-only (null in
        // a player), so it is copied into a Resources asset here.
        static void ApplyPlaceholderMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath) != null) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources")) AssetDatabase.CreateFolder("Assets/_Game", "Resources");
            if (!AssetDatabase.IsValidFolder(PlaceholderDir)) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Placeholder");
            var material = new Material(GraphicsSettings.currentRenderPipeline.defaultMaterial) { name = "PlaceholderLit" };
            AssetDatabase.CreateAsset(material, PlaceholderMaterialPath);
        }

        // Art direction 2026-11-12: the CM/Toon and CM/ToonGround base materials the views clone (ToonMaterials). Living in
        // Resources keeps both shaders in a player build (the views never Shader.Find at runtime). Idempotent: an existing
        // material keeps its asset (and GUID) and only gets its shader set back.
        const string ToonDir = "Assets/_Game/Resources/Toon";

        static void ApplyToonMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources")) AssetDatabase.CreateFolder("Assets/_Game", "Resources");
            if (!AssetDatabase.IsValidFolder(ToonDir)) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Toon");
            EnsureMaterial(ToonDir + "/CMToon.mat", "CM/Toon");
            EnsureMaterial(ToonDir + "/CMToonGround.mat", "CM/ToonGround");
            // Art import (2026-11-12): the shared material every Blender FBX under Assets/_Game/Art imports with
            // (ArtImportPostprocessor), CM/Toon in vertex-colour mode; the views clone it per team (ArtMaterials).
            EnsureMaterial(ToonDir + "/CMToonArt.mat", "CM/Toon");
            var art = AssetDatabase.LoadAssetAtPath<Material>(ToonDir + "/CMToonArt.mat");
            if (art.GetFloat("_ColorMode") != 1f)
            {
                art.SetFloat("_ColorMode", 1f);
                EditorUtility.SetDirty(art);
            }
        }

        // World art (2026-11-12): one material per WorldLook (Runtime/View/World/WorldKit), made here so the import
        // postprocessor can assign them and a player build keeps the shaders. Idempotent: a value is written only when it
        // differs, so a second Apply leaves git clean. Colours are sRGB hex (Unity converts Color properties to linear).
        static void ApplyWorldMaterials()
        {
            string dir = WorldKit.MaterialAssetDir.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(ToonDir, "World");
            foreach (WorldLook look in WorldKit.AllLooks)
            {
                string path = WorldKit.AssetPath(look);
                bool decal = look == WorldLook.GlowDecal || look == WorldLook.ShadowDecal || look == WorldLook.Pool;
                string shader = look == WorldLook.Ground ? "CM/WorldGround" : decal ? "CM/WorldDecal" : "CM/Toon";
                EnsureMaterial(path, shader);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool dirty = false;
                if (shader == "CM/Toon") dirty |= WorldToonValues(m, look);
                else if (decal)
                {
                    bool glow = look == WorldLook.GlowDecal, pool = look == WorldLook.Pool;
                    dirty |= SetF(m, "_Mode", glow ? 0f : pool ? 2f : 1f);
                    // Pools (art refresh 2): additive soft light, 0.3 at the centre, nothing at the rim.
                    dirty |= SetF(m, "_Strength", glow ? 0.6f : pool ? 0.3f : 1f);
                    dirty |= SetF(m, "_SrcBlend", glow ? 1f : 5f);
                    dirty |= SetF(m, "_DstBlend", glow || pool ? 1f : 10f);
                }
                if (dirty) EditorUtility.SetDirty(m);
            }
        }

        static bool WorldToonValues(Material m, WorldLook look)
        {
            bool d = false;
            d |= SetF(m, "_ColorMode", 1f);
            d |= SetF(m, "_OutlinePx", WorldKit.OutlinePx(look));
            d |= SetF(m, "_OutlineSource", 2f);
            d |= SetF(m, "_RimStrength", 0f);
            d |= SetF(m, "_FogAmount", 1f);
            d |= SetC(m, "_FogColor", "#B4DAEE");
            d |= SetF(m, "_FogTop", -3.5f);
            d |= SetF(m, "_FogBottom", -11f);
            d |= SetF(m, "_ExtrudePx", look == WorldLook.Water ? 11f : 0f);
            bool foliage = look == WorldLook.Foliage, crystal = look == WorldLook.Crystal;
            // Foliage (kit README v3): bands exactly #86BD4E / #5E9A45 / #3F6E36 on the leaf colour, N.L only, cuts 0.30 / 0.02.
            Color leaf = Hex("#86BD4E").linear, mid = Hex("#5E9A45").linear, low = Hex("#3F6E36").linear;
            d |= SetCRaw(m, "_MidRatio", foliage ? new Color(mid.r / leaf.r, mid.g / leaf.g, mid.b / leaf.b, 1f) : Color.white);
            d |= SetCRaw(m, "_ShadowRatio", foliage ? new Color(low.r / leaf.r, low.g / leaf.g, low.b / leaf.b, 1f) : Color.white);
            d |= SetF(m, "_MidMultiplier", foliage || crystal ? 1f : 0.84f);
            d |= SetF(m, "_ShadowMultiplier", foliage ? 1f : crystal ? 0.46f : 0.66f);
            d |= SetF(m, "_ShadowTintAmount", foliage ? 0f : crystal ? 0.25f : 0.12f);
            d |= SetF(m, "_LitMultiplier", crystal ? 1.9f : 1f);
            d |= SetF(m, "_LitThreshold", foliage ? 0.30f : 0.35f);
            d |= SetF(m, "_ShadowThreshold", foliage ? 0.02f : -0.05f);
            d |= SetF(m, "_ReceiveShadows", foliage || crystal ? 0f : 1f);
            d |= SetF(m, "_TopBandLift", foliage || crystal ? 0f : 1f);
            bool bases = look == WorldLook.Base || look == WorldLook.BaseFlat;
            string cloth = bases ? WorldKit.BaseAuthoredCloth : WorldKit.EnvAuthoredCloth;
            string glow = bases ? WorldKit.BaseAuthoredGlow : WorldKit.EnvAuthoredGlow;
            string dark = bases ? WorldKit.BaseAuthoredDark : WorldKit.EnvAuthoredDark;
            d |= SetC(m, "_DefaultCloth", cloth); d |= SetC(m, "_TeamCloth", cloth);
            d |= SetC(m, "_DefaultGlow", glow); d |= SetC(m, "_TeamGlow", glow);
            d |= SetC(m, "_DefaultDark", dark); d |= SetC(m, "_TeamDark", dark);
            return d;
        }

        static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

        static bool SetF(Material m, string n, float v)
        {
            if (!m.HasProperty(n) || Mathf.Abs(m.GetFloat(n) - v) < 1e-6f) return false;
            m.SetFloat(n, v);
            return true;
        }

        static bool SetC(Material m, string n, string hex) => SetCRaw(m, n, Hex(hex));

        static bool SetCRaw(Material m, string n, Color c)
        {
            if (!m.HasProperty(n)) return false;
            Color o = m.GetColor(n);
            if (Mathf.Abs(o.r - c.r) < 1e-5f && Mathf.Abs(o.g - c.g) < 1e-5f && Mathf.Abs(o.b - c.b) < 1e-5f && Mathf.Abs(o.a - c.a) < 1e-5f) return false;
            m.SetColor(n, c);
            return true;
        }

        static void EnsureMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("shader " + shaderName + " not found");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader == shader) return;
                existing.shader = shader;
                EditorUtility.SetDirty(existing);
                return;
            }
            AssetDatabase.CreateAsset(new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) }, path);
        }

        // NET_Standard and the deprecated NET_Standard_2_0 share one value, so ToString() can print the old name.
        static string DescribeApi(ApiCompatibilityLevel level) =>
            level == ApiCompatibilityLevel.NET_Standard ? ".NET Standard 2.1" : level.ToString();

        /// <summary>Logs the active pipeline; exits non-zero if it is not URP.</summary>
        public static void LogPipeline()
        {
            var rp = GraphicsSettings.defaultRenderPipeline;
            string name = rp != null ? rp.name : "<none>";
            string type = rp != null ? rp.GetType().FullName : "<none>";
            string mode = rp is UniversalRenderPipelineAsset urp && urp.rendererDataList.Length > 0 &&
                          urp.rendererDataList[0] is UniversalRendererData data
                ? data.renderingMode.ToString()
                : "<n/a>";
            var levels = string.Join(", ", Enumerable.Range(0, QualitySettings.names.Length).Select(i =>
            {
                var q = QualitySettings.GetRenderPipelineAssetAt(i);
                return $"{QualitySettings.names[i]}={(q != null ? q.name : "<none>")}";
            }));
            Debug.Log($"[CriticalMass.Pipeline] GraphicsSettings.defaultRenderPipeline={name} type={type} " +
                      $"renderingMode={mode} colorSpace={PlayerSettings.colorSpace} " +
                      $"api={DescribeApi(PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone))} " +
                      $"backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)} quality=[{levels}]");
            if (!(rp is UniversalRenderPipelineAsset)) EditorApplication.Exit(1);
        }
    }
}
