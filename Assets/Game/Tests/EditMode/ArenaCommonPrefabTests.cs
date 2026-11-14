using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaCommonPrefabTests
    {
        private const string PrefabRoot = "Assets/Game/Common/Res/Prefab/";

        [TestCase("ConfirmButton", "_btn_green", "UiAccept")]
        [TestCase("CancelButton", "_btn_gold", "UiCancel")]
        public void CommonButtonsHaveNativeArtworkAndClickAudio(string prefab, string sprite, string clip)
        {
            var map = Load(prefab).GetComponent<BindingMap>();
            var button = map.Require<GameButton>("button");
            var label = map.Require<GameText>("label");
            var surface = (Image)button.targetGraphic;
            Assert.That(((RectTransform)map.transform).sizeDelta, Is.EqualTo(new Vector2(288f, 91.5f)));
            Assert.That(label.raycastTarget, Is.False);
            Assert.That(label.fontSize, Is.EqualTo(31.5f));
            Assert.That(label.fontStyle.HasFlag(FontStyles.Bold), Is.True);
            Assert.That(label.rectTransform.anchoredPosition.y, Is.EqualTo(5.49f).Within(.001f));
            Assert.That(surface.sprite.name, Is.EqualTo(sprite));
            Assert.That(surface.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(surface.pixelsPerUnitMultiplier, Is.EqualTo(2f / 3f).Within(.001f));
            Assert.That(surface.sprite.border, Is.EqualTo(new Vector4(50f, 14f, 50f, 12f)));
            Assert.That(button.spriteState.pressedSprite.name, Is.EqualTo(sprite + "_p"));
            Assert.That(button.spriteState.pressedSprite.border, Is.EqualTo(surface.sprite.border));
            var audio = (AudioSource)new SerializedObject(button).FindProperty("clickAudio").objectReferenceValue;
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.clip.name, Is.EqualTo(clip));
            Assert.That(audio.playOnAwake, Is.False);
        }

        [Test]
        public void ChoicePopupHasBlockingSurfaceAndInactiveRowTemplate()
        {
            var map = Load("ChoicePopup").GetComponent<BindingMap>();
            var body = map.Require<RectTransform>("body");
            var dismiss = map.Require<GameButton>("dismiss");
            var template = map.Require<GameButton>("choiceTemplate");
            Assert.That(dismiss.transform.GetSiblingIndex(), Is.LessThan(body.GetSiblingIndex()));
            Assert.That(dismiss.targetGraphic.raycastTarget, Is.True);
            Assert.That(body.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(template.gameObject.activeSelf, Is.False);
            Assert.That(template.GetComponent<BindingMap>().Require<GameText>("label"), Is.Not.Null);
            Assert.That(template.GetComponent<BindingMap>().Require<Image>("check"), Is.Not.Null);
        }

        [Test]
        public void AvatarUsesNativeMaskFrameAndOptionalBadgesWithoutCatchingInput()
        {
            var prefab = Load("PlayerAvatar");
            var map = prefab.GetComponent<BindingMap>();
            var portrait = map.Require<Image>("portrait");
            Assert.That(portrait.GetComponentInParent<Mask>(true), Is.Not.Null);
            Assert.That(map.Require<Image>("frame").sprite.name, Is.EqualTo("_hero_s_frame_enemy"));
            Assert.That(map.Require<Image>("owner").gameObject.activeSelf, Is.False);
            Assert.That(map.Require<Image>("badge").gameObject.activeSelf, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Image>(true).All(image => !image.raycastTarget), Is.True);
        }

        [Test]
        public void ChoiceBinderSelectsOnceAndClearsRowsOnClose()
        {
            var instance = Object.Instantiate(Load("ChoicePopup"));
            try
            {
                using var binder = new ChoicePopupBinder(ChoicePopup.FromInstance(instance));
                int selected = -1;
                binder.Open(new[] { "全部", "公开" }, 0, value => selected = value);
                var view = instance.GetComponent<BindingMap>();
                var body = view.Require<RectTransform>("body");
                var rows = body.GetComponentsInChildren<GameButton>(false);
                Assert.That(rows, Has.Length.EqualTo(2));
                Assert.That(rows[0].GetComponent<BindingMap>().Require<Image>("check").gameObject.activeSelf, Is.True);
                rows[1].onClick.Invoke();
                Assert.That(selected, Is.EqualTo(1));
                Assert.That(instance.activeSelf, Is.False);
                Assert.That(body.childCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void AvatarResetClearsOptionalPortraitAndRestoresTheNativeFrame()
        {
            var instance = Object.Instantiate(Load("PlayerAvatar"));
            try
            {
                var view = PlayerAvatar.FromInstance(instance);
                var map = instance.GetComponent<BindingMap>();
                var native = map.Require<Image>("frame").sprite;
                var other = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Common/Res/Textures/cm_icon_etcleader.png");
                view.Set(other, other, true, other);
                Assert.That(map.Require<Image>("portrait").sprite, Is.SameAs(other));
                Assert.That(map.Require<Image>("owner").gameObject.activeSelf, Is.True);
                view.Set(null);
                Assert.That(map.Require<Image>("portrait").enabled, Is.False);
                Assert.That(map.Require<Image>("frame").sprite, Is.SameAs(native));
                Assert.That(map.Require<Image>("owner").gameObject.activeSelf, Is.False);
                Assert.That(map.Require<Image>("badge").gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static GameObject Load(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            return prefab;
        }
    }
}