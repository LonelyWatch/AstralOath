using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

public sealed class PlayerUidTests
{
    [MySqlFact]
    public async Task CollisionRetriesKeepExistingUidAndDatabaseRejectsDuplicatesAndOutOfRangeValues()
    {
        await using var f = await SocialFixture.Create();
        var (client, first) = await f.Guest(); using var owner = client;
        string uid = (await SocialFixture.Get(owner, "/api/profile/me")).GetProperty("uid").GetString()!;
        long number = long.Parse(uid);
        long other = number == 9_999_999_999L ? 1_000_000_000L : number + 1;
        await using var db = new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")); await db.OpenAsync();
        // Hand-selected free value next to the existing mapping, independent of allocator logic.
        using (var free = new MySqlCommand("SELECT COUNT(*) FROM player_public_identity WHERE uid=@uid", db))
        { free.Parameters.AddWithValue("@uid", other); Assert.Equal(0L, Convert.ToInt64(await free.ExecuteScalarAsync())); }
        string second = SocialFixture.Id(); f.Track(second);
        await using (var tx = await db.BeginTransactionAsync())
        {
            await InsertBarePlayer(db, tx, second);
            using var entropy = new Samples(ulong.MaxValue, (ulong)(number - 1_000_000_000L), (ulong)(other - 1_000_000_000L));
            await PlayerPublicIdentity.Assign(db, tx, second, entropy, default);
            await tx.CommitAsync();
        }
        Assert.Equal(1L, await f.Count($"SELECT COUNT(*) FROM player_public_identity WHERE player_id=@p AND uid={other}", second));
        Assert.Equal(1L, await f.Count($"SELECT COUNT(*) FROM player_public_identity WHERE player_id=@p AND uid={number}", first));
        foreach (long invalid in new[] { number, 999_999_999L, 10_000_000_000L })
        {
            using var invalidWrite = new MySqlCommand("UPDATE player_public_identity SET uid=@uid WHERE player_id=@p", db);
            invalidWrite.Parameters.AddWithValue("@uid", invalid); invalidWrite.Parameters.AddWithValue("@p", second);
            await Assert.ThrowsAsync<MySqlException>(() => invalidWrite.ExecuteNonQueryAsync());
        }
        string exhausted = SocialFixture.Id(); f.Track(exhausted);
        await using (var tx = await db.BeginTransactionAsync())
        {
            await InsertBarePlayer(db, tx, exhausted);
            using var entropy = new Samples(Enumerable.Repeat((ulong)(number - 1_000_000_000L), 16).ToArray());
            await Assert.ThrowsAsync<MySqlException>(() => PlayerPublicIdentity.Assign(db, tx, exhausted, entropy, default));
            await tx.RollbackAsync();
        }
        Assert.Equal(0L, await f.Count("SELECT COUNT(*) FROM arena_player WHERE id=@p", exhausted));
    }

    [MySqlFact]
    public async Task InitializingPreUidPlayersIsReentrantAndPreservesProfileReplayIdentity()
    {
        await using var f = await SocialFixture.Create();
        var (client, id) = await f.Guest(); using var owner = client;
        var action = new { requestId = SocialFixture.Id(), expectedVersion = 0, kind = "greeting", greeting = "migration fixture" };
        await SocialFixture.Post(owner, "/api/profile/me/actions", action);
        await using var db = new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")); await db.OpenAsync();
        using (var preUid = new MySqlCommand("DELETE FROM player_public_identity WHERE player_id=@p; UPDATE player_profile_operation SET receipt=JSON_REMOVE(receipt,'$.uid') WHERE player_id=@p", db))
        { preUid.Parameters.AddWithValue("@p", id); await preUid.ExecuteNonQueryAsync(); }
        await PlayerPublicIdentity.InitializeExisting(db, default);
        string uid = (await SocialFixture.Get(owner, "/api/profile/me")).GetProperty("uid").GetString()!;
        await PlayerPublicIdentity.InitializeExisting(db, default);
        Assert.Equal(uid, (await SocialFixture.Get(owner, "/api/profile/me")).GetProperty("uid").GetString());
        Assert.Equal(uid, (await SocialFixture.Post(owner, "/api/profile/me/actions", action)).GetProperty("uid").GetString());
    }

    [MySqlFact]
    public async Task PublicUidIsAStableTenDigitStringAcrossRenameAndHostRestart()
    {
        await using var f = await SocialFixture.Create();
        var (client, id) = await f.Guest(); using var owner = client;
        var profile = await SocialFixture.Get(owner, "/api/profile/me");
        string uid = profile.GetProperty("uid").GetString()!;
        Assert.Matches("^[1-9][0-9]{9}$", uid);
        Assert.NotEqual(id, uid);
        var changed = await SocialFixture.Post(owner, "/api/profile/me/actions", new
        {
            requestId = SocialFixture.Id(), expectedVersion = 0, kind = "nickname", nickname = SocialFixture.Name()
        });
        Assert.Equal(uid, changed.GetProperty("uid").GetString());
        await using var restarted = f.Restart(); using var returned = restarted.CreateClient();
        returned.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        Assert.Equal(uid, (await SocialFixture.Get(returned, "/api/profile/me")).GetProperty("uid").GetString());
        Assert.Equal(1L, await f.Count("SELECT COUNT(*) FROM player_public_identity WHERE player_id=@p AND uid BETWEEN 1000000000 AND 9999999999", id));
    }

