using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class SummonBannerAndFooterTests
    {
        private const string Prefab = "Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab";

        [Test]
        public void RepeatDrawUsesTheSourcePricePlateAndOneHitArea()
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<BindingMap>();
            var button = map.Require<GameButton>("resultAgain");
            var rect = (RectTransform)button.transform;
            var size = new Vector2(340, 71);
            Assert.That(rect.sizeDelta, Is.EqualTo(size * 1.5f));
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(-361, 20) * 1.5f));
            Assert.That(button.targetGraphic.raycastTarget, Is.True);
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
                if (graphic != button.targetGraphic)
                    Assert.That(graphic.raycastTarget, Is.False, graphic.name);
            Assert.That(button.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
            Assert.That(button.image.sprite.name, Is.EqualTo("_btn_green"));
            Assert.That(button.spriteState.pressedSprite.name, Is.EqualTo("_btn_green_p"));
            var plate = rect.Find("Native repeat price plate").GetComponent<Image>();
            Assert.That(plate.color, Is.EqualTo(Color.black), "Source n_counter/bg tints the nine-slice black.");
            void Center(RectTransform child, float x, float y)
            {
                var center = Vector2.Scale((child.anchorMin + child.anchorMax) * .5f, size);
                Assert.That(Vector2.Distance(center, new Vector2(x, y)), Is.LessThan(.001f), child.name);
            }
            Center(plate.rectTransform, 94, 36.5f);
            foreach (var key in new[] { "resultAgainCurrency", "resultAgainEpicCurrency" })
            {
                var icon = map.Require<Image>(key).rectTransform;
                Center(icon, 32, 37);
                Assert.That(Vector2.Distance(Vector2.Scale(icon.anchorMax - icon.anchorMin, size),
                    new Vector2(50.4f, 50.4f)), Is.LessThan(.001f), key);
            }
            Center(map.Require<GameText>("resultAgainPrice").rectTransform, 93, 39);
            Center((RectTransform)rect.Find("Summon again"), 238, 39);
        }

        [Test]
        public void SummonHeaderUsesSharedControlsAndWhiteLabelsHaveARealOutline()
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<BindingMap>();
            foreach (var key in new[] { "singleLabel", "tenLabel" })
            {
                var material = map.Require<GameText>(key).fontSharedMaterial;
                Assert.That(material.IsKeywordEnabled("OUTLINE_ON"), Is.True, key);
                Assert.That(material.GetFloat("_OutlineWidth"), Is.GreaterThan(.15f), key);
            }
            foreach (var key in new[] { "transmitBalance", "moonBalance", "mysticBalance", "balance", "skyBalance" })
            {
                var label = map.Require<GameText>(key);
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(label);
                Assert.That(AssetDatabase.GetAssetPath(source), Does.EndWith("/TopBarToken.prefab"), key);
            }
            var back = map.Require<GameButton>("back");
            Assert.That(back.image.sprite.name, Is.EqualTo("cm_hud_top_btn_back"));
            Assert.That(back.spriteState.pressedSprite.name, Is.EqualTo("cm_hud_top_btn_back_p"));
            Assert.That(map.Require<GameButton>("settings").transform.Find("Native settings icon")
                .GetComponent<Image>().sprite.name, Is.EqualTo("renew_icon_quick_setting"));
        }

        [Test]
        public void CovenantRosterNamesHaveLightItalicStrokesAndAThinDarkOutline()
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<BindingMap>();
            for (int index = 0; index < 7; index++)
            {
                string key = $"wishHero{index}Name";
                var label = map.Require<GameText>(key);
                var material = label.fontSharedMaterial;
                Assert.That(label.fontStyle, Is.EqualTo(FontStyles.Italic), key);
                Assert.That(material.GetFloat("_FaceDilate"), Is.InRange(0f, .12f), key);
                Assert.That(material.IsKeywordEnabled("OUTLINE_ON"), Is.True, key);
                Assert.That(material.GetFloat("_OutlineWidth"), Is.InRange(.05f, .12f), key);
                Assert.That(material.GetColor("_OutlineColor").maxColorComponent, Is.LessThan(.1f), key);
            }
        }

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
