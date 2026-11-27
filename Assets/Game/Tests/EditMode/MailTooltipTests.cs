using AstralOath.Client.Common;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class MailTooltipTests
    {
        [Test]
        public void UnverifiedItemDoesNotInventDescriptionOrEchoClaimQuantity()
        {
            var unknown = new MailReward { kind = "currency", key = "future_item", name = "测试物品", quantity = 100 };
            Assert.That(MailItemDescriptions.GetKind(unknown), Is.Empty);
            Assert.That(MailItemDescriptions.GetDescription(unknown), Is.Empty);
            Assert.That(MailItemDescriptions.GetDescription(null), Is.Empty);
        }

        [Test]
        public void EnergyAndGiftUseFilmedDescriptionsAndSeparateFixedTemplates()
        {
            var energy = new MailReward { kind = "energy", key = "energy", quantity = 50 };
            var gift = new MailReward { kind = "bundle", key = "gm_gift", quantity = 1 };
            Assert.That(MailItemDescriptions.GetKind(energy), Is.EqualTo("入场券"));
            Assert.That(MailItemDescriptions.GetDescription(energy), Is.EqualTo("冒险时必不可少的行动力。"));
            Assert.That(MailItemDescriptions.DesignHeight(energy), Is.EqualTo(375));
            Assert.That(MailItemDescriptions.GetKind(gift), Is.EqualTo("特别礼物"));
            Assert.That(MailItemDescriptions.GetDescription(gift), Does.Contain("领取时可立即获得箱内所有道具"));
            Assert.That(MailItemDescriptions.DesignHeight(gift), Is.EqualTo(285));
            Assert.That(MailItemDescriptions.Owned(1960), Is.EqualTo("目前持有1,960个"));
        }

        [Test]
        public void CovenantBookmarkUsesSourceClassificationAndDescriptionInsteadOfClaimAmount()
        {
            var reward = new MailReward { kind = "currency", key = "bookmarks", name = "誓约书签", quantity = 5 };
            Assert.That(MailItemDescriptions.GetKind(reward), Is.EqualTo("召唤"));
            Assert.That(MailItemDescriptions.GetDescription(reward), Does.Contain("特殊书页"));
            Assert.That(MailItemDescriptions.GetDescription(reward), Does.Not.Contain("×5"));
        }

        [Test]
        public void ItemCardUsesOriginalFrameAndFixedCanvasDimensions()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab");
            var layer = prefab.GetComponent<BindingMap>().Require<GameObject>("tooltip");
            var card = layer.transform.Find("Attachment description") as RectTransform;
            Assert.That(card, Is.Not.Null);
            Assert.That(card.anchorMin, Is.EqualTo(card.anchorMax), "物品卡不得随窗口分别拉伸长宽");
            Assert.That(card.sizeDelta, Is.EqualTo(new Vector2(480, 375)));
            var background = card.Find("Original frame and shadow").GetComponent<Image>();
            Assert.That(background.sprite.name, Is.EqualTo("MailItemTooltipFrame"));
            Assert.That(background.sprite.border, Is.EqualTo(new Vector4(65, 60, 65, 60)));
            Assert.That(background.type, Is.EqualTo(Image.Type.Sliced));
            var icon = prefab.GetComponent<BindingMap>().Require<Image>("tooltipIcon");
            var iconBackground = icon.transform.parent.GetComponent<Image>();
            Assert.That(iconBackground, Is.Not.Null, "物品图必须保留方形原物品底板");
            Assert.That(iconBackground.sprite.name, Is.EqualTo("_itembg_stuff_1"));
            Assert.That(icon.preserveAspect, Is.True);
        }
    }
}
