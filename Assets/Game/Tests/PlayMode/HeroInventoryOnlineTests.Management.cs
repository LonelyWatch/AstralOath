#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
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
        public IEnumerator WornBagFiltersOpensTheWearerAndReturnsToItsRows()
        {
            string url = Environment.GetEnvironmentVariable("ASTRAL_EQUIPMENT_TEST_URL");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a loopback service with training equipment.");
            using var scope = new AstralOath.Client.Core.BindingScope(CancellationToken.None);
            using var arena = new Arena.ArenaApi(endpoint, scope.Token);
            var login = arena.GetSessionTokenAsync("Worn bag QA", scope.Token);
            yield return Wait(() => login.IsCompleted, "Guest login did not complete.");
            string bearer = login.GetAwaiter().GetResult();
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(endpoint, Token, scope.Token);
            using var api = new EquipmentInventoryApi(endpoint, Token, scope.Token);
            var opening = OpenArtifactWindow(false, scope, heroes, null, null, api);
            yield return Wait(() => opening.IsCompleted, "The worn bag did not open.");
            var page = opening.GetAwaiter().GetResult();
            var filters = page.Require<BindingMap>("wornFilters");
            Click(page, "bagSort");
            Assert.That(filters.gameObject.activeInHierarchy, Is.True);
            Click(filters.Require<BindingMap>("element_ice"), "button");
            Assert.That(VisibleRows(page, "bagScroll").Single().Require<GameText>("name").text, Is.EqualTo("安洁莉卡"));
            Click(filters.Require<BindingMap>("element_all"), "button");
            Click(filters, "sort_acquired");
            if (filters.gameObject.activeSelf) Click(filters, "close");
            Click(page, "filter_knight");
            var row = VisibleRows(page, "bagScroll").Single();
            Assert.That(row.Require<GameText>("name").text, Is.EqualTo("拉斯"));
            Click(row, "bagManage");
            var browser = page.Require<BindingMap>("gearBrowser");
            Assert.That(browser.Require<GameObject>("pickerSurface").activeInHierarchy, Is.True);
            Click(browser, "close");
            Assert.That(page.Require<GameObject>("roster").activeInHierarchy, Is.True);
            Assert.That(page.Require<GameText>("detailName").text, Is.EqualTo("拉斯"));
            Click(page, "back");
            Assert.That(page.Require<GameObject>("bag").activeInHierarchy, Is.True);
            Assert.That(VisibleRows(page, "bagScroll").Single().name, Is.EqualTo(row.name));
            Click(page, "filter_all");
            Assert.That(VisibleRows(page, "bagScroll").Length, Is.EqualTo(3));
            Click(page, "bagSort");
            Assert.That(filters.gameObject.activeSelf, Is.True);
            Click(filters, "close");
        }

        [UnityTest]
        public IEnumerator EquipmentManagerPreviewsCancelsAndConfirmsOneRealChange()
        {
            var url = Environment.GetEnvironmentVariable("ASTRAL_EQUIPMENT_TEST_URL");
            if (!Uri.TryCreate(url,UriKind.Absolute,out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a loopback service with seven training items.");
            using var scope = new AstralOath.Client.Core.BindingScope(CancellationToken.None);
            using var arena = new Arena.ArenaApi(endpoint,scope.Token);
            var login = arena.GetSessionTokenAsync("Gear manager QA",scope.Token);
            yield return Wait(() => login.IsCompleted,"Guest login did not complete.");
            var bearer = login.GetAwaiter().GetResult();
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(endpoint,Token,scope.Token);
            using var equipment = new EquipmentInventoryApi(endpoint,Token,scope.Token);
            var opening = OpenArtifactWindow(true,scope,heroes,null,null,equipment);
            yield return Wait(() => opening.IsCompleted,"Hero screen did not open.");
            var page = opening.GetAwaiter().GetResult();
            var browser = page.Require<BindingMap>("gearBrowser");
            var read = equipment.FetchAsync(scope.Token);
            yield return Wait(() => read.IsCompleted,"Could not read gear.");
            var before = read.GetAwaiter().GetResult();
            var weapons = before.items.Where(i => i.slot == "weapon" && string.IsNullOrEmpty(i.equippedHeroId))
                .OrderBy(i => i.mainStat.value).ToArray();
            var weapon = weapons.Last();
            Click(page,"gearManage");
            Assert.That(browser.Require<GameObject>("pickerSurface").activeInHierarchy,Is.True);
            Assert.That(browser.Require<GameObject>("detail").activeSelf,Is.False);
            Assert.That(browser.Require<GameText>("managePrompt").isActiveAndEnabled,Is.True);
            Assert.That(browser.Require<GameButton>("apply").interactable,Is.False);
            yield return Capture("equipment-manager-before",1478,720);
            Click(browser,"filter_helmet");
            Assert.That(VisibleRows(browser,"items").Length,Is.EqualTo(1));
            Click(browser,"filter_weapon");
            Assert.That(VisibleRows(browser,"items").Length,Is.EqualTo(2));
            Click(VisibleRows(browser,"items").Single(m => m.name == weapon.instanceId),"select");
            var current = browser.Require<BindingMap>("manageCurrent_weapon").Require<Image>("item");
            var preview = browser.Require<BindingMap>("managePreview_weapon").Require<Image>("item");
            Assert.That(current.enabled,Is.False);
            Assert.That(preview.isActiveAndEnabled && preview.sprite,Is.True);
            Assert.That(browser.Require<GameText>("manageDescription").text,Is.Not.Empty);
            Assert.That(int.TryParse(browser.Require<BindingMap>("manageScore").Require<GameText>("value").text,out _),Is.True);
            string proposed = browser.Require<BindingMap>("managePreview_attack").Require<GameText>("value").text;
            var difference = browser.Require<GameText>("manageDelta_attack");
            Assert.That(difference.text,Does.StartWith("+"));
            Assert.That((Color32)difference.color,Is.EqualTo(new Color32(255,149,41,255)));
            yield return Capture("equipment-manager-preview",1478,720);
            Click(browser,"apply");
            Assert.That(browser.Require<GameObject>("managementQuote").activeInHierarchy,Is.True);
            Assert.That(browser.Require<GameText>("manageChange").text,Does.Contain("→"));
            yield return Capture("equipment-manager-confirm",1478,720);
            Click(browser,"manageCancel");
            read = equipment.FetchAsync(scope.Token);
            yield return Wait(() => read.IsCompleted,"Could not verify cancel.");
            Assert.That(read.GetAwaiter().GetResult().version,Is.EqualTo(before.version));
            Assert.That(current.enabled,Is.False);
            Click(browser,"apply");
            Click(browser,"manageConfirm");
            yield return Wait(() => current.enabled && !browser.Require<GameObject>("dialog").activeSelf,"Server did not acknowledge the change.");
            Assert.That(browser.Require<GameObject>("detail").activeSelf,Is.False);
            Assert.That(browser.Require<GameObject>("pickerSurface").activeInHierarchy,Is.True);
            Assert.That(proposed,Is.EqualTo(browser.Require<BindingMap>("manageCurrent_attack").Require<GameText>("value").text));
            read = equipment.FetchAsync(scope.Token);
            yield return Wait(() => read.IsCompleted,"Could not verify stored loadout.");
            var after = read.GetAwaiter().GetResult();
            Assert.That(after.version,Is.EqualTo(before.version + 1));
            Assert.That(after.items.Single(i => i.instanceId == weapon.instanceId).equippedHeroId,Is.Not.Null.And.Not.Empty);
            yield return Capture("equipment-manager-applied",1478,720);
            Click(VisibleRows(browser,"items").Single(m => m.name == weapons.First().instanceId),"select");
            Assert.That(difference.text,Does.StartWith("-"));
            Assert.That((Color32)difference.color,Is.EqualTo(new Color32(56,148,240,255)));
            Click(browser,"manageReset");
            Assert.That(browser.Require<GameObject>("detail").activeSelf,Is.False);
        }
    }
}
#endif
