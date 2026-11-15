using System;

namespace CriticalMass.Net
{
    /// <summary>
    /// online-rooms-9: a client's input lead (the felt input delay, in ticks), adapted from what the host reports.
    /// <para>
    /// Every bundle packet carries the host's acknowledgement of this client's inputs; the margin is how many ticks of
    /// this client's inputs were already at the host beyond the last tick the host had sequenced when it sent the packet.
    /// A margin near 0 means the inputs arrive just in time; a margin of m means the lead is m ticks longer than needed.
    /// </para>
    /// <list type="bullet">
    /// <item>Grow: the host replaced one of our inputs (it came too late), or the smallest margin of the last window fell
    /// below 1 tick. One tick at a time, at most every <see cref="GrowGapMs"/>, up to <see cref="Ceiling"/>.</item>
    /// <item>Shrink: no late input for <see cref="QuietMs"/> and the smallest margin over the last two windows (about
    /// 1.5 to 3 s, so jitter spikes count) is above <see cref="SafetyTicks"/>: one tick per <see cref="ShrinkEveryMs"/>,
    /// down to <see cref="Floor"/>. The old rule needed the margin to stay exactly the same for 3 s, which a real link never
    /// does, so a lead that once grew (19 ticks, 633 ms, in the 2026-11-15 relay run) never came back down.</item>
    /// </list>
    /// Pure and deterministic in its inputs; the lead only decides which future tick a local input is sent for, so it never
    /// touches what the worlds compute.
    /// </summary>
    public sealed class LeadPolicy
    {
        public const int SafetyTicks = 2;
        public const long WindowMs = 1500, ShrinkEveryMs = 1000, QuietMs = 3000, GrowGapMs = 500;

        public readonly int Floor, Ceiling;
        public int Lead { get; private set; }
        public int Min { get; private set; }
        public int Max { get; private set; }
        public int Grown { get; private set; }
        public int Shrunk { get; private set; }

        int _winMin = int.MaxValue, _prevMin = int.MaxValue;
        long _winStart = long.MinValue / 2;
        long _lastLate = long.MinValue / 2, _lastGrow = long.MinValue / 2, _lastShrink = long.MinValue / 2;
        bool _anyMargin;

        public LeadPolicy(int start, int floor, int ceiling)
        {
            Floor = Math.Max(1, floor);
            Ceiling = Math.Max(Floor, ceiling);
            Lead = Math.Min(Ceiling, Math.Max(Floor, start));
            Min = Max = Lead;
        }

        /// <summary>The floor and ceiling at a tick length: 100 ms (at least 2 ticks) to maxLeadMs.</summary>
        public static LeadPolicy For(int start, double tickMs, int maxLeadMs) =>
            new LeadPolicy(start, Math.Max(2, (int)Math.Ceiling(FloorMs / tickMs - 1e-6)), Math.Max(2, (int)Math.Ceiling(maxLeadMs / tickMs - 1e-6)));

        /// <summary>The lead never goes below this many milliseconds (frame and send pacing alone take about this).</summary>
        public const int FloorMs = 100;

        /// <summary>The host replaced one of our inputs.</summary>
        public void OnLate(long now)
        {
            _lastLate = now;
            Grow(now);
        }

        /// <summary>One packet's margin (host-acknowledged input tick minus the host's last sequenced tick in it).</summary>
        public void OnMargin(int margin, long now)
        {
            if (now - _winStart >= WindowMs)
            {
                _prevMin = _anyMargin ? _winMin : int.MaxValue;
                _winMin = int.MaxValue;
                _winStart = now;
            }
            _anyMargin = true;
            if (margin < _winMin) _winMin = margin;
            int low = Math.Min(_winMin, _prevMin);
            if (low < 1) { Grow(now); return; }   // 0: just in time; below 0: the host had to wait or repeat
            if (now - _lastLate < QuietMs || now - _lastGrow < QuietMs) return;
            if (_prevMin == int.MaxValue) return;   // need a whole previous window before shrinking
            if (low > SafetyTicks && Lead > Floor && now - _lastShrink >= ShrinkEveryMs)
            {
                Lead--;
                Shrunk++;
                _lastShrink = now;
                if (Lead < Min) Min = Lead;
                // A lead one tick shorter makes every margin one tick smaller: count that now rather than wait a round
                // trip to see it, so the next shrink needs margins that still clear the safety with the shorter lead.
                if (_prevMin != int.MaxValue) _prevMin--;
                if (_winMin != int.MaxValue) _winMin--;
            }
        }

        void Grow(long now)
        {
            if (Lead >= Ceiling || now - _lastGrow < GrowGapMs) return;
            Lead++;
            Grown++;
            _lastGrow = now;
            if (Lead > Max) Max = Lead;
            // Margins measured for the shorter lead no longer apply.
            _prevMin = int.MaxValue;
            _winMin = int.MaxValue;
            _winStart = now;
            _anyMargin = false;
        }
    }
}
