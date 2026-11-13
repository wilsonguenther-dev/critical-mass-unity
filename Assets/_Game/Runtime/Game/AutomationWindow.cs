using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Automated runs never take the owner's screen (standing rule; owner report 2026-11-13 10:19: a perf agent's demo smoke
    /// put a windowed "Critical Mass" with the AUTOMATED TEST RUN plate on his screen and in front of his work, although the
    /// script launched it with `open -g -j`: the Unity player activates itself and re-shows its window at boot and at every
    /// match load, so a launcher flag cannot hide it). The game hides itself instead, so no script can forget:
    /// <list type="bullet">
    /// <item>Any automation flag on the command line (<see cref="AutomationFlags"/>: -autoplay, -quitAfterMatch, -perfLog,
    /// -shots, -simHashOut, -capture, -soak, ...) makes the window hidden by default; <c>-showWindow</c> is the explicit
    /// opt-in for a person who wants to watch a test run.</item>
    /// <item>macOS player: the app turns into an accessory app (no Dock icon, no Dock bounce, no menu bar), every window
    /// is made fully transparent and click-through, the app is hidden, and a keeper checks every frame: if the app was
    /// unhidden or activated (Unity does both on a resolution change and a scene load) it is hidden again at once, which
    /// also hands focus back to the app the owner was using. Fullscreen requests become a window (a hidden fullscreen
    /// window would still claim the display).</item>
    /// <item>Audio is muted (AudioListener.volume 0, enforced every frame, after GameSettings applies the saved volume);
    /// the audio engine still runs, so smoke measurements of its cost stay valid.</item>
    /// <item>The editor, batch mode and test runs are never touched. Other platforms: the flag parsing applies, the hiding
    /// is macOS only (Windows test runs go through tools/windows-run.ps1 on a separate machine).</item>
    /// </list>
    /// Pure flag parsing is <see cref="ShouldHide"/> (EditMode tests: AutomationWindowTests).
    /// </summary>
    public static class AutomationWindow
    {
        public const string LogTag = "[CM.Window]";
        public const string ShowWindowFlag = "-showwindow";

        /// <summary>Command-line flags that only automated runs use (lower case; matched case-insensitively).</summary>
        public static readonly string[] AutomationFlags =
        {
            "-autoplay", "-quitaftermatch", "-autoplaymatches", "-autoplayqualities", "-perflog", "-shots", "-simhashout",
            "-capture", "-soak", "-tracestates", "-debughangseconds", "-automation",
        };

        /// <summary>True when this run must stay hidden: an automation flag is present and -showWindow is not (pure; tests).</summary>
        public static bool ShouldHide(string[] args)
        {
            if (args == null) return false;
            bool automation = false;
            foreach (string raw in args)
            {
                string a = (raw ?? "").Trim().ToLowerInvariant();
                if (a == ShowWindowFlag) return false;
                if (Array.IndexOf(AutomationFlags, a) >= 0) automation = true;
            }
            return automation;
        }

        /// <summary>This process hides its window (decided once from the command line; player builds only).</summary>
        public static bool Active { get; private set; }

        /// <summary>Times the keeper found the app unhidden or active and hid it again (perf report, tests).</summary>
        public static int Rehides { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void Boot()
        {
            Active = false;
            Rehides = 0;
            if (Application.isEditor || Application.isBatchMode) return;
            if (!ShouldHide(Environment.GetCommandLineArgs())) return;
            Active = true;
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
                Screen.SetResolution(Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height), FullScreenMode.Windowed);
            Native.MakeAccessory();
            Native.HideNow();
            AudioListener.volume = 0f;
            var go = new GameObject("CriticalMass AutomationWindow") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Keeper>();
            Debug.Log($"{LogTag} automated run: window hidden, click-through and muted, no Dock icon (pass -showWindow to watch it)");
        }

        /// <summary>Re-hides every frame the app was shown or activated; keeps the audio muted.</summary>
        sealed class Keeper : MonoBehaviour
        {
            void LateUpdate()
            {
                if (AudioListener.volume != 0f) AudioListener.volume = 0f;
                if (Screen.fullScreenMode != FullScreenMode.Windowed)
                    Screen.SetResolution(Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height), FullScreenMode.Windowed);
                if (Native.NeedsHide())
                {
                    Rehides++;
                    Native.HideNow();
                }
            }

            void OnApplicationFocus(bool focused)
            {
                if (focused) { Rehides++; Native.HideNow(); }
            }
        }

        /// <summary>
        /// AppKit through the Objective-C runtime (no native plugin to build or sign): NSApp setActivationPolicy:,
        /// hide:, isHidden, isActive, and per window setAlphaValue: / setIgnoresMouseEvents:. Main thread only (Unity's
        /// main thread is AppKit's on macOS). Every call is guarded: a failure logs once and turns the native path off.
        /// </summary>
        static class Native
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            const string ObjC = "/usr/lib/libobjc.A.dylib";
            [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
            [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr self, IntPtr sel);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr self, IntPtr sel, IntPtr arg);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendLong(IntPtr self, IntPtr sel, long arg);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendULong(IntPtr self, IntPtr sel, ulong arg);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendDouble(IntPtr self, IntPtr sel, double arg);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr self, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool SendRetBool(IntPtr self, IntPtr sel);
            [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern ulong SendRetULong(IntPtr self, IntPtr sel);

            const long ActivationPolicyAccessory = 1;
            static bool _broken;

            static IntPtr App() => Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));

            static void Guard(Action a, string what)
            {
                if (_broken) return;
                try { a(); }
                catch (Exception e)
                {
                    _broken = true;
                    Debug.LogWarning($"{LogTag} could not {what} ({e.GetType().Name}: {e.Message}); the native hiding is off for this run");
                }
            }

            public static void MakeAccessory() => Guard(() =>
            {
                SendLong(App(), sel_registerName("setActivationPolicy:"), ActivationPolicyAccessory);
            }, "make the app an accessory");

            public static void HideNow() => Guard(() =>
            {
                IntPtr app = App();
                IntPtr windows = Send(app, sel_registerName("windows"));
                ulong n = windows == IntPtr.Zero ? 0 : SendRetULong(windows, sel_registerName("count"));
                IntPtr at = sel_registerName("objectAtIndex:"), alpha = sel_registerName("setAlphaValue:"),
                    mouse = sel_registerName("setIgnoresMouseEvents:");
                for (ulong k = 0; k < n; k++)
                {
                    IntPtr w = SendULong(windows, at, k);
                    if (w == IntPtr.Zero) continue;
                    SendDouble(w, alpha, 0.0);
                    SendBool(w, mouse, true);
                }
                SendPtr(app, sel_registerName("hide:"), IntPtr.Zero);
            }, "hide the window");

            public static bool NeedsHide()
            {
                if (_broken) return false;
                bool needs = false;
                Guard(() =>
                {
                    IntPtr app = App();
                    needs = !SendRetBool(app, sel_registerName("isHidden")) || SendRetBool(app, sel_registerName("isActive"));
                }, "read the window state");
                return needs;
            }
#else
            public static void MakeAccessory() { }
            public static void HideNow() { }
            public static bool NeedsHide() => false;
#endif
        }
    }
}
