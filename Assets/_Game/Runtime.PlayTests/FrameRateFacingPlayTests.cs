using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// fix/facing-frame-timing (2026-11-13): the drawn facing at ANY frame rate. FacingSanity found the Farmer drawn 114 to
    /// 156 degrees off its sim facing on GitHub's slow runners and never on the fast Mac, and the angle changed from run to
    /// run with the start tick pinned: the drawn pose depended on frame timing. These tests run the real game loop (the
    /// driver's clock, one ActorViews.Sync per frame in LateUpdate, the post-pose hooks) on a fake clock
    /// (Time.captureDeltaTime) at 10, 20, 30, 60 and 144 frames per second and on an irregular clock with 300 ms hitches,
    /// for every class with a model, running in all 8 screen directions without aim and again while aiming screen up.
    /// <para>What is promised, per frame (the view drawn in frame N is read at frame N+1's resume, against the sim state
    /// it was drawn from): (1) the model root faces the sim facing within 30 degrees once 0.3 sim s have passed since the
    /// direction changed; (2) never the long way round: the root's error to the sim facing never grows by more than the sim
    /// facing itself turned since the last frame (a turned-around model is caught on the frame it turns away); (3) the
    /// locomotion's travel angle matches the drawn velocity within 30 degrees (no backwards stride); (4) the planted toes
    /// point along the sim facing turned by the animator's leg yaw within 30 degrees in at least 95 % of the moving samples
    /// (the planted-toe cue's own misreads, as in EveryMovingBotModelFacesItsSimFacing's 5 % budget).</para>
    /// </summary>
    public sealed class FrameRateFacingPlayTests
    {
        const float ToleranceDeg = 30f;
        const double SettleSim = 0.3, DwellSim = 0.55, MinSpeed = 1.0;

        /// <summary>A frame clock: the dt (wall seconds) of frame n.</summary>
        sealed class Schedule
        {
            public readonly string Name;
            readonly Func<int, float> _dt;
            public Schedule(string name, Func<int, float> dt) { Name = name; _dt = dt; }
            public float Dt(int n) => _dt(n);
            public override string ToString() => Name;
        }

        static readonly float[] Irregular = { 1f / 144f, 1f / 60f, 0.021f, 1f / 30f, 0.045f, 1f / 20f, 0.07f, 0.1f, 1f / 90f };

        static readonly Schedule[] Schedules =
        {
            new Schedule("10 fps", n => 0.1f),
            new Schedule("20 fps", n => 0.05f),
            new Schedule("30 fps", n => 1f / 30f),
            new Schedule("60 fps", n => 1f / 60f),
            new Schedule("144 fps", n => 1f / 144f),
            // Deterministic irregular frames (a hash of n), and a single 300 ms hitch every 17th frame.
            new Schedule("irregular with 300 ms hitches", n => n % 17 == 16 ? 0.3f : Irregular[ActorAnimator.Hash(n, 7) % Irregular.Length]),
        };

        public static IEnumerable<ClassId> AllClasses() => ClassIds.All;

        SanityWorld _w;

        [TearDown]
        public void TearDown()
        {
            SanityWorld.Reset();
            _w = null;
        }

        struct Snap
        {
            public bool Alive;
            public double SimTime;
            public float SimYaw;
            public string Leg;
        }

        [UnityTest]
        public IEnumerator DrawnFacingConvergesAndNeverTurnsTheLongWayAtAnyFrameRate([ValueSource(nameof(AllClasses))] ClassId cls)
        {
            if (!SanityWorld.HasModel(ArtMapping.ClassModel(cls))) Assert.Ignore(cls + " has no imported model (grey box): nothing to face");
            _w = new SanityWorld();
            yield return _w.Load(cls);
            double pace = _w.Driver.Request != null && _w.Driver.Request.Pace > 0 ? _w.Driver.Request.Pace : _w.Cfg.DefaultSpeed;
            double hz = _w.Cfg.SimHz;
            HumanInputState inp = _w.HumanInput;
            var bad = new List<string>();
            var summary = new List<string>();
            int measuredAll = 0;
            _w.Driver.TimeScale = pace;
            try
            {
                foreach (Schedule sch in Schedules)
                {
                    int frame = 0, samples = 0, toeSamples = 0, toeOff = 0, hits = 0, dead = 0;
                    float worstToe = 0f, worstRoot = 0f;
                    string worstToeAt = null;
                    bool hasPrev = false;
                    float prevErr = 0f, prevSimYaw = 0f;
                    for (int aimed = 0; aimed < 2; aimed++)
                    {
                        SanityWorld.FromScreenIntent(0, 1, out double ax, out double az);
                        for (int k = 0; k < 8; k++)
                        {
                            double ang = k * Math.PI / 4;
                            inp.SetMove(Math.Cos(ang), Math.Sin(ang));
                            if (aimed == 0) inp.ClearAim();
                            string leg = sch.Name + ", running " + SanityWorld.ScreenWord(k) + (aimed == 1 ? " while aiming screen up" : "");
                            // The leg starts on the first tick that sees the new input (the next frame's Update).
                            double legStart = double.NaN;
                            while (true)
                            {
                                if (aimed == 1) inp.SetAim(_w.W.X[0] + 4 * ax, _w.W.Z[0] + 4 * az);
                                Snap s = Take(hz, leg);
                                Time.captureDeltaTime = sch.Dt(frame++);
                                yield return null;
                                ForceAnimate();
                                if (double.IsNaN(legStart)) { legStart = s.SimTime + 1e-9; continue; } // the frame before the input applies
                                if (s.SimTime - legStart >= DwellSim) break;
                                // ---- the view on screen now was drawn (last LateUpdate) from state s
                                if (!s.Alive || _w.ActorModel(0) == null) { dead++; hasPrev = false; continue; }
                                ActorAnimator anim = _w.Views.AnimatorOf(0);
                                if (anim.HasReaction) { hits++; hasPrev = false; continue; } // a hit twists and staggers on purpose
                                Transform model = _w.ActorModel(0);
                                float rootErr = RootError(model, s.SimYaw);
                                double t = s.SimTime - legStart;
                                samples++;
                                measuredAll++;
                                worstRoot = t >= SettleSim ? Mathf.Max(worstRoot, rootErr) : worstRoot;
                                if (hasPrev)
                                {
                                    float allowed = Mathf.Max(ToleranceDeg, prevErr + Mathf.Abs(Mathf.DeltaAngle(prevSimYaw, s.SimYaw))) + 2f;
                                    if (rootErr > allowed)
                                        bad.Add($"{cls} {leg}: the model turned AWAY from the sim facing: {prevErr:0} -> {rootErr:0} degrees in one " +
                                                $"frame while the sim facing turned {Mathf.Abs(Mathf.DeltaAngle(prevSimYaw, s.SimYaw)):0} {FacingSanityPlayTests.PoseDiag(_w, 0)}");
                                }
                                hasPrev = true;
                                prevErr = rootErr;
                                prevSimYaw = s.SimYaw;
                                if (t < SettleSim) continue;
                                if (rootErr > ToleranceDeg)
                                    bad.Add($"{cls} {leg}: {t:0.00} sim s after the turn the model root is {rootErr:0} degrees off the sim facing " +
                                            $"({SanityRig.Verdict(rootErr)}) {FacingSanityPlayTests.PoseDiag(_w, 0)}");
                                float travel = TravelError(anim, s.SimYaw);
                                if (travel > ToleranceDeg)
                                    bad.Add($"{cls} {leg}: the legs stride {travel:0} degrees away from where the model moves {FacingSanityPlayTests.PoseDiag(_w, 0)}");
                                Actor a = _w.W.Actors[0];
                                if (anim.Speed < MinSpeed || a.AirLen > 0 || !SanityRig.TryFront(model, out Vector3 front, out _)) continue;
                                toeSamples++;
                                Vector3 expected = Quaternion.AngleAxis(-anim.LegYawDeg, Vector3.up) * Dir(s.SimYaw);
                                float toeErr = SanityRig.AngleDeg(front, expected);
                                if (toeErr <= ToleranceDeg) continue;
                                toeOff++;
                                if (toeErr > worstToe) { worstToe = toeErr; worstToeAt = $"{leg}: {toeErr:0} degrees {FacingSanityPlayTests.PoseDiag(_w, 0)}"; }
                            }
                        }
                    }
                    string line = $"{cls} {sch.Name}: {samples} frames measured, root worst {worstRoot:0} deg after settling, toes off {toeOff} of {toeSamples}" +
                                  (hits > 0 ? $", {hits} hit frames skipped" : "") + (dead > 0 ? $", {dead} dead frames" : "");
                    summary.Add(line);
                    if (toeSamples > 0 && toeOff > toeSamples * 0.05)
                        bad.Add($"{line}: over the 5 % budget; worst {worstToeAt}");
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                _w.Driver.TimeScale = 0;
                inp.SetMove(0, 0);
                inp.ClearAim();
            }
            Debug.Log("[FrameRateFacing] " + string.Join("\n", summary));
            if (measuredAll == 0) Assert.Inconclusive(cls + ": no frame was measurable (actor 0 dead or no model)");
            CollectionAssert.IsEmpty(bad, $"{bad.Count} facing problems for {cls}:\n" + string.Join("\n", bad.Take(16)) + "\n" + string.Join("\n", summary));
        }

        Snap Take(double hz, string leg)
        {
            Actor a = _w.W.Actors[0];
            return new Snap
            {
                Alive = a.Alive,
                SimTime = (_w.W.Tick + _w.Driver.Alpha) / hz,
                SimYaw = SimSpace.YawDeg(a.Facing),
                Leg = leg,
            };
        }

        /// <summary>
        /// The batch-mode gate runs -nographics, where no renderer is ever visible, so the animation level of detail would
        /// stop evaluating actor 0 after its fresh frames (SanityWorld.Sync does the same for the whole crowd).
        /// </summary>
        void ForceAnimate()
        {
            Transform m = _w.ActorModel(0);
            if (m == null) return;
            Animator an = m.GetComponent<Animator>();
            if (an != null && an.cullingMode != AnimatorCullingMode.AlwaysAnimate) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        static Vector3 Dir(float yawDeg) => Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;

        /// <summary>Degrees between the model root's front (its rotation without the art's own yaw) and the sim facing.</summary>
        float RootError(Transform model, float simYaw)
        {
            ModelView mv = _w.Views.ModelOf(0);
            float art = mv != null && mv.Spec != null ? mv.Spec.ArtYawDeg : 0f;
            Vector3 f = model.rotation * Quaternion.Euler(0f, -art, 0f) * Vector3.forward;
            return SanityRig.AngleDeg(f, Dir(simYaw));
        }

        static float TravelError(ActorAnimator anim, float simYaw)
        {
            if (anim.Speed < 0.5f) return -1f;
            Vector3 f = Dir(simYaw), v = anim.Velocity;
            double rel = Locomotion.LeftAngleDeg(f.x, f.z, v.x, v.z);
            return Mathf.Abs(Mathf.DeltaAngle((float)rel, anim.TravelLeftDeg));
        }
    }
}
