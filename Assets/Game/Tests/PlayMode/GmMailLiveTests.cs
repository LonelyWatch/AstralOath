#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class GmMailLiveTests
    {
        [Serializable] private sealed class Labels { public Row[] rows; }
        [Serializable] private sealed class Row { public string key, text; }
        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object => Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/FoundationDemo/Res/Prefab/DevelopmentGmWindow.prefab"));
        }
        [UnityTest, Timeout(60000)] public IEnumerator NewQaRealServerPreviewBecomesSendable()
        {
            if (Environment.GetEnvironmentVariable("ASTRAL_GM_MAIL_LIVE") != "1") Assert.Ignore("Explicit isolated live QA required.");
            var task = Check(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
        }
        private static async Task Check()
        {
            using var owner = new BindingScope(CancellationToken.None);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(40));
            var endpoint = new Uri("http://127.0.0.6:7728");
            using var guest = new ArenaApi(endpoint, owner.Token);
            string token = await guest.GetSessionTokenAsync("GMPreviewQA" + Guid.NewGuid().ToString("N").Substring(0, 10), deadline.Token);
            Func<CancellationToken, Task<string>> bearer = _ => Task.FromResult(token);
            using var windows = new WindowCoordinator(owner.Token);
            using var generic = new DevelopmentGmApi(endpoint, bearer);
            using var session = new MailSession(new MailApi(endpoint, bearer));
            await session.RefreshAsync(deadline.Token);
            var mail = new DevelopmentGmMailApi(endpoint, bearer);
            var catalog = await mail.CatalogAsync(deadline.Token);
            var template = catalog.templates.Single(t => t.id == "maintenance");
            var direct = await mail.PreviewAsync(new GmMailDraft { templateId = template.id, letterType = template.letterType, senderId = template.senderId,
                title = template.title, body = template.body, expiresInDays = template.expiresInDays, attachments = template.attachments }, deadline.Token);
            Debug.Log("GM_LIVE_DIRECT_PREVIEW fields=" + (direct.mail != null) + ";rewards=" + direct.totalRewards.Length + ";type=" + direct.mail?.letterType);
            var canvas = new GameObject("Live GM preview", typeof(RectTransform), typeof(Canvas));
            try
            {
                var registry = new UIViewRegistry(); registry.Register<DevelopmentGmWindow>();
                var view = await registry.CreateAsync<DevelopmentGmWindow>(new Assets(), canvas.transform, owner);
                var messages = JsonUtility.FromJson<Labels>(File.ReadAllText("Assets/Game/Modules/Arena/Configs/arena-zh-hans.json")).rows.ToDictionary(r => r.key, r => r.text);
                var locale = new LocaleService(new Dictionary<string, IReadOnlyDictionary<string, string>> { ["zh-Hans"] = messages }, "zh-Hans", "ja", "ja");
                var binder = new DevelopmentGmWindowBinder(view, windows, generic, locale, (_, key) => key, () => { }, () => Task.CompletedTask);
                binder.ConfigureMail(session, mail); binder.Install(owner);
                await windows.ShowPageAsync(_ => Task.CompletedTask);
                view.Button("entry").onClick.Invoke(); view.Button("mail").onClick.Invoke();
                await Wait(() => view.MailList("mailTemplates").content.GetComponentsInChildren<GameButton>().Length > 0, deadline.Token);
                var maintenance = view.MailList("mailTemplates").content.GetComponentsInChildren<GameButton>().Single(b => b.GetComponentInChildren<GameText>().text.Contains("维护补偿"));
                maintenance.onClick.Invoke(); view.Field("mailTitle").text = "GMPreviewQA " + Guid.NewGuid().ToString("N").Substring(0, 8);
                view.Button("mailPreviewMake").onClick.Invoke();
                await Task.Delay(2000, deadline.Token);
                var intent = (DevelopmentGmMailIntent)typeof(DevelopmentGmWindowBinder).GetField("mailIntent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binder);
                Debug.Log("GM_LIVE_BINDER state: preview=" + (intent.Preview != null) + ";busy=" + intent.IsBusy + ";panel=" + view.MailPreview.activeInHierarchy + ";send=" + view.Button("mailSend").interactable + ";status=" + view.Label("status").text);
                Assert.That(view.MailPreview.activeInHierarchy, Is.True, view.Label("status").text);
                Assert.That(view.Button("mailSend").interactable, Is.True, view.Label("status").text);
            }
            finally { windows.Dispose(); owner.Dispose(); Object.DestroyImmediate(canvas); }
        }
        private static async Task Wait(Func<bool> ready, CancellationToken token) { while (!ready()) await Task.Delay(50, token); }
    }
}
#endif
