using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class GmMailBindingTests
    {
#if UNITY_EDITOR
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator TabsEditAndSendAnotherRetainPreviewAndSendBindingsUnity() { var task = TabsEditAndSendAnotherRetainPreviewAndSendBindings(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
#endif

        [Serializable] private sealed class Labels { public Row[] rows; }
        [Serializable] private sealed class Row { public string key, text; }
        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object => Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/FoundationDemo/Res/Prefab/DevelopmentGmWindow.prefab"));
        }
        private sealed class Replies : HttpMessageHandler
        {
            internal int previews, sends;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                string result;
                if (request.RequestUri.AbsolutePath.EndsWith("catalog"))
                    result = "{\"letterTypes\":[{\"id\":\"standard\"},{\"id\":\"gm\"}],\"senders\":[{\"id\":\"courier\",\"name\":\"送货员\"}],\"resources\":[{\"kind\":\"energy\",\"key\":\"energy\",\"name\":\"行动力\"}],\"bundles\":[],\"templates\":[{\"id\":\"maintenance\",\"purpose\":\"maintenance\",\"letterType\":\"standard\",\"senderId\":\"courier\",\"title\":\"测试标题\",\"body\":\"测试正文\",\"expiresInDays\":7,\"attachments\":[{\"kind\":\"energy\",\"key\":\"energy\",\"quantity\":100}]}],\"limits\":{\"maxAttachments\":20,\"maxQuantity\":1000000,\"maxTitleLength\":120,\"maxBodyLength\":8000,\"maxExpiryDays\":90}}";
                else if (request.RequestUri.AbsolutePath.EndsWith("preview"))
                {
                    previews++;
                    result = "{\"previewId\":\"preview-" + previews + "\",\"realmId\":\"realm\",\"playerId\":\"owner\",\"recipientCount\":1,\"expiresInDays\":7,\"mail\":{\"letterType\":\"standard\",\"senderName\":\"送货员\",\"title\":\"<b>纯文本</b>\",\"body\":\"<color=red>纯文本正文</color>\",\"attachments\":[],\"rewards\":[]},\"totalRewards\":[]}";
                }
                else
                {
                    sends++;
                    var sent = JsonUtility.FromJson<GmMailSendRequest>(await request.Content.ReadAsStringAsync());
                    result = "{\"requestId\":\"" + sent.requestId + "\",\"deliveryId\":\"delivery-" + sends + "\",\"summary\":{\"version\":" + sends + ",\"effectiveCount\":" + sends + "}}";
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result) };
            }
        }
        private sealed class MailReads : IMailApi
        {
            public Task<MailSummary> SummaryAsync(CancellationToken token) => Task.FromResult(new MailSummary());
            public Task<MailPage> ListAsync(bool history, string cursor, CancellationToken token) => throw new NotSupportedException();
            public Task<MailLetter> ReadAsync(string id, CancellationToken token) => throw new NotSupportedException();
            public Task<MailClaimReceipt> ClaimAsync(MailClaimRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WatchAsync(Action<long> version, CancellationToken token) => throw new NotSupportedException();
            public void Dispose() { }
        }
        public async Task TabsEditAndSendAnotherRetainPreviewAndSendBindings()
        {
            using var owner = new BindingScope(CancellationToken.None);
            using var windows = new WindowCoordinator(owner.Token);
            using var generic = new DevelopmentGmApi(new Uri("http://127.0.0.1:1"), _ => Task.FromResult("test"));
            using var session = new MailSession(new MailReads());
            var handler = new Replies();
            var api = new DevelopmentGmMailApi(new Uri("http://127.0.0.6:7728"), _ => Task.FromResult("test"), handler);
            var canvas = new GameObject("GM mail binding", typeof(RectTransform), typeof(Canvas));
            try
            {
                var registry = new UIViewRegistry(); registry.Register<DevelopmentGmWindow>();
                var view = await registry.CreateAsync<DevelopmentGmWindow>(new Assets(), canvas.transform, owner);
                var messages = JsonUtility.FromJson<Labels>(File.ReadAllText("Assets/Game/Modules/Arena/Configs/arena-zh-hans.json")).rows.ToDictionary(row => row.key, row => row.text);
                var locale = new LocaleService(new Dictionary<string, IReadOnlyDictionary<string, string>> { ["zh-Hans"] = messages }, "zh-Hans", "ja", "ja");
                int reconciled = 0;
                var binder = new DevelopmentGmWindowBinder(view, windows, generic, locale, (_, key) => key, () => reconciled++, () => Task.CompletedTask);
                binder.ConfigureMail(session, api); binder.Install(owner);
                await windows.ShowPageAsync(_ => Task.CompletedTask);
                void Click(string key) => view.Button(key).onClick.Invoke();
                Click("entry"); Click("mail"); Click("resources"); Click("mail");
                Capture(canvas, "gm-compose-1080", 1920, 1080);
                Capture(canvas, "gm-compose-4k", 3840, 2160);
                Capture(canvas, "gm-compose-narrow", 1440, 1080);
                Click("mailPreviewMake");
                Assert.That(handler.previews, Is.EqualTo(1), "The preview action must survive switching module tabs");
                Capture(canvas, "gm-preview-1080", 1920, 1080);
                Click("mailEdit"); Click("mailPreviewMake");
                Assert.That(handler.previews, Is.EqualTo(2), "The preview action must survive editing a frozen preview");
                Click("mailSend"); Assert.That(handler.sends, Is.EqualTo(1));
                Capture(canvas, "gm-sent-1080", 1920, 1080);
                Click("mailAnother"); Click("mailPreviewMake"); Click("mailSend");
                Assert.That(handler.previews, Is.EqualTo(3)); Assert.That(handler.sends, Is.EqualTo(2));
                Assert.That(reconciled, Is.EqualTo(0), "Sending must not reconcile wallet/inventory rewards");
                Assert.That(session.Summary.effectiveCount, Is.EqualTo(2));
            }
            finally { windows.Dispose(); owner.Dispose(); Object.DestroyImmediate(canvas); }
        }
        private static void Capture(GameObject root, string name, int width, int height)
        {
            string folder = Environment.GetEnvironmentVariable("ASTRAL_GM_MAIL_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            var canvas = root.GetComponent<Canvas>();
            var camera = new GameObject("GM capture camera", typeof(Camera)).GetComponent<Camera>();
            var target = RenderTexture.GetTemporary(width, height, 24);
            var frame = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.scaleFactor = height / 1080f;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, width, height), 0, 0); frame.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(frame); Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
