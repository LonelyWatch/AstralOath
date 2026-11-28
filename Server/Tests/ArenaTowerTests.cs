using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.ArenaTower;
using AstralOath.Server.Battle;
using AstralOath.Shared.Battle;
using MySqlConnector;
using Xunit;

public sealed class ArenaTowerTests
{
    private const string Tower = "/api/arena/tower";

    [Fact]
    public void StartingRosterClearsFirstFiveStagesWithNoGearAtLevelOne()
    {
        var tower = new TowerCatalog();
        var (catalog, _) = ArenaBattleCatalog.ReadEmbeddedCatalog();
        var policy = new BattleAiPolicy(catalog);
        var attackers = new[] { "c1001", "c1005", "c1062" }.Select((hero, i) =>
            new TowerHero("starter-" + i, hero, i + 1, 1, i == 0 ? 3 : 4, 0, null, 0, [])).ToArray();
        foreach (var stage in tower.Stages.Take(5))
        {
            Assert.Single(stage.HeroIds);
            Assert.Equal(0, stage.AttackFlat + stage.HealthFlat + stage.DefenseFlat + stage.SpeedFlat);
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var room = tower.CreateRoom($"newcomer-{stage.Stage}-{seed}", seed, attackers, stage);
                var snapshot = room.Read(0).Snapshot;
                for (int turn = 0; turn < 100 && snapshot.Outcome == BattleOutcome.InProgress; turn++)
                {
                    var action = policy.ChooseAction(snapshot);
                    Assert.NotNull(action);
                    var reply = room.Submit(BattleSide.Ally, new BattlePreviewCommand(snapshot.RulesVersion,
                        snapshot.LastCommandSequence + 1, "action-" + turn, snapshot.CurrentActorId, action.SkillId,
                        action.TargetId, action.UseSoulBurn, tower.RulesHash));
                    Assert.Equal(BattleCommandStatus.Accepted, reply?.Status);
                    snapshot = reply!.Snapshot;
                }
                Assert.Equal(BattleOutcome.AllyVictory, snapshot.Outcome);
            }
        }
        foreach (var stage in tower.Stages)
        {
            var opening = tower.CreateRoom("opening-" + stage.Stage, 1, attackers, stage).Read(0).Snapshot;
            Assert.Equal(stage.HeroIds.Length, opening.Units.Count(unit => unit.Slot.Side == BattleSide.Enemy));
        }
        Assert.Equal("master", tower.Rank(54));
        Assert.Equal("challenger", tower.Rank(55));
        Assert.Equal("legend", tower.Rank(100));
    }

    [MySqlFact]
    public async Task FirstStageCanStartWithStartersAndOtherPlayersCannotReadOrSkipIt()
    {
        await using var f = await SocialFixture.Create();
        var (first, _) = await f.Guest(); using var player = first;
        var (second, _) = await f.Guest(); using var other = second;
        var list = await SocialFixture.Get(player, Tower);
        Assert.Equal(1, list.GetProperty("currentStage").GetInt32());
        Assert.Equal(100, list.GetProperty("stages").GetArrayLength());
        var ids = InventoryTests.AssertStarters(await InventoryTests.Read(player, default));
        var body = new { requestId = SocialFixture.Id(), heroInstanceIds = ids };
        Assert.Equal(HttpStatusCode.Conflict, (await player.PostAsJsonAsync(Tower + "/stages/2/challenge", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(Tower + "/stages/1/challenge", body)).StatusCode);
        var opening = await SocialFixture.Post(player, Tower + "/stages/1/challenge", body);
        var room = opening.GetProperty("roomId").GetString();
        Assert.Equal(room, (await SocialFixture.Post(player, Tower + "/stages/1/challenge", body)).GetProperty("roomId").GetString());
        string path = Tower + "/battles/" + room;
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        await using var restart = f.Restart();
        using var client = restart.CreateClient();
        client.DefaultRequestHeaders.Authorization = player.DefaultRequestHeaders.Authorization;
        var snapshot = (await SocialFixture.Get(client, path)).GetProperty("snapshot");
        Assert.Equal(room, snapshot.GetProperty("battleId").GetString());
        Assert.Equal(1, snapshot.GetProperty("units").EnumerateArray().Count(u => u.GetProperty("slot").GetProperty("side").GetInt32() == (int)BattleSide.Enemy));
    }

    [MySqlFact]
    public async Task ConcurrentFirstWinsFreezeOneFormationAndPayOnlyOnce()
    {
        await using var f = await SocialFixture.Create();
        var (c, playerId) = await f.Guest(); using var player = c;
        var ids = InventoryTests.AssertStarters(await InventoryTests.Read(player, default));
        // Bring two real account instances to level 60 so a seed or miss cannot decide the fixture.
        await using (var db = new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")))
        {
            await db.OpenAsync();
            using var q = new MySqlCommand("UPDATE inventory_hero SET stars=6,level=60 WHERE player_id=@player", db);
            q.Parameters.AddWithValue("@player", playerId);
            await q.ExecuteNonQueryAsync();
        }
        var formations = new[] { new[] { ids[0] }, new[] { ids[1], ids[0] } };
        var initial = await SocialFixture.Get(player, Tower);
        int reward = initial.GetProperty("stages")[0].GetProperty("reward").GetInt32();
        var invitations = await Task.WhenAll(formations.Select(team => SocialFixture.Post(player, Tower + "/stages/1/challenge",
            new { requestId = SocialFixture.Id(), heroInstanceIds = team })));
        await Task.WhenAll(invitations.Select(Win));
        long points = await f.Count("SELECT balance FROM arena_tower_wallet WHERE player_id=@p AND currency_id='conquest_point'", playerId);
        Assert.Equal(reward, points);
        var state = await SocialFixture.Get(player, Tower);
        Assert.Equal(1, state.GetProperty("highestCleared").GetInt32());
        Assert.Equal(2, state.GetProperty("currentStage").GetInt32());
        var record = await SocialFixture.Get(player, Tower + "/stages/1/clear-team");
        Assert.Equal(new[] { "c1001" }, record.GetProperty("defenderHeroIds").EnumerateArray().Select(id => id.GetString()));
        string[] frozen = record.GetProperty("heroes").EnumerateArray().Select(h => h.GetProperty("instanceId").GetString()!).ToArray();
        Assert.Contains(formations, team => team.SequenceEqual(frozen));
        Assert.Equal(1, await f.Count("SELECT COUNT(*) FROM arena_tower_clear WHERE player_id=@p", playerId));
        var repeat = await SocialFixture.Post(player, Tower + "/stages/1/challenge",
            new { requestId = SocialFixture.Id(), heroInstanceIds = formations.First(v => !frozen.SequenceEqual(v)) });
        await Win(repeat);
        Assert.Equal(record.GetRawText(), (await SocialFixture.Get(player, Tower + "/stages/1/clear-team")).GetRawText());

        async Task Win(JsonElement invite)
        {
            var snapshot = invite.GetProperty("opening").GetProperty("snapshot");
            string room = invite.GetProperty("roomId").GetString()!;
            for (int turn = 0; turn < 40 && snapshot.GetProperty("outcome").GetInt32() == 0; turn++)
            {
                string actor = snapshot.GetProperty("currentActorId").GetString()!;
                var units = snapshot.GetProperty("units").EnumerateArray().ToArray();
                var actorRow = units.Single(u => u.GetProperty("instanceId").GetString() == actor);
                var command = new { battleId = room, rulesVersion = BattlePreviewFactory.RulesVersion,
                    sequence = 1 + snapshot.GetProperty("lastCommandSequence").GetUInt64(), commandId = SocialFixture.Id(), actorId = actor,
                    skillId = actorRow.GetProperty("form").GetProperty("skillIds")[0].GetString(),
                    targetId = units.First(u => u.GetProperty("isAlive").GetBoolean() && u.GetProperty("slot").GetProperty("side").GetInt32() == 1).GetProperty("instanceId").GetString(),
                    contentHash = invite.GetProperty("contentHash").GetString() };
                var result = await SocialFixture.Post(player, Tower + "/battles/" + room + "/commands", command);
                Assert.Equal((int)BattleCommandStatus.Accepted, result.GetProperty("status").GetInt32());
                snapshot = result.GetProperty("snapshot");
            }
            Assert.Equal((int)BattleOutcome.AllyVictory, snapshot.GetProperty("outcome").GetInt32());
        }
    }
}
