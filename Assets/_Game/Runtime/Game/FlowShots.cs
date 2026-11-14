using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// -flowShots (player build, automation only; feat/flow-capture-1): walks the real screens through the real navigation
    /// code, the way a first-time player does, and writes a still of each step at 1920x1080 (16:9) and at 1280x720 into the
    /// data folder as shot-flow-NN-step-WxH.png (tools/remote-playtest.sh --mode captures copies shot-*.png back). The
    /// editor tools (FlowCaptureTool, MenuCaptureTool) cannot run in a player; this one can. Steps, in flow order:
    /// title; main menu; every settings tab (top, and bottom where it scrolls); match setup on each team and with each
    /// class highlighted; the loading screen; the match at its first second and at 5 s; the local player knocked out; the
    /// respawn; end of match; results; the rematch prompt; back to the main menu and to the title; the quit prompt (armed,
    /// never confirmed). Each step logs "[CM.FlowShots] NN step REACHED ..." or "NOT REACHED: why", then the run quits.
    /// Do not pass -autoplay (it skips the screens). Knock-out: the nearest enemy deals the local player a finishing hit
    /// through the sim's own Damage.ApplyDamage (capture runs only); match end: the clock is moved to its last ticks, as
    /// FlowCaptureTool does. Add -soundVolume V to hear and log the audio (AudioCueLog).
    /// </summary>
    public sealed class FlowShots : MonoBehaviour
    {
        public const string Flag = "-flowshots";
        public const string LogTag = "[CM.FlowShots]";
        public const int WideW = 1920, WideH = 1080, SmallW = 1280, SmallH = 720;
        /// <summary>Real seconds a step waits for its screen before it is logged NOT REACHED.</summary>
        public const float StepTimeout = 45f;
        /// <summary>Real seconds after a screen change before its still (the 180 ms transitions have finished).</summary>
        public const float Settle = 0.45f;

        int _step;
        int _reached, _missed;
        readonly List<string> _summary = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !AutoplayArgs.HasAutomationFlag(Flag)) return;
            GameFlow.ForceEnabled = true;          // the title and menus run (never the straight-to-match boot)
            StoryIntro.PlayOnFirstLaunch = false;  // the film has its own capture (-introCapture)
            ClassSelect.AutoRespawn = true;
            var go = new GameObject("CriticalMass FlowShots");
            DontDestroyOnLoad(go);
            go.AddComponent<FlowShots>();
            Debug.Log($"{LogTag} on: walking the player flow, stills at {WideW}x{WideH} and {SmallW}x{SmallH} into {GamePaths.DataDir}");
        }

        IEnumerator Start()
        {
            SetSize(WideW, WideH);
            yield return Walk();
            Debug.Log($"{LogTag} done: {_reached} REACHED, {_missed} NOT REACHED");
            foreach (string s in _summary) Debug.Log($"{LogTag} summary {s}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        // ---------------------------------------------------------------- the walk

        static FlowDirector D => FlowDirector.Current;
        static SimDriverBehaviour Driver => D != null ? D.Driver : null;
        static World W => Driver != null ? Driver.World : null;

        IEnumerator Walk()
        {
            // 1. Title.
            yield return WaitFor(() => D != null && !D.Loading && Title() != null && GameFlow.State == FlowState.Title, StepTimeout);
            if (StoryIntro.IsPlaying) { StoryIntro.Skip(); yield return new WaitForSecondsRealtime(1.2f); }
            yield return Shoot("title", () => Title() != null, "no title screen after boot");

            // 2. Main menu, through the title's HOME button.
            TitleScreen t = Title();
            if (t != null && t.MainMenuItem != null && t.MainMenuItem.Submitted != null) t.MainMenuItem.Submitted();
            else if (D != null) D.ShowMenu();
            yield return WaitFor(() => D != null && D.MainMenu != null && D.MainMenu.IsVisible, 10f);
            yield return Shoot("main-menu", () => D != null && D.MainMenu != null && D.MainMenu.IsVisible, "the main menu did not open");

            // 3. Every settings tab, opened the way the menu's SETTINGS entry opens it; scrolled top and bottom.
            yield return SettingsTabs();

            // 4. Match setup: each team, each class highlighted.
            if (D != null) D.ShowSetup();
            yield return WaitFor(() => D != null && D.Setup != null && D.Setup.IsVisible, 10f);
            MatchSetup setup = D != null ? D.Setup : null;
            bool setupUp = setup != null && setup.IsVisible;
            foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
            {
                if (setupUp) { setup.SetTeam(team); Focus(setup.TeamItem); }
                yield return Shoot("setup-team-" + team.ToString().ToLowerInvariant(), () => setupUp, "match setup did not open");
            }
            foreach (ClassId c in ClassIds.All)
            {
                if (setupUp)
                {
                    if (MatchSetup.Playable.Contains(c)) setup.SetClass(c);
                    Focus(setup.Card(c));
                }
                yield return Shoot("setup-class-" + c.ToString().ToLowerInvariant(), () => setupUp,
                    "match setup did not open");
            }
            if (setupUp)
            {
                setup.SetTeam(TeamId.Azure);
                setup.SetClass(ClassId.Warrior);
                Focus(setup.StartItem);
            }

            // 5. Loading screen: START on the setup screen.
            SimDriverBehaviour before = Driver;
            if (setupUp) setup.StartMatch();
            else GameFlow.BeginMatch(GameFlow.LastSetup);
            yield return WaitFor(() => LoadingScreen.IsOpen, 15f);
            yield return Shoot("loading", () => LoadingScreen.IsOpen,
                LoadingScreen.UseInFlow ? "the loading screen never opened" : "the loading screen is off in this run (LoadingScreen.UseInFlow false)");

            // 6. Match start: its first second, then 5 s in.
            yield return WaitFor(() => Driver != null && Driver != before && GameFlow.State == FlowState.Match && !LoadingScreen.IsOpen
                && W != null && W.Match.Time >= 0.6, 60f);
            yield return Shoot("match-start-1s", () => InMatch() && W.Match.Time < 2.5, InMatch() ? $"late: match time {W.Match.Time:F1} s" : "the match did not start");
            yield return WaitFor(() => InMatch() && W.Match.Time >= 5.0, 30f);
            yield return Shoot("match-5s", () => InMatch() && W.Match.Time >= 5.0, "the match clock did not reach 5 s");

            // 7. The local player knocked out (a finishing hit from the nearest enemy, through the sim's damage code).
            double koAt = -1;
            float koDeadline = Time.realtimeSinceStartup + 15f;
            while (InMatch() && W.Actors[0].Alive && Time.realtimeSinceStartup < koDeadline)
            {
                World w = W;
                int foe = NearestEnemy(w, 0);
                double sx = foe >= 0 ? w.X[foe] : w.X[0] + 1, sz = foe >= 0 ? w.Z[foe] : w.Z[0];
                Damage.ApplyDamage(w, 0, w.Actors[0].MaxHp + 1000, foe, sx, sz);
                yield return null;
            }
            if (InMatch() && !W.Actors[0].Alive) koAt = W.Match.Time;
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shoot("knocked-out", () => koAt >= 0, InMatch() ? "the hit did not knock the player out (spawn protection or ward held)" : "not in a match");

            // 8. The respawn (automatic; a respawn class pick, if one opens, is shot and taken as is).
            yield return WaitFor(() => !InMatch() || W.Actors[0].Alive || (ClassSelect.Instance != null && ClassSelect.Instance.Mode == ClassSelect.SelectMode.Respawn), 40f);
            if (ClassSelect.Instance != null && ClassSelect.Instance.Mode == ClassSelect.SelectMode.Respawn)
            {
                yield return Shoot("respawn-class-pick", () => true, "");
                ClassSelect.Instance.PickRespawn(ClassSelect.Instance.Focused);
                yield return WaitFor(() => !InMatch() || W.Actors[0].Alive, 30f);
            }
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shoot("respawn", () => koAt >= 0 && InMatch() && W.Actors[0].Alive, koAt < 0 ? "no knock-out to come back from" : "the player did not respawn in 40 s");

            // 9. End of match: the clock moved to its last ticks.
            if (InMatch()) W.Match.Time = Math.Max(W.Match.Time, W.Config.MatchSeconds - 2.0 / W.Config.SimHz);
            yield return WaitFor(() => W != null && W.Match.Phase == MatchPhase.Ended, 20f);
            yield return Shoot("match-end", () => W != null && W.Match.Phase == MatchPhase.Ended, "the match did not end");

            // 10. Results, 2 s after they open.
            yield return WaitFor(() => D != null && D.Results != null && D.Results.IsVisible, 30f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Shoot("results", () => D != null && D.Results != null && D.Results.IsVisible, "the results screen did not open");

            // 11. Rematch prompt: PLAY AGAIN focused on the results.
            bool resultsUp = D != null && D.Results != null && D.Results.IsVisible;
            if (resultsUp) Focus(D.Results.PlayAgainItem);
            yield return Shoot("rematch-prompt", () => resultsUp && D.Results.PlayAgainItem != null, "no results screen to offer a rematch");

            // 12. Return to the main menu (the results' MAIN MENU button reloads a backdrop), then BACK to the title.
            SimDriverBehaviour matchDriver = Driver;
            if (resultsUp && D.Results.MainMenuItem != null && D.Results.MainMenuItem.Submitted != null) D.Results.MainMenuItem.Submitted();
            else if (D != null) D.ToMainMenu();
            yield return WaitFor(() => D != null && !D.Loading && Driver != matchDriver && D.MainMenu != null && D.MainMenu.IsVisible, 60f);
            yield return Shoot("back-to-menu", () => D != null && D.MainMenu != null && D.MainMenu.IsVisible, "the main menu did not come back after the results");
            if (D != null && D.MainMenu != null && D.MainMenu.IsVisible) D.MainMenu.Back();
            yield return WaitFor(() => Title() != null && GameFlow.State == FlowState.Title, 15f);
            yield return Shoot("return-to-title", () => Title() != null && GameFlow.State == FlowState.Title, "the title did not come back");

            // 13. Quit prompt: HOME, then QUIT once (it arms CONFIRM QUIT; the second press is never sent).
            t = Title();
            if (t != null && t.MainMenuItem != null && t.MainMenuItem.Submitted != null) t.MainMenuItem.Submitted();
            else if (D != null) D.ShowMenu();
            yield return WaitFor(() => D != null && D.MainMenu != null && D.MainMenu.IsVisible, 10f);
            MenuItem quit = D != null && D.MainMenu != null ? D.MainMenu.Item(OverlayRegistry.Quit) : null;
            if (quit != null) { Focus(quit); quit.Submitted?.Invoke(); }
            yield return Shoot("quit-prompt", () => D.MainMenu.QuitConfirming, quit == null ? "no QUIT entry on the main menu" : "QUIT did not arm its confirm");
            yield return new WaitForSecondsRealtime(MainMenu.QuitConfirmSeconds + 0.5f);   // let it disarm: this run quits on its own
        }

        IEnumerator SettingsTabs()
        {
            int tabs = 4 + PauseMenu.ExtraTabs.Count;
            if (D != null) D.OpenSettings(PauseMenu.Tab.Controls, OverlayRegistry.Settings);
            PauseMenu pm = D != null ? D.GetComponent<PauseMenu>() : null;
            yield return WaitFor(() => pm != null && pm.IsOpen, 5f);
            for (int i = 0; i < tabs; i++)
            {
                string name = "settings-" + TabName(i);
                bool open = pm != null && pm.IsOpen;
                if (open) pm.ShowSettings((PauseMenu.Tab)i);
                yield return new WaitForSecondsRealtime(0.05f);
                ScrollRect sr = open ? ActiveScroll(pm) : null;
                if (sr != null) sr.verticalNormalizedPosition = 1f;
                yield return Shoot(name + "-top", () => open, "the settings page did not open");
                if (sr != null)
                {
                    sr.verticalNormalizedPosition = 0f;
                    yield return Shoot(name + "-bottom", () => true, "");
                }
                else Log(name + "-bottom", false, "this tab fits its panel (no scroll)", false);
            }
            if (pm != null && pm.IsOpen) pm.ShowRoot();   // the settings page's BACK: the bridge returns to the main menu
            yield return WaitFor(() => D != null && D.MainMenu != null && D.MainMenu.IsVisible, 5f);
            if (D != null && D.MainMenu != null && !D.MainMenu.IsVisible) D.ShowMenu();
        }

        static string TabName(int i)
        {
            if (i < 4) return ((PauseMenu.Tab)i).ToString().ToLowerInvariant();
            string n = PauseMenu.ExtraTabs[i - 4].Name ?? ("extra-" + i);
            return n.ToLowerInvariant().Replace(' ', '-');
        }

        static ScrollRect ActiveScroll(PauseMenu pm)
        {
            foreach (ScrollRect s in pm.GetComponentsInChildren<ScrollRect>(false))
                if (s.isActiveAndEnabled) return s;
            return null;
        }

        // ---------------------------------------------------------------- helpers

        static TitleScreen Title()
        {
            TitleScreen t = FindAnyObjectByType<TitleScreen>();
            return t != null && t.isActiveAndEnabled ? t : null;
        }

        static bool InMatch() => W != null && GameFlow.State == FlowState.Match && W.Match.Phase != MatchPhase.Ended && W.ActorCount > 0;

        static void Focus(MenuItem it)
        {
            if (it == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(it.gameObject);
        }

        static int NearestEnemy(World w, int me)
        {
            int best = -1;
            double bd = double.MaxValue;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == me || !w.Actors[j].Alive || w.Actors[j].Team == w.Actors[me].Team) continue;
                double dx = w.X[j] - w.X[me], dz = w.Z[j] - w.Z[me], d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = j; }
            }
            return best;
        }

        static IEnumerator WaitFor(Func<bool> ok, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                bool done;
                try { done = ok(); }
                catch (Exception) { done = false; }
                if (done) yield break;
                yield return null;
            }
        }

        /// <summary>Settles, checks the step's screen is up, and writes both stills (or logs why not).</summary>
        IEnumerator Shoot(string name, Func<bool> reached, string why)
        {
            _step++;
            string id = _step.ToString("00") + "-" + name;
            yield return new WaitForSecondsRealtime(Settle);
            bool ok;
            try { ok = reached(); }
            catch (Exception e) { ok = false; why = e.GetType().Name + ": " + e.Message; }
            if (!ok)
            {
                Log(id, false, why, true);
                yield break;
            }
            string state = $"flow {GameFlow.State}, match time {(W != null ? W.Match.Time : 0):F1} s";
            yield return Still(id, WideW, WideH);
            int got = _lastW;
            yield return Still(id, SmallW, SmallH);
            int got2 = _lastW;
            SetSize(WideW, WideH);
            yield return WaitSize(WideW, WideH);
            string sizes = (got == WideW ? $"{WideW}x{WideH}" : $"{WideW}x{WideH} NOT REACHED (window stayed {got} wide)")
                + ", " + (got2 == SmallW ? $"{SmallW}x{SmallH}" : $"{SmallW}x{SmallH} NOT REACHED (window stayed {got2} wide)");
            Log(id, true, state + "; " + sizes, true);
        }

        int _lastW;

        IEnumerator Still(string id, int w, int h)
        {
            SetSize(w, h);
            yield return WaitSize(w, h);
            Relayout();
            yield return null;
            yield return new WaitForEndOfFrame();
            _lastW = Screen.width;
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string path = Path.Combine(GamePaths.DataDir, $"shot-flow-{id}-{Screen.width}x{Screen.height}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogTag} {id}: could not write the still: {e.Message}");
            }
            finally
            {
                Destroy(tex);
            }
        }

        static void SetSize(int w, int h)
        {
            if (Screen.width != w || Screen.height != h) Screen.SetResolution(w, h, FullScreenMode.Windowed);
        }

        static IEnumerator WaitSize(int w, int h)
        {
            for (int i = 0; i < 40 && (Screen.width != w || Screen.height != h); i++) yield return null;
            yield return null;
            yield return null;
        }

        static void Relayout()
        {
            foreach (FlowScreen s in FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) s.UpdateScale();
            TitleScreen t = FindAnyObjectByType<TitleScreen>();
            if (t != null) t.UpdateScale();
            GameHud hud = FindAnyObjectByType<GameHud>();
            if (hud != null) hud.UpdateScale();
            PauseMenu pm = FindAnyObjectByType<PauseMenu>();
            if (pm != null) pm.UpdateScale();
            Canvas.ForceUpdateCanvases();
        }

        void Log(string id, bool reached, string note, bool counts)
        {
            if (counts) { if (reached) _reached++; else _missed++; }
            string line = $"{id} {(reached ? "REACHED" : "NOT REACHED")}" + (string.IsNullOrEmpty(note) ? "" : (reached ? " (" + note + ")" : ": " + note));
            _summary.Add(line);
            Debug.Log($"{LogTag} {line}");
        }
    }
}
