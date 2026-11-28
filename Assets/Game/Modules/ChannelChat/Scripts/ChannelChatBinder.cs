using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
using Object=UnityEngine.Object;
namespace AstralOath.Client.ChannelChat
{
    public sealed class ChannelChatBinder
    {
        private sealed class Row:IDisposable
        {
            internal GameObject root;internal BindingMap map;internal BindingScope bindings;
            public void Dispose(){bindings?.Dispose();if(root)Object.Destroy(root);}
        }
        private readonly ChannelChat view;private readonly BindingScope scope;private readonly ChannelChatSession session;
        private readonly Action close;private readonly Action<string> unavailable,playerSelected;private readonly Func<string> ownPlayerId;private readonly Func<string,Sprite> avatarArt;
        private readonly WindowCoordinator windows;private readonly GameToastBinder toast;private readonly Dictionary<string,Row> rows=new Dictionary<string,Row>(StringComparer.Ordinal);
        public Func<string,string,Sprite> RewardIcon { private get; set; }
        public Func<string,string,string> RewardName { private get; set; }
        public Action<string,string> RewardSelected { private get; set; }
        private readonly bool embedded;
        private readonly Dictionary<string,Sprite> stickers=new Dictionary<string,Sprite>(StringComparer.Ordinal);
        private readonly List<GameObject> stickerCells=new List<GameObject>();
        private CancellationTokenSource opening;private bool loading,sending,saving,opened,rendering;private int generation,shownChannel;private float lastWidth=-1,lastHeight=-1;
        private bool Closed=>scope.Token.IsCancellationRequested;
        private T Get<T>(string key)where T:Object=>view.Map.Require<T>(key);
        private static string L(string key)=>ChannelChatLabels.Text(key);
        public ChannelChatBinder(ChannelChat view,BindingScope pageScope,ChannelChatSession loginSession,Action close,Action<string> unavailable,Action<string> playerSelected,
            Func<string> ownPlayerId=null,Func<string,Sprite> avatarArt=null,bool embedded=false)
        {
            this.view=view??throw new ArgumentNullException(nameof(view));scope=pageScope??throw new ArgumentNullException(nameof(pageScope));session=loginSession??throw new ArgumentNullException(nameof(loginSession));
            this.close=close;this.unavailable=unavailable;this.playerSelected=playerSelected;this.ownPlayerId=ownPlayerId;this.avatarArt=avatarArt;this.embedded=embedded;
            windows=new WindowCoordinator(scope.Token);windows.ShowPageAsync(_=>Task.CompletedTask).GetAwaiter().GetResult();toast=new GameToastBinder(GameToast.ForInstance(Get<GameObject>("messageChrome")));
            scope.Add(()=>{opened=false;++generation;CancelOpening();windows.Dispose();session.Changed-=Changed;session.PreferencesChanged-=PreferencesChanged;session.Faulted-=Faulted;session.Uncertain-=Uncertain;Canvas.willRenderCanvases-=Layout;foreach(var row in rows.Values)row.Dispose();rows.Clear();foreach(var cell in stickerCells)if(cell)Object.Destroy(cell);stickerCells.Clear();toast.Hide();view.Hide();});
            session.Changed+=Changed;session.PreferencesChanged+=PreferencesChanged;session.Faulted+=Faulted;session.Uncertain+=Uncertain;Canvas.willRenderCanvases+=Layout;
            foreach(string key in new[]{"close","favoriteClose","channelClose","settingsClose","stickerClose"})Get<GameButton>(key).KeepBrightnessOnPress=true;
            Hook(scope,"close",()=>{if(!TryCloseOverlay())close?.Invoke();});Hook(scope,"publicTab",()=>TryCloseOverlay());Hook(scope,"guildTab",()=>unavailable?.Invoke("chat.guild"));Hook(scope,"notice",()=>unavailable?.Invoke("chat.notices"));
            Hook(scope,"favorite",()=>Editor("favorite"));Hook(scope,"channelSwitch",()=>Editor("channel"));Hook(scope,"opacity",()=>_=Background());Hook(scope,"settings",()=>Settings());Hook(scope,"emoji",()=>Picker());Hook(scope,"send",()=>_=Send(null));
            foreach(string key in new[]{"favoriteLayer","channelLayer","settingsLayer","stickerLayer"})Get<GameObject>(key).SetActive(false);
            foreach(var image in Get<BindingMap>("stickerArt").GetComponentsInChildren<Image>(true))if(image.sprite)stickers.Add(image.sprite.name,image.sprite);
            var history=Get<ScrollRect>("messages");UnityAction<Vector2> read=_=>{if(!rendering)ReadVisible();};history.onValueChanged.AddListener(read);scope.Add(()=>{if(history)history.onValueChanged.RemoveListener(read);});
            if(embedded)
            {
                foreach(string key in new[]{"close","publicTab","guildTab","notice"})Get<GameButton>(key).gameObject.SetActive(false);
                Get<Image>("publicSelected").gameObject.SetActive(false);
                foreach(Transform child in Get<RectTransform>("drawer"))
                    if(child.name=="Original header shadow"||child.name=="Notice collapse arrow"||child.name=="Original gold rule")child.gameObject.SetActive(false);
            }
            BuildStickerCells();RenderPreferences();
        }
        public async Task OpenAsync(CancellationToken token)
        {
            CancelOpening();int mine=++generation;opening=CancellationTokenSource.CreateLinkedTokenSource(scope.Token,token);opening.CancelAfter(TimeSpan.FromSeconds(20));var request=opening;
            opened=true;view.Show();loading=true;Notice("loading");Render();RenderPreferences();
            try
            {
                if(session.HasPendingPreferences)await session.RetryPendingPreferencesAsync(request.Token);
                await Task.WhenAll(session.JoinAsync(session.Channel,request.Token),session.RefreshPreferencesAsync(request.Token));request.Token.ThrowIfCancellationRequested();
                if(Closed||mine!=generation)return;loading=false;Render();RenderPreferences();if(session.HasPendingSend||session.HasPendingPreferences)Notice("pending");else toast.Hide();
            }
            catch(OperationCanceledException)when(request.IsCancellationRequested){if(!Closed&&mine==generation){loading=false;Render();RenderPreferences();if(!token.IsCancellationRequested)Notice(session.HasPendingPreferences?"pending":"error");}}
            catch(Exception error)when(RequestError(error)){if(!Closed&&mine==generation){loading=false;Render();RenderPreferences();Error(error);}}
        }
        private void CancelOpening(){var old=opening;opening=null;if(old==null)return;old.Cancel();old.Dispose();}
        private void Hook(BindingScope owner,string key,UnityAction action){Hook(owner,Get<GameButton>(key),action);}
        private static void Hook(BindingScope owner,GameButton button,UnityAction action)
        {button.onClick.AddListener(action);owner.Add(()=>{if(button)button.onClick.RemoveListener(action);});}
        private void Notice(string key){if(Closed)return;toast.Show(L(key),scope);if(key!="loading")GameUiFeedback.Play(Get<GameText>("channelName"),key=="saved"?GameUiFeedback.Sound.Confirm:GameUiFeedback.Sound.Blocked);}
        private void Faulted(string code){if(!Closed&&opened)Notice(code);}
        private void Uncertain(){if(!Closed&&opened)Notice("pending");}
        private void Error(Exception error)=>Notice(session.HasPendingSend||session.HasPendingPreferences?"pending":error is ChannelChatRequestException reject?reject.Code:"error");
        private void Changed(){if(!Closed&&opened)Render();}
        private void PreferencesChanged(ChatPreferences preferences){if(!Closed&&opened)RenderPreferences();}
        private void Render()
        {
            rendering=true;
            try
            {
            Layout();
            Get<GameText>("channelName").text=string.Format(CultureInfo.InvariantCulture,L("channel"),session.Channel);
            Get<GameText>("entered").text=string.Format(CultureInfo.InvariantCulture,L("entered"),session.Channel);
            Get<GameObject>("enteredStrip").SetActive(session.Messages.Count==0);
            Get<GameButton>("send").interactable=!loading&&!sending;Get<TMP_InputField>("input").interactable=!loading&&!sending&&!session.HasPendingSend;
            Get<GameButton>("emoji").interactable=!loading&&!sending&&!session.HasPendingSend;
            bool channelChanged=shownChannel!=session.Channel;shownChannel=session.Channel;var scroll=Get<ScrollRect>("messages");bool follow=channelChanged||AtBottom(scroll)||rows.Count==0;
            var ids=new HashSet<string>(session.Messages.Select(m=>m.id),StringComparer.Ordinal);
            foreach(string id in rows.Keys.Where(id=>!ids.Contains(id)).ToArray()){rows[id].Dispose();rows.Remove(id);}
            foreach(var message in session.Messages)
            {
                if(!rows.TryGetValue(message.id,out var row))
                {
                    var root=Object.Instantiate(Get<GameObject>("row"),scroll.content,false);root.name="Message "+message.id;root.SetActive(true);row=new Row{root=root,map=root.GetComponent<BindingMap>(),bindings=new BindingScope(scope.Token)};rows.Add(message.id,row);
                    if (message.IsReward)
                        Hook(row.bindings, row.map.Require<GameButton>("rewardButton"), () =>
                        {
                            if (!Closed) RewardSelected?.Invoke(message.kind, message.templateId);
                        });
                    string playerId=message.player?.id;Hook(row.bindings,row.map.Require<GameButton>("avatar"),()=>{if(!Closed&&!string.IsNullOrEmpty(playerId))playerSelected?.Invoke(playerId);});
                }
                row.root.transform.SetAsLastSibling();RenderRow(row,message);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);if(follow)scroll.verticalNormalizedPosition=0;
            }
            finally{rendering=false;}
            ReadVisible();
        }
        private bool TryCloseOverlay()
        {
            bool closed=windows.TryCloseOverlay();
            if(closed)ReadVisible();
            return closed;
        }
        private void CloseOverlay(string key)
        {
            windows.CloseOverlay(key);
            ReadVisible();
        }
        private void ReadVisible()
        {
            if(Closed||!opened||loading||!view.Root.activeInHierarchy||session.Messages.Count==0||!AtBottom(Get<ScrollRect>("messages")))return;
            if(new[]{"favorite","channel","settings","stickers"}.Any(windows.IsOverlayOpen))return;
            session.MarkRead(session.Channel,session.Messages[session.Messages.Count-1].id);
        }
        private static bool AtBottom(ScrollRect list) =>
            list.content.rect.height <= list.viewport.rect.height + .5f || list.verticalNormalizedPosition <= .08f;
        private void RenderRow(Row row,ChatMessage message)
        {
            string own=ownPlayerId?.Invoke();bool self=!string.IsNullOrEmpty(own)&&message.player?.id==own;var name=row.map.Require<GameText>("sender");
            name.text=(message.player?.displayName??"")+(self?L("self"):"");name.richText=false;name.color=self?new Color32(125,204,18,255):Color.white;
            var nameRect=(RectTransform)name.transform.parent;float nameWidth=Mathf.Min(510,name.GetPreferredValues().x+6);nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,nameWidth);
            var time=row.map.Require<GameText>("time");time.text=RelativeTime(message.createdAt);((RectTransform)time.transform.parent).anchoredPosition=new Vector2(108+nameWidth+8,-7.5f);
            var avatar=row.map.Require<Image>("face");avatar.sprite=avatarArt?.Invoke(message.player?.avatarId);avatar.enabled=avatar.sprite;row.map.Require<GameButton>("avatar").interactable=!string.IsNullOrEmpty(message.player?.id);
            bool isSticker=message.kind=="sticker";var body=row.map.Require<GameText>("body");var sticker=row.map.Require<Image>("sticker");body.gameObject.SetActive(!isSticker);sticker.gameObject.SetActive(isSticker);
            row.map.Require<GameObject>("reward").SetActive(message.IsReward);
            float height;
            if (message.IsReward)
            {
                body.gameObject.SetActive(false);
                var icon=row.map.Require<Image>("rewardIcon");
                icon.sprite=RewardIcon?.Invoke(message.kind,message.templateId);
                icon.enabled=icon.sprite;
                row.map.Require<Image>("rewardHeroBase").enabled=message.kind=="hero5";
                row.map.Require<Image>("rewardHeroRing").enabled=message.kind=="hero5";
                var title=RewardName?.Invoke(message.kind,message.templateId)??message.templateId;
                // TMP treats text as markup. Neutralize delimiters in all outside names.
                string Safe(string value)=>(value??"").Replace("<","＜").Replace(">","＞");
                row.map.Require<GameText>("rewardText").text=string.Format(L(message.kind=="hero5"?"reward.hero":"reward.artifact"),Safe(message.player.displayName),Safe(title));
                // The space between entries belongs to the list, not below its final bubble.
                height=message.id==session.Messages[session.Messages.Count-1].id ? 149f : 173f;
            }
            else if(isSticker){stickers.TryGetValue(message.stickerId??"",out var sprite);sticker.sprite=sprite;sticker.enabled=sprite;height=225;}
            else
            {body.text=message.text??"";body.richText=false;float textHeight=Mathf.Max(45,body.GetPreferredValues(body.text,693,0).y);((RectTransform)body.transform.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,textHeight);height=Mathf.Max(108,48+textHeight+13);}
            row.root.GetComponent<LayoutElement>().preferredHeight=height;
        }
        private static string RelativeTime(string value)
        {
            if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date))return "";
            var delta=DateTimeOffset.UtcNow-date;if(delta.TotalMinutes<1)return L("justNow");if(delta.TotalHours<1)return string.Format(CultureInfo.InvariantCulture,L("minutesAgo"),Math.Max(1,(int)delta.TotalMinutes));if(delta.TotalDays<1)return string.Format(CultureInfo.InvariantCulture,L("hoursAgo"),(int)delta.TotalHours);return date.ToLocalTime().ToString("MM-dd HH:mm",CultureInfo.InvariantCulture);
        }
        private async Task Send(string stickerId)
        {
            if(Closed||loading||sending)return;string value=Get<TMP_InputField>("input").text.Trim();
            if(!session.HasPendingSend&&stickerId==null){if(value.Length==0){Notice("empty");return;}if(Scalars(value)>200){Notice("tooLong");return;}}
            if(stickerId!=null&&!stickers.ContainsKey(stickerId)){Notice("unknownSticker");return;}
            sending=true;Render();try
            {
                var result=session.HasPendingSend?await session.RetryPendingSendAsync(scope.Token):await session.SendAsync(new ChatSendRequest{kind=stickerId==null?"text":"sticker",text=stickerId==null?value:null,stickerId=stickerId},scope.Token);
                if(Closed)return;if(result!=null&&result.kind=="text"&&Get<TMP_InputField>("input").text.Trim()==result.text)Get<TMP_InputField>("input").SetTextWithoutNotify("");CloseOverlay("stickers");
            }
            catch(OperationCanceledException)when(Closed){}
            catch(Exception error)when(RequestError(error)){if(!Closed)Error(error);}
            finally{if(!Closed){sending=false;Render();}}
        }
        private void Editor(string kind)
        {
            if(Closed||loading)return;windows.ShowOverlay(kind,owner=>
            {
                var layer=Get<GameObject>(kind+"Layer");layer.SetActive(true);owner.Add(()=>{if(layer)layer.SetActive(false);});
                Get<TMP_InputField>(kind+"Input").SetTextWithoutNotify("");RenderPreferences();
                Hook(owner,kind+"Close",()=>CloseOverlay(kind));Hook(owner,kind+"CloseCaption",()=>CloseOverlay(kind));Hook(owner,kind+"Save",()=>_=SaveChannel(kind,owner));
            });
        }
        private async Task SaveChannel(string kind,BindingScope owner)
        {
            if(saving||loading||Closed)return;if(!int.TryParse(Get<TMP_InputField>(kind+"Input").text,NumberStyles.None,CultureInfo.InvariantCulture,out int channel)||channel<1||channel>9999){Notice("invalidChannel");return;}
            if(kind=="favorite")
            {if(await Preference(new ChatPreferenceRequest{kind="favorite",favoriteChannel=channel},owner.Token)&&!Closed&&!owner.Token.IsCancellationRequested)CloseOverlay(kind);return;}
            loading=true;Render();try{await session.JoinAsync(channel,owner.Token);if(!Closed&&!owner.Token.IsCancellationRequested)CloseOverlay(kind);}
            catch(OperationCanceledException)when(owner.Token.IsCancellationRequested){}
            catch(Exception error)when(RequestError(error)){if(!Closed&&!owner.Token.IsCancellationRequested)Error(error);}
            finally{if(!Closed){loading=false;Render();}}
        }
        private void Settings()
        {
            if(Closed||loading)return;windows.ShowOverlay("settings",owner=>
            {
                var layer=Get<GameObject>("settingsLayer");layer.SetActive(true);owner.Add(()=>{if(layer)layer.SetActive(false);});Hook(owner,"settingsClose",()=>CloseOverlay("settings"));
                Hook(owner,"notificationTab",()=>SettingsTab(false));Hook(owner,"keywordTab",()=>SettingsTab(true));Hook(owner,"guildNotificationTab",()=>unavailable?.Invoke("chat.guildNotifications"));
                Hook(owner,"allNotifications",()=>_=ToggleNotifications(-1,owner));for(int i=0;i<4;i++){int index=i;Hook(owner,"notification"+i,()=>_=ToggleNotifications(index,owner));}
                for(int i=0;i<5;i++)
                {var field=Get<TMP_InputField>("keyword"+i);UnityAction<string> edit=value=>{if(!Closed&&!owner.Token.IsCancellationRequested)_=SaveKeywords(owner);};field.onEndEdit.AddListener(edit);owner.Add(()=>{if(field)field.onEndEdit.RemoveListener(edit);});}
                SettingsTab(false);RenderPreferences();
            });
        }
        private void SettingsTab(bool keywords)
        {Get<GameObject>("notificationContent").SetActive(!keywords);Get<GameObject>("keywordContent").SetActive(keywords);Get<Image>("notificationTabSelected").enabled=!keywords;Get<Image>("keywordTabSelected").enabled=keywords;Get<Image>("guildNotificationTabSelected").enabled=false;}
        private async Task Background(){var p=session.Preferences;if(p==null||saving)return;await Preference(new ChatPreferenceRequest{kind="background",transparentBackground=!p.transparentBackground},scope.Token);}
        private async Task ToggleNotifications(int index,BindingScope owner)
        {
            if(saving||session.Preferences?.notifications==null)return;var n=session.Preferences.notifications;var values=new[]{n.hero5,n.artifact5,n.guildPromotion,n.generalChat};
            if(index<0){bool next=!values.All(v=>v);for(int i=0;i<values.Length;i++)values[i]=next;}else values[index]=!values[index];
            await Preference(new ChatPreferenceRequest{kind="notifications",notifications=new ChatNotifications{hero5=values[0],artifact5=values[1],guildPromotion=values[2],generalChat=values[3]}},owner.Token);
        }
        private async Task SaveKeywords(BindingScope owner)
        {
            if(saving||owner.Token.IsCancellationRequested)return;var values=Enumerable.Range(0,5).Select(i=>Get<TMP_InputField>("keyword"+i).text.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(values.Any(s=>Scalars(s)>20)){Notice("keywordTooLong");return;}
            if(values.SequenceEqual(session.Preferences?.keywords??Array.Empty<string>()))return;await Preference(new ChatPreferenceRequest{kind="keywords",keywords=values},owner.Token);
        }
        private async Task<bool> Preference(ChatPreferenceRequest request,CancellationToken token)
        {
            if(Closed||saving||session.Preferences==null)return false;saving=true;RenderPreferences();try
            {
                bool retry=session.HasPendingPreferences;await (retry?session.RetryPendingPreferencesAsync(token):session.SavePreferencesAsync(request,token));if(Closed||token.IsCancellationRequested)return false;RenderPreferences();Notice("saved");return !retry;
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error)when(RequestError(error))
            {
                if(!Closed&&!token.IsCancellationRequested){Error(error);if(error is ChannelChatRequestException rejected&&(int)rejected.Status==409)try{await session.RefreshPreferencesAsync(scope.Token);}catch(Exception refresh)when(RequestError(refresh)||refresh is OperationCanceledException){}}
            }
            finally{if(!Closed){saving=false;RenderPreferences();}}return false;
        }
        private void RenderPreferences()
        {
            var p=session.Preferences;bool writable=!loading&&!saving&&!session.HasPendingPreferences&&p!=null;bool transparent=p?.transparentBackground==true;Get<Image>("drawerSurface").enabled=!embedded&&!transparent;Get<Image>("transparentBorder").enabled=!embedded&&transparent;Get<Image>("opacitySelected").enabled=transparent;Get<Image>("opacityIcon").color=transparent?Color.black:new Color(1,1,1,.5f);
            Get<GameText>("favoriteValue").text=string.Format(CultureInfo.InvariantCulture,L("favoriteValue"),p?.favoriteChannel?.ToString(CultureInfo.InvariantCulture)??L("none"));Get<GameText>("channelValue").text=string.Format(CultureInfo.InvariantCulture,L("channel"),session.Channel);
            var n=p?.notifications??new ChatNotifications();var values=new[]{n.hero5,n.artifact5,n.guildPromotion,n.generalChat};bool all=values.All(v=>v);Get<Image>("allNotificationsCheck").enabled=all;Get<Image>("allBackground").enabled=all;Get<GameText>("allNotificationsLabel").color=all?new Color32(125,204,18,255):new Color32(136,136,136,255);
            for(int i=0;i<4;i++)Get<Image>("notification"+i+"Check").enabled=values[i];
            for(int i=0;i<5;i++)
            {var field=Get<TMP_InputField>("keyword"+i);if(!field.isFocused&&!saving&&!session.HasPendingPreferences)field.SetTextWithoutNotify(p?.keywords!=null&&i<p.keywords.Length?p.keywords[i]:"");field.interactable=writable;}
            Get<TMP_InputField>("favoriteInput").interactable=writable;
            foreach(string key in new[]{"favoriteSave","opacity","allNotifications","notification0","notification1","notification2","notification3"})Get<GameButton>(key).interactable=writable;
        }
        private void BuildStickerCells()
        {
            var template=Get<GameObject>("stickerCell");var parent=Get<ScrollRect>("stickerScroll").content;
            foreach(var pair in stickers.OrderBy(p=>p.Key,StringComparer.Ordinal))
            {var cell=Object.Instantiate(template,parent,false);cell.name=pair.Key;cell.SetActive(true);var map=cell.GetComponent<BindingMap>();map.Require<Image>("icon").sprite=pair.Value;map.Require<Image>("icon").enabled=true;string id=pair.Key;Hook(scope,map.Require<GameButton>("button"),()=>_=Send(id));stickerCells.Add(cell);}
        }
        private void Picker()
        {
            if(Closed||loading||sending)return;windows.ShowOverlay("stickers",owner=>{var layer=Get<GameObject>("stickerLayer");layer.SetActive(true);owner.Add(()=>{if(layer)layer.SetActive(false);});Hook(owner,"stickerClose",()=>CloseOverlay("stickers"));Layout();});
        }
        private void Layout()
        {
            if(Closed||!view.Root)return;var bounds=((RectTransform)view.Root.transform).rect;float width=bounds.width,height=bounds.height;
            if(width<=0||height<=0||(Mathf.Abs(lastWidth-width)<.5f&&Mathf.Abs(lastHeight-height)<.5f))return;lastWidth=width;lastHeight=height;
            var panel=Get<RectTransform>("stickerPanel");float offset=Mathf.Min(844.5f,Mathf.Max(0,width-panel.rect.width-12));panel.anchoredPosition=new Vector2(-offset,-36);
            var messageList=Get<ScrollRect>("messages");
            var historyHeight=Get<LayoutElement>("historyHeight");
            if(!embedded){historyHeight.minHeight=messageList.viewport.rect.height;return;}
            float scale=width/924f;var drawer=Get<RectTransform>("drawer");drawer.anchorMin=drawer.anchorMax=drawer.pivot=Vector2.zero;drawer.anchoredPosition=Vector2.zero;drawer.sizeDelta=new Vector2(924,height/scale);drawer.localScale=Vector3.one*scale;
            var history=(RectTransform)Get<ScrollRect>("messages").transform.parent;history.anchorMin=Vector2.zero;history.anchorMax=Vector2.one;history.offsetMin=new Vector2(81,132);history.offsetMax=new Vector2(-24,-95);
            historyHeight.minHeight=messageList.viewport.rect.height;
            PlaceBottom((RectTransform)Get<GameButton>("emoji").transform,78,18);
            PlaceBottom((RectTransform)Get<TMP_InputField>("input").transform,159,18);
            PlaceBottom((RectTransform)Get<GameButton>("send").transform,669,18);
            PlaceBottom((RectTransform)Get<GameObject>("enteredStrip").transform,81,132);
            void PlaceBottom(RectTransform rect,float x,float y){rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(x,y);}
        }
        private static int Scalars(string value){int count=0;for(int i=0;i<value.Length;i++,count++)if(char.IsHighSurrogate(value[i])&&i+1<value.Length&&char.IsLowSurrogate(value[i+1]))i++;return count;}
        private static bool RequestError(Exception error)=>error is IOException||error is HttpRequestException||error is OperationCanceledException;
    }
}
