using System.IO;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Menus.Story;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// first-minutes-1 (playthrough walk 1): what a first-time player hits in the first minutes. The YOU marker and team
    /// banner (#1), keys on the ability slots (#2), one wood number (#6) and the SPACE SKIP hint off the version stamp (#10).
    /// </summary>
    public sealed class FirstMinutesTests
    {
        static World NewWorld(ClassId cls) => World.CreateWorld(new WorldOptions
        {
            Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = cls, Config = GameConfig.CreateDefault(),
        });

        static string Source(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Runtime", rel));

        // ---------------------------------------------------------------- #1 YOU marker and team banner

        [Test]
        public void TheYouMarkerLingersThenShrinksAway()
        {
            Assert.AreEqual(1f, FirstMinutes.MarkerScale(0f));
            Assert.AreEqual(1f, FirstMinutes.MarkerScale(FirstMinutes.LingerSeconds));
            Assert.AreEqual(0.5f, FirstMinutes.MarkerScale(FirstMinutes.LingerSeconds + FirstMinutes.FadeSeconds * 0.5f), 1e-4f);
            Assert.AreEqual(0f, FirstMinutes.MarkerScale(FirstMinutes.LingerSeconds + FirstMinutes.FadeSeconds));
            Assert.AreEqual(0f, FirstMinutes.MarkerScale(60f));
            Assert.AreEqual(0f, FirstMinutes.MarkerScale(-1f));
            Assert.GreaterOrEqual(FirstMinutes.LingerSeconds, 3f, "a few seconds, long enough to find yourself");
        }

        [Test]
        public void TheMarkerComesBackAfterARespawnAndANewMatch()
        {
            Assert.IsTrue(FirstMinutes.Respawned(false, true));
            Assert.IsFalse(FirstMinutes.Respawned(true, true));
            Assert.IsFalse(FirstMinutes.Respawned(true, false));
            Assert.IsFalse(FirstMinutes.Respawned(false, false));
            Assert.IsTrue(FirstMinutes.NewMatch(300.0, 0.0));
            Assert.IsFalse(FirstMinutes.NewMatch(10.0, 10.1));
            Assert.IsFalse(FirstMinutes.NewMatch(double.MaxValue - 1, double.MaxValue - 1));
        }

        [Test]
        public void TheStartBannerNamesYourTeamAndTheGoal()
        {
            BannerSpec a = FirstMinutes.StartBanner(TeamId.Azure);
            Assert.AreEqual("YOU ARE AZURE", a.Headline);
            Assert.AreEqual(BannerStyle.Azure, a.Style);
            StringAssert.Contains("hill", a.Reason);
            StringAssert.Contains("CRIMSON", a.Reason);
            Assert.AreEqual(UiIcons.Banner[0], a.Icon);
            BannerSpec c = FirstMinutes.StartBanner(TeamId.Crimson);
            Assert.AreEqual("YOU ARE CRIMSON", c.Headline);
            Assert.AreEqual(BannerStyle.Crimson, c.Style);
            StringAssert.Contains("AZURE", c.Reason);
            Assert.LessOrEqual(a.Reason.Length, 48, "one short line");
        }

        [Test]
        public void TheMarkerMeshesStayTiny()
        {
            Mesh ring = FirstMinutes.RingMesh(), arrow = FirstMinutes.ArrowMesh();
            Assert.AreEqual(FirstMinutes.RingSegments * 2, ring.triangles.Length / 3);
            Assert.AreEqual(6, arrow.triangles.Length / 3);
            Assert.LessOrEqual(ring.triangles.Length / 3 + arrow.triangles.Length / 3, 100);
            Assert.Greater(ring.bounds.extents.x, 1f, "wider than a fighter's footprint");
            Object.DestroyImmediate(ring);
            Object.DestroyImmediate(arrow);
        }

        [Test]
        public void TheMarkerInstallsOnlyInMatches()
        {
            var at = (Flow.GameOverlayAttribute)System.Attribute.GetCustomAttribute(typeof(YouMarker), typeof(Flow.GameOverlayAttribute));
            Assert.IsNotNull(at);
            Assert.AreEqual(Flow.OverlayScope.MatchOnly, at.Scope);
        }

        // ---------------------------------------------------------------- #2 keys on the ability slots

        [Test]
        public void EverySlotHasAKeyForKeyboardAndGamepad()
        {
            CollectionAssert.AreEqual(new[] { "Attack", "Ability", "Interact" }, HudPrompts.Actions);
            StringAssert.Contains("\"Key Label \" + i", Source("Hud/GameHud.cs"), "the three key plates under the slots");
            if (InputSystem.actions == null) Assert.Ignore("no project-wide actions in this editor");
            foreach (ControlDevice d in new[] { ControlDevice.KeyboardMouse, ControlDevice.Gamepad })
                foreach (string action in HudPrompts.Actions)
                {
                    GlyphSpec g = InputGlyphs.ForAction(action, d);
                    Assert.IsFalse(string.IsNullOrEmpty(g.Words), $"{action} on {d} has no key");
                    Assert.AreNotEqual("-", g.Words, $"{action} on {d} has no binding");
                }
        }

        // ---------------------------------------------------------------- #6 one wood number

        [Test]
        public void EveryWoodDisplayReadsTheOneTeamWoodNumber()
        {
            Assert.AreEqual("WOOD 2", MechanicsHud.WoodNote(2));
            Assert.AreEqual("TEAM WOOD 2  LADDER 1, BRIDGE 2", MechanicsHud.TeamWoodLine(2));
            StringAssert.Contains("BuilderBank.Whole(w, t)", Source("Hud/Mechanics/DepthHud.cs"), "top strip WOOD chip");
            StringAssert.Contains("BuilderBank.Whole(w, a.Team)", Source("Hud/Mechanics/MechanicsHud.cs"), "kit footer");
            StringAssert.Contains("BuilderBank.WoodWord", Source("Hud/TerrainPrompt.cs"), "bottom build prompt");
            foreach (string f in new[] { "Hud/Mechanics/MechanicsHud.cs", "Hud/Mechanics/DepthHud.cs", "Hud/TerrainPrompt.cs" })
            {
                string s = Source(f);
                StringAssert.DoesNotContain("\"TEAM BANK ", s, f);
                StringAssert.DoesNotContain("\"BANK \" +", s, f);
            }

            World w = NewWorld(ClassId.Builder);
            int before = BuilderBank.Whole(w, TeamId.Azure);
            Assert.AreEqual((int)System.Math.Floor(System.Math.Max(0, BuiltPieces.Bank(w, TeamId.Azure))), before);
        }

        [Test]
        public void TheBuildPromptNoLongerCarriesASecondWoodCount()
        {
            string s = Source("View/Affordance/AffordanceCore.cs");
            StringAssert.DoesNotContain("p.Count = (int)Math.Floor(Math.Max(0, BuiltPieces.Bank(", s);
        }

        // ---------------------------------------------------------------- #10 SPACE SKIP off the version stamp

        [Test]
        public void TheSkipHintClearsTheVersionStamp()
        {
            Assert.GreaterOrEqual(IntroFilm.SkipBottomPx, PerfOverlayGui.VersionTopPx + 4f,
                "the SPACE SKIP hint's bottom edge sits above the whole version stamp line (both bottom right, 1080 px reference)");
            StringAssert.Contains("IntroFilm.SkipBottomPx", Source("Menus/Story/IntroFilm.cs"));
            StringAssert.Contains("VersionBottomPx * s", Source("Hud/PerfOverlay.cs"));
        }
    }
}
