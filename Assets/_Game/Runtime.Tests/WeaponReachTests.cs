using System;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b11-c (RULE CHANGE): the sim's swing reach table (DepthConfig.WeaponReach, Combat.ReachOf) must be what each class's
    /// imported model really reaches at the drawn strike. Per class the model is built the way the game builds it
    /// (ModelView from the art catalog, the light-weapon prop attached as WeaponProps does), the clip the animator plays is
    /// measured by the game's own code (ActorAnimator.MeasureStrike on AttackTiming.PoseDrawn: idle legs, the clip on the
    /// upper-body layer; the Healer at her lantern hand) and the reach is the measured tip reach at the view contact plus
    /// the view step-in cap (HitFeel.MaxViewOffset) plus the strike-lean gain (HitPose.LeanGain at MaxStrikeLeanDeg): the
    /// handoff doc's "cover". Each class prints one line "[CM.WeaponReach] &lt;class&gt;: measured reach X u, table Y u";
    /// the table rows are re-pinned from those lines, and must match within Tolerance.
    /// </summary>
    public sealed class WeaponReachTests
    {
        const double Tolerance = 0.05;

        static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        [TestCaseSource(nameof(Classes))]
        public void TheReachTableIsWhatTheWeaponReachesAtTheDrawnStrike(ClassId cls)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " (run ProjectSetup.Apply)");
            string key = ArtMapping.ClassModel(cls);
            ArtEntry entry = cat.Find(key);
            Assert.IsNotNull(entry, key + " not in the catalog");
            var holder = new GameObject("WeaponReachTests " + cls);
            ModelView m = null;
            try
            {
                m = new ModelView(entry, holder.transform, "reach " + cls);
                m.Transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                HeldWeapon kind = WeaponProps.Plan(cls, m.Spec);
                if (kind != HeldWeapon.None)
                    WeaponProps.Attach(m.Bone(WeaponProps.HandBone) ?? m.Bone(WeaponProps.HandBoneFallback), kind, TeamId.Azure, cat);
                string clip = AttackTiming.SwingClip(m, cls, cfg);
                Assert.IsNotNull(clip, cls + ": no attack clip");
                Assert.IsTrue(m.HasClip(clip), cls + ": model lacks " + clip);
                bool ok = ActorAnimator.MeasureStrike(m, cls, clip, m.Transform.forward, out Transform hand, out _, out string end,
                    out double fact, out double view, out double tip, out double lever);
                double measured = tip + HitFeel.MaxViewOffset + HitPose.LeanGain(HitPose.MaxStrikeLeanDeg, lever);
                double table = Combat.ReachOf(cfg, cls);
                string n = cls.ToString().ToLowerInvariant();
                Debug.Log("[CM.WeaponReach] " + n + ": measured reach " + measured.ToString("0.00") + " u, table " + table.ToString("0.00") + " u");
                Debug.Log("[CM.WeaponReach] " + n + " detail: clip " + clip + ", hand " + (hand != null ? hand.name : "none") + ", striking end " + end +
                    ", contact " + fact.ToString("0.000") + " s, view contact " + view.ToString("0.000") + " s, tip forward " + tip.ToString("0.00") +
                    " u, lever " + lever.ToString("0.00") + " u, held " + kind);
                Assert.IsTrue(ok, cls + ": the strike could not be measured (hand, spine or clip missing, or the graph did not run)");
                Assert.AreEqual(table, measured, Tolerance, cls + ": DepthConfig.WeaponReach row vs the measured reach (re-pin the row from the printed line)");
            }
            finally
            {
                m?.Dispose();
                Object.DestroyImmediate(holder);
            }
        }
    }
}
