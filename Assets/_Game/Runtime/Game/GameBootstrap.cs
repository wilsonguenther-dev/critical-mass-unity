using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Config;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Builds the whole Phase 0 game in code when the Boot scene loads (Boot.unity stays empty; O-17, U-027): the Game
    /// root (driver, input, views, HUD), the main camera with its Cinemachine brain, the camera rig (F-08b) and one
    /// directional light. Note: UnityEngine.Camera is spelled out because CriticalMass.Runtime.Camera is a namespace.
    /// </summary>
    public static class GameBootstrap
    {
        /// <summary>The scene the game is built into.</summary>
        public const string BootScenePath = "Assets/_Game/Scenes/Boot.unity";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Domain reload is disabled on entering Play Mode (ProjectSetup.ApplyEditorSettings): statics and
            // subscriptions survive between play sessions, so unsubscribe before subscribing.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Build(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Build(scene);
        }

        /// <summary>
        /// When scene is Boot and no SimDriverBehaviour exists, creates the game in it and returns the Game object.
        /// Otherwise creates nothing and returns the existing driver's GameObject (null when there is none).
        /// </summary>
        public static GameObject Build(Scene scene)
        {
            SimDriverBehaviour existing = Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (existing != null) return existing.gameObject;
            if (scene.path != BootScenePath) return null;

            // Game root, components in this order.
            var game = new GameObject("Game");
            PlaceInScene(game, scene);
            SimDriverBehaviour driver = game.AddComponent<SimDriverBehaviour>();
            game.AddComponent<HumanInputSource>();
            game.AddComponent<ActorViews>();
            game.AddComponent<GridChunkMeshView>();
            DebugHud hud = game.AddComponent<DebugHud>();
            hud.Driver = game.GetComponent<SimDriverBehaviour>();

            // Main camera. Cinemachine 3.1.7 copies Lens.ModeOverride to the Unity camera only when the brain's override
            // is enabled (CinemachineBrain.cs lines 755-773); it defaults to disabled Perspective (P-09).
            var camGo = new GameObject("Main Camera");
            PlaceInScene(camGo, scene);
            camGo.tag = "MainCamera";
            UnityEngine.Camera cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex(Palette.VoidNavy);
            cam.orthographic = true;
            CinemachineBrain brain = camGo.AddComponent<CinemachineBrain>();
            brain.LensModeOverride = new CinemachineBrain.LensModeOverrideSettings
            {
                Enabled = true,
                DefaultMode = LensSettings.OverrideModes.Orthographic,
            };

            // Camera rig (F-08b).
            GameObject rig = CameraRig.Create(driver);
            if (rig != null && rig.transform.parent == null) PlaceInScene(rig, scene);

            // One directional light.
            var lightGo = new GameObject("Directional Light");
            PlaceInScene(lightGo, scene);
            lightGo.transform.rotation = Quaternion.Euler(50f, 330f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;

            return game;
        }

        static void PlaceInScene(GameObject go, Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
    }
}
