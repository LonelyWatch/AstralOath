using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class FoundationContentContractTests
    {
        private const string ActiveMessages =
            "[{\"key\":\"foundation.title\",\"zhCn\":\"基础设施演示\"}," +
            "{\"key\":\"foundation.message\",\"zhCn\":\"配置加载成功\"}," +
            "{\"key\":\"foundation.open\",\"zhCn\":\"打开\"},{\"key\":\"foundation.close\",\"zhCn\":\"关闭\"}," +
            "{\"key\":\"foundation.echo\",\"zhCn\":\"发送\"},{\"key\":\"foundation.update.recovered\",\"zhCn\":\"已恢复\"}," +
            "{\"key\":\"foundation.ready\",\"zhCn\":\"{0} {1}\"},{\"key\":\"foundation.pending\",\"zhCn\":\"发送中\"}," +
            "{\"key\":\"foundation.echo.ok\",\"zhCn\":\"{0} {1}\"},{\"key\":\"foundation.echo.error\",\"zhCn\":\"失败\"}]";

        [TestCase("foundation.ready", "{2}")]
        [TestCase("foundation.pending", "{0}")]
        [TestCase("foundation.echo.ok", "{2}")]
        [TestCase("foundation.echo.error", "{0}")]
        public void CandidateRejectsBrokenDisplayFormat(string key, string format)
        {
            string prefix = "\"" + key + "\",\"zhCn\":\"";
            string content = Regex.Replace(ActiveMessages, Regex.Escape(prefix) + "[^\"]*", prefix + format);
            Assert.Throws<InvalidDataException>(() => Validate(new Assets(content), new Config()));
        }

        [Test]
        public void CandidateRejectsEmptySample()
        {
            Assert.Throws<InvalidDataException>(() => Validate(new Assets(sample: "  "), new Config()));
        }

        [Test]
        public void CandidateAcceptsValidDisplayContract()
        {
            Validate(new Assets(), new Config());
        }

        [Test]
        public void ProtobufRejectsMessageThatExceedsLimitAfterUtf8Encoding()
        {
            Assert.Throws<InvalidDataException>(() =>
                ProtobufEchoRequests.ValidateMessage(new string('星', 2716) + "AB"));
        }

        [Test]
        public void ProtobufAcceptsChineseMessageWithinLimit()
        {
            ProtobufEchoRequests.ValidateMessage(new string('星', 2716) + "A");
        }

        [Test]
        public void CandidateUsesItsOwnNewKeyAndLeavesActiveMessagesUntouched()
        {
            using var scope = new BindingScope(default);
            var active = LoadLocale(new Assets(), scope).GetAwaiter().GetResult();
            var assets = new Assets(ActiveMessages.Replace("foundation.title", "foundation.new_title")
                .Replace("基础设施演示", "新标题").Replace("配置加载成功", "已修改"));
            var candidate = LoadLocale(assets, scope).GetAwaiter().GetResult();
            var config = new Config("foundation.new_title", 10);
            Assert.Throws<InvalidDataException>(() =>
                FoundationWindowBinder.ValidateContentAsync(assets, config, active, scope).GetAwaiter().GetResult());
            FoundationWindowBinder.ValidateContentAsync(assets, config, candidate, scope).GetAwaiter().GetResult();
            Assert.That(candidate.Resolve("foundation.new_title"), Is.EqualTo("新标题"));
            Assert.That(candidate.Resolve("foundation.message"), Is.EqualTo("已修改"));
            Assert.That(active.Resolve("foundation.message"), Is.EqualTo("配置加载成功"));
            Assert.Throws<KeyNotFoundException>(() => active.Resolve("foundation.new_title"));
        }

        private static async Task<ILocaleService> LoadLocale(Assets assets, BindingScope scope) =>
            new LocaleService(await DemoMessages.LoadAsync(new JsonConfigService(assets), scope), "zh-Hans", "ja", "ja");

        private static void Validate(Assets assets, Config config)
        {
            using var scope = new BindingScope(default);
            var locale = LoadLocale(assets, scope).GetAwaiter().GetResult();
            FoundationWindowBinder.ValidateContentAsync(assets, config, locale, scope).GetAwaiter().GetResult();
        }

        private sealed class Assets : IAssetService
        {
            private readonly string locale;
            private readonly string sample;

            public Assets(string locale = ActiveMessages, string sample = "AstralOath") =>
                (this.locale, this.sample) = (locale, sample);

            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                string contents = address == DemoMessages.Address ? locale : sample;
                var asset = new TextAsset(contents);
                scope.Add(() => UnityEngine.Object.DestroyImmediate(asset));
                return Task.FromResult((T)(UnityEngine.Object)asset);
            }
        }

        private sealed class Config : IConfigService
        {
            private readonly string title;
            private readonly int duration;

            public Config(string title = "foundation.title", int duration = 5) =>
                (this.title, this.duration) = (title, duration);

            public Task<T> ReadAsync<T>(string address, BindingScope scope) where T : class =>
                Task.FromResult(JsonUtility.FromJson<T>(
                    "{\"rows\":[{\"id\":1,\"titleKey\":\"" + title + "\",\"messageKey\":\"foundation.message\"," +
                    "\"displaySeconds\":" + duration + ",\"assetAddress\":\"foundation/demo-config\"}]}"));
        }
    }
}
