using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Replay;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// rough-spots pass 2 (2026-11-23): an Editor session once wrote the owner's profile, logs, both preference stores and a
    /// replay, and the keep-20 replay limit deleted one of his older replays. In the Editor every game file goes to the
    /// sibling "NAME (Editor)" folder, the replay store refuses the player's folder, and settings never touch PlayerPrefs.
    /// </summary>
    public sealed class EditorDataIsolationTests
    {
        [Test]
        public void TheEditorDataRootIsNotThePlayersFolder()
        {
            Assert.IsTrue(GamePaths.InEditor);
            string player = Application.persistentDataPath;
            Assert.AreEqual(player, GamePaths.PlayerRoot);
            Assert.AreNotEqual(Path.GetFullPath(player), Path.GetFullPath(GamePaths.PersistentRoot), "the Editor keeps its own folder");
            Assert.IsFalse(GamePaths.IsInsidePlayerRoot(GamePaths.PersistentRoot), "and it is not inside the player's folder");
            Assert.AreEqual(Path.GetFileName(player.TrimEnd('/')) + GamePaths.EditorSuffix, Path.GetFileName(GamePaths.PersistentRoot));
            Assert.AreEqual(Path.GetDirectoryName(player.TrimEnd('/')), Path.GetDirectoryName(GamePaths.PersistentRoot), "a sibling");
            if (!GamePaths.Isolated) Assert.IsFalse(GamePaths.IsInsidePlayerRoot(GamePaths.DataDir), "DataDir (profile, logs, replays) follows it");
            Assert.IsFalse(GamePaths.IsInsidePlayerRoot(ReplayStore.Folder));
            Assert.IsFalse(GamePaths.IsInsidePlayerRoot(CmPrefs.EditorFile));
        }

        [Test]
        public void APlayerBuildKeepsThePlatformFolder()
        {
            const string p = "/Users/x/Library/Application Support/Drivia/Critical Mass";
            Assert.AreEqual(p, GamePaths.RootFor(p, false));
            Assert.AreEqual("/Users/x/Library/Application Support/Drivia/Critical Mass (Editor)", GamePaths.RootFor(p, true));
            Assert.AreEqual("/Users/x/Library/Application Support/Drivia/Critical Mass (Editor)", GamePaths.EditorRoot(p + "/"));
            Assert.IsTrue(GamePaths.IsInside(p + "/Replays/a.cmreplay", p));
            Assert.IsTrue(GamePaths.IsInside(p, p));
            Assert.IsFalse(GamePaths.IsInside(GamePaths.EditorRoot(p), p), "the sibling is not inside (prefix match on whole names)");
        }

        [Test]
        public void TheReplayStoreRefusesToSaveOrPruneInThePlayersFolder()
        {
            // asks the guard only: never prunes or saves in the real folder (a broken guard must not cost him a replay)
            Assert.IsTrue(ReplayStore.EditorRefuses(Path.Combine(GamePaths.PlayerRoot, ReplayStore.FolderName), "test"));
            Assert.IsTrue(ReplayStore.EditorRefuses(GamePaths.PlayerRoot, "test"));
            Assert.IsFalse(ReplayStore.EditorRefuses(Path.Combine(GamePaths.PersistentRoot, ReplayStore.FolderName), "test"));
            Assert.IsFalse(ReplayStore.EditorRefuses(Path.Combine(Path.GetTempPath(), "cm-replays-test"), "test"));
        }

        [Test]
        public void SettingsInTheEditorNeverReachPlayerPrefs()
        {
            const string key = "cm.test.editorIsolation";
            string tmp = Path.Combine(Path.GetTempPath(), "cm-prefs-test-" + System.Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                CmPrefs.SetFileForTests(tmp);
                bool had = PlayerPrefs.HasKey(key);
                CmPrefs.SetInt(key, 7);
                CmPrefs.SetFloat(key + ".f", 0.25f);
                CmPrefs.SetString(key + ".s", "a\tb c");
                CmPrefs.Save();
                Assert.AreEqual(had, PlayerPrefs.HasKey(key), "PlayerPrefs untouched");
                CmPrefs.SetFileForTests(tmp); // reload from the file
                Assert.AreEqual(7, CmPrefs.GetInt(key, 0));
                Assert.AreEqual(0.25f, CmPrefs.GetFloat(key + ".f", 0f));
                Assert.AreEqual("a\tb c", CmPrefs.GetString(key + ".s", ""));
                Assert.AreEqual(3, CmPrefs.GetInt("cm.test.missing", 3));
                CmPrefs.DeleteKey(key);
                Assert.IsFalse(CmPrefs.HasKey(key));
            }
            finally
            {
                CmPrefs.SetFileForTests(Path.Combine(GamePaths.PersistentRoot, CmPrefs.EditorFileName));
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }
}
