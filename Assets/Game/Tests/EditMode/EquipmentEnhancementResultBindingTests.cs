using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentEnhancementResultBindingTests
    {
        private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class Labels : ILocaleService
        {
            [Serializable] private sealed class Row { public string key, text; }
            [Serializable] private sealed class File { public Row[] rows; }
            private readonly Dictionary<string, string> values = JsonUtility.FromJson<File>(System.IO.File.ReadAllText(
                "Assets/Game/Modules/Arena/Configs/arena-zh-hans.json")).rows.ToDictionary(row => row.key, row => row.text);
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add { } remove { } }
            public void SelectText(string value) { }
            public void SelectVoice(string value) { }
            public string Resolve(string key) => values.TryGetValue(key, out var value) ? value : key;
        }

        private static EquipmentInventoryItem Item(int level, int main, int defense, int damage) => new EquipmentInventoryItem
        {
            instanceId = "result-binding", slot = "boots", setId = "critical", grade = "epic", itemLevel = 85,
            enhanceLevel = level, xp = level == 3 ? 2000 : level == 6 ? 6700 : 0, enhancementSupported = true,
            mainStat = new EquipmentStatValue { kind = "healthPct", value = main },
            substats = new[] { new EquipmentStatValue { kind = "attack", value = 34 },
                new EquipmentStatValue { kind = "critChanceBp", value = 400 },
                new EquipmentStatValue { kind = "defensePct", value = defense, enhancementCount = level >= 3 ? 1 : 0, enhancementCountKnown = true },
                new EquipmentStatValue { kind = "critDamageBp", value = damage, enhancementCount = level >= 6 ? 1 : 0, enhancementCountKnown = true } }
        };

        [Test]
        public void ConsecutiveAuthoritativeRevealsUpdateTheActualLabelsAndOnlyTheNewSweepRows()
        {
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Home/Res/Prefab/EquipmentEnhancement.prefab"));
            var scope = new BindingScope(default);
            try
            {
                var map = root.GetComponent<BindingMap>();
                var view = (EquipmentEnhancement)Activator.CreateInstance(typeof(EquipmentEnhancement), Internal, null, new object[] { map }, null);
                var type = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementBinder");
                string Stat(EquipmentStatValue stat) => stat.kind == "attack" ? stat.value.ToString(CultureInfo.InvariantCulture)
                    : (stat.value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
                var binder = Activator.CreateInstance(type, Internal, null, new object[] { view, scope, new Labels(), null,
                    (Action)(() => { }), (Action<EquipmentInventorySnapshot>)(_ => { }),
                    (Action<EquipmentItemCardBinder, EquipmentInventoryItem>)((_, __) => { }),
                    (Func<EquipmentInventoryItem, string>)(_ => "fixture"), (Func<EquipmentStatValue, Sprite>)(_ => null),
                    (Func<EquipmentStatValue, string>)Stat, (Func<EquipmentInventoryItem, string>)(_ => "fixture"),
                    (Func<EquipmentInventoryItem, string>)(_ => "fixture") }, null);
                void Set(string key, object value) => type.GetField(key, Internal).SetValue(binder, value);
                void Call(string key) => type.GetMethod(key, Internal).Invoke(binder, null);
                var opening = Item(0, 1200, 500, 700);
                Set("target", opening);
                Set("inventory", new EquipmentInventorySnapshot { items = new[] { opening }, materials = new EquipmentEnhancementBalances() });
                Set("profile", new EquipmentEnhancementProfile { supported = true,
                    mainValues = new[] { 1200, 1400, 1600, 1900, 2100, 2400, 2600, 2800, 3100, 3400, 3600, 3900, 4300, 4600, 5000, 6000 },
                    xpThresholds = new long[] { 0, 525, 1182, 1969, 3019, 4594, 6694, 9582, 13257, 17719, 23232, 30319, 39244, 50269, 64182, 82032 } });
                var displayType = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementResultDisplay");
                Set("resultDisplay", Activator.CreateInstance(displayType, Internal, null, new object[] { opening }, null));
                void Reveal(EquipmentInventoryItem item, string changed)
                {
                    Set("presentedReceipt", new EquipmentInventorySnapshot { items = new[] { item }, materials = new EquipmentEnhancementBalances() });
                    Set("lastResult", new EquipmentEnhancementResult { changes = new[] { new EquipmentEnhancementChange
                        { kind = changed, before = changed == "defensePct" ? 500 : 700, after = changed == "defensePct" ? 1000 : 1200 } } });
                    Set("animating", true); Set("startPlayed", true); Set("finishPlayed", true);
                    Set("animationStart", Time.unscaledTime - 2.3f);
                    Call("Animate");
                }
                view.Show();
                Reveal(Item(3, 1900, 1000, 700), "defensePct");
                Assert.That(map.Require<GameText>("mainValue").text, Is.EqualTo("19%"));
                Assert.That(map.Require<GameText>("subName2").text, Does.Contain("<color=#FF7E00>(1)</color>"));
                Assert.That(map.Require<GameText>("score").text, Does.Contain("(+8)"));
                Assert.That(map.Require<RawImage>("subSweep2").enabled, Is.True);
                Reveal(Item(6, 2600, 1000, 1200), "critDamageBp");
                Assert.That(map.Require<GameText>("mainValue").text, Is.EqualTo("26%"));
                Assert.That(map.Require<GameText>("subValue2").text, Does.Contain("(+5%)"));
                Assert.That(map.Require<GameText>("subValue3").text, Does.Contain("(+5%)"));
                Assert.That(map.Require<GameText>("score").text, Does.Contain("(+16)"));
                Assert.That(map.Require<GameText>("subValue2").color, Is.EqualTo(Color.white));
                Assert.That(map.Require<GameText>("subValue0").color, Is.Not.EqualTo(Color.white));
                Assert.That(map.Require<RawImage>("subSweep2").enabled, Is.False, "Previously improved row retains its delta without replaying this receipt's effect.");
                Assert.That(map.Require<RawImage>("subSweep3").enabled, Is.True);
                Set("resultSweepStart", Time.unscaledTime - 2f); Call("UpdateResultSweeps");
                foreach (var image in root.GetComponentsInChildren<RawImage>(true)) Assert.That(image.enabled, Is.False);
                map.Require<GameButton>("powderOnly").onClick.Invoke();
                Assert.That(map.Require<Image>("powderOnlyCheck").enabled, Is.True);
                Assert.That(map.Require<GameText>("powderOnlyLabel").text, Does.Not.Contain("☑"));
                map.Require<GameButton>("powderOnly").onClick.Invoke();
                Assert.That(map.Require<Image>("powderOnlyCheck").enabled, Is.False);
            }
            finally { scope.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
