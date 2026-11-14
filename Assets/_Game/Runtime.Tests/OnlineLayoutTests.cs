using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// online-rooms-5: the online screens' layout table (OnlineLayout) at 1280 x 720 and 1920 x 1080: regions never
    /// overlap, every child sits inside its region, the player table holds 1, 8 and 32 players without leaving the list
    /// region or touching the controls or chat, the chat box holds 5 lines plus the typing line, everything stays on
    /// screen, and the match HUD chat lane is in the HUD collision table and inside the screen.
    /// </summary>
    public sealed class OnlineLayoutTests
    {
        static readonly Vector2Int[] Sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) };

        static bool Inside(Rect inner, Rect outer) =>
            inner.xMin >= outer.xMin - 0.01f && inner.yMin >= outer.yMin - 0.01f && inner.xMax <= outer.xMax + 0.01f && inner.yMax <= outer.yMax + 0.01f;

        [Test]
        public void RegionsDoNotOverlapAndSitInsideThePanel()
        {
            Rect[] r = OnlineLayout.Regions();
            for (int i = 0; i < r.Length; i++)
            {
                Assert.IsTrue(Inside(r[i], OnlineLayout.Panel), "region " + i + " leaves the panel");
                for (int j = i + 1; j < r.Length; j++) Assert.IsFalse(r[i].Overlaps(r[j]), $"regions {i} and {j} overlap");
            }
        }

        [Test]
        public void ChildrenStayInTheirRegions()
        {
            foreach (Rect c in new[] { OnlineLayout.Title, OnlineLayout.Sub, OnlineLayout.Code, OnlineLayout.Copy, OnlineLayout.Count })
                Assert.IsTrue(Inside(c, OnlineLayout.Header), "header child " + c);
            var header = new List<Rect> { OnlineLayout.Title, OnlineLayout.Sub, OnlineLayout.Code, OnlineLayout.Copy, OnlineLayout.Count };
            for (int i = 0; i < header.Count; i++)
                for (int j = i + 1; j < header.Count; j++) Assert.IsFalse(header[i].Overlaps(header[j]), $"header children {i} and {j} overlap");
            var controls = new List<Rect> { OnlineLayout.Team, OnlineLayout.Class, OnlineLayout.StartButton, OnlineLayout.StartWhy };
            foreach (Rect c in controls) Assert.IsTrue(Inside(c, OnlineLayout.Controls), "control " + c);
            for (int i = 0; i < controls.Count; i++)
                for (int j = i + 1; j < controls.Count; j++) Assert.IsFalse(controls[i].Overlaps(controls[j]), $"controls {i} and {j} overlap");
            Assert.IsTrue(Inside(OnlineLayout.Leave, OnlineLayout.Footer));
            Assert.IsTrue(Inside(OnlineLayout.ChatLane, OnlineLayout.Chat));
            foreach (Rect c in new[] { OnlineLayout.NameLabel, OnlineLayout.NameBox, OnlineLayout.CreateButton, OnlineLayout.JoinButton,
                         OnlineLayout.CodeLabel, OnlineLayout.CodeBig, OnlineLayout.CodeHint, OnlineLayout.PasteButton, OnlineLayout.JoinGo })
                Assert.IsTrue(Inside(c, OnlineLayout.List), "home or join child " + c);
            Assert.IsFalse(OnlineLayout.PasteButton.Overlaps(OnlineLayout.JoinGo));
        }

        [TestCase(1)]
        [TestCase(8)]
        [TestCase(32)]
        public void ThePlayerTableHoldsTheRoomWithoutTouchingAnythingElse(int players)
        {
            Assert.LessOrEqual(players, OnlineLayout.MaxRows);
            bool two = OnlineLayout.TwoColumns(players);
            var cells = new List<Rect>();
            for (int i = 0; i < players; i++)
            {
                Rect row = OnlineLayout.Row(i, two);
                Assert.IsTrue(Inside(row, OnlineLayout.List), $"row {i} leaves the list region");
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                {
                    Rect cell = OnlineLayout.Cell(i, c, two);
                    Assert.IsTrue(Inside(cell, row), $"row {i} cell {c} leaves its row");
                    foreach (Rect other in cells) Assert.IsFalse(cell.Overlaps(other), $"row {i} cell {c} overlaps another cell");
                    cells.Add(cell);
                }
                foreach (Rect r in new[] { OnlineLayout.Controls, OnlineLayout.Chat, OnlineLayout.Status, OnlineLayout.Header, OnlineLayout.Footer })
                    Assert.IsFalse(row.Overlaps(r), $"row {i} overlaps a region");
            }
        }

        [Test]
        public void OneColumnUpTo16PlayersTwoAbove()
        {
            Assert.IsFalse(OnlineLayout.TwoColumns(1));
            Assert.IsFalse(OnlineLayout.TwoColumns(16));
            Assert.IsTrue(OnlineLayout.TwoColumns(17));
            Assert.AreEqual(OnlineLayout.List.width, OnlineLayout.ColumnW(false), 0.01f, "one column uses the whole table width");
            Assert.Greater(OnlineLayout.Cell(0, OnlineLayout.CellName, false).width, OnlineLayout.Cell(0, OnlineLayout.CellName, true).width);
            Assert.GreaterOrEqual(OnlineLayout.RowSize(true), UiScale.TypeMin, "never below the type scale's minimum");
            Assert.GreaterOrEqual(OnlineLayout.RowSize(false), OnlineLayout.RowSize(true));
            Assert.GreaterOrEqual(OnlineLayout.Cell(0, OnlineLayout.CellName, true).width, 160f - 0.01f, "the two-column name keeps its width (online-rooms-7 took the class room from the gutters)");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ColumnsHaveGapsAndHeadsSitOverTheirColumns(bool two)
        {
            for (int col = 0; col < (two ? 2 : 1); col++)
            {
                int first = col * OnlineLayout.RowsPerColumn;
                Rect row = OnlineLayout.Row(first, two);
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                {
                    Rect cell = OnlineLayout.Cell(first, c, two), head = OnlineLayout.HeadCell(col, c, two);
                    Assert.Greater(cell.width, 0f, $"column {c} has a width");
                    Assert.IsTrue(Inside(cell, row), $"column {c} stays in its row");
                    Assert.AreEqual(cell.x, head.x, 0.01f, $"head {c} starts over its column");
                    Assert.AreEqual(cell.width, head.width, 0.01f, $"head {c} is as wide as its column");
                    if (c > 0)
                        Assert.GreaterOrEqual(cell.xMin - OnlineLayout.Cell(first, c - 1, two).xMax, OnlineLayout.CellGap - 0.01f, $"a gap before column {c}");
                }
            }
        }

        /// <summary>
        /// online-rooms-6: at 1280 x 720 (the font drawn at its rounded screen size, the conservative case) every cell's
        /// widest text fits its column: a 12-character name, CRIMSON, the longest class, NOT READY and a 3-digit ping, and
        /// every head, for 32 players (two columns) and 8 players (one column).
        /// </summary>
        [TestCase(32)]
        [TestCase(8)]
        public void EveryCellsWidestTextFitsItsColumn(int players)
        {
            bool two = OnlineLayout.TwoColumns(players);
            const float w = 1280f, h = 720f;
            float scale = Mathf.Min(w / OnlineLayout.RefW, h / OnlineLayout.RefH);
            int drawn = Mathf.Max(1, Mathf.RoundToInt(OnlineLayout.RowSize(two) * scale));
            string longestClass = "";
            foreach (string n in System.Enum.GetNames(typeof(CriticalMass.Sim.Config.ClassId)))
                if (n.Length > longestClass.Length) longestClass = n.ToUpperInvariant();
            string[][] texts =
            {
                new[] { "SHAYDENCALEB", "PLAYER" },
                new[] { "CRIMSON", "AZURE", "TEAM" },
                new[] { longestClass, "CLASS" },
                new[] { "NOT READY", "READY", "HOST" },
                new[] { "999 MS", "PING" },
            };
            var root = new GameObject("Online Table Probe");
            try
            {
                Text t = HudText.Make(root.transform, "Probe", CMTokens.Ui, drawn, Color.white, TextAnchor.MiddleLeft, 2f);
                Assert.IsNotNull(t.font, "the font loads");
                int last = players - 1;
                for (int c = 0; c < OnlineLayout.CellCount; c++)
                {
                    float cellW = OnlineLayout.ToScreen(OnlineLayout.Cell(last, c, two), w, h).width;
                    // online-rooms-7: the class is never cut, so its column keeps a couple of px to spare at the smallest screen.
                    float spare = c == OnlineLayout.CellClass ? 2f : 0f;
                    foreach (string s in texts[c])
                        Assert.LessOrEqual(TextFit.WidthOf(t, s, drawn) + spare, cellW, $"{players} players: \"{s}\" fits column {c} at {w}x{h}" + (spare > 0f ? " with 2 px to spare" : ""));
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheChatBoxHoldsFiveLinesAndTheTypingLine()
        {
            Rect lane = OnlineLayout.ChatLane;
            float need = OnlineChatHud.MaxLines * HudZones.ChatLineH * 0.8f + 6f + HudZones.ChatInputH;
            Assert.GreaterOrEqual(lane.height, need, "5 lines of small text plus the typing line fit the chat box");
            Rect hud = HudZones.Chat;
            Assert.GreaterOrEqual(hud.height, OnlineChatHud.MaxLines * HudZones.ChatLineH * 0.8f + 6f + HudZones.ChatInputH, "the match lane fits 5 lines too");
            bool listed = false;
            foreach (HudZones.Piece p in HudZones.Pieces()) if (p.Lane == HudZone.Chat) listed = true;
            Assert.IsTrue(listed, "the match chat lane is in the HUD collision table");
        }

        [Test]
        public void EverythingIsOnScreenAtBothSizes()
        {
            foreach (Vector2Int s in Sizes)
            {
                var screen = new Rect(0, 0, s.x, s.y);
                Assert.IsTrue(Inside(OnlineLayout.ToScreen(OnlineLayout.Panel, s.x, s.y), screen), s + ": the panel is on screen");
                Assert.IsTrue(Inside(OnlineLayout.ToScreen(OnlineLayout.Row(OnlineLayout.MaxRows - 1), s.x, s.y), screen), s + ": the 32nd row is on screen");
                Rect hud = HudLayout.Scale(s.x, s.y) > 0 ? new Rect(HudZones.Chat.x * HudLayout.Scale(s.x, s.y), HudZones.Chat.y * HudLayout.Scale(s.x, s.y),
                    HudZones.Chat.width * HudLayout.Scale(s.x, s.y), HudZones.Chat.height * HudLayout.Scale(s.x, s.y)) : default;
                Assert.IsTrue(Inside(hud, screen), s + ": the match chat lane is on screen");
                Assert.GreaterOrEqual(hud.xMin, 0f, s + ": the match chat lane starts inside the left edge");
            }
        }
    }
}
