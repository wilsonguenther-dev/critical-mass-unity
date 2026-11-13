using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// fix/kaiju-alive-1 (2026-11-13): the Kaiju's hits land in the game's default rules. Fighters' Knockback ships off (D-030
    /// passive check, CM-64 berserk share), and it switched the Kaiju's throw and stagger off with it, so a claw or a slam
    /// was a number on a health bar. PhysicsConfig.KaijuKnockback gives the Kaiju's own hits their knock and stun back;
    /// PhysicsConfig.KaijuTopples lets the claw arc and the slam ring knock structures down through Topple.
    /// </summary>
    public sealed class KaijuHitsLandTests
    {
        static World Make(bool kaijuKnock, bool topples = true) => SimPhysicsTests.Make(c =>
        {
            c.Physics.Knockback = false;              // the shipped default for fighters
            c.Physics.KaijuKnockback = kaijuKnock;
            c.Physics.KaijuTopples = topples;
        });

        static void LockClaw(World w, double facing, int target)
        {
            Kaiju k = w.Kaiju;
            k.MeleeWindupIn = w.Dur.KaijuClawWindup;
            k.MeleeWindupLen = w.Dur.KaijuClawWindup;
            k.ClawX = k.X;
            k.ClawZ = k.Z;
            k.ClawFacing = facing;
            k.Facing = facing;
            k.MeleeTargetId = target;
        }

        static void RunClaw(World w)
        {
            int n = w.Kaiju.MeleeWindupIn;
            for (int t = 0; t < n + 1; t++)
            {
                SimPhysicsTests.Idle(w);
                SimStep.Step(w);
            }
        }

        [Test]
        public void TheClawThrowsAndStaggersWithFighterKnockbackOff()
        {
            World w = Make(true);
            SimPhysicsTests.HatchedKaiju(w);
            SimPhysicsTests.ParkOthers(w, 6);
            SimPhysicsTests.Place(w, 6, SimPhysicsTests.HillX + 1.8, SimPhysicsTests.HillZ);
            LockClaw(w, 0, 6);
            double hp = w.Actors[6].Hp;
            bool stunned = false;
            int n = w.Kaiju.MeleeWindupIn;
            for (int t = 0; t < n + 4; t++)
            {
                SimPhysicsTests.Idle(w);
                SimStep.Step(w);
                stunned |= w.Actors[6].HitStunTicks > 0;
            }
            Assert.Less(w.Actors[6].Hp, hp, "the claw hits");
            Assert.Greater(w.X[6], SimPhysicsTests.HillX + 2.3, "and throws the fighter back (KaijuClawKnock)");
            Assert.IsTrue(stunned, "and staggers it (KaijuStunSeconds)");
        }

        [Test]
        public void WithKaijuKnockbackOffTheClawOnlyDamages()
        {
            World w = Make(false);
            SimPhysicsTests.HatchedKaiju(w);
            SimPhysicsTests.ParkOthers(w, 6);
            SimPhysicsTests.Place(w, 6, SimPhysicsTests.HillX + 1.8, SimPhysicsTests.HillZ);
            LockClaw(w, 0, 6);
            double hp = w.Actors[6].Hp;
            RunClaw(w);
            Assert.Less(w.Actors[6].Hp, hp, "the claw hits");
            Assert.AreEqual(0, w.Actors[6].HitStunTicks, "no stagger");
            Assert.Less(w.X[6], SimPhysicsTests.HillX + 1.9, "no throw");
        }

        [Test]
        public void TheClawArcChipsAStructureAndEnoughStrikesKnockItDown()
        {
            World w = Make(true);
            Kaiju k = SimPhysicsTests.HatchedKaiju(w);
            SimPhysicsTests.ParkOthers(w);
            DecorationDef[] decs = w.Map.Decorations;
            Assume.That(decs != null && decs.Length > 0, "the map has no topple pieces");
            DecorationDef d = decs[0];
            // Stand the Kaiju 2 u west of the piece's base cell, facing it.
            double cx = d.Cx + 0.5, cz = d.Cz + 0.5;
            k.X = k.PrevX = cx - 2;
            k.Z = k.PrevZ = cz;
            int before = w.Topple.Hp[0];
            int hitEvents = 0, cursor = w.EventHead;
            for (int strike = 0; strike < before + 1 && w.Topple.Fallen[0] == Topple.Standing; strike++)
            {
                k.X = k.PrevX = cx - 2;
                k.Z = k.PrevZ = cz;
                LockClaw(w, 0, -1);
                RunClaw(w);
                for (; cursor < w.EventHead; cursor++)
                    if (w.Events[cursor % World.EventCapacity].Type == SimEventType.ToppleHit) hitEvents++;
            }
            Assert.Greater(hitEvents, 0, "the claw arc took hits off the piece");
            Assert.IsTrue(w.Topple.Fallen[0] != Topple.Standing || w.Topple.Hp[0] < before, "the piece fell or lost hits");
        }

        [Test]
        public void WithKaijuTopplesOffTheClawLeavesStructuresAlone()
        {
            World w = Make(true, false);
            Kaiju k = SimPhysicsTests.HatchedKaiju(w);
            SimPhysicsTests.ParkOthers(w);
            DecorationDef[] decs = w.Map.Decorations;
            Assume.That(decs != null && decs.Length > 0, "the map has no topple pieces");
            DecorationDef d = decs[0];
            k.X = k.PrevX = d.Cx + 0.5 - 2;
            k.Z = k.PrevZ = d.Cz + 0.5;
            int before = w.Topple.Hp[0];
            LockClaw(w, 0, -1);
            RunClaw(w);
            Assert.AreEqual(before, w.Topple.Hp[0]);
        }
    }
}
