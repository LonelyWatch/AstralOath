using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    public enum BattleOutcome { InProgress, AllyVictory, EnemyVictory, Draw }
    public enum BattleActionOrigin { Primary, DualAttack, Counter, ExtraAttack, Passive }
    public enum BattleHitKind { None, Miss, Normal, Crushing, Critical }
    public enum BattleEffectRejection { Blocked, Resisted, ChanceFailed, Missed, HealingPrevented }
    public enum BattleStatusKind
    {
        AttackUp, DefenseUp, Immunity, Barrier, Stun, Counter,
        SpeedUp, SpeedDown, AttackDown, DefenseDown, Unhealable, Silence, EvasionUp, CriticalResistanceUp, Stealth, Provoke, Indomitable
    }
    public enum BattleFlowEventKind
    {
        BattleStarted, TurnStarted, Action, Damage, Healing, ReadinessChanged,
        StatusApplied, StatusRemoved, CooldownChanged, SoulChanged, BarrierAbsorbed,
        UnitDefeated, TurnSkipped, TurnEnded, BattleEnded, EffectRejected, ResourceChanged, StatusDurationChanged, ExtraTurnGranted
    }
    public enum BattleCommandStatus
    {
        Accepted, Duplicate, WrongBattle, WrongRulesVersion, SequenceMismatch, WrongActor,
        UnknownSkill, UnknownTarget, BattleEnded, CommandIdConflict, SkillOnCooldown, InvalidAction, InvalidResolution
    }

    public sealed class BattleSkillCommand : IBattleCommand
    {
        public BattleSkillCommand(string battleId, uint rulesVersion, ulong sequence, string commandId,
            string actorId, string skillId, string targetId, bool useSoulBurn = false)
        {
            BattleId = battleId;
            RulesVersion = rulesVersion;
            Sequence = sequence;
            CommandId = commandId;
            ActorId = actorId;
            SkillId = skillId;
            TargetId = targetId;
            UseSoulBurn = useSoulBurn;
        }

        public string BattleId { get; }
        public uint RulesVersion { get; }
        public ulong Sequence { get; }
        public string CommandId { get; }
        public string ActorId { get; }
        public string SkillId { get; }
        public string TargetId { get; }
        public bool UseSoulBurn { get; }

        internal bool SameInput(BattleSkillCommand other) =>
            BattleId == other.BattleId && RulesVersion == other.RulesVersion && Sequence == other.Sequence &&
            CommandId == other.CommandId && ActorId == other.ActorId && SkillId == other.SkillId &&
            TargetId == other.TargetId && UseSoulBurn == other.UseSoulBurn;
    }

    public sealed class BattleFlowRules
    {
        public const int StandardMaximumSoul = 80;
        public const int SoulPerSlot = 10;

        public BattleFlowRules(int openingReadinessMaxBp = 0, int maxTurns = 200,
            int initialAllySoul = 0, int initialEnemySoul = 0, int maximumSoul = StandardMaximumSoul)
        {
            if (openingReadinessMaxBp < 0 || openingReadinessMaxBp >= 10000)
                throw new ArgumentOutOfRangeException(nameof(openingReadinessMaxBp));
            if (maxTurns < 1 || maxTurns > 10000) throw new ArgumentOutOfRangeException(nameof(maxTurns));
            if (maximumSoul < 0 || initialAllySoul < 0 || initialEnemySoul < 0 ||
                initialAllySoul > maximumSoul || initialEnemySoul > maximumSoul)
                throw new ArgumentOutOfRangeException(nameof(maximumSoul));
            OpeningReadinessMaxBp = openingReadinessMaxBp;
            MaxTurns = maxTurns;
            InitialAllySoul = initialAllySoul;
            InitialEnemySoul = initialEnemySoul;
            MaximumSoul = maximumSoul;
        }

        public int OpeningReadinessMaxBp { get; }
        public int MaxTurns { get; }
        public int InitialAllySoul { get; }
        public int InitialEnemySoul { get; }
        public int MaximumSoul { get; }
    }

    public sealed class BattleStatusSnapshot
    {
        internal BattleStatusSnapshot(BattleStatusKind kind, int remainingTurns, int strength)
            : this(kind, remainingTurns, strength, null) { }

        internal BattleStatusSnapshot(BattleStatusKind kind, int remainingTurns, int strength, string sourceId)
        {
            SourceId = sourceId;
            Kind = kind;
            RemainingTurns = remainingTurns;
            Strength = strength;
        }
        public BattleStatusKind Kind { get; }
        public int RemainingTurns { get; }
        public int Strength { get; }
        public string SourceId { get; }
    }

    public sealed class BattleResourceSnapshot
    {
        internal BattleResourceSnapshot(BattleResourceDefinition definition, int amount)
        {
            Kind = definition.Kind;
            Maximum = definition.Maximum;
            Amount = amount;
        }
        public BattleResourceKind Kind { get; }
        public int Maximum { get; }
        public int Amount { get; }
    }

    public sealed class BattleUnitSnapshot
    {
        internal BattleUnitSnapshot(BattleUnit unit, int health, int readinessBp,
            IReadOnlyDictionary<string, int> cooldowns, IReadOnlyList<BattleStatusSnapshot> statuses,
            int resourceAmount)
        {
            InstanceId = unit.InstanceId;
            DefinitionId = unit.Definition.Id;
            NameKey = unit.Definition.NameKey;
            Level = unit.Level;
            Awakenings = unit.Awakenings ?? 0;
            Element = unit.Definition.Element;
            Slot = unit.Slot;
            Form = unit.Form;
            Stats = unit.Stats;
            CurrentHealth = health;
            ReadinessBp = readinessBp;
            Cooldowns = cooldowns;
            Statuses = statuses;
            ResourceRules = (unit.Definition as HeroDefinition)?.Resource;
            Resource = ResourceRules == null ? null : new BattleResourceSnapshot(ResourceRules, resourceAmount);
        }
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string NameKey { get; }
        public int Level { get; }
        public BattleElement Element { get; }
        public BattleSlot Slot { get; }
        public BattleUnitForm Form { get; }
        public BattleStats Stats { get; }
        public int CurrentHealth { get; }
        public bool IsAlive => CurrentHealth > 0;
        public int ReadinessBp { get; }
        public BattleResourceSnapshot Resource { get; }
        internal BattleResourceDefinition ResourceRules { get; }
        internal int Awakenings { get; }
        public int Barrier => GetStatus(BattleStatusKind.Barrier)?.Strength ?? 0;
        public IReadOnlyDictionary<string, int> Cooldowns { get; }
        public IReadOnlyList<BattleStatusSnapshot> Statuses { get; }
        public int GetCooldown(string skillId) => Cooldowns.TryGetValue(skillId, out int turns) ? turns : 0;
        public BattleStatusSnapshot GetStatus(BattleStatusKind kind) => Statuses.FirstOrDefault(status => status.Kind == kind);
    }

    public sealed class BattleSnapshot : IBattleSnapshot, IBattleResult
    {
        internal BattleSnapshot(string battleId, uint rulesVersion, ulong commandSequence, ulong eventSequence,
            int turn, string currentActorId, BattleOutcome outcome, int allySoul, int enemySoul,
            BattleUnitSnapshot[] units)
        {
            BattleId = battleId;
            RulesVersion = rulesVersion;
            LastCommandSequence = commandSequence;
            LastEventSequence = eventSequence;
            TurnNumber = turn;
            CurrentActorId = currentActorId;
            Outcome = outcome;
            AllySoul = allySoul;
            EnemySoul = enemySoul;
            Units = Array.AsReadOnly(units);
        }
        public string BattleId { get; }
        public uint RulesVersion { get; }
        public ulong LastCommandSequence { get; }
        public ulong LastEventSequence { get; }
        public int TurnNumber { get; }
        public string CurrentActorId { get; }
        public BattleOutcome Outcome { get; }
        public int AllySoul { get; }
        public int EnemySoul { get; }
        public IReadOnlyList<BattleUnitSnapshot> Units { get; }
        public BattleUnitSnapshot GetUnit(string instanceId) => Units.FirstOrDefault(unit => unit.InstanceId == instanceId);
        public int GetSoul(BattleSide side) => side == BattleSide.Ally ? AllySoul : EnemySoul;
    }

    public sealed class BattleFlowEvent : IBattleEvent
    {
        internal BattleFlowEvent(ulong sequence, int turn, BattleFlowEventKind kind,
            string sourceId = null, string targetId = null, int amount = 0, string skillId = null,
            string presentationId = null, BattleActionOrigin origin = BattleActionOrigin.Primary,
            BattleStatusKind? statusKind = null, BattleStatusSnapshot status = null, BattleSide? side = null,
            BattleOutcome? outcome = null, BattleHitKind hitKind = BattleHitKind.None,
            BattleEffectRejection? effectRejection = null, bool useSoulBurn = false, int? remainingHealth = null)
        {
            Sequence = sequence; TurnNumber = turn; Kind = kind; SourceId = sourceId; TargetId = targetId;
            Amount = amount; SkillId = skillId; PresentationId = presentationId; Origin = origin;
            StatusKind = statusKind; Status = status; Side = side; Outcome = outcome; HitKind = hitKind;
            EffectRejection = effectRejection; UseSoulBurn = useSoulBurn; RemainingHealth = remainingHealth;
        }
        public ulong Sequence { get; }
        public int TurnNumber { get; }
        public BattleFlowEventKind Kind { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public int Amount { get; }
        public string SkillId { get; }
        public string PresentationId { get; }
        public BattleActionOrigin Origin { get; }
        public BattleStatusKind? StatusKind { get; }
        public BattleStatusSnapshot Status { get; }
        public BattleSide? Side { get; }
        public BattleOutcome? Outcome { get; }
        public BattleHitKind HitKind { get; }
        public BattleEffectRejection? EffectRejection { get; }
        public bool UseSoulBurn { get; }
        public int? RemainingHealth { get; }
    }

    public sealed class BattleCommandResult
    {
        internal BattleCommandResult(BattleCommandStatus status, BattleSnapshot snapshot, IReadOnlyList<BattleFlowEvent> events)
        {
            Status = status;
            Snapshot = snapshot;
            Events = events;
        }
        public BattleCommandStatus Status { get; }
        public BattleSnapshot Snapshot { get; }
        public IReadOnlyList<BattleFlowEvent> Events { get; }
    }

    public interface IBattleActionResolver
    {
        bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution);
    }

    internal interface IBattleLifecycleResolver
    {
        IReadOnlyList<BattleChange> BattleStart(BattleSnapshot snapshot);
        IReadOnlyList<BattleChange> TurnEnd(BattleSnapshot snapshot, string actorId);
    }

    public sealed class BattleActionContext
    {
        internal BattleActionContext(BattleSnapshot snapshot) => Snapshot = snapshot;
        public BattleSnapshot Snapshot { get; }
        public BattleUnitSnapshot Actor => Snapshot.GetUnit(Snapshot.CurrentActorId);
    }

    public sealed class BattleActionResolution
    {
        public BattleActionResolution(IEnumerable<BattleChange> changes, string presentationId = null)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            Changes = Array.AsReadOnly(changes.ToArray());
            PresentationId = presentationId;
        }
        public IReadOnlyList<BattleChange> Changes { get; }
        public string PresentationId { get; }
    }

    public sealed class BattleChange
    {
        private BattleChange(BattleFlowEventKind kind, string source, string target, int amount = 0,
            string skillId = null, string presentationId = null, BattleActionOrigin origin = BattleActionOrigin.Primary,
            BattleStatusKind? status = null, int turns = 0, BattleSide side = default,
            BattleHitKind hitKind = default, BattleEffectRejection rejection = default)
        {
            Kind = kind; SourceId = source; TargetId = target; Amount = amount; SkillId = skillId;
            PresentationId = presentationId; Origin = origin; Status = status; Turns = turns;
            Side = side; HitKind = hitKind; Rejection = rejection;
        }
        internal BattleFlowEventKind Kind { get; }
        internal string SourceId { get; }
        internal string TargetId { get; }
        internal int Amount { get; }
        internal string SkillId { get; }
        internal string PresentationId { get; }
        internal BattleActionOrigin Origin { get; }
        internal BattleStatusKind? Status { get; }
        internal int Turns { get; }
        internal BattleSide Side { get; }
        internal BattleHitKind HitKind { get; }
        internal BattleEffectRejection Rejection { get; }

        public static BattleChange Damage(string source, string target, int amount, BattleHitKind hitKind = BattleHitKind.Normal) =>
            new BattleChange(BattleFlowEventKind.Damage, source, target, amount, hitKind: hitKind);
        public static BattleChange Healing(string source, string target, int amount) =>
            new BattleChange(BattleFlowEventKind.Healing, source, target, amount);
        public static BattleChange Readiness(string source, string target, int deltaBp) =>
            new BattleChange(BattleFlowEventKind.ReadinessChanged, source, target, deltaBp);
        public static BattleChange ApplyStatus(string source, string target, BattleStatusKind status, int turns, int strength = 0) =>
            new BattleChange(BattleFlowEventKind.StatusApplied, source, target, strength, status: status, turns: turns);
        public static BattleChange RemoveStatus(string source, string target, BattleStatusKind status) =>
            new BattleChange(BattleFlowEventKind.StatusRemoved, source, target, status: status);
        public static BattleChange EffectRejected(string source, string target, BattleStatusKind? status, BattleEffectRejection reason) =>
            new BattleChange(BattleFlowEventKind.EffectRejected, source, target, status: status, rejection: reason);
        public static BattleChange Cooldown(string source, string target, string skillId, int delta) =>
            new BattleChange(BattleFlowEventKind.CooldownChanged, source, target, delta, skillId);
        public static BattleChange Soul(string source, BattleSide side, int delta) =>
            new BattleChange(BattleFlowEventKind.SoulChanged, source, null, delta, side: side);
        internal static BattleChange Resource(string source, string target, int delta) =>
            new BattleChange(BattleFlowEventKind.ResourceChanged, source, target, delta);
        public static BattleChange ExtraTurn(string actor) =>
            new BattleChange(BattleFlowEventKind.ExtraTurnGranted, actor, actor);
        public static BattleChange Action(string source, string target, string skillId, BattleActionOrigin origin, string presentationId = null) =>
            new BattleChange(BattleFlowEventKind.Action, source, target, skillId: skillId, origin: origin, presentationId: presentationId);
    }
}
