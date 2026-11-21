using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 view polish, Editor review items 2 and 8: the hut vote cards, the RALLY tag and the Kaiju-on-the-hill pill read at
    /// 1080p. Pixel-size asserts (type at the 1920 x 1080 canvas scale, and the floor a fitted text may shrink to) and
    /// overlap asserts for every rect in the top-centre lane. Every case PRINTS what, value, limit.
    /// </summary>
    public sealed class B15HudReadabilityTests
    {
        static Rect Match => HudLayout.RefRect(HudCluster.Match);
        /// <summary>GameHud's hill bar (cluster 236, 80, 328 x 26) in reference px.</summary>
        static Rect HillBar => new Rect(Match.x + 236f, 80f, 328f, 26f);
        /// <summary>The objective (job) line alone: HudZones.JobY, JobW x JobH.</summary>
        static Rect JobLine => new Rect((HudLayout.RefW - HudZones.JobW) * 0.5f, HudZones.JobY, HudZones.JobW, HudZones.JobH);

        static void AtLeast(string what, float px, float limit)
        {
            Debug.Log($"[b15 hud type] {what}: {px:0.0} px at 1080p (limit >= {limit:0})");
            Assert.GreaterOrEqual(px, limit, what);
        }

        [Test]
        public void VoteCardsAndTheTagMeetTheTypeSizesAt1080p()
        {
            Assert.AreEqual(300f, B15Hud.CardW);
            Assert.AreEqual(96f, B15Hud.CardH);
            AtLeast("vote head (countdown)", B15Hud.ScreenPx(B15Hud.HeadFont, 1920, 1080), 18);
            AtLeast("card name", B15Hud.ScreenPx(B15Hud.NameFont, 1920, 1080), 18);
            AtLeast("card line", B15Hud.ScreenPx(B15Hud.LineFont, 1920, 1080), 16);
            AtLeast("card line floor", B15Hud.ScreenPx(B15Hud.LineFloor, 1920, 1080), 15);
            AtLeast("card votes / LEADING", B15Hud.ScreenPx(B15Hud.VotesFont, 1920, 1080), 15);
            AtLeast("card key", B15Hud.ScreenPx(B15Hud.KeyFont, 1920, 1080), 15);
            AtLeast("RALLY tag floor", B15Hud.ScreenPx(B15Hud.RallyFloor, 1920, 1080), 16);
            AtLeast("Kaiju pill floor", B15Hud.ScreenPx(B15Hud.KaijuPillFloor, 1920, 1080), 16);
            // scales with the resolution: 1440p and 4K keep the 1080p proportions (never smaller in screen share)
            Assert.AreEqual(B15Hud.ScreenPx(B15Hud.LineFont, 1920, 1080) * 2f, B15Hud.ScreenPx(B15Hud.LineFont, 3840, 2160), 1e-3f);
            Assert.AreEqual(B15Hud.LineFont * 4f / 3f, B15Hud.ScreenPx(B15Hud.LineFont, 2560, 1440), 1e-3f);
        }

        [Test]
        public void TheTopCentreRectsNeverOverlap()
        {
            Rect vote = B15Hud.VoteRect, rally = B15Hud.RallyRect, pill = B15Hud.KaijuPillRect;
            var named = new (string, Rect)[]
            {
                ("RALLY", rally), ("hut vote", vote), ("Kaiju pill", pill), ("objective line", JobLine), ("hill bar", HillBar),
                ("event bar", new Rect((HudLayout.RefW - HudZones.EventW) * 0.5f, HudZones.EventY, HudZones.EventW, HudZones.EventH)),
                ("hut badge", B13Hud.HutRect), ("corruption meter", B13Hud.CorruptionRect), ("kill feed", HudZones.Feed),
                ("lower lane", HudZones.Lower), ("Kaiju cluster", HudLayout.RefRect(HudCluster.Kaiju)),
            };
            foreach (var (an, a) in new[] { ("RALLY", rally), ("hut vote", vote), ("Kaiju pill", pill) })
                foreach (var (bn, b) in named)
                {
                    if (an == bn) continue;
                    if (an == "Kaiju pill" && bn == "hill bar") { Assert.GreaterOrEqual(a.y, b.yMax, "the pill sits under the hill meter"); continue; }
                    bool hit = a.Overlaps(b);
                    Debug.Log($"[b15 hud rects] {an} {a} vs {bn} {b}: {(hit ? "OVERLAP" : "clear")} (limit clear)");
                    Assert.IsFalse(hit, an + " overlaps " + bn);
                }
            Assert.IsFalse(vote.Overlaps(Match), "the vote never sits on the score");
            Assert.LessOrEqual(pill.y - HillBar.yMax, 4f, "directly under the hill meter");
            Assert.AreEqual(B15Hud.VoteTop, vote.y, "the vote keeps its own anchor");
            for (int c = 0; c < 2; c++) Assert.IsTrue(vote.Contains(B15Hud.CardRect(c).min) && vote.Contains(B15Hud.CardRect(c).max - Vector2.one * 0.01f), "card " + c + " inside the vote");
            Assert.IsFalse(B15Hud.CardRect(0).Overlaps(B15Hud.CardRect(1)));
        }

        [Test]
        public void LeadingAndPulseFollowTheRules()
        {
            Assert.AreEqual(1, B15Hud.Leading(3, 1));
            Assert.AreEqual(2, B15Hud.Leading(0, 2));
            Assert.AreEqual(0, B15Hud.Leading(2, 2), "a tie leads nothing");
            Assert.AreEqual(1f, B15Hud.Pulse(-0.1f));
            Assert.AreEqual(1f, B15Hud.Pulse(B15Hud.PulseSeconds));
            Assert.Greater(B15Hud.Pulse(B15Hud.PulseSeconds * 0.5f), 1.05f, "a visible pulse when the state begins");
            Assert.AreEqual(0f, B15Hud.PulseFlash(0.2f, false), "reduced flashing: no brightness flash");
            Assert.Greater(B15Hud.PulseFlash(0.2f, true), 0f);
        }

        [Test]
        public void ThePillIsBuiltUnderTheHillMeterWithReadableType()
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
            var go = new GameObject("B15 pill test");
            System.Action<HudState> prev = GameHud.PreviewOverride;
            try
            {
                GameHud hud = go.AddComponent<GameHud>();
                hud.Build();
                hud.SnapAnimations = true;
                foreach (int blocking in new[] { 0, 1 })
                {
                    GameHud.PreviewOverride = s => { s.KaijuBlocksHill = blocking; s.HasPlayer = true; s.Team = TeamId.Azure; };
                    hud.Refresh(w, 0.016f);
                    Assert.IsTrue(hud.KaijuPill.gameObject.activeSelf, "pill shown while a Kaiju blocks the hill");
                    Text t = hud.KaijuPillText;
                    string want = blocking == 0 ? ComebackNames.KaijuHillOwnWord : ComebackNames.KaijuHillEnemyWord;
                    Assert.AreEqual(want, t.text);
                    Debug.Log($"[b15 hud pill] {want}: font {t.fontSize}, fit floor {t.resizeTextMinSize}, size {hud.KaijuPill.rect.size} (limit font >= 16, floor >= 16)");
                    Assert.GreaterOrEqual(t.fontSize, 16);
                    Assert.GreaterOrEqual(t.resizeTextMinSize, 16);
                    Assert.AreEqual(B15Hud.KaijuPillW, hud.KaijuPill.rect.width, 0.01f);
                }
                GameHud.PreviewOverride = s => { s.KaijuBlocksHill = -1; };
                hud.Refresh(w, 0.016f);
                Assert.IsFalse(hud.KaijuPill.gameObject.activeSelf, "pill hidden when no Kaiju blocks the hill");
            }
            finally
            {
                GameHud.PreviewOverride = prev;
                Object.DestroyImmediate(go);
            }
        }
    }
}
