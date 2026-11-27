using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AstralOath.Client.PlayerProfile
{
    public sealed class PlayerProfileBinder
    {
        private readonly PlayerProfile view;private readonly BindingScope scope;private readonly PlayerProfileSession session;
        private readonly WindowCoordinator windows;private readonly Action close;private readonly Action<string> unavailable;private readonly Action<string> copy;
        private readonly HomeHeroArt art;private readonly HeroPortraitSources sources;private readonly HeroRosterPortrait portrait;private readonly GameToastBinder toast;private readonly Func<string,string> realmName;
        private CancellationTokenSource read,check;private int generation,checkGeneration;private string checkedName;private bool saving,loading;
        private PlayerProfileData current;private HomeHeroArt.Hero shownHero;
        private bool Closed=>scope.Token.IsCancellationRequested;
        private T Get<T>(string key)where T:Object=>view.Map.Require<T>(key);
        private static string L(string key)=>PlayerProfileLabels.Text(key);
        public PlayerProfileBinder(PlayerProfile view,BindingScope scope,PlayerProfileSession session,Action close,Action<string> unavailable,
            HomeHeroArt heroArt=null,HeroPortraitSources portraitSources=null,Action<string> copyId=null,Func<string,string> realmName=null)
        {
            this.view=view;this.scope=scope;this.session=session;this.close=close;this.unavailable=unavailable;art=heroArt;sources=portraitSources;this.realmName=realmName;
            copy=copyId??(value=>GUIUtility.systemCopyBuffer=value);
            portrait=new HeroRosterPortrait(Get<RawImage>("portrait"),1024,true,positionOffset:720,framing:1.07f);
            toast=new GameToastBinder(GameToast.ForInstance(Get<GameObject>("messageChrome")));
            windows=new WindowCoordinator(scope.Token);windows.ShowPageAsync(_=>Task.CompletedTask).GetAwaiter().GetResult();
            scope.Add(windows.Dispose);scope.Add(()=>{++generation;++checkGeneration;Cancel(ref read);Cancel(ref check);session.Changed-=Changed;Canvas.willRenderCanvases-=Tick;portrait.Dispose();toast.Hide();view.Hide();});
            session.Changed+=Changed;Canvas.willRenderCanvases+=Tick;
            Hook(scope,"close",()=>{if(!windows.TryCloseOverlay())close();});Hook(scope,"closeLabelButton",()=>close());
            Hook(scope,"copyId",()=>{if(current==null)return;copy(current.playerId);Notice("copied");});
            Hook(scope,"nickname",()=>OpenEditor("nickname"));Hook(scope,"greeting",()=>OpenEditor("greeting"));
            Hook(scope,"seasonPrivacy",()=>_=SetVisibility(true));Hook(scope,"bestPrivacy",()=>_=SetVisibility(false));
            foreach(string key in new[]{"representative","support","frame","customCard"})
            {string feature=key;Hook(scope,key,()=>{if(CanEdit)unavailable?.Invoke("profile."+feature);});}
            Get<GameObject>("nicknameLayer").SetActive(false);Get<GameObject>("greetingLayer").SetActive(false);
            Get<GameObject>("profileContent").SetActive(false);
        }
        private bool CanEdit=>current!=null&&current.isSelf&&current.canEdit&&!loading&&!saving&&!session.HasPending;
        private void Hook(BindingScope owner,string key,UnityAction callback)
        {var button=Get<GameButton>(key);button.onClick.AddListener(callback);owner.Add(()=>{if(button)button.onClick.RemoveListener(callback);});}
        private void Notice(string key){if(!Closed)toast.Show(L(key),scope);}
        private static void Cancel(ref CancellationTokenSource source){var old=source;source=null;if(old==null)return;old.Cancel();old.Dispose();}
        public async Task OpenAsync(string playerId,CancellationToken token)
        {
            Cancel(ref read);int mine=++generation;current=null;shownHero=null;loading=true;saving=false;portrait.SetVisible(false);
            Get<GameObject>("profileContent").SetActive(false);windows.TryCloseOverlay();view.Show();Notice("loading");
            read=CancellationTokenSource.CreateLinkedTokenSource(scope.Token,token);var reading=read;
            try
            {
                if(string.IsNullOrEmpty(playerId)||session.Self?.playerId==playerId)
                {try{await session.RetryPendingAsync(reading.Token);}catch(Exception error)when(IsRequestError(error)){if(!Closed&&mine==generation)Notice("pending");}}
                var data=await session.ReadAsync(playerId,reading.Token);reading.Token.ThrowIfCancellationRequested();
                if(Closed||mine!=generation)return;current=data;loading=false;Render();
                var hero=Hero(data);if(hero!=null&&sources!=null)await sources.PrepareAsync(hero,reading.Token);
                if(Closed||mine!=generation)return;ShowPortrait(hero);if(session.HasPending)Notice("pending");else toast.Hide();
            }
            catch(OperationCanceledException)when(reading.IsCancellationRequested){}
            catch(Exception error)when(IsRequestError(error)){if(!Closed&&mine==generation){loading=false;Notice("error");}}
        }
        private HomeHeroArt.Hero Hero(PlayerProfileData data)=>art?.heroes?.FirstOrDefault(h=>h.id==(data.representativeHero?.templateId??data.avatarId));
        private void ShowPortrait(HomeHeroArt.Hero hero){if(ReferenceEquals(shownHero,hero))return;shownHero=hero;if(hero==null)portrait.SetVisible(false);else portrait.Show(hero);}
        private void Changed(PlayerProfileData data)
        {if(Closed||current==null||!current.isSelf||current.playerId!=data.playerId||current.realmId!=data.realmId||data.version<current.version)return;current=data;Render();ShowPortrait(Hero(data));}
        private void Render()
        {
            Get<GameObject>("profileContent").SetActive(true);
            Get<GameText>("serverId").text=(realmName?.Invoke(current.realmId)??current.realmId??"")+" | "+(current.playerId??"");
            Get<GameText>("displayName").text=current.displayName??"";
            Get<GameText>("rank").text=current.rank?.available==true&&current.rank.value.HasValue?"Rank. "+current.rank.value.Value:"Rank. —";
            Get<GameText>("greetingText").text=current.greetingIsDefault?L("greetingDefault"):current.greeting??"";
            Record("season",current.seasonRecord);Record("best",current.bestRecord);
            bool own=current.isSelf&&current.canEdit;
            Get<GameObject>("selfActions").SetActive(own);Get<GameButton>("nickname").gameObject.SetActive(own);
            Get<GameButton>("greeting").interactable=CanEdit;Get<Image>("greetingPencil").enabled=own;
            foreach(string prefix in new[]{"season","best"})
            {
                Get<GameButton>(prefix+"Privacy").gameObject.SetActive(own);Get<GameButton>(prefix+"Privacy").interactable=CanEdit;
                bool visible=prefix=="season"?current.visibility?.seasonVisible==true:current.visibility?.bestVisible==true;
                Get<Image>(prefix+"Private").enabled=!visible;Get<Image>(prefix+"Eye").color=visible?Color.white:Color.black;
            }
            foreach(string key in new[]{"nickname","support","representative","frame","customCard"})Get<GameButton>(key).interactable=CanEdit;
            var hero=art?.heroes?.FirstOrDefault(h=>h.id==current.avatarId);Get<Image>("avatar").sprite=hero?.portrait;Get<Image>("avatar").enabled=hero?.portrait;
            // Representative and support are distinct selections. No support authority exists in this contract yet.
            Get<Image>("supportAvatar").sprite=null;Get<Image>("supportAvatar").transform.parent.gameObject.SetActive(false);
        }
        private void Record(string prefix,ProfileRecord record)
        {
            // No authority-backed rank records exist yet. Never turn an unknown payload into a sample bronze badge.
            string text=record?.visible==false?L("private"):L("notParticipated");
            Get<GameText>(prefix+"Arena").text=text;Get<GameText>(prefix+"World").text=text;
        }
        private void OpenEditor(string kind)
        {
            if(!CanEdit)return;
            windows.ShowOverlay(kind,owner=>
            {
                var layer=Get<GameObject>(kind+"Layer");layer.SetActive(true);owner.Add(()=>{layer.SetActive(false);Cancel(ref check);++checkGeneration;});
                Hook(owner,kind+"Cancel",()=>windows.CloseOverlay(kind));
                if(kind=="nickname")
                {
                    var field=Get<TMP_InputField>("nicknameInput");field.SetTextWithoutNotify("");checkedName=null;
                    UnityAction<string> edit=_=>{checkedName=null;++checkGeneration;Cancel(ref check);};field.onValueChanged.AddListener(edit);owner.Add(()=>field.onValueChanged.RemoveListener(edit));
                    Hook(owner,"nicknameCheck",()=>_=CheckNickname(owner));Hook(owner,"nicknameSave",()=>_=SaveNickname(owner));
                }
                else{Get<TMP_InputField>("greetingInput").SetTextWithoutNotify(current.greetingIsDefault?L("greetingDefault"):current.greeting??"");Hook(owner,"greetingSave",()=>_=SaveGreeting(owner));}
            });
        }
        private async Task CheckNickname(BindingScope owner)
        {
            string name=PlayerProfileRules.NormalizeNickname(Get<TMP_InputField>("nicknameInput").text);
            if(!PlayerProfileRules.ValidNickname(name)){Notice("invalid_nickname");return;}
            Cancel(ref check);int mine=++checkGeneration;check=CancellationTokenSource.CreateLinkedTokenSource(owner.Token);var checking=check;
            try
            {
                var result=await session.CheckNicknameAsync(name,checking.Token);
                if(Closed||owner.Token.IsCancellationRequested||mine!=checkGeneration||name!=PlayerProfileRules.NormalizeNickname(Get<TMP_InputField>("nicknameInput").text))return;
                checkedName=result.available?name:null;Notice(result.available?"nickname_available":"nickname_unavailable");
            }
            catch(OperationCanceledException)when(checking.IsCancellationRequested){}
            catch(Exception error)when(IsRequestError(error)){if(!Closed&&!owner.Token.IsCancellationRequested&&mine==checkGeneration)Notice("error");}
        }
        private Task SaveNickname(BindingScope owner)
        {
            string name=PlayerProfileRules.NormalizeNickname(Get<TMP_InputField>("nicknameInput").text);
            if(!PlayerProfileRules.ValidNickname(name)){Notice("invalid_nickname");return Task.CompletedTask;}
            if(name!=checkedName){Notice("check_first");return Task.CompletedTask;}
            return Save(new ProfileActionRequest{kind="nickname",nickname=name},owner,"nickname");
        }
        private Task SaveGreeting(BindingScope owner)
        {
            string value=PlayerProfileRules.NormalizeGreeting(Get<TMP_InputField>("greetingInput").text);
            if(!PlayerProfileRules.ValidGreeting(value)){Notice("invalid_greeting");return Task.CompletedTask;}
            return Save(new ProfileActionRequest{kind="greeting",greeting=value},owner,"greeting");
        }
        private Task SetVisibility(bool season)
        {
            if(!CanEdit)return Task.CompletedTask;
            bool s=current.visibility?.seasonVisible==true,b=current.visibility?.bestVisible==true;
            return Save(new ProfileActionRequest{kind="visibility",seasonVisible=season?!s:s,bestVisible=season?b:!b},scope,null);
        }
        private async Task Save(ProfileActionRequest request,BindingScope owner,string overlay)
        {
            if(!CanEdit)return;int mine=generation;saving=true;Render();
            try
            {
                var result=await session.SubmitAsync(current,request,owner.Token);
                if(Closed||mine!=generation||owner.Token.IsCancellationRequested)return;
                current=result;if(overlay!=null)windows.CloseOverlay(overlay);Notice("saved");
            }
            catch(OperationCanceledException)when(owner.Token.IsCancellationRequested){}
            catch(Exception error)when(IsRequestError(error))
            {
                if(Closed||mine!=generation||owner.Token.IsCancellationRequested)return;
                Notice(session.HasPending?"pending":error is PlayerProfileRequestException e?e.Code:"error");
                if(error is PlayerProfileRequestException conflict&&conflict.Code=="profile_version_conflict")
                {checkedName=null;try{current=await session.ReadAsync(null,scope.Token);}catch(Exception refresh)when(IsRequestError(refresh)||refresh is OperationCanceledException){}}
            }
            finally{if(!Closed&&mine==generation){saving=false;Render();}}
        }
        private void Tick(){if(!Closed)portrait.Tick();}
        private static bool IsRequestError(Exception error)=>error is IOException||error is HttpRequestException||error is OperationCanceledException;
    }
}
