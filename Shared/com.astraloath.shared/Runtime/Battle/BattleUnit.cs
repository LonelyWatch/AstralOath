using System;

namespace AstralOath.Shared.Battle
{
    public enum MonsterRank { Normal, Elite, Boss }

    // One instance per combatant, regardless of team, definition or presentation.
    public sealed class BattleUnit
    {
        public string InstanceId { get; }
        public BattleUnitDefinition Definition { get; }
        public BattleSlot Slot { get; }
        public BattleUnitForm Form { get; }
        public BattleStats Stats { get; }
        public int Level { get; }
        public int? Stars { get; }
        public int? Awakenings { get; }
        public MonsterRank? MonsterRank { get; }
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        private BattleUnit(string instanceId, BattleUnitDefinition definition, BattleSlot slot, BattleStats stats,
            int level, int? stars, int? awakenings, MonsterRank? monsterRank)
        {
            InstanceId = BattleUnitDefinition.RequireKey(instanceId, nameof(instanceId));
            _ = slot.GetIndex();
            Definition = definition;
            Slot = slot;
            Stats = stats;
            Form = definition.InitialForm;
            Level = level;
            Stars = stars;
            Awakenings = awakenings;
            MonsterRank = monsterRank;
            CurrentHealth = stats.MaxHealth;
        }

        public static BattleUnit CreateHero(string instanceId, HeroDefinition definition, BattleSlot slot,
            int level, int stars, int awakenings)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            var stats = HeroStatCalculator.Calculate(definition.GrowthPreset, level, stars, awakenings, definition.AwakeningSteps);
            return new BattleUnit(instanceId, definition, slot, stats, level, stars, awakenings, null);
        }

        public static BattleUnit CreateMonster(string instanceId, MonsterDefinition definition, BattleSlot slot,
            int level, MonsterRank rank, BattleStats encounterStats)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (encounterStats == null)
                throw new ArgumentNullException(nameof(encounterStats));
            if (level <= 0)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (!Enum.IsDefined(typeof(MonsterRank), rank))
                throw new ArgumentOutOfRangeException(nameof(rank));
            return new BattleUnit(instanceId, definition, slot, encounterStats, level, null, null, rank);
        }

        // Apply an already resolved health change. Damage, shields and revive rules live above this type.
        public int ApplyDamage(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            int applied = Math.Min(CurrentHealth, amount);
            CurrentHealth -= applied;
            return applied;
        }

        public int ApplyHealing(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (!IsAlive)
                return 0;
            int applied = Math.Min(Stats.MaxHealth - CurrentHealth, amount);
            CurrentHealth += applied;
            return applied;
        }
    }
}
