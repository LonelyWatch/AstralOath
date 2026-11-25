using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentEnhancementTests
    {
        private const string Path = "Assets/Game/Modules/Home/Res/Prefab/EquipmentEnhancement.prefab";

        [Test]
        public void OrdinaryEnhancementHasAnIndependentPageAndQuantityRegistration()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            Assert.That(prefab, Is.Not.Null, "Ordinary equipment needs its own enhancement page; the artifact page is not its substitute.");
            var map = prefab.GetComponent<BindingMap>();
            foreach (var key in new[] { "back", "submit", "clear", "auto", "quantityConfirm", "quantityCancel", "quantityMin", "quantityMax", "quantityMinus", "quantityPlus" })
                Assert.That(map.Require<GameButton>(key), Is.Not.Null, key);
            Assert.That(map.Require<Slider>("quantitySlider"), Is.Not.Null);
            Assert.That(map.Require<GameObject>("quantityPopup").activeSelf, Is.False);
            Assert.That(map.Require<GameObject>("autoPopup").activeSelf, Is.False);
            for (int i = 0; i < 5; i++) Assert.That(map.Require<GameButton>("auto" + (3 + i * 3)), Is.Not.Null);
        }

        [Test]
        public void OrdinaryEnhancementUsesTheOriginalRuneDialAndFourMaterialRows()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            Assert.That(prefab, Is.Not.Null);
            var map = prefab.GetComponent<BindingMap>();
            Assert.That(map.Require<Image>("stoneImage").sprite.name, Is.EqualTo("stone"));
            Assert.That(map.Require<Image>("background").sprite.name, Is.EqualTo("base_unit_hero"));
            Assert.That(map.Require<BindingMap>("card"), Is.Not.Null);
            Assert.That(map.Require<BindingMap>("thumbnail"), Is.Not.Null);
            for (int i = 0; i < 4; i++)
            {
                var row = map.Require<BindingMap>("row" + i);
                Assert.That(row.Require<Image>("icon").sprite, Is.Not.Null);
                Assert.That(row.Require<GameText>("quantity"), Is.Not.Null);
                Assert.That(row.Require<GameButton>("select"), Is.Not.Null);
            }
            Assert.That(map.Require<AudioSource>("audio").playOnAwake, Is.False);
            foreach (var key in new[] { "slotInSound", "slotOutSound", "startSound", "finishSound" })
                Assert.That(map.Require<AudioSource>(key).clip.length, Is.GreaterThan(.05f), key);
        }
    }
}
