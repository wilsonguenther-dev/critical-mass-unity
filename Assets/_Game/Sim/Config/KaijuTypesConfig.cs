namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item K (five Kaiju types): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class KaijuTypesConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        public KaijuTypesConfig Clone() => (KaijuTypesConfig)MemberwiseClone();
    }
}
