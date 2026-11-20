namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>The weapon feel's sound (feat/b15-combat-feel).</summary>
    public enum CombatCue { SwingLight, SwingMid, SwingHeavy, HitLight, HitHeavy, HitCrit, HitArmor, Blocked, BlockedHeavy, Dodge, WallHit, WallBreak, KaijuHit }

    /// <summary>
    /// b15 (Skybreak sound): the small cue table the combat-feel lane calls for its per-weapon / per-hit hooks, so it never
    /// names files or mixes on its own. Each entry is a SoundTable cue, with its voice cap, cooldown, priority and caption,
    /// placed in screen space by SoundDirector. Overlap: the sim's own events (hits, swings, blocks, Kaiju damage, Dodge,
    /// WallBroken) already play these through SoundFeed, so a hook should only add what the sim does not say (a view-only
    /// beat such as a hit-stop accent); calling it for the same moment as the sim event plays it twice.
    /// </summary>
    public static class CombatSoundHooks
    {
        /// <summary>The table cue behind each hook.</summary>
        public static string CueId(CombatCue c)
        {
            switch (c)
            {
                case CombatCue.SwingLight: return "swing_light";
                case CombatCue.SwingMid: return "swing_mid";
                case CombatCue.SwingHeavy: return "swing_heavy";
                case CombatCue.HitLight: return "hit_flesh_light";
                case CombatCue.HitHeavy: return "hit_flesh_heavy";
                case CombatCue.HitCrit: return "hit_flesh_crit";
                case CombatCue.HitArmor: return "hit_armor";
                case CombatCue.Blocked: return "block_shield";
                case CombatCue.BlockedHeavy: return "block_heavy";
                case CombatCue.Dodge: return "sb_dodge";
                case CombatCue.WallHit: return "sb_wall_hit";
                case CombatCue.WallBreak: return "sb_wall_break";
                case CombatCue.KaijuHit: return "kaiju_hurt";
                default: return null;
            }
        }

        /// <summary>Plays a hook at a sim position. own = the local player's action (it wins a voice). gain scales the cue's
        /// table gain (0..1.5); pitch its rate (0.5..2). False when refused (muted, capped, inaudible, unknown).</summary>
        public static bool Play(CombatCue c, double simX, double simZ, bool own = false, float gain = 1f, float pitch = 1f)
        {
            string id = CueId(c);
            if (id == null || SoundTable.IndexOf(id) < 0) return false;
            if (own) return SoundDirector.PlayCueAt(id, simX, simZ, true);
            return SoundDirector.PlayCueAt(id, simX, simZ, gain, pitch);
        }
    }
}
