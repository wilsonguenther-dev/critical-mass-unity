using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12 r3 (Air picture shot-grenade-02-midfuse of a07ee1d; the tester could not find her explosive): under the b12-jz
    /// rule the Demolitionist's ATTACK press throws TNT when ready and swings the pry bar while it reloads, but her HUD said
    /// PRY BAR on the attack slot, TNT on F in the kit, and "F: THROW" in both prompts. Every surface must name the attack
    /// binding (keyboard/mouse and pad). Each case prints one line: what was measured, the value, the limit.
    /// </summary>
    public sealed class DemolitionistAttackWordsTests
    {
        const int Me = 0;

        static World Field()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = GameConfig.CreateDefault() });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                if (w.Depth != null) w.Depth.WasAlive[j] = 0;
            }
            Actor a = w.Actors[Me];
            a.Alive = true;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            w.Tick = 100;
            w.Demolitions.TntReadyTick[Me] = 0;
            return w;
        }

        static void Print(string s) => Debug.Log("[DemolitionistWords] " + s);

        [Test]
        public void TheDefaultRulesThrowTntOnAttack()
        {
            GameConfig c = GameConfig.CreateDefault();
            Print("default rules: ThrowOnAttack " + c.Abilities.DemolitionsTnt.ThrowOnAttack + ", Classic " + c.Depth.Classic + ", AttackThrows " + GrenadeTuning.AttackThrows(c) + " (want True)");
            Assert.IsTrue(GrenadeTuning.AttackThrows(c));
            c.Depth.Classic = true;
            Assert.IsFalse(GrenadeTuning.AttackThrows(c), "Classic rules: Dig / place throws, as before");
        }

        [Test]
        public void ThePrimarySlotReadsTntWhenReadyAndPryBarWhileItReloads()
        {
            World w = Field();
            var s = new HudState();
            HudBinder.Read(w, Me, s);
            string ready = GameHud.AttackName((int)ClassId.Demolitions, false, s.DemoTntReady);
            Print("TNT ready: DemoTntOnAttack " + s.DemoTntOnAttack + ", ready " + s.DemoTntReady + ", slot '" + ready + "' (want 'TNT')");
            Assert.IsTrue(s.DemoTntOnAttack);
            Assert.IsTrue(s.DemoTntReady);
            Assert.AreEqual(GameHud.DemolitionsTntWord, ready);

            w.Demolitions.TntReadyTick[Me] = w.Tick + 90;
            HudBinder.Read(w, Me, s);
            string reloading = GameHud.AttackName((int)ClassId.Demolitions, false, s.DemoTntReady);
            Print("TNT reloading 90 ticks: ready " + s.DemoTntReady + ", slot '" + reloading + "', sweep " + s.DemoTntCd01.ToString("0.00") + ", seconds "
                  + s.DemoTntSeconds.ToString("0.0") + " (want 'PRY BAR', sweep > 0, 3.0 s)");
            Assert.IsFalse(s.DemoTntReady);
            Assert.AreEqual(LightWeapons.DemolitionsName, reloading);
            Assert.Greater(s.DemoTntCd01, 0f);
            Assert.AreEqual(90.0 / w.Config.SimHz, s.DemoTntSeconds, 1e-3);
            CollectionAssert.Contains(GameHud.AbilitySlotNames()[0], GameHud.DemolitionsTntWord, "the slot layout is measured with TNT too");
        }

        [Test]
        public void ThePromptsNameTheAttackBindingOnBothDevices()
        {
            foreach (ControlDevice d in new[] { ControlDevice.KeyboardMouse, ControlDevice.Gamepad })
            {
                string attack = TrialText.Fill("{Attack}", d), dig = TrialText.Fill("{DigPlace}", d);
                string readyLine = TerrainPrompt.DemolitionsLine(true, 0, d), waitLine = TerrainPrompt.DemolitionsLine(true, 3, d);
                Print(d + ": strip ready '" + readyLine + "', reloading '" + waitLine + "' (want the attack key '" + attack + "', not '" + dig + ": THROW')");
                StringAssert.Contains(attack + ": THROW", readyLine);
                StringAssert.Contains(attack + ": " + LightWeapons.DemolitionsName, waitLine);
                StringAssert.DoesNotContain(dig + ": THROW", readyLine);

                World w = Field();
                var scan = new AffordanceScan();
                scan.Scan(w, Me, false, double.NaN, double.NaN, true);
                string main = TerrainAffordanceView.Main(scan.Primary, d);
                Print(d + ": pill primary ready '" + main + "' button " + scan.Primary.Button + " (want it to start with '" + TerrainAffordanceView.Glyph("Attack", d) + "' and say THROW TNT)");
                Assert.AreEqual("Attack", scan.Primary.Button);
                StringAssert.StartsWith(TerrainAffordanceView.Glyph("Attack", d), main);
                StringAssert.Contains(AffWords.ThrowTnt, main);
                Assert.IsTrue(scan.Primary.Valid);

                w.Demolitions.TntReadyTick[Me] = w.Tick + 60;
                scan.Scan(w, Me, false, double.NaN, double.NaN, true);
                string mainWait = TerrainAffordanceView.Main(scan.Primary, d), why = TerrainAffordanceView.Detail(scan.Primary);
                Print(d + ": pill primary reloading '" + mainWait + "' / '" + why + "' (want PRY BAR and 'TNT in 2.0 s')");
                StringAssert.Contains(AffWords.PryBar, mainWait);
                StringAssert.StartsWith(AffWords.TntIn, why);
            }
        }

        [Test]
        public void TheKitRowShowsTntOnTheAttackBinding()
        {
            World w = Field();
            var row = new KitRow();
            Assert.IsTrue(ClassKit.Read(w, Me, 1, ref row, new[] { "BANK 0" }));
            Print("kit row 1: name " + row.Name + ", action " + row.Action + ", icon " + CriticalMass.Runtime.Hud.Mechanics.MechanicsHud.RowIcon(row.Action, (int)ClassId.Demolitions, 0) + " (want TNT on Attack)");
            Assert.AreEqual(DemolitionsNames.Tnt, row.Name);
            Assert.AreEqual("Attack", row.Action);
        }

        [Test]
        public void TheStripNeverTouchesThePillAt720pAnd1080p()
        {
            foreach (Vector2 px in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1080), new Vector2(1280, 1024) })
            {
                float s = HudLayout.Scale(px.x, px.y);
                Rect lane = HudZones.PromptLanePixels(px.x, px.y);
                float laneTopPx = px.y - lane.y, chipBottomPx = TerrainPrompt.ChipBottom(px.x, px.y, s, true) * s;
                Print(px.x + "x" + px.y + ": pill lane top " + laneTopPx.ToString("0.0") + " px from the bottom, strip bottom " + chipBottomPx.ToString("0.0")
                      + " px (limit: at least " + (laneTopPx + TerrainPrompt.PillGap * s * 0.99f).ToString("0.0") + ")");
                Assert.GreaterOrEqual(chipBottomPx, laneTopPx + TerrainPrompt.PillGap * s * 0.99f);
            }
        }
    }
}
