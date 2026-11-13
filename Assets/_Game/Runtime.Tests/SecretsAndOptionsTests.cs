using System;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Secrets;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-26 secrets (one scripted check each; the title egg and credits gag check their counters and switches) and
    /// DG-30 options (defaults, persistence, the flash limit, and the colour-blind check on Azure vs Crimson).
    /// </summary>
    public sealed class SecretsAndOptionsTests
    {
        static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        // ---------------------------------------------------------------- DG-26

        [Test]
        public void TheJumpSecretIsOffUntilTheSimCanJump()
        {
            JumpGate.Override = null;
            bool hasJump = typeof(Actor).GetField("AirLen") != null || typeof(Actor).GetField("AirTick") != null;
            Assert.AreEqual(hasJump, JumpGate.Available, "follows the sim's airborne fields");
            Assert.AreEqual(hasJump, SecretsBoard.IsActive(SecretsBoard.Crown));
            JumpGate.Override = true;
            Assert.IsTrue(SecretsBoard.IsActive(SecretsBoard.Crown));
            JumpGate.Override = null;
        }

        [Test]
        public void TheTitleEggHatchesOnTheTenthHitOnce()
        {
            var c = new TitleEggCounter();
            for (int i = 1; i < TitleEggCounter.HitsToHatch; i++) Assert.IsFalse(c.Hit(), "hit " + i);
            Assert.IsTrue(c.Hit(), "hit 10 hatches");
            Assert.IsFalse(c.Hit(), "only once");
            Assert.IsFalse(SecretsBoard.IsActive(SecretsBoard.TitleEgg), "off until the title hands over its egg");
            Assert.IsFalse(SecretsBoard.IsActive(SecretsBoard.CreditsGag), "off until the credits place it");
            Assert.IsTrue(SecretsBoard.IsActive(SecretsBoard.HillAlone));
            Assert.IsTrue(SecretsBoard.IsActive(SecretsBoard.ExactTie));
            Assert.IsNotEmpty(CreditsGag.Line);
        }

        [Test]
        public void HoldingTheHillAloneForSixtySecondsFiresOnce()
        {
            World w = NewWorld();
            for (int i = 0; i < w.ActorCount; i++)
            {
                // Everyone but actor 0 far from the hill (the pads), actor 0 on the hill centre.
                w.X[i] = w.Actors[i].Team == TeamId.Azure ? 8 : 104;
                w.Z[i] = 36;
            }
            w.X[0] = w.Map.Hill.X;
            w.Z[0] = w.Map.Hill.Z;
            var t = new HillAloneTracker();
            int fired = 0;
            double firedAt = -1;
            for (int step = 0; step <= 70 * 30; step++)
            {
                w.Match.Time = step / 30.0;
                if (t.Step(w)) { fired++; firedAt = w.Match.Time; }
            }
            Assert.AreEqual(1, fired);
            Assert.AreEqual(60.0, firedAt, 1.0 / 30 + 1e-9);

            // An ally stepping on resets the clock.
            var t2 = new HillAloneTracker();
            for (int step = 0; step <= 50 * 30; step++) { w.Match.Time = 100 + step / 30.0; Assert.IsFalse(t2.Step(w)); }
            int ally = -1;
            for (int i = 1; i < w.ActorCount; i++) if (w.Actors[i].Team == TeamId.Azure) { ally = i; break; }
            w.X[ally] = w.Map.Hill.X + 1;
            w.Z[ally] = w.Map.Hill.Z;
            w.Match.Time += 1;
            Assert.IsFalse(t2.Step(w));
            Assert.AreEqual(0.0, t2.Held, "not alone: the clock resets");
        }

        [Test]
        public void AnExactTieAtTheHatchIsTheShrugAndNothingElseIs()
        {
            var m = new MatchState { Allegiance = new PerTeam { Azure = 40, Crimson = 40 } };
            Assert.IsTrue(SecretRules.IsExactTie(0, m));
            m.Allegiance.Crimson = 41;
            Assert.IsFalse(SecretRules.IsExactTie(0, m));
            m.Allegiance.Crimson = 40;
            Assert.IsFalse(SecretRules.IsExactTie(1, m), "Azure hatch");
            m.BerserkLocked = true;
            Assert.IsFalse(SecretRules.IsExactTie(0, m), "berserk is not a tie");
        }

        // ---------------------------------------------------------------- DG-30

        string[] _keys;
        int[] _ints;
        bool[] _had;

        [SetUp]
        public void SaveKeys()
        {
            _keys = new string[SettingsExtras.AllKeys.Length + 1];
            Array.Copy(SettingsExtras.AllKeys, _keys, SettingsExtras.AllKeys.Length);
            _keys[_keys.Length - 1] = GameSettings.ShakeKey;
            _ints = new int[_keys.Length];
            _had = new bool[_keys.Length];
            for (int i = 0; i < _keys.Length; i++)
            {
                _had[i] = PlayerPrefs.HasKey(_keys[i]);
                _ints[i] = PlayerPrefs.GetInt(_keys[i], 0);
            }
        }

        [TearDown]
        public void RestoreKeys()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                if (_had[i]) PlayerPrefs.SetInt(_keys[i], _ints[i]);
                else PlayerPrefs.DeleteKey(_keys[i]);
            }
            PlayerPrefs.Save();
            JumpGate.Override = null;
        }

        [Test]
        public void OptionsHaveTheirDefaultsAndPersist()
        {
            SettingsExtras.ResetAll();
            Assert.IsFalse(SettingsExtras.HideNames);
            Assert.IsFalse(SettingsExtras.CleanHud);
            Assert.IsTrue(SettingsExtras.Subtitles);
            Assert.AreEqual(100, SettingsExtras.UiScalePercent);
            Assert.IsFalse(SettingsExtras.ReduceFlashing);
            Assert.AreEqual(0, SettingsExtras.ShakeAmount, "camera shake defaults to 0");

            SettingsExtras.HideNames = true;
            SettingsExtras.CleanHud = true;
            SettingsExtras.Subtitles = false;
            SettingsExtras.UiScalePercent = 130;
            SettingsExtras.ReduceFlashing = true;
            SettingsExtras.ShakeAmount = 35;
            Assert.AreEqual(1, PlayerPrefs.GetInt(SettingsExtras.HideNamesKey));
            Assert.AreEqual(1, PlayerPrefs.GetInt(SettingsExtras.CleanHudKey));
            Assert.AreEqual(0, PlayerPrefs.GetInt(SettingsExtras.SubtitlesKey));
            Assert.AreEqual(130, PlayerPrefs.GetInt(SettingsExtras.UiScaleKey));
            Assert.AreEqual(1, PlayerPrefs.GetInt(SettingsExtras.ReduceFlashingKey));
            Assert.AreEqual(35, PlayerPrefs.GetInt(SettingsExtras.ShakeAmountKey));
            Assert.IsTrue(GameSettings.CameraShake, "the older on/off flag follows the amount");
            Assert.AreEqual(1.3f, SettingsExtras.UiScale, 1e-6f);
            Assert.AreEqual(3, SettingsExtras.MaxFlashesPerSecond);

            SettingsExtras.ShakeAmount = 0;
            Assert.IsFalse(GameSettings.CameraShake);
            SettingsExtras.ShakeAmount = 250;
            Assert.AreEqual(100, SettingsExtras.ShakeAmount, "clamped");
            SettingsExtras.ResetAll();
            Assert.AreEqual(100, SettingsExtras.UiScalePercent);
            Assert.IsTrue(SettingsExtras.Subtitles);
        }

        [Test]
        public void UiScaleStepsBetweenEightyAndOneThirty()
        {
            SettingsExtras.UiScalePercent = 100;
            SettingsExtras.StepUiScale(-1);
            SettingsExtras.StepUiScale(-1);
            SettingsExtras.StepUiScale(-1);
            Assert.AreEqual(80, SettingsExtras.UiScalePercent, "stops at 80%");
            for (int i = 0; i < 10; i++) SettingsExtras.StepUiScale(1);
            Assert.AreEqual(130, SettingsExtras.UiScalePercent, "stops at 130%");
            Assert.AreEqual("130%", SettingsExtras.UiScaleText());
            SettingsExtras.UiScalePercent = 115;
            Assert.AreEqual(100, SettingsExtras.UiScalePercent, "an off-list value falls back to 100%");
        }

        [Test]
        public void ReduceFlashingAllowsAtMostThreeFlashesPerSecond()
        {
            var f = new FlashLimiter();
            int allowed = 0;
            for (int i = 0; i < 100; i++) if (f.TryFlash(i * 0.05f, true)) allowed++;   // 20 requests a second for 5 s
            Assert.LessOrEqual(allowed, 3 * 5 + 3);
            var any = new FlashLimiter();
            float[] times = new float[64];
            int n = 0;
            for (int i = 0; i < 200; i++)
                if (any.TryFlash(i * 0.01f, true)) times[n++] = i * 0.01f;
            for (int i = 3; i < n; i++) Assert.GreaterOrEqual(times[i] - times[i - 3], 1f - 1e-4f, "any 4 flashes span a second");
            var off = new FlashLimiter();
            for (int i = 0; i < 10; i++) Assert.IsTrue(off.TryFlash(i * 0.01f, false), "no limit when the option is off");
        }

        // ---- colour-blind check: Azure vs Crimson simulated for deuteranopia and protanopia (Machado et al. 2009,
        // severity 1, applied in linear sRGB), CIEDE2000 difference at least 20 ----

        static readonly double[,] Deuteranopia = { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } };
        static readonly double[,] Protanopia = { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } };

        [Test]
        public void AzureAndCrimsonStayApartForDeuteranopiaAndProtanopia()
        {
            Assert.AreEqual(2.0425, DeltaE2000(new[] { 50, 2.6772, -79.7751 }, new[] { 50, 0, -82.7485 }), 1e-4, "CIEDE2000 reference pair (Sharma 2005)");
            double[] a = Rgb(Palette.Azure), c = Rgb(Palette.Crimson);
            double normal = DeltaE2000(Lab(a), Lab(c));
            double deut = DeltaE2000(Lab(Simulate(a, Deuteranopia)), Lab(Simulate(c, Deuteranopia)));
            double prot = DeltaE2000(Lab(Simulate(a, Protanopia)), Lab(Simulate(c, Protanopia)));
            TestContext.WriteLine($"Azure vs Crimson dE2000: normal {normal:F1}, deuteranopia {deut:F1}, protanopia {prot:F1}");
            Assert.GreaterOrEqual(deut, 20.0, "deuteranopia");
            Assert.GreaterOrEqual(prot, 20.0, "protanopia");
        }

        static double[] Rgb(string hex)
        {
            int v = Convert.ToInt32(hex.Substring(1), 16);
            return new[] { (v >> 16 & 255) / 255.0, (v >> 8 & 255) / 255.0, (v & 255) / 255.0 };
        }

        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        static double Unlin(double c)
        {
            c = Math.Max(0, Math.Min(1, c));
            return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        }

        static double[] Simulate(double[] rgb, double[,] m)
        {
            var l = new[] { Lin(rgb[0]), Lin(rgb[1]), Lin(rgb[2]) };
            var o = new double[3];
            for (int r = 0; r < 3; r++) o[r] = Unlin(m[r, 0] * l[0] + m[r, 1] * l[1] + m[r, 2] * l[2]);
            return o;
        }

        static double[] Lab(double[] rgb)
        {
            double r = Lin(rgb[0]), g = Lin(rgb[1]), b = Lin(rgb[2]);
            double x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
            double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
            double z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;
            double fx = F(x / 0.95047), fy = F(y), fz = F(z / 1.08883);
            return new[] { 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz) };
        }

        static double F(double t) => t > 216.0 / 24389 ? Math.Pow(t, 1.0 / 3) : (24389.0 / 27 * t + 16) / 116;

        static double Rad(double d) => d * Math.PI / 180;

        static double DeltaE2000(double[] p, double[] q)
        {
            double l1 = p[0], a1 = p[1], b1 = p[2], l2 = q[0], a2 = q[1], b2 = q[2];
            double c1 = Math.Sqrt(a1 * a1 + b1 * b1), c2 = Math.Sqrt(a2 * a2 + b2 * b2), cb = (c1 + c2) / 2;
            double g = 0.5 * (1 - Math.Sqrt(Math.Pow(cb, 7) / (Math.Pow(cb, 7) + Math.Pow(25, 7))));
            double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
            double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
            double h1p = c1p == 0 ? 0 : (Math.Atan2(b1, a1p) * 180 / Math.PI + 360) % 360;
            double h2p = c2p == 0 ? 0 : (Math.Atan2(b2, a2p) * 180 / Math.PI + 360) % 360;
            double dlp = l2 - l1, dcp = c2p - c1p, dhp;
            if (c1p * c2p == 0) dhp = 0;
            else
            {
                double dh = h2p - h1p;
                dhp = Math.Abs(dh) <= 180 ? dh : dh > 180 ? dh - 360 : dh + 360;
            }
            double dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dhp / 2));
            double lbp = (l1 + l2) / 2, cbp = (c1p + c2p) / 2, hbp;
            if (c1p * c2p == 0) hbp = h1p + h2p;
            else if (Math.Abs(h1p - h2p) <= 180) hbp = (h1p + h2p) / 2;
            else if (h1p + h2p < 360) hbp = (h1p + h2p + 360) / 2;
            else hbp = (h1p + h2p - 360) / 2;
            double t = 1 - 0.17 * Math.Cos(Rad(hbp - 30)) + 0.24 * Math.Cos(Rad(2 * hbp)) + 0.32 * Math.Cos(Rad(3 * hbp + 6)) - 0.20 * Math.Cos(Rad(4 * hbp - 63));
            double dth = 30 * Math.Exp(-Math.Pow((hbp - 275) / 25, 2));
            double rc = 2 * Math.Sqrt(Math.Pow(cbp, 7) / (Math.Pow(cbp, 7) + Math.Pow(25, 7)));
            double sl = 1 + 0.015 * Math.Pow(lbp - 50, 2) / Math.Sqrt(20 + Math.Pow(lbp - 50, 2));
            double sc = 1 + 0.045 * cbp, sh = 1 + 0.015 * cbp * t, rt = -Math.Sin(Rad(2 * dth)) * rc;
            return Math.Sqrt(Math.Pow(dlp / sl, 2) + Math.Pow(dcp / sc, 2) + Math.Pow(dHp / sh, 2) + rt * (dcp / sc) * (dHp / sh));
        }
    }
}
