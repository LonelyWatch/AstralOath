using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class FoundationContentContractTests
    {
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
            Task validation = DemoWindow.ValidateContentAsync(new Assets(), new Config(), lua, new Locale(), scope);
            Assert.That(validation.IsCompleted, Is.True, "Fixture providers must complete synchronously.");
            validation.GetAwaiter().GetResult();
        }

        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                var sample = new TextAsset("AstralOath");
                scope.Add(() => UnityEngine.Object.DestroyImmediate(sample));
                return Task.FromResult((T)(UnityEngine.Object)sample);
            }
        }

        private sealed class Config : IConfigService
        {
            public Task<T> ReadAsync<T>(string address, BindingScope scope) where T : class =>
                Task.FromResult(JsonUtility.FromJson<T>(
                    "{\"rows\":[{\"id\":1,\"titleKey\":\"foundation.title\",\"messageKey\":\"foundation.message\"," +
                    "\"displaySeconds\":5,\"assetAddress\":\"foundation/demo-config\"}]}"));
        }

        private sealed class Locale : ILocaleService
        {
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event System.Action TextChanged { add { } remove { } }
            public void SelectText(string locale) { }
            public void SelectVoice(string locale) { }
            public string Resolve(string key)
            {
                if (DemoMessages.Text[TextLocale].TryGetValue(key, out string message))
                    return message;
                throw new KeyNotFoundException(key);
            }
        }
    }
}