namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item J (jump and dodge): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class DodgeConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = false;   // b13 integrator: item J-dodge is CUT this batch (stub only); off so no HUD, hint or wording shows it

        public DodgeConfig Clone() => (DodgeConfig)MemberwiseClone();
    }
}
