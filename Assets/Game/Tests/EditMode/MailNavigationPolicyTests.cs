using System;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class MailNavigationPolicyTests
    {
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        public void VerificationRequiresNativeDevelopmentAndAnExplicitFlag(bool editor, bool development, bool expected)
        {
            Assert.That(StartupNavigation.MailVerificationEnabled(editor, development, new[] { "game.exe", "-astral-mail-smoke" }), Is.EqualTo(expected));
            Assert.That(StartupNavigation.MailVerificationEnabled(editor, development, new[] { "game.exe" }), Is.False);
        }

        [TestCase("http://127.0.0.6:7728", true)]
        [TestCase("http://localhost:7728", true)]
        [TestCase("https://example.com", false)]
        [TestCase("file:///D:/mail", false)]
        public void VerificationNeverTargetsAnExternalOrFileServer(string uri, bool allowed)
        {
            if (allowed) Assert.DoesNotThrow(() => StartupNavigation.CheckMailVerificationTarget(new Uri(uri)));
            else Assert.Throws<InvalidOperationException>(() => StartupNavigation.CheckMailVerificationTarget(new Uri(uri)));
        }
    }
}
