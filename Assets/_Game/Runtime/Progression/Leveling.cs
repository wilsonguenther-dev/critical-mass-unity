using System;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>Where a skill-point total sits on the level curve.</summary>
    public readonly struct LevelProgress
    {
        public readonly int Level;
        /// <summary>Skill points earned inside the current level.</summary>
        public readonly int Into;
        /// <summary>Skill points the current level needs in all (0 at MaxLevel).</summary>
        public readonly int Needed;

        public LevelProgress(int level, int into, int needed)
        {
            Level = level;
            Into = into;
            Needed = needed;
        }

        /// <summary>0..1 through the level (1 at MaxLevel).</summary>
        public float Fraction => Needed <= 0 ? 1f : Math.Max(0f, Math.Min(1f, Into / (float)Needed));
        public bool IsMax => Needed <= 0;
    }

    /// <summary>
    /// The level curve (DESIGN-progression-pickups section 3): going from level L to L + 1 costs 300 + 60 x (L - 1) skill
    /// points, so level 2 is at 300, level 5 at 1,560, level 10 at 4,860; the cap is level 30 (33,060). Pure integer maths.
    /// </summary>
    public static class Leveling
    {
        public const int MaxLevel = 30;
        public const int BaseCost = 300;
        public const int StepCost = 60;

        /// <summary>Skill points from level to level + 1 (0 at or above MaxLevel).</summary>
        public static int CostToNext(int level)
        {
            if (level < 1) level = 1;
            return level >= MaxLevel ? 0 : BaseCost + StepCost * (level - 1);
        }

        /// <summary>Total skill points needed to reach a level: 300 (n - 1) + 30 (n - 1)(n - 2).</summary>
        public static int TotalFor(int level)
        {
            if (level <= 1) return 0;
            if (level > MaxLevel) level = MaxLevel;
            int n = level - 1;
            return BaseCost * n + StepCost * n * (n - 1) / 2;
        }

        public static int LevelFor(int skillPoints)
        {
            int level = 1;
            while (level < MaxLevel && skillPoints >= TotalFor(level + 1)) level++;
            return level;
        }

        public static LevelProgress Progress(int skillPoints)
        {
            if (skillPoints < 0) skillPoints = 0;
            int level = LevelFor(skillPoints);
            return new LevelProgress(level, skillPoints - TotalFor(level), CostToNext(level));
        }
    }
}
