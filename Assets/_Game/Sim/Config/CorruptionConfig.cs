namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item C (corruption crystal and the wild Kaiju): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class CorruptionConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        public CorruptionConfig Clone() => (CorruptionConfig)MemberwiseClone();
    }
}
