using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.ChannelChat;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatUnreadTests
    {
        private sealed class Api : IChannelChatApi
        {
            internal readonly List<ChatMessage> messages = new List<ChatMessage>();
            internal Action invalidated;
            internal bool disposed;
            public Task<ChatPreferences> PreferencesAsync(CancellationToken token) => Task.FromResult(new ChatPreferences { notifications = new ChatNotifications(), keywords = Array.Empty<string>() });
            public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<ChatPage> ListAsync(int channel, string after, int waitMs, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var found = messages.Where(m => m.channel == channel && Compare(m.id, after) > 0).OrderBy(m => m.id.Length).ThenBy(m => m.id, StringComparer.Ordinal).ToArray();
                return Task.FromResult(new ChatPage { channel = channel, messages = found, nextAfter = found.LastOrDefault()?.id ?? after });
            }
            public Task<ChatMessage> SendAsync(int channel, ChatSendRequest request, CancellationToken token)
            {
                request.dispatched = true;
                var own = Message("999", channel, "self"); messages.Add(own); return Task.FromResult(own);
            }
            public async Task WatchAsync(int channel, Action invalidate, CancellationToken token)
            { invalidated = invalidate; invalidate(); await Task.Delay(Timeout.Infinite, token); }
            public void Dispose() => disposed = true;
        }
        private static int Compare(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        private static ChatMessage Message(string id, int channel = 1, string player = "other") => new ChatMessage { id = id, channel = channel, kind = "text", text = "fixture", player = new ChatSender { id = player } };
        private static int Count(ChannelChatSession session, int? channel = null)
        {
            if (channel.HasValue)
            {
                var method = typeof(ChannelChatSession).GetMethod("UnreadFor", new[] { typeof(int) });
                Assert.That(method, Is.Not.Null, "Unread state must be retained separately for each visited channel.");
                return (int)method.Invoke(session, new object[] { channel.Value });
            }
            var property = typeof(ChannelChatSession).GetProperty("UnreadCount");
            Assert.That(property, Is.Not.Null, "Login session must expose received but unread public messages to every chat entry.");
            return (int)property.GetValue(session);
        }
        private static void Read(ChannelChatSession session, int channel, string through)
        {
            var method = typeof(ChannelChatSession).GetMethod("MarkRead", new[] { typeof(int), typeof(string) });
            Assert.That(method, Is.Not.Null, "Only a rendered channel and visible message ID may acknowledge unread messages.");
            method.Invoke(session, new object[] { channel, through });
        }
        private static IEnumerator Done(Task task) { while (!task.IsCompleted) yield return null; if (task.IsFaulted) throw task.Exception.InnerException; Assert.That(task.IsCanceled, Is.False); }

        [UnityTest] public IEnumerator InitialHistoryIsBaselineAndRepeatedRefreshDoesNotDoubleCount() => Done(Baseline());
        private async Task Baseline()
        {
            var api = new Api(); api.messages.Add(Message("1")); using var session = new ChannelChatSession(api);
            await session.JoinAsync(1, default); Assert.That(Count(session), Is.Zero);
            api.messages.Add(Message("2")); await session.RefreshAsync(default); await session.JoinAsync(1, default);
            Assert.That(Count(session), Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ReadingWrongChannelOrUnknownMessageCannotClearUnread() => Done(WrongRead());
        private async Task WrongRead()
        {
            var api = new Api(); using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            api.messages.Add(Message("1")); await session.RefreshAsync(default); Assert.That(Count(session), Is.EqualTo(1));
            Read(session, 2, "1"); Read(session, 1, "999"); Assert.That(Count(session), Is.EqualTo(1));
            Read(session, 1, "1"); Assert.That(Count(session), Is.Zero);
        }
        [UnityTest] public IEnumerator AcknowledgingVisibleMessageLeavesLaterMessageUnread() => Done(PartialRead());
        private async Task PartialRead()
        {
            var api = new Api(); using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            api.messages.AddRange(new[] { Message("9007199254740992"), Message("9007199254740993") }); await session.RefreshAsync(default);
            Assert.That(Count(session), Is.EqualTo(2)); Read(session, 1, "9007199254740992"); Assert.That(Count(session), Is.EqualTo(1));
            Read(session, 1, "9007199254740993"); Assert.That(Count(session), Is.Zero);
        }
        [UnityTest] public IEnumerator SwitchingChannelsDoesNotReadThePreviousChannel() => Done(Switch());
        private async Task Switch()
        {
            var api = new Api(); using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            api.messages.Add(Message("1")); await session.RefreshAsync(default); await session.JoinAsync(2, default);
            Assert.That(Count(session, 1), Is.EqualTo(1)); Assert.That(Count(session, 2), Is.Zero); Assert.That(Count(session), Is.EqualTo(1));
            await session.JoinAsync(1, default); Assert.That(Count(session), Is.EqualTo(1)); Read(session, 1, "1"); Assert.That(Count(session), Is.Zero);
        }
        [UnityTest] public IEnumerator OwnSendReceiptAndItsLaterListReplayAreNotUnread() => Done(OwnSend());
        private async Task OwnSend()
        {
            var api = new Api(); using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            api.messages.Add(Message("1")); await session.SendAsync(new ChatSendRequest { kind = "text", text = "fixture" }, default);
            await session.RefreshAsync(default); Assert.That(Count(session), Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ClosedPagesKeepLoginObservationAndLogoutCancelsIt() => Done(Observation());
        private async Task Observation()
        {
            var api = new Api(); using var login = new CancellationTokenSource(); var session = new ChannelChatSession(api, login.Token);
            try
            {
                await session.JoinAsync(1, default); var watching = session.ObserveAsync(default);
                api.messages.Add(Message("1")); api.invalidated(); await Task.Yield();
                Assert.That(Count(session), Is.EqualTo(1)); Assert.That(session.ObserveAsync(default), Is.SameAs(watching));
                login.Cancel(); await watching; session.Dispose(); Assert.That(api.disposed, Is.True); Assert.That(Count(session), Is.Zero);
            }
            finally { session.Dispose(); }
        }
        [UnityTest] public IEnumerator UnreadSurvivesHistoryCacheTrimmingUntilRead() => Done(Trim());
        private async Task Trim()
        {
            var api = new Api(); using var session = new ChannelChatSession(api); await session.JoinAsync(1, default);
            api.messages.AddRange(Enumerable.Range(1, 205).Select(id => Message(id.ToString()))); await session.RefreshAsync(default);
            Assert.That(session.Messages.Count, Is.EqualTo(200)); Assert.That(Count(session), Is.EqualTo(205));
            Read(session, 1, "205"); Assert.That(Count(session), Is.Zero);
        }
    }
}
