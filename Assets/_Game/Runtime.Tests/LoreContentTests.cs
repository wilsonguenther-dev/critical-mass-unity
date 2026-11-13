using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-04 words pack (Assets/_Game/Resources/Lore/*.json, loaded as Resources "Lore/<name>" in player builds): schema of every file, counts and budgets from the build list,
    /// every number a GameConfig token that resolves, no digits outside tokens, no technology or vendor names, and any
    /// line about a rule the sim does not run yet flagged with the build-list id it waits for.
    /// </summary>
    public class LoreContentTests
    {
        static readonly string Dir = Path.Combine(Application.dataPath, "_Game", "Resources", "Lore");

        // ---------------------------------------------------------------- JSON shapes (JsonUtility)
        [Serializable] public class Format { public string id; public string rule; }
        [Serializable] public class Token { public string id; public string path; public string format; }
        [Serializable] public class Slot { public string id; public string meaning; }
        [Serializable] public class TokensFile { public string schema; public Format[] formats; public Token[] tokens; public Slot[] runSlots; }
        [Serializable] public class Line { public string id; public string text; }
        [Serializable] public class PremiseFile { public string schema; public string title; public string text; public Line[] loreLines; }
        [Serializable] public class Card { public string id; public int ticks; public string caption; }
        [Serializable] public class StoryFile { public string schema; public int simHz; public string narrator; public string titleVo; public Card[] cards; }
        [Serializable] public class Callout { public string id; public string trigger; public int priority; public string text; }
        [Serializable] public class AnnouncerFile { public string schema; public Callout[] lines; }
        [Serializable] public class Tip { public string id; public string text; public string needsRule; }
        [Serializable] public class TipsFile { public string schema; public Tip[] tips; }
        [Serializable] public class Relic { public string id; public string name; public string line1; public string line2; }
        [Serializable] public class RelicsFile { public string schema; public Relic[] relics; }
        [Serializable] public class Ability { public string input; public string name; public string text; public string cooldown; public string needsRule; }
        [Serializable] public class ClassCard { public string id; public string name; public string role; public string bio; public int difficulty; public Ability[] abilities; }
        [Serializable] public class ClassesFile { public string schema; public ClassCard[] classes; }
        [Serializable] public class Result { public string id; public string title; public string line; public string reason; }
        [Serializable] public class Outro { public string id; public string caption; public string reason; }
        [Serializable] public class ResultsFile { public string schema; public Result[] results; public Outro[] outros; }
        [Serializable] public class Egg { public string id; public string name; public string design; public string reward; public string line; public string[] waitsOn; }
        [Serializable] public class EggsFile { public string schema; public Egg[] eggs; }

        static readonly string[] Files =
        {
            "tokens.json", "premise.json", "story.json", "announcer.json", "tips.json", "relics.json", "classes.json",
            "results.json", "eastereggs.json",
        };

        /// <summary>Keys whose string values are shown to the player (or spoken).</summary>
        static readonly HashSet<string> DisplayKeys = new HashSet<string>
        {
            "title", "text", "caption", "titleVo", "name", "role", "bio", "line", "line1", "line2", "reason", "cooldown",
        };

        static readonly Regex TokenRe = new Regex(@"\{(cfg|run):([A-Za-z]+)\}");
        static readonly Regex PairRe = new Regex("\"(\\w+)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        static readonly Regex NeedsRuleRe = new Regex(@"^DG-\d{2}[ab]?$");
        /// <summary>Rules the sim does not run yet (gap list section 3): any line naming one must say what it waits for.</summary>
        static readonly Regex UnbuiltRuleRe = new Regex(
            // integration batch 2: jump (DG-02), dig (DG-09) and cleanse (DG-16a) are in the sim now and left this list
            @"\b(heal(?:s|ing|ed)?|fountain|meat|mines?|tnt|ladders?|rope bridges?|upgrades?|stations?|heavy)\b",
            RegexOptions.IgnoreCase);
        /// <summary>Technology, vendor and other games' names never appear in the words.</summary>
        static readonly string[] Forbidden =
        {
            "unity", "blender", "kaykit", "kenney", "elevenlabs", "kokoro", "openai", "gemini", "claude", "anthropic", "gpt",
            "vercel", "supabase", "unreal", "nvidia", "runpod", "cinemachine", "urp", "shader", "nunit", "github",
            "fat princess", "overwatch", "zelda", "blizzard", "dota", "starcraft", "fall guys",
        };

        static string Read(string f) => File.ReadAllText(Path.Combine(Dir, f));
        static T Load<T>(string f) => JsonUtility.FromJson<T>(Read(f));

        static Dictionary<string, Token> TokenMap()
        {
            var map = new Dictionary<string, Token>();
            foreach (Token t in Load<TokensFile>("tokens.json").tokens) map.Add(t.id, t);
            return map;
        }

        static HashSet<string> RunSlots()
        {
            var set = new HashSet<string>();
            foreach (Slot s in Load<TokensFile>("tokens.json").runSlots) set.Add(s.id);
            return set;
        }

        /// <summary>A GameConfig field path such as "Abilities.FarmerRoots.SlowMultiplier" or "Classes[Farmer].MaxHp".</summary>
        static double ResolvePath(GameConfig cfg, string path)
        {
            object o = cfg;
            foreach (string part in path.Split('.'))
            {
                string name = part;
                string index = null;
                int b = part.IndexOf('[');
                if (b >= 0)
                {
                    name = part.Substring(0, b);
                    index = part.Substring(b + 1, part.Length - b - 2);
                }
                FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(f, $"GameConfig path '{path}': no field '{name}' on {o.GetType().Name}");
                o = f.GetValue(o);
                if (index != null)
                {
                    int i = int.TryParse(index, out int n) ? n : (int)Enum.Parse(typeof(ClassId), index);
                    o = ((IList)o)[i];
                }
            }
            return Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }

        /// <summary>The token formats documented in tokens.json.</summary>
        static string Render(GameConfig cfg, Token t)
        {
            double v = ResolvePath(cfg, t.path);
            switch (t.format)
            {
                case "number": return Number(v);
                case "clock": return $"{(int)(v / 60)}:{(int)Math.Round(v % 60):00}";
                case "percent": return Number(Math.Round(v * 100));
                case "percentOff": return Number(Math.Round((1 - v) * 100));
                case "ticksAsSeconds": return Number(v / cfg.SimHz);
                default: Assert.Fail($"token {t.id}: unknown format '{t.format}'"); return null;
            }
        }

        static string Number(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Text with every cfg token resolved and every run slot shown as a one-word stand-in.</summary>
        static string Resolve(string s, GameConfig cfg, Dictionary<string, Token> tokens)
        {
            return TokenRe.Replace(s, m => m.Groups[1].Value == "cfg" ? Render(cfg, tokens[m.Groups[2].Value]) : "X");
        }

        static int Words(string s) => s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

        static void Schema(string file, string schema)
        {
            Assert.AreEqual(schema, JsonUtility.FromJson<TokensFile>(Read(file)).schema, file);
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void EveryFileParsesWithItsSchema()
        {
            foreach (string f in Files)
            {
                string kind = Path.GetFileNameWithoutExtension(f);
                Schema(f, "cm.lore." + kind + "/1");
            }
            Assert.AreEqual(Files.Length, Directory.GetFiles(Dir, "*.json").Length, "an unlisted lore file");
        }

        [Test]
        public void EveryTokenResolvesFromGameConfigAndIsUsed()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Dictionary<string, Token> tokens = TokenMap();
            HashSet<string> slots = RunSlots();
            var used = new HashSet<string>();
            foreach (string f in Files)
            {
                if (f == "tokens.json") continue;
                foreach (Match m in TokenRe.Matches(Read(f)))
                {
                    string kind = m.Groups[1].Value, id = m.Groups[2].Value;
                    if (kind == "cfg") Assert.IsTrue(tokens.ContainsKey(id), $"{f}: undeclared token {m.Value}");
                    else Assert.IsTrue(slots.Contains(id), $"{f}: undeclared run slot {m.Value}");
                    used.Add(id);
                }
            }
            foreach (Token t in tokens.Values)
            {
                double v = ResolvePath(cfg, t.path);
                Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v), t.id);
                Assert.IsNotEmpty(Render(cfg, t), t.id);
                Assert.IsTrue(used.Contains(t.id), $"token {t.id} is declared but never used");
            }
            foreach (string s in slots) Assert.IsTrue(used.Contains(s), $"run slot {s} is declared but never used");
            // Spot checks against today's rules, so a broken path or format shows here first.
            Assert.AreEqual("8:00", Render(cfg, tokens["matchClock"]));
            Assert.AreEqual("4:00", Render(cfg, tokens["hatchClock"]));
            Assert.AreEqual("50", Render(cfg, tokens["rootSlowPct"]));
            Assert.AreEqual("60", Render(cfg, tokens["shieldReductionPct"]));
            Assert.AreEqual("1.5", Render(cfg, tokens["tntFuse"]));
            Assert.AreEqual("160", Render(cfg, tokens["warriorHp"]));
        }

        [Test]
        public void NoDigitsOutsideTokensAndNoVendorNames()
        {
            foreach (string f in Files)
            {
                if (f == "tokens.json") continue;
                foreach (Match m in PairRe.Matches(Read(f)))
                {
                    string key = m.Groups[1].Value, value = m.Groups[2].Value;
                    string lower = value.ToLowerInvariant();
                    foreach (string bad in Forbidden)
                        Assert.IsFalse(Regex.IsMatch(lower, @"\b" + Regex.Escape(bad) + @"\b"), $"{f} {key}: '{bad}' in \"{value}\"");
                    if (!DisplayKeys.Contains(key)) continue;
                    string bare = TokenRe.Replace(value, "");
                    Assert.IsFalse(Regex.IsMatch(bare, @"\d"), $"{f} {key}: a number that is not a GameConfig token in \"{value}\"");
                }
            }
        }

        [Test]
        public void PremiseAndLoreLines()
        {
            var p = Load<PremiseFile>("premise.json");
            int w = Words(p.text);
            Assert.That(w, Is.InRange(120, 160), "premise words");
            foreach (ClassId c in Enum.GetValues(typeof(ClassId)))
                Assert.IsTrue(p.text.Contains(c.ToString()), $"premise names every class: {c}");
            Assert.That(p.loreLines.Length, Is.InRange(6, 12));
            foreach (Line l in p.loreLines) Assert.LessOrEqual(Words(l.text), 20, l.id);
        }

        [Test]
        public void FiveStoryCardsReadInTwentyThreeSeconds()
        {
            var s = Load<StoryFile>("story.json");
            Assert.AreEqual(5, s.cards.Length);
            Assert.AreEqual(30, s.simHz);
            int ticks = 0, words = 0;
            foreach (Card c in s.cards)
            {
                ticks += c.ticks;
                words += Words(c.caption);
                Assert.LessOrEqual(Words(c.caption) / (c.ticks / 30.0) * 60.0, 150.0, $"{c.id} is too fast for the slow narrator");
            }
            Assert.AreEqual(690, ticks, "DG-19: 690 frames at 30 Hz");
            // 23 s for a deep, warm, slow narrator (the owner's ask): about 105 to 135 words a minute.
            Assert.That(words, Is.InRange(40, 52));
            Assert.IsNotEmpty(s.narrator, "the voice direction rides with the cards");
        }

        [Test]
        public void AnnouncerCoversEveryMatchMoment()
        {
            var a = Load<AnnouncerFile>("announcer.json");
            Assert.That(a.lines.Length, Is.InRange(18, 24));
            var triggers = new HashSet<string>();
            var ids = new HashSet<string>();
            foreach (Callout c in a.lines)
            {
                Assert.IsTrue(ids.Add(c.id), "duplicate id " + c.id);
                triggers.Add(c.trigger);
                Assert.LessOrEqual(Words(c.text), 10, c.id + " is too long to call out");
                Assert.That(c.priority, Is.InRange(1, 3), c.id);
            }
            string[] required =
            {
                "matchStart", "hillTaken", "hillTakenByEnemy", "hillLost", "finalMinute", "eggSoon", "hatchAzure", "hatchCrimson",
                "hatchNeutral", "berserkLocked", "berserkHatch", "kaijuKnockedOut", "victory", "defeat", "draw", "multiKo2", "multiKo3",
            };
            foreach (string r in required) Assert.IsTrue(triggers.Contains(r), "no announcer line for " + r);
        }

        [Test]
        public void TwentyFiveTipsEachTwentyWordsOrFewerAndTrueToTheSim()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Dictionary<string, Token> tokens = TokenMap();
            var t = Load<TipsFile>("tips.json");
            Assert.AreEqual(25, t.tips.Length);
            foreach (Tip tip in t.tips)
            {
                string shown = Resolve(tip.text, cfg, tokens);
                Assert.LessOrEqual(Words(shown), 20, $"{tip.id}: \"{shown}\"");
                CheckRuleFlag(tip.id, tip.text, tip.needsRule);
            }
        }

        [Test]
        public void TwelveRelicsWithTwoLinesEach()
        {
            var r = Load<RelicsFile>("relics.json");
            Assert.AreEqual(12, r.relics.Length);
            foreach (Relic x in r.relics)
            {
                Assert.IsNotEmpty(x.name, x.id);
                Assert.That(Words(x.line1), Is.InRange(3, 14), x.id);
                Assert.That(Words(x.line2), Is.InRange(2, 12), x.id);
            }
        }

        [Test]
        public void ClassCardsForAllFiveClasses()
        {
            var c = Load<ClassesFile>("classes.json");
            string[] names = Enum.GetNames(typeof(ClassId));
            Assert.AreEqual(names.Length, c.classes.Length);
            for (int i = 0; i < names.Length; i++)
            {
                ClassCard card = c.classes[i];
                Assert.AreEqual(names[i], card.id, "class cards follow ClassId order");
                Assert.IsNotEmpty(card.name);
                Assert.LessOrEqual(Words(card.role), 5, card.id + " role line");
                Assert.That(Words(card.bio), Is.InRange(10, 30), card.id + " bio");
                Assert.That(card.difficulty, Is.InRange(1, 3), card.id);
                Assert.AreEqual(3, card.abilities.Length, card.id + " has three abilities");
                foreach (Ability ab in card.abilities)
                {
                    Assert.Contains(ab.input, new[] { "attack", "ability", "throw", "interact", "passive" }, card.id + " " + ab.name);
                    Assert.LessOrEqual(Words(ab.text), 25, card.id + " " + ab.name);
                    CheckRuleFlag(card.id + " " + ab.name, ab.name + " " + ab.text, ab.needsRule);
                }
            }
        }

        [Test]
        public void ResultsOutrosAndEasterEggs()
        {
            var r = Load<ResultsFile>("results.json");
            var ids = new List<string>();
            foreach (Result x in r.results) ids.Add(x.id);
            CollectionAssert.AreEquivalent(new[] { "victory", "defeat", "draw" }, ids);
            var outros = new List<string>();
            foreach (Outro o in r.outros)
            {
                outros.Add(o.id);
                Assert.IsNotEmpty(o.reason, "every Kaiju outcome says why: " + o.id);
            }
            CollectionAssert.AreEquivalent(new[] { "azure", "crimson", "neutral", "berserk" }, outros);
            var e = Load<EggsFile>("eastereggs.json");
            Assert.AreEqual(5, e.eggs.Length, "DG-26 builds five");
            foreach (Egg egg in e.eggs)
            {
                Assert.IsNotEmpty(egg.design, egg.id);
                Assert.IsNotEmpty(egg.waitsOn, egg.id);
                foreach (string w in egg.waitsOn) Assert.IsTrue(NeedsRuleRe.IsMatch(w), egg.id + " waitsOn " + w);
            }
        }

        static void CheckRuleFlag(string what, string text, string needsRule)
        {
            if (!string.IsNullOrEmpty(needsRule))
            {
                Assert.IsTrue(NeedsRuleRe.IsMatch(needsRule), $"{what}: needsRule '{needsRule}' is not a build-list id");
                return;
            }
            Match m = UnbuiltRuleRe.Match(text);
            Assert.IsFalse(m.Success, $"{what}: '{m.Value}' is not in the sim yet; flag needsRule with the DG id it waits for");
        }
    }
}
