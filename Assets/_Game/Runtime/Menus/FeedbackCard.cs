using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Replay;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// b15 safe defaults, item 4 (owner-approved 2026-11-17): an optional feedback card after the results screen, and the
    /// time-to-first-fun markers. View and files only: it reads the world, never writes to it.
    /// <list type="bullet">
    /// <item>While a person plays a match (not a backdrop, capture, autoplay, replay or spectator world) it keeps a frame-time
    /// histogram and the match's class, team, length and result.</item>
    /// <item>When the flow leaves the results screen for the home screen or class select, the card shows over it: fun 1..5,
    /// "what felt bad?" chips and an optional one-line note; SAVE FEEDBACK or SKIP (Esc / B). Three presses at most. Going
    /// straight into another match never shows it (the line is written as not shown).</item>
    /// <item>Every human match writes one line to FeedbackLog's file next to the replays; after saving, the card shows the
    /// file's folder with COPY FILE PATH and OPEN FOLDER. Nothing is sent anywhere.</item>
    /// <item>Seconds from launch to the first match start, the first hit landed and the first objective credit are written as
    /// a "first_fun" line once a match ends.</item>
    /// </list>
    /// </summary>
    [GameOverlay(Order = 96, Scope = OverlayScope.Always)]
    public sealed class FeedbackCard : MonoBehaviour
    {
        public const string LogTag = "[CM.Feedback]";
        public const int Sorting = 66;
        /// <summary>Seconds after the results screen closes before the card shows (the next screen has settled).</summary>
        public const float ShowDelay = 0.3f;

        public const string Heading = "HOW WAS THAT MATCH?";
        public const string Optional = "Optional. Answer what you like, or skip.";
        public const string FunLabel = "HOW FUN WAS IT?";
        public const string FunScale = "1 = not fun     5 = great";
        public const string BadLabel = "WHAT FELT BAD?";
        public const string NoteLabel = "ONE-LINE NOTE (OPTIONAL)";
        public const string NoteHintKeys = "Select to type, then press Enter";
        public const string Privacy = "Your answers stay on your computer until you send the file yourself.";
        public const string SavedHeading = "SAVED. THANK YOU.";
        public const string SavedBody = "Your feedback is in this file. It stays on your computer until you send it:";
        public const string SaveFailed = "Unable to save the file. Check there is free disk space, then play another match.";

        public static FeedbackCard Current { get; private set; }
        /// <summary>The card is on screen.</summary>
        public static bool IsShowing => Current != null && Current._showing;
        /// <summary>Tests point the file somewhere else (null = the replay folder).</summary>
        public static string FolderOverride;
        public static string Folder => FolderOverride ?? ReplayStore.Folder;
        public static string FilePath => FeedbackLog.PathIn(Folder);
        /// <summary>Tests turn the card and the file on in the editor (off there by default, like the boot title, so test
        /// runs never stop on the card or write into the real data folder).</summary>
        public static bool EnabledInEditorForTests;
        /// <summary>The card and the file run in player builds (the boot title's rule) or when a test turns them on.</summary>
        public static bool Active => TitleScreen.ShowOnBoot || EnabledInEditorForTests;

        // first-fun markers (this launch)
        static FirstFun _fun;
        static int _funWrittenBits;
        /// <summary>This launch's markers (tests, debug).</summary>
        public static FirstFun Markers => _fun;

        SimDriverBehaviour _driver;
        FlowState _lastState = FlowState.Off;
        readonly FrameHistogram _frames = new FrameHistogram();
        bool _tracking;
        World _world;
        int _eventHead;
        // last known match facts (the world may be torn down when the results close)
        string _cls, _team;
        int _teamIdx, _winner, _seed;
        double _matchSeconds;
        MatchFeedback _pending;
        float _showAt = -1f;

        // card
        Canvas _canvas;
        RectTransform _space;
        bool _showing, _saved;
        readonly List<MenuItem[]> _rows = new List<MenuItem[]>();
        int _row, _col;
        MenuItem[] _funItems, _chipItems;
        MenuItem _noteItem, _saveItem, _skipItem;
        GameObject _form, _done;
        Text _pathText, _statusText;
        bool _typing;
        string _note = "";
        int _typingFrame;
        float _navHeldUntil;

        void Awake()
        {
            Current = this;
            _driver = GetComponent<SimDriverBehaviour>();
        }

        void OnDestroy()
        {
            StopTyping();
            if (_showing) OverlayGuard.End();
            FlushPending(false);
            if (Current == this) Current = null;
        }

        /// <summary>A match a person plays: the world is not a backdrop, capture, autoplay, replay or spectator world.</summary>
        public static bool HumanMatch(bool toolWorld, bool replay, bool spectating) => !toolWorld && !replay && !spectating;

        void Update()
        {
            if (_showing) OverlayGuard.Tick();
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            FlowState s = GameFlow.State;
            World w = _driver != null ? _driver.World : null;
            bool human = _driver != null && w != null && w.ActorCount > 0 &&
                         HumanMatch(HumanControlGuard.ToolWorld(_driver), _driver.IsReplay, SpectatorMode.IsSpectating);
            if (!Active) { _lastState = s; return; }
            if (s == FlowState.Match && human) TrackMatch(w);
            if (s != _lastState) OnStateChanged(_lastState, s);
            _lastState = s;

            if (_pending != null && !_showing && _showAt >= 0f && Time.unscaledTime >= _showAt && !OverlayGuard.Active)
            {
                _showAt = -1f;
                Show();
            }
            if (_showing) UpdateCard();
        }

        void TrackMatch(World w)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_tracking || w != _world)
            {
                _tracking = true;
                _world = w;
                _eventHead = 0;
                _frames.Clear();
                if (!_fun.FirstMatch.HasValue) _fun.FirstMatch = now;
            }
            else _frames.Add(Time.unscaledDeltaTime * 1000.0);

            int me = LocalSeat.Actor;
            if (me < 0 || me >= w.ActorCount) return;
            Actor a = w.Actors[me];
            _cls = a.Cls.ToString();
            _team = a.Team.ToString();
            _teamIdx = (int)a.Team;
            _winner = (int)w.Match.Winner;
            _matchSeconds = w.Match.Time;
            _seed = GameFlow.LastSeed;

            int head = w.EventHead;
            int from = Math.Max(_eventHead, head - World.EventCapacity);
            if (head < _eventHead) from = Math.Max(0, head - World.EventCapacity);   // the world was reset
            for (int i = from; i < head; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e == null) continue;
                if (!_fun.FirstHit.HasValue && e.Type == SimEventType.Hit && e.B == me && e.A >= 0 && e.A != me) _fun.FirstHit = now;
                if (!_fun.FirstObjective.HasValue && e.A == me &&
                    (e.Type == SimEventType.DeliverBiomass || e.Type == SimEventType.DeliverCorruption)) _fun.FirstObjective = now;
            }
            _eventHead = head;
            if (!_fun.FirstObjective.HasValue && a.HillSeconds > 0) _fun.FirstObjective = now;
        }

        void OnStateChanged(FlowState from, FlowState to)
        {
            if (to == FlowState.Results && _tracking)
            {
                _tracking = false;
                FlushPending(false);
                _pending = new MatchFeedback
                {
                    Utc = DateTime.UtcNow, Build = BuildInfo.Version, Commit = BuildInfo.Commit, Class = _cls, Team = _team,
                    Result = FeedbackLog.Result(_winner, _teamIdx), MatchSeconds = _matchSeconds, Seed = _seed,
                    Replay = ReplayName(ReplayCapture.LastSaved), Frames = _frames.Summary(),
                };
                WriteFirstFun();
                return;
            }
            if (from == FlowState.Match && to != FlowState.Results && _tracking)
            {
                // Left mid-match (quit to the menu): one "unfinished" line, no card.
                _tracking = false;
                FlushPending(false);
                _pending = new MatchFeedback
                {
                    Utc = DateTime.UtcNow, Build = BuildInfo.Version, Commit = BuildInfo.Commit, Class = _cls, Team = _team,
                    Result = "unfinished", MatchSeconds = _matchSeconds, Seed = _seed, Replay = null, Frames = _frames.Summary(),
                };
                FlushPending(false);
                WriteFirstFun();
                return;
            }
            if (from == FlowState.Results && _pending != null)
            {
                if (to == FlowState.Match) FlushPending(false);   // straight into another match: never in the way
                else _showAt = Time.unscaledTime + ShowDelay;
            }
            if (to == FlowState.Match || to == FlowState.Off) _tracking = false;
        }

        static string ReplayName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return System.IO.Path.GetFileName(path); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>Bits of the markers reached: 1 first match, 2 first hit, 4 first objective.</summary>
        public static int MarkerBits(FirstFun f) =>
            (f.FirstMatch.HasValue ? 1 : 0) | (f.FirstHit.HasValue ? 2 : 0) | (f.FirstObjective.HasValue ? 4 : 0);

        /// <summary>A "first_fun" line at the end of a match when a marker was reached since the last line (the latest line
        /// in the file is the launch's full record; seconds since launch: realtimeSinceStartup).</summary>
        static void WriteFirstFun()
        {
            int bits = MarkerBits(_fun);
            if (bits == 0 || bits == _funWrittenBits) return;
            string line = FeedbackLog.FirstFunLine(_fun, DateTime.UtcNow, BuildInfo.Version);
            if (FeedbackLog.Append(FilePath, line, out string err)) _funWrittenBits = bits;
            else Debug.LogWarning(LogTag + " first-fun line not written: " + err);
        }

        /// <summary>Writes the pending match line (shown: whether the card was on screen).</summary>
        void FlushPending(bool shown)
        {
            if (_pending == null) return;
            _pending.Shown = shown;
            if (!shown) _pending.Skipped = true;
            string line = FeedbackLog.MatchLine(_pending);
            _pending = null;
            _showAt = -1f;
            if (!FeedbackLog.Append(FilePath, line, out string err)) Debug.LogWarning(LogTag + " match line not written: " + err);
        }

        // ---------------------------------------------------------------- card

        void Show()
        {
            Build();
            _saved = false;
            _note = "";
            _pending.Fun = 0;
            _pending.FeltBad.Clear();
            _form.SetActive(true);
            _done.SetActive(false);
            HudText.Set(_noteItem.Label, NotePlaceholder());
            RefreshMarks();
            _rows.Clear();
            _rows.Add(_funItems);
            _rows.Add(new[] { _chipItems[0], _chipItems[1], _chipItems[2] });
            _rows.Add(new[] { _chipItems[3], _chipItems[4], _chipItems[5] });
            _rows.Add(new[] { _noteItem });
            _rows.Add(new[] { _saveItem, _skipItem });
            _row = 0;
            _col = 2;
            _showing = true;
            _canvas.enabled = true;
            MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.25f;   // the press that left the results never answers the card
            _navHeldUntil = Time.unscaledTime + 0.25f;
            // The Pause press (Esc, Start) closes the card; while typing it only ends the note.
            OverlayGuard.Begin(gameObject, () => { if (_typing) StopTyping(); else Close(); return true; }, true);
            Focus();
        }

        /// <summary>Saves the answers (the card's SAVE FEEDBACK; tests).</summary>
        public void Save()
        {
            if (!_showing || _saved || _pending == null) return;
            StopTyping();
            _pending.Note = _note;
            _pending.Skipped = false;
            MatchFeedback p = _pending;
            p.Shown = true;
            string line = FeedbackLog.MatchLine(p);
            _pending = null;
            bool ok = FeedbackLog.Append(FilePath, line, out string err);
            if (!ok) Debug.LogWarning(LogTag + " match line not written: " + err);
            _saved = true;
            _form.SetActive(false);
            _done.SetActive(true);
            HudText.Set(_statusText, ok ? SavedBody : SaveFailed);
            HudText.Set(_pathText, ok ? FilePath : "");
            MenuItem copy = _done.transform.Find("Copy Path").GetComponent<MenuItem>();
            MenuItem open = _done.transform.Find("Open Folder").GetComponent<MenuItem>();
            MenuItem close = _done.transform.Find("Done").GetComponent<MenuItem>();
            copy.gameObject.SetActive(ok);
            open.gameObject.SetActive(ok);
            _rows.Clear();
            _rows.Add(ok ? new[] { copy, open, close } : new[] { close });
            _row = 0;
            _col = 0;
            Focus();
        }

        /// <summary>Closes the card; unanswered, the match line is written as skipped (SKIP, Esc, B; tests).</summary>
        public void Close()
        {
            if (!_showing) return;
            StopTyping();
            if (_pending != null)
            {
                _pending.Note = "";
                _pending.Fun = 0;
                _pending.FeltBad.Clear();
                FlushPending(true);
            }
            _showing = false;
            if (_canvas != null) _canvas.enabled = false;
            OverlayGuard.End();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                _canvas != null && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_canvas.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>Picks a fun score 1..5 (tests and the buttons).</summary>
        public void SetFun(int v)
        {
            if (_pending == null || v < 1 || v > 5) return;
            _pending.Fun = _pending.Fun == v ? 0 : v;
            RefreshMarks();
        }

        /// <summary>Toggles a "what felt bad?" chip (tests and the buttons).</summary>
        public void ToggleChip(int i)
        {
            if (_pending == null || i < 0 || i >= FeedbackLog.ChipIds.Length) return;
            string id = FeedbackLog.ChipIds[i];
            if (!_pending.FeltBad.Remove(id)) _pending.FeltBad.Add(id);
            RefreshMarks();
        }

        void RefreshMarks()
        {
            if (_pending == null || _funItems == null) return;
            for (int i = 0; i < _funItems.Length; i++) Mark(_funItems[i], _pending.Fun == i + 1);
            for (int i = 0; i < _chipItems.Length; i++) Mark(_chipItems[i], _pending.FeltBad.Contains(FeedbackLog.ChipIds[i]));
        }

        static void Mark(MenuItem item, bool on)
        {
            Transform m = item.transform.Find("Mark");
            if (m != null) m.gameObject.SetActive(on);
        }

        string NotePlaceholder() => _note.Length > 0 ? _note : NoteHintKeys;

        void UpdateCard()
        {
            MenuCanvas.UpdateScale(_canvas);
            if (_typing) { UpdateTyping(); return; }
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (Time.unscaledTime < _navHeldUntil) return;
            int dx = 0, dy = 0;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) dx = -1;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) dx = 1;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) dy = -1;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) dy = 1;
                if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
                if (kb.digit1Key.wasPressedThisFrame) SetFun(1);
                if (kb.digit2Key.wasPressedThisFrame) SetFun(2);
                if (kb.digit3Key.wasPressedThisFrame) SetFun(3);
                if (kb.digit4Key.wasPressedThisFrame) SetFun(4);
                if (kb.digit5Key.wasPressedThisFrame) SetFun(5);
            }
            if (pad != null)
            {
                if (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame) dx = -1;
                if (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame) dx = 1;
                if (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame) dy = -1;
                if (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame) dy = 1;
                if (pad.buttonEast.wasPressedThisFrame) { Close(); return; }
            }
            if (dx != 0 || dy != 0) Move(dx, dy);
            bool submit = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) ||
                          (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (submit && Time.unscaledTime >= MenuItem.SubmitBlockedUntil)
            {
                MenuItem it = FocusedItem();
                if (it != null && it.gameObject.activeInHierarchy) it.Submitted?.Invoke();
            }
        }

        void Move(int dx, int dy)
        {
            if (_rows.Count == 0) return;
            MenuItem.PointerNavigated = false;
            if (dy != 0)
            {
                float x = CentreX(FocusedItem());
                _row = (_row + dy + _rows.Count) % _rows.Count;
                _col = NearestCol(_rows[_row], x);
            }
            else
            {
                MenuItem[] r = _rows[_row];
                _col = (_col + dx + r.Length) % r.Length;
            }
            Focus();
        }

        static float CentreX(MenuItem it)
        {
            if (it == null) return 0f;
            RectTransform rt = (RectTransform)it.transform;
            return rt.anchoredPosition.x + rt.rect.width * 0.5f;
        }

        static int NearestCol(MenuItem[] row, float x)
        {
            int best = 0;
            float d = float.MaxValue;
            for (int i = 0; i < row.Length; i++)
            {
                float di = Mathf.Abs(CentreX(row[i]) - x);
                if (di < d) { d = di; best = i; }
            }
            return best;
        }

        MenuItem FocusedItem()
        {
            if (_row < 0 || _row >= _rows.Count) return null;
            MenuItem[] r = _rows[_row];
            return r.Length == 0 ? null : r[Mathf.Clamp(_col, 0, r.Length - 1)];
        }

        void Focus()
        {
            MenuItem it = FocusedItem();
            if (it != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(it.gameObject);
        }

        // ---- note ----

        void StartTyping()
        {
            Keyboard kb = Keyboard.current;
            if (_typing || kb == null) return;
            _typing = true;
            _typingFrame = Time.frameCount;
            kb.onTextInput += OnChar;
            HudText.Set(_noteItem.Label, _note + "_");
        }

        void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            Keyboard kb = Keyboard.current;
            if (kb != null) kb.onTextInput -= OnChar;
            _note = FeedbackLog.CleanNote(_note);
            if (_noteItem != null) HudText.Set(_noteItem.Label, NotePlaceholder());
        }

        void UpdateTyping()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) { StopTyping(); return; }
            if (kb.backspaceKey.wasPressedThisFrame && _note.Length > 0)
            {
                _note = _note.Substring(0, _note.Length - 1);
                HudText.Set(_noteItem.Label, _note + "_");
            }
            if (Time.frameCount == _typingFrame) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame ||
                kb.tabKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
            {
                StopTyping();
                MenuItem.SubmitBlockedUntil = Time.unscaledTime + 0.1f;
                _row = _rows.Count - 1;   // on to SAVE FEEDBACK
                _col = 0;
                Focus();
            }
        }

        void OnChar(char c)
        {
            if (!_typing || Time.frameCount == _typingFrame || char.IsControl(c)) return;
            if (_note.Length >= FeedbackLog.NoteMax) return;
            _note += c;
            HudText.Set(_noteItem.Label, _note + "_");
        }

        // ---- build ----

        void Build()
        {
            if (_canvas != null) return;
            PauseMenu.EnsureEventSystem(transform);
            _canvas = MenuCanvas.Make(transform, "Feedback Card Canvas", Sorting, out _space);
            MenuCanvas.Wash(_canvas.transform, 0.92f);
            const float w = 1040f, h = 700f, pad = 48f;
            float x0 = (HudLayout.RefW - w) * 0.5f, y0 = (HudLayout.RefH - h) * 0.5f;
            VectorShape panel = MenuUi.Panel(_space, "Feedback Panel", x0, y0, w, h);
            Transform p = panel.transform;
            float inner = w - 2f * pad;

            _form = MenuUi.Rt(p, "Form").gameObject;
            MenuUi.Stretch((RectTransform)_form.transform);
            Transform f = _form.transform;
            MenuUi.Label(f, "Heading", CMTokens.Display, UiScale.TypeHeading, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                pad, 36f, inner, 40f, Heading, 3f);
            MenuUi.Label(f, "Optional", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                pad, 80f, inner, 26f, Optional, 2f);

            // fun 1..5
            float y = 128f;
            MenuUi.Label(f, "Fun Label", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                pad, y, 420f, 32f, FunLabel, 2f);
            MenuUi.Label(f, "Fun Scale", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleRight,
                pad + inner - 420f, y, 420f, 32f, FunScale, 2f);
            y += 40f;
            _funItems = new MenuItem[5];
            float fw = (inner - 4f * 16f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                int v = i + 1;
                _funItems[i] = MarkedItem(f, "Fun " + v, pad + i * (fw + 16f), y, fw, 64f, v.ToString(), UiScale.TypeHeading);
                _funItems[i].Submitted = () => SetFun(v);
            }

            // what felt bad
            y += 64f + 28f;
            MenuUi.Label(f, "Bad Label", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                pad, y, inner, 32f, BadLabel, 2f);
            y += 40f;
            _chipItems = new MenuItem[FeedbackLog.ChipIds.Length];
            float cw = (inner - 2f * 16f) / 3f;
            for (int i = 0; i < _chipItems.Length; i++)
            {
                int k = i;
                float cx = pad + (i % 3) * (cw + 16f), cy = y + (i / 3) * (56f + 12f);
                _chipItems[i] = MarkedItem(f, "Chip " + FeedbackLog.ChipIds[i], cx, cy, cw, 56f, FeedbackLog.ChipWords[i], UiScale.TypeBody);
                _chipItems[i].Submitted = () => ToggleChip(k);
            }

            // note
            y += 2f * 56f + 12f + 28f;
            MenuUi.Label(f, "Note Label", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft,
                pad, y, inner, 32f, NoteLabel, 2f);
            y += 40f;
            _noteItem = MenuUi.Item(f, "Note", pad, y, inner, 52f, NoteHintKeys, MenuItem.Kind.Button, UiScale.TypeBody, false, CMTokens.Ui);
            _noteItem.Submitted = StartTyping;

            // actions
            float by = h - 36f - 60f;
            _saveItem = MenuUi.Item(f, "Save", pad, by, 320f, 60f, "SAVE FEEDBACK", MenuItem.Kind.Button, UiScale.TypeBody, true);
            _saveItem.Submitted = Save;
            _skipItem = MenuUi.Item(f, "Skip", pad + 320f + 16f, by, 200f, 60f, "SKIP", MenuItem.Kind.Button, UiScale.TypeBody, true);
            _skipItem.Submitted = Close;
            Text priv = MenuUi.Label(f, "Privacy", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft,
                pad + 320f + 16f + 200f + 24f, by, inner - (320f + 16f + 200f + 24f), 60f, Privacy, 2f);
            priv.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUi.Fit(priv, UiScale.TypeMin);

            // saved
            _done = MenuUi.Rt(p, "Saved").gameObject;
            MenuUi.Stretch((RectTransform)_done.transform);
            Transform d = _done.transform;
            MenuUi.Label(d, "Saved Heading", CMTokens.Display, UiScale.TypeHeading, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                pad, 36f, inner, 40f, SavedHeading, 3f);
            _statusText = MenuUi.Label(d, "Saved Body", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.UpperLeft,
                pad, 110f, inner, 64f, SavedBody, 2f);
            _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _pathText = MenuUi.Label(d, "Path", CMTokens.Mono, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.UpperLeft,
                pad, 190f, inner, 120f, "", 2f);
            _pathText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float dy0 = h - 36f - 60f;
            MenuItem copy = MenuUi.Item(d, "Copy Path", pad, dy0, 320f, 60f, "COPY FILE PATH", MenuItem.Kind.Button, UiScale.TypeBody, true);
            copy.Submitted = () => { GUIUtility.systemCopyBuffer = FilePath; HudText.Set(copy.Label, "PATH COPIED"); };
            MenuItem open = MenuUi.Item(d, "Open Folder", pad + 336f, dy0, 280f, 60f, "OPEN FOLDER", MenuItem.Kind.Button, UiScale.TypeBody, true);
            open.Submitted = OpenFolder;
            MenuItem done = MenuUi.Item(d, "Done", pad + 336f + 296f, dy0, 200f, 60f, "DONE", MenuItem.Kind.Button, UiScale.TypeBody, true);
            done.Submitted = Close;
            _done.SetActive(false);
            _canvas.enabled = false;
            MenuCanvas.UpdateScale(_canvas);
        }

        static MenuItem MarkedItem(Transform parent, string name, float x, float y, float w, float h, string text, int size)
        {
            MenuItem it = MenuUi.Item(parent, name, x, y, w, h, text, MenuItem.Kind.Button, size, true);
            // The chosen state: a cream bar along the bottom edge (not colour alone: the bar is a shape; the file holds the words).
            VectorShape mark = MenuUi.Shape(it.transform, "Mark", ShapeKind.RoundRect, 14f, h - 12f, w - 28f, 6f, CMTokens.WarmCream.Color, 3f);
            mark.gameObject.SetActive(false);
            return it;
        }

        static void OpenFolder()
        {
            try
            {
                string dir = Folder;
                System.IO.Directory.CreateDirectory(dir);
                Application.OpenURL(new Uri(dir).AbsoluteUri);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogTag + " open folder: " + e.Message);
            }
        }

        /// <summary>Tests: forget this launch's markers.</summary>
        public static void ResetMarkersForTests()
        {
            _fun = default;
            _funWrittenBits = 0;
        }
    }
}
