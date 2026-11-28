using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Server.Entry;
using AstralOath.Shared.Battle;

namespace AstralOath.Server.ArenaTower;

internal sealed record TowerRank(string RankId, int RequiredClears);
internal sealed record TowerStage(int Stage, int Level, string[] HeroIds, int Reward, int AttackFlat,
    int HealthFlat, int DefenseFlat, int SpeedFlat);

internal sealed record TowerHero(string InstanceId, string HeroId, int Slot, int Level, int Stars,
    int Awakenings, string? ArtifactId, int ArtifactLevel, IReadOnlyList<EquipmentPiece> Equipment)
{
    internal static TowerHero Freeze(string id, int slot, BattleOwnedHero hero) =>
        new(id, hero.HeroId, slot, hero.Level, hero.Stars, hero.Awakenings, hero.Artifact?.Id, hero.ArtifactLevel, hero.Equipment);
}

internal sealed class TowerCatalog
{
    private sealed record Table(TowerRank[] Ranks, TowerStage[] Stages);
    private readonly BattleCatalogData battle;
    private readonly BattleAiPolicy ai;
    private readonly ArtifactCatalog artifacts;
    private readonly TowerRank[] ranks;
    private readonly Dictionary<string, int> nativeStars;
    internal readonly string BattleHash, RulesHash;
    internal readonly TowerStage[] Stages;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TowerCatalog()
    {
        (battle, BattleHash) = ArenaBattleCatalog.ReadEmbeddedCatalog();
        artifacts = ArtifactCatalogReader.Load();
        ai = new BattleAiPolicy(battle);
        var presets = battle.growthPresets.ToDictionary(x => x.id, x => x.nativeStars);
        nativeStars = battle.heroes.ToDictionary(h => h.id, h => presets[h.growthPresetId]);
        using var stream = typeof(TowerCatalog).Assembly.GetManifestResourceStream("AstralOath.ArenaTower")
            ?? throw new InvalidDataException("Missing tower configuration.");
        var table = JsonSerializer.Deserialize<Table>(stream, Json) ?? throw new InvalidDataException("Empty tower configuration.");
        Stages = table.Stages;
        ranks = table.Ranks;
        if (Stages is not { Length: 100 } || !Stages.Select(s => s.Stage).SequenceEqual(Enumerable.Range(1, 100)) ||
            Stages.Any(s => s.HeroIds is not { Length: >= 1 and <= 4 } || s.HeroIds.Any(h => !nativeStars.ContainsKey(h)) ||
                s.Level is < 1 or > 60 || s.Reward is < 0 or > 1000000 || s.AttackFlat is < 0 or > 100000 ||
                s.HealthFlat is < 0 or > 1000000 || s.DefenseFlat is < 0 or > 100000 || s.SpeedFlat is < 0 or > 1000) ||
            ranks is not { Length: > 0 } || ranks[0].RequiredClears != 0 || ranks.Any(r => string.IsNullOrWhiteSpace(r.RankId) ||
                r.RequiredClears is < 0 or > 100) || !ranks.Select(r => r.RequiredClears).SequenceEqual(ranks.Select(r => r.RequiredClears).Distinct().Order()))
            throw new InvalidDataException("Invalid tower stages or rank thresholds.");
        using var artifact = typeof(TowerCatalog).Assembly.GetManifestResourceStream("AstralOath.ArtifactCatalog")!;
        string replayRules = string.Join("|", "tower.v1", BattleHash, BattlePreviewFactory.RulesVersion,
            EquipmentStatCalculator.RulesVersion, Convert.ToHexString(SHA256.HashData(artifact)));
        RulesHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replayRules)));
    }

    internal TowerStage Stage(int stage) => stage >= 1 && stage <= Stages.Length ? Stages[stage - 1] :
        throw new EntryProblem(404, "tower_stage_not_found");
    internal string Rank(int highest) => ranks.Last(r => r.RequiredClears <= highest).RankId;

    internal BattlePreviewRoom CreateRoom(string id, ulong seed, TowerHero[] allies, TowerStage stage)
    {
        var equipment = stage.AttackFlat + stage.DefenseFlat + stage.HealthFlat + stage.SpeedFlat == 0 ?
            Array.Empty<EquipmentPiece>() : new[] { new EquipmentPiece("speed", new EquipmentStat("attack", stage.AttackFlat),
                new[] { new EquipmentStat("health", stage.HealthFlat), new EquipmentStat("defense", stage.DefenseFlat),
                    new EquipmentStat("speed", stage.SpeedFlat) }) };
        var defenders = stage.HeroIds.Select(h => new BattleOwnedHero(h, stage.Level, Math.Max(nativeStars[h], (stage.Level + 9) / 10),
            0, null, 0, equipment)).ToArray();
        var attackers = allies.Select(h =>
        {
            ArtifactTemplate? artifact = null;
            if (h.ArtifactId != null && !artifacts.TryGet(h.ArtifactId, out artifact))
                throw new InvalidDataException("Missing frozen artifact.");
            return new BattleOwnedHero(h.HeroId, h.Level, h.Stars, h.Awakenings, artifact, h.ArtifactLevel, h.Equipment);
        }).ToArray();
        var match = BattlePreviewFactory.CreateOwnedDuel(id, seed, battle, attackers, defenders);
        return new BattlePreviewRoom(match, "", "", DateTimeOffset.MaxValue, RulesHash, ai, automaticEnemy: true);
    }
}
