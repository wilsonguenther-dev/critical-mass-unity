using System;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>How far a cue carries (near / far distances in world units, measured from the gameplay camera's focus).</summary>
    public enum SoundRange : byte { Ui, Near, Mid, Far, Huge }

    /// <summary>Which ducking and voice pool a cue uses. Every bus plays through the settings mixer's Effects group.</summary>
    public enum SoundBus : byte { Sfx, Ui, Ambience, Stinger, Sub }

    /// <summary>
    /// One cue set: the recorded files (Kenney CC0, Resources CMSfx) and/or synth recipes it picks variations from, its
    /// gain, rate, pitch and volume randomisation, voice cap, cooldown (wall seconds), base priority, range and bus. A
    /// Layer cue plays with it at LayerGain (a blade on a whoosh, debris on a blast). FarCue swaps in past FarDistance
    /// (a distant blast). Caption is the on-screen words for an important non-speech cue (null = none).
    /// </summary>
    public sealed class CueDef
    {
        public string Id;
        public string[] Files;
        public string[] Synth;
        public int Variants;
        public float Gain, Rate, PitchJitter, VolJitter;
        public int MaxVoices;
        public float Cooldown;
        public int Priority;
        public SoundRange Range;
        public SoundBus Bus;
        public bool Loop;
        public string Layer;
        public float LayerGain;
        public string FarCue;
        public float FarDistance;
        public string Caption;
    }

    /// <summary>How a route picks among its cue set.</summary>
    public enum Pick : byte
    {
        /// <summary>Silent on purpose (Why says what covers it).</summary>
        Silence,
        /// <summary>Always Cues[0].</summary>
        Fixed,
        /// <summary>Hit: strength x material (see SoundFeed for the index rules).</summary>
        Hit, Death, Swing, Bio, Wall, Hill, Ability, Terraform, Topple, Fuse, Blast, Piece, PieceEnd, Heal,
        /// <summary>Footsteps: by surface under the foot.</summary>
        Surface,
        /// <summary>Only when the actor is no longer alive (a launched corpse landing; jumps land through the tick decode).</summary>
        CorpseOnly,
        /// <summary>b15: the blacksmith hut levelled (A = team): loud for your team, quieter for theirs.</summary>
        Hut,
        /// <summary>b15: a Miner's ore press: B &lt; 0 = refused (your own press only), else the mining hit.</summary>
        Ore,
        /// <summary>b15: a Builder wall or block: B &lt; 0 = refused (yours only); Value = length (1 = a block).</summary>
        BuildWall,
        /// <summary>b15: a deposit at a pad: ore into the hut sounds; other deposits stay with the depth HUD.</summary>
        Bank,
        /// <summary>px lane F: Respawn (crowd blip / my own respawn), AttackContact (whiff swish only), Feint, HitBlocked,
        /// StructureHit, ActionRefused (local only), WorldAction (channel start or the verb).</summary>
        Respawn, Contact, Feint, Blocked, Structure, Refused, WorldAct, Autopilot, Critical,
    }

    /// <summary>One sim event type's sound: a pick rule over a cue set, or an explicit silence with its reason.</summary>
    public struct SimRoute
    {
        public SimEventType Type;
        public Pick Pick;
        public string[] Cues;
        public string Why;
    }

    /// <summary>One world-force source: the audible cue it plays from the bus (or silence with a reason) and its low-frequency layer.</summary>
    public struct ForceRoute
    {
        public ForceSource Source;
        public Pick Pick;
        public string Cue;
        public string Why;
        /// <summary>Low-frequency layer for big impacts (null = none): played when the felt shock passes CameraResponse.BassThreshold.</summary>
        public string Sub;
    }

    /// <summary>Sounds that come from tick edges and view hooks rather than the sim's event ring.</summary>
    public enum SoundEdge : byte { JumpTakeoff, JumpLand, JumpLandHeavy, MeatPickup, BerserkLoop, RelicPickup, Coins, LevelUp, Ambience, Wind, Water, Bird, BirdTrill }

    /// <summary>
    /// THE sound event table (data, not scattered calls): cue sets, and the routes from every game event to them. Tests
    /// hold it complete: every SimEventType, ForceSource, FootfallKind and MomentId has a row (a cue or an explicit silence).
    /// </summary>
    public static class SoundTable
    {
        private static CueDef C(string id, string[] files, string[] synth, float gain, float rate, float pitchJitter, int max,
            float cooldown, int priority, SoundRange range, SoundBus bus = SoundBus.Sfx, string layer = null, float layerGain = 0f,
            string caption = null, bool loop = false, int variants = 0, string farCue = null, float farDistance = 0f) =>
            new CueDef
            {
                Id = id, Files = files ?? Array.Empty<string>(), Synth = synth ?? Array.Empty<string>(), Gain = gain, Rate = rate,
                PitchJitter = pitchJitter, VolJitter = 0.08f, MaxVoices = max, Cooldown = cooldown, Priority = priority, Range = range,
                Bus = bus, Layer = layer, LayerGain = layerGain, Caption = caption, Loop = loop, Variants = variants,
                FarCue = farCue, FarDistance = farDistance,
            };

        private static string[] F(params string[] f) => f;
        private static string[] S(params string[] s) => s;
        private const SoundRange Ui = SoundRange.Ui, Near = SoundRange.Near, Mid = SoundRange.Mid, Far = SoundRange.Far, Huge = SoundRange.Huge;

        /// <summary>Every cue. Order is the cue index (stable within a build).</summary>
        public static readonly CueDef[] Cues =
        {
            // ---- footsteps (through the animation pass's Footfalls sink), by surface; heavy classes add the weight layer
            C("step_grass", F("footstep_grass_000", "footstep_grass_001", "footstep_grass_002"), null, 0.32f, 1f, 0.08f, 3, 0.05f, 10, Near),
            C("step_wood", F("footstep_wood_000", "footstep_wood_001", "footstep_wood_002"), null, 0.36f, 1f, 0.08f, 3, 0.05f, 10, Near),
            C("step_stone", F("footstep_concrete_000", "footstep_concrete_001", "footstep_concrete_002"), null, 0.36f, 1f, 0.08f, 3, 0.05f, 10, Near),
            C("step_dirt", null, S("step_dirt"), 0.3f, 1f, 0.08f, 3, 0.05f, 10, Near),
            C("step_water", null, S("step_water"), 0.32f, 1f, 0.08f, 3, 0.05f, 10, Near),
            C("step_weight", null, S("step_weight"), 0.25f, 1f, 0.06f, 3, 0.05f, 9, Near),
            C("kaiju_step", null, S("kaiju_step"), 0.85f, 1f, 0.05f, 2, 0.12f, 60, Far),

            // ---- swings, per class weight
            // px lane F: the Warrior's cleave is a low, wide air push (px_whoosh_cleave), not a knife slice; the knife slices moved to the feint.
            C("swing_warrior", null, S("px_whoosh_cleave"), 0.42f, 1f, 0.06f, 4, 0.05f, 25, Near, layer: "swing_heavy", layerGain: 0.35f),
            C("swing_heavy", null, S("whoosh_heavy"), 0.4f, 1f, 0.08f, 4, 0.05f, 22, Near),
            C("swing_mid", null, S("whoosh_mid"), 0.35f, 1f, 0.08f, 4, 0.05f, 22, Near),
            C("swing_light", null, S("whoosh_light"), 0.3f, 1f, 0.08f, 4, 0.05f, 20, Near),

            // ---- hits by strength and material
            C("hit_flesh_light", F("impactPunch_medium_000", "impactPunch_medium_001"), null, 0.5f, 1f, 0.08f, 5, 0.04f, 35, Mid),
            C("hit_flesh_heavy", F("impactPunch_heavy_000", "impactPunch_heavy_001"), null, 0.62f, 1f, 0.06f, 4, 0.05f, 45, Mid),
            C("hit_flesh_crit", F("impactPunch_heavy_000", "impactPunch_heavy_001"), null, 0.75f, 0.88f, 0.05f, 3, 0.06f, 55, Mid, layer: "hit_crit_crack", layerGain: 0.6f),
            C("hit_crit_crack", null, S("hit_crit_crack"), 0.5f, 1f, 0.08f, 3, 0.06f, 50, Mid),
            C("hit_armor", F("impactPlate_medium_000", "impactPlate_medium_001"), null, 0.48f, 1f, 0.08f, 4, 0.05f, 38, Mid),
            C("block_shield", F("impactPlate_light_000", "impactPlate_light_002"), null, 0.5f, 1.2f, 0.06f, 3, 0.06f, 40, Mid),
            C("block_heavy", F("impactPlate_heavy_001"), null, 0.6f, 1f, 0.04f, 2, 0.1f, 45, Mid),
            C("hit_stone", null, S("hit_stone"), 0.5f, 1f, 0.08f, 3, 0.08f, 30, Mid),
            C("hit_wood", null, S("hit_wood"), 0.5f, 1f, 0.08f, 3, 0.08f, 30, Mid),
            C("kaiju_melee", F("impactPunch_heavy_002"), null, 0.8f, 0.8f, 0.05f, 2, 0.1f, 70, Far),
            C("kaiju_hurt", null, S("kaiju_hurt"), 0.55f, 1f, 0.08f, 3, 0.06f, 35, Far),
            C("grunt", null, S("grunt"), 0.35f, 1f, 0.12f, 2, 0.2f, 20, Near),
            // feat/hit-feel-1: the voice. Pain on every melee hit taken, effort on every other swing. Near range (far fights stay
            // quiet), 3 and 2 voices at most, pitch jitter 10% on top of the body pitch (SoundFeed.Weight): a brawl is a few
            // voices, the nearest and your own first, never a wall.
            C("pain", null, S("pain_a", "pain_b", "pain_c"), 0.42f, 1f, 0.1f, 3, 0.12f, 28, Near),
            // px lane F fix round: the same pain voices for a hit I dealt or took (own priority, its own repeat guard).
            C("own_pain", null, S("pain_a", "pain_b", "pain_c"), 0.42f, 1f, 0.1f, 2, 0f, 70, Near),
            C("effort", null, S("effort_a", "effort_b"), 0.3f, 1f, 0.1f, 2, 0.15f, 18, Near),
            C("death", F("impactSoft_heavy_000", "impactSoft_heavy_001"), null, 0.6f, 1f, 0.08f, 3, 0.1f, 50, Mid, layer: "death_ko", layerGain: 0.55f),
            C("death_ko", null, S("death_ko"), 0.5f, 1f, 0.06f, 3, 0.1f, 45, Mid),
            C("respawn", F("maximize_003"), null, 0.4f, 1f, 0.03f, 2, 0.2f, 20, Near),

            // ---- movement
            C("jump", null, S("jump"), 0.32f, 1f, 0.1f, 2, 0.1f, 15, Near),
            C("land_light", null, S("land_light"), 0.38f, 1f, 0.1f, 3, 0.08f, 15, Near),
            C("land_heavy", null, S("land_heavy"), 0.5f, 1f, 0.08f, 2, 0.1f, 25, Near),
            C("body_land", F("impactSoft_heavy_000", "impactSoft_heavy_001"), null, 0.5f, 0.85f, 0.08f, 2, 0.12f, 25, Mid),
            C("fall", null, S("fall"), 0.5f, 1f, 0.06f, 2, 0.2f, 30, Mid),
            C("splash", null, S("splash"), 0.5f, 1f, 0.08f, 2, 0.15f, 25, Mid),

            // ---- abilities and refusals
            C("ability_shield", F("impactPlate_heavy_001"), null, 0.45f, 1.4f, 0.04f, 2, 0.2f, 30, Mid),
            C("ability_farmer", null, S("throw"), 0.4f, 0.8f, 0.06f, 2, 0.2f, 30, Mid),
            C("ability_denied", F("click_001"), null, 0.22f, 0.72f, 0.02f, 1, 0.12f, 60, Ui, SoundBus.Ui),
            C("refused", F("error_004"), null, 0.4f, 1f, 0f, 1, 0.3f, 60, Ui, SoundBus.Ui),

            // ---- terrain: dig, place, walls, barricades, pillars
            C("dig", F("impactMining_000", "impactMining_001"), S("dig_dirt"), 0.45f, 1f, 0.1f, 3, 0.08f, 30, Mid),
            C("place_block", null, S("place_block"), 0.5f, 1f, 0.08f, 2, 0.1f, 30, Mid),
            C("barricade_place", F("impactWood_medium_000", "impactPlank_medium_000"), null, 0.5f, 1f, 0.06f, 2, 0.15f, 30, Mid),
            C("barricade_break", F("impactPlank_medium_001", "impactWood_heavy_000"), null, 0.6f, 0.85f, 0.06f, 2, 0.15f, 40, Mid, layer: "debris", layerGain: 0.35f),
            C("wall_crack", F("impactMining_002", "impactMining_003"), S("hit_stone"), 0.55f, 1f, 0.06f, 2, 0.12f, 40, Mid),
            // wall-damage-1: a breakable wall is stone, so its break is stone (it played a wood sample); the barricade gets a wood knock per hit (impactWood_heavy_003, the old wall sample, stays in use there: all 42 credited sounds are used).
            C("wall_break", F("impactMining_002", "impactMining_003"), S("hit_stone"), 0.75f, 0.62f, 0.05f, 2, 0.2f, 55, Far, layer: "debris", layerGain: 0.6f),
            C("barricade_hit", F("impactPlank_medium_000", "impactWood_medium_000", "impactWood_heavy_003"), null, 0.45f, 1.05f, 0.08f, 3, 0.06f, 30, Mid),
            C("debris", null, S("debris"), 0.45f, 1f, 0.08f, 3, 0.1f, 15, Mid),
            C("topple_creak", null, S("topple_creak"), 0.6f, 1f, 0.05f, 2, 0.3f, 50, Far, caption: "Pillar toppling"),
            C("topple_crash", null, S("topple_crash"), 0.85f, 1f, 0.05f, 2, 0.2f, 65, Far, layer: "slam_wood", layerGain: 0.45f),
            C("slam_wood", F("impactWood_heavy_000", "impactWood_heavy_001"), null, 0.7f, 0.6f, 0.05f, 2, 0.15f, 60, Far),

            // ---- demolitions
            C("tnt_throw", null, S("throw"), 0.45f, 1f, 0.08f, 3, 0.08f, 35, Mid),
            C("tnt_land", F("impactWood_medium_000"), null, 0.4f, 1.25f, 0.06f, 2, 0.1f, 35, Mid),
            C("fuse", null, S("fuse_loop"), 0.35f, 1f, 0f, 4, 0f, 60, Near, caption: "Fuse hissing", loop: true),
            C("blast_near", null, S("blast_near"), 1f, 1f, 0.06f, 3, 0.05f, 90, Huge, layer: "debris", layerGain: 0.6f, caption: "Explosion", farCue: "blast_far", farDistance: 24f),
            C("blast_far", null, S("blast_far"), 0.8f, 1f, 0.06f, 2, 0.08f, 50, Huge, caption: "Distant explosion"),
            C("blast_mine", null, S("blast_mine"), 1f, 1f, 0.06f, 3, 0.05f, 90, Huge, layer: "debris", layerGain: 0.5f, caption: "Mine explodes", farCue: "blast_far", farDistance: 24f),
            C("mine_place", null, S("mine_place"), 0.45f, 1f, 0.06f, 2, 0.1f, 30, Near),
            C("mine_arm", null, S("mine_arm"), 0.45f, 1f, 0.02f, 2, 0.2f, 55, Mid, caption: "Mine armed"),
            C("mine_trigger", null, S("mine_trigger"), 0.7f, 1f, 0.02f, 2, 0.05f, 85, Far, caption: "Mine triggered"),
            C("mine_fizzle", null, S("mine_fizzle"), 0.35f, 1f, 0.06f, 2, 0.2f, 15, Near),

            // ---- healer
            C("heal_cast", null, S("heal_cast"), 0.5f, 1f, 0.03f, 2, 0.15f, 35, Mid),
            C("heal_pulse", null, S("heal_pulse"), 0.6f, 1f, 0.02f, 1, 0.3f, 55, Far, caption: "Healing wings"),
            C("heal_landed", null, S("heal_landed"), 0.4f, 1f, 0.05f, 3, 0.1f, 30, Near),
            C("heal_tick", null, S("heal_tick"), 0.25f, 1f, 0.1f, 1, 0.8f, 10, Near),
            C("purify", null, S("purify"), 0.55f, 1f, 0.03f, 1, 0.5f, 50, Far, caption: "Egg purified"),

            // ---- builder pieces
            C("build_ladder", F("impactWood_medium_000", "impactPlank_medium_000"), S("build_span"), 0.5f, 1f, 0.06f, 2, 0.15f, 35, Mid),
            C("build_bridge", F("impactPlank_medium_000"), S("build_bridge"), 0.55f, 1f, 0.05f, 2, 0.15f, 40, Mid),
            C("build_warn", null, S("build_warn"), 0.5f, 1f, 0.05f, 2, 0.5f, 50, Mid, caption: "Bridge creaking"),
            C("piece_expire", F("impactPlank_medium_001"), S("piece_expire"), 0.55f, 1f, 0.06f, 2, 0.2f, 40, Mid),
            C("piece_break", F("impactPlank_medium_001"), S("hit_wood"), 0.6f, 0.9f, 0.06f, 2, 0.15f, 40, Mid),
            C("piece_pickup", null, S("pickup_lift"), 0.4f, 1f, 0.06f, 2, 0.15f, 20, Near),

            // ---- the Kaiju
            C("kaiju_hatch", null, S("kaiju_hatch"), 1f, 1f, 0f, 1, 2f, 100, Huge, caption: "The Kaiju hatches", variants: 1),
            C("kaiju_roar", null, S("kaiju_roar"), 0.9f, 1f, 0.04f, 1, 1.5f, 90, Huge, caption: "Kaiju roars", variants: 2),
            C("kaiju_growl", null, S("kaiju_growl"), 0.7f, 1f, 0.05f, 1, 1f, 70, Far),
            C("kaiju_windup", null, S("kaiju_windup"), 0.75f, 1f, 0.04f, 2, 0.3f, 85, Far, caption: "Claw winding up"),
            C("kaiju_slam_tele", null, S("kaiju_slam_tele"), 0.8f, 1f, 0.03f, 1, 0.5f, 85, Huge, caption: "Ground rumbling"),
            C("kaiju_slam", null, S("kaiju_slam"), 1f, 1f, 0.05f, 2, 0.3f, 95, Huge, layer: "slam_wood", layerGain: 0.6f, caption: "Kaiju slam"),
            C("kaiju_ko", null, S("kaiju_ko"), 0.9f, 1f, 0.03f, 1, 2f, 90, Huge, caption: "Kaiju down", variants: 1),
            C("berserk_lock", F("impactPunch_heavy_002", "impactPlate_heavy_001"), null, 0.8f, 0.6f, 0.04f, 1, 1f, 95, Ui, layer: "kaiju_growl", layerGain: 0.7f, caption: "Kaiju goes berserk"),
            C("berserk_loop", null, S("berserk_loop"), 0.45f, 1f, 0f, 1, 0f, 70, Far, loop: true),

            // ---- egg, corruption, shrine, gathering
            C("egg_feed", F("confirmation_002"), S("egg_feed"), 0.45f, 1f, 0.03f, 2, 0.2f, 30, Mid),
            C("egg_feed_meat", null, S("egg_feed"), 0.45f, 0.8f, 0.03f, 2, 0.2f, 30, Mid),
            C("corrupt", F("bong_001"), S("corrupt"), 0.5f, 0.7f, 0.03f, 2, 0.2f, 40, Mid),
            C("intercept", null, S("intercept"), 0.5f, 1f, 0.05f, 2, 0.15f, 35, Mid),
            C("cleanse_start", null, S("cleanse"), 0.45f, 1f, 0.02f, 1, 0.5f, 40, Far),
            C("cleanse_end", null, S("purify"), 0.5f, 0.8f, 0.02f, 1, 0.5f, 45, Far),
            C("gather_done", F("handleCoins"), null, 0.4f, 1f, 0.05f, 2, 0.15f, 25, Near),
            C("pickup_meat", null, S("pickup_meat"), 0.45f, 1f, 0.05f, 2, 0.1f, 30, Near),
            C("pickup_relic", null, S("pickup_relic"), 0.6f, 1f, 0f, 1, 0.3f, 70, Ui, SoundBus.Ui),
            C("coins", F("handleCoins"), null, 0.6f, 1f, 0.03f, 1, 0.1f, 70, Ui, SoundBus.Ui),
            C("level_up", null, S("level_up"), 0.7f, 1f, 0f, 1, 0.5f, 80, Ui, SoundBus.Ui, variants: 1),

            // ---- the hill
            C("hill_enter", F("toggle_001"), null, 0.35f, 1f, 0.02f, 1, 0.5f, 20, Ui, SoundBus.Ui),
            C("hill_captured", F("confirmation_003"), null, 0.5f, 1f, 0f, 1, 0.5f, 70, Ui, SoundBus.Ui, layer: "hill_fanfare", layerGain: 0.5f, caption: "Hill captured"),
            C("hill_fanfare", null, S("hill_fanfare"), 0.5f, 1f, 0f, 1, 0.5f, 65, Ui, SoundBus.Ui, variants: 1),
            C("hill_lost", null, S("hill_lost"), 0.55f, 1f, 0f, 1, 0.5f, 70, Ui, SoundBus.Ui, caption: "Hill lost", variants: 1),
            C("hill_contested", null, S("hill_contested"), 0.5f, 1f, 0f, 1, 0.5f, 60, Ui, SoundBus.Ui, caption: "Hill contested", variants: 1),

            // ---- pings, map, UI
            C("ping_attack", null, S("ping_attack"), 0.55f, 1f, 0f, 2, 0.1f, 60, Ui, SoundBus.Ui, variants: 1),
            C("ping_onmyway", null, S("ping_onmyway"), 0.55f, 1f, 0f, 2, 0.1f, 60, Ui, SoundBus.Ui, variants: 1),
            C("ping_defend", null, S("ping_defend"), 0.55f, 1f, 0f, 2, 0.1f, 60, Ui, SoundBus.Ui, variants: 1),
            C("ping_help", null, S("ping_help"), 0.55f, 1f, 0f, 2, 0.1f, 60, Ui, SoundBus.Ui, variants: 1),
            C("map_open", null, S("map_open"), 0.4f, 1f, 0f, 1, 0.1f, 50, Ui, SoundBus.Ui, variants: 1),
            C("map_close", null, S("map_close"), 0.4f, 1f, 0f, 1, 0.1f, 50, Ui, SoundBus.Ui, variants: 1),
            C("ui_click", F("click_001", "click3"), null, 0.35f, 1f, 0.04f, 2, 0.03f, 50, Ui, SoundBus.Ui),

            // ---- big-moment stingers (draft PR #40's IBigMomentsAudio)
            C("sting_takeover", null, S("sting_takeover"), 0.8f, 1f, 0f, 1, 0.5f, 100, Ui, SoundBus.Stinger, variants: 1),
            C("sting_small", null, S("sting_small"), 0.6f, 1f, 0f, 1, 0.3f, 100, Ui, SoundBus.Stinger, variants: 1),
            C("sting_kaiju", null, S("sting_kaiju"), 0.85f, 1f, 0f, 1, 0.5f, 100, Ui, SoundBus.Stinger, variants: 1),
            C("sting_heal", null, S("sting_heal"), 0.7f, 1f, 0f, 1, 0.5f, 100, Ui, SoundBus.Stinger, variants: 1),

            // ---- low-frequency layer (draft PR #43's force bus; replaces camera shake when shake is off)
            C("sub_huge", null, S("sub_huge"), 1f, 1f, 0.03f, 1, 0.25f, 90, Ui, SoundBus.Sub, variants: 1),
            C("sub_big", null, S("sub_big"), 0.9f, 1f, 0.04f, 1, 0.15f, 80, Ui, SoundBus.Sub, variants: 1),
            C("sub_step", null, S("sub_step"), 0.7f, 1f, 0.05f, 1, 0.2f, 60, Ui, SoundBus.Sub, variants: 1),
            C("sub_small", null, S("sub_small"), 0.6f, 1f, 0.05f, 1, 0.12f, 50, Ui, SoundBus.Sub, variants: 1),

            // ---- ambience (with the world). b12-jz: wind 0.22 -> 0.08 (-8.8 dB) and water 0.5 -> 0.3 (-4.4 dB); both beds
            // play on the Music mixer group, so the MUSIC slider and its mute control them (SoundDirector.BuildVoices).
            C("amb_wind", null, S("amb_wind"), 0.08f, 1f, 0f, 1, 0f, 5, Ui, SoundBus.Ambience, loop: true),
            C("amb_water", null, S("amb_water"), 0.3f, 1f, 0f, 1, 0f, 5, Mid, SoundBus.Ambience, loop: true),
            C("amb_bird", null, S("amb_bird"), 0.22f, 1f, 0.1f, 1, 2f, 2, Mid, SoundBus.Ambience),
            C("amb_bird_trill", null, S("amb_bird_trill"), 0.2f, 1f, 0.1f, 1, 2f, 2, Mid, SoundBus.Ambience),

            // ---- b15: the Skybreak effects (original, tuned to the score's F minor; tools/audio/skybreak, Resources CMSfxScore).
            // Few and distinct: each its own centroid and shape (tools/audio/skybreak/ANALYZER-REPORT.md), capped and spaced.
            C("sb_dodge", F("sb_dodge"), null, 0.45f, 1f, 0.05f, 3, 0.08f, 30, Near),
            C("sb_wall_place", F("sb_wall_place_0", "sb_wall_place_1"), null, 0.5f, 1f, 0.03f, 2, 0.1f, 35, Mid),
            C("sb_block_place", F("sb_block_place_0", "sb_block_place_1"), null, 0.45f, 1f, 0.03f, 2, 0.1f, 30, Mid),
            C("sb_wall_hit", F("sb_wall_hit_0", "sb_wall_hit_1"), null, 0.45f, 1f, 0.04f, 3, 0.06f, 30, Mid),
            C("sb_wall_break", F("sb_wall_break"), null, 0.7f, 1f, 0.03f, 2, 0.2f, 60, Far, caption: "Wall broken"),
            C("sb_hut_level", F("sb_hut_level"), null, 0.6f, 1f, 0f, 1, 1f, 75, Ui, SoundBus.Ui, caption: "Hut levelled up"),
            C("sb_ore_hit", F("sb_ore_hit_0", "sb_ore_hit_1", "sb_ore_hit_2"), null, 0.42f, 1f, 0.02f, 3, 0.06f, 28, Near),
            C("sb_ore_bank", F("sb_ore_bank"), null, 0.5f, 1f, 0.02f, 2, 0.2f, 35, Mid),
            C("sb_ladder_place", F("sb_ladder_place"), null, 0.5f, 1f, 0.04f, 2, 0.15f, 35, Mid),
            C("sb_climb_step", F("sb_climb_step_0", "sb_climb_step_1"), null, 0.3f, 1f, 0.06f, 3, 0.05f, 10, Near),
            C("sb_bridge_place", F("sb_bridge_place"), null, 0.55f, 1f, 0.03f, 2, 0.15f, 40, Mid),
            C("sb_pit_dig", F("sb_pit_dig"), null, 0.45f, 1f, 0.08f, 3, 0.08f, 30, Mid),
            C("sb_behind_tick", F("sb_behind_tick"), null, 0.35f, 1f, 0f, 1, 1.5f, 85, Ui, SoundBus.Ui, caption: "Danger behind you"),

            // ---- player-experience wave (lane F, 2026-11-22): the local player's own tier (threat hierarchy: other people's
            // events keep their old cues and levels). Measured numbers: data/f-modes-audio/cues.md.
            // MY landed hit: a meaty impact by my weapon (gain >= 0.7) + the hit-confirm tick (the same for every class).
            C("own_impact_thud", F("impactPunch_heavy_000", "impactPunch_heavy_001"), null, 0.85f, 0.92f, 0.05f, 3, 0f, 82, Mid, layer: "own_thud_body", layerGain: 0.85f),
            C("own_thud_body", null, S("px_thud"), 0.8f, 1f, 0.05f, 3, 0f, 80, Mid),
            C("own_impact_crack", F("impactPunch_medium_000", "impactPunch_medium_001"), null, 0.8f, 1.05f, 0.05f, 3, 0f, 82, Mid, layer: "own_crack_body", layerGain: 0.8f),
            C("own_crack_body", null, S("px_crack"), 0.75f, 1f, 0.05f, 3, 0f, 80, Mid),
            C("own_impact_clank", F("impactPlate_medium_000", "impactPlate_medium_001"), null, 0.8f, 1f, 0.05f, 3, 0f, 82, Mid, layer: "own_clank_body", layerGain: 0.7f),
            C("own_clank_body", null, S("px_clank"), 0.7f, 1f, 0.04f, 3, 0f, 80, Mid),
            C("own_impact_bell", F("impactSoft_heavy_000", "impactSoft_heavy_001"), null, 0.75f, 1.1f, 0.04f, 3, 0f, 82, Mid, layer: "own_bell_body", layerGain: 0.8f),
            C("own_bell_body", null, S("px_bell"), 0.7f, 1f, 0.03f, 3, 0f, 80, Mid),
            C("hit_confirm", null, S("px_confirm"), 0.5f, 1f, 0f, 2, 0f, 96, Ui, SoundBus.Ui, variants: 1),
            C("kill_confirm", null, S("px_confirm_kill"), 0.55f, 1f, 0f, 2, 0f, 97, Ui, SoundBus.Ui, variants: 1),
            // MY contact with nobody in it: a light air swish only.
            C("whiff_swish", null, S("px_air_swish"), 0.32f, 1f, 0.08f, 3, 0.05f, 18, Near),
            // The receiving end: my own hurt thump (scaled by the damage share), the ring when my shield took it, my death,
            // my respawn, and the low-health heartbeat (one lub-dub; SoundDirector repeats it at Heartbeat.Rate).
            C("own_hurt", null, S("px_hurt_thump"), 0.72f, 1f, 0.05f, 2, 0f, 86, Ui),
            C("own_hurt_block", null, S("px_hurt_blocked"), 0.6f, 1f, 0.04f, 2, 0f, 86, Ui),
            C("heartbeat", null, S("px_heartbeat"), 0.55f, 1f, 0f, 1, 0.25f, 90, Ui, SoundBus.Ui, caption: "Heartbeat: low health", variants: 1),
            C("my_death", null, S("px_my_death"), 0.7f, 1f, 0f, 1, 0.5f, 100, Ui, SoundBus.Stinger, variants: 1),
            C("my_respawn", null, S("px_my_respawn"), 0.5f, 1f, 0f, 1, 0.5f, 90, Ui, SoundBus.Ui, variants: 1),
            // Kills credited to me: a short proud sting on the Stinger bus (never queued behind the announcer); assists softer.
            C("kill_sting", null, S("px_kill_sting"), 0.7f, 1f, 0f, 2, 0.05f, 100, Ui, SoundBus.Stinger, variants: 1),
            C("assist_sting", null, S("px_assist_sting"), 0.45f, 1f, 0f, 1, 0.1f, 95, Ui, SoundBus.Stinger, variants: 1),
            // The world answers.
            C("denied", null, S("px_denied"), 0.35f, 1f, 0f, 1, 0.25f, 70, Ui, SoundBus.Ui, variants: 1),
            C("shield_clang", null, S("px_shield_clang"), 0.55f, 1f, 0.04f, 2, 0.06f, 45, Mid),
            C("dodge_phew", null, S("px_phew"), 0.45f, 1f, 0.06f, 2, 0.06f, 40, Near),
            // px lane F fix round: the local player's own defence (own priority, captioned) and the glancing hit.
            C("my_dodge", null, S("px_my_dodge"), 0.6f, 1f, 0.04f, 2, 0f, 88, Ui, caption: "Dodged"),
            C("my_shield", null, S("px_shield_clang"), 0.7f, 1.06f, 0.03f, 2, 0f, 88, Ui, layer: "block_shield", layerGain: 0.6f, caption: "Blocked"),
            C("glance_tap", null, S("px_glance"), 0.3f, 1f, 0.06f, 2, 0.1f, 30, Near),
            C("block_wall", null, S("px_wall_thud"), 0.5f, 1f, 0.06f, 2, 0.06f, 35, Mid),
            C("structure_solid", null, S("px_clonk"), 0.55f, 1f, 0.05f, 2, 0.08f, 40, Mid),
            C("dig_start", null, S("px_dig_scrape"), 0.45f, 1f, 0.08f, 2, 0.1f, 32, Mid),
            C("gather_start", null, S("px_gather_start"), 0.3f, 1f, 0.08f, 2, 0.1f, 25, Near),
            C("feint", F("knifeSlice", "knifeSlice2"), null, 0.26f, 1.35f, 0.06f, 2, 0.05f, 28, Near, layer: "feint_flick", layerGain: 0.6f),
            C("feint_flick", null, S("px_feint_flick"), 0.4f, 1f, 0.06f, 2, 0.05f, 26, Near),
            C("corruption_roll", null, S("px_corruption_roll"), 0.5f, 1f, 0.03f, 1, 0.5f, 50, Far, caption: "Corruption rolls"),
            C("kaiju_power", null, S("px_kaiju_power"), 0.6f, 1f, 0.03f, 1, 0.5f, 60, Huge, caption: "Kaiju power"),
            C("crystal_mined", null, S("px_crystal"), 0.45f, 1f, 0.03f, 2, 0.1f, 30, Mid),
            // Combine hooks (not routed from the event ring on this branch): SoundDirector.PlayAutopilot / PlayCritical.
            C("autopilot_on", null, S("px_autopilot_on"), 0.5f, 1f, 0f, 1, 0.3f, 90, Ui, SoundBus.Ui, caption: "Autopilot on", variants: 1),
            C("autopilot_off", null, S("px_autopilot_off"), 0.5f, 1f, 0f, 1, 0.3f, 90, Ui, SoundBus.Ui, caption: "You have control", variants: 1),
            C("critical_hit", null, S("px_critical"), 0.7f, 1f, 0.04f, 2, 0.05f, 75, Mid),
        };

        /// <summary>Every sim event type. Silence rows say what covers them.</summary>
        public static readonly SimRoute[] Sim =
        {
            // cues 9..15 (px lane F): my weapon impacts thud / crack / clank / bell, the hit-confirm tick, my hurt thump, my shield ring
            R(SimEventType.Hit, Pick.Hit, "hit_flesh_light", "hit_flesh_heavy", "hit_flesh_crit", "hit_armor", "block_shield", "block_heavy", "kaiju_melee", "grunt", "pain",
                "own_impact_thud", "own_impact_crack", "own_impact_clank", "own_impact_bell", "hit_confirm", "own_hurt", "own_hurt_block", "own_pain"),
            R(SimEventType.Death, Pick.Death, "death", "hit_flesh_crit", "kaiju_melee", "my_death", "kill_sting", "kill_confirm", "assist_sting"),
            R(SimEventType.Respawn, Pick.Respawn, "respawn", "my_respawn"),
            R(SimEventType.Gather, Pick.Fixed, "gather_done"),
            R(SimEventType.DeliverBiomass, Pick.Bio, "egg_feed", "egg_feed_meat"),
            R(SimEventType.DeliverCorruption, Pick.Fixed, "corrupt"),
            R(SimEventType.Hatch, Pick.Fixed, "kaiju_hatch"),
            R(SimEventType.KaijuTelegraph, Pick.Fixed, "kaiju_slam_tele"),
            R(SimEventType.KaijuMeleeWindup, Pick.Fixed, "kaiju_windup"),
            R(SimEventType.KaijuSlam, Pick.Fixed, "kaiju_slam"),
            R(SimEventType.KaijuKO, Pick.Fixed, "kaiju_ko"),
            R(SimEventType.KaijuRecover, Pick.Fixed, "kaiju_growl"),
            R(SimEventType.WallBreak, Pick.Wall, "wall_crack", "wall_break"),
            R(SimEventType.BarricadePlaced, Pick.Fixed, "barricade_place"),
            R(SimEventType.BarricadeBroken, Pick.Fixed, "barricade_break"),
            R(SimEventType.HillOwnerChange, Pick.Hill, "hill_captured", "hill_lost", "hill_contested", "hill_enter"),
            R(SimEventType.BerserkLocked, Pick.Fixed, "berserk_lock"),
            R(SimEventType.Ability, Pick.Ability, "ability_shield", "ability_farmer", "refused"),
            R(SimEventType.Terraform, Pick.Terraform, "sb_pit_dig", "place_block", "refused"),   // b15: the dig is the Skybreak pit dig
            R(SimEventType.Topple, Pick.Topple, "topple_creak"),
            R(SimEventType.ToppleHit, Pick.Fixed, "hit_stone"),
            R(SimEventType.ToppleDown, Pick.Fixed, "topple_crash"),
            R(SimEventType.Fall, Pick.Fixed, "fall"),
            R(SimEventType.AttackStart, Pick.Swing, "swing_warrior", "swing_mid", "swing_heavy", "swing_mid", "swing_mid", "swing_light"),
            R(SimEventType.KaijuDamaged, Pick.Fixed, "kaiju_hurt"),
            R(SimEventType.DeliverIntercepted, Pick.Fixed, "intercept"),
            R(SimEventType.Cleanse, Pick.Fixed, "cleanse_start"),
            R(SimEventType.CleanseEnd, Pick.Fixed, "cleanse_end"),
            R(SimEventType.MinePlaced, Pick.Fixed, "mine_place"),
            R(SimEventType.MineArmed, Pick.Fixed, "mine_arm"),
            R(SimEventType.MineTriggered, Pick.Fixed, "mine_trigger"),
            R(SimEventType.MineRemoved, Pick.Fixed, "mine_fizzle"),
            R(SimEventType.TntThrown, Pick.Fixed, "tnt_throw"),
            R(SimEventType.TntLanded, Pick.Fuse, "tnt_land", "fuse"),
            R(SimEventType.Explosion, Pick.Blast, "blast_near", "blast_mine"),
            R(SimEventType.PiecePlaced, Pick.Piece, "sb_ladder_place", "sb_bridge_place"),   // b15: Skybreak ladder and bridge
            R(SimEventType.PieceCrumbling, Pick.Fixed, "build_warn"),
            R(SimEventType.PieceExpired, Pick.PieceEnd, "piece_expire", "piece_pickup"),
            R(SimEventType.PieceDestroyed, Pick.Fixed, "piece_break"),
            R(SimEventType.PiecePickedUp, Pick.Fixed, "piece_pickup"),
            R(SimEventType.HealCast, Pick.Heal, "heal_cast", "heal_pulse"),
            R(SimEventType.HealLanded, Pick.Fixed, "heal_landed"),
            R(SimEventType.Purify, Pick.Fixed, "purify"),
            R(SimEventType.Heal, Pick.Fixed, "heal_tick"),
            Silent(SimEventType.MatchEnd, "the music director plays the victory, defeat or draw sting"),
            // ---- integration batch 4: the physics events (#61, DG-69/70/86/99/100)
            R(SimEventType.AttackContact, Pick.Contact, "whiff_swish"),   // px lane F: a whiff only (a contact with hits is the impact's)
            R(SimEventType.AttackCancel, Pick.Fixed, "block_shield"),
            // b16 CANCEL: the feint / hit-cancel hook. px lane F: its own cue (a bright flick over the knife slice)
            R(SimEventType.Feint, Pick.Feint, "feint"),
            Silent(SimEventType.HitStun, "the Hit that caused it plays the impact and the heavy-hit grunt"),
            Silent(SimEventType.Knock, "the Hit or blast that pushed the body is the sound; launched landings come from the force bus"),
            Silent(SimEventType.BodyShove, "the Kaiju's steps and the low-frequency layer come from the force bus"),
            R(SimEventType.PillarCrush, Pick.Fixed, "hit_stone"),
            Silent(SimEventType.MeatPop, "the meat pickup edge plays when someone takes the orb"),
            // ---- integration batch 4: the mechanics-depth events (#61, U-063..U-068)
            Silent(SimEventType.DeliverRefused, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.Handoff, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            R(SimEventType.SourceGather, Pick.Fixed, "gather_done"),
            R(SimEventType.BankDeposit, Pick.Bank, "sb_ore_bank"),   // b15: ore into the hut; other deposits: the depth HUD's own depth-* cue
            Silent(SimEventType.BankWithdraw, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.ClassSwitch, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.Upgrade, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.PickupSpawned, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            R(SimEventType.PickupTaken, Pick.Fixed, "pickup_relic"),
            Silent(SimEventType.PickupExpired, "a pickup fading untaken is visual only"),
            Silent(SimEventType.PowerUpEnded, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            R(SimEventType.WardAbsorb, Pick.Fixed, "block_heavy"),
            Silent(SimEventType.Ping, "pings play through UiSound.Play(ping-*) from the ping wheel and PingBook"),
            Silent(SimEventType.PingRefused, "the ping wheel plays ping-refused"),
            Silent(SimEventType.PingAnswered, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.SupplyDropIncoming, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            R(SimEventType.SupplyDropLanded, Pick.Fixed, "body_land"),
            Silent(SimEventType.SupplyDropContest, "the contest ring is visual; the claim plays"),
            Silent(SimEventType.SupplyDropClaimed, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.SupplyDropExpired, "a drop fading untaken is visual only"),
            Silent(SimEventType.EggExposed, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.EggExposedEnded, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            Silent(SimEventType.BridgeWarning, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            R(SimEventType.BridgeCollapsed, Pick.Fixed, "piece_break"),
            Silent(SimEventType.BridgeRepaired, "the depth HUD (PR #59) plays its own depth-* cue through UiSound for the local player"),
            // CM b13 S0: silent until the owner item routes a cue (one route per type; SoundCoverageTests).
            // b15 (Skybreak sound): the five b13 events that were silent
            R(SimEventType.HutLevel, Pick.Hut, "sb_hut_level"),
            R(SimEventType.OreMined, Pick.Ore, "sb_ore_hit", "refused"),
            R(SimEventType.WallBuilt, Pick.BuildWall, "sb_wall_place", "sb_block_place", "refused"),
            R(SimEventType.WallBroken, Pick.Fixed, "sb_wall_break"),
            R(SimEventType.Dodge, Pick.Fixed, "sb_dodge"),
            R(SimEventType.CorruptionRoll, Pick.Fixed, "corruption_roll"),   // px lane F
            R(SimEventType.KaijuPower, Pick.Fixed, "kaiju_power"),
            R(SimEventType.CrystalMined, Pick.Fixed, "crystal_mined"),
            R(SimEventType.JumpRefused, Pick.Fixed, "refused"),   // b16 [11]: the soft refused cue, the local player's own only (SoundFeed.Fixed)
            // ---- player-experience wave reporting events (lane F routes; SoundFeed.Blocked / Structure / Refused / WorldAct)
            // HitBlocked by Value: spawn protect, passive, immune = clang; dodge = phew; wall = thud; ward = WardAbsorb's
            // block_heavy; shield = the Hit's own block cue.
            // + my dodge, my shield block (local Warrior), the glancing hit (BlockedGlancing 8, 2 s per target).
            R(SimEventType.HitBlocked, Pick.Blocked, "shield_clang", "dodge_phew", "block_wall", "my_dodge", "my_shield", "glance_tap"),
            // StructureHit by kind: barricade wood knock, built wall chip, breakable crack, piece wood, solid clonk.
            R(SimEventType.StructureHit, Pick.Structure, "barricade_hit", "sb_wall_hit", "wall_crack", "hit_wood", "structure_solid"),
            // ActionRefused: the local player's own only, one soft neutral tick (the event is already rate limited).
            R(SimEventType.ActionRefused, Pick.Refused, "denied"),
            // WorldAction: Value 0 = a channel started (dig scrape, pick strike, gather rustle); else the verb's own cue.
            R(SimEventType.WorldAction, Pick.WorldAct, "dig_start", "sb_ore_hit", "gather_start", "sb_pit_dig", "place_block", "sb_wall_place", "sb_ore_bank",
                "egg_feed", "corrupt", "purify", "build_bridge", "gather_done"),
            // AutopilotChanged (lane S2: A actor, Value 1 taken over / 0 handed back): the local player's own only, captioned.
            R(SimEventType.AutopilotChanged, Pick.Autopilot, "autopilot_on", "autopilot_off"),
            // CriticalHit (lane S1: A victim, B attacker, Value 1 counter / 2 punish / 3 back hit): own priority when I am either
            // side, a little brighter for a back hit; a crowd crit nearby is softer. The Hit itself still plays the impact.
            R(SimEventType.CriticalHit, Pick.Critical, "critical_hit"),
        };

        /// <summary>Every world-force source (draft PR #43): audible cue or silence, and the low-frequency layer.</summary>
        public static readonly ForceRoute[] Forces =
        {
            FSilent(ForceSource.HitLight, "the Hit event plays the impact", null),
            FSilent(ForceSource.HitHeavy, "the Hit event plays the impact", null),
            FSilent(ForceSource.HitCritical, "the Hit event plays the impact", "sub_small"),
            FSilent(ForceSource.Death, "the Death event plays the knock-out", "sub_small"),
            FSilent(ForceSource.Block, "the Hit event plays the shield block", null),
            FSilent(ForceSource.KaijuMelee, "the Hit event plays the Kaiju's blow", "sub_big"),
            new ForceRoute { Source = ForceSource.KaijuStep, Pick = Pick.Fixed, Cue = "kaiju_step", Sub = "sub_step" },
            FSilent(ForceSource.KaijuSlam, "the KaijuSlam event plays the slam", "sub_huge"),
            FSilent(ForceSource.Explosion, "the Explosion event plays the blast", "sub_big"),
            FSilent(ForceSource.WallCrack, "the WallBreak event plays the crack", null),
            FSilent(ForceSource.WallBreak, "the WallBreak event plays the break", "sub_small"),
            FSilent(ForceSource.BarricadeBreak, "the BarricadeBroken / PieceExpired events play it", null),
            new ForceRoute { Source = ForceSource.Land, Pick = Pick.CorpseOnly, Cue = "body_land", Why = "jump landings play from the tick decode; this is the launched body landing" },
            new ForceRoute { Source = ForceSource.Splash, Pick = Pick.Fixed, Cue = "splash" },
            FSilent(ForceSource.Hatch, "the Hatch event plays the hatch", "sub_huge"),
            FSilent(ForceSource.Topple, "the ToppleDown event plays the crash", "sub_big"),
            FSilent(ForceSource.Trample, "the Footfalls sink plays every step", null),
        };

        /// <summary>Every footfall kind (the animation pass's Footfalls).</summary>
        public static readonly (FootfallKind Kind, Pick Pick, string[] Cues, string Why)[] Footsteps =
        {
            (FootfallKind.Step, Pick.Surface, new[] { "step_grass", "step_wood", "step_stone", "step_dirt", "step_water", "step_weight" }, null),
            (FootfallKind.Land, Pick.Silence, null, "jump landings play from the tick decode (AirLen edge), never twice"),
            (FootfallKind.KaijuStep, Pick.Silence, null, "the force bus's KaijuStep plays it (it de-duplicates clip contacts and the stride guess)"),
        };

        /// <summary>Every big moment (draft PR #40): the takeover stinger. Corner stingers all use sting_small.</summary>
        public static readonly (MomentId Id, string Cue, string Why)[] Moments =
        {
            (MomentId.None, null, "no moment"),
            (MomentId.OpeningStrike, "sting_takeover", null),
            (MomentId.TwoDown, "sting_takeover", null),
            (MomentId.ThreeDown, "sting_takeover", null),
            (MomentId.Wildfire, "sting_takeover", null),
            (MomentId.HillCleared, "sting_takeover", null),
            (MomentId.BatteringRam, "sting_takeover", null),
            (MomentId.Overcharged, "sting_takeover", null),
            (MomentId.Hatch, null, "b15: the music director plays the hatched type's stinger on the beat (CMStings/sting-hatch-*)"),
            (MomentId.AllegianceTurns, "sting_kaiju", null),
            (MomentId.Berserk, "sting_kaiju", null),
            (MomentId.Quake, "sting_kaiju", null),
            (MomentId.KaijuDown, "sting_kaiju", null),
            (MomentId.Turnaround, "sting_takeover", null),
            (MomentId.AtTheWire, "sting_takeover", null),
            (MomentId.MatchPoint, "sting_takeover", null),
            (MomentId.BlastRadius, "sting_takeover", null),
            (MomentId.Lifeline, "sting_heal", null),
            (MomentId.Finisher, "sting_takeover", null),
        };

        /// <summary>Tick edges and view hooks.</summary>
        public static readonly (SoundEdge Edge, string Cue)[] Edges =
        {
            (SoundEdge.JumpTakeoff, "jump"), (SoundEdge.JumpLand, "land_light"), (SoundEdge.JumpLandHeavy, "land_heavy"),
            (SoundEdge.MeatPickup, "pickup_meat"), (SoundEdge.BerserkLoop, "berserk_loop"), (SoundEdge.RelicPickup, "pickup_relic"),
            (SoundEdge.Coins, "coins"), (SoundEdge.LevelUp, "level_up"), (SoundEdge.Ambience, "amb_wind"), (SoundEdge.Wind, "amb_wind"),
            (SoundEdge.Water, "amb_water"), (SoundEdge.Bird, "amb_bird"), (SoundEdge.BirdTrill, "amb_bird_trill"),
        };

        /// <summary>Names other systems pass to UiSound.Play(string) / CombatAudio.PlayUi (pings, map, HUD), and their cues.</summary>
        public static readonly (string Name, string Cue)[] Named =
        {
            ("ping-attack", "ping_attack"), ("ping-onmyway", "ping_onmyway"), ("ping-defend", "ping_defend"), ("ping-help", "ping_help"),
            ("ping-refused", "refused"), ("map-open", "map_open"), ("map-close", "map_close"), ("ui-click", "ui_click"), ("ui_click", "ui_click"),
            ("ability_denied", "ability_denied"), ("barricade_invalid", "refused"), ("hill_capture", "hill_captured"), ("hill_enter", "hill_enter"),
            ("berserk_lock", "berserk_lock"), ("level_up", "level_up"), ("coins", "coins"),
            // integration batch 4: the depth HUD's named hooks (PR #59, ItemHooks.Sound -> UiSound.Play -> PlayNamed)
            ("depth-ack", "ping_onmyway"), ("depth-bank", "gather_done"), ("depth-withdraw", "gather_done"), ("depth-chop", "hit_wood"),
            ("depth-splash", "splash"), ("depth-coin", "coins"), ("depth-handoff", "piece_pickup"), ("depth-no", "refused"),
            ("depth-bridge-creak", "build_warn"), ("depth-bridge-fall", "piece_break"), ("depth-hammer", "build_bridge"),
            ("depth-drop-incoming", "ping_attack"), ("depth-drop-thud", "body_land"), ("depth-drop-claim", "hill_fanfare"),
            ("depth-egg-exposed", "kaiju_growl"), ("depth-egg-sealed", "cleanse_end"), ("depth-egg-cool", "egg_feed"),
            ("depth-power-spawn", "pickup_relic"), ("depth-power-take", "level_up"), ("depth-power-other", "ui_click"),
            ("depth-power-end", "piece_expire"), ("depth-ward-break", "barricade_break"),
            ("depth-station-open", "map_open"), ("depth-station-move", "ui_click"), ("depth-station-pick", "ui_click"),
            ("depth-station-buy", "coins"), ("depth-switch", "respawn"), ("depth-upgrade", "level_up"),
        };

        private static SimRoute R(SimEventType t, Pick p, params string[] cues) => new SimRoute { Type = t, Pick = p, Cues = cues };
        private static SimRoute Silent(SimEventType t, string why) => new SimRoute { Type = t, Pick = Pick.Silence, Cues = Array.Empty<string>(), Why = why };
        private static ForceRoute FSilent(ForceSource s, string why, string sub) => new ForceRoute { Source = s, Pick = Pick.Silence, Why = why, Sub = sub };

        // ------------------------------------------------------------------ lookups (built once, no allocation after)

        private static Dictionary<string, int> _index;
        private static int[][] _simCues;
        private static Pick[] _simPick;
        private static int[] _forceCue, _forceSub;
        private static Pick[] _forcePick;
        private static int[] _momentCue;
        private static int[] _edgeCue;
        private static int[] _stepCues;
        private static Dictionary<string, int> _named;

        /// <summary>Cue index of an id, or -1.</summary>
        public static int IndexOf(string id)
        {
            Build();
            return id != null && _index.TryGetValue(id, out int i) ? i : -1;
        }

        public static CueDef Cue(int i) => i >= 0 && i < Cues.Length ? Cues[i] : null;

        /// <summary>The route's cue indices for a sim event type (empty for silence or an unknown type).</summary>
        public static int[] SimCues(SimEventType t, out Pick pick)
        {
            Build();
            int k = (int)t;
            if (k < 0 || k >= _simCues.Length || _simCues[k] == null) { pick = Pick.Silence; return Array.Empty<int>(); }
            pick = _simPick[k];
            return _simCues[k];
        }

        public static int ForceCue(ForceSource s, out Pick pick)
        {
            Build();
            int k = (int)s;
            if (k < 0 || k >= _forceCue.Length) { pick = Pick.Silence; return -1; }
            pick = _forcePick[k];
            return _forceCue[k];
        }

        public static int ForceSub(ForceSource s)
        {
            Build();
            int k = (int)s;
            return k >= 0 && k < _forceSub.Length ? _forceSub[k] : -1;
        }

        public static int MomentCue(MomentId id)
        {
            Build();
            int k = (int)id;
            return k >= 0 && k < _momentCue.Length ? _momentCue[k] : IndexOf("sting_takeover");
        }

        public static int EdgeCue(SoundEdge e)
        {
            Build();
            int k = (int)e;
            return k >= 0 && k < _edgeCue.Length ? _edgeCue[k] : -1;
        }

        /// <summary>Footstep cues in order grass, wood, stone, dirt, water, weight.</summary>
        public static int[] StepCues { get { Build(); return _stepCues; } }

        public static int NamedCue(string name)
        {
            Build();
            return name != null && _named.TryGetValue(name, out int i) ? i : -1;
        }

        private static void Build()
        {
            if (_index != null) return;
            var index = new Dictionary<string, int>(Cues.Length);
            for (int i = 0; i < Cues.Length; i++) index[Cues[i].Id] = i;
            int Ix(string id) => id != null && index.TryGetValue(id, out int v) ? v : -1;

            int simN = Enum.GetValues(typeof(SimEventType)).Length;
            _simCues = new int[simN][];
            _simPick = new Pick[simN];
            foreach (SimRoute r in Sim)
            {
                int k = (int)r.Type;
                if (k < 0 || k >= simN) continue;
                var arr = new int[r.Cues != null ? r.Cues.Length : 0];
                for (int i = 0; i < arr.Length; i++) arr[i] = Ix(r.Cues[i]);
                _simCues[k] = arr;
                _simPick[k] = r.Pick;
            }
            int fN = Enum.GetValues(typeof(ForceSource)).Length;
            _forceCue = new int[fN];
            _forceSub = new int[fN];
            _forcePick = new Pick[fN];
            for (int i = 0; i < fN; i++) { _forceCue[i] = -1; _forceSub[i] = -1; }
            foreach (ForceRoute r in Forces)
            {
                int k = (int)r.Source;
                if (k < 0 || k >= fN) continue;
                _forceCue[k] = Ix(r.Cue);
                _forceSub[k] = Ix(r.Sub);
                _forcePick[k] = r.Pick;
            }
            int mN = Enum.GetValues(typeof(MomentId)).Length;
            _momentCue = new int[mN];
            for (int i = 0; i < mN; i++) _momentCue[i] = Ix("sting_takeover");
            foreach (var m in Moments) if ((int)m.Id < mN) _momentCue[(int)m.Id] = Ix(m.Cue);
            int eN = Enum.GetValues(typeof(SoundEdge)).Length;
            _edgeCue = new int[eN];
            for (int i = 0; i < eN; i++) _edgeCue[i] = -1;
            foreach (var e in Edges) _edgeCue[(int)e.Edge] = Ix(e.Cue);
            _stepCues = new int[Footsteps[0].Cues.Length];
            for (int i = 0; i < _stepCues.Length; i++) _stepCues[i] = Ix(Footsteps[0].Cues[i]);
            var named = new Dictionary<string, int>(Named.Length);
            foreach (var n in Named) named[n.Name] = Ix(n.Cue);
            _named = named;
            _index = index;
        }

        /// <summary>Distinct recorded library files the table uses (tests: all 42 credited Kenney sounds). b15: the original
        /// Skybreak effects (sb_*, Resources CMSfxScore) are not library files and are listed by ScoreFiles.</summary>
        public static HashSet<string> Files()
        {
            var s = new HashSet<string>();
            foreach (CueDef c in Cues) foreach (string f in c.Files) if (!IsScoreFile(f)) s.Add(f);
            return s;
        }

        /// <summary>b15: the original Skybreak effect files the table uses (Resources CMSfxScore).</summary>
        public static HashSet<string> ScoreFiles()
        {
            var s = new HashSet<string>();
            foreach (CueDef c in Cues) foreach (string f in c.Files) if (IsScoreFile(f)) s.Add(f);
            return s;
        }

        public static bool IsScoreFile(string f) => f != null && f.StartsWith("sb_", StringComparison.Ordinal);

        /// <summary>Cues with no recorded file (synth only): the list to replace with recordings later.</summary>
        public static List<string> SynthOnly()
        {
            var l = new List<string>();
            foreach (CueDef c in Cues) if (c.Files.Length == 0) l.Add(c.Id);
            return l;
        }
    }
}
