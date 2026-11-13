using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    // The host serializes commands for each match. Consumers observe immutable snapshots.
    public sealed class BattleMatch
    {
        private const int Ready = 10000;
        private readonly string id;
        private readonly uint version;
        private readonly ulong seed;
        private readonly IBattleActionResolver resolver;
        private readonly BattleFlowRules rules;
        private readonly State[] units;
        private readonly Dictionary<string, State> byId;
        private readonly Dictionary<string, (BattleSkillCommand Command, BattleCommandResult Result)> accepted =
            new Dictionary<string, (BattleSkillCommand, BattleCommandResult)>(StringComparer.Ordinal);
        private readonly int[] soul;
        private ulong eventSequence;
        private ulong commandSequence;
        private int turn;
        private State actor;
        private BattleOutcome outcome;
        private State extraTurn;

        public BattleMatch(string battleId, uint rulesVersion, ulong randomSeed, BattleFormation formation,
            IBattleActionResolver resolver, BattleFlowRules rules = null)
        {
            id = BattleUnitDefinition.RequireKey(battleId, nameof(battleId));
            if (rulesVersion == 0) throw new ArgumentOutOfRangeException(nameof(rulesVersion));
            if (formation == null) throw new ArgumentNullException(nameof(formation));
            version = rulesVersion;
            seed = randomSeed;
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.rules = rules ?? new BattleFlowRules();
            soul = new[] { this.rules.InitialAllySoul, this.rules.InitialEnemySoul };
            var random = new BattleRandom(seed);
            units = formation.Units.Select(unit => new State(unit,
                this.rules.OpeningReadinessMaxBp == 0 ? 0 : (int)(random.NextUInt64() % (ulong)(this.rules.OpeningReadinessMaxBp + 1)))).ToArray();
            byId = units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
            var opening = new List<BattleFlowEvent>();
            Emit(opening, BattleFlowEventKind.BattleStarted);
            foreach (var unit in units)
                ChangeResource(unit, unit.Id, unit.Resource?.OpeningGain ?? 0, opening);
            if (resolver is IBattleLifecycleResolver lifecycle) ApplyLifecycle(lifecycle.BattleStart(Capture()), opening);
            Advance(opening);
            OpeningEvents = opening.AsReadOnly();
            Snapshot = Capture();
        }

        public BattleSnapshot Snapshot { get; private set; }
        public int MaximumSoul => rules.MaximumSoul;
        public IReadOnlyList<BattleFlowEvent> OpeningEvents { get; }

        public BattleCommandResult Submit(BattleSkillCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.BattleId != id) return Reject(BattleCommandStatus.WrongBattle);
            if (command.RulesVersion != version) return Reject(BattleCommandStatus.WrongRulesVersion);
            if (string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId.Length > 80)
                return Reject(BattleCommandStatus.InvalidAction);
            if (accepted.TryGetValue(command.CommandId, out var previous))
                return previous.Command.SameInput(command)
                    ? new BattleCommandResult(BattleCommandStatus.Duplicate, previous.Result.Snapshot, previous.Result.Events)
                    : Reject(BattleCommandStatus.CommandIdConflict);
            if (outcome != BattleOutcome.InProgress) return Reject(BattleCommandStatus.BattleEnded);
            if (command.Sequence != commandSequence + 1) return Reject(BattleCommandStatus.SequenceMismatch);
            if (command.ActorId != actor.Id) return Reject(BattleCommandStatus.WrongActor);
            if (!actor.Unit.Form.SkillIds.Contains(command.SkillId)) return Reject(BattleCommandStatus.UnknownSkill);
            if (actor.Cooldowns.ContainsKey(command.SkillId)) return Reject(BattleCommandStatus.SkillOnCooldown);
            if (!string.IsNullOrEmpty(command.TargetId) && (!byId.TryGetValue(command.TargetId, out var selected) || selected.Health == 0))
                return Reject(BattleCommandStatus.UnknownTarget);
            var random = new BattleRandom(unchecked(seed ^ command.Sequence * 0x9E3779B97F4A7C15UL));
            if (!resolver.TryResolve(new BattleActionContext(Snapshot), command, random, out var resolution))
                return Reject(BattleCommandStatus.InvalidAction);
            if (resolution == null || resolution.Changes.Count > 512 || resolution.Changes.Any(change => !IsValid(change) ||
                    (change.Kind == BattleFlowEventKind.ExtraTurnGranted && change.SourceId != actor.Id)))
                return Reject(BattleCommandStatus.InvalidResolution);

            var events = new List<BattleFlowEvent>();
            var previousStatuses = actor.Statuses.ToArray();
            actor.Readiness = 0;
            commandSequence = command.Sequence;
            Emit(events, BattleFlowEventKind.Action, actor.Id, command.TargetId, skillId: command.SkillId,
                presentationId: resolution.PresentationId, useSoulBurn: command.UseSoulBurn);
            var refreshed = new HashSet<BattleStatusKind>();
            foreach (var change in resolution.Changes)
            {
                Apply(change, events);
                if (change.Kind == BattleFlowEventKind.StatusApplied && change.TargetId == actor.Id &&
                    !BattleStatusSnapshot.IsStackable(change.Status.Value))
                    refreshed.Add(change.Status.Value);
            }
            FinishTurn(actor, previousStatuses, events, refreshed);
            Advance(events);
            Snapshot = Capture();
            var result = new BattleCommandResult(BattleCommandStatus.Accepted, Snapshot, events.AsReadOnly());
            accepted.Add(command.CommandId, (command, result));
            return result;
        }

        private BattleCommandResult Reject(BattleCommandStatus status) =>
            new BattleCommandResult(status, Snapshot, Array.Empty<BattleFlowEvent>());

        private bool IsValid(BattleChange change)
        {
            if (change == null || change.SourceId == null || !byId.TryGetValue(change.SourceId, out var source))
                return false;
            if (change.Kind == BattleFlowEventKind.SoulChanged)
                return (change.Side == BattleSide.Ally || change.Side == BattleSide.Enemy) && source.Unit.Slot.Side == change.Side;
            if (change.Kind == BattleFlowEventKind.Action)
                return source.Unit.Form.SkillIds.Contains(change.SkillId) &&
                    (change.TargetId == null || byId.ContainsKey(change.TargetId));
            if (change.TargetId == null || !byId.TryGetValue(change.TargetId, out var target))
                return false;
            switch (change.Kind)
            {
                case BattleFlowEventKind.Damage:
                case BattleFlowEventKind.Healing: return change.Amount >= 0;
                case BattleFlowEventKind.CooldownChanged: return target.Unit.Form.SkillIds.Contains(change.SkillId);
                case BattleFlowEventKind.ResourceChanged: return target.Resource != null;
                case BattleFlowEventKind.StatusApplied: return change.Status.HasValue && change.Turns > 0 && change.Turns <= 60 && change.Amount >= 0;
                case BattleFlowEventKind.StatusRemoved: return change.Status.HasValue;
                default: return true;
            }
        }

        private void Apply(BattleChange change, List<BattleFlowEvent> events)
        {
            string source = change.SourceId;
            string target = change.TargetId;
            if (change.Kind == BattleFlowEventKind.SoulChanged)
            {
                int side = (int)change.Side;
                soul[side] = (int)Math.Clamp((long)soul[side] + change.Amount, 0L, rules.MaximumSoul);
                Emit(events, change.Kind, source, amount: soul[side], side: change.Side);
                return;
            }
            if (change.Kind == BattleFlowEventKind.Action)
            {
                Emit(events, change.Kind, source, target, skillId: change.SkillId,
                    presentationId: change.PresentationId, origin: change.Origin);
                return;
            }

            var unit = byId[target];
            if (unit.Health == 0) return;
            if (change.Kind == BattleFlowEventKind.ExtraTurnGranted)
            {
                if (extraTurn == null)
                {
                    extraTurn = unit;
                    Emit(events, change.Kind, source, target);
                }
                return;
            }
            switch (change.Kind)
            {
                case BattleFlowEventKind.Damage:
                    ApplyDamage(unit, source, change.Amount, change.HitKind, events);
                    break;
                case BattleFlowEventKind.ResourceChanged:
                    ChangeResource(unit, source, change.Amount, events);
                    break;
                case BattleFlowEventKind.Healing:
                    int healing = Math.Min(change.Amount, unit.Unit.Stats.MaxHealth - unit.Health);
                    unit.Health += healing;
                    Emit(events, change.Kind, source, target, healing, remainingHealth: unit.Health);
                    break;
                case BattleFlowEventKind.ReadinessChanged:
                    unit.Readiness = (int)Math.Clamp((long)unit.Readiness + change.Amount, 0L, 1000000L);
                    Emit(events, change.Kind, source, target, unit.Readiness);
                    break;
                case BattleFlowEventKind.CooldownChanged:
                    int cooldown = unit.Cooldowns.TryGetValue(change.SkillId, out int value) ? value : 0;
                    ChangeCooldown(unit, source, change.SkillId,
                        (int)Math.Clamp((long)cooldown + change.Amount, 0L, 60L), events);
                    break;
                case BattleFlowEventKind.StatusApplied:
                    var kind = change.Status.Value;
                    bool stackable = BattleStatusSnapshot.IsStackable(kind);
                    ulong instanceId = change.StatusInstanceId;
                    if (stackable && instanceId == 0) instanceId = eventSequence + 1;
                    var status = new BattleStatusSnapshot(kind, change.Turns, change.Amount, source, instanceId);
                    int index = stackable ? -1 : unit.Statuses.FindIndex(item => item.Kind == kind);
                    if (index < 0) unit.Statuses.Add(status);
                    else unit.Statuses[index] = status;
                    Emit(events, change.Kind, source, target, statusKind: change.Status, status: status);
                    break;
                case BattleFlowEventKind.StatusRemoved:
                    RemoveStatus(unit, source, change.Status.Value, events, change.StatusInstanceId);
                    break;
                case BattleFlowEventKind.EffectRejected:
                    Emit(events, change.Kind, source, target, statusKind: change.Status, rejection: change.Rejection);
                    break;
            }
        }

        private void ApplyDamage(State unit, string source, int amount, BattleHitKind hit, List<BattleFlowEvent> events,
            BattleStatusSnapshot periodic = null)
        {
            int damage = unit.Statuses.Any(item => item.Kind == BattleStatusKind.Invincible)
                ? 0 : Math.Min(unit.Health, Absorb(unit, amount, source, events));
            unit.Health -= damage;
            Emit(events, BattleFlowEventKind.Damage, source, unit.Id, damage, statusKind: periodic?.Kind,
                status: periodic, hitKind: hit, remainingHealth: unit.Health);
            if (unit.Health == 0)
                Emit(events, BattleFlowEventKind.UnitDefeated, source, unit.Id, statusKind: periodic?.Kind, status: periodic);
        }

        private void ApplyStartOfTurnDamage(State unit, List<BattleFlowEvent> events)
        {
            foreach (var status in unit.Statuses.Where(item => item.Kind == BattleStatusKind.Poison).ToArray())
            {
                int damage = EpicSevenCombatMath.ScaleHealth(unit.Unit.Stats.MaxHealth, status.Strength);
                ApplyDamage(unit, status.SourceId, damage, BattleHitKind.None, events, status);
                if (unit.Health == 0) return;
            }
        }

        private int Absorb(State unit, int amount, string source, List<BattleFlowEvent> events)
        {
            int index = unit.Statuses.FindIndex(item => item.Kind == BattleStatusKind.Barrier);
            if (index < 0 || amount == 0) return amount;
            var barrier = unit.Statuses[index];
            int absorbed = Math.Min(amount, barrier.Strength);
            Emit(events, BattleFlowEventKind.BarrierAbsorbed, source, unit.Id, absorbed);
            if (barrier.Strength == absorbed)
                RemoveStatus(unit, source, BattleStatusKind.Barrier, events);
            else
                unit.Statuses[index] = new BattleStatusSnapshot(barrier.Kind, barrier.RemainingTurns, barrier.Strength - absorbed, barrier.SourceId);
            return amount - absorbed;
        }

        private void RemoveStatus(State unit, string source, BattleStatusKind kind, List<BattleFlowEvent> events,
            ulong instanceId = 0)
        {
            for (int index = 0; index < unit.Statuses.Count;)
            {
                var status = unit.Statuses[index];
                if (status.Kind != kind || (instanceId != 0 && status.InstanceId != instanceId))
                {
                    index++;
                    continue;
                }
                unit.Statuses.RemoveAt(index);
                Emit(events, BattleFlowEventKind.StatusRemoved, source, unit.Id, statusKind: kind, status: status);
            }
        }

        private void ChangeResource(State unit, string source, int delta, List<BattleFlowEvent> events)
        {
            if (delta == 0 || unit.Resource == null) return;
            int next = (int)Math.Clamp((long)unit.ResourceAmount + delta, 0L, unit.Resource.Maximum);
            if (next == unit.ResourceAmount) return;
            unit.ResourceAmount = next;
            Emit(events, BattleFlowEventKind.ResourceChanged, source, unit.Id, next);
        }

        private void ChangeCooldown(State unit, string source, string skill, int value, List<BattleFlowEvent> events)
        {
            if (value == 0) unit.Cooldowns.Remove(skill);
            else unit.Cooldowns[skill] = value;
            Emit(events, BattleFlowEventKind.CooldownChanged, source, unit.Id, value, skillId: skill);
        }

        private void FinishTurn(State unit, BattleStatusSnapshot[] previous, List<BattleFlowEvent> events,
            HashSet<BattleStatusKind> refreshed = null)
        {
            foreach (var skill in unit.Unit.Form.SkillIds)
                if (unit.Cooldowns.TryGetValue(skill, out int cooldown))
                    ChangeCooldown(unit, unit.Id, skill, cooldown - 1, events);
            foreach (var status in previous)
            {
                if (refreshed?.Contains(status.Kind) == true) continue;
                int index = unit.Statuses.FindIndex(item => item.Kind == status.Kind && item.InstanceId == status.InstanceId);
                if (index < 0) continue;
                var remaining = unit.Statuses[index];
                if (remaining.RemainingTurns == 1) RemoveStatus(unit, unit.Id, remaining.Kind, events, remaining.InstanceId);
                else
                {
                    var updated = new BattleStatusSnapshot(remaining.Kind, remaining.RemainingTurns - 1,
                        remaining.Strength, remaining.SourceId, remaining.InstanceId);
                    unit.Statuses[index] = updated;
                    Emit(events, BattleFlowEventKind.StatusDurationChanged, unit.Id, unit.Id,
                        statusKind: updated.Kind, status: updated);
                }
            }
            if (unit.Health > 0 && resolver is IBattleLifecycleResolver lifecycle)
                ApplyLifecycle(lifecycle.TurnEnd(Capture(), unit.Id), events);
            Emit(events, BattleFlowEventKind.TurnEnded, unit.Id);
        }

        private void ApplyLifecycle(IReadOnlyList<BattleChange> changes, List<BattleFlowEvent> events)
        {
            if (changes.Count > 512 || changes.Any(change => !IsValid(change) ||
                change.Kind != BattleFlowEventKind.StatusApplied && change.Kind != BattleFlowEventKind.StatusRemoved))
                throw new InvalidOperationException("Battle lifecycle program returned invalid changes.");
            foreach (var change in changes) Apply(change, events);
        }

        private void Advance(List<BattleFlowEvent> events)
        {
            while (true)
            {
                bool allyAlive = units.Any(unit => unit.Health > 0 && unit.Unit.Slot.Side == BattleSide.Ally);
                bool enemyAlive = units.Any(unit => unit.Health > 0 && unit.Unit.Slot.Side == BattleSide.Enemy);
                if (!allyAlive || !enemyAlive || turn >= rules.MaxTurns)
                {
                    End(events, allyAlive == enemyAlive ? BattleOutcome.Draw :
                        allyAlive ? BattleOutcome.AllyVictory : BattleOutcome.EnemyVictory);
                    return;
                }
                var living = units.Where(unit => unit.Health > 0).ToArray();
                var granted = extraTurn;
                extraTurn = null;
                if (granted?.Health <= 0) granted = null;
                if (granted == null && living.All(unit => unit.Readiness < Ready))
                {
                    long ticks = long.MaxValue;
                    foreach (var unit in living)
                    {
                        long speed = unit.Speed;
                        if (speed > 0) ticks = Math.Min(ticks, (Ready - unit.Readiness + speed - 1) / speed);
                    }
                    if (ticks == long.MaxValue)
                    {
                        End(events, BattleOutcome.Draw);
                        return;
                    }
                    foreach (var unit in living)
                        unit.Readiness = (int)Math.Min(1000000L, unit.Readiness + ticks * unit.Speed);
                }
                actor = granted ?? living.OrderByDescending(unit => unit.Readiness).First();
                turn++;
                Emit(events, BattleFlowEventKind.TurnStarted, actor.Id);
                ApplyStartOfTurnDamage(actor, events);
                if (actor.Health == 0) continue;
                ChangeResource(actor, actor.Id, actor.Resource?.TurnGain ?? 0, events);
                if (!actor.Statuses.Any(status => status.Kind == BattleStatusKind.Stun)) return;
                actor.Readiness = 0;
                Emit(events, BattleFlowEventKind.TurnSkipped, actor.Id, statusKind: BattleStatusKind.Stun);
                FinishTurn(actor, actor.Statuses.ToArray(), events);
            }
        }

        private void End(List<BattleFlowEvent> events, BattleOutcome result)
        {
            outcome = result;
            actor = null;
            Emit(events, BattleFlowEventKind.BattleEnded, result: result);
        }

        private BattleSnapshot Capture() => new BattleSnapshot(id, version, commandSequence, eventSequence, turn,
            actor?.Id, outcome, soul[0], soul[1], units.Select(unit => unit.Snapshot()).ToArray());

        private void Emit(List<BattleFlowEvent> events, BattleFlowEventKind kind, string source = null,
            string target = null, int amount = 0, string skillId = null, string presentationId = null,
            BattleActionOrigin origin = BattleActionOrigin.Primary, BattleStatusKind? statusKind = null,
            BattleStatusSnapshot status = null, BattleSide? side = null, BattleOutcome? result = null,
            BattleHitKind hitKind = BattleHitKind.None, BattleEffectRejection? rejection = null,
            bool useSoulBurn = false, int? remainingHealth = null) =>
            events.Add(new BattleFlowEvent(++eventSequence, turn, kind, source, target, amount, skillId, presentationId,
                origin, statusKind, status, side, result, hitKind, rejection, useSoulBurn, remainingHealth));

        private sealed class State
        {
            public State(BattleUnit unit, int readiness)
            {
                Unit = unit; Health = unit.CurrentHealth; Readiness = readiness;
            }
            public BattleUnit Unit { get; }
            public string Id => Unit.InstanceId;
            public int Health { get; set; }
            public BattleResourceDefinition Resource => (Unit.Definition as HeroDefinition)?.Resource;
            public int ResourceAmount { get; set; }
            public int Readiness { get; set; }
            public Dictionary<string, int> Cooldowns { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
            public List<BattleStatusSnapshot> Statuses { get; } = new List<BattleStatusSnapshot>();
            public long Speed => Unit.Stats.Speed * Math.Max(0L, 10000L +
                Math.Max(Statuses.FirstOrDefault(item => item.Kind == BattleStatusKind.SpeedUp)?.Strength ?? 0,
                    Statuses.FirstOrDefault(item => item.Kind == BattleStatusKind.GreaterSpeedUp)?.Strength ?? 0) -
                (Statuses.FirstOrDefault(item => item.Kind == BattleStatusKind.SpeedDown)?.Strength ?? 0)) / 10000L;

            public BattleUnitSnapshot Snapshot() => new BattleUnitSnapshot(Unit, Health, Readiness,
                new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(Cooldowns, StringComparer.Ordinal)),
                Array.AsReadOnly(Statuses.ToArray()), ResourceAmount);
        }
    }
}
