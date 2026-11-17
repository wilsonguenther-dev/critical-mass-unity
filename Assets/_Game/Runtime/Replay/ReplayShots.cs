using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Net.Replay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Replay
{
    /// <summary>
    /// b13-replay capture switches (player builds only; read here, as GoreShots and OnlineShots read theirs):
    /// <list type="bullet">
    /// <item><c>-replayLast</c>: from the main menu, open the newest replay this build can play and leave it playing.</item>
    /// <item><c>-replayShots [dir]</c>: stills (1920x1080 and 1280x720) of the Replays list, the "other version" refusal,
    /// and playback at 1x, 4x, paused, following another player, free camera, after a 10 s jump forward, the rewind
    /// cover and after the rewind, into dir (default DataDir/replay-shots) as shot-replay-NN-NAME-WxH.png, one
    /// "[CM.ReplayShots] NN-NAME REACHED / NOT REACHED" line each, then quits.</item>
    /// </list>
    /// When the Replays folder has no playable replay a short sample is recorded first, headless (a 16v16 on seed 4242,
    /// 90 s of match time, actor 0 scripted), and saved like a played match, so the run needs nobody to play. The
    /// "other version" still uses a copy of that replay with an old build stamp, deleted right after. Pass -automation
    /// too so the window stays hidden (AutomationWindow hides on its flag list).
    /// </summary>
    public sealed class ReplayShots : MonoBehaviour
    {
        public const string ShotsFlag = "-replayShots", LastFlag = "-replayLast";
        const string Tag = "[CM.ReplayShots]";
        const int WideW = 1920, WideH = 1080, SmallW = 1280, SmallH = 720;
        public const int SampleSeed = 4242, SampleTeamSize = 16;
        public const double SampleSeconds = 90;

        bool _shots;
        string _dir;
        int _step, _reached, _missed;
        static FlowDirector D => FlowDirector.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            string[] a = Environment.GetCommandLineArgs();
            int shots = Array.FindIndex(a, s => string.Equals(s, ShotsFlag, StringComparison.OrdinalIgnoreCase));
            bool last = Array.FindIndex(a, s => string.Equals(s, LastFlag, StringComparison.OrdinalIgnoreCase)) >= 0;
            if (shots < 0 && !last) return;
            GameFlow.ForceEnabled = true;
            Menus.StoryIntro.PlayOnFirstLaunch = false;
            var go = new GameObject("CriticalMass ReplayShots");
            DontDestroyOnLoad(go);
            ReplayShots r = go.AddComponent<ReplayShots>();
            r._shots = shots >= 0;
            r._dir = shots >= 0 && shots + 1 < a.Length && !a[shots + 1].StartsWith("-") ? a[shots + 1] : Path.Combine(GamePaths.DataDir, "replay-shots");
            if (r._shots) Directory.CreateDirectory(r._dir);
            Debug.Log($"{Tag} on: {(r._shots ? "stills into " + r._dir : "open the newest replay")}");
        }

        IEnumerator Start()
        {
            if (_shots) SetSize(WideW, WideH);
            yield return WaitFor(() => D != null && !D.Loading && D.MainMenu != null, 60f);
            if (GameFlow.State == FlowState.Title) GameFlow.ReturnTo(FlowState.Menu);
            yield return WaitFor(() => D != null && !D.Loading && D.MainMenu != null && D.MainMenu.IsVisible, 30f);

            ReplayEntry newest = NewestPlayable();
            if (newest == null)
            {
                string made = ReplayStore.SaveNow(RecordSample(), ReplayStore.Folder);
                Debug.Log($"{Tag} no playable replay: recorded a sample ({(made != null ? Path.GetFileName(made) : "save FAILED")})");
                newest = NewestPlayable();
            }
            if (newest == null) { Finish("no replay to open"); yield break; }

            if (_shots)
            {
                MenuEntry entry = OverlayRegistry.MenuEntries().Find(e => e.Id == ReplaysMenuScreen.MenuId);
                if (entry != null) D.OpenRegistered(entry);
                yield return Shoot("list", () => ReplaysMenuScreen.Instance != null && ReplaysMenuScreen.Instance.IsOpen && ReplaysMenuScreen.Instance.Entries.Count > 0);

                // A copy stamped by an older build: listed as OTHER VERSION, refused with the plain message, nothing loads.
                string old = WriteOtherVersionCopy(newest);
                if (ReplaysMenuScreen.Instance != null && old != null)
                {
                    ReplaysMenuScreen.Instance.Back();
                    yield return null;
                    if (entry != null) D.OpenRegistered(entry);
                    yield return null;
                    ReplaysMenuScreen s = ReplaysMenuScreen.Instance;
                    int idx = s != null ? s.Entries.FindIndex(e => e.Path == old) : -1;
                    if (idx >= 0) s.ChooseForCapture(idx);
                    yield return Shoot("other-version-refused", () => s != null && s.IsOpen && s.Notice.StartsWith(ReplayCompat.OtherVersionText));
                    if (s != null) s.Back();
                }
                TryDelete(old);
            }

            ReplayData d = ReplayStore.Load(newest.Path, out string why);
            if (d == null || !ReplayPlayback.Play(d, out why)) { Finish("could not open the newest replay: " + why); yield break; }
            yield return WaitFor(() => ReplayPlayback.Current != null && !D.Loading && !Menus.LoadingScreen.IsOpen, 60f);
            if (!_shots) yield break;   // -replayLast: leave it playing

            yield return new WaitForSecondsRealtime(3f);
            yield return Shoot("playback-1x", () => ReplayPlayback.Current != null && ReplayPlayback.Current.Player.TicksPlayed > 0);
            ReplayPlayback.Current?.SetSpeedIndex(ReplayPlayback.Speeds.Length - 1);
            yield return new WaitForSecondsRealtime(2f);
            yield return Shoot("playback-4x", () => ReplayPlayback.Current != null && ReplayPlayback.Current.Speed == 4);
            ReplayPlayback.Current?.TogglePause();
            yield return Shoot("paused", () => ReplayPlayback.Current != null && ReplayPlayback.Current.Paused);
            ReplayPlayback.Current?.TogglePause();
            ReplayPlayback.Current?.SetSpeedIndex(ReplayPlayback.DefaultSpeedIndex);
            ReplayPlayback.Current?.CycleFollow(1);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shoot("follow-next-player", () => ReplayPlayback.Current != null && ReplayPlayback.Current.Followed != LocalSeat.Actor);
            ReplayPlayback.Current?.SetFreeCamera(true);
            yield return Shoot("free-camera", () => ReplayPlayback.Current != null && ReplayPlayback.Current.FreeCamera);
            ReplayPlayback.Current?.SetFreeCamera(false);
            int before = ReplayPlayback.Current != null ? ReplayPlayback.Current.Player.TicksPlayed : 0;
            ReplayPlayback.Current?.Jump(1);
            yield return WaitFor(() => ReplayPlayback.Current == null || ReplayPlayback.Current.SeekTarget < 0, 20f);
            yield return Shoot("forward-10s", () => ReplayPlayback.Current != null && ReplayPlayback.Current.Player.TicksPlayed >= before + 250);
            ReplayPlayback.Current?.Jump(-1);
            yield return WaitFor(() => ReplayPlayback.Current != null && ReplayPlayback.Current.Rewinding && !D.Loading, 30f);
            yield return Shoot("rewinding-cover", () => ReplayPlayback.Current != null && ReplayPlayback.Current.Rewinding);
            yield return WaitFor(() => ReplayPlayback.Current != null && !ReplayPlayback.Current.Rewinding, 120f);
            yield return Shoot("after-rewind", () => ReplayPlayback.Current != null && !ReplayPlayback.Current.Rewinding &&
                                                     ReplayPlayback.Current.Message == null);
            Finish(null);
        }

        static ReplayEntry NewestPlayable()
        {
            foreach (ReplayEntry e in ReplayStore.List()) if (e.Compat == ReplayCompatibility.Ok) return e;
            return null;
        }

        /// <summary>A headless 16v16 recorded exactly as a played match is (its own world, never shown).</summary>
        static ReplayData RecordSample()
        {
            var s = new ReplayStart { Kind = ReplayKind.Offline, Basis = ConfigBasis.Default, Seed = SampleSeed, TeamSize = SampleTeamSize,
                HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Pace = 1 };
            GameConfig cfg = s.BuildConfig();
            World w = World.CreateWorld(s.ToWorldOptions(cfg));
            var h = new ReplayHeader { BuildVersion = BuildInfo.Version, BuildCommit = BuildInfo.Commit, MapName = PlayModes.MapName,
                Mode = (int)MatchMode.Custom, ModeLabel = "SAMPLE", Start = s, Roster = new ReplayActor[w.ActorCount] };
            for (int i = 0; i < w.ActorCount; i++)
                h.Roster[i] = new ReplayActor { Team = w.Actors[i].Team, Class = w.Actors[i].Cls, Human = w.Actors[i].IsHuman, Name = i == 0 ? "SAMPLE" : "" };
            var rec = new ReplayRecorder();
            rec.Begin(w, h);
            InputFrame f = SimInput.EmptyInput(), scratch = SimInput.EmptyInput();
            int ticks = (int)(SampleSeconds * cfg.SimHz);
            IMatchInputTap keep = MatchTick.Tap;
            MatchTick.Tap = rec;   // the live capture must not see this world
            try
            {
                for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    // Actor 0 walks a slow square and swings now and then; the bots play as always.
                    int leg = (t / 90) % 4;
                    f.MoveX = leg == 0 ? 1 : leg == 2 ? -1 : 0;
                    f.MoveZ = leg == 1 ? 1 : leg == 3 ? -1 : 0;
                    f.AimX = w.X[0] + f.MoveX * 3 + 0.5;
                    f.AimZ = w.Z[0] + f.MoveZ * 3 + 0.5;
                    f.Attack = t % 25 < 3;
                    MatchTick.Advance(w, t < 600 ? f : null, scratch);
                }
            }
            finally { MatchTick.Tap = keep; }
            return rec.Finish(w.Match.Phase == MatchPhase.Ended, DateTime.UtcNow.Ticks);
        }

        static string WriteOtherVersionCopy(ReplayEntry e)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(e.Path);
                if (ReplayFile.TryRead(bytes, out ReplayData d, out _) != ReplayReadStatus.Ok) return null;
                d.Header.BuildVersion = "0.9.1";
                d.Header.BuildCommit = "0000000";
                d.Header.RecordedUtcTicks = DateTime.UtcNow.Ticks;
                string path = Path.Combine(ReplayStore.Folder, "replay-capture-other-version" + ReplayStore.Extension);
                File.WriteAllBytes(path, ReplayFile.Write(d, true));
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} could not write the other-version copy: {ex.Message}");
                return null;
            }
        }

        static void TryDelete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        void Finish(string problem)
        {
            if (problem != null) Debug.LogError($"{Tag} {problem}");
            if (!_shots) return;
            Debug.Log($"{Tag} done: {_reached} reached, {_missed} not reached");
            Application.Quit(problem == null && _missed == 0 ? 0 : 1);
        }

        IEnumerator Shoot(string name, Func<bool> reached)
        {
            _step++;
            string id = _step.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "-" + name;
            yield return new WaitForSecondsRealtime(0.5f);
            bool ok;
            try { ok = reached(); } catch (Exception) { ok = false; }
            if (!ok) { _missed++; Debug.Log($"{Tag} {id} NOT REACHED"); yield break; }
            yield return Still(id, WideW, WideH);
            yield return Still(id, SmallW, SmallH);
            SetSize(WideW, WideH);
            _reached++;
            Debug.Log($"{Tag} {id} REACHED");
        }

        IEnumerator Still(string id, int w, int h)
        {
            SetSize(w, h);
            for (int i = 0; i < 40 && (Screen.width != w || Screen.height != h); i++) yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(_dir, $"shot-replay-{id}-{Screen.width}x{Screen.height}.png"), tex.EncodeToPNG()); }
            catch (Exception e) { Debug.LogWarning($"{Tag} {id}: could not write the still: {e.Message}"); }
            finally { Destroy(tex); }
        }

        static void SetSize(int w, int h)
        {
            if (Screen.width != w || Screen.height != h) Screen.SetResolution(w, h, FullScreenMode.Windowed);
        }

        static IEnumerator WaitFor(Func<bool> ok, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                bool done;
                try { done = ok(); } catch (Exception) { done = false; }
                if (done) yield break;
                yield return null;
            }
        }
    }
}
