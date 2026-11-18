using System.Collections.Generic;
using System.IO;
using System.Text;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 tools (one tuning index): docs/balance/TUNING-INDEX.md lists every tuning field of GameConfig. A new config field with
    /// no row fails here; the fresh index is written to Temp/balance/TUNING-INDEX.md so it can be copied over.
    /// </summary>
    [TestFixture]
    public sealed class TuningIndexTests
    {
        [Test]
        public void EveryConfigFieldHasARowInTheTuningIndex()
        {
            List<TuningIndex.Entry> entries = TuningIndex.Collect(GameConfig.CreateDefault());
            Assert.Greater(entries.Count, 400, "the walk reaches the nested config classes");
            HashSet<string> listed = File.Exists(TuningIndex.IndexPath) ? TuningIndex.PathsIn(File.ReadAllText(TuningIndex.IndexPath)) : new HashSet<string>();
            var missing = new StringBuilder();
            int count = 0;
            foreach (TuningIndex.Entry e in entries)
            {
                if (listed.Contains(e.Path)) continue;
                count++;
                if (count <= 40) missing.Append("\n  ").Append(e.Path).Append(" (").Append(e.Owner).Append(')');
            }
            if (count > 0)
            {
                Directory.CreateDirectory("Temp/balance");
                File.WriteAllText("Temp/balance/TUNING-INDEX.md", TuningIndex.Markdown(entries));
            }
            Assert.AreEqual(0, count, count + " config fields have no row in " + TuningIndex.IndexPath + ":" + missing +
                "\nRegenerate it with " + TuningIndex.Regenerate + " (a fresh copy was written to Temp/balance/TUNING-INDEX.md).");
        }

        [Test]
        public void TheIndexFindsTheHutCostWithItsFileValueAndMeaning()
        {
            TuningIndex.Entry e = TuningIndex.Collect(GameConfig.CreateDefault()).Find(x => x.Path == "Depth.Hut.LevelCost");
            Assert.IsNotNull(e);
            StringAssert.EndsWith("Config/HutConfig.cs", e.File);
            Assert.AreEqual("[6, 10, 14, 18, 24]", e.Value);
            StringAssert.Contains("Ore each level costs", e.Meaning);
            Assert.AreEqual("double[]", e.Type);
            Assert.IsNotNull(TuningIndex.Collect(GameConfig.CreateDefault()).Find(x => x.Path == "Classes[0].MaxHp"), "class rows are walked by index");
        }

        [Test]
        public void EveryIndexPathIsASwitchTheBalanceReportCanSet()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            int set = 0;
            foreach (TuningIndex.Entry e in TuningIndex.Collect(GameConfig.CreateDefault()))
            {
                if (e.Value == "null" || e.Value.Contains("\"") || e.Value.Contains("...")) continue;
                ConfigOverrides.Set(cfg, e.Path, e.Value);
                set++;
            }
            Assert.Greater(set, 400);
            Assert.AreEqual(TuningIndex.Markdown(TuningIndex.Collect(GameConfig.CreateDefault())), TuningIndex.Markdown(TuningIndex.Collect(cfg)),
                "setting every dial to its own value changes nothing");
        }
    }
}
