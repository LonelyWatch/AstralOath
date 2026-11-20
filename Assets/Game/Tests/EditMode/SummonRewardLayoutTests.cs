using System;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class SummonRewardLayoutTests
    {
        [Serializable] private sealed class Message { public string key, text; }
        [Serializable] private sealed class Messages { public Message[] rows; }

        [TestCase("c1004", 190)]
        [TestCase("c1001", 222)]
        [TestCase("c1088", 326)]
        [TestCase("ef301", 260)]
        public void ResultCardBindsNativeArtworkWithoutStretching(string id, int width)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab")
                .GetComponent<BindingMap>().Require<GameObject>("resultCardTemplate");
            var row = UnityEngine.Object.Instantiate(template);
            try
            {
                var bindings = row.GetComponent<BindingMap>();
                bool isHero = id[0] == 'c';
                var presenter = Type.GetType("AstralOath.Client.Summon.SummonResultCard, AstralOath.Client.Summon", true)
                    .GetMethod("Present", BindingFlags.Static | BindingFlags.NonPublic);
                var parameters = presenter.GetParameters();
                var reward = JsonUtility.FromJson("{\"kind\":\"" + (isHero ? "hero" : "artifact") + "\",\"stars\":4}", parameters[1].ParameterType);
                var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Modules/Home/Configs/" +
                    (isHero ? "HomeHeroArt.asset" : "ArtifactArtCatalog.asset"));
                object hero = null, artifact = null;
                if (isHero)
                {
                    var heroes = (Array)catalog.GetType().GetField("heroes").GetValue(catalog);
                    var heroId = parameters[3].ParameterType.GetField("id");
                    hero = heroes.Cast<object>().Single(entry => (string)heroId.GetValue(entry) == id);
                }
                else artifact = catalog.GetType().GetMethod("Find").Invoke(catalog, new object[] { id });

                presenter.Invoke(null, new[] { bindings, reward, id, hero, artifact });
                var artwork = bindings.Require<Image>(isHero ? "icon" : "artifactArtwork");
                Assert.That(artwork.enabled, Is.True);
                Assert.That(artwork.sprite.rect.size, Is.EqualTo(new Vector2(width, isHero ? 89 : 92)));
                Assert.That(artwork.preserveAspect, Is.True, "The artwork must retain its original pixel aspect.");
                if (!isHero)
                {
                    Assert.That(bindings.Require<Image>("icon").enabled, Is.False);
                    return;
                }
                var rect = artwork.rectTransform;
                Assert.That(rect.rect.width, Is.EqualTo(width * 1.1f * 1.5f).Within(.02f));
                Assert.That(rect.rect.height, Is.EqualTo(89 * 1.1f * 1.5f).Within(.02f));
                Assert.That(rect.pivot, Is.EqualTo(new Vector2(1, 0)));
                var mask = (RectTransform)rect.parent;
                Assert.That(mask.GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(mask.rect.size, Is.EqualTo(new Vector2(339, 99) * 1.5f));
                Vector2 anchor = (Vector2)mask.InverseTransformPoint(rect.position) - mask.rect.min;
                Assert.That(anchor.x, Is.EqualTo(352 * 1.5f).Within(.02f));
                Assert.That(anchor.y, Is.EqualTo(1 * 1.5f).Within(.02f));
            }
            finally { UnityEngine.Object.DestroyImmediate(row); }
        }

        [Test]
        public void EverySummonHeroHasSeparatePortraitBattleModelAndLocalizedAcquisitionLine()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var locale = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Modules/Arena/Configs/arena-zh-hans.json");
            var lines = JsonUtility.FromJson<Messages>(locale.text).rows.ToDictionary(row => row.key, row => row.text);
            var heroes = new SerializedObject(catalog).FindProperty("heroes");
            Assert.That(heroes.arraySize, Is.GreaterThan(0));
            for (int i = 0; i < heroes.arraySize; i++)
            {
                var hero = heroes.GetArrayElementAtIndex(i);
                string id = hero.FindPropertyRelative("id").stringValue;
                string AssetPath(string field) => AssetDatabase.GetAssetPath(hero.FindPropertyRelative(field).objectReferenceValue);
                Assert.That(AssetPath("skeleton"), Does.Contain("/Animations/portrait/"), id);
                Assert.That(AssetPath("modelSkeleton"), Does.Contain("/Animations/model/"), id);
                Assert.That(AssetPath("modelAtlas"), Does.Contain("/Atlas/model/"), id);
                Assert.That(AssetPath("modelPage"), Does.Contain("/Textures/model/"), id);
                foreach (var field in new[] { "line", "cv", "intro", "story" })
                    Assert.That(lines["summon.reveal." + id + "." + field], Is.Not.Empty, id);
            }
            Assert.That(lines["summon.reveal.c1001.line"], Is.EqualTo("今天也要尽好我的职责！"));
            var map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab").GetComponent<BindingMap>();
            Assert.That(map.Require<GameText>("revealQuote").transform.IsChildOf(map.Require<GameObject>("revealSpeech").transform), Is.True);
            Assert.That(map.Require<RawImage>("revealHero").GetComponent<AspectRatioFitter>().aspectRatio, Is.EqualTo(1f));
            foreach (var key in new[] { "revealVoice", "revealDetails", "revealDismiss" }) map.Require<GameButton>(key);
            foreach (var key in new[] { "revealElement", "revealRole", "revealZodiac" }) map.Require<Image>(key);
            var row = map.Require<GameObject>("heroCaption").GetComponent<HorizontalLayoutGroup>();
            Assert.That(row.childControlWidth && row.childControlHeight, Is.True, "Attribute icons and labels must get their preferred sizes.");
            for (int i = 0; i < 6; i++)
                Assert.That(map.Require<GameObject>("revealStar" + i).GetComponent<Image>().sprite.name, Is.EqualTo("cm_icon_star"));
        }
    }
}
