using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Net;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b12-r (RULE CHANGE, Travis's request): the plant / meat mix both teams fed before the hatch picks the Kaiju's type
    /// (KaijuConfig.Types): 65 % or more plant = GROVE, 65 % or more meat = BRUTE, else STANDARD. Full profile only. Every case
    /// prints one plain line: what was measured, the value, the limit. The balance check (Slow) runs the Full profile with the
    /// rule on against a same-seed control with it off and fails past the cut limits, so the integrator can revert R alone.
    /// </summary>
    [TestFixture]
    public sealed class KaijuTypeTests
    {
        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static void Print(string what, string value, string limit) =>
            Console.WriteLine("[KaijuType] " + what + ": " + value + " (limit: " + limit + ")");

        /// <summary>The 5v5 Full config (both rule lanes on, the Kaiju-type rule on by default).</summary>
        private static GameConfig Full() => DepthKit.Cfg();

        // ------------------------------------------------------------------ the pure picker

        [Test]
        public void ThePickerSplitsAtSixtyFivePercent()
        {
            KaijuTypesConfig t = new KaijuTypesConfig();
            var cases = new (double plant, double meat, KaijuType want)[]
            {
                (64.9, 35.1, KaijuType.Standard),
                (65.0, 35.0, KaijuType.Grove),
                (35.0, 65.0, KaijuType.Brute),
                (35.1, 64.9, KaijuType.Standard),
                (50, 50, KaijuType.Standard),
                (80, 20, KaijuType.Grove),
                (20, 80, KaijuType.Brute),
                (0, 0, KaijuType.Standard),
                (7, 0, KaijuType.Grove),
                (0, 3, KaijuType.Brute),
                (double.NaN, -4, KaijuType.Standard),
            };
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                KaijuType got = Kaiju.PickType(c.plant, c.meat, t);
                Print("picker plant " + F(c.plant) + " meat " + F(c.meat) + " (plant share " + F(100 * Kaiju.PlantShare(c.plant > 0 ? c.plant : 0, c.meat > 0 ? c.meat : 0)) + " %)",
                    got.ToString(), "expected " + c.want + ", split at " + F(100 * t.MixShare) + " %");
                if (got != c.want) wrong.Add(c.plant + "/" + c.meat + " gave " + got + ", expected " + c.want);
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        // ------------------------------------------------------------------ staged hatches

        /// <summary>A 5v5 world on cfg with both teams fed the same plant and meat (a tie: the hatch is NEUTRAL), force-hatched.</summary>
        private static World StagedHatch(GameConfig cfg, double plantPerTeam, double meatPerTeam, out double baseMaxHp)
        {
            World w = DepthKit.Make(cfg, ClassId.Warrior, seed: 7);
            baseMaxHp = w.Kaiju.MaxHp;
            foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
            {
                if (plantPerTeam > 0) SimResources.ApplyDelivery(w, team, CarryKind.Biomass, plantPerTeam, -1, BiomassType.Plant);
                if (meatPerTeam > 0) SimResources.ApplyDelivery(w, team, CarryKind.Biomass, meatPerTeam, -1, BiomassType.Meat);
            }
            w.ForceHatch = true;
            SimStep.Step(w);
            Assert.IsTrue(w.Match.Hatched, "the forced hatch ran");
            return w;
        }

        [Test]
        public void AStagedHatchGivesTheTypeAndItsNumbers()
        {
            KaijuConfig kc = Full().Kaiju;
            var cases = new (string name, double plant, double meat, KaijuType want, double hpScale, double speed, double claw, double slam)[]
            {
                ("80 % plant", 4, 1, KaijuType.Grove, 1.25, kc.MoveSpeed * 0.90, kc.MeleeDamage, kc.SlamDamage),
                ("80 % meat", 1, 4, KaijuType.Brute, 0.90, kc.MoveSpeed * 1.10, kc.MeleeDamage * 1.20, kc.SlamDamage * 1.20),
                ("50/50", 2.5, 2.5, KaijuType.Standard, 1.00, kc.MoveSpeed, kc.MeleeDamage, kc.SlamDamage),
                ("nothing fed", 0, 0, KaijuType.Standard, 1.00, kc.MoveSpeed, kc.MeleeDamage, kc.SlamDamage),
            };
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                World w = StagedHatch(Full(), c.plant, c.meat, out double baseHp);
                Kaiju k = w.Kaiju;
                double speed = Kaiju.TypeMoveSpeed(w), claw = Kaiju.TypeDamage(w, kc.MeleeDamage), slam = Kaiju.TypeDamage(w, kc.SlamDamage);
                Print("staged hatch " + c.name + " (plant " + F(k.MixPlant) + ", meat " + F(k.MixMeat) + ")",
                    "type " + k.Type + ", state " + k.State + ", max HP " + F(k.MaxHp) + " of base " + F(baseHp) + ", hp " + F(k.Hp) +
                    ", speed " + F(speed) + ", claw " + F(claw) + ", slam " + F(slam),
                    "type " + c.want + ", max HP " + F(baseHp * c.hpScale) + ", speed " + F(c.speed) + ", claw " + F(c.claw) + ", slam " + F(c.slam));
                if (k.Type != c.want) wrong.Add(c.name + ": type " + k.Type);
                if (Math.Abs(k.MaxHp - baseHp * c.hpScale) > 1e-6 || Math.Abs(k.Hp - k.MaxHp) > 1e-9) wrong.Add(c.name + ": max HP " + k.MaxHp + " hp " + k.Hp);
                if (Math.Abs(speed - c.speed) > 1e-9) wrong.Add(c.name + ": speed " + speed);
                if (Math.Abs(claw - c.claw) > 1e-9 || Math.Abs(slam - c.slam) > 1e-9) wrong.Add(c.name + ": damage " + claw + " / " + slam);
            }
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        [Test]
        public void GroveRegrowsOnePercentASecondOnlyFiveSecondsAfterTheLastHit()
        {
            World w = StagedHatch(Full(), 4, 1, out _);
            Kaiju k = w.Kaiju;
            Assert.AreEqual(KaijuType.Grove, k.Type);
            double hz = w.Config.SimHz;
            int src = 1;
            w.Actors[src].SpawnProtect = 0;
            double dealt = Kaiju.DamageKaiju(w, src, k.MaxHp * 0.5);
            Assert.Greater(dealt, 0, "the staged hit landed");
            // Nobody else can touch it: every actor benched for the rest of the test.
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                a.Alive = false;
                a.Hp = 0;
                a.RespawnIn = 1e9;
                a.Moving = false;
            }
            double hit = k.Hp;
            DepthKit.Steps(w, (int)(4.5 * hz));
            double at45 = k.Hp;
            DepthKit.Steps(w, (int)(0.5 * hz) + 2);
            double at5 = k.Hp;
            DepthKit.Steps(w, (int)(10 * hz));
            double at15 = k.Hp;
            double perSecond = (at15 - at5) / 10.0;
            double want = k.MaxHp * w.Config.Kaiju.Types.GroveRegenSharePerSecond;
            Print("GROVE hp after the hit", F(hit) + " of " + F(k.MaxHp), "half");
            Print("GROVE hp 4.5 s after the hit", F(at45), "unchanged " + F(hit));
            Print("GROVE regrowth from 5 s to 15 s after the hit", F(perSecond) + " hp/s", F(want) + " hp/s (1 % of max HP) within 1 %");
            Assert.AreEqual(hit, at45, 1e-9, "no regrowth inside the 5 s");
            Assert.AreEqual(want, perSecond, want * 0.01, "1 % of max HP per second once not hit for 5 s");

            // A STANDARD Kaiju never regrows.
            World s = StagedHatch(Full(), 2.5, 2.5, out _);
            Assert.AreEqual(KaijuType.Standard, s.Kaiju.Type);
            s.Actors[src].SpawnProtect = 0;
            Kaiju.DamageKaiju(s, src, s.Kaiju.MaxHp * 0.5);
            for (int id = 0; id < s.ActorCount; id++) { s.Actors[id].Alive = false; s.Actors[id].RespawnIn = 1e9; }
            double sHit = s.Kaiju.Hp;
            DepthKit.Steps(s, (int)(15 * hz));
            Print("STANDARD hp 15 s after the hit", F(s.Kaiju.Hp), "unchanged " + F(sHit));
            Assert.AreEqual(sHit, s.Kaiju.Hp, 1e-9, "STANDARD does not regrow");
        }

        [Test]
        public void ClassicAndPhysicsOnlyAlwaysHatchStandard()
        {
            var wrong = new List<string>();
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                foreach (bool forceFlag in new[] { false, true })
                {
                    foreach (var mix in new[] { (4.0, 1.0), (1.0, 4.0) })
                    {
                        GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(p);
                        if (forceFlag) cfg.Kaiju.Types.On = true;   // the flag alone never turns it on outside Full
                        World w = StagedHatch(cfg, mix.Item1, mix.Item2, out double baseHp);
                        Print(p + " (flag " + (cfg.Kaiju.Types.On ? "on" : "off") + ") plant " + F(mix.Item1 * 2) + " meat " + F(mix.Item2 * 2),
                            "type " + w.Kaiju.Type + ", max HP " + F(w.Kaiju.MaxHp) + ", speed " + F(Kaiju.TypeMoveSpeed(w)),
                            "STANDARD, max HP " + F(baseHp) + ", speed " + F(cfg.Kaiju.MoveSpeed));
                        if (w.Kaiju.Type != KaijuType.Standard || w.Kaiju.MaxHp != baseHp) wrong.Add(p + " gave " + w.Kaiju.Type);
                    }
                }
            }
            Assert.IsFalse(GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.PhysicsOnly).Kaiju.Types.On, "ApplyRules switches the flag off outside Full");
            Assert.IsTrue(GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full).Kaiju.Types.On, "and on in Full");
            Assert.IsTrue(Kaiju.TypesOn(GameConfig.CreateDefault()), "the game's default (Full) has the rule on");
            GameConfig off = Full();
            off.Kaiju.Types.On = false;
            World c = StagedHatch(off, 4, 1, out _);
            Print("Full with the rule off (the control arm), 80 % plant", "type " + c.Kaiju.Type, "STANDARD");
            if (c.Kaiju.Type != KaijuType.Standard) wrong.Add("Full rule off gave " + c.Kaiju.Type);
            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        [Test]
        public void TheConfigCloneCopiesTheTypeTableAndTheHashSeesIt()
        {
            GameConfig a = Full();
            GameConfig b = a.Clone();
            Assert.AreNotSame(a.Kaiju.Types, b.Kaiju.Types, "deep clone of the nested table");
            b.Kaiju.Types.GroveHpScale = 1.3;
            Print("clone edit leaves the original", F(a.Kaiju.Types.GroveHpScale), "1.25");
            Assert.AreEqual(1.25, a.Kaiju.Types.GroveHpScale);
            ulong ha = ConfigHash.Of(a), hb = ConfigHash.Of(b);
            Print("config hash before / after a type-table edit", "0x" + ha.ToString("X16") + " / 0x" + hb.ToString("X16"), "different");
            Assert.AreNotEqual(ha, hb, "ConfigHash picks the table up by reflection");
        }

        [Test]
        public void ANewMatchStartsWithNoTypeAndTheStateHashIsUnchangedWithoutOne()
        {
            World w = StagedHatch(Full(), 4, 1, out double baseHp);
            Assert.AreEqual(KaijuType.Grove, w.Kaiju.Type);
            ulong grove = StateHash.Of(w);
            w.Kaiju.Type = KaijuType.Standard;
            ulong plain = StateHash.Of(w);
            w.Kaiju.Type = KaijuType.Grove;
            Print("state hash GROVE / same world as STANDARD", "0x" + grove.ToString("X16") + " / 0x" + plain.ToString("X16"), "different");
            Assert.AreNotEqual(grove, plain, "the hash sees the type");
            World.ResetWorld(w, new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = w.Config });
            SimStep.Step(w);
            Print("after a reset and one step", "type " + w.Kaiju.Type + ", max HP " + F(w.Kaiju.MaxHp) + ", hatched " + w.Match.Hatched,
                "STANDARD, max HP " + F(baseHp) + ", not hatched");
            Assert.AreEqual(KaijuType.Standard, w.Kaiju.Type);
            Assert.AreEqual(baseHp, w.Kaiju.MaxHp, 1e-9);
            // The next hatch picks again from the new match's deliveries (meat this time).
            SimResources.ApplyDelivery(w, TeamId.Azure, CarryKind.Biomass, 4, -1, BiomassType.Meat);
            SimResources.ApplyDelivery(w, TeamId.Crimson, CarryKind.Biomass, 1, -1, BiomassType.Plant);
            w.ForceHatch = true;
            SimStep.Step(w);
            Print("the next match's hatch, 80 % meat", "type " + w.Kaiju.Type + ", max HP " + F(w.Kaiju.MaxHp), "BRUTE, max HP " + F(baseHp * 0.9));
            Assert.AreEqual(KaijuType.Brute, w.Kaiju.Type);
            Assert.AreEqual(baseHp * 0.9, w.Kaiju.MaxHp, 1e-6);
        }

        // ------------------------------------------------------------------ whole matches

        private sealed class Arm
        {
            public KaijuType Type;
            public double PlantShare;
            public MatchResult Winner;
            public int HatchTick = -1;
            public KaijuState HatchState;
            public int Kos;
            public int FirstKoTick = -1;
            public int EndTick;
            public bool BerserkLocked;
            public ulong Hash;
            public double Hz;

            /// <summary>Seconds from the hatch to the first knock-out; with none, the seconds the Kaiju was up (a lower bound).</summary>
            public double TimeToFirstKo => HatchTick < 0 ? 0 : ((FirstKoTick >= 0 ? FirstKoTick : EndTick) - HatchTick) / Hz;

            public string Line(string arm, int seed, int size) =>
                arm + " " + size + "v" + size + " seed " + seed + ": type " + Type + " (plant share " + F(100 * PlantShare) + " %), winner " + Winner +
                ", hatch tick " + HatchTick + " (" + HatchState + "), Kaiju KOs " + Kos + ", first KO " +
                (FirstKoTick >= 0 ? F(TimeToFirstKo) + " s after the hatch" : "none (up " + F(TimeToFirstKo) + " s)") + ", berserk lock " + (BerserkLocked ? "yes" : "no");
        }

        private static Arm RunMatch(int seed, int teamSize, bool typesOn, int stopAfterHatchTicks = -1)
        {
            GameConfig cfg = Full();
            cfg.Kaiju.Types.On = typesOn;
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int maxSteps = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
            var r = new Arm { Hz = cfg.SimHz };
            bool wasKo = false;
            for (int s = 0; s < maxSteps && w.Match.Phase != MatchPhase.Ended; s++)
            {
                MatchTick.Advance(w, null, scratch);
                if (r.HatchTick < 0 && w.Match.Hatched)
                {
                    r.HatchTick = w.Tick;
                    r.HatchState = w.Kaiju.State;
                    r.Type = w.Kaiju.Type;
                    r.PlantShare = Kaiju.PlantShare(w.Kaiju.MixPlant, w.Kaiju.MixMeat);   // snapshotted at the hatch with the rule on or off
                }
                bool ko = w.Kaiju.KoIn > 0;
                if (ko && !wasKo)
                {
                    r.Kos++;
                    if (r.FirstKoTick < 0) r.FirstKoTick = w.Tick;
                }
                wasKo = ko;
                if (stopAfterHatchTicks >= 0 && r.HatchTick >= 0 && w.Tick - r.HatchTick >= stopAfterHatchTicks) break;
            }
            r.EndTick = w.Tick;
            r.Winner = w.Match.Winner;
            r.BerserkLocked = w.Match.BerserkLocked;
            r.Hash = StateHash.Of(w);
            return r;
        }

        [Test]
        public void TheSameSeedTwiceGivesTheSameType()
        {
            Arm a = RunMatch(42, 5, true, 300);
            Arm b = RunMatch(42, 5, true, 300);
            Print("5v5 seed 42 run 1 / run 2", a.Type + " / " + b.Type + ", state hash 0x" + a.Hash.ToString("X16") + " / 0x" + b.Hash.ToString("X16"), "identical");
            Assert.AreNotEqual(-1, a.HatchTick, "the match hatched");
            Assert.AreEqual(a.Type, b.Type);
            Assert.AreEqual(a.PlantShare, b.PlantShare);
            Assert.AreEqual(a.Hash, b.Hash);
        }

        /// <summary>Win share limit (cut rule): the rule-on arm's Azure win share within this of the control's.</summary>
        public const double WinShareLimit = 0.15;
        /// <summary>Median time-to-first-KO limit (cut rule): within this share of the control's median.</summary>
        public const double FirstKoLimit = 0.25;

        private static double Median(List<double> v)
        {
            v.Sort();
            int n = v.Count;
            return n == 0 ? 0 : n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }

        /// <summary>
        /// BALANCE CHECK (cut rule): the Full profile with the rule on against a same-seed control with it off: 5v5 seeds 1 to
        /// 20 and 16v16 seeds 1337 and 4242. Per arm and match it prints the type, the winner, the hatch tick, Kaiju KOs, the
        /// time to the first KO and the berserk lock. Fails when the Azure win share differs by more than 0.15 or the median
        /// time to the first KO (no KO = the seconds the Kaiju was up, a lower bound) by more than 25 % of the control's.
        /// </summary>
        [Test, Category("Slow")]
        public void BalanceAgainstASameSeedControlStaysInsideTheCutLimits()
        {
            var runs = new List<(int seed, int size)>();
            for (int s = 1; s <= 20; s++) runs.Add((s, 5));
            runs.Add((1337, 16));
            runs.Add((4242, 16));
            int winsOn = 0, winsOff = 0, drawsOn = 0, drawsOff = 0, changed = 0;
            var koOn = new List<double>();
            var koOff = new List<double>();
            var types = new Dictionary<KaijuType, int> { { KaijuType.Standard, 0 }, { KaijuType.Grove, 0 }, { KaijuType.Brute, 0 } };
            foreach (var (seed, size) in runs)
            {
                Arm off = RunMatch(seed, size, false);
                Arm on = RunMatch(seed, size, true);
                Console.WriteLine("[KaijuType] " + off.Line("control (types off)", seed, size));
                Console.WriteLine("[KaijuType] " + on.Line("types on           ", seed, size));
                types[on.Type]++;
                if (on.Winner == MatchResult.Azure) winsOn++;
                else if (on.Winner != MatchResult.Crimson) drawsOn++;
                if (off.Winner == MatchResult.Azure) winsOff++;
                else if (off.Winner != MatchResult.Crimson) drawsOff++;
                if (on.Winner != off.Winner) changed++;
                koOn.Add(on.TimeToFirstKo);
                koOff.Add(off.TimeToFirstKo);
            }
            int n = runs.Count;
            double shareOn = (double)winsOn / n, shareOff = (double)winsOff / n;
            double medOn = Median(koOn), medOff = Median(koOff);
            Print("types picked with the rule on (" + n + " matches)", "STANDARD " + types[KaijuType.Standard] + ", GROVE " + types[KaijuType.Grove] + ", BRUTE " + types[KaijuType.Brute], "printed only");
            Print("winners that changed against the control", changed + " of " + n, "printed only");
            Print("Azure win share types on / control", F(shareOn) + " / " + F(shareOff) + " (draws " + drawsOn + " / " + drawsOff + ")", "within " + F(WinShareLimit));
            Print("median time to the first Kaiju KO types on / control", F(medOn) + " s / " + F(medOff) + " s", "within " + F(100 * FirstKoLimit) + " % (" + F(FirstKoLimit * medOff) + " s)");
            Assert.LessOrEqual(Math.Abs(shareOn - shareOff), WinShareLimit + 1e-9, "CUT R: the team win share moved past 0.15 of the control's");
            Assert.LessOrEqual(Math.Abs(medOn - medOff), FirstKoLimit * medOff + 1e-9, "CUT R: the median time to the first Kaiju KO moved past 25 % of the control's");
        }
    }
}
