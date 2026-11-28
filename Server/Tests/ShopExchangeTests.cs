using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using AstralOath.Server.Shop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class ShopExchangeTests
{
    private const string Route="/api/shop/arena";
    private static string Id()=>Guid.NewGuid().ToString("N");

    [Fact]
    public void DailyAndWeeklyQuotasResetAtShanghaiMidnight()
    {
        var sunday=new DateTimeOffset(2026,11,29,15,59,59,TimeSpan.Zero);
        Assert.Equal("2026-11-29",ShopCatalog.PeriodKey("day",sunday));
        Assert.Equal("2026-11-23",ShopCatalog.PeriodKey("week",sunday));
        Assert.Equal("2026-11-30",ShopCatalog.PeriodKey("day",sunday.AddSeconds(1)));
        Assert.Equal("2026-11-30",ShopCatalog.PeriodKey("week",sunday.AddSeconds(1)));
    }

    [MySqlFact]
    public async Task PurchasesReachRealInventoryAndDeduplicateUnderContention()
    {
        string connection=Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct=deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection,ct);
        using var factory=new WebApplicationFactory<Program>();
        using var app=factory.WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>
            c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=connection,["Entry:AllowLegacyArenaGuests"]="true"})));
        using var http=app.CreateClient();
        using(var anonymous=await http.GetAsync(Route,ct)) Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        string player=await InventoryTests.OpenGuest(http,"shop_"+Id()[..12],ct);
        await using var db=new MySqlConnection(connection);
        await db.OpenAsync(ct);
        try
        {
            var initial=await Page();
            var purchasable=initial.GetProperty("offers").EnumerateArray().Where(x=>x.GetProperty("price").GetInt32()>0).ToArray();
            Assert.Equal(6,purchasable.Length);
            var expected=purchasable.ToDictionary(x=>x.GetProperty("id").GetString()!,x=>x.GetProperty("owned").GetInt32());
            using(var broke=await Buy("gear1",Id())) await Error(broke,"insufficient_conquest");
            using(var seed=new MySqlCommand("INSERT INTO arena_tower_wallet(player_id,realm_id,currency_id,balance) VALUES(@p,'undying-memory','conquest_point',1000)",db))
            {
                seed.Parameters.AddWithValue("@p",player);
                await seed.ExecuteNonQueryAsync(ct);
            }
            int balance=1000;
            foreach (var row in purchasable)
            {
                string offer=row.GetProperty("id").GetString()!,request=Id();
                var results=await Task.WhenAll(Buy(offer,request),Buy(offer,request));
                balance-=row.GetProperty("price").GetInt32();
                expected[offer]+=row.GetProperty("quantity").GetInt32();
                foreach(var response in results)
                {
                    using(response)
                    {
                        response.EnsureSuccessStatusCode();
                        var receipt=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                        Assert.Equal(balance,receipt.GetProperty("balance").GetInt32());
                        Assert.Equal(expected[offer],Offer(receipt,offer).GetProperty("owned").GetInt32());
                    }
                }
                using var clash=await Buy(offer=="gear1"?"gear2":"gear1",request);
                await Error(clash,"request_conflict");
            }
            using(var exhausted=await Buy("molagora",Id()))await Error(exhausted,"shop_limit");
            var contenders=await Task.WhenAll(Enumerable.Range(0,4).Select(i=>Buy("energy",Id())));
            try
            {
                Assert.Equal(2,contenders.Count(r=>r.IsSuccessStatusCode));
                foreach(var r in contenders.Where(r=>!r.IsSuccessStatusCode)) await Error(r,"shop_limit");
            }
            finally{foreach(var r in contenders)r.Dispose();}
            using(var directory=await Buy("season0",Id())) Assert.Equal(HttpStatusCode.BadRequest,directory.StatusCode);
            var after=await Page();
            Assert.Equal(balance-60,after.GetProperty("balance").GetInt32());
            Assert.Equal(0,Offer(after,"energy").GetProperty("remaining").GetInt32());
            Assert.Equal(0,Offer(after,"molagora").GetProperty("remaining").GetInt32());
            var bag=await http.GetFromJsonAsync<JsonElement>("/api/inventory/equipment",ct);
            var stones=bag.GetProperty("materials").GetProperty("items").EnumerateArray().ToDictionary(
                x=>x.GetProperty("kind").GetString()!,x=>x.GetProperty("quantity").GetInt32());
            foreach(var id in new[]{"gear1","gear2","jewel1","jewel2"})
                Assert.Equal(expected[id],stones[Offer(after,id).GetProperty("material").GetString()!]);
            var resources=await http.GetFromJsonAsync<JsonElement>("/api/development/gm",ct);
            Assert.Equal(expected["energy"]+80,resources.GetProperty("resources").EnumerateArray().Single(x=>x.GetProperty("key").GetString()=="energy").GetProperty("value").GetInt32());
            using var count=new MySqlCommand("SELECT COUNT(*) FROM shop_exchange WHERE player_id=@p",db);
            count.Parameters.AddWithValue("@p",player);
            Assert.Equal(8,Convert.ToInt32(await count.ExecuteScalarAsync(ct)));
        }
        finally
        {
            using var cleanup=new MySqlCommand("DELETE FROM arena_player WHERE id=@p",db);
            cleanup.Parameters.AddWithValue("@p",player);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
        async Task<JsonElement> Page()=>await http.GetFromJsonAsync<JsonElement>(Route,ct);
        Task<HttpResponseMessage> Buy(string offer,string request)=>http.PostAsJsonAsync(Route+"/exchanges",new {offerId=offer,requestId=request},ct);
        static JsonElement Offer(JsonElement p,string id)=>p.GetProperty("offers").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);
        async Task Error(HttpResponseMessage response,string code)
        {
            Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
            Assert.Equal(code,(await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
        }
    }
}
