using System;
using System.Collections;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// Owner walk 1: the live match HUD at the five screen shapes (with the MacBook camera notch), with the optional pieces
    /// forced on (five kill-feed lines, an event banner, the pickup coins): no two visible pieces intersect, none leaves the
    /// safe area, and no HUD text is cut.
    /// </summary>
    public sealed class OwnerWalkHudPlayTests
    {
        const string BootScene = "Assets/_Game/Scenes/Boot.unity";
        RenderTexture _rt;

        static readonly Vector2Int[] Shapes =
        {
            new Vector2Int(3024, 1964), new Vector2Int(1512, 982), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080), new Vector2Int(1280, 800),
        };

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = true;
            GameFlow.Autoplay = false;
            SimDriverBehaviour.CaptureMode = false;
            HumanControlGuard.IdleAutopilotEnabled = false;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            HudSafeArea.ClearOverride();
            if (UnityEngine.Camera.main != null) { UnityEngine.Camera.main.targetTexture = null; UnityEngine.Camera.main.ResetAspect(); }
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            GameFlow.ResetForTests();
            GameFlow.ForceEnabled = false;
            LoadingScreen.UseInFlow = true;
            HumanControlGuard.IdleAutopilotEnabled = true;
        }

        static SimDriverBehaviour Driver() => UnityEngine.Object.FindAnyObjectByType<SimDriverBehaviour>();

        static IEnumerator Until(Func<bool> c, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!c())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) Assert.Fail(what + " (waited " + timeout + " s)");
                yield return null;
            }
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnityTest, Timeout(180000)]
        public IEnumerator TheMatchHudNeverCollidesAtAnyScreenShape()
        {
            SceneManager.LoadScene(BootScene);
            yield return Until(() => Driver() != null && Driver().World != null && FlowDirector.Current != null && !FlowDirector.Current.Loading &&
                                     UnityEngine.Object.FindAnyObjectByType<TitleScreen>() != null, 30f, "boot");
            StoryIntro.Skip();
            UnityEngine.Object.FindAnyObjectByType<TitleScreen>().Hide();
            yield return Frames(3);
            FlowDirector d = FlowDirector.Current;
            LoadingScreen.UseInFlow = false;
            SimDriverBehaviour from = Driver();
            MatchSetupChoice quick = PlayModes.ToChoice(MatchMode.Quick, MatchSetupChoice.Default, 8);
            quick.Class = ClassId.Warrior;
            quick.Team = TeamId.Azure;
            d.ShowReady(new PlayLaunch { Choice = quick, Seed = 4242, BackTo = LobbyScreen.ModeSelect });
            yield return Until(() => Driver() != null && Driver() != from && Driver().World != null && GameFlow.State == FlowState.Match &&
                                     !d.Loading && !LoadingScreen.IsOpen, 60f, "READY launches the match");
            SimDriverBehaviour drv = Driver();
            yield return Until(() => drv.World.Match.Phase == MatchPhase.Playing && drv.World.Actors[0].Alive, 30f, "the match plays");
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 4f) yield return null;   // the intro line and the spawn crowd pass

            World w = drv.World;
            KillFeed kf = UnityEngine.Object.FindAnyObjectByType<KillFeed>();
            Assert.IsNotNull(kf);
            for (int i = 1, pushed = 0; i < w.ActorCount && pushed < 5; i++, pushed++)
            {
                int killer = (i + 3) % w.ActorCount;
                kf.Push(new CombatEvent
                {
                    Kind = CombatEventKind.Knockout, Actor = i, Other = killer, Blow = 1, ActorTeam = w.Actors[i].Team, OtherTeam = w.Actors[killer].Team,
                    ActorClass = w.Actors[i].Cls, OtherClass = w.Actors[killer].Cls, Time = w.Match.Time,
                }, 0, w.Kaiju.State);
            }
            Banner.Show(new BannerSpec { Headline = "SUPPLY DROP INCOMING", Reason = "A crate lands beside the hill.", Style = BannerStyle.Warning, HoldSeconds = 120, Priority = 3 });

            var bad = new List<string>();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Assert.IsNotNull(cam);
            foreach (Vector2Int s in Shapes)
            {
                if (_rt != null) { cam.targetTexture = null; _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                _rt = new RenderTexture(s.x, s.y, 24);
                cam.targetTexture = _rt;
                cam.aspect = (float)s.x / s.y;
                HudSafeArea.SetOverride(HudSafeArea.NotchFor(s.x, s.y));
                yield return Frames(6);
                HudSafeSpace.ApplyAll();
                Canvas.ForceUpdateCanvases();
                yield return Frames(2);
                string tag = s.x + "x" + s.y + ": ";
                List<HudAudit.Item> items = HudAudit.Collect();
                if (items.Count < 6) bad.Add(tag + "only " + items.Count + " HUD pieces visible");
                foreach (string o in HudAudit.Overlaps(2f)) bad.Add(tag + "overlap " + o);
                foreach (string o in HudAudit.OutsideSafeArea(s.x, s.y, 2f)) bad.Add(tag + "outside the safe area " + o);
                foreach (string o in HudAudit.TextIssues()) bad.Add(tag + "text " + o);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }
    }
}
