using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    public enum BattleElement { Fire, Ice, Wind, Light, Dark }

    public sealed class BattleUnitForm
    {
        public string Id { get; }
        public string PresentationId { get; }
        public IReadOnlyList<string> SkillIds { get; }

        public BattleUnitForm(string id, string presentationId, IEnumerable<string> skillIds)
        {
            Id = BattleUnitDefinition.RequireKey(id, nameof(id));
            PresentationId = BattleUnitDefinition.RequireKey(presentationId, nameof(presentationId));
            if (skillIds == null)
                throw new ArgumentNullException(nameof(skillIds));

            string[] ids = skillIds.ToArray();
            if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new ArgumentException("Skill IDs in a form must be nonempty and unique.", nameof(skillIds));
            SkillIds = Array.AsReadOnly(ids);
        }
    }

    public abstract class BattleUnitDefinition
    {
        public string Id { get; }
        public string NameKey { get; }
        public BattleElement Element { get; }
        public BattleUnitForm InitialForm { get; }

        protected BattleUnitDefinition(string id, string nameKey, BattleElement element, BattleUnitForm initialForm)
        {
            Id = RequireKey(id, nameof(id));
            NameKey = RequireKey(nameKey, nameof(nameKey));
            if (!Enum.IsDefined(typeof(BattleElement), element))
                throw new ArgumentOutOfRangeException(nameof(element));
            Element = element;
            InitialForm = initialForm ?? throw new ArgumentNullException(nameof(initialForm));
        }

        internal static string RequireKey(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("An ID or key is required.", parameter) : value;
    }

    public enum BattleResourceKind { Focus, FightingSpirit }

    public sealed class BattleResourceDefinition
    {
        public BattleResourceKind Kind { get; }
        public int Maximum { get; }
        internal int TurnGain { get; }
        internal int AttackedSpend { get; }

        public BattleResourceDefinition(BattleResourceKind kind, int maximum, int turnGain = 0, int attackedSpend = 0)
        {
            if (!Enum.IsDefined(typeof(BattleResourceKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (turnGain < 0 || turnGain > maximum) throw new ArgumentOutOfRangeException(nameof(turnGain));
            if (attackedSpend < 0 || attackedSpend > maximum) throw new ArgumentOutOfRangeException(nameof(attackedSpend));
            Kind = kind;
            Maximum = maximum;
            TurnGain = turnGain;
            AttackedSpend = attackedSpend;
        }
    }

    public sealed class HeroDefinition : BattleUnitDefinition
    {
        public string HeroClass { get; }
        public string Zodiac { get; }
        public HeroGrowthPreset GrowthPreset { get; }
        public IReadOnlyList<HeroAwakeningStep> AwakeningSteps { get; }
        public BattleResourceDefinition Resource { get; }

        public HeroDefinition(string id, string nameKey, BattleElement element, string heroClass, string zodiac,
            BattleUnitForm initialForm, HeroGrowthPreset growthPreset, IEnumerable<HeroAwakeningStep> awakeningSteps,
            BattleResourceDefinition resource = null)
            : base(id, nameKey, element, initialForm)
        {
            HeroClass = RequireKey(heroClass, nameof(heroClass));
            Zodiac = RequireKey(zodiac, nameof(zodiac));
            GrowthPreset = growthPreset ?? throw new ArgumentNullException(nameof(growthPreset));
            if (awakeningSteps == null)
                throw new ArgumentNullException(nameof(awakeningSteps));
            AwakeningSteps = Array.AsReadOnly(awakeningSteps.ToArray());
            Resource = resource;
        }
    }

    public sealed class MonsterDefinition : BattleUnitDefinition
    {
        public MonsterDefinition(string id, string nameKey, BattleElement element, BattleUnitForm initialForm)
            : base(id, nameKey, element, initialForm) { }
    }
}
