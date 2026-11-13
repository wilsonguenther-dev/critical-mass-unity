using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CriticalMass.Runtime.PlayTests
{
    /// <summary>
    /// fix/kaiju-alive-1 (2026-11-13): "nothing glides", in the real Boot game. The owner watched the Kaiju glide with no
    /// skeleton motion (its 8 clips had been re-exported as the rest pose) while every name-and-count test passed. This
    /// runs a 16v16 bot match with the Kaiju hatched and provoked, and for every moving actor (each class, bots and the
    /// player, and the Kaiju) measures what is drawn: bones must turn while the body travels (a 1 s window of travel with
    /// almost no bone motion is "frozen"), the planted foot must stay put (foot slide = planted-foot travel / body travel),
    /// and each Kaiju sim event that should start an animation (hatch roar, claw wind-up, slam telegraph, slam, a hit
    /// flinch) shows it within a few ticks. Read-only on the views; it writes the sim only through the human input, the
    /// forced hatch and Kaiju.DamageKaiju (the provocation).
    /// </summary>
    [Category("KaijuAlive")]
    public sealed class NothingGlidesPlayTests
    {
        /// <summary>A window is 30 ticks (1 sim s) of travel above MoveSpeed; frozen when its top bones turn under FrozenDeg.</summary>
        public const int Window = 30;
        public const double MoveSpeed = 0.8;
        public const float FrozenDeg = 4f;
        /// <summary>Most frozen windows tolerated per class (a share: stagger holds, knock-downs, a pooled respawn frame).</summary>
        public const double MaxFrozenShare = 0.10;
        /// <summary>Foot slide limits (planted-foot travel per unit of body travel). A frozen or unmatched stride reads ~1; a
        /// fighter in the crowd is also shoved by separation and knocks (first CI run: 0.30 to 0.65 per class), so its limit
        /// only catches the gross case; the Kaiju walks alone and must plant its feet.</summary>
        public const double KaijuSlideLimit = 0.3, FighterSlideLimit = 0.75;
        /// <summary>Ticks an animation may lag its sim event.</summary>
        public const int EventLagTicks = 3, RoarLagTicks = 30;

        SanityWorld _w;

        [TearDown]
        public void TearDown()
        {
            SanityWorld.Reset();
            _w = null;
        }

        sealed class Track
        {
            public string Label;
            public Transform Root;
            public Transform[] Bones;
            public Quaternion[] Start;
            public float[] Turn;
            public int Ticks;
            public Transform FootL, FootR;
            public float Stance;
            public readonly Vector3[] PL = new Vector3[Window], PR = new Vector3[Window], PB = new Vector3[Window];
            public readonly float[] PY = new float[Window];
        }

        sealed class Tally
        {
            public int Windows, Frozen;
            public double FootTravel, BodyTravel;
            public string Worst = "";
            public float WorstTurn = float.PositiveInfinity;
        }

        readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        readonly Dictionary<string, Tally> _tally = new Dictionary<string, Tally>();

        [UnityTest]
        public IEnumerator EveryMovingActorAnimatesAndTheKaijuReactsToItsEvents()
        {
            _w = new SanityWorld();
            yield return _w.Load(ClassId.Warrior);
            World w = _w.W;
            w.ForceHatch = true;
            for (int i = 0; i < _w.Ticks(6) && w.Kaiju.State == KaijuState.Egg; i++) _w.Tick();
            Assert.AreNotEqual(KaijuState.Egg, w.Kaiju.State, "the forced hatch did not happen");
            KaijuAnimator ka = _w.Views.KaijuAnimator;
            if (_w.KaijuModel() == null) Assert.Ignore("the Kaiju has no model (grey-box cube)");

            var pending = new List<(int due, string what, Func<bool> ok)>();
            var missed = new List<string>();
            var seen = new Dictionary<string, int>();
            int roarsAtHatch = ka.Roars;
            pending.Add((w.Tick + RoarLagTicks, "hatch roar", () => ka.Roars > roarsAtHatch));
            int cursor = w.EventHead;
            int total = _w.Ticks(40);
            for (int t = 0; t < total; t++)
            {
                // Provoke: once a second the nearest fighter the Kaiju may target (away from its pad, no spawn shield) wounds
                // it, so the neutral Kaiju remembers a threat, walks to it, claws and flinches; the provoker changes as the
                // crowd moves, so the Kaiju keeps walking.
                if (t % 30 == 0 && w.Kaiju.KoIn <= 0)
                {
                    int src = -1;
                    double best = double.PositiveInfinity;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        if (!w.Actors[i].Alive || w.Actors[i].SpawnProtect > 0 || Kaiju.NearOwnPad(w, i)) continue;
                        double ex = w.X[i] - w.Kaiju.X, ez = w.Z[i] - w.Kaiju.Z, e2 = ex * ex + ez * ez;
                        if (e2 < best) { best = e2; src = i; }
                    }
                    if (src >= 0) Kaiju.DamageKaiju(w, src, 5);
                }
                _w.Tick();

                for (; cursor < w.EventHead; cursor++)
                {
                    SimEvent e = w.Events[cursor % World.EventCapacity];
                    int now = w.Tick;
                    switch (e.Type)
                    {
                        case SimEventType.KaijuMeleeWindup:
                            pending.Add((now + 2 * EventLagTicks, "claw wind-up -> Swipe", () => ka.Dominant == "Swipe" || ka.Dominant == "SwipeWindup" ||
                                w.Kaiju.KoIn > 0 || w.Kaiju.TelegraphIn > 0));   // a KO or a slam overrides the claw
                            Count(seen, "KaijuMeleeWindup");
                            break;
                        case SimEventType.KaijuTelegraph:
                            pending.Add((now + EventLagTicks, "slam telegraph -> SlamWindup", () => ka.Dominant == "SlamWindup"));
                            Count(seen, "KaijuTelegraph");
                            break;
                        case SimEventType.KaijuSlam:
                            pending.Add((now + EventLagTicks, "slam -> Slam", () => ka.Dominant == "Slam"));
                            Count(seen, "KaijuSlam");
                            break;
                        case SimEventType.KaijuDamaged:
                        {
                            // The views may already have posed this tick before the event is read: compare with the event's
                            // sim time, not with the flinch time seen now.
                            double at = (double)(now - 1) / w.Config.SimHz - 1e-6;
                            pending.Add((now + EventLagTicks, "hit -> flinch", () => ka.LastFlinchAt >= at || w.Kaiju.KoIn > 0));
                            Count(seen, "KaijuDamaged");
                            break;
                        }
                    }
                }
                // Satisfied checks leave; overdue ones are misses.
                for (int p = pending.Count - 1; p >= 0; p--)
                {
                    if (pending[p].ok()) { pending.RemoveAt(p); continue; }
                    if (w.Tick > pending[p].due) { missed.Add(pending[p].what + " (due tick " + pending[p].due + ")"); pending.RemoveAt(p); }
                }

                Sample(w, ka);
            }

            var sb = new StringBuilder("[NothingGlides]");
            foreach (KeyValuePair<string, Tally> kv in _tally)
            {
                Tally y = kv.Value;
                double slide = y.BodyTravel > 1e-6 ? y.FootTravel / y.BodyTravel : 0;
                sb.Append($"\n  {kv.Key}: {y.Windows} moving windows, {y.Frozen} frozen (least bone turn {y.WorstTurn:0.0} deg {y.Worst}), foot slide {slide:0.00} over {y.BodyTravel:0.0} u");
            }
            sb.Append("\n  events:");
            foreach (KeyValuePair<string, int> kv in seen) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value);
            sb.Append($"\n  Kaiju: {ka.Roars} roars, {ka.Steps} footfalls, walk rate {ka.WalkRate:0.00}, measured stride {ka.LastStrideMetres:0.00} m per cycle ({ka.LastWalkSpeed:0.00} m/s planted-foot speed)");
            Debug.Log(sb.ToString());

            var bad = new List<string>(missed);
            Assert.IsTrue(_tally.ContainsKey("Kaiju") && _tally["Kaiju"].Windows >= 3, "the Kaiju barely walked in 40 s of being provoked; nothing to measure\n" + sb);
            Assert.IsTrue(seen.ContainsKey("KaijuMeleeWindup"), "the provoked Kaiju never wound up a claw in 40 s\n" + sb);
            foreach (KeyValuePair<string, Tally> kv in _tally)
            {
                Tally y = kv.Value;
                bool kaiju = kv.Key == "Kaiju";
                if (y.Windows == 0) continue;
                double share = y.Frozen / (double)y.Windows;
                if (kaiju ? y.Frozen > 0 : share > MaxFrozenShare)
                    bad.Add($"{kv.Key}: {y.Frozen} of {y.Windows} moving windows had frozen bones (least turn {y.WorstTurn:0.0} deg): it glides");
                double slide = y.BodyTravel > 2 ? y.FootTravel / y.BodyTravel : 0;
                if (slide > (kaiju ? KaijuSlideLimit : FighterSlideLimit))
                    bad.Add($"{kv.Key}: the planted foot slides {slide:0.00} u per u travelled (limit {(kaiju ? KaijuSlideLimit : FighterSlideLimit)})");
            }
            Assert.Greater(ka.Steps, 0, "the walking Kaiju raised no footfall from its clip contacts\n" + sb);
            Assert.IsEmpty(bad, string.Join("\n", bad) + "\n" + sb);
        }

        static void Count(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out int n) ? n + 1 : 1;

        void Sample(World w, KaijuAnimator ka)
        {
            for (int i = 0; i < w.ActorCount; i++)
            {
                Transform m = _w.ActorModel(i);
                // Ground speed from the drawn displacement (a fighter running into a wall has velocity but goes nowhere).
                double sp = Math.Sqrt((w.X[i] - w.PrevX[i]) * (w.X[i] - w.PrevX[i]) + (w.Z[i] - w.PrevZ[i]) * (w.Z[i] - w.PrevZ[i])) * w.Config.SimHz;
                string label = i == 0 ? "Player " + w.Actors[i].Cls : w.Actors[i].Cls.ToString();
                Step(i, m, label, sp, "toes.l", "toes.r");
            }
            Transform km = _w.KaijuModel();
            Kaiju k = w.Kaiju;
            double ksp = Math.Sqrt((k.X - k.PrevX) * (k.X - k.PrevX) + (k.Z - k.PrevZ) * (k.Z - k.PrevZ)) * w.Config.SimHz;
            Step(-2, km, "Kaiju", ksp, "Foot_L", "Foot_R");
        }

        void Step(int id, Transform model, string label, double speed, string footL, string footR)
        {
            _tracks.TryGetValue(id, out Track tr);
            if (model == null || speed < MoveSpeed)
            {
                if (tr != null) tr.Ticks = 0;
                return;
            }
            if (tr == null || tr.Root != model)
            {
                tr = new Track { Root = model, Label = label };
                var list = new List<Transform>();
                foreach (Transform t in model.GetComponentsInChildren<Transform>(false))
                    if (t != model && t.GetComponent<Renderer>() == null) list.Add(t);
                tr.Bones = list.ToArray();
                tr.Start = new Quaternion[tr.Bones.Length];
                tr.Turn = new float[tr.Bones.Length];
                tr.FootL = SanityRig.Find(model, footL);
                tr.FootR = SanityRig.Find(model, footR);
                tr.Stance = id == -2 ? 0.08f : 0.03f;
                _tracks[id] = tr;
            }
            tr.Label = label;
            if (!_tally.TryGetValue(label, out Tally y)) _tally[label] = y = new Tally();

            int idx = tr.Ticks;
            if (idx == 0)
                for (int b = 0; b < tr.Bones.Length; b++) { tr.Start[b] = tr.Bones[b].localRotation; tr.Turn[b] = 0f; }
            else
                for (int b = 0; b < tr.Bones.Length; b++)
                    tr.Turn[b] = Mathf.Max(tr.Turn[b], Quaternion.Angle(tr.Start[b], tr.Bones[b].localRotation));
            bool feet = tr.FootL != null && tr.FootR != null;
            if (feet)
            {
                tr.PL[idx] = tr.FootL.position;
                tr.PR[idx] = tr.FootR.position;
                tr.PB[idx] = model.position;
                tr.PY[idx] = model.eulerAngles.y;
            }
            tr.Ticks++;
            if (tr.Ticks < Window) return;
            tr.Ticks = 0;
            // The mean of the 5 most-turned bones: one twitching bone does not make a walk.
            float[] s = (float[])tr.Turn.Clone();
            Array.Sort(s);
            float top = 0f;
            int n = Math.Min(5, s.Length);
            for (int q = 0; q < n; q++) top += s[s.Length - 1 - q];
            top = n > 0 ? top / n : 0f;
            y.Windows++;
            if (top < FrozenDeg) y.Frozen++;
            if (top < y.WorstTurn) { y.WorstTurn = top; y.Worst = "(" + label + ")"; }
            // Foot slide over the window (the MotionSanity reading): a foot is planted within Stance of its lowest height in
            // the window, and only consecutive planted samples of the same foot count.
            if (!feet) return;
            PlantedSlide(tr.PL, tr.PR, tr.PB, tr.Stance, out double foot, out double body, tr.PY);
            if (body < 2.0 * Window / 30.0 * 6.0)   // no respawn or teleport inside the window (6 u/s is faster than anyone runs)
            {
                y.FootTravel += foot;
                y.BodyTravel += body;
            }
        }

        /// <summary>Planted-foot travel and the body's travel over the same sample pairs (FacingMotionSanity's definition,
        /// with the stance height a parameter: the Kaiju's ankle bones ride higher than a fighter's toes).</summary>
        public static void PlantedSlide(Vector3[] left, Vector3[] right, Vector3[] body, float stance, out double footTravel, out double bodyTravel,
            float[] yaw = null)
        {
            footTravel = bodyTravel = 0;
            for (int f = 0; f < 2; f++)
            {
                Vector3[] p = f == 0 ? left : right;
                float min = float.PositiveInfinity;
                for (int t = 0; t < p.Length; t++) min = Mathf.Min(min, p[t].y);
                for (int t = 1; t < p.Length; t++)
                {
                    if (p[t].y > min + stance || p[t - 1].y > min + stance) continue;
                    // The planted foot is the LOWER foot: a low swing (the Kaiju lifts its feet about 0.1 u) dips within the
                    // stance band mid-stride and was counted as planted while it travelled at twice the body's speed (CI run
                    // 43487360396 read 1.14 u/u over 2.6 u). Only the foot that bears the weight at both samples counts.
                    Vector3[] o = f == 0 ? right : left;
                    if (p[t].y > o[t].y || p[t - 1].y > o[t - 1].y) continue;
                    // A pivot (the drawn body turning more than 2 degrees in a tick) swings the planted feet round the body:
                    // that is a turn, measured as one, not a glide.
                    if (yaw != null && Mathf.Abs(Mathf.DeltaAngle(yaw[t], yaw[t - 1])) > 2f) continue;
                    Vector3 df = p[t] - p[t - 1], db = body[t] - body[t - 1];
                    footTravel += new Vector2(df.x, df.z).magnitude;
                    bodyTravel += new Vector2(db.x, db.z).magnitude;
                }
            }
        }
    }
}
