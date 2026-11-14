using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleActorShadowTests
    {
        private const string Module = "Assets/Game/Modules/BattleScene/";
        private const string ActorPrefabPath = Module + "Res/Prefab/BattleActor.prefab";
        private const string Ras = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/";

        [Test]
        public void FormationInheritsShadowWithoutIndependentSlotRenderers()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefabPath);
            var sourceShadow = prefab.transform.Find("Shadow");
            Assert.That(sourceShadow, Is.Not.Null);
            string path = Module + "Scenes/BattleScene.unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
                foreach (Transform team in units)
                foreach (Transform slot in team)
                {
                    var shadow = slot.GetComponentsInChildren<SpriteRenderer>(true)
                        .Single(item => item.sprite && item.sprite.name == "UnitShadow");
                    Assert.That(shadow.transform.parent.parent, Is.EqualTo(slot));
                    Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(shadow.gameObject),
                        Is.EqualTo(sourceShadow.gameObject));
                }
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        [Test]
        public void PrefabShadowKeepsItsWorldSizeAndStaysOnTheGroundDuringSourceJump()
        {
            var world = new GameObject("Source shadow fixture");
            world.SetActive(false);
            try
            {
                var slot = new GameObject("Slot").transform;
                slot.SetParent(world.transform, false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefabPath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
                var actor = instance.GetComponent("BattlePreviewActor");
                var shadow = instance.transform.Find("Shadow");
                Assert.That(shadow, Is.Not.Null);
                Assert.That(shadow.GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(-50));
                Vector3 authoredScale = shadow.localScale;
                instance.transform.localScale = Vector3.one * .53f;
                actor.GetType().GetMethod("EnableEditorPreview", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(actor, null);
                Assert.That(Vector3.Distance(shadow.lossyScale, authoredScale), Is.LessThan(.0001f));
                var ground = shadow.position;

                using (var source = new SerializedObject(actor))
                {
                    source.FindProperty("skeleton").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<TextAsset>(Ras + "Animations/model/ras.spine.json");
                    source.ApplyModifiedPropertiesWithoutUndo();
                }
                var runtime = actor.GetType().Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
                var asset = actor.GetType().Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
                const string graphId = "c_ras_skill_03";
                var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(Ras + "Configs/stagept/" + graphId + ".graph.json");
                using (var playback = (IDisposable)Activator.CreateInstance(runtime,
                    new object[] { world.transform, new[] { graph }, Array.CreateInstance(asset, 0), null, null, null }))
                {
                    var targets = Array.CreateInstance(actor.GetType(), 1);
                    targets.SetValue(actor, 0);
                    runtime.GetMethod("Begin").Invoke(playback, new object[] { graphId, actor, actor, targets });
                    runtime.GetMethod("Tick").Invoke(playback, new object[] { 1.65f });
                    Assert.That(instance.transform.position.y, Is.GreaterThan(.1f));
                    Assert.That(Vector3.Distance(shadow.position, ground), Is.LessThan(.0001f),
                        "The attached shadow follows the ground path while the body is lifted.");
                }
                Assert.That(Vector3.Distance(shadow.position, ground), Is.LessThan(.0001f));
            }
            finally
            {
                Object.DestroyImmediate(world);
            }
        }
    }
}
