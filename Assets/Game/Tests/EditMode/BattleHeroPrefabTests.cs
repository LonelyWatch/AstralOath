using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AstralOath.Client.Tests
{
    public sealed class BattleHeroPrefabTests
    {
        [Test]
        public void OverheadHudHasNoCombatReadinessStrip()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/BattleHud.prefab");
            Assert.That(hud.transform.Find("Combat Readiness"), Is.Not.Null, "Keep the separate turn-order rail.");
            foreach (string side in new[] { "Allies", "Enemies" })
            {
                var team = hud.transform.Find("Unit Status/" + side);
                Assert.That(team.childCount, Is.EqualTo(4));
                foreach (Transform marker in team)
                {
                    Assert.That(marker.Find("Combat Readiness"), Is.Null, marker.name);
                    Assert.That(marker.Find("Frame").GetComponent<UnityEngine.UI.Image>().sprite.name,
                        Is.EqualTo("game_hud_bar_bg_n"), marker.name);
                }
            }
        }

        [Test]
        public void EightAuthoredSlotsUseOneIdentityNeutralPrefab()
        {
            const string module = "Assets/Game/Modules/BattleScene";
            string prefabPath = module + "/Res/Prefab/BattleActor.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            const string actorType = "BattlePreviewActor";
            var template = new SerializedObject(prefab.GetComponent(actorType));
            Assert.That(template.FindProperty("heroId").stringValue, Is.Empty);
            Assert.That(template.FindProperty("skeleton").objectReferenceValue, Is.Null);

            string scenePath = module + "/Scenes/BattleScene.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool ownsScene = !scene.IsValid() || !scene.isLoaded;
            if (ownsScene) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
                var expected = new[] { "c1001", "c1062", "c1005", "c1002", "c1003", "c1004", "c1006", "c1007" };
                int index = 0;
                foreach (string side in new[] { "Allies", "Enemies" })
                {
                    var team = units.Find(side);
                    Assert.That(team.childCount, Is.EqualTo(4));
                    foreach (Transform slot in team)
                    {
                        var actor = slot.Cast<Transform>().Select(child => child.GetComponent(actorType)).Single(item => item);
                        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(actor.gameObject), Is.SameAs(prefab));
                        Assert.That(new SerializedObject(actor).FindProperty("heroId").stringValue, Is.EqualTo(expected[index++]));
                    }
                }
            }
            finally
            {
                if (ownsScene) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }
}
