using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    public sealed class EquipmentStat
    {
        public EquipmentStat(string kind, int value)
        {
            switch (kind)
            {
                case "attack": case "health": case "defense": case "speed":
                case "attackPct": case "healthPct": case "defensePct":
                case "critChanceBp": case "critDamageBp": case "effectivenessBp": case "resistanceBp":
                    break;
                default: throw new ArgumentException("Unknown equipment stat.", nameof(kind));
            }
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            Kind = kind;
            Value = value;
        }

        public string Kind { get; }
        public int Value { get; }
    }

    public sealed class EquipmentPiece
    {
        public EquipmentPiece(string setId, EquipmentStat mainStat, IReadOnlyList<EquipmentStat> substats)
        {
            if (setId != "speed" && setId != "critical") throw new ArgumentException("Unknown equipment set.", nameof(setId));
            if (substats == null || substats.Count > 4 || substats.Any(stat => stat == null))
                throw new ArgumentException("At most four substats can be equipped on a piece.", nameof(substats));
            SetId = setId;
            MainStat = mainStat ?? throw new ArgumentNullException(nameof(mainStat));
            Substats = Array.AsReadOnly(substats.ToArray());
        }

        public string SetId { get; }
        public EquipmentStat MainStat { get; }
        public IReadOnlyList<EquipmentStat> Substats { get; }
    }

    public static class EquipmentStatCalculator
    {
        // Included in owned-trial content identity. Advance it when arithmetic or set rules change.
        public const string RulesVersion = "equipment.stats.v1";

        public static IReadOnlyDictionary<string, int> ActiveSets(IReadOnlyList<EquipmentPiece> equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            if (equipment.Count > 6) throw new ArgumentException("A hero can equip six pieces.", nameof(equipment));
            int speed = 0, critical = 0;
            foreach (var piece in equipment)
            {
                if (piece == null) throw new ArgumentException("Missing equipment piece.", nameof(equipment));
                if (piece.SetId == "speed") speed++;
                else critical++;
            }
            var sets = new Dictionary<string, int>(StringComparer.Ordinal);
            if (speed >= 4) sets.Add("speed", speed / 4);
            if (critical >= 2) sets.Add("critical", critical / 2);
            return sets;
        }

        public static BattleStats Calculate(BattleStats baseStats, IReadOnlyList<EquipmentPiece> equipment)
        {
            if (baseStats == null) throw new ArgumentNullException(nameof(baseStats));
            var sets = ActiveSets(equipment);
            if (equipment.Count == 0) return baseStats;
            sets.TryGetValue("speed", out int speedSets);
            sets.TryGetValue("critical", out int criticalSets);

            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var piece in equipment)
            {
                Add(piece.MainStat);
                foreach (var substat in piece.Substats) Add(substat);
            }
            return new BattleStats(
                Scaled(baseStats.Attack, Total("attackPct"), Total("attack")),
                Scaled(baseStats.MaxHealth, Total("healthPct"), Total("health")),
                Scaled(baseStats.Defense, Total("defensePct"), Total("defense")),
                Scaled(baseStats.Speed, speedSets * 2500, Total("speed")),
                checked((int)(baseStats.CritChanceBp + Total("critChanceBp") + criticalSets * 1200)),
                checked((int)(baseStats.CritDamageBp + Total("critDamageBp"))),
                checked((int)(baseStats.EffectivenessBp + Total("effectivenessBp"))),
                checked((int)(baseStats.EffectResistanceBp + Total("resistanceBp"))));

            long Total(string kind) => totals.TryGetValue(kind, out long value) ? value : 0;
            void Add(EquipmentStat stat) => totals[stat.Kind] = Total(stat.Kind) + stat.Value;
        }

        private static int Scaled(int basis, long percentBp, long flat) =>
            checked((int)(basis + (long)basis * percentBp / 10000 + flat));
    }
}
