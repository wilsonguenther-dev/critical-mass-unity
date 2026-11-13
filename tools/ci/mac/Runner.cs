// Minimal NUnit runner for the Mac's no-Unity Sim gate (tools/ci/sim-tests-mac.sh). Lives outside Assets/, so Unity never
// compiles it. Runs every [Test] and [TestCase] of every fixture in this assembly with SetUp/TearDown and
// OneTimeSetUp/OneTimeTearDown; skips [Explicit] and [Ignore]. Args: [--slow|--all] [fixture substrings...].
// CI uses NUnitLite instead (tools/ci/sim-tests.sh), which has the full NUnit semantics.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public static class Runner
{
    static bool Has<T>(MemberInfo m) => m.GetCustomAttributes(typeof(T), true).Length > 0;
    static IEnumerable<MethodInfo> Marked<T>(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(Has<T>);
    static bool IsSlow(MemberInfo m) => m.GetCustomAttributes(typeof(CategoryAttribute), true).Cast<CategoryAttribute>().Any(c => c.Name == "Slow");

    public static int Main(string[] args)
    {
        string mode = args.Contains("--all") ? "all" : args.Contains("--slow") ? "slow" : "fast";
        var filters = args.Where(a => !a.StartsWith("--")).ToArray();
        int pass = 0, fail = 0, skip = 0;
        foreach (Type t in typeof(Runner).Assembly.GetTypes().Where(x => !x.IsAbstract && Has<TestFixtureAttribute>(x) || x.GetMethods().Any(Has<TestAttribute>)).OrderBy(x => x.FullName))
        {
            if (t == typeof(Runner) || t.IsAbstract) continue;
            if (filters.Length > 0 && !filters.Any(f => t.Name.Contains(f))) continue;
            bool fixtureSlow = IsSlow(t);
            object inst = null; bool setupFailed = false;
            foreach (MethodInfo m in t.GetMethods().OrderBy(x => x.Name))
            {
                var cases = m.GetCustomAttributes(typeof(TestCaseAttribute), true).Cast<TestCaseAttribute>().Select(c => c.Arguments).ToList();
                if (cases.Count == 0 && Has<TestAttribute>(m)) cases.Add(new object[0]);
                if (cases.Count == 0) continue;
                bool slow = fixtureSlow || IsSlow(m);
                if ((mode == "fast" && slow) || (mode == "slow" && !slow) || Has<ExplicitAttribute>(m) || Has<IgnoreAttribute>(m) || Has<IgnoreAttribute>(t)) { skip += cases.Count; continue; }
                foreach (object[] a in cases)
                {
                    string name = t.Name + "." + m.Name + (a.Length > 0 ? "(" + string.Join(",", a.Select(x => x?.ToString() ?? "null")) + ")" : "");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        if (setupFailed) throw new Exception("OneTimeSetUp failed");
                        if (inst == null)
                        {
                            inst = Activator.CreateInstance(t);
                            try { foreach (var os in Marked<OneTimeSetUpAttribute>(t)) os.Invoke(os.IsStatic ? null : inst, null); }
                            catch { setupFailed = true; throw; }
                        }
                        foreach (var su in Marked<SetUpAttribute>(t)) su.Invoke(inst, null);
                        try { m.Invoke(m.IsStatic ? null : inst, a.Length == 0 ? null : a); }
                        finally { foreach (var td in Marked<TearDownAttribute>(t)) td.Invoke(inst, null); }
                        pass++;
                        Console.WriteLine("PASS " + name + " (" + sw.ElapsedMilliseconds + " ms)");
                    }
                    catch (Exception e)
                    {
                        Exception x = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                        if (x is IgnoreException || x is InconclusiveException || x is SuccessException) { if (x is SuccessException) pass++; else skip++; Console.WriteLine("SKIP " + name); continue; }
                        fail++;
                        string msg = (x.GetType().Name + ": " + x.Message).Replace("\n", " | ");
                        Console.WriteLine("FAIL " + name + ": " + (msg.Length > 4000 ? msg.Substring(0, 4000) : msg));
                    }
                }
            }
            if (inst != null) foreach (var ot in Marked<OneTimeTearDownAttribute>(t)) { try { ot.Invoke(ot.IsStatic ? null : inst, null); } catch { } }
        }
        Console.WriteLine("passed " + pass + ", failed " + fail + ", not run " + skip);
        return fail == 0 && pass > 0 ? 0 : 1;
    }
}
