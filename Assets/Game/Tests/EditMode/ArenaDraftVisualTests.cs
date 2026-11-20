using AstralOath.Client.Common;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDraftVisualTests
    {
        private const string Path = "Assets/Game/Modules/Arena/Res/Prefab/ArenaDraft.prefab";

        [TestCase("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset", "Heroes", "Id", "Face", "Card", "PortraitSkeleton", "ModelSkeleton")]
        [TestCase("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset", "heroes", "id", "portrait", "cardPortrait", "skeleton", "modelSkeleton")]
        public void RosterArtworkCoversEveryBattleHero(string asset, string rows, string idField, params string[] artFields)
        {
            var source = JObject.Parse(System.IO.File.ReadAllText("Assets/Game/Modules/BattleScene/Configs/battle-catalog.json"));
            var expected = source["heroes"].Select(row => (string)row["id"]).ToArray();
            var heroes = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(asset)).FindProperty(rows);
            var ids = Enumerable.Range(0, heroes.arraySize).Select(index =>
            {
                var row = heroes.GetArrayElementAtIndex(index);
                string id = row.FindPropertyRelative(idField).stringValue;
                foreach (string field in artFields)
                    Assert.That(row.FindPropertyRelative(field).objectReferenceValue, Is.Not.Null, id + "/" + field);
                return id;
            }).ToArray();
            Assert.That(ids, Is.EquivalentTo(expected));
        }

        [TestCase("c1054", "status_GreaterAttackUp", "status_CriticalChanceUp", "status_CriticalDamageUp", "status_DefenseUp", "status_ContinuousHealing", "status_EvasionUp", "status_Immunity")]
        [TestCase("c1053", "status_AttackDown", "status_DefenseDown", "status_Unhealable", "status_Silence", "effect_cleanse")]
        [TestCase("c1044", "effect_raise")]
        [TestCase("c1046", "effect_raise")]
        [TestCase("c1048", "effect_raise")]
        [TestCase("c1050", "effect_raise")]
        public void GeneratedHeroEffectsIncludeActualSkillOutcomes(string id, params string[] keys)
        {
            var rows = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset")).FindProperty("Heroes");
            var row = Enumerable.Range(0, rows.arraySize).Select(i => rows.GetArrayElementAtIndex(i))
                .Single(hero => hero.FindPropertyRelative("Id").stringValue == id);
            var effects = row.FindPropertyRelative("FilterEffects");
            var actual = Enumerable.Range(0, effects.arraySize).Select(i => effects.GetArrayElementAtIndex(i).stringValue);
            Assert.That(actual, Is.SupersetOf(keys), id);
        }

        [TestCase("weaken", "MagicNail")]
        [TestCase("weaken", "Sleep")]
        [TestCase("weaken", "Restrict")]
        [TestCase("weaken", "Bind")]
        [TestCase("enhance", "SkillNullifier")]
        [TestCase("enhance", "Vigor")]
        [TestCase("enhance", "Reflect")]
        [TestCase("weaken", "Bomb")]
        [TestCase("weaken", "VampiricTouch")]
        public void NewStatusFiltersAppearInTheirSourceGroup(string group, string status)
        {
            var view = AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<BindingMap>();
            var filters = view.Require<GameObject>("rosterFilters").GetComponent<BindingMap>();
            var options = filters.Require<BindingMap>(group).Require<RectTransform>("options");
            Assert.That(options.Find("status_" + status), Is.Not.Null);
        }

        [Test]
        public void TeamPortraitsKeepNativeAspectAndOpposingOrientation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var bindings = prefab.GetComponent<BindingMap>();
            var own = bindings.Require<BindingMap>("blue0").Require<Image>("portrait");
            var enemy = bindings.Require<BindingMap>("red0").Require<Image>("portrait");
            Assert.That(own.preserveAspect && enemy.preserveAspect, Is.True, "Portrait crops must not stretch to the differently sized trays.");
            Assert.That(own.rectTransform.lossyScale.x, Is.LessThan(0));
            Assert.That(enemy.rectTransform.lossyScale.x, Is.GreaterThan(0));
        }

        [Test]
        public void PendingSlotOverlaySharesTheTraySilhouetteOnBothTeams()
        {
            var bindings = AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<BindingMap>();
            foreach (var side in new[] { "blue", "red" })
            for (int index = 0; index < 5; index++)
            {
                var card = bindings.Require<BindingMap>(side + index);
                var tray = (Image)card.Require<GameButton>("button").targetGraphic;
                var pending = card.Require<Image>("pending");
                var confirmation = card.Require<RawImage>("confirm");
                Vector2 center = Center(tray.rectTransform);
                Assert.That(pending.sprite.name, Is.EqualTo(tray.sprite.name + "_glow_" + side));
                Assert.That(Vector2.Distance(Center(pending.rectTransform), center), Is.LessThan(.001f));
                Assert.That(Vector2.Distance(Center(confirmation.rectTransform), center), Is.LessThan(.001f));
                Assert.That(pending.rectTransform.anchorMin.x, Is.LessThan(tray.rectTransform.anchorMin.x));
                Assert.That(pending.rectTransform.localScale.x, Is.GreaterThan(0),
                    "The source red outline is already mirrored.");
                Assert.That(confirmation.raycastTarget, Is.False);
                Assert.That(confirmation.transform.GetSiblingIndex(), Is.GreaterThan(pending.transform.GetSiblingIndex()));

                Vector2 Center(RectTransform picture) => (picture.anchorMin + picture.anchorMax) * .5f;
            }
        }

        [Test]
        public void FormationHasNativeChangeControlsAndReadableSeparatedFloorRings()
        {
            var bindings = AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<BindingMap>();
            var upper = bindings.Require<BindingMap>("formation2");
            var lower = bindings.Require<BindingMap>("formation1");
            var change = upper.Require<Image>("change");
            Assert.That(change.sprite.name, Is.EqualTo("hero_config_btn_change"));
            Assert.That(((RectTransform)upper.transform).anchorMin.x, Is.LessThan(((RectTransform)lower.transform).anchorMin.x));
        }
    }
}
