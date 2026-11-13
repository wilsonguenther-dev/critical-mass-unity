using System.Collections;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Dressing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>Technical hardening PlayMode checks: DG-58 (world rebuilds do not leak) and DG-61 (controller unplugged).</summary>
    public sealed class TechHardeningPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";

        static IEnumerator LoadBoot()
        {
            SceneManager.LoadScene(BootScene);
            yield return null;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TenWorldRebuildsEndWithTheSameMeshAndMaterialCounts()
        {
            yield return LoadBoot();
            yield return LoadBoot();   // the first build loads the kit; count from the second
            Resources.UnloadUnusedAssets();
            yield return null;
            int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            for (int i = 0; i < 10; i++) yield return LoadBoot();
            yield return null;
            int meshesAfter = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int materialsAfter = Resources.FindObjectsOfTypeAll<Material>().Length;
            Assert.LessOrEqual(meshesAfter, meshes + 2, $"meshes {meshes} -> {meshesAfter} after 10 rebuilds");
            Assert.LessOrEqual(materialsAfter, materials + 2, $"materials {materials} -> {materialsAfter} after 10 rebuilds");
            WorldView view = Object.FindAnyObjectByType<WorldView>();
            if (view != null) Assert.LessOrEqual(view.BasesParses, 1, "the bases file is parsed once per world build");
        }

        [UnityTest]
        public IEnumerator UnpluggingTheControllerMidMatchOpensThePauseMenu()
        {
            Gamepad pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return LoadBoot();
                TitleScreen title = Object.FindAnyObjectByType<TitleScreen>();
                if (title != null) title.Hide();
                yield return null;
                yield return null;
                PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>();
                Assert.IsNotNull(menu, "the pause menu is attached to the game");
                if (menu.IsOpen) menu.Resume();
                yield return null;
                InputSystem.RemoveDevice(pad);
                pad = null;
                yield return null;
                yield return null;
                Assert.IsTrue(menu.IsOpen, "the pause menu opens the frame after the controller left");
                Assert.AreEqual(SystemEvents.ControllerNotice, SystemEvents.Notice);
                menu.Resume();
            }
            finally
            {
                if (pad != null) InputSystem.RemoveDevice(pad);
            }
        }
    }
}
