using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class GmMailLayoutTests
    {
        private const string Prefab = "Assets/Game/Modules/FoundationDemo/Res/Prefab/DevelopmentGmWindow.prefab";
        [Test] public void DraftAndFrozenPreviewUsePureTextAndSeparateEditableRegions()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var map = prefab.GetComponent<BindingMap>();
            var compose = map.Require<GameObject>("mailCompose").transform;
            var preview = map.Require<GameObject>("mailPreview").transform;
            foreach (string key in new[] { "mailTitle", "mailBody", "mailExpiry", "mailQuantity", "mailSearch" })
            {
                var input = map.Require<TMP_InputField>(key);
                Assert.That(input.transform.IsChildOf(compose), Is.True);
                Assert.That(input.richText, Is.False);
                Assert.That(input.textComponent.richText, Is.False);
            }
            Assert.That(map.Require<GameText>("mailFrozenText").richText, Is.False);
            Assert.That(map.Require<GameButton>("mailSend").transform.IsChildOf(preview), Is.True);
            Assert.That(map.Require<GameButton>("mailAnother").transform.IsChildOf(preview), Is.True);
            Assert.That(map.Require<GameButton>("mailPreviewMake").transform.IsChildOf(compose), Is.True);
        }
        [TestCase(1920,1080)] [TestCase(3840,2160)] [TestCase(1440,1080)]
        public void TemplateAndResourceListsKeepUsableHeightAndPreviewCanScrollLongBody(int width, int height)
        {
            var canvas = new GameObject("GM mail layout", typeof(RectTransform), typeof(Canvas));
            try
            {
                canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)canvas.transform;
                rect.sizeDelta = new Vector2(width * 1080f / height, 1080);
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), rect, false);
                var map = instance.GetComponent<BindingMap>();
                map.Require<GameObject>("panel").SetActive(true);
                map.Require<GameObject>("editor").SetActive(false);
                map.Require<GameObject>("mailPanel").SetActive(true);
                map.Require<GameObject>("mailCompose").SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)instance.transform); Canvas.ForceUpdateCanvases();
                Assert.That(map.Require<ScrollRect>("mailTemplates").viewport.rect.height, Is.GreaterThan(400));
                Assert.That(map.Require<ScrollRect>("mailResources").viewport.rect.height, Is.GreaterThan(220));
                Assert.That(map.Require<ScrollRect>("mailAttachments").viewport.rect.height, Is.GreaterThan(90));
                map.Require<GameObject>("mailCompose").SetActive(false); map.Require<GameObject>("mailPreview").SetActive(true);
                map.Require<GameText>("mailFrozenText").text = new string('文', 8000);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)instance.transform); Canvas.ForceUpdateCanvases();
                var scroll = map.Require<ScrollRect>("mailFrozen");
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            }
            finally { Object.DestroyImmediate(canvas); }
        }
    }
}
