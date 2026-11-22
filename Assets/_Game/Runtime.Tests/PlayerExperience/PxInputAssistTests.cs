using System;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// Lane I (player-experience wave, 2026-11-22): the aim help maths (AimAssistBrain, AimHitTest, CastDistance). Pure: no
    /// scene, no devices. The hittable test is checked against real sim swings on a grid for all six class rows.
    /// </summary>
    public sealed class PxInputAssistTests
    {
        static AimCandidate Enemy(int id, double x, double z, double vx = 0, double vz = 0) =>
            new AimCandidate { Id = id, X = x, Z = z, Vx = vx, Vz = vz, Radius = 0.45, Enemy = true, Alive = true, ClearLine = true };

        static AimAssistFrame Press(double rawX = 5, double rawZ = 0) => new AimAssistFrame
        {
            Px = 0, Pz = 0, Facing = 0, HasRaw = true, RawX = rawX, RawZ = rawZ, Attacking = true, Dt = 1.0 / 60,
            Kind = AimPressKind.Melee, Reach = 1.8, ArcDeg = 110,
        };

        static double AngleDeg(double x, double z) => Math.Atan2(z, x) * 180 / Math.PI;

        static double Run(AimHelpLevel lv, AimAssistFrame f, AimCandidate[] c, out AimAssistBrain b)
        {
            b = new AimAssistBrain();
            b.Step(f, c, c.Length, AimAssistTuning.For(lv), out double ax, out double az);
            return AngleDeg(ax, az);
        }

        [Test]
        public void OffLeavesTheRawAimUntouchedBitForBit()
        {
            var f = Press(4.123456789, -1.98765);
            var c = new[] { Enemy(3, 1.5, 0.2) };
            var b = new AimAssistBrain();
            b.Step(f, c, 1, AimAssistTuning.For(AimHelpLevel.Off), out double ax, out double az);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(f.RawX), BitConverter.DoubleToInt64Bits(ax));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(f.RawZ), BitConverter.DoubleToInt64Bits(az));
            Assert.AreEqual(-1, b.Lock);
        }

        [Test]
        public void NoCandidateLeavesTheRawAimUntouched()
        {
            var f = Press(4, 1);
            var b = new AimAssistBrain();
            b.Step(f, new[] { Enemy(3, -2, 0) }, 1, AimAssistTuning.For(AimHelpLevel.Low), out double ax, out double az);
            Assert.AreEqual(4, ax);
            Assert.AreEqual(1, az);
            Assert.AreEqual(-1, b.Lock);
        }

        [Test]
        public void ConeEdgesPerStrength()
        {
            // An enemy 3 u away (outside High's in-your-face ring) at 24 / 26 / 49 / 51 degrees off the raw aim (+x).
            foreach (var (deg, low, high) in new[] { (24.0, true, true), (26.0, false, true), (49.0, false, true), (51.0, false, false) })
            {
                double r = deg * Math.PI / 180;
                var c = new[] { Enemy(7, Math.Cos(r) * 3, Math.Sin(r) * 3) };
                Run(AimHelpLevel.Low, Press(), c, out var bl);
                Run(AimHelpLevel.High, Press(), c, out var bh);
                Assert.AreEqual(low ? 7 : -1, bl.Lock, "low at " + deg);
                Assert.AreEqual(high ? 7 : -1, bh.Lock, "high at " + deg);
            }
        }

        [Test]
        public void RangeLimitIsReachPlusBodyPlusExtra()
        {
            double low = 1.8 + 0.45 + AimAssistTunables.LowRangeExtra, high = 1.8 + 0.45 + AimAssistTunables.HighRangeExtra;
            Run(AimHelpLevel.Low, Press(), new[] { Enemy(1, low - 0.01, 0) }, out var a);
            Run(AimHelpLevel.Low, Press(), new[] { Enemy(1, low + 0.01, 0) }, out var b);
            Run(AimHelpLevel.High, Press(), new[] { Enemy(1, high - 0.01, 0) }, out var c);
            Run(AimHelpLevel.High, Press(), new[] { Enemy(1, high + 0.01, 0) }, out var d);
            Assert.AreEqual(1, a.Lock);
            Assert.AreEqual(-1, b.Lock);
            Assert.AreEqual(1, c.Lock);
            Assert.AreEqual(-1, d.Lock);
        }

        [Test]
        public void PicksTheNearestByAngleAndIgnoresDeadAlliesAndFogged()
        {
            double r10 = 10 * Math.PI / 180, r20 = 20 * Math.PI / 180;
            var c = new[]
            {
                Enemy(1, Math.Cos(r20) * 2, Math.Sin(r20) * 2),
                Enemy(2, Math.Cos(r10) * 2, -Math.Sin(r10) * 2),
                new AimCandidate { Id = 3, X = 2, Z = 0, Radius = 0.45, Enemy = true, Alive = false, ClearLine = true },
                new AimCandidate { Id = 4, X = 2, Z = 0.01, Radius = 0.45, Enemy = false, Alive = true, ClearLine = true },
                new AimCandidate { Id = 5, X = 2, Z = -0.01, Radius = 0.45, Enemy = true, Alive = true, Hidden = true, ClearLine = true },
            };
            Run(AimHelpLevel.High, Press(), c, out var b);
            Assert.AreEqual(2, b.Lock, "10 degrees beats 20 at the same distance; dead, ally and fogged are never picked");
        }

        [Test]
        public void AClearLineIsPreferred()
        {
            var blocked = Enemy(1, 2, 0.05);
            blocked.ClearLine = false;
            double r = 15 * Math.PI / 180;
            Run(AimHelpLevel.High, Press(), new[] { blocked, Enemy(2, Math.Cos(r) * 2, Math.Sin(r) * 2) }, out var b);
            Assert.AreEqual(2, b.Lock);
        }

        [Test]
        public void LeadIsVelocityTimesTheBotsLead()
        {
            var c = new[] { Enemy(1, 2, 0, 0, 4) };
            var b = new AimAssistBrain();
            b.Step(Press(), c, 1, AimAssistTuning.For(AimHelpLevel.High), out double ax, out double az);
            Assert.AreEqual(2, ax, 1e-12);
            Assert.AreEqual(4 * AimAssistTunables.LeadSeconds, az, 1e-12);
            Assert.AreEqual(0.13, AimAssistTunables.LeadSeconds, 1e-12, "the bots' own BotLeadSeconds");
        }

        [Test]
        public void TntLeadsByTheCappedFuse()
        {
            var f = Press();
            f.Kind = AimPressKind.Tnt;
            f.TntRange = 7;
            f.TntLead = 0.5;
            var b = new AimAssistBrain();
            b.Step(f, new[] { Enemy(1, 4, 0, 0, 2) }, 1, AimAssistTuning.For(AimHelpLevel.High), out double ax, out double az);
            Assert.AreEqual(4, ax, 1e-12);
            Assert.AreEqual(1.0, az, 1e-12);
        }

        [Test]
        public void NeverBendsMoreThanTheCone()
        {
            var rng = new Random(11);
            foreach (AimHelpLevel lv in new[] { AimHelpLevel.Low, AimHelpLevel.High })
            {
                AimAssistTuning t = AimAssistTuning.For(lv);
                for (int i = 0; i < 400; i++)
                {
                    double a = (rng.NextDouble() * 2 - 1) * Math.PI, d = 1.0 + rng.NextDouble() * 4;
                    var c = new[] { Enemy(1, Math.Cos(a) * d, Math.Sin(a) * d, rng.NextDouble() * 8 - 4, rng.NextDouble() * 8 - 4) };
                    var b = new AimAssistBrain();
                    b.Step(Press(), c, 1, t, out double ax, out double az);
                    if (b.Lock < 0) continue;
                    double limit = d <= 1.8 + 0.45 + AimAssistTunables.CloseMargin && t.CloseConeDeg > 0 ? t.CloseConeDeg : t.ConeDeg;
                    Assert.LessOrEqual(Math.Abs(AngleDeg(ax, az)), limit + 1e-6, lv + " case " + i);
                }
            }
        }

        [Test]
        public void IdleBendIsGentleAndOffInThirdPerson()
        {
            var f = Press();
            f.Attacking = false;
            var c = new[] { Enemy(1, 2, 2) };   // 45 degrees off
            var b = new AimAssistBrain();
            b.Step(f, c, 1, AimAssistTuning.For(AimHelpLevel.High), out double ax, out double az);
            Assert.AreEqual(45 * AimAssistTunables.IdleBendHigh, AngleDeg(ax, az), 1e-6);
            f.ThirdPerson = true;
            b.Reset();
            b.Step(f, c, 1, AimAssistTuning.For(AimHelpLevel.High), out ax, out az);
            Assert.AreEqual(5, ax);
            Assert.AreEqual(0, az);
        }

        [Test]
        public void StickinessHoldsAfterTheAttackThenLetsGo()
        {
            var c = new[] { Enemy(1, 2, 0.5) };
            AimAssistTuning t = AimAssistTuning.For(AimHelpLevel.Low);   // Low: no idle bend
            var b = new AimAssistBrain();
            b.Step(Press(), c, 1, t, out _, out _);
            Assert.IsTrue(b.JustEngaged);
            var f = Press();
            f.Attacking = false;
            f.Dt = 0.3;
            b.Step(f, c, 1, t, out double ax, out double az);
            Assert.AreEqual(1, b.Lock, "0.3 s after: still bent");
            Assert.IsFalse(b.JustEngaged, "reported once per lock");
            b.Step(f, c, 1, t, out ax, out az);
            Assert.AreEqual(-1, b.Lock, "0.6 s after: the player's own aim");
            Assert.AreEqual(5, ax);
        }

        [Test]
        public void IntentConeFollowsTheMoveDirection()
        {
            // Camera (raw) points +x; the player walks toward +z where the enemy is: the cone is measured around the move.
            var f = Press();
            f.HasIntent = true;
            f.IntentX = 0;
            f.IntentZ = 1;
            var c = new[] { Enemy(1, 0.2, 3.5) };
            Run(AimHelpLevel.High, f, c, out var b);
            Assert.AreEqual(1, b.Lock);
            f.HasIntent = false;
            Run(AimHelpLevel.High, f, c, out b);
            Assert.AreEqual(-1, b.Lock, "measured around the camera the enemy is 87 degrees off");
        }

        [Test]
        public void HealerBendsToTheHurtAllyWhenNoEnemyIsInLanternReach()
        {
            var f = Press();
            f.Kind = AimPressKind.Healer;
            f.Reach = 1.5;
            f.MendRange = 6;
            var ally = new AimCandidate { Id = 4, X = 3, Z = 0.4, Radius = 0.45, Enemy = false, Alive = true, ClearLine = true, Hurt = true };
            var far = Enemy(2, 3.5, -0.3);
            var b = new AimAssistBrain();
            b.Step(f, new[] { far, ally }, 2, AimAssistTuning.For(AimHelpLevel.High), out double ax, out double az);
            Assert.AreEqual(4, b.Lock);
            Assert.IsTrue(b.LockIsAlly);
            Assert.AreEqual(3, ax, 1e-12);
            Assert.AreEqual(0.4, az, 1e-12);
            var near = Enemy(3, 1.2, 0.1);
            b.Reset();
            b.Step(f, new[] { near, ally }, 2, AimAssistTuning.For(AimHelpLevel.High), out ax, out az);
            Assert.AreEqual(3, b.Lock, "an enemy inside lantern reach: she swings");
            Assert.IsFalse(b.LockIsAlly);
        }

        [Test]
        public void CastDistanceFromStickAndHold()
        {
            Assert.AreEqual(CastDistance.DefaultCast, CastDistance.FromStick(0.1, 6), 1e-12);
            Assert.AreEqual(CastDistance.MinCast, CastDistance.FromStick(CastDistance.StickChooseMin, 6), 1e-12);
            Assert.AreEqual(6, CastDistance.FromStick(1, 6), 1e-12);
            Assert.AreEqual(CastDistance.DefaultCast, CastDistance.FromHold(0, 6), 1e-12);
            Assert.AreEqual(6, CastDistance.FromHold(CastDistance.HoldExtendSeconds, 6), 1e-12);
            Assert.AreEqual(4.5, CastDistance.FromHold(CastDistance.HoldExtendSeconds / 2, 6), 1e-12);
        }

        [Test]
        public void DefaultsAreMouseLowControllerAndKeysHigh()
        {
            Assert.AreEqual(AimHelpLevel.Low, AimAssistTunables.DefaultFor(AimDevice.Mouse));
            Assert.AreEqual(AimHelpLevel.High, AimAssistTunables.DefaultFor(AimDevice.Controller));
            Assert.AreEqual(AimHelpLevel.High, AimAssistTunables.DefaultFor(AimDevice.Keys));
        }

        /// <summary>AimHitTest.SwingHits against a real swing of the sim, target held still, for all six class rows.</summary>
        [Test]
        public void HittableEqualsTheSimsSwingOnAGrid()
        {
            for (int a = 0; a < 6; a++)
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                for (int di = 0; di <= 10; di += 2)
                    for (int bi = 0; bi <= 18; bi += 3)
                    {
                        double d = 1.0 + di * 0.2, bear = bi * 5 * Math.PI / 180;
                        World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = (ClassId)a, Config = cfg, BotDifficulty = BotDifficulty.Normal });
                        int ia = 0, ib = w.ActorCount / 2;
                        for (int j = 0; j < w.ActorCount; j++)
                        {
                            if (j == ia || j == ib) continue;
                            w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; w.MeatDropped[j] = 1; w.Depth.WasAlive[j] = 0;
                        }
                        double cx = w.Map.Hill.X, cz = w.Map.Hill.Z, tx = cx + Math.Cos(bear) * d, tz = cz + Math.Sin(bear) * d;
                        Place(w, ia, (ClassId)a, cx, cz);
                        Place(w, ib, ClassId.Farmer, tx, tz);
                        w.Demolitions.TntReadyTick[ia] = int.MaxValue;
                        InputFrame hf = SimInput.EmptyInput(), idle = SimInput.EmptyInput();
                        hf.ClassPick = -1;
                        idle.ClassPick = -1;
                        hf.AimX = cx + 3;
                        hf.AimZ = cz;
                        hf.Attack = true;
                        ClassConfig cc = cfg.Class((ClassId)a);
                        double facing = AimHitTest.FacingFor(cx, cz, hf.AimX, hf.AimZ, w.Actors[ia].Facing);
                        bool pred = AimHitTest.SwingHits(cx, cz, facing, cc.AttackRange, cc.AttackArcDeg, Phys.On(w, cfg.Physics.ArcCoversBody),
                            Phys.On(w, cfg.Physics.WallsBlockHits), tx, tz, cfg.Class(ClassId.Farmer).Radius, SegmentClear.Clear(w.Map, cx, cz, tx, tz));
                        int cursor = w.EventHead;
                        bool? sim = null;
                        for (int t = 0; t < 40 && sim == null; t++)
                        {
                            SimInput.ApplyInput(w, ia, hf);
                            for (int j = 1; j < w.ActorCount; j++) SimInput.ApplyInput(w, j, idle);
                            SimStep.Step(w);
                            w.X[ib] = tx; w.Z[ib] = tz; w.X[ia] = cx; w.Z[ia] = cz;
                            hf.Attack = false;
                            for (; cursor < w.EventHead; cursor++)
                            {
                                SimEvent e = w.Events[cursor % World.EventCapacity];
                                if (e.Type == SimEventType.AttackContact && e.A == ia) sim = e.Value > 0;
                            }
                        }
                        Assert.IsTrue(sim.HasValue, (ClassId)a + " swung");
                        Assert.AreEqual(sim.Value, pred, (ClassId)a + " at " + d + " u, " + (bi * 5) + " degrees");
                    }
            }
        }

        static void Place(World w, int j, ClassId cls, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Cls = cls; a.MaxHp = w.Config.Class(cls).MaxHp; a.Hp = a.MaxHp; a.Alive = true; a.SpawnProtect = 0; a.StandUp = 0; a.AirLen = 0;
            w.X[j] = x; w.Z[j] = z; w.PrevX[j] = x; w.PrevZ[j] = z; w.Vx[j] = 0; w.Vz[j] = 0;
        }
    }
}
