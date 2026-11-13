using System;
using System.Reflection;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

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
        /// Clip time of an attack clip `age` sim seconds after the press: contact c, length L, wind-up W seconds.
        /// W > 0: 0..c over the wind-up, then c..L at rate 1. W = 0: starts ParityLeadFrames before c at rate 1, so the
        /// contact frame is shown within 2 frames of the damage tick (the hit cannot land before the swing).
        /// </summary>
        public static double ClipTime(double age, double contact, double length, double windupSeconds)
        {
            if (age < 0) age = 0;
            if (windupSeconds > 1e-9)
                return age < windupSeconds ? contact * age / windupSeconds : Math.Min(length, contact + (age - windupSeconds));
            double start = Math.Max(0, contact - ParityLeadFrames / 30.0);
            return Math.Min(length, start + age);
        }

        /// <summary>The sim seconds after the press at which the contact frame is drawn (W seconds, or the parity lead).</summary>
        public static double ContactAge(double contact, double windupSeconds) =>
            windupSeconds > 1e-9 ? windupSeconds : contact - Math.Max(0, contact - ParityLeadFrames / 30.0);

        /// <summary>
        /// Envelope weight of a one-shot upper clip: fades in over FadeIn (or half the wind-up), out over the last FadeOut
        /// of its clip time. 0 once the clip has finished.
        /// </summary>
        public static float Envelope(double age, double clipTime, double length, double windupSeconds)
        {
            if (clipTime >= length - 1e-4) return 0f;
            double fin = windupSeconds > 1e-9 ? Math.Min(FadeIn, windupSeconds * 0.5) : 0.02;
            double a = fin <= 0 ? 1 : Math.Min(1, age / fin);
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
