using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Net;
using CriticalMass.Net.Relay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-4: -onlineShots walks every online screen and writes a still of each (1920x1080 and 1280x720) into the
    /// data folder as shot-online-NN-NAME-WxH.png, with one "[CM.OnlineShots] NN-NAME REACHED / NOT REACHED" line per
    /// step, then quits. Nothing touches the network: the room is a real RoomHost plus fake joiners (real RoomClients)
    /// on an in-process LocalRelay, so names, teams, ready states, pings and chat lines are the real code paths.
    /// Steps: Play Online home (name line), join with a typed code, connecting, the lobby with 1, 3 and 8 people
    /// (ready and not ready), the joiner's view while waiting, the chat panel (a long line, a filtered word, a link), the
    /// "version too old" and "host closed the room" messages, the in-match strip and panels (waiting, CANNOT START, host
    /// left), and a real online match with chat on the HUD lane. Every text is a contained box (MenuUi.Contain / TextFit).
    /// </summary>
    public sealed class OnlineShots : MonoBehaviour
    {
        public const string Flag = "-onlineshots";   // lower case: AutoplayArgs compares lower-cased args
        const string Tag = "[CM.OnlineShots]";
        const int WideW = 1920, WideH = 1080, SmallW = 1280, SmallH = 720;

        static readonly string[] FakeNames = { "JEISIL", "TRAVIS", "AIDAN", "MAX", "SHAYDEN", "CALEB", "FRANK" };
        LocalRelay _relay;
        readonly List<RoomClient> _fakes = new List<RoomClient>();
        readonly List<ITransport> _fakeLinks = new List<ITransport>();
        int _step, _reached, _missed;
        static FlowDirector D => FlowDirector.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !AutoplayArgs.HasAutomationFlag(Flag)) return;
            GameFlow.ForceEnabled = true;
            Menus.StoryIntro.PlayOnFirstLaunch = false;
            var go = new GameObject("CriticalMass OnlineShots");
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineShots>();
            Debug.Log($"{Tag} on: stills of every online screen into {GamePaths.DataDir}");
        }

        void Update()
        {
            using var __ab = CriticalMass.Runtime.Game.AllocBy.Upd(this);
            if (_relay == null) return;
            long now = OnlineMatch.NowMs;
            _relay.Advance(now);
            foreach (RoomClient c in _fakes) c.Update(now);
        }

        IEnumerator Start()
        {
            SetSize(WideW, WideH);
            yield return WaitFor(() => D != null && !D.Loading && D.MainMenu != null, 60f);
            if (GameFlow.State == FlowState.Title) { GameFlow.ReturnTo(FlowState.Menu); }
            yield return WaitFor(() => D != null && D.MainMenu != null && D.MainMenu.IsVisible, 20f);
            MenuEntry entry = OverlayRegistry.MenuEntries().Find(e => e.Id == "online");
            if (entry != null) D.OpenRegistered(entry);
            OnlineMenuScreen screen = null;
            yield return WaitFor(() => (screen = FindAnyObjectByType<OnlineMenuScreen>()) != null && screen.IsVisible, 10f);
            if (screen == null) { Debug.LogError($"{Tag} the Play Online screen did not open"); Application.Quit(1); yield break; }

            screen.CapturePage("home", "");
            yield return Shoot("home", () => true);
            screen.CapturePage("join", "", "K7Q2PF");
            yield return Shoot("join-typed-code", () => true);
            screen.CapturePage("join", "A room code is 6 letters and numbers, for example K7Q2PF.", "K7Q");
            yield return Shoot("join-bad-code", () => true);
            screen.CapturePage("busy", "Making a room...");
            yield return Shoot("connecting", () => true);

            // A real room on the in-process relay: the host is this game, the others are fake joiners.
            _relay = new LocalRelay(25);
            (string code, string key) = _relay.CreateRoom();
            OnlineMatch.HostRoom(_relay.Connect(code, key), code, "WILSON");
            yield return WaitFor(() => OnlineMatch.Host != null && OnlineMatch.Transport.LocalPeer == Peers.Host, 5f);
            OnlineMatch.Host.TeamSize = 16;
            screen.CapturePage("room");
            yield return Shoot("lobby-1-host-alone", () => OnlineMatch.InRoom);
            AddFakes(2, code);
            yield return WaitFor(() => Confirmed() >= 3, 8f);
            _fakes[0].SetReady(true);
            yield return new WaitForSecondsRealtime(1.2f);
            screen.CapturePage("room");
            yield return Shoot("lobby-3-one-not-ready", () => Confirmed() >= 3);
            AddFakes(5, code);
            yield return WaitFor(() => Confirmed() >= 8, 8f);
            foreach (RoomClient c in _fakes) c.SetReady(true);
            yield return new WaitForSecondsRealtime(1.2f);
            screen.CapturePage("room");
            yield return Shoot("lobby-8-all-ready", () => Confirmed() >= 8);

            // Chat in the room: an ordinary line, a long one, a filtered word, a link, a team line.
            long now = OnlineMatch.NowMs;
            _fakes[0].Chat.Say("hey, I'm in. Azure or crimson?", ChatScope.All, now);
            _fakes[1].Chat.Say("This is a deliberately long chat line to prove it stays on one line inside its own box, however much anyone types", ChatScope.All, now);
            _fakes[2].Chat.Say("you are a b1tch lol", ChatScope.All, now);
            _fakes[3].Chat.Say("join my server at www.example.com", ChatScope.All, now);
            OnlineMatch.Host.Chat.Say("ready when you are", ChatScope.All, now);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shoot("lobby-chat", () => OnlineMatch.Host.Chat.Lines.Count >= 4);

            // A full room: 32 people (two table columns), joins and leaves as chat notices.
            AddFakes(24, code);
            yield return WaitFor(() => Confirmed() >= 32, 15f);
            foreach (RoomClient c in _fakes) c.SetReady(true);
            yield return new WaitForSecondsRealtime(1.5f);
            screen.CapturePage("room");
            yield return Shoot("lobby-32-full", () => Confirmed() >= 32);
            for (int i = _fakes.Count - 1; i >= 8; i--) { _fakeLinks[i].Close(); _fakes.RemoveAt(i); _fakeLinks.RemoveAt(i); }
            yield return WaitFor(() => Confirmed() == 9, 8f);

            // The messages a player can meet, as the screens show them.
            screen.CapturePage("home", "Your game is a different version from the host's (1.0.509 vs 1.0.512). Both of you need the same update.");
            yield return Shoot("message-version-too-old", () => true);
            screen.CapturePage("home", "The host closed the room.");
            yield return Shoot("message-host-closed-room", () => true);
            screen.CapturePage("home", RelayTransport.NotFoundText);
            yield return Shoot("message-no-such-room", () => true);
            screen.CapturePage("room");

            // The match overlay's strip and panels (match-only: shown without the room screen, as a player meets them).
            screen.Hide();
            var overlay = new GameObject("Online Overlay Preview").AddComponent<OnlineMatchOverlay>();
            yield return null;
            overlay.Preview(null, null, "WAITING FOR A SLOW CONNECTION...");
            yield return Shoot("match-waiting-strip", () => true);
            overlay.Preview("CANNOT START", "Your game's match rules differ from the host's. Both players need the same update.");
            yield return Shoot("match-cannot-start", () => true);
            overlay.Preview("MATCH ENDED", "The host left the match or lost their connection.");
            yield return Shoot("match-host-left", () => true);
            Destroy(overlay.gameObject);

            // A real online match with two fake joiners, chat on the HUD lane.
            // Five of the fakes leave first (fewer worlds to step on this machine): the match is the host plus two joiners.
            for (int i = _fakes.Count - 1; i >= 2; i--) { _fakeLinks[i].Close(); _fakes.RemoveAt(i); _fakeLinks.RemoveAt(i); }
            yield return WaitFor(() => Confirmed() == 3, 5f);
            yield return new WaitForSecondsRealtime(1.2f);
            string why = OnlineMatch.StartAsHost();
            if (why != null) Debug.LogWarning($"{Tag} could not start the match: {why}");
            yield return WaitFor(() => GameFlow.State == FlowState.Match && FindAnyObjectByType<SimDriverBehaviour>() is SimDriverBehaviour d && d.IsOnline && !LoadingScreenOpen(), 60f);
            foreach (RoomClient c in _fakes) if (c.Session != null) c.Session.Sampler = new AutoplayPilot(7).Fill;
            yield return new WaitForSecondsRealtime(3f);
            now = OnlineMatch.NowMs;
            _fakes[0].Chat.Say("push the hill!", ChatScope.Team, now);
            _fakes[1].Chat.Say("gg everyone, that kaiju is huge", ChatScope.All, now);
            OnlineMatch.Host.Chat.Say("on my way", ChatScope.All, now);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shoot("match-hud-chat", () => OnlineMatch.InMatch);

            Debug.Log($"{Tag} done: {_reached} reached, {_missed} not reached");
            OnlineMatch.Leave();
            Application.Quit(_missed == 0 ? 0 : 1);
        }

        static bool LoadingScreenOpen() => Menus.LoadingScreen.IsOpen;

        void AddFakes(int n, string code)
        {
            for (int i = 0; i < n; i++)
            {
                int k = _fakes.Count;
                ITransport link = _relay.Connect(code, null);
                string fake = FakeNames[k % FakeNames.Length] + (k >= FakeNames.Length ? (k / FakeNames.Length + 1).ToString() : "");
                var c = new RoomClient(link, GameConfig.CreateDefault(), fake, OnlineConfig.Build);
                _fakeLinks.Add(link);
                c.SetPick(k % 2 == 0 ? TeamId.Crimson : TeamId.Azure, ClassIds.All[k % ClassIds.All.Count]);
                _fakes.Add(c);
            }
        }

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
            foreach (FlowScreen s in FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) s.UpdateScale();
            GameHud hud = FindAnyObjectByType<GameHud>();
            if (hud != null) hud.UpdateScale();
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(GamePaths.DataDir, $"shot-online-{id}-{Screen.width}x{Screen.height}.png"), tex.EncodeToPNG()); }
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
