using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Audio
{
    /// <summary>What happened in the match that the announcer may call. One trigger plays exactly one line.</summary>
    public enum AnnouncerTrigger
    {
        MatchStart, HillTaken, HillTakenByEnemy, HillLost, HillContested, FinalMinute, EggSoon, HatchAzure, HatchCrimson,
        HatchNeutral, BerserkLocked, BerserkHatch, KaijuKnockedOut, KaijuRecovers, FirstKnockOut, MultiKo2, MultiKo3,
        MultiKo4, Victory, Defeat, Draw, SecretHillAlone,
    }

    /// <summary>One announcer line: words-pack id (announcer.json), trigger, priority (higher wins a tie) and text.</summary>
    public sealed class AnnouncerLine
    {
        public string Id;
        public AnnouncerTrigger Trigger;
        public int Priority;
        public string Text;
        /// <summary>The baked voice clip (Resources CMVoice/&lt;Id&gt;), or null when it is missing.</summary>
        public AudioClip Clip;
    }

    /// <summary>
    /// The announcer's lines. Built-in copies of the words pack (Assets/_Game/Content/Lore/announcer.json, DG-04, schema
    /// cm.lore.announcer/1) so the game speaks even without it; ApplyWordsPack overrides them from the JSON.
    /// </summary>
    public static class AnnouncerLines
    {
        /// <summary>The words pack's trigger names, in AnnouncerTrigger order.</summary>
        public static readonly string[] TriggerNames =
        {
            "matchStart", "hillTaken", "hillTakenByEnemy", "hillLost", "hillContested", "finalMinute", "eggSoon", "hatchAzure",
            "hatchCrimson", "hatchNeutral", "berserkLocked", "berserkHatch", "kaijuKnockedOut", "kaijuRecovers",
            "firstKnockOut", "multiKo2", "multiKo3", "multiKo4", "victory", "defeat", "draw", "secretHillAlone",
        };

        /// <summary>Indexed by (int)AnnouncerTrigger.</summary>
        public static readonly AnnouncerLine[] Table =
        {
            L("an-01", AnnouncerTrigger.MatchStart, 2, "Banners up. The hill is open."),
            L("an-02", AnnouncerTrigger.HillTaken, 2, "Hill taken. Now keep it."),
            L("an-03", AnnouncerTrigger.HillTakenByEnemy, 2, "They have the hill. Take it back."),
            L("an-04", AnnouncerTrigger.HillLost, 2, "Hill lost."),
            L("an-05", AnnouncerTrigger.HillContested, 1, "The hill is contested."),
            L("an-06", AnnouncerTrigger.FinalMinute, 3, "One minute left. Make it count."),
            L("an-07", AnnouncerTrigger.EggSoon, 3, "The egg is cracking!"),
            L("an-08", AnnouncerTrigger.HatchAzure, 3, "The Kaiju sides with Azure!"),
            L("an-09", AnnouncerTrigger.HatchCrimson, 3, "The Kaiju sides with Crimson!"),
            L("an-10", AnnouncerTrigger.HatchNeutral, 3, "Dead even. The Kaiju sides with nobody."),
            L("an-11", AnnouncerTrigger.BerserkLocked, 3, "Too much corruption. That egg is going to hatch angry."),
            L("an-12", AnnouncerTrigger.BerserkHatch, 3, "Berserk! It wants everyone."),
            L("an-13", AnnouncerTrigger.KaijuKnockedOut, 3, "The Kaiju is down!"),
            L("an-14", AnnouncerTrigger.KaijuRecovers, 2, "The Kaiju is back up. It remembers."),
            L("an-15", AnnouncerTrigger.FirstKnockOut, 1, "First knock-out of the match."),
            L("an-16", AnnouncerTrigger.MultiKo2, 1, "Double knock-out!"),
            L("an-17", AnnouncerTrigger.MultiKo3, 1, "Triple knock-out!"),
            L("an-18", AnnouncerTrigger.MultiKo4, 1, "Four down. Somebody stop them."),
            L("an-19", AnnouncerTrigger.Victory, 3, "Victory. The hill is yours."),
            L("an-20", AnnouncerTrigger.Defeat, 3, "Defeat. Regroup, and climb again."),
            L("an-21", AnnouncerTrigger.Draw, 3, "A draw. The hill will take a rematch."),
            L("an-22", AnnouncerTrigger.SecretHillAlone, 1, "Still up there on your own? Respect."),
        };

        static AnnouncerLine L(string id, AnnouncerTrigger t, int p, string text) =>
            new AnnouncerLine { Id = id, Trigger = t, Priority = p, Text = text };

        public static AnnouncerLine For(AnnouncerTrigger t) => Table[(int)t];

        /// <summary>The trigger for a words-pack trigger name, or -1.</summary>
        public static int TriggerIndex(string name) => Array.IndexOf(TriggerNames, name);

        [Serializable] public sealed class PackRow { public string id; public string trigger; public int priority; public string text; }
        [Serializable] public sealed class Pack { public string schema; public PackRow[] lines; }

        /// <summary>
        /// Overrides ids, priorities and texts from an announcer.json (schema cm.lore.announcer/1); rows with an unknown
        /// trigger or empty text are skipped. Returns the number of lines applied (0 on bad JSON).
        /// </summary>
        public static int ApplyWordsPack(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            Pack p;
            try { p = JsonUtility.FromJson<Pack>(json); }
            catch (ArgumentException) { return 0; }
            if (p == null || p.lines == null) return 0;
            int n = 0;
            foreach (PackRow r in p.lines)
            {
                int i = r != null ? TriggerIndex(r.trigger) : -1;
                if (i < 0 || string.IsNullOrEmpty(r.text) || string.IsNullOrEmpty(r.id)) continue;
                AnnouncerLine line = Table[i];
                if (line.Id != r.id) line.Clip = null;
                line.Id = r.id;
                line.Priority = r.priority;
                line.Text = r.text;
                n++;
            }
            return n;
        }
    }

    /// <summary>
    /// The anti-spam rule, pure and clock-driven (tests pass their own clock): a trigger already waiting is not queued
    /// twice; a line waits at most MaxAgeSeconds, then it is dropped; a new line never starts within CooldownSeconds of
    /// the last line's start; the highest priority waiting line goes first, the oldest on a tie. No allocation after
    /// construction.
    /// </summary>
    public sealed class AnnouncerQueue
    {
        public const double CooldownSeconds = 6.0;
        public const double MaxAgeSeconds = 2.0;
        public const int Capacity = 8;
        public const int HistoryCapacity = 256;

        public struct Play
        {
            public double Time;
            public AnnouncerTrigger Trigger;
            public string Id;
        }

        readonly AnnouncerTrigger[] _trig = new AnnouncerTrigger[Capacity];
        readonly double[] _at = new double[Capacity];
        int _count;
        double _lastStart = double.NegativeInfinity;
        readonly Play[] _history = new Play[HistoryCapacity];
        int _historyCount;

        /// <summary>Lines waiting.</summary>
        public int Pending => _count;
        /// <summary>Lines dropped (too old, or pushed out of a full queue).</summary>
        public int Dropped { get; private set; }
        /// <summary>Lines started, all time (the history keeps the last HistoryCapacity).</summary>
        public int Started { get; private set; }
        public int HistoryCount => Math.Min(_historyCount, HistoryCapacity);

        /// <summary>The i-th of the kept plays, oldest first.</summary>
        public Play HistoryAt(int i)
        {
            int start = _historyCount > HistoryCapacity ? _historyCount - HistoryCapacity : 0;
            return _history[(start + i) % HistoryCapacity];
        }

        public bool IsPending(AnnouncerTrigger t)
        {
            for (int i = 0; i < _count; i++) if (_trig[i] == t) return true;
            return false;
        }

        /// <summary>Queues a trigger at time now; false when it was already waiting.</summary>
        public bool Enqueue(AnnouncerTrigger t, double now)
        {
            if (IsPending(t)) return false;
            if (_count == Capacity)
            {
                // Full: push out the lowest priority, oldest line.
                int worst = 0;
                for (int i = 1; i < _count; i++)
                {
                    int pi = AnnouncerLines.For(_trig[i]).Priority, pw = AnnouncerLines.For(_trig[worst]).Priority;
                    if (pi < pw || (pi == pw && _at[i] < _at[worst])) worst = i;
                }
                RemoveAt(worst);
                Dropped++;
            }
            _trig[_count] = t;
            _at[_count] = now;
            _count++;
            return true;
        }

        /// <summary>Drops lines older than MaxAgeSeconds.</summary>
        public void Expire(double now)
        {
            for (int i = _count - 1; i >= 0; i--)
                // Finisher (batch 3, sim-long FullMatchKeepsTheRule): the result never goes stale. A line that started under
                // 6 s before the end held the result past MaxAgeSeconds and it was dropped, so the match ended uncalled.
                if (now - _at[i] > MaxAgeSeconds && !IsResult(_trig[i]))
                {
                    RemoveAt(i);
                    Dropped++;
                }
        }

        /// <summary>The line to start now, if the cooldown allows one; it is recorded in the history.</summary>
        static bool IsResult(AnnouncerTrigger t) =>
            t == AnnouncerTrigger.Victory || t == AnnouncerTrigger.Defeat || t == AnnouncerTrigger.Draw;

        /// <summary>True when a line queued now could start now (the cooldown since the last start has run out).</summary>
        public bool CooledDown(double now) => now - _lastStart >= CooldownSeconds;

        public bool TryNext(double now, out AnnouncerLine line)
        {
            line = null;
            Expire(now);
            if (_count == 0 || now - _lastStart < CooldownSeconds) return false;
            int best = 0;
            for (int i = 1; i < _count; i++)
            {
                int pi = AnnouncerLines.For(_trig[i]).Priority, pb = AnnouncerLines.For(_trig[best]).Priority;
                if (pi > pb || (pi == pb && _at[i] < _at[best])) best = i;
            }
            AnnouncerTrigger t = _trig[best];
            RemoveAt(best);
            line = AnnouncerLines.For(t);
            MarkStarted(line, now);
            return true;
        }

        /// <summary>Records a line started outside the queue (a forced line): it restarts the cooldown.</summary>
        public void MarkStarted(AnnouncerLine line, double now)
        {
            _lastStart = now;
            _history[_historyCount % HistoryCapacity] = new Play { Time = now, Trigger = line.Trigger, Id = line.Id };
            _historyCount++;
            Started++;
        }

        public void Clear()
        {
            _count = 0;
        }

        /// <summary>Forgets everything (a new match).</summary>
        public void Reset()
        {
            _count = 0;
            _lastStart = double.NegativeInfinity;
            _historyCount = 0;
            Started = 0;
            Dropped = 0;
        }

        void RemoveAt(int i)
        {
            for (int k = i; k < _count - 1; k++)
            {
                _trig[k] = _trig[k + 1];
                _at[k] = _at[k + 1];
            }
            _count--;
        }
    }

    /// <summary>
    /// Turns the sim's event ring and match state into announcer triggers. Reads only; never writes the world. The
    /// human is actor 0. Hill: "taken" when the owner changes to a team, "lost" when our hill goes neutral, "contested"
    /// only for a held hill. Multi knock-outs count the human's own knock-outs within MultiKoWindowSeconds of sim time.
    /// </summary>
    public sealed class AnnouncerDecoder
    {
        public const double FinalMinuteSeconds = 60;
        public const double EggSoonSeconds = 30;
        public const double MultiKoWindowSeconds = 4;

        World _world;
        int _cursor;
        bool _started, _finalCalled, _eggCalled, _firstKoCalled, _endCalled;
        int _owner;               // 0 none, 1 Azure, 2 Crimson
        int _koCount;
        double _lastKoAt = double.NegativeInfinity;

        public World Current => _world;

        /// <summary>Starts decoding a world from its current event head (earlier events are not replayed).</summary>
        public void Reset(World w)
        {
            _world = w;
            _cursor = w != null ? w.EventHead : 0;
            _started = _finalCalled = _eggCalled = _firstKoCalled = _endCalled = false;
            _owner = 0;
            if (w != null)
            {
                TeamId? held = SimEnumMap.TeamOf(w.Match.HillState);
                _owner = held.HasValue ? (held.Value == TeamId.Azure ? 1 : 2) : 0;
            }
            _koCount = 0;
            _lastKoAt = double.NegativeInfinity;
        }

        /// <summary>Reads everything new since the last poll and queues its triggers at time now.</summary>
        public void Poll(World w, AnnouncerQueue q, double now)
        {
            if (w == null) return;
            if (!ReferenceEquals(w, _world) || w.EventHead < _cursor) Reset(w);
            TeamId me = w.ActorCount > 0 ? w.Actors[0].Team : TeamId.Azure;
            MatchState m = w.Match;
            if (!_started)
            {
                _started = true;
                if (m.Phase == MatchPhase.Playing) q.Enqueue(AnnouncerTrigger.MatchStart, now);
            }

            int head = w.EventHead;
            if (head - _cursor > World.EventCapacity) _cursor = head - World.EventCapacity;
            double simNow = m.Time;
            for (; _cursor < head; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                // Integration batch 3 (sim-long FullMatchKeepsTheRule): once the result is called nothing else is queued.
                // Events written in the same tick after MatchEnd (a hill-contested flip) used to play after "Victory".
                if (_endCalled && e.Type != SimEventType.MatchEnd) continue;
                switch (e.Type)
                {
                    case SimEventType.HillOwnerChange: OnHill(e.A, me, q, now); break;
                    case SimEventType.Hatch:
                    {
                        int code = (int)e.Value;
                        q.Enqueue(code == 1 ? AnnouncerTrigger.HatchAzure : code == 2 ? AnnouncerTrigger.HatchCrimson :
                            code == 3 ? AnnouncerTrigger.BerserkHatch : AnnouncerTrigger.HatchNeutral, now);
                        _eggCalled = true;
                        break;
                    }
                    case SimEventType.BerserkLocked: q.Enqueue(AnnouncerTrigger.BerserkLocked, now); break;
                    case SimEventType.KaijuKO: q.Enqueue(AnnouncerTrigger.KaijuKnockedOut, now); break;
                    case SimEventType.KaijuRecover: q.Enqueue(AnnouncerTrigger.KaijuRecovers, now); break;
                    case SimEventType.Death:
                        if (!_firstKoCalled)
                        {
                            _firstKoCalled = true;
                            q.Enqueue(AnnouncerTrigger.FirstKnockOut, now);
                        }
                        if (e.B == 0 && e.A != 0)
                        {
                            _koCount = simNow - _lastKoAt <= MultiKoWindowSeconds ? _koCount + 1 : 1;
                            _lastKoAt = simNow;
                            if (_koCount >= 4) q.Enqueue(AnnouncerTrigger.MultiKo4, now);
                            else if (_koCount == 3) q.Enqueue(AnnouncerTrigger.MultiKo3, now);
                            else if (_koCount == 2) q.Enqueue(AnnouncerTrigger.MultiKo2, now);
                        }
                        break;
                    case SimEventType.MatchEnd: End(m, me, q, now); break;
                }
            }

            if (m.Phase != MatchPhase.Playing)
            {
                End(m, me, q, now);
                return;
            }
            MatchClock c = Match.GetMatchClock(w);
            if (!_finalCalled && w.Config.MatchSeconds > FinalMinuteSeconds && c.Remaining <= FinalMinuteSeconds)
            {
                _finalCalled = true;
                q.Enqueue(AnnouncerTrigger.FinalMinute, now);
            }
            if (!_eggCalled && !m.Hatched && w.Config.HatchSeconds > EggSoonSeconds && c.UntilHatch <= EggSoonSeconds)
            {
                _eggCalled = true;
                q.Enqueue(AnnouncerTrigger.EggSoon, now);
            }
        }

        void OnHill(int code, TeamId me, AnnouncerQueue q, double now)
        {
            int mine = me == TeamId.Azure ? 1 : 2;
            if (code == 1 || code == 2)
            {
                if (code != _owner) q.Enqueue(code == mine ? AnnouncerTrigger.HillTaken : AnnouncerTrigger.HillTakenByEnemy, now);
                _owner = code;
            }
            else if (code == 0)
            {
                if (_owner == mine) q.Enqueue(AnnouncerTrigger.HillLost, now);
                _owner = 0;
            }
            else if (code == 3 && _owner != 0) q.Enqueue(AnnouncerTrigger.HillContested, now);
        }

        void End(MatchState m, TeamId me, AnnouncerQueue q, double now)
        {
            if (_endCalled || m.Phase != MatchPhase.Ended) return;
            _endCalled = true;
            // The match is over: whatever was waiting is stale next to the result.
            q.Clear();
            AnnouncerTrigger t = m.Winner == MatchResult.Draw ? AnnouncerTrigger.Draw :
                SimEnumMap.TeamOf(m.Winner) == me ? AnnouncerTrigger.Victory : AnnouncerTrigger.Defeat;
            if (m.Winner != MatchResult.None) q.Enqueue(t, now);
        }
    }

    /// <summary>
    /// DG-18 announcer: decodes the match (AnnouncerDecoder), applies the anti-spam rule (AnnouncerQueue: 6 s between
    /// lines, lines older than 2 s dropped), plays the baked voice line on the Effects mixer group, ducks the music, and
    /// shows a subtitle for every line (bottom centre, chunky Panel Navy sticker, Warm Cream bold text). View only.
    /// Self-installed on the persistent "CM Audio" object (AudioHost). Silent while the title screen or the pause menu
    /// is up.
    /// <para>Public API for other items: <see cref="Say"/> (queue a trigger, e.g. DG-26's secret line),
    /// <see cref="ForceLine"/> (captures), <see cref="SubtitlesEnabled"/>, <see cref="LineStarted"/>,
    /// <see cref="IsSpeaking"/>, <see cref="CurrentText"/>.</para>
    /// </summary>
    public sealed class Announcer : MonoBehaviour
    {
        public const string SubtitlesKey = "cm.subtitles";
        public const string VoiceFolder = "CMVoice/";
        public const float SubtitleTailSeconds = 0.5f;
        public const float FadeSeconds = 0.15f;

        /// <summary>Raised when a line starts (subtitle shown), with the line.</summary>
        public static event Action<AnnouncerLine> LineStarted;

        public static Announcer Instance { get; private set; }

        /// <summary>Subtitles for announcer lines (PlayerPrefs cm.subtitles, default on).</summary>
        public static bool SubtitlesEnabled
        {
            // Integration batch 2: the ACCESS tab's SUBTITLES line (SettingsExtras) switches these off too.
            get => PlayerPrefs.GetInt(SubtitlesKey, 1) != 0 && CriticalMass.Runtime.Menu.SettingsExtras.Subtitles;
            set
            {
                PlayerPrefs.SetInt(SubtitlesKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Free to speak a low-priority line now (nothing playing, nothing queued). A queued line ages out after
        /// AnnouncerQueue.MaxAgeSeconds, so a rare line (the hill secret) is only handed over when it will really be said.
        /// </summary>
        /// hud-art-1 merge: also out of its cooldown. A line handed over inside the 6 s cooldown waited, aged out after 2 s and
        /// was never said or captioned (ExtrasPlayTests.HoldingTheHillAloneShowsTheSecretLine on batch-6 + longer cut-ins).
        public static bool Free => Instance != null && !IsSpeaking && Instance._queue.Pending == 0
            && Instance._queue.CooledDown(Time.unscaledTimeAsDouble);

        public static bool IsSpeaking => Instance != null && Instance._lineEnds > Time.unscaledTime;
        /// <summary>The subtitle on screen ("" when none).</summary>
        public static string CurrentText => Instance != null && IsSpeaking && Instance._current != null ? Instance._current.Text : string.Empty;

        /// <summary>Queues a trigger now (it obeys the anti-spam rule like any match event).</summary>
        public static bool Say(AnnouncerTrigger t) => Instance != null && Instance._queue.Enqueue(t, Time.unscaledTimeAsDouble);

        /// <summary>Plays a line at once, skipping the queue (capture tools and tests only).</summary>
        public static void ForceLine(AnnouncerTrigger t)
        {
            if (Instance == null) return;
            AnnouncerLine line = AnnouncerLines.For(t);
            Instance._queue.MarkStarted(line, Time.unscaledTimeAsDouble);
            Instance.Begin(line);
        }

        public AnnouncerQueue Queue => _queue;

        readonly AnnouncerQueue _queue = new AnnouncerQueue();
        readonly AnnouncerDecoder _decoder = new AnnouncerDecoder();
        AudioSource _voice;
        SimDriverBehaviour _driver;
        HumanInputSource _input;
        int _lookupFrames;
        AnnouncerLine _current;
        float _lineStart, _lineEnds;
        bool _voicePaused;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _space;
        VectorShape _panel;
        Text _text;

        void Awake()
        {
            Instance = this;
            LoadWords();
            for (int i = 0; i < AnnouncerLines.Table.Length; i++)
            {
                AnnouncerLine l = AnnouncerLines.Table[i];
                if (l.Clip == null) l.Clip = Resources.Load<AudioClip>(VoiceFolder + l.Id);
            }
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.loop = false;
            _voice.spatialBlend = 0f;
            _voice.priority = 8;
            _voice.outputAudioMixerGroup = GameSettings.EffectsGroup;
            BuildSubtitles();
            SceneManager.sceneLoaded += OnSceneLoaded;
            _lookupFrames = 0;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            _driver = null;
            _input = null;
            _lookupFrames = 0;
            _queue.Reset();
            _decoder.Reset(null);
            Stop();
        }

        /// <summary>The words pack, when it is reachable at run time (a Resources copy, or the source file in the editor).</summary>
        static void LoadWords()
        {
            TextAsset ta = Resources.Load<TextAsset>("Lore/announcer");
            if (ta != null && AnnouncerLines.ApplyWordsPack(ta.text) > 0) return;
#if UNITY_EDITOR
            string path = System.IO.Path.Combine(Application.dataPath, "_Game", "Resources", "Lore", "announcer.json");
            if (System.IO.File.Exists(path)) AnnouncerLines.ApplyWordsPack(System.IO.File.ReadAllText(path));
#endif
        }

        void Update() { var cost = CriticalMass.Runtime.Game.FrameCost.Enter(); UpdateCost(); CriticalMass.Runtime.Game.FrameCost.Exit(CriticalMass.Runtime.Game.FrameCost.Slot.Sound, cost); }

        void UpdateCost()
        {
            float now = Time.unscaledTime;
            // The match may be built after a scene load or later (game flow builds it on Play): look every 15 frames.
            if (_driver == null && (_lookupFrames++ % 15) == 0)
            {
                _driver = FindAnyObjectByType<SimDriverBehaviour>();
                if (_driver != null) _input = _driver.GetComponent<HumanInputSource>();
            }
            bool paused = _input != null && _input.State.Paused;
            bool title = _driver != null && _driver.TryGetComponent(out TitleScreen _);

            if (paused && _current != null)
            {
                // The line waits with the game: its subtitle comes back for the rest of the line on resume.
                float d = Time.unscaledDeltaTime;
                _lineStart += d;
                _lineEnds += d;
            }
            if (paused != _voicePaused)
            {
                _voicePaused = paused;
                if (paused) _voice.Pause();
                else _voice.UnPause();
            }

            World w = _driver != null ? _driver.World : null;
            if (w != null && !title)
            {
                _decoder.Poll(w, _queue, now);
                if (!paused && _queue.TryNext(now, out AnnouncerLine line)) Begin(line);
            }
            else
            {
                // Behind the title the bot match runs on; nothing it does is news yet.
                if (title && w != null) _decoder.Reset(null);
                _queue.Clear();
            }
            UpdateSubtitle(now, paused || title);
        }

        void Begin(AnnouncerLine line)
        {
            _current = line;
            float now = Time.unscaledTime;
            _lineStart = now;
            float len = line.Clip != null ? line.Clip.length : Mathf.Max(1.6f, line.Text.Length * 0.06f);
            _lineEnds = now + len + SubtitleTailSeconds;
            if (line.Clip != null)
            {
                _voice.Stop();
                _voice.clip = line.Clip;
                _voice.Play();
                MusicDirector.Duck(len);
            }
            if (_text != null)
            {
                HudText.Set(_text, line.Text);
                float w = Mathf.Clamp(_text.preferredWidth + 72f, 360f, MaxWidth);
                MenuUi.Place(_panel.rectTransform, (HudLayout.RefW - w) * 0.5f, SubtitleY, w, SubtitleH);
                MenuUi.Place(_text.rectTransform, 0f, 0f, w, SubtitleH);
            }
            LineStarted?.Invoke(line);
        }

        void Stop()
        {
            if (_voice != null) _voice.Stop();
            _lineEnds = 0f;
            _current = null;
        }

        // ---- subtitles ----

        const float MaxWidth = 920f;     // the free band between the player cluster and the minimap
        const float SubtitleH = 60f;
        const float SubtitleY = HudLayout.RefH - HudLayout.Margin - SubtitleH - 4f;

        void BuildSubtitles()
        {
            var go = new GameObject("Subtitles Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Overlay draws over every camera-space canvas (HUD, title, pause menu); it is hidden whenever the title or the
            // pause menu is up, so it only ever sits over the match.
            _canvas.sortingOrder = 30;
            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            var root = (RectTransform)go.transform;

            _space = MenuUi.Rt(root, "Space");
            _space.anchorMin = _space.anchorMax = new Vector2(0.5f, 0.5f);
            _space.pivot = new Vector2(0.5f, 0.5f);
            _space.sizeDelta = new Vector2(HudLayout.RefW, HudLayout.RefH);

            _panel = MenuUi.Panel(_space, "Subtitle", (HudLayout.RefW - 600f) * 0.5f, SubtitleY, 600f, SubtitleH);
            _panel.OutlinePx = 3f;
            _panel.RimPx = 2f;
            _text = MenuUi.Label(_panel.transform, "Line", CMTokens.Ui, UiScale.TypeButton, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                0f, 0f, 600f, SubtitleH, string.Empty, 2f);
            _canvas.enabled = false;
        }

        void UpdateSubtitle(float now, bool hidden)
        {
            if (_canvas == null) return;
            bool show = !hidden && _current != null && now < _lineEnds && SubtitlesEnabled;
            float a = 0f;
            if (show)
            {
                float fin = Mathf.Clamp01((now - _lineStart) / FadeSeconds);
                float fout = Mathf.Clamp01((_lineEnds - now) / FadeSeconds);
                a = Mathf.Min(fin, fout);
            }
            if (a <= 0f)
            {
                if (_canvas.enabled) _canvas.enabled = false;
                return;
            }
            if (!_canvas.enabled) _canvas.enabled = true;
            _group.alpha = a;
            float s = HudLayout.Scale(Screen.width, Screen.height);
            if (s > 0f && Mathf.Abs(_space.localScale.x - s) > 1e-4f) _space.localScale = new Vector3(s, s, 1f);
        }
    }
}
