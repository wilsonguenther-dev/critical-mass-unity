using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>One number per team (browser Record&lt;TeamId, number&gt;).</summary>
    public sealed class PerTeam
    {
        public double Azure;
        public double Crimson;

        public double this[TeamId t]
        {
            get
            {
                switch (t)
                {
                    case TeamId.Azure: return Azure;
                    case TeamId.Crimson: return Crimson;
                    default: throw new ArgumentOutOfRangeException(nameof(t));
                }
            }
            set
            {
                switch (t)
                {
                    case TeamId.Azure: Azure = value; break;
                    case TeamId.Crimson: Crimson = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(t));
                }
            }
        }

        public void Set(double azure, double crimson)
        {
            Azure = azure;
            Crimson = crimson;
        }
    }
}
