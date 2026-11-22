using System;
using System.Reflection;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// The view half of the hit-frame contract C2 (DG-74): the attack clip's contact frame lands on the sim's damage tick.
    /// <para>The wind-up W is read from the sim config, never assumed. Today the sim has none (W = 0, "browser parity": the
    /// damage tick is the press tick), so the clip starts 2 frames before its contact and the hit lands on the contact frame.
    /// When DG-69 adds the wind-up, the clip plays 0..contact over exactly W ticks, `t = c x age / W`, and the contact
    /// frame lands on tick T + W. After contact it plays to its end at its authored rate (the envelope lets the
    /// follow-through finish past the cooldown).</para>
    /// <para>How W is found (no compile dependency on fields that do not exist yet): `ClassConfig.AttackWindupTicks` if
    /// present, else `ClassConfig.AttackWindupSeconds` (DG-69's name, written in on-screen seconds) converted with the
    /// config's SimHz and DefaultSpeed. The swing's age is `Actor.SwingTick` (DG-69's appended field) when present, else
    /// the sim's existing `AnimAction` (seconds since the press). Both lookups run once per config, not per frame.</para>
    /// </summary>
    public static class AttackTiming
    {
        /// <summary>Frames of the clip shown before contact in parity mode (W = 0), at 30 fps clip time.</summary>
        public const double ParityLeadFrames = 2;
        /// <summary>Envelope (sim seconds): fade in, fade out at the end of the follow-through, cross-fade on a re-trigger.</summary>
        public const double FadeIn = 0.05, FadeOut = 0.12, Retrigger = 0.06;

        static readonly string[] WindupTickFields = { "AttackWindupTicks", "WindupTicks", "StartupTicks", "AttackStartupTicks" };
        static readonly string[] WindupSecondFields = { "AttackWindupSeconds", "WindupSeconds", "StartupSeconds" };
        static FieldInfo _swingTick;
        static bool _swingLooked;

        /// <summary>Wind-up ticks per class for one config (cached on the config instance).</summary>
        static GameConfig _cachedFor;
        static readonly int[] _windup = new int[16];

        /// <summary>W in sim ticks for a class: 0 until the sim has a wind-up field.</summary>
        public static int WindupTicks(GameConfig cfg, ClassId cls)
        {
            if (cfg == null) return 0;
            if (!ReferenceEquals(cfg, _cachedFor))
            {
                _cachedFor = cfg;
                for (int i = 0; i < _windup.Length; i++) _windup[i] = 0;
                foreach (ClassId c in Enum.GetValues(typeof(ClassId)))
                {
                    int k = (int)c;
                    if (k < 0 || k >= _windup.Length) continue;
                    ClassConfig cc;
                    try { cc = cfg.Class(c); } catch (Exception) { continue; }
                    _windup[k] = ReadWindupTicks(cc, cfg.SimHz, cfg.DefaultSpeed);
                }
            }
            int idx = (int)cls;
            return idx >= 0 && idx < _windup.Length ? _windup[idx] : 0;
        }

        /// <summary>Reads a wind-up from a class config by reflection (0 when the field does not exist yet).</summary>
        public static int ReadWindupTicks(object classConfig, double simHz, double pace)
        {
            if (classConfig == null) return 0;
            Type t = classConfig.GetType();
            foreach (string n in WindupTickFields)
            {
                double v = ReadNumber(t, classConfig, n);
                if (!double.IsNaN(v)) return Math.Max(0, (int)Math.Round(v));
            }
            foreach (string n in WindupSecondFields)
            {
                double v = ReadNumber(t, classConfig, n);
                // On-screen seconds -> sim ticks at the default pace (DG-68 converts the same way).
                if (!double.IsNaN(v)) return Math.Max(0, (int)Math.Round(v * simHz * Math.Max(pace, 1e-6)));
            }
            return 0;
        }

        static double ReadNumber(Type t, object o, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
            {
                object v = f.GetValue(o);
                if (v is int i) return i;
                if (v is double d) return d;
                if (v is float s) return s;
            }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanRead)
            {
                object v = p.GetValue(o);
                if (v is int i) return i;
                if (v is double d) return d;
                if (v is float s) return s;
            }
            return double.NaN;
        }

        /// <summary>
        /// Seconds since the swing began (the press), at render alpha: from Actor.SwingTick when the sim has it, else from
        /// AnimAction (reset to 0 on the press tick). Boxing happens only when SwingTick exists (DG-69), once per swinging actor.
        /// </summary>
        public static double SwingAge(Actor a, int tick, double alpha, double simHz)
        {
            if (!_swingLooked)
            {
                _swingLooked = true;
                _swingTick = typeof(Actor).GetField("SwingTick", BindingFlags.Public | BindingFlags.Instance);
                if (_swingTick != null && _swingTick.FieldType != typeof(int)) _swingTick = null;
            }
            if (_swingTick != null)
            {
                int st = (int)_swingTick.GetValue(a);
                if (st > 0 && st <= tick) return (tick - st + alpha) / simHz;
            }
            return a.AnimAction + alpha / simHz;
        }

        /// <summary>
        /// The wind-up of the actor's current swing in sim seconds, read from the sim's own swing (Actor.SwingWindup: the
        /// hit resolves and AttackContact fires on SwingTick + SwingWindup), so the view never guesses it from the config at
        /// a different pace.
        /// </summary>
        public static double SwingWindupSeconds(Actor a, double simHz) =>
            a != null && simHz > 0 ? Math.Max(0, a.SwingWindup) / simHz : 0;

        /// <summary>
        /// Clip time of an attack clip `age` sim seconds after the press: contact c, length L, wind-up W seconds.
        /// W > 0: 0..c over the wind-up, then c..L at rate 1. W = 0: starts ParityLeadFrames before c at rate 1, so the
        /// contact frame is shown within 2 frames of the damage tick (the hit cannot land before the swing).
        /// </summary>
        public static double ClipTime(double age, double contact, double length, double windupSeconds) =>
            ClipTime(age, contact, length, windupSeconds, -1);

        /// <summary>ClipTime with the clip's measured wind-up extreme (clip seconds; below 0: the estimate, Extreme).</summary>
        public static double ClipTime(double age, double contact, double length, double windupSeconds, double extreme)
        {
            if (age < 0) age = 0;
            if (windupSeconds > 1e-9)
            {
                if (age >= windupSeconds) return Math.Min(length, contact + (age - windupSeconds));
                // fix/hit-feel-3 (fast-attack craft, corrected): ease to the clip's MEASURED wind-up extreme, hold it, and play
                // the whole strike (extreme to contact) inside the last tick before the damage tick, so the weapon hand is
                // fastest exactly on that tick. hit-feel-2 snapped in 2 frames and struck over 1.5: the snap out-ran the
                // strike and the hand peaked 4 to 5 ticks early (AnimationPassPlayTests.TheWeaponConnectsOnTheDamageTick).
                if (FastCraft) return CraftTime(age, contact, windupSeconds, extreme);
                double s = age / windupSeconds;
                return contact * s * s * s;
            }
            double start = Math.Max(0, contact - ParityLeadFrames / 30.0);
            return Math.Min(length, start + age);
        }

        /// <summary>The fast-attack wind-up (on). Off restores the eased cubic map.</summary>
        public static bool FastCraft = true;
        /// <summary>
        /// Estimated extreme (contact minus StrikeClipSeconds) when a clip cannot be measured; the ease into the extreme
        /// (at most AnticipationSeconds: 4 sim ticks, 2 frames at the default pace 2) and the strike (one sim tick at 30 Hz).
        /// </summary>
        /// <para>px lane C (2026-11-22): the strike is HitFeelTuning.StrikeFrames (5) frames of 60 fps, at most
        /// HitFeelTuning.StrikeMaxShare of the wind-up (was ONE sim tick: the "half motion"); the anticipation ease is
        /// HitFeelTuning.AnticipationSeconds.</para>
        public const double StrikeClipSeconds = 0.25, AnticipationSeconds = HitFeelTuning.AnticipationSeconds, StrikeSeconds = HitFeelTuning.StrikeSecondsTarget;

        /// <summary>The strike's length (sim s) for a wind-up W: StrikeSeconds, at most StrikeMaxShare of W.</summary>
        public static double StrikeFor(double windupSeconds) => Math.Min(StrikeSeconds, windupSeconds * HitFeelTuning.StrikeMaxShare);

        /// <summary>The estimated wind-up extreme, clip seconds.</summary>
        public static double Extreme(double contact) => Math.Max(0, contact - StrikeClipSeconds);

        /// <summary>
        /// Fast-attack wind-up clip time for age in [0, W): the strike is the last min(StrikeSeconds, 0.4 W); before it,
        /// ease (smoothstep) from 0 to the extreme over min(AnticipationSeconds, the time before the strike), then hold.
        /// The strike runs extreme to contact linearly and ends exactly on the damage tick.
        /// </summary>
        public static double CraftTime(double age, double contact, double windupSeconds, double extreme = -1)
        {
            double e = extreme >= 0 && extreme <= contact ? extreme : Extreme(contact);
            double strike = StrikeFor(windupSeconds), holdEnd = windupSeconds - strike;
            double ease = Math.Min(AnticipationSeconds, holdEnd);
            if (age <= 0) return 0;
            // anticipation: ease out into the wind-up extreme (fast first, so the pose commits at once), then hold it
            if (age < ease) { double s = age / ease; return e * (1 - (1 - s) * (1 - s)); }
            if (age < holdEnd) return e;
            // px lane C: the strike over 4-6 frames, ease-in (the weapon is fastest on the contact frame)
            return e + (contact - e) * HitFeelTuning.StrikeShape(Math.Min(1, (age - holdEnd) / Math.Max(1e-9, strike)));
        }

        static readonly System.Collections.Generic.Dictionary<string, double> _extremes = new System.Collections.Generic.Dictionary<string, double>();
        public const int ExtremeSamples = 24;

        /// <summary>
        /// fix/hit-feel-3: the clip's wind-up extreme MEASURED on the rig: of ExtremeSamples clip times in [0, contact], the
        /// one where the weapon hand (handslot.r, else hand.r, model space) is farthest from where it is at contact. So the
        /// strike (extreme to contact) is the longest hand path the clip has before the hit. Measured once per clip name
        /// (the shared clips are shared across rigs), before this frame's pose is built; -1 when it cannot be measured.
        /// </summary>
        public static double MeasureExtreme(ModelView m, string clip, double contact)
        {
            if (clip == null) return -1;
            if (_extremes.TryGetValue(clip, out double cached)) return cached;
            double result = ExtremeNow(m, clip, contact);
            _extremes[clip] = result;
            return result;
        }

        /// <summary>fix/hit-feel-7: MeasureExtreme without the per-clip cache (the animator keeps one per model and clip).</summary>
        public static double ExtremeNow(ModelView m, string clip, double contact)
        {
            if (clip == null) return -1;
            double result = -1;
            Transform hand = m != null ? (m.Bone("handslot.r") ?? m.Bone("hand.r")) : null;
            if (hand != null && contact > 1e-3)
            {
                m.BeginPose();
                m.Pose(clip, contact, 1f, false);
                if (m.Evaluate(true))
                {
                    Vector3 hc = m.Transform.InverseTransformPoint(hand.position);
                    double best = -1;
                    for (int k = 0; k < ExtremeSamples; k++)
                    {
                        double t = contact * k / ExtremeSamples;
                        m.BeginPose();
                        m.Pose(clip, t, 1f, false);
                        if (!m.Evaluate(true)) { best = -1; break; }
                        double d = (m.Transform.InverseTransformPoint(hand.position) - hc).sqrMagnitude;
                        if (d > best) { best = d; result = t; }
                    }
                    if (best < 1e-6) result = -1;
                }
            }
            return result;
        }

        /// <summary>Tests: forget the measured extremes.</summary>
        public static void ResetExtremes() => _extremes.Clear();

        /// <summary>The sim seconds after the press at which the contact frame is drawn (W seconds, or the parity lead).</summary>
        public static double ContactAge(double contact, double windupSeconds) =>
            windupSeconds > 1e-9 ? windupSeconds : contact - Math.Max(0, contact - ParityLeadFrames / 30.0);

        /// <summary>
        /// Envelope weight of a one-shot upper clip: fades in over FadeIn (or half the wind-up), out over the last FadeOut
        /// of its clip time. 0 once the clip has finished.
        /// </summary>
        public static float Envelope(double age, double clipTime, double length, double windupSeconds) =>
            Envelope(age, clipTime, length, windupSeconds, false);

        /// <summary>
        /// px lane C (2026-11-22): with a wind-up the pose COMMITS at once: ease-out to full over HitFeelTuning.CommitRise
        /// (0.05 s for the local player: 0.89 at 2 frames of 60 fps; 0.075 s for everyone else: 0.89 at 3 frames). The
        /// smoothstep over the whole wind-up (26% at a third of it) was the "half motion". The clip time eases out of
        /// frame 0 into the wind-up extreme (CraftTime), so the snap is into the anticipation pose, not the strike.
        /// </summary>
        public static float Envelope(double age, double clipTime, double length, double windupSeconds, bool local)
        {
            if (clipTime >= length - 1e-4) return 0f;
            double a;
            if (windupSeconds > 1e-9) a = HitFeelTuning.CommitWeight(age, local);
            else a = Math.Min(1, age / 0.02);
            double b = Math.Min(1, (length - clipTime) / FadeOut);
            return (float)Math.Max(0, Math.Min(a, b));
        }

        /// <summary>The attack clip this model plays for a class (its own, else the shared one-hand chop).</summary>
        public static string AttackClip(ModelView m, ClassId cls)
        {
            string c = ArtMapping.ClassClips(cls).Attack;
            return m != null && m.HasClip(c) ? c : ArtMapping.Attack;
        }

        /// <summary>The ability clip for a class, or null (never the attack: a shield cast must not replay the chop).</summary>
        public static string AbilityClip(ModelView m, ClassId cls)
        {
            string c = ArtMapping.ClassClips(cls).Ability;
            return m != null && m.HasClip(c) ? c : null;
        }

        /// <summary>
        /// Hit-frame timing table for tests and the capture report: per class, the attack clip, its contact seconds, its
        /// length and the sim seconds after the press at which the contact frame is drawn for a wind-up of W ticks.
        /// </summary>
        public static void Row(ClassId cls, int windupTicks, double simHz, out string clip, out double contact, out double length, out double contactAge)
        {
            clip = ArtMapping.ClassClips(cls).Attack;
            ClipFact f = ClipFacts.Get(clip) ?? ClipFacts.Get(ArtMapping.Attack);
            contact = f != null ? f.contact : 0.567;
            length = f != null ? f.length : 1.067;
            contactAge = ContactAge(contact, windupTicks / simHz);
        }
    }
}
