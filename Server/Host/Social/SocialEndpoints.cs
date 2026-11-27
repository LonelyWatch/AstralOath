using System.Globalization;
using AstralOath.Server.Arena;
using AstralOath.Server.Entry;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using static AstralOath.Server.Social.SocialData;

namespace AstralOath.Server.Social;

internal static class SocialEndpoints
{
    internal static IServiceCollection AddSocial(this IServiceCollection services)
    {
        services.AddSingleton<ChannelChatEvents>();
        services.AddSingleton(p=>new PlayerProfileStore(p.GetRequiredService<IConfiguration>().GetConnectionString("MySql")));
        services.AddSingleton(p=>new ChannelChatStore(p.GetRequiredService<IConfiguration>().GetConnectionString("MySql"),p.GetRequiredService<ChannelChatEvents>()));return services;
    }
    internal static void MapSocial(this WebApplication app)
    {
        var api=app.MapGroup("/api");
        api.WithRequestTimeout(TimeSpan.FromSeconds(10));
        api.WithMetadata(new RequestSizeLimitAttribute(4096));
        api.AddEndpointFilter(async(context,next)=>
        {
            var http=context.HttpContext;http.Response.Headers.CacheControl="no-store";
            try
            {
                var auth=http.RequestServices.GetRequiredService<ArenaStore>();var player=await auth.Authenticate(http.Request.Headers.Authorization.ToString(),http.RequestAborted);
                if(player is null)return Problem(401,"unauthorized");
                await using(var db=await Open(http.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("MySql"),http.RequestAborted))
                    if(!await PlayerNicknameRegistry.InRealm(db,player.Id,http.RequestAborted))return Problem(403,"invalid_realm");
                http.Items[typeof(ArenaPlayer)]=player;return await next(context);
            }
            catch(SocialProblem p){return Problem(p.Status,p.Code);}
            catch(EntryProblem p){return Problem(p.Status,p.Code);}
            catch(ArenaProblem p){return Problem(p.Status,p.Code);}
            catch(MySqlException e){app.Logger.LogWarning("Social database operation failed with MySQL error {ErrorNumber}.",e.Number);return Problem(503,"database_unavailable");}
        });
        api.MapGet("/profile/me",async(HttpContext h,PlayerProfileStore s,CancellationToken ct)=>Results.Ok(await s.Read(Player(h),Player(h),ct)));
        api.MapGet("/profile/players/{playerId}",async(string playerId,HttpContext h,PlayerProfileStore s,CancellationToken ct)=>Results.Ok(await s.Read(Player(h),playerId,ct)));
        api.MapPost("/profile/nickname/check",async(NicknameCheck input,HttpContext h,PlayerProfileStore s,CancellationToken ct)=>Results.Ok(await s.Check(Player(h),input,ct)));
        api.MapPost("/profile/me/actions",async(ProfileAction input,HttpContext h,PlayerProfileStore s,CancellationToken ct)=>Results.Ok(await s.Change(Player(h),input,ct)));
        api.MapGet("/chat/preferences",async(HttpContext h,ChannelChatStore s,CancellationToken ct)=>Results.Ok(await s.Preferences(Player(h),ct)));
        api.MapPost("/chat/preferences/actions",async(ChatPreferenceAction input,HttpContext h,ChannelChatStore s,CancellationToken ct)=>Results.Ok(await s.ChangePreferences(Player(h),input,ct)));
        api.MapGet("/chat/channels/{channel:int}/messages",async(int channel,string? after,int? waitMs,ChannelChatStore s,CancellationToken ct)=>
        {
            if(after is not null&&(!long.TryParse(after,NumberStyles.None,CultureInfo.InvariantCulture,out _)||after.Length>19))throw new SocialProblem(400,"invalid_message_cursor");
            return Results.Ok(await s.Messages(channel,after is null?0:long.Parse(after,CultureInfo.InvariantCulture),waitMs??0,ct));
        });
        api.MapPost("/chat/channels/{channel:int}/messages",async(int channel,ChatSend input,HttpContext h,ChannelChatStore s,CancellationToken ct)=>Results.Ok(await s.Send(Player(h),channel,input,ct)));
        api.MapGet("/chat/channels/{channel:int}/events",Stream).DisableRequestTimeout();
    }
    private static string Player(HttpContext h)=>((ArenaPlayer)h.Items[typeof(ArenaPlayer)]!).Id;
    private static IResult Problem(int status,string code)=>Results.Json(new{code},statusCode:status);
    private static async Task<IResult> Stream(int channel,HttpContext http,ArenaStore auth,ChannelChatEvents events,IHostApplicationLifetime lifetime,CancellationToken ct)
    {
        Channel(channel);string player=Player(http);
        using var stopped=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.ApplicationStopping);ct=stopped.Token;
        using var subscription=events.Subscribe(Realm,channel);
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();http.Response.ContentType="application/x-ndjson";http.Response.Headers["X-Accel-Buffering"]="no";
        try
        {
            await Send("sync");long due=Environment.TickCount64+15000;
            while(!ct.IsCancellationRequested)
            {
                int remaining=(int)Math.Max(0,due-Environment.TickCount64);
                if(remaining==0)
                {
                    if((await auth.Authenticate(http.Request.Headers.Authorization.ToString(),ct))?.Id!=player)break;
                    await using(var db=await Open(http.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("MySql"),ct))
                        if(!await PlayerNicknameRegistry.InRealm(db,player,ct))break;
                    await Send("heartbeat");due=Environment.TickCount64+15000;continue;
                }
                using var tick=CancellationTokenSource.CreateLinkedTokenSource(ct);tick.CancelAfter(remaining);
                try{await subscription.Wait(tick.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){continue;}
                await Send("chat");
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        catch(IOException){}
        catch(MySqlException e){http.RequestServices.GetRequiredService<ILogger<ChannelChatStore>>().LogWarning("Chat stream database operation failed with MySQL error {ErrorNumber}.",e.Number);}
        return Results.Empty;
        async Task Send(string kind){await http.Response.WriteAsync("{\"kind\":\""+kind+"\"}\n",ct);await http.Response.Body.FlushAsync(ct);}
    }
}
