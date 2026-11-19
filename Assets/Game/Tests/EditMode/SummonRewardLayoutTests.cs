using System;
using System.Linq;
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
