using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Flow;
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

            long b0 = System.Diagnostics.Stopwatch.GetTimestamp();
            // Game root, components in this order.
            var game = new GameObject("Game");
            PlaceInScene(game, scene);
            // DG-01: the world is created from the flow's request (a new seed per match; the menus' backdrop otherwise).
            SimDriverBehaviour.NextMatch = GameFlow.RequestForBuild();
            SimDriverBehaviour driver = game.AddComponent<SimDriverBehaviour>();
            game.AddComponent<HumanInputSource>();
            game.AddComponent<ActorViews>();
            game.AddComponent<CriticalMass.Runtime.View.Effects.CombatFx>();
            game.AddComponent<GridChunkMeshView>();
            DebugHud hud = game.AddComponent<DebugHud>();
            hud.Driver = game.GetComponent<SimDriverBehaviour>();
            // The debug box stays for the loop's tools but is hidden from players (F1 toggles it); the bible p15 HUD is
            // GameHud, built after the camera below so its Screen Space - Camera canvas finds Camera.main.
            hud.Visible = false;

            // Main camera: perspective (U-005 amendment 2026-11-12). The P-09 orthographic brain override is gone: the
            // brain's LensModeOverride stays at its default (disabled), so the Unity camera keeps its own perspective mode
            // and the CinemachineCamera lens (ModeOverride None, FOV 35) drives it.
            var camGo = new GameObject("Main Camera");
            PlaceInScene(camGo, scene);
            camGo.tag = "MainCamera";
            UnityEngine.Camera cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex(Palette.SkyTop);
            cam.orthographic = false;
            cam.fieldOfView = (float)GameplayCamera.FovDeg;
            cam.nearClipPlane = GameplayCamera.NearClip;
            cam.farClipPlane = GameplayCamera.FarClip;
            camGo.AddComponent<CinemachineBrain>();

            // Camera rig (F-08b).
            GameObject rig = CameraRig.Create(driver);
            if (rig != null && rig.transform.parent == null) PlaceInScene(rig, scene);

            // Bible p15 HUD (2026-11-12) and, in player builds, the p04 title screen over the live match.
            long b1 = System.Diagnostics.Stopwatch.GetTimestamp();
            GameHud gameHud = game.AddComponent<GameHud>();
            gameHud.Driver = driver;
            // DG-01 game flow (title, menu, setup, results; the title only in player builds or when forced) and every
            // [GameOverlay] component registered by later items.
            long b2 = System.Diagnostics.Stopwatch.GetTimestamp();
            FlowDirector.Install(game, driver, gameHud);
            long b3 = System.Diagnostics.Stopwatch.GetTimestamp();
            int overlays = OverlayRegistry.InstallOverlays(game);

            // One directional sun (art direction 2.2): from the screen's upper-left corner, elevation 50 degrees, so it
            // travels 135 degrees clockwise from screen-up (Unity yaw 315 + 135 = 90) and shadows fall to the lower right.
            // Hard shadows (CM/Toon puts a shadowed pixel on the receiving surface's shadow band, never black).
            var lightGo = new GameObject("Directional Light");
            PlaceInScene(lightGo, scene);
            lightGo.transform.rotation = SunRotation(driver.Config);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = Color.white;
            light.shadows = LightShadows.Hard;

            // perf-title-start: the boot build runs inside the scene activation frame; name its parts in the log.
            long b4 = System.Diagnostics.Stopwatch.GetTimestamp();
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Debug.Log($"[CM.Load] boot build {(b4 - b0) * f:0} ms: world and views {(b1 - b0) * f:0}, hud {(b2 - b1) * f:0}, " +
                      $"flow {(b3 - b2) * f:0}, {overlays} overlays {OverlayRegistry.LastInstallMs:0} " +
                      $"(slowest {OverlayRegistry.Slowest} {OverlayRegistry.SlowestMs:0})");
            return game;
        }

        /// <summary>Sun elevation (degrees) and its travel direction clockwise from screen-up (art direction 2.2).</summary>
        public const float SunElevationDeg = 50f;
        public const float SunAzimuthFromScreenUpDeg = 135f;

        /// <summary>The sun's rotation: Euler(50, 360 - yaw + 135, 0) = (50, 90, 0) at yaw 45.</summary>
        public static Quaternion SunRotation(GameConfig cfg) =>
            Quaternion.Euler(SunElevationDeg, (float)(360 - cfg.Render.CameraYawDeg) + SunAzimuthFromScreenUpDeg, 0f);

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
