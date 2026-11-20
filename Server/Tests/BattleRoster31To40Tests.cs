using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster31To40Tests
{
    [Theory]
    [InlineData("c1032")]
    [InlineData("c1033")]
    [InlineData("c1034")]
    [InlineData("c1035")]
    [InlineData("c1036")]
    [InlineData("c1037")]
    [InlineData("c1038")]
    [InlineData("c1039")]
    [InlineData("c1040")]
    [InlineData("c1042")]
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
            var result = match.Submit(new BattleSkillCommand("roster", state.RulesVersion, 1, "cmd", actor,
                skill.id, skill.target == "one_enemy" ? "enemy-1" : null));
            Assert.Equal(BattleCommandStatus.Accepted, result.Status);
            Assert.Contains(result.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
                evt.SkillId == skill.id && evt.PresentationId == skill.presentationId);
        }
    }
}
