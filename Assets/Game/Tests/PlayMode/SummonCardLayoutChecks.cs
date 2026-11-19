#if UNITY_EDITOR
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    internal static class SummonCardLayoutChecks
    {
        internal static void Verify(BindingMap window, RectTransform card)
        {
            if (card == (RectTransform)window.Require<GameObject>("wishHero0Card").transform)
            {
                // Inspect the material after HeroRosterPortrait has installed its camera texture.
                float[] authoredWidths = { 500, 134, 135, 135, 136, 134, 500 };
                for (int index = 0; index < 7; index++)
                {
                    var image = window.Require<UnityEngine.UI.RawImage>("wishHero" + index);
                    Assert.That(image.material.shader.name, Is.EqualTo("AstralOath/UI/SummonWishPortrait"));
                    Assert.That(image.materialForRendering.GetFloat("_Stencil"), Is.GreaterThan(0f));
                    var mask = image.GetComponentInParent<UnityEngine.UI.Mask>().rectTransform;
                    Assert.That(mask.rect.width, Is.EqualTo(authoredWidths[index] * 1.5f).Within(.1f));
                    var caption = window.Require<GameText>("wishHero" + index + "Name");
                    Assert.That(caption.fontStyle.HasFlag(TMPro.FontStyles.Italic), Is.True);
                    var nameMaterial = caption.fontSharedMaterial;
                    Assert.That(nameMaterial.GetTexture("_MainTex"), Is.EqualTo(caption.font.atlasTextures[0]));
                    Assert.That(caption.color, Is.EqualTo(Color.white));
                    Assert.That(nameMaterial.GetColor("_FaceColor"), Is.EqualTo(Color.white));
                    Assert.That(nameMaterial.IsKeywordEnabled("UNDERLAY_ON"), Is.True);
                    Assert.That(nameMaterial.GetFloat("_UnderlayDilate"), Is.GreaterThan(0f));
                    Assert.That(nameMaterial.GetFloat("_UnderlayOffsetX"), Is.Zero);
                    Assert.That(nameMaterial.GetFloat("_UnderlayOffsetY"), Is.Zero);
                    Assert.That(nameMaterial.GetColor("_UnderlayColor").a, Is.EqualTo(1f));
                    var label = caption.transform.parent.Find("Ownership/Ownership label shade") as RectTransform;
                    Assert.That(label.rect.size, Is.EqualTo(new Vector2(164, 48) * 1.5f));
                    var line = caption.transform.parent.Find("Native banner divider").GetComponent<UnityEngine.UI.Image>();
                    Assert.That(line.sprite.name, Is.EqualTo(index == 6 ? "gacha_wish_deco_2" : "gacha_wish_deco_1"));
                }
            }
            var root = (RectTransform)window.transform;
            Vector2 original = root.sizeDelta;
            try
            {
                Resize(root, 2217.6f, 1080);
                Vector2 reference = card.rect.size;
                Vector3[] corners = new Vector3[4];
                card.GetWorldCorners(corners);
                float aspect = Vector3.Distance(corners[0], corners[3]) / Vector3.Distance(corners[0], corners[1]);
                foreach (int width in new[] { 1920, 1440, 2560 })
                {
                    Resize(root, width, 1080);
                    Assert.That(Vector2.Distance(card.rect.size, reference), Is.LessThan(.1f),
                        $"The card changed its design dimensions at {width}×1080.");
                    card.GetWorldCorners(corners);
                    float resizedAspect = Vector3.Distance(corners[0], corners[3]) / Vector3.Distance(corners[0], corners[1]);
                    Assert.That(resizedAspect, Is.EqualTo(aspect).Within(.002f), "The card stretched on screen.");
                }
            }
            finally
            {
                root.sizeDelta = original;
                Canvas.ForceUpdateCanvases();
            }
        }

        private static void Resize(RectTransform window, float width, float height)
        {
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Canvas.ForceUpdateCanvases();
        }
    }
}
#endif
