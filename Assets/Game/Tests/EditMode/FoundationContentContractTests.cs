using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
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

        [TestCase("open")]
        [TestCase("click")]
        [TestCase("success")]
        [TestCase("failure")]
        public void CandidateRejectsBrokenDisplayContract(string branch)
        {
            string title = branch == "open" ? "foundation.echo.ok" : "foundation.title";
            string pending = branch == "click" ? "missing.pending" : "foundation.pending";
            string success = branch == "success" ? "missing.success" : "foundation.echo.ok";
            string failure = branch == "failure" ? "foundation.echo.ok" : "foundation.echo.error";
            try { Validate(title, pending, success, failure); }
            catch (InvalidDataException) { return; }
            Assert.Fail("Candidate accepted an invalid " + branch + " display contract.");
        }

        [Test]
        public void CandidateAcceptsValidDisplayContract()
        {
            Validate("foundation.title", "foundation.pending", "foundation.echo.ok", "foundation.echo.error");
        }

        [Test]
        public void CandidateAcceptsBinderWithOneCompletionPerClick()
        {
            Validate("foundation.title", "foundation.pending", "foundation.echo.ok",
                "foundation.echo.error", stateful: true);
        }

        [Test]
        public void CandidateRejectsMessageThatExceedsProtobufLimitAfterUtf8Encoding()
        {
            Assert.Throws<InvalidDataException>(() => Validate("foundation.title", "foundation.pending",
                "foundation.echo.ok", "foundation.echo.error", new string('星', 2716) + "AB"));
        }

        [Test]
        public void CandidateAcceptsChineseMessageWithinProtobufLimit()
        {
            Validate("foundation.title", "foundation.pending", "foundation.echo.ok",
                "foundation.echo.error", new string('星', 2716) + "A");
        }

        [Test]
        public void CandidateUsesItsOwnNewKeyAndLeavesActiveMessagesUntouched()
        {
            using var scope = new BindingScope(default);
            var active = LoadLocale(new Assets(), scope).GetAwaiter().GetResult();
            var assets = new Assets(ActiveMessages.Replace("foundation.title", "foundation.new_title")
                .Replace("基础设施演示", "新标题").Replace("配置加载成功", "已修改"));
            var candidate = LoadLocale(assets, scope).GetAwaiter().GetResult();
            using var binder = new FoundationLua(@"return {
                open = function(title, message, duration, sample) return title, message, 'foundation.ready', sample, duration end,
                click = function() return 'foundation.pending', 'AstralOath' end,
                completed = function(state, message, sequence)
                    if state == 'ok' then return 'foundation.echo.ok', message, sequence end
                    return 'foundation.echo.error'
                end
            }");
            var config = new Config("foundation.new_title", 10);
            Assert.Throws<InvalidDataException>(() =>
                DemoWindow.ValidateContentAsync(assets, config, binder, active, scope).GetAwaiter().GetResult());
            Task validation = DemoWindow.ValidateContentAsync(assets, config, binder, candidate, scope);
            Assert.That(validation.IsCompleted, Is.True, "Fixture providers must complete synchronously.");
            validation.GetAwaiter().GetResult();
            Assert.That(candidate.Resolve("foundation.new_title"), Is.EqualTo("新标题"));
            Assert.That(candidate.Resolve("foundation.message"), Is.EqualTo("已修改"));
            Assert.That(active.Resolve("foundation.message"), Is.EqualTo("配置加载成功"));
            Assert.Throws<KeyNotFoundException>(() => active.Resolve("foundation.new_title"));
        }

        private static async Task<ILocaleService> LoadLocale(Assets assets, BindingScope scope) =>
            new LocaleService(await DemoMessages.LoadAsync(new JsonConfigService(assets), scope), "zh-Hans", "ja", "ja");

        private static void Validate(string title, string pending, string success, string failure,
            string requestMessage = "AstralOath", bool stateful = false)
        {
            string requirePending = stateful ? "if not pending then return 'missing.lifecycle' end" : "";
            using var lua = new FoundationLua($@"local pending = false
            return {{
                open = function(_, message, duration, sample) return '{title}', message, 'foundation.ready', sample, duration end,
                click = function() pending = true; return '{pending}', '{requestMessage}' end,
                completed = function(state, message, sequence)
                    {requirePending}
                    pending = false
                    if state == 'ok' then return '{success}', message, sequence end
                    return '{failure}'
                end
            }}");
            using var scope = new BindingScope(default);
            var assets = new Assets();
            Task validation = DemoWindow.ValidateContentAsync(assets, new Config(), lua, LoadLocale(assets, scope).GetAwaiter().GetResult(), scope);
            Assert.That(validation.IsCompleted, Is.True, "Fixture providers must complete synchronously.");
            validation.GetAwaiter().GetResult();
        }

        private sealed class Assets : IAssetService
        {
            private readonly string locale;

            public Assets(string locale = ActiveMessages) => this.locale = locale;

            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                string contents = address == DemoMessages.Address ? locale : "AstralOath";
                var sample = new TextAsset(contents);
                scope.Add(() => UnityEngine.Object.DestroyImmediate(sample));
                return Task.FromResult((T)(UnityEngine.Object)sample);
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
