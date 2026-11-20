#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public class HeroEquipmentPortraitFramingTests
    {
        [UnityTest]
        public IEnumerator PreparedPortraitIsSharedAcrossWindowsAndClearedWithItsSession()
        {
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var sources = new HeroPortraitSources();
            var catalog = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var hero = catalog.heroes.Single(row => row.id == "c1001");
            var canvas = new GameObject("Portrait session test", typeof(Canvas));
            var surface = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            surface.transform.SetParent(canvas.transform, false);
            SkeletonPlayerSource Source() => (SkeletonPlayerSource)typeof(HeroPortraitSources)
                .GetMethod("Get", members).Invoke(sources, new object[] { hero });
            var parsed = typeof(SkeletonPlayerSource).GetField("ParsedData", members);
            try
            {
                var preparation = sources.PrepareAsync(hero, CancellationToken.None);
                while (!preparation.IsCompleted) yield return null;
                preparation.GetAwaiter().GetResult();
                var prepared = Source();
                var data = parsed.GetValue(prepared);
                Assert.That(data, Is.Not.Null);
                for (int opening = 0; opening < 2; opening++)
                {
                    Camera camera;
                    using (var portrait = new HeroRosterPortrait(surface, sources))
                    {
                        Assert.That(portrait.Show(hero), Is.True);
                        camera = Resources.FindObjectsOfTypeAll<Camera>().Single(c => c.targetTexture == surface.texture);
                        // Inspect the data actually backing this live Spine skeleton.
                        var player = typeof(HeroRosterPortrait).GetField("player", members).GetValue(portrait);
                        var runtime = player.GetType().GetField("runtime", members).GetValue(player);
                        var skeleton = runtime.GetType().GetField("skeleton", members).GetValue(runtime);
                        Assert.That(skeleton.GetType().GetProperty("Data").GetValue(skeleton), Is.SameAs(data));
                    }
                    yield return null;
                    Assert.That(camera == null && surface.texture == null, Is.True, "Window disposal must release its camera and render target.");
                    Assert.That(Source(), Is.SameAs(prepared));
                }
                sources.Clear();
                var replacement = Source();
                Assert.That(replacement, Is.Not.SameAs(prepared));
                Assert.That(parsed.GetValue(replacement), Is.Null);
            }
            finally { sources.Clear(); Object.Destroy(canvas); }
        }

        [UnityTest]
        public IEnumerator EquipmentPortraitKeepsTheNativeScaleAndBottomOrigin()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var canvas = new GameObject("Equipment portrait test", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                var root = Object.Instantiate(prefab, canvas.transform, false);
                var map = root.GetComponent<BindingMap>();
                map.Require<GameObject>("roster").SetActive(true);
                var page = map.Require<RectTransform>("rosterDetail");
                var output = map.Require<RawImage>("detailPortrait");
                using var portrait = new HeroRosterPortrait(output);
                foreach (var width in new[] { 1280, 1478 })
                {
                    var panel = (RectTransform)root.transform;
                    panel.anchorMin = panel.anchorMax = Vector2.one * .5f;
                    panel.sizeDelta = new Vector2(width, 720);
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    foreach (var id in new[] { "c1001", "c1062" })
                    {
                        Assert.That(portrait.Show(catalog.heroes.Single(hero => hero.id == id)), Is.True);
                        var camera = Resources.FindObjectsOfTypeAll<Camera>().Single(c => c.targetTexture == output.texture);
                        var model = camera.transform.parent.GetComponentInChildren<MeshFilter>().transform;
                        Vector2 Project(Vector3 point)
                        {
                            Vector2 uv = camera.WorldToViewportPoint(model.TransformPoint(point));
                            var surface = output.rectTransform;
                            return page.InverseTransformPoint(surface.TransformPoint(Rect.NormalizedToPoint(surface.rect, uv)));
                        }
                        float pixels = 720f / page.rect.height;
                        var origin = Project(Vector3.zero);
                        Assert.That((Project(Vector3.up) - origin).y * pixels, Is.EqualTo(80f).Within(1f), id);
                        Assert.That((Project(Vector3.right) - origin).x * pixels, Is.EqualTo(80f).Within(1f), id);
                        Assert.That((origin.x - page.rect.center.x) * pixels, Is.EqualTo(20f).Within(1f), id);
                        Assert.That((origin.y - page.rect.yMin) * pixels, Is.EqualTo(0f).Within(1f), id);
                    }
                }
            }
            finally { Object.Destroy(canvas.gameObject); }
        }
    }
}
#endif
