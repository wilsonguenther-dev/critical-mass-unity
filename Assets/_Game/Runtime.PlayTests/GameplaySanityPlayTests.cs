using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// Gameplay sanity suite (test/gameplay-sanity, 2026-11-12), part 3 of 3: GROUNDING, TEAM AND IDENTITY, WORLD
    /// AGREEMENT (drawn), CONTROL and HUD TRUTH, on the real Boot game. Each check is something a person watching the
    /// game would notice at once and a rule test cannot see. A red means fix the GAME; the message says what a player sees.
    /// </summary>
    public sealed class GameplaySanityPlayTests
    {
        SanityWorld _w;
        readonly List<string> _bad = new List<string>();
        Keyboard _kb;
        Mouse _mouse;
        bool _settingsSaved;
        InputSettings.BackgroundBehavior _savedBackground;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;

        [SetUp]
        public void SetUp() => _bad.Clear();

        [TearDown]
        public void TearDown()
        {
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            _kb = null;
            _mouse = null;
            if (_settingsSaved)
            {
                InputSettings s = InputSystem.settings;
                s.backgroundBehavior = _savedBackground;
                s.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                _settingsSaved = false;
            }
            GameFlow.ForceEnabled = false;
            SanityWorld.Reset();
            _w = null;
        }

        void Fail(string line, int cap = 40)
        {
            if (_bad.Count < cap) _bad.Add(line);
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Lets the camera rig settle on actor 0 (its springs run on unscaled frame time).</summary>
        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        // ------------------------------------------------------------------------------------------------ 3. GROUNDING

        /// <summary>
        /// 20 sim seconds of 16v16 bots, sampled every second: every living, grounded model stands on the ground of its
        /// cell (feet within -0.2..+0.35 u), at its sim position, upright, at its art height, never inside a wall cell; its
        /// ring sits under it; the Kaiju's feet are on the ground too.
        /// </summary>
        [UnityTest]
        public IEnumerator ModelsStandOnTheirGroundUprightToScaleAndUnderTheirRings()
        {
            _w = new SanityWorld();
            yield return _w.Load(autopilot: true);
            World w = _w.W;
            MapDef map = w.Map;
            UnitRings rings = Object.FindAnyObjectByType<UnitRings>();
            int checkedModels = 0;
            for (int s = 0; s < 20; s++)
            {
                _w.Driver.StepTicks(30);
                yield return null;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    if (!a.Alive) continue;
                    _w.SimPos(i, out double x, out double z);
                    int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                    string at = $"actor {i} ({_w.ActorModelKey(i)}) at {w.Match.Time:0} s, sim ({x:0.0}, {z:0.0})";
                    bool onBridge = cx >= 0 && cz >= 0 && cx < map.Width && cz < map.Height && map.Cells[cz * map.Width + cx] == Cell.Bridge;
                    if (!MapGrid.IsWalkable(map, cx, cz) && !onBridge && a.AirLen == 0)
                        Fail($"{at} stands inside a non-walkable cell (wall or void): the player sees it inside a wall or over the void");
                    if (rings != null && i < rings.Capacity)
                    {
                        UnitRings.Style want = i == UnitRings.SelfId ? UnitRings.Style.Self : a.Team == w.Actors[0].Team ? UnitRings.Style.Ally : UnitRings.Style.Enemy;
                        if (rings.StyleOf(i) != want) Fail($"{at}: its ring is drawn as {rings.StyleOf(i)}, it should be {want}");
                        float ringOff = (Flat(rings.CentreOf(i)) - Flat(SimSpace.ToWorld(x, 0, z))).magnitude;
                        if (ringOff > 0.3f) Fail($"{at}: its ring is {ringOff:0.00} u away from it: rings sit under the wrong actor");
                    }
                    Transform model = _w.ActorModel(i);
                    if (model == null || a.AirLen > 0) continue;
                    ModelSpec spec = ArtMapping.Spec(_w.ActorModelKey(i));
                    checkedModels++;
                    float off = (Flat(model.position) - Flat(SimSpace.ToWorld(x, 0, z))).magnitude;
                    if (off > 0.35f) Fail($"{at}: its model is drawn {off:0.00} u from its sim position");
                    float g = (float)_w.Ground(x, z);
                    float feet = float.PositiveInfinity;
                    foreach (string bone in new[] { "toes.l", "toes.r", "foot.l", "foot.r" })
                    {
                        Transform t = SanityRig.Find(model, bone);
                        if (t != null) feet = Mathf.Min(feet, t.position.y);
                    }
                    if (!float.IsInfinity(feet))
                    {
                        float lift = feet - g;
                        if (lift > 0.35f) Fail($"{at}: its feet float {lift:0.00} u above the ground of its cell");
                        else if (lift < -0.2f) Fail($"{at}: its feet sink {-lift:0.00} u into the ground of its cell");
                    }
                    if (Vector3.Dot(model.up, Vector3.up) < Mathf.Cos(25f * Mathf.Deg2Rad))
                        Fail($"{at}: its model leans {Vector3.Angle(model.up, Vector3.up):0} degrees off upright");
                    Transform head = SanityRig.Find(model, "head"), hips = SanityRig.Find(model, "hips");
                    if (spec != null && head != null && hips != null && head.position.y - hips.position.y < 0.15f * spec.Height)
                        Fail($"{at}: its head is not above its hips (lying down or upside down while alive)");
                    Renderer body = SanityWorld.Body(model, spec);
                    if (spec != null && body != null && spec.Height > 0)
                    {
                        float ratio = body.bounds.size.y / spec.Height;
                        if (ratio < 0.7f || ratio > 1.4f)
                            Fail($"{at}: its body is drawn {body.bounds.size.y:0.00} u tall, the art direction asks {spec.Height:0.00} u (wrong scale)");
                    }
                }
                Transform kaiju = _w.KaijuModel();
                Kaiju k = w.Kaiju;
                if (kaiju != null && k.KoIn <= 0)
                {
                    float g = (float)_w.Ground(k.X, k.Z);
                    Transform fl = SanityRig.Find(kaiju, "Foot_L"), fr = SanityRig.Find(kaiju, "Foot_R");
                    if (fl != null && fr != null)
                    {
                        float lift = Mathf.Min(fl.position.y, fr.position.y) - g;
                        if (lift > 0.6f || lift < -0.4f) Fail($"the Kaiju's feet are {lift:0.00} u from the ground under it (floating or sunk)");
                    }
                }
            }
            if (checkedModels == 0) Assert.Inconclusive("no actor had an imported model to check");
            CollectionAssert.IsEmpty(_bad, $"grounding and placement problems (first {_bad.Count}):\n" + string.Join("\n", _bad));
        }

        // ------------------------------------------------------------------------------------------------ 4. TEAM AND IDENTITY

        /// <summary>
        /// Every actor's model is its sim class's model in its sim team's colours; actor 0 has the self ring and the camera
        /// keeps it near the centre of the screen; the minimap's own marker and hill sit the same way round as the view.
        /// </summary>
        [UnityTest]
        public IEnumerator ClassesTeamsRingCameraAndMinimapMatchTheSim()
        {
            _w = new SanityWorld();
            yield return _w.Load();
            yield return Frames(90);
            _w.Sync();
            World w = _w.W;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive) continue;
                string want = ArtMapping.ClassModel(a.Cls);
                string got = _w.ActorModelKey(i);
                if (SanityWorld.HasModel(want))
                {
                    if (got != want) Fail($"actor {i} is a {a.Cls} in the sim but is drawn as {(got ?? "a grey capsule")}");
                }
                else if (got != null) Fail($"actor {i} is a {a.Cls} (no model yet) but is drawn as {got}");
                else if (_w.ActorCapsule(i) == null) Fail($"actor {i} ({a.Cls}) is not drawn at all");
                if (got == null) continue;
                ModelSpec spec = ArtMapping.Spec(got);
                Renderer body = SanityWorld.Body(_w.ActorModel(i), spec);
                Material m = body != null ? body.sharedMaterial : null;
                if (m == null || !m.HasProperty("_TeamCloth") || m.name.IndexOf("flash", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Color c = m.GetColor("_TeamCloth"), t = ArtMaterials.Hex(ArtMapping.Swatch(spec, a.Team).Cloth);
                if (Mathf.Abs(c.r - t.r) > 0.02f || Mathf.Abs(c.g - t.g) > 0.02f || Mathf.Abs(c.b - t.b) > 0.02f)
                    Fail($"actor {i} is on {a.Team} in the sim but wears #{ColorUtility.ToHtmlStringRGB(c)} instead of {ArtMapping.Swatch(spec, a.Team).Cloth}");
            }

            UnitRings rings = Object.FindAnyObjectByType<UnitRings>();
            if (rings == null) Fail("no UnitRings in the game: the player cannot find themself");
            else if (rings.StyleOf(0) != UnitRings.Style.Self) Fail($"actor 0's ring is {rings.StyleOf(0)}, not the self ring");

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsNotNull(cam, "no main camera");
            Vector3 me = _w.WorldPos(0);
            Vector3 vp = cam.WorldToViewportPoint(me + Vector3.up);
            if (vp.z <= 0 || vp.x < 0.25f || vp.x > 0.75f || vp.y < 0.25f || vp.y > 0.75f)
                Fail($"the camera does not follow actor 0: it is at viewport ({vp.x:0.00}, {vp.y:0.00}), the middle half is 0.25..0.75");

            GameHud hud = _w.Hud;
            if (hud == null || hud.MapArea == null) Fail("no minimap");
            else
            {
                RectTransform you = FindRect(hud.MapArea, "Map You"), hill = FindRect(hud.MapArea, "Map Hill");
                if (you == null || hill == null) Fail("the minimap has no 'Map You' or 'Map Hill' marker");
                else
                {
                    Vector3 hillWorld = SimSpace.ToWorld(w.Map.Hill.X, _w.Ground(w.Map.Hill.X, w.Map.Hill.Z), w.Map.Hill.Z);
                    Vector2 onScreen = (Vector2)(cam.WorldToScreenPoint(hillWorld) - cam.WorldToScreenPoint(me));
                    Vector2 onMap = hill.anchoredPosition - you.anchoredPosition;
                    if (onScreen.magnitude > 20f && onMap.magnitude > 2f && Vector2.Angle(onScreen, onMap) > 25f)
                        Fail($"the minimap is turned {Vector2.Angle(onScreen, onMap):0} degrees against the view: the hill is one way on screen and another on the map");
                    RectTransform arrow = FindRect(you, "Facing");
                    if (arrow != null)
                    {
                        float z = arrow.localEulerAngles.z * Mathf.Deg2Rad;
                        Vector2 mapFacing = new Vector2(Mathf.Cos(z), Mathf.Sin(z));
                        Vector2 screenFacing = (Vector2)(cam.WorldToScreenPoint(me + 2f * SanityRig.SimDir(w.Actors[0].Facing)) - cam.WorldToScreenPoint(me));
                        if (screenFacing.magnitude > 5f && Vector2.Angle(mapFacing, screenFacing) > 30f)
                            Fail($"the minimap's own arrow points {Vector2.Angle(mapFacing, screenFacing):0} degrees away from where actor 0 faces on screen");
                    }
                }
            }
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));
        }

        static RectTransform FindRect(Transform root, string name)
        {
            Transform t = SanityRig.Find(root, name);
            return t as RectTransform;
        }

        // ------------------------------------------------------------------------------------------------ 5. WORLD AGREEMENT (drawn)

        /// <summary>
        /// The dressed world's ground chunks really exist and cover every land cell at its sim height (the plan-level checks
        /// are EditMode GameplaySanityTests). Skipped when the world kit is not imported (the grey box draws every cell).
        /// </summary>
        [UnityTest]
        public IEnumerator DrawnGroundCoversEveryLandCellAtItsHeight()
        {
            _w = new SanityWorld();
            yield return _w.Load(settleSeconds: 0);
            yield return Frames(3);
            WorldView view = _w.Driver.GetComponent<WorldView>();
            if (view == null || view.Plan == null) Assert.Ignore("no dressed world (kit not imported): the grey-box view draws every cell");
            Transform art = _w.Driver.transform.Find("World Art");
            Assert.IsNotNull(art, "the dressed world has no 'World Art' root");
            MapDef map = _w.W.Map;
            var bounds = new Dictionary<int, Bounds?>();
            int missing = 0;
            for (int cz = 0; cz < map.Height; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                int i = cz * map.Width + cx;
                if (!WorldDresser.Solid(map.Cells[i])) continue;
                int chunk = (cz / WorldView.ChunkSize) * 64 + cx / WorldView.ChunkSize;
                if (!bounds.TryGetValue(chunk, out Bounds? b))
                {
                    Transform go = art.Find("Ground " + chunk);
                    Renderer r = go != null ? go.GetComponent<Renderer>() : null;
                    b = r != null && r.enabled && go.gameObject.activeInHierarchy ? r.bounds : (Bounds?)null;
                    bounds[chunk] = b;
                }
                var top = new Vector3(cx + 0.5f, (float)map.Height01[i], -(cz + 0.5f));
                if (b == null) { if (missing++ < 10) Fail($"land cell {cx},{cz} has no drawn ground chunk: a hole where the sim lets players stand"); continue; }
                Bounds e = b.Value;
                e.Expand(0.1f);
                if (!e.Contains(top)) { if (missing++ < 10) Fail($"land cell {cx},{cz}: the drawn ground chunk does not reach its sim height {map.Height01[i]:0.00}"); }
            }
            CollectionAssert.IsEmpty(_bad, $"{missing} land cells without drawn ground:\n" + string.Join("\n", _bad));
        }

        // ------------------------------------------------------------------------------------------------ 6. CONTROL

        void RouteInputToGame()
        {
            InputSettings s = InputSystem.settings;
            _savedBackground = s.backgroundBehavior;
            _savedEditorBehaviour = s.editorInputBehaviorInPlayMode;
            _settingsSaved = true;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        /// <summary>
        /// A virtual keyboard through the real actions and HumanInputSource: W, A, S, D and the arrows each move actor 0
        /// in the matching direction ON SCREEN, measured through the real gameplay camera (not the sim's basis constants).
        /// </summary>
        [UnityTest]
        public IEnumerator EachMovementKeyMovesActor0TheMatchingWayOnScreen()
        {
            _w = new SanityWorld();
            yield return _w.Load(manualInput: false);
            RouteInputToGame();
            _kb = InputSystem.AddDevice<Keyboard>();
            yield return Frames(60);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsNotNull(cam, "no main camera");
            var keys = new[] { Key.W, Key.A, Key.S, Key.D, Key.UpArrow, Key.LeftArrow, Key.DownArrow, Key.RightArrow };
            var dirs = new[] { Vector2.up, Vector2.left, Vector2.down, Vector2.right, Vector2.up, Vector2.left, Vector2.down, Vector2.right };
            int brake = _w.Ticks(_w.Cfg.Movement.BrakeSeconds) + 2;
            for (int k = 0; k < keys.Length; k++)
            {
                Vector3 start = _w.WorldPos(0);
                InputSystem.QueueStateEvent(_kb, new KeyboardState(keys[k]));
                InputSystem.Update();
                for (int t = 0; t < 15; t++)
                {
                    yield return null;
                    _w.Driver.StepTicks(1);
                }
                InputSystem.QueueStateEvent(_kb, new KeyboardState());
                InputSystem.Update();
                for (int t = 0; t < brake; t++)
                {
                    yield return null;
                    _w.Driver.StepTicks(1);
                }
                Vector3 end = _w.WorldPos(0);
                Vector2 moved = (Vector2)(cam.WorldToScreenPoint(end) - cam.WorldToScreenPoint(start));
                if (moved.magnitude < 10f) Fail($"{keys[k]} moved actor 0 only {moved.magnitude:0} px on screen");
                else if (Vector2.Angle(moved, dirs[k]) > 30f)
                    Fail($"{keys[k]} moves actor 0 {Vector2.Angle(moved, dirs[k]):0} degrees away from {(dirs[k] == Vector2.up ? "up" : dirs[k] == Vector2.down ? "down" : dirs[k] == Vector2.left ? "left" : "right")} on screen");
            }
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));
        }

        /// <summary>
        /// A normal launch (flow off in the editor, and a match started through the game flow as in a player build) puts a
        /// person on actor 0, not the autopilot: with no input actor 0 stays where it is.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHumanSlotIsHumanDrivenOnANormalLaunch()
        {
            SceneManager.LoadScene(SanityWorld.BootScene);
            yield return null;
            yield return null;
            SimDriverBehaviour d = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(d, "Boot did not build a SimDriverBehaviour");
            Assert.IsFalse(SimDriverBehaviour.CaptureMode, "capture mode is left on: actor 0 would be on autopilot");
            Assert.IsFalse(d.HumanAutopilot, "a normal launch puts actor 0 on AUTOPILOT: the player's character walks by itself");
            Assert.IsTrue(d.World.Actors[0].IsHuman, "actor 0 is not marked human");
            d.TimeScale = 0;
            d.StepTicks(90);
            double x0 = d.World.X[0], z0 = d.World.Z[0];
            d.StepTicks(90);
            double moved = Math.Sqrt((d.World.X[0] - x0) * (d.World.X[0] - x0) + (d.World.Z[0] - z0) * (d.World.Z[0] - z0));
            Assert.Less(moved, 1.5, $"with no input actor 0 walked {moved:0.0} u in 3 sim seconds: something other than the player drives it");

            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            GameFlow.QueueMatch(MatchSetupChoice.Default);
            SceneManager.LoadScene(SanityWorld.BootScene);
            yield return null;
            yield return null;
            yield return null;
            d = Object.FindAnyObjectByType<SimDriverBehaviour>();
            Assert.IsNotNull(d, "Boot did not build a SimDriverBehaviour for the flow's match");
            Assert.IsFalse(d.HumanAutopilot, "a match started from the menus (player-build flow) puts actor 0 on AUTOPILOT");
            Assert.IsTrue(d.World.Actors[0].IsHuman, "actor 0 of the flow's match is not marked human");
        }

        /// <summary>A virtual mouse: clicking 3 u from actor 0 in 4 screen directions attacks toward the cursor each time.</summary>
        [UnityTest]
        public IEnumerator AttackInputAttacksTowardTheCursor()
        {
            _w = new SanityWorld();
            yield return _w.Load(manualInput: false);
            RouteInputToGame();
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
            yield return Frames(60);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsNotNull(cam, "no main camera");
            World w = _w.W;
            int cooldown = _w.Ticks(_w.Cfg.Class(w.Actors[0].Cls).AttackCooldown) + 2;
            for (int k = 0; k < 8; k += 2)
            {
                double ang = k * Math.PI / 4;
                SanityWorld.FromScreenIntent(Math.Cos(ang), Math.Sin(ang), out double dx, out double dz);
                double tx = w.X[0] + 3 * dx, tz = w.Z[0] + 3 * dz;
                Vector3 p = cam.WorldToScreenPoint(SimSpace.ToWorld(tx, _w.Ground(w.X[0], w.Z[0]), tz));
                var pos = new Vector2(p.x, p.y);
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = pos });
                InputSystem.Update();
                yield return null;
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = pos }.WithButton(MouseButton.Left));
                InputSystem.Update();
                yield return null;
                _w.Driver.StepTicks(1);
                Actor a = w.Actors[0];
                string where = SanityWorld.ScreenWord(k);
                if (!(a.AttackCd > 0 && a.AnimAction < 0.1)) Fail($"clicking {where} of actor 0 did not attack");
                float err = SanityRig.AngleDeg(SanityRig.SimDir(a.Facing), SimSpace.ToWorld(tx - w.X[0], 0, tz - w.Z[0]));
                if (err > 20f) Fail($"clicking {where} of actor 0 attacks {err:0} degrees away from the cursor");
                InputSystem.QueueStateEvent(_mouse, new MouseState { position = pos });
                InputSystem.Update();
                for (int t = 0; t < cooldown; t++)
                {
                    yield return null;
                    _w.Driver.StepTicks(1);
                }
            }
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));
        }

        // ------------------------------------------------------------------------------------------------ 7. HUD TRUTH

        static string Mmss(double seconds)
        {
            int t = (int)Math.Ceiling(Math.Max(0, seconds) - 1e-6);
            if (t < 0) t = 0;
            return (t / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        static string HillWord(HillState h) =>
            h == HillState.Azure ? "AZURE HOLDS" : h == HillState.Crimson ? "CRIMSON HOLDS" : h == HillState.Contested ? "CONTESTED" : "NEUTRAL";

        /// <summary>
        /// Each second for 20 sim seconds of bots (the Kaiju forced to hatch at 10 s): the HUD's clock, both scores, the
        /// hill owner, the Kaiju word and meter and the player's health read what the sim says.
        /// </summary>
        [UnityTest]
        public IEnumerator HudShowsWhatTheSimSaysEverySecondFor20Seconds()
        {
            _w = new SanityWorld();
            yield return _w.Load(autopilot: true);
            GameHud.PreviewOverride = null;
            GameHud hud = _w.Hud;
            Assert.IsNotNull(hud, "no GameHud");
            Assert.IsNotNull(hud.Root, "the HUD was never built");
            var labels = new Dictionary<string, Text>();
            foreach (Text t in hud.Root.GetComponentsInChildren<Text>(true))
                if (!labels.ContainsKey(t.name)) labels[t.name] = t;
            string[] names = { "Clock", "Azure Score", "Crimson Score", "Hill Word", "Kaiju Word", "Meter Number", "HP Number" };
            foreach (string n in names) if (!labels.ContainsKey(n)) Fail($"the HUD has no '{n}' label");
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));

            World w = _w.W;
            GameConfig c = _w.Cfg;
            for (int s = 1; s <= 20; s++)
            {
                if (s == 10) w.ForceHatch = true;
                _w.Driver.StepTicks((int)c.SimHz);
                yield return null;
                MatchState m = w.Match;
                string when = $"at match time {m.Time:0.0} s";
                void Expect(string label, string want)
                {
                    string got = labels[label].text;
                    if (got != want) Fail($"{when} the HUD's {label} reads '{got}' but the sim says '{want}'");
                }
                void Near(string label, int want)
                {
                    string got = labels[label].text;
                    if (!int.TryParse(got, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || Math.Abs(v - want) > 1)
                        Fail($"{when} the HUD's {label} reads '{got}' but the sim says {want}");
                }
                Expect("Clock", Mmss(c.MatchSeconds - m.Time));
                Expect("Azure Score", ((int)Math.Floor(m.HillSeconds.Azure)).ToString(CultureInfo.InvariantCulture));
                Expect("Crimson Score", ((int)Math.Floor(m.HillSeconds.Crimson)).ToString(CultureInfo.InvariantCulture));
                Expect("Hill Word", HillWord(m.HillState));
                Kaiju k = w.Kaiju;
                bool egg = !m.Hatched || k.State == KaijuState.Egg;
                if (egg)
                {
                    int left = (int)Math.Ceiling(Math.Max(0, c.HatchSeconds - m.Time) - 1e-6);
                    Expect("Kaiju Word", "HATCH " + (left / 60).ToString("0", CultureInfo.InvariantCulture) + ":" + (left % 60).ToString("00", CultureInfo.InvariantCulture));
                    Near("Meter Number", Mathf.RoundToInt(Mathf.Clamp01((float)(m.Time / c.HatchSeconds)) * 100f));
                }
                else
                {
                    Expect("Kaiju Word", k.State == KaijuState.Azure ? "AZURE" : k.State == KaijuState.Crimson ? "CRIMSON" : k.State == KaijuState.Berserk ? "BERSERK" : "NEUTRAL");
                    Near("Meter Number", Mathf.RoundToInt(Mathf.Clamp01(k.MaxHp > 0 ? (float)(k.Hp / k.MaxHp) : 0f) * 100f));
                }
                Actor me = w.Actors[0];
                Near("HP Number", me.Alive ? Mathf.CeilToInt((float)Math.Max(0, me.Hp)) : 0);
            }
            CollectionAssert.IsEmpty(_bad, $"HUD disagrees with the sim (first {_bad.Count}):\n" + string.Join("\n", _bad));
        }
    }
}
