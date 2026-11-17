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
            // art/parts-characters: the clean-parts Healer has her lantern and the Demolitions Expert her pry bar modelled.
            Assert.IsTrue(ArtMapping.Spec(ArtMapping.Healer).HeldWeaponInMesh, "the parts Healer carries her lantern in the mesh");
            Assert.IsTrue(ArtMapping.Spec(ArtMapping.Demolitions).HeldWeaponInMesh, "the parts Demolitions Expert has her pry bar modelled");
            Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(ClassId.Builder, ArtMapping.Spec(ArtMapping.Builder)));
            Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(ClassId.Healer, ArtMapping.Spec(ArtMapping.Healer)));
            Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(ClassId.Demolitions, ArtMapping.Spec(ArtMapping.Demolitions)));
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

        /// <summary>
        /// b12-i (carried by b12-c2; Air look of build 36a581c: "no mallet in either hand"). The character shots hang the
        /// class's hand prop exactly as the game does (CharacterShotsCapture spawns ModelView directly, outside ActorViews,
        /// and used to shoot the Builder, Healer and Demolitions Expert empty-handed). b12-c2: the base has the OLD KayKit
        /// Builder, whose spec says HeldWeaponInMesh (his mallet is part of his mesh, rigid on handslot.r), so for that spec
        /// the truth is "no prop is hung, the mesh shows it"; for a Builder spec without the flag the prop hangs from the real
        /// weapon slot (handslot.r, not the hand.r fallback), is drawn, about one mallet long and points out of the fist.
        /// Prints which branch it checked.
        /// </summary>
        [Test]
        public void TheBuilderRigShowsHisMalletOnTheHandSlot()
        {
            ModelSpec spec = ArtMapping.Spec(ArtMapping.Builder);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath);
            Assert.IsNotNull(asset, spec.AssetPath + " is not imported");
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_Game/Runtime/View/Art/CharacterShotsCapture.cs"));
            StringAssert.Contains("HangHandProp(m, cls, team, _catalog);", src, "the character shots hang the hand prop as the game does");
            GameObject go = Object.Instantiate(asset);
            try
            {
                Transform hand = null;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == WeaponProps.HandBone) { hand = t; break; }
                Assert.IsNotNull(hand, "the Builder rig has the weapon slot " + WeaponProps.HandBone + " (not only the fallback)");
                if (spec.HeldWeaponInMesh)
                {
                    Debug.Log("[WeaponProps] Builder spec " + spec.AssetPath + ": HeldWeaponInMesh = true (the mallet is part of his mesh); "
                        + "Plan = " + WeaponProps.Plan(ClassId.Builder, spec) + " (limit None), so the shots and the game hang no prop on him");
                    Assert.AreEqual(HeldWeapon.None, WeaponProps.Plan(ClassId.Builder, spec), "the mesh carries the mallet; no second one is hung");
                    Assert.IsNull(CharacterShotsCapture.HangHandProp(null, ClassId.Builder, TeamId.Azure, null), "nothing hung without a model");
                    return;
                }
                Assert.AreEqual(HeldWeapon.Mallet, WeaponProps.Plan(ClassId.Builder, spec));
                Transform prop = WeaponProps.Attach(hand, HeldWeapon.Mallet, TeamId.Azure, null);
                Assert.IsNotNull(prop);
                Assert.AreSame(hand, prop.parent, "parented to the hand slot");
                Assert.IsTrue(prop.gameObject.activeInHierarchy, "active");
                Renderer r = prop.GetComponentInChildren<Renderer>();
                Assert.IsNotNull(r, "drawn");
                Assert.IsTrue(r.enabled, "renderer enabled");
                float len = r.bounds.size.magnitude;
                Transform fore = hand.parent != null ? hand.parent.parent : null;
                float dot = fore != null && (hand.position - fore.position).sqrMagnitude > 1e-6f
                    ? Vector3.Dot(prop.up, (hand.position - fore.position).normalized) : 1f;
                Debug.Log("[WeaponProps] Builder spec " + spec.AssetPath + ": HeldWeaponInMesh = false; mallet hung on " + hand.name
                    + ", bounds " + len.ToString("F2") + " u (limit > " + (WeaponProps.MalletLength * 0.8f).ToString("F2")
                    + "), handle-out dot " + dot.ToString("F2") + " (limit > -0.50)");
                Assert.Greater(len, WeaponProps.MalletLength * 0.8f, "about one mallet long in the world, not scaled away");
                Assert.Greater(dot, -0.5f, "the handle leaves the fist; it does not point back up the forearm into the sleeve");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheMalletShapeIsOneSmallSharedMeshInTeamSafeColours()
        {
            Mesh m = WeaponProps.MalletMesh();
            Assert.AreSame(m, WeaponProps.MalletMesh(), "built once, shared by every holder");
            Assert.Less(m.triangles.Length / 3, 200, "a few dozen triangles, not a model");
            Assert.AreEqual(WeaponProps.MalletLength, m.bounds.max.y, 0.05f, "handle along +Y up to the head");
            Assert.AreEqual(2, m.subMeshCount, "b12-c2 view: handle and head are their own submeshes");
            Debug.Log("[WeaponProps] mallet head " + WeaponProps.MalletHeadHex + ", handle " + WeaponProps.MalletHandleHex + ", " + (m.triangles.Length / 3) +
                " triangles (limit < 200), submeshes " + m.subMeshCount + " (limit 2)");
            foreach (string hex in new[] { WeaponProps.MalletHeadHex, WeaponProps.MalletHandleHex, WeaponProps.PryBarIronHex })
            {
                Assert.AreNotEqual(Palette.Azure.ToLowerInvariant(), hex.ToLowerInvariant());
                Assert.AreNotEqual(Palette.Crimson.ToLowerInvariant(), hex.ToLowerInvariant());
            }
            Assert.AreNotEqual(WeaponProps.MalletHeadHex, WeaponProps.PryBarIronHex, "the stand-in reads as iron, not the mallet");
            Assert.AreNotEqual(WeaponProps.MalletHeadHex, WeaponProps.MalletHandleHex, "head and handle read apart");
        }

        [Test]
        public void ThePryBarIsAForearmLongHookedIronBarOfFourBoxes()
        {
            // fix/light-weapons-3: the tinted mallet read as a small blob; the stand-in is now its own bar.
            Mesh m = WeaponProps.PryBarMesh();
            Assert.AreSame(m, WeaponProps.PryBarMesh(), "built once, shared");
            Assert.AreNotSame(WeaponProps.MalletMesh(), m, "no longer the mallet shape");
            Assert.AreEqual(WeaponProps.PryBarBoxes * 12, m.triangles.Length / 3, "four boxes, 48 triangles");
            Bounds b = m.bounds;
            Assert.AreEqual(WeaponProps.PryBarLength, b.max.y, 0.08f, "about forearm length along the grip axis");
            Assert.Greater(b.size.z, WeaponProps.PryBarThick * 2f, "the hooked end sticks out to one side");
            Assert.Less(b.size.x, WeaponProps.PryBarThick * 2f, "a bar, not a head");
            var root = new GameObject("hand");
            Transform prop = WeaponProps.Attach(root.transform, HeldWeapon.PryBarStandIn, TeamId.Azure, null);
            Assert.AreSame(m, prop.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TheWeaponCaptureKeepsItsDummyPassiveAndTheBannerOff()
        {
            // fix/light-weapons-3: the dummy hit back (the attacker flashed white) and the start banner covered the shots.
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_Game/Runtime/View/Anim/WeaponCapture.cs"));
            StringAssert.Contains("KeepDummyPassive(w);", src);
            StringAssert.Contains("FirstMinutes.StartBannerOff = true", src);
            StringAssert.Contains("Banner.ClearAll()", src);
            StringAssert.Contains("if (!FirstMinutes.StartBannerOff)", System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "_Game/Runtime/Hud/YouMarker.cs")));
        }

        [Test]
        public void TheAttackSlotNamesTheWeapon()
        {
            Assert.AreEqual("MALLET", GameHud.AttackName((int)ClassId.Builder, false));
            Assert.AreEqual("PRY BAR", GameHud.AttackName((int)ClassId.Demolitions, false), "TNT reloading: the pry bar swings");
            Assert.AreEqual("TNT", GameHud.AttackName((int)ClassId.Demolitions, false, true), "b12 r3: TNT ready, her attack throws it");
            Assert.AreEqual(HealerNames.Mend, GameHud.AttackName((int)ClassId.Healer, false), "someone to mend: MEND");
            Assert.AreEqual("LANTERN", GameHud.AttackName((int)ClassId.Healer, true), "nobody to mend: LANTERN");
            Assert.AreEqual("ATTACK", GameHud.AttackName((int)ClassId.Warrior, true));
            string[] slot = GameHud.AbilitySlotNames()[0];
            foreach (string word in new[] { "MALLET", "PRY BAR", "LANTERN", "TNT" }) CollectionAssert.Contains(slot, word);
            CollectionAssert.DoesNotContain(slot, "NO SWORD");
        }

        [Test]
        public void TheHudSaysLanternOnlyWhenThePressWouldSwingIt()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 9, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Healer, Config = GameConfig.CreateDefault() });
            for (int j = 1; j < w.ActorCount; j++) w.Actors[j].Alive = false;
            var s = new HudState();
            HudBinder.Read(w, 0, s);
            Assert.IsTrue(s.HealerLantern, "nobody hurt in range");
            int ally = 1;
            Assert.AreEqual(w.Actors[0].Team, w.Actors[ally].Team);
            w.Actors[ally].Alive = true;
            w.Actors[ally].Hp = w.Actors[ally].MaxHp * 0.5;
            w.X[ally] = w.X[0] + 1;
            w.Z[ally] = w.Z[0];
            HudBinder.Read(w, 0, s);
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
