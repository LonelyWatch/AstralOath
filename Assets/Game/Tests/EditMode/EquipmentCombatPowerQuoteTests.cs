using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentCombatPowerQuoteTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class Labels : ILocaleService
        {
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add { } remove { } }
            public void SelectText(string value) { }
            public void SelectVoice(string value) { }
            public string Resolve(string key) => key == "home.gear.combatPower" ? "战斗力" :
                key == "home.gear.manageChangeSummary" ? "将变更为如下装备。" : key;
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly BindingMap Map;
            private readonly object binder;
            private readonly Type type;

            internal Fixture()
            {
                Root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab"));
                var page = Root.GetComponent<BindingMap>();
                Map = page.Require<BindingMap>("gearBrowser");
                type = typeof(HeroInventoryBinder).Assembly.GetType("AstralOath.Client.Home.EquipmentInventoryBinder");
                binder = Activator.CreateInstance(type, Hidden, null, new object[]
                {
                    page, new Labels(), null,
                    (Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<EquipmentInventorySnapshot>>)
                        (_ => System.Threading.Tasks.Task.FromResult<EquipmentInventorySnapshot>(null)),
                    (Func<HeroInventoryItem[]>)(() => Array.Empty<HeroInventoryItem>()),
                    (Func<HeroInventoryItem>)(() => null), (Func<string, string>)(id => id),
                    (Func<HeroInventoryItem, BattleStats>)(_ => null),
                    (Func<string, (int attack, int health)>)(_ => (777, 888)),
                    (Func<string, Sprite>)(_ => null), (Action)(() => { })
                }, null);
                type.GetField("targetId", Hidden).SetValue(binder, "target-hero");
            }

            internal void Configure(Func<string, (int stars, int level)> progress)
            {
                var configure = type.GetMethod("ConfigureCombatPower", Hidden);
                Assert.That(configure, Is.Not.Null, "The management page must receive confirmed artifact progress rather than invent a default.");
                configure.Invoke(binder, new object[] { progress });
            }

            internal void Paint(int before, int after, params string[] notes) => type.GetMethod("PaintManagementQuote", Hidden)
                .Invoke(binder, new object[] { Panel(before), Panel(after), new List<string>(notes) });
            private static int[] Panel(int health) => new[] { 0, 0, health, 45, 0, 0, 0, 0 };
            public void Dispose() => UnityEngine.Object.DestroyImmediate(Root);
        }

        private sealed class ArtifactProgressFixture : IDisposable
        {
            private readonly Fixture page = new Fixture();
            private readonly BindingScope scope = new BindingScope(default);
            private readonly object binder;
            private readonly Type type;
            internal readonly ArtifactInventoryApi Api;
            internal readonly ArtifactInventoryItem Item;
            internal Func<Task<ArtifactInventorySnapshot>> Read;

            internal ArtifactProgressFixture()
            {
                Api = new ArtifactInventoryApi(new Uri("http://127.0.0.1:7728"), _ => Task.FromResult("unused"), CancellationToken.None);
                var http = typeof(ArtifactInventoryApi).GetField("http", Hidden);
                ((HttpClient)http.GetValue(Api)).Dispose();
                http.SetValue(Api, new HttpClient(new UncertainArtifactResponse()));
                var assembly = typeof(HeroInventoryBinder).Assembly;
                var bindings = Activator.CreateInstance(assembly.GetType("AstralOath.Client.Home.ArtifactInventoryBindings"),
                    Hidden, null, new object[] { page.Root.GetComponent<BindingMap>() }, null);
                var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
                Item = new ArtifactInventoryItem { instanceId = Guid.NewGuid().ToString("N"), heroInstanceId = "hero",
                    templateId = art.Catalog.Templates.First(template => template.Stars == 5).Id, level = 3 };
                Read = () => Task.FromResult(Snapshot());
                type = assembly.GetType("AstralOath.Client.Home.ArtifactInventoryBinder");
                binder = Activator.CreateInstance(type, Hidden, null, new object[] { bindings, Api, art, new Labels(),
                    (Func<HeroInventoryItem>)(() => null), (Func<string, string>)(_ => "knight"),
                    (Func<string, string>)(id => id), (Action)(() => { }),
                    (Func<CancellationToken, Task<ArtifactInventorySnapshot>>)(_ => Read()), null, null }, null);
                type.GetField("snapshot", Hidden).SetValue(binder, Snapshot());
                type.GetField("selected", Hidden).SetValue(binder, Item);
                type.GetMethod("Bind", Hidden).Invoke(binder, new object[] { scope });
            }

            internal ArtifactInventorySnapshot Snapshot() => new ArtifactInventorySnapshot { capacity = 400, used = 1,
                artifacts = new[] { Item }, materials = new ArtifactInventoryMaterials() };
            internal (int stars, int level) Progress() => ((int, int))type.GetMethod("EquippedProgress", Hidden)
                .Invoke(binder, new object[] { "hero" });
            internal Task Reload() => (Task)type.GetMethod("LoadAsync", Hidden, null,
                new[] { typeof(CancellationToken), typeof(ArtifactInventorySnapshot) }, null)
                .Invoke(binder, new object[] { CancellationToken.None, null });
            internal Task Preview() => (Task)type.GetMethod("PreviewAction", Hidden).Invoke(binder, new object[] { "lock" });
            internal void Back() => type.GetMethod("Back", Hidden).Invoke(binder, null);
            public void Dispose() { scope.Dispose(); Api.Dispose(); page.Dispose(); }
        }

        private sealed class UncertainArtifactResponse : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
        }

        [Test]
        public void KnownArtifactProgressUsesProjectCpAndKeepsTransactionNotes()
        {
            using var fixture = new Fixture();
            string requested = null;
            fixture.Configure(hero => { requested = hero; return (4, 3); });
            fixture.Paint(10000, 12000, "替换原装备", "从另一名英雄转移");
            Assert.That(requested, Is.EqualTo("target-hero"));
            Assert.That(fixture.Map.Require<GameText>("manageQuoteBefore").text, Is.EqualTo("10,495"));
            Assert.That(fixture.Map.Require<GameText>("manageQuoteAfter").text, Is.EqualTo("12,594"));
            Assert.That(fixture.Map.Require<GameText>("manageQuoteDelta").text, Is.EqualTo("2,099"));
            Assert.That(fixture.Map.Require<GameText>("manageQuoteBeforeTitle").text, Is.EqualTo("战斗力"));
            Assert.That(fixture.Map.Require<GameText>("manageQuoteAfterTitle").text, Is.EqualTo("战斗力"));
            Assert.That(fixture.Map.Require<GameText>("manageSummary").text, Is.EqualTo("将变更为如下装备。"));
            Assert.That(fixture.Map.Require<GameText>("manageChange").text, Is.EqualTo("替换原装备\n从另一名英雄转移"));
            Assert.That(fixture.Map.Require<Image>("manageQuoteDirection").enabled, Is.True);
            Assert.That(fixture.Map.Require<Image>("manageQuoteDeltaArrow").enabled, Is.True);
        }

        [Test]
        public void UnconfiguredOrUnknownArtifactProgressShowsUnavailableInsteadOfZero()
        {
            using var fixture = new Fixture();
            void CheckUnknown()
            {
                fixture.Paint(10000, 12000);
                foreach (string key in new[] { "manageQuoteBefore", "manageQuoteAfter", "manageQuoteDelta" })
                    Assert.That(fixture.Map.Require<GameText>(key).text, Is.EqualTo("—"), key);
                Assert.That(fixture.Map.Require<Image>("manageQuoteDeltaArrow").enabled, Is.False);
            }
            CheckUnknown();
            fixture.Configure(_ => (-1, -1));
            CheckUnknown();
            fixture.Configure(_ => (0, 0));
            fixture.Paint(10000, 12000);
            Assert.That(fixture.Map.Require<GameText>("manageQuoteBefore").text, Is.EqualTo("10,000"));
            Assert.That(fixture.Map.Require<GameText>("manageQuoteAfter").text, Is.EqualTo("12,000"));
        }

        [Test]
        public void NegativeAndUnchangedCpQuotesResetTheirArrowState()
        {
            using var fixture = new Fixture();
            fixture.Configure(_ => (0, 0));
            fixture.Paint(12000, 10000);
            var arrow = fixture.Map.Require<Image>("manageQuoteDeltaArrow");
            Assert.That(fixture.Map.Require<GameText>("manageQuoteDelta").text, Is.EqualTo("2,000"));
            Assert.That((Color32)arrow.color, Is.EqualTo(new Color32(56, 148, 240, 255)));
            Assert.That(Mathf.DeltaAngle(arrow.rectTransform.localEulerAngles.z, 180f), Is.EqualTo(0f).Within(.01f));
            fixture.Paint(10000, 10000);
            Assert.That(fixture.Map.Require<GameText>("manageQuoteDelta").text, Is.EqualTo("0"));
            Assert.That(arrow.enabled, Is.False);
            fixture.Paint(10000, 12000);
            Assert.That(arrow.enabled, Is.True);
            Assert.That(Mathf.DeltaAngle(arrow.rectTransform.localEulerAngles.z, 0f), Is.EqualTo(0f).Within(.01f));
            Assert.That((Color32)arrow.color, Is.EqualTo(new Color32(255, 120, 0, 255)));
        }

        [Test]
        public void ArtifactProgressDistinguishesUnloadedEmptyAndUnknownTemplate()
        {
            using var fixture = new Fixture();
            var assembly = typeof(HeroInventoryBinder).Assembly;
            var bindings = Activator.CreateInstance(assembly.GetType("AstralOath.Client.Home.ArtifactInventoryBindings"),
                Hidden, null, new object[] { fixture.Root.GetComponent<BindingMap>() }, null);
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var type = assembly.GetType("AstralOath.Client.Home.ArtifactInventoryBinder");
            Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ArtifactInventorySnapshot>> fetch =
                _ => System.Threading.Tasks.Task.FromResult(new ArtifactInventorySnapshot());
            var binder = Activator.CreateInstance(type, Hidden, null,
                new object[] { bindings, null, art, null, null, null, null, null, fetch, null, null }, null);
            var method = type.GetMethod("EquippedProgress", Hidden);
            Assert.That(method, Is.Not.Null, "Artifact progression must distinguish unknown metadata from confirmed unequipped.");
            (int stars, int level) Progress() => ((int, int))method.Invoke(binder, new object[] { "hero" });
            Assert.That(Progress(), Is.EqualTo((-1, -1)));
            type.GetField("snapshot", Hidden).SetValue(binder, new ArtifactInventorySnapshot());
            Assert.That(Progress(), Is.EqualTo((-1, -1)));
            var loaded = new ArtifactInventorySnapshot { artifacts = Array.Empty<ArtifactInventoryItem>() };
            type.GetField("snapshot", Hidden).SetValue(binder, loaded);
            Assert.That(Progress(), Is.EqualTo((0, 0)));
            var item = new ArtifactInventoryItem { heroInstanceId = "hero", templateId = "unknown-template", level = 15 };
            loaded.artifacts = new[] { item };
            Assert.That(Progress(), Is.EqualTo((-1, -1)));
            item.templateId = art.Catalog.Templates.First(template => template.Stars == 5).Id;
            Assert.That(Progress(), Is.EqualTo((5, 15)));
        }

        [Test]
        public void LoadedArtifactProgressStaysUnknownAfterReadFailureUntilSuccessfulReadback()
        {
            using var fixture = new ArtifactProgressFixture();
            Assert.That(fixture.Progress(), Is.EqualTo((5, 3)));
            fixture.Read = () => Task.FromException<ArtifactInventorySnapshot>(new IOException("Controlled artifact read failure."));
            fixture.Reload().GetAwaiter().GetResult();
            Assert.That(fixture.Progress(), Is.EqualTo((-1, -1)), "Retaining old cards after GET failure does not confirm their combat inputs.");
            var reply = new TaskCompletionSource<ArtifactInventorySnapshot>();
            fixture.Read = () => reply.Task;
            var retry = fixture.Reload();
            Assert.That(fixture.Progress(), Is.EqualTo((-1, -1)), "Starting a retry cannot restore confidence before its response.");
            reply.SetResult(fixture.Snapshot());
            retry.GetAwaiter().GetResult();
            Assert.That(fixture.Progress(), Is.EqualTo((5, 3)));
        }

        [Test]
        public void LocalPreviewKeepsArtifactProgressButUnresolvedWriteStaysUnknownAfterClosingIt()
        {
            using var fixture = new ArtifactProgressFixture();
            fixture.Preview().GetAwaiter().GetResult();
            Assert.That(fixture.Progress(), Is.EqualTo((5, 3)), "A local confirmation preview has not dispatched an inventory write.");
            ArtifactRequestException rejected = null;
            try { fixture.Api.LockAsync(fixture.Item.instanceId, Guid.NewGuid().ToString("N"), true, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (ArtifactRequestException error) { rejected = error; }
            Assert.That(rejected, Is.Not.Null, "The controlled 503 must reach the API's ambiguous failure path.");
            Assert.That(fixture.Progress(), Is.EqualTo((-1, -1)), "A dispatched write with no authoritative receipt invalidates the prior snapshot.");
            fixture.Back();
            Assert.That(fixture.Progress(), Is.EqualTo((-1, -1)), "Dismissing a popup does not resolve an uncertain write.");
            fixture.Reload().GetAwaiter().GetResult();
            Assert.That(fixture.Progress(), Is.EqualTo((5, 3)), "Only successful authoritative readback can restore the known inputs.");
        }
    }
}
