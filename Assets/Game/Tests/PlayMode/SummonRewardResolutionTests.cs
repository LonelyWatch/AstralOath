#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using AstralOath.Client.Summon;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public class SummonRewardResolutionTests
    {
        [UnityTest] public IEnumerator SilkCloseUpSamplesTheProjectedPortrait() => CheckReward(true);
        [UnityTest] public IEnumerator SilkMiniatureSamplesTheProjectedStage() => CheckReward(false);

        private static IEnumerator CheckReward(bool large)
        {
            var hero = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset")
                .heroes.Single(row => row.id == "c1004");
            Assert.That(new Vector2Int(hero.page.width, hero.page.height), Is.EqualTo(new Vector2Int(2001, 634)));
            Assert.That(new Vector2Int(hero.modelPage.width, hero.modelPage.height), Is.EqualTo(new Vector2Int(126, 2017)));
            var camera = new GameObject("Reward test camera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << 5;
            var canvas = new GameObject("Reward test canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.scaleFactor = 1.25f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab");
            var window = Object.Instantiate(prefab, canvas.transform, false).GetComponent<BindingMap>();
            window.Require<GameObject>("revealPanel").SetActive(true);
            var actor = window.Require<RawImage>("revealHero");
            var stage = window.Require<RawImage>("effectCanvas");
            var assets = AssetDatabase.LoadAssetAtPath<SummonPresentation>("Assets/Game/Modules/Summon/Res/Presentation/SummonPresentation.asset");
            var art = window.Require<Image>("revealArt").rectTransform;
            Camera actorCamera = null;
            RenderTexture screen = null;
            using var portrait = new HeroRosterPortrait(actor, 1024, positionOffset: 3000, framing: .90f, headFocus: .26f);
            using var show = assets.CreatePlayer(stage, window.Require<AudioSource>("audio"));
            try
            {
                foreach (var size in new[] { new Vector2Int(1824,1120), new Vector2Int(1368,840) })
                {
                    var previousScreen = screen;
                    screen = RenderTexture.GetTemporary(size.x, size.y, 16);
                    camera.targetTexture = screen;
                    if (previousScreen) RenderTexture.ReleaseTemporary(previousScreen);
                    Canvas.ForceUpdateCanvases();
                    if (!actorCamera)
                    {
                        Assert.That(portrait.Show(hero), Is.True);
                        actorCamera = Resources.FindObjectsOfTypeAll<Camera>().Single(c => c.targetTexture == actor.texture);
                        show.ShowRewardDetail(false, art, hero);
                    }
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    portrait.Tick();
                    actorCamera.Render();
                    var output = large ? actor : stage;
                    var corners = new Vector3[4];
                    output.rectTransform.GetWorldCorners(corners);
                    Vector2 origin = camera.WorldToScreenPoint(corners[0]);
                    var width = Mathf.RoundToInt(Vector2.Distance(origin, camera.WorldToScreenPoint(corners[3])));
                    var height = Mathf.RoundToInt(Vector2.Distance(origin, camera.WorldToScreenPoint(corners[1])));
                    var target = (RenderTexture)output.texture;
                    Debug.Log($"Silk reveal {size}, {(large ? "portrait" : "stage")} display={width}x{height}, target={target.width}x{target.height}");
                    var folder = Environment.GetEnvironmentVariable("ASTRAL_SUMMON_QUALITY_CAPTURE");
                    if (!string.IsNullOrEmpty(folder) && size.x == 1824)
                    {
                        Directory.CreateDirectory(folder);
                        var active = RenderTexture.active;
                        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                        try
                        {
                            RenderTexture.active = target;
                            copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                            copy.Apply();
                            File.WriteAllBytes(Path.Combine(folder, large ? "portrait.png" : "stage.png"), copy.EncodeToPNG());
                        }
                        finally { RenderTexture.active = active; Object.Destroy(copy); }
                    }
                    Assert.That(width, Is.GreaterThan(large ? 1024 : 1280));
                    Assert.That(height, Is.GreaterThan(720));
                    Assert.That(target.width, Is.EqualTo(width).Within(1));
                    Assert.That(target.height, Is.EqualTo(height).Within(1));
                }
            }
            finally
            {
                camera.targetTexture = null;
                if (screen) RenderTexture.ReleaseTemporary(screen);
                Object.Destroy(canvas.gameObject);
                Object.Destroy(camera.gameObject);
            }
        }
    }
}
#endif
