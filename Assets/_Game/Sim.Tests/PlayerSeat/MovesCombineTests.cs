// Class moves COMBINE (integrator, 2026-11-22): the all-switches-ON measures the combine's decisions are judged by. Every
// number is the HUMAN seat (actor 0, PlayerSeatRig through MatchTick.AdvanceHumans), lag 6 ticks (0.2 s), aim help Low, against
// Normal bots, with MovesConfig.PlayBuildAllOn (the owner's play build) and the knobs as committed. Lever probes: the
// environment variable CM_MVI_KNOBS ("Name=value;Name[i]=value", MovesConfig fields) overrides knobs for one run, CM_MVI_CLASSES
// and CM_MVI_VARIANTS ("Warrior,Healer" / "base,guard") narrow the table. Every line is a "[px] mvi-..." line.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>The combine's switch sets and the lever-probe environment.</summary>
    public static class MvCombine
    {
        /// <summary>The play build: every switch on, then the CM_MVI_KNOBS overrides (none by default).</summary>
        public static void AllOn(GameConfig c)
        {
            MovesConfig.PlayBuildAllOn(c.Depth.Moves);
            ApplyKnobs(c.Depth.Moves, Environment.GetEnvironmentVariable("CM_MVI_KNOBS"));
        }

        /// <summary>Every switch off (today's game).</summary>
        public static void AllOff(GameConfig c) => MovesConfig.AllOff(c.Depth.Moves);

        /// <summary>"Name=value;Name[i]=value" onto MovesConfig fields (double, int, bool and their arrays).</summary>
        public static void ApplyKnobs(MovesConfig m, string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            foreach (string part in spec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2) throw new ArgumentException("CM_MVI_KNOBS: " + part);
                string name = kv[0].Trim(), val = kv[1].Trim();
                int idx = -1;
                int br = name.IndexOf('[');
                if (br > 0) { idx = int.Parse(name.Substring(br + 1, name.Length - br - 2), CultureInfo.InvariantCulture); name = name.Substring(0, br); }
                FieldInfo f = typeof(MovesConfig).GetField(name);
                if (f == null) throw new ArgumentException("CM_MVI_KNOBS: no MovesConfig field " + name);
                Type ft = idx >= 0 ? f.FieldType.GetElementType() : f.FieldType;
                object v = ft == typeof(bool) ? (object)(val == "1" || val.ToLowerInvariant() == "true")
                    : ft == typeof(int) ? (object)int.Parse(val, CultureInfo.InvariantCulture)
                    : (object)double.Parse(val, CultureInfo.InvariantCulture);
                if (idx >= 0) ((Array)f.GetValue(m)).SetValue(v, idx);
                else f.SetValue(m, v);
            }
        }

        public static ClassId[] Classes()
        {
            string s = Environment.GetEnvironmentVariable("CM_MVI_CLASSES");
            if (string.IsNullOrWhiteSpace(s)) return MvDefenceRig.All;
            return s.Split(',').Select(x => (ClassId)Enum.Parse(typeof(ClassId), x.Trim())).ToArray();
        }

        public static string[] Variants()
        {
            string s = Environment.GetEnvironmentVariable("CM_MVI_VARIANTS");
            return string.IsNullOrWhiteSpace(s) ? new[] { "base", "dodge", "guard", "strong" } : s.Split(',').Select(x => x.Trim()).ToArray();
        }

        public static string Knobs() => Environment.GetEnvironmentVariable("CM_MVI_KNOBS") ?? "";

        public static string Cells(Dictionary<ClassId, int> cells) => string.Join(" ", cells.Select(kv => kv.Key.ToString().Substring(0, 3) + kv.Value));
    }

    [TestFixture, Category("PlayerSeat")]
    public class MovesCombineTests
    {
        /// <summary>
        /// The 40-duel table (5 opponents x 8 starts, 30 s, Normal bots) per class and variant with every switch on: base (walk in
        /// and swing), dodge (a side dodge on each read wind-up), guard (dodge, and a held guard when the dodge cools down), strong
        /// (the S48 strong model). Rulings asserted: Warrior at least 36 of 40 and 6 of 8 per opponent in every variant; Healer at
        /// most 8 of 40 in every variant. The all-off base row is printed beside each class.
        /// </summary>
        [Test, Category("Slow"), Timeout(6000000)]
        public void Combine_DuelTable_EverySwitchOn()
        {
            var bad = new List<string>();
            foreach (ClassId me in MvCombine.Classes())
            {
                if (Environment.GetEnvironmentVariable("CM_MVI_OFF") == "1")
                {
                    var offCells = new Dictionary<ClassId, int>();
                    MvDefenceTally off = MvDefenceRig.DuelRow(me, "base", MvCombine.AllOff, perCell: offCells);
                    PxLog.Line("mvi-duel", me + " base, every switch OFF", off.Wins + "/40 (" + MvCombine.Cells(offCells) + ")", "reference", true);
                }
                foreach (string v in MvCombine.Variants())
                {
                    var cells = new Dictionary<ClassId, int>();
                    MvDefenceTally on = MvDefenceRig.DuelRow(me, v, MvCombine.AllOn, perCell: cells);
                    bool ok = true;
                    string rule = "report";
                    if (me == ClassId.Warrior) { ok = on.Wins >= 36 && cells.Values.Min() >= 6; rule = ">= 36 and >= 6 of 8 each"; }
                    else if (me == ClassId.Healer) { ok = on.Wins <= 8; rule = "<= 8"; }
                    if (!PxLog.Line("mvi-duel", me + " " + v + ", every switch ON" + (MvCombine.Knobs().Length > 0 ? " [" + MvCombine.Knobs() + "]" : ""),
                        on.Wins + "/40 (" + MvCombine.Cells(cells) + ")", rule, ok)) bad.Add(me + " " + v + " " + on.Wins);
                }
            }
            Assert.IsEmpty(bad, "rulings missed: " + string.Join("; ", bad));
        }

        /// <summary>
        /// REACT (the audit's reactive defence) with every switch on: the human stands, a Normal bot fights it 30 s; a side dodge on
        /// each read wind-up in reach. Answered share by the dodge alone (target at least 0.30 every class), and the guard against a
        /// dodge spammer being unhittable: the land rate of the swings in reach it could NOT answer (dodge cooling down) and of all.
        /// </summary>
        [Test, Category("Slow"), Timeout(3000000)]
        public void Combine_React_DodgeAloneAnswersAThirdAndASpammerIsStillHittable()
        {
            var bad = new List<string>();
            foreach (ClassId me in MvCombine.Classes())
            {
                var on = new MvDefenceTally();
                var off = new MvDefenceTally();
                foreach (ClassId them in MvDefenceRig.All)
                {
                    if (them == me) continue;
                    on.Add(MvDefenceRig.React(me, them, "side", MvCombine.AllOn));
                    off.Add(MvDefenceRig.React(me, them, "side", MvCombine.AllOff));
                }
                double Sh(MvDefenceTally t) => t.Answered + t.Unanswered > 0 ? (double)t.Answered / (t.Answered + t.Unanswered) : 0;
                double CdHit(MvDefenceTally t) => t.Unanswered > 0 ? (double)t.UnansweredLanded / t.Unanswered : 0;
                double AllHit(MvDefenceTally t) => t.Answered + t.Unanswered > 0 ? (double)(t.AnsweredLanded + t.UnansweredLanded) / (t.Answered + t.Unanswered) : 0;
                bool ok = Sh(on) >= 0.30 - 1e-9;
                if (!PxLog.Line("mvi-react", me + " side dodge alone, every switch ON" + (MvCombine.Knobs().Length > 0 ? " [" + MvCombine.Knobs() + "]" : ""),
                    "answered " + PxLog.F(Sh(on)) + " (off " + PxLog.F(Sh(off)) + "); hit rate while the dodge cools " + PxLog.F(CdHit(on)) + " of " + on.Unanswered
                    + " (off " + PxLog.F(CdHit(off)) + "); hit rate of every swing in reach " + PxLog.F(AllHit(on)) + " (off " + PxLog.F(AllHit(off)) + ")",
                    "answered >= 0.30", ok)) bad.Add(me + " " + PxLog.F(Sh(on)));
            }
            Assert.IsEmpty(bad, "answered share under 0.30: " + string.Join("; ", bad));
        }

        /// <summary>
        /// The escape band with every switch on: me at 30% runs from a lag-6 HUMAN Farmer chaser who dodges in (lunges), one dodge
        /// per read wind-up. The runner's eye varies 5 to 8 ticks per start (humans vary; asserted band 6 to 13 of 16); the fixed
        /// 6-tick eye is printed beside it.
        /// </summary>
        [Test, Category("Slow"), Timeout(3000000)]
        public void Combine_Escape_ALungingChaserLeavesAChanceEitherWay()
        {
            var bad = new List<string>();
            foreach (ClassId me in MvCombine.Classes())
            {
                if (me == ClassId.Farmer || me == ClassId.Healer) continue;   // the audit's rows (the Farmer is the chaser; the Healer glides)
                MvDefenceTally varied = MvDefenceRig.Escape(me, ClassId.Farmer, true, MvCombine.AllOn, spreadLag: true);
                MvDefenceTally fixed6 = MvDefenceRig.Escape(me, ClassId.Farmer, true, MvCombine.AllOn);
                bool ok = varied.Escaped >= 6 && varied.Escaped <= 13;
                if (!PxLog.Line("mvi-escape", me + " at 30% from a lunging lag-6 human Farmer, every switch ON" + (MvCombine.Knobs().Length > 0 ? " [" + MvCombine.Knobs() + "]" : ""),
                    "varied eye 5-8: " + varied.Escaped + "/16 | fixed 6: " + fixed6.Escaped + "/16 (lunges " + varied.Uses + ")", "varied 6..13", ok)) bad.Add(me + " " + varied.Escaped);
            }
            Assert.IsEmpty(bad, "escape band missed: " + string.Join("; ", bad));
        }

        /// <summary>Chase a fleeing 29% Normal bot (S11 staging) with every switch on: plain and dodge-in (lunge), per class, with
        /// the all-off rows beside. Target: dodge-in at least plain + 0.10 (the Miner's lunge chase in particular).</summary>
        [Test, Category("Slow"), Timeout(3000000)]
        public void Combine_Chase_TheDodgeInLungeCatchesMore()
        {
            var bad = new List<string>();
            foreach (ClassId me in MvCombine.Classes())
            {
                MvDefenceTally plain = MvDefenceRig.ChaseRow(me, "plain", MvCombine.AllOn);
                MvDefenceTally lunge = MvDefenceRig.ChaseRow(me, "dodgein", MvCombine.AllOn);
                MvDefenceTally offLunge = MvDefenceRig.ChaseRow(me, "dodgein", MvCombine.AllOff);
                bool ok = lunge.Share >= plain.Share + 0.10 - 1e-9;
                if (!PxLog.Line("mvi-chase", me + " catch within 4 s, every switch ON" + (MvCombine.Knobs().Length > 0 ? " [" + MvCombine.Knobs() + "]" : ""),
                    "plain " + PxLog.F(plain.Share) + ", dodge-in " + PxLog.F(lunge.Share) + " (off dodge-in " + PxLog.F(offLunge.Share) + ")", "dodge-in >= plain + 0.10", ok)) bad.Add(me.ToString());
            }
            Assert.IsEmpty(bad, "lunge chase under plain + 0.10: " + string.Join("; ", bad));
        }
    }
}
