using AstralOath.Config;
using AstralOath.Shared.Battle;
using Luban;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Provide the Luban binary and JSON output directories.");
var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(Path.Combine(args[0], file + ".bytes"))));
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
T[] Read<T>(string file) =>
    JsonSerializer.Deserialize<T[]>(File.ReadAllBytes(Path.Combine(args[1], file + ".json")), json)
    ?? throw new InvalidDataException($"Missing JSON data for {file}.");

var ui = Read<UiData>("demo_ui");
var locale = Read<LocaleData>("demo_localizationkeys");
var assets = Read<AssetData>("demo_assets");
if (!ui.SequenceEqual(tables.Ui.DataList.Select(row =>
        new UiData(row.Id, row.TitleKey, row.MessageKey, row.DisplaySeconds, row.AssetAddress))) ||
    !locale.SequenceEqual(tables.LocalizationKeys.DataList.Select(row => new LocaleData(row.Key, row.ZhCn))) ||
    !assets.SequenceEqual(tables.Assets.DataList.Select(row => new AssetData(row.Address, row.SourcePath))))
    throw new InvalidDataException("The generated C# accessor read binary data that differs from Luban JSON.");

if (tables.Ui.GetOrDefault(1) == null || tables.Ui.DataList.Any(row =>
        row.Id is < 1 or > 9999 || row.DisplaySeconds is < 1 or > 30 ||
        row.TitleKey_Ref?.Key != row.TitleKey || row.MessageKey_Ref?.Key != row.MessageKey ||
        row.AssetAddress_Ref?.Address != row.AssetAddress))
    throw new InvalidDataException("The generated C# accessor could not resolve valid demo rows and references.");
var messages = Read<LocaleData>("characterlab_messages");
var characters = Read<CharacterData>("characterlab_characters");
if (!messages.SequenceEqual(tables.Messages.DataList.Select(row => new LocaleData(row.Key, row.ZhCn))) ||
    !characters.SequenceEqual(tables.Characters.DataList.Select(row => new CharacterData(row.Id, row.NameKey, row.SortOrder, row.BattleSkin, row.PortraitSkin))) ||
    tables.Characters.DataList.Any(row => row.NameKey_Ref?.Key != row.NameKey || row.SortOrder is < 1 or > 9999))
    throw new InvalidDataException("CharacterLab binary rows or resolved localization references differ from JSON.");
