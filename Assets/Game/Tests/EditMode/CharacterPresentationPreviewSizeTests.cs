using AstralOath.Client.CharacterLab;
using AstralOath.Client.Common;
using System.Linq;
using Newtonsoft.Json.Linq;
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
        [Test] public void KenInvertChangesDisplayRgbForOneSecondAndRestoresOnStopOrReset()
        {
            var invert=KenProgram(26);
            Assert.That((int)invert["duration"],Is.EqualTo(1000));
            image.rectTransform.sizeDelta=new Vector2(64,4);
            preview=new CharacterBlurPreview(camera,image,source);
            var display=new Color(.8f,.25f,.65f,.37f);
            Color original=WorkingColor(display);
            Color inverted=WorkingColor(new Color(.2f,.75f,.35f,.37f));
            AssertColor(DrawPixel(original,original),original);
            preview.Play(invert,_=>{});
            preview.Tick(.99f);
            AssertColor(DrawPixel(original,original),inverted);
            preview.Tick(.02f);
            AssertColor(DrawPixel(original,original),original);
            preview.Play(invert,_=>{});
            preview.Stop("pp:69");
            AssertColor(DrawPixel(original,original),inverted);
            preview.Stop("pp:26");
            AssertColor(DrawPixel(original,original),original);
            preview.Play(invert,_=>{});
            preview.Reset();
            AssertColor(DrawPixel(original,original),original);
        }
        [Test] public void KenSourceBlurAndInvertHaveIndependentDurationsAndStops()
        {
            image.rectTransform.sizeDelta=new Vector2(64,4);
            preview=new CharacterBlurPreview(camera,image,source);
            var blur=KenProgram(82);
            var invert=KenProgram(26);
            preview.Play(blur,_=>{});
            var blurred=DrawPixel(Color.black,Color.white);
            Assert.That(blurred.r,Is.InRange(.05f,.95f),"Ken's directional blur must mix both sides of the edge.");
            preview.Play(invert,_=>{});
            preview.Stop("pp:26");
            AssertColor(DrawPixel(Color.black,Color.white),blurred);
            preview.Play(invert,_=>{});
            preview.Tick(1.01f);
            AssertColor(DrawPixel(Color.black,Color.white),blurred);
            preview.Tick(.8f);
            AssertColor(DrawPixel(Color.black,Color.white),Color.white);
            preview.Play(KenProgram(69),_=>{});
            preview.Play(invert,_=>{});
            preview.Tick(.21f);
            AssertColor(DrawPixel(Color.black,Color.white),Color.black);
        }
        private static JObject KenProgram(int guid)
        {
            var graph=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Modules/CharacterPresentation/Res/Characters/c1047/Configs/stagept/c_ken_skill_03.graph.json");
            return JObject.Parse(graph.text)["source"]["entities"].OfType<JObject>().Single(node=>(int)node["guid"]==guid);
        }
        private static Color WorkingColor(Color display)=>QualitySettings.activeColorSpace==ColorSpace.Linear?display.linear:display;
        private Color DrawPixel(Color left,Color right)
        {
            var texture=new Texture2D(64,4,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var target=RenderTexture.GetTemporary(64,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var readback=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;
            try
            {
                texture.SetPixels(Enumerable.Range(0,256).Select(index=>index%64<32?left:right).ToArray());
                texture.Apply();
                Graphics.Blit(texture,target,image.material);
                RenderTexture.active=target;
                readback.ReadPixels(new Rect(32,2,1,1),0,0);
                return readback.GetPixel(0,0);
            }
            finally
            {
                RenderTexture.active=previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(readback);
            }
        }
        private static void AssertColor(Color actual,Color expected)
        {
            for(int channel=0;channel<4;channel++)
                Assert.That(actual[channel],Is.EqualTo(expected[channel]).Within(.002f),"Channel "+channel);
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
