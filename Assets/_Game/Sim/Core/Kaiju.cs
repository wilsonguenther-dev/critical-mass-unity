namespace CriticalMass.Sim.Core
{
    /// <summary>The Kaiju (browser Kaiju). The static functions of kaiju.ts live in other files of this partial class.</summary>
    public sealed partial class Kaiju
    {
        public KaijuState State;
        public double X;
        public double Z;
        public double PrevX;
        public double PrevZ;
        public double Facing;
        public double Hp;
        public double MaxHp;
        /// <summary>> 0 while knocked out.</summary>
        public double KoIn;
        public double MeleeCd;
        public double SlamCd;
        /// <summary>> 0 while the slam winds up.</summary>
        public double TelegraphIn;
        /// <summary>CM-26: sim ticks left of the melee wind-up; 0 = no swing in flight.</summary>
        public int MeleeWindupIn;
        /// <summary>CM-26: the actor the wind-up is aimed at (-1 when none).</summary>
        public int MeleeTargetId;
        public double TelegraphX;
        public double TelegraphZ;
        public int TargetId;
        /// <summary>Per actor, seconds of remembered threat (browser Float32Array: stores go through JsMath.F32).</summary>
        public double[] Threat;
    }
}
