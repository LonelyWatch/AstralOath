using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    [Serializable]
    public sealed class ArtifactCatalogData
    {
        public Row[] templates;
        public int[] experienceByLevel, materialExperience, charmExperience;
        public int goldPerExperience;

        [Serializable]
        public sealed class Row
        {
            public string id, name, heroClass, icon, description, effect;
            public bool canSummon, canUseBottle;
            public int stars, baseAttack, baseHealth, attackMilli, healthMilli;
            public int effectBase, effectMax, secondaryBase, secondaryMax, sellGold, sellPowder;
        }
    }

    public sealed class ArtifactCatalog
    {
        private readonly Dictionary<string, ArtifactTemplate> templates;
        private readonly int[] experienceByLevel, materialExperience, charmExperience;
        private readonly int goldPerExperience;

        public ArtifactCatalog(ArtifactCatalogData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var levels = data.experienceByLevel;
            if (levels == null || levels.Length != 31 || levels[0] != 0 || data.goldPerExperience < 1 ||
                data.templates == null || data.templates.Length == 0)
                throw new ArgumentException("Invalid artifact catalog.", nameof(data));
            for (int i = 1; i < levels.Length; i++)
                if (levels[i] <= levels[i - 1])
                    throw new ArgumentException("Artifact experience thresholds must increase.", nameof(data));

            experienceByLevel = (int[])levels.Clone();
            goldPerExperience = data.goldPerExperience;
            materialExperience = CopyExperience(data.materialExperience);
            charmExperience = CopyExperience(data.charmExperience);
            templates = data.templates.Select(row => new ArtifactTemplate(row))
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
        }

        public IReadOnlyCollection<ArtifactTemplate> Templates => templates.Values;
        public bool TryGet(string id, out ArtifactTemplate template) => templates.TryGetValue(id, out template);
        public ArtifactTemplate Get(string id) => templates[id];

        public int ExperienceForLevel(int level)
        {
            if (level < 0 || level > 30) throw new ArgumentOutOfRangeException(nameof(level));
            return experienceByLevel[level];
        }

        public int LevelForExperience(int experience, int limitBreaks)
        {
            if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
            if (limitBreaks < 0 || limitBreaks > 5) throw new ArgumentOutOfRangeException(nameof(limitBreaks));
            int floor = Array.BinarySearch(experienceByLevel, experience);
            return Math.Min(15 + limitBreaks * 3, floor >= 0 ? floor : ~floor - 1);
        }

        public int CharmExperience(string kind) => kind switch
        {
            "lesserCharm" => charmExperience[0],
            "greaterCharm" => charmExperience[1],
            "epicCharm" => charmExperience[2],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public int MaterialExperience(int stars, int experience)
        {
            if (stars < 3 || stars > 5 || experience < 0) throw new ArgumentOutOfRangeException(nameof(stars));
            return checked(materialExperience[stars - 3] + experience);
        }

        private static int[] CopyExperience(int[] source)
        {
            if (source == null) return new int[3];
            if (source.Length != 3 || source.Any(value => value <= 0))
                throw new ArgumentException("Invalid artifact material thresholds.");
            return (int[])source.Clone();
        }

        public int EnhancementCost(int experienceGranted)
        {
            if (experienceGranted < 0) throw new ArgumentOutOfRangeException(nameof(experienceGranted));
            return checked(experienceGranted * goldPerExperience);
        }
    }

    public sealed class ArtifactTemplate
    {
        private readonly int effectBase, effectMax, secondaryBase, secondaryMax, attackMilli, healthMilli;

        internal ArtifactTemplate(ArtifactCatalogData.Row row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.id) || string.IsNullOrWhiteSpace(row.name) ||
                string.IsNullOrWhiteSpace(row.icon) || string.IsNullOrWhiteSpace(row.description) ||
                row.stars < 3 || row.stars > 5 || row.sellGold < 0 || row.sellPowder < 0 ||
                row.baseAttack < 0 || row.baseHealth < 0 || row.attackMilli < 0 || row.healthMilli < 0 ||
                row.canUseBottle && row.stars != 5)
                throw new ArgumentException("Invalid artifact template.", nameof(row));

            Id = row.id;
            Name = row.name;
            HeroClass = row.heroClass ?? "";
            Icon = row.icon;
            Description = row.description;
            Effect = row.effect ?? "";
            Stars = row.stars;
            CanSummon = row.canSummon;
            CanUseBottle = row.canUseBottle;
            SellGold = row.sellGold;
            SellPowder = row.sellPowder;
            effectBase = row.effectBase;
            effectMax = row.effectMax;
            secondaryBase = row.secondaryBase;
            secondaryMax = row.secondaryMax;
            attackMilli = row.attackMilli > 0 ? row.attackMilli : checked(row.baseAttack * 1000);
            healthMilli = row.healthMilli > 0 ? row.healthMilli : checked(row.baseHealth * 1000);
        }

        public string Id { get; }
        public string Name { get; }
        public string HeroClass { get; }
        public string Icon { get; }
        public string Description { get; }
        public string Effect { get; }
        public int Stars { get; }
        public bool CanSummon { get; }
        public bool CanUseBottle { get; }
        public int SellGold { get; }
        public int SellPowder { get; }

        public int AttackAt(int level) => StatAt(attackMilli, level);
        public int HealthAt(int level) => StatAt(healthMilli, level);
        public int EffectAt(int level)
        {
            ValidateLevel(level);
            return effectBase + (effectMax - effectBase) * (level / 3) / 10;
        }

        public int SecondaryEffectAt(int level)
        {
            ValidateLevel(level);
            return secondaryBase + (secondaryMax - secondaryBase) * (level / 3) / 10;
        }

        public string DescriptionAt(int level) => FormatDescription(level, Description);

        public string FormatDescription(int level, string format)
        {
            string primary = Effect == "opening_soul" ? EffectAt(level).ToString(System.Globalization.CultureInfo.InvariantCulture) : Percent(EffectAt(level));
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, primary, Percent(SecondaryEffectAt(level)));
        }

        private static string Percent(int value) => (value / 100m).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        private static int StatAt(int baseMilli, int level)
        {
            ValidateLevel(level);
            return checked((int)((long)baseMilli * (5 + 2 * level) / 5000));
        }

        private static void ValidateLevel(int level)
        {
            if (level < 0 || level > 30) throw new ArgumentOutOfRangeException(nameof(level));
        }
    }
}
