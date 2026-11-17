namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item H (Miner and the Blacksmith hut): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class HutConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        public HutConfig Clone() => (HutConfig)MemberwiseClone();
    }
}
