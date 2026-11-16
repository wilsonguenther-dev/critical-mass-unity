using System;
using System.Collections.Generic;
using System.Text;
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
    /// (ModelView from the art catalog, the light-weapon prop attached as WeaponProps does), turned the way the game turns
    /// it for sim facing 0 (ModelView.SetFacing at SimSpace.YawDeg(0): the spec's ArtYawDeg included, 180 on the HY rigs),
    /// and measured by the game's own code (ActorAnimator.MeasureStrike) on the clip the animator plays, with the pose
    /// sampled STRAIGHT ONTO THE RIG (RigPose: Idle on every bone, then the attack clip on the "spine" subtree, the game's
    /// upper-body layer): the playable graph does not move the bones in EditMode. The reach is the measured tip reach at
    /// the view contact plus the view step-in cap (HitFeel.MaxViewOffset) plus the strike-lean gain (HitPose.LeanGain at
    /// MaxStrikeLeanDeg): the handoff doc's "cover". Each class prints
    ///   [CM.WeaponReach] &lt;class&gt;: measured reach X u, table Y u
    /// and a detail line (clip, sample time, facing, tip world position, every sample). The rows are re-pinned from those
    /// lines and must match within Tolerance. Sanity: all six between SaneMin and SaneMax, the Warrior's the longest.
    /// </summary>
    public sealed class WeaponReachTests
    {
        const double Tolerance = 0.05, SaneMin = 0.5, SaneMax = 2.2;

        static readonly ClassId[] Classes = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        /// <summary>Samples clips onto the rig the way the game layers a swing: idle legs, the clip on the spine subtree.</summary>
        sealed class RigPose
        {
            readonly ModelView _m;
            readonly ArtEntry _e;
            readonly Quaternion _facing;
            readonly Transform[] _lower;
            readonly Vector3[] _pos;
            readonly Quaternion[] _rot;
            readonly Vector3[] _scale;
            public readonly bool HasSpine;
            public int Samples;

            public RigPose(ModelView m, ArtEntry e, Quaternion facing)
            {
                _m = m;
                _e = e;
                _facing = facing;
                Transform spine = m.Bone(ModelView.UpperBodyRootBone);
                HasSpine = spine != null;
                var lower = new List<Transform>();
                foreach (Transform t in m.Root.GetComponentsInChildren<Transform>(true))
                    if (spine == null || (t != spine && !t.IsChildOf(spine))) lower.Add(t);
                _lower = lower.ToArray();
                _pos = new Vector3[_lower.Length];
                _rot = new Quaternion[_lower.Length];
                _scale = new Vector3[_lower.Length];
            }

            public bool Pose(string clip, double t)
            {
                AnimationClip a = _e.Clip(clip);
                if (a == null) return false;
                AnimationClip idle = _e.Clip(ArtMapping.Idle);
                if (idle != null) idle.SampleAnimation(_m.Root, 0f);
                for (int i = 0; i < _lower.Length; i++)
                {
                    _pos[i] = _lower[i].localPosition;
                    _rot[i] = _lower[i].localRotation;
                    _scale[i] = _lower[i].localScale;
                }
                a.SampleAnimation(_m.Root, (float)Math.Min(Math.Max(t, 0), a.length));
                if (HasSpine && _m.OnUpperLayer(clip))
                    for (int i = 0; i < _lower.Length; i++)
                    {
                        _lower[i].localPosition = _pos[i];
                        _lower[i].localRotation = _rot[i];
                        _lower[i].localScale = _scale[i];
                    }
                _m.Transform.position = Vector3.zero;
                _m.SetFacing(_facing);   // the game owns the root's turn (a clip may not)
                Samples++;
                return true;
            }
        }

        sealed class Result
        {
            public bool Ok;
            public double Measured, Table, Tip, Lever, Fact, View;
            public string Line, Detail;
        }

        static Result Measure(ClassId cls)
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
                // sim facing 0 (+x in sim, +x in world): the game's own turn, ArtYawDeg included
                Quaternion facing = Quaternion.Euler(0f, SimSpace.YawDeg(0), 0f);
                Vector3 fwd = SimSpace.ToWorld(1, 0, 0);
                m.Transform.position = Vector3.zero;
                m.SetFacing(facing);
                HeldWeapon kind = WeaponProps.Plan(cls, m.Spec);
                if (kind != HeldWeapon.None)
                    WeaponProps.Attach(m.Bone(WeaponProps.HandBone) ?? m.Bone(WeaponProps.HandBoneFallback), kind, TeamId.Azure, cat);
                string clip = AttackTiming.SwingClip(m, cls, cfg);
                Assert.IsNotNull(clip, cls + ": no attack clip");
                Assert.IsTrue(m.HasClip(clip), cls + ": model lacks " + clip);
                Assert.IsNotNull(entry.Clip(clip), cls + ": the catalog entry has no " + clip + " clip to sample");
                var rig = new RigPose(m, entry, facing);
                var trace = new StringBuilder();
                bool ok = ActorAnimator.MeasureStrike(m, cls, clip, fwd, out Transform hand, out Vector3 tipLocal, out string end,
                    out double fact, out double view, out double tip, out double lever, rig.Pose, trace);
                // b12-c: the whole clip, not only the contact window: does the striking end ever come forward, and does the
                // clip bind to this rig at all (curve paths that resolve under the model root)?
                var whole = new StringBuilder();
                double bestWhole = double.NegativeInfinity, bestWholeT = 0, handFwdMax = double.NegativeInfinity;
                float clipLen = m.ClipLength(clip);
                for (int k = 0; k <= 24 && hand != null; k++)
                {
                    double tt = clipLen * k / 24.0;
                    if (!rig.Pose(clip, tt)) break;
                    double fw = Vector3.Dot(hand.TransformPoint(tipLocal) - m.Transform.position, fwd);
                    double hf = Vector3.Dot(hand.position - m.Transform.position, fwd);
                    if (fw > bestWhole) { bestWhole = fw; bestWholeT = tt; }
                    if (hf > handFwdMax) handFwdMax = hf;
                    whole.Append(' ').Append(tt.ToString("0.00")).Append('=').Append(fw.ToString("0.00")).Append("/h").Append(hf.ToString("0.00"));
                }
                AnimationClip ac = entry.Clip(clip);
                EditorCurveBinding[] binds = ac != null ? AnimationUtility.GetCurveBindings(ac) : new EditorCurveBinding[0];
                var paths = new HashSet<string>();
                int resolved = 0, spineCurves = 0;
                Transform spineBone = m.Bone(ModelView.UpperBodyRootBone);
                foreach (EditorCurveBinding bnd in binds)
                {
                    if (!paths.Add(bnd.path)) continue;
                    Transform bt = bnd.path.Length == 0 ? m.Root.transform : m.Root.transform.Find(bnd.path);
                    if (bt == null) continue;
                    resolved++;
                    if (spineBone != null && (bt == spineBone || bt.IsChildOf(spineBone))) spineCurves++;
                }
                Debug.Log("[CM.WeaponReach] " + cls.ToString().ToLowerInvariant() + " whole clip: asset " + (ac != null ? AssetDatabase.GetAssetPath(ac) : "none") +
                    ", bound paths " + resolved + " of " + paths.Count + " (" + spineCurves + " under spine), striking end furthest forward " +
                    bestWhole.ToString("0.00") + " u at " + bestWholeT.ToString("0.000") + " s, hand furthest forward " + handFwdMax.ToString("0.00") +
                    " u; tip/hand forward per sample:" + whole);
                var r = new Result
                {
                    Ok = ok,
                    Tip = tip,
                    Lever = lever,
                    Fact = fact,
                    View = view,
                    Measured = tip + HitFeel.MaxViewOffset + HitPose.LeanGain(HitPose.MaxStrikeLeanDeg, lever),
                    Table = Combat.ReachOf(cfg, cls),
                };
                string n = cls.ToString().ToLowerInvariant();
                HitPose.StrikeEnds(m, cls, out _, out _, out string source);
                r.Line = "[CM.WeaponReach] " + n + ": measured reach " + r.Measured.ToString("0.00") + " u, table " + r.Table.ToString("0.00") + " u";
                r.Detail = "[CM.WeaponReach] " + n + " detail: ok " + ok + ", clip " + clip + " (" + m.ClipLength(clip).ToString("0.000") + " s, upper layer " +
                    m.OnUpperLayer(clip) + ", spine " + rig.HasSpine + "), samples " + rig.Samples + ", art yaw " + (m.Spec != null ? m.Spec.ArtYawDeg : 0f) +
                    ", root rotation " + m.Transform.rotation.eulerAngles.ToString("F1") + ", facing " + fwd.ToString("F2") + ", held " + kind +
                    ", tip from " + source + ", hand " + (hand != null ? hand.name : "none") + ", tip local " + tipLocal.ToString("F3") +
                    ", striking end " + end + ", contact " + fact.ToString("0.000") + " s, strike sample " + view.ToString("0.000") +
                    " s, tip forward " + tip.ToString("0.00") + " u, lever " + lever.ToString("0.00") + " u; samples:" + trace;
                Debug.Log(r.Line);
                Debug.Log(r.Detail);
                return r;
            }
            finally
            {
                m?.Dispose();
                Object.DestroyImmediate(holder);
            }
        }

        [TestCaseSource(nameof(Classes))]
        public void TheReachTableIsWhatTheWeaponReachesAtTheDrawnStrike(ClassId cls)
        {
            Result r = Measure(cls);
            Assert.IsTrue(r.Ok, cls + ": the strike could not be measured (hand, spine or clip missing): " + r.Detail);
            Assert.AreEqual(r.Table, r.Measured, Tolerance, cls + ": DepthConfig.WeaponReach row vs the measured reach (re-pin the row from the printed line)");
        }

        [Test]
        public void EveryMeasuredReachIsSaneAndTheWarriorsIsTheLongest()
        {
            var got = new Dictionary<ClassId, double>();
            foreach (ClassId cls in Classes)
            {
                Result r = Measure(cls);
                Assert.IsTrue(r.Ok, cls + ": not measured: " + r.Detail);
                got[cls] = r.Measured;
            }
            foreach (ClassId cls in Classes)
                Assert.That(got[cls], Is.InRange(SaneMin, SaneMax), cls + ": a weapon reach between " + SaneMin + " and " + SaneMax + " u (else the measure is broken)");
            foreach (ClassId cls in Classes)
                if (cls != ClassId.Warrior)
                    Assert.Greater(got[ClassId.Warrior], got[cls], "the Warrior's sword reaches further than the " + cls + "'s weapon");
        }
    }
}
