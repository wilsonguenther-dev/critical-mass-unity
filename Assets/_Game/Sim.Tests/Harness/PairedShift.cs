using System;
using System.Collections.Generic;
using System.Globalization;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>
    /// b15 rules pass 3 (2026-11-20): a same-seed "rule on against control" difference with its 95% interval, for the balance
    /// gates whose 24-seed samples became noise-limited once the comeback rule made matches close (a close match flips on any
    /// perturbation, so the on and control results of a seed stop being correlated). Values are paired by index (the same seed
    /// and size at the same position in both lists). Interval = mean of the per-seed differences +- 1.96 x their standard error
    /// (normal approximation; n is printed with it, and every gate using it runs at least 72 16v16 matches).
    /// <para>Sizing: for a win share with uncorrelated arms the standard error of the shift is sqrt(0.5 / n): 0.144 at n = 24
    /// (a 0.15 limit then fails about 30% of the time on noise alone), 0.083 at n = 72 (about 7%).</para>
    /// </summary>
    public static class PairedShift
    {
        public struct Result
        {
            public int N;
            public double Mean, Half, MeanOn, MeanOff;
            public double Lo => Mean - Half;
            public double Hi => Mean + Half;
        }

        public static Result Of(IList<double> on, IList<double> off)
        {
            int n = Math.Min(on.Count, off.Count);
            var r = new Result { N = n };
            if (n == 0) return r;
            double s = 0, so = 0, sf = 0;
            for (int i = 0; i < n; i++) { s += on[i] - off[i]; so += on[i]; sf += off[i]; }
            r.Mean = s / n;
            r.MeanOn = so / n;
            r.MeanOff = sf / n;
            double v = 0;
            for (int i = 0; i < n; i++) { double d = on[i] - off[i] - r.Mean; v += d * d; }
            double sd = n > 1 ? Math.Sqrt(v / (n - 1)) : 0;
            r.Half = 1.96 * sd / Math.Sqrt(n);
            return r;
        }

        public static string Text(Result r, string f = "0.000") =>
            "on " + r.MeanOn.ToString(f, CultureInfo.InvariantCulture) + ", control " + r.MeanOff.ToString(f, CultureInfo.InvariantCulture) + ", paired shift "
            + r.Mean.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + " (95% interval " + r.Lo.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + " to "
            + r.Hi.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + ", n " + r.N + ")";

        /// <summary>The 48 16v16 seeds after WinShare.Seeds16 that make the 72-seed pool (B15SweepTests.Seeds72's tail).</summary>
        public static int[] Extra48()
        {
            var s = new int[48];
            for (int i = 0; i < 48; i++) s[i] = 50000 + (i + 24) * 7919;
            return s;
        }
    }
}
