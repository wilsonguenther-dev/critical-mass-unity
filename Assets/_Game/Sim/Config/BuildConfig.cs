namespace CriticalMass.Sim.Config
{
    /// <summary>CM b13 item B (Builder walls and blocks): every number of the rule lives here; items deep-clone any array they add in Clone.</summary>
    public sealed class BuildConfig
    {
        /// <summary>The rule's own switch (Full profile only: systems read it through DepthRules.On).</summary>
        public bool On = false;   // b13 integrator: item B is CUT this batch (stub only); off so no build kit, ghost hint or wall words show

        public BuildConfig Clone() => (BuildConfig)MemberwiseClone();
    }
}
