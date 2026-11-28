using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using AstralOath.Client.Shop;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        private HttpClient exchangeHttp;
        private HomePagePreparation.Resource<ArenaTower> towerView;
        private HomePagePreparation.Resource<ArenaDraftArt> towerArt;
        private HomePagePreparation.Resource<ShopWindow> shopView;
        private readonly ShopExchangeRequests shopRequests = new ShopExchangeRequests();
        private bool returnToTower;
        private bool startingTower;

        private void InstallTowerShop()
        {
            views.Register<ArenaTower>();
            views.Register<ShopWindow>();
            towerView = new HomePagePreparation.Resource<ArenaTower>(() => views.CreateAsync<ArenaTower>(assets,parent,lifetime));
            towerArt = new HomePagePreparation.Resource<ArenaDraftArt>(() => assets.LoadAsync<ArenaDraftArt>(ArenaDraftArt.Address,lifetime));
            shopView = new HomePagePreparation.Resource<ShopWindow>(() => views.CreateAsync<ShopWindow>(assets,parent,lifetime));
            exchangeHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, UseProxy=!endpoint.IsLoopback })
                { MaxResponseContentBufferSize=512*1024 };
            lifetime.Add(exchangeHttp.Dispose);
            home.OpenShop = ct => OpenArenaShop(ct,ShowHome);
            directory.OpenTower = OpenTower;
            directory.OpenRecords = ct => ShowFeatureNoticeAsync(locale.Resolve("tower.record"),SocialLabels.Text("unavailable"),ct);
        }

        private async Task OpenTower(CancellationToken request)
        {
            var view = await towerView.Get();
            var art = await towerArt.Get();
            request.ThrowIfCancellationRequested();
            var byId = art.Heroes.ToDictionary(x=>x.Id);
            await ShowPageWithMail(scope =>
            {
                var binder = new ArenaTowerBinder(view,locale,windows,
                    ct => ReadFeature<TowerPageDto>("/api/arena/tower",null,ct),
                    (stage,ct) => ReadFeature<TowerTeamDto>("/api/arena/tower/stages/"+stage+"/clear-team",null,ct),
                    async ct => (await firstReads.ReadHeroesAsync(ct)).heroes.Select(x=>new TowerHeroDto
                        { instanceId=x.instanceId,heroId=x.templateId,level=x.level,stars=x.stars }).ToArray(),
                    StartTowerBattle, id=>byId.TryGetValue(id,out var hero)?hero.Face:null,
                    ShowHome, ReturnToDirectory, () => OpenArenaShop(scope.Token,()=>OpenTower(token)),
                    title=>ObserveSocial(ShowFeatureNoticeAsync(title,SocialLabels.Text("unavailable"),scope.Token)),
                    id=>byId.TryGetValue(id,out var hero)?hero.Name:id, art);
                return binder.OpenAsync(scope);
            });
        }

        [Serializable,Preserve] private sealed class ChallengeInput { public string requestId; public string[] heroInstanceIds; }
        [Serializable,Preserve] private sealed class ShopInput { public string requestId,offerId; }
        [Serializable,Preserve] private sealed class FailureCode { public string code; }

        private async Task StartTowerBattle(int stage,string[] heroes,CancellationToken ct)
        {
            if (startingTower || BattleOnlineLaunch.IsActive) return;
            startingTower = true;
            try
            {
                var bearer = await socialBearer(ct);
                ct.ThrowIfCancellationRequested();
                var pending = TowerChallengeRequest.Resume(endpoint, api.Player?.id ?? loginPlayerId, stage, heroes);
                var body = JsonUtility.ToJson(new ChallengeInput {requestId = pending.Id, heroInstanceIds = heroes});
                ArtifactTrialInvitation invitation;
                try
                {
                    invitation = await ReadFeature<ArtifactTrialInvitation>("/api/arena/tower/stages/"+stage+"/challenge",body,ct);
                }
                catch (IOException error) when (TowerChallengeRequest.IsRejected(error.Message))
                {
                    pending.Complete();
                    throw;
                }
                var art = await pages.Artifacts.Get();
                ct.ThrowIfCancellationRequested();
                await BattleOnlineLaunch.LoadOwnedTrialAsync(assets,scenes,endpoint,invitation.roomId,
                    invitation.opening.snapshot.battleId,invitation.contentHash,invitation.battleContentHash,
                    bearer,ct,id=>{ var item=art.Find(id);return(item?.template.Name,item?.combatIcon); });
                returnToTower = true;
                pending.Complete();
            }
            finally { startingTower = false; }
        }

        private async Task OpenArenaShop(CancellationToken request,Func<Task> back)
        {
            var view=await shopView.Get();
            request.ThrowIfCancellationRequested();
            await ShowPageWithMail(scope =>
            {
                var binder=new ShopWindowBinder(view,locale,windows,shopRequests,
                    ct => ReadFeature<ShopPageDto>("/api/shop/arena",null,ct),
                    async (offer,id,ct) =>
                    {
                        var result=await ReadFeature<ShopPageDto>("/api/shop/arena/exchanges",
                            JsonUtility.ToJson(new ShopInput{offerId=offer,requestId=id}),ct);
                        firstReads.ReconcileEquipment();
                        return result;
                    },back);
                binder.Navigate=(key,ct) => key switch
                {
                    "more" => OpenMore(ct), "bag" => OpenInventory(false,ct),
                    _ => ShowFeatureNoticeAsync(locale.Resolve("shop.nav."+key),SocialLabels.Text("unavailable"),ct)
                };
                void Wallet(AstralOath.Client.Mail.MailSummary summary) =>
                    binder.ShowCurrencies(summary?.balances?.gold,summary?.balances?.crystals);
                Wallet(mailSession?.Summary);
                if(mailSession!=null)
                {
                    mailSession.Changed+=Wallet;
                    scope.Add(()=>mailSession.Changed-=Wallet);
                }
                return binder.OpenAsync(scope);
            });
        }

        private async Task<T> ReadFeature<T>(string path,string json,CancellationToken ct) where T:class
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct,token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using var req=new HttpRequestMessage(json==null?HttpMethod.Get:HttpMethod.Post,new Uri(endpoint,path));
                req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await socialBearer(deadline.Token));
                if(json!=null) req.Content=new StringContent(json,Encoding.UTF8,"application/json");
                using var response=await exchangeHttp.SendAsync(req,deadline.Token);
                var content=await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                if(!response.IsSuccessStatusCode)
                {
                    string code=null;
                    try { code=JsonUtility.FromJson<FailureCode>(content)?.code; } catch(ArgumentException) { }
                    throw new IOException(code??"request_failed");
                }
                return JsonUtility.FromJson<T>(content)??throw new InvalidDataException("Empty server response.");
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested && !token.IsCancellationRequested)
            {throw new IOException("request_timeout");}
            catch(HttpRequestException ex) {throw new IOException("network_unavailable",ex);}
        }
    }
}