var battleHeroes = Read<BattleHeroData>("battle_battleheroes");
var growthPresets = Read<BattleGrowthPresetData>("battle_battlegrowthpresets");
var awakeningSteps = Read<BattleAwakeningStepData>("battle_battleawakeningsteps");
var battleSkills = Read<BattleSkillData>("battle_battleskills");
var battleEffects = Read<BattleSkillEffectData>("battle_battleskilleffects");
var battleEnhancements = Read<BattleSkillEnhancementData>("battle_battleskillenhancements");
var battleMessages = Read<LocaleData>("battle_battlemessages");
if (!battleHeroes.SequenceEqual(tables.BattleHeroes.DataList.Select(row => new BattleHeroData(
        row.Id, row.NameKey, row.Element, row.HeroClass, row.Zodiac, row.GrowthPresetId, row.BaseStars, row.Level, row.Stars,
        row.Awakenings, row.Variant, row.Attack, row.Health, row.Defense, row.Speed, row.CritChanceBp,
        row.CritDamageBp, row.EffectivenessBp, row.EffectResistanceBp, row.ResourceKind, row.ResourceMaximum,
        row.ResourceTurnGain, row.ResourceAttackedSpend, row.ResourceOpeningGain, row.ResourceAttackedGain))) ||
    !growthPresets.SequenceEqual(tables.BattleGrowthPresets.DataList.Select(row => new BattleGrowthPresetData(
        row.Id, row.NativeStars, row.HeroClass, row.Zodiac, row.AttackCoefficientMilli,
        row.HealthCoefficientMilli, row.DefenseCoefficientMilli, row.SpeedMilli))) ||
    !awakeningSteps.SequenceEqual(tables.BattleAwakeningSteps.DataList.Select(row => new BattleAwakeningStepData(
        row.Id, row.HeroId, row.Rank, row.AttackFlat, row.HealthFlat, row.AttackPercentBp,
        row.HealthPercentBp, row.DefensePercentBp, row.SpeedFlat, row.CritChanceBp, row.CritDamageBp,
        row.EffectivenessBp, row.EffectResistanceBp))) ||
    !battleSkills.SequenceEqual(tables.BattleSkills.DataList.Select(row => new BattleSkillData(
        row.Id, row.HeroId, row.Slot, row.NameKey, row.DescriptionKey, row.PresentationId, row.IconAsset,
        row.Target, row.CooldownTurns, row.SoulGain, row.SoulBurnCost, row.SoulBurnKind, row.SoulBurnTurns,
        row.AttackRateBp, row.PowerBp, row.CasterHealthDamageBp, row.CasterHealthHealBp,
        row.CasterHealthBarrierBp, row.BurnAttackRateBp, row.CasterDefenseBarrierBp, row.CasterDefenseDamageBp,
        row.CasterSpeedDamagePpm, row.BurnSpeedDamagePpm, row.CasterHealthRatioDamageBp, row.DamageAgainstBuffBp,
        row.DefensePenetrationBp, row.StealthPenetrationBp, row.HighestDefensePenetrationBp,
        row.AlwaysCritical, row.AoeWhenBuffed, row.AoeOutsideTurn, row.CasterAttackBarrierBp,
        row.ResourceCost, row.ResourceGain, row.ResourceGainWhenBuffed,
        row.SpeedUpPresentationId, row.SpeedUpIconAsset, row.SpeedUpAttackRateBp, row.SpeedUpPowerBp,
        row.SpeedUpNameKey, row.SpeedUpDescriptionKey))) ||
    !battleEffects.SequenceEqual(tables.BattleSkillEffects.DataList.Select(row => new BattleSkillEffectData(
        row.Id, row.SkillId, row.Order, row.Phase, row.Operation, row.Recipient, row.Status, row.ChanceBp, row.Turns, row.Value,
        row.Condition, row.ChanceBonusStatus, row.ChanceBonusBp))) ||
    !battleEnhancements.SequenceEqual(tables.BattleSkillEnhancements.DataList.Select(row =>
        new BattleSkillEnhancementData(row.Id, row.SkillId, row.Step, row.Kind, row.Value))) ||
    !battleMessages.SequenceEqual(tables.BattleMessages.DataList.Select(row => new LocaleData(row.Key, row.ZhCn))))
    throw new InvalidDataException("Battle binary rows differ from the generated Luban JSON.");

if (tables.BattleHeroes.DataList.Any(row => row.NameKey_Ref?.Key != row.NameKey ||
        row.GrowthPresetId_Ref?.Id != row.GrowthPresetId) ||
    tables.BattleAwakeningSteps.DataList.Any(row => row.HeroId_Ref?.Id != row.HeroId) ||
    tables.BattleSkills.DataList.Any(row => row.HeroId_Ref?.Id != row.HeroId ||
        row.NameKey_Ref?.Key != row.NameKey || row.DescriptionKey_Ref?.Key != row.DescriptionKey) ||
    tables.BattleSkillEffects.DataList.Any(row => row.SkillId_Ref?.Id != row.SkillId) ||
    tables.BattleSkillEnhancements.DataList.Any(row => row.SkillId_Ref?.Id != row.SkillId))
    throw new InvalidDataException("Battle accessors could not resolve hero, skill or localization references.");
