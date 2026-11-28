using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ChatView = AstralOath.Client.ChannelChat.ChannelChat;

namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatUnreadBinderTests
    {
        private sealed class Api : IChannelChatApi
        {
            internal readonly List<ChatMessage> messages = new List<ChatMessage>();
            internal TaskCompletionSource<ChatPage> pending;
            public Task<ChatPreferences> PreferencesAsync(CancellationToken token) => Task.FromResult(new ChatPreferences { notifications = new ChatNotifications(), keywords = Array.Empty<string>() });
            public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<ChatPage> ListAsync(int channel, string after, int waitMs, CancellationToken token) => pending?.Task ?? Task.FromResult(Page(channel, after));
            internal ChatPage Page(int channel, string after) => new ChatPage { channel = channel, messages = messages.Where(m => m.channel == channel && int.Parse(m.id) > int.Parse(after)).ToArray(), nextAfter = messages.LastOrDefault(m => m.channel == channel)?.id ?? after };
            public Task<ChatMessage> SendAsync(int channel, ChatSendRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WatchAsync(int channel, Action invalidate, CancellationToken token) => Task.Delay(Timeout.Infinite, token);
            public void Dispose() { }
            internal void Add(int id) => messages.Add(new ChatMessage { id = id.ToString(), channel = 1, kind = "text", text = "fixture", player = new ChatSender { id = "other" } });
        }
        private sealed class Assets : IAssetService
        {
            internal GameObject prefab;
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object => Task.FromResult(prefab as T);
        }
        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject root = new GameObject("Unread chat canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            internal readonly BindingScope scope = new BindingScope(default);
            internal ChatView view;
            internal ChannelChatBinder binder;
            internal async Task Create(ChannelChatSession session)
            {
                GameObject prefab = null;
#if UNITY_EDITOR
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/ChannelChat/Res/Prefab/ChannelChat.prefab");
#endif
                Assert.That(prefab, Is.Not.Null); root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var registry = new UIViewRegistry(); registry.Register<ChatView>();
                view = await registry.CreateAsync<ChatView>(new Assets { prefab = prefab }, root.transform, scope);
                binder = new ChannelChatBinder(view, scope, session, () => { }, _ => { }, _ => { });
            }
            public void Dispose() { scope.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }
        private static int Count(ChannelChatSession session)
        {
            var property = typeof(ChannelChatSession).GetProperty("UnreadCount");
            Assert.That(property, Is.Not.Null, "Unread state belongs to the login session, not the chat window.");
            return (int)property.GetValue(session);
        }
        private static IEnumerator Done(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception.InnerException; Assert.That(task.IsCanceled, Is.False); }

        [UnityTest] public IEnumerator LoadingWindowDoesNotReadUntilMessagesAreRendered() => Done(Loading());
        private async Task Loading()
        {
            var api = new Api(); using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            api.Add(1); await session.RefreshAsync(default); Assert.That(Count(session), Is.EqualTo(1));
            using var fixture = new Fixture(); await fixture.Create(session); api.pending = new TaskCompletionSource<ChatPage>();
            var open = fixture.binder.OpenAsync(default); Assert.That(Count(session), Is.EqualTo(1), "Creating or loading a window is not reading it.");
            api.pending.SetResult(api.Page(1, "1")); await open; Assert.That(Count(session), Is.Zero);
        }

        [UnityTest] public IEnumerator ScrollingOldMessagesAndClosingDoNotReadNewArrivals() => Done(Scrolling());
        private async Task Scrolling()
        {
            var api = new Api(); for (int id = 1; id <= 30; id++) api.Add(id);
            using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            using var fixture = new Fixture(); await fixture.Create(session); await fixture.binder.OpenAsync(default);
            var scroll = fixture.view.Map.Require<ScrollRect>("messages"); scroll.verticalNormalizedPosition = 1;
            api.Add(31); await session.RefreshAsync(default); Assert.That(Count(session), Is.EqualTo(1), "Appending while reading old messages must preserve unread state.");
            scroll.verticalNormalizedPosition = 0; scroll.onValueChanged.Invoke(Vector2.zero); Assert.That(Count(session), Is.Zero);
            fixture.scope.Dispose(); api.Add(32); await session.RefreshAsync(default); Assert.That(Count(session), Is.EqualTo(1), "Disposing the window must detach its read listener while the login session survives.");
        }
    }
}
