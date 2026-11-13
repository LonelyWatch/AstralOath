using System;
using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    // A program runs synchronously against a private projection. Only BattleMatch commits its typed changes.
    public sealed class BattleSkillResolver : IBattleActionResolver
    {
        private readonly Dictionary<string, Program> skills;
        private readonly Func<string, string, int> enhancementRank;
        private readonly int randomDualChanceBp;

        public BattleSkillResolver(BattleCatalogData catalog, bool includePreviewSlime = false,
            Func<string, string, int> enhancementRank = null, int randomDualChanceBp = 300)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (catalog.schemaVersion != 2 || catalog.skills == null || catalog.effects == null || catalog.enhancements == null)
                throw new ArgumentException("Battle skill catalog is incomplete.", nameof(catalog));
            if (randomDualChanceBp < 0 || randomDualChanceBp > Basis)
                throw new ArgumentOutOfRangeException(nameof(randomDualChanceBp));
            this.enhancementRank = enhancementRank ?? ((_, _) => 0);
            this.randomDualChanceBp = randomDualChanceBp;
            var effectGroups = catalog.effects.ToLookup(row => row.skillId, StringComparer.Ordinal);
            var rankGroups = catalog.enhancements.ToLookup(row => row.skillId, StringComparer.Ordinal);
            skills = catalog.skills.ToDictionary(row => row.id,
                row => new Program(row, effectGroups[row.id], rankGroups[row.id]), StringComparer.Ordinal);
            if (catalog.effects.Any(row => !skills.ContainsKey(row.skillId)))
                throw new ArgumentException("A skill effect refers to an unknown skill.", nameof(catalog));
            if (includePreviewSlime)
            {
                var slime = CreatePreviewSlimeSkill();
                skills.Add(slime.id, new Program(slime, new[]
                {
                    new BattleCatalogData.EffectRow { order = 1, phase = "impact", operation = "damage", recipient = "targets", status = "none", chanceBp = Basis }
                }, Array.Empty<BattleCatalogData.EnhancementRow>()));
            }
        }

        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            resolution = null;
            var work = new Execution(this, context.Snapshot, random);
            var actor = work.Get(command.ActorId);
            if (actor == null || !skills.TryGetValue(command.SkillId, out var program) ||
                !CanUseSkill(context.Snapshot, actor.Source, program.Row, command.UseSoulBurn))
                return false;

            var selected = work.Get(command.TargetId);
            if (!work.CanSelect(program.Row.target, actor, selected)) return false;
            var modifiers = ModifiersFor(actor, program);
            int cooldown = Math.Max(0, program.Row.cooldownTurns - modifiers.CooldownTurns);
            if (command.UseSoulBurn)
            {
                work.Changes.Add(BattleChange.Soul(actor.Id, actor.Side, -program.Row.soulBurnCost));
                if (program.Row.soulBurnKind == "reduce_cooldown")
                    cooldown = Math.Max(0, cooldown - program.Row.soulBurnTurns);
            }
            if (cooldown > 0)
                work.Changes.Add(BattleChange.Cooldown(actor.Id, actor.Id, program.Row.id, cooldown));
            work.Cast(actor, program, selected, command.UseSoulBurn, BattleActionOrigin.Primary);
            int soulGain = program.Row.soulGain + modifiers.SoulGain;
            if (soulGain > 0)
                work.Changes.Add(BattleChange.Soul(actor.Id, actor.Side, soulGain));
            resolution = new BattleActionResolution(work.Changes, program.Row.presentationId);
            return true;
        }

        internal static BattleCatalogData.SkillRow CreatePreviewSlimeSkill() => new BattleCatalogData.SkillRow
        {
            id = BattlePreviewFactory.SlimeSkillId, heroId = "preview.slime", slot = 1,
            target = "one_enemy", soulBurnKind = "none", attackRateBp = 10000, powerBp = 10000
        };

        internal static bool CanUseSkill(BattleSnapshot snapshot, BattleUnitSnapshot actor,
            BattleCatalogData.SkillRow skill, bool useSoulBurn) =>
            actor.IsAlive && skill.target != "passive" && actor.GetCooldown(skill.id) == 0 &&
            (skill.slot <= 1 || actor.GetStatus(BattleStatusKind.Silence) == null) &&
            (actor.Resource?.Amount ?? 0) >= skill.resourceCost &&
            (!useSoulBurn || skill.soulBurnCost > 0 && snapshot.GetSoul(actor.Slot.Side) >= skill.soulBurnCost);

        internal static bool IsHarmful(BattleStatusKind kind) =>
            kind == BattleStatusKind.Stun || kind == BattleStatusKind.SpeedDown ||
            kind == BattleStatusKind.AttackDown || kind == BattleStatusKind.DefenseDown ||
            kind == BattleStatusKind.Unhealable || kind == BattleStatusKind.Silence;

        private Modifiers ModifiersFor(Unit actor, Program program)
        {
            int rank = enhancementRank(actor.Id, program.Row.id);
            if (rank < 0 || rank >= program.Modifiers.Length)
                throw new ArgumentOutOfRangeException(nameof(rank), "Skill enhancement is outside the loaded program.");
            return program.Modifiers[rank];
        }

        private sealed class Program
        {
            public readonly BattleCatalogData.SkillRow Row;
            public readonly BattleCatalogData.EffectRow[] Effects;
            public readonly Modifiers[] Modifiers;

            public Program(BattleCatalogData.SkillRow row, IEnumerable<BattleCatalogData.EffectRow> effects,
                IEnumerable<BattleCatalogData.EnhancementRow> ranks)
            {
                Row = row.Copy();
                Effects = effects.OrderBy(item => item.order).Select(item => item.Copy()).ToArray();
                if ((row.target == "passive") != (Effects.Length == 0) || Effects.Length > 64)
                    throw new ArgumentException($"Skill '{row.id}' has an invalid number of effects.");
                for (int i = 0; i < Effects.Length; i++)
                {
                    var effect = Effects[i];
                    if (effect.order != i + 1 || effect.chanceBp < 1 || effect.chanceBp > Basis)
                        throw new ArgumentException($"Skill '{row.id}' has an invalid effect sequence.");
                }
                var rankRows = ranks.OrderBy(item => item.step).ToArray();
                Modifiers = new Modifiers[rankRows.Length + 1];
                for (int i = 0; i < rankRows.Length; i++)
                {
                    var step = rankRows[i];
                    if (step.step != i + 1) throw new ArgumentException($"Skill '{row.id}' has a missing enhancement.");
                    var current = Modifiers[i];
                    switch (step.kind)
                    {
                        case "damage_bp": current.DamageBp += step.value; break;
                        case "healing_bp": current.HealingBp += step.value; break;
                        case "effect_chance_bp": current.ChanceBp += step.value; break;
                        case "cooldown_turns": current.CooldownTurns += step.value; break;
                        case "readiness_bp": current.ReadinessBp += step.value; break;
                        case "soul_gain": current.SoulGain += step.value; break;
                        default: throw new ArgumentException($"Unsupported enhancement '{step.kind}'.");
                    }
                    Modifiers[i + 1] = current;
                }
            }
        }

        private struct Modifiers
        {
            public int DamageBp, HealingBp, ChanceBp, CooldownTurns, ReadinessBp, SoulGain;
        }

        private sealed class Unit
        {
            public readonly BattleUnitSnapshot Source;
            public readonly List<BattleStatusSnapshot> Statuses;
            public int Health;
            public int ResourceAmount;

            public Unit(BattleUnitSnapshot source)
            {
                Source = source;
                Health = source.CurrentHealth;
                ResourceAmount = source.Resource?.Amount ?? 0;
                Statuses = source.Statuses.ToList();
            }

            public string Id => Source.InstanceId;
            public BattleSide Side => Source.Slot.Side;
            public bool Alive => Health > 0;
            public int MaximumHealth => Source.Stats.MaxHealth;
            public decimal Attack => ScaleStat(Source.Stats.Attack, BattleStatusKind.AttackUp, BattleStatusKind.AttackDown);
            public decimal Defense => ScaleStat(Source.Stats.Defense, BattleStatusKind.DefenseUp, BattleStatusKind.DefenseDown);
            public decimal Speed => ScaleStat(Source.Stats.Speed, BattleStatusKind.SpeedUp, BattleStatusKind.SpeedDown);
            public BattleStatusSnapshot Status(BattleStatusKind kind) => Statuses.FirstOrDefault(item => item.Kind == kind);
            public bool Has(BattleStatusKind kind) => Status(kind) != null;
            public int Strength(BattleStatusKind kind) => Status(kind)?.Strength ?? 0;
            private decimal ScaleStat(int value, BattleStatusKind up, BattleStatusKind down) =>
                value * Math.Max(0m, 1m + (Strength(up) - (decimal)Strength(down)) / Basis);
        }

        private sealed class Execution
        {
            private readonly BattleSkillResolver owner;
            private readonly Unit[] units;
            private readonly BattleRandom random;
            internal readonly List<BattleChange> Changes = new List<BattleChange>();

            public Execution(BattleSkillResolver owner, BattleSnapshot snapshot, BattleRandom random)
            {
                this.owner = owner;
                this.random = random;
                units = snapshot.Units.Select(unit => new Unit(unit)).ToArray();
            }

            public Unit Get(string id) => units.FirstOrDefault(unit => unit.Id == id);

            public bool CanSelect(string rule, Unit actor, Unit selected)
            {
                bool ally = rule == "one_ally" || rule == "all_allies";
                if (selected != null) return selected.Alive && (selected.Side == actor.Side) == ally;
                return rule == "all_enemies" || rule == "all_allies";
            }

            private Unit[] ChooseTargets(string rule, Unit actor, Unit selected)
            {
                if (rule == "all_enemies") return units.Where(unit => unit.Alive && unit.Side != actor.Side).ToArray();
                if (rule == "all_allies") return Allies(actor);
                if (rule == "one_enemy" || rule == "one_ally") return new[] { selected };
                if (rule != "two_enemies") throw new InvalidOperationException($"Unknown targeting rule '{rule}'.");
                var other = units.Where(unit => unit.Alive && unit.Side != actor.Side && unit != selected).ToArray();
                return other.Length == 0 ? new[] { selected } : new[] { selected, Choose(other) };
            }

            public void Cast(Unit actor, Program program, Unit selected, bool soulBurn, BattleActionOrigin origin)
            {
                var skill = program.Row;
                if (!actor.Alive || actor.ResourceAmount < skill.resourceCost) return;
                bool buffed = HasBuff(actor);
                string targeting = skill.target;
                if ((skill.aoeWhenBuffed && origin != BattleActionOrigin.DualAttack && buffed) ||
                    (skill.aoeOutsideTurn && (origin == BattleActionOrigin.Counter || origin == BattleActionOrigin.DualAttack)))
                    targeting = "all_enemies";
                var targets = ChooseTargets(targeting, actor, selected);
                var highestDefense = skill.highestDefensePenetrationBp.HasValue
                    ? targets.OrderByDescending(target => target.Defense).FirstOrDefault()
                    : null;
                var hit = new Dictionary<string, BattleHitKind>(StringComparer.Ordinal);
                var modifiers = owner.ModifiersFor(actor, program);
                if (origin != BattleActionOrigin.Primary)
                    Changes.Add(BattleChange.Action(actor.Id, selected?.Id, program.Row.id, origin, program.Row.presentationId));
                ChangeResource(actor, actor, -skill.resourceCost);

                foreach (var effect in program.Effects)
                {
                    switch (effect.operation)
                    {
                        case "dual_attack":
                            if (origin == BattleActionOrigin.Primary && selected?.Alive == true)
                                ForceDual(actor, selected);
                            continue;
                        case "repeat_if_status":
                            if (origin == BattleActionOrigin.Primary && actor.Has(ParseStatus(effect.status)) &&
                                units.Any(unit => unit.Alive && unit.Side != actor.Side))
                                Cast(actor, program, selected?.Alive == true ? selected : null, false, BattleActionOrigin.ExtraAttack);
                            continue;
                    }

                    foreach (var target in Recipients(effect.recipient, actor, targets))
                    {
                        if (!target.Alive || (effect.condition == "caster_health_below_half" && (long)actor.Health * 2 >= actor.MaximumHealth)) continue;
                        bool hasHit = hit.TryGetValue(target.Id, out var lastHit);
                        if (effect.phase == "after_hit" && !hasHit) continue;
                        ApplyEffect(actor, target, skill, effect, modifiers, soulBurn, lastHit, hit, target == highestDefense);
                    }
                }

                int resourceGain = origin != BattleActionOrigin.DualAttack && buffed && skill.resourceGainWhenBuffed > 0
                    ? skill.resourceGainWhenBuffed : skill.resourceGain;
                ChangeResource(actor, actor, resourceGain);

                // Counters and random Dual Attacks are limited to the primary attack window.
                if (origin != BattleActionOrigin.Primary || hit.Count == 0) return;
                if (program.Row.slot == 1 && targeting == "one_enemy" && program.Row.heroId != "preview.slime" &&
                    selected?.Alive == true)
                {
                    var eligible = Allies(actor).Where(unit => unit != actor && CanReact(unit)).ToArray();
                    if (eligible.Length > 0 && Roll(random, owner.randomDualChanceBp))
                        CastBasic(Choose(eligible), selected, BattleActionOrigin.DualAttack);
                }
                foreach (var target in targets)
                {
                    if (!actor.Alive) break;
                    if (hit.ContainsKey(target.Id) && CanReact(target) && target.Has(BattleStatusKind.Counter) &&
                        Roll(random, target.Strength(BattleStatusKind.Counter)))
                        CastBasic(target, actor, BattleActionOrigin.Counter);
                }
            }

            private void ApplyEffect(Unit actor, Unit target, BattleCatalogData.SkillRow skill, BattleCatalogData.EffectRow effect,
                Modifiers modifiers, bool soulBurn, BattleHitKind lastHit, Dictionary<string, BattleHitKind> hit,
                bool highestDefense)
            {
                int bonusChance = effect.chanceBonusBp > 0 && target.Has(ParseStatus(effect.chanceBonusStatus))
                    ? effect.chanceBonusBp : 0;
                int chance = Math.Min(Basis, effect.chanceBp + modifiers.ChanceBp + bonusChance);
                switch (effect.operation)
                {
                    case "damage":
                        int relation = ElementRelation(actor.Source.Element, target.Source.Element);
                        var result = RollHit(random, relation, actor.Source.Stats.CritChanceBp,
                            target.Strength(BattleStatusKind.EvasionUp), target.Strength(BattleStatusKind.CriticalResistanceUp),
                            alwaysCritical: skill.alwaysCritical);
                        int rate = (soulBurn ? skill.burnAttackRateBp : null) ?? skill.attackRateBp.Value;
                        int penetration = (skill.defensePenetrationBp ?? 0) +
                            (actor.Has(BattleStatusKind.Stealth) ? skill.stealthPenetrationBp ?? 0 : 0) +
                            (highestDefense ? skill.highestDefensePenetrationBp ?? 0 : 0);
                        decimal defense = target.Defense * (1m - Math.Min(Basis, penetration) / (decimal)Basis);
                        int speedScale = (soulBurn ? skill.burnSpeedDamagePpm : null) ?? skill.casterSpeedDamagePpm ?? 0;
                        decimal damageBonus = actor.Speed * speedScale / 1000000m +
                            (decimal)actor.Health / actor.MaximumHealth * (skill.casterHealthRatioDamageBp ?? 0) / Basis +
                            (HasBuff(target) ? (skill.damageAgainstBuffBp ?? 0) / (decimal)Basis : 0m);
                        int damage = Damage(actor.Attack, defense, actor.MaximumHealth, rate, skill.powerBp.Value,
                            skill.casterHealthDamageBp ?? 0, modifiers.DamageBp, relation, result, actor.Source.Stats.CritDamageBp,
                            additionalBaseDamage: actor.Defense * (skill.casterDefenseDamageBp ?? 0) / Basis,
                            damageMultiplier: 1m + damageBonus);
                        bool firstHit = !hit.ContainsKey(target.Id);
                        hit[target.Id] = result;
                        ProjectDamage(actor, target, damage, result);
                        if (firstHit && target.Alive && actor.Side != target.Side)
                            ChangeResource(actor, target, -(target.Source.ResourceRules?.AttackedSpend ?? 0));
                        break;
                    case "heal":
                        if (!target.Has(BattleStatusKind.Unhealable))
                        {
                            int healing = ScaleHealth(actor.MaximumHealth, skill.casterHealthHealBp.Value, modifiers.HealingBp);
                            target.Health += Math.Min(healing, target.MaximumHealth - target.Health);
                            Changes.Add(BattleChange.Healing(actor.Id, target.Id, healing));
                        }
                        break;
                    case "readiness":
                        Changes.Add(BattleChange.Readiness(actor.Id, target.Id, effect.value + modifiers.ReadinessBp));
                        break;
                    case "reduce_readiness":
                        if (CheckEffect(actor, target, null, chance, lastHit, hostile: true, checksImmunity: true))
                            Changes.Add(BattleChange.Readiness(actor.Id, target.Id, -effect.value));
                        break;
                    case "extend_cooldown":
                        for (int turn = 0; turn < effect.value; turn++)
                        {
                            if (!CheckEffect(actor, target, null, chance, lastHit, hostile: true, checksImmunity: true)) continue;
                            foreach (var id in target.Source.Form.SkillIds.Skip(1))
                                if (owner.skills.TryGetValue(id, out var delayed) && delayed.Row.cooldownTurns > 0)
                                    Changes.Add(BattleChange.Cooldown(actor.Id, target.Id, id, 1));
                        }
                        break;
                    case "cleanse":
                        foreach (var status in target.Statuses.Where(item => IsHarmful(item.Kind)).Take(effect.value).ToArray())
                        {
                            target.Statuses.Remove(status);
                            Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, status.Kind));
                        }
                        break;
                    case "dispel":
                        foreach (var status in target.Statuses.Where(item => !IsHarmful(item.Kind)).Take(effect.value).ToArray())
                            if (CheckEffect(actor, target, status.Kind, chance, lastHit, hostile: true, checksImmunity: false))
                            {
                                target.Statuses.Remove(status);
                                Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, status.Kind));
                            }
                        break;
                    case "apply_status":
                        var kind = ParseStatus(effect.status);
                        bool harmful = IsHarmful(kind);
                        if (!CheckEffect(actor, target, kind, chance, lastHit, harmful, harmful)) break;
                        int duration = effect.turns;
                        if (soulBurn && !harmful && skill.soulBurnKind == "extend_granted_buffs") duration += skill.soulBurnTurns;
                        int strength = kind == BattleStatusKind.Barrier ? BarrierStrength(actor, skill) : effect.value;
                        var previous = target.Status(kind);
                        if (previous != null)
                        {
                            duration = Math.Max(duration, previous.RemainingTurns);
                            strength = Math.Max(strength, previous.Strength);
                        }
                        SetStatus(target, kind, duration, strength);
                        Changes.Add(BattleChange.ApplyStatus(actor.Id, target.Id, kind, duration, strength));
                        break;
                    default: throw new InvalidOperationException($"Unknown skill effect '{effect.operation}'.");
                }
            }

            private static int BarrierStrength(Unit actor, BattleCatalogData.SkillRow skill)
            {
                if (skill.casterDefenseBarrierBp.HasValue) return ScaleHealth(actor.Defense, skill.casterDefenseBarrierBp.Value);
                if (skill.casterAttackBarrierBp.HasValue) return ScaleHealth(actor.Attack, skill.casterAttackBarrierBp.Value);
                return ScaleHealth(actor.MaximumHealth, skill.casterHealthBarrierBp.Value);
            }

            private bool CheckEffect(Unit source, Unit target, BattleStatusKind? kind, int chance, BattleHitKind hit,
                bool hostile, bool checksImmunity)
            {
                BattleEffectRejection? reason = null;
                if (hostile && (hit == BattleHitKind.Miss || (checksImmunity && target.Has(BattleStatusKind.Immunity))))
                    reason = BattleEffectRejection.Blocked;
                else if (!Roll(random, chance))
                    reason = BattleEffectRejection.ChanceFailed;
                else if (hostile && !Roll(random, EffectSuccessBp(source.Source.Stats.EffectivenessBp, target.Source.Stats.EffectResistanceBp)))
                    reason = BattleEffectRejection.Resisted;
                if (!reason.HasValue) return true;
                if (kind.HasValue) Changes.Add(BattleChange.EffectRejected(source.Id, target.Id, kind.Value, reason.Value));
                return false;
            }

            private void ChangeResource(Unit source, Unit target, int delta)
            {
                if (delta == 0 || target.Source.Resource == null) return;
                int next = (int)Math.Clamp((long)target.ResourceAmount + delta, 0L, target.Source.Resource.Maximum);
                if (next == target.ResourceAmount) return;
                Changes.Add(BattleChange.Resource(source.Id, target.Id, next - target.ResourceAmount));
                target.ResourceAmount = next;
            }

            private void ProjectDamage(Unit source, Unit target, int damage, BattleHitKind hit)
            {
                var barrier = target.Status(BattleStatusKind.Barrier);
                int absorb = Math.Min(damage, barrier?.Strength ?? 0);
                if (absorb > 0)
                {
                    if (absorb == barrier.Strength) target.Statuses.Remove(barrier);
                    else SetStatus(target, barrier.Kind, barrier.RemainingTurns, barrier.Strength - absorb);
                }
                target.Health = Math.Max(0, target.Health - (damage - absorb));
                Changes.Add(BattleChange.Damage(source.Id, target.Id, damage, hit));
            }

            private Unit[] Recipients(string selector, Unit actor, Unit[] targets)
            {
                switch (selector)
                {
                    case "self": return new[] { actor };
                    case "allies": return Allies(actor);
                    case "targets": return targets;
                    case "target_and_lowest_ally":
                        var selected = targets[0];
                        var lowest = Allies(actor).OrderBy(unit => (decimal)unit.Health / unit.MaximumHealth).First();
                        return lowest == selected ? new[] { selected } : new[] { selected, lowest };
                    default: throw new InvalidOperationException($"Unknown effect recipient '{selector}'.");
                }
            }

            private void ForceDual(Unit actor, Unit target)
            {
                var ally = Allies(actor).Where(unit => unit != actor && CanReact(unit))
                    .OrderByDescending(unit => unit.Attack).FirstOrDefault();
                if (ally != null) CastBasic(ally, target, BattleActionOrigin.DualAttack);
            }

            private void CastBasic(Unit actor, Unit target, BattleActionOrigin origin)
            {
                var skillId = actor.Source.Form.SkillIds.FirstOrDefault();
                if (skillId != null && owner.skills.TryGetValue(skillId, out var program) &&
                    (program.Row.target == "one_enemy" || program.Row.target == "two_enemies"))
                    Cast(actor, program, target, false, origin);
            }

            private Unit Choose(Unit[] candidates) => candidates.Length == 1 ? candidates[0]
                : candidates[(int)(random.NextUInt64() % (ulong)candidates.Length)];
            private Unit[] Allies(Unit actor) => units.Where(unit => unit.Alive && unit.Side == actor.Side).ToArray();
            private static bool HasBuff(Unit unit) => unit.Statuses.Any(status => !IsHarmful(status.Kind));
            private static bool CanReact(Unit unit) => unit.Alive && !unit.Has(BattleStatusKind.Stun);
            private static BattleStatusKind ParseStatus(string value) => Enum.Parse<BattleStatusKind>(value);
            private static void SetStatus(Unit target, BattleStatusKind kind, int turns, int strength)
            {
                int index = target.Statuses.FindIndex(item => item.Kind == kind);
                var status = new BattleStatusSnapshot(kind, turns, strength);
                if (index < 0) target.Statuses.Add(status);
                else target.Statuses[index] = status;
            }
        }
    }
}
