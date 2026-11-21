#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Arena;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public class ArenaPrebanRevealTimingTests
    {
        [UnityTest]
        public IEnumerator APreloadStallStillShowsAndDesaturatesTheSecondPrebanPair()
        {
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
            ArenaDraftArt.Hero Hero(string id) => art.Heroes.Single(hero => hero.Id == id);
            var canvas = new GameObject("Preban reveal surface", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = new GameObject("Two-pair reveal", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(1280, 720);
            var chips = new GameObject("Legacy chips", typeof(RectTransform));
            chips.transform.SetParent(root.transform, false);
            var type = typeof(ArenaDraftArt).Assembly.GetType("AstralOath.Client.Arena.ArenaPrebanReveal", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using var reveal = (IDisposable)Activator.CreateInstance(type, flags, null, new object[] { root, chips }, null);
            var tick = (Func<float, bool>)Delegate.CreateDelegate(typeof(Func<float, bool>), reveal, type.GetMethod("Tick", flags));
            try
            {
                type.GetMethod("Start", flags).Invoke(reveal, new object[] {
                    new[] { Hero("c1001"), Hero("c1002") }, new[] { Hero("c1003"), Hero("c1004") }, null, null
                });
                RawImage Portrait(string side) => root.GetComponentsInChildren<RawImage>().Single(image =>
                    image.isActiveAndEnabled && image.name.StartsWith(side + " reveal portrait ", StringComparison.Ordinal));
                for (int frame = 0; frame < 21; ++frame) tick(1f / 60f);
                float upperX = Portrait("Own").rectTransform.anchoredPosition.x;
                float lowerX = Portrait("Opponent").rectTransform.anchoredPosition.x;
                for (int frame = 0; frame < 10; ++frame) tick(1f / 60f);
                Assert.That(Portrait("Own").rectTransform.anchoredPosition.x, Is.LessThan(upperX - 10));
                Assert.That(Portrait("Opponent").rectTransform.anchoredPosition.x, Is.GreaterThan(lowerX + 10));
                for (int frame = 0; frame < 24; ++frame) tick(1f / 60f);
                Assert.That(Portrait("Own").name, Does.EndWith("c1001"));
                Assert.That(tick(1.120996f), Is.False, "The preload frame must leave the second pair on screen.");
                for (int frame = 0; frame < 3; ++frame) tick(1f / 60f);
                yield return null;
                Assert.That(root.activeInHierarchy, Is.True);
                var own = Portrait("Own");
                var opponent = Portrait("Opponent");
                Assert.That(own.name, Does.EndWith("c1002"));
                Assert.That(opponent.name, Does.EndWith("c1004"));
                foreach (var portrait in new[] { own, opponent })
                    Assert.That(HasPaintedPixels(portrait), Is.True, portrait.name);
                for (int frame = 0; frame < 20; ++frame) Assert.That(tick(1f / 60f), Is.False);
                Assert.That(own.material.GetFloat("_Desaturation"), Is.GreaterThan(0f));
                bool finished = false;
                for (int frame = 0; frame < 70 && !finished; ++frame) finished = tick(1f / 60f);
                Assert.That(finished, Is.True);
                Assert.That(root.activeSelf, Is.False);
            }
            finally { Object.Destroy(canvas); }
        }

        private static bool HasPaintedPixels(RawImage portrait)
        {
            if (!portrait.isActiveAndEnabled || !portrait.texture) return false;
            var sample = RenderTexture.GetTemporary(32, 32);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(portrait.texture, sample);
                RenderTexture.active = sample;
                pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
                return pixels.GetPixels32().Any(pixel => pixel.a > 32 && (pixel.r > 32 || pixel.g > 32 || pixel.b > 32));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(sample);
                Object.Destroy(pixels);
            }
        }
    }
}
#endif
