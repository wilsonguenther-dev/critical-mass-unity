using System;
using CriticalMass.Net;
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
    /// b13-replay: every played match saves a replay. Installs ONE ReplayRecorder as MatchTick.Tap (the single point where
    /// inputs enter the sim; the single-player driver and the online lockstep both pass through it), and decides, once per
    /// new world at its tick 0, whether that world is recorded:
    /// <list type="bullet">
    /// <item>an online room's world (OnlineMatch.Session.World): start conditions from its MatchSetup, every human's
    /// decoded wire frames and hand-overs to bots;</item>
    /// <item>the match the flow started (driver.Request, not a backdrop, not a capture-mode world, not a replay);</item>
    /// <item>everything else (title/menu backdrops, tests, capture tools, hidden online peers, replays) is never recorded.</item>
    /// </list>
    /// The match end (final whistle) saves at once on a worker thread; leaving early saves if at least MinAbandonedSeconds
    /// of sim time were recorded. A match whose config was changed outside its mode rules (so its start cannot be rebuilt)
    /// is not saved and says so in the log. -noReplay turns recording off. Read-only on the sim.
    /// </summary>
    public static class ReplayCapture
    {
        public const string NoReplayFlag = "-noReplay";
        /// <summary>A match left before the end is kept only past this many sim seconds.</summary>
        public const double MinAbandonedSeconds = 30;
        const string Tag = "[CM.Replay]";

        static readonly ReplayRecorder Recorder = new ReplayRecorder();
        static readonly Tap TheTap = new Tap();
        static World _rejected;
        static bool _off;

        /// <summary>The path of the last replay saved this session (null: none yet). Written by the worker thread.</summary>
        public static volatile string LastSaved;

        /// <summary>True while a world is being recorded.</summary>
        public static bool Recording => Recorder.Recording;
        public static World RecordingWorld => Recorder.World;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            Recorder.Cancel();
            _rejected = null;
            LastSaved = null;
            _off = HasFlag(NoReplayFlag) || Application.isBatchMode && !HasFlag("-replayRecord");
            MatchTick.Tap = _off ? null : TheTap;
        }

        static bool HasFlag(string flag)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The tap: binds new worlds at tick 0, forwards to the recorder (which ignores every other world).</summary>
        sealed class Tap : IMatchInputTap
        {
            public void OnAdvance(World w, InputFrame human) { if (Watch(w)) Recorder.OnAdvance(w, human); }
            public void OnAdvanceHumans(World w, InputFrame[] frames) { if (Watch(w)) Recorder.OnAdvanceHumans(w, frames); }
            public void OnAdvanceNoHuman(World w) { if (Watch(w)) Recorder.OnAdvanceNoHuman(w); }
            public void OnHandToBot(World w, int id) { if (Watch(w)) Recorder.OnHandToBot(w, id); }
            public void OnBotToHuman(World w, int id) { if (Watch(w)) Recorder.OnBotToHuman(w, id); }   // b15-rejoin
        }

        /// <summary>Per tick: two reference compares for every world but a new one at tick 0.</summary>
        static bool Watch(World w)
        {
            if (ReferenceEquals(w, Recorder.World))
            {
                if (w.Match.Phase == MatchPhase.Ended) { End(w, true); return false; }
                return true;
            }
            if (ReferenceEquals(w, _rejected) || w.Tick != 0) return false;
            return Decide(w);
        }

        static bool Decide(World w)
        {
            _rejected = w;
            try
            {
                LockstepSession online = Online.OnlineMatch.Session;
                if (online != null && ReferenceEquals(online.World, w)) return BeginOnline(w, online);
                FlowDirector fd = FlowDirector.Current;
                SimDriverBehaviour d = fd != null ? fd.Driver : null;
                if (d == null || !ReferenceEquals(d.World, w) || d.IsReplay || d.IsOnline || d.CreatedInCaptureMode) return false;
                MatchRequest req = d.Request;
                if (req == null || req.Backdrop || req.Replay) return false;
                return BeginOffline(w, req);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} not recorded: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        static bool BeginOffline(World w, MatchRequest req)
        {
            // The world's own options (World.Opts: exactly what it was created from) plus the config recipe: the default
            // factory and the mode's match rules (MatchRequest.ToWorldOptions applies only those).
            ReplayStart s = ReplayStart.From(w.Opts, ReplayKind.Offline, ConfigBasis.Default, -1,
                req.Rules.MatchSeconds, req.Rules.HatchSeconds, req.Rules.PassiveBotSeconds, 0);
            ModeInfo info = PlayModes.Info(req.Mode);
            ReplayHeader h = NewHeader(w, s, (int)req.Mode, info != null ? info.Title : "", 30);
            string me = "";
            try { me = Progression.Profile.Current.PlayerName ?? ""; } catch (Exception) { }
            if (h.Roster.Length > 0) h.Roster[0].Name = me;
            return Bind(w, h);
        }

        static bool BeginOnline(World w, LockstepSession s)
        {
            ReplayStart st = ReplayStart.From(w.Opts, ReplayKind.Online, ConfigBasis.Default, -1, 0, 0, 0, s.LocalActor);
            ReplayHeader h = NewHeader(w, st, (int)MatchMode.Custom, "ONLINE", s.Setup.HashEvery);
            foreach (HumanSlot slot in s.Setup.Humans)
                if (slot.Actor >= 0 && slot.Actor < h.Roster.Length) h.Roster[slot.Actor].Name = slot.Name ?? "";
            return Bind(w, h);
        }

        static ReplayHeader NewHeader(World w, ReplayStart s, int mode, string modeLabel, int hashEvery)
        {
            var h = new ReplayHeader
            {
                BuildVersion = BuildInfo.Version,
                BuildCommit = BuildInfo.Commit,
                MapName = PlayModes.MapName,
                Mode = mode,
                ModeLabel = modeLabel ?? "",
                HashEvery = hashEvery > 0 ? hashEvery : 30,
                Start = s,
                Roster = new ReplayActor[w.ActorCount],
            };
            for (int i = 0; i < w.ActorCount; i++)
            {
                h.Roster[i].Team = w.Actors[i].Team;
                h.Roster[i].Class = w.Actors[i].Cls;
                h.Roster[i].Human = w.Actors[i].IsHuman;
                h.Roster[i].Name = "";
            }
            return h;
        }

        static bool Bind(World w, ReplayHeader h)
        {
            if (Recorder.Recording) End(Recorder.World, false);   // a new match began while the old one was still open
            if (!Recorder.Begin(w, h))
            {
                Debug.Log($"{Tag} not recorded: {Recorder.Refused}");
                return false;
            }
            _rejected = null;
            return true;
        }

        /// <summary>The match in w ended (final whistle) or was left: stop and save.</summary>
        public static void End(World w, bool finished)
        {
            if (w == null || !ReferenceEquals(w, Recorder.World)) return;
            ReplayData d = Recorder.Finish(finished, DateTime.UtcNow.Ticks);
            if (d == null) return;
            if (!finished && d.Header.SimSeconds < MinAbandonedSeconds)
            {
                Debug.Log($"{Tag} not saved: left after {d.Header.SimSeconds:0} s (keeps {MinAbandonedSeconds:0} s or more)");
                return;
            }
            // The start must be rebuildable: a world built from the recorded start conditions has the recorded config hash
            // and the recorded tick-0 fingerprint (a guided match or tour that tuned its config outside its mode rules fails
            // here and is not saved). Once per match, on the main thread (the sim's shared scratch is main-thread only).
            string bad = RebuildProblem(d);
            if (bad != null)
            {
                Debug.Log($"{Tag} not saved: {bad}");
                return;
            }
            ReplayStore.SaveAsync(d, path => { if (path != null) LastSaved = path; });
        }

        /// <summary>Null when the replay's start rebuilds the recorded world exactly; otherwise why not (pure sim; tests).</summary>
        public static string RebuildProblem(ReplayData d)
        {
            if (d.FingerprintCount == 0 || d.FingerprintTicks[0] != 0) return "no tick-0 fingerprint";
            World probe = World.CreateWorld(d.Header.Start.ToWorldOptions(d.Header.Start.BuildConfig()));
            ulong cfg = ConfigHash.Of(probe.Config);
            if (cfg != d.Header.ConfigHash) return $"this match's rules were changed outside its mode (config 0x{d.Header.ConfigHash:X16}, rebuilt 0x{cfg:X16})";
            ulong h0 = StateHash.Of(probe);
            if (h0 != d.Fingerprints[0]) return $"the rebuilt arena differs (tick 0 0x{d.Fingerprints[0]:X16}, rebuilt 0x{h0:X16})";
            return null;
        }

        /// <summary>The match scene is going away (results, menu, quit, a new match): a match still open is saved as left early.</summary>
        public static void Abandon(World w) => End(w, w != null && w.Match.Phase == MatchPhase.Ended);
    }

    /// <summary>
    /// b13-replay: on every match scene: notices the final whistle right after the tick that blew it (an online session
    /// may stop stepping at its end, so the tap alone would never see the ended world) and saves a match still open when
    /// the scene goes away.
    /// </summary>
    [GameOverlay(Order = 95, Scope = OverlayScope.MatchOnly)]
    public sealed class ReplayCaptureHook : MonoBehaviour
    {
        SimDriverBehaviour _driver;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver != null) _driver.Ticked += OnTicked;
        }

        void OnTicked(World w)
        {
            if (w.Match.Phase == MatchPhase.Ended && ReferenceEquals(w, ReplayCapture.RecordingWorld)) ReplayCapture.End(w, true);
        }

        void OnDestroy()
        {
            if (_driver == null) return;
            _driver.Ticked -= OnTicked;
            if (_driver.World != null && ReferenceEquals(_driver.World, ReplayCapture.RecordingWorld)) ReplayCapture.Abandon(_driver.World);
        }
    }
}