foreach (var hero in tables.BattleHeroes.DataList)
{
    var row = hero.GrowthPresetId_Ref;
    var preset = new HeroGrowthPreset(row.Id, row.NativeStars, row.AttackCoefficientMilli,
        row.HealthCoefficientMilli, row.DefenseCoefficientMilli, row.SpeedMilli);
    var steps = tables.BattleAwakeningSteps.DataList.Where(step => step.HeroId == hero.Id)
        .OrderBy(step => step.Rank).Select(step => new HeroAwakeningStep(step.Rank, step.AttackFlat,
            step.HealthFlat, step.AttackPercentBp, step.HealthPercentBp, step.DefensePercentBp,
            step.SpeedFlat, step.CritChanceBp, step.CritDamageBp, step.EffectivenessBp,
            step.EffectResistanceBp)).ToArray();
    var stats = HeroStatCalculator.Calculate(preset, hero.Level, hero.Stars, hero.Awakenings, steps);
    if (stats.Attack != hero.Attack || stats.MaxHealth != hero.Health || stats.Defense != hero.Defense ||
        stats.Speed != hero.Speed || stats.CritChanceBp != hero.CritChanceBp ||
        stats.CritDamageBp != hero.CritDamageBp || stats.EffectivenessBp != hero.EffectivenessBp ||
        stats.EffectResistanceBp != hero.EffectResistanceBp)
        throw new InvalidDataException($"Battle hero growth stats do not match the source panel for {hero.Id} " +
            $"at level {hero.Level}, stars {hero.Stars}, awakenings {hero.Awakenings}. " +
            $"Calculated attack/health/defense/speed: {stats.Attack}/{stats.MaxHealth}/{stats.Defense}/{stats.Speed}.");
}
Console.WriteLine("Generated C# accessors match the current Luban JSON. Content references and hero growth panels were verified.");

record UiData(int Id, string TitleKey, string MessageKey, int DisplaySeconds, string AssetAddress);
record LocaleData(string Key, string ZhCn);
record AssetData(string Address, string SourcePath);

record CharacterData(string Id, string NameKey, int SortOrder, string BattleSkin, string PortraitSkin);

record BattleHeroData(string Id, string NameKey, string Element, string HeroClass, string Zodiac,
    string GrowthPresetId, int BaseStars, int Level, int Stars, int Awakenings, string Variant, int Attack, int Health, int Defense,
    int Speed, int CritChanceBp, int CritDamageBp, int EffectivenessBp, int EffectResistanceBp,
    string ResourceKind, int ResourceMaximum, int ResourceTurnGain, int ResourceAttackedSpend, int ResourceOpeningGain, int ResourceAttackedGain);
record BattleSkillData(string Id, string HeroId, int Slot, string NameKey, string DescriptionKey,
    string PresentationId, string IconAsset, string Target, int CooldownTurns, int SoulGain, int SoulBurnCost,
    string SoulBurnKind, int SoulBurnTurns, int? AttackRateBp, int? PowerBp, int? CasterHealthDamageBp,
    int? CasterHealthHealBp, int? CasterHealthBarrierBp, int? BurnAttackRateBp, int? CasterDefenseBarrierBp,
    int? CasterDefenseDamageBp, int? CasterSpeedDamagePpm, int? BurnSpeedDamagePpm, int? CasterHealthRatioDamageBp,
    int? DamageAgainstBuffBp, int? DefensePenetrationBp, int? StealthPenetrationBp, int? HighestDefensePenetrationBp,
    bool AlwaysCritical, bool AoeWhenBuffed, bool AoeOutsideTurn, int? CasterAttackBarrierBp,
    int ResourceCost, int ResourceGain, int ResourceGainWhenBuffed, string SpeedUpPresentationId,
    string SpeedUpIconAsset, int? SpeedUpAttackRateBp, int? SpeedUpPowerBp, string SpeedUpNameKey, string SpeedUpDescriptionKey);
record BattleSkillEffectData(string Id, string SkillId, int Order, string Phase, string Operation, string Recipient,
    string Status, int ChanceBp, int Turns, int Value, string Condition, string ChanceBonusStatus, int ChanceBonusBp);
record BattleSkillEnhancementData(string Id, string SkillId, int Step, string Kind, int Value);

record BattleGrowthPresetData(string Id, int NativeStars, string HeroClass, string Zodiac,
    int AttackCoefficientMilli, int HealthCoefficientMilli, int DefenseCoefficientMilli, int SpeedMilli);
record BattleAwakeningStepData(string Id, string HeroId, int Rank, int AttackFlat, int HealthFlat,
    int AttackPercentBp, int HealthPercentBp, int DefensePercentBp, int SpeedFlat, int CritChanceBp,
    int CritDamageBp, int EffectivenessBp, int EffectResistanceBp);
