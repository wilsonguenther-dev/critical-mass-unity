using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
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
