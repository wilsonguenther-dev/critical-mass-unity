using System;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>The four calls of the ping wheel, in wheel order: up, right, down, left.</summary>
    public enum PingKind : byte { Attack = 0, OnMyWay = 1, Defend = 2, Help = 3 }

    /// <summary>One placed ping. Times are sim seconds (World.Match.Time).</summary>
    public struct Ping
    {
        public bool Active;
        public PingKind Kind;
        public double X, Z;
        public double Time;
        /// <summary>Actor who placed it (0 = the local player in this single-player demo).</summary>
        public int Owner;
        /// <summary>PingWords.Places index of the nearest landmark.</summary>
        public int Place;
        /// <summary>Increments with every placement (views use it to restart their pop animation).</summary>
        public int Serial;
    }

    /// <summary>
    /// The pings of one match, view-side only (the sim never reads them; bots do not answer yet, see the spec below).
    /// Rate limit: at most RateCount placements in any RateWindow seconds; each ping lives Life seconds and fades over its
    /// last FadeSeconds. Fixed capacity, no allocation after construction.
    ///
    /// <para><b>Spec for the rules lane, bots answering pings (not built; the sim is not edited by this item).</b>
    /// 1. Input: the sim gets a ping as an input-frame field of the placing human, <c>InputFrame.Ping = { kind, x, z }</c>
    /// set for exactly one tick (the tick the wheel closed), so replays stay deterministic; this view's PingBook then mirrors
    /// what the sim accepted. The rate limit (3 per 5 s) moves into the sim with it, counted in ticks.
    /// 2. Who answers: living bots of the pinger's team within 40 sim units of the ping, at most 3 for Attack and Defend,
    /// 2 for Help, 0 for On my way (an announcement), nearest first; a bot carrying cargo or holding the hill with fewer than
    /// 2 teammates on it never answers. Each answers once per ping.
    /// 3. What they do, through the existing BotFsm states only: Attack: Fight with the nearest enemy within 8 units of the
    /// ping, else move to the ping (ToHill-style steer to a point) and fight on arrival. Defend: move within 5 units and hold
    /// (HoldHill behaviour at the ping point) for 12 s. Help: move to the pinger (not the ping point) and Fight the pinger's
    /// last attacker for 8 s. The ping expires after 6 s, but an answer already taken runs its own timer.
    /// 4. Acknowledge: the sim pushes SimEventType.PingAnswered (A = bot id, B = pinger, Value = kind), and the HUD shows
    /// "Brook: On my way" in the ping feed from that event.
    /// 5. Tests the rules lane writes: a bot within range changes state on the tick after the ping; one out of range does
    /// not; a 4th ping inside 5 s is ignored by the sim; same seed + same ping ticks = identical world hash.</para>
    /// </summary>
    public sealed class PingBook
    {
        public const int Capacity = 8;
        public const int RateCount = 3;
        public const double RateWindow = 5.0;
        public const double Life = 6.0;
        public const double FadeSeconds = 0.6;

        readonly Ping[] _pings = new Ping[Capacity];
        readonly double[] _recent = new double[RateCount];
        int _recentNext;
        int _serial;

        public PingBook() => Clear();

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++) _pings[i] = default;
            for (int i = 0; i < RateCount; i++) _recent[i] = double.NegativeInfinity;
            _recentNext = 0;
        }

        /// <summary>Whether a ping may be placed at sim time now (the oldest of the last RateCount is RateWindow old).</summary>
        public bool CanPlace(double now) => now - _recent[_recentNext] >= RateWindow - 1e-9;

        /// <summary>Seconds until the next ping is allowed (0 when allowed now).</summary>
        public double WaitSeconds(double now) => Math.Max(0, RateWindow - (now - _recent[_recentNext]));

        /// <summary>Places a ping; false (and nothing changes) when the rate limit refuses it.</summary>
        public bool TryPlace(PingKind kind, double x, double z, double now, int owner, int place, out int slot)
        {
            slot = -1;
            if (!CanPlace(now)) return false;
            _recent[_recentNext] = now;
            _recentNext = (_recentNext + 1) % RateCount;
            // a free slot, else the oldest
            int best = 0;
            double oldest = double.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                if (!_pings[i].Active) { best = i; break; }
                if (_pings[i].Time < oldest) { oldest = _pings[i].Time; best = i; }
            }
            _pings[best] = new Ping
            {
                Active = true, Kind = kind, X = x, Z = z, Time = now, Owner = owner, Place = place, Serial = ++_serial,
            };
            slot = best;
            return true;
        }

        /// <summary>Retires pings older than Life; a clock that went backwards (a new match) clears the book.</summary>
        public void Expire(double now)
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (!_pings[i].Active) continue;
                if (now - _pings[i].Time >= Life || now < _pings[i].Time - 1e-6) _pings[i].Active = false;
            }
        }

        public Ping Get(int i) => _pings[i];

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (_pings[i].Active) n++;
                return n;
            }
        }

        /// <summary>Opacity at sim time now: 1, then a fade over the last FadeSeconds, 0 when gone.</summary>
        public static float Alpha(in Ping p, double now)
        {
            if (!p.Active) return 0f;
            double age = now - p.Time;
            if (age < 0 || age >= Life) return 0f;
            double left = Life - age;
            return left >= FadeSeconds ? 1f : (float)(left / FadeSeconds);
        }
    }

    /// <summary>
    /// The match's pings and the one place they are made (the full map, the world wheel and the HUD hotspots all call
    /// Place). Raises Placed for the feed, the markers and any other item (a sound or announcer item may subscribe).
    /// </summary>
    public static class Pings
    {
        public static readonly PingBook Book = new PingBook();
        /// <summary>Raised after a ping is accepted, with its slot.</summary>
        public static event Action<Ping, int> Placed;
        /// <summary>Raised when the rate limit refuses a ping (the feed says so).</summary>
        public static event Action<double> Refused;

        static int _epoch = int.MinValue;

        /// <summary>Clears the book when a new match starts (World.Epoch changed) and retires old pings.</summary>
        public static void Track(World w)
        {
            if (w == null) return;
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                Book.Clear();
            }
            Book.Expire(w.Match.Time);
        }

        /// <summary>Places a ping at sim point (x, z) for actor owner; false when rate limited.</summary>
        public static bool Place(World w, PingKind kind, double x, double z, int owner)
        {
            if (w == null) return false;
            Track(w);
            double now = w.Match.Time;
            int place = PingWords.NearestPlace(w, x, z);
            if (!Book.TryPlace(kind, x, z, now, owner, place, out int slot))
            {
                Refused?.Invoke(Book.WaitSeconds(now));
                ItemHooks.Sound("ping-refused");
                return false;
            }
            ItemHooks.Sound(PingWords.SoundCue[(int)kind]);
            Placed?.Invoke(Book.Get(slot), slot);
            return true;
        }
    }

    /// <summary>Words, colours and shapes of the pings, and the landmark names used in the feed line.</summary>
    public static class PingWords
    {
        public static readonly string[] Label = { "Attack here", "On my way", "Defend here", "Need help" };
        public static readonly string[] Upper = { "ATTACK HERE", "ON MY WAY", "DEFEND HERE", "NEED HELP" };
        public static readonly string[] SoundCue = { "ping-attack", "ping-onmyway", "ping-defend", "ping-help" };
        /// <summary>Shape per kind, so the call reads without colour: a target arrow, a forward arrow, a shield, a diamond.</summary>
        public static readonly ShapeKind[] Shape = { ShapeKind.TriangleDown, ShapeKind.TriangleRight, ShapeKind.Shield, ShapeKind.Diamond };
        public static readonly string[] Glyph = { "!", ">", "", "?" };

        /// <summary>Colours outside both team hues (art direction: team colours mean teams only).</summary>
        public static Color Colour(PingKind k) =>
            k == PingKind.Attack ? CMTokens.MeterOrange.Color : k == PingKind.OnMyWay ? CMTokens.Biomass.Color :
            k == PingKind.Defend ? CMTokens.WarmCream.Color : CMTokens.ToolBrass.Color;

        public const int Hill = 0, Shrine = 1, AzureBase = 2, CrimsonBase = 3, KaijuPlace = 4, Biomass = 5, Corruption = 6, Field = 7;
        public static readonly string[] Places = { "the hill", "the shrine", "Azure base", "Crimson base", "the Kaiju", "the biomass", "the corruption", "the field" };
        /// <summary>A landmark counts when the ping is within this many sim units of it.</summary>
        public const double PlaceRadius = 12.0;

        /// <summary>Every feed line, built once: [kind, place] = "You: Attack here, the hill".</summary>
        public static readonly string[,] FeedLine = BuildFeed();

        static string[,] BuildFeed()
        {
            var a = new string[Label.Length, Places.Length];
            for (int k = 0; k < Label.Length; k++)
                for (int p = 0; p < Places.Length; p++)
                    a[k, p] = "You: " + Label[k] + ", " + Places[p];
            return a;
        }

        /// <summary>The nearest named landmark within PlaceRadius of (x, z), else Field. Allocation-free.</summary>
        public static int NearestPlace(World w, double x, double z)
        {
            MapDef m = w.Map;
            int best = Field;
            double bestD = PlaceRadius * PlaceRadius;
            Consider(ref best, ref bestD, Hill, m.Hill.X, m.Hill.Z, x, z);
            if (w.Match.Hatched && w.Kaiju.State != KaijuState.Egg) Consider(ref best, ref bestD, KaijuPlace, w.Kaiju.X, w.Kaiju.Z, x, z);
            else Consider(ref best, ref bestD, Shrine, m.Shrine.X, m.Shrine.Z, x, z);
            for (int t = 0; t < 2 && t < m.Pads.Length; t++) Consider(ref best, ref bestD, t == 0 ? AzureBase : CrimsonBase, m.Pads[t].X, m.Pads[t].Z, x, z);
            if (w.Nodes != null)
                for (int i = 0; i < w.Nodes.Length; i++)
                {
                    ResourceNode n = w.Nodes[i];
                    if (n == null) continue;
                    // nodes are small: they only name a ping placed right on them
                    double dx = n.X - x, dz = n.Z - z, d = dx * dx + dz * dz;
                    if (d < 16.0 && d < bestD) { bestD = d; best = n.Kind == NodeKind.Corruption ? Corruption : Biomass; }
                }
            return best;
        }

        static void Consider(ref int best, ref double bestD, int place, double px, double pz, double x, double z)
        {
            double dx = px - x, dz = pz - z, d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = place; }
        }

        /// <summary>The call a hotspot sends for an objective: Defend when your team holds it, else Attack.</summary>
        public static PingKind ForHill(HillState hill, TeamId mine) =>
            (hill == HillState.Azure && mine == TeamId.Azure) || (hill == HillState.Crimson && mine == TeamId.Crimson) ? PingKind.Defend : PingKind.Attack;

        public static PingKind ForKaiju(KaijuState k, TeamId mine) =>
            (k == KaijuState.Azure && mine == TeamId.Azure) || (k == KaijuState.Crimson && mine == TeamId.Crimson) ? PingKind.Defend : PingKind.Attack;
    }
}
