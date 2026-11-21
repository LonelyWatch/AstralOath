using System;
using System.Collections.Generic;

namespace AstralOath.Shared.Battle
{
    public static class EquipmentScoreCalculator
    {
        // One score point = 100,000 units. A weight applies directly to the stored integer
        // (flat points or basis points). Keep every fraction until the final displayed score.
        private const int UnitsPerPoint = 100000;

        public static int Calculate(EquipmentPiece piece)
        {
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            return Calculate(piece.MainStat, piece.Substats);
        }

        public static int Calculate(EquipmentStat mainStat, IReadOnlyList<EquipmentStat> substats)
        {
            if (mainStat == null) throw new ArgumentNullException(nameof(mainStat));
            if (substats == null) throw new ArgumentNullException(nameof(substats));
            if (substats.Count > 4) throw new ArgumentException("An item has at most four substats.", nameof(substats));
            long total = WeightedValue(mainStat, true);
            for (int i = 0; i < substats.Count; i++)
            {
                if (substats[i] == null) throw new ArgumentException("Missing substat.", nameof(substats));
                total += WeightedValue(substats[i], false);
            }
            return checked((int)(total / UnitsPerPoint));
        }

        private static long WeightedValue(EquipmentStat stat, bool main)
        {
            int weight;
            switch (stat.Kind)
            {
                case "attack": weight = main ? 4980 : 9720; break;
                case "health": weight = main ? 930 : 2000; break;
                case "defense": weight = main ? 8440 : 13500; break;
                case "speed": weight = main ? 58800 : 200000; break;
                case "critChanceBp": weight = main ? 435 : 1600; break;
                case "critDamageBp": weight = main ? 375 : 1140; break;
                case "attackPct":
                case "healthPct":
                case "defensePct":
                case "effectivenessBp":
                case "resistanceBp": weight = main ? 400 : 1000; break;
                default: throw new ArgumentException("Unknown equipment score stat.", nameof(stat));
            }
            return (long)stat.Value * weight;
        }
    }
}
