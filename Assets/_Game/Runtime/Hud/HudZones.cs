using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>Screen zones of the match HUD (polish pass 1). Every overlay that draws during a match lives in one.</summary>
    public enum HudZone
    {
        /// <summary>Match and Kaiju clusters: the clock, scores, hill bar and Kaiju meter. Nothing else draws here.</summary>
        TopBand,
        /// <summary>
        /// Centre lane under the top band, shared by priority: the autopilot banner (and its respawn notice), the lockout
        /// warning, event banners and the Kaiju callout. One thing at a time; the highest priority wins.
        /// </summary>
        Callout,
        /// <summary>Right column under the Kaiju cluster: the kill feed.</summary>
        Feed,
        /// <summary>Right column under the kill feed: big-moment stingers.</summary>
        Stinger,
        /// <summary>Right column under the stingers: achievement toasts.</summary>
        Toast,
        /// <summary>Left, above the player card: the mascot's tutorial bubble (its upper spot sits under the centre lane).</summary>
        Guide,
        /// <summary>
        /// Centre, below the middle: the death recap and the "you are the ..." line. It runs beside the mascot's column and
        /// overlaps its right edge, so the mascot takes part in this lane's priority (it steps back while either shows).
        /// </summary>
        Lower,
        /// <summary>Bottom-left player card (bible p15 cluster 03).</summary>
        PlayerCard,
        /// <summary>Bottom-right minimap (bible p15 cluster 04).</summary>
        Minimap,
    }

    /// <summary>Who wins a shared zone when two things want it in the same frame. Higher value wins.</summary>
    public enum HudPriority
    {
        Tip = 0,
        Guide = 1,
        KaijuCallout = 2,
        Intro = 3,
        Banner = 4,
        Lockout = 5,
        Recap = 6,
        Autopilot = 7,
        Takeover = 8,
    }

    /// <summary>
    /// Polish pass 1, HUD declutter: the zones of the 1920x1080 reference space (top-left origin, y down, the space every
    /// HUD canvas scales uniformly from) and a tiny priority arbiter for the shared ones. Always on screen is only what a
    /// player reads every second (TopBand, PlayerCard, Minimap); everything else appears in its own zone when relevant, and
    /// when two things want the same zone the lower priority one waits (it stays built, its group goes transparent).
    /// <para>Use: an overlay that is showing calls <see cref="Hold"/> every frame; one that shares a zone asks
    /// <see cref="Blocked"/> before drawing. A hold lasts the frame it was made and the next one, so update order never
    /// makes two overlays flicker. Fixed arrays, no allocation, view only (nothing reaches the sim).</para>
    /// </summary>
    public static class HudZones
    {
        public const float RefW = HudLayout.RefW, RefH = HudLayout.RefH, M = HudLayout.Margin;

        // ---- zone rects (reference px, top-left origin) ----
        /// <summary>The match cluster (clock, scores, hill bar and its word) and the Kaiju cluster, as one band.</summary>
        public static readonly Rect TopBand = new Rect(HudLayout.RefRect(HudCluster.Match).x, 0f,
            HudLayout.RefRect(HudCluster.Kaiju).xMax - HudLayout.RefRect(HudCluster.Match).x, 136f);
        /// <summary>Top of the centre lane: 12 px under the top band (the hill word ends at 130).</summary>
        public const float AlertTop = 148f;
        public const float CalloutTop = AlertTop, CalloutH = 188f;
        /// <summary>The centre lane: Banner (1032 wide plus its two discs) and the 1040 autopilot banner, clear of the feed.</summary>
        public static readonly Rect Callout = new Rect(404f, CalloutTop, 1092f, CalloutH);
        public const float ColumnW = 600f;
        public static readonly Rect Feed = new Rect(RefW - M - 400f, 136f, 400f, 46f * 5);
        public static readonly Rect Stinger = new Rect(RefW - M - 560f, 380f, 560f, 140f);
        public const float ToastTop = 540f;
        public static readonly Rect Toast = new Rect(RefW - M - ColumnW, ToastTop, ColumnW, 96f);
        public const float GuideUpperY = CalloutTop + CalloutH + 8f;
        public static readonly Rect Guide = new Rect(M, GuideUpperY, 620f, RefH - M - HudLayout.PlayerSize.y - M - GuideUpperY);
        public static readonly Rect Lower = new Rect(550f, 640f, 820f, 196f);
        public static Rect PlayerCard => HudLayout.RefRect(HudCluster.Player);
        public static Rect Minimap => HudLayout.RefRect(HudCluster.Minimap);

        // ---- owner walk 1: the named pieces that used to pick their own spots (every one is checked by the collision test) ----
        /// <summary>Top of the centre lane's residents: the job line, the team stock and the map-event bar sit here.</summary>
        public const float JobY = 140f, JobW = 760f, JobH = 40f;
        public const float StockY = 188f, StockW = 342f, StockH = 40f;
        public const float EventY = 236f, EventW = 560f, EventH = 38f;
        /// <summary>
        /// The job line, stock and event bar live INSIDE the centre lane (the lane's lowest-priority residents): they show
        /// while nothing else holds the lane and step back for the banner, the Kaiju callout, the autopilot banner and the
        /// takeovers (they used to stay under the banner and peek out above it).
        /// </summary>
        public static readonly Rect Job = new Rect((RefW - JobW) * 0.5f, JobY, JobW, EventY + EventH - JobY);
        /// <summary>Coin counter: right column, 12 px above the minimap frame (it used to sit on the minimap's top edge).</summary>
        public const float CoinsW = 168f, CoinsH = 52f, PowerW = 330f, PowerH = 68f, ColumnGap = 12f;
        public static Rect Coins => new Rect(RefW - M - CoinsW, Minimap.y - ColumnGap - CoinsH, CoinsW, CoinsH);
        /// <summary>Power-up timer: left of the coin counter, bottoms aligned.</summary>
        public static Rect Power => new Rect(Coins.x - ColumnGap - PowerW, Minimap.y - ColumnGap - PowerH, PowerW, PowerH);
        /// <summary>The coin chip of the match rewards: left column, 12 px above the player card.</summary>
        public const float CoinChipW = 168f, CoinChipH = 46f;
        public static Rect CoinChip => new Rect(M, PlayerCard.y - ColumnGap - CoinChipH, CoinChipW, CoinChipH);
        /// <summary>The ping / team line feed: left column, above the coin chip (four 30 px lines, newest at the bottom).</summary>
        public const float PingLineH = 30f, PingLineW = 520f;
        public static Rect PingFeed => new Rect(28f, CoinChip.y - ColumnGap - 4 * PingLineH, PingLineW, 4 * PingLineH);
        /// <summary>The ping feed's bottom, px from the bottom of the reference space (its lines are anchored bottom-left).</summary>
        public static float PingFeedBottom => RefH - PingFeed.yMax;

        /// <summary>The interact prompt ("B Base station"): 12 px under the lower lane (it sat inside it, under the intro line).</summary>
        public const float PromptW = 520f, PromptH = 50f;
        public static Rect Prompt => new Rect((RefW - PromptW) * 0.5f, Lower.yMax + ColumnGap, PromptW, PromptH);

        /// <summary>A named HUD piece for the collision test: its rect (reference px, top-left origin) and its lane.</summary>
        public struct Piece
        {
            public string Name;
            public Rect Rect;
            /// <summary>Pieces in the same shared lane are never drawn together (the arbiter), so they may overlap.</summary>
            public HudZone Lane;
            public bool Shared;
            public Piece(string n, Rect r, HudZone lane, bool shared) { Name = n; Rect = r; Lane = lane; Shared = shared; }
        }

        /// <summary>Every always-on and optional piece of the match HUD (owner walk 1's single layout table).</summary>
        public static Piece[] Pieces()
        {
            Rect kaiju = HudLayout.RefRect(HudCluster.Kaiju), match = HudLayout.RefRect(HudCluster.Match);
            return new[]
            {
                new Piece("match cluster (clock, scores, hill)", match, HudZone.TopBand, false),
                new Piece("kaiju cluster (egg, rage, hatch timer)", kaiju, HudZone.TopBand, false),
                new Piece("job line + stock + event bar", Job, HudZone.Callout, true),
                new Piece("banner / autopilot / callout lane", Callout, HudZone.Callout, true),
                new Piece("kill feed (5 lines)", new Rect(RefW - KillFeed.Right - KillFeed.LineW, KillFeed.Top, KillFeed.LineW, KillFeed.Pitch * KillFeed.Lines), HudZone.Feed, false),
                new Piece("stingers", Stinger, HudZone.Stinger, false),
                new Piece("achievement toast", Toast, HudZone.Toast, false),
                new Piece("power-up timer", Power, HudZone.Minimap, false),
                new Piece("coin counter", Coins, HudZone.Minimap, false),
                new Piece("minimap", Minimap, HudZone.Minimap, false),
                new Piece("player card", PlayerCard, HudZone.PlayerCard, false),
                new Piece("coin chip", CoinChip, HudZone.PlayerCard, false),
                new Piece("ping feed", PingFeed, HudZone.Guide, false),
                new Piece("intro line (you are the ...)", Lower, HudZone.Lower, false),
                new Piece("death recap (centre lane, Recap priority)", new Rect((RefW - DeathRecap.CardW) * 0.5f, DeathRecap.TopY, DeathRecap.CardW, DeathRecap.CardH), HudZone.Callout, true),
                new Piece("interact prompt", Prompt, HudZone.Lower, false),
            };
        }

        /// <summary>The rect of a zone.</summary>
        public static Rect Of(HudZone z)
        {
            switch (z)
            {
                case HudZone.TopBand: return TopBand;
                case HudZone.Callout: return Callout;
                case HudZone.Feed: return Feed;
                case HudZone.Stinger: return Stinger;
                case HudZone.Toast: return Toast;
                case HudZone.Guide: return Guide;
                case HudZone.Lower: return Lower;
                case HudZone.PlayerCard: return PlayerCard;
                default: return Minimap;
            }
        }

        /// <summary>Every zone (tests check that no two overlap, except Guide and Lower, which share a priority).</summary>
        public static readonly HudZone[] All =
        {
            HudZone.TopBand, HudZone.Callout, HudZone.Feed, HudZone.Stinger, HudZone.Toast, HudZone.Guide,
            HudZone.Lower, HudZone.PlayerCard, HudZone.Minimap,
        };

        /// <summary>Where a zone is anchored (normalised, y up, as HudLayout.Anchor): right column right, lanes centre.</summary>
        public static Vector2 AnchorOf(HudZone z)
        {
            switch (z)
            {
                case HudZone.TopBand: case HudZone.Callout: return new Vector2(0.5f, 1f);
                case HudZone.Feed: case HudZone.Stinger: case HudZone.Toast: return new Vector2(1f, 1f);
                case HudZone.Guide: return new Vector2(0f, 1f);
                case HudZone.Lower: return new Vector2(0.5f, 0.5f);
                case HudZone.PlayerCard: return HudLayout.Anchor(HudCluster.Player);
                default: return HudLayout.Anchor(HudCluster.Minimap);
            }
        }

        /// <summary>A zone in device pixels on a w x h screen (top-left origin), keeping its anchor (HudLayout.PixelRect's rule).</summary>
        public static Rect PixelRect(HudZone z, float w, float h)
        {
            float s = HudLayout.Scale(w, h);
            Rect r = Of(z);
            Vector2 a = AnchorOf(z);
            float ax = a.x * RefW, ay = (1f - a.y) * RefH;
            return new Rect(a.x * w + (r.x - ax) * s, (1f - a.y) * h + (r.y - ay) * s, r.width * s, r.height * s);
        }

        /// <summary>Zones that may overlap because they share one priority order (the mascot steps back for the lower lane).</summary>
        public static bool Arbitrated(HudZone a, HudZone b) =>
            (a == HudZone.Guide && b == HudZone.Lower) || (a == HudZone.Lower && b == HudZone.Guide);

        // ---- arbiter ----
        const int Zones = 9, Priorities = 9;
        static readonly int[] _held = NewHeld();

        static int[] NewHeld()
        {
            var a = new int[Zones * Priorities];
            for (int i = 0; i < a.Length; i++) a[i] = int.MinValue;
            return a;
        }

        /// <summary>Tests set a frame number; -1 reads Time.frameCount.</summary>
        public static int FrameOverride = -1;

        static int Frame => FrameOverride >= 0 ? FrameOverride : Time.frameCount;

        /// <summary>The caller is drawing in <paramref name="z"/> this frame at priority <paramref name="p"/>.</summary>
        public static void Hold(HudZone z, HudPriority p) => _held[(int)z * Priorities + (int)p] = Frame;

        /// <summary>Something of higher priority holds <paramref name="z"/> (this frame or the last): wait.</summary>
        public static bool Blocked(HudZone z, HudPriority p)
        {
            int f = Frame, row = (int)z * Priorities;
            for (int q = (int)p + 1; q < Priorities; q++)
                if (_held[row + q] >= f - 1) return true;
            return false;
        }

        /// <summary>Clears every hold (a new match, tests).</summary>
        public static void Clear()
        {
            for (int i = 0; i < _held.Length; i++) _held[i] = int.MinValue;
        }
    }
}
