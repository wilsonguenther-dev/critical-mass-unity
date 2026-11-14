using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Online;
using NUnit.Framework;
using UnityEngine;

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
            var cells = new List<Rect>();
            for (int i = 0; i < players; i++)
            {
                Rect row = OnlineLayout.Row(i);
                Assert.IsTrue(Inside(row, OnlineLayout.List), $"row {i} leaves the list region");
                for (int c = 0; c < OnlineLayout.Cells.Length; c++)
                {
                    Rect cell = OnlineLayout.Cell(i, c);
                    Assert.IsTrue(Inside(cell, row), $"row {i} cell {c} leaves its row");
                    foreach (Rect other in cells) Assert.IsFalse(cell.Overlaps(other), $"row {i} cell {c} overlaps another cell");
                    cells.Add(cell);
                }
                foreach (Rect r in new[] { OnlineLayout.Controls, OnlineLayout.Chat, OnlineLayout.Status, OnlineLayout.Header, OnlineLayout.Footer })
                    Assert.IsFalse(row.Overlaps(r), $"row {i} overlaps a region");
            }
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
