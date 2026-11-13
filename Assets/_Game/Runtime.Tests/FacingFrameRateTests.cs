using System;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Anim;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// fix/facing-frame-timing (2026-11-13): the facing spring (ActorAnimator.StepFacing, DG-89) on a fake clock. A view
    /// update steps it by the sim time since the last one, which is 1/144 s of wall time at pace 1 on a fast machine and
    /// 0.5 sim s after a 250 ms hitch at pace 2. At every such clock the drawn yaw must reach the sim facing within 30
    /// degrees in 0.15 sim s and within 1 degree in 0.3 sim s, move toward it every step (never the long way round; the
    /// momentum of an unfinished turn may carry it at most 2 degrees further), and every clock must end on the same facing.
    /// </summary>
    public sealed class FacingFrameRateTests
    {
        /// <summary>Sim seconds per view update: pace x wall frame time.</summary>
        static IEnumerable<KeyValuePair<string, Func<int, double>>> Clocks()
        {
            foreach (double pace in new[] { 1.0, 2.0 })
            {
                foreach (double fps in new[] { 10.0, 20.0, 30.0, 60.0, 144.0 })
                {
                    double d = pace / fps;
                    yield return new KeyValuePair<string, Func<int, double>>($"{fps} fps at pace {pace}", n => d);
                }
                double p = pace;
                double[] irregular = { 1 / 144.0, 1 / 60.0, 0.021, 1 / 30.0, 0.045, 0.05, 0.07, 0.1, 1 / 90.0 };
                // The driver clamps one frame to SimClock.MaxDelta (0.25 s) of wall time: the longest view step is 0.25 x pace.
                yield return new KeyValuePair<string, Func<int, double>>($"irregular with 300 ms hitches at pace {pace}",
                    n => p * (n % 17 == 16 ? 0.25 : irregular[ActorAnimator.Hash(n, 7) % irregular.Length]));
            }
        }

        static double Err(double yaw, double target)
        {
            double d = (yaw - target) % 360.0;
            if (d > 180) d -= 360;
            else if (d < -180) d += 360;
            return Math.Abs(d);
        }

        [Test]
        public void TheDrawnYawReachesTheSimFacingTheShortWayAtEveryFrameRate()
        {
            // From rest on 0: the 8 screen directions' turns, a full reversal both ways, and reversals while still turning.
            double[] targets = { 45, 90, 135, 180, -135, -90, -45, 0, 179.9, -179.9, 180, 0, 90, -90, 135, -45, 180 };
            var fails = new List<string>();
            var finals = new Dictionary<string, double>();
            foreach (KeyValuePair<string, Func<int, double>> clock in Clocks())
            {
                double yaw = 0, vel = 0, t = 0;
                int n = 0;
                for (int leg = 0; leg < targets.Length; leg++)
                {
                    double target = targets[leg], start = Err(yaw, target), prev = start, since = 0;
                    // Half the legs change target again before the turn has settled (0.12 sim s), the others hold 0.6 sim s.
                    double hold = leg % 2 == 0 ? 0.6 : 0.12;
                    while (since < hold)
                    {
                        double dt = clock.Value(n++);
                        ActorAnimator.StepFacing(ref yaw, ref vel, target, dt);
                        since += dt;
                        t += dt;
                        double e = Err(yaw, target);
                        if (e > prev + 2.0)
                            fails.Add($"{clock.Key}, turn {leg} to {target}: the error grew {prev:0.0} -> {e:0.0} at {since:0.000} s (turned away)");
                        if (e > start + 2.0)
                            fails.Add($"{clock.Key}, turn {leg} to {target}: {e:0.0} degrees off, more than the {start:0.0} it started from");
                        if (since >= 0.15 && e > 30)
                            fails.Add($"{clock.Key}, turn {leg} to {target}: still {e:0.0} degrees off after {since:0.000} sim s");
                        if (since >= 0.3 && e > 1)
                            fails.Add($"{clock.Key}, turn {leg} to {target}: still {e:0.0} degrees off after {since:0.000} sim s");
                        prev = e;
                    }
                }
                finals[clock.Key] = yaw;
                if (double.IsNaN(yaw) || double.IsInfinity(vel)) fails.Add($"{clock.Key}: the spring blew up ({yaw}, {vel})");
            }
            foreach (KeyValuePair<string, double> kv in finals)
                if (Err(kv.Value, targets[targets.Length - 1]) > 1) fails.Add($"{kv.Key}: ends {kv.Value:0.0}, not on the last target");
            CollectionAssert.IsEmpty(fails, string.Join("\n", fails.GetRange(0, Math.Min(16, fails.Count))));
        }
            /// <summary>
        /// The facing cue reads the planted foot by its ANKLE (fix/facing-frame-timing). Just after toe-off the trailing
        /// foot's heel is up and its toes hang behind and lowest; the old lower-toe rule read that foot (toes backwards) as
        /// the front of a correctly drawn runner. The rule must still read a model drawn turned around as turned around.
        /// </summary>
        [Test]
        public void ThePlantedFootIsTheLowerAnkleAndATurnedAroundModelStillReadsBackwards()
        {
            var root = new GameObject("cue rig");
            try
            {
                Transform hips = Bone("hips", root.transform, new Vector3(0f, 0.9f, 0f));
                // Stance (landing) foot: ankle low, toes ahead (+Z) on the ground.
                Transform fr = Bone("foot.r", hips, new Vector3(0.1f, -0.82f, 0.05f));
                Bone("toes.r", fr, new Vector3(0f, -0.05f, 0.12f));
                // Trailing foot after toe-off: heel up behind, toes hanging back and 5 mm LOWER than the stance toes.
                Transform fl = Bone("foot.l", hips, new Vector3(-0.1f, -0.70f, -0.35f));
                Bone("toes.l", fl, new Vector3(0f, -0.175f, -0.06f));
                Assert.Less(SanityRig.Find(root.transform, "toes.l").position.y, SanityRig.Find(root.transform, "toes.r").position.y,
                    "fixture: the trailing toes are the lowest point");
                Assert.IsTrue(SanityRig.TryFront(root.transform, out Vector3 front, out string cue), cue);
                Assert.LessOrEqual(SanityRig.AngleDeg(front, Vector3.forward), 5f, "a correctly drawn runner reads forward (" + cue + ")");
                root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Assert.IsTrue(SanityRig.TryFront(root.transform, out front, out cue), cue);
                Assert.GreaterOrEqual(SanityRig.AngleDeg(front, Vector3.forward), 175f, "a model drawn turned around reads turned around");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Transform Bone(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }
    }
}
