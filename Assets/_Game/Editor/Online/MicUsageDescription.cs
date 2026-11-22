using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CriticalMass.Editor.Online
{
    /// <summary>
    /// b17-chat: lobby voice (off by default) opens the microphone only while push-to-talk is held. macOS asks the player
    /// the first time and refuses an app whose Info.plist has no NSMicrophoneUsageDescription, so a mac build gets one here
    /// (Windows asks through its own privacy settings and needs nothing). Idempotent: an existing key is left alone.
    /// </summary>
    public sealed class MicUsageDescription : IPostprocessBuildWithReport
    {
        public const string Words = "Critical Mass uses the microphone only while you hold the talk key in an online room, to send your voice to your team.";

        public int callbackOrder => 50;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX) return;
            string plist = Path.Combine(report.summary.outputPath, "Contents", "Info.plist");
            if (!File.Exists(plist)) { Debug.LogWarning("[b17-voice] no Info.plist at " + plist); return; }
            string s = File.ReadAllText(plist);
            string patched = Patch(s);
            if (patched == s) return;
            File.WriteAllText(plist, patched);
            Debug.Log("[b17-voice] NSMicrophoneUsageDescription added to " + plist);
        }

        /// <summary>The plist with the key added before its last &lt;/dict&gt; (unchanged when present or not a plist dict).</summary>
        public static string Patch(string plist)
        {
            if (string.IsNullOrEmpty(plist) || plist.Contains("NSMicrophoneUsageDescription")) return plist;
            int end = plist.LastIndexOf("</dict>", System.StringComparison.Ordinal);
            if (end < 0) return plist;
            return plist.Substring(0, end) + "\t<key>NSMicrophoneUsageDescription</key>\n\t<string>" + Words + "</string>\n" + plist.Substring(end);
        }
    }
}
