using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Full map, pings and the clickable HUD (feat/map-overlay-pings-hud-interactive): full-map markers sit where the
    /// minimap's projection puts the sim positions, the full map hides far enemies by the minimap's own rule, the ping
    /// rate limit and fade, every HUD element in the table has a tooltip and resolves on the HUD, and HUD or map input
    /// never reaches the sim's attack.
    /// </summary>
    public class MapPingHudTests
    {
        static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        static void Run(World w, int ticks)
        {
            var scratch = new InputFrame();
            for (int i = 0; i < ticks; i++) MatchTick.AdvanceNoHuman(w, scratch);
        }

        static RectTransform NewRoot(out GameObject go)
        {
            go = new GameObject("Full map test", typeof(RectTransform));
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            return (RectTransform)go.transform;
        }

        /// <summary>The expected viewport point, computed here from the projection alone (not through the view's code).</summary>
        static Vector2 Expected(double x, double z, float zoom, Vector2 centre)
        {
            MinimapProjection p = MinimapGeometry.Projection;
            float fit = Mathf.Min(FullMapView.ViewW / p.Width, FullMapView.ViewH / p.Height);
            float k = fit * zoom;
            Vector2 px = p.ToPixel(x, z);
            return new Vector2(k * (px.x - centre.x), -k * (px.y - centre.y));
        }

        [Test]
        public void FullMapMarkersSitOnTheMinimapProjection()
        {
            World w = NewWorld();
            Run(w, 90);
            RectTransform root = NewRoot(out GameObject go);
            try
            {
                var view = new FullMapView();
                view.Build(root, 2);
                view.Show(w, HudBinder.LocalActor, null);
                Assert.IsNotNull(view.Texture, "the baked map texture loads on Show");
                MinimapProjection p = MinimapGeometry.Projection;
                var centres = new[] { new Vector2(p.Width * 0.5f, p.Height * 0.5f), new Vector2(p.Width * 0.3f, p.Height * 0.6f) };
                var zooms = new[] { 1f, 2.5f };
                for (int v = 0; v < 2; v++)
                {
                    view.SetView(zooms[v], centres[v]);
                    view.Refresh(w, HudBinder.LocalActor);
                    view.Reposition();
                    Vector2 c = view.Centre;   // after clamping
                    Assert.AreEqual(zooms[v], view.Zoom, 1e-4f);
                    int checkedUnits = 0;
                    for (int i = 1; i < w.ActorCount; i++)
                    {
                        if (!view.ActorShown(i) || view.ActorRespawning(i)) continue;
                        Vector2 want = Expected(w.X[i], w.Z[i], zooms[v], c);
                        Assert.Less(Vector2.Distance(view.ActorView(i), want), 1f, "unit " + i + " at zoom " + zooms[v]);
                        checkedUnits++;
                    }
                    Assert.Greater(checkedUnits, 10, "teammates drawn");
                    Assert.Less(Vector2.Distance(view.HillView, Expected(w.Map.Hill.X, w.Map.Hill.Z, zooms[v], c)), 1f, "hill");
                    for (int t = 0; t < 2; t++)
                        Assert.Less(Vector2.Distance(view.BaseView(t), Expected(w.Map.Pads[t].X, w.Map.Pads[t].Z, zooms[v], c)), 1f, "base " + t);
                    Assert.IsTrue(view.YouShown);
                    Assert.Less(Vector2.Distance(view.YouView, Expected(w.X[0], w.Z[0], zooms[v], c)), 1f, "you");

                    // the inverse: a viewport point back to the sim lands on the same ground point
                    view.ViewToSim(view.SimToView(w.Map.Hill.X, w.Map.Hill.Z), out double hx, out double hz);
                    Assert.AreEqual(w.Map.Hill.X, hx, 1e-3);
                    Assert.AreEqual(w.Map.Hill.Z, hz, 1e-3);
                }
                // teammates carry names, the local player is not in the unit pool
                Assert.IsFalse(view.ActorShown(HudBinder.LocalActor));
                for (int i = 1; i < w.ActorCount; i++)
                    if (view.ActorShown(i) && w.Actors[i].Team == w.Actors[0].Team) Assert.IsNotEmpty(view.ActorName(i), "name " + i);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ZoomKeepsThePointUnderTheCursorAndClampsTheView()
        {
            RectTransform root = NewRoot(out GameObject go);
            try
            {
                var view = new FullMapView();
                view.Build(root, 2);
                Vector2 cursor = new Vector2(120f, -80f);
                Vector2 before = view.ViewToMapPx(cursor);
                view.ZoomAt(2f, cursor);
                Assert.AreEqual(2f, view.Zoom, 1e-4f);
                Assert.Less(Vector2.Distance(view.ViewToMapPx(cursor), before), 0.5f, "the map point under the cursor stays put");
                view.ZoomAt(100f, cursor);
                Assert.AreEqual(FullMapView.MaxZoom, view.Zoom, 1e-4f);
                view.ZoomAt(0.001f, cursor);
                Assert.AreEqual(FullMapView.MinZoom, view.Zoom, 1e-4f);
                view.SetView(3f, new Vector2(-500f, -500f));
                MinimapProjection p = MinimapGeometry.Projection;
                // the map's top-left corner may not come inside the viewport's top-left corner
                Vector2 tl = view.MapPxToView(Vector2.zero);
                Assert.LessOrEqual(tl.x, -FullMapView.ViewW * 0.5f + 0.01f);
                Assert.GreaterOrEqual(tl.y, FullMapView.ViewH * 0.5f - 0.01f);
                Assert.Greater(HudInteract.ZoomFactor(120f), 1f);
                Assert.Less(HudInteract.ZoomFactor(-120f), 1f);
                Assert.AreEqual(p.Width, MinimapGeometry.Projection.Width);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void FullMapHidesFarEnemiesLikeTheMinimap()
        {
            World w = NewWorld();
            TeamId mine = w.Actors[0].Team;
            var enemies = new List<int>();
            for (int i = 0; i < w.ActorCount; i++)
            {
                w.Actors[i].Alive = true;
                w.Actors[i].Hp = w.Actors[i].MaxHp;
                if (w.Actors[i].Team == mine) { w.X[i] = 20 + (i % 4); w.Z[i] = 30 + (i % 3); }
                else enemies.Add(i);
            }
            Assert.GreaterOrEqual(enemies.Count, 2);
            int near = enemies[0];
            for (int k = 0; k < enemies.Count; k++)
            {
                int e = enemies[k];
                // far: more than SightRadius from every teammate; near: within 5 units of the cluster
                w.X[e] = e == near ? 24 : 20 + GameHud.SightRadius + 30 + k;
                w.Z[e] = e == near ? 33 : 70;
            }
            int far = enemies[1];
            RectTransform root = NewRoot(out GameObject go);
            var hudGo = new GameObject("HUD visibility test");
            try
            {
                var view = new FullMapView();
                view.Build(root, 2);
                view.Show(w, 0, null);
                Assert.IsTrue(view.ActorShown(near), "an enemy near a teammate shows");
                Assert.IsFalse(view.ActorShown(far), "an enemy far from every teammate is hidden on the full map");
                Assert.IsFalse(MapRules.Visible(w, far, 0));
                Assert.IsTrue(MapRules.Visible(w, near, 0));

                // the same answer as the HUD minimap for every unit
                GameHud hud = hudGo.AddComponent<GameHud>();
                hud.Build();
                hud.Refresh(w, 0.016f);
                for (int i = 1; i < w.ActorCount; i++)
                {
                    Transform dot = hud.MapArea.Find("Dot " + i);
                    Assert.IsNotNull(dot, "minimap dot " + i);
                    Assert.AreEqual(dot.gameObject.activeSelf, view.ActorShown(i), "unit " + i + ": full map and minimap disagree");
                }

                // a knocked-out ally waits at its base with a timer; a knocked-out enemy is not shown
                int ally = 1;
                while (w.Actors[ally].Team != mine) ally++;
                w.Actors[ally].Alive = false;
                w.Actors[ally].RespawnIn = 3.2;
                w.Actors[near].Alive = false;
                view.Refresh(w, 0);
                view.Reposition();
                Assert.IsTrue(view.ActorRespawning(ally), "respawning ally at base");
                PadDefAt(w, mine, out double px, out double pz);
                Assert.Less(Vector2.Distance(view.ActorView(ally), view.SimToView(px, pz)), 70f, "ally fanned around its base banner");
                Assert.IsFalse(view.ActorShown(near), "dead enemy hidden");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(hudGo);
            }
        }

        static void PadDefAt(World w, TeamId t, out double x, out double z)
        {
            x = w.Map.Pads[(int)t].X;
            z = w.Map.Pads[(int)t].Z;
        }

        [Test]
        public void FullMapRefreshAndHoverAllocateNothingAfterWarmUp()
        {
            World w = NewWorld();
            Run(w, 300);
            RectTransform root = NewRoot(out GameObject go);
            try
            {
                var view = new FullMapView();
                view.Build(root, 2);
                view.Show(w, 0, null);
                for (int i = 0; i < 3; i++) { view.Refresh(w, 0); view.Reposition(); view.Hover(view.HillView, true); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 60; i++)
                {
                    view.Refresh(w, 0);
                    view.PanBy(new Vector2(1f, 0f));
                    view.Reposition();
                    view.Hover(i % 2 == 0 ? view.HillView : view.YouView, true);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0, bytes, "full map refresh + hover allocated " + bytes + " bytes over 60 frames");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void PingRateLimitIsThreePerFiveSecondsAndPingsFadeAfterSix()
        {
            var b = new PingBook();
            Assert.IsTrue(b.TryPlace(PingKind.Attack, 1, 1, 0.0, 0, 0, out _));
            Assert.IsTrue(b.TryPlace(PingKind.Defend, 1, 1, 1.0, 0, 0, out _));
            Assert.IsTrue(b.TryPlace(PingKind.Help, 1, 1, 2.0, 0, 0, out _));
            Assert.IsFalse(b.TryPlace(PingKind.OnMyWay, 1, 1, 3.0, 0, 0, out _), "4th inside 5 s refused");
            Assert.IsFalse(b.TryPlace(PingKind.OnMyWay, 1, 1, 4.99, 0, 0, out _));
            Assert.AreEqual(0.01, b.WaitSeconds(4.99), 1e-6);
            Assert.IsTrue(b.TryPlace(PingKind.OnMyWay, 1, 1, 5.0, 0, 0, out _), "allowed once the oldest is 5 s old");
            Assert.IsFalse(b.TryPlace(PingKind.Attack, 1, 1, 5.5, 0, 0, out _), "the window slides");
            Assert.IsTrue(b.TryPlace(PingKind.Attack, 1, 1, 6.0, 0, 0, out _));

            var c = new PingBook();
            Assert.IsTrue(c.TryPlace(PingKind.Attack, 3, 4, 10.0, 0, 2, out int slot));
            CriticalMass.Runtime.Hud.Interact.Ping p = c.Get(slot);
            Assert.AreEqual(1f, PingBook.Alpha(p, 10.0), 1e-6);
            Assert.AreEqual(1f, PingBook.Alpha(p, 15.0), 1e-6);
            Assert.Less(PingBook.Alpha(p, 15.8), 1f);
            Assert.Greater(PingBook.Alpha(p, 15.8), 0f);
            Assert.AreEqual(0f, PingBook.Alpha(p, 16.0), 1e-6);
            c.Expire(16.0);
            Assert.AreEqual(0, c.ActiveCount, "gone after 6 s");
            for (int i = 0; i < 20; i++) c.TryPlace(PingKind.Help, 0, 0, 100 + i * 2.0, 0, 0, out _);
            Assert.LessOrEqual(c.ActiveCount, PingBook.Capacity, "hard cap");
        }

        [Test]
        public void PingWheelSectorsFeedLinesAndLandmarks()
        {
            Assert.AreEqual(-1, PingWheel.Sector(new Vector2(5f, 5f), PingWheel.DeadZone));
            Assert.AreEqual((int)PingKind.Attack, PingWheel.Sector(new Vector2(0f, 80f), PingWheel.DeadZone));
            Assert.AreEqual((int)PingKind.OnMyWay, PingWheel.Sector(new Vector2(80f, 10f), PingWheel.DeadZone));
            Assert.AreEqual((int)PingKind.Defend, PingWheel.Sector(new Vector2(-10f, -80f), PingWheel.DeadZone));
            Assert.AreEqual((int)PingKind.Help, PingWheel.Sector(new Vector2(-80f, 0f), PingWheel.DeadZone));
            World w = NewWorld();
            Assert.AreEqual(PingWords.Hill, PingWords.NearestPlace(w, w.Map.Hill.X + 1, w.Map.Hill.Z));
            Assert.AreEqual(PingWords.AzureBase, PingWords.NearestPlace(w, w.Map.Pads[0].X, w.Map.Pads[0].Z));
            Assert.AreEqual("You: Attack here, the hill", PingWords.FeedLine[(int)PingKind.Attack, PingWords.Hill]);
            Assert.AreEqual(PingKind.Defend, PingWords.ForHill(HillState.Azure, TeamId.Azure));
            Assert.AreEqual(PingKind.Attack, PingWords.ForHill(HillState.Crimson, TeamId.Azure));

            // the shared book: a new match (epoch) clears it, and a 4th quick ping is refused
            Pings.Track(w);
            Pings.Book.Clear();
            double refused = -1;
            Action<double> onRefused = s => refused = s;
            Pings.Refused += onRefused;
            try
            {
                // Integration batch 4: with the depth rules on (#59/#61) the local player's ping is a sim input (the sim
                // rate-limits it); the book's own limit still applies to any other owner, and to every ping with the rules off.
                if (CriticalMass.Runtime.View.Mechanics.DepthRead.PingsOn(w))
                {
                    Assert.IsTrue(Pings.Place(w, PingKind.Attack, w.Map.Hill.X, w.Map.Hill.Z, 0));
                    Assert.IsTrue(CriticalMass.Runtime.View.Mechanics.DepthInputLatch.PingPending, "the local ping goes to the sim");
                    CriticalMass.Runtime.View.Mechanics.DepthInputLatch.Take(out _, out _, out _, out _);
                }
                const int owner = 3;
                Assert.IsTrue(Pings.Place(w, PingKind.Attack, w.Map.Hill.X, w.Map.Hill.Z, owner));
                Assert.IsTrue(Pings.Place(w, PingKind.Defend, w.Map.Hill.X, w.Map.Hill.Z, owner));
                Assert.IsTrue(Pings.Place(w, PingKind.Help, w.Map.Hill.X, w.Map.Hill.Z, owner));
                Assert.IsFalse(Pings.Place(w, PingKind.Help, w.Map.Hill.X, w.Map.Hill.Z, owner));
                Assert.Greater(refused, 0.0, "the refusal reports the wait");
            }
            finally
            {
                Pings.Refused -= onRefused;
                Pings.Book.Clear();
            }
        }

        [Test]
        public void EveryHudElementInTheTableHasATooltipAndMainOnesResolve()
        {
            var ids = new HashSet<string>();
            foreach (HotspotDef d in HotspotTable.All)
            {
                Assert.IsTrue(ids.Add(d.Id), "duplicate id " + d.Id);
                Assert.IsNotEmpty(d.Name, d.Id + " name");
                Assert.IsNotEmpty(d.Meaning, d.Id + " meaning");
                Assert.IsNotEmpty(d.ClickText, d.Id + " click");
                Assert.IsNotEmpty(d.Paths, d.Id + " paths");
                Assert.IsNotEmpty(d.Source, d.Id + " source");
            }
            var go = new GameObject("HUD hotspot test", typeof(RectTransform));
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                foreach (HotspotDef d in HotspotTable.All)
                {
                    if (d.Source != "main") continue;
                    foreach (string path in d.Paths) Assert.IsNotNull(hud.Root.Find(path), d.Id + ": " + path + " is not on the HUD");
                }
                var spots = new HudHotspots();
                var canvasGo = new GameObject("Interact test canvas", typeof(RectTransform));
                canvasGo.transform.SetParent(go.transform, false);
                canvasGo.AddComponent<Canvas>();
                spots.Build((RectTransform)canvasGo.transform, hud, go.transform);
                for (int i = 0; i < spots.Count; i++)
                    if (spots.Def(i).Source == "main") Assert.IsTrue(spots.Found(i), spots.Def(i).Id + " found");
                spots.RefreshKeys(null, ControlDevice.KeyboardMouse);
                for (int i = 0; i < spots.Count; i++)
                    if (spots.Def(i).KeyAction != null) Assert.IsNotEmpty(spots.KeyText(i), spots.Def(i).Id + " key");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void HudAndMapInputNeverReachTheSimsAttack()
        {
            foreach (GateReason r in new[] { GateReason.Modal, GateReason.PointerOverHud, GateReason.CursorFree, GateReason.PingWheel, GateReason.PadHudFocus })
            {
                Assert.IsTrue(InputGate.Blocks(r, "Attack"), r + " must hold Attack back");
                Assert.IsTrue(InputGate.Blocks(r, "Ability"), r + " must hold Ability back");
            }
            Assert.IsTrue(InputGate.Blocks(GateReason.Modal, "SwitchDigPlace"), "the wheel zooms the map, not the dig mode");
            Assert.IsTrue(InputGate.Blocks(GateReason.PadOwnsSticks, "Aim"));
            Assert.IsTrue(InputGate.Blocks(GateReason.PadHudFocus, "Jump"), "A clicks the HUD in controller focus");
            Assert.IsFalse(InputGate.Blocks(GateReason.Modal, "Move"), "you can still walk with the map open (it is a match)");
            Assert.AreEqual(0, InputGate.MaskFor(GateReason.None));

            var map = new InputActionMap("Player");
            foreach (string a in InputGate.Actions) map.AddAction(a, InputActionType.Button, "<Keyboard>/space");
            map.Enable();
            try
            {
                var gate = new InputGate();
                gate.Bind(map);
                InputAction attack = map.FindAction("Attack"), jump = map.FindAction("Jump"), move = map.FindAction("Move");
                gate.Apply(GateReason.Modal);
                Assert.IsFalse(attack.enabled, "map open: attack disabled, the press never starts");
                Assert.IsTrue(move.enabled);
                gate.Apply(GateReason.None);
                Assert.IsTrue(attack.enabled, "map closed: attack back");
                gate.Apply(GateReason.PointerOverHud);
                Assert.IsFalse(attack.enabled, "cursor over a HUD control: attack disabled");
                // an action someone else disabled stays disabled
                jump.Disable();
                gate.Apply(GateReason.None);
                Assert.IsTrue(attack.enabled);
                Assert.IsFalse(jump.enabled, "the gate only re-enables what it disabled");
                gate.Apply(GateReason.CursorFree);
                gate.Release();
                Assert.IsTrue(attack.enabled, "release gives everything back");
            }
            finally
            {
                map.Disable();
                map.Dispose();
            }
        }

        [Test]
        public void TheNewActionsAreInTheAssetWithoutConflicts()
        {
            var src = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Game/Settings/Controls.inputactions");
            InputActionAsset asset = InputActionAsset.FromJson(src.ToJson());
            try
            {
                InputActionMap player = asset.FindActionMap("Player", true);
                foreach (string name in new[] { "Map", "Ping", "HudCursor", "MinimapZoom" })
                {
                    InputAction a = player.FindAction(name, false);
                    Assert.IsNotNull(a, name);
                    foreach (ControlDevice d in new[] { ControlDevice.KeyboardMouse, ControlDevice.Gamepad })
                    {
                        int i = ControlBindings.FindBindingIndex(a, new RebindSlot(name, d));
                        Assert.GreaterOrEqual(i, 0, name + " " + d);
                        CollectionAssert.IsEmpty(ControlBindings.Conflicts(a, i), name + " " + d + " conflicts");
                    }
                }
                Assert.AreEqual("M", ControlBindings.PromptLabel(player.FindAction("Map"), ControlDevice.KeyboardMouse));
                Assert.AreEqual("D-PAD LEFT", ControlBindings.PromptLabel(player.FindAction("Ping"), ControlDevice.Gamepad));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
    }
}
