using System;
using System.Reflection;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// Which mechanics the sim exposes right now, probed once when a trial begins (reflection on the sim's types, never per
    /// frame). The guided match keeps the steps for mechanics that are still being ported and switches them off when their
    /// state is missing. An item that lands a mechanic can also force the answer (JumpOverride, TerraformOverride) without
    /// touching this file's probe.
    /// </summary>
    public static class TrialFeatures
    {
        /// <summary>CM-32's actor fields, as named by the placeholder comment in Sim/Core/Actor.cs ("AirLen, AirTick, ...").</summary>
        public static readonly string[] JumpActorFields = { "AirLen", "AirTick" };
        /// <summary>CM-30's world state, as named by the placeholder comment in Sim/Core/World.cs ("Terraform: Carried, ...").</summary>
        public const string TerraformWorldField = "Terraform";
        /// <summary>CM-30's event, as named by the placeholder comment in Sim/Core/SimEnums.cs.</summary>
        public const string TerraformEvent = "Terraform";
        /// <summary>The bot-passivity setting the trial asks for (not in BotsConfig on main; see TrialMatch).</summary>
        public const string PassiveBotsField = "PassiveSeconds";

        /// <summary>null = probe the sim; true or false = the mechanic's own item says so.</summary>
        public static bool? JumpOverride;
        public static bool? TerraformOverride;

        /// <summary>
        /// True while the full map is open; registered by the HUD's interact layer when it builds (null in headless runs,
        /// which switches the map step off). 2026-11-12.
        /// </summary>
        public static Func<bool> MapOpenProbe;

        public static bool HasMap() => MapOpenProbe != null;

        public static bool HasJump()
        {
            if (JumpOverride.HasValue) return JumpOverride.Value;
            Type t = typeof(Actor);
            for (int i = 0; i < JumpActorFields.Length; i++)
                if (t.GetField(JumpActorFields[i], BindingFlags.Public | BindingFlags.Instance) == null) return false;
            return true;
        }

        public static bool HasTerraform()
        {
            if (TerraformOverride.HasValue) return TerraformOverride.Value;
            if (typeof(World).GetField(TerraformWorldField, BindingFlags.Public | BindingFlags.Instance) == null) return false;
            return Enum.IsDefined(typeof(SimEventType), TerraformEvent);
        }

        /// <summary>True when the mechanic a step needs is exposed.</summary>
        public static bool Has(TrialFeature f)
        {
            switch (f)
            {
                case TrialFeature.Jump: return HasJump();
                case TrialFeature.Terraform: return HasTerraform();
                case TrialFeature.Map: return HasMap();
                default: return true;
            }
        }

        /// <summary>The reason a feature is off, for the log line.</summary>
        public static string MissingReason(TrialFeature f)
        {
            switch (f)
            {
                case TrialFeature.Jump:
                    return "the sim does not expose jumping yet (no Actor." + string.Join("/Actor.", JumpActorFields) +
                        "; it arrives with branch feat/dg-02-jump-bridges)";
                case TrialFeature.Terraform:
                    return "the sim does not expose dig and place yet (no World." + TerraformWorldField + " or SimEventType." +
                        TerraformEvent + "; DG-09 is not built)";
                case TrialFeature.Map:
                    return "no full map in this run (the HUD's interact layer is not installed)";
                default: return "";
            }
        }

        /// <summary>BotsConfig.PassiveSeconds when a rules-lane item adds it, else null.</summary>
        public static FieldInfo PassiveBotsSetting()
        {
            FieldInfo f = typeof(BotsConfig).GetField(PassiveBotsField, BindingFlags.Public | BindingFlags.Instance);
            return f != null && f.FieldType == typeof(double) ? f : null;
        }
    }
}
