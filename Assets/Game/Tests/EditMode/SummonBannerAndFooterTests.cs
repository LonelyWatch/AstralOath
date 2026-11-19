using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class SummonBannerAndFooterTests
    {
        private const string Prefab = "Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab";

        [Test]
        public void CardsKeepTheirNativeAspectAndThePriceUsesOneLinearSpaceOverlay()
        {
            var view = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<BindingMap>();
            foreach (var key in new[] { "iseria", "covenant" })
            {
                var slot = (RectTransform)view.Require<GameButton>(key).transform;
                Assert.That(slot.anchorMin, Is.EqualTo(new Vector2(0, 1)), key);
                Assert.That(slot.anchorMax, Is.EqualTo(slot.anchorMin), key);
                Assert.That(slot.sizeDelta.x / slot.sizeDelta.y, Is.EqualTo(320.5f / 131f).Within(.001f), key);
                var poster = view.Require<Image>(key + "Poster");
                var mask = poster.transform.parent.GetComponent<Mask>();
                Assert.That(mask, Is.Not.Null, key);
                Assert.That(mask.transform.parent, Is.EqualTo(slot), key);
                Assert.That(mask.enabled, Is.True, key);
                Assert.That(mask.showMaskGraphic, Is.False, key);
            }

            foreach (var key in new[] { "single", "ten" })
            {
                var draw = view.Require<GameButton>(key);
                var badge = draw.transform.Find("Price badge").GetComponent<Image>();
                Assert.That(badge.sprite.name, Is.EqualTo("_btn_small_black_p_linear"));
                Assert.That(badge.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(view.Require<GameText>(key + "Price").fontSize, Is.EqualTo(28.8f).Within(.01f));
                Assert.That(view.Require<GameText>(key + "Label").fontSize, Is.EqualTo(31.5f).Within(.01f));
            }

            var free = view.Require<GameButton>("free");
            Assert.That(((Image)free.targetGraphic).sprite.name, Is.EqualTo("cm_ballonsm"));
            Assert.That(free.spriteState.pressedSprite.name, Is.EqualTo("cm_ballonsm_p"));
            Assert.That(free.transform.Find("Bubble pointer").GetComponent<Image>().sprite.name,
                Is.EqualTo("cm_ballonsm_arrow"));
        }
    }
}
