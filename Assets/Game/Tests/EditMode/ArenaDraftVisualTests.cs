using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDraftVisualTests
    {
        private const string Path = "Assets/Game/Modules/Arena/Res/Prefab/ArenaDraft.prefab";

        [Test]
        public void TeamPortraitsKeepNativeAspectAndOpposingOrientation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var bindings = prefab.GetComponent<BindingMap>();
            var own = bindings.Require<BindingMap>("blue0").Require<Image>("portrait");
            var enemy = bindings.Require<BindingMap>("red0").Require<Image>("portrait");
            Assert.That(own.preserveAspect && enemy.preserveAspect, Is.True, "Portrait crops must not stretch to the differently sized trays.");
            Assert.That(own.rectTransform.lossyScale.x, Is.LessThan(0));
            Assert.That(enemy.rectTransform.lossyScale.x, Is.GreaterThan(0));
        }

        [Test]
        public void PendingSlotOverlaySharesTheTraySilhouetteOnBothTeams()
        {
            var bindings = AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<BindingMap>();
            foreach (var side in new[] { "blue", "red" })
            for (int index = 0; index < 5; index++)
            {
                var card = bindings.Require<BindingMap>(side + index);
                var tray = (Image)card.Require<GameButton>("button").targetGraphic;
                var pending = card.Require<Image>("pending");
                var confirmation = card.Require<RawImage>("confirm");
                Vector2 center = Center(tray.rectTransform);
                Assert.That(pending.sprite.name, Is.EqualTo(tray.sprite.name + "_glow_" + side));
                Assert.That(Vector2.Distance(Center(pending.rectTransform), center), Is.LessThan(.001f));
                Assert.That(Vector2.Distance(Center(confirmation.rectTransform), center), Is.LessThan(.001f));
                Assert.That(pending.rectTransform.anchorMin.x, Is.LessThan(tray.rectTransform.anchorMin.x));
                Assert.That(pending.rectTransform.localScale.x, Is.GreaterThan(0),
                    "The source red outline is already mirrored.");
                Assert.That(confirmation.raycastTarget, Is.False);
                Assert.That(confirmation.transform.GetSiblingIndex(), Is.GreaterThan(pending.transform.GetSiblingIndex()));

                Vector2 Center(RectTransform picture) => (picture.anchorMin + picture.anchorMax) * .5f;
            }
        }

        [Test]
        public void FormationHasNativeChangeControlsAndReadableSeparatedFloorRings()
        {
            var bindings = AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<BindingMap>();
            var upper = bindings.Require<BindingMap>("formation2");
            var lower = bindings.Require<BindingMap>("formation1");
            var change = upper.Require<Image>("change");
            Assert.That(change.sprite.name, Is.EqualTo("hero_config_btn_change"));
            Assert.That(((RectTransform)upper.transform).anchorMin.x, Is.LessThan(((RectTransform)lower.transform).anchorMin.x));
        }
    }
}
