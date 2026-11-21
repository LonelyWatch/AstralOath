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

        [Test]
        public void DraftAndInventoryShareTheRosterPlateShadow()
        {
            var draft = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var hero = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var normal = draft.GetComponentsInChildren<Image>(true).Single(image => image.name == "Normal plate");
            var card = hero.GetComponentsInChildren<Image>(true).Single(image => image.name == "Hero card template");
            var draftShadow = normal.GetComponent<Shadow>();
            var heroShadow = card.GetComponent<Shadow>();
            Assert.That(draftShadow, Is.Not.Null);
            Assert.That(heroShadow, Is.Not.Null);
            Assert.That(draftShadow.effectDistance, Is.EqualTo(heroShadow.effectDistance));
            Assert.That(draftShadow.effectDistance.y, Is.LessThan(0));
            Assert.That(draftShadow.effectColor, Is.EqualTo(heroShadow.effectColor));
            Assert.That(draftShadow.effectColor.a, Is.InRange(.2f, .6f));
            Assert.That(draftShadow.useGraphicAlpha && heroShadow.useGraphicAlpha, Is.True);
        }

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
                {
                    Assert.That(row.FindPropertyRelative(field).objectReferenceValue, Is.Not.Null, id + "/" + field);
                    if(id=="c1067"&&field.Equals("modelSkeleton",System.StringComparison.OrdinalIgnoreCase))
                        Assert.That(AssetDatabase.GetAssetPath(row.FindPropertyRelative(field).objectReferenceValue),
                            Does.EndWith("/model/tamarinne.spine.json"),"Inventory and draft must start in the ordinary form.");
                }
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
        [TestCase("c1060", "effect_barrier", "effect_cleanse")]
        [TestCase("c1065", "effect_raise")]
        [TestCase("c1067", "status_Idol", "effect_reduce_cd")]
        [TestCase("c1069", "effect_raise")]
        [TestCase("c1073", "status_MagicBarrier", "effect_extend_cd")]
        [TestCase("c1074", "status_Perception", "effect_raise")]
        [TestCase("c1076", "status_PenetrationResistanceUp", "effect_cleanse", "effect_raise")]
        [TestCase("c1082", "effect_barrier")]
        [TestCase("c1085", "effect_raise")]
        [TestCase("c1083", "status_Frostbite", "status_Rupture", "effect_dispel")]
        public void GeneratedHeroEffectsIncludeActualSkillOutcomes(string id, params string[] keys)
        {
            var rows = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset")).FindProperty("Heroes");
            var row = Enumerable.Range(0, rows.arraySize).Select(i => rows.GetArrayElementAtIndex(i))
                .Single(hero => hero.FindPropertyRelative("Id").stringValue == id);
            var effects = row.FindPropertyRelative("FilterEffects");
            var actual = Enumerable.Range(0, effects.arraySize).Select(i => effects.GetArrayElementAtIndex(i).stringValue);
            Assert.That(actual, Is.SupersetOf(keys), id);
            if(id=="c1060")Assert.That(actual,Does.Not.Contain("status_Venom"),"Reacting to venom does not apply it.");
        }

        [TestCase("weaken", "MagicNail")]
        [TestCase("weaken", "Sleep")]
        [TestCase("weaken", "Restrict")]
        [TestCase("weaken", "Bind")]
        [TestCase("enhance", "SkillNullifier")]
        [TestCase("enhance", "Vigor")]
        [TestCase("enhance", "Idol")]
        [TestCase("enhance", "MagicBarrier")]
        [TestCase("enhance", "Perception")]
        [TestCase("enhance", "Reflect")]
        [TestCase("weaken", "Bomb")]
        [TestCase("weaken", "VampiricTouch")]
        [TestCase("weaken", "Frostbite")]
        [TestCase("weaken", "Rupture")]
        [TestCase("enhance", "PenetrationResistanceUp")]
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
