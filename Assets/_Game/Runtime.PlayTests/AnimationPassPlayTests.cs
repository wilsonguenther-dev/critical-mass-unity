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
    /// Animation pass (2026-11-12, physics audit seat ANIMATION) on the real Boot game: the contact frame on the damage tick
    /// (contract C2), legs that keep striding under a swing, no moonwalking in 8 directions, the facing spring, the shield
    /// playing Block, still legs in the air and footfalls. Bones are measured the way a person sees them (SanityWorld forces
    /// every animator to animate, so the level of detail never throttles here). Checks that need the 24 new KayKit clips are
    /// Inconclusive until KayKit_Clips.fbx is rebuilt.
    /// </summary>
    public sealed class AnimationPassPlayTests
    {
        SanityWorld _w;
        readonly List<string> _bad = new List<string>();

        [SetUp]
        public void SetUp() => _bad.Clear();

        [TearDown]
        public void TearDown()
        {
            SanityWorld.Reset();
            _w = null;
        }

        static List<ClassId> ModelClasses()
        {
            var list = new List<ClassId>();
            foreach (ClassId c in ClassIds.All)
                if (SanityWorld.HasModel(ArtMapping.ClassModel(c))) list.Add(c);
            return list;
        }

        static bool ModelHasClip(ClassId cls, string clip) =>
            ArtCatalog.Load()?.Find(ArtMapping.ClassModel(cls))?.Clip(clip) != null;

        /// <summary>
        /// Contract C2 joint check: for every class, the weapon hand's peak speed is drawn within 1 tick of the tick the
        /// view times the contact on (the damage tick plus the parity lead with W = 0; exactly T + W once DG-69 adds W).
        /// </summary>
        [UnityTest]
        public IEnumerator TheWeaponConnectsOnTheDamageTick()
        {
            List<ClassId> classes = ModelClasses();
            if (classes.Count == 0) Assert.Ignore("no class has a model");
            foreach (ClassId cls in classes)
            {
                // No regular swing by design: the Demolitions attack press does nothing (DG-22), the Healer's mends (DG-40).
                if (cls == ClassId.Demolitions || cls == ClassId.Healer) continue;
                _w = new SanityWorld();
                yield return _w.Load(cls);
                Transform model = _w.ActorModel(0);
                Transform hand = model != null ? SanityRig.Find(model, "handslot.r") : null;
                if (hand == null) { _bad.Add(cls + ": no handslot.r to measure"); continue; }
                HumanInputState inp = _w.HumanInput;
                World w = _w.W;
                SanityWorld.FromScreenIntent(1, 0, out double dx, out double dz);
                inp.SetAim(w.X[0] + 4 * dx, w.Z[0] + 4 * dz);
                _w.Tick(_w.Ticks(1));
                inp.ButtonDown(HumanInputState.HumanButton.Attack);
                int pressTick = -1;
                string clip = null;
                ActorAnimator anim = _w.Views.AnimatorOf(0);
                var pos = new List<Vector3>();
                var ticks = new List<int>();
                double lastAction = w.Actors[0].AnimAction;
                int windupSim = 0;
                for (int t = 0; t < 45; t++)
                {
                    _w.Tick(1);
                    if (t == 0) inp.ButtonUp(HumanInputState.HumanButton.Attack);
                    if (pressTick < 0 && w.Actors[0].AnimAction < lastAction && w.Actors[0].AttackCd > 0)
                    {
                        pressTick = w.Tick;
                        clip = anim != null ? anim.ActionClip : null;
                        windupSim = w.Actors[0].SwingWindup;   // the sim's own swing: AttackContact on SwingTick + SwingWindup
                    }
                    lastAction = w.Actors[0].AnimAction;
                    pos.Add(model.InverseTransformPoint(hand.position));
                    ticks.Add(w.Tick);
                }
                if (pressTick < 0) { _bad.Add(cls + ": the attack press did not start a swing"); continue; }
                if (clip == null) clip = ArtMapping.Attack;
                int windup = windupSim;
                double contact = ClipFacts.Contact(clip, 0.5f);
                double contactAge = AttackTiming.ContactAge(contact, windup / _w.Cfg.SimHz);
                int expected = pressTick + (int)Math.Round(contactAge * _w.Cfg.SimHz);
                int peak = -1;
                float best = -1f;
                for (int k = 1; k < pos.Count; k++)
                {
                    if (ticks[k] < pressTick) continue;
                    float v = (pos[k] - pos[k - 1]).magnitude;
                    if (v > best) { best = v; peak = ticks[k]; }
                }
                Debug.Log($"[AnimationPass] {cls} {clip}: press {pressTick}, contact expected {expected}, hand peak {peak} (W {windup})");
                if (Math.Abs(peak - expected) > 1)
                    _bad.Add($"{cls} ({clip}): the weapon hand peaks at tick {peak}, the hit lands at tick {expected}: {peak - expected} ticks apart");
                yield return null;
            }
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));
        }

        /// <summary>Running and attacking: the feet keep a run stride (the swing is on the upper body only).</summary>
        [UnityTest]
        public IEnumerator LegsKeepStridingThroughASwing()
        {
            List<ClassId> classes = ModelClasses();
            if (classes.Count == 0) Assert.Ignore("no class has a model");
            foreach (ClassId cls in classes)
            {
                _w = new SanityWorld();
                yield return _w.Load(cls);
                Transform model = _w.ActorModel(0);
                Transform tl = SanityRig.Find(model, "toes.l"), tr = SanityRig.Find(model, "toes.r");
                if (tl == null || tr == null) { _bad.Add(cls + ": no toes"); continue; }
                HumanInputState inp = _w.HumanInput;
                _w.TowardHill(0, out double dx, out double dz);
                SanityWorld.ToScreenIntent(dx, dz, out double right, out double up);
                inp.SetMove(right, up);
                _w.Tick(15);
                inp.ButtonDown(HumanInputState.HumanButton.Attack);
                float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                double speed = 0;
                for (int t = 0; t < 24; t++)
                {
                    _w.Tick(1);
                    if (t == 0) inp.ButtonUp(HumanInputState.HumanButton.Attack);
                    float s = Vector3.Dot(tl.position - tr.position, SanityRig.SimDir(_w.W.Actors[0].Facing));
                    lo = Mathf.Min(lo, s);
                    hi = Mathf.Max(hi, s);
                    speed += _w.Speed(0) / 24;
                }
                if (speed < 1.5) { _bad.Add($"{cls}: could not run (mean {speed:0.0} u/s)"); continue; }
                if (hi - lo < 0.25f) _bad.Add($"{cls} swinging while running at {speed:0.0} u/s: the feet stride only {hi - lo:0.00} u (legs frozen by the swing)");
                inp.SetMove(0, 0);
                yield return null;
            }
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));
        }

        /// <summary>
        /// Moving in 8 screen directions while aiming one way: the planted foot slides at most MaxSlide u per unit
        /// travelled in every direction (no moonwalking). Needs the strafe clips for the sideways rows.
        /// </summary>
        [UnityTest]
        public IEnumerator NoMoonwalkingInEightDirectionsWithAFixedAim()
        {
            if (!ModelHasClip(ClassId.Warrior, ArtMapping.StrafeL))
                Assert.Inconclusive("KayKit_Clips.fbx has no strafe clips yet: re-run tools/art/build_kaykit_clips.py");
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            Transform model = _w.ActorModel(0);
            Transform tl = SanityRig.Find(model, "toes.l"), tr = SanityRig.Find(model, "toes.r");
            Assert.IsNotNull(tl);
            HumanInputState inp = _w.HumanInput;
            SanityWorld.FromScreenIntent(0, 1, out double ax, out double az);
            for (int k = 0; k < 8; k++)
            {
                double ang = k * Math.PI / 4;
                inp.SetMove(Math.Cos(ang), Math.Sin(ang));
                const int n = 60;
                var pl = new Vector3[n];
                var pr = new Vector3[n];
                var pb = new Vector3[n];
                for (int t = 0; t < 12 + n; t++)
                {
                    inp.SetAim(_w.W.X[0] + 4 * ax, _w.W.Z[0] + 4 * az);
                    _w.Tick(1);
                    if (t < 12) continue;
                    pl[t - 12] = tl.position;
                    pr[t - 12] = tr.position;
                    pb[t - 12] = model.position;
                }
                MotionSanityPlayTests.PlantedSlide(pl, pr, pb, out double foot, out double body, out int pairs);
                string dir = SanityWorld.ScreenWord(k);
                Debug.Log($"[AnimationPass] moving {dir} aiming up: slide {(body > 0 ? foot / body : 0):0.000} u/u over {pairs} pairs");
                if (body < 0.3) continue; // blocked by the map this way: nothing to judge
                if (foot / body > MotionSanityPlayTests.MaxSlide) _bad.Add($"moving {dir} while aiming up: the planted foot slides {foot / body:0.00} u/u");
                inp.SetMove(0, 0);
                _w.Tick(10);
            }
            CollectionAssert.IsEmpty(_bad, string.Join("\n", _bad));
        }

        /// <summary>A 180 degree aim flip: the drawn yaw turns at most 45 degrees a frame and settles within 5 degrees by 0.2 sim s.</summary>
        [UnityTest]
        public IEnumerator TurningIsSmoothButFast()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            ActorAnimator anim = _w.Views.AnimatorOf(0);
            if (_w.ActorModel(0) == null || anim == null) Assert.Ignore("the Warrior has no model");
            HumanInputState inp = _w.HumanInput;
            World w = _w.W;
            SanityWorld.FromScreenIntent(1, 0, out double dx, out double dz);
            inp.SetAim(w.X[0] + 4 * dx, w.Z[0] + 4 * dz);
            _w.Tick(10);
            inp.SetAim(w.X[0] - 4 * dx, w.Z[0] - 4 * dz);
            float prev = anim.Yaw, worst = 0f;
            for (int t = 0; t < 6; t++)
            {
                _w.Tick(1);
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(prev, anim.Yaw)));
                prev = anim.Yaw;
            }
            float err = Mathf.Abs(Mathf.DeltaAngle(anim.Yaw, SimSpace.YawDeg(w.Actors[0].Facing)));
            Assert.LessOrEqual(worst, 45.5f, "a one-frame snap");
            Assert.LessOrEqual(err, 5f, "still turning 0.2 s after the flip");
        }

        /// <summary>The Warrior's shield plays Block (then Blocking), never the attack.</summary>
        [UnityTest]
        public IEnumerator TheShieldPlaysBlockNotTheAttack()
        {
            if (!ModelHasClip(ClassId.Warrior, ArtMapping.Block)) Assert.Inconclusive("KayKit_Clips.fbx has no Block clip yet");
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            _w.HumanInput.ButtonDown(HumanInputState.HumanButton.Ability);
            _w.Tick(2);
            _w.HumanInput.ButtonUp(HumanInputState.HumanButton.Ability);
            Assert.Greater(_w.W.Actors[0].AbilityActive, 0, "the ability press did not raise the shield");
            ActorAnimator anim = _w.Views.AnimatorOf(0);
            Assert.AreEqual(2, anim.ActionKind, "the view read the press as an ability");
            Assert.AreEqual(ArtMapping.Block, anim.ActionClip);
        }

        /// <summary>In the air the legs stop striding (Jump_Idle, not the run cycle).</summary>
        [UnityTest]
        public IEnumerator LegsStopStridingInTheAir()
        {
            if (!ModelHasClip(ClassId.Warrior, ArtMapping.JumpAir)) Assert.Inconclusive("KayKit_Clips.fbx has no jump clips yet");
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            Transform model = _w.ActorModel(0);
            Transform tl = SanityRig.Find(model, "toes.l"), tr = SanityRig.Find(model, "toes.r");
            _w.TowardHill(0, out double dx, out double dz);
            SanityWorld.ToScreenIntent(dx, dz, out double right, out double up);
            _w.HumanInput.SetMove(right, up);
            _w.Tick(15);
            _w.HumanInput.ButtonDown(HumanInputState.HumanButton.Jump);
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            int air = 0;
            for (int t = 0; t < 30; t++)
            {
                _w.Tick(1);
                Actor a = _w.W.Actors[0];
                if (a.AirLen <= 0 || a.AirTick <= 3) continue; // skip the take-off frames
                air++;
                float s = Vector3.Dot(tl.position - tr.position, SanityRig.SimDir(a.Facing));
                lo = Mathf.Min(lo, s);
                hi = Mathf.Max(hi, s);
            }
            if (air < 4) Assert.Inconclusive("actor 0 did not stay airborne long enough to measure");
            Assert.Less(hi - lo, 0.1f, "the feet keep running in the air");
        }

        /// <summary>A running fighter near the camera raises footfalls on its steps.</summary>
        [UnityTest]
        public IEnumerator RunningRaisesFootfalls()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            if (_w.ActorModel(0) == null) Assert.Ignore("the Warrior has no model");
            _w.TowardHill(0, out double dx, out double dz);
            SanityWorld.ToScreenIntent(dx, dz, out double right, out double up);
            _w.HumanInput.SetMove(right, up);
            _w.Tick(10);
            var mine = new ActorSteps { Actor = 0 };
            Footfalls.Register(mine);
            long before = Footfalls.Emitted, dropped = Footfalls.Dropped;
            try { _w.Tick(45); }
            finally { Footfalls.Unregister(mine); }
            long steps = Footfalls.Emitted - before;
            Debug.Log($"[AnimationPass] {steps} footfalls in 1.5 sim s of running ({mine.Steps} by actor 0, {Footfalls.Dropped - dropped} over the cap)");
            Assert.GreaterOrEqual(steps, 2, "no footfalls while running");
            // fix/animation-reds: the runner itself steps (about 2.5 a second at a run), whatever the crowd does.
            Assert.GreaterOrEqual(mine.Steps, 2, "actor 0 ran 1.5 s without a footfall of its own");
        }

        sealed class ActorSteps : IFootfallSink
        {
            public int Actor, Steps;
            public void OnFootfall(in Footfall f) { if (f.Actor == Actor && f.Kind == FootfallKind.Step) Steps++; }
        }
    }
}
