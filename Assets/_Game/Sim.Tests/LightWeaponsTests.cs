using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// feat/light-weapons-1: the Builder's mallet, the Demolitions Expert's pry bar and the Healer's lantern
    /// (LightWeapons in Combat.cs, numbers in DepthConfig.LightWeapons). Damage, reach, cooldown, the shove, the Healer's
    /// mend-first rule, the rule switch, the "lighter than the Warrior" band, and the bots' close-range press.
    /// Instant hits (sim physics Classic) with the depth lane on, so a press resolves inside one CombatSystem call.
    /// </summary>
    [TestFixture]
    public sealed class LightWeaponsTests
    {
        private const int Me = 0;
        private const double X0 = 56, Z0 = 40;

        private static GameConfig Cfg(bool on = true)
        {
            GameConfig c = GameConfig.CreateBrowser5v5();
            c.Physics.Classic = true; // instant hits: no wind-up, so one CombatSystem call resolves the swing
            c.Depth.Classic = !on;    // the light weapons are a mechanics-depth rule
            return c;
        }

        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            a.AbilityActive = 0;
            a.AttackCd = 0;
            a.KVx = 0;
            a.KVz = 0;
            w.X[j] = x;
            w.Z[j] = z;
        }

        private static int Ally(World w, int k) => 1 + k;
        private static int Enemy(World w, int k) => w.ActorCount / 2 + k;

        /// <summary>The human of class cls at (X0, Z0) facing +x, everyone else dead, one Miner enemy (radius 0.45) parked away.</summary>
        private static World Field(ClassId cls, bool on = true)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = cls, Config = Cfg(on) });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            Put(w, Me, X0, Z0);
            w.Actors[Me].Facing = 0;
            int e = Enemy(w, 0);
            w.Actors[e].Cls = ClassId.Miner;
            w.Actors[e].MaxHp = w.Config.Class(ClassId.Miner).MaxHp;
            w.Tick = 100;
            Assert.AreEqual(cls, w.Actors[Me].Cls);
            Assert.AreEqual(TeamId.Crimson, w.Actors[e].Team);
            return w;
        }

        private static void Swing(World w)
        {
            w.Inputs[Me].Attack = true;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            Combat.CombatSystem(w, SimStep.Dt(w.Config));
            w.Inputs[Me].Attack = false;
        }

        private static LightWeapon Row(World w, ClassId cls) => LightWeapons.Row(w.Config, cls);

        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Healer)]
        [TestCase(ClassId.Builder)]
        public void AHitInReachDealsTheWeaponDamageSetsItsCooldownAndShovesTheTargetAway(ClassId cls)
        {
            World w = Field(cls);
            LightWeapon lw = Row(w, cls);
            int e = Enemy(w, 0);
            Put(w, e, X0 + lw.Range, Z0); // body edge well inside reach
            double hp0 = w.Actors[e].Hp;
            Swing(w);
            Assert.AreEqual(hp0 - lw.Damage, w.Actors[e].Hp, 1e-9, cls + " damage");
            Assert.AreEqual(lw.CooldownSeconds, w.Actors[Me].AttackCd, 1e-9, cls + " cooldown");
            Assert.Greater(w.Actors[e].KVx, 0, cls + " shoved away from the wielder (+x)");
            Assert.AreEqual(0, w.Actors[e].HitStunTicks, "a shove never staggers");
        }

        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Healer)]
        public void ATargetBeyondReachOrBehindTheArcIsNotHit(ClassId cls)
        {
            World w = Field(cls);
            LightWeapon lw = Row(w, cls);
            int e = Enemy(w, 0);
            double radius = w.Config.Class(ClassId.Miner).Radius;
            Put(w, e, X0 + lw.Range + radius + 0.05, Z0);
            double hp0 = w.Actors[e].Hp;
            Swing(w);
            Assert.AreEqual(hp0, w.Actors[e].Hp, 1e-9, "just out of reach");
            w.Actors[Me].AttackCd = 0;
            Put(w, e, X0 - 1.0, Z0); // behind her
            Swing(w);
            Assert.AreEqual(hp0, w.Actors[e].Hp, 1e-9, "outside the arc");
        }

        [Test]
        public void TheCooldownGatesTheNextSwingToTheTick()
        {
            World w = Field(ClassId.Demolitions);
            LightWeapon lw = Row(w, ClassId.Demolitions);
            int e = Enemy(w, 0);
            Put(w, e, X0 + 1.0, Z0);
            double dt = SimStep.Dt(w.Config);
            int cdTicks = (int)Math.Ceiling(lw.CooldownSeconds / dt - 1e-9);
            Swing(w);
            double hp1 = w.Actors[e].Hp;
            for (int k = 1; k < cdTicks; k++)
            {
                w.X[e] = X0 + 1.0; // keep it in reach (the shove is integrated by Movement, not here)
                Swing(w);
                Assert.AreEqual(hp1, w.Actors[e].Hp, 1e-9, "no second hit at tick " + k);
            }
            Swing(w);
            Assert.AreEqual(hp1 - lw.Damage, w.Actors[e].Hp, 1e-9, "the next hit lands exactly when the cooldown runs out");
        }

        [Test]
        public void TheHealerMendsFirstAndSwingsTheLanternOnlyWhenNobodyCanBeMended()
        {
            World w = Field(ClassId.Healer);
            LightWeapon lw = Row(w, ClassId.Healer);
            int e = Enemy(w, 0);
            int ally = Ally(w, 0);
            Put(w, e, X0 + 1.0, Z0);
            Put(w, ally, X0, Z0 + 2.0);
            w.Actors[ally].Hp = 50;
            double hp0 = w.Actors[e].Hp;
            int head = w.EventHead;
            Swing(w);
            Assert.AreEqual(50 + w.Config.Abilities.Healer.MendHeal, w.Actors[ally].Hp, 1e-9, "a hurt ally in range is mended");
            Assert.AreEqual(hp0, w.Actors[e].Hp, 1e-9, "no lantern hit on a mend press");
            Assert.AreEqual(w.Config.Class(ClassId.Healer).AttackCooldown, w.Actors[Me].AttackCd, 1e-9);
            // Nobody hurt in range: the same press swings the lantern, on the same shared cooldown.
            w.Actors[ally].Hp = w.Actors[ally].MaxHp;
            w.Actors[Me].AttackCd = 0;
            Swing(w);
            Assert.AreEqual(hp0 - lw.Damage, w.Actors[e].Hp, 1e-9, "lantern hit");
            Assert.AreEqual(lw.CooldownSeconds, w.Actors[Me].AttackCd, 1e-9);
            Assert.Greater(w.EventHead, head);
        }

        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Healer)]
        public void RuleOffIsTheGameBeforeNoSwingNoDamage(ClassId cls)
        {
            World w = Field(cls, on: false);
            int e = Enemy(w, 0);
            Put(w, e, X0 + 1.0, Z0);
            double hp0 = w.Actors[e].Hp;
            Swing(w);
            Assert.AreEqual(hp0, w.Actors[e].Hp, 1e-9);
            Assert.AreEqual(0, w.Actors[Me].AttackCd, 1e-9, "no cooldown spent");
            Assert.AreEqual(0, w.Actors[e].KVx, 1e-12);
            Assert.IsNull(LightWeapons.Of(w, cls));
        }

        [Test]
        public void TheBuilderRowKeepsHisOldSwingAndTheOthersStayInTheBandBelowTheWarrior()
        {
            GameConfig c = GameConfig.CreateDefault();
            ClassConfig b = c.Class(ClassId.Builder);
            LightWeapon m = c.Depth.LightWeapons.Builder;
            Assert.AreEqual(b.AttackDamage, m.Damage, "the mallet is the Builder's swing as it was");
            Assert.AreEqual(b.AttackRange, m.Range);
            Assert.AreEqual(b.AttackArcDeg, m.ArcDeg);
            Assert.AreEqual(b.AttackCooldown, m.CooldownSeconds);
            ClassConfig war = c.Class(ClassId.Warrior);
            double warDps = war.AttackDamage / war.AttackCooldown;
            foreach (LightWeapon lw in new[] { c.Depth.LightWeapons.Demolitions, c.Depth.LightWeapons.Healer })
            {
                double share = lw.Damage / lw.CooldownSeconds / warDps;
                Assert.That(share, Is.InRange(0.25, 0.5), "about a third to a half of the Warrior's damage per second");
                Assert.Less(lw.Range, war.AttackRange, "shorter reach than the Warrior");
                Assert.Less(lw.Damage, c.Class(ClassId.Healer).MaxHp / 10.0, "never a big hit");
                Assert.Less(lw.Shove, 1.0, "a small step, not a throw");
            }
        }

        [Test]
        public void BotsPressTheLightWeaponOnlyAtCloseRange()
        {
            World w = Field(ClassId.Demolitions);
            LightWeapon lw = Row(w, ClassId.Demolitions);
            Assert.IsTrue(LightWeapons.BotKeepsPress(w, Me, X0 + lw.Range, Z0), "enemy at reach");
            Assert.IsFalse(LightWeapons.BotKeepsPress(w, Me, X0 + 3.0, Z0), "enemy 3 u away: no swing at air");
            Assert.IsFalse(LightWeapons.BotKeepsPress(w, Me, double.NaN, double.NaN), "no aim, no swing");

            World h = Field(ClassId.Healer);
            Assert.IsFalse(LightWeapons.BotKeepsPress(h, Me, X0 + 4.0, Z0), "healer stand-off, nobody to mend: hold");
            int ally = Ally(h, 0);
            Put(h, ally, X0 + 3.0, Z0);
            h.Actors[ally].Hp = 40;
            Assert.IsTrue(LightWeapons.BotKeepsPress(h, Me, X0 + 4.0, Z0), "a press that mends is always kept");

            World b = Field(ClassId.Builder);
            Assert.IsTrue(LightWeapons.BotKeepsPress(b, Me, X0 + 3.0, Z0), "the Builder's steering is unchanged");
            World off = Field(ClassId.Demolitions, on: false);
            Assert.IsTrue(LightWeapons.BotKeepsPress(off, Me, X0 + 3.0, Z0), "rule off: bots unchanged");
        }

        [Test]
        public void ConfigCloneCopiesTheBlock()
        {
            GameConfig c = GameConfig.CreateDefault();
            GameConfig d = c.Clone();
            d.Depth.LightWeapons.Healer.Damage = 99;
            Assert.AreNotEqual(99, c.Depth.LightWeapons.Healer.Damage);
        }
    }
}
