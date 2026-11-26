using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class InventoryButtonPrefabTests
    {
        private const string Home = "Assets/Game/Modules/Home/Res/Prefab/";
        private const string Styles = "Assets/Game/Common/Res/Prefab/Styles/";

        private static BindingMap Map(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Home + name + ".prefab").GetComponent<BindingMap>();

        [Test]
        public void EquipmentEnhancementEntrancesKeepTheCompactOriginalBlueCorners()
        {
            var bag = Map("HeroInventory").Require<BindingMap>("gearBrowser").Require<BindingMap>("bagPane");
            CheckBlue(bag.Require<GameButton>("enhance"));
            var single = Map("SingleSlotEquipment");
            foreach (var key in new[] { "currentEnhance", "candidateEnhance", "popupEnhance" })
                CheckBlue(single.Require<GameButton>(key));
        }

        [Test]
        public void ArtifactEnhancementEntrancesUseTheSameCommonBlueButton()
        {
            var inventory = Map("HeroInventory");
            CheckBlue(inventory.Require<GameButton>("artifactAction_enhance"));
            var hero = inventory.Require<BindingMap>("artifactHeroEquipment");
            foreach (var key in new[] { "training", "enhance" }) CheckBlue(hero.Require<GameButton>(key));
        }

        [Test]
        public void BothEnhancementPricesSliceTheBackingInsideTheCommonGreenButton()
        {
            CheckCost(Map("EquipmentEnhancement").Require<GameButton>("submit"));
            CheckCost(Map("HeroInventory").Require<BindingMap>("artifactEnhancement").Require<GameButton>("submit"));
        }

        [Test]
        public void AutomaticStagesHaveIndependentSelectedArtworkAndRealPowderCheckbox()
        {
            var page = Map("EquipmentEnhancement");
            var powder = page.Require<GameButton>("powderOnly");
            var powderMap = powder.GetComponent<BindingMap>();
            Assert.That(powderMap, Is.Not.Null, "Powder must use the shared checkbox style, not a text square.");
            Assert.That(powderMap.Require<Image>("check").sprite.name, Is.EqualTo("cm_checkicon2"));
            Assert.That(powderMap.Require<Image>("checkFrame").sprite.name, Is.EqualTo("cm_checkbg"));
            Assert.That(((Image)powder.targetGraphic).sprite, Is.Null, "The source powder row has no black button surround.");
            for (int stage = 3; stage <= 15; stage += 3)
            {
                var button = page.Require<GameButton>("auto" + stage);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(button), Is.EqualTo(Styles + "Black/ConfirmButton.prefab"));
                var highlight = button.GetComponent<BindingMap>().Require<Image>("selected");
                Assert.That(highlight.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(highlight.raycastTarget, Is.False);
            }
        }

        [Test]
        public void ResultSweepsUseNativePmaFramesAndNeverInterceptMaterialInput()
        {
            var page = Map("EquipmentEnhancement");
            foreach (var key in new[] { "subSweep0", "subSweep1", "subSweep2", "subSweep3", "scoreSweep" })
            {
                var sweep = page.Require<RawImage>(key);
                Assert.That(sweep.enabled, Is.False);
                Assert.That(sweep.raycastTarget, Is.False);
                Assert.That(sweep.texture.width, Is.EqualTo(4096), "The 512px source frame cells must not be resized during import.");
                Assert.That(sweep.material.shader.name, Is.EqualTo("AstralOath/UI/TouchPulse"));
            }
        }

        private static void CheckBlue(GameButton button)
        {
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(button), Is.EqualTo(Styles + "Blue/ConfirmButton.prefab"));
            var image = (Image)button.targetGraphic;
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            // inventory_equip_popup.csb cap(45,12,62,35), original 152x61 sprite.
            Assert.That(image.sprite.border, Is.EqualTo(new Vector4(45, 14, 45, 12)));
            Assert.That(button.spriteState.pressedSprite.border, Is.EqualTo(image.sprite.border));
            Assert.That(button.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
        }

        private static void CheckCost(GameButton button)
        {
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(button), Is.EqualTo(Styles + "GreenCost/ConfirmButton.prefab"));
            var plate = button.GetComponent<BindingMap>().Require<Image>("costPlate");
            Assert.That(plate.type, Is.EqualTo(Image.Type.Sliced), "A simple Image stretches the round source texture into an oval.");
            // unit_item_upgrade.csb cap(14,14,8,6), original 36x34 sprite.
            Assert.That(plate.sprite.border, Is.EqualTo(new Vector4(14, 14, 14, 14)));
            Assert.That(plate.preserveAspect, Is.False);
            Assert.That(plate.raycastTarget, Is.False);
        }
    }
}
