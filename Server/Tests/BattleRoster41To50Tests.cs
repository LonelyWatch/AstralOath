using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster41To50Tests
{
    [Theory]
    [InlineData("c1043")]
    [InlineData("c1044")]
    [InlineData("c1046")]
    [InlineData("c1047")]
    [InlineData("c1048")]
    [InlineData("c1049")]
    [InlineData("c1050")]
    [InlineData("c1053")]
    [InlineData("c1054")]
    [InlineData("c1055")]
    public void OriginalSlotsSubmitThroughBattleAndEmitTheirOwnPresentation(string hero)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var skills = catalog.skills.Where(skill => skill.heroId == hero).OrderBy(skill => skill.slot).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, skills.Select(skill => skill.slot));
        foreach (var skill in skills.Where(skill => skill.target != "passive"))
        {
            var match = BattlePreviewFactory.Create("roster", 1, catalog, false, Enumerable.Repeat(hero, 4).ToArray());
            var state = match.Snapshot;
            var actor = state.CurrentActorId;
            Assert.Equal(hero, state.GetUnit(actor).DefinitionId);
            var ai = new BattleAiPolicy(catalog);
            if (skill.id == "c1047.s3")
            {
                Assert.Equal(40, state.GetUnit(actor).Resource.Amount);
                Assert.False(ai.IsSkillAvailable(state, skill.id));
                var charge = Cast(match, ai.ChooseTarget(state, "c1047.s2"));
                Assert.Equal(80, charge.Snapshot.GetUnit(actor).Resource.Amount);
                AdvanceTo(match, actor, ai);
            }
            var result = Cast(match, ai.ChooseTarget(match.Snapshot, skill.id));
            Assert.Contains(result.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
                evt.SkillId == skill.id && evt.PresentationId == skill.presentationId);
            if (skill.id == "c1047.s3")
            {
                Assert.Equal(0, result.Snapshot.GetUnit(actor).Resource.Amount);
                AdvanceTo(match, actor, ai);
                var empowered = Cast(match, ai.ChooseTarget(match.Snapshot, "c1047.s1"));
                Assert.Contains(empowered.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
                    evt.SourceId == actor && evt.PresentationId == "c_ken_skill_01_01");
            }
        }
    }

    private static void AdvanceTo(BattleMatch match, string actor, BattleAiPolicy ai)
    {
        for (int turn = 0; turn < 20 && match.Snapshot.CurrentActorId != actor; turn++)
        {
            var state = match.Snapshot;
            var active = state.GetUnit(state.CurrentActorId);
            Cast(match, ai.ChooseTarget(state, active.Form.SkillIds[0]));
        }
        Assert.Equal(actor, match.Snapshot.CurrentActorId);
    }

    private static BattleCommandResult Cast(BattleMatch match, BattleAiChoice choice)
    {
        Assert.NotNull(choice);
        var state = match.Snapshot;
        var sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "cmd-" + sequence, state.CurrentActorId, choice.SkillId, choice.TargetId));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
