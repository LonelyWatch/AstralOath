using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Social;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

public sealed class SocialBoundaryTests
{
    [Theory]
    [InlineData("Abcd",true)] [InlineData("Abc",false)] [InlineData("Abcdefghijkl",true)] [InlineData("Abcdefghijklm",false)]
    [InlineData("中文",true)] [InlineData("中",false)] [InlineData("中文玩家名字八个",true)] [InlineData("中文玩家名字超过八个",false)]
    [InlineData("中Ab12",true)] [InlineData("1234",false)] [InlineData("Ab_12",false)] [InlineData("中 文",false)]
    [InlineData("昵称😊",false)] [InlineData(" 玩家 ",true)] [InlineData("中文<tag>",false)]
    public void NicknameRulesMatchTheClientScalarContract(string input,bool valid)
    {
        if(valid)Assert.NotEmpty(PlayerNicknameRegistry.Validate(input));
        else Assert.Throws<EntryProblem>(()=>PlayerNicknameRegistry.Validate(input));
    }

    [Fact]
    public void GreetingCountsUnicodeScalarsAndEventSubscriptionsHaveDeterministicOwnership()
    {
        string fifty=string.Concat(Enumerable.Repeat("\U00020000",50));
        Assert.Equal(fifty,SocialData.Text(fifty,50,true,"invalid_greeting",true));
        Assert.Throws<SocialProblem>(()=>SocialData.Text(fifty+"中",50,true,"invalid_greeting",true));
        Assert.Equal("",SocialData.Text("   ",50,true,"invalid_greeting",true));
        Assert.Equal("甲\n乙",SocialData.Text("甲\r\n乙",50,true,"invalid_greeting",true));
        var events=new ChannelChatEvents();using(var first=events.Subscribe("realm",1))using(var second=events.Subscribe("realm",2))Assert.Equal(2,events.SubscriptionCount);
        Assert.Equal(0,events.SubscriptionCount);
    }

