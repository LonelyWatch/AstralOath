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

        [Test]
        public void ArtifactInventoryUsesReusableBannerWithSeparateEnhancementStarsAndOwner()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var template = page.Require<GameObject>("artifactCard");
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(template),
                Is.EqualTo("Assets/Game/Modules/Home/Res/Prefab/ArtifactInventoryCard.prefab"));
            var card = template.GetComponent<BindingMap>();
            var badge = card.Require<BindingMap>("artifactCardBadge");
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(badge.gameObject),
                Is.EqualTo(CommonPath + "NotificationBadge.prefab"));
            Assert.That(card.Require<GameText>("artifactCardLevel"), Is.EqualTo(badge.Require<GameText>("label")));
            Assert.That(card.Require<BindingMap>("artifactCardWearer").Require<Image>("portrait"), Is.Not.Null);
            Assert.That(card.Require<Image>("star4"), Is.Not.Null);
            Assert.That(card.Require<Image>("break4"), Is.Not.Null);
        }

        [Test]
        public void WornEquipmentUsesTheSameItemCardAcrossHeroBagAndManager()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var bagRow = page.Require<GameObject>("bagRow").GetComponent<BindingMap>();
            var manager = page.Require<BindingMap>("gearBrowser");
            string[] heroKeys = { "weapon", "helm", "armor", "neck", "ring", "boots" };
            string[] apiKeys = { "weapon", "helmet", "armor", "necklace", "ring", "boots" };
            for (int i = 0; i < heroKeys.Length; ++i)
            {
                var bag = bagRow.Require<Image>("emptyGear" + i).GetComponentInParent<BindingMap>(true);
                foreach (var slot in new[] { page.Require<BindingMap>("equipmentSlot_" + heroKeys[i]), bag,
                    manager.Require<BindingMap>("manageCurrent_" + apiKeys[i]), manager.Require<BindingMap>("managePreview_" + apiKeys[i]) })
                {
                    var card = slot.Require<BindingMap>("wornCard");
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(card.gameObject),
                        Is.EqualTo(CommonPath + "EquipmentItemCard.prefab"));
                    Assert.That(slot.Require<Image>("item"), Is.EqualTo(card.Require<Image>("icon")));
                    Assert.That(card.gameObject.activeSelf, Is.False);
                    Assert.That(card.Require<GameText>("stat").transform.parent.gameObject.activeSelf, Is.EqualTo(slot == bag));
                }
            }
        }

        [Test]
        public void EquipmentManagerAuthorsCurrentAndPreviewLoadoutsBesideCandidates()
        {
            var browser = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>().Require<BindingMap>("gearBrowser");
            foreach (string group in new[] { "manageCurrent", "managePreview" })
            {
                foreach (string slot in new[] { "weapon", "helmet", "armor", "necklace", "ring", "boots" })
                {
                    var map = browser.Require<BindingMap>(group + "_" + slot);
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(map.gameObject),
                        Is.EqualTo(CommonPath + "EquipmentSlot.prefab"));
                }
                var row = browser.Require<BindingMap>(group + "_attack");
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(row.gameObject),
                    Is.EqualTo(CommonPath + "EquipmentStatRow.prefab"));
            }
            Assert.That(browser.Require<ScrollRect>("items").GetComponent<RectTransform>().anchorMin.x,Is.GreaterThan(.45f));
        }

        [Test]
        public void BagUsesSharedItemCardsAndSeparateStatRows()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var browser = page.Require<BindingMap>("gearBrowser");
            var card = browser.Require<GameObject>("template");
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(card),
                Is.EqualTo(CommonPath + "EquipmentItemCard.prefab"));
            foreach (var key in new[] { "bagMain", "bagSub0", "bagSub1", "bagSub2", "bagSub3" })
            {
                var row = browser.Require<BindingMap>(key);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(row.gameObject),
                    Is.EqualTo(CommonPath + "EquipmentStatRow.prefab"), key);
            }
            var grid = browser.Require<ScrollRect>("bagItems").content.GetComponent<GridLayoutGroup>();
            Assert.That(grid.cellSize, Is.EqualTo(new Vector2(129, 166.5f)));
            Assert.That(grid.spacing, Is.EqualTo(new Vector2(19.5f, 33)));
        }

        [TestCase(2217f,8)]
        [TestCase(1170f,3)]
        public void BagToolbarAndGridFitAvailableWidth(float width,int expectedColumns)
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var source = page.Require<BindingMap>("gearBrowser").Require<BindingMap>("bagPane");
            var root = (RectTransform)new GameObject("Bag layout",typeof(RectTransform)).transform;
            try
            {
                root.sizeDelta = new Vector2(width,1080f);
                var bag = Object.Instantiate(source,root,false);
                bag.gameObject.SetActive(true);
                var last = new Vector3[4];
                var sort = new Vector3[4];
                ((RectTransform)bag.Require<GameButton>("filter_boots").transform).GetWorldCorners(last);
                ((RectTransform)bag.Require<GameButton>("sort").transform).GetWorldCorners(sort);
                Assert.That(last[2].x,Is.LessThan(sort[0].x),"The rightmost slot must have an independent hit area.");
                var list = bag.Require<ScrollRect>("items");
                var grid = list.content.GetComponent<GridLayoutGroup>();
                float available = list.viewport.rect.width - grid.padding.horizontal;
                int columns = Mathf.FloorToInt((available+grid.spacing.x)/(grid.cellSize.x+grid.spacing.x));
                Assert.That(columns,Is.EqualTo(expectedColumns));
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source.Require<BindingMap>("thumbnail").gameObject),
                    Is.EqualTo(CommonPath+"EquipmentItemCard.prefab"));
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void OwnedGearBrowserAndSixActionsAreAuthoredInTheHeroPrefab()
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var browser = map.Require<BindingMap>("gearBrowser");
            Assert.That(browser.gameObject.activeSelf, Is.False);
            foreach (string slot in new[] { "weapon", "helmet", "armor", "necklace", "ring", "boots" })
            {
                Assert.That(map.Require<GameButton>("gearOpen_" + slot), Is.Not.Null);
                Assert.That(browser.Require<Image>("icon_" + slot).sprite, Is.Not.Null);
            }
            Assert.That(browser.Require<GameButton>("apply"), Is.Not.Null);
            Assert.That(browser.Require<GameButton>("confirm"), Is.Not.Null);
        }

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
        public void ArtifactBagDetailsUseThePaintingEmblemTabsAndActionIcons()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            foreach (string key in new[] { "artifactDetailSkillTab", "artifactDetailStoryTab", "artifactDetailZoom", "artifactChange" })
                Assert.That(page.Require<GameButton>(key).targetGraphic.raycastTarget, Is.True, key);
            Assert.That(page.Require<Image>("artifactSellGlyph").sprite.name, Is.EqualTo("cm_icon_etcbin"));
            Assert.That(page.Require<Image>("artifactLockOn").sprite.name, Is.EqualTo("cm_icon_etclocked"));
            Assert.That(page.Require<Image>("artifactLockOff").sprite.name, Is.EqualTo("cm_icon_etclockun"));
            Assert.That(page.Require<BindingMap>("artifactDetailBadge"), Is.Not.Null);
            Assert.That(page.Require<Image>("artifactDetailEmblem"), Is.Not.Null);
            Assert.That(page.Require<Image>("artifactDetailWearer"), Is.Not.Null);
        }

        [Test]
        public void WornEquipmentBagBindsRowActionsAndReusableFilterChoices()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath).GetComponent<BindingMap>();
            var row = page.Require<GameObject>("bagRow").GetComponent<BindingMap>();
            foreach (string key in new[] { "bagManage", "bagPortrait", "bagArtifact", "bagInspect0", "bagInspect1",
                         "bagInspect2", "bagInspect3", "bagInspect4", "bagInspect5" })
                Assert.That(row.Require<GameButton>(key).targetGraphic.raycastTarget, Is.True, key);
            Assert.That(page.Require<GameButton>("bagSort").targetGraphic.raycastTarget, Is.True);
            var menu = page.Require<BindingMap>("wornFilters");
            Assert.That(menu.gameObject.activeSelf, Is.False);
            foreach (string key in new[] { "sort_stars", "sort_level", "sort_element", "sort_name", "sort_acquired",
                         "sort_ability", "reverse", "hideMax", "hideFull", "close" })
                Assert.That(menu.Require<GameButton>(key), Is.Not.Null, key);
            foreach (string option in new[] { "fire", "ice", "wind", "light", "dark", "all" })
                Assert.That(menu.Require<BindingMap>("element_" + option).Require<GameButton>("button"), Is.Not.Null);
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
