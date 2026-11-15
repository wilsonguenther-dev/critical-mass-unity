using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-9: -onlineRelayShots plays one real online match through the real relay in a rendered (windowed, not
    /// batch mode) game and saves stills of the lobby, the match and the results into the data folder as
    /// shot-online-relay-NN-NAME-1920x1080.png, one "[CM.OnlineRelayShots] NN-NAME REACHED / NOT REACHED" line each.
    /// The other players are hidden peers (-onlineHost / -onlineJoin with -onlineHeadless) on the same room code.
    /// <para>Flags (the hidden peer's own, parsed by HeadlessPeer.FromArgs):</para>
    /// <list type="bullet">
    /// <item>-onlineHost: this game makes the room; -onlineCodeOut PATH writes the code for the hidden peers;
    /// -onlineHumans N starts the match once N people (this game included) are in and ready.</item>
    /// <item>-onlineJoin CODE: this game joins that room and readies up.</item>
    /// <item>-onlineRelay URL (or CM_RELAY_URL), -onlineName NAME, -onlineTimeout SECONDS (whole run, default 900),
    /// -onlineChatAt TICK, -onlineChatText TEXT, -onlineChatTeam: a scripted chat line from this game.</item>
    /// </list>
    /// Like every automation flag the window stays hidden unless -showWindow is given (AutomationWindow).
    /// </summary>
    public sealed class OnlineRelayShots : MonoBehaviour
    {
        public const string Flag = "-onlinerelayshots";   // lower case: AutoplayArgs compares lower-cased args
        const string Tag = "[CM.OnlineRelayShots]";
        const int W = 1920, H = 1080;

        HeadlessPeer _args;
        int _step, _reached, _missed;
        float _deadline;
        static FlowDirector D => FlowDirector.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !AutoplayArgs.HasAutomationFlag(Flag)) return;
            GameFlow.ForceEnabled = true;
            Menus.StoryIntro.PlayOnFirstLaunch = false;
            Application.runInBackground = true;
            var go = new GameObject("CriticalMass OnlineRelayShots");
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineRelayShots>();
            Debug.Log($"{Tag} on: a real-relay match, stills into {GamePaths.DataDir}");
        }

        IEnumerator Start()
        {
            _args = HeadlessPeer.FromArgs(Environment.GetCommandLineArgs(), OnlineConfig.RelayUrl);
            int timeout = _args.TimeoutSeconds > 0 ? _args.TimeoutSeconds : 900;
            _deadline = Time.realtimeSinceStartup + timeout;
            string relay = _args.RelayUrl;
            string name = string.IsNullOrEmpty(_args.Name) ? "SHOTS" : _args.Name;
            if (string.IsNullOrEmpty(relay)) { Debug.LogError($"{Tag} no relay address (pass -onlineRelay URL or set CM_RELAY_URL)"); Application.Quit(5); yield break; }
            if (!_args.Host && string.IsNullOrEmpty(_args.Code)) { Debug.LogError($"{Tag} pass -onlineHost or -onlineJoin CODE"); Application.Quit(5); yield break; }

            SetSize(W, H);
            yield return WaitFor(() => D != null && !D.Loading && D.MainMenu != null, 60f);
            if (GameFlow.State == FlowState.Title) GameFlow.ReturnTo(FlowState.Menu);
            yield return WaitFor(() => D != null && D.MainMenu != null && D.MainMenu.IsVisible, 20f);
            MenuEntry entry = OverlayRegistry.MenuEntries().Find(e => e.Id == "online");
            if (entry != null) D.OpenRegistered(entry);
            OnlineMenuScreen screen = null;
            yield return WaitFor(() => (screen = FindAnyObjectByType<OnlineMenuScreen>()) != null && screen.IsVisible, 10f);
            if (screen == null) { Debug.LogError($"{Tag} the Play Online screen did not open"); Application.Quit(1); yield break; }

            // The room, through the real relay.
            if (_args.Host)
            {
                Task<(string code, string key)> making = RelayTransport.CreateRoomAsync(relay, CancellationToken.None);
                yield return WaitFor(() => making.IsCompleted, 30f);
                if (!making.IsCompleted || making.IsFaulted || making.IsCanceled)
                {
                    Debug.LogError($"{Tag} could not make a room: {(making.Exception != null ? making.Exception.GetBaseException().Message : "timed out")}");
                    Application.Quit(4);
                    yield break;
                }
                (string code, string key) = making.Result;
                OnlineMatch.HostRoom(RelayTransport.Connect(relay, code, key), code, name);
                if (!string.IsNullOrEmpty(_args.CodeOutPath))
                {
                    try { File.WriteAllText(_args.CodeOutPath, code); } catch (Exception e) { Debug.LogWarning($"{Tag} could not write the code: {e.Message}"); }
                }
                Debug.Log($"{Tag} room {code}");
            }
            else
            {
                string code = RelayTransport.CleanCode(_args.Code);
                OnlineMatch.JoinRoom(RelayTransport.Connect(relay, code, null), code, name);
            }
            screen.CapturePage("room");
            yield return WaitFor(() => OnlineMatch.InRoom, 30f);
            yield return Shoot("lobby-first", () => OnlineMatch.InRoom);

            // Everyone in and ready, then the match.
            int want = Math.Max(2, _args.Humans);
            if (_args.Host)
            {
                yield return WaitFor(() => Confirmed() >= want, Left());
                screen.CapturePage("room");
                yield return Shoot("lobby-full", () => Confirmed() >= want);
                string why = "not tried";
                float until = Time.realtimeSinceStartup + Math.Min(60f, Left());
                while (Time.realtimeSinceStartup < until && (why = OnlineMatch.StartAsHost()) != null)
                    yield return new WaitForSecondsRealtime(1f);
                if (why != null) Debug.LogWarning($"{Tag} could not start the match: {why}");
            }
            else
            {
                yield return WaitFor(() => OnlineMatch.Client != null && OnlineMatch.Client.Phase == RoomPhase.Lobby, 30f);
                OnlineMatch.Client?.SetReady(true);
                yield return new WaitForSecondsRealtime(2f);
                screen.CapturePage("room");
                yield return Shoot("lobby-ready", () => OnlineMatch.InRoom);
            }
            yield return WaitFor(() => GameFlow.State == FlowState.Match && FindAnyObjectByType<SimDriverBehaviour>() is SimDriverBehaviour d && d.IsOnline
                && !Menus.LoadingScreen.IsOpen, Left());
            LockstepSession s = OnlineMatch.Session;
            if (s != null) s.Sampler = new AutoplayPilot(11).Fill;   // this game plays itself, so the stills show a fight
            yield return new WaitForSecondsRealtime(3f);
            yield return Shoot("match-start", () => OnlineMatch.InMatch);
            bool chatted = _args.ChatAt < 0;
            float mid = Time.realtimeSinceStartup + 60f;
            bool midShot = false;
            while (GameFlow.State == FlowState.Match && Time.realtimeSinceStartup < _deadline)
            {
                s = OnlineMatch.Session;
                if (!chatted && s != null && s.Stepped >= _args.ChatAt)
                {
                    chatted = true;
                    ChatChannel ch = OnlineMatch.Host != null ? OnlineMatch.Host.Chat : OnlineMatch.Client?.Chat;
                    string refused = ch?.Say(_args.ChatText, _args.ChatTeam ? ChatScope.Team : ChatScope.All, OnlineMatch.NowMs);
                    Debug.Log($"{Tag} chat sent{(_args.ChatTeam ? " to the team" : "")} at tick {s.Stepped}{(refused != null ? " refused: " + refused : "")}");
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shoot("match-chat", () => OnlineMatch.InMatch);
                }
                if (!midShot && Time.realtimeSinceStartup >= mid) { midShot = true; yield return Shoot("match-mid", () => OnlineMatch.InMatch); }
                if (s != null && s.Stepped % 300 == 0) Summary(s);
                yield return null;
            }
            Summary(OnlineMatch.Session);
            yield return WaitFor(() => GameFlow.State == FlowState.Results, Math.Min(30f, Left()));
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shoot("results", () => GameFlow.State == FlowState.Results);
            Debug.Log($"{Tag} done: {_reached} reached, {_missed} not reached");
            OnlineMatch.Leave();
            Application.Quit(_missed == 0 ? 0 : 1);
        }

        void Summary(LockstepSession s)
        {
            if (s == null) return;
            Debug.Log($"{Tag} tick {s.Stepped} lead {s.Lead} (min {s.LeadMin}, max {s.LeadMax}) late {s.LateInputs} stall {s.StallMs} ms status {s.Status}");
        }

        float Left() => Math.Max(1f, _deadline - Time.realtimeSinceStartup);

        static int Confirmed()
        {
            int n = 0;
            if (OnlineMatch.Host != null) foreach (RoomMember m in OnlineMatch.Host.Members) if (m.Confirmed) n++;
            return n;
        }

        IEnumerator Shoot(string name, Func<bool> reached)
        {
            _step++;
            string id = _step.ToString("00") + "-" + name;
            yield return new WaitForSecondsRealtime(0.5f);
            bool ok;
            try { ok = reached(); } catch (Exception) { ok = false; }
            if (!ok) { _missed++; Debug.Log($"{Tag} {id} NOT REACHED"); yield break; }
            foreach (FlowScreen f in FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) f.UpdateScale();
            GameHud hud = FindAnyObjectByType<GameHud>();
            if (hud != null) hud.UpdateScale();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(GamePaths.DataDir, $"shot-online-relay-{id}-{Screen.width}x{Screen.height}.png"), tex.EncodeToPNG()); }
            catch (Exception e) { Debug.LogWarning($"{Tag} {id}: could not write the still: {e.Message}"); }
            finally { Destroy(tex); }
            _reached++;
            Debug.Log($"{Tag} {id} REACHED");
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
