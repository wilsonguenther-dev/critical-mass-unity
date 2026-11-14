using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// feat/light-weapons-2: the light weapons seen. What each class model gets in its hand (and that nothing is doubled on
    /// models that carry their own), the attachment itself (parent, world size, shared mesh, stand-in marking, team-safe
    /// colours), the HUD words and the view's arc and shove reads.
    /// </summary>
    public sealed class WeaponPropsTests
    {
        [Test]
        public void EachClassModelGetsTheRightWeaponAndNothingIsDoubled()
        {
            Assert.IsTrue(ArtMapping.Spec(ArtMapping.Builder).HeldWeaponInMesh, "today's Builder has his mallet in the mesh");
            Assert.IsTrue(ArtMapping.Spec(ArtMapping.Healer).HeldWeaponInMesh, "today's Healer has her lantern in the mesh");
            Assert.IsFalse(ArtMapping.Spec(ArtMapping.Demolitions).HeldWeaponInMesh, "the Demolitions Expert has no weapon modelled");
            Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(ClassId.Builder, ArtMapping.Spec(ArtMapping.Builder)));
            Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(ClassId.Healer, ArtMapping.Spec(ArtMapping.Healer)));
            Assert.AreEqual(HeldWeapon.PryBarStandIn, WeaponProps.Plan(ClassId.Demolitions, ArtMapping.Spec(ArtMapping.Demolitions)));
            // A new class model (no flag) gets the prop attached.
            var fresh = new ModelSpec { Key = "NewModel" };
            Assert.AreEqual(HeldWeapon.Mallet, WeaponProps.Plan(ClassId.Builder, fresh));
            Assert.AreEqual(HeldWeapon.Lantern, WeaponProps.Plan(ClassId.Healer, fresh));
            Assert.AreEqual(HeldWeapon.PryBarStandIn, WeaponProps.Plan(ClassId.Demolitions, fresh));
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner })
                Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(c, fresh), c + " keeps its own weapon");
        }

        [TestCase(HeldWeapon.Mallet)]
        [TestCase(HeldWeapon.PryBarStandIn)]
        [TestCase(HeldWeapon.Lantern)]
        public void TheAttachmentRidesTheHandSlotAtWorldSize(HeldWeapon kind)
        {
            var root = new GameObject("Rig root");
            try
            {
                root.transform.localScale = Vector3.one * 0.01f; // a rig imported in centimetres
                var hand = new GameObject(WeaponProps.HandBone).transform;
                hand.SetParent(root.transform, false);
                Transform prop = WeaponProps.Attach(hand, kind, TeamId.Azure, null);
                Assert.IsNotNull(prop);
                Assert.AreSame(hand, prop.parent, "parented to the hand slot, so it follows the swing");
                Assert.AreEqual(1f, prop.lossyScale.y, 1e-3f, "world size whatever the rig scale");
                Assert.That(prop.name, Does.StartWith(WeaponProps.PropName));
                if (kind == HeldWeapon.PryBarStandIn) StringAssert.Contains("STAND-IN", prop.name, "the stand-in is marked");
                Renderer r = prop.GetComponentInChildren<Renderer>();
                Assert.IsNotNull(r, "drawn");
                hand.Rotate(0f, 0f, 90f);
                Assert.AreSame(hand, prop.parent);
            }
            finally { Object.DestroyImmediate(root); }
            Assert.IsNull(WeaponProps.Attach(null, HeldWeapon.Mallet, TeamId.Azure, null));
        }

        [Test]
        public void TheMalletShapeIsOneSmallSharedMeshInTeamSafeColours()
        {
            Mesh m = WeaponProps.MalletMesh();
            Assert.AreSame(m, WeaponProps.MalletMesh(), "built once, shared by every holder");
            Assert.Less(m.triangles.Length / 3, 200, "a few dozen triangles, not a model");
            Assert.AreEqual(WeaponProps.MalletLength, m.bounds.max.y, 0.05f, "handle along +Y up to the head");
            foreach (string hex in new[] { WeaponProps.MalletWoodHex, WeaponProps.PryBarIronHex })
            {
                Assert.AreNotEqual(Palette.Azure.ToLowerInvariant(), hex.ToLowerInvariant());
                Assert.AreNotEqual(Palette.Crimson.ToLowerInvariant(), hex.ToLowerInvariant());
            }
            Assert.AreNotEqual(WeaponProps.MalletWoodHex, WeaponProps.PryBarIronHex, "the stand-in reads as iron, not the mallet");
        }

        [Test]
        public void TheAttackSlotNamesTheWeapon()
        {
            Assert.AreEqual("MALLET", GameHud.AttackName((int)ClassId.Builder, false));
            Assert.AreEqual("PRY BAR", GameHud.AttackName((int)ClassId.Demolitions, false));
            Assert.AreEqual(HealerNames.Mend, GameHud.AttackName((int)ClassId.Healer, false), "someone to mend: MEND");
            Assert.AreEqual("LANTERN", GameHud.AttackName((int)ClassId.Healer, true), "nobody to mend: LANTERN");
            Assert.AreEqual("ATTACK", GameHud.AttackName((int)ClassId.Warrior, true));
            string[] slot = GameHud.AbilitySlotNames()[0];
            foreach (string word in new[] { "MALLET", "PRY BAR", "LANTERN" }) CollectionAssert.Contains(slot, word);
            CollectionAssert.DoesNotContain(slot, "NO SWORD");
        }

        [Test]
        public void TheHudSaysLanternOnlyWhenThePressWouldSwingIt()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 9, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Healer, Config = GameConfig.CreateDefault() });
            for (int j = 1; j < w.ActorCount; j++) w.Actors[j].Alive = false;
            var s = new HudState();
            HudState.Read(w, 0, s);
            Assert.IsTrue(s.HealerLantern, "nobody hurt in range");
            int ally = 1;
            Assert.AreEqual(w.Actors[0].Team, w.Actors[ally].Team);
            w.Actors[ally].Alive = true;
            w.Actors[ally].Hp = w.Actors[ally].MaxHp * 0.5;
            w.X[ally] = w.X[0] + 1;
            w.Z[ally] = w.Z[0];
            HudState.Read(w, 0, s);
            Assert.IsFalse(s.HealerLantern, "a hurt ally in range: the press mends");
            var copy = new HudState();
            s.HealerLantern = true;
            copy.CopyFrom(s);
            Assert.IsTrue(copy.HealerLantern);
        }

        [Test]
        public void TheSwingDrawsTheWeaponsOwnArcAndTheShoveIsRead()
        {
            GameConfig on = GameConfig.CreateDefault();
            Assert.AreEqual(on.Depth.LightWeapons.Healer.Range, LightWeaponView.Range(on, ClassId.Healer), 1e-9);
            Assert.AreEqual(on.Depth.LightWeapons.Demolitions.ArcDeg, LightWeaponView.ArcDeg(on, ClassId.Demolitions), 1e-9);
            Assert.AreEqual(on.Class(ClassId.Warrior).AttackArcDeg, LightWeaponView.ArcDeg(on, ClassId.Warrior), 1e-9);
            Assert.IsTrue(LightWeaponView.Shoves(on, true, ClassId.Builder));
            Assert.IsFalse(LightWeaponView.Shoves(on, true, ClassId.Warrior));
            Assert.IsFalse(LightWeaponView.Shoves(on, false, ClassId.Healer), "no attacker, no shove dust");
            GameConfig off = GameConfig.CreateBrowser5v5(); // depth rules off: the game before
            Assert.AreEqual(off.Class(ClassId.Healer).AttackRange, LightWeaponView.Range(off, ClassId.Healer), 1e-9);
            Assert.IsFalse(LightWeaponView.Shoves(off, true, ClassId.Demolitions));
        }
    }
}
