using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.PlayerProfile;
using UnityEngine;
using ProfileView = AstralOath.Client.PlayerProfile.PlayerProfile;
using ChatView = AstralOath.Client.ChannelChat.ChannelChat;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        private PlayerProfileSession profileSession;
        private ChannelChatSession chatSession;
        private SocialPanel profilePanel, chatPanel, noticePanel;
        private string loginPlayerId;
        private Func<CancellationToken, Task<string>> socialBearer;

        private void InstallSocial(Func<CancellationToken, Task<string>> bearer, GameAccountSession account)
        {
            views.Register<ProfileView>();
            views.Register<ChatView>();
            views.Register<FeatureNotice>();
            loginPlayerId = account?.PlayerId;
            socialBearer = bearer;
            profileSession = new PlayerProfileSession(new PlayerProfileApi(endpoint, bearer));
            chatSession = new ChannelChatSession(new ChannelChatApi(endpoint, bearer), token);
            lifetime.Add(profileSession.Dispose);
            lifetime.Add(chatSession.Dispose);
            profileSession.Changed += ApplyProfileHeader;
            lifetime.Add(() =>
            {
                profileSession.Changed -= ApplyProfileHeader;
                noticePanel?.Dispose();
                profilePanel?.Dispose();
                chatPanel?.Dispose();
            });
            home.ConfigureNavigation(ct => OpenPlayerProfileAsync(null, ct), OpenSummon, OpenUnavailableAsync);
            home.ApplyPlayerHeader(account?.DisplayName, Avatar(account?.AvatarId));
        }

        private Sprite Avatar(string id) => pages.Heroes.Loaded?.heroes?.FirstOrDefault(hero => hero.id == id)?.portrait;

        private void ApplyProfileHeader(PlayerProfileData data)
        {
            if (token.IsCancellationRequested || data == null) return;
            loginPlayerId = data.playerId;
            home.ApplyPlayerHeader(data.displayName, Avatar(data.avatarId), rank:
                data.rank?.available == true ? data.rank.value : null);
        }

        private async Task PrepareSocialAsync()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await Task.WhenAll(OptionalSocial(() => profileSession.ReadAsync(null, deadline.Token)), PrepareChat());
            if (!token.IsCancellationRequested)
            {
                ApplyProfileHeader(profileSession.Self);
                _ = chatSession.ObserveAsync(token);
            }

            async Task PrepareChat()
            {
                await OptionalSocial(() => chatSession.RefreshPreferencesAsync(deadline.Token));
                await OptionalSocial(() => chatSession.JoinAsync(chatSession.Preferences?.favoriteChannel ?? 1, deadline.Token));
            }
        }

        private async Task OptionalSocial(Func<Task> operation)
        {
            try { await operation(); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (!token.IsCancellationRequested)
            { Debug.LogWarning("Social preparation: " + error.GetType().Name); }
        }

        private async Task OpenPlayerProfileAsync(string playerId, CancellationToken owner)
        {
            if (profilePanel?.IsOpen == true) return;
            var panel = new SocialPanel(parent, 180, owner);
            profilePanel = panel;
            await panel.OpenAsync(async scope =>
            {
                var view = await views.CreateAsync<ProfileView>(assets, panel.Parent, scope);
                var art = await pages.Heroes.Get();
                scope.Token.ThrowIfCancellationRequested();
                var binder = new PlayerProfileBinder(view, scope, profileSession, panel.Dispose,
                    key => ObserveSocial(OpenUnavailableAsync(key, scope.Token)), art, portraits,
                    realmName: SocialLabels.Realm);
                await binder.OpenAsync(playerId, scope.Token);
            });
        }

        private async Task OpenChatAsync(CancellationToken owner)
        {
            if (chatPanel?.IsOpen == true) return;
            var panel = new SocialPanel(parent, 160, owner);
            chatPanel = panel;
            await panel.OpenAsync(async scope =>
            {
                var view = await views.CreateAsync<ChatView>(assets, panel.Parent, scope);
                await pages.Heroes.Get();
                scope.Token.ThrowIfCancellationRequested();
                var binder = new ChannelChatBinder(view, scope, chatSession, panel.Dispose,
                    key => ObserveSocial(OpenUnavailableAsync(key, scope.Token)),
                    id => ObserveSocial(OpenPlayerProfileAsync(id, scope.Token)),
                    () => loginPlayerId, Avatar);
                await binder.OpenAsync(scope.Token);
            });
        }

        private async Task OpenUnavailableAsync(string key, CancellationToken owner)
        {
            if (noticePanel?.IsOpen == true) return;
            var panel = new SocialPanel(parent, 220, owner);
            noticePanel = panel;
            await panel.OpenAsync(async scope =>
            {
                var view = await views.CreateAsync<FeatureNotice>(assets, panel.Parent, scope);
                scope.Token.ThrowIfCancellationRequested();
                new FeatureNoticeBinder(view).Open(scope, SocialLabels.Title(key),
                    SocialLabels.Text("unavailable"), SocialLabels.Text("dismiss"), panel.Dispose);
            });
        }

        private static async void ObserveSocial(Task operation)
        {
            try { await operation; }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogWarning("Social window: " + error.GetType().Name); }
        }

        // Cached header entries keep a page-owned listener while the More drawer is hidden.
        private void BindChatEntries(GameObject root, BindingScope scope, HashSet<BindingMap> bound, bool pageHeaders = true)
        {
            if (chatSession == null) return;
            foreach (var map in root.GetComponentsInChildren<BindingMap>(true))
            {
                if (map.name != "ChatEntry" || (pageHeaders && mailPortal && map.transform.IsChildOf(mailPortal.transform)) || !bound.Add(map)) continue;
                var button = map.Require<GameButton>("button");
                bool opening = false;
                async void Click()
                {
                    if (opening || scope.Token.IsCancellationRequested) return;
                    opening = true;
                    try { await OpenChatAsync(scope.Token); }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { Debug.LogWarning("Chat entry: " + error.GetType().Name); }
                    finally { opening = false; }
                }
                button.onClick.AddListener(Click);
                scope.Add(() => { if (button) button.onClick.RemoveListener(Click); });
            }
        }
    }

    internal static class SocialLabels
    {
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unavailable"] = "功能尚未完成。", ["dismiss"] = "点击关闭", ["feature"] = "功能尚未完成",
            ["realm.undying-memory"] = "不凋零回忆", ["realm"] = "区服", ["chat.guild"] = "骑士团",
            ["chat.guildNotifications"] = "骑士团通知", ["chat.notices"] = "聊天通知",
        };
        internal static string Text(string key) => Zh.TryGetValue(key ?? "", out var text) ? text : Zh["feature"];
        internal static string Realm(string id) => Zh.TryGetValue("realm." + id, out var name) ? name : Text("realm");
        internal static string Title(string key)
        {
            if (key != null && key.StartsWith(HomeFeatureLabels.Prefix, StringComparison.Ordinal)) return HomeFeatureLabels.Text(key);
            if (key != null && key.StartsWith("profile.", StringComparison.Ordinal)) return PlayerProfileLabels.Text(key.Substring(8));
            return Text(key);
        }
    }
}