    [MySqlFact]
    public async Task ChannelPagingNeverIncludesAnotherRealmAndReadsAreNonMutating()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var client=c;
        string connection=Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await using(var db=new MySqlConnection(connection))
        {
            await db.OpenAsync();await using var tx=await db.BeginTransactionAsync();
            using var add=new MySqlCommand("INSERT INTO channel_chat_message(realm_id,channel_no,player_id,kind,body,created_at) VALUES(@r,9141,@p,'text',@text,UTC_TIMESTAMP(6))",db,tx);
            add.Parameters.AddWithValue("@p",id);var realm=add.Parameters.AddWithValue("@r","undying-memory");var text=add.Parameters.AddWithValue("@text","");
            for(int i=0;i<225;i++){text.Value="seed"+i;await add.ExecuteNonQueryAsync();}
            realm.Value="another-realm";text.Value="must_not_leak";await add.ExecuteNonQueryAsync();await tx.CommitAsync();
        }
        var first=await SocialFixture.Get(client,"/api/chat/channels/9141/messages?after=1");
        Assert.Equal(100,first.GetProperty("messages").GetArrayLength());Assert.True(first.GetProperty("hasMore").GetBoolean());
        var all=new List<JsonElement>(first.GetProperty("messages").EnumerateArray());string cursor=first.GetProperty("nextAfter").GetString()!;
        while(true){var page=await SocialFixture.Get(client,"/api/chat/channels/9141/messages?after="+cursor);all.AddRange(page.GetProperty("messages").EnumerateArray());cursor=page.GetProperty("nextAfter").GetString()!;if(!page.GetProperty("hasMore").GetBoolean())break;}
        Assert.Equal(225,all.Count(x=>x.GetProperty("player").GetProperty("id").GetString()==id));Assert.DoesNotContain(all,x=>x.GetProperty("text").GetString()=="must_not_leak");
        Assert.Equal(all.Count,all.Select(x=>x.GetProperty("id").GetString()).Distinct().Count());
        var newest=await SocialFixture.Get(client,"/api/chat/channels/9141/messages");Assert.Equal(50,newest.GetProperty("messages").GetArrayLength());
        Assert.Equal(0L,await f.Count("SELECT COUNT(*) FROM channel_chat_preferences WHERE player_id=@p",id));
    }

    [MySqlFact]
    public async Task StickerWhitelistRateLimitAndConcurrentRetryUseOneCommittedMessage()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var a=c;
        using var invalid=await a.PostAsJsonAsync("/api/chat/channels/9142/messages",new{requestId=SocialFixture.Id(),kind="sticker",stickerId="untrusted_url"});Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var input=new{requestId=SocialFixture.Id(),kind="sticker",stickerId="alki_glasses_001"};
        var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>SocialFixture.Post(a,"/api/chat/channels/9142/messages",input)));
        Assert.Single(results.Select(x=>x.GetProperty("id").GetString()).Distinct());Assert.All(results,x=>Assert.Equal("alki_glasses_001",x.GetProperty("stickerId").GetString()));
        using var rapid=await a.PostAsJsonAsync("/api/chat/channels/9143/messages",new{requestId=SocialFixture.Id(),kind="text",text="cannot bypass by switching"});Assert.Equal(HttpStatusCode.TooManyRequests,rapid.StatusCode);
        Assert.Equal(1L,await f.Count("SELECT COUNT(*) FROM channel_chat_message WHERE player_id=@p",id));
    }

    [MySqlFact]
    public async Task InitialIdentityAndRenameSerializeOnTheSameDatabaseNameKey()
    {
        await using var f=await SocialFixture.Create();var(c,_)=await f.Guest();using var owner=c;
        string name=SocialFixture.Name();using var newcomer=f.Factory.CreateClient();
        var results=await Task.WhenAll(owner.PostAsJsonAsync("/api/profile/me/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="nickname",nickname=name}),
            newcomer.PostAsJsonAsync("/api/arena/sessions",new{displayName=name}));
        Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        if(results[1].IsSuccessStatusCode){var created=await results[1].Content.ReadFromJsonAsync<JsonElement>();f.Track(created.GetProperty("player").GetProperty("id").GetString()!);}
        foreach(var response in results)response.Dispose();
    }

    [MySqlFact]
    public async Task ChannelCancellationReleasesSubscriptionsAndRevokedTokenCannotReadOrWrite()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var client=c;
        var events=f.Factory.Services.GetRequiredService<ChannelChatEvents>();Assert.Equal(0,events.SubscriptionCount);
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            using var stream=await client.GetAsync("/api/chat/channels/9144/events",HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            Assert.Equal(1,events.SubscriptionCount);deadline.Cancel();
        }
        for(int i=0;i<20&&events.SubscriptionCount!=0;i++)await Task.Delay(20);
        Assert.Equal(0,events.SubscriptionCount);
        await using(var db=new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")))
        {await db.OpenAsync();using var q=new MySqlCommand("UPDATE arena_player SET token_hash=RANDOM_BYTES(32) WHERE id=@p",db);q.Parameters.AddWithValue("@p",id);await q.ExecuteNonQueryAsync();}
        using var read=await client.GetAsync("/api/chat/channels/1/messages");Assert.Equal(HttpStatusCode.Unauthorized,read.StatusCode);
        using var write=await client.PostAsJsonAsync("/api/chat/channels/1/messages",new{requestId=SocialFixture.Id(),kind="text",text="denied"});Assert.Equal(HttpStatusCode.Unauthorized,write.StatusCode);
    }

    [MySqlFact]
    public async Task ChannelStreamReauthenticatesAndClosesAfterTokenRevocation()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var client=c;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(22));
        using var stream=await client.GetAsync("/api/chat/channels/9145/events",HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        using var reader=new StreamReader(await stream.Content.ReadAsStreamAsync(deadline.Token));Assert.Equal("{\"kind\":\"sync\"}",await reader.ReadLineAsync(deadline.Token));
        await using(var db=new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")))
        {await db.OpenAsync();using var q=new MySqlCommand("UPDATE arena_player SET token_hash=RANDOM_BYTES(32) WHERE id=@p",db);q.Parameters.AddWithValue("@p",id);await q.ExecuteNonQueryAsync();}
        Assert.Null(await reader.ReadLineAsync(deadline.Token));
        Assert.Equal(0,f.Factory.Services.GetRequiredService<ChannelChatEvents>().SubscriptionCount);
    }

    [MySqlFact]
    public async Task NotificationWritesRequireAllFourFlagsWithoutChangingStateOnMalformedInput()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var client=c;
        using(var empty=await client.PostAsJsonAsync("/api/chat/preferences/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="notifications",notifications=new{}}))Assert.Equal(HttpStatusCode.BadRequest,empty.StatusCode);
        using(var partial=await client.PostAsJsonAsync("/api/chat/preferences/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="notifications",notifications=new{hero5=false}}))Assert.Equal(HttpStatusCode.BadRequest,partial.StatusCode);
        var unchanged=await SocialFixture.Get(client,"/api/chat/preferences");Assert.Equal(0,unchanged.GetProperty("version").GetInt64());Assert.True(unchanged.GetProperty("notifications").GetProperty("generalChat").GetBoolean());
        Assert.Equal(0L,await f.Count("SELECT COUNT(*) FROM game_operation_audit WHERE actor_id=@p",id));
    }

    [MySqlFact]
    public async Task ProfilesAndNicknameAvailabilityRespectExistingRealmMembership()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var viewer=c;
        string name=SocialFixture.Name();var(d,other)=await f.Guest(name);using var foreign=d;
        await using(var db=new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")))
        {
            await db.OpenAsync();using var q=new MySqlCommand("INSERT INTO player_realm_membership(realm_id,player_id,first_entered_at,last_entered_at,registered) VALUES('another-realm',@p,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),FALSE)",db);q.Parameters.AddWithValue("@p",other);await q.ExecuteNonQueryAsync();
        }
        using var hidden=await viewer.GetAsync("/api/profile/players/"+other);Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        Assert.True((await SocialFixture.Post(viewer,"/api/profile/nickname/check",new{nickname=name})).GetProperty("available").GetBoolean());
        using var denied=await foreign.GetAsync("/api/chat/channels/1/messages");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [MySqlFact]
    public async Task PreferenceKeywordsAndNoOpReceiptsSurviveConflictsAndDoNotCreateAudits()
    {
        await using var f=await SocialFixture.Create();var(c,id)=await f.Guest();using var a=c;
        var noOp=new{requestId=SocialFixture.Id(),expectedVersion=0,kind="favorite",favoriteChannel=(int?)null};
        Assert.Equal(0,(await SocialFixture.Post(a,"/api/chat/preferences/actions",noOp)).GetProperty("version").GetInt64());
        Assert.Equal(0L,await f.Count("SELECT COUNT(*) FROM game_operation_audit WHERE actor_id=@p",id));
        var input=new{requestId=SocialFixture.Id(),expectedVersion=0,kind="keywords",keywords=new[]{"提醒","双人"}};
        var saved=await SocialFixture.Post(a,"/api/chat/preferences/actions",input);Assert.Equal(1,saved.GetProperty("version").GetInt64());
        Assert.Equal(saved.GetRawText(),(await SocialFixture.Post(a,"/api/chat/preferences/actions",input)).GetRawText());
        using var changed=await a.PostAsJsonAsync("/api/chat/preferences/actions",new{requestId=input.requestId,expectedVersion=0,kind="keywords",keywords=new[]{"other"}});Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
        using var stale=await a.PostAsJsonAsync("/api/chat/preferences/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="favorite",favoriteChannel=2});Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        using var six=await a.PostAsJsonAsync("/api/chat/preferences/actions",new{requestId=SocialFixture.Id(),expectedVersion=1,kind="keywords",keywords=Enumerable.Repeat("key",6).ToArray()});Assert.Equal(HttpStatusCode.BadRequest,six.StatusCode);
        await using var restarted=f.Restart();using var r=restarted.CreateClient();r.DefaultRequestHeaders.Authorization=a.DefaultRequestHeaders.Authorization;
        Assert.Equal("提醒",(await SocialFixture.Get(r,"/api/chat/preferences")).GetProperty("keywords")[0].GetString());
    }
}
