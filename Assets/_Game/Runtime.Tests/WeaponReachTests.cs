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

            public bool Pose(string clip, double t) => PoseClip(_e.Clip(clip), t, _m.OnUpperLayer(clip));

            /// <summary>b12-c2: any clip (another rig's clip pack too) on this rig, upper-body layered when upper.</summary>
            public bool PoseClip(AnimationClip a, double t, bool upper)
            {
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
                if (HasSpine && upper)
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

        /// <summary>
        /// b12-c2 (PR #177 point 1, the Demolitions pry bar that never comes forward): separates the code-side causes from
        /// an art one and prints a verdict line "[CM.WeaponReach] demolitions verdict: ...". Code causes, asserted: the
        /// attack clip not on the upper-body layer, the wrong clip picked by name (not her own Demolitions_Clips pack), the
        /// bar not on the weapon slot handslot.r. Printed: the bar's angle to her forearm and its sideways share at the
        /// strike (an attach turned sideways), her hand's forward travel over her own Attack clip, and the same for the
        /// shared KayKit Attack (the Builder's pack) sampled onto HER rig. Her own clip barely moving the hand forward while
        /// the KayKit clip does on the same rig = ART: her baked Attack clip (X2 measures and re-bakes); the frames are printed.
        /// </summary>
        [Test]
        public void TheDemolitionsBarComesForwardOrTheCauseIsNamed()
        {
            const double TravelArt = 0.25, TravelRef = 0.40;
            GameConfig cfg = GameConfig.CreateDefault();
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " (run ProjectSetup.Apply)");
            ArtEntry entry = cat.Find(ArtMapping.Demolitions), kay = cat.Find(ArtMapping.Builder);
            Assert.IsNotNull(entry, "Demolitions not in the catalog");
            var holder = new GameObject("WeaponReachTests demolitions verdict");
            ModelView m = null;
            try
            {
                m = new ModelView(entry, holder.transform, "verdict demolitions");
                Quaternion facing = Quaternion.Euler(0f, SimSpace.YawDeg(0), 0f);
                Vector3 fwd = SimSpace.ToWorld(1, 0, 0);
                m.Transform.position = Vector3.zero;
                m.SetFacing(facing);
                Transform slot = m.Bone(WeaponProps.HandBone), hand = m.Bone(WeaponProps.HandBoneFallback);
                Transform bar = WeaponProps.Attach(slot ?? hand, WeaponProps.Plan(ClassId.Demolitions, m.Spec), TeamId.Azure, cat);
                string clip = AttackTiming.SwingClip(m, ClassId.Demolitions, cfg);
                AnimationClip own = entry.Clip(clip);
                AnimationClip kayAttack = kay != null ? kay.Clip(ArtMapping.Attack) : null;
                string ownPath = own != null ? AssetDatabase.GetAssetPath(own) : "none";
                bool upper = m.OnUpperLayer(clip);
                var rig = new RigPose(m, entry, facing);
                Transform arm = hand != null ? hand.parent : null;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                double ownMin, ownMax, ownTipMax, ownMinT, ownMaxT, kayMin, kayMax, kayTipMax, kayMinT, kayMaxT;
                var ownTrace = new StringBuilder();
                var kayTrace = new StringBuilder();
                Travel(rig, own, upper, hand, bar, fwd, m, ownTrace, out ownMin, out ownMinT, out ownMax, out ownMaxT, out ownTipMax);
                Travel(rig, kayAttack, upper, hand, bar, fwd, m, kayTrace, out kayMin, out kayMinT, out kayMax, out kayMaxT, out kayTipMax);
                // the bar at the fact contact of her own clip: angle to the forearm, sideways share of its axis
                double contact = ClipFacts.Contact(clip, own != null ? own.length * 0.55f : 0.5f);
                rig.PoseClip(own, contact, upper);
                double armAngle = bar != null && arm != null ? Vector3.Angle(bar.up, (hand.position - arm.position).normalized) : double.NaN;
                double sideways = bar != null ? Math.Abs(Vector3.Dot(bar.up, right)) : double.NaN;
                double fps = own != null && own.frameRate > 0 ? own.frameRate : 30;
                double kfps = kayAttack != null && kayAttack.frameRate > 0 ? kayAttack.frameRate : 30;
                double ownTravel = ownMax - ownMin, kayTravel = kayMax - kayMin;
                string verdict;
                if (!upper) verdict = "CODE: her attack clip is not on the upper-body layer";
                else if (own == null || !ownPath.Contains("Demolitions_Clips")) verdict = "CODE: the clip picked by name is not her own pack (" + ownPath + ")";
                else if (slot == null || bar == null || bar.parent != slot) verdict = "CODE: the bar is not on " + WeaponProps.HandBone;
                else if (ownTravel < TravelArt && kayTravel >= TravelRef)
                    verdict = "ART (X2): her Attack clip " + ownPath + " moves her hand forward only " + ownTravel.ToString("0.00") + " u (" +
                        ownMin.ToString("0.00") + " at frame " + (ownMinT * fps).ToString("0") + " to " + ownMax.ToString("0.00") + " at frame " +
                        (ownMaxT * fps).ToString("0") + ", " + fps.ToString("0") + " fps); the shared KayKit Attack on HER rig moves it " +
                        kayTravel.ToString("0.00") + " u (frames " + (kayMinT * kfps).ToString("0") + " to " + (kayMaxT * kfps).ToString("0") + "): re-bake her Attack";
                else if (ownTravel < TravelArt) verdict = "ART or RIG: neither her clip (" + ownTravel.ToString("0.00") + " u) nor the KayKit Attack (" + kayTravel.ToString("0.00") + " u) moves her hand forward on her rig";
                else if (ownTipMax < ownMax + 0.1) verdict = "ATTACH: her hand comes forward " + ownTravel.ToString("0.00") + " u but the bar tip only to " + ownTipMax.ToString("0.00") + " u (bar to forearm " + armAngle.ToString("0") + " deg, sideways share " + sideways.ToString("0.00") + ")";
                else verdict = "OK: the bar comes forward, tip furthest " + ownTipMax.ToString("0.00") + " u, hand travel " + ownTravel.ToString("0.00") + " u";
                Debug.Log("[CM.WeaponReach] demolitions verdict: " + verdict);
                Debug.Log("[CM.WeaponReach] demolitions verdict detail: clip " + clip + " from " + ownPath + ", upper layer " + upper + ", slot " +
                    (slot != null ? slot.name : "MISSING") + ", bar parent " + (bar != null && bar.parent != null ? bar.parent.name : "none") +
                    ", bar to forearm at contact " + armAngle.ToString("0") + " deg, sideways share " + sideways.ToString("0.00") + " (limit 0.70)" +
                    ", own hand forward " + ownMin.ToString("0.00") + ".." + ownMax.ToString("0.00") + " (limit travel " + TravelArt + "), tip max " + ownTipMax.ToString("0.00") +
                    "; KayKit Attack on her rig " + (kayAttack != null ? "hand " + kayMin.ToString("0.00") + ".." + kayMax.ToString("0.00") + ", tip max " + kayTipMax.ToString("0.00") : "not found") +
                    "; own t=hand/tip:" + ownTrace + "; kaykit t=hand/tip:" + kayTrace);
                Assert.IsTrue(upper, "her attack clip plays on the upper-body layer");
                Assert.That(ownPath, Does.Contain("Demolitions_Clips"), "her own baked clip pack is the one picked by name");
                Assert.IsNotNull(slot, "her rig has the weapon slot " + WeaponProps.HandBone);
                Assert.AreSame(slot, bar != null ? bar.parent : null, "the bar hangs from " + WeaponProps.HandBone);
            }
            finally
            {
                m?.Dispose();
                Object.DestroyImmediate(holder);
            }
        }

        static void Travel(RigPose rig, AnimationClip a, bool upper, Transform hand, Transform bar, Vector3 fwd, ModelView m, StringBuilder trace,
            out double min, out double minT, out double max, out double maxT, out double tipMax)
        {
            min = double.PositiveInfinity; max = double.NegativeInfinity; tipMax = double.NegativeInfinity; minT = maxT = 0;
            if (a == null || hand == null) { min = max = tipMax = double.NaN; return; }
            for (int k = 0; k <= 24; k++)
            {
                double t = a.length * k / 24.0;
                if (!rig.PoseClip(a, t, upper)) break;
                double h = Vector3.Dot(hand.position - m.Transform.position, fwd);
                double tip = double.NaN;
                if (bar != null)
                {
                    MeshFilter mf = bar.GetComponentInChildren<MeshFilter>();
                    Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.up * WeaponProps.PryBarLength, Vector3.zero);
                    tip = Vector3.Dot(mf != null ? mf.transform.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)) - m.Transform.position : Vector3.zero, fwd);
                    if (tip > tipMax) tipMax = tip;
                }
                if (h < min) { min = h; minT = t; }
                if (h > max) { max = h; maxT = t; }
                trace.Append(' ').Append(t.ToString("0.00")).Append('=').Append(h.ToString("0.00")).Append('/').Append(tip.ToString("0.00"));
            }
        }

        /// <summary>
        /// b12-c2 (hit-feel capture): HitFeelCapture.DummyAhead puts the dummy's body SURFACE at the class's sim reach
        /// (Combat.ReachOf, Full) minus HitFeelCapture.SurfaceInset (0.02 u) for every class row and every class's body
        /// radius as the dummy: inside the reach (the hit lands) and at its edge. Prints one line per attacker class.
        /// </summary>
        [Test]
        public void TheHitFeelDummySurfaceStandsAtTheSimReachMinusTwoHundredths()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (ClassId cls in Classes)
            {
                double reach = Combat.ReachOf(cfg, cls), worst = 0;
                foreach (ClassId dummy in Classes)
                {
                    double radius = cfg.Class(dummy).Radius;
                    double ahead = HitFeelCapture.DummyAhead(reach, radius);
                    double surface = HitFeelCapture.DummySurface(ahead, radius);
                    worst = Math.Max(worst, Math.Abs(surface - (reach - 0.02)));
                    Assert.AreEqual(reach - 0.02, surface, 1e-9, cls + " vs a " + dummy + " dummy: surface at reach - 0.02 u");
                    Assert.Less(surface, reach, cls + ": inside the reach, the hit lands");
                    Assert.AreEqual(ahead, surface + radius, 1e-9, "the centre is the surface plus the dummy's radius");
                }
                Debug.Log("[CM.HitFeel] " + cls.ToString().ToLowerInvariant() + ": sim reach " + reach.ToString("0.00") + " u, dummy surface placed at " +
                    (reach - HitFeelCapture.SurfaceInset).ToString("0.00") + " u (limit reach - 0.02), worst error over the six dummy radii " + worst.ToString("E1"));
            }
        }
    }
}
