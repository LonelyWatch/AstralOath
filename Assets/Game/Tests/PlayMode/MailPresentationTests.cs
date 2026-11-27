#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AstralOath.Client.Tests
{
    // Controlled authority for client interaction/layout only; real DB/native acceptance is separate.
    public sealed class MailPresentationTests
    {
        private sealed class Replies : IMailApi
        {
            public readonly List<MailLetter> inbox=new List<MailLetter>(),history=new List<MailLetter>();
            public int writes; public long version=1;public TaskCompletionSource<MailLetter> readGate;
            public Replies()
            {
                for(int i=0;i<5;i++)
                {
                    bool gm=i>=3;bool gift=i==3;
                    var reward=new MailReward{kind=gift?"bundle":"energy",key=gift?"gm_gift":"energy",name=gift?"邮件礼物箱":"行动力",icon=gift?"gm_gift":"to_stamina",quantity=gift?1:i==4?50:100};
                    inbox.Add(new MailLetter{deliveryId=i.ToString(),letterType=gm?"gm":"standard",senderName=gm?"GM亚卡":"送货员梅露铃",senderIcon=gm?"npc1010_s":"npc1004_s",title=gift?"继承者，特别的礼物到啦":"继承者！午餐时间的礼物到啦",body="亲爱的继承者，请查收随信送来的礼物。\n<color=red>这是纯文本，不解释为标签。</color>",expiresAt=DateTimeOffset.UtcNow.AddDays(6).ToString("O"),deliveredAt=DateTimeOffset.UtcNow.ToString("O"),outcome="active",bulkEligible=!gm,attachments=new[]{reward},rewards=new[]{reward},presentation=gift?"gmGift":"item",cover=gift?"gm_gift":""});
                }
            }
            public MailSummary Summary()=>new MailSummary{version=version,effectiveCount=inbox.Count,capacity=100,serverUtc=DateTimeOffset.UtcNow.ToString("O"),energy=new MailEnergy{balance=2000+(5-inbox.Count)*50,naturalCap=360,revision=version},balances=new MailBalances{revision=version,gold=148608996,crystals=9098}};
            public Task<MailSummary> SummaryAsync(CancellationToken token)=>Task.FromResult(Summary());
            public Task<MailPage> ListAsync(bool h,string c,CancellationToken token)=>Task.FromResult(new MailPage{items=(h?history:inbox).ToArray(),summary=Summary()});
            public Task<MailLetter> ReadAsync(string id,CancellationToken token)=>readGate?.Task??Task.FromResult(inbox.Concat(history).First(m=>m.deliveryId==id));
            public Task<MailClaimReceipt> ClaimAsync(MailClaimRequest r,CancellationToken token)
            {
                writes++;var mail=inbox.First(m=>m.deliveryId==r.deliveryId);inbox.Remove(mail);mail.outcome="claimed";mail.historyAt=DateTimeOffset.UtcNow.ToString("O");history.Insert(0,mail);version++;
                var rewards=mail.presentation=="gmGift"?new[]{new MailReward{name="传说神器强化石",icon="charm_artifact_3",quantity=2},new MailReward{name="誓约书签",icon="to_covenant",quantity=5},new MailReward{name="上级神器强化石",icon="charm_artifact_2",quantity=5}}:mail.rewards;
                return Task.FromResult(new MailClaimReceipt{requestId=r.requestId,claimed=new[]{r.deliveryId},rewards=rewards,summary=Summary(),presentation=mail.presentation,cover=mail.cover});
            }
            public Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest r,CancellationToken token)
            {
                if(!inbox.Any(m=>r.deliveryIds.Contains(m.deliveryId)&&m.bulkEligible))return Task.FromException<MailClaimReceipt>(new MailRequestException(System.Net.HttpStatusCode.Conflict,"no_eligible_mail"));
                writes++;var selected=inbox.Where(m=>r.deliveryIds.Contains(m.deliveryId)&&m.bulkEligible).ToArray();
                foreach(var m in selected){inbox.Remove(m);m.outcome="claimed";m.historyAt=DateTimeOffset.UtcNow.ToString("O");history.Insert(0,m);}version++;
                return Task.FromResult(new MailClaimReceipt{requestId=r.requestId,claimed=selected.Select(m=>m.deliveryId).ToArray(),rewards=new[]{new MailReward{name="行动力",icon="to_stamina",quantity=300}},summary=Summary(),presentation="bundle"});
            }
            public Task WatchAsync(Action<long> changed,CancellationToken token)=>Task.Delay(Timeout.Infinite,token);
            public void Dispose(){}
        }
        private sealed class Assets : IAssetService
        { public Task<T> LoadAsync<T>(string address,BindingScope scope)where T:Object=>Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab")); }
        private sealed class Page : IDisposable
        {
            public readonly GameObject root;public readonly BindingScope scope=new BindingScope(CancellationToken.None);public readonly MailWindow view;public readonly MailWindowBinder binder;
            public Page(MailSession session)
            {
                root=new GameObject("Controlled Mail presentation",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;root.GetComponent<Canvas>().sortingOrder=1000;
                var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=1;
                var registry=new UIViewRegistry();registry.Register<MailWindow>();view=registry.CreateAsync<MailWindow>(new Assets(),root.transform,scope).GetAwaiter().GetResult();
                binder=new MailWindowBinder(view,scope,null,session,()=>{},(_,__)=>Task.CompletedTask);binder.InitializeAsync(scope.Token).GetAwaiter().GetResult();
            }
            public T Get<T>(string key)where T:Object=>view.Map.Require<T>(key);
            public void Click(string key){var b=Get<GameButton>(key);Assert.That(b.interactable&&b.gameObject.activeInHierarchy,Is.True,key);b.onClick.Invoke();}
            public void Row(string id){var child=Get<ScrollRect>("scroll").content.Cast<Transform>().First(t=>t.name=="Mail "+id);child.GetComponent<BindingMap>().Require<GameButton>("action").onClick.Invoke();}
            public void Dispose(){scope.Dispose();Object.Destroy(root);}
        }
        [UnityTest,Timeout(60000)]
        public IEnumerator SortNoticeExpiresAndReplacementGetsItsOwnLifetime()
        {
            var api=new Replies();using var session=new MailSession(api);using var page=new Page(session);
            yield return new WaitForSecondsRealtime(.25f);
            page.Click("sort");Assert.That(page.Get<GameObject>("messageChrome").activeSelf,Is.True);
            yield return Frame(page.root,"notice-visible",1920,1080);
            yield return new WaitForSecondsRealtime(1.2f);
            page.Click("sort");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(page.Get<GameObject>("messageChrome").activeSelf,Is.True,"新消息不能被上一条的计时器关闭");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(page.Get<GameObject>("messageChrome").activeSelf,Is.False,"提示应在两秒后自动关闭");
            yield return Frame(page.root,"notice-dismissed",1920,1080);
        }
        [UnityTest,Timeout(60000)]
        public IEnumerator LateDetailReplyCannotReopenAfterSwitchingTabs()
        {
            var api=new Replies{readGate=new TaskCompletionSource<MailLetter>()};using var session=new MailSession(api);using var page=new Page(session);
            page.Row("3");page.Click("history");api.readGate.SetResult(api.inbox.First(m=>m.deliveryId=="3"));yield return null;
            Assert.That(page.Get<GameObject>("detail").activeSelf,Is.False,"离开页签后旧Read不得重开正文");
        }
        [UnityTest,Timeout(60000)]
        public IEnumerator MixedFiveMailCancelBulkGmGiftEnergyEmptyAndHistory()
        {
            var api=new Replies();using var session=new MailSession(api);using var page=new Page(session);
            yield return new WaitForSecondsRealtime(.25f);yield return Frame(page.root,"inbox5",1920,1080);
            yield return Frame(page.root,"inbox5-4k",3840,2160);yield return Frame(page.root,"inbox5-narrow",1280,960);
            page.Click("claimAll");Assert.That(page.Get<GameObject>("confirmation").activeSelf,Is.True);yield return new WaitForSecondsRealtime(.20f);yield return Frame(page.root,"bulk-confirm",1920,1080);
            yield return Frame(page.root,"bulk-confirm-4k",3840,2160);yield return Frame(page.root,"bulk-confirm-narrow",1280,960);
            page.Click("confirmCancel");Assert.That(api.writes,Is.Zero);Assert.That(session.Summary.effectiveCount,Is.EqualTo(5));
            page.Click("claimAll");page.Click("confirmOk");yield return null;Assert.That(session.Summary.effectiveCount,Is.EqualTo(2));Assert.That(api.writes,Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(.20f);yield return Frame(page.root,"bulk-result2",1920,1080);page.Click("rewardClose");
            page.Click("claimAll");page.Click("confirmOk");yield return null;Assert.That(session.Summary.effectiveCount,Is.EqualTo(2));Assert.That(page.Get<GameText>("message").text,Is.EqualTo(MailLabels.Text("noEligible")));
            page.Row("3");yield return new WaitForSecondsRealtime(.20f);Assert.That(session.Summary.effectiveCount,Is.EqualTo(2));Assert.That(page.Get<GameText>("detailBody").richText,Is.False);yield return Frame(page.root,"gm-detail2",1920,1080);
            page.Click("detailAttachment");yield return new WaitForSecondsRealtime(.20f);Assert.That(page.Get<GameObject>("detail").activeSelf,Is.True);Assert.That(page.Get<GameObject>("tooltip").activeSelf,Is.True);yield return Frame(page.root,"gm-attachment-retains-detail",1920,1080);page.Click("tooltipClose");
            page.Click("detailClaim");yield return new WaitForSecondsRealtime(.20f);Assert.That(session.Summary.effectiveCount,Is.EqualTo(1));Assert.That(api.writes,Is.EqualTo(2));yield return Frame(page.root,"gm-gift-cover",1920,1080);
            page.Click("rewardClose");Assert.That(page.Get<GameObject>("reward").activeSelf,Is.True);Assert.That(api.writes,Is.EqualTo(2),"封面点击不得二次发奖");yield return Frame(page.root,"gm-gift-items",1920,1080);var quantity=page.Get<GameText>("rewardQuantity0");quantity.ForceMeshUpdate();Assert.That(quantity.textInfo.characterCount,Is.GreaterThan(0));Assert.That(quantity.textInfo.characterInfo[0].isVisible,Is.True,"两阶段奖励2/5/5数量必须实际绘出");page.Click("rewardClose");
            page.Row("4");yield return new WaitForSecondsRealtime(.20f);page.Click("detailClaim");yield return null;Assert.That(session.Summary.effectiveCount,Is.Zero);Assert.That(page.Get<GameButton>("claimAll").interactable,Is.False);yield return Sequence(page.root,"single-bluebar");yield return Frame(page.root,"energy-empty0",1920,1080);
            page.Click("history");yield return null;Assert.That(api.history.Count,Is.EqualTo(5));page.Row("3");yield return new WaitForSecondsRealtime(.20f);page.Click("detailClaim");Assert.That(api.writes,Is.EqualTo(3));Assert.That(page.Get<GameText>("message").text,Is.EqualTo(MailLabels.Text("already")));yield return Frame(page.root,"history-gm-replay",1920,1080);
        }
        private static IEnumerator Sequence(GameObject root,string name)
        {
            string folder=Environment.GetEnvironmentVariable("ASTRAL_MAIL_CAPTURE_DIR");if(string.IsNullOrEmpty(folder))yield break;
            int oldRate=Time.captureFramerate;float started=Time.unscaledTime;var times=new System.Text.StringBuilder("frame,elapsedSeconds\n");Time.captureFramerate=30;
            try{for(int i=0;i<70;i++){times.Append(i).Append(',').Append((Time.unscaledTime-started).ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append('\n');yield return Frame(root,name+"-"+i.ToString("D3"),960,540);}}
            finally{Time.captureFramerate=oldRate;File.WriteAllText(Path.Combine(folder,name+"-times.csv"),times.ToString());}
        }
        private static IEnumerator Frame(GameObject root,string name,int width,int height)
        {
            string folder=Environment.GetEnvironmentVariable("ASTRAL_MAIL_CAPTURE_DIR");if(string.IsNullOrEmpty(folder))yield break;Directory.CreateDirectory(folder);
            var canvas=root.GetComponent<Canvas>();var camera=new GameObject("Mail capture camera",typeof(Camera)).GetComponent<Camera>();var target=RenderTexture.GetTemporary(width,height,24);var previous=RenderTexture.active;var frame=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();yield return null;camera.Render();RenderTexture.active=target;frame.ReadPixels(new Rect(0,0,width,height),0,0);frame.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),frame.EncodeToPNG());}
            finally{canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.Destroy(frame);Object.Destroy(camera.gameObject);}
        }
    }
}
#endif
