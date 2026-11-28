using System.Data;
using System.Globalization;
using System.Text.Json;
using AstralOath.Server.Arena;
using AstralOath.Server.Foundation;
using MySqlConnector;
using static AstralOath.Server.Social.SocialData;

namespace AstralOath.Server.Social;

internal sealed class ChannelChatStore(string? connectionString,ChannelChatEvents events)
{
    internal async Task<ChatPreferences> Preferences(string player,CancellationToken ct)
    {
        await using var db=await Open(connectionString,ct);return await ReadPreferences(db,null,player,ct);
    }
    internal async Task<ChatPreferences> ChangePreferences(string player,ChatPreferenceAction input,CancellationToken ct)
    {
        string request=Id(input.RequestId);
        if(input.ExpectedVersion is null or <0)throw new SocialProblem(400,"invalid_chat_preferences");
        switch(input.Kind)
        {
            case "favorite" when input.Notifications is null&&input.Keywords is null&&input.TransparentBackground is null:
                if(input.FavoriteChannel is {} favorite)Channel(favorite);break;
            case "notifications" when input.Notifications is not null&&input.FavoriteChannel is null&&input.Keywords is null&&input.TransparentBackground is null:break;
            case "keywords" when input.Keywords is {Length:<=5}&&input.FavoriteChannel is null&&input.Notifications is null&&input.TransparentBackground is null:
                input=input with{Keywords=input.Keywords.Select(x=>Text(x,20,false,"invalid_chat_preferences")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()};break;
            case "background" when input.TransparentBackground.HasValue&&input.FavoriteChannel is null&&input.Notifications is null&&input.Keywords is null:break;
            default:throw new SocialProblem(400,"invalid_chat_preferences");
        }
        input=input with{RequestId=request};byte[] fingerprint=Fingerprint(input);
        await using var db=await Open(connectionString,ct);await using var tx=await db.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await LockOwner(db,tx,player,ct);
        if(await Receipt<ChatPreferences>(db,tx,"channel_preferences_operation",player,request,fingerprint,ct) is {} saved){await tx.CommitAsync(ct);return saved;}
        var before=await ReadPreferences(db,tx,player,ct);if(before.Version!=input.ExpectedVersion)throw new SocialProblem(409,"chat_preferences_version_conflict");
        var after=before with{FavoriteChannel=input.Kind=="favorite"?input.FavoriteChannel:before.FavoriteChannel,
            Notifications=input.Notifications??before.Notifications,Keywords=input.Keywords??before.Keywords,
            TransparentBackground=input.TransparentBackground??before.TransparentBackground};
        bool changed=JsonSerializer.Serialize(before,Json)!=JsonSerializer.Serialize(after,Json);
        if(changed)
        {
            await EnsurePreferences(db,tx,player,ct);after=after with{Version=before.Version+1};
            using var q=Sql(db,tx,"""
                UPDATE channel_chat_preferences SET favorite_channel=@favorite,hero5=@hero,artifact5=@artifact,
                    guild_promotion=@guild,general_chat=@general,keywords=@keys,transparent_background=@background,
                    revision=@version,updated_at=UTC_TIMESTAMP(6) WHERE player_id=@p AND realm_id=@r
                """,("@favorite",after.FavoriteChannel),("@hero",after.Notifications.Hero5),("@artifact",after.Notifications.Artifact5),
                ("@guild",after.Notifications.GuildPromotion),("@general",after.Notifications.GeneralChat),("@keys",JsonSerializer.Serialize(after.Keywords,Json)),
                ("@background",after.TransparentBackground),("@version",after.Version),("@p",player),("@r",Realm));
            await q.ExecuteNonQueryAsync(ct);await OperationAudit.Record(db,tx,"chat.preferences.change","arena_player",player,"realm",Realm,null,ct);
        }
        await SaveReceipt(db,tx,"channel_preferences_operation",player,request,fingerprint,after,ct);await tx.CommitAsync(ct);return after;
    }
    internal async Task<ChatMessage> Send(string player,int channel,ChatSend input,CancellationToken ct)
    {
        Channel(channel);string request=Id(input.RequestId);string? text=null,sticker=null;
        if(input.Kind=="text"&&string.IsNullOrEmpty(input.StickerId))text=Text(input.Text,240,false,"invalid_message");
        else if(input.Kind=="sticker"&&string.IsNullOrEmpty(input.Text)&&input.StickerId is {Length:>0 and <=48} key&&ArenaChatStickers.Contains(key))sticker=key;
        else throw new SocialProblem(400,"invalid_message");
        input=input with{RequestId=request,Text=text,StickerId=sticker};byte[] fingerprint=Fingerprint(new{channel,input});
        await using var db=await Open(connectionString,ct);await using var tx=await db.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);await LockOwner(db,tx,player,ct);
        if(await Receipt<ChatMessage>(db,tx,"channel_chat_operation",player,request,fingerprint,ct) is {} saved){await tx.CommitAsync(ct);return saved;}
        await LockOrder(db,tx,channel,ct);
        await EnsurePreferences(db,tx,player,ct);DateTime now=DateTime.UtcNow;
        using(var q=Sql(db,tx,"""
            SELECT (last_sent_at IS NOT NULL AND last_sent_at>@recent)
                OR (SELECT COUNT(*) FROM channel_chat_message WHERE player_id=@p AND realm_id=@r AND kind IN ('text','sticker') AND created_at>@minute)>=30
            FROM channel_chat_preferences WHERE player_id=@p AND realm_id=@r
            """,("@p",player),("@r",Realm),("@recent",now.AddMilliseconds(-500)),("@minute",now.AddMinutes(-1))))
            if(Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0)throw new SocialProblem(429,"rate_limited");
        ChatSender sender;
        using(var q=Sql(db,tx,"SELECT display_name,avatar_id FROM arena_player WHERE id=@p",("@p",player)))
        await using(var r=await q.ExecuteReaderAsync(ct)){await r.ReadAsync(ct);sender=new(player,r.GetString(0),r.GetString(1));}
        long id;
        using(var q=Sql(db,tx,"INSERT INTO channel_chat_message(realm_id,channel_no,player_id,kind,body,sticker_id,created_at) VALUES(@r,@channel,@p,@kind,@text,@sticker,@now)",
            ("@r",Realm),("@channel",channel),("@p",player),("@kind",input.Kind),("@text",text),("@sticker",sticker),("@now",now)))
        {await q.ExecuteNonQueryAsync(ct);id=q.LastInsertedId;}
        using(var q=Sql(db,tx,"UPDATE channel_chat_preferences SET last_sent_at=@now WHERE player_id=@p AND realm_id=@r",("@now",now),("@p",player),("@r",Realm)))await q.ExecuteNonQueryAsync(ct);
        var result=new ChatMessage(id.ToString(CultureInfo.InvariantCulture),channel,sender,input.Kind!,text,sticker,now);
        await SaveReceipt(db,tx,"channel_chat_operation",player,request,fingerprint,result,ct);
        await OperationAudit.Record(db,tx,"chat.message.send","arena_player",player,"chat_message",result.Id,null,ct);
        await tx.CommitAsync(ct);events.Publish(Realm,channel);return result;
    }
    internal static async Task LockOrder(MySqlConnection db, MySqlTransaction tx, int channel, CancellationToken ct)
    {
        // Hold a database X lock from before id allocation until commit. Owner
        // locks alone cannot stop another player's later id committing first.
        // ON DUPLICATE UPDATE avoids INSERT IGNORE's shared-lock upgrade race.
        using(var order=Sql(db,tx,"INSERT INTO channel_chat_order(realm_id,channel_no) VALUES(@r,@channel) ON DUPLICATE KEY UPDATE channel_no=@channel",("@r",Realm),("@channel",channel)))
            await order.ExecuteNonQueryAsync(ct);
    }

