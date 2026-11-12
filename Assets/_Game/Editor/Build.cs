using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CriticalMass.Editor
{
    /// <summary>
    /// Command-line player builds. Run with:
    /// Unity -batchmode -quit -projectPath . -executeMethod CriticalMass.Editor.Build.MacOS
    /// Every failure path exits the editor with a non-zero code.
    /// </summary>
    public static class Build
    {
        public static void MacOS() => Run(BuildTarget.StandaloneOSX, "Builds/macOS/CriticalMass.app");

        public static void Windows() => Run(BuildTarget.StandaloneWindows64, "Builds/Windows/CriticalMass.exe");

        static void Run(BuildTarget target, string outputPath)
        {
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
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[CriticalMass.Build] target={target} result={summary.result} " +
                          $"errors={summary.totalErrors} sizeBytes={summary.totalSize} output={summary.outputPath}");

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
