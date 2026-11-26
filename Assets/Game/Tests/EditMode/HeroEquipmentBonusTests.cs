using System;
using System.Globalization;
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
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class HeroEquipmentBonusTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void HeroBonusUsesCurrentEquipmentAndArtifactInsteadOfPreviousChange()
        {
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab"));
            using var equipmentApi = new EquipmentInventoryApi(new Uri("http://127.0.0.1:7728"), _ => Task.FromResult("unused"), CancellationToken.None);
            using var artifactApi = new ArtifactInventoryApi(new Uri("http://127.0.0.1:7728"), _ => Task.FromResult("unused"), CancellationToken.None);
            try
            {
                var map = instance.GetComponent<BindingMap>();
                var view = new HeroInventory();
                typeof(UIBaseView).GetMethod("Attach", Hidden).Invoke(view, new object[] { instance });
                var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
                var artifactArt = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
                var binder = new HeroInventoryBinder(view, art, new Text(), null, () => Task.CompletedTask,
                    artifactArt, artifactApi, equipmentApi: equipmentApi);
                var hero = new HeroInventoryItem { instanceId = "bonus-hero", templateId = "c1001", level = 30, stars = 3 };
                Set(binder, "selected", hero);
                Invoke(binder, "SetDetailStats");
                int plainAttack = int.Parse(map.Require<GameText>("detailStat_attack").text,CultureInfo.InvariantCulture);
                int plainHealth = int.Parse(map.Require<GameText>("detailStat_health").text,CultureInfo.InvariantCulture);
                var gear = Field(binder, "equipment");
                var artifacts = Field(binder, "artifacts");
                var template = artifactArt.Catalog.Templates.First();
                Set(artifacts, "snapshot", new ArtifactInventorySnapshot { artifacts = new[] {
                    new ArtifactInventoryItem { instanceId = "bonus-artifact", templateId = template.Id, heroInstanceId = hero.instanceId }
                }});
                var equipment = new EquipmentInventorySnapshot { version = 1, items = new[] {
                    new EquipmentInventoryItem { instanceId = "bonus-weapon", slot = "weapon", equippedHeroId = hero.instanceId,
                        setId = "critical", mainStat = Stat("attack", 100), substats = new[] { Stat("health", 200), Stat("healthPct", 1000), Stat("speed", 5), Stat("critDamageBp", 500) } },
                    new EquipmentInventoryItem { instanceId = "bonus-ring", slot = "ring", equippedHeroId = hero.instanceId,
                        setId = "critical", mainStat = Stat("defense", 20), substats = new[] { Stat("attackPct", 2000) } }
                }};
                Invoke(gear, "AcceptSnapshot", equipment);
                Invoke(binder, "SetDetailStats");
                Assert.That(map.Require<GameText>("detailStat_attack").text,
                    Is.EqualTo((plainAttack + plainAttack / 5 + 100 + template.AttackAt(0)).ToString(CultureInfo.InvariantCulture)));
                Assert.That(map.Require<GameText>("detailBonus_attack").text, Is.EqualTo((plainAttack / 5 + 100 + template.AttackAt(0)).ToString()));
                Assert.That(map.Require<GameText>("detailBonus_health").text, Is.EqualTo((plainHealth / 10 + 200 + template.HealthAt(0)).ToString()));
                Assert.That(map.Require<GameText>("detailBonus_defense").text, Is.EqualTo("20"));
                Assert.That(map.Require<GameText>("detailBonus_speed").text, Is.EqualTo("5"));
                Assert.That(map.Require<GameText>("detailBonus_critChance").text, Is.EqualTo("12%"), "The current two-piece set contributes to the total bonus.");
                Assert.That(map.Require<GameText>("detailBonus_critDamage").text, Is.EqualTo("5%"));
                Assert.That(map.Require<GameText>("detailBonus_effectiveness").gameObject.activeSelf, Is.False);
                Assert.That(map.Require<Image>("detailBonusArrow_attack").gameObject.activeSelf, Is.True);

                // Repainting and selecting another hero must derive from the current snapshots every time.
                Invoke(binder, "SetDetailStats");
                Assert.That(map.Require<GameText>("detailBonus_defense").text, Is.EqualTo("20"));
                Set(artifacts, "snapshot", null);
                Invoke(binder, "SetDetailStats");
                Assert.That(map.Require<GameText>("detailBonus_attack").gameObject.activeSelf, Is.False,
                    "An unknown artifact snapshot must not present an equipment-only attack value as the complete bonus.");
                Assert.That(map.Require<GameText>("detailBonus_health").gameObject.activeSelf, Is.False);
                Assert.That(map.Require<Image>("detailBonusArrow_attack").gameObject.activeSelf, Is.False);
                Assert.That(map.Require<GameText>("detailBonus_defense").gameObject.activeSelf, Is.True);
                Assert.That(map.Require<GameText>("detailBonus_defense").text, Is.EqualTo("20"));
                Assert.That(map.Require<GameText>("detailBonus_speed").text, Is.EqualTo("5"));
                Set(binder, "selected", new HeroInventoryItem { instanceId = "empty-hero", templateId = "c1001", level = 30, stars = 3 });
                Invoke(binder, "SetDetailStats");
                Assert.That(map.Require<GameText>("detailBonus_attack").gameObject.activeSelf, Is.False);
                Assert.That(map.Require<Image>("detailBonusArrow_attack").gameObject.activeSelf, Is.False);

                Set(binder, "selected", hero);
                Set(artifacts, "snapshot", new ArtifactInventorySnapshot { artifacts = Array.Empty<ArtifactInventoryItem>() });
                Invoke(gear, "AcceptSnapshot", new EquipmentInventorySnapshot { version = 2, items = Array.Empty<EquipmentInventoryItem>() });
                Invoke(binder, "SetDetailStats");
                Assert.That(map.Require<GameText>("detailBonus_attack").text, Is.Empty);
                Assert.That(map.Require<GameText>("detailBonus_attack").gameObject.activeSelf, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static EquipmentStatValue Stat(string kind, int value) => new EquipmentStatValue { kind = kind, value = value };
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
        private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);
        private sealed class Text : ILocaleService
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
