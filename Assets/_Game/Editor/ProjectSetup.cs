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

        static void ApplyPlayerSettings()
        {
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.companyName = "Drivia";
            PlayerSettings.productName = "Critical Mass";
            PlayerSettings.SetApplicationIdentifier(standalone, "com.drivia.criticalmass");
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.SetApiCompatibilityLevel(standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.colorSpace = ColorSpace.Linear;

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
            asset.shadowDistance = 60f;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;

            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
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
