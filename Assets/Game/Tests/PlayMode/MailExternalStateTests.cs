#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class MailExternalStateTests
    {
        [UnityTest]
        public IEnumerator HeroMailRefreshKeepsTheSameSelectedHeroFilterAndScroll()
        {
            using var fixture = new InventoryFixture(true);
            var view = fixture.View;
            fixture.Map.Require<TMPro.TMP_InputField>("search").text = "拉斯";
            Click(fixture.Map.Require<GameButton>("expand"));
            yield return null;
            var selected = fixture.Heroes.heroes[4];
            Set(fixture.Binder, "selected", selected);
            Set(fixture.Binder, "preferredRosterId", selected.instanceId);
            var position = new Vector2(0, 40);
            fixture.Map.Require<ScrollRect>("rosterScroll").content.anchoredPosition = position;
            fixture.Heroes = fixture.MakeHeroes(31);
            yield return Complete(fixture.Binder.RefreshExternalAsync(fixture.Scope.Token));
            Assert.That(Get<HeroInventorySnapshot>(fixture.Binder, "snapshot").used, Is.EqualTo(31));
            Assert.That(Get<HeroInventoryItem>(fixture.Binder, "selected").instanceId, Is.EqualTo(selected.instanceId));
            Assert.That(fixture.Map.Require<TMPro.TMP_InputField>("search").text, Is.EqualTo("拉斯"));
            Assert.That(Get<bool>(fixture.Binder, "expanded"), Is.True);
            Assert.That(fixture.Map.Require<ScrollRect>("rosterScroll").content.anchoredPosition.y, Is.EqualTo(position.y).Within(.1f),
                "邮件领取刷新不应把当前英雄滚动位置退回顶部。");
        }

        [UnityTest]
        public IEnumerator EquipmentMailRefreshKeepsTheTabFilterSelectionAndScrollWhileAddingRows()
        {
            using var fixture = new InventoryFixture(false);
            Click(fixture.Map.Require<GameButton>("tabEquipment"));
            yield return null;
            var gear = Get<object>(fixture.Binder, "equipment");
            Set(gear, "selected", fixture.Gear.items[3]);
            Set(gear, "orderByLevelAscending", true);
            var pane = fixture.View.Root.GetComponent<BindingMap>().Require<BindingMap>("gearBrowser").Require<BindingMap>("bagPane");
            var scroll = pane.Require<ScrollRect>("items");
            var position = new Vector2(0, 173);
            scroll.content.anchoredPosition = position;
            fixture.Gear = fixture.MakeGear(31, 2);
            yield return Complete(fixture.Binder.RefreshExternalAsync(fixture.Scope.Token));
            Assert.That(Get<EquipmentInventorySnapshot>(gear, "snapshot").used, Is.EqualTo(31), "必须读到邮件新增装备。");
            Assert.That(Get<object>(fixture.Binder, "tab").ToString(), Is.EqualTo("Equipment"));
            Assert.That(Get<EquipmentInventoryItem>(gear, "selected").instanceId, Is.EqualTo("gear-03"));
            Assert.That(Get<bool>(gear, "orderByLevelAscending"), Is.True);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(position.y).Within(.1f));
        }

        [UnityTest]
        public IEnumerator ArtifactMailRefreshKeepsTheSelectionAndScrollWhileAddingRows()
        {
            using var fixture = new InventoryFixture(false);
            Click(fixture.Map.Require<GameButton>("tabArtifacts"));
            yield return null;
            var artifacts = Get<object>(fixture.Binder, "artifacts");
            Set(artifacts, "selected", fixture.Artifacts.artifacts[3]);
            var scroll = fixture.View.Root.GetComponent<BindingMap>().Require<ScrollRect>("artifactItems");
            var position = new Vector2(0, 183);
            scroll.content.anchoredPosition = position;
            fixture.Artifacts = fixture.MakeArtifacts(31);
            yield return Complete(fixture.Binder.RefreshExternalAsync(fixture.Scope.Token));
            Assert.That(Get<ArtifactInventorySnapshot>(artifacts, "snapshot").used, Is.EqualTo(31), "必须读到邮件新增神器。");
            Assert.That(Get<object>(fixture.Binder, "tab").ToString(), Is.EqualTo("Artifacts"));
            Assert.That(Get<ArtifactInventoryItem>(artifacts, "selected").instanceId, Is.EqualTo("artifact-03"));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(position.y).Within(.1f));
        }

        private static void Click(GameButton button)
        {
            Assert.That(button.isActiveAndEnabled && button.IsInteractable(), Is.True, button.name);
            button.onClick.Invoke();
        }

        private static IEnumerator Complete(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }

        private static T Get<T>(object value, string name) => (T)value.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
        private static void Set(object value, string name, object next) => value.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(value, next);

        private sealed class InventoryFixture : IDisposable
        {
            internal readonly BindingScope Scope = new BindingScope(CancellationToken.None);
            internal readonly HeroInventory View;
            internal BindingMap Map => View.Root.GetComponent<BindingMap>();
            internal readonly HeroInventoryBinder Binder;
            internal HeroInventorySnapshot Heroes;
            internal EquipmentInventorySnapshot Gear;
            internal ArtifactInventorySnapshot Artifacts;
            private readonly GameObject canvas;
            private readonly EquipmentInventoryApi gearApi;
            private readonly ArtifactInventoryApi artifactApi;
            private readonly string artifactId;

            internal InventoryFixture(bool roster)
            {
                canvas = new GameObject("Mail external state", typeof(RectTransform), typeof(Canvas));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var root = (RectTransform)canvas.transform;
                root.sizeDelta = new Vector2(1920, 1080);
                var registry = new UIViewRegistry();
                registry.Register<HeroInventory>();
                View = registry.CreateAsync<HeroInventory>(new Prefabs(), root, Scope).GetAwaiter().GetResult();
                var heroArt = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
                var artifactArt = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
                artifactId = artifactArt.Catalog.Templates.First().Id;
                Heroes = MakeHeroes(30);
                Gear = MakeGear(30, 1);
                Artifacts = MakeArtifacts(30);
                var endpoint = new Uri("http://127.0.0.6:7728");
                Task<string> Bearer(CancellationToken _) => Task.FromResult("unused-controlled-read");
                gearApi = new EquipmentInventoryApi(endpoint, Bearer, Scope.Token);
                artifactApi = new ArtifactInventoryApi(endpoint, Bearer, Scope.Token);
                Binder = new HeroInventoryBinder(View, heroArt, new Labels(), _ => Task.FromResult(Heroes), () => Task.CompletedTask,
                    artifactArt, artifactApi, fetchArtifacts: _ => Task.FromResult(Artifacts), equipmentApi: gearApi,
                    readEquipment: _ => Task.FromResult(Gear));
                Binder.OpenAsync(roster, Scope, Heroes, Artifacts, Gear).GetAwaiter().GetResult();
                Canvas.ForceUpdateCanvases();
            }

            internal HeroInventorySnapshot MakeHeroes(int count) => new HeroInventorySnapshot
            {
                capacity = 1000, used = count,
                heroes = Enumerable.Range(0, count).Select(index => new HeroInventoryItem
                    { instanceId = "hero-" + index.ToString("D2"), templateId = "c1001", stars = 3, level = 1 }).ToArray()
            };

            internal EquipmentInventorySnapshot MakeGear(int count, int version) => new EquipmentInventorySnapshot
            {
                capacity = 500, used = count, version = version,
                items = Enumerable.Range(0, count).Select(index => new EquipmentInventoryItem
                {
                    instanceId = "gear-" + index.ToString("D2"), templateId = "training_speed_weapon", slot = "weapon",
                    setId = "speed", grade = "good", itemLevel = 15,
                    mainStat = new EquipmentStatValue { kind = "attack", value = 20 }, substats = Array.Empty<EquipmentStatValue>()
                }).ToArray()
            };

            internal ArtifactInventorySnapshot MakeArtifacts(int count) => new ArtifactInventorySnapshot
            {
                capacity = 400, used = count, materials = new ArtifactInventoryMaterials(),
                artifacts = Enumerable.Range(0, count).Select(index => new ArtifactInventoryItem
                    { instanceId = "artifact-" + index.ToString("D2"), templateId = artifactId, level = 0 }).ToArray()
            };

            public void Dispose()
            {
                Scope.Dispose();
                gearApi.Dispose();
                artifactApi.Dispose();
                Object.DestroyImmediate(canvas);
            }
        }

        private sealed class Prefabs : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object => Task.FromResult(
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab"));
        }

        private sealed class Labels : ILocaleService
        {
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add { } remove { } }
            public void SelectText(string locale) { }
            public void SelectVoice(string locale) { }
            public string Resolve(string key) => key;
        }
    }
}
#endif
