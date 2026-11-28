using System.Data;
using System.Globalization;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using MySqlConnector;
using static AstralOath.Server.Social.SocialData;

namespace AstralOath.Server.Social;

internal sealed class PlayerProfileStore(string? connectionString)
{
    internal async Task<PlayerProfileData> Read(string viewer,string target,CancellationToken ct)
    {
        if(!Guid.TryParseExact(target,"N",out var id)||id==Guid.Empty)throw new SocialProblem(404,"profile_not_found");
        await using var db=await Open(connectionString,ct);
        return await Snapshot(db,null,viewer,id.ToString("N"),ct);
    }
    internal async Task<PlayerProfileData> ReadUid(string viewer,string uid,CancellationToken ct)
    {
        if(uid.Length!=10||uid[0]=='0'||uid.Any(c=>c<'0'||c>'9'))throw new SocialProblem(400,"invalid_player_uid");
        await using var db=await Open(connectionString,ct);
        using var query=Sql(db,null,"SELECT player_id FROM player_public_identity WHERE uid=@uid",("@uid",long.Parse(uid,CultureInfo.InvariantCulture)));
        string target=(string?)await query.ExecuteScalarAsync(ct)??throw new SocialProblem(404,"profile_not_found");
        return await Snapshot(db,null,viewer,target,ct);
    }
    internal async Task<object> Check(string player,NicknameCheck input,CancellationToken ct)
    {
        string name=PlayerNicknameRegistry.Validate(input.Nickname);await using var db=await Open(connectionString,ct);
        bool available=await PlayerNicknameRegistry.Available(db,null,name,player,ct);
        return new{available,code=available?"nickname_available":"nickname_unavailable",rulesVersion=1};
    }
    internal async Task<PlayerProfileData> Change(string player,ProfileAction input,CancellationToken ct)
    {
        string request=Id(input.RequestId);
        if(input.ExpectedVersion is null or <0)throw new SocialProblem(400,"invalid_profile_action");
        string? nickname=null,greeting=null;
        switch(input.Kind)
        {
            case "nickname" when input.Greeting is null&&input.SeasonVisible is null&&input.BestVisible is null:
                nickname=PlayerNicknameRegistry.Validate(input.Nickname);break;
            case "greeting" when input.Nickname is null&&input.SeasonVisible is null&&input.BestVisible is null:
                greeting=Text(input.Greeting,50,true,"invalid_greeting",true);break;
            case "visibility" when input.Nickname is null&&input.Greeting is null&&input.SeasonVisible.HasValue&&input.BestVisible.HasValue:break;
            default:throw new SocialProblem(400,"invalid_profile_action");
        }
        input=input with{RequestId=request,Nickname=nickname,Greeting=greeting};var fingerprint=Fingerprint(input);
        await using var db=await Open(connectionString,ct);await using var tx=await db.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        await LockOwner(db,tx,player,ct);
        if(await Receipt<PlayerProfileData>(db,tx,"player_profile_operation",player,request,fingerprint,ct) is {} saved){await tx.CommitAsync(ct);return saved;}
        var before=await Snapshot(db,tx,player,player,ct);
        if(input.ExpectedVersion!=before.Version)throw new SocialProblem(409,"profile_version_conflict");
        bool changed=input.Kind switch{"nickname"=>nickname!=before.DisplayName,"greeting"=>greeting!=before.Greeting,_=>input.SeasonVisible!=before.Visibility.SeasonVisible||input.BestVisible!=before.Visibility.BestVisible};
        if(changed)
        {
            if(nickname is not null)
            {
                await PlayerNicknameRegistry.Claim(db,tx,nickname,before.DisplayName,player,ct);
                using var rename=Sql(db,tx,"UPDATE arena_player SET display_name=@n WHERE id=@p",("@n",nickname),("@p",player));await rename.ExecuteNonQueryAsync(ct);
            }
            using var update=Sql(db,tx,"""
                INSERT INTO player_profile(player_id,realm_id,greeting,season_visible,best_visible,revision,updated_at)
                VALUES(@p,@r,@g,@season,@best,1,UTC_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE greeting=@g,season_visible=@season,best_visible=@best,revision=revision+1,updated_at=UTC_TIMESTAMP(6)
                """,("@p",player),("@r",Realm),("@g",greeting??before.Greeting),("@season",input.SeasonVisible??before.Visibility.SeasonVisible),("@best",input.BestVisible??before.Visibility.BestVisible));
            await update.ExecuteNonQueryAsync(ct);
            await OperationAudit.Record(db,tx,"profile."+input.Kind+".change","arena_player",player,"realm",Realm,null,ct);
        }
        var result=await Snapshot(db,tx,player,player,ct);
        await SaveReceipt(db,tx,"player_profile_operation",player,request,fingerprint,result,ct);await tx.CommitAsync(ct);return result;
    }
    private static async Task<PlayerProfileData> Snapshot(MySqlConnection db,MySqlTransaction? tx,string viewer,string target,CancellationToken ct)
    {
        string name,avatar,greeting,uid;bool season,best;long version;
        using(var q=Sql(db,tx,"""
            SELECT p.display_name,p.avatar_id,COALESCE(s.greeting,''),COALESCE(s.season_visible,TRUE),
                   COALESCE(s.best_visible,TRUE),COALESCE(s.revision,0),u.uid
            FROM arena_player p JOIN player_public_identity u ON u.player_id=p.id
            LEFT JOIN player_profile s ON s.player_id=p.id AND s.realm_id=@r WHERE p.id=@p AND
            """+PlayerNicknameRegistry.RealmMembershipPredicate,("@p",target),("@r",Realm)))
        await using(var r=await q.ExecuteReaderAsync(ct))
        {
            if(!await r.ReadAsync(ct))throw new SocialProblem(404,"profile_not_found");
            name=r.GetString(0);avatar=r.GetString(1);greeting=r.GetString(2);season=r.GetBoolean(3);best=r.GetBoolean(4);version=r.GetInt64(5);
            uid=r.GetInt64(6).ToString(CultureInfo.InvariantCulture);
        }
        ProfileHero? hero=null;
        using(var q=Sql(db,tx,"""
            SELECT instance_id,template_id,level,stars FROM inventory_hero WHERE player_id=@p AND realm_id=@r
            ORDER BY (template_id=@avatar) DESC,acquisition_order LIMIT 1
            """,("@p",target),("@r",Realm),("@avatar",avatar)))
        await using(var r=await q.ExecuteReaderAsync(ct))
            if(await r.ReadAsync(ct))hero=new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3));
        bool self=viewer==target;
        return new(target,uid,Realm,name,avatar,greeting,greeting.Length==0,version,self,self,hero,false,new(season,best),new(),new(false,self||season),new(false,self||best));
    }
}
