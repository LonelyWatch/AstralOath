using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ChatView=AstralOath.Client.ChannelChat.ChannelChat;
namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatBinderTests
    {
        private sealed class Assets:IAssetService
        {public GameObject prefab;public Task<T> LoadAsync<T>(string address,BindingScope scope)where T:UnityEngine.Object=>Task.FromResult(prefab as T);}
        private sealed class Api:IChannelChatApi
        {
            public readonly List<ChatMessage> messages=new List<ChatMessage>();public readonly List<ChatPreferenceRequest> writes=new List<ChatPreferenceRequest>();public readonly List<string> calls=new List<string>();public int preferenceFailures;
            public TaskCompletionSource<ChatMessage> slow;public TaskCompletionSource<ChatPreferences> slowPreferences;public int sends,disposed;public ChatSendRequest lastSend;
            public ChatPreferences preferences=new ChatPreferences{version=1,notifications=new ChatNotifications(),keywords=Array.Empty<string>()};
            public Task<ChatPreferences> PreferencesAsync(CancellationToken token){calls.Add("read-preferences");return Task.FromResult(preferences);}
            public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request,CancellationToken token)
            {
                request.dispatched=true;writes.Add(request);calls.Add("save-preferences");if(preferenceFailures>0){preferenceFailures--;throw new IOException("Controlled preference transport interruption.");}preferences=new ChatPreferences{version=preferences.version+1,favoriteChannel=request.kind=="favorite"?request.favoriteChannel:preferences.favoriteChannel,notifications=request.notifications??preferences.notifications,keywords=request.keywords??preferences.keywords,transparentBackground=request.transparentBackground??preferences.transparentBackground};return slowPreferences?.Task??Task.FromResult(preferences);
            }
            public Task<ChatPage> ListAsync(int channel,string after,int waitMs,CancellationToken token)=>Task.FromResult(new ChatPage{channel=channel,messages=messages.Where(m=>m.channel==channel&&(after=="0"||long.Parse(m.id)>long.Parse(after))).ToArray(),nextAfter=messages.LastOrDefault(m=>m.channel==channel)?.id??"0",hasMore=false});
            public Task<ChatMessage> SendAsync(int channel,ChatSendRequest request,CancellationToken token)
            {request.dispatched=true;sends++;lastSend=request;if(slow!=null)return slow.Task;var m=Message((messages.Count+1).ToString(),"self",request.text,channel);m.kind=request.kind;m.stickerId=request.stickerId;messages.Add(m);return Task.FromResult(m);}
            public Task WatchAsync(int channel,Action invalidated,CancellationToken token)=>Task.Delay(Timeout.Infinite,token);
            public void Dispose(){disposed++;}
        }
        private sealed class Fixture:IDisposable
        {
            public readonly GameObject canvas;public readonly BindingScope scope=new BindingScope(default);public readonly Api api;public readonly ChannelChatSession session;private readonly bool ownsSession;public ChatView view;public ChannelChatBinder binder;public int closes;public string selected,unavailable;
            public Fixture(Api api,ChannelChatSession sharedSession=null){this.api=api;ownsSession=sharedSession==null;session=sharedSession??new ChannelChatSession(api);canvas=new GameObject("Channel chat test canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;}
            public async Task Open()
            {
                GameObject prefab=null;
#if UNITY_EDITOR
                prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/ChannelChat/Res/Prefab/ChannelChat.prefab");
#endif
                Assert.That(prefab,Is.Not.Null);var registry=new UIViewRegistry();registry.Register<ChatView>();view=await registry.CreateAsync<ChatView>(new Assets{prefab=prefab},canvas.transform,scope);
                binder=new ChannelChatBinder(view,scope,session,()=>closes++,value=>unavailable=value,value=>selected=value,()=>"self");await binder.OpenAsync(default);
            }
            public void Click(string key)=>view.Map.Require<GameButton>(key).onClick.Invoke();
            public void Dispose(){scope.Dispose();if(ownsSession)session.Dispose();UnityEngine.Object.DestroyImmediate(canvas);}
        }
        private static ChatMessage Message(string id,string player,string text,int channel=1)=>new ChatMessage{id=id,kind="text",text=text,channel=channel,createdAt=DateTimeOffset.UtcNow.ToString("O"),player=new ChatSender{id=player,displayName="<b>"+player+"</b>",avatarId="c1001"}};
        private static IEnumerator Done(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.InnerException;Assert.That(task.IsCanceled,Is.False);}
        [UnityTest]public IEnumerator PlainTextMessagesAvatarsStickersAndChannelSwitchUseSession()=>Done(Messages());
        private async Task Messages()
        {
            var api=new Api();api.messages.Add(Message("1","other","<size=90>原样正文</size>"));using var f=new Fixture(api);await f.Open();
            var first=f.view.Root.GetComponentsInChildren<BindingMap>().Single(m=>m.name=="Message 1");Assert.That(first.Require<GameText>("body").text,Is.EqualTo("<size=90>原样正文</size>"));Assert.That(first.Require<GameText>("body").richText,Is.False);
            first.Require<GameButton>("avatar").onClick.Invoke();Assert.That(f.selected,Is.EqualTo("other"));
            f.view.Map.Require<TMP_InputField>("input").text="<b>plain</b>";f.Click("send");Assert.That(api.sends,Is.EqualTo(1));Assert.That(api.lastSend.text,Is.EqualTo("<b>plain</b>"));Assert.That(f.view.Map.Require<TMP_InputField>("input").text,Is.Empty);
            var own=f.view.Root.GetComponentsInChildren<BindingMap>().Single(m=>m.name=="Message 2");Assert.That(own.Require<GameText>("sender").text,Is.EqualTo("<b>self</b>[me]"));Assert.That(own.Require<GameText>("sender").color,Is.EqualTo((Color)new Color32(125,204,18,255)));
            f.Click("emoji");Assert.That(f.view.Map.Require<GameObject>("stickerLayer").activeSelf,Is.True);
            var cell=f.view.Map.Require<ScrollRect>("stickerScroll").content.GetComponentsInChildren<BindingMap>().First(m=>m.gameObject.activeSelf);cell.Require<GameButton>("button").onClick.Invoke();Assert.That(api.lastSend.kind,Is.EqualTo("sticker"));Assert.That(api.lastSend.stickerId,Is.Not.Empty);Assert.That(f.view.Map.Require<GameObject>("stickerLayer").activeSelf,Is.False);
            f.Click("channelSwitch");f.view.Map.Require<TMP_InputField>("channelInput").text="2";f.Click("channelSave");Assert.That(f.session.Channel,Is.EqualTo(2));Assert.That(f.view.Map.Require<GameText>("channelName").text,Is.EqualTo("频道#2"));Assert.That(f.view.Map.Require<GameObject>("enteredStrip").activeSelf,Is.True);Assert.That(f.session.Messages,Is.Empty);
            f.Click("guildTab");Assert.That(f.unavailable,Is.EqualTo("chat.guild"));
        }
        [UnityTest]public IEnumerator RepeatedSettingsDoNotDuplicateWritesAndKeepAllPreferenceFields()=>Done(Preferences());
        private async Task Preferences()
        {
            var api=new Api();using var f=new Fixture(api);await f.Open();
            for(int i=0;i<6;i++){f.Click("settings");f.Click("settingsClose");}f.Click("settings");f.Click("notification0");Assert.That(api.writes.Count,Is.EqualTo(1));Assert.That(api.writes[0].notifications.hero5,Is.False);Assert.That(api.writes[0].notifications.artifact5&&api.writes[0].notifications.guildPromotion&&api.writes[0].notifications.generalChat,Is.True);
            f.Click("keywordTab");var field=f.view.Map.Require<TMP_InputField>("keyword2");field.text="<b>abc</b>";field.onEndEdit.Invoke(field.text);Assert.That(api.writes.Count,Is.EqualTo(2));CollectionAssert.AreEqual(new[]{"<b>abc</b>"},api.preferences.keywords);Assert.That(field.richText,Is.False);
            f.Click("settingsClose");f.Click("opacity");Assert.That(api.preferences.transparentBackground,Is.True);Assert.That(f.view.Map.Require<Image>("drawerSurface").enabled,Is.False);
            f.Click("favorite");f.view.Map.Require<TMP_InputField>("favoriteInput").text="9999";f.Click("favoriteSave");Assert.That(api.preferences.favoriteChannel,Is.EqualTo(9999));Assert.That(f.session.Channel,Is.EqualTo(1),"收藏不静默切换当前频道");
        }
        [UnityTest]public IEnumerator PendingPreferenceDisablesKeywordEditsUntilItsReceipt()=>Done(PendingPreferences());
        private async Task PendingPreferences()
        {
            var api=new Api{slowPreferences=new TaskCompletionSource<ChatPreferences>()};using var f=new Fixture(api);await f.Open();f.Click("settings");f.Click("keywordTab");
            var first=f.view.Map.Require<TMP_InputField>("keyword0");first.text="first";first.onEndEdit.Invoke(first.text);
            try
            {
                Assert.That(f.session.HasPendingPreferences,Is.True);Assert.That(first.text,Is.EqualTo("first"),"禁输入时保留已提交草稿，不能先重填旧快照");
                for(int i=0;i<5;i++)Assert.That(f.view.Map.Require<TMP_InputField>("keyword"+i).interactable,Is.False,"等待偏好回执时不能接受随后会被旧快照清掉的第二次输入");
            }
            finally{api.slowPreferences.TrySetResult(api.preferences);for(int i=0;i<4;i++)await Task.Yield();}
            Assert.That(f.view.Map.Require<TMP_InputField>("keyword1").interactable,Is.True);
        }
        [UnityTest]public IEnumerator UncertainPreferenceStaysReadOnlyAndReopeningRetriesOnlyTheFrozenIntent()=>Done(UncertainPreferences());
        private async Task UncertainPreferences()
        {
            var api=new Api{preferenceFailures=2};using var session=new ChannelChatSession(api);
            using(var first=new Fixture(api,session))
            {
                await first.Open();first.Click("settings");first.Click("keywordTab");var input=first.view.Map.Require<TMP_InputField>("keyword0");input.text="frozen";input.onEndEdit.Invoke(input.text);
                Assert.That(session.HasPendingPreferences,Is.True);
                AssertPreferencesReadOnly(first);
            }
            Assert.That(api.disposed,Is.Zero);Assert.That(api.writes.Count,Is.EqualTo(1));string requestId=api.writes[0].requestId;
            using(var stillOffline=new Fixture(api,session))
            {
                await stillOffline.Open();Assert.That(api.writes.Count,Is.EqualTo(2),"重新打开先重试原pending，不接受新意图");Assert.That(session.HasPendingPreferences,Is.True);AssertPreferencesReadOnly(stillOffline);
                var toast=stillOffline.view.Map.Require<GameObject>("messageChrome").GetComponent<BindingMap>().Require<GameText>("message");Assert.That(toast.text,Is.EqualTo(ChannelChatLabels.Text("pending")));
            }
            int before=api.calls.Count;
            using(var recovered=new Fixture(api,session))
            {
                await recovered.Open();Assert.That(session.HasPendingPreferences,Is.False);Assert.That(api.writes.Count,Is.EqualTo(3));Assert.That(api.calls[before],Is.EqualTo("save-preferences"),"冻结操作回执必须先于新偏好读取");
                Assert.That(api.writes.All(w=>w.requestId==requestId&&w.expectedVersion==1&&w.keywords.SequenceEqual(new[]{"frozen"})),Is.True);
                Assert.That(recovered.view.Map.Require<TMP_InputField>("keyword0").text,Is.EqualTo("frozen"));Assert.That(recovered.view.Map.Require<TMP_InputField>("keyword1").interactable,Is.True);
            }
        }
        private static void AssertPreferencesReadOnly(Fixture f)
        {
            for(int i=0;i<5;i++)Assert.That(f.view.Map.Require<TMP_InputField>("keyword"+i).interactable,Is.False,"未确认偏好不得继续输入并被旧回执覆盖");Assert.That(f.view.Map.Require<TMP_InputField>("favoriteInput").interactable,Is.False);
            foreach(string key in new[]{"opacity","favoriteSave","allNotifications","notification0","notification1","notification2","notification3"})Assert.That(f.view.Map.Require<GameButton>(key).interactable,Is.False,key);
        }
        [UnityTest]public IEnumerator ClosingOnlyUnbindsPageWhileDispatchedSendKeepsItsLoginReceipt()=>Done(Late());
        private async Task Late()
        {
            var api=new Api{slow=new TaskCompletionSource<ChatMessage>()};using var f=new Fixture(api);await f.Open();f.view.Map.Require<TMP_InputField>("input").text="late";f.Click("send");Assert.That(f.session.HasPendingSend,Is.True);var close=f.view.Map.Require<GameButton>("close");f.scope.Dispose();close.onClick.Invoke();Assert.That(f.closes,Is.Zero);Assert.That(api.disposed,Is.Zero,"页面关闭不能销毁登录Session/Api");
            api.slow.SetResult(Message("1","self","late"));for(int i=0;i<8&&f.session.HasPendingSend;i++)await Task.Yield();Assert.That(f.session.HasPendingSend,Is.False);Assert.That(f.session.Messages.Single().text,Is.EqualTo("late"));Assert.That(f.view.Root==null||!f.view.Root.activeSelf,Is.True);
        }
    }
}
