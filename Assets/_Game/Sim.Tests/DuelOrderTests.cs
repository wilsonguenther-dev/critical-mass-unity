using System;
using System.Text;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b16 duel order (design advisor 2026-11-21): "fighter class is all about fighting ... they should be winning all 1v1
    /// fights based on equal skill"; "healer class should be killable by all other classes one on one"; "their speed should
    /// be a touch slower so the non fighter has a chance of escape". The 6x6 duel matrix of the balance report (DuelRunner,
    /// 8 start angles, both seatings) must hold the order; the scripted chase prints every class's escape rate from a
    /// Warrior at the shipped speed and at the b16 speed.
    /// </summary>
    [TestFixture]
    public sealed class DuelOrderTests
    {
        static string Matrix(DuelRunner.Cell[,] d)
        {
            var sb = new StringBuilder("row beats column (wins of 16, both seatings):\n        ");
            for (int b = 0; b < 6; b++) sb.Append(((ClassId)b).ToString().Substring(0, 3)).Append("  ");
            sb.Append('\n');
            for (int a = 0; a < 6; a++)
            {
                sb.Append(((ClassId)a).ToString().PadRight(8));
                for (int b = 0; b < 6; b++)
                {
                    if (a == b) { sb.Append(" --  "); continue; }
                    int wins = d[a, b].Wins + d[b, a].Losses, tries = d[a, b].Reps + d[b, a].Reps;
                    sb.Append((wins + "/" + tries).PadRight(5));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void TheWarriorWinsEveryDuelTheHealerLosesEveryDuelAndTheMiddleFourHaveNoKing()
        {
            DuelRunner.Cell[,] d = DuelRunner.Duels(RulesProfile.Full, null, 8);
            int v = DuelRunner.OrderViolations(d, out string why);
            Console.WriteLine(Matrix(d));
            Console.WriteLine("{\"what\":\"b16 duel order\",\"violations\":" + v + ",\"why\":\"" + why + "\",\"limit\":0}");
            Assert.AreEqual(0, v, why);
        }

        [Test]
        public void ANonFighterAtFullHealthGetsAwayFromAWarriorMoreOftenThanAtTheOldSpeed()
        {
            const int reps = 8;
            DuelRunner.Cell[] now = DuelRunner.Escape(RulesProfile.Full, null, reps, false);
            DuelRunner.Cell[] old = DuelRunner.Escape(RulesProfile.Full, new[] { "Depth.CombatFeel.Classes[Warrior].MoveSpeed=3.8" }, reps, false);
            DuelRunner.Cell[] dodge = DuelRunner.Escape(RulesProfile.Full, null, reps, true);
            int sumNow = 0, sumOld = 0, sumDodge = 0, n = 0;
            var sb = new StringBuilder();
            for (int c = 0; c < 6; c++)
            {
                if (c == (int)ClassId.Warrior) continue;
                sumNow += now[c].Wins;
                sumOld += old[c].Wins;
                sumDodge += dodge[c].Wins;
                n += reps;
                sb.Append((ClassId)c).Append(' ').Append(old[c].Wins).Append("->").Append(now[c].Wins).Append(" (dodging ")
                    .Append(dodge[c].Wins).Append(")/").Append(reps).Append("; ");
            }
            Console.WriteLine("{\"what\":\"b16 escape from a Warrior, 4 u start, 20 u goal\",\"old\":" + sumOld + ",\"now\":" + sumNow +
                ",\"nowDodging\":" + sumDodge + ",\"of\":" + n + ",\"perClass\":\"" + sb + "\"}");
            Assert.GreaterOrEqual(sumNow, sumOld, "a slower Warrior never catches more runners");
            Assert.Less(w0Speed(), 3.8, "the Warrior is a touch slower than in b15");
        }

        static double w0Speed() => BalanceRunner.Config(RulesProfile.Full, null).Depth.CombatFeel.Classes[(int)ClassId.Warrior].MoveSpeed;
    }
}
