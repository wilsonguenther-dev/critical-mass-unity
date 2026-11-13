using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/kaiju-alive-1 (2026-11-13): "nothing glides" at the asset level. The Kaiju glided because the fighter-LOD pass
    /// re-exported Kaiju_Adult.fbx and Kaiju_Hatchling.fbx with the rig at its rest position, so all 8 clips imported as
    /// the rest pose (2 constant keys per curve): every check of names and counts passed, nothing measured motion. These
    /// tests sample every clip a model's state table needs on an instance of the imported model (AnimationClip
    /// .SampleAnimation, no graph, no culling) and fail when a clip is missing or does not move the bones.
    /// </summary>
    [Category("KaijuAlive")]
    public sealed class ClipMotionTests
    {
        /// <summary>A clip "moves" when some bone turns at least this far from the clip's first frame.</summary>
        public const float MinBoneTurnDeg = 3f;
        /// <summary>The egg's Idle is a gentle rock of its Shell bone (2.4 degrees as imported).</summary>
        public const float EggMinTurnDeg = 1f;

        /// <summary>The clips each fighter's ActorAnimator states play (locomotion, hit, death, get-up, jumps, carry, cheer,
        /// idle break), plus its class attack, ability and gather.</summary>
        static readonly string[] FighterCore =
        {
            ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run, ArtMapping.StrafeL, ArtMapping.StrafeR, ArtMapping.Hit,
            ArtMapping.Death, ArtMapping.GetUp, ArtMapping.JumpStart, ArtMapping.JumpAir, ArtMapping.JumpLand, ArtMapping.Carry,
            ArtMapping.Cheer, ArtMapping.IdleLook,
        };

        /// <summary>The Kaiju state table: idle, walk, the hatch/side roar, the slam telegraph and slam, the claw, berserk, KO.</summary>
        static readonly string[] KaijuTable =
        {
            ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Roar, ArtMapping.SlamWindup, ArtMapping.Slam, ArtMapping.Swipe,
            ArtMapping.Berserk, ArtMapping.KO,
        };

        static IEnumerable<(string key, string[] clips)> StateTables()
        {
            foreach (ClassId c in ClassIds.All)
            {
                string key = ArtMapping.ClassModel(c);
                if (key == null) continue;
                ArtMapping.ClassClipSet set = ArtMapping.ClassClips(c);
                var need = new List<string>(FighterCore) { set.Attack, set.Gather };
                if (set.Ability != null) need.Add(set.Ability);
                if (set.AbilityHold != null) need.Add(set.AbilityHold);
                yield return (key, need.ToArray());
            }
            yield return (ArtMapping.KaijuHatchling, KaijuTable);
            yield return (ArtMapping.KaijuAdult, KaijuTable);
            yield return (ArtMapping.KaijuEgg, new[] { ArtMapping.Idle, ArtMapping.Shake });
        }

        [Test]
        public void EveryModelHasTheClipsItsStateTableNeeds()
        {
            ArtCatalog cat = ArtCatalog.Load();
            Assert.IsNotNull(cat, "no ArtCatalog in Resources");
            var bad = new List<string>();
            foreach ((string key, string[] clips) in StateTables())
            {
                ArtEntry e = cat.Find(key);
                if (e == null) { bad.Add(key + ": no model in the catalog"); continue; }
                foreach (string c in clips)
                    if (e.Clip(c) == null) bad.Add(key + ": missing clip " + c);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void EveryStateClipMovesTheBones()
        {
            ArtCatalog cat = ArtCatalog.Load();
            Assert.IsNotNull(cat, "no ArtCatalog in Resources");
            var bad = new List<string>();
            var report = new System.Text.StringBuilder();
            foreach ((string key, string[] clips) in StateTables())
            {
                ArtEntry e = cat.Find(key);
                if (e == null) continue;
                GameObject go = Object.Instantiate(e.Model);
                try
                {
                    Transform[] bones = go.GetComponentsInChildren<Transform>(true);
                    report.Append(key).Append(':');
                    foreach (string c in clips)
                    {
                        AnimationClip clip = e.Clip(c);
                        if (clip == null) continue;
                        float turn = MaxTurnDeg(go, bones, clip, out string bone);
                        report.Append(' ').Append(c).Append(' ').Append(turn.ToString("0")).Append(" (").Append(bone).Append(')');
                        float floor = key == ArtMapping.KaijuEgg ? EggMinTurnDeg : MinBoneTurnDeg;   // the egg only rocks
                        if (turn < floor) bad.Add($"{key} {c}: the largest bone turn is {turn:0.0} deg ({bone}); the clip is static");
                    }
                    report.Append('\n');
                }
                finally { Object.DestroyImmediate(go); }
            }
            Debug.Log("[ClipMotion]\n" + report);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        /// <summary>The Kaiju Walk lifts each foot (a planted-foot stride exists to match the ground speed to).</summary>
        [Test]
        public void KaijuWalkLiftsBothFeet()
        {
            ArtCatalog cat = ArtCatalog.Load();
            foreach (string key in new[] { ArtMapping.KaijuHatchling, ArtMapping.KaijuAdult })
            {
                ArtEntry e = cat != null ? cat.Find(key) : null;
                if (e == null) { Assert.Ignore(key + " has no model"); return; }
                AnimationClip walk = e.Clip(ArtMapping.Walk);
                Assert.IsNotNull(walk, key + " Walk");
                GameObject go = Object.Instantiate(e.Model);
                try
                {
                    ModelSpec spec = ArtMapping.Spec(key);
                    float h = spec != null && spec.Height > 0 ? spec.Height : 2f;
                    foreach (string foot in new[] { "Foot_L", "Foot_R" })
                    {
                        Transform f = Find(go.transform, foot);
                        Assert.IsNotNull(f, key + " has no " + foot);
                        float min = float.PositiveInfinity, max = float.NegativeInfinity;
                        for (int k = 0; k <= 24; k++)
                        {
                            walk.SampleAnimation(go, walk.length * k / 24f);
                            float y = go.transform.InverseTransformPoint(f.position).y;
                            min = Mathf.Min(min, y);
                            max = Mathf.Max(max, y);
                        }
                        Assert.Greater(max - min, 0.02f * h, $"{key} {foot} never lifts in Walk ({max - min:0.000} u): the feet would slide");
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        static float MaxTurnDeg(GameObject go, Transform[] bones, AnimationClip clip, out string bone)
        {
            var first = new Quaternion[bones.Length];
            clip.SampleAnimation(go, 0f);
            for (int i = 0; i < bones.Length; i++) first[i] = bones[i].localRotation;
            float best = 0f;
            bone = "-";
            const int n = 12;
            for (int k = 1; k <= n; k++)
            {
                clip.SampleAnimation(go, clip.length * k / n);
                for (int i = 0; i < bones.Length; i++)
                {
                    float a = Quaternion.Angle(first[i], bones[i].localRotation);
                    if (a > best) { best = a; bone = bones[i].name; }
                }
            }
            return best;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = Find(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
