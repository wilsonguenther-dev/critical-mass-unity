using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Camera;
using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Runtime.View;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Editor.Flow
{
    /// <summary>
    /// Integration batch 4's one-run walk (windowless, in the editor): the lobby (title, home with the plinth fighter, mode
    /// cards, class select for all six classes, READY with named bots), four story frames, the match start with the real-input
    /// control check, the mechanics-depth view (job line, wood and water, station panel, pickup and power-up timer, ping and
    /// a bot's answer, named scoreboard, map event), physics (wind-up, hit with stagger), TNT, ladder, bridge, heal, the
    /// Kaiju hatch and claw, a big moment with its painted portrait, death and recap, pause, settings tabs, the Codex and the
    /// results. Every shot logs the labels whose text is cut or spills out of its box. Big moments are off for the mechanic
    /// shots. Usage: Unity -batchmode -projectPath P -executeMethod CriticalMass.Editor.Flow.Batch4CaptureTool.Run -walkOut DIR
    /// (needs a GPU: no -nographics). Exit 0 when every shot was written, 1 otherwise.
    /// </summary>
    public static class Batch4CaptureTool
    {
        const int Width = 1920, Height = 1080, MaxFrames = 40000;
        static string _out;
        static int _frames, _shots, _wanted;
        static IEnumerator<Func<bool>> _walk;
        static Func<bool> _wait;
        static readonly List<string> Log = new List<string>();

        public static void Run()
        {
            try
            {
                _out = Arg("-walkOut");
                if (string.IsNullOrEmpty(_out)) throw new ArgumentException("-walkOut DIR is required");
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
                Debug.LogError("[Batch4Capture] " + e);
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

        static void Note(string s)
        {
            Log.Add($"{EditorApplication.timeSinceStartup:0.0}s {s}");
            Debug.Log("[Batch4Capture] " + s);
        }

        static HumanInputState Inp => Driver != null && Driver.GetComponent<HumanInputSource>() != null ? Driver.GetComponent<HumanInputSource>().State : null;

        /// <summary>Steps one sim tick per editor frame, n times (the views draw every tick).</summary>
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

        static void Teleport(int i, double x, double z)
        {
            World w = W;
            w.X[i] = w.PrevX[i] = x;
            w.Z[i] = w.PrevZ[i] = z;
        }

        static void Frame(double x, double z)
        {
            CameraFollowTarget f = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (f != null) f.SetOverride(x, z);
        }

        static void Unframe()
        {
            CameraFollowTarget f = UnityEngine.Object.FindAnyObjectByType<CameraFollowTarget>();
            if (f != null) f.ClearOverride();
        }

        static byte CellAt(MapDef m, int cx, int cz) => cx < 0 || cz < 0 || cx >= m.Width || cz >= m.Height ? Cell.Void : m.Cells[cz * m.Width + cx];

        /// <summary>A walkable cell with a cell of type t within 2 cells along an axis (for the Builder) nearest (x, z).</summary>
        static bool FindEdge(World w, byte t, double nearX, double nearZ, out double fx, out double fz, out double ax, out double az)
        {
            MapDef m = w.Map;
            fx = fz = ax = az = 0;
            double best = double.MaxValue;
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            for (int cz = 2; cz < m.Height - 2; cz++)
            for (int cx = 2; cx < m.Width - 2; cx++)
            {
                if (!MapGrid.IsWalkableType(CellAt(m, cx, cz))) continue;
                for (int k = 0; k < 4; k++)
                {
                    if (CellAt(m, cx + dx[k], cz + dz[k]) != t) continue;
                    double d = (cx + 0.5 - nearX) * (cx + 0.5 - nearX) + (cz + 0.5 - nearZ) * (cz + 0.5 - nearZ);
                    if (d >= best) continue;
                    best = d;
                    fx = cx + 0.5; fz = cz + 0.5; ax = fx + dx[k] * 2; az = fz + dz[k] * 2;
                }
            }
            return best < double.MaxValue;
        }

        static string MomentNow()
        {
            try
            {
                var bm = CriticalMass.Runtime.View.Moments.BigMoments.Instance;
                if (bm == null) return "no BigMoments";
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object q = bm.GetType().GetField("_queue", f)?.GetValue(bm);
                if (q == null) return "no queue";
                object cur = q.GetType().GetProperty("Current")?.GetValue(q);
                object req = cur?.GetType().GetField("Request")?.GetValue(cur) ?? cur?.GetType().GetProperty("Request")?.GetValue(cur);
                object id = req?.GetType().GetField("Id")?.GetValue(req) ?? req?.GetType().GetProperty("Id")?.GetValue(req);
                return "playing " + CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying + ", " + (id ?? "?");
            }
            catch (Exception e) { return "moment? " + e.Message; }
        }

        static int _realInputFails;

        static IEnumerator<Func<bool>> Walk()
        {
            yield return () => Driver != null && W != null && D != null && !D.Loading &&
                               UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null;
            Note($"boot: state {GameFlow.State}, seed {W.Opts.Seed}");
            StoryIntro.Skip();
            yield return Seconds(1.0);
            Shot("01-title");
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Seconds(0.6);
            D.ShowMenu();
            yield return Seconds(1.2);
            GameObject plinth = GameObject.Find("Home Fighter Warrior") ?? FindPrefix("Home Fighter");
            Note($"home: menu visible {D.MainMenu.IsVisible}, plinth fighter {(plinth != null ? plinth.name + " yaw " + plinth.transform.eulerAngles.y.ToString("0") : "none")}");
            Shot("02-home-lobby");

            D.ShowModes(MatchMode.Quick);
            yield return Seconds(0.8);
            Shot("03-mode-select");
            D.HideFlowScreens();
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer })
            {
                ClassSelect.SelectedClass = c;
                ClassSelect.ShowSetup(TeamId.Azure);
                yield return Seconds(0.9);
                Note($"class select {c}: open {ClassSelect.IsOpen}, model {CriticalMass.Runtime.View.Art.ArtMapping.ClassModel(c) != null}");
                Shot("04-class-" + c.ToString().ToLowerInvariant());
            }
            ClassSelect.Hide();

            // Story intro: four frames at different shots.
            D.ShowMenu();
            yield return Seconds(0.4);
            bool story = StoryIntro.Play();
            Note($"story: playing {story}");
            int[] want = { 1, 4, 8, 10 };
            for (int k = 0; k < want.Length && story; k++)
            {
                int target = want[k];
                double sUntil = EditorApplication.timeSinceStartup + 40;
                yield return () => !StoryIntro.IsPlaying || (StoryIntro.Instance.Timeline != null && StoryIntro.Instance.Timeline.Index >= target) ||
                                   EditorApplication.timeSinceStartup > sUntil;
                yield return Seconds(1.2);
                Note($"story frame: index {(StoryIntro.Instance != null && StoryIntro.Instance.Timeline != null ? StoryIntro.Instance.Timeline.Index : -1)}");
                Shot($"06-story-{k + 1}-shot{target + 1:00}");
            }
            StoryIntro.Skip();
            yield return Seconds(1.0);

            // READY with named bots, then its countdown launches the match (seed 4242).
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver;
            MatchSetupChoice quick = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
            quick.Class = ClassId.Warrior;
            quick.Team = TeamId.Azure;
            D.ShowReady(new PlayLaunch { Choice = quick, Seed = 4242, BackTo = LobbyScreen.ModeSelect });
            yield return Seconds(2.0);
            Note($"ready: rows {D.Ready.RowsPerSide}, enemy row 0 '{D.Ready.RowName(1, 0)}', ally row 1 '{D.Ready.RowName(0, 1)}'");
            Shot("05-ready");
            double startUntil = EditorApplication.timeSinceStartup + 60;
            yield return () => (Driver != null && Driver != from && W != null && GameFlow.State == FlowState.Match && !D.Loading &&
                                !LoadingScreen.IsOpen) || EditorApplication.timeSinceStartup > startUntil;
            SimDriverBehaviour drv = Driver;
            HumanInputSource src = drv.GetComponent<HumanInputSource>();
            Note($"match: seed {W.Opts.Seed}, actors {W.ActorCount}, class {W.Actors[0].Cls}, pace {W.Opts.Pace}, timescale {drv.TimeScale}, depth {(W.Depth != null)}");
            {
                double rt = EditorApplication.timeSinceStartup + 30;
                yield return () => (W.Match.Phase == MatchPhase.Playing && W.Actors[0].Alive) || EditorApplication.timeSinceStartup > rt;
                Shot("10-match-start");
                var ins = UnityEngine.InputSystem.InputSystem.settings;
                ins.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                ins.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                var kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
                yield return Seconds(0.1);
                bool autoBefore = drv.HumanAutopilot;
                double x0 = W.X[0], z0 = W.Z[0];
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
                yield return Seconds(1.0);
                double along = (W.X[0] - x0) * HumanInputState.ScreenUpX + (W.Z[0] - z0) * HumanInputState.ScreenUpZ;
                bool ok = !drv.HumanAutopilot && along > 0.3;
                if (!ok) _realInputFails++;
                Note($"REAL INPUT keyboard: autopilot before {autoBefore}, after 1 s of W: autopilot {drv.HumanAutopilot}, moved {along:0.00} u screen-up -> {(ok ? "PASS" : "FAIL")}");
                Shot("10b-real-input-after-W");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return Seconds(0.2);
                x0 = W.X[0]; z0 = W.Z[0];
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = new Vector2(0f, 1f) });
                yield return Seconds(1.0);
                along = (W.X[0] - x0) * HumanInputState.ScreenUpX + (W.Z[0] - z0) * HumanInputState.ScreenUpZ;
                ok = !drv.HumanAutopilot && along > 0.3;
                if (!ok) _realInputFails++;
                Note($"REAL INPUT gamepad: after 1 s of left stick up: autopilot {drv.HumanAutopilot}, moved {along:0.00} u screen-up -> {(ok ? "PASS" : "FAIL")}");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                yield return Seconds(0.1);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(kb);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
            }
            drv.HumanAutopilot = false;
            drv.TimeScale = 0;
            if (src != null) src.State.Reset();
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Off;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();
            HumanInputState inp = src != null ? src.State : null;
            yield return StepFrames(15);
            Shot("11-job-line");

            // Wood and water: stand at a grove and a spring holding interact.
            double sx = W.X[0], sz = W.Z[0];
            foreach (BiomassType kind in new[] { BiomassType.Wood, BiomassType.Water })
            {
                string name = kind == BiomassType.Wood ? "12-harvest-wood" : "13-harvest-water";
                SourceNode node = null;
                double best = double.MaxValue;
                if (W.Depth != null && W.Depth.Sources != null)
                    foreach (SourceNode n in W.Depth.Sources)
                    {
                        if (n == null || n.Kind != kind || n.Charges <= 0) continue;
                        double d = (n.X - sx) * (n.X - sx) + (n.Z - sz) * (n.Z - sz);
                        if (d < best) { best = d; node = n; }
                    }
                if (node == null) { Note(name + ": no source of that kind"); Shot(name); continue; }
                Teleport(0, node.X + 0.7, node.Z);
                if (inp != null) inp.ButtonDown(HumanInputState.HumanButton.Interact);
                yield return StepFrames((int)(2.5 * drv.Config.SimHz));
                if (inp != null) inp.ButtonUp(HumanInputState.HumanButton.Interact);
                Note($"{name}: source at {node.X:0.0},{node.Z:0.0}, charges {node.Charges}, carry {W.Actors[0].Carry}");
                Frame(node.X, node.Z);
                yield return StepFrames(2);
                Shot(name);
            }
            Unframe();

            // Station panel at the own base.
            {
                PadDef pad = W.Map.Pads[(int)W.Actors[0].Team];
                Teleport(0, pad.X, pad.Z);
                yield return StepFrames(6);
                CriticalMass.Runtime.Hud.Mechanics.StationPanel.Open();
                yield return Seconds(0.5);
                Note($"station: in station {Stations.InStation(W, 0)}, panel open {CriticalMass.Runtime.Hud.Mechanics.StationPanel.IsOpen}");
                Shot("14-station-panel");
                CriticalMass.Runtime.Hud.Mechanics.StationPanel.Close();
            }

            // A power-up pickup: run the sim until one is out, stand on it, show the timer.
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
                    Frame(p.X, p.Z);
                    yield return StepFrames(4);
                    Shot("15a-pickup");
                    Teleport(0, p.X, p.Z);
                    yield return StepFrames(8);
                    Note($"pickup: {p.Kind} at {p.X:0.0},{p.Z:0.0}, sim {W.Match.Time:0}s, still active {p.Active}");
                    Shot("15b-powerup-timer");
                }
                else { Note("pickup: none appeared in 120 sim s"); Shot("15a-pickup"); }
                Unframe();
            }

            // A ping and a bot's answer (through the input latch, the same path as the wheel).
            {
                Teleport(0, sx, sz);
                yield return StepFrames(2);
                double hx = W.Map.Hill.X, hz = W.Map.Hill.Z;
                bool queued = CriticalMass.Runtime.View.Mechanics.DepthInputLatch.QueuePing(0, hx, hz);
                int h0 = W.EventHead;
                yield return StepFrames(60);
                int answered = CountEvents(W, h0, SimEventType.PingAnswered);
                Note($"ping: queued {queued}, answers seen {answered}");
                Shot("16-ping-answer");
            }
            Scoreboard.ForceOpen = true;
            yield return StepFrames(3);
            Shot("17-scoreboard-names");
            Scoreboard.ForceOpen = false;

            // A map event: run to the first one (50 s), then show its banner.
            {
                int eventAt = -1;
                for (int guard = 0; guard < 140 && eventAt < 0; guard++)
                {
                    int h0 = W.EventHead;
                    drv.StepTicks((int)drv.Config.SimHz);
                    if (CountEvents(W, h0, SimEventType.SupplyDropIncoming) + CountEvents(W, h0, SimEventType.EggExposed) +
                        CountEvents(W, h0, SimEventType.BridgeWarning) > 0) eventAt = W.Tick;
                }
                yield return StepFrames(12);
                Note($"map event: at tick {eventAt}, sim {W.Match.Time:0}s");
                Shot("18-map-event-banner");
            }

            // Physics: a wind-up and a hit with stagger on an enemy in front.
            {
                Teleport(0, sx, sz);
                SanityDir(0, 1, out double upX, out double upZ);
                int foe = -1;
                for (int i = 1; i < W.ActorCount; i++) if (W.Actors[i].Alive && W.Actors[i].Team != W.Actors[0].Team) { foe = i; break; }
                if (foe >= 0) { Teleport(foe, sx + 1.1 * upX, sz + 1.1 * upZ); W.Actors[foe].Hp = W.Actors[foe].MaxHp; }
                if (inp != null)
                {
                    inp.SetAim(sx + 3 * upX, sz + 3 * upZ);
                    yield return StepFrames(2);
                    inp.ButtonDown(HumanInputState.HumanButton.Attack);
                    yield return StepFrames(1);
                    inp.ButtonUp(HumanInputState.HumanButton.Attack);
                    int wind = W.Actors[0].SwingWindup;
                    yield return StepFrames(Math.Max(1, wind / 2));
                    Note($"wind-up: swing windup {wind} ticks, pending {W.Actors[0].SwingPending}");
                    ShotFramed("19-windup-attack", ActorModel(0), 6f, 35f);
                    yield return StepFrames(Math.Max(1, wind - wind / 2 + 2));
                    Note($"hit: foe {foe} hp {(foe >= 0 ? W.Actors[foe].Hp : 0):0}, hit-stun {(foe >= 0 ? W.Actors[foe].HitStunTicks : 0)}");
                    ShotFramed("20-hit-stagger", ActorModel(0), 6f, 35f);
                }
            }

            // Demolitions TNT.
            W.Actors[0].Cls = ClassId.Demolitions;
            Teleport(0, sx, sz);
            yield return StepFrames(2);
            {
                SanityDir(0, 1, out double upX, out double upZ);
                double tx = W.X[0] + 4 * upX, tz = W.Z[0] + 4 * upZ;
                int blast = -1;
                if (inp != null)
                {
                    inp.SetAim(tx, tz);
                    inp.ButtonDown(HumanInputState.HumanButton.DigPlace);
                    yield return StepFrames(2);
                    inp.ButtonUp(HumanInputState.HumanButton.DigPlace);
                    foreach (Bomb b in W.Demolitions.Bombs) if (b != null && b.Active) { blast = b.BlastTick; tx = b.X; tz = b.Z; }
                }
                Note($"tnt: thrown {blast >= 0}, blast tick {blast}, now {W.Tick}");
                Frame(tx, tz);
                if (blast > W.Tick + 3) drv.StepTicks(blast - W.Tick - 3);
                yield return StepFrames(6);
                Shot("21-tnt-blast");
            }

            // Builder: ladder and bridge at spots where the sim says a piece can be built.
            W.Actors[0].Cls = ClassId.Builder;
            foreach (byte t in new[] { Cell.Wall, Cell.Void })
            {
                string name = t == Cell.Wall ? "22-builder-ladder" : "23-builder-bridge";
                W.Builder.Spent[(int)W.Actors[0].Team] = -6;
                drv.StepTicks(Math.Max(0, Terraform.WaitTicks(W, W.Actors[0].Team)) + 5);
                if (!FindBuildable(W, t, sx, sz, out double fx, out double fz, out double ax, out double az, out int tried))
                { Note($"{name}: no buildable spot in {tried} candidates"); Shot(name); continue; }
                Teleport(0, fx, fz);
                if (inp != null)
                {
                    inp.SetAim(ax, az);
                    inp.ButtonDown(HumanInputState.HumanButton.Ability);
                    yield return StepFrames(2);
                    inp.ButtonUp(HumanInputState.HumanButton.Ability);
                }
                Note($"{name}: at {fx:0.0},{fz:0.0} aim {ax:0.0},{az:0.0} (candidate {tried}), owned pieces {BuiltPieces.OwnedCount(W, 0)}");
                Frame(fx, fz);
                yield return StepFrames(20);
                Shot(name);
            }
            Unframe();

            // Healer.
            W.Actors[0].Cls = ClassId.Healer;
            Teleport(0, sx, sz);
            {
                int hurt = 0;
                for (int i = 1; i < W.ActorCount; i++)
                {
                    if (W.Actors[i].Team != W.Actors[0].Team || !W.Actors[i].Alive) continue;
                    if (hurt < 3) { Teleport(i, sx + 0.8 * (hurt - 1), sz + 0.9); W.Actors[i].Hp = Math.Max(1, W.Actors[i].Hp * 0.4); hurt++; }
                }
                W.Actors[0].AbilityCd = 0;
                if (inp != null)
                {
                    inp.SetAim(sx, sz + 0.5);
                    inp.ButtonDown(HumanInputState.HumanButton.Ability);
                    yield return StepFrames(2);
                    inp.ButtonUp(HumanInputState.HumanButton.Ability);
                }
                yield return StepFrames(6);
                Note($"healer: {hurt} hurt allies, mends {W.Healer.Stats.Mends}");
                ShotFramed("24-healer-heal", ActorModel(0), 8f, 45f);
            }
            W.Actors[0].Cls = ClassId.Warrior;

            // Kaiju hatch and claw.
            W.ForceHatch = true;
            for (int guard = 0; guard < 600 && W.Kaiju.State == KaijuState.Egg; guard++) drv.StepTicks(1);
            Frame(W.Kaiju.X, W.Kaiju.Z);
            yield return StepFrames(10);
            Note($"kaiju hatch: state {W.Kaiju.State}");
            ShotFramed("25-kaiju-hatch", KaijuModel(), 14f, 45f);
            for (int guard = 0; guard < 3000 && W.Kaiju.MeleeWindupIn <= 0; guard++) drv.StepTicks(1);
            Frame(W.Kaiju.X, W.Kaiju.Z);
            yield return StepFrames(2);
            Note($"kaiju claw: windup {W.Kaiju.MeleeWindupIn}");
            ShotFramed("26-kaiju-claw", KaijuModel(), 12f, 50f);
            Unframe();

            // A big moment with its painted portrait (the Warrior's, then the Kaiju's).
            drv.TimeScale = 1;
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Full;
            CriticalMass.Runtime.View.Moments.BigMoments.Play(CriticalMass.Runtime.View.Moments.MomentId.OpeningStrike);
            yield return Seconds(0.45);
            Note($"big moment: {MomentNow()}, portrait {CriticalMass.Runtime.View.Moments.BigMoments.LastPortraitSource}");
            Shot("27a-big-moment-portrait");
            yield return () => !CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying;
            CriticalMass.Runtime.View.Moments.BigMoments.Play(CriticalMass.Runtime.View.Moments.MomentId.KaijuDown);
            yield return Seconds(0.45);
            Note($"big moment: {MomentNow()}, portrait {CriticalMass.Runtime.View.Moments.BigMoments.LastPortraitSource}");
            Shot("27b-big-moment-kaiju");
            yield return () => !CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying;
            CriticalMass.Runtime.View.Moments.BigMomentsSettings.ModeOverride = CriticalMass.Runtime.View.Moments.BigMomentsMode.Off;
            CriticalMass.Runtime.View.Moments.BigMoments.StopAll();

            // Death and recap: actor 0 at 1 hp among enemies.
            {
                drv.TimeScale = 0;
                Teleport(0, sx, sz);
                W.Actors[0].Hp = 1;
                int placed = 0;
                for (int i = 1; i < W.ActorCount && placed < 4; i++)
                    if (W.Actors[i].Alive && W.Actors[i].Team != W.Actors[0].Team) { Teleport(i, sx + 0.9 * (placed % 2 == 0 ? 1 : -1), sz + 0.5 * placed); placed++; }
                for (int guard = 0; guard < 900 && W.Actors[0].Alive; guard++) drv.StepTicks(1);
                yield return StepFrames(10);
                Note($"death: alive {W.Actors[0].Alive}, cause {W.Actors[0].LastCause}");
                yield return Seconds(0.6);
                Shot("28-death-recap");
            }

            PauseMenu pm = drv.GetComponent<PauseMenu>();
            if (pm != null)
            {
                pm.Pause();
                yield return Seconds(0.6);
                Shot("29-pause");
                foreach (PauseMenu.Tab tab in new[] { PauseMenu.Tab.Controls, PauseMenu.Tab.Video })
                {
                    pm.ShowSettings(tab);
                    yield return Seconds(0.5);
                    Shot("30-settings-" + tab.ToString().ToLowerInvariant());
                }
                int access = PauseMenu.ExtraTabs.FindIndex(x => x.Name == SettingsExtras.TabName);
                if (access >= 0)
                {
                    pm.ShowSettings((PauseMenu.Tab)(4 + access));
                    yield return Seconds(0.5);
                    Shot("30-settings-access");
                }
                int codex = PauseMenu.ExtraPages.FindIndex(x => x.Label != null && x.Label.ToUpperInvariant().Contains("CODEX"));
                if (codex >= 0)
                {
                    pm.ShowExtra(codex);
                    yield return Seconds(0.6);
                    Note($"codex: page {codex}");
                    Shot("31-codex-watch-story");
                }
                else Note("codex: no extra page named CODEX");
                pm.Resume();
                yield return Seconds(0.3);
            }

            D.ShowResults();
            yield return Seconds(1.0);
            Note($"results: HOME button '{(D.Results.MainMenuItem != null ? LabelOf(D.Results.MainMenuItem) : "none")}'");
            Shot("32-results-summary");
            D.Results.ShowTab(ResultsTab.Rewards);
            yield return Seconds(0.5);
            Shot("33-results-rewards");
            D.Results.ShowTab(ResultsTab.Highlights);
            yield return Seconds(0.5);
            Shot("34-results-highlights");
            Note($"REAL INPUT summary: {(_realInputFails == 0 ? "PASS" : "FAIL (" + _realInputFails + ")")}");
        }

        /// <summary>Events of type t written to the ring since head value from (at most one ring's worth).</summary>
        static int CountEvents(World w, int from, SimEventType t)
        {
            int n = 0, cap = w.Events.Length;
            for (int h = Math.Max(from, w.EventHead - cap); h < w.EventHead; h++) if (w.Events[h % cap].Type == t) n++;
            return n;
        }

        static GameObject FindPrefix(string prefix)
        {
            foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith(prefix, StringComparison.Ordinal)) return t.gameObject;
            return null;
        }

        static string LabelOf(CriticalMass.Runtime.Menu.MenuItem item)
        {
            UnityEngine.UI.Text t = item.GetComponentInChildren<UnityEngine.UI.Text>(true);
            return t != null ? t.text : item.name;
        }

        /// <summary>The nearest edge cell beside a cell of type t where BuiltPieces.CanBuild says yes for actor 0 standing on it.</summary>
        static bool FindBuildable(World w, byte t, double nearX, double nearZ, out double fx, out double fz, out double ax, out double az, out int tried)
        {
            MapDef m = w.Map;
            fx = fz = ax = az = 0;
            tried = 0;
            var cands = new List<(double d, double fx, double fz, double ax, double az)>();
            int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
            for (int cz = 2; cz < m.Height - 2; cz++)
            for (int cx = 2; cx < m.Width - 2; cx++)
            {
                if (!MapGrid.IsWalkableType(CellAt(m, cx, cz))) continue;
                for (int k = 0; k < 4; k++)
                {
                    if (CellAt(m, cx + dx[k], cz + dz[k]) != t) continue;
                    double px = cx + 0.5, pz = cz + 0.5;
                    cands.Add(((px - nearX) * (px - nearX) + (pz - nearZ) * (pz - nearZ), px, pz, px + dx[k] * 2, pz + dz[k] * 2));
                }
            }
            cands.Sort((a, b) => a.d.CompareTo(b.d));
            double ox = w.X[0], oz = w.Z[0];
            foreach (var c in cands)
            {
                if (++tried > 600) break;
                Teleport(0, c.fx, c.fz);
                if (!BuiltPieces.CanBuild(w, 0, c.ax, c.az)) continue;
                fx = c.fx; fz = c.fz; ax = c.ax; az = c.az;
                return true;
            }
            Teleport(0, ox, oz);
            return false;
        }

        /// <summary>Labels on screen whose text is cut short or runs out of its box (the walk's spill check).</summary>
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
            return bad.Count == 0 ? "text ok" : "text issues " + bad.Count + ": " + string.Join(", ", bad.GetRange(0, Math.Min(6, bad.Count)));
        }

        static void SanityDir(double right, double up, out double dx, out double dz)
        {
            dx = right * HumanInputState.ScreenRightX + up * HumanInputState.ScreenUpX;
            dz = right * HumanInputState.ScreenRightZ + up * HumanInputState.ScreenUpZ;
        }

        static Transform ActorModel(int i)
        {
            string prefix = "Actor " + i + " ";
            foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (a.gameObject.activeInHierarchy && a.name.StartsWith(prefix, StringComparison.Ordinal)) return a.transform;
            GameObject cap = GameObject.Find("Actor " + i);
            return cap != null ? cap.transform : null;
        }

        static Transform KaijuModel()
        {
            GameObject g = GameObject.Find("Kaiju Adult");
            if (g == null) g = GameObject.Find("Kaiju Hatchling");
            return g != null ? g.transform : null;
        }

        static void ShotFramed(string name, Transform target, float dist, float pitch)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || target == null)
            {
                Note(name + ": no camera or no target, shot at the gameplay framing");
                Shot(name);
                return;
            }
            Vector3 pos = cam.transform.position;
            Quaternion rot = cam.transform.rotation;
            cam.transform.rotation = Quaternion.Euler(pitch, rot.eulerAngles.y, 0f);
            cam.transform.position = target.position + Vector3.up * 1.0f - cam.transform.forward * dist;
            try { Shot(name); }
            finally { cam.transform.position = pos; cam.transform.rotation = rot; }
        }

        static Func<bool> SimAt(double t)
        {
            double until = EditorApplication.timeSinceStartup + 40;
            return () => W == null || W.Match.Time >= t || EditorApplication.timeSinceStartup > until;
        }

        static int Dead(World w)
        {
            int n = 0;
            for (int i = 0; w != null && i < w.ActorCount; i++) if (!w.Actors[i].Alive) n++;
            return n;
        }

        static void Drawer(string name)
        {
            try
            {
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                Type dt = typeof(ResultsScreen).GetNestedType("Drawer", System.Reflection.BindingFlags.NonPublic);
                typeof(ResultsScreen).GetMethod("ToggleDrawer", f).Invoke(D.Results, new[] { Enum.Parse(dt, name) });
                Note("drawer " + name + " toggled");
            }
            catch (Exception e) { Note("drawer " + name + " FAILED: " + (e.InnerException ?? e).Message); }
        }

        static void Update()
        {
            try
            {
                if (++_frames > MaxFrames) throw new TimeoutException($"after {MaxFrames} frames");
                if (!EditorApplication.isPlaying) return;
                // One 1920x1080 target for the whole walk, so every screen's own per-frame scaling sees a 1080p camera.
                UnityEngine.Camera cam = UnityEngine.Camera.main;
                if (cam != null)
                {
                    if (_rt == null)
                    {
                        _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                        _rt.Create();
                    }
                    if (cam.targetTexture != _rt) { cam.targetTexture = _rt; cam.aspect = (float)Width / Height; }
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
                Debug.LogError("[Batch4Capture] " + e);
                Note("FAILED: " + e.Message);
                Finish(1);
            }
        }

        static RenderTexture _rt;

        static void Shot(string name)
        {
            _wanted++;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null || _rt == null)
            {
                Note(name + ": no main camera");
                return;
            }
            string path = Path.Combine(_out, name + ".png");
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = _rt;
            cam.aspect = (float)Width / Height;
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
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                _shots++;
                Note("wrote " + name + "; " + TextIssues());
            }
            catch (Exception e) { Note(name + " FAILED: " + e.Message); }
            finally { RenderTexture.active = prev; }
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Update;
            GameFlow.ForceEnabled = false;
            Scoreboard.ForceOpen = false;
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
            Note($"exit {code}, {_shots} of {_wanted} shot(s)");
            try
            {
                File.WriteAllLines(Path.Combine(_out, "walk.log"), Log);
            }
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
