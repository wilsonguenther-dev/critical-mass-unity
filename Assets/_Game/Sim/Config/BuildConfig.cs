namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item B (Builder walls and blocks): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class BuildConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = true;

        public BuildConfig Clone() => (BuildConfig)MemberwiseClone();
    }
}
