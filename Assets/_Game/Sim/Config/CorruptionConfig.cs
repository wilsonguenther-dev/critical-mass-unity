namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item C (corruption crystal and the wild Kaiju): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class CorruptionConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = false;   // b13 integrator: item C is OUT this batch (stub only); off so the meter and roll banner never show

        public CorruptionConfig Clone() => (CorruptionConfig)MemberwiseClone();
    }
}
