using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// Owner walk 1 (2026-11-13): the screens the owner reads, each captured at every screen shape a player may have:
    /// 3024x1964 and 1512x982 (14-inch MacBook Pro full screen, with its camera notch as the safe-area inset), 1920x1080,
    /// 2560x1080 (ultrawide) and 1280x800 (16:10). Home (and the fighter hovered), the Barracks, mode select, class select,
    /// READY, the match start, a busy HUD (every optional element at once: power-up timer, a full kill feed, an event banner,
    /// the job line, coins, team stock, the hatch timer), the station panel, the scoreboard, a map event with the autopilot
    /// banner, the full map, a big moment, the death recap, pause and results. Each resolution renders after a few frames at
    /// that size so every screen's own per-frame layout has run. walk.log lists cut text and the HUD overlap check per shot.
    /// Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.OwnerWalkCaptureTool.Run -walkOut DIR
    /// [-walkRes 3024x1964,1920x1080] (needs a GPU: no -nographics). Exit 0 when every shot was written.
    /// </summary>
    public static class OwnerWalkCaptureTool
    {
        struct Res
        {
            public int W, H;
            public float Notch;   // top safe-area inset, device px
            public Res(int w, int h, float notch) { W = w; H = h; Notch = notch; }
            public string Tag => W + "x" + H;
        }

        static readonly Res[] AllRes =
        {
            new Res(3024, 1964, 74f), new Res(1512, 982, 37f), new Res(1920, 1080, 0f), new Res(2560, 1080, 0f), new Res(1280, 800, 0f),
        };

        const int MaxFrames = 200000;
        static Res[] _res = AllRes;
        static Res _cur = new Res(1920, 1080, 0f);
        static string _out;
        static int _frames, _shots, _wanted;
        static IEnumerator<Func<bool>> _walk;
        static Func<bool> _wait;
        static readonly List<string> Log = new List<string>();
        static readonly Dictionary<string, RenderTexture> Targets = new Dictionary<string, RenderTexture>();
        static RenderTexture _rt;

        public static void Run()
        {
            try
            {
                _out = Arg("-walkOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-walkOut DIR is required");
                string only = Arg("-walkRes");
                if (!string.IsNullOrEmpty(only))
                {
                    var pick = new List<Res>();
                    foreach (Res r in AllRes) if (only.Contains(r.Tag)) pick.Add(r);
                    if (pick.Count > 0) _res = pick.ToArray();
                }
                Directory.CreateDirectory(_out);
                _frames = _shots = _wanted = 0;
                Log.Clear();
                EditorSceneManager.OpenScene(GameBootstrap.BootScenePath, OpenSceneMode.Single);
                GameFlow.ForceEnabled = true;
                _walk = Walk();
                _wait = null;
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogError("[OwnerWalk] " + e);
                EditorApplication.Exit(1);
            }
        }

        static SimDriverBehaviour Driver => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();
        static World W => Driver != null ? Driver.World : null;
        static FlowDirector D => FlowDirector.Current;

        static Func<bool> Seconds(double s)
        {
            double until = EditorApplication.timeSinceStartup + s;
            return () => EditorApplication.timeSinceStartup >= until;
        }

        static Func<bool> StepFrames(int n)
        {
            int left = n;
            return () =>
            {
                if (left <= 0) return true;
                if (Driver != null) Driver.StepTicks(1);
                left--;
                return false;
            };
        }

        static void Note(string s)
        {
            Log.Add($"{EditorApplication.timeSinceStartup:0.0}s {s}");
            Debug.Log("[OwnerWalk] " + s);
        }

        static void Teleport(int i, double x, double z)
        {
            World w = W;
            w.X[i] = w.PrevX[i] = x;
            w.Z[i] = w.PrevZ[i] = z;
        }

        /// <summary>Optional hooks that exist only on some commits (the walk runs before and after the fix).</summary>
        static bool TryInvoke(Type t, object target, string method, params object[] args)
        {
            try
            {
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                             System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                if (m == null) { Note($"hook {t.Name}.{method}: not on this commit"); return false; }
                m.Invoke(target, args);
                return true;
            }
            catch (Exception e) { Note($"hook {t.Name}.{method} FAILED: {(e.InnerException ?? e).Message}"); return false; }
        }

        /// <summary>One shot at every resolution: switch, let a few frames run at that size, render.</summary>
        static IEnumerable<Func<bool>> Shots(string name, double settle = 0.3)
        {
            foreach (Res r in _res)
            {
                SetRes(r);
                yield return Seconds(settle);
                Shot(name, r);
            }
            SetRes(new Res(1920, 1080, 0f));
        }

        static void SetRes(Res r)
        {
            _cur = r;
            if (!Targets.TryGetValue(r.Tag, out RenderTexture rt) || rt == null)
            {
                rt = new RenderTexture(r.W, r.H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                rt.Create();
                Targets[r.Tag] = rt;
            }
            _rt = rt;
            if (r.Notch > 0f) HudSafeArea.SetOverride(new Vector4(0f, r.Notch, 0f, 0f));
            else HudSafeArea.ClearOverride();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null) { cam.targetTexture = _rt; cam.aspect = (float)r.W / r.H; }
        }

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            Note($"boot: state {GameFlow.State}, seed {W.Opts.Seed}, resolutions {_res.Length}");
            StoryIntro.Skip();
            yield return Seconds(1.0);
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);
            D.ShowMenu();
            yield return Seconds(1.2);
            foreach (var f in Shots("01-home")) yield return f;
            if (TryInvoke(typeof(MainMenu), D.MainMenu, "CaptureHoverFighter", true))
            {
                foreach (var f in Shots("01b-home-fighter-hover")) yield return f;
                TryInvoke(typeof(MainMenu), D.MainMenu, "CaptureHoverFighter", false);
            }

            // The Barracks for the Warrior (what the fighter on Home opens).
            D.HideFlowScreens();
            Barracks.Show(ClassId.Warrior);
            yield return Seconds(0.8);
            Note($"barracks: open {Barracks.IsOpen}, tab {(CharacterPanel.Instance != null ? CharacterPanel.Instance.CurrentTab.ToString() : "-")}");
            foreach (var f in Shots("02-barracks")) yield return f;
            Barracks.Hide();
            yield return Seconds(0.4);

            D.ShowModes(MatchMode.Quick);
            yield return Seconds(0.8);
            foreach (var f in Shots("03-mode-select")) yield return f;
            D.HideFlowScreens();
            ClassSelect.SelectedClass = ClassId.Warrior;
            ClassSelect.ShowSetup(TeamId.Azure);
            yield return Seconds(0.9);
            foreach (var f in Shots("04-class-select")) yield return f;
            ClassSelect.Hide();

            // READY, then its countdown launches the match (seed 4242).
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver;
            MatchSetupChoice quick = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
            quick.Class = ClassId.Warrior;
            quick.Team = TeamId.Azure;
            D.ShowReady(new PlayLaunch { Choice = quick, Seed = 4242, BackTo = LobbyScreen.ModeSelect });
            yield return Seconds(1.2);
            foreach (var f in Shots("05-ready", 0.15)) yield return f;
            double startUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => (Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > startUntil;
            SimDriverBehaviour drv = Driver;
            {
                double rt = EditorApplication.timeSinceStartup + 30;
                yield return () => (W.Match.Phase == MatchPhase.Playing && W.Actors[0].Alive) || EditorApplication.timeSinceStartup > rt;
            }
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();   // stays connected: removing it pauses
            drv.HumanAutopilot = false;
            drv.TimeScale = 0;
            HumanInputSource src = drv.GetComponent<HumanInputSource>();
            if (src != null) src.State.Reset();
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Off;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            yield return StepFrames(15);
            foreach (var f in Shots("10-match-start")) yield return f;

            // Busy HUD: stand on a power-up, then a full kill feed and an event banner on top of everything else.
            double sx = W.X[0], sz = W.Z[0];
            {
                int slot = -1;
                for (int guard = 0; guard < 120 && slot < 0; guard++)
                {
                    if (W.Depth != null && W.Depth.Pickups != null)
                        for (int i = 0; i < W.Depth.Pickups.Length; i++)
                        {
                            Pickup p = W.Depth.Pickups[i];
                            if (p != null && p.Active && p.Kind != PickupKind.Coin && p.ReadyTick <= W.Tick) { slot = i; break; }
                        }
                    if (slot < 0) drv.StepTicks((int)drv.Config.SimHz);
                }
                if (slot >= 0)
                {
                    Pickup p = W.Depth.Pickups[slot];
                    Teleport(0, p.X, p.Z);
                    yield return StepFrames(8);
                    Note($"power-up: {p.Kind}, still active {p.Active}");
                }
                else Note("power-up: none in 120 sim s");
            }
            Busy(drv, false);
            yield return StepFrames(3);
            Note("busy HUD: " + HudReport());
            foreach (var f in Shots("11-busy-hud")) yield return f;

            {
                PadDef home = W.Map.Pads[(int)W.Actors[0].Team];
                Teleport(0, home.X, home.Z);
                yield return StepFrames(6);
                CriticalMass.Runtime.Hud.Mechanics.StationPanel.Open();
                yield return Seconds(0.5);
                foreach (var f in Shots("12-station-panel")) yield return f;
                CriticalMass.Runtime.Hud.Mechanics.StationPanel.Close();
                Teleport(0, sx, sz);
                yield return StepFrames(3);
            }

            Scoreboard.ForceOpen = true;
            yield return StepFrames(3);
            foreach (var f in Shots("13-scoreboard")) yield return f;
            Scoreboard.ForceOpen = false;

            // A map event (the first one), with the autopilot banner and the kill feed.
            {
                int eventAt = -1;
                for (int guard = 0; guard < 140 && eventAt < 0; guard++)
                {
                    int h0 = W.EventHead;
                    drv.StepTicks((int)drv.Config.SimHz);
                    if (CountEvents(W, h0, SimEventType.SupplyDropIncoming) + CountEvents(W, h0, SimEventType.EggExposed) +
                        CountEvents(W, h0, SimEventType.BridgeWarning) > 0) eventAt = W.Tick;
                }
                Busy(drv, true);
                yield return StepFrames(12);
                Note($"map event at tick {eventAt}: " + HudReport());
                foreach (var f in Shots("14-map-event-autopilot")) yield return f;
                drv.HumanAutopilot = false;
                if (src != null) src.State.Reset();
            }

            CriticalMass.Runtime.Hud.Interact.FullMap.Show();
            yield return Seconds(0.6);
            foreach (var f in Shots("15-full-map")) yield return f;
            CriticalMass.Runtime.Hud.Interact.FullMap.Hide();
            yield return StepFrames(6);

            drv.TimeScale = 1;
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Full;
            CriticalMass.Runtime.View.Moments.BigMoments.Play(CriticalMass.Runtime.View.Moments.MomentId.OpeningStrike);
            yield return Seconds(0.4);
            foreach (var f in Shots("16-big-moment", 0.12)) yield return f;
            {
                double until = EditorApplication.timeSinceStartup + 8;
                yield return () => !CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying || EditorApplication.timeSinceStartup > until;
            }
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Off;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();

            {
                drv.TimeScale = 0;
                Teleport(0, sx, sz);
                W.Actors[0].Hp = 1;
                int placed = 0;
                for (int i = 1; i < W.ActorCount && placed < 4; i++)
                    if (W.Actors[i].Alive && W.Actors[i].Team != W.Actors[0].Team) { Teleport(i, sx + 0.9 * (placed % 2 == 0 ? 1 : -1), sz + 0.5 * placed); placed++; }
                for (int guard = 0; guard < 900 && W.Actors[0].Alive; guard++) drv.StepTicks(1);
                yield return StepFrames(10);
                yield return Seconds(0.6);
                Note($"death: alive {W.Actors[0].Alive}");
                foreach (var f in Shots("17-death-recap")) yield return f;
            }

            PauseMenu pm = drv.GetComponent<PauseMenu>();
            if (pm != null)
            {
                pm.Pause();
                yield return Seconds(0.6);
                foreach (var f in Shots("18-pause")) yield return f;
                pm.Resume();
                yield return Seconds(0.3);
            }

            D.ShowResults();
            yield return Seconds(1.0);
            foreach (var f in Shots("19-results")) yield return f;
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }

        /// <summary>Every optional HUD element at once: five kill-feed lines (one the player's), an event banner, the job
        /// line and stock (already on in a depth match), coins, and the autopilot banner when asked.</summary>
        static void Busy(SimDriverBehaviour drv, bool autopilot)
        {
            World w = W;
            KillFeed kf = UnityEngine.Object.FindAnyObjectByType<KillFeed>();
            if (kf != null)
            {
                int pushed = 0;
                for (int i = 1; i < w.ActorCount && pushed < 5; i++)
                {
                    int killer = pushed == 4 ? 0 : (i + 3) % w.ActorCount;
                    var e = new CombatEvent
                    {
                        Kind = CombatEventKind.Knockout, Actor = i, Other = killer, Blow = 1,
                        ActorTeam = w.Actors[i].Team, OtherTeam = w.Actors[killer].Team,
                        ActorClass = w.Actors[i].Cls, OtherClass = w.Actors[killer].Cls, Time = w.Match.Time,
                    };
                    kf.Push(e, 0, w.Kaiju.State);
                    pushed++;
                }
            }
            Banner.Show(new BannerSpec
            {
                Headline = "SUPPLY DROP INCOMING", Reason = "A crate lands beside the hill. Grab it before Crimson does.",
                Icon = null, Badge = null, Style = BannerStyle.Warning, HoldSeconds = 120, Priority = 3,
            });
            drv.HumanAutopilot = autopilot;
        }

        /// <summary>The HUD overlap check of this frame (OwnerWalk HudAudit when the commit has it).</summary>
        static string HudReport()
        {
            Type t = Type.GetType("CriticalMass.Runtime.Hud.HudAudit, CriticalMass.Runtime");
            if (t == null) return "no HudAudit on this commit";
            try
            {
                var m = t.GetMethod("Describe", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return m != null ? (string)m.Invoke(null, null) : "no HudAudit.Describe";
            }
            catch (Exception e) { return "HudAudit failed: " + (e.InnerException ?? e).Message; }
        }

        static int CountEvents(World w, int from, SimEventType t)
        {
            int n = 0, cap = w.Events.Length;
            for (int h = Math.Max(from, w.EventHead - cap); h < w.EventHead; h++) if (w.Events[h % cap].Type == t) n++;
            return n;
        }

        static string TextIssues()
        {
            var bad = new List<string>();
            foreach (UnityEngine.UI.Text t in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.canvas == null || !t.canvas.enabled) continue;
                CanvasGroup g = t.GetComponentInParent<CanvasGroup>();
                if (g != null && g.alpha < 0.05f) continue;
                if (t.color.a < 0.05f) continue;
                Rect r = t.rectTransform.rect;
                if (r.width < 1f || r.height < 1f) continue;
                string plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "").Replace("\n", "").Replace(" ", "");
                int visible = t.cachedTextGenerator.characterCountVisible;
                bool cut = visible > 0 && visible < plain.Length - 1 && t.verticalOverflow == VerticalWrapMode.Truncate;
                bool spillW = t.horizontalOverflow == HorizontalWrapMode.Overflow && !t.resizeTextForBestFit && t.preferredWidth > r.width + 6f;
                bool spillH = t.verticalOverflow == VerticalWrapMode.Overflow && !t.resizeTextForBestFit && t.preferredHeight > r.height + 6f;
                if (cut || spillW || spillH)
                    bad.Add($"{t.name}('{(plain.Length > 18 ? plain.Substring(0, 18) : plain)}' {(cut ? "cut" : spillW ? "wide" : "tall")})");
            }
            return bad.Count == 0 ? "text ok" : "text issues " + bad.Count + ": " + string.Join(", ", bad.GetRange(0, Math.Min(8, bad.Count)));
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    if (_rt == null) SetRes(_cur);
                    if (cam.targetTexture != _rt) { cam.targetTexture = _rt; cam.aspect = (float)_cur.W / _cur.H; }
                }
                if (_wait != null)
                {
                    if (!_wait()) return;
                    _wait = null;
                }
                if (_walk.MoveNext()) _wait = _walk.Current;
                else Finish(_shots == _wanted ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("[OwnerWalk] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

        static void Shot(string name, Res r)
        {
            _wanted++;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || _rt == null)
            {
                Note(name + ": no main camera");
                return;
            }
            string file = name + "@" + r.Tag;
            string path = Path.Combine(_out, file + ".png");
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = _rt;
            cam.aspect = (float)r.W / r.H;
            try
            {
                foreach (FlowScreen s in UnityEngine.Object.FindObjectsByType<FlowScreen>(FindObjectsSortMode.None)) s.UpdateScale();
                GameHud hud = UnityEngine.Object.FindAnyObjectByType<GameHud>();
                if (hud != null) hud.UpdateScale();
                Canvas.ForceUpdateCanvases();
                Canvas.ForceUpdateCanvases();
                var req = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                else cam.Render();
                RenderTexture.active = _rt;
                var tex = new Texture2D(r.W, r.H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, r.W, r.H), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                _shots++;
                Note("wrote " + file + "; " + TextIssues() + (name.StartsWith("1", StringComparison.Ordinal) ? "; " + HudReport() : ""));
            }
            catch (Exception e) { Note(file + " FAILED: " + e.Message); }
            finally { RenderTexture.active = prev; }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            GameFlow.ForceEnabled = false;
            Scoreboard.ForceOpen = false;
            HudSafeArea.ClearOverride();
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
            foreach (RenderTexture rt in Targets.Values) if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            Targets.Clear();
            _rt = null;
            Note($"exit {code}, {_shots} of {_wanted} shot(s)");
            try { File.WriteAllLines(Path.Combine(_out, "walk.log"), Log); }
            catch (Exception) { }
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
