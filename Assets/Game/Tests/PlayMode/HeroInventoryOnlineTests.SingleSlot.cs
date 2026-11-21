#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed partial class HeroInventoryOnlineTests
    {
        [UnityTest]
        public IEnumerator SingleSlotTransferRejectsStaleChoiceAndReplaysLostReceipt()
        {
            string url = Environment.GetEnvironmentVariable("ASTRAL_EQUIPMENT_TEST_URL");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a loopback service with training equipment enabled.");
            using var scope = new BindingScope(CancellationToken.None);
            var token = scope.Token;
            using var arena = new AstralOath.Client.Arena.ArenaApi(endpoint, token);
            var admission = arena.GetSessionTokenAsync("Single slot QA", token);
            yield return Wait(() => admission.IsCompleted, "Guest sign-in timed out.");
            string bearer = admission.GetAwaiter().GetResult();
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(endpoint, Token, token);
            using var local = new EquipmentInventoryApi(endpoint, Token, token);
            using var remote = new EquipmentInventoryApi(endpoint, Token, token);
            var roster = heroes.FetchAsync(token);
            var initial = remote.FetchAsync(token);
            yield return Wait(() => roster.IsCompleted && initial.IsCompleted, "Could not read the test player's inventory.");
            var party = roster.GetAwaiter().GetResult().heroes;
            string receiver = party.Single(h => h.templateId == "c1001").instanceId;
            string donor = party.Single(h => h.templateId == "c1005").instanceId;
            var inventory = initial.GetAwaiter().GetResult();
            var weapon = inventory.items.First(i => i.slot == "weapon");
            var helmet = inventory.items.Single(i => i.slot == "helmet");
            Task<EquipmentInventorySnapshot> EquipRemote(string hero, EquipmentInventoryItem item, long version) =>
                remote.ChangeAsync("equip", Guid.NewGuid().ToString("N"), hero, item.instanceId, item.slot, version, token);
            var seed = EquipRemote(donor, weapon, inventory.version);
            yield return Wait(() => seed.IsCompleted, "Could not prepare the donor's weapon.");
            long version = seed.GetAwaiter().GetResult().version;

            bool loseNextRead = false;
            Task<EquipmentInventorySnapshot> Read(CancellationToken ct)
            {
                if (!loseNextRead) return local.FetchAsync(ct);
                loseNextRead = false;
                return Task.FromException<EquipmentInventorySnapshot>(new IOException("Simulated lost post-write refresh."));
            }
            var open = OpenArtifactWindow(true, scope, heroes, null, null, local, Read);
            yield return Wait(() => open.IsCompleted, "Hero page did not open.");
            var page = open.GetAwaiter().GetResult();
            var browser = page.Require<BindingMap>("gearBrowser");
            var slot = browser.Require<BindingMap>("singleSlot");
            var dialog = browser.Require<GameObject>("dialog");
            var feedback = slot.Require<GameText>("feedback");
            var selected = slot.Require<BindingMap>("candidate");
            BindingMap WeaponRow() => VisibleRows(slot, "candidates").Single(r => r.name == weapon.instanceId);
            void ChooseAndQuote()
            {
                Click(WeaponRow(), "select");
                AssertDescribedScore(selected);
                Click(slot, "equip");
                Assert.That(dialog.activeInHierarchy, Is.True);
                Assert.That(browser.Require<GameText>("change").text, Does.Contain("原佩戴者").And.Contain("梅赛德丝"));
                Assert.That(browser.Require<GameText>("summary").text, Does.Contain("拉斯"));
            }
            Click(page, "gearOpen_weapon");
            ChooseAndQuote();

            // No foreground notification. The old quote reaches the server and receives a real 409.
            var bump = EquipRemote(donor, helmet, version);
            yield return Wait(() => bump.IsCompleted, "Concurrent mutation timed out.");
            version = bump.GetAwaiter().GetResult().version;
            Click(browser, "confirm");
            yield return Wait(() => !dialog.activeSelf && feedback.text.Contains("重新确认"), "Missing conflict feedback.");
            Assert.That(selected.Require<GameObject>("empty").activeInHierarchy, Is.True,
                "The old weapon still exists, but the player must choose it again after a revision conflict.");
            Assert.That(slot.Require<GameButton>("equip").gameObject.activeSelf, Is.False);
            Assert.That(WeaponRow().Require<Image>("highlight").enabled, Is.False);
            var afterConflict = remote.FetchAsync(token);
            yield return Wait(() => afterConflict.IsCompleted, "Could not read the conflict result.");
            var unchanged = afterConflict.GetAwaiter().GetResult();
            Assert.That(unchanged.version, Is.EqualTo(version));
            Assert.That(unchanged.items.Single(i => i.instanceId == weapon.instanceId).equippedHeroId, Is.EqualTo(donor));

            // A newer foreground snapshot changes the actual wearer while the new quote is open.
            ChooseAndQuote();
            var moved = EquipRemote(receiver, weapon, version);
            yield return Wait(() => moved.IsCompleted, "Remote equip timed out.");
            var notified = local.RefreshIfChangedAsync(version, token);
            yield return Wait(() => notified.IsCompleted, "Equipment foreground refresh timed out.");
            notified.GetAwaiter().GetResult();
            version = moved.GetAwaiter().GetResult().version;
            Assert.That(dialog.activeSelf, Is.False);
            Assert.That(feedback.text, Does.Contain("重新确认"));
            Assert.That(selected.Require<GameObject>("empty").activeInHierarchy, Is.True);
            Assert.That(VisibleRows(slot, "candidates").Any(r => r.name == weapon.instanceId), Is.False);
            Assert.That(slot.Require<GameButton>("equip").gameObject.activeSelf, Is.False);
            var equipped = slot.Require<BindingMap>("current");
            Assert.That(equipped.Require<BindingMap>("card").Require<Image>("wearerIcon").isActiveAndEnabled, Is.True);
            AssertDescribedScore(equipped);

            var restored = EquipRemote(donor, weapon, version);
            yield return Wait(() => restored.IsCompleted, "Could not restore donor for a fresh transfer.");
            var resume = local.RefreshIfChangedAsync(version, token);
            yield return Wait(() => resume.IsCompleted, "Second foreground refresh timed out.");
            resume.GetAwaiter().GetResult();
            version = restored.GetAwaiter().GetResult().version;
            Assert.That(feedback.text, Does.Contain("重新确认"), "A repaint erased the unresolved notice.");
            Assert.That(selected.Require<GameObject>("empty").activeInHierarchy, Is.True);
            Click(WeaponRow(), "select");
            Assert.That(feedback.text, Is.Empty);
            Canvas.ForceUpdateCanvases();
            AssertSingleSlotArt(selected);
            AssertSingleSlotArt(WeaponRow());
            Click(slot, "equip");
            Click(browser, "confirm");
            yield return Wait(() => !browser.gameObject.activeSelf, "Transfer did not close the single-slot pane.");
            var applied = remote.FetchAsync(token);
            yield return Wait(() => applied.IsCompleted, "Could not verify the accepted transfer.");
            var accepted = applied.GetAwaiter().GetResult();
            Assert.That(accepted.version, Is.EqualTo(version + 1));
            Assert.That(accepted.items.Single(i => i.instanceId == weapon.instanceId).equippedHeroId, Is.EqualTo(receiver));

            Click(page, "gearOpen_weapon");
            Canvas.ForceUpdateCanvases();
            AssertSingleSlotArt(slot.Require<BindingMap>("popupItem"));
            AssertDescribedScore(slot.Require<BindingMap>("popupItem"));
            loseNextRead = true;
            Click(slot, "popupRemove");
            yield return Wait(() => feedback.text.Contains("请重试"), "A lost post-write refresh needs a visible retry.");
            Assert.That(slot.Require<GameObject>("popup").activeInHierarchy, Is.True);
            var committed = remote.FetchAsync(token);
            yield return Wait(() => committed.IsCompleted, "Could not inspect the committed removal.");
            var firstReceipt = committed.GetAwaiter().GetResult();
            Assert.That(firstReceipt.version, Is.EqualTo(accepted.version + 1));
            Assert.That(firstReceipt.items.Single(i => i.instanceId == weapon.instanceId).equippedHeroId, Is.Null.Or.Empty);
            Click(slot, "popupRemove");
            yield return Wait(() => !browser.gameObject.activeSelf, "The same intent did not recover its original receipt.");
            var replayed = remote.FetchAsync(token);
            yield return Wait(() => replayed.IsCompleted, "Could not verify the idempotent replay.");
            Assert.That(replayed.GetAwaiter().GetResult().version, Is.EqualTo(firstReceipt.version));
        }

        private static void AssertDescribedScore(BindingMap column)
        {
            var description = column.Require<GameText>("description");
            Assert.That(description.isActiveAndEnabled, Is.True);
            Assert.That(description.text, Is.Not.Empty);
            Assert.That(description.text, Does.Not.StartWith("home."));
            var score = column.Require<BindingMap>("score");
            Assert.That(score.Require<GameText>("label").text, Is.EqualTo("装备分数"));
            Assert.That(int.TryParse(score.Require<GameText>("value").text, out int points) && points > 0, Is.True);
        }

        private static void AssertSingleSlotArt(BindingMap owner)
        {
            var art = owner.Require<BindingMap>("card").Require<Image>("icon");
            Assert.That(art.isActiveAndEnabled && art.sprite, Is.True, owner.name);
            var corners = new Vector3[4];
            art.rectTransform.GetWorldCorners(corners);
            var bl = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var tr = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            Assert.That(tr.x - bl.x, Is.GreaterThan(20), owner.name + " drawable width in pixels");
            Assert.That(tr.y - bl.y, Is.GreaterThan(20), owner.name + " drawable height in pixels");
            Assert.That(tr.x - bl.x, Is.EqualTo(tr.y - bl.y).Within(2), owner.name + " square icon");
        }
    }
}
#endif
