using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleZeratoTests
{
    [Fact]
    public void IcePickUsesTheAwakenedDebuffConditionAndSoulBurnWithoutChangingPresentation()
    {
        int normal = IcePick(3, null, false);
        Assert.InRange(IcePick(3, BattleStatusKind.Unhealable, false), normal * 13 / 10 - 1, normal * 13 / 10 + 2);
        Assert.Equal(normal, IcePick(3, BattleStatusKind.AttackUp, false));
        Assert.Equal(IcePick(2, null, false), IcePick(2, BattleStatusKind.Unhealable, false));
        Assert.InRange(IcePick(3, null, true), normal * 4 / 3 - 1, normal * 4 / 3 + 2);
    }

    [Theory]
    [InlineData(1, "c_jerato_skill_01", 1)]
    [InlineData(3, "c_jerato_skill_03", 2)]
    public void StandardSkillsSelectExpectedRecipientsAndPublishTheirSourceGraph(int slot, string presentation, int recipients)
    {
        var catalog = Catalog();
        var match = Match(catalog, 6, null);
        Start(match);
        var response = Submit(match, "zerato", $"c1010.s{slot}", "enemy-1");
        Assert.Equal(BattleCommandStatus.Accepted, response.Status);
        Assert.Equal(presentation, Assert.Single(response.Events, e => e.Kind == BattleFlowEventKind.Action).PresentationId);
        Assert.Equal(recipients, response.Events.Count(e => e.Kind == BattleFlowEventKind.Damage));
    }

    private static int IcePick(int awakenings, BattleStatusKind? status, bool burn)
    {
        var match = Match(Catalog(), awakenings, status);
        Start(match);
        var result = Submit(match, "zerato", "c1010.s2", "enemy-1", burn);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal("c_jerato_skill_02", Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Action).PresentationId);
        Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.ReadinessChanged &&
            e.TargetId == "zerato" && e.Amount == 5000);
        Assert.Equal(burn ? 12 : 22, result.Snapshot.AllySoul);
        return Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage).Amount;
    }

    private static void Start(BattleMatch match) =>
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, "setup", "setup.s1", "enemy-1").Status);

    private static BattleMatch Match(BattleCatalogData catalog, int awakenings, BattleStatusKind? status)
    {
        var row = Assert.Single(catalog.heroes, hero => hero.id == "c1010");
        var data = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(data.id, data.nativeStars, data.attackCoefficientMilli,
            data.healthCoefficientMilli, data.defenseCoefficientMilli, data.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(step => new HeroAwakeningStep(step.rank, step.attackFlat, step.healthFlat, step.attackPercentBp,
                step.healthPercentBp, step.defensePercentBp, step.speedFlat, step.critChanceBp,
                step.critDamageBp, step.effectivenessBp, step.effectResistanceBp)).ToArray();
        var skills = catalog.skills.Where(skill => skill.heroId == row.id).OrderBy(skill => skill.slot).Select(skill => skill.id);
        var definition = new HeroDefinition(row.id, row.nameKey, BattleElement.Ice, row.heroClass, row.zodiac,
            new BattleUnitForm("standard", row.id, skills), preset, steps);
        var zerato = BattleUnit.CreateHero("zerato", definition, new BattleSlot(BattleSide.Ally, 2), 60, 6, awakenings);
        return new BattleMatch("ice", BattlePreviewFactory.RulesVersion, 23, new BattleFormation(new[]
        {
            Dummy("setup", BattleSide.Ally, 1, 150), zerato,
            Dummy("enemy-1", BattleSide.Enemy, 1, 20), Dummy("enemy-2", BattleSide.Enemy, 2, 15),
        }), new SetupResolver(catalog, status), new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleUnit Dummy(string id, BattleSide side, int slot, int speed)
    {
        var form = new BattleUnitForm("standard", id, new[] { id + ".s1" });
        return BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Ice, form),
            new BattleSlot(side, slot), 60, MonsterRank.Normal, new BattleStats(1, 30000, 300, speed));
    }

    private static BattleCommandResult Submit(BattleMatch match, string actor, string skill, string target, bool burn = false)
    {
        var snapshot = match.Snapshot;
        ulong sequence = snapshot.LastCommandSequence + 1;
        return match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion, sequence,
            $"action-{sequence}", actor, skill, target, burn));
    }

    private static BattleCatalogData Catalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }

    private sealed class SetupResolver(BattleCatalogData catalog, BattleStatusKind? status) : IBattleActionResolver
    {
        private readonly BattleSkillResolver inner = new(catalog, randomDualChanceBp: 0);

        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            if (command.ActorId != "setup")
                return inner.TryResolve(context, command, random, out resolution);
            resolution = new BattleActionResolution(status.HasValue
                ? new[] { BattleChange.ApplyStatus(command.ActorId, command.TargetId, status.Value, 2, 5000) }
                : Array.Empty<BattleChange>());
            return true;
        }
    }
}
