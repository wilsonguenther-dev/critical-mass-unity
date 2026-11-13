using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Art;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>
    /// Attaches the DG-39 / DG-27 pieces to the game GameBootstrap builds, on boot and after every scene load, without
    /// editing the bootstrap (the pattern of MenuInstaller): MatchRewardTracker, LocalCosmetics, ProgressionHud,
    /// RewardsOverlay and CharacterPanel on the Game object (the one with the SimDriverBehaviour). The game-flow item's
    /// overlay registry can replace this installer by adding the same components.
    /// </summary>
    public static class ProgressionInstaller
    {
        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        /// <summary>Adds the components once; returns the Game object (null: no game in this scene).</summary>
        public static GameObject Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            SimDriverBehaviour driver = Object.FindAnyObjectByType<SimDriverBehaviour>();
            if (driver == null) return null;
            GameObject go = driver.gameObject;
            if (go.GetComponent<MatchRewardTracker>() == null) go.AddComponent<MatchRewardTracker>().Driver = driver;
            if (go.GetComponent<LocalCosmetics>() == null) go.AddComponent<LocalCosmetics>();
            if (go.GetComponent<ProgressionHud>() == null) go.AddComponent<ProgressionHud>();
            if (go.GetComponent<RewardsOverlay>() == null) go.AddComponent<RewardsOverlay>();
            if (go.GetComponent<CharacterPanel>() == null) go.AddComponent<CharacterPanel>();
            return go;
        }
    }
}
