using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Online
{
    /// <summary>
    /// online-rooms-5: the one layout table of the online screens, in the 1920 x 1080 reference space every menu scales
    /// uniformly from (top-left origin, y down). Everything sits on one navy panel over a dim scrim, in fixed regions that
    /// never overlap: header (title, subtitle, room code, COPY CODE, the "2 of 3" count), the player table (left), the
    /// controls (right), the chat box (right, its own bordered region), the status line, and the footer (LEAVE / BACK).
    /// The screen builds from these rects and OnlineLayoutTests proves no two regions overlap, every child fits its
    /// region, and the table holds 32 players, at 1280 x 720 and 1920 x 1080.
    /// </summary>
    public static class OnlineLayout
    {
        public const float RefW = 1920f, RefH = 1080f;

        public static readonly Rect Panel = new Rect(110f, 54f, 1700f, 952f);

        // Regions (inside Panel, 24 px gutters).
        public static readonly Rect Header = new Rect(140f, 78f, 1640f, 118f);
        public static readonly Rect List = new Rect(140f, 216f, 1152f, 590f);   // online-rooms-6: wider, so 32 rows of five columns fit
        public static readonly Rect Controls = new Rect(1316f, 216f, 464f, 300f);
        public static readonly Rect Chat = new Rect(1316f, 536f, 464f, 270f);
        public static readonly Rect Status = new Rect(140f, 826f, 1640f, 58f);
        public static readonly Rect Footer = new Rect(140f, 900f, 1640f, 84f);

        // Header children.
        public static readonly Rect Title = new Rect(140f, 80f, 860f, 62f);
        public static readonly Rect Sub = new Rect(140f, 148f, 1040f, 40f);
        public static readonly Rect Code = new Rect(1020f, 80f, 470f, 62f);
        public static readonly Rect Copy = new Rect(1510f, 84f, 270f, 56f);
        public static readonly Rect Count = new Rect(1200f, 148f, 580f, 40f);

        // Player table. online-rooms-6: one column using the whole table width up to 16 players; two columns of 16 rows
        // (the second fills from the 17th player) only above that. Every column has a fixed x and width inside its row:
        // the name is left-aligned and takes what is left (ellipsised at its width), then team, class, state, and the
        // ping right-aligned. One font size for the whole table from the type scale: TypeSmall in one column, TypeMin
        // (the scale's minimum, never smaller) in two. The fixed widths are at TypeMin and grow with the size.
        public const int RowsPerColumn = 16, MaxRows = 2 * RowsPerColumn, CellCount = 5;
        public const int CellName = 0, CellTeam = 1, CellClass = 2, CellState = 3, CellPing = 4;
        public const float RowH = 34f, TableHeadH = 30f, ColumnGap = 20f, CellPad = 6f, CellGap = 10f;
        static readonly float[] FixedAtMin = { 0f, 80f, 116f, 96f, 62f };   // team, class, state, ping (the name takes the rest)

        public static bool TwoColumns(int players) => players > RowsPerColumn;

        public static float ColumnW(bool two) => two ? (List.width - ColumnGap) * 0.5f : List.width;

        public static int RowSize(bool two) => two ? UiScale.TypeMin : UiScale.TypeSmall;

        public static TextAnchor CellAnchor(int c) => c == CellPing ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

        public static Rect Row(int i, bool two)
        {
            int col = two ? i / RowsPerColumn : 0, row = two ? i % RowsPerColumn : i;
            float w = ColumnW(two);
            return new Rect(List.x + col * (w + ColumnGap), List.y + TableHeadH + row * RowH, w, RowH);
        }

        /// <summary>The two-column row (the 32-player layout; tests and the 32nd row).</summary>
        public static Rect Row(int i) => Row(i, true);

        static float CellWidth(int c, bool two)
        {
            float k = RowSize(two) / (float)UiScale.TypeMin;
            if (c != CellName) return FixedAtMin[c] * k;
            float fixedSum = 0f;
            for (int f = 1; f < CellCount; f++) fixedSum += FixedAtMin[f] * k;
            return ColumnW(two) - 2f * CellPad - (CellCount - 1) * CellGap - fixedSum;
        }

        public static Rect Cell(int i, int c, bool two)
        {
            Rect r = Row(i, two);
            float x = r.x + CellPad;
            for (int k = 0; k < c; k++) x += CellWidth(k, two) + CellGap;
            return new Rect(x, r.y, CellWidth(c, two), r.height - 4f);
        }

        public static Rect Cell(int i, int c) => Cell(i, c, true);

        public static Rect HeadCell(int col, int c, bool two)
        {
            Rect r = Cell(col * RowsPerColumn, c, two);
            return new Rect(r.x, List.y, r.width, TableHeadH - 4f);
        }

        // Controls children.
        public static readonly Rect Team = new Rect(1316f, 216f, 464f, 64f);
        public static readonly Rect Class = new Rect(1316f, 292f, 464f, 64f);
        public static readonly Rect StartButton = new Rect(1316f, 368f, 464f, 80f);
        public static readonly Rect StartWhy = new Rect(1316f, 458f, 464f, 56f);

        /// <summary>The chat text lane inside the chat box (OnlineChatHud draws here on the room screen).</summary>
        public static Rect ChatLane => new Rect(Chat.x + 14f, Chat.y + 12f, Chat.width - 28f, Chat.height - 24f);

        // Footer children.
        public static readonly Rect Leave = new Rect(140f, 908f, 320f, 72f);
        public static readonly Rect Back = Leave;

        // Home and join pages (their own children inside List and Controls).
        public static readonly Rect NameLabel = new Rect(140f, 216f, 620f, 30f);
        public static readonly Rect NameBox = new Rect(140f, 250f, 620f, 80f);
        public static readonly Rect CreateButton = new Rect(140f, 360f, 620f, 92f);
        public static readonly Rect JoinButton = new Rect(140f, 472f, 620f, 92f);
        public static readonly Rect CodeLabel = new Rect(140f, 216f, 900f, 30f);
        public static readonly Rect CodeBig = new Rect(140f, 250f, 900f, 130f);
        public static readonly Rect CodeHint = new Rect(140f, 396f, 1060f, 40f);
        public static readonly Rect PasteButton = new Rect(140f, 456f, 300f, 84f);
        public static readonly Rect JoinGo = new Rect(460f, 456f, 420f, 84f);

        /// <summary>Every region (for the overlap test).</summary>
        public static Rect[] Regions() => new[] { Header, List, Controls, Chat, Status, Footer };

        /// <summary>Uniform fit of the reference space into a w x h screen, centred (the menus' scale rule).</summary>
        public static Rect ToScreen(Rect r, float w, float h)
        {
            float s = Mathf.Min(w / RefW, h / RefH);
            float ox = (w - RefW * s) * 0.5f, oy = (h - RefH * s) * 0.5f;
            return new Rect(ox + r.x * s, oy + r.y * s, r.width * s, r.height * s);
        }
    }
}
