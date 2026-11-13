using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Anim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.View.Reactions
{
    /// <summary>
    /// Installs the world-reactions layer on the game object (the pause menu's pattern: AfterSceneLoad, then on every scene
    /// load): ForceField (bending grass, wind, bridge sag, water rings), GroundPaint (marks stamped once), CameraResponse
    /// (the shake replacement), and the contract C3 listeners CharacterSprings and CargoView with PostPoseShim (their
    /// driver until DG-71's ActorViews.PostPose merges). GameBootstrap.Build is idempotent and is called first, as the
    /// pause menu does. Public API for other items: Attach(scene), and each component's own static surface
    /// (WorldForceBus.Active / Push, ForceField.WindDir / WindStrength, CameraResponse.LowFrequency, ReactionBudget).
    /// </summary>
    public static class WorldReactions
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>Adds the reaction components to the game's SimDriverBehaviour object; returns it (null: no game here).</summary>
        public static GameObject Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (driver == null) return null;
            GameObject go = driver.gameObject;
            Ensure<ForceField>(go);
            Ensure<GroundPaint>(go);
            Ensure<CameraResponse>(go);
            Ensure<PostPoseShim>(go);
            Ensure<CharacterSprings>(go);
            Ensure<CargoView>(go);
            return go;
        }

        private static void Ensure<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() == null) go.AddComponent<T>();
        }
    }
}
