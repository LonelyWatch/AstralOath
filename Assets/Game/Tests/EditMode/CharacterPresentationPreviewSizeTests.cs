using AstralOath.Client.CharacterLab;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.Tests
{
    public sealed class CharacterPresentationPreviewSizeTests
    {
        private GameObject canvasRoot,cameraRoot;
        private Canvas canvas;
        private RawImage image;
        private Camera camera;
        private Material source;
        private CharacterBlurPreview preview;
        [SetUp] public void SetUp()
        {
            canvasRoot=new GameObject("preview-canvas",typeof(RectTransform),typeof(Canvas));
            canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var imageRoot=new GameObject("preview",typeof(RectTransform),typeof(RawImage));
            image=imageRoot.GetComponent<RawImage>();image.rectTransform.SetParent(canvasRoot.transform,false);
            image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(.5f,.5f);
            image.rectTransform.sizeDelta=new Vector2(1920,1080);
            cameraRoot=new GameObject("preview-camera",typeof(Camera));camera=cameraRoot.GetComponent<Camera>();
            source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Modules/CharacterPresentation/Res/Materials/CharacterSpriteBlur.mat");
            Assert.That(source,Is.Not.Null);
        }
        [TearDown] public void TearDown()
        {
            preview?.Dispose();
            Object.DestroyImmediate(canvasRoot);Object.DestroyImmediate(cameraRoot);
        }
        [TestCase(.625f,1200,675)]
        [TestCase(.75f,1440,810)]
        [TestCase(1f,1920,1080)]
        public void TextureUsesThePreviewPixelExtentInsteadOfTheWholeScreen(float scale,int width,int height)
        {
            canvas.scaleFactor=scale;Canvas.ForceUpdateCanvases();
            preview=new CharacterBlurPreview(camera,image,source);
            AssertSize(width,height);
        }
        [Test] public void ResizeReplacesOnlyChangedTargetsAndDisposeRestoresCameraOwnership()
        {
            var previous=new RenderTexture(64,64,0);
            try
            {
                camera.targetTexture=previous;camera.aspect=1.5f;
                canvas.scaleFactor=.625f;Canvas.ForceUpdateCanvases();
                preview=new CharacterBlurPreview(camera,image,source);
                var first=camera.targetTexture;AssertSize(1200,675);
                preview.Resize();Assert.That(camera.targetTexture,Is.SameAs(first));
                canvas.scaleFactor=.75f;Canvas.ForceUpdateCanvases();preview.Resize();
                AssertSize(1440,810);Assert.That(first==null,Is.True,"Superseded target must be destroyed in EditMode.");
                var last=camera.targetTexture;preview.Dispose();
                Assert.That(camera.targetTexture,Is.SameAs(previous));
                Assert.That(camera.aspect,Is.EqualTo(1.5f).Within(.0001f));
                Assert.That(image.texture,Is.Null);Assert.That(last==null,Is.True);
                Assert.DoesNotThrow(()=>preview.Dispose());
            }
            finally {camera.targetTexture=null;Object.DestroyImmediate(previous);}
        }
        [Test] public void SavedPrefabPreviewSharesTheCenteredReferenceLayout()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/CharacterLab/Res/Prefab/CharacterLabWindow.prefab");
            Assert.That(prefab,Is.Not.Null);
            var rect=prefab.GetComponent<BindingMap>().Require<RawImage>("preview").rectTransform;
            Assert.That(rect.anchorMin,Is.EqualTo(new Vector2(.5f,.5f)));
            Assert.That(rect.anchorMax,Is.EqualTo(new Vector2(.5f,.5f)));
            Assert.That(rect.anchoredPosition,Is.EqualTo(Vector2.zero));
            Assert.That(rect.sizeDelta,Is.EqualTo(new Vector2(1920,1080)));
        }
        private void AssertSize(int width,int height)
        {
            var target=camera.targetTexture;
            Assert.That(target.width,Is.EqualTo(width));Assert.That(target.height,Is.EqualTo(height));
            Assert.That(image.texture,Is.SameAs(target));
            Assert.That(camera.aspect,Is.EqualTo(width/(float)height).Within(.0001f));
            var resolution=image.material.GetVector("u_resolution");
            Assert.That(resolution.x,Is.EqualTo(width));Assert.That(resolution.y,Is.EqualTo(height));
        }
    }
}
