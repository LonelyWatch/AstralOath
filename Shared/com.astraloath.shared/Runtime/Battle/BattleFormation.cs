using System;
using System.Collections.Generic;

namespace AstralOath.Shared.Battle
{
    public enum BattleSide { Ally, Enemy }

    public readonly struct BattleSlot : IEquatable<BattleSlot>
    {
        public const int PositionsPerSide = 4;

        public BattleSide Side { get; }
        public int Position { get; }

        public BattleSlot(BattleSide side, int position)
        {
            if (side != BattleSide.Ally && side != BattleSide.Enemy)
                throw new ArgumentOutOfRangeException(nameof(side));
            if (position < 1 || position > PositionsPerSide)
                throw new ArgumentOutOfRangeException(nameof(position));
            Side = side;
            Position = position;
        }

        // default(BattleSlot) bypasses the constructor; reject it at the boundary.
        internal int GetIndex()
        {
            if (Position == 0)
                throw new ArgumentException("A battle slot must be initialized.", nameof(BattleSlot));
            return (Side == BattleSide.Ally ? 0 : PositionsPerSide) + Position - 1;
        }

        public bool Equals(BattleSlot other) => Side == other.Side && Position == other.Position;
        public override bool Equals(object obj) => obj is BattleSlot other && Equals(other);
        public override int GetHashCode() => ((int)Side * PositionsPerSide) + Position;
    }

    public sealed class BattleFormation
    {
        private readonly BattleUnit[] slots = new BattleUnit[2 * BattleSlot.PositionsPerSide];
        public IReadOnlyList<BattleUnit> Units { get; }

        public BattleFormation(IEnumerable<BattleUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            var instances = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unit in units)
            {
                if (unit == null)
                    throw new ArgumentException("A formation cannot contain a null unit.", nameof(units));
                int index = unit.Slot.GetIndex();
                if (slots[index] != null)
                    throw new ArgumentException($"Battle slot {unit.Slot.Side}/{unit.Slot.Position} is occupied.", nameof(units));
                if (!instances.Add(unit.InstanceId))
                    throw new ArgumentException($"Duplicate battle unit instance: {unit.InstanceId}.", nameof(units));
                slots[index] = unit;
            }

            var ordered = new List<BattleUnit>(instances.Count);
            foreach (var unit in slots)
                if (unit != null)
                    ordered.Add(unit);
            Units = ordered.AsReadOnly();
        }

        public BattleUnit GetUnitOrDefault(BattleSlot slot) => slots[slot.GetIndex()];
    }
}
