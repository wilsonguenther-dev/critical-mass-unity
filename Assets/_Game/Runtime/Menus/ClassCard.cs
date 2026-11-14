using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    // ===================================================================================================== words

    /// <summary>One ability line of a class card, tokens resolved.</summary>
    public sealed class AbilityWords
    {
        /// <summary>attack, ability, interact, throw or passive (the words pack's "input").</summary>
        public string Input;
        public string Name;
        public string Text;
        /// <summary>Resolved cooldown in seconds as text ("10"), "" when none.</summary>
        public string Cooldown;
        /// <summary>The build-list id the rule waits for ("DG-22"), "" when the sim runs it today.</summary>
        public string NeedsRule;
    }

    /// <summary>One class card's words, tokens resolved from GameConfig.</summary>
    public sealed class ClassWords
    {
        public ClassId Id;
        public string Name;
        public string Role;
        public string Bio;
        /// <summary>1..3.</summary>
        public int Difficulty;
        public AbilityWords[] Abilities;
        /// <summary>Any ability still waits for a rule (the card shows an "early" tag).</summary>
        public bool Early;
    }

    /// <summary>
    /// The DG-04 words pack (Assets/_Game/Content/Lore/*.json, schema cm.lore.*/1) with built-in fallbacks. Source order:
    /// Resources "Lore/&lt;file&gt;" TextAsset (if the pack is ever copied into Resources), then, in the editor, the pack
    /// file itself, then the built-in copy below. Every {cfg:id} token resolves from GameConfig through the pack's
    /// tokens.json (or the built-in token table), so no number on screen can drift from the rules. Lines with an
    /// unresolved token or a {run:} slot are skipped for tips and lore lines. Loaded once; nothing here runs per frame.
    /// </summary>
    public static class LoreWords
    {
        [Serializable] class Token { public string id; public string path; public string format; }
        [Serializable] class TokensFile { public string schema; public Token[] tokens; }
        [Serializable] class Line { public string id; public string text; }
        [Serializable] class PremiseFile { public string schema; public string title; public string text; public Line[] loreLines; }
        [Serializable] class Tip { public string id; public string text; public string needsRule; }
        [Serializable] class TipsFile { public string schema; public Tip[] tips; }
        [Serializable] class Ability { public string input; public string name; public string text; public string cooldown; public string needsRule; }
        [Serializable] class ClassCardJson { public string id; public string name; public string role; public string bio; public int difficulty; public Ability[] abilities; }
        [Serializable] class ClassesFile { public string schema; public ClassCardJson[] classes; }

        static readonly Regex TokenRe = new Regex(@"\{(cfg|run):([A-Za-z]+)\}");

        static GameConfig _cfg;
        static Dictionary<string, Token> _tokens;
        static ClassWords[] _classes;
        static List<string> _tips;
        static List<string> _lore;

        /// <summary>True when the words came from the pack (false: the built-in copy).</summary>
        public static bool FromPack { get; private set; }

        /// <summary>Forget everything loaded (tests; a changed config).</summary>
        public static void Reload()
        {
            _cfg = null;
            _tokens = null;
            _classes = null;
            _tips = null;
            _lore = null;
            FromPack = false;
        }

        static GameConfig Cfg => _cfg ?? (_cfg = GameConfig.CreateDefault());

        static string ReadFile(string name)
        {
            TextAsset ta = Resources.Load<TextAsset>("Lore/" + name);
            if (ta != null && !string.IsNullOrEmpty(ta.text)) return ta.text;
            if (!Application.isEditor) return null;
            try
            {
                string p = Path.Combine(Application.dataPath, "_Game", "Resources", "Lore", name + ".json");
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static T Parse<T>(string json, string schema) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                T v = JsonUtility.FromJson<T>(json);
                FieldInfo f = typeof(T).GetField("schema", BindingFlags.Public | BindingFlags.Instance);
                return f != null && (string)f.GetValue(v) == schema ? v : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Dictionary<string, Token> Tokens()
        {
            if (_tokens != null) return _tokens;
            _tokens = new Dictionary<string, Token>();
            TokensFile tf = Parse<TokensFile>(ReadFile("tokens"), "cm.lore.tokens/1");
            if (tf != null && tf.tokens != null)
                foreach (Token t in tf.tokens) if (t != null && !string.IsNullOrEmpty(t.id)) _tokens[t.id] = t;
            for (int i = 0; i < BuiltinTokens.Length; i += 3)
                if (!_tokens.ContainsKey(BuiltinTokens[i]))
                    _tokens[BuiltinTokens[i]] = new Token { id = BuiltinTokens[i], path = BuiltinTokens[i + 1], format = BuiltinTokens[i + 2] };
            return _tokens;
        }

        /// <summary>A GameConfig field path such as "Abilities.FarmerRoots.SlowMultiplier" or "Classes[Farmer].MaxHp".</summary>
        public static bool TryResolvePath(GameConfig cfg, string path, out double value)
        {
            value = 0;
            if (cfg == null || string.IsNullOrEmpty(path)) return false;
            try
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
                    if (f == null) return false;
                    o = f.GetValue(o);
                    if (index != null)
                    {
                        int i = int.TryParse(index, out int n) ? n : (int)Enum.Parse(typeof(ClassId), index);
                        o = ((IList)o)[i];
                    }
                    if (o == null) return false;
                }
                value = System.Convert.ToDouble(o, CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Number(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        static string Render(GameConfig cfg, Token t)
        {
            if (t == null || !TryResolvePath(cfg, t.path, out double v)) return null;
            switch (t.format)
            {
                case "number": return Number(v);
                case "clock": return $"{(int)(v / 60)}:{(int)Math.Round(v % 60):00}";
                case "percent": return Number(Math.Round(v * 100));
                case "percentOff": return Number(Math.Round((1 - v) * 100));
                case "ticksAsSeconds": return Number(v / cfg.SimHz);
                default: return null;
            }
        }

        /// <summary>Text with every {cfg:id} token resolved; null when a token does not resolve or a {run:} slot is left.</summary>
        public static string Resolve(string text, GameConfig cfg = null)
        {
            if (text == null) return null;
            cfg = cfg ?? Cfg;
            Dictionary<string, Token> tokens = Tokens();
            bool ok = true;
            string s = TokenRe.Replace(text, m =>
            {
                if (m.Groups[1].Value != "cfg" || !tokens.TryGetValue(m.Groups[2].Value, out Token t))
                {
                    ok = false;
                    return string.Empty;
                }
                string r = Render(cfg, t);
                if (r == null) ok = false;
                return r ?? string.Empty;
            });
            return ok ? s : null;
        }

        // ---------------------------------------------------------------- classes

        /// <summary>The card words of a class (pack first, then built in).</summary>
        public static ClassWords Class(ClassId id)
        {
            if (_classes == null) LoadClasses();
            int i = (int)id;
            return i >= 0 && i < _classes.Length ? _classes[i] : _classes[0];
        }

        static void LoadClasses()
        {
            var result = new ClassWords[ClassIds.All.Count];
            ClassesFile pack = Parse<ClassesFile>(ReadFile("classes"), "cm.lore.classes/1");
            if (pack != null && pack.classes != null)
            {
                foreach (ClassCardJson c in pack.classes)
                {
                    ClassWords w = ToWords(c);
                    if (w != null && result[(int)w.Id] == null) result[(int)w.Id] = w;
                }
            }
            bool all = true;
            for (int i = 0; i < result.Length; i++) all &= result[i] != null;
            FromPack = pack != null && all;
            ClassesFile builtin = Parse<ClassesFile>(BuiltinClasses, "cm.lore.classes/1");
            if (builtin != null)
                foreach (ClassCardJson c in builtin.classes)
                {
                    ClassWords w = ToWords(c);
                    if (w != null && result[(int)w.Id] == null) result[(int)w.Id] = w;
                }
            for (int i = 0; i < result.Length; i++)
                if (result[i] == null)
                    result[i] = new ClassWords { Id = ClassIds.All[i], Name = ClassIds.All[i].ToString(), Role = "", Bio = "", Difficulty = 1, Abilities = new AbilityWords[0] };
            _classes = result;
        }

        static ClassWords ToWords(ClassCardJson c)
        {
            if (c == null || string.IsNullOrEmpty(c.id)) return null;
            if (!Enum.TryParse(c.id, out ClassId id) || !Enum.IsDefined(typeof(ClassId), id)) return null;
            var w = new ClassWords
            {
                Id = id,
                Name = c.name ?? c.id,
                Role = Resolve(c.role) ?? c.role ?? "",
                Bio = Resolve(c.bio) ?? c.bio ?? "",
                Difficulty = Mathf.Clamp(c.difficulty, 1, 3),
            };
            var list = new List<AbilityWords>();
            if (c.abilities != null)
                foreach (Ability a in c.abilities)
                {
                    if (a == null) continue;
                    string cd = string.IsNullOrEmpty(a.cooldown) ? "" : Resolve(a.cooldown) ?? "";
                    var aw = new AbilityWords
                    {
                        Input = a.input ?? "passive",
                        Name = a.name ?? "",
                        Text = Resolve(a.text) ?? StripTokens(a.text),
                        Cooldown = cd,
                        NeedsRule = a.needsRule ?? "",
                    };
                    w.Early |= aw.NeedsRule.Length > 0;
                    list.Add(aw);
                }
            w.Abilities = list.ToArray();
            return w;
        }

        static string StripTokens(string s) => s == null ? "" : TokenRe.Replace(s, string.Empty);

        // ---------------------------------------------------------------- tips and lore lines

        /// <summary>Loading-screen tips: every pack tip whose rule exists today and whose tokens resolve.</summary>
        public static IReadOnlyList<string> Tips()
        {
            if (_tips != null) return _tips;
            _tips = new List<string>();
            TipsFile pack = Parse<TipsFile>(ReadFile("tips"), "cm.lore.tips/1") ?? Parse<TipsFile>(BuiltinTips, "cm.lore.tips/1");
            if (pack != null && pack.tips != null)
                foreach (Tip t in pack.tips)
                {
                    if (t == null || !string.IsNullOrEmpty(t.needsRule)) continue;
                    string s = Resolve(t.text);
                    if (!string.IsNullOrEmpty(s)) _tips.Add(s);
                }
            return _tips;
        }

        /// <summary>Short lore lines for the loading screen.</summary>
        public static IReadOnlyList<string> LoreLines()
        {
            if (_lore != null) return _lore;
            _lore = new List<string>();
            PremiseFile pack = Parse<PremiseFile>(ReadFile("premise"), "cm.lore.premise/1") ?? Parse<PremiseFile>(BuiltinPremise, "cm.lore.premise/1");
            if (pack != null && pack.loreLines != null)
                foreach (Line l in pack.loreLines)
                {
                    string s = l != null ? Resolve(l.text) : null;
                    if (!string.IsNullOrEmpty(s)) _lore.Add(s);
                }
            return _lore;
        }

        // ---------------------------------------------------------------- built-in copy (DG-04 words, 2026-11-12)

        /// <summary>id, path, format triples (the subset of tokens.json the built-in words use).</summary>
        static readonly string[] BuiltinTokens =
        {
            "matchClock", "MatchSeconds", "clock",
            "hatchClock", "HatchSeconds", "clock",
            "respawnSeconds", "Respawn.DelaySeconds", "number",
            "farmerGather", "Classes[Farmer].GatherSeconds", "number",
            "farmerCarry", "Classes[Farmer].CarryCapacity", "number",
            "baseGather", "Classes[Warrior].GatherSeconds", "number",
            "baseCarry", "Classes[Warrior].CarryCapacity", "number",
            "warriorHp", "Classes[Warrior].MaxHp", "number",
            "warriorDamage", "Classes[Warrior].AttackDamage", "number",
            "warriorArc", "Classes[Warrior].AttackArcDeg", "number",
            "warriorCooldown", "Classes[Warrior].AbilityCooldown", "number",
            "shieldReductionPct", "Abilities.WarriorShield.DamageReduction", "percent",
            "shieldSeconds", "Abilities.WarriorShield.DurationSeconds", "number",
            "shieldSpeedPct", "Abilities.WarriorShield.MoveSpeedMultiplier", "percent",
            "farmerHp", "Classes[Farmer].MaxHp", "number",
            "farmerDamage", "Classes[Farmer].AttackDamage", "number",
            "farmerCooldown", "Classes[Farmer].AbilityCooldown", "number",
            "rootRange", "Abilities.FarmerRoots.CastRange", "number",
            "rootRadius", "Abilities.FarmerRoots.Radius", "number",
            "rootSlowPct", "Abilities.FarmerRoots.SlowMultiplier", "percentOff",
            "rootSlowSeconds", "Abilities.FarmerRoots.SlowSeconds", "number",
            "minerDamage", "Classes[Miner].AttackDamage", "number",
            "minerCooldown", "Classes[Miner].AbilityCooldown", "number",
            "barricadeHp", "Abilities.MinerBarricade.MaxHp", "number",
            "barricadeMax", "Abilities.MinerBarricade.MaxActivePerMiner", "number",
            "barricadeLife", "Abilities.MinerBarricade.LifetimeSeconds", "number",
            "mineArm", "Abilities.DemolitionsMine.ArmTicks", "ticksAsSeconds",
            "mineDamage", "Abilities.DemolitionsMine.Damage", "number",
            "mineMax", "Abilities.DemolitionsMine.MaxActivePerOwner", "number",
            "tntRange", "Abilities.DemolitionsTnt.ThrowRange", "number",
            "tntFuse", "Abilities.DemolitionsTnt.FuseTicks", "ticksAsSeconds",
            "tntDamage", "Abilities.DemolitionsTnt.Damage", "number",
            "tntCooldown", "Abilities.DemolitionsTnt.CooldownTicks", "ticksAsSeconds",
            // integration batch 3: Builder (DG-24) and Healer (DG-40)
            "ladderCost", "Abilities.BuilderPieces.LadderCost", "number",
            "bridgeCost", "Abilities.BuilderPieces.BridgeCost", "number",
            "pieceLife", "Abilities.BuilderPieces.LifetimeTicks", "ticksAsSeconds",
            "mendHeal", "Abilities.Healer.MendHeal", "number",
            "mendRange", "Abilities.Healer.MendRange", "number",
            "pulseHeal", "Abilities.Healer.PulseHeal", "number",
            "pulseRadius", "Abilities.Healer.PulseRadius", "number",
            "healerCooldown", "Classes[Healer].AbilityCooldown", "number",
            "purifyCooldown", "Abilities.Healer.PurifyCooldownTicks", "ticksAsSeconds",
        };

        /// <summary>The token ids of the built-in table (tests check each resolves).</summary>
        public static IEnumerable<string> BuiltinTokenIds()
        {
            for (int i = 0; i < BuiltinTokens.Length; i += 3) yield return BuiltinTokens[i];
        }

        const string BuiltinClasses = @"{""schema"":""cm.lore.classes/1"",""classes"":[
{""id"":""Warrior"",""name"":""Warrior"",""role"":""Holds the hill."",""bio"":""Wide shield, low stance, no plans to leave. The Warrior is how a team keeps the hill once it has it."",""difficulty"":1,""abilities"":[
{""input"":""attack"",""name"":""Cleave"",""text"":""A wide swing across {cfg:warriorArc}° in front for {cfg:warriorDamage} damage."",""cooldown"":"""",""needsRule"":""""},
{""input"":""ability"",""name"":""Shield Up"",""text"":""Takes {cfg:shieldReductionPct}% off hits from the front for {cfg:shieldSeconds} s. You move at {cfg:shieldSpeedPct}% while it is up."",""cooldown"":""{cfg:warriorCooldown}"",""needsRule"":""""},
{""input"":""passive"",""name"":""Low Stance"",""text"":""{cfg:warriorHp} health, the most in the Basin. You are the wall."",""cooldown"":"""",""needsRule"":""""}]},
{""id"":""Farmer"",""name"":""Farmer"",""role"":""Feeds the egg."",""bio"":""Part gardener, part something older. The backpack glows with whatever it carried last, and the egg can tell from across the Basin."",""difficulty"":2,""abilities"":[
{""input"":""attack"",""name"":""Staff Hook"",""text"":""A quick hook for {cfg:farmerDamage} damage. Not the reason anyone picks a Farmer."",""cooldown"":"""",""needsRule"":""""},
{""input"":""ability"",""name"":""Root Seeds"",""text"":""Lob seeds up to {cfg:rootRange} m. Enemies within {cfg:rootRadius} m of the landing lose {cfg:rootSlowPct}% speed for {cfg:rootSlowSeconds} s."",""cooldown"":""{cfg:farmerCooldown}"",""needsRule"":""""},
{""input"":""passive"",""name"":""Green Thumb"",""text"":""Gathers in {cfg:farmerGather} s and carries {cfg:farmerCarry} at once. Light on armour at {cfg:farmerHp} health."",""cooldown"":"""",""needsRule"":""""}]},
{""id"":""Miner"",""name"":""Miner"",""role"":""Opens the roads."",""bio"":""Lamp on, pick out, and a firm belief that every wall is a door nobody has told yet."",""difficulty"":2,""abilities"":[
{""input"":""attack"",""name"":""Pick-Hammer"",""text"":""{cfg:minerDamage} damage, and every swing knocks a wall down a stage: intact, cracked, gone."",""cooldown"":"""",""needsRule"":""""},
{""input"":""ability"",""name"":""Barricade"",""text"":""Drop a timber-and-stone barricade with {cfg:barricadeHp} health. Up to {cfg:barricadeMax} at once, {cfg:barricadeLife} s each."",""cooldown"":""{cfg:minerCooldown}"",""needsRule"":""""},
{""input"":""passive"",""name"":""Dig and Place"",""text"":""Dig ground out and put it back where it helps: a ramp, a step, a wall."",""cooldown"":"""",""needsRule"":""DG-09""}]},
{""id"":""Demolitions"",""name"":""Demolitions Expert"",""role"":""Rewrites the map."",""bio"":""Singed eyebrows, steady hands, and a backpack nobody stands next to. Carries a pry bar for close calls."",""difficulty"":3,""abilities"":[
{""input"":""attack"",""name"":""Pry Bar"",""text"":""A short, light swing that shoves an enemy back a step. Everything else you hit, you planned."",""cooldown"":"""",""needsRule"":""DG-22""},
{""input"":""ability"",""name"":""Land Mine"",""text"":""A loose-dirt mound everyone can see. It arms in {cfg:mineArm} s and hits for {cfg:mineDamage}. Up to {cfg:mineMax} at once."",""cooldown"":"""",""needsRule"":""DG-22""},
{""input"":""throw"",""name"":""TNT"",""text"":""Thrown up to {cfg:tntRange} m. After a {cfg:tntFuse} s fuse it deals {cfg:tntDamage} and blows a hole in the ground."",""cooldown"":""{cfg:tntCooldown}"",""needsRule"":""DG-22""}]},
{""id"":""Builder"",""name"":""Builder"",""role"":""Builds the way across."",""bio"":""Flat cap, pencil behind the ear, ladder on his back. Give the Builder biomass and a gap, and he hands you a shortcut."",""difficulty"":3,""abilities"":[
{""input"":""ability"",""name"":""Ladder"",""text"":""Spend biomass on a ladder up one level. Anyone can pick it up and move it."",""cooldown"":"""",""needsRule"":""DG-24""},
{""input"":""interact"",""name"":""Rope Bridge"",""text"":""Spend biomass to span a gap. Anyone can carry it off, so guard it."",""cooldown"":"""",""needsRule"":""DG-24""},
{""input"":""passive"",""name"":""Station Upgrades"",""text"":""At your base station, turn biomass into upgrades for the whole team."",""cooldown"":"""",""needsRule"":""DG-24""}]}]}";

        const string BuiltinTips = @"{""schema"":""cm.lore.tips/1"",""tips"":[
{""id"":""tip-01"",""text"":""Your team scores only while it has more living players on the hill than the enemy."",""needsRule"":""""},
{""id"":""tip-02"",""text"":""An even hill scores nothing. Bring one more friend than they did."",""needsRule"":""""},
{""id"":""tip-03"",""text"":""The match ends at {cfg:matchClock}. Most hill time wins. Nothing else counts."",""needsRule"":""""},
{""id"":""tip-04"",""text"":""The egg hatches at {cfg:hatchClock}. Whatever it was fed by then decides who it fights for."",""needsRule"":""""},
{""id"":""tip-09"",""text"":""Farmers gather in {cfg:farmerGather} s and carry {cfg:farmerCarry}. Everyone else takes {cfg:baseGather} s and carries {cfg:baseCarry}."",""needsRule"":""""},
{""id"":""tip-x1"",""text"":""Knocked out? Pick a different class while you wait. You come back in {cfg:respawnSeconds} s."",""needsRule"":""""}]}";

        const string BuiltinPremise = @"{""schema"":""cm.lore.premise/1"",""title"":""Skybreak Basin"",""text"":"""",""loreLines"":[
{""id"":""ll-01"",""text"":""The Basin floats. Nobody has asked what it floats on, and the Basin prefers it that way.""},
{""id"":""ll-02"",""text"":""Whoever stands on the hill is heard. Whoever stands on it longest is remembered.""},
{""id"":""ll-03"",""text"":""The egg was here before the banners. It intends to be here after them.""},
{""id"":""ll-04"",""text"":""Biomass grows back. Patience, it turns out, does not.""},
{""id"":""ll-06"",""text"":""Azure fights under the shield. Crimson fights under the flame. Both fight uphill.""}]}";
    }

    // ===================================================================================================== glyphs

    /// <summary>Key glyph text for a words-pack input on a device, from the CURRENT bindings (after rebinding).</summary>
    public static class ClassGlyphs
    {
        /// <summary>The Player action behind a words-pack input ("" = passive, no key).</summary>
        public static string ActionFor(string input)
        {
            switch (input)
            {
                case "attack": return "Attack";
                case "ability": return "Ability";
                case "interact": return "Interact";
                case "throw": return "DigPlace";
                default: return "";
            }
        }

        public static string Glyph(string input, ControlDevice device)
        {
            string action = ActionFor(input);
            if (action.Length == 0) return "PASSIVE";
            InputActionMap map = ControlBindings.Map(InputSystem.actions);
            InputAction a = map != null ? map.FindAction(action, false) : null;
            return ControlBindings.PromptLabel(a, device);
        }
    }

    // ===================================================================================================== tile

    /// <summary>
    /// A focusable class tile (keyboard, mouse and controller share the EventSystem focus, as the pause menu's MenuItem).
    /// Focus shows as a thick Tool Brass rim and a 4% lift that eases in about 120 ms (view only). Picked shows a brass
    /// ribbon. Enter / A / click submits.
    /// </summary>
    public sealed class ClassTile : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public Action Submitted;
        public Action Focused;
        public VectorShape Body;
        public VectorShape Ribbon;
        public ClassId Class;
        bool _focus;
        float _scale = 1f;

        public bool HasFocus => _focus;

        public void SetPicked(bool on)
        {
            if (Ribbon != null && Ribbon.gameObject.activeSelf != on) Ribbon.gameObject.SetActive(on);
        }

        public void OnSubmit(BaseEventData e)
        {
            if (IsInteractable() && Time.unscaledTime >= MenuItem.SubmitBlockedUntil) Submitted?.Invoke();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (IsInteractable() && e.button == PointerEventData.InputButton.Left && Time.unscaledTime >= MenuItem.SubmitBlockedUntil)
                Submitted?.Invoke();
        }

        public override void OnSelect(BaseEventData e)
        {
            base.OnSelect(e);
            Focused?.Invoke();
        }

        public override void OnPointerEnter(PointerEventData e)
        {
            base.OnPointerEnter(e);
            if (IsInteractable() && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) Select();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            _focus = state == SelectionState.Selected || state == SelectionState.Highlighted || state == SelectionState.Pressed;
            if (Body == null) return;
            Body.SetRim(_focus ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color);
            float rim = _focus ? 5f : 2f;
            if (Mathf.Abs(Body.RimPx - rim) > 0.01f)
            {
                Body.RimPx = rim;
                Body.SetVerticesDirty();
            }
            if (instant)
            {
                _scale = _focus ? 1.04f : 1f;
                transform.localScale = new Vector3(_scale, _scale, 1f);
            }
        }

        void Update()
        {
            float target = _focus ? 1.04f : 1f;
            if (Mathf.Abs(_scale - target) < 1e-4f) return;
            _scale = Mathf.MoveTowards(_scale, target, Time.unscaledDeltaTime * (0.04f / 0.12f));
            transform.localScale = new Vector3(_scale, _scale, 1f);
        }
    }

    // ===================================================================================================== card

    /// <summary>
    /// A class card built in code from the HUD tokens (Panel Navy, Void Navy outline, Warm Cream rim, bible p05 fonts).
    /// Tile: portrait, name, role line, difficulty pips, the class ability with its key glyph, an optional hotkey badge;
    /// focusable (ClassTile). Detail: big portrait, name, role, difficulty, bio and three abilities, each with its icon,
    /// key glyph (current bindings, device used last), cooldown and text; lines whose rule is not in the game yet carry a
    /// "COMING SOON" tag. Bind once per class or team change; RefreshGlyphs on a device or binding change. No per-frame work.
    /// </summary>
    public sealed class ClassCard
    {
        public enum Layout { Tile, Detail }

        public readonly Layout Kind;
        public readonly RectTransform Root;
        public readonly VectorShape Body;
        public ClassTile Tile { get; private set; }
        public ClassId Class { get; private set; }
        public TeamId Team { get; private set; }

        readonly float _w, _h;
        float _rowY, _rowH;
        HudIcon _portrait;
        float _portraitPx;
        Text _name, _role, _bio, _hotkey, _diffLabel;
        readonly VectorShape[] _pips = new VectorShape[3];
        Text _tileKey, _tileAbility;
        readonly AbilityRow[] _rows = new AbilityRow[3];
        ClassWords _words;

        sealed class AbilityRow
        {
            public RectTransform Rt;
            public HudIcon Icon;
            public VectorShape Chip;
            public Text Key, Name, Cooldown, Text, Soon;
            public VectorShape SoonTag;
        }

        static Color Navy => CMTokens.VoidNavy.Color;
        static Color Cream => CMTokens.WarmCream.Color;
        static Color Brass => CMTokens.ToolBrass.Color;
        static Color PanelNavy => CMTokens.PanelNavy.Color;
        static Color Empty => CMTokens.MeterEmpty.Color;

        ClassCard(Layout kind, RectTransform root, VectorShape body, float w, float h)
        {
            Kind = kind;
            Root = root;
            Body = body;
            _w = w;
            _h = h;
        }

        /// <summary>Builds a card at (x, y) from parent's top-left (1920x1080 reference units).</summary>
        public static ClassCard Build(Transform parent, Layout kind, float x, float y, float w, float h, string name = "Class Card")
        {
            VectorShape body = MenuUi.Shape(parent, name, ShapeKind.RoundRect, x, y, w, h, PanelNavy, kind == Layout.Tile ? 16f : 22f, kind == Layout.Tile);
            body.OutlinePx = 4f;
            body.RimPx = kind == Layout.Tile ? 2f : 3f;
            var card = new ClassCard(kind, body.rectTransform, body, w, h);
            if (kind == Layout.Tile) card.BuildTile();
            else card.BuildDetail();
            return card;
        }

        void BuildTile()
        {
            Tile = Root.gameObject.AddComponent<ClassTile>();
            Tile.Body = Body;
            Tile.targetGraphic = Body;
            Tile.transition = Selectable.Transition.None;
            float p = Mathf.Min(_w * 0.46f, _h * 0.42f);
            _portraitPx = p;
            // Portrait on a team-coloured disc.
            VectorShape disc = MenuUi.Shape(Root, "Portrait Disc", ShapeKind.Circle, (_w - p - 8f) * 0.5f, 10f, p + 8f, p + 8f, Empty, 0f);
            disc.RimPx = 0f;
            disc.OutlinePx = 3f;
            _portrait = HudIcon.Make(Root, "Portrait", (_w - p) * 0.5f, 14f, p, p);
            float y = 18f + p + 4f;
            _name = MenuUi.Label(Root, "Name", CMTokens.Display, _w > 290f ? 26 : 22, Cream, TextAnchor.MiddleCenter, 6f, y, _w - 12f, 32f, "", 3f);
            y += 32f;
            _role = MenuUi.Label(Root, "Role", CMTokens.Ui, UiScale.TypeSmall, Brass, TextAnchor.MiddleCenter, 6f, y, _w - 12f, 24f, "", 2f);
            y += 28f;
            BuildPips(Root, (_w - 3 * 16f - 2 * 8f) * 0.5f, y, 16f, 8f);
            y += 24f;
            // owner walk 1: the key chip is wide enough for RIGHT CLICK (it overflowed the 78 px chip into the ability name)
            _tileKey = Chip(Root, "Key", 12f, y, 104f, 28f, 16, out _);
            _tileAbility = MenuUi.Label(Root, "Ability", CMTokens.Ui, UiScale.TypeMin, Cream, TextAnchor.MiddleLeft, 124f, y, _w - 132f, 28f, "", 2f);
            Fit(_name, 14);
            Fit(_role, 12);
            Fit(_tileAbility, 10);   // batch 4: "LADDER AND BRIDGE" was cut at 12 px on the 236 px respawn tile
            _hotkey = MenuUi.Label(Root, "Hotkey", CMTokens.Mono, UiScale.TypeSmall, Navy, TextAnchor.MiddleCenter, 10f, 10f, 30f, 30f, "", 0f);
            VectorShape hk = MenuUi.Shape(Root, "Hotkey Badge", ShapeKind.Circle, 8f, 8f, 34f, 34f, Brass, 0f);
            hk.RimPx = 0f;
            hk.transform.SetSiblingIndex(_hotkey.transform.GetSiblingIndex());
            _hotkey.gameObject.SetActive(false);
            hk.gameObject.SetActive(false);
            _hotkeyBadge = hk;
            VectorShape ribbon = MenuUi.Shape(Root, "Picked", ShapeKind.Banner, _w - 50f, -6f, 38f, 52f, Brass, 0f);
            ribbon.RimPx = 0f;
            Tile.Ribbon = ribbon;
            ribbon.gameObject.SetActive(false);
        }

        VectorShape _hotkeyBadge;

        /// <summary>Shows a hotkey number badge (respawn screen: 1..5).</summary>
        public void SetHotkey(string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (_hotkey == null) return;
            _hotkey.gameObject.SetActive(on);
            _hotkeyBadge.gameObject.SetActive(on);
            if (on) HudText.Set(_hotkey, text);
        }

        void BuildDetail()
        {
            float p = 196f;
            _portraitPx = p;
            VectorShape disc = MenuUi.Shape(Root, "Portrait Disc", ShapeKind.Circle, 30f, 28f, p + 12f, p + 12f, Empty, 0f);
            disc.RimPx = 0f;
            disc.OutlinePx = 4f;
            _portrait = HudIcon.Make(Root, "Portrait", 36f, 34f, p, p);
            float tx = 30f + p + 40f, tw = _w - tx - 30f;
            _name = MenuUi.Label(Root, "Name", CMTokens.Display, UiScale.TypeDisplay, Cream, TextAnchor.MiddleLeft, tx, 28f, tw, 70f, "", 4f);
            _role = MenuUi.Label(Root, "Role", CMTokens.Ui, UiScale.TypeButton, Brass, TextAnchor.MiddleLeft, tx, 98f, tw, 38f, "", 3f);
            Fit(_name, 30);
            Fit(_role, UiScale.TypeMin);   // finisher: the two-line role ran past the panel and over DIFFICULTY
            _diffLabel = MenuUi.Label(Root, "Difficulty", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleLeft, tx, 140f, 130f, 26f, "DIFFICULTY", 2f);
            BuildPips(Root, tx + 132f, 145f, 16f, 8f);
            _bio = MenuUi.Fit(MenuUi.Label(Root, "Bio", CMTokens.Ui, UiScale.TypeBody, Cream, TextAnchor.UpperLeft, tx, 176f, tw, 70f, "", 2f), 21);
            _bio.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bio.lineSpacing = 1.05f;

            float rowY = 260f, rowH = (_h - rowY - 24f) / 3f;
            _rowY = rowY;
            _rowH = rowH;
            for (int i = 0; i < 3; i++)
            {
                var r = new AbilityRow();
                r.Rt = MenuUi.Rt(Root, "Ability " + i);
                MenuUi.Place(r.Rt, 24f, rowY + i * rowH, _w - 48f, rowH - 10f);
                VectorShape bg = MenuUi.Shape(r.Rt, "Back", ShapeKind.RoundRect, 0f, 0f, _w - 48f, rowH - 10f, CMTokens.WithAlpha(Navy, 0.45f), 14f);
                bg.RimPx = 0f;
                bg.OutlinePx = 0f;
                r.Icon = HudIcon.Make(r.Rt, "Icon", 14f, (rowH - 10f - 64f) * 0.5f, 64f, 64f);
                r.Key = Chip(r.Rt, "Key", 92f, 12f, 104f, 34f, 18, out r.Chip);
                r.Name = MenuUi.Label(r.Rt, "Name", CMTokens.Display, UiScale.TypeBody, Cream, TextAnchor.MiddleLeft, 210f, 10f, 420f, 36f, "", 3f);
                r.Cooldown = MenuUi.Label(r.Rt, "Cooldown", CMTokens.Mono, UiScale.TypeSmall, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleRight, _w - 48f - 200f - 18f, 12f, 200f, 32f, "", 2f);
                r.SoonTag = MenuUi.Shape(r.Rt, "Soon", ShapeKind.RoundRect, _w - 48f - 180f - 18f, 12f, 180f, 30f, CMTokens.WithAlpha(Brass, 0.18f), -1f);
                r.SoonTag.RimColor = Brass;
                r.SoonTag.RimPx = 2f;
                r.SoonTag.OutlinePx = 2f;
                r.Soon = MenuUi.Fit(MenuUi.Label(r.SoonTag.transform, "Label", CMTokens.Ui, UiScale.TypeMin, Brass, TextAnchor.MiddleCenter, 0f, 0f, 180f, 30f, "", 2f), 15);
                r.Text = MenuUi.Label(r.Rt, "Text", CMTokens.Ui, UiScale.TypeSmall, Cream, TextAnchor.UpperLeft, 92f, 52f, _w - 48f - 92f - 18f, rowH - 64f, "", 2f);
                r.Text.horizontalOverflow = HorizontalWrapMode.Wrap;
                _rows[i] = r;
            }
        }

        void BuildPips(Transform parent, float x, float y, float size, float gap)
        {
            for (int i = 0; i < 3; i++)
            {
                VectorShape d = MenuUi.Shape(parent, "Pip " + i, ShapeKind.Diamond, x + i * (size + gap), y, size, size, Brass, 0f);
                d.OutlinePx = 2f;
                d.RimPx = 0f;
                _pips[i] = d;
            }
        }

        /// <summary>One line that shrinks (down to min) instead of running out of its box: long names such as
        /// "Demolitions Expert" in the wide display face.</summary>
        public static void Fit(Text t, int min)
        {
            t.resizeTextMaxSize = t.fontSize;
            t.resizeTextMinSize = Mathf.Min(min, t.fontSize);
            MenuUi.Contain(t);   // hud-containment-1: the one text rule (wrap if the box allows, shrink, then ellipsis)
        }

        /// <summary>A key glyph chip: Void Navy fill, Warm Cream rim, mono text.</summary>
        static Text Chip(Transform parent, string name, float x, float y, float w, float h, int size, out VectorShape chip)
        {
            chip = MenuUi.Shape(parent, name + " Chip", ShapeKind.RoundRect, x, y, w, h, Navy, 8f);
            chip.OutlinePx = 2f;
            chip.RimPx = 2f;
            Text t = MenuUi.Label(chip.transform, name, CMTokens.Mono, size, Cream, TextAnchor.MiddleCenter, 0f, 0f, w, h, "", 0f);
            Fit(t, 10);
            return t;
        }

        /// <summary>Icon tier so a texture of base 1x size basePx is never stretched up at displayPx x canvasScale.</summary>
        public static int TierFor(float displayPx, float basePx, float canvasScale)
        {
            float need = displayPx * Mathf.Max(0.01f, canvasScale);
            return need <= basePx ? 1 : need <= basePx * 2f ? 2 : 4;
        }

        /// <summary>Integration batch 5: the detail card's one-line role, "Holds the hill.   FRONT LINE": the role and the tag
        /// (the depth role line up to its first full stop); the rest of that sentence ran past the card.</summary>
        public static string RoleWithTag(string role, string depthLine)
        {
            if (string.IsNullOrEmpty(depthLine)) return role ?? string.Empty;
            int dot = depthLine.IndexOf('.');
            string tag = dot > 0 ? depthLine.Substring(0, dot) : depthLine;
            return string.IsNullOrEmpty(role) ? tag : role + "   " + tag;
        }

        /// <summary>Fills the card with a class's words, portrait and icons for a team.</summary>
        public void Bind(ClassId cls, TeamId team, float canvasScale)
        {
            Class = cls;
            Team = team;
            if (Tile != null) Tile.Class = cls;
            _words = LoreWords.Class(cls);
            int c = (int)cls, t = (int)team;
            _portrait.Set(UiIcons.Portrait[c, t], TierFor(_portraitPx, 106f, canvasScale));
            HudText.Set(_name, _words.Name.ToUpperInvariant());
            // owner walk 1: the small tile shows the one-line role only (the role tag after it was cut on every tile); the
            // detail card keeps the full line with the tag. Integration batch 5: on ONE line ("Feeds the egg.   FEEDER"): two
            // lines in the 38 px row were cut at the bottom on every class (Bind runs on a class change, not per frame)
            HudText.Set(_role, Kind == Layout.Tile || !View.Mechanics.DepthRead.Compiled ? _words.Role : RoleWithTag(_words.Role, View.Mechanics.DepthWords.Role(_words.Id))); // U-063 role line
            for (int i = 0; i < 3; i++)
            {
                bool on = i < _words.Difficulty;
                _pips[i].SetFill(on ? Brass : Empty);
            }
            if (_bio != null) HudText.Set(_bio, _words.Bio);
            int shown = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                AbilityRow r = _rows[i];
                if (r == null) continue;
                bool has = i < _words.Abilities.Length;
                // Polish pass 1: rows close up from the top, so a class with fewer lines leaves no hole.
                r.Rt.gameObject.SetActive(has);
                if (!has) continue;
                MenuUi.Place(r.Rt, 24f, _rowY + shown * _rowH, _w - 48f, _rowH - 10f);
                shown++;
                AbilityWords a = _words.Abilities[i];
                HudText.Set(r.Name, a.Name.ToUpperInvariant());
                HudText.Set(r.Text, a.Text);
                // Polish pass 1: every rule a card names is in the game since batch 3 (Demolitions DG-22, Builder DG-24), so
                // the old COMING SOON tag never shows (its label is empty too).
                bool soon = false;
                r.SoonTag.gameObject.SetActive(soon);
                r.Cooldown.gameObject.SetActive(!soon && a.Cooldown.Length > 0);
                if (a.Cooldown.Length > 0) HudText.Set(r.Cooldown, a.Cooldown + " S COOLDOWN");
                string icon = a.Input == "attack" ? UiIcons.Attack[c, t] : a.Input == "passive" ? UiIcons.ClassBadge[c, t] : UiIcons.Ability[c, t];
                r.Icon.Set(icon, TierFor(64f, 54f, canvasScale));
            }
            RefreshGlyphs(ControlBindings.ActiveDevice);
        }

        /// <summary>The ability a tile shows: the class ability, else the first line with a key.</summary>
        static AbilityWords TileAbility(ClassWords w)
        {
            for (int i = 0; i < w.Abilities.Length; i++) if (w.Abilities[i].Input == "ability") return w.Abilities[i];
            for (int i = 0; i < w.Abilities.Length; i++) if (ClassGlyphs.ActionFor(w.Abilities[i].Input).Length > 0) return w.Abilities[i];
            return w.Abilities.Length > 0 ? w.Abilities[0] : null;
        }

        /// <summary>Key glyphs from the current bindings for a device.</summary>
        public void RefreshGlyphs(ControlDevice device)
        {
            if (_words == null) return;
            if (_tileKey != null)
            {
                AbilityWords a = TileAbility(_words);
                HudText.Set(_tileKey, a != null ? ClassGlyphs.Glyph(a.Input, device) : "-");
                HudText.Set(_tileAbility, a != null ? a.Name.ToUpperInvariant() : "");
            }
            for (int i = 0; i < _rows.Length; i++)
            {
                AbilityRow r = _rows[i];
                if (r == null || i >= _words.Abilities.Length) continue;
                HudText.Set(r.Key, ClassGlyphs.Glyph(_words.Abilities[i].Input, device));
            }
        }
    }
}
