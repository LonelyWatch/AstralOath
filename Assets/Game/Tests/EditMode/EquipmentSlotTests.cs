using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentSlotTests
    {
        private const string HeroPath = "Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab";
        private const string CommonPath = "Assets/Game/Common/Res/Prefab/";

        [TestCase(HeroPath, "rosterScroll")]
        [TestCase("Assets/Game/Modules/Arena/Res/Prefab/ArenaDraft.prefab", "roster")]
        public void RosterPageContainsItsInactiveScrollController(string path, string key)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var scroll = prefab.GetComponent<BindingMap>().Require<ScrollRect>(key);
            var controllers = scroll.GetComponents<FocusedRosterScroll>();
            Assert.That(controllers, Has.Length.EqualTo(1), key + " must include the focus controller before binding.");
            Assert.That(controllers[0].enabled, Is.False, "Only the active window's binder may enable the controller.");
            var instance = Object.Instantiate(scroll);
            try
            {
                var controller = instance.GetComponent<FocusedRosterScroll>();
                using (var scope = new BindingScope(System.Threading.CancellationToken.None))
                {
                    Assert.That(FocusedRosterScroll.Bind(instance, scope, null, null), Is.SameAs(controller));
                    Assert.That(controller.enabled, Is.True);
                }
                Assert.That(controller.enabled, Is.False, "Closing the page must disable its existing controller.");
            }
            finally { Object.DestroyImmediate(instance.gameObject); }
        }

        [Test]
        public void HeroEquipmentUsesNestedSlotsAndSharedNotificationBadge()
        {
            var hero = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var slotAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CommonPath + "EquipmentSlot.prefab");
            var badgeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CommonPath + "NotificationBadge.prefab");
            Assert.That(slotAsset && badgeAsset, Is.True, "GenerateInventory must produce both shared prefabs.");
            UIViewRegistry.ValidatePrefab<EquipmentSlot>(slotAsset);
            UIViewRegistry.ValidatePrefab<NotificationBadge>(badgeAsset);
            foreach (var path in AssetDatabase.GetDependencies(CommonPath + "EquipmentSlot.prefab", true))
                Assert.That(path, Does.Not.StartWith("Assets/Game/Modules/"), "Shared slot dependency");
            var numberFont = badgeAsset.GetComponent<BindingMap>().Require<GameText>("label").font;
            foreach (char digit in "+0123456789")
                Assert.That(numberFont.HasCharacter(digit), Is.True, "Badge font must include " + digit);
            var nested = slotAsset.GetComponent<BindingMap>().Require<BindingMap>("badge");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(nested.gameObject), Is.EqualTo(badgeAsset));
            foreach (string key in new[] { "equipmentSlot_weapon", "equipmentSlot_helm", "equipmentSlot_armor",
                         "equipmentSlot_neck", "equipmentSlot_ring", "equipmentSlot_boots", "artifactEquipmentSlot" })
            {
                var slot = hero.Require<BindingMap>(key);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(slot.gameObject), Is.EqualTo(slotAsset), key);
                Assert.That(slot.Require<Image>("frame").sprite && slot.Require<Image>("placeholder").sprite, Is.True, key);
            }
            var row = hero.Require<GameObject>("bagRow").GetComponent<BindingMap>();
            foreach (string key in new[] { "emptyGear0", "emptyGear1", "emptyGear2", "emptyGear3", "emptyGear4", "emptyGear5",
                         "emptyArtifact", "emptyExclusive" })
            {
                var slot = row.Require<Image>(key).GetComponentInParent<BindingMap>(true);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(slot.gameObject), Is.EqualTo(slotAsset), key);
            }
        }

        [Test]
        public void EquipmentSlotResetsItemAndBadgeWhenRebound()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath));
            try
            {
                var slot = instance.GetComponent<BindingMap>().Require<BindingMap>("artifactEquipmentSlot");
                var binder = new EquipmentSlotBinder(slot);
                var placeholder = slot.Require<Image>("placeholder");
                var item = slot.Require<Image>("item");
                var badge = slot.Require<BindingMap>("badge");
                var number = badge.Require<GameText>("label");
                var art = placeholder.sprite;
                binder.Set(null, 0);
                Assert.That(placeholder.enabled && !item.enabled && !badge.gameObject.activeSelf, Is.True);
                binder.Set(art, 0);
                Assert.That(item.sprite, Is.SameAs(art));
                Assert.That(item.enabled && !placeholder.enabled && !badge.gameObject.activeSelf, Is.True);
                binder.Set(art, 12);
                Assert.That(badge.gameObject.activeSelf && number.enabled, Is.True);
                Assert.That(number.text, Is.EqualTo("+12"));
                binder.Set(null, 0, notify: true);
                Assert.That(placeholder.enabled && !item.enabled && !item.sprite, Is.True);
                Assert.That(badge.gameObject.activeSelf && !number.enabled, Is.True);
                Assert.That(number.text, Is.Empty);
                binder.Set(null, 12);
                Assert.That(badge.gameObject.activeSelf, Is.False, "An empty slot must clear a stale enhancement.");
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
