using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// px S1 [9] gate S50: critical strikes are earned, never rolled. Each kind (1 counter: the victim is in his own wind-up,
    /// 2 punish: he is in the recovery of a WHIFFED swing, 3 back hit: the striker stands in his rear 120 degrees) fires exactly
    /// when staged and never otherwise; the bonus is exactly CritDamageMul; the strike's Hit event reads Tier 2 and one
    /// CriticalHit event follows (A victim, B striker, Value kind, Radius bonus dealt).
    /// </summary>
    [TestFixture]
    public sealed class PxCritTests
    {
        private static World Duel(ClassId atk, ClassId def)
        {
            World w = CombatFeelTests.Arena(atk, def);
            w.Config.Depth.CombatFeel.CritEnabled = true; // off by default until the skill-ceiling numbers hold; the rule is tested on
            return w;
        }

        private static double MulWithAndWithout(World w, int src, int tgt)
        {
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            double d = Math.Sqrt((w.X[src] - w.X[tgt]) * (w.X[src] - w.X[tgt]) + (w.Z[src] - w.Z[tgt]) * (w.Z[src] - w.Z[tgt]));
            f.CritEnabled = false;
            double off = CombatFeel.DamageMul(w, src, tgt, d);
            f.CritEnabled = true;
            double on = CombatFeel.DamageMul(w, src, tgt, d);
            return on / off;
        }

        [Test]
        public void Crit_CounterPunishBack_AreDeterministic()
        {
            // Neutral: facing the striker, no swing, no whiff: never critical, multiplier untouched.
            World w = Duel(ClassId.Warrior, ClassId.Farmer);
            int e = CombatFeelTests.Foe(w);
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(0), "neutral");
            Assert.That(MulWithAndWithout(w, 0, e), Is.EqualTo(1).Within(1e-12));

            // 1 COUNTER: inside his own wind-up (also through the Warrior's armoured wind-up).
            w.Actors[e].SwingPending = true;
            w.Actors[e].SwingTick = w.Tick;
            w.Actors[e].SwingWindup = 12;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(1), "counter");
            Assert.That(MulWithAndWithout(w, 0, e), Is.EqualTo(f.CritDamageMul).Within(1e-12), "counter bonus");
            World wa = Duel(ClassId.Farmer, ClassId.Warrior);
            int ea = CombatFeelTests.Foe(wa);
            wa.Actors[ea].SwingPending = true;
            wa.Actors[ea].SwingTick = wa.Tick;
            wa.Actors[ea].SwingWindup = 12;
            Assert.That(CombatFeel.CritKindOf(wa, 0, ea), Is.EqualTo(1), "counter on an armoured Warrior");
            w.Actors[e].SwingPending = false;

            // 2 PUNISH: the recovery of a whiff, and only of a whiff.
            w.Actors[e].SwingRecovery = 10;
            w.Actors[e].SwingTick = w.Tick - 12;
            CombatFeel.AfterContact(w, e, 1); // a landed swing: no punish window
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(0), "recovery after a landed swing");
            w.Actors[e].SwingRecovery = 10;
            CombatFeel.AfterContact(w, e, 0); // a whiff
            int until = w.Depth.Feel.WhiffRecoverUntil[e];
            Assert.That(until, Is.GreaterThan(w.Tick));
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(2), "punish");
            Assert.That(MulWithAndWithout(w, 0, e), Is.EqualTo(f.CritDamageMul).Within(1e-12), "punish bonus");
            int t0 = w.Tick;
            w.Tick = until + 1;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(0), "after the whiff recovery");
            w.Tick = t0;
            w.Depth.Feel.WhiffRecoverUntil[e] = CombatFeel.NoTick;

            // 3 BACK: the striker is at -x of the victim; the way to him is angle PI.
            w.Actors[e].Facing = 0;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(3), "back, straight behind");
            w.Actors[e].Facing = Math.PI - 125 * Math.PI / 180;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(3), "back, 125 deg off his facing");
            w.Actors[e].Facing = Math.PI - 115 * Math.PI / 180;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(0), "side, 115 deg off his facing");
            w.Actors[e].Facing = Math.PI / 2;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(0), "side, 90 deg");

            // Off switch: nothing is critical.
            w.Actors[e].Facing = 0;
            f.CritEnabled = false;
            Assert.That(CombatFeel.CritKindOf(w, 0, e), Is.EqualTo(0), "CritEnabled false");
            f.CritEnabled = true;

            // The landed strike: Hit Tier 2 + one CriticalHit, the same in two fresh worlds.
            string a = Strike(), b = Strike();
            Assert.That(a, Is.EqualTo(b), "deterministic");
            StringAssert.Contains("crit kind 3", a);
        }

        private static string Strike()
        {
            World w = Duel(ClassId.Warrior, ClassId.Farmer);
            int e = CombatFeelTests.Foe(w);
            w.Actors[e].Facing = 0; // back to the striker
            double d = w.X[e] - w.X[0];
            double amt = 10 * CombatFeel.DamageMul(w, 0, e, d);
            int head0 = w.EventHead;
            double dealt = CombatFeel.ApplyStrike(w, e, amt, 0, w.X[0], w.Z[0]);
            int crits = 0, tier2 = 0;
            string s = "";
            for (int k = head0; k < w.EventHead; k++)
            {
                SimEvent ev = w.Events[k % World.EventCapacity];
                if (ev.Type == SimEventType.Hit && ev.A == e && ev.Tier == 2) tier2++;
                if (ev.Type != SimEventType.CriticalHit) continue;
                crits++;
                Assert.That(ev.A, Is.EqualTo(e));
                Assert.That(ev.B, Is.EqualTo(0));
                Assert.That(ev.Radius, Is.EqualTo(dealt - dealt / w.Config.Depth.CombatFeel.CritDamageMul).Within(1e-9), "bonus dealt");
                s += "crit kind " + (int)ev.Value + " bonus " + ev.Radius.ToString("R") + ";";
            }
            Assert.That(crits, Is.EqualTo(1), "one CriticalHit");
            Assert.That(tier2, Is.EqualTo(1), "the Hit reads Tier 2");
            return s + " hp " + w.Actors[e].Hp.ToString("R");
        }
    }
}
