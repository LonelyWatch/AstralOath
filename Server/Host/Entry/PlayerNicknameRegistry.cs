using System.Text;
using MySqlConnector;

namespace AstralOath.Server.Entry;

// Identity naming is shared by entry creation and profile editing. Every caller
// holds this key's database row until its identity transaction commits.
internal static class PlayerNicknameRegistry
{
    // Legacy guests had no realm row. Only identities with no explicit realm
    // evidence retain that compatibility; an explicitly foreign identity does not.
    internal const string RealmMembershipPredicate="""
        (EXISTS(SELECT 1 FROM player_realm_membership m WHERE m.player_id=p.id AND m.realm_id=@r)
         OR EXISTS(SELECT 1 FROM inventory_capacity i WHERE i.player_id=p.id AND i.realm_id=@r)
         OR (NOT EXISTS(SELECT 1 FROM entry_account a WHERE a.player_id=p.id)
             AND NOT EXISTS(SELECT 1 FROM player_realm_membership m WHERE m.player_id=p.id)
             AND NOT EXISTS(SELECT 1 FROM inventory_capacity i WHERE i.player_id=p.id)))
        """;
    internal static string Normalize(string value)=>value.Trim().Normalize(NormalizationForm.FormC);
    internal static string Validate(string? input)
    {
        if(input is null||input.Length>48)throw new EntryProblem(400,"invalid_nickname");
        string name=Normalize(input);var runes=name.EnumerateRunes().ToArray();
        bool latin=runes.All(r=>r.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        if((latin ? runes.Length is <4 or >12 : runes.Length is <2 or >8)||
            (!latin&&(!runes.Any(IsCjk)||runes.Any(r=>!IsCjk(r)&&r.Value is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')))))
            throw new EntryProblem(400,"invalid_nickname");
        return name;
    }
    private static bool IsCjk(Rune r)=>r.Value is >=0x3400 and <=0x4dbf or >=0x4e00 and <=0x9fff or >=0xf900 and <=0xfaff or >=0x20000 and <=0x2fa1f or >=0x30000 and <=0x323af;
    private static MySqlCommand Sql(MySqlConnection db,MySqlTransaction tx,string sql,string name,string? player=null)
    {
        var q=new MySqlCommand(sql,db,tx);q.Parameters.AddWithValue("@r",EntryEndpoints.RealmId);
        q.Parameters.AddWithValue("@name",Normalize(name));q.Parameters.AddWithValue("@p",player);return q;
    }
    private static async Task Lock(MySqlConnection db,MySqlTransaction tx,string name,CancellationToken ct)
    {
        using(var add=Sql(db,tx,"INSERT IGNORE INTO entry_nickname_key(realm_id,name_key) VALUES(@r,@name)",name))await add.ExecuteNonQueryAsync(ct);
        using var q=Sql(db,tx,"SELECT name_key FROM entry_nickname_key WHERE realm_id=@r AND name_key=@name FOR UPDATE",name);
        await q.ExecuteScalarAsync(ct);
    }
    internal static async Task<string> Initial(MySqlConnection db,MySqlTransaction tx,string name,string player,bool allowDefault,CancellationToken ct)
    {
        await Lock(db,tx,name,ct);
        using var q=Sql(db,tx,"SELECT owner_player_id FROM entry_nickname_key WHERE realm_id=@r AND name_key=@name AND claimed=TRUE AND owner_player_id IS NOT NULL AND owner_player_id<>@p",name,player);
        if(await q.ExecuteScalarAsync(ct) is null)return name;
        if(!allowDefault)throw new EntryProblem(409,"nickname_unavailable");
        // This 13-character system name is outside the editable nickname range.
        string fallback="冒险者"+player[..10];await Lock(db,tx,fallback,ct);return fallback;
    }
    internal static async Task<bool> Available(MySqlConnection db,MySqlTransaction? tx,string name,string player,CancellationToken ct)
    {
        using var q=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM entry_nickname_key WHERE realm_id=@r AND name_key=@name
                           AND claimed=TRUE AND owner_player_id IS NOT NULL AND owner_player_id<>@p)
                OR EXISTS(SELECT 1 FROM arena_player p WHERE p.display_name=@name AND p.id<>@p AND
            """+RealmMembershipPredicate+");",db,tx);
        q.Parameters.AddWithValue("@r",EntryEndpoints.RealmId);q.Parameters.AddWithValue("@name",name);q.Parameters.AddWithValue("@p",player);
        return Convert.ToInt32(await q.ExecuteScalarAsync(ct))==0;
    }
    internal static async Task<bool> InRealm(MySqlConnection db,string player,CancellationToken ct)
    {
        using var q=new MySqlCommand("SELECT EXISTS(SELECT 1 FROM arena_player p WHERE p.id=@p AND "+RealmMembershipPredicate+")",db);
        q.Parameters.AddWithValue("@p",player);q.Parameters.AddWithValue("@r",EntryEndpoints.RealmId);
        return Convert.ToInt32(await q.ExecuteScalarAsync(ct))!=0;
    }
    internal static async Task Claim(MySqlConnection db,MySqlTransaction tx,string name,string oldName,string player,CancellationToken ct)
    {
        // All identity creators take the same target key, including unclaimed
        // legacy names; a concurrent create cannot slip between check and claim.
        await Lock(db,tx,name,ct);
        if(!await Available(db,tx,name,player,ct))throw new EntryProblem(409,"nickname_unavailable");
        // Retain unclaimed old-name rows as stable lock keys; never delete keys
        // while another transaction may be waiting for their lock.
        using(var release=Sql(db,tx,"UPDATE entry_nickname_key SET owner_player_id=NULL,claimed=FALSE WHERE realm_id=@r AND owner_player_id=@p",oldName,player))await release.ExecuteNonQueryAsync(ct);
        using var claim=Sql(db,tx,"UPDATE entry_nickname_key SET owner_player_id=@p,claimed=TRUE WHERE realm_id=@r AND name_key=@name",name,player);
        await claim.ExecuteNonQueryAsync(ct);
    }
}
