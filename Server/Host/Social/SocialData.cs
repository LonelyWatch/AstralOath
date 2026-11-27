using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstralOath.Server.Entry;
using MySqlConnector;

namespace AstralOath.Server.Social;

internal static class SocialData
{
    internal const string Realm = EntryEndpoints.RealmId;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static async Task<MySqlConnection> Open(string? connection, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connection)) throw new SocialProblem(503,"database_unavailable");
        var db = new MySqlConnection(connection);
        try { await db.OpenAsync(ct); return db; }
        catch { await db.DisposeAsync(); throw; }
    }
    internal static MySqlCommand Sql(MySqlConnection db, MySqlTransaction? tx, string sql, params (string,object?)[] parameters)
    {
        var q = new MySqlCommand(sql,db,tx);
        foreach(var (key,value) in parameters)q.Parameters.AddWithValue(key,value??DBNull.Value);
        return q;
    }
    internal static async Task LockOwner(MySqlConnection db, MySqlTransaction tx,string player,CancellationToken ct)
    {
        using var q=Sql(db,tx,"SELECT id FROM arena_player WHERE id=@p FOR UPDATE",("@p",player));
        if(await q.ExecuteScalarAsync(ct) is null)throw new SocialProblem(401,"unauthorized");
    }
    internal static string Id(string? value)
    {
        if(!Guid.TryParseExact(value,"N",out var id)||id==Guid.Empty)throw new SocialProblem(400,"invalid_request_id");
        return id.ToString("N");
    }
    internal static byte[] Fingerprint<T>(T input)=>SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input,Json));
    internal static async Task<T?> Receipt<T>(MySqlConnection db,MySqlTransaction tx,string table,string player,string request,byte[] fingerprint,CancellationToken ct) where T:class
    {
        using var q=Sql(db,tx,$"SELECT fingerprint,receipt FROM {Table(table)} WHERE player_id=@p AND realm_id=@r AND request_id=@id",("@p",player),("@r",Realm),("@id",request));
        await using var read=await q.ExecuteReaderAsync(ct);
        if(!await read.ReadAsync(ct))return null;
        if(!CryptographicOperations.FixedTimeEquals(read.GetFieldValue<byte[]>(0),fingerprint))throw new SocialProblem(409,"request_conflict");
        return JsonSerializer.Deserialize<T>(read.GetString(1),Json)??throw new InvalidDataException("Invalid social receipt.");
    }
    internal static async Task SaveReceipt<T>(MySqlConnection db,MySqlTransaction tx,string table,string player,string request,byte[] fingerprint,T result,CancellationToken ct)
    {
        using var q=Sql(db,tx,$"INSERT INTO {Table(table)}(player_id,realm_id,request_id,fingerprint,receipt) VALUES(@p,@r,@id,@f,@receipt)",("@p",player),("@r",Realm),("@id",request),("@f",fingerprint),("@receipt",JsonSerializer.Serialize(result,Json)));
        await q.ExecuteNonQueryAsync(ct);
    }
    private static string Table(string table)=>table is "player_profile_operation" or "channel_chat_operation" or "channel_preferences_operation" ? table : throw new InvalidOperationException("Invalid receipt table.");
    internal static string Text(string? value,int max,bool empty,string code,bool multiline=false)
    {
        if(value is null||value.Length>max*2+2)throw new SocialProblem(400,code);
        string text=value.Replace("\r\n","\n",StringComparison.Ordinal).Trim();int length=0;
        foreach(var rune in text.EnumerateRunes())
            if(rune==Rune.ReplacementChar||(Rune.IsControl(rune)&&!(multiline&&rune.Value=='\n'))||++length>max)throw new SocialProblem(400,code);
        if(!empty&&length==0)throw new SocialProblem(400,code);
        return text;
    }
    internal static int Channel(int value)=>value is >=1 and <=9999 ? value : throw new SocialProblem(400,"invalid_channel");
}
