using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Skills
{
    /// <summary>Every class skill (and the Kaiju's claw and slam) that the skill-effects lane gives a readable tell.</summary>
    public enum SkillId : byte
    {
        WarriorCleave, WarriorShield,
        FarmerHook, FarmerRoots,
        MinerPick, MinerBarricade, MinerDig, MinerPlace,
        DemoMine, DemoTnt,
        BuilderHammer, BuilderBuild, BuilderCarry, BuilderRepair,
        HealerMend, HealerWings, HealerPurify,
        KaijuClaw, KaijuSlam,
    }

    /// <summary>How the capture director presses a skill (SkillCapture).</summary>
    public enum SkillPress : byte { None, Attack, Ability, Terraform, TerraformPlace }

    /// <summary>One skill's three beats (wind-up on the caster, the effect, the mark on whoever it affects) and its pose.</summary>
    public sealed class SkillVisual
    {
        public SkillId Id;
        public ClassId Cls;
        public bool Kaiju;
        /// <summary>HUD name and a short key for capture file names.</summary>
        public string Name, Key;
        public string WindUp, Effect, Mark;
        /// <summary>The clip from the shared 32-clip KayKit pack (or the Kaiju's own) the skill plays.</summary>
        public string Clip;
        /// <summary>true: a real authored clip plays. false: only the procedural overlay (SkillPoseOverlay) moves the rig.</summary>
        public bool ClipIsReal;
        /// <summary>The procedural overlay on top of (or instead of) the clip, or null.</summary>
        public string Procedural;
        /// <summary>Which view draws the effect (for the report and the registry test).</summary>
        public string DrawnBy;
        public SkillPress Press;
    }

    /// <summary>
    /// The registry: every class skill id must have an entry here (SkillEffectsTests fails when a sim ability kind or a
    /// SkillId has none). View-only data; the rules never read it.
    /// </summary>
    public static class SkillVisuals
    {
        const string Fx = "SkillFxView", Mech = "MechanicsWorldView", Trail = "FxScene (slash trail, hit sparks)";

        public static readonly SkillVisual[] All =
        {
            new SkillVisual { Id = SkillId.WarriorCleave, Cls = ClassId.Warrior, Name = "CLEAVE", Key = "cleave", Press = SkillPress.Attack,
                WindUp = "sim swing wind-up with the AttackSlice clip", Effect = "120 degree slash trail", Mark = "hit sparks and flinch on each victim",
                Clip = "AttackSlice", ClipIsReal = true, DrawnBy = Trail },
            new SkillVisual { Id = SkillId.WarriorShield, Cls = ClassId.Warrior, Name = "SHIELD UP", Key = "shield", Press = SkillPress.Ability,
                WindUp = "team ring closing on the caster", Effect = "raised shield wall and a ground wedge over the blocked 120 degree front",
                Mark = "a spark on the shield at every blocked hit; the wedge stays while the shield is up",
                Clip = "Block then Blocking", ClipIsReal = true, DrawnBy = Fx + " + FxScene shield arc" },
            new SkillVisual { Id = SkillId.FarmerHook, Cls = ClassId.Farmer, Name = "STAFF HOOK", Key = "hook", Press = SkillPress.Attack,
                WindUp = "sim swing wind-up", Effect = "90 degree slash trail", Mark = "hit sparks", Clip = "Attack2HSlice", ClipIsReal = true, DrawnBy = Trail },
            new SkillVisual { Id = SkillId.FarmerRoots, Cls = ClassId.Farmer, Name = "ROOT SEEDS", Key = "roots", Press = SkillPress.Ability,
                WindUp = "team ring closing on the caster and a throw", Effect = "a seed lobbed in an arc, then roots breaking out of the ground over the area with a team ring",
                Mark = "vines round the feet of each slowed enemy, a slow icon and a ring that counts the slow down, gone when the slow ends",
                Clip = "Throw", ClipIsReal = true, Procedural = "throw swing on the right arm if the model lacks Throw", DrawnBy = Fx },
            new SkillVisual { Id = SkillId.MinerPick, Cls = ClassId.Miner, Name = "PICK-HAMMER", Key = "pick", Press = SkillPress.Attack,
                WindUp = "sim swing wind-up", Effect = "slash trail, wall chips", Mark = "hit sparks, wall crack decals", Clip = "Attack2HChop", ClipIsReal = true, DrawnBy = Trail },
            new SkillVisual { Id = SkillId.MinerBarricade, Cls = ClassId.Miner, Name = "BARRICADE", Key = "barricade", Press = SkillPress.Ability,
                WindUp = "team ring closing on the caster", Effect = "barricade model and dust puff (other agent owns the build look)", Mark = "the barricade itself and its health bar",
                Clip = "UseItem", ClipIsReal = true, DrawnBy = Fx + " (wind-up) + WorldView" },
            new SkillVisual { Id = SkillId.MinerDig, Cls = ClassId.Miner, Name = "DIG", Key = "dig", Press = SkillPress.Terraform,
                WindUp = "the chop", Effect = "dirt burst and the block lifted out of the ground up to the miner's hands", Mark = "the hole and the carried block",
                Clip = "Attack2HChop", ClipIsReal = true, DrawnBy = Fx + " + chunk mesh" },
            new SkillVisual { Id = SkillId.MinerPlace, Cls = ClassId.Miner, Name = "PLACE", Key = "place", Press = SkillPress.TerraformPlace,
                WindUp = "the use pose", Effect = "the block dropped from the miner's hands into the hole and a dust ring", Mark = "the filled cell",
                Clip = "UseItem", ClipIsReal = true, DrawnBy = Fx + " + chunk mesh" },
            new SkillVisual { Id = SkillId.DemoMine, Cls = ClassId.Demolitions, Name = "LAND MINE", Key = "mine", Press = SkillPress.Ability,
                WindUp = "team ring closing on the caster and the crouch", Effect = "dirt mound, armed ring", Mark = "mound until it blows or expires",
                Clip = "PickUp", ClipIsReal = true, DrawnBy = Fx + " (wind-up) + " + Mech },
            new SkillVisual { Id = SkillId.DemoTnt, Cls = ClassId.Demolitions, Name = "TNT", Key = "tnt", Press = SkillPress.Terraform,
                WindUp = "team ring closing on the caster and the throw", Effect = "a three-stick bundle with a team band and a lit fuse on an arc, the blast-radius ring filling while it flies, then the blast",
                Mark = "fuse countdown ring where it lies, crater after",
                Clip = "Throw", ClipIsReal = true, Procedural = "throw swing on the right arm if the model lacks Throw", DrawnBy = Fx + " (bundle, wind-up) + " + Mech },
            new SkillVisual { Id = SkillId.BuilderHammer, Cls = ClassId.Builder, Name = "HAMMER", Key = "hammer", Press = SkillPress.Attack,
                WindUp = "sim swing wind-up", Effect = "slash trail", Mark = "hit sparks", Clip = "Attack", ClipIsReal = true, DrawnBy = Trail },
            new SkillVisual { Id = SkillId.BuilderBuild, Cls = ClassId.Builder, Name = "BUILD", Key = "build", Press = SkillPress.Ability,
                WindUp = "team ring closing on the caster", Effect = "ladder or bridge (the construction agent owns the build look)", Mark = "the piece and its countdown ring",
                Clip = "UseItem", ClipIsReal = true, DrawnBy = Fx + " (wind-up) + " + Mech },
            new SkillVisual { Id = SkillId.BuilderCarry, Cls = ClassId.Builder, Name = "CARRY", Key = "carry", Press = SkillPress.None,
                WindUp = "none (a pick-up)", Effect = "planks over the head", Mark = "countdown ring at the feet", Clip = "Carry", ClipIsReal = true, DrawnBy = Mech },
            new SkillVisual { Id = SkillId.BuilderRepair, Cls = ClassId.Builder, Name = "BRIDGE REPAIR", Key = "repair", Press = SkillPress.None,
                WindUp = "none (a hold)", Effect = "progress bar at the bridge", Mark = "the bar", Clip = "none", ClipIsReal = false, DrawnBy = "WorldReactionsCore" },
            new SkillVisual { Id = SkillId.HealerMend, Cls = ClassId.Healer, Name = "MEND", Key = "mend", Press = SkillPress.Attack,
                WindUp = "team ring closing on the caster", Effect = "heal beam and sparkles", Mark = "a short green glow on the healed ally",
                Clip = "Cast", ClipIsReal = true, DrawnBy = Fx + " + " + Mech },
            new SkillVisual { Id = SkillId.HealerWings, Cls = ClassId.Healer, Name = "HEALING WINGS", Key = "wings", Press = SkillPress.Ability,
                WindUp = "team ring closing on the caster", Effect = "a pulse ring from the healer plus the pulse rings at the aim",
                Mark = "a short green glow on each ally healed", Clip = "UseItem", ClipIsReal = true, DrawnBy = Fx + " + " + Mech },
            new SkillVisual { Id = SkillId.HealerPurify, Cls = ClassId.Healer, Name = "PURIFY", Key = "purify", Press = SkillPress.Terraform,
                WindUp = "team ring closing on the caster and a raised arm", Effect = "a wide pulse from the healer out to the purify range, then the purify column at the egg",
                Mark = "the egg column", Clip = "Cast", ClipIsReal = true, Procedural = "right arm raised overhead on top of Cast", DrawnBy = Fx + " + " + Mech },
            new SkillVisual { Id = SkillId.KaijuClaw, Kaiju = true, Name = "CLAW", Key = "claw", Press = SkillPress.None,
                WindUp = "11-tick wind-up roar ring", Effect = "swipe", Mark = "hit sparks", Clip = "SwipeWindup then Swipe", ClipIsReal = true, DrawnBy = "FxScene" },
            new SkillVisual { Id = SkillId.KaijuSlam, Kaiju = true, Name = "SLAM", Key = "slam", Press = SkillPress.None,
                WindUp = "1.2 s telegraph ring", Effect = "slam shock and shake", Mark = "falloff and safe edge", Clip = "SlamWindup then Slam", ClipIsReal = true, DrawnBy = Mech },
        };

        public static SkillVisual Get(SkillId id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        /// <summary>The skill behind a sim ability kind (Combat.Kind*, AttackStart.B). False for a kind with no visual.</summary>
        public static bool TryForAbilityKind(int kind, out SkillId id)
        {
            switch (kind)
            {
                case Combat.KindShield: id = SkillId.WarriorShield; return true;
                case Combat.KindRoots: id = SkillId.FarmerRoots; return true;
                case Combat.KindBarricade: id = SkillId.MinerBarricade; return true;
                case Combat.KindMine: id = SkillId.DemoMine; return true;
                case Combat.KindBuild: id = SkillId.BuilderBuild; return true;
                case Combat.KindPulse: id = SkillId.HealerWings; return true;
                case Combat.KindTnt: id = SkillId.DemoTnt; return true;
                default: id = SkillId.WarriorCleave; return false;
            }
        }

        public const int Count = (int)SkillId.KaijuSlam + 1;
    }
}
