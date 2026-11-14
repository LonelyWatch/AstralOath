using System;
using System.Reflection;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Shared.Battle;
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
                Assert.That(hud.transform.Find("Unit Status/" + side).childCount, Is.Zero,
                    "The shared HUD owns attachment points, not eight copied actor markers.");
            var actor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/BattleActor.prefab");
            var owner = actor.GetComponent("BattleActorStatusTemplate");
            Assert.That(owner, Is.Not.Null, "The actor prefab owns its status template and presentation profile.");
            var data = new SerializedObject(owner);
            var marker = data.FindProperty("markerTemplate").objectReferenceValue as RectTransform;
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.IsChildOf(actor.transform), Is.True);
            Assert.That(marker.gameObject.activeSelf, Is.False, "The source template must never render in the world.");
            Assert.That(marker.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(marker.GetComponentsInChildren<AstralOath.Client.Common.BindingMap>(true), Is.Empty);
            Assert.That(marker.Find("Combat Readiness"), Is.Null);
            Assert.That(marker.Find("Frame").GetComponent<UnityEngine.UI.Image>().sprite.name,
                Is.EqualTo("game_hud_bar_bg_n"));
            Assert.That(marker.Find("HP/Shield Fill").GetComponent<UnityEngine.UI.Image>().sprite.name,
                Is.EqualTo("game_hud_bar_sh"));
            Assert.That(marker.Find("HP/Shield Fill").gameObject.activeSelf, Is.False);
            Assert.That(marker.Find("Frame/Status Icons"), Is.Not.Null);
            Assert.That(data.FindProperty("profile").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void ActorStatusTemplateCreatesIndependentMarkersUnderTheSharedCanvas()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/BattleActor.prefab");
            var actor = Object.Instantiate(prefab);
            var canvas = new GameObject("Shared Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var owner = actor.GetComponent("BattleActorStatusTemplate");
                Assert.That(owner, Is.Not.Null);
                var create = owner.GetType().GetMethod("CreateMarker");
                Assert.That(create, Is.Not.Null);
                var first = (GameObject)create.Invoke(owner, new object[] { canvas.transform, "01 Front" });
                var second = (GameObject)create.Invoke(owner, new object[] { canvas.transform, "02 Lower" });
                Assert.That(first.name, Is.EqualTo("01 Front"));
                Assert.That(first.transform.parent, Is.SameAs(canvas.transform));
                Assert.That(first.activeSelf && second.activeSelf, Is.True);
                Assert.That(first, Is.Not.SameAs(second));
                first.transform.Find("HP").GetComponent<UnityEngine.UI.Image>().fillAmount = .25f;
                Assert.That(second.transform.Find("HP").GetComponent<UnityEngine.UI.Image>().fillAmount, Is.EqualTo(1f));
                Assert.That(actor.transform.Find("StatusTemplate").gameObject.activeSelf, Is.False);
                Assert.That(canvas.GetComponentsInChildren<Canvas>().Length, Is.EqualTo(1), "Markers reuse the root canvas.");
            }
            finally { Object.DestroyImmediate(canvas); Object.DestroyImmediate(actor); }
        }

        [Test]
        public void AuthoredShieldAndStatusContainerAreReusedAndSurvivePresenterDisposal()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/BattleActor.prefab");
            var actor = Object.Instantiate(prefab);
            var canvas = new GameObject("Shared Canvas", typeof(RectTransform), typeof(Canvas));
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            IDisposable healthPresenter = null, iconsPresenter = null;
            try
            {
                var owner = actor.GetComponent("BattleActorStatusTemplate");
                Assert.That(owner, Is.Not.Null);
                var marker = (GameObject)owner.GetType().GetMethod("CreateMarker").Invoke(owner,
                    new object[] { canvas.transform, "01 Front" });
                var frame = (RectTransform)marker.transform.Find("Frame");
                var health = marker.transform.Find("HP").GetComponent<UnityEngine.UI.Image>();
                var shield = health.transform.Find("Shield Fill");
                var icons = frame.Find("Status Icons");
                var profile = owner.GetType().GetProperty("Profile").GetValue(owner);
                var statusArt = profile.GetType().GetMethod("CreateStatusArt", flags).Invoke(profile, null);
                var statusType = Type.GetType("AstralOath.Client.BattleScene.BattleUnitStatusPresentation, AstralOath.Client.BattleScene", true);
                healthPresenter = (IDisposable)Activator.CreateInstance(statusType, (RectTransform)marker.transform, health,
                    actor.transform, (Func<Vector3>)(() => actor.transform.position + Vector3.up), camera, null, 10);
                var iconsType = Type.GetType("AstralOath.Client.BattleScene.BattleStatusIcons, AstralOath.Client.BattleScene", true);
                iconsPresenter = (IDisposable)Activator.CreateInstance(iconsType, frame, statusArt, 3, null);
                Assert.That(health.transform.Find("Shield Fill"), Is.SameAs(shield));
                Assert.That(health.transform.Cast<Transform>().Count(item => item.name == "Shield Fill"), Is.EqualTo(1));
                Assert.That(frame.Cast<Transform>().Count(item => item.name == "Status Icons"), Is.EqualTo(1));
                var effect = Activator.CreateInstance(typeof(BattleStatusSnapshot), flags, null,
                    new object[] { BattleStatusKind.AttackUp, 2, 5000 }, null);
                iconsType.GetMethod("Apply").Invoke(iconsPresenter, new[] { effect });
                Assert.That(icons.childCount, Is.EqualTo(1));
                iconsPresenter.Dispose(); iconsPresenter = null;
                healthPresenter.Dispose(); healthPresenter = null;
                Assert.That(frame.Find("Status Icons"), Is.SameAs(icons));
                Assert.That(icons.childCount, Is.Zero, "Release runtime icons without deleting the authored container.");
                Assert.That(health.transform.Find("Shield Fill"), Is.SameAs(shield));
                Assert.That(shield.gameObject.activeSelf, Is.False);
            }
            finally
            {
                iconsPresenter?.Dispose(); healthPresenter?.Dispose();
                Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(canvas); Object.DestroyImmediate(actor);
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
                var catalogAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(module + "/Configs/battle-catalog.json");
                Assert.That(catalogAsset, Is.Not.Null);
                var catalog = JsonUtility.FromJson<BattleCatalogData>(catalogAsset.text);
                var knownHeroes = new HashSet<string>(catalog.heroes.Select(hero => hero.id));

                foreach (string side in new[] { "Allies", "Enemies" })
                {
                    var team = units.Find(side);
                    Assert.That(team.childCount, Is.EqualTo(4));
                    foreach (Transform slot in team)
                    {
                        var actor = slot.Cast<Transform>().Select(child => child.GetComponent(actorType)).Single(item => item);
                        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(actor.gameObject), Is.SameAs(prefab));
                        string hero = new SerializedObject(actor).FindProperty("heroId").stringValue;
                        Assert.That(knownHeroes, Does.Contain(hero), slot.name);
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
