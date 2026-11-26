using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.FoundationDemo;
using AstralOath.Client.Mail;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class GmMailWireTests
    {
        private sealed class Transport : HttpMessageHandler
        {
            internal string payload, sent;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                sent = request.Content == null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) };
            }
        }
        [UnityTest] public IEnumerator PlayerPageReadAndClaimPreserveEightNestedBundles()
        {
            var task = CheckPlayer(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
        }
        [UnityTest] public IEnumerator GmPreviewAndReceiptPreserveTreeAndCompoundFields()
        {
            var task = CheckGm(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult();
        }
        private static MailReward Leaf() => new MailReward { kind = "currency", key = "bookmarks", name = "誓约书签", quantity = 137 };
        private static MailReward Tree()
        {
            var reward = Leaf();
            // Server FreezeAttachment accepts depth 0..8: eight bundles followed by a leaf.
            for (int depth = 7; depth >= 0; depth--)
                reward = new MailReward { kind = "bundle", key = "bundle_" + depth, name = "礼包" + depth, quantity = 1, children = new[] { reward } };
            return reward;
        }
        private static MailLetter Letter() => new MailLetter { deliveryId = "delivery", letterType = "gm", title = "深层礼包", body = "<b>纯文本</b>", attachments = new[] { Tree() }, rewards = new[] { Leaf() } };
        private static MailSummary Summary() => new MailSummary { playerId = "owner", realmId = "realm", effectiveCount = 1, version = 3,
            energy = new MailEnergy { balance = 55, revision = 8 }, balances = new MailBalances { gold = 12345, revision = 9 } };
        private static void AssertTree(MailReward reward)
        {
            for (int depth = 0; depth < 8; depth++)
            {
                Assert.That(reward, Is.Not.Null, "Missing bundle at depth " + depth);
                Assert.That(reward.key, Is.EqualTo("bundle_" + depth));
                Assert.That(reward.children, Is.Not.Null, "Missing children at depth " + depth);
                Assert.That(reward.children.Length, Is.EqualTo(1), "Truncated bundle at depth " + depth);
                reward = reward.children[0];
            }
            Assert.That(reward.key, Is.EqualTo("bookmarks")); Assert.That(reward.quantity, Is.EqualTo(137));
        }
        private static async Task CheckPlayer()
        {
            var transport = new Transport();
            using var api = new MailApi(new Uri("http://127.0.0.6:7728"), _ => Task.FromResult("fixture-only"), transport);
            int typeTreeWarnings = 0;
            Application.LogCallback log = (message, stack, type) => { if (message.Contains("Serialization depth limit")) typeTreeWarnings++; };
            Application.logMessageReceived += log;
            try
            {
                transport.payload = JsonConvert.SerializeObject(new MailPage { items = new[] { Letter() }, summary = Summary() });
                var page = await api.ListAsync(false, null, CancellationToken.None);
                AssertTree(page.items[0].attachments[0]); Assert.That(page.summary.balances.gold, Is.EqualTo(12345));
                transport.payload = JsonConvert.SerializeObject(Letter());
                var letter = await api.ReadAsync("delivery", CancellationToken.None); AssertTree(letter.attachments[0]);
                transport.payload = JsonConvert.SerializeObject(new MailClaimReceipt { requestId = "claim", claimed = new[] { "delivery" }, rewards = new[] { Leaf() }, summary = Summary() });
                var claim = await api.ClaimAsync(new MailClaimRequest { requestId = "claim", deliveryId = "delivery" }, CancellationToken.None);
                Assert.That(claim.rewards[0].quantity, Is.EqualTo(137)); Assert.That(claim.summary.energy.balance, Is.EqualTo(55));
                Assert.That(transport.sent.Contains("dispatched"), Is.False, "Client transport state is not a protocol field");
                Assert.That(typeTreeWarnings, Is.EqualTo(0), "Wire parsing must not build Unity's recursive native TypeTree");
            }
            finally { Application.logMessageReceived -= log; }
        }
        private static async Task CheckGm()
        {
            var transport = new Transport();
            using var api = new DevelopmentGmMailApi(new Uri("http://127.0.0.6:7728"), _ => Task.FromResult("fixture-only"), transport);
            int typeTreeWarnings = 0;
            Application.LogCallback log = (message, stack, type) => { if (message.Contains("Serialization depth limit")) typeTreeWarnings++; };
            Application.logMessageReceived += log;
            try
            {
                transport.payload = JsonConvert.SerializeObject(new GmMailPreview { previewId = "preview", playerId = "owner", realmId = "realm", recipientCount = 1, mail = Letter(), totalRewards = new[] { Leaf() } });
                var preview = await api.PreviewAsync(new GmMailDraft { title = "深层礼包" }, CancellationToken.None);
                AssertTree(preview.mail.attachments[0]); Assert.That(preview.totalRewards[0].quantity, Is.EqualTo(137));
                transport.payload = JsonConvert.SerializeObject(new GmMailReceipt { requestId = "send", deliveryId = "delivery", summary = Summary() });
                var receipt = await api.SendAsync(new GmMailSendRequest { requestId = "send", previewId = "preview" }, CancellationToken.None);
                Assert.That(receipt.summary.balances.gold, Is.EqualTo(12345)); Assert.That(receipt.summary.energy.balance, Is.EqualTo(55));
                Assert.That(typeTreeWarnings, Is.EqualTo(0), "Wire parsing must not build Unity's recursive native TypeTree");
            }
            finally { Application.logMessageReceived -= log; }
        }
    }
}
