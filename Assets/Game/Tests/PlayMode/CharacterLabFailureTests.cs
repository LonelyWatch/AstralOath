#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabFailureTests
    {
        private GameObject root, statusObject;
        private CharacterLabBootstrap bootstrap;
        private GameText status;
        private BindingScope owner;
        private ProbeEntry entry;
        private FmodCharacterAudioService audio;
        private sealed class ProbeEntry : ICharacterLabEntry
        {
            public int Ticks;
            public bool Disposed, FailTick;
            public Task StartAsync(CharacterLabLaunchContext context) => Task.CompletedTask;
            public void Tick(float delta) { if (FailTick) throw new InvalidOperationException("entry-frame-failure"); Ticks++; }
            public void Dispose() { Disposed = true; }
        }
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        [SetUp] public void SetUp()
        {
            root = new GameObject("failure-bootstrap"); root.SetActive(false);
            bootstrap = root.AddComponent<CharacterLabBootstrap>();
            statusObject = new GameObject("failure-status", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(GameText));
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset");
            Assert.That(font, Is.Not.Null, "Use the project's actual localized font.");
            statusObject.GetComponent<TextMeshProUGUI>().font = font;
            status = statusObject.GetComponent<GameText>(); statusObject.SetActive(false);
            owner = new BindingScope(CancellationToken.None); entry = new ProbeEntry();
            owner.Add(entry.Dispose);
            Set(bootstrap,"startupStatus",status); Set(bootstrap,"lifetime",owner); Set(bootstrap,"entry",entry); Set(bootstrap,"ready",true);
        }
        [TearDown] public void TearDown()
        {
            Invoke(bootstrap,"OnDestroy"); // The test object stays inactive to prevent automatic Start.
            UnityEngine.Object.DestroyImmediate(root); owner?.Dispose(); audio?.Dispose();
            UnityEngine.Object.DestroyImmediate(statusObject);
        }
        [Test] public void AudioFailureDisablesAudioAndKeepsEntryTicking()
        {
            // Real scene-owned service, no native playback is started or claimed by this test.
            audio = new FmodCharacterAudioService(CancellationToken.None); owner.Add(audio.Dispose); Set(bootstrap,"audio",audio);
            LogAssert.Expect(LogType.Warning,new Regex("ASTRAL_CHARACTERLAB_AUDIO_RUNTIME_UNAVAILABLE.*audio-frame-failure"));
            Invoke(bootstrap,"HandleAudioTickFailure",new InvalidOperationException("audio-frame-failure"));
            Invoke(bootstrap,"Update");
            Assert.That(bootstrap.IsReady, Is.True); Assert.That(bootstrap.StartupError, Is.Null);
            Assert.That(owner.Token.IsCancellationRequested, Is.False); Assert.That(entry.Disposed, Is.False);
            Assert.That(entry.Ticks, Is.EqualTo(1)); Assert.That(audio.IsAvailable, Is.False);
            Assert.That(audio.Diagnostic, Does.Contain("audio-frame-failure"));
        }
        [Test] public void EntryFailureShowsErrorAfterDisposingItsOldLifetime()
        {
            entry.FailTick = true;
            LogAssert.Expect(LogType.Error,new Regex("ASTRAL_CHARACTERLAB_TICK_FAILED.*entry-frame-failure"));
            Invoke(bootstrap,"Update");
            Assert.That(bootstrap.IsReady, Is.False); Assert.That(owner.Token.IsCancellationRequested, Is.True);
            Assert.That(entry.Disposed, Is.True); Assert.That(bootstrap.StartupError, Does.Contain("entry-frame-failure"));
            Assert.That(statusObject.activeInHierarchy, Is.True);
            Assert.That(statusObject.GetComponent<TextMeshProUGUI>().text, Does.Contain("entry-frame-failure"));
            var diagnostic = (BindingScope)typeof(CharacterLabBootstrap).GetField("diagnosticLifetime",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(bootstrap);
            Assert.That(diagnostic.Token.IsCancellationRequested, Is.False, "Error text cannot bind to the failed scene scope.");
        }
        [Test] public void StartupFailureAlsoReactivatesVisibleStatus()
        {
            LogAssert.Expect(LogType.Error,new Regex("ASTRAL_CHARACTERLAB_FAILED"));
            Invoke(bootstrap,"Start"); // Missing scene roots fail synchronously before any asynchronous/native work.
            Assert.That(bootstrap.IsReady, Is.False);
            Assert.That(statusObject.activeInHierarchy, Is.True);
            Assert.That(statusObject.GetComponent<TextMeshProUGUI>().text, Is.Not.Empty);
            Assert.That(bootstrap.StartupError, Does.Contain("角色实验室场景缺少启动引用"));
        }
    }
}
#endif
