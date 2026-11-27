using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MySqlConnector;
using Xunit;

public sealed class SocialOrderingTests
{
    [MySqlFact]
    public async Task FirstRealmEntryRechecksAnInitialNameClaimedWhileRegistrationWasPending()
    {
        await using var f=await SocialFixture.Create();var(ca,_)=await f.Guest();using var owner=ca;
        string name=SocialFixture.Name();var(cb,registered)=await f.Guest(name);using var oldGuest=cb;
        string entryToken=SocialFixture.Id()+SocialFixture.Id();
        string connection=Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        try
        {
            // A completed registration before first select-realm. Only its entry
            // token is issued; the initial display name is not a realm claim.
            await using(var db=new MySqlConnection(connection))
            {
                await db.OpenAsync();using var q=new MySqlCommand("""
                    INSERT INTO entry_account(player_id,user_key,email,password_salt,password_hash,password_iterations,
                        entry_hash,entry_expires_at,created_at)
                    VALUES(@p,@user,@email,RANDOM_BYTES(16),RANDOM_BYTES(32),100000,@hash,UTC_TIMESTAMP(6)+INTERVAL 10 MINUTE,UTC_TIMESTAMP(6))
                    """,db);
                string user="pending_"+SocialFixture.Id()[..12];q.Parameters.AddWithValue("@p",registered);q.Parameters.AddWithValue("@user",user);q.Parameters.AddWithValue("@email",user+"@example.test");
                q.Parameters.AddWithValue("@hash",SHA256.HashData(Convert.FromHexString(entryToken)));await q.ExecuteNonQueryAsync();
            }
            await SocialFixture.Post(owner,"/api/profile/me/actions",new{requestId=SocialFixture.Id(),expectedVersion=0,kind="nickname",nickname=name});
            using var entering=f.Factory.CreateClient();entering.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",entryToken);
            var result=await SocialFixture.Post(entering,"/api/entry/select-realm",new{realmId="undying-memory",appVersion="0.1.27"});
            Assert.Equal(registered,result.GetProperty("player").GetProperty("id").GetString());
            Assert.NotEqual(name,result.GetProperty("player").GetProperty("displayName").GetString());
            entering.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",result.GetProperty("token").GetString());
            var profile=await SocialFixture.Get(entering,"/api/profile/me");Assert.NotEqual(name,profile.GetProperty("displayName").GetString());
        }
        finally
        {
            await using var db=new MySqlConnection(connection);await db.OpenAsync();using var q=new MySqlCommand("DELETE FROM entry_account WHERE player_id=@p",db);q.Parameters.AddWithValue("@p",registered);await q.ExecuteNonQueryAsync();
        }
    }

    [MySqlFact]
    public async Task ACommittedCursorCannotSkipAnEarlierUncommittedMessageAndOtherChannelsRemainIndependent()
    {
        await using var f=await SocialFixture.Create();var(ca,a)=await f.Guest();using var first=ca;
        var(cb,_)=await f.Guest();using var second=cb;var(cc,_)=await f.Guest();using var otherChannel=cc;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));var ct=deadline.Token;
        await using var db=new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST"));await db.OpenAsync(ct);
        const string request="88888888888888888888888888888888";
        // Fence only A's receipt-key gap. This pauses the real HTTP sender after
        // message allocation, when it inserts its receipt, without production hooks.
        foreach(string boundary in new[]{new string('0',32),new string('f',32)})
        {
            using var seed=new MySqlCommand("INSERT INTO channel_chat_operation(player_id,realm_id,request_id,fingerprint,receipt) VALUES(@p,'undying-memory',@id,RANDOM_BYTES(32),JSON_OBJECT())",db);
            seed.Parameters.AddWithValue("@p",a);seed.Parameters.AddWithValue("@id",boundary);await seed.ExecuteNonQueryAsync(ct);
        }
        await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        using(var gate=new MySqlCommand("SELECT request_id FROM channel_chat_operation WHERE player_id=@p AND realm_id='undying-memory' AND request_id=@id FOR UPDATE",db,tx))
        {gate.Parameters.AddWithValue("@p",a);gate.Parameters.AddWithValue("@id",request);Assert.Null(await gate.ExecuteScalarAsync(ct));}
        var earlierSend=first.PostAsJsonAsync("/api/chat/channels/9155/messages",new{requestId=request,kind="text",text="allocated before commit"},ct);
        long earlier=0;
        await using(var probe=new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")))
        {
            await probe.OpenAsync(ct);await using var read=await probe.BeginTransactionAsync(IsolationLevel.ReadUncommitted,ct);
            using var q=new MySqlCommand("SELECT id FROM channel_chat_message WHERE player_id=@p AND channel_no=9155",probe,read);q.Parameters.AddWithValue("@p",a);
            for(int i=0;i<100&&earlier==0;i++){earlier=Convert.ToInt64(await q.ExecuteScalarAsync(ct));if(earlier==0)await Task.Delay(20,ct);}
        }
        Assert.True(earlier>0,"Real sender A must allocate an id before its receipt insert is released.");Assert.False(earlierSend.IsCompleted);
        var later=second.PostAsJsonAsync("/api/chat/channels/9155/messages",new{requestId=SocialFixture.Id(),kind="text",text="later transaction"},ct);
        using var independent=await otherChannel.PostAsJsonAsync("/api/chat/channels/9156/messages",new{requestId=SocialFixture.Id(),kind="text",text="other channel"},ct);
        Assert.Equal(HttpStatusCode.OK,independent.StatusCode);
        // Give the independent HTTP writer an opportunity to commit. It must
        // remain blocked by A only in9155;9156 above already committed.
        await Task.WhenAny(later,Task.Delay(150,ct));
        var seen=await SocialFixture.Get(first,"/api/chat/channels/9155/messages");string cursor=seen.GetProperty("nextAfter").GetString()!;
        await tx.CommitAsync(ct);
        using var firstCompleted=await earlierSend;Assert.Equal(HttpStatusCode.OK,firstCompleted.StatusCode);
        using var completed=await later;Assert.Equal(HttpStatusCode.OK,completed.StatusCode);
        var after=await SocialFixture.Get(first,"/api/chat/channels/9155/messages?after="+cursor);
        Assert.Contains(after.GetProperty("messages").EnumerateArray(),m=>m.GetProperty("id").GetString()==earlier.ToString());
        Assert.DoesNotContain(seen.GetProperty("messages").EnumerateArray(),m=>m.GetProperty("player").GetProperty("id").GetString()!=a);
    }
}
