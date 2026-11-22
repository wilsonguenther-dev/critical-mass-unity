namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 12 (P3/P5 assists): who damaged whom recently, so a kill credits ASSISTS: every actor (other than the
    /// killer and the victim) that damaged the victim within Window seconds of the knock-out. Fixed 32 x 32 table of last
    /// damage times, no allocation; pure (tests: PxHudAssistTests). View side only: the sim has no assist rule.
    /// </summary>
    public sealed class AssistTracker
    {
        public const int Max = 32;
        public const double DefaultWindow = 8.0;
        public const int RecapMax = 4;

        /// <summary>Seconds before a knock-out that a hit still counts as an assist (tunable).</summary>
        public double Window = DefaultWindow;

        readonly double[] _last = new double[Max * Max];   // [victim * Max + attacker] = match time of the last damage, 0 never
        readonly int[] _assists = new int[Max];
        readonly int[] _recap = new int[RecapMax];

        /// <summary>The assisters of the last death of the watched victim (the death recap's ASSIST line).</summary>
        public int RecapCount { get; private set; }
        public int RecapAt(int i) => i >= 0 && i < RecapCount ? _recap[i] : -1;

        public void Clear()
        {
            System.Array.Clear(_last, 0, _last.Length);
            System.Array.Clear(_assists, 0, _assists.Length);
            RecapCount = 0;
        }

        public void OnHit(int victim, int attacker, double time)
        {
            if (victim < 0 || victim >= Max || attacker < 0 || attacker >= Max || victim == attacker) return;
            _last[victim * Max + attacker] = time <= 0 ? 1e-6 : time;
        }

        /// <summary>True when attacker damaged victim within the window before time.</summary>
        public bool DamagedRecently(int victim, int attacker, double time)
        {
            if (victim < 0 || victim >= Max || attacker < 0 || attacker >= Max) return false;
            double t = _last[victim * Max + attacker];
            return t > 0 && time - t <= Window;
        }

        /// <summary>
        /// A knock-out: credits every assister, clears the victim's row. Returns true when watch (the local player) earned an
        /// assist. When the victim is recapVictim (the local player) its assisters are kept for the death recap.
        /// </summary>
        public bool OnDeath(int victim, int killer, double time, int watch, int recapVictim)
        {
            if (victim < 0 || victim >= Max) return false;
            bool credited = false;
            if (victim == recapVictim) RecapCount = 0;
            for (int a = 0; a < Max; a++)
            {
                if (a == killer || a == victim || !DamagedRecently(victim, a, time)) continue;
                _assists[a]++;
                if (a == watch) credited = true;
                if (victim == recapVictim && RecapCount < RecapMax) _recap[RecapCount++] = a;
            }
            System.Array.Clear(_last, victim * Max, Max);
            return credited;
        }

        public int Assists(int id) => id >= 0 && id < Max ? _assists[id] : 0;
    }
}
