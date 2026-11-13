using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>U-066 pickups and drops: coins from knock-outs and map piles, three power-ups with counters, a seed-fixed spawner.</summary>
    [TestFixture]
    public sealed class PickupsTests
    {
        private static GameConfig Cfg => DepthKit.Cfg();

        private static int Active(World w, PickupKind kind)
        {
            int n = 0;
            foreach (Pickup p in w.Depth.Pickups) if (p.Active && p.Kind == kind) n++;
            return n;
        }

        [Test]
        public void U066SpotsAreOnFloorCoinSpotsMirroredAndPowerSpotsOnTheCentreLine()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior);
            DepthState d = w.Depth;
            Assert.AreEqual(10, d.CoinSpots.Length);
            for (int i = 0; i < d.CoinSpots.Length; i += 2)
            {
                Assert.AreEqual(w.Map.Width - d.CoinSpots[i].X, d.CoinSpots[i + 1].X, 1e-9);
                Assert.AreEqual(d.CoinSpots[i].Z, d.CoinSpots[i + 1].Z);
                Assert.AreEqual(Cell.Floor, w.Map.Cells[(int)d.CoinSpots[i].Z * w.Map.Width + (int)d.CoinSpots[i].X]);
            }
            Assert.AreEqual(4, d.PowerSpots.Length);
            var sb = new System.Text.StringBuilder();
            foreach (PointXZ s in d.PowerSpots)
            {
                Assert.AreEqual(w.Map.Width / 2.0, s.X);
                sb.Append(s.Z).Append(' ');
            }
            Console.WriteLine("{\"item\":\"U-066\",\"powerSpotsZ\":\"" + sb + "\"}");
        }

        [Test]
        public void U066AKnockOutDropsCoinsAnyoneCanTakeAndTheyExpire()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            int enemy = DepthKit.Enemy(w, 0);
            DepthKit.Put(w, enemy, 60, 40);
            DepthKit.Steps(w, 1);
            int head = w.EventHead;
            Damage.ApplyDamage(w, enemy, 9999, 0, 40, 40);
            DepthKit.Steps(w, 1);
            List<SimEvent> sp = DepthKit.Since(w, head, SimEventType.PickupSpawned);
            Assert.AreEqual(1, sp.Count);
            Assert.AreEqual(enemy, sp[0].A);
            Assert.AreEqual((int)PickupKind.Coin, sp[0].B);
            Assert.AreEqual(3, sp[0].Value);
            Assert.AreEqual(60, sp[0].X, 0.5);
            Assert.GreaterOrEqual(sp[0].Slot, 0);
            DepthKit.Put(w, 0, sp[0].X + 0.5, sp[0].Z);
            DepthKit.Steps(w, 1);
            Assert.AreEqual(3, w.Actors[0].CoinsPicked);
            SimEvent tk = DepthKit.Since(w, head, SimEventType.PickupTaken)[0];
            Assert.AreEqual(0, tk.A);
            Assert.AreEqual(sp[0].Slot, tk.Slot);
            Assert.AreEqual(0, Active(w, PickupKind.Coin));

            World v = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            int e2 = DepthKit.Enemy(v, 0);
            DepthKit.Put(v, e2, 60, 44);
            DepthKit.Steps(v, 1);
            v.Actors[e2].RespawnIn = 1e9;
            Damage.ApplyDamage(v, e2, 9999, 0, 40, 40);
            v.Depth.CoinSpotNextTick[0] = int.MaxValue;
            for (int s = 0; s < v.Depth.CoinSpotNextTick.Length; s++) v.Depth.CoinSpotNextTick[s] = int.MaxValue;
            head = v.EventHead;
            DepthKit.Steps(v, (int)v.Config.Depth.Pickups.CoinDropLifetimeTicks + 2);
            Assert.AreEqual(1, DepthKit.Since(v, head, SimEventType.PickupExpired).Count);
            Assert.AreEqual(0, Active(v, PickupKind.Coin));
        }

        [Test]
        public void U066MapCoinPilesAppearWaitAndComeBackAfterBeingTaken()
        {
            World w = DepthKit.Field(Cfg, ClassId.Farmer, 40, 40);
            PickupsConfig P = w.Config.Depth.Pickups;
            DepthKit.Steps(w, (int)P.CoinFirstTicks + 1);
            Assert.AreEqual(10, Active(w, PickupKind.Coin));
            PointXZ s = w.Depth.CoinSpots[0];
            DepthKit.Put(w, 0, s.X, s.Z);
            DepthKit.Steps(w, 1);
            Assert.AreEqual(5, w.Actors[0].CoinsPicked);
            Assert.AreEqual(9, Active(w, PickupKind.Coin));
            DepthKit.Put(w, 0, 40, 40);
            DepthKit.Steps(w, (int)P.CoinRespawnTicks - 5);
            Assert.AreEqual(9, Active(w, PickupKind.Coin));
            DepthKit.Steps(w, 10);
            Assert.AreEqual(10, Active(w, PickupKind.Coin));
        }

        [Test]
        public void U066ThePowerUpSpawnerIsFixedByTheSeedAnnouncedAheadAndTakenOnlyWhenReady()
        {
            string Run(int seed)
            {
                World w = DepthKit.Field(Cfg, ClassId.Warrior, 20, 40, seed);
                var sb = new System.Text.StringBuilder();
                int head = w.EventHead;
                DepthKit.Steps(w, 4000);
                foreach (SimEvent e in DepthKit.Since(w, head, SimEventType.PickupSpawned))
                    if (e.B != (int)PickupKind.Coin) sb.Append(e.B).Append('@').Append(e.Z).Append(' ');
                return sb.ToString();
            }
            string a = Run(63), b = Run(63), c = Run(64);
            Console.WriteLine("{\"item\":\"U-066\",\"seed63\":\"" + a + "\",\"seed64\":\"" + c + "\"}");
            Assert.AreEqual(a, b);
            Assert.AreEqual(3, a.Trim().Split(' ').Length, "20 s, 65 s, 110 s");

            World w2 = DepthKit.Field(Cfg, ClassId.Warrior, 20, 40);
            PickupsConfig P = w2.Config.Depth.Pickups;
            int h = w2.EventHead;
            DepthKit.Steps(w2, (int)P.FirstPowerUpTicks + 1);
            SimEvent sp = default;
            foreach (SimEvent e in DepthKit.Since(w2, h, SimEventType.PickupSpawned)) if (e.B != (int)PickupKind.Coin) sp = e;
            Assert.AreEqual(-1, sp.A);
            Assert.AreEqual(P.FirstPowerUpTicks + P.PowerUpArmTicks, sp.Value, "the event says when it can be taken");
            DepthKit.Put(w2, 0, sp.X, sp.Z);
            DepthKit.Steps(w2, (int)sp.Value - w2.Tick - 3);
            Assert.AreEqual(PickupKind.Coin, Pickups.PowerOf(w2, 0), "announced, not ready");
            DepthKit.Steps(w2, 6);
            Assert.AreEqual((PickupKind)sp.B, Pickups.PowerOf(w2, 0));
            Assert.AreEqual(1, w2.Depth.PowerUpsTaken);
        }

        [Test]
        public void U066HasteIsFasterAndRootsStripIt()
        {
            double Walk(bool haste)
            {
                World w = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
                if (haste) Pickups.Give(w, 0, PickupKind.Haste, 0);
                w.Inputs[0].MoveX = 1;
                DepthKit.Steps(w, 30);
                double x0 = w.X[0];
                DepthKit.Steps(w, 10);
                return w.X[0] - x0;
            }
            double plain = Walk(false), fast = Walk(true);
            Assert.AreEqual(1.3, fast / plain, 0.02);

            World r = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            Pickups.Give(r, 0, PickupKind.Haste, 0);
            r.Actors[0].SlowTimer = 1;
            int head = r.EventHead;
            DepthKit.Steps(r, 1);
            Assert.AreEqual(PickupKind.Coin, Pickups.PowerOf(r, 0));
            Assert.AreEqual(Pickups.EndStripped, DepthKit.Since(r, head, SimEventType.PowerUpEnded)[0].Value);

            World t = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            Pickups.Give(t, 0, PickupKind.Haste, 0);
            DepthKit.Steps(t, (int)t.Config.Depth.Pickups.HasteTicks + 1);
            Assert.AreEqual(PickupKind.Coin, Pickups.PowerOf(t, 0), "timed out");
        }

        [Test]
        public void U066MightHitsHarderAndIsDroppedOnAKnockOutForAnyoneToTake()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            int enemy = DepthKit.Enemy(w, 0);
            DepthKit.Become(w, enemy, ClassId.Warrior);
            DepthKit.Put(w, enemy, 41.2, 40);
            Pickups.Give(w, 0, PickupKind.Might, 0);
            double hp = w.Actors[enemy].Hp;
            w.Inputs[0].AimX = 1;
            w.Inputs[0].AimZ = 0;
            w.Inputs[0].Attack = true;
            int head = w.EventHead;
            DepthKit.Steps(w, 40);
            w.Inputs[0].Attack = false;
            List<SimEvent> hits = DepthKit.Since(w, head, SimEventType.Hit);
            Assert.Greater(hits.Count, 0);
            Assert.AreEqual(w.Config.Class(ClassId.Warrior).AttackDamage * 1.5, hits[0].Value, 1e-9);
            Assert.Less(w.Actors[enemy].Hp, hp);

            DepthKit.Put(w, enemy, 60, 40);
            DepthKit.Steps(w, 1);
            for (int s = 0; s < w.Depth.CoinSpotNextTick.Length; s++) w.Depth.CoinSpotNextTick[s] = int.MaxValue;
            int left = w.Depth.PowerEndTick[0] - w.Tick;
            head = w.EventHead;
            Damage.ApplyDamage(w, 0, 9999, enemy, 41, 40);
            DepthKit.Steps(w, 1);
            Assert.AreEqual(Pickups.EndDropped, DepthKit.Since(w, head, SimEventType.PowerUpEnded)[0].Value);
            Assert.AreEqual(1, Active(w, PickupKind.Might));
            Assert.AreEqual(1, w.Depth.PowerUpsDropped);
            DepthKit.Put(w, enemy, w.X[0], w.Z[0]);
            DepthKit.Steps(w, 1);
            Assert.AreEqual(PickupKind.Might, Pickups.PowerOf(w, enemy), "the enemy took it");
            Assert.AreEqual(left - 2, w.Depth.PowerEndTick[enemy] - w.Tick, 2, "with the time that was left");
            Assert.AreEqual(3, w.Actors[enemy].CoinsPicked, "and the coins");
        }

        [Test]
        public void U066WardSoaksSixtyThenBreaksAndAnExplosionGoesStraightThrough()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            int enemy = DepthKit.Enemy(w, 0);
            DepthKit.Put(w, enemy, 60, 40);
            Pickups.Give(w, 0, PickupKind.Ward, 0);
            double hp = w.Actors[0].Hp;
            int head = w.EventHead;
            Assert.AreEqual(0, Damage.ApplyDamage(w, 0, 40, enemy, 60, 40));
            Assert.AreEqual(hp, w.Actors[0].Hp);
            Assert.AreEqual(enemy, w.Actors[0].LastAttacker);
            Assert.AreEqual(0, DepthKit.Since(w, head, SimEventType.Hit).Count);
            Damage.ApplyDamage(w, 0, 40, enemy, 60, 40);
            Assert.AreEqual(hp - 20, w.Actors[0].Hp, 1e-9, "20 left of the ward, 20 through");
            Assert.AreEqual(PickupKind.Coin, Pickups.PowerOf(w, 0));
            List<SimEvent> ab = DepthKit.Since(w, head, SimEventType.WardAbsorb);
            Assert.AreEqual(2, ab.Count);
            Assert.AreEqual(40, ab[0].Value);
            Assert.AreEqual(20, ab[1].Value);
            Assert.AreEqual(Pickups.EndBroken, DepthKit.Since(w, head, SimEventType.PowerUpEnded)[0].Value);

            World v = DepthKit.Field(Cfg, ClassId.Warrior, 40, 40);
            Pickups.Give(v, 0, PickupKind.Ward, 0);
            hp = v.Actors[0].Hp;
            Damage.ApplyDamage(v, 0, 30, -1, 40, 40, DamageCause.Explosion);
            Assert.AreEqual(hp - 30, v.Actors[0].Hp, 1e-9);
            Assert.AreEqual(PickupKind.Coin, Pickups.PowerOf(v, 0), "broken by the blast");
        }

        [Test]
        public void U066ClassicHasNoPickups()
        {
            World w = DepthKit.Field(GameConfig.CreateBrowser5v5(), ClassId.Warrior, 40, 40);
            int enemy = DepthKit.Enemy(w, 0);
            DepthKit.Put(w, enemy, 60, 40);
            int head = w.EventHead;
            Damage.ApplyDamage(w, enemy, 9999, 0, 40, 40);
            DepthKit.Steps(w, 800);
            Assert.AreEqual(0, DepthKit.Since(w, head, SimEventType.PickupSpawned).Count);
            Assert.AreEqual(1, Pickups.SpeedScale(w, 0));
        }

        [Test]
        public void U066BotsPickUpCoinsAndPowerUpsAndThePoolHolds()
        {
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 7, 16);
            int ended = 0, absorbed = 0;
            DepthKit.RunBots(w, 5400, e =>
            {
                if (e.Type == SimEventType.PowerUpEnded) ended++;
                if (e.Type == SimEventType.WardAbsorb) absorbed++;
            });
            DepthState d = w.Depth;
            int coins = 0;
            for (int i = 0; i < w.ActorCount; i++) coins += w.Actors[i].CoinsPicked;
            Console.WriteLine("{\"item\":\"U-066\",\"coinsTaken\":" + d.CoinsTaken + ",\"powerUpsSpawned\":" + d.PowerIndex + ",\"powerUpsTaken\":" + d.PowerUpsTaken + ",\"dropped\":" + d.PowerUpsDropped + ",\"ended\":" + ended + ",\"wardAbsorbs\":" + absorbed + ",\"poolRecycles\":" + d.PickupOverflows + "}");
            Assert.AreEqual(coins, d.CoinsTaken);
            Assert.Greater(d.CoinsTaken, 20);
            Assert.GreaterOrEqual(d.PowerUpsTaken, 1);
            Assert.AreEqual(0, d.PickupOverflows);
        }
    }
}
