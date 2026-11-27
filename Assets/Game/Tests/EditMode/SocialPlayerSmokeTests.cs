using System;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class SocialPlayerSmokeTests
    {
        [Test]
        public void DiagnosticCannotRunInEditorOrReleaseOrWithoutExplicitFlag()
        {
            var flag = new[] { "-astral-social-smoke" };
            Assert.That(SocialPlayerSmoke.Enabled(true, true, flag), Is.False);
            Assert.That(SocialPlayerSmoke.Enabled(false, false, flag), Is.False);
            Assert.That(SocialPlayerSmoke.Enabled(false, true, Array.Empty<string>()), Is.False);
            Assert.That(SocialPlayerSmoke.Enabled(false, true, flag), Is.True);
        }

        [TestCase("http://127.0.0.1:7728")]
        [TestCase("http://127.0.0.4:7728")]
        [TestCase("http://localhost:7728")]
        [TestCase("https://example.org")]
        [TestCase("http://user@127.0.0.6:7728")]
        public void DiagnosticRejectsMainAndUnknownServices(string address) =>
            Assert.Throws<InvalidOperationException>(() => SocialPlayerSmoke.CheckTarget(new Uri(address)));

        [Test]
        public void DedicatedQaAddressIsAllowed() =>
            Assert.DoesNotThrow(() => SocialPlayerSmoke.CheckTarget(new Uri("http://127.0.0.6:7728")));
    }
}
