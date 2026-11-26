#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class MailExternalStateEnhancementTests
    {
        private const BindingFlags Members = BindingFlags.NonPublic | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator MailRefreshesOpenEnhancementBalancesWithoutDiscardingSelectedMaterials()
        {
            using var page = new Page();
            page.Selected["ma_est1e"] = 2;
            page.Render();
            var updated = page.Fresh();
            yield return Complete(page.Apply(updated));
            Assert.That(page.Map.Require<GameText>("balance").text, Is.EqualTo("2,000"));
            Assert.That(page.Selected["ma_est1e"], Is.EqualTo(2));
            Assert.That(page.Map.Require<BindingMap>("row0").Require<GameText>("quantity").text, Does.Contain("48"));
            Assert.That(page.Map.gameObject.activeSelf, Is.True, "领取材料不应关闭正在使用的强化页。");
        }

        [UnityTest]
        public IEnumerator RefreshDoesNotRewriteAnUncertainEnhancementRequest()
        {
            using var page = new Page();
            var recipe = new[] { new EquipmentEnhancementMaterial { kind = "ma_est1e", quantity = 2 } };
            page.Selected["ma_est1e"] = 2;
            page.Set("requestId", "11111111111111111111111111111111");
            page.Set("requestVersion", 1L);
            page.Set("retryMaterials", recipe);
            var updated = page.Fresh();
            updated.items[0].enhanceLevel = 1;
            updated.items[0].xp = 525;
            updated.items[0].mainStat.value = 1400;
            yield return Complete(page.Apply(updated));
            Assert.That(page.Get<string>("requestId"), Is.EqualTo("11111111111111111111111111111111"));
            Assert.That(page.Get<long>("requestVersion"), Is.EqualTo(1L));
            Assert.That(page.Get<EquipmentEnhancementMaterial[]>("retryMaterials"), Is.SameAs(recipe));
            Assert.That(page.Selected["ma_est1e"], Is.EqualTo(2));
        }

        private static IEnumerator Complete(Task task)
        {
            float until = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }

        private sealed class Page : IDisposable
        {
            internal readonly BindingMap Map;
            private readonly GameObject canvas;
            private readonly BindingScope scope = new BindingScope(CancellationToken.None);
            private readonly EquipmentInventoryApi api;
            private readonly object binder;
            private readonly Type type;
            private readonly EquipmentInventorySnapshot initial;
            internal Dictionary<string, int> Selected => Get<Dictionary<string, int>>("selected");

            internal Page()
            {
                canvas = new GameObject("Mail enhancement calibration", typeof(RectTransform), typeof(Canvas));
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Modules/Home/Res/Prefab/EquipmentEnhancement.prefab"), canvas.transform, false);
                Map = root.GetComponent<BindingMap>();
                var view = (EquipmentEnhancement)Activator.CreateInstance(typeof(EquipmentEnhancement), Members, null, new object[] { Map }, null);
                api = new EquipmentInventoryApi(new Uri("http://127.0.0.6:7728"), _ => Task.FromResult("unused-controlled"), scope.Token);
                type = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementBinder", true);
                binder = Activator.CreateInstance(type, Members, null, new object[]
                {
                    view, scope, new Labels(), api, (Action)(() => {}), (Action<EquipmentInventorySnapshot>)(_ => {}),
                    (Action<EquipmentItemCardBinder, EquipmentInventoryItem>)((card, item) => {}),
                    (Func<EquipmentInventoryItem, string>)(_ => "训练装备"), (Func<EquipmentStatValue, Sprite>)(_ => null),
                    (Func<EquipmentStatValue, string>)(stat => stat.value.ToString()),
                    (Func<EquipmentInventoryItem, string>)(_ => "传说鞋子"), (Func<EquipmentInventoryItem, string>)(_ => "暴击套装 (0/2)")
                }, null);
                initial = new EquipmentInventorySnapshot
                {
                    version = 1, capacity = 500, used = 1,
                    items = new[] { new EquipmentInventoryItem
                    {
                        instanceId = "22222222222222222222222222222222", slot = "boots", setId = "critical", grade = "epic", itemLevel = 85,
                        enhancementSupported = true, mainStat = new EquipmentStatValue { kind = "healthPct", value = 1200 },
                        substats = Array.Empty<EquipmentStatValue>()
                    } },
                    materials = new EquipmentEnhancementBalances
                        { gold = 1000, items = new[] { new EquipmentEnhancementBalance { kind = "ma_est1e", quantity = 40 } } }
                };
                var profile = new EquipmentEnhancementProfile
                {
                    supported = true, itemLevel = 85, grade = "epic", slot = "boots", mainKind = "healthPct",
                    mainValues = Enumerable.Range(0, 16).Select(index => 1200 + index * 200).ToArray(),
                    xpThresholds = Enumerable.Range(0, 16).Select(index => (long)index * 525).ToArray()
                };
                Set("inventory", initial);
                Set("target", initial.items[0]);
                Set("profile", profile);
                Set("materials", new[] { new EquipmentEnhancementMaterialRule
                    { id = "ma_est1e", experience = 100, goldPerUnit = 100, slots = new[] { "boots" } } });
                view.Show();
                Render();
            }

            internal EquipmentInventorySnapshot Fresh()
            {
                var value = JsonUtility.FromJson<EquipmentInventorySnapshot>(JsonUtility.ToJson(initial));
                value.version = 2;
                value.materials.gold = 2000;
                value.materials.items[0].quantity = 50;
                return value;
            }
            internal T Get<T>(string field) => (T)type.GetField(field, Members).GetValue(binder);
            internal void Set(string field, object value) => type.GetField(field, Members).SetValue(binder, value);
            internal void Render() => type.GetMethod("Refresh", Members).Invoke(binder, null);
            internal Task Apply(EquipmentInventorySnapshot value) => (Task)type.GetMethod("RefreshExternalAsync", Members)
                .Invoke(binder, new object[] { value, scope.Token });
            public void Dispose() { scope.Dispose(); api.Dispose(); Object.DestroyImmediate(canvas); }
        }

        private sealed class Labels : ILocaleService
        {
            [Serializable] private sealed class Row { public string key, text; }
            [Serializable] private sealed class Data { public Row[] rows; }
            private readonly Dictionary<string, string> values = JsonUtility.FromJson<Data>(System.IO.File.ReadAllText(
                "Assets/Game/Modules/Arena/Configs/arena-zh-hans.json")).rows.ToDictionary(row => row.key, row => row.text);
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add {} remove {} }
            public void SelectText(string locale) { }
            public void SelectVoice(string locale) { }
            public string Resolve(string key) => values.TryGetValue(key, out var text) ? text : key;
        }
    }
}
#endif