    [MySqlFact]
    public async Task UidLookupUsesAuthenticatedRealmScopedProfilePrivacyAndDoesNotAuthenticateTheUid()
    {
        await using var f = await SocialFixture.Create();
        var (ca, id) = await f.Guest(); using var owner = ca;
        var (cb, _) = await f.Guest(); using var viewer = cb;
        await SocialFixture.Post(owner, "/api/profile/me/actions", new
        {
            requestId = SocialFixture.Id(), expectedVersion = 0, kind = "visibility", seasonVisible = false, bestVisible = false
        });
        string uid = (await SocialFixture.Get(owner, "/api/profile/me")).GetProperty("uid").GetString()!;
        var viewed = await SocialFixture.Get(viewer, "/api/profile/uids/" + uid);
        Assert.Equal(id, viewed.GetProperty("playerId").GetString());
        Assert.Equal(uid, viewed.GetProperty("uid").GetString());
        Assert.False(viewed.GetProperty("isSelf").GetBoolean());
        Assert.False(viewed.GetProperty("canEdit").GetBoolean());
        Assert.False(viewed.GetProperty("seasonRecord").GetProperty("visible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, viewed.GetProperty("seasonRecord").GetProperty("data").ValueKind);
        using var anonymous = f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile/uids/" + uid)).StatusCode);
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", uid);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile/me")).StatusCode);
        foreach (string invalid in new[] { "123", "0123456789", "10000000000", "123456789a", "+1234567890", "１２３４５６７８９０" })
            Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync("/api/profile/uids/" + Uri.EscapeDataString(invalid))).StatusCode);
        await using var db = new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST"));
        await db.OpenAsync();
        using var realm = new MySqlCommand("INSERT INTO player_realm_membership(realm_id,player_id,first_entered_at,last_entered_at,registered) VALUES('other-realm',@p,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),FALSE)", db);
        realm.Parameters.AddWithValue("@p", id); await realm.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/profile/uids/" + uid)).StatusCode);
    }

    [MySqlFact]
    public async Task ConcurrentNewIdentitiesGetDistinctPersistedUidsBeforeAnyProfileRead()
    {
        await using var f = await SocialFixture.Create();
        using var client = f.Factory.CreateClient();
        var sessions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SocialFixture.Post(client, "/api/arena/sessions", new { displayName = "uid" + SocialFixture.Id()[..10] })));
        var uids = new HashSet<string>();
        foreach (var session in sessions)
        {
            string id = session.GetProperty("player").GetProperty("id").GetString()!; f.Track(id);
            Assert.Equal(1L, await f.Count("SELECT COUNT(*) FROM player_public_identity WHERE player_id=@p", id));
            using var authenticated = f.Factory.CreateClient();
            authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
            string uid = (await SocialFixture.Get(authenticated, "/api/profile/me")).GetProperty("uid").GetString()!;
            Assert.Matches("^[1-9][0-9]{9}$", uid); Assert.True(uids.Add(uid));
        }
    }

    [MySqlFact]
    public async Task RegisteredIdentityKeepsItsUidAcrossRepeatedRealmEntryAndLogin()
    {
        await using var f = await SocialFixture.Create();
        var store = f.Factory.Services.GetRequiredService<EntryStore>();
        string name = "uid" + SocialFixture.Id()[..12], email = name + "@example.test";
        const string password = "UID isolated account test password";
        var (code, digest) = await store.ReserveCode(email, default); await store.EnableCode(email, digest, default);
        var registered = await store.Register(name, email, password, code, default);
        Assert.Equal(1L, await f.Count("SELECT COUNT(*) FROM entry_account a JOIN player_public_identity u ON u.player_id=a.player_id WHERE a.email=@p", email));
        using var client = f.Factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.Token);
        var session = await SocialFixture.Post(client, "/api/entry/select-realm", new { realmId = "undying-memory", appVersion = "0.1.27" });
        string id = session.GetProperty("player").GetProperty("id").GetString()!; f.Track(id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        try
        {
            string uid = (await SocialFixture.Get(client, "/api/profile/me")).GetProperty("uid").GetString()!;
            var logged = await store.Login(name, false, password, default);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", logged.Token);
            session = await SocialFixture.Post(client, "/api/entry/select-realm", new { realmId = "undying-memory", appVersion = "0.1.27" });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
            Assert.Equal(id, session.GetProperty("player").GetProperty("id").GetString());
            Assert.Equal(uid, (await SocialFixture.Get(client, "/api/profile/me")).GetProperty("uid").GetString());
        }
        finally
        {
            await using var db = new MySqlConnection(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")); await db.OpenAsync();
            using var cleanup = new MySqlCommand("DELETE FROM entry_account WHERE player_id=@p", db);
            cleanup.Parameters.AddWithValue("@p", id); await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task InsertBarePlayer(MySqlConnection db, MySqlTransaction tx, string id)
    {
        using var player = new MySqlCommand("INSERT INTO arena_player(id,token_hash,display_name,avatar_id,created_at) VALUES(@p,RANDOM_BYTES(32),'uid-fixture','c1001',UTC_TIMESTAMP(6))", db, tx);
        player.Parameters.AddWithValue("@p", id); await player.ExecuteNonQueryAsync();
    }

    private sealed class Samples(params ulong[] samples) : RandomNumberGenerator
    {
        private int index;
        public override void GetBytes(byte[] data) => GetBytes(data.AsSpan());
        public override void GetBytes(Span<byte> data)
        {
            Assert.True(index < samples.Length, "Allocator must finish within the provided collision/rejection samples.");
            BinaryPrimitives.WriteUInt64LittleEndian(data, samples[index++]);
        }
    }
}
