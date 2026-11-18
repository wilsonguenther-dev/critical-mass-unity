using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Audio
{
    /// <summary>The adaptive score's moods during a match (b15 Skybreak score).</summary>
    public enum ScoreState { Calm, Contested, Leading, Trailing, Rally, Egg, FinalMinute, Berserk }

    /// <summary>The Kaiju colours the bed and the theme: fed (it sides with a team) = the motif's major form, berserk = its bent form.</summary>
    public enum ScoreColour { Minor, Feed, Corrupt }

    /// <summary>The three stems the director layers. Bed = strings, choir, sub; Rhythm = ostinato and drums; Theme = the motif.</summary>
    public enum ScoreLayer { Bed, Rhythm, Theme }

    /// <summary>The rhythm stem's four forms.</summary>
    public enum RhythmForm { Pulse, March, Drive, Heart }

    /// <summary>What the match wants from the score right now: one gain and one form per layer.</summary>
    public struct ScoreTarget
    {
        public ScoreState State;
        public ScoreColour Colour;
        public RhythmForm Rhythm;
        public float BedGain, RhythmGain, ThemeGain;

        /// <summary>The variant index of a layer's clip (bed: minor, feed; rhythm: pulse, march, drive, heart; theme: minor, feed, corrupt).</summary>
        public int Variant(ScoreLayer l) =>
            l == ScoreLayer.Bed ? (Colour == ScoreColour.Feed ? 1 : 0) : l == ScoreLayer.Rhythm ? (int)Rhythm : (int)Colour;

        public float Gain(ScoreLayer l) => l == ScoreLayer.Bed ? BedGain : l == ScoreLayer.Rhythm ? RhythmGain : ThemeGain;

        public bool SameAs(in ScoreTarget o) =>
            State == o.State && Colour == o.Colour && Rhythm == o.Rhythm && BedGain == o.BedGain && RhythmGain == o.RhythmGain && ThemeGain == o.ThemeGain;
    }

    /// <summary>The match facts the score reads (filled by MusicDirector from the World; tests fill it by hand).</summary>
    public struct ScoreInputs
    {
        public bool HillContested;
        /// <summary>Hill seconds of the local player's team minus the other team's.</summary>
        public double LeadSeconds;
        /// <summary>The trailing team's rally is on for the local player's team (MusicDirector.RallyProbe; false without one).</summary>
        public bool Rally;
        public bool Hatched;
        public double UntilHatch;
        public KaijuState Kaiju;
        public double Remaining;
        public double MatchSeconds;
        public bool LessSound;
    }

    /// <summary>
    /// The Skybreak game score's data (pure; tests): the 80 BPM grid, the four 8-bar sections and their 12-step plan, the
    /// stem clip names, the mix-state table and the hatch duck. tools/audio/skybreak/build_game_score.py writes the clips
    /// and holds the same table (STATES, PLAN); tools/audio/skybreak/analyze_score.py measures every state from it.
    /// </summary>
    public static class ScoreMix
    {
        public const double Bpm = 80, Beat = 0.75, Bar = 3.0, Section = 24.0, Tail = 2.5;
        public const int SectionBars = 8;
        public const string Folder = "CMScore/";
        /// <summary>Hill-time lead (s) that counts as leading or trailing.</summary>
        public const double LeadSecondsFor = 10;
        /// <summary>The egg's heartbeat starts this long before the hatch.</summary>
        public const double EggSeconds = 30;
        public const double FinalMinuteSeconds = 60;
        /// <summary>The less-sound setting: the rhythm stem at this share.</summary>
        public const float LessSoundRhythm = 0.6f;

        public static readonly string[] Sections = { "A", "B", "C", "D" };
        /// <summary>The order the sections play (indices into Sections): 12 x 24 s = 4 min 48 s before it repeats, never one twice in a row.</summary>
        public static readonly int[] Plan = { 0, 1, 2, 3, 1, 0, 3, 2, 0, 2, 1, 3 };
        public static readonly string[][] VariantNames =
        {
            new[] { "minor", "feed" },
            new[] { "pulse", "march", "drive", "heart" },
            new[] { "minor", "feed", "corrupt" },
        };
        static readonly string[] LayerNames = { "bed", "rhythm", "theme" };
        public static readonly string[] HatchKinds = { "elder", "grove", "brute", "timber", "mire" };

        static string[][][] _clips;

        /// <summary>Resources name (under CMScore/) of a section's stem: sb-&lt;section&gt;-&lt;layer&gt;-&lt;variant&gt;. Built once.</summary>
        public static string Clip(int section, ScoreLayer layer, int variant)
        {
            if (_clips == null)
            {
                var c = new string[Sections.Length][][];
                for (int s = 0; s < Sections.Length; s++)
                {
                    c[s] = new string[LayerNames.Length][];
                    for (int l = 0; l < LayerNames.Length; l++)
                    {
                        c[s][l] = new string[VariantNames[l].Length];
                        for (int v = 0; v < VariantNames[l].Length; v++) c[s][l][v] = "sb-" + Sections[s] + "-" + LayerNames[l] + "-" + VariantNames[l][v];
                    }
                }
                _clips = c;
            }
            return _clips[section][(int)layer][variant];
        }

        /// <summary>The hatch stinger (Resources CMStings/) for the hatched type: the motif in that type's voice.</summary>
        public static string HatchSting(KaijuType t)
        {
            int k = (int)t;
            return "sting-hatch-" + HatchKinds[k >= 0 && k < HatchKinds.Length ? k : 0];
        }

        /// <summary>Plan step k's section (wraps).</summary>
        public static int SectionAt(long k) => Plan[(int)(((k % Plan.Length) + Plan.Length) % Plan.Length)];

        /// <summary>The mix-state table (layer gains and the rhythm form); build_game_score.py STATES holds the same numbers.</summary>
        public static void Table(ScoreState s, out float bed, out RhythmForm rhythm, out float rhythmGain, out float theme)
        {
            switch (s)
            {
                case ScoreState.Contested: bed = 0.80f; rhythm = RhythmForm.March; rhythmGain = 1.00f; theme = 0.80f; return;
                case ScoreState.Leading: bed = 0.90f; rhythm = RhythmForm.March; rhythmGain = 0.85f; theme = 1.00f; return;
                case ScoreState.Trailing: bed = 1.00f; rhythm = RhythmForm.Pulse; rhythmGain = 0.90f; theme = 0.55f; return;
                case ScoreState.Rally: bed = 1.00f; rhythm = RhythmForm.Drive; rhythmGain = 0.80f; theme = 1.00f; return;
                case ScoreState.Egg: bed = 0.70f; rhythm = RhythmForm.Heart; rhythmGain = 0.95f; theme = 0.35f; return;
                case ScoreState.FinalMinute: bed = 1.00f; rhythm = RhythmForm.Drive; rhythmGain = 1.00f; theme = 1.00f; return;
                case ScoreState.Berserk: bed = 0.90f; rhythm = RhythmForm.Drive; rhythmGain = 0.90f; theme = 1.00f; return;
                default: bed = 0.85f; rhythm = RhythmForm.Pulse; rhythmGain = 0.60f; theme = 0.40f; return;
            }
        }

        /// <summary>
        /// The state, by priority: final minute, berserk Kaiju, the egg about to hatch, the hill contested, rally, leading,
        /// trailing, else calm (build-up). The colour: berserk = corrupt; a Kaiju on a team's side = feed; else minor.
        /// </summary>
        public static ScoreTarget Decide(in ScoreInputs i)
        {
            ScoreState s;
            if (i.Remaining <= FinalMinuteSeconds && i.MatchSeconds > FinalMinuteSeconds) s = ScoreState.FinalMinute;
            else if (i.Kaiju == KaijuState.Berserk) s = ScoreState.Berserk;
            else if (!i.Hatched && i.UntilHatch > 0 && i.UntilHatch <= EggSeconds) s = ScoreState.Egg;
            else if (i.HillContested) s = ScoreState.Contested;
            else if (i.Rally) s = ScoreState.Rally;
            else if (i.LeadSeconds >= LeadSecondsFor) s = ScoreState.Leading;
            else if (i.LeadSeconds <= -LeadSecondsFor) s = ScoreState.Trailing;
            else s = ScoreState.Calm;
            Table(s, out float bed, out RhythmForm form, out float rg, out float theme);
            ScoreColour c = i.Kaiju == KaijuState.Berserk ? ScoreColour.Corrupt
                : i.Hatched && (i.Kaiju == KaijuState.Azure || i.Kaiju == KaijuState.Crimson) ? ScoreColour.Feed : ScoreColour.Minor;
            if (i.LessSound) rg *= LessSoundRhythm;
            return new ScoreTarget { State = s, Colour = c, Rhythm = form, BedGain = bed, RhythmGain = rg, ThemeGain = theme };
        }

        /// <summary>The score's level around the hatch stinger (which starts at start and lasts len s): down to 0.15 over 0.12 s
        /// as it hits, held under it, back to 1 over one bar once only its ring is left (len - 1 s).</summary>
        public static float HatchDuck(double t, double start, double len)
        {
            const float Floor = 0.15f;
            if (double.IsNaN(start) || t < start - 0.12) return 1f;
            if (t < start) return (float)(1.0 - (1.0 - Floor) * (t - (start - 0.12)) / 0.12);
            double end = start + Math.Max(0.5, len - 1.0);
            if (t < end) return Floor;
            if (t < end + Bar) return (float)(Floor + (1.0 - Floor) * (t - end) / Bar);
            return 1f;
        }

        /// <summary>The next grid line (spacing step, from origin) strictly after t + margin.</summary>
        public static double NextLine(double origin, double step, double t, double margin = 0.05)
        {
            double k = Math.Floor((t + margin - origin) / step) + 1.0;
            return origin + k * step;
        }

        /// <summary>Equal-power fade factor of a source: fading in (dir +1) or out (dir -1) from at over len s; dir 0 = held at level.</summary>
        public static float Fade(double t, double at, double len, int dir, float level)
        {
            if (dir == 0) return level;
            double x = len <= 0 ? 1.0 : (t - at) / len;
            x = x < 0 ? 0 : x > 1 ? 1 : x;
            return dir > 0 ? (float)Math.Sin(x * Math.PI * 0.5) : (float)Math.Cos(x * Math.PI * 0.5);
        }
    }
}
