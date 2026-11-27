#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class SocialPanelTests
    {
        private static readonly Type PanelType = typeof(FoundationEntry).Assembly.GetType(
            "AstralOath.Client.FoundationDemo.SocialPanel", true);
        private static readonly Type NavigationType = typeof(FoundationEntry).Assembly.GetType(
            "AstralOath.Client.FoundationDemo.StartupNavigation", true);
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object Panel(Transform parent, CancellationToken owner) => Activator.CreateInstance(
            PanelType, Hidden, null, new object[] { parent, 180, owner }, null);
        private static Task Open(object panel, Func<BindingScope, Task> open) =>
            (Task)PanelType.GetMethod("OpenAsync", Hidden).Invoke(panel, new object[] { open });
        private static CancellationToken Token(object panel) =>
            (CancellationToken)PanelType.GetProperty("Token", Hidden).GetValue(panel);
        private static bool IsOpen(object panel) =>
            (bool)PanelType.GetProperty("IsOpen", Hidden).GetValue(panel);
        private static GameObject Portal(object panel) =>
            ((Transform)PanelType.GetProperty("Parent", Hidden).GetValue(panel)).gameObject;

        [UnityTest]
        public IEnumerator NestedSocialClosePreservesUnderlyingPageAndOverlay()
        {
            var root = new GameObject("Social ownership fixture", typeof(RectTransform));
            using var login = new BindingScope(default);
            using var underlying = new WindowCoordinator(login.Token);
            object panel = null;
            int pageReleased = 0, overlayReleased = 0, panelReleased = 0;
            try
            {
                var main = underlying.ShowPageAsync(scope =>
                { scope.Add(() => pageReleased++); return Task.CompletedTask; });
                Assert.That(main.IsCompletedSuccessfully, Is.True);
                underlying.ShowOverlay("original-dialog", scope => scope.Add(() => overlayReleased++));
                panel = Panel(root.transform, login.Token);
                var opening = Open(panel, scope =>
                { scope.Add(() => panelReleased++); return Task.CompletedTask; });
                Assert.That(opening.IsCompletedSuccessfully, Is.True);
                Assert.That(underlying.IsOverlayOpen("original-dialog"), Is.True);
                ((IDisposable)panel).Dispose();
                Assert.That(Token(panel).IsCancellationRequested, Is.True);
                Assert.That(panelReleased, Is.EqualTo(1));
                Assert.That(Portal(panel).activeSelf, Is.False);
                Assert.That(pageReleased, Is.Zero);
                Assert.That(overlayReleased, Is.Zero);
                Assert.That(underlying.IsOverlayOpen("original-dialog"), Is.True);
                ((IDisposable)panel).Dispose();
                Assert.That(panelReleased, Is.EqualTo(1));
            }
            finally { (panel as IDisposable)?.Dispose(); Object.Destroy(root); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ParentCloseCancelsPendingOpeningAndReleasesBindings()
        {
            var root = new GameObject("Social cancellation fixture", typeof(RectTransform));
            using var owner = new BindingScope(default);
            object panel = Panel(root.transform, owner.Token);
            int released = 0;
            var gate = new TaskCompletionSource<bool>();
            Task opening = Open(panel, async scope =>
            {
                scope.Add(() => released++);
                using var cancelled = scope.Token.Register(() => gate.TrySetCanceled());
                await gate.Task;
            });
            try
            {
                owner.Dispose();
                yield return null;
                Assert.That(opening.IsCanceled, Is.True);
                Assert.That(IsOpen(panel), Is.False);
                Assert.That(released, Is.EqualTo(1));
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally { ((IDisposable)panel).Dispose(); Object.Destroy(root); }
            yield return null;
        }

        // Exercise real event dispatch and cancellation, rather than counting UnityEvent internals.
        // A login-cached Mail header must be bound only by each Mail opening, even when a new
        // main page recursively scans all inactive cached views under the same root.
        [UnityTest]
        public IEnumerator CachedMailChatEntryUsesMailOpeningAndRebindsAfterClose()
        {
            var root = new GameObject("Cached headers fixture", typeof(RectTransform));
            var mail = new GameObject("Mail overlay", typeof(RectTransform));
            mail.transform.SetParent(root.transform, false);
            var entry = new GameObject("ChatEntry", typeof(RectTransform), typeof(BindingMap), typeof(GameButton));
            entry.transform.SetParent(mail.transform, false);
            var button = entry.GetComponent<GameButton>();
            var map = entry.GetComponent<BindingMap>();
            var serialized = new SerializedObject(map);
            var entries = serialized.FindProperty("entries");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("key").stringValue = "button";
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("component").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            mail.SetActive(false);
            using var mainPage = new BindingScope(default);
            using var chat = new ChannelChatSession(new NoCallsApi());
            var assets = new PendingAssets();
            var registry = new UIViewRegistry();
            registry.Register<AstralOath.Client.ChannelChat.ChannelChat>();
            var navigation = FormatterServices.GetUninitializedObject(NavigationType);
            void Set(string field, object value) => NavigationType.GetField(field, Hidden).SetValue(navigation, value);
            Set("parent", root.transform); Set("views", registry); Set("assets", assets);
            Set("chatSession", chat); Set("mailPortal", mail);
            var bind = NavigationType.GetMethod("BindChatEntries", Hidden);
            void Bind(GameObject target, BindingScope scope, bool pageHeaders) => bind.Invoke(navigation,
                new object[] { target, scope, new HashSet<BindingMap>(), pageHeaders });
            BindingScope first = null, second = null;
            try
            {
                Bind(root, mainPage, true);
                first = new BindingScope(mainPage.Token);
                Bind(mail, first, false);
                button.onClick.Invoke();
                Assert.That(assets.Calls, Is.EqualTo(1));
                first.Dispose();
                yield return null;
                Assert.That(assets.LastToken.IsCancellationRequested, Is.True,
                    "Closing Mail must cancel its Chat load while the underlying main page stays open.");
                Assert.That(mainPage.Token.IsCancellationRequested, Is.False);
                second = new BindingScope(mainPage.Token);
                Bind(mail, second, false);
                button.onClick.Invoke();
                Assert.That(assets.Calls, Is.EqualTo(2), "Reopening Mail must create exactly one fresh Chat opening.");
                second.Dispose();
                yield return null;
                Assert.That(assets.LastToken.IsCancellationRequested, Is.True);
                button.onClick.Invoke();
                Assert.That(assets.Calls, Is.EqualTo(2), "Closed Mail headers must have no remaining handler.");
            }
            finally
            {
                first?.Dispose(); second?.Dispose();
                (NavigationType.GetField("chatPanel", Hidden).GetValue(navigation) as IDisposable)?.Dispose();
                Object.Destroy(root);
            }
            yield return null;
        }

        private sealed class PendingAssets : IAssetService
        {
            internal int Calls;
            internal CancellationToken LastToken;
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                Calls++; LastToken = scope.Token;
                var pending = new TaskCompletionSource<T>();
                scope.Token.Register(() => pending.TrySetCanceled());
                return pending.Task;
            }
        }

        private sealed class NoCallsApi : IChannelChatApi
        {
            public Task<ChatPreferences> PreferencesAsync(CancellationToken token) => throw new NotSupportedException();
            public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<ChatPage> ListAsync(int channel, string after, int waitMs, CancellationToken token) => throw new NotSupportedException();
            public Task<ChatMessage> SendAsync(int channel, ChatSendRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WatchAsync(int channel, Action invalidated, CancellationToken token) => throw new NotSupportedException();
            public void Dispose() { }
        }
    }
}
#endif
