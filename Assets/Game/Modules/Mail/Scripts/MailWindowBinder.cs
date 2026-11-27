using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace AstralOath.Client.Mail
{
    public sealed class MailWindowBinder
    {
        private readonly MailWindow view;
        private readonly BindingScope scope;
        private readonly MailSession session;
        private readonly Action close;
        private readonly Func<MailClaimReceipt,CancellationToken,Task> reconcile;
        private readonly WindowCoordinator windows;
        private readonly MailRewardEffects effects;
        private readonly GameToastBinder notice;
        private readonly UiAudioPlayback.OneShots sounds = new UiAudioPlayback.OneShots();
        private readonly List<GameObject> rows = new List<GameObject>();
        private BindingScope rowScope;
        private MailLetter[] letters = Array.Empty<MailLetter>();
        private MailLetter detailLetter;
        private MailClaimReceipt shownReceipt;
        private bool history, loading, submitting, showingCover;
        private int loadGeneration, detailGeneration;
        private CancellationTokenSource detailRead;
        private float pageOpened, layerOpened=-1, singleUntil=-1, singleStarted, summaryAt;
        private CanvasGroup animatedLayer;
        private RectTransform animatedPanel;
        private Vector2 panelOrigin;
        private bool Closed => scope.Token.IsCancellationRequested;
        private T Get<T>(string key) where T:Object => view.Map.Require<T>(key);
        private string L(string key) => MailLabels.Text(key);
        public MailWindowBinder(MailWindow view, BindingScope scope, ILocaleService locale, MailSession session,
            Action close, Func<MailClaimReceipt,CancellationToken,Task> reconcile)
        {
            this.view=view;this.scope=scope;this.session=session;this.close=close;this.reconcile=reconcile;
            effects=new MailRewardEffects(view.Map);
            notice=new GameToastBinder(GameToast.ForInstance(Get<GameObject>("messageChrome")));
            scope.Add(notice.Hide);
            windows=new WindowCoordinator(scope.Token);windows.ShowPageAsync(_=>Task.CompletedTask).GetAwaiter().GetResult();
            scope.Add(windows.Dispose);
            Hook(scope,"back",()=>{if(!windows.TryCloseOverlay())close();});
            Hook(scope,"inbox",()=>{if(!history)return;history=false;CancelDetailRead();windows.TryCloseOverlay();_ = LoadAsync();});
            Hook(scope,"history",()=>{if(history)return;history=true;CancelDetailRead();windows.TryCloseOverlay();_ = LoadAsync();});
            Get<GameButton>("back").FeedbackSound=GameUiFeedback.Sound.PageBack;
            Hook(scope,"claimAll",OpenConfirmation);
            Hook(scope,"sort",()=>Message("sortHint"));
            Get<GameButton>("sort").FeedbackSound=GameUiFeedback.Sound.Silent;
            session.Changed+=OnSummary;session.Invalidated+=OnInvalidated;
            scope.Add(()=>{session.Changed-=OnSummary;session.Invalidated-=OnInvalidated;CancelDetailRead();rowScope?.Dispose();foreach(var row in rows)if(row)Object.Destroy(row);rows.Clear();Get<GameObject>("singleReward").SetActive(false);effects.Stop();sounds.FadeOut();Canvas.willRenderCanvases-=Animate;});
            Canvas.willRenderCanvases+=Animate;
            pageOpened=Time.unscaledTime;
            OnSummary(session.Summary);
        }
        public async Task InitializeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();view.Show();Play("MailOpen");
            bool retryFailed=false;
            try { await session.RetryPendingAsync(scope.Token); }
            catch (OperationCanceledException) when (Closed) { return; }
            catch (Exception error) when (IsRequestError(error)) { retryFailed=true; }
            if(await LoadAsync()&&retryFailed&&!Closed)Message("retry");
        }
        private void Hook(BindingScope owner,string key,UnityAction action)
        {
            var button=Get<GameButton>(key);button.onClick.AddListener(action);
            owner.Add(()=>{if(button)button.onClick.RemoveListener(action);});
        }
        private void OnSummary(MailSummary summary)
        {
            if(Closed || summary==null)return;summaryAt=Time.unscaledTime;
            Get<GameText>("capacity").text=MailLabels.Format("capacity",summary.effectiveCount,summary.capacity);
            if(summary.energy!=null)Get<GameText>("energy").text=MailLabels.Format("energyValue",Number(summary.energy.balance),Number(summary.energy.naturalCap));
            if(summary.balances!=null){Get<GameText>("gold").text=Number(summary.balances.gold);Get<GameText>("crystals").text=Number(summary.balances.crystals);}
            Get<GameButton>("claimAll").interactable=!history && summary.effectiveCount>0 && !submitting;Get<Image>("allArrow").color=new Color(116f/255,220f/255,0,Get<GameButton>("claimAll").interactable?1:.25f);Get<GameText>("claimAllLabel").color=new Color(1,1,1,Get<GameButton>("claimAll").interactable?1:.4f);
        }
        private void OnInvalidated(long version)
        {
            if(!Closed && !submitting && !loading)_=LoadAsync();
        }
        private async Task<bool> LoadAsync()
        {
            if(Closed)return false;
            int generation=++loadGeneration;bool requestedHistory=history;loading=true;
            Message("loading");Get<ScrollRect>("scroll").verticalNormalizedPosition=1;
            try
            {
                var page=await session.ListAsync(requestedHistory,null,scope.Token);
                if(Closed || generation!=loadGeneration)return false;
                if(page==null) {loading=false;_ = LoadAsync();return false;}
                letters=page.items ?? Array.Empty<MailLetter>();RenderRows();Message(null);
                return true;
            }
            catch(OperationCanceledException) when(Closed) { return false; }
            catch(Exception error) when(IsRequestError(error)) { if(!Closed && generation==loadGeneration)Message("error");return false; }
            finally {if(generation==loadGeneration)loading=false;}
        }
        private void RenderRows()
        {
            rowScope?.Dispose();rowScope=new BindingScope(scope.Token);
            foreach(var item in rows){if(item)Object.Destroy(item);}rows.Clear();
            var template=Get<GameObject>("row");
            foreach(var letter in letters)
            {
                var row=Object.Instantiate(template,template.transform.parent,false);row.name="Mail "+letter.deliveryId;row.SetActive(true);rows.Add(row);
                var map=row.GetComponent<BindingMap>();
                map.Require<GameText>("sender").text=letter.senderName ?? "";
                map.Require<GameText>("title").text=letter.title ?? "";
                map.Require<GameObject>("gm").SetActive(letter.letterType=="gm");
                SetArt(map.Require<Image>("avatar"),letter.senderIcon ?? "npc1004_s");
                map.Require<GameText>("timeLabel").text=L(history?"readTime":"remaining");
                map.Require<GameText>("time").text=TimeLabel(letter,history);
                var attachment=letter.attachments?.FirstOrDefault();
                SetArt(map.Require<Image>("icon"),attachment?.icon);
                map.Require<GameText>("quantity").text=attachment!=null&&attachment.quantity>1?Number(attachment.quantity):"";
                var inspect=map.Require<GameButton>("attachment");inspect.gameObject.SetActive(attachment!=null);
                UnityAction inspectAction=()=>OpenTooltip(attachment,null,(RectTransform)inspect.transform);inspect.onClick.AddListener(inspectAction);
                rowScope.Add(()=>{if(inspect)inspect.onClick.RemoveListener(inspectAction);});
                var action=map.Require<GameButton>("action");var caption=map.Require<GameText>("actionLabel");
                bool isGm=letter.letterType=="gm";
                caption.text=L(history?(isGm?"replay":letter.outcome=="expired"?"deleted":"claimed"):"claim");
                action.interactable=!submitting&&(!history||isGm);
                if(history&&!isGm){((Image)action.targetGraphic).sprite=Get<Image>("art__btn_white_r_p").sprite;((Image)action.targetGraphic).color=Color.white;}
                UnityAction click=()=>{if(submitting)return;if(isGm)_=OpenDetailAsync(letter);else if(!history)_=ClaimAsync(letter);};
                action.onClick.AddListener(click);rowScope.Add(()=>{if(action)action.onClick.RemoveListener(click);});
            }
            Get<Image>("emptyIcon").enabled=letters.Length==0;Get<GameText>("empty").text=letters.Length==0?L(history?"emptyHistory":"empty"):"";
            Get<Image>("inboxSelected").enabled=!history;Get<Image>("historySelected").enabled=history;
            Get<GameText>("capacity").gameObject.SetActive(!history);
            Get<GameButton>("claimAll").gameObject.SetActive(!history);
            Get<GameButton>("sort").gameObject.SetActive(!history);
            Get<GameText>("historyHint").gameObject.SetActive(history);Get<GameText>("historyHint").text=L("historyHint");
            OnSummary(session.Summary);
        }
        private void CancelDetailRead()
        {
            detailGeneration++;var previous=detailRead;detailRead=null;previous?.Cancel();previous?.Dispose();
        }
        private async Task OpenDetailAsync(MailLetter letter)
        {
            CancelDetailRead();int opening=detailGeneration,page=loadGeneration;bool requestedHistory=history;
            var request=CancellationTokenSource.CreateLinkedTokenSource(scope.Token);detailRead=request;
            try
            {
                var current=await session.Api.ReadAsync(letter.deliveryId,request.Token);
                if(Closed||opening!=detailGeneration||page!=loadGeneration||requestedHistory!=history)return;detailLetter=current;
                windows.CloseOverlay("detail");
                windows.ShowOverlay("detail",layer=>
                {
                    ShowLayer(layer,"detail","detailPanel");
                    SetArt(Get<Image>("detailAvatar"),current.senderIcon);Get<Image>("detailGm").enabled=current.letterType=="gm";
                    Get<GameText>("detailSender").text=current.senderName;Get<GameText>("detailTime").text=TimeLabel(current,false);
                    Get<GameText>("detailBody").richText=false;Get<GameText>("detailBody").text=current.body;
                    var attachment=current.attachments?.FirstOrDefault();SetArt(Get<Image>("detailIcon"),attachment?.icon);
                    Get<GameButton>("detailAttachment").gameObject.SetActive(attachment!=null);
                    // A nested coordinator retains the letter while its attachment tooltip is open.
                    var children=new WindowCoordinator(layer.Token);children.ShowPageAsync(_=>Task.CompletedTask).GetAwaiter().GetResult();layer.Add(children.Dispose);
                    Hook(layer,"detailClose",()=>windows.CloseOverlay("detail"));
                    Hook(layer,"detailAttachment",()=>OpenTooltip(attachment,children,(RectTransform)Get<GameButton>("detailAttachment").transform));
                    var button=Get<GameButton>("detailClaim");bool active=current.outcome=="active";
                    button.interactable=true;((Image)button.targetGraphic).color=active?Color.white:new Color(1,1,1,.3f);
                    Hook(layer,"detailClaim",()=>{if(submitting)return;if(current.outcome!="active"){Message("already");return;}_=ClaimAsync(current);});
                });
            }
            catch(OperationCanceledException) when(Closed||opening!=detailGeneration) { }
            catch(Exception error) when(IsRequestError(error)) {if(!Closed&&opening==detailGeneration)Message("error");}
            finally {if(detailRead==request){detailRead=null;request.Dispose();}}
        }
        private void OpenTooltip(MailReward attachment,WindowCoordinator parent,RectTransform origin)
        {
            if(Closed||attachment==null)return;
            var coordinator=parent??windows;
            coordinator.ShowOverlay("tooltip",layer=>
            {
                ShowLayer(layer,"tooltip");SetArt(Get<Image>("tooltipIcon"),attachment.icon);
                Get<RectTransform>("tooltipPanel").sizeDelta=new Vector2(480,MailItemDescriptions.DesignHeight(attachment));
                Get<GameText>("tooltipKind").text=MailItemDescriptions.GetKind(attachment);
                Get<GameText>("tooltipName").text=attachment.name;
                Get<GameText>("tooltipBody").text=MailItemDescriptions.GetDescription(attachment);
                bool energy=attachment.kind=="energy"&&session.Summary?.energy!=null;
                Get<GameObject>("tooltipOwnedFrame").SetActive(energy);
                Get<GameText>("tooltipOwned").text=energy?MailItemDescriptions.Owned(session.Summary.energy.balance):"";
                PositionTooltip(origin);
                Hook(layer,"tooltipClose",()=>coordinator.CloseOverlay("tooltip"));
            });
        }
        private void PositionTooltip(RectTransform origin)
        {
            var card=Get<RectTransform>("tooltipPanel");var area=(RectTransform)card.parent;
            Canvas.ForceUpdateCanvases();
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(area,origin);
            var available=area.rect;const float margin=18;
            float scale=Mathf.Min(1,(available.width-2*margin)/card.sizeDelta.x,(available.height-2*margin)/card.sizeDelta.y);
            card.localScale=Vector3.one*Mathf.Max(.01f,scale);
            var size=card.sizeDelta*card.localScale.x;
            card.anchorMin=card.anchorMax=area.pivot;card.pivot=Vector2.one;
            card.anchoredPosition=new Vector2(
                Mathf.Clamp(bounds.min.x-24,available.xMin+size.x+margin,available.xMax-margin),
                Mathf.Clamp(bounds.max.y+24,available.yMin+size.y+margin,available.yMax-margin));
        }
        private void OpenConfirmation()
        {
            if(Closed||submitting||history||session.Summary?.effectiveCount==0)return;
            string[] snapshot=letters.Select(m=>m.deliveryId).ToArray();
            windows.ShowOverlay("confirmation",layer=>
            {
                ShowLayer(layer,"confirmation");
                Hook(layer,"confirmCancel",()=>windows.CloseOverlay("confirmation"));
                Hook(layer,"confirmOk",()=>{if(submitting)return;windows.CloseOverlay("confirmation");_=ClaimAllAsync(snapshot);});
            });
        }
        private async Task ClaimAsync(MailLetter letter)
        {
            await SubmitAsync(()=>session.ClaimAsync(letter.deliveryId,scope.Token),false);
        }
        private async Task ClaimAllAsync(string[] snapshot)
        {
            await SubmitAsync(()=>session.ClaimAllAsync(snapshot,scope.Token),true);
        }
        private async Task SubmitAsync(Func<Task<MailClaimReceipt>> operation,bool bulk)
        {
            if(Closed||submitting)return;submitting=true;Message(null);OnSummary(session.Summary);
            foreach(var row in rows)if(row)row.GetComponent<BindingMap>().Require<GameButton>("action").interactable=false;
            try
            {
                var receipt=await operation();if(Closed)return;
                windows.TryCloseOverlay();
                // Only the authoritative receipt reaches resource reconciliation and presentation.
                if(reconcile!=null)await reconcile(receipt,scope.Token);
                if(Closed)return;
                await LoadAsync();if(Closed)return;
                ShowReceipt(receipt,bulk);
            }
            catch(OperationCanceledException) when(Closed) { }
            catch(MailRequestException error)
            {
                if(!Closed&&await LoadAsync()&&!Closed)Message(error.Code=="no_eligible_mail"?"noEligible":error.Code);
            }
            catch(Exception error) when(IsRequestError(error)) {if(!Closed)Message("retry");}
            finally {submitting=false;if(!Closed){OnSummary(session.Summary);foreach(var row in rows)if(row){var b=row.GetComponent<BindingMap>().Require<GameButton>("action");b.interactable=!history||letters.FirstOrDefault(m=>row.name=="Mail "+m.deliveryId)?.letterType=="gm";}}}
        }
        private void ShowReceipt(MailClaimReceipt receipt,bool bulk)
        {
            shownReceipt=receipt;var rewards=receipt.rewards??Array.Empty<MailReward>();
            if(!bulk&&receipt.presentation=="item"&&rewards.Length==1)
            {
                var reward=rewards[0];SetArt(Get<Image>("singleIcon"),reward.icon);
                Get<GameText>("singleText").text=reward.name;Get<GameText>("singleQuantity").text=Number(reward.quantity);
                Get<GameText>("singleKind").text=L(reward.kind=="energy"?"entryCurrency":reward.kind=="currency"||reward.key=="gold"?"currency":reward.kind=="material"?"enhancementStone":"attachment");
                windows.ShowOverlay("singleReward",toast=>
                {
                    var single=Get<GameObject>("singleReward");single.SetActive(true);single.transform.SetAsLastSibling();single.GetComponent<CanvasGroup>().alpha=1;
                    toast.Add(()=>{if(single)single.SetActive(false);});singleStarted=Time.unscaledTime;singleUntil=singleStarted+64f/30f;
                });
                Play("SingleReward");return;
            }
            windows.ShowOverlay("reward",layer=>
            {
                ShowLayer(layer,"reward");showingCover=receipt.presentation=="gmGift"&&!string.IsNullOrEmpty(receipt.cover);
                Get<GameText>("rewardBody").text=L(bulk?"bulkResult":"bundleResult");
                PresentRewards();effects.Play();Play("BundleReward");Play("RewardTitle");
                Hook(layer,"rewardClose",()=>
                {
                    if(showingCover){showingCover=false;PresentRewards();effects.Play();Play("RewardTitle");}
                    else {effects.Stop();windows.CloseOverlay("reward");}
                });
            });
        }
        private void PresentRewards()
        {
            var rewards=shownReceipt.rewards??Array.Empty<MailReward>();
            Get<RectTransform>("rewardItems").gameObject.SetActive(!showingCover);
            var cover=Get<Image>("rewardCover");Get<GameObject>("rewardCoverCard").SetActive(showingCover);
            if(showingCover)SetArt(cover,"cover_"+shownReceipt.cover);
            int count=Math.Min(rewards.Length,20),columns=Math.Min(count,7),rowCount=Math.Max(1,(count+6)/7);
            float side=count<=7?126f:76f;
            for(int i=0;i<20;i++)
            {
                var tile=Get<GameObject>("rewardTile"+i);tile.SetActive(!showingCover&&i<count);
                if(i>=count)continue;
                var rt=(RectTransform)tile.transform;rt.anchorMin=rt.anchorMax=rt.pivot=Vector2.one*.5f;rt.sizeDelta=Vector2.one*side;
                rt.anchoredPosition=new Vector2((i%7-(columns-1)/2f)*side*1.24f,((rowCount-1)/2f-i/7)*side);
                SetArt(Get<Image>("rewardIcon"+i),rewards[i].icon);
                Get<GameText>("rewardQuantity"+i).text=Number(rewards[i].quantity);
            }
        }
        private void ShowLayer(BindingScope layer,string key,string panel=null)
        {
            var root=Get<GameObject>(key);root.SetActive(true);root.transform.SetAsLastSibling();
            layer.Add(()=>{if(root)root.SetActive(false);if(animatedLayer&&animatedLayer.gameObject==root){if(animatedPanel)animatedPanel.anchoredPosition=panelOrigin;animatedLayer=null;animatedPanel=null;}});
            animatedLayer=root.GetComponent<CanvasGroup>();animatedLayer.alpha=0;layerOpened=Time.unscaledTime;
            animatedPanel=panel==null?null:Get<RectTransform>(panel);if(animatedPanel){panelOrigin=animatedPanel.anchoredPosition;animatedPanel.anchoredPosition=panelOrigin+new Vector2(0,-18);}
        }
        private void Animate()
        {
            if(Closed||!view.Root)return;
            var panel=Get<RectTransform>("detailPanel");var bounds=((RectTransform)view.Root.transform).rect.size;float fit=Mathf.Min(1f,Mathf.Min(bounds.x*.90f/1440f,bounds.y*.84f/765f));panel.localScale=Vector3.one*fit;Get<RectTransform>("balancesFrame").localScale=Vector3.one*Mathf.Min(1f,Mathf.Max(.5f,(bounds.x-700f)/802.5f));
            effects.Update();
            float page=Mathf.Clamp01((Time.unscaledTime-pageOpened)/.20f);view.Root.GetComponent<CanvasGroup>().alpha=Mathf.SmoothStep(0,1,page);
            if(animatedLayer)
            {
                float progress=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-layerOpened)/.16f));animatedLayer.alpha=progress;
                if(animatedPanel)animatedPanel.anchoredPosition=panelOrigin+new Vector2(0,-18*(1-progress));
            }
            if(singleUntil>=0)
            {
                int frame=Mathf.Clamp((int)((Time.unscaledTime-singleStarted)*30),0,63);Get<RawImage>("singleEffect").uvRect=new Rect((frame%4)/4f,1-(frame/4+1)/16f,.25f,1f/16f);
                Get<GameObject>("singleReward").GetComponent<CanvasGroup>().alpha=Mathf.Clamp01((singleUntil-Time.unscaledTime)/.20f);
                if(Time.unscaledTime>=singleUntil){windows.CloseOverlay("singleReward");singleUntil=-1;}
            }
        }
        private void Message(string key)
        {
            if(key==null){notice.Hide();return;}
            Get<GameObject>("messageChrome").transform.SetAsLastSibling();
            notice.Show(L(key),scope);
            if(key!="loading")Play(key=="sortHint"?"UiConfirm":"UiBlocked");
        }
        private void SetArt(Image image,string id)
        {
            if(string.IsNullOrEmpty(id)){image.enabled=false;return;}
            try {image.sprite=Get<Image>("art_"+id).sprite;image.enabled=image.sprite;}
            catch(InvalidOperationException){image.sprite=null;image.enabled=false;}
        }
        private void Play(string name)=>sounds.Play(Get<AudioSource>("audio"),Get<AudioSource>("sound_"+name).clip);
        private static string Number(long value)=>value.ToString("N0",CultureInfo.InvariantCulture);
        private string TimeLabel(MailLetter letter,bool read)
        {
            if(!read&&letter.outcome=="expired")return L("expired");
            if(!DateTimeOffset.TryParse(read?letter.historyAt:letter.expiresAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var time))return "";
            DateTimeOffset now=DateTimeOffset.UtcNow;
            if(DateTimeOffset.TryParse(session.Summary?.serverUtc,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var server))now=server+TimeSpan.FromSeconds(Time.unscaledTime-summaryAt);
            TimeSpan span=read?now-time:time-now;
            if(span.TotalSeconds<=0)return L(read?"now":"expired");
            if(span.TotalDays>=1)return MailLabels.Format(read?"agoDays":"days",(int)Math.Ceiling(span.TotalDays));
            if(span.TotalHours>=1)return MailLabels.Format(read?"agoHours":"hours",(int)Math.Ceiling(span.TotalHours));
            return MailLabels.Format(read?"agoMinutes":"minutes",Math.Max(1,(int)Math.Ceiling(span.TotalMinutes)));
        }
        private static bool IsRequestError(Exception error)=>error is System.IO.IOException||error is HttpRequestException||error is OperationCanceledException;
    }
}
