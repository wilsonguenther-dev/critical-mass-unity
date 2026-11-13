using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CriticalMass.Runtime.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CriticalMass.Editor
{
    /// <summary>
    /// Command-line player builds. Run with:
    /// Unity -batchmode -quit -projectPath . -executeMethod CriticalMass.Editor.Build.MacOS
    /// Every failure path exits the editor with a non-zero code.
    /// DG-05: every player carries a version stamp (BuildInfo: bundleVersion.commitCount, short commit, dirty flag, UTC
    /// time), written to Assets/_Game/Resources/BuildInfo.txt for the length of the build and deleted after it, so the
    /// tree stays clean. Command-line options after -executeMethod: -buildOutput PATH (relative to the project; default
    /// below) and -development (a development player: profiler markers for the F3 overlay and the autoplay report).
    /// </summary>
    public static class Build
    {
        public const string DefaultMacOutput = "Builds/macOS/CriticalMass.app";
        public const string DefaultWindowsOutput = "Builds/Windows/CriticalMass.exe";
        public const string StampAssetPath = "Assets/_Game/Resources/" + BuildInfo.ResourcePath + ".txt";

        public static void MacOS() => Run(BuildTarget.StandaloneOSX, DefaultMacOutput);

        public static void Windows() => Run(BuildTarget.StandaloneWindows64, DefaultWindowsOutput);

        /// <summary>The value after flag on the command line, or null.</summary>
        static string Arg(string flag)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
                if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }

        static bool Flag(string flag) =>
            Environment.GetCommandLineArgs().Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));

        static string Git(string root, string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 ? o.Trim() : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes the BuildInfo stamp for this build (deleted again by DeleteStamp).</summary>
        static void WriteStamp(BuildTarget target, bool development)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string commit = Git(root, "rev-parse --short=7 HEAD") ?? "unknown";
            string count = Git(root, "rev-list --count HEAD") ?? "0";
            string status = Git(root, "status --porcelain --untracked-files=no");
            bool dirty = !string.IsNullOrEmpty(status);
            string version = PlayerSettings.bundleVersion + "." + count;
            string stamp = BuildInfo.Serialize(version, commit, dirty,
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), target.ToString(), development);
            File.WriteAllText(Path.Combine(root, StampAssetPath), stamp);
            AssetDatabase.ImportAsset(StampAssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[CriticalMass.Build] stamp {BuildInfo.Format(version, commit + (dirty ? "+dirty" : ""), development)}");
        }

        static void DeleteStamp()
        {
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(StampAssetPath) != null) AssetDatabase.DeleteAsset(StampAssetPath);
            string root = Directory.GetParent(Application.dataPath).FullName;
            foreach (string f in new[] { StampAssetPath, StampAssetPath + ".meta" })
                if (File.Exists(Path.Combine(root, f))) File.Delete(Path.Combine(root, f));
        }

        static void Run(BuildTarget target, string defaultOutput)
        {
            string outputPath = Arg("-buildOutput") ?? defaultOutput;
            bool development = Flag("-development");
            try
            {
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0)
                {
                    Fail(2, "no enabled scenes in Build Settings");
                    return;
                }

                var group = BuildPipeline.GetBuildTargetGroup(target);
                if (!BuildPipeline.IsBuildTargetSupported(group, target))
                {
                    Fail(3, $"build target {target} is not installed in this editor");
                    return;
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = target,
                    targetGroup = group,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };

                WriteStamp(target, development);
                BuildReport report;
                try
                {
                    report = BuildPipeline.BuildPlayer(options);
                }
                finally
                {
                    DeleteStamp();
                }
                BuildSummary summary = report.summary;
                Debug.Log($"[CriticalMass.Build] target={target} result={summary.result} " +
                          $"errors={summary.totalErrors} sizeBytes={summary.totalSize} seconds={summary.totalTime.TotalSeconds:F0} " +
                          $"development={development} output={summary.outputPath}");

                if (summary.result != BuildResult.Succeeded)
                {
                    Fail(1, $"build result {summary.result}");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(1, "exception during build");
            }
        }

        static void Fail(int code, string reason)
        {
            Debug.LogError($"[CriticalMass.Build] FAILED ({code}): {reason}");
            EditorApplication.Exit(code);
        }
    }
}