    internal async Task<ChatPage> Messages(int channel,long after,int waitMs,CancellationToken ct)
    {
        Channel(channel);if(after<0||waitMs is <0 or >8000)throw new SocialProblem(400,"invalid_message_cursor");
        // Subscribe before reading, so a commit between initial read and wait is retained.
        using var subscription=events.Subscribe(Realm,channel);var page=await Page(channel,after,ct);
        if(page.Messages.Count!=0||waitMs==0)return page;
        using var wait=CancellationTokenSource.CreateLinkedTokenSource(ct);wait.CancelAfter(waitMs);
        try{await subscription.Wait(wait.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){}
        ct.ThrowIfCancellationRequested();return await Page(channel,after,ct);
    }
    private async Task<ChatPage> Page(int channel,long after,CancellationToken ct)
    {
        await using var db=await Open(connectionString,ct);int limit=after==0?50:100;
        using var q=Sql(db,null,"""
            SELECT m.id,p.id,p.display_name,p.avatar_id,m.kind,m.body,m.sticker_id,m.created_at,m.template_id
            FROM channel_chat_message m JOIN arena_player p ON p.id=m.player_id
            WHERE m.realm_id=@r AND m.channel_no=@channel
            """+(after==0?" ORDER BY m.id DESC LIMIT 51":" AND m.id>@after ORDER BY m.id ASC LIMIT 101"),
            ("@r",Realm),("@channel",channel),("@after",after));
        var list=new List<ChatMessage>();await using(var r=await q.ExecuteReaderAsync(ct))
            while(await r.ReadAsync(ct))list.Add(new(r.GetInt64(0).ToString(CultureInfo.InvariantCulture),channel,new(r.GetString(1),r.GetString(2),r.GetString(3)),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),DateTime.SpecifyKind(r.GetDateTime(7),DateTimeKind.Utc),r.IsDBNull(8)?null:r.GetString(8)));
        bool more=list.Count>limit;if(more)list.RemoveAt(limit);if(after==0)list.Reverse();
        return new(channel,list,list.Count==0?after.ToString(CultureInfo.InvariantCulture):list[^1].Id,after>0&&more);
    }
    private static async Task EnsurePreferences(MySqlConnection db,MySqlTransaction tx,string player,CancellationToken ct)
    {
        using var q=Sql(db,tx,"INSERT IGNORE INTO channel_chat_preferences(player_id,realm_id,keywords,updated_at) VALUES(@p,@r,JSON_ARRAY(),UTC_TIMESTAMP(6))",("@p",player),("@r",Realm));await q.ExecuteNonQueryAsync(ct);
    }
    private static async Task<ChatPreferences> ReadPreferences(MySqlConnection db,MySqlTransaction? tx,string player,CancellationToken ct)
    {
        using var q=Sql(db,tx,"SELECT revision,favorite_channel,hero5,artifact5,guild_promotion,general_chat,keywords,transparent_background FROM channel_chat_preferences WHERE player_id=@p AND realm_id=@r",("@p",player),("@r",Realm));
        await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return new(0,null,new(true,true,true,true),[],false);
        return new(r.GetInt64(0),r.IsDBNull(1)?null:r.GetInt32(1),new(r.GetBoolean(2),r.GetBoolean(3),r.GetBoolean(4),r.GetBoolean(5)),JsonSerializer.Deserialize<string[]>(r.GetString(6),Json)??[],r.GetBoolean(7));
    }
}
