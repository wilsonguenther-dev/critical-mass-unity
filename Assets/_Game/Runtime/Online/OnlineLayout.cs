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
        public static readonly Rect List = new Rect(140f, 216f, 1060f, 590f);
        public static readonly Rect Controls = new Rect(1228f, 216f, 552f, 300f);
        public static readonly Rect Chat = new Rect(1228f, 536f, 552f, 270f);
        public static readonly Rect Status = new Rect(140f, 826f, 1640f, 58f);
        public static readonly Rect Footer = new Rect(140f, 900f, 1640f, 84f);

        // Header children.
        public static readonly Rect Title = new Rect(140f, 80f, 860f, 62f);
        public static readonly Rect Sub = new Rect(140f, 148f, 1040f, 40f);
        public static readonly Rect Code = new Rect(1020f, 80f, 470f, 62f);
        public static readonly Rect Copy = new Rect(1510f, 84f, 270f, 56f);
        public static readonly Rect Count = new Rect(1200f, 148f, 580f, 40f);

        // Player table: two columns of 16 rows (the second column fills from the 17th player).
        public const int RowsPerColumn = 16, MaxRows = 2 * RowsPerColumn;
        public const float RowH = 34f, TableHeadH = 30f, ColumnGap = 20f;
        public static float ColumnW => (List.width - ColumnGap) * 0.5f;
        /// <summary>Cell widths inside one table column: name, team, class, ready, ping.</summary>
        public static readonly float[] Cells = { 168f, 92f, 108f, 102f, 50f };

        public static Rect Row(int i)
        {
            int col = i / RowsPerColumn, row = i % RowsPerColumn;
            return new Rect(List.x + col * (ColumnW + ColumnGap), List.y + TableHeadH + row * RowH, ColumnW, RowH);
        }

        public static Rect Cell(int i, int c)
        {
            Rect r = Row(i);
            float x = r.x;
            for (int k = 0; k < c; k++) x += Cells[k];
            return new Rect(x, r.y, Cells[c], r.height - 4f);
        }

        public static Rect HeadCell(int col, int c)
        {
            Rect r = Cell(col * RowsPerColumn, c);
            return new Rect(r.x, List.y, r.width, TableHeadH - 4f);
        }

        // Controls children.
        public static readonly Rect Team = new Rect(1228f, 216f, 552f, 64f);
        public static readonly Rect Class = new Rect(1228f, 292f, 552f, 64f);
        public static readonly Rect StartButton = new Rect(1228f, 368f, 552f, 80f);
        public static readonly Rect StartWhy = new Rect(1228f, 458f, 552f, 56f);

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
