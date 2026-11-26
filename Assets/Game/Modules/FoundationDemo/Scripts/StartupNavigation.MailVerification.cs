using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        private Func<CancellationToken, Task<string>> mailVerificationBearer;

        internal static bool MailVerificationRequested => MailVerificationEnabled(Application.isEditor,
            Debug.isDebugBuild, Environment.GetCommandLineArgs());

        internal static bool MailVerificationEnabled(bool editor, bool development, string[] arguments) =>
            !editor && development && arguments != null && Array.IndexOf(arguments, "-astral-mail-smoke") >= 0;

        internal static void CheckMailVerificationTarget(Uri target)
        {
            if (target == null || !target.IsAbsoluteUri || !target.IsLoopback ||
                (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Mail player verification requires an isolated loopback server.");
        }

        internal void ConfigureMailVerification(Func<CancellationToken, Task<string>> bearer)
        {
            if (!MailVerificationRequested) throw new InvalidOperationException("Mail player verification is not requested.");
            CheckMailVerificationTarget(endpoint);
            mailVerificationBearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
        }

        internal void StartMailVerification(CancellationToken cancellation) => _ = RunMailVerificationAndQuitAsync(cancellation);

        private async Task RunMailVerificationAndQuitAsync(CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(4));
            try
            {
                Application.runInBackground = true;
                await Task.Yield();
                await VerifyMailAsync(deadline.Token);
                Application.Quit(0);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                Debug.LogException(error);
                Application.Quit(1);
            }
        }

        // This explicit Development diagnostic prepares only its current, initially empty QA mailbox.
        // GM-composer sending and every read/claim below use the real installed UI and current session.
        internal async Task VerifyMailAsync(CancellationToken cancellation)
        {
            if (!MailVerificationRequested || mailVerificationBearer == null)
                throw new InvalidOperationException("Mail player verification requires an explicit development session.");
            CheckMailVerificationTarget(endpoint);
            string output = MailVerificationDirectory();
            if (File.Exists(Path.Combine(output, "mail-player.json")))
                throw new IOException("Use a new mail verification directory; evidence is not overwritten.");
            using var gm = new DevelopmentGmApi(endpoint, mailVerificationBearer);
            using var sending = new DevelopmentGmMailApi(endpoint, mailVerificationBearer);
            await mailSession.RefreshAsync(cancellation);
            var initial = mailSession.Summary;
            if (initial == null || initial.effectiveCount != 0)
                throw new InvalidOperationException("Mail player verification requires a new QA account with an empty inbox.");
            var before = await gm.SendAsync(null, cancellation);
            if (before.playerId != initial.playerId || before.realmId != initial.realmId)
                throw new InvalidDataException("Mail and GM must use the same authenticated QA owner.");
            string run = Guid.NewGuid().ToString("N").Substring(0, 12);
            var homeBefore = MailVerificationPage("HomeWindow");
            if (!homeBefore) throw new InvalidOperationException("Mail verification must begin on the normal home page.");

            // Actual GM UI: template -> editable draft -> frozen self-only preview -> send.
            var gmMap = MailVerificationPage("DevelopmentGmWindow");
            if (!gmMap) throw new InvalidOperationException("Development GM is missing from the installed player.");
            MailVerificationClick(gmMap, "entry");
            MailVerificationClick(gmMap, "mail");
            await MailVerificationUntil(() => gmMap.Require<ScrollRect>("mailTemplates").content
                .GetComponentsInChildren<GameButton>().Any(b => b.gameObject.activeInHierarchy), "GM mail catalog", cancellation);
            var maintenance = gmMap.Require<ScrollRect>("mailTemplates").content.GetComponentsInChildren<GameButton>()
                .Single(b => b.GetComponentInChildren<GameText>().text.Contains("维护补偿"));
            maintenance.onClick.Invoke();
            string uiTitle = "邮件原生验收 " + run;
            gmMap.Require<TMP_InputField>("mailTitle").text = uiTitle;
            await CaptureMailVerification("01-gm-compose", cancellation);
            MailVerificationClick(gmMap, "mailPreviewMake");
            await MailVerificationUntil(() => gmMap.Require<GameObject>("mailPreview").activeInHierarchy &&
                gmMap.Require<GameButton>("mailSend").interactable, "frozen GM preview", cancellation);
            string frozen = gmMap.Require<GameText>("mailFrozenText").text;
            if (!frozen.Contains(initial.playerId) || !frozen.Contains(initial.realmId))
                throw new InvalidDataException("The frozen GM preview does not identify the current owner.");
            await CaptureMailVerification("02-gm-preview", cancellation);
            MailVerificationClick(gmMap, "mailSend");
            await MailVerificationUntil(() => gmMap.Require<GameButton>("mailAnother").gameObject.activeInHierarchy &&
                mailSession.Summary.effectiveCount == 1, "GM self-send receipt", cancellation);
            await CaptureMailVerification("03-gm-sent", cancellation);
            var sentOnly = await gm.SendAsync(null, cancellation);
            if (before.resources.Length != sentOnly.resources.Length || before.resources.Any(resource =>
                !sentOnly.resources.Any(value => value.key == resource.key && value.value == resource.value)))
                throw new InvalidDataException("Sending a mail changed resources before claiming.");
            var inbox = await mailSession.Api.ListAsync(false, null, cancellation);
            var uiMail = inbox.items.Single(m => m.title == uiTitle);
            if (uiMail.letterType != "standard" || !uiMail.bulkEligible)
                throw new InvalidDataException("The maintenance template did not retain its configured standard type.");
            MailVerificationClick(gmMap, "close");

            // The remaining source-video states are prepared through the same self-only API.
            // This is fixture preparation, not a claim or an ops/all-player delivery.
            var catalog = await sending.CatalogAsync(cancellation);
            var gift = await MailVerificationSendFixture(sending, catalog, "gm_gift", run, null, cancellation);
            var energy = await MailVerificationSendFixture(sending, catalog, "event_reward", run, null, cancellation);
            var gold = await MailVerificationSendFixture(sending, catalog, "maintenance", run,
                new[] { new GmMailAttachment { kind = "material", key = "gold", quantity = 10000 } }, cancellation);
            await mailSession.RefreshAsync(cancellation);
            if (mailSession.Summary.effectiveCount != 4)
                throw new InvalidDataException("Exactly four QA letters must be present before opening the inbox.");
            int giftClaimReceipts = 0;
            using var receiptWatch = new BindingScope(cancellation);
            void CountGiftReceipt(MailClaimReceipt receipt)
            {
                if (receipt.claimed != null && receipt.claimed.Contains(gift.deliveryId)) giftClaimReceipts++;
            }
            mailSession.Claimed += CountGiftReceipt;
            receiptWatch.Add(() => mailSession.Claimed -= CountGiftReceipt);
            Debug.Log("ASTRAL_MAIL_FIXTURE_SELF_ONLY count=4 uiSent=1 apiPrepared=3 rewardsClaimed=0");
            var header = MailVerificationHeader();
            if (header.Require<GameText>("count").text != "4" || !header.Require<GameObject>("counter").activeSelf)
                throw new InvalidDataException("The home mail counter did not update to four.");
            await CaptureMailVerification("04-home-counter", cancellation);
            MailVerificationClick(header, "button");
            await MailVerificationUntil(() => mailView != null && mailView.Root.activeInHierarchy &&
                MailVerificationRow(gift.deliveryId), "populated inbox", cancellation);
            var map = mailView.Map;
            await CaptureMailVerification("05-inbox", cancellation);

            var giftRow = MailVerificationRow(gift.deliveryId);
            MailVerificationClick(giftRow, "attachment");
            await MailVerificationUntil(() => map.Require<GameObject>("tooltip").activeInHierarchy, "attachment tooltip", cancellation);
            await CaptureMailVerification("06-attachment-tooltip", cancellation);
            MailVerificationClick(map, "tooltipClose");
            MailVerificationClick(giftRow, "action");
            await MailVerificationUntil(() => map.Require<GameObject>("detail").activeInHierarchy, "GM letter body", cancellation);
            await CaptureMailVerification("07-gm-detail", cancellation);
            if (mailSession.Summary.effectiveCount != 4)
                throw new InvalidDataException("Reading a letter must not reduce the actionable counter.");
            MailVerificationClick(map, "detailClaim");
            await MailVerificationUntil(() => mailSession.Summary.effectiveCount == 3 &&
                map.Require<GameObject>("reward").activeInHierarchy && map.Require<GameObject>("rewardCoverCard").activeInHierarchy,
                "one claimed GM gift and its illustration stage", cancellation);
            await CaptureMailVerification("08-gift-cover", cancellation);
            MailVerificationClick(map, "rewardClose");
            await MailVerificationUntil(() => !map.Require<GameObject>("rewardCoverCard").activeSelf &&
                map.Require<RectTransform>("rewardItems").gameObject.activeInHierarchy, "gift item stage", cancellation);
            await CaptureMailVerification("09-gift-items", cancellation);
            if (mailSession.Summary.effectiveCount != 3)
                throw new InvalidDataException("Changing gift presentation stages must not claim another letter.");
            MailVerificationClick(map, "rewardClose");

            await MailVerificationUntil(() => MailVerificationRow(uiMail.deliveryId), "remaining standard mail", cancellation);
            MailVerificationClick(MailVerificationRow(uiMail.deliveryId), "action");
            await MailVerificationUntil(() => mailSession.Summary.effectiveCount == 2 &&
                map.Require<GameObject>("singleReward").activeInHierarchy, "single-item reward strip", cancellation);
            await CaptureMailVerification("10-single-reward", cancellation);
            MailVerificationClick(map, "claimAll");
            await MailVerificationUntil(() => map.Require<GameObject>("confirmation").activeInHierarchy, "bulk confirmation", cancellation);
            await CaptureMailVerification("11-claim-all-confirm", cancellation);
            MailVerificationClick(map, "confirmOk");
            await MailVerificationUntil(() => mailSession.Summary.effectiveCount == 0 && map.Require<GameObject>("reward").activeInHierarchy,
                "authoritative bulk receipt", cancellation);
            await CaptureMailVerification("12-claim-all-reward", cancellation);
            MailVerificationClick(map, "rewardClose");
            MailVerificationClick(map, "history");
            await MailVerificationUntil(() => MailVerificationRow(gift.deliveryId) && MailVerificationRow(energy.deliveryId) &&
                MailVerificationRow(gold.deliveryId) && MailVerificationRow(uiMail.deliveryId), "all four claimed history rows", cancellation);
            await CaptureMailVerification("13-history", cancellation);
            MailVerificationClick(MailVerificationRow(gift.deliveryId), "action");
            await MailVerificationUntil(() => map.Require<GameObject>("detail").activeInHierarchy, "replayed GM letter", cancellation);
            MailVerificationClick(map, "detailClaim");
            await Task.Delay(150, cancellation);
            if (mailSession.Summary.effectiveCount != 0) throw new InvalidDataException("History replay changed the mailbox count.");
            MailVerificationClick(map, "detailClose");
            MailVerificationClick(map, "back");
            await MailVerificationUntil(() => mailPortal && !mailPortal.activeInHierarchy, "return to the original home page", cancellation);
            if (MailVerificationPage("HomeWindow") != homeBefore || !homeBefore.gameObject.activeInHierarchy)
                throw new InvalidDataException("Closing mail rebuilt or failed to restore the original page.");
            if (header.Require<GameObject>("counter").activeSelf)
                throw new InvalidDataException("The home counter must hide when the inbox is empty.");
            MailVerificationClick(header, "button");
            await MailVerificationUntil(() => mailView.Root.activeInHierarchy && map.Require<GameText>("empty").text.Length > 0,
                "reopened empty inbox", cancellation);
            await CaptureMailVerification("14-empty-reopen", cancellation);

            var after = await gm.SendAsync(null, cancellation);
            long Balance(GmSnapshot value, string key) => value.resources.Single(r => r.key == key).value;
            if (Balance(after, "bookmarks") - Balance(before, "bookmarks") != 10 ||
                Balance(after, "epicCharm") - Balance(before, "epicCharm") != 2 ||
                Balance(after, "greaterCharm") - Balance(before, "greaterCharm") != 5 ||
                Balance(after, "gold") - Balance(before, "gold") != 10000 ||
                Balance(after, "energy") - Balance(before, "energy") != 100)
                throw new InvalidDataException("Claimed resources disagree with the four frozen QA letters or were granted twice.");
            await mailSession.RefreshAsync(cancellation);
            var final = mailSession.Summary;
            if (final.energy.balance - initial.energy.balance != 100 || final.balances.gold - initial.balances.gold != 10000 ||
                !map.Require<GameText>("energy").text.Contains(final.energy.balance.ToString("N0", CultureInfo.InvariantCulture)) ||
                map.Require<GameText>("gold").text != final.balances.gold.ToString("N0", CultureInfo.InvariantCulture))
                throw new InvalidDataException("The mailbox header is stale after the real resource claim.");
            var history = await mailSession.Api.ListAsync(true, null, cancellation);
            string[] ids = { uiMail.deliveryId, gift.deliveryId, energy.deliveryId, gold.deliveryId };
            if (ids.Any(id => !history.items.Any(m => m.deliveryId == id && m.outcome == "claimed")))
                throw new InvalidDataException("All four claimed letters must be persisted in history.");
            if (giftClaimReceipts != 1)
                throw new InvalidDataException("Both GM gift presentation stages must share one successful claim receipt.");
            int captureCount = Directory.GetFiles(output, "*.png").Length;
            if (captureCount != 14) throw new InvalidDataException("The native mail run must retain fourteen states.");
            var receipt = new MailPlayerVerificationReceipt { appVersion = Application.version,
                endpoint = endpoint.AbsoluteUri, playerId = final.playerId, realmId = final.realmId,
                width = Screen.width, height = Screen.height, initial = initial, final = final,
                deliveryIds = ids, uiSent = 1, fixtureApiSent = 3, claimed = ids.Length, giftClaimReceipts = giftClaimReceipts,
                bookmarksDelta = Balance(after,"bookmarks") - Balance(before,"bookmarks"),
                epicCharmDelta = Balance(after,"epicCharm") - Balance(before,"epicCharm"),
                greaterCharmDelta = Balance(after,"greaterCharm") - Balance(before,"greaterCharm"),
                goldDelta = Balance(after,"gold") - Balance(before,"gold"), energyDelta = Balance(after,"energy") - Balance(before,"energy"),
                samePageRestored = true, reopened = true, captureCount = captureCount, historyVerified = true };
            File.WriteAllText(Path.Combine(output, "mail-player.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("ASTRAL_MAIL_PLAYER_OK uiSent=1 apiPrepared=3 claimed=4 count=0 giftClaims=1 goldDelta=10000 energyDelta=100 history=True reopened=True screen=" + Screen.width + "x" + Screen.height);
        }

        private async Task<GmMailReceipt> MailVerificationSendFixture(DevelopmentGmMailApi sending, GmMailCatalog catalog,
            string templateId, string run, GmMailAttachment[] attachments, CancellationToken cancellation)
        {
            var template = catalog.templates.Single(t => t.id == templateId);
            var preview = await sending.PreviewAsync(new GmMailDraft { templateId = template.id, letterType = template.letterType,
                senderId = template.senderId, title = template.title + " " + run, body = template.body,
                expiresInDays = template.expiresInDays, attachments = attachments ?? template.attachments }, cancellation);
            if (preview.playerId != mailSession.Summary.playerId || preview.realmId != mailSession.Summary.realmId || preview.recipientCount != 1)
                throw new InvalidDataException("QA fixture preview targeted a different owner.");
            var receipt = await sending.SendAsync(new GmMailSendRequest { requestId = Guid.NewGuid().ToString("N"), previewId = preview.previewId }, cancellation);
            mailSession.ApplySummary(receipt.summary);
            return receipt;
        }

        private BindingMap MailVerificationPage(string name) => parent.GetComponentsInChildren<BindingMap>(true)
            .FirstOrDefault(map => map.name == name || map.name == name + "(Clone)");

        private BindingMap MailVerificationHeader() => parent.GetComponentsInChildren<BindingMap>()
            .First(map => map.name == "MailEntry" && (!mailPortal || !map.transform.IsChildOf(mailPortal.transform)));

        private BindingMap MailVerificationRow(string id) => mailView?.Map.Require<ScrollRect>("scroll").content
            .GetComponentsInChildren<BindingMap>().FirstOrDefault(map => map.name == "Mail " + id);

        private static void MailVerificationClick(BindingMap map, string key)
        {
            if (!map) throw new InvalidOperationException("Missing mail verification map for " + key);
            var button = map.Require<GameButton>(key);
            if (!button.isActiveAndEnabled || !button.IsInteractable()) throw new InvalidOperationException("Unavailable mail verification button: " + key);
            button.onClick.Invoke();
        }

        private static async Task MailVerificationUntil(Func<bool> ready, string step, CancellationToken cancellation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 25;
            while (!ready())
            {
                cancellation.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("Mail player verification timed out: " + step);
                await Task.Delay(40, cancellation);
            }
        }

        private static string MailVerificationDirectory()
        {
            string value = Environment.GetEnvironmentVariable("ASTRAL_MAIL_CAPTURE_DIR");
            if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("Mail verification capture directory is required.");
            string path = Path.GetFullPath(value);
            if (!path.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mail evidence must stay on D drive.");
            Directory.CreateDirectory(path);
            return path;
        }

        private static async Task CaptureMailVerification(string name, CancellationToken cancellation)
        {
            string path = Path.Combine(MailVerificationDirectory(), name + ".png");
            if (File.Exists(path)) throw new IOException("Mail capture already exists: " + name);
            await Task.Delay(260, cancellation);
            ScreenCapture.CaptureScreenshot(path);
            await MailVerificationUntil(() => File.Exists(path) && new FileInfo(path).Length > 1024, "capture " + name, cancellation);
            await Task.Delay(80, cancellation);
        }

        [Serializable]
        private sealed class MailPlayerVerificationReceipt
        {
            public string appVersion, endpoint, playerId, realmId;
            public int width, height, uiSent, fixtureApiSent, claimed, giftClaimReceipts, captureCount;
            public long bookmarksDelta, epicCharmDelta, greaterCharmDelta, goldDelta, energyDelta;
            public bool samePageRestored, reopened, historyVerified;
            public MailSummary initial, final;
            public string[] deliveryIds;
        }
    }
}
