using System;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b13-v: the batch 13 HUD words and places (kit slots, hut badge, corruption meter, roll banner). Every case prints.</summary>
    public class B13HudTests
    {
        static void P(string s) => Debug.Log("[B13Hud] " + s);

        [Test]
        public void SlotLabelsPerClass()
        {
            foreach (ClassId c in ClassIds.All)
            {
                string[] names = B13Kit.SlotNames(c);
                P(c + " slots " + (names == null ? "today's kit (no batch 13 rows)" : "F " + names[0] + ", Space " + names[1]));
            }
            CollectionAssert.AreEqual(new[] { "MINE", "HAUL" }, B13Kit.SlotNames(ClassId.Miner));
            CollectionAssert.AreEqual(new[] { "BUILD", "NEXT TOOL" }, B13Kit.SlotNames(ClassId.Builder));   // b14: one tool wheel
            Assert.AreEqual("LADDER  2 WOOD", B13Kit.ToolNote(BuilderTool.Ladder, 0, 2));
            Assert.AreEqual("TALL WALL x5  2 WOOD", B13Kit.ToolNote(BuilderTool.TallWall, 2, 2));
            Assert.AreEqual("DIG", B13Kit.ToolNote(BuilderTool.Dig, 0, 0));
            Assert.AreSame(B13Kit.ToolNote(BuilderTool.Block, 1, 1), B13Kit.ToolNote(BuilderTool.Block, 1, 1), "cached: no allocation per frame");
            Assert.IsNull(B13Kit.SlotNames(ClassId.Warrior));
            for (int n = 0; n <= 3; n++) { P("ore note " + n + ": " + B13Kit.OreNote(n)); Assert.AreEqual("ORE " + n + "/3", B13Kit.OreNote(n)); }
            Assert.AreEqual("ORE 3/3", B13Kit.OreNote(7), "clamped");
            string[] want = { "SHORT WALL", "TALL WALL", "TALL WALL x5", "BLOCK" };
            string[] got =
            {
                B13Kit.PieceWord(BuildPiece.Wall, false, 0), B13Kit.PieceWord(BuildPiece.Wall, true, 1),
                B13Kit.PieceWord(BuildPiece.Wall, true, 2), B13Kit.PieceWord(BuildPiece.Block, true, 2),
            };
            for (int i = 0; i < want.Length; i++) P("piece word " + i + ": " + got[i] + " (want " + want[i] + ")");
            CollectionAssert.AreEqual(want, got);
            P("dodge ring ready=" + B13Kit.DodgeFill(0, 0) + " half=" + B13Kit.DodgeFill(45, 90) + " just used=" + B13Kit.DodgeFill(90, 90));
            Assert.AreEqual(1f, B13Kit.DodgeFill(0, 90));
            Assert.AreEqual(0.5f, B13Kit.DodgeFill(45, 90), 1e-4f);
            Assert.AreEqual(0f, B13Kit.DodgeFill(90, 90), 1e-4f);
            P("builder wood line: " + B13Kit.BankLine("TEAM WOOD 4", "WHEEL"));
            P("dodge label: " + B13Kit.DodgeLabel("SHIFT"));
        }

        [Test]
        public void HutBadgeAtLevelsZeroThreeFive()
        {
            int[] levels = { 0, 3, 5 };
            string[] want = { "HUT 0 +0%", "HUT 3 +15%", "HUT 5 +25%" };
            for (int i = 0; i < levels.Length; i++)
            {
                P("hut level " + levels[i] + ": " + B13Hud.HutLine(levels[i]));
                Assert.AreEqual(want[i], B13Hud.HutLine(levels[i]));
            }
            P("ore line 4 of 18: " + B13Hud.OreLine(4, 18) + "; at max: " + B13Hud.OreLine(0, 0));
            Assert.AreEqual("ore 4/18", B13Hud.OreLine(4, 18));
            Assert.AreEqual("max level", B13Hud.OreLine(0, 0));
            P("hut badge rect " + B13Hud.HutRect + ", corruption meter rect " + B13Hud.CorruptionRect);
        }

        [Test]
        public void CorruptionMeterHiddenAtZeroAndItsWordsAtFive()
        {
            P("pool 0 shown=" + B13Hud.MeterShown(true, 0) + "; rule off, pool 5 shown=" + B13Hud.MeterShown(false, 5) + "; pool 5 shown=" + B13Hud.MeterShown(true, 5));
            Assert.IsFalse(B13Hud.MeterShown(true, 0));
            Assert.IsFalse(B13Hud.MeterShown(false, 5));
            Assert.IsTrue(B13Hud.MeterShown(true, 5));
            string chance = B13Hud.ChanceLine(0.5), pool = B13Hud.PoolLine(5), hatch = B13Hud.RollLine(42, false), wake = B13Hud.RollLine(18, true);
            P("pool 5 chance 0.5: \"" + chance + "\" \"" + pool + "\" \"" + hatch + "\" / \"" + wake + "\"");
            Assert.AreEqual("CORRUPTION 50%", chance);
            Assert.AreEqual("5 crystals", pool);
            Assert.AreEqual("1 crystal", B13Hud.PoolLine(1));
            Assert.AreEqual("6.5 crystals", B13Hud.PoolLine(6.5));
            Assert.AreEqual("roll at hatch 0:42", hatch);
            Assert.AreEqual("roll when it wakes 0:18", wake);
            Assert.AreEqual(string.Empty, B13Hud.RollLine(-1, false));
            Assert.AreEqual(2, B13Hud.SecondsUntil(1060, 1000, 30));
            P("chance bar at 0.5: " + B13Hud.ChanceBar01(0.5) + ", at the 0.70 cap: " + B13Hud.ChanceBar01(0.7));
            Assert.AreEqual(1f, B13Hud.ChanceBar01(0.7), 1e-4f);
        }

        [Test]
        public void TheRollBannerSaysBothResults()
        {
            BannerSpec wild = Banner.CorruptionRollSpec(true), resisted = Banner.CorruptionRollSpec(false);
            P("wild: \"" + wild.Headline + "\" / \"" + wild.Reason + "\" style " + wild.Style);
            P("resisted: \"" + resisted.Headline + "\" / \"" + resisted.Reason + "\" style " + resisted.Style);
            Assert.AreEqual("THE KAIJU GOES WILD", wild.Headline);
            Assert.AreEqual("THE KAIJU RESISTED", resisted.Headline);
            Assert.AreEqual(BannerStyle.Corruption, wild.Style);
        }

        [Test]
        public void TheBadgeAndMeterNeverOverlapAnotherHudPiece()
        {
            Rect[] mine = { B13Hud.HutRect, B13Hud.CorruptionRect };
            foreach (HudZones.Piece p in HudZones.Pieces())
                foreach (Rect r in mine)
                {
                    bool o = r.Overlaps(p.Rect);
                    P((r == B13Hud.HutRect ? "hut badge" : "corruption meter") + " vs " + p.Name + " " + p.Rect + ": " + (o ? "OVERLAP" : "clear"));
                    Assert.IsFalse(o, p.Name);
                }
        }

        [Test]
        public void NoAllocationWhileValuesStayTheSameFor300Frames()
        {
            var cache = new B13HudCache();
            cache.Ore(4, 18); cache.Chance(0.5); cache.Pool(5); cache.Roll(42, false);
            B13Hud.HutLine(3); B13Kit.OreNote(2); B13Kit.DodgeFill(45, 90);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int len = 0;
            for (int f = 0; f < 300; f++)
            {
                len += cache.Ore(4, 18).Length + cache.Chance(0.5).Length + cache.Pool(5).Length + cache.Roll(42, false).Length;
                len += B13Hud.HutLine(3).Length + B13Kit.OreNote(2).Length;
                len += (int)B13Kit.DodgeFill(45, 90) + (B13Hud.MeterShown(true, 5) ? 1 : 0);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            P("300 frames unchanged: " + bytes + " B allocated (limit 0), checksum " + len);
            Assert.AreEqual(0, bytes);
            string a = cache.Ore(5, 18);
            P("a change rebuilds once: \"" + a + "\"");
            Assert.AreEqual("ore 5/18", a);
        }
    }
}
