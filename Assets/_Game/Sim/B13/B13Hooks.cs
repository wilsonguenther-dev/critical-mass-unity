// CM b13 S0 (integrator only; frozen after S0: items change their stub files, never this one).
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM b13 S0: the one place shared sim files call into the batch 13 items. Every line in a shared file that calls this
    /// class ends with <c>// b13-hook</c>. With every item stub an identity (false, 1.0, its input), every call here is an
    /// identity too, so the simulation is bit-identical to the base (1.0 x d == d exactly in IEEE doubles).
    /// Routing by class: the Miner's F and ability go to Blacksmith (H), the Builder's F and switch to BuilderWalls (B).
    /// </summary>
    public static class B13Hooks
    {
        /// <summary>Damage.ApplyHit: true = target ignores this hit (J's dodge invulnerability).</summary>
        public static bool Immune(World w, int target, DamageCause cause) => Dodge.Immune(w, target, cause);

        /// <summary>
        /// Damage.ApplyHit and Kaiju.DamageKaiju (target -1 = the Kaiju): the source actor's team StatScale (H's hut level);
        /// 1 for the Kaiju and the world as the source.
        /// </summary>
        public static double DamageScale(World w, int source, int target, DamageCause cause)
        {
            // b15 rule B: x the hut side-grades (SHIELD WALL, BLAST GUARD, KAIJU HUNTERS; exactly 1 with the rule off)
            double choice = HutChoices.DamageScale(w, source, target, cause);
            if (!(source >= 0 && source < w.ActorCount)) return choice;
            return Blacksmith.StatScale(w, w.Actors[source].Team) * choice;
        }

        /// <summary>Heal.HealSystem (the fountain: the healed actor) and Healer.Restore: the healer's team StatScale; 1 for none.</summary>
        public static double HealScale(World w, int healer)
        {
            if (!(healer >= 0 && healer < w.ActorCount)) return 1.0;
            return Blacksmith.StatScale(w, w.Actors[healer].Team);
        }

        /// <summary>Movement: the walk speed multiplier beside Pickups.SpeedScale (H's HAUL x C's crystal carrier).</summary>
        public static double SpeedScale(World w, int id) => Blacksmith.SpeedScale(w, id) * Corruption.SpeedScale(w, id) * HutChoices.SpeedScale(w, id);   // b15: SWIFT FEET (1 with the rule off)

        /// <summary>Movement, for a living grounded actor before its walk update: true = J's dodge moved it this tick.</summary>
        public static bool DodgeStep(World w, int id, double dt) => Dodge.Step(w, id, dt);

        /// <summary>
        /// Terraform.TerraformSystem, on the RISING edge of InputFrame.Terraform of a living actor of any class (after the
        /// system's own edge bookkeeping): true = taken (the Miner's MINE by H, the Builder's BUILD by B), Terraform skips it.
        /// </summary>
        public static bool TakeTerraformPress(World w, int id)
        {
            ClassId c = w.Actors[id].Cls;
            if (c == ClassId.Miner) return Blacksmith.TakeMinerPress(w, id);
            if (c == ClassId.Builder) return BuilderWalls.TakeBuilderPress(w, id);
            return false;
        }

        /// <summary>Terraform.TerraformSystem, on an InputFrame.TerraformSwitch press of a living actor: true = taken (Builder by B).</summary>
        public static bool TakeTerraformSwitch(World w, int id)
        {
            if (w.Actors[id].Cls == ClassId.Builder) return BuilderWalls.TakeBuilderSwitch(w, id);
            return false;
        }

        /// <summary>Abilities.AbilitiesSystem, for a press that would cast today: true = taken (the Miner's HAUL by H).</summary>
        public static bool TakeAbilityPress(World w, int id)
        {
            if (w.Actors[id].Cls == ClassId.Miner) return Blacksmith.TakeMinerAbility(w, id);
            return false;
        }

        /// <summary>BotSteer, AiState.Task within 1.2 u of (TaskX, TaskZ): routes by TaskKind (1-9 H, 20-29 C); true = frame written.</summary>
        public static bool BotTaskAct(World w, BotsState st, int id, InputFrame o)
        {
            int kind = st.TaskKind[id];
            if (kind >= 1 && kind <= 9) return BotMine.Act(w, st, id, o);
            if (kind >= 20 && kind <= 29) return BotCorrupt.Act(w, st, id, o);
            return false;
        }
    }
}
