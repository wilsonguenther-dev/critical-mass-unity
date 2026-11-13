// Entry point for the no-Unity Sim test run (NUnitLite). Arguments are NUnitLite's: --where "cat != Slow", --result=FILE.
namespace CriticalMass.Ci
{
    public static class SimTestsProgram
    {
        public static int Main(string[] args) =>
            new NUnitLite.AutoRun(typeof(SimTestsProgram).Assembly).Execute(args);
    }
}
