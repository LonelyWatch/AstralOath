using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.FoundationDemo
{
    // Additional installed-Player checks. The caller must already have created isolated QA identities.
    internal static class SocialChatSettingsSmoke
    {
        internal static async Task VerifyAsync(BindingMap chat,ChannelChatSession session,Uri endpoint,
            Func<CancellationToken,Task<string>> bearer,Func<string,Task> capture,CancellationToken token)
        {
            if(!SocialPlayerSmoke.Requested)throw new InvalidOperationException("Explicit Native Development social verification is required.");
            SocialPlayerSmoke.CheckTarget(endpoint);
            if(!chat||session==null||bearer==null||capture==null)throw new ArgumentException("Chat settings verification requires installed UI, login session, authenticated QA API and capture callback.");
            using var authoritative=new ChannelChatApi(endpoint,bearer);
            await Until(()=>session.Preferences!=null&&!session.HasPendingPreferences&&chat.Require<GameButton>("send").IsInteractable(),"chat settings ready",token);
            Require(session.Channel==1,"settings verification starts in channel one");
            var initial=await authoritative.PreferencesAsync(token);var history=session.Messages.Select(m=>m.id).ToArray();

            Click(chat,"favorite");Require(chat.Require<GameObject>("favoriteLayer").activeInHierarchy,"favorite input opened");
            chat.Require<TMP_InputField>("favoriteInput").text="123";await capture("17-chat-favorite-input");Click(chat,"favoriteSave");
            await Until(()=>session.Preferences.favoriteChannel==123&&!session.HasPendingPreferences&&!chat.Require<GameObject>("favoriteLayer").activeInHierarchy,"favorite UI receipt",token);
            var favorite=await authoritative.PreferencesAsync(token);Require(favorite.favoriteChannel==123&&favorite.version>initial.version,"favorite persisted independently");Require(session.Channel==1,"saving favorite does not silently join it");
            Click(chat,"favorite");Require(chat.Require<GameText>("favoriteValue").text.Contains("123"),"saved favorite shown on reopen");await capture("18-chat-favorite-saved");Click(chat,"favoriteClose");

            Click(chat,"channelSwitch");chat.Require<TMP_InputField>("channelInput").text="2";await capture("19-chat-channel-input");Click(chat,"channelSave");
            await Until(()=>session.Channel==2&&!chat.Require<GameObject>("channelLayer").activeInHierarchy&&chat.Require<GameButton>("send").IsInteractable(),"channel two UI receipt",token);
            Require(chat.Require<GameText>("channelName").text==string.Format(ChannelChatLabels.Text("channel"),2)&&session.Messages.All(m=>m.channel==2),"channel two title and isolated rows");
            Require((await authoritative.ListAsync(2,"0",0,token)).channel==2,"channel two independently readable");await capture("20-chat-channel-two");
            Click(chat,"channelSwitch");chat.Require<TMP_InputField>("channelInput").text="1";Click(chat,"channelSave");
            await Until(()=>session.Channel==1&&!chat.Require<GameObject>("channelLayer").activeInHierarchy&&chat.Require<GameButton>("send").IsInteractable(),"return to channel one",token);
            Require(history.All(id=>session.Messages.Any(m=>m.id==id)),"switch back retains existing history without deletion");

            Click(chat,"settings");Require(chat.Require<GameObject>("settingsLayer").activeInHierarchy,"notification overlay opened");
            var before=session.Preferences.notifications;var expected=new ChatNotifications{hero5=!before.hero5,artifact5=before.artifact5,guildPromotion=before.guildPromotion,generalChat=before.generalChat};long revision=session.Preferences.version;
            Click(chat,"notification0");await Until(()=>session.Preferences.version>revision&&!session.HasPendingPreferences&&Same(session.Preferences.notifications,expected),"single notification UI receipt",token);
            Require(Same((await authoritative.PreferencesAsync(token)).notifications,expected),"all four notification values persisted independently");await capture("21-chat-notifications");
            bool all=!(expected.hero5&&expected.artifact5&&expected.guildPromotion&&expected.generalChat);expected=new ChatNotifications{hero5=all,artifact5=all,guildPromotion=all,generalChat=all};revision=session.Preferences.version;
            Click(chat,"allNotifications");await Until(()=>session.Preferences.version>revision&&!session.HasPendingPreferences&&Same(session.Preferences.notifications,expected),"all notifications UI receipt",token);
            Require(Same((await authoritative.PreferencesAsync(token)).notifications,expected),"all notification toggle persisted independently");await capture("22-chat-notifications-all");

            Click(chat,"keywordTab");Require(chat.Require<GameObject>("keywordContent").activeInHierarchy,"keyword tab opened");
            string[] keywords={"开发验收","调试"};for(int i=0;i<5;i++)chat.Require<TMP_InputField>("keyword"+i).text=i<keywords.Length?keywords[i]:"";revision=session.Preferences.version;
            var keyword=chat.Require<TMP_InputField>("keyword0");keyword.onEndEdit.Invoke(keyword.text);
            await Until(()=>session.Preferences.version>revision&&!session.HasPendingPreferences&&session.Preferences.keywords.SequenceEqual(keywords)&&keyword.interactable,"keyword end-edit UI receipt",token);
            Require((await authoritative.PreferencesAsync(token)).keywords.SequenceEqual(keywords),"keywords independently persisted without empty slots");
            Require(Enumerable.Range(0,5).All(i=>!chat.Require<TMP_InputField>("keyword"+i).richText),"keywords remain plain text");await capture("23-chat-keywords");Click(chat,"settingsClose");

            await Background(true,"24-chat-transparent");await Background(false,"25-chat-opaque-restored");
            var final=await authoritative.PreferencesAsync(token);Require(final.favoriteChannel==123&&final.keywords.SequenceEqual(keywords)&&Same(final.notifications,expected)&&!final.transparentBackground,"complete settings survive independent final read");
            Require(session.Channel==1&&history.All(id=>session.Messages.Any(m=>m.id==id)),"settings leave channel one history intact");

            async Task Background(bool transparent,string name)
            {
                if(session.Preferences.transparentBackground!=transparent)Click(chat,"opacity");
                await Until(()=>!session.HasPendingPreferences&&session.Preferences.transparentBackground==transparent&&chat.Require<GameButton>("opacity").IsInteractable(),"background UI receipt",token);
                Require((await authoritative.PreferencesAsync(token)).transparentBackground==transparent,"background preference persisted independently");
                Require(chat.Require<Image>("drawerSurface").enabled!=transparent&&chat.Require<Image>("transparentBorder").enabled==transparent&&chat.Require<Image>("opacitySelected").enabled==transparent,"source backdrop and selected icon match transparency");await capture(name);
            }
        }
        private static bool Same(ChatNotifications a,ChatNotifications b)=>a!=null&&b!=null&&a.hero5==b.hero5&&a.artifact5==b.artifact5&&a.guildPromotion==b.guildPromotion&&a.generalChat==b.generalChat;
        private static void Click(BindingMap map,string key)
        {var button=map.Require<GameButton>(key);Require(button.gameObject.activeInHierarchy&&button.IsInteractable(),"active settings target "+key);button.onClick.Invoke();}
        private static void Require(bool condition,string check){if(!condition)throw new InvalidOperationException("Social settings verification failed: "+check);}
        private static async Task Until(Func<bool> condition,string check,CancellationToken token)
        {var watch=Stopwatch.StartNew();while(!condition()){token.ThrowIfCancellationRequested();if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new TimeoutException("Social settings verification timed out: "+check);await Task.Delay(30,token);}}
    }
}
