using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>Technical hardening (DG-48 to DG-66, 2026-11-12 panel): one test per acceptance check a test can express.</summary>
    public sealed class TechHardeningTests
    {
        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "cm-hardening-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        // ── DG-49 one owner for frame settings ──

        [Test]
        public void OnlyFrameGovernorWritesVsyncOrTargetFrameRate()
        {
            var re = new Regex(@"vSyncCount\s*=[^=]|targetFrameRate\s*=[^=]|maxQueuedFrames\s*=[^=]");
            string root = Path.Combine(Application.dataPath, "_Game", "Runtime");
            var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != "FrameGovernor.cs" && re.IsMatch(File.ReadAllText(f)))
                .Select(f => f.Substring(root.Length + 1)).ToArray();
            CollectionAssert.IsEmpty(offenders, "only FrameGovernor.cs may write vSyncCount, targetFrameRate or maxQueuedFrames");
        }

        [Test]
        public void EveryQualityLevelKeepsVsyncOn()
        {
            string q = File.ReadAllText(Path.Combine(ProjectRoot, "ProjectSettings", "QualitySettings.asset"));
            Assert.AreEqual(0, Regex.Matches(q, @"vSyncCount: 0").Count);
        }

        [Test]
        public void TwoMillisecondFramesAtCapSixtyEndOnATimedCapOfSixty()
        {
            var g = new TimedCapGuard();
            for (int i = 0; i < 1500; i++) g.Feed(0.002, 60, true);   // 3 s of 500 fps: vsync is not holding
            Assert.IsTrue(g.ForceTimed);
            Assert.AreEqual((0, 60), g.Settings(60, 120));
            var ok = new TimedCapGuard();
            for (int i = 0; i < 300; i++) ok.Feed(1.0 / 60, 60, true);
            Assert.IsFalse(ok.ForceTimed, "a display that holds the cap keeps vsync");
        }

        [Test]
        public void BatteryCapsMenusAtThirtyAndMatchesAtSixty()
        {
            Assert.AreEqual(30, FramePolicy.EffectiveCap(60, true, true));
            Assert.AreEqual(30, FramePolicy.EffectiveCap(0, true, true));
            Assert.AreEqual(60, FramePolicy.EffectiveCap(120, false, true));
            Assert.AreEqual(30, FramePolicy.EffectiveCap(30, false, true));
            Assert.AreEqual(120, FramePolicy.EffectiveCap(120, false, false));
            Assert.AreEqual(30, FramePolicy.TitleCapFor(60), "title idle cap");
            Assert.AreEqual(30, FramePolicy.TitleCapFor(0));
        }

        [Test]
        public void BackgroundKeepsTheCapUnlessSavingPower()
        {
            Assert.AreEqual(60, FramePolicy.BackgroundCap(60, false));
            Assert.AreEqual(30, FramePolicy.BackgroundCap(30, false), "never below 30");
            Assert.AreEqual(15, FramePolicy.BackgroundCap(60, true));
            PlayerPrefs.DeleteKey(GameSettings.BgThrottleKey);
            Assert.IsFalse(GameSettings.BackgroundThrottle, "save power in background is off by default (DG-51)");
        }

        [Test]
        public void NoVsyncIsNeverUnlimited()
        {
            Assert.AreEqual(240, FramePolicy.NoVsyncTarget);
            Assert.Greater(FrameGovernor.NoVsyncTarget, 0);
        }

        // ── DG-50 quality ladder and pixel budget ──

        [Test]
        public void FirstLaunchPicksForTheFloorMachines()
        {
            var m1 = FramePolicy.PickQuality("Apple M1", 0, 8192, 6, 3);
            Assert.LessOrEqual(m1.level, FramePolicy.LevelMedium);
            Assert.LessOrEqual(m1.scale, 0.75f);
            var m2 = FramePolicy.PickQuality("Apple M2", 0, 16384, 6, 3);
            Assert.AreEqual(FramePolicy.LevelMedium, m2.level, "base M2 is mid tier even with 16 GB");
            Assert.AreEqual(FramePolicy.LevelLow, FramePolicy.PickQuality("Intel(R) UHD Graphics 620", 1536, 7987, 6, 3).level);
            Assert.AreEqual(FramePolicy.LevelHigh, FramePolicy.PickQuality("Apple M4 Pro", 18432, 24576, 6, 5).level, "never Ultra");
            Assert.AreEqual(FramePolicy.LevelHigh, FramePolicy.PickQuality("Apple M1 Pro", 0, 16384, 6, 3).level);
        }

        [Test]
        public void A4kScreenRendersAtMostTwoPointOneMegapixels()
        {
            float s = FramePolicy.RenderScaleFor(1f, FramePolicy.DefaultPixelBudget, 3840, 2160);
            Assert.LessOrEqual(3840.0 * s * 2160.0 * s, 2_100_000);
            float r = FramePolicy.RenderScaleFor(1f, FramePolicy.DefaultPixelBudget, 3024, 1898);
            Assert.LessOrEqual(3024.0 * r * 1898.0 * r, 2_100_000, "Retina laptop panel");
            Assert.AreEqual(1f, FramePolicy.RenderScaleFor(1f, FramePolicy.DefaultPixelBudget, 1920, 1080), "1080p is under budget");
            Assert.AreEqual(0.75f, FramePolicy.RenderScaleFor(0.75f, FramePolicy.DefaultPixelBudget, 1280, 720), "the chosen scale wins when smaller");
            Assert.LessOrEqual(3840.0 * 2160.0 * Math.Pow(FramePolicy.RenderScaleFor(1f, FramePolicy.PixelBudget(3), 3840, 2160), 2), 3_700_000);
        }

        [Test]
        public void LevelsMapToThreePipelineTiersAndQueuedFrames()
        {
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 2, 2 }, Enumerable.Range(0, 6).Select(FramePolicy.PipelineTier).ToArray());
            Assert.AreEqual(1, FramePolicy.MaxQueuedFrames(1, false));
            Assert.AreEqual(1, FramePolicy.MaxQueuedFrames(3, true));
            Assert.AreEqual(2, FramePolicy.MaxQueuedFrames(3, false));
            Assert.AreEqual((false, false), RenderTier.Policy(1));
            Assert.AreEqual((true, false), RenderTier.Policy(2));
            Assert.AreEqual((true, true), RenderTier.Policy(3));
        }

        /// <summary>Holds once the integrator ran ProjectSetup.Apply and committed the generated assets (DG-50).</summary>
        [Test]
        public void ThreeDifferentPipelineAssetsAcrossTheLevelsAndLowIsCheap()
        {
            string q = File.ReadAllText(Path.Combine(ProjectRoot, "ProjectSettings", "QualitySettings.asset"));
            var guids = Regex.Matches(q, @"customRenderPipeline: \{fileID: \d+, guid: (\w+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
            Assert.GreaterOrEqual(guids.Length, 3, "run ProjectSetup.Apply: Low, Medium and High assets");
            string low = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Settings", "CriticalMass_URP_Low.asset"));
            StringAssert.Contains("m_SupportsHDR: 0", low);
            int res = int.Parse(Regex.Match(low, @"m_MainLightShadowmapResolution: (\d+)").Groups[1].Value);
            Assert.LessOrEqual(res, 1024);
            StringAssert.Contains("Standalone: 3", q, "first launch defaults to High");
        }

        [Test]
        public void PlayerSettingsKeepRunningBorderlessAndSingleInstance()
        {
            string p = File.ReadAllText(Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset"));
            StringAssert.Contains("runInBackground: 1", p);
            Assert.IsTrue(Regex.IsMatch(p, @"(?m)^  fullscreenMode: 1$"), "borderless fullscreen-window (run ProjectSetup.Apply)");
            StringAssert.Contains("forceSingleInstance: 1", p);
            string editor = Path.Combine(Application.dataPath, "_Game", "Editor");
            var hits = Directory.GetFiles(editor, "*.cs", SearchOption.AllDirectories)
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"ExclusiveFullScreen|FullScreenMode\.Windowed")).ToArray();
            CollectionAssert.IsEmpty(hits);
        }

        // ── DG-52 windows and displays ──

        [Test]
        public void TenFullscreenTogglesInOneSecondRunOnce()
        {
            var d = new ToggleDebouncer();
            int ran = 0;
            for (int i = 0; i < 10; i++)
                if (d.TryFire(100.0 + i * 0.1)) ran++;
            Assert.AreEqual(1, ran);
            Assert.IsTrue(d.TryFire(101.05));
        }

        [Test]
        public void ASavedHugeWindowOpensNoLargerThanTheDisplay()
        {
            Vector2Int r = GameSettings.ClampedResolution(new Vector2Int(10000, 8000), 1920, 1080);
            Assert.LessOrEqual(r.x, 1920);
            Assert.LessOrEqual(r.y, 1080);
            Assert.AreEqual(new Vector2Int(0, 0), GameSettings.ClampedResolution(Vector2Int.zero, 1920, 1080));
        }

        // ── DG-53 saves ──

        [Test]
        public void AFaultAfterEveryWriteStepReadsOldOrNewNeverNothing()
        {
            string dir = TempDir();
            try
            {
                foreach (string step in new[] { "tmp-written", "before-replace", "after-replace" })
                {
                    string p = Path.Combine(dir, step + ".json");
                    SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "old\n"), true);
                    SafeFile.FaultHook = s => { if (s == step) throw new IOException("power cut at " + s); };
                    try { SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "new\n"), true); }
                    catch (IOException) { }
                    finally { SafeFile.FaultHook = null; }
                    string got = SafeFile.ReadVersioned(p, 1, out _);
                    Assert.IsTrue(got == "old\n" || got == "new\n", $"after a fault at {step}: '{got}'");
                }
            }
            finally
            {
                SafeFile.FaultHook = null;
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ATruncatedFileReadsBackFromTheBackupAndHugeFilesAreRejected()
        {
            string dir = TempDir();
            try
            {
                string p = Path.Combine(dir, "machine-profile.json");
                SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "level=3\n"), true);
                SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "level=2\n"), true);
                File.WriteAllBytes(p, new byte[0]);
                Assert.AreEqual("level=3\n", SafeFile.ReadVersioned(p, 1, out _));
                File.WriteAllText(p, SafeFile.Versioned(1, new string('x', SafeFile.MaxReadBytes + 10)));
                File.Delete(p + ".bak");
                Assert.IsNull(SafeFile.ReadVersioned(p, 1, out string why));
                StringAssert.Contains("64 KB", why);
                // a crash between the flush and the first rename: only the .tmp exists; boot promotes it
                string q = Path.Combine(dir, "first.json");
                File.WriteAllText(q + ".tmp", SafeFile.Versioned(1, "fresh\n"));
                SafeFile.CleanStale(dir);
                Assert.AreEqual("fresh\n", SafeFile.ReadVersioned(q, 1, out _));
                Assert.IsFalse(File.Exists(q + ".tmp"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void AHeldFileStaysReadableAndRetriesAreLogged()
        {
            string dir = TempDir();
            try
            {
                string p = Path.Combine(dir, "held.json");
                SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "old\n"), true);
                int before = SafeFile.RetryCount;
                bool threw = false;
                LogAssert.ignoreFailingMessages = true;
                using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    try { SafeFile.WriteAtomic(p, SafeFile.Versioned(1, "new\n"), true); }
                    catch (Exception) { threw = true; }
                }
                string got = SafeFile.ReadVersioned(p, 1, out _);
                Assert.IsTrue(got == "old\n" || got == "new\n", got);
                if (threw) Assert.GreaterOrEqual(SafeFile.RetryCount - before, SafeFile.Attempts - 1, "the retry was logged");
                Assert.IsFalse(SafeFile.TryWriteAtomic(Path.Combine(dir, "missing-folder", "x.json"), "x", false, out string err));
                Assert.IsNotNull(err);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void AnInvalidBindingsKeyDoesNotThrow()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.AddActionMap("Test").AddAction("Fire", binding: "<Keyboard>/space");
            string saved = PlayerPrefs.GetString(ControlBindings.PrefsKey, null);
            LogAssert.ignoreFailingMessages = true;
            try
            {
                PlayerPrefs.SetString(ControlBindings.PrefsKey, "{ this is not json ]");
                Assert.DoesNotThrow(() => ControlBindings.Restore(asset));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                if (saved != null) PlayerPrefs.SetString(ControlBindings.PrefsKey, saved);
                else PlayerPrefs.DeleteKey(ControlBindings.PrefsKey);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AHundredVolumeChangesSaveNothingUntilThePageCloses()
        {
            float keep = GameSettings.MasterVolume;
            GameSettings.Flush();
            int saves = GameSettings.SaveCount;
            for (int i = 0; i < 100; i++) GameSettings.MasterVolume = i / 100f;
            Assert.AreEqual(saves, GameSettings.SaveCount, "no disk write per slider tick");
            Assert.IsTrue(GameSettings.Dirty);
            GameSettings.Flush();
            Assert.AreEqual(saves + 1, GameSettings.SaveCount);
            GameSettings.MasterVolume = keep;
            GameSettings.Flush();
        }

        [Test]
        public void SafeFileHasNoDeleteBeforeRenameInTheGovernor()
        {
            string gov = File.ReadAllText(Path.Combine(Application.dataPath, "_Game", "Runtime", "Game", "FrameGovernor.cs"));
            StringAssert.DoesNotContain("File.Delete(path)", gov);
        }

        // ── DG-48 hang record, DG-62 safe mode ──

        [Test]
        public void HangRecordHoldsTheLastFramesInOrder()
        {
            var ring = new float[] { 10, 20, 30, 40 };
            string t = SessionKeeper.HangText(6.2, false, ring, 1, 4, 1234, "match");
            StringAssert.Contains("stalled_s=6.2", t);
            StringAssert.Contains("tick=1234", t);
            StringAssert.Contains("last_frames_ms=20.0,30.0,40.0,10.0", t);
        }

        [Test]
        public void SafeModeOnlyAfterAStartupCrashOrTwoUncleanRunsInARow()
        {
            Assert.IsFalse(FramePolicy.SafeModeDecision(false, true, 600, 1).safe, "a force quit after 10 minutes");
            Assert.IsTrue(FramePolicy.SafeModeDecision(false, true, 20, 1).safe, "a crash at startup");
            Assert.IsTrue(FramePolicy.SafeModeDecision(false, true, 600, 2).safe, "two unclean runs in a row");
            Assert.IsFalse(FramePolicy.SafeModeDecision(false, false, -1, 0).safe);
            Assert.IsTrue(FramePolicy.SafeModeDecision(true, false, -1, 0).safe);
        }

        [Test]
        public void ThermalParserFlagsSeriousStates()
        {
            Assert.AreEqual(0, FramePolicy.ParsePmsetTherm("Note: No thermal warning level has been recorded\nNote: No performance warning level has been recorded\n"));
            Assert.AreEqual(2, FramePolicy.ParsePmsetTherm("CPU_Scheduler_Limit \t= 100\nCPU_Available_CPUs \t= 8\nCPU_Speed_Limit \t= 62\n"));
            Assert.AreEqual(0, FramePolicy.ParsePmsetTherm("CPU_Speed_Limit = 100\n"));
        }

        // ── DG-60 catch-up cap ──

        [Test]
        public void OneSecondFramesRunAtMostFourTicksAndTheWorldMatchesAFreshOne()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            double dt = SimStep.Dt(cfg);
            var clock = new SimClock();
            World a = AutoplayArgs.RunSimMatch(4242, 0);
            InputFrame scratch = SimInput.EmptyInput();
            int total = 0;
            for (int f = 0; f < 10; f++)
            {
                int n = clock.Advance(1.0, cfg.DefaultSpeed, dt, false);
                Assert.LessOrEqual(n, SimClock.MaxTicksPerFrame);
                for (int i = 0; i < n; i++) MatchTick.Advance(a, null, scratch);
                total += n;
            }
            Assert.Greater(clock.DroppedTicks, 0, "the backlog was dropped, not run");
            World b = AutoplayArgs.RunSimMatch(4242, total);
            Assert.AreEqual(AutoplayArgs.WorldHash(b), AutoplayArgs.WorldHash(a), "same ticks, same world");
            Assert.AreEqual(4, SimClock.TickCap(2, 1.0 / 30));
            Assert.AreEqual(9, SimClock.TickCap(8, 1.0 / 30), "a fast autoplay keeps pace");
        }

        // ── DG-63 crash log ──

        [Test]
        public void TenThousandDifferentErrorsInOneFrameWriteAtMostTheRepeatLimitPlusTwoLines()
        {
            string dir = TempDir();
            try
            {
                string p = Path.Combine(dir, "crash-log.txt");
                var log = new CrashLog(p, false);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 10000; i++) log.Submit("Error", "index " + i + " out of range", "");
                sw.Stop();
                log.FlushNow();
                string[] lines = File.ReadAllLines(p);
                Assert.LessOrEqual(lines.Length, CrashLog.RepeatLimit + 2, string.Join("\n", lines.Take(5)));
                Assert.Less(sw.ElapsedMilliseconds, 50, "main-thread cost of the burst");
                Assert.Less(new FileInfo(p).Length, CrashLog.MaxBytes);
                Assert.AreEqual(CrashLog.RepeatKey("Error", "index 7 out of range", ""), CrashLog.RepeatKey("Error", "index 81 out of range", ""));
                Assert.AreEqual(CrashLog.RepeatKey("Error", "at 0x1F3A", ""), CrashLog.RepeatKey("Error", "at 0xBEEF00", ""));
                Assert.AreNotEqual(CrashLog.RepeatKey("Error", "a", ""), CrashLog.RepeatKey("Exception", "a", ""));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ASimOrDriverStackEndsTheMatchInsteadOfFreezingIt()
        {
            Assert.IsTrue(AutoplayArgs.IsMatchCritical(AutoplayArgs.StackTypeNames("CriticalMass.Sim.Bots.MatchTick:Advance()\nCriticalMass.Runtime.Game.SimDriverBehaviour:Update()\n")));
            Assert.IsFalse(AutoplayArgs.IsMatchCritical(AutoplayArgs.StackTypeNames("CriticalMass.Runtime.Hud.GameHud:Update()\n")));
        }

        // ── command line, paths, live-match test ──

        [Test]
        public void NewFlagsParse()
        {
            AutoplayArgs.Options o = AutoplayArgs.Parse(new[] { "-debugHangSeconds", "12", "-seed", "77", "-simHashOut", "/tmp/h.txt", "-shots" });
            Assert.AreEqual(12.0, o.DebugHangSeconds);
            Assert.AreEqual(77, o.Seed);
            Assert.AreEqual("/tmp/h.txt", o.SimHashOut);
            Assert.IsTrue(o.Shots);
            Assert.AreEqual("/x/y", GamePaths.DataDirArg(new[] { "-autoplay", "-dataDir", "/x/y" }));
            Assert.IsNull(GamePaths.DataDirArg(new[] { "-dataDir", "-autoplay" }));
            string[] names = { "Very Low", "Low", "Medium", "High", "Very High", "Ultra" };
            Assert.AreEqual(1, FrameGovernor.QualityArg(new[] { "-quality", "Low" }, names));
            Assert.AreEqual(0, FrameGovernor.QualityArg(new[] { "-quality", "verylow" }, names));
            Assert.AreEqual(3, FrameGovernor.QualityArg(new[] { "-quality", "3" }, names));
            Assert.AreEqual(-1, FrameGovernor.QualityArg(new[] { "-quality", "9" }, names));
        }

        [Test]
        public void OnlyALiveHumanMatchPausesForFocusOrController()
        {
            Assert.IsTrue(SystemEvents.IsLiveMatch(true, false, false, false, false));
            Assert.IsFalse(SystemEvents.IsLiveMatch(true, true, false, false, false), "title");
            Assert.IsFalse(SystemEvents.IsLiveMatch(true, false, true, false, false), "ended");
            Assert.IsFalse(SystemEvents.IsLiveMatch(true, false, false, true, false), "backdrop");
            Assert.IsFalse(SystemEvents.IsLiveMatch(true, false, false, false, true), "autoplay");
        }

        [Test]
        public void SameSeedSameHash()
        {
            Assert.AreEqual(AutoplayArgs.WorldHash(AutoplayArgs.RunSimMatch(9, 600)), AutoplayArgs.WorldHash(AutoplayArgs.RunSimMatch(9, 600)));
        }
    }
}
