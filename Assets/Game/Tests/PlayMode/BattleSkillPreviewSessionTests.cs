#if UNITY_EDITOR
using System.Linq;
using AstralOath.Client.BattleScene;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleSkillPreviewSessionTests
    {
        private const string AssetPath = "Assets/Game/Modules/Summon/Res/Presentation/IseriaSkillLoop/BattleSkillPreviewDefinition.asset";

        [Test]
        public void IseriaSwordFlashesAcrossScreenAndHitsTheVisibleTarget()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BattleSkillPreviewDefinition>(AssetPath);
            var output = new GameObject("Iseria impact", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            output.rectTransform.sizeDelta = new Vector2(640, 360);
            var copy = new Texture2D(640, 360, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                using var preview = definition.Create(output);
                Color32[] At(float seconds)
                {
                    preview.Step(seconds - preview.Elapsed);
                    RenderTexture.active = (RenderTexture)output.texture;
                    copy.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
                    return copy.GetPixels32();
                }
                var gate = At(69f / 24f);
                foreach (int y in new[] { 60, 300 })
                {
                    var row = gate.Skip(y * 640 + 160).Take(320);
                    Assert.That(row.Average(pixel => (pixel.r + pixel.g + pixel.b) / 3f),
                        Is.GreaterThan(180f), "The sword gate must illuminate both halves of the viewport.");
                }

                // The R24 comparison measured the slime's pink cheek in this region
                // immediately before impact. The framebuffer has its origin at lower left.
                var waiting = At(5.625f);
                var cheek = (from y in Enumerable.Range(360 - 322, 43)
                             from x in Enumerable.Range(300, 40)
                             let pixel = waiting[y * 640 + x]
                             where pixel.b > 100 && pixel.r > Mathf.Max(124f, pixel.b * .78f) && pixel.g < pixel.r * .76f
                             select new Vector2Int(x, y)).ToArray();
                Assert.That(cheek.Length, Is.GreaterThan(12), "The target should be visible below the blade.");
                var hit = At(6.4366f);
                Assert.That(cheek.Count(point =>
                {
                    var pixel = hit[point.y * 640 + point.x];
                    return pixel.r > 210 && pixel.g > 210 && pixel.b > 210;
                }), Is.GreaterThan(cheek.Length / 2), "The impact should cover the target.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(output.gameObject);
            }
        }

        [Test]
        public void FeaturedIseriaRendersOneSlimeCutinAndRestoresOutputAcrossInterruptedSessions()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BattleSkillPreviewDefinition>(AssetPath);
            Assert.That(definition, Is.Not.Null, AssetPath);
            var output = new GameObject("Summon skill surface", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            output.rectTransform.sizeDelta = new Vector2(960, 540);
            var original = Texture2D.grayTexture;
            output.texture = original;
            BattleSkillPreviewSession session = null;
            try
            {
                for (int run = 0; run < 2; ++run)
                {
                    session = definition.Create(output);
                    var target = output.texture as RenderTexture;
                    Assert.That(target && target.IsCreated(), Is.True);
                    var camera = Resources.FindObjectsOfTypeAll<Camera>().Single(c => c.targetTexture == target);
                    var actors = camera.transform.root.GetComponentsInChildren<BattlePreviewActor>();
                    Assert.That(actors.Length, Is.EqualTo(2));
                    Assert.That(actors.Any(actor => actor.name.Contains("Slime")), Is.True);
                    Assert.That(camera.transform.root.GetComponentsInChildren<AudioSource>(), Is.Empty);
                    Assert.That(camera.transform.root.GetComponentsInChildren<AudioListener>(), Is.Empty);
                    Assert.That(session.Duration, Is.InRange(8f, 15f));
                    TestContext.WriteLine($"Source {definition.GraphId}, duration with cut-in {session.Duration:F3}s.");
                    var initial = Sample(target);
                    Assert.That(initial.Distinct().Count(), Is.GreaterThan(12), "Forest and characters must already be rendered.");
                    if (run == 1)
                    {
                        output.rectTransform.sizeDelta = new Vector2(1920, 1080);
                        session.Step(.03f);
                        var resized = (RenderTexture)output.texture;
                        Assert.That(resized.width, Is.EqualTo(1920));
                        Assert.That(resized.height, Is.EqualTo(1080));
                        Assert.That(target.IsCreated(), Is.False);
                        target = resized;
                        session.Step(2.35f);
                        Assert.That(Sample(target), Is.Not.EqualTo(initial), "The cinematic must replace the initial battlefield.");
                        session.Step(session.Duration - session.Elapsed);
                        Assert.That(session.IsComplete, Is.True);
                        Assert.That(session.Elapsed, Is.EqualTo(session.Duration).Within(.001f));
                        var slime = actors.Single(actor => actor.name.Contains("Slime")).GetComponentInChildren<MeshFilter>().sharedMesh;
                        var settled = slime.vertices;
                        session.Step(.35f);
                        Assert.That(slime.vertices, Is.Not.EqualTo(settled), "The slime must keep idling under the returning poster.");
                        Assert.That(session.Elapsed, Is.EqualTo(session.Duration).Within(.001f));
                    }
                    else session.Step(.15f);
                    session.Dispose();
                    session = null;
                    Assert.That(output.texture, Is.SameAs(original));
                    Assert.That(target.IsCreated(), Is.False);
                    Assert.That(camera && camera.enabled, Is.False);
                }
            }
            finally { session?.Dispose(); Object.DestroyImmediate(output.gameObject); }
        }

        [Test]
        public void SimultaneousPreviewClocksAndSurfacesStayIndependent()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BattleSkillPreviewDefinition>(AssetPath);
            RawImage Surface()
            {
                var image = new GameObject("Independent summon", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                image.rectTransform.sizeDelta = new Vector2(640, 360);
                return image;
            }
            var first = Surface();
            var second = Surface();
            try
            {
                using var a = definition.Create(first);
                using var b = definition.Create(second);
                var initial = Sample((RenderTexture)second.texture);
                a.Step(2.4f);
                Assert.That(b.Elapsed, Is.Zero);
                Assert.That(second.texture, Is.Not.SameAs(first.texture));
                Assert.That(Sample((RenderTexture)second.texture), Is.EqualTo(initial));
                a.Dispose();
                b.Step(2.4f);
                Assert.That(Sample((RenderTexture)second.texture), Is.Not.EqualTo(initial));
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        private static Color32[] Sample(RenderTexture input)
        {
            var small = RenderTexture.GetTemporary(32, 18, 0);
            var previous = RenderTexture.active;
            var sample = new Texture2D(32, 18, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(input, small);
                RenderTexture.active = small;
                sample.ReadPixels(new Rect(0, 0, 32, 18), 0, 0);
                return sample.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(small);
                Object.DestroyImmediate(sample);
            }
        }
    }
}
#endif
