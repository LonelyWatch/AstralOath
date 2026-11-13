using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleUnitStatusPresentationTests
    {
        private GameObject root;
        private Camera camera;
        private Canvas canvas;
        private Transform actor, bone;
        private RectTransform marker, frame;
        private Image hp;
        private IDisposable presenter;
        private Type type;
        private Texture2D texture;
        private Sprite sprite;
        private RenderTexture renderTexture;
        private Vector3 originalPosition, originalScale;
        [SetUp] public void SetUp()
        {
            root = new GameObject("Health presentation regression");
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform); camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.orthographicSize = 5;
            renderTexture = new RenderTexture(512, 512, 0); camera.targetTexture = renderTexture;
            canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.transform.SetParent(root.transform); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 10;
            actor = new GameObject("Actor").transform; actor.SetParent(root.transform);
            bone = new GameObject("Animated top anchor").transform; bone.SetParent(actor); bone.localPosition = Vector3.up * 2;
            marker = Rect("Status", canvas.transform, new Vector2(30, 20), new Vector2(200, 100));
            frame = Rect("Frame", marker, new Vector2(65, 40), new Vector2(86, 26));
            hp = new GameObject("HP", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            hp.transform.SetParent(marker, false); hp.rectTransform.anchoredPosition = new Vector2(76, 47);
            hp.rectTransform.sizeDelta = new Vector2(56, 6); hp.type = Image.Type.Filled;
            hp.fillMethod = Image.FillMethod.Horizontal; hp.fillAmount = .8f;
            texture = new Texture2D(2, 6); texture.SetPixels(new Color[12]); texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, 2, 6), Vector2.zero); hp.sprite = sprite;
            Canvas.ForceUpdateCanvases(); originalPosition = marker.localPosition; originalScale = marker.localScale;
            type = Type.GetType("AstralOath.Client.BattleScene.BattleUnitStatusPresentation, AstralOath.Client.BattleScene", false);
            Assert.That(type, Is.Not.Null, "A reusable, lifecycle-owned unit status presenter must replace fixed canvas markers.");
            presenter = (IDisposable)Activator.CreateInstance(type, marker, hp, actor, (Func<Vector3>)(() => bone.position), camera, sprite, sprite, 10);
        }
        [TearDown] public void TearDown()
        {
            presenter?.Dispose(); presenter = null;
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (sprite) UnityEngine.Object.DestroyImmediate(sprite);
            if (texture) UnityEngine.Object.DestroyImmediate(texture);
            if (renderTexture) UnityEngine.Object.DestroyImmediate(renderTexture);
        }
        [Test] public void FollowsAnimatedTopAndActorMovementInsteadOfTheOldCanvasPosition()
        {
            Call("LateTick", camera); AssertAnchor();
            actor.position = new Vector3(1.5f, -.8f, 0f); bone.localPosition += new Vector3(.4f, .7f, 0f);
            Call("LateTick", camera); AssertAnchor();
        }
        [Test] public void CameraZoomAndActorScaleAffectTheMarkerWhileFacingStaysReadable()
        {
            camera.Render(); Call("LateTick", camera); camera.Render(); float baselineWidth = ScreenWidth();
            camera.orthographicSize *= .5f; Canvas.ForceUpdateCanvases(); Call("LateTick", camera); camera.Render();
            Assert.That(ScreenWidth(), Is.EqualTo(baselineWidth * 2).Within(.2f), "Compare rendered width, not the Canvas-dependent localScale.");
            actor.localScale = new Vector3(-1.5f, 1.5f, 1.5f); Call("LateTick", camera); camera.Render();
            Assert.That(ScreenWidth(), Is.EqualTo(baselineWidth * 3).Within(.3f));
            Assert.That(marker.localScale.y, Is.GreaterThan(0)); AssertAnchor();
        }
        [TestCase(false, false)] [TestCase(false, true)]
        [TestCase(true, false)] [TestCase(true, true)]
        public void SameFrameCameraChangeStaysAlignedAfterCanvasRebuild(bool insetViewport, bool zoom)
        {
            // Hold the output, aspect and viewport fixed throughout the camera change.
            // RT rendering ignores the screen rect, so the inset cases use the screen output.
            // The assertion happens after Unity synchronizes its ScreenSpaceCamera Canvas, as
            // rendering does; forcing it before LateTick would hide the production ordering bug.
            camera.targetTexture = insetViewport ? null : renderTexture;
            camera.aspect = 1f;
            camera.rect = insetViewport ? new Rect(.1f, .15f, .8f, .7f) : new Rect(0, 0, 1, 1);
            Canvas.ForceUpdateCanvases(); camera.Render();
            Call("LateTick", camera); Canvas.ForceUpdateCanvases(); camera.Render(); AssertAnchor();
            float width = ScreenWidth();
            Vector3 baseline = camera.transform.position;
            float size = camera.orthographicSize;
            camera.transform.position += new Vector3(2f, -.75f, 0f);
            if (zoom) camera.orthographicSize *= .65f;
            Call("LateTick", camera);
            Canvas.ForceUpdateCanvases();
            TestContext.WriteLine("Before render: canvas=" + canvas.transform.position + " scale=" + canvas.transform.localScale +
                " anchor error=" + Vector2.Distance(Screen(frame.position), Screen(bone.position)));
            camera.Render();
            TestContext.WriteLine("After render: canvas=" + canvas.transform.position + " scale=" + canvas.transform.localScale +
                " anchor error=" + Vector2.Distance(Screen(frame.position), Screen(bone.position)));
            AssertAnchor();
            Assert.That(ScreenWidth(), Is.EqualTo(width / (zoom ? .65f : 1f)).Within(.2f),
                "The first rendered frame must use the current zoom exactly once.");
            camera.transform.position = baseline; camera.orthographicSize = size;
            Call("LateTick", camera); Canvas.ForceUpdateCanvases(); camera.Render();
            AssertAnchor();
            Assert.That(ScreenWidth(), Is.EqualTo(width).Within(.2f), "Same-frame return must not retain the prior camera projection.");
        }
        [Test] public void ResizeDoesNotDoubleApplyCanvasScale()
        {
            Call("LateTick", camera);
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 2f; canvas.scaleFactor = 2f; Canvas.ForceUpdateCanvases(); Call("LateTick", camera);
            Vector3[] corners = new Vector3[4]; frame.GetWorldCorners(corners);
            float width = (Screen(corners[3]) - Screen(corners[0])).magnitude;
            Assert.That(width, Is.EqualTo(86f).Within(1f), "The status width in screen pixels should stay tied to the unchanged world actor.");
            AssertAnchor();
        }
        [Test] public void DamageHasAContinuousFrontAndADelayedLossLayer()
        {
            Call("UpdateHealth", 10000L, 10000, true); Call("UpdateHealth", 6000L, 10000, false);
            var trail = Trail(); Assert.That(hp.fillAmount, Is.EqualTo(1)); Assert.That(trail.fillAmount, Is.EqualTo(1));
            Call("Tick", .05f); Assert.That(hp.fillAmount, Is.InRange(.6001f, .9999f));
            Assert.That(trail.fillAmount, Is.GreaterThan(hp.fillAmount));
            Call("Tick", 2f); Assert.That(hp.fillAmount, Is.EqualTo(.6f).Within(.0001f));
            Assert.That(trail.fillAmount, Is.EqualTo(.6f).Within(.0001f));
        }
        [Test] public void AnotherHitAndHealingRetargetCurrentVisualsWithoutShowingFalseDamage()
        {
            Call("UpdateHealth", 10000L, 10000, true); Call("UpdateHealth", 8000L, 10000, false); Call("Tick", .05f);
            float front = hp.fillAmount, tail = Trail().fillAmount;
            Call("UpdateHealth", 4000L, 10000, false);
            Assert.That(hp.fillAmount, Is.EqualTo(front)); Assert.That(Trail().fillAmount, Is.EqualTo(tail));
            Call("Tick", .05f); Call("UpdateHealth", 9500L, 10000, false); Call("Tick", 2f);
            Assert.That(hp.fillAmount, Is.EqualTo(.95f).Within(.0001f)); Assert.That(Trail().fillAmount, Is.EqualTo(.95f).Within(.0001f));
        }
        [Test] public void ZeroDeltaFreezesHealthAndChangingMaximumDoesNotCreateATrail()
        {
            Call("UpdateHealth", 10000L, 10000, true); Call("UpdateHealth", 5000L, 10000, false);
            Call("Tick", .05f); float current = hp.fillAmount, trail = Trail().fillAmount;
            Call("Tick", 0f); Assert.That(hp.fillAmount, Is.EqualTo(current)); Assert.That(Trail().fillAmount, Is.EqualTo(trail));
            Call("UpdateHealth", 5000L, 5000, false); Assert.That(hp.fillAmount, Is.EqualTo(1)); Assert.That(Trail().fillAmount, Is.EqualTo(1));
        }
        [TestCase(100, 40, 5f / 7f, 1f, 5f / 7f)]
        [TestCase(60, 30, .6f, .9f, 1f)]
        [TestCase(60, 60, .5f, 1f, 5f / 6f)]
        public void BarrierAddsAnIndependentWhiteSegmentAndFitsInsideTheExistingTrack(
            int current, int barrier, float expectedHealth, float expectedEnd, float expectedHealthTrack)
        {
            Call("SetShieldSprite", sprite);
            Call("UpdateHealth", (long)current, 100, true);
            Call("UpdateShield", (long)barrier, 100, true);

            var shield = Shield();
            Assert.That(shield.sprite, Is.SameAs(sprite));
            Assert.That(shield.color, Is.EqualTo(Color.white));
            Assert.That(shield.raycastTarget, Is.False);
            Assert.That(shield.rectTransform.anchorMin.x, Is.EqualTo(expectedHealth).Within(.0001f));
            Assert.That(shield.rectTransform.anchorMax.x, Is.EqualTo(expectedEnd).Within(.0001f));
            Assert.That(shield.rectTransform.rect.width, Is.EqualTo(hp.rectTransform.rect.width * (expectedEnd - expectedHealth)).Within(.001f));
            Assert.That(shield.rectTransform.rect.height, Is.EqualTo(hp.rectTransform.rect.height).Within(.001f));
            Assert.That(hp.fillAmount, Is.EqualTo(expectedHealth).Within(.0001f));
            Assert.That(Trail().fillAmount, Is.EqualTo(expectedHealth).Within(.0001f));
            Assert.That(hp.transform.Find("Health Segments").GetComponent<RectTransform>().anchorMax.x,
                Is.EqualTo(expectedHealthTrack).Within(.0001f), "HP marks stop before the overflow portion.");
        }

        [Test] public void BarrierUpdatesRetargetFromTheVisibleFillAndFreezeWhenPaused()
        {
            Call("UpdateHealth", 60L, 100, true);
            Call("UpdateShield", 30L, 100, true);
            Call("UpdateShield", 20L, 100, false);
            var shield = Shield();
            Call("Tick", .05f);
            float visibleEnd = shield.rectTransform.anchorMax.x;
            Assert.That(visibleEnd, Is.InRange(.8001f, .8999f));
            Call("UpdateShield", 0L, 100, false);
            Call("Tick", 0f);
            Assert.That(shield.rectTransform.anchorMax.x, Is.EqualTo(visibleEnd));
            Call("Tick", 1f);
            Assert.That(shield.gameObject.activeSelf, Is.False);
            Assert.That(hp.fillAmount, Is.EqualTo(.6f).Within(.0001f));
            Assert.That(Trail().fillAmount, Is.EqualTo(.6f).Within(.0001f));
        }

        [Test] public void ChangingMaximumHealthRescalesTheSameAbsoluteBarrier()
        {
            Call("UpdateHealth", 100L, 100, true);
            Call("UpdateShield", 50L, 100, true);
            Call("UpdateHealth", 100L, 200, false);
            var shield = Shield();
            Assert.That(shield.rectTransform.anchorMin.x, Is.EqualTo(.5f).Within(.0001f));
            Assert.That(shield.rectTransform.anchorMax.x, Is.EqualTo(.75f).Within(.0001f));
        }

        [Test] public void BarrierFollowsTheStatusMarkerAndIsCleanedUpWithIt()
        {
            Call("UpdateHealth", 100L, 100, true);
            Call("UpdateShield", 50L, 100, true);
            var shield = Shield();
            Assert.That(shield.gameObject.activeInHierarchy, Is.True);
            marker.gameObject.SetActive(false);
            Assert.That(shield.gameObject.activeInHierarchy, Is.False);
            marker.gameObject.SetActive(true);
            Call("UpdateHealth", 0L, 100, true);
            Assert.That(shield.gameObject.activeSelf, Is.False);
            presenter.Dispose(); presenter = null;
            Assert.That(hp.transform.Find("Shield Fill"), Is.Null);
            Assert.That(hp.fillAmount, Is.EqualTo(.8f));
        }

        [TestCase(2500, 2)] [TestCase(10000, 9)] [TestCase(1000, 0)]
        public void DividerValuesUseMaximumHealthAndDoNotInventPercentageSegments(int max, int expected)
        {
            Call("UseHealthIntervals", 1000);
            Call("UpdateHealth", (long)max, max, true);
            var ticks = marker.GetComponentsInChildren<MaskableGraphic>(true);
            var graphic = Array.Find(ticks, item => item.GetType().Name == "BattleHealthSegments");
            Assert.That(graphic, Is.Not.Null);
            Assert.That((int)graphic.GetType().GetProperty("DividerCount").GetValue(graphic), Is.EqualTo(expected));
            Call("UpdateHealth", max / 2L, max, false);
            Assert.That((int)graphic.GetType().GetProperty("DividerCount").GetValue(graphic), Is.EqualTo(expected));
        }
        [Test] public void FixedSectionsDoNotChangeWhenTheUnitHasMoreHealth()
        {
            Call("UseFixedSections", 10);
            foreach (int maximum in new[] { 1000, 10000, 34813 })
            {
                Call("UpdateHealth", (long)maximum, maximum, true);
                var graphic = Array.Find(marker.GetComponentsInChildren<MaskableGraphic>(true), item => item.GetType().Name == "BattleHealthSegments");
                Assert.That((int)graphic.GetType().GetProperty("DividerCount").GetValue(graphic), Is.EqualTo(9));
            }
        }
        [Test] public void TenPercentageSectionsHaveShortBottomMarksAndATallerMiddleMarkEvenWhenEmpty()
        {
            Call("UseFixedSections", 10); Call("UpdateHealth", 0L, 34813, true);
            var graphic = Array.Find(marker.GetComponentsInChildren<MaskableGraphic>(true), item => item.GetType().Name == "BattleHealthSegments");
            using var mesh = new VertexHelper();
            graphic.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { mesh });
            Assert.That(mesh.currentVertCount, Is.EqualTo(36), "Nine inner marks must remain visible across the empty slot.");
            UIVertex a = default, b = default, middleA = default, middleB = default;
            mesh.PopulateUIVertex(ref a, 0); mesh.PopulateUIVertex(ref b, 1);
            mesh.PopulateUIVertex(ref middleA, 16); mesh.PopulateUIVertex(ref middleB, 17);
            Assert.That(a.position.y, Is.EqualTo(middleA.position.y), "All ticks are bottom aligned.");
            Assert.That(middleB.position.y - middleA.position.y, Is.GreaterThan((b.position.y - a.position.y) * 1.5f));
        }
        [Test] public void MissingSourceSpritesUseOwnedColoredFallbackInsteadOfWhiteBars()
        {
            presenter.Dispose();
            presenter = (IDisposable)Activator.CreateInstance(type, marker, hp, actor, (Func<Vector3>)(() => bone.position), camera, null, null, 10);
            var tail = Trail(); var fallback = tail.sprite;
            Assert.That(fallback, Is.Not.Null, "UGUI ignores filled geometry when an Image has no sprite.");
            Assert.That(tail.color, Is.EqualTo(new Color(129f / 255f, 50f / 255f, 45f / 255f, 1f)));
            var graphic = Array.Find(marker.GetComponentsInChildren<MaskableGraphic>(true), item => item.GetType().Name == "BattleHealthSegments");
            Assert.That(graphic.color, Is.EqualTo(new Color(0, 0, 0, 128f / 255f)));
            Call("UpdateHealth", 2500L, 10000, true); Assert.That(tail.fillAmount, Is.EqualTo(.25f));
            presenter.Dispose(); presenter = null; Assert.That(fallback == null, Is.True, "Dispose owns the fallback Sprite, but not Texture2D.whiteTexture.");
            Assert.That(Texture2D.whiteTexture, Is.Not.Null);
        }
        [Test] public void NativeDividerGraphicSubmitsItsMeshThroughAnActualCanvasRenderer()
        {
            Call("UpdateHealth", 10000L, 10000, true);
            var graphic = Array.Find(marker.GetComponentsInChildren<MaskableGraphic>(true), item => item.GetType().Name == "BattleHealthSegments");
            Assert.That(graphic.canvasRenderer, Is.Not.Null,
                "Graphic alone requires only RectTransform: without CanvasRenderer it silently skips the PreRender rebuild.");
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Modules/BattleScene/Res/Textures/Hud/game_hud_bar_split.png");
            Assert.That(source, Is.Not.Null);
            graphic.GetType().GetProperty("SourceSprite").SetValue(graphic, source);
            graphic.canvasRenderer.cull = false; graphic.SetAllDirty(); Canvas.ForceUpdateCanvases();
            graphic.Rebuild(CanvasUpdate.PreRender);
            // Unity 2022 returns the renderer's Mesh; it is not a caller-created buffer.
            var mesh = graphic.canvasRenderer.GetMesh();
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.EqualTo(36), "Check the submitted renderer mesh, not a direct OnPopulateMesh call.");
            Assert.That(graphic.canvasRenderer.materialCount, Is.EqualTo(1));
            Assert.That(graphic.canvasRenderer.GetMaterial(0), Is.Not.Null);
            Assert.That(graphic.mainTexture, Is.SameAs(source.texture));
            Assert.That(mesh.uv[0], Is.Not.EqualTo(mesh.uv[2]));
        }
        [Test] public void DisposeRestoresAuthoredStateAndDetachesOnlyOwnedUi()
        {
            Call("UpdateHealth", 10000L, 10000, true); actor.position = Vector3.right; Call("LateTick", camera);
            presenter.Dispose(); presenter.Dispose(); presenter = null;
            Assert.That(marker.localPosition, Is.EqualTo(originalPosition)); Assert.That(marker.localScale, Is.EqualTo(originalScale));
            Assert.That(hp.fillAmount, Is.EqualTo(.8f)); Assert.That(marker.Find("Damage Trail"), Is.Null);
            Assert.That(hp.transform.Find("Health Segments"), Is.Null); Assert.That(marker.Find("Frame"), Is.SameAs(frame));
        }
        private float ScreenWidth()
        {
            Vector3[] corners = new Vector3[4]; frame.GetWorldCorners(corners);
            return Vector2.Distance(Screen(corners[3]), Screen(corners[0]));
        }
        private Image Trail() => marker.Find("Damage Trail").GetComponent<Image>();
        private Image Shield() => hp.transform.Find("Shield Fill").GetComponent<Image>();
        private void Call(string method, params object[] values) => type.GetMethod(method).Invoke(presenter, values);
        private Vector2 Screen(Vector3 world) => RectTransformUtility.WorldToScreenPoint(camera, world);
        private void AssertAnchor() => Assert.That(Vector2.Distance(Screen(frame.position), Screen(bone.position)), Is.LessThan(.1f));
        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var result = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent, false); result.anchoredPosition = position; result.sizeDelta = size; return result;
        }
    }
}
