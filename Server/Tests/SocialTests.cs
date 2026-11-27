using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class SocialProfileTests
{
    [MySqlFact]
    public async Task ProfilesUseStableIdentityAndRealInventoryWithoutGrantingOtherPlayers()
    {
        await using var f = await SocialFixture.Create();
        using var anonymous = f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile/me")).StatusCode);
        var (first, id) = await f.Guest(); using var a = first;
        var (second, other) = await f.Guest(); using var b = second;
        var me = await SocialFixture.Get(a, "/api/profile/me");
        Assert.Equal(id, me.GetProperty("playerId").GetString());
        Assert.True(me.GetProperty("isSelf").GetBoolean());
        Assert.False(me.GetProperty("rank").GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("representativeHero").ValueKind);
        using var inventory = await a.GetAsync("/api/inventory/heroes"); inventory.EnsureSuccessStatusCode();
        me = await SocialFixture.Get(a, "/api/profile/me");
        Assert.Equal("c1001", me.GetProperty("representativeHero").GetProperty("templateId").GetString());
        var viewed = await SocialFixture.Get(b, "/api/profile/players/" + id);
        Assert.False(viewed.GetProperty("isSelf").GetBoolean()); Assert.False(viewed.GetProperty("canEdit").GetBoolean());
        Assert.Equal(id, viewed.GetProperty("playerId").GetString());
        Assert.Equal(0L, await f.Count("SELECT COUNT(*) FROM inventory_hero WHERE player_id=@p", other));
        using var forged = await b.PostAsJsonAsync("/api/profile/me/actions", new { requestId=SocialFixture.Id(), expectedVersion=0, kind="greeting", greeting="x", playerId=id });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    [MySqlFact]
    public async Task GreetingPrivacyAndIdempotencyPersistWithoutAuditingFreeText()
    {
        await using var f = await SocialFixture.Create();
        var (client,id) = await f.Guest(); using var a = client;
        var (other,_) = await f.Guest(); using var b = other;
        string request = SocialFixture.Id(), secret = "问候" + Guid.NewGuid().ToString("N");
        var input = new { requestId=request, expectedVersion=0, kind="greeting", greeting=secret };
        var responses = await Task.WhenAll(Enumerable.Range(0,4).Select(_ => SocialFixture.Post(a,"/api/profile/me/actions",input)));
        Assert.All(responses,x=>Assert.Equal(1,x.GetProperty("version").GetInt64()));
        Assert.All(responses,x=>Assert.Equal(secret,x.GetProperty("greeting").GetString()));
        using var conflict = await a.PostAsJsonAsync("/api/profile/me/actions", new {requestId=request,expectedVersion=0,kind="greeting",greeting="不同"});
        Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        var hidden=await SocialFixture.Post(a,"/api/profile/me/actions",new {requestId=SocialFixture.Id(),expectedVersion=1,kind="visibility",seasonVisible=false,bestVisible=false});
        Assert.Equal(2,hidden.GetProperty("version").GetInt64());
        var viewed=await SocialFixture.Get(b,"/api/profile/players/"+id);
        Assert.False(viewed.GetProperty("seasonRecord").GetProperty("visible").GetBoolean());
        Assert.Equal(JsonValueKind.Null,viewed.GetProperty("seasonRecord").GetProperty("data").ValueKind);
        using var tooLong=await a.PostAsJsonAsync("/api/profile/me/actions",new {requestId=SocialFixture.Id(),expectedVersion=2,kind="greeting",greeting=new string('中',51)});
        Assert.Equal(HttpStatusCode.BadRequest,tooLong.StatusCode);
        using var stale=await a.PostAsJsonAsync("/api/profile/me/actions",new {requestId=SocialFixture.Id(),expectedVersion=0,kind="greeting",greeting="旧"});
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        Assert.Equal(2L,await f.Count("SELECT COUNT(*) FROM game_operation_audit WHERE actor_id=@p AND operation LIKE 'profile.%'",id));
        Assert.Equal(0L,await f.Count("SELECT COUNT(*) FROM game_operation_audit WHERE actor_id=@p AND (CONVERT(subject_id USING utf8mb4) LIKE '%问候%' OR CONVERT(operation USING utf8mb4) LIKE '%问候%')",id));
        await using var restarted=f.Restart(); using var returned=restarted.CreateClient();returned.DefaultRequestHeaders.Authorization=a.DefaultRequestHeaders.Authorization;
        Assert.Equal(secret,(await SocialFixture.Get(returned,"/api/profile/me")).GetProperty("greeting").GetString());
    }

    [MySqlFact]
    public async Task OldDuplicateNamesSurviveAndConcurrentClaimsHaveOneWinner()
    {
        await using var f=await SocialFixture.Create();string legacy=SocialFixture.Name();
        var (ca,ia)=await f.Guest(legacy);using var a=ca;var (cb,ib)=await f.Guest(legacy);using var b=cb;
        Assert.NotEqual(ia,ib);await SocialFixture.Get(a,"/api/profile/me");await SocialFixture.Get(b,"/api/profile/me");
        var unavailable=await SocialFixture.Post(a,"/api/profile/nickname/check",new{nickname=legacy});
        Assert.False(unavailable.GetProperty("available").GetBoolean());
        string name="N"+SocialFixture.Id()[..8].Replace('0','a').Replace('1','b').Replace('2','c').Replace('3','d').Replace('4','e').Replace('5','f').Replace('6','g').Replace('7','h').Replace('8','i').Replace('9','j');
        var writes=await Task.WhenAll(new[]{a,b}.Select(c=>c.PostAsJsonAsync("/api/profile/me/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="nickname",nickname=name})));
        Assert.Single(writes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(writes,x=>x.StatusCode==HttpStatusCode.Conflict);
        using var create=f.Factory.CreateClient();using var collision=await create.PostAsJsonAsync("/api/arena/sessions",new{displayName=name});
        Assert.Equal(HttpStatusCode.Conflict,collision.StatusCode);
        using var invalid=await a.PostAsJsonAsync("/api/profile/nickname/check",new{nickname="has space"});
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        foreach(var response in writes)response.Dispose();
    }
}

public sealed class SocialChannelTests
{
    [MySqlFact]
    public async Task PublicChatIsRealmChannelScopedAndIdempotentAcrossRestart()
    {
        await using var f=await SocialFixture.Create();var(ca,id)=await f.Guest();using var a=ca;var(cb,_)=await f.Guest();using var b=cb;
        var input=new{requestId=SocialFixture.Id(),kind="text",text="公开测试"};
        var sent=await SocialFixture.Post(a,"/api/chat/channels/9137/messages",input);
        var replay=await SocialFixture.Post(a,"/api/chat/channels/9137/messages",input);
        Assert.Equal(sent.GetProperty("id").GetString(),replay.GetProperty("id").GetString());
        var same=await SocialFixture.Get(b,"/api/chat/channels/9137/messages");
        Assert.Contains(same.GetProperty("messages").EnumerateArray(),m=>m.GetProperty("id").GetString()==sent.GetProperty("id").GetString());
        var other=await SocialFixture.Get(b,"/api/chat/channels/9138/messages");Assert.DoesNotContain(other.GetProperty("messages").EnumerateArray(),m=>m.GetProperty("player").GetProperty("id").GetString()==id);
        using var wrong=await a.PostAsJsonAsync("/api/chat/channels/9138/messages",input);Assert.Equal(HttpStatusCode.Conflict,wrong.StatusCode);
        using var bad=await a.GetAsync("/api/chat/channels/10000/messages");Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        await using var restarted=f.Restart();using var r=restarted.CreateClient();r.DefaultRequestHeaders.Authorization=a.DefaultRequestHeaders.Authorization;
        Assert.Equal(sent.GetProperty("id").GetString(),(await SocialFixture.Post(r,"/api/chat/channels/9137/messages",input)).GetProperty("id").GetString());
        Assert.Equal(1L,await f.Count("SELECT COUNT(*) FROM channel_chat_message WHERE player_id=@p",id));
        Assert.Equal(1L,await f.Count("SELECT COUNT(*) FROM game_operation_audit WHERE actor_id=@p AND operation='chat.message.send'",id));
    }

    [MySqlFact]
    public async Task PreferencesStayPrivateAndDoNotFilterPublicHistory()
    {
        await using var f=await SocialFixture.Create();var(ca,_)=await f.Guest();using var a=ca;var(cb,_)=await f.Guest();using var b=cb;
        var initial=await SocialFixture.Get(a,"/api/chat/preferences");Assert.Equal(JsonValueKind.Null,initial.GetProperty("favoriteChannel").ValueKind);
        var saved=await SocialFixture.Post(a,"/api/chat/preferences/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="favorite",favoriteChannel=9999});
        Assert.Equal(9999,saved.GetProperty("favoriteChannel").GetInt32());
        saved=await SocialFixture.Post(a,"/api/chat/preferences/actions",new{requestId=SocialFixture.Id(),expectedVersion=1,kind="notifications",notifications=new{hero5=false,artifact5=false,guildPromotion=false,generalChat=false}});
        Assert.False(saved.GetProperty("notifications").GetProperty("generalChat").GetBoolean());
        Assert.Equal(JsonValueKind.Null,(await SocialFixture.Get(b,"/api/chat/preferences")).GetProperty("favoriteChannel").ValueKind);
        await SocialFixture.Post(b,"/api/chat/channels/9139/messages",new{requestId=SocialFixture.Id(),kind="text",text="仍可读取"});
        Assert.NotEmpty((await SocialFixture.Get(a,"/api/chat/channels/9139/messages")).GetProperty("messages").EnumerateArray());
    }

    [MySqlFact]
    public async Task ChannelStreamPublishesCommittedMessagesAndClosingCancelsWait()
    {
        await using var f=await SocialFixture.Create();var(ca,_)=await f.Guest();using var a=ca;var(cb,_)=await f.Guest();using var b=cb;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var stream=await a.GetAsync("/api/chat/channels/9140/events",HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        Assert.Equal("application/x-ndjson",stream.Content.Headers.ContentType?.MediaType);
        using var reader=new StreamReader(await stream.Content.ReadAsStreamAsync(deadline.Token));
        Assert.Equal("{\"kind\":\"sync\"}",await reader.ReadLineAsync(deadline.Token));
        await SocialFixture.Post(b,"/api/chat/channels/9140/messages",new{requestId=SocialFixture.Id(),kind="text",text="已提交"});
        Assert.Equal("{\"kind\":\"chat\"}",await reader.ReadLineAsync(deadline.Token));
        var page=await SocialFixture.Get(a,"/api/chat/channels/9140/messages");string cursor=page.GetProperty("nextAfter").GetString()!;
        using var cancelled=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>a.GetAsync("/api/chat/channels/9140/messages?after="+cursor+"&waitMs=8000",cancelled.Token));
    }
}

internal sealed class SocialFixture : IAsyncDisposable
{
    internal WebApplicationFactory<Program> Factory { get; }
    private readonly string connection;
    private readonly List<string> players=[];
    private SocialFixture(string connection){this.connection=connection;Factory=Restart();}
    internal WebApplicationFactory<Program> Restart()=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:MySql",connection},{"Entry:AllowLegacyArenaGuests","true"}})));
    internal static async Task<SocialFixture> Create(){string c=Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;using var d=new CancellationTokenSource(TimeSpan.FromSeconds(30));await DatabaseMigrator.ApplyAsync(c,d.Token);return new(c);}
    internal static string Id()=>Guid.NewGuid().ToString("N");
    internal static string Name()=>"N"+string.Concat(Id()[..8].Select(c=>(char)('a'+Convert.ToInt32(c.ToString(),16))));
    internal async Task<(HttpClient,string)> Guest(string? name=null){var c=Factory.CreateClient();var j=await Post(c,"/api/arena/sessions",new{displayName=name??"social"+Id()[..10]});string id=j.GetProperty("player").GetProperty("id").GetString()!;players.Add(id);c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",j.GetProperty("token").GetString());return(c,id);}
    internal void Track(string player)=>players.Add(player);
    internal static async Task<JsonElement> Get(HttpClient c,string path){using var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.True(r.Headers.CacheControl?.NoStore);return await r.Content.ReadFromJsonAsync<JsonElement>();}
    internal static async Task<JsonElement> Post(HttpClient c,string path,object input){using var r=await c.PostAsJsonAsync(path,input);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return await r.Content.ReadFromJsonAsync<JsonElement>();}
    internal async Task<long> Count(string sql,string id){await using var db=new MySqlConnection(connection);await db.OpenAsync();using var q=new MySqlCommand(sql,db);q.Parameters.AddWithValue("@p",id);return Convert.ToInt64(await q.ExecuteScalarAsync());}
    public async ValueTask DisposeAsync(){await Factory.DisposeAsync();await using var db=new MySqlConnection(connection);await db.OpenAsync();foreach(string id in players){using var q=new MySqlCommand("DELETE FROM arena_player WHERE id=@p",db);q.Parameters.AddWithValue("@p",id);await q.ExecuteNonQueryAsync();}}
}
