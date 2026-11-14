#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleInteractionTests
    {
        private Scene scene;
        private BattleHudPreview host;
        private BindingMap map;

        [UnitySetUp]
        public IEnumerator Load()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            host = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>()).Single();
            map = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
        }

        [Test]
        public void PlayAutomaticallyStartsBattleWithLevel60EnemiesAndReadinessAbovePortrait()
        {
            Assert.That(host.SimulationSnapshot, Is.Not.Null, "Play must enter the battle without a second start click.");
            Assert.That(map.Require<GameButton>("startPreview").gameObject.activeSelf, Is.False);
            Assert.That(host.SimulationSnapshot.Units.Count(unit => unit.Slot.Side == BattleSide.Enemy), Is.EqualTo(4));
            foreach (var enemy in host.SimulationSnapshot.Units.Where(unit => unit.Slot.Side == BattleSide.Enemy))
                Assert.That(enemy.Level, Is.EqualTo(60));
            Assert.That(map.Require<Image>("activePortrait").transform.parent.GetSiblingIndex(),
                Is.LessThan(map.Require<Image>("orderHourglass").transform.parent.GetSiblingIndex()),
                "The large acting portrait must render behind the readiness queue.");
        }

        [UnityTest]
        public IEnumerator SkillCameraKeepsWorldHealthAndLevelMarkersActive()
        {
            var status = map.transform.Find("Unit Status").gameObject;
            Assert.That(status.activeInHierarchy, Is.True);
            var simulation = typeof(BattleHudPreview).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            var cinematic = simulation.GetType().GetField("cinematicVisible", BindingFlags.Instance | BindingFlags.NonPublic);
            var target = host.SimulationSnapshot.Units.First(unit => unit.Slot.Side == BattleSide.Enemy);
            map.Require<GameButton>("skill3").onClick.Invoke();
            Assert.That(host.TrySelectBattleTarget(target.InstanceId), Is.True);
            bool observedCinematic = false;
            double deadline = Time.realtimeSinceStartupAsDouble + 20d;
            do
            {
                yield return null;
                bool presenting = (bool)cinematic.GetValue(simulation);
                observedCinematic |= presenting;
                Assert.That(status.activeInHierarchy, Is.True,
                    "Health, level and status markers stay active during source camera/effect cues; only a real full-screen overlay may cover them.");
                if (observedCinematic && !presenting) break;
            } while (Time.realtimeSinceStartupAsDouble < deadline);
            Assert.That(observedCinematic, Is.True, "The real skill must exercise the cinematic callback.");
            Assert.That((bool)cinematic.GetValue(simulation), Is.False, "The skill must finish and restore player controls.");
        }

        [UnityTest]
        public IEnumerator PauseBlocksSkillInputAndFreezesPresentationUntilContinueAndRestartResetsBattle()
        {
            if (host.SimulationSnapshot == null) host.StartBattle();
            var enemy = host.SimulationSnapshot.Units.First(unit => unit.Slot.Side == BattleSide.Enemy);
            Assert.That(host.TrySelectBattleTarget(enemy.InstanceId), Is.True);
            yield return null;
            Click(map.Require<GameButton>("pause"));
            Assert.That(Paused(), Is.True);
            var panel = map.transform.Find("Pause Panel");
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.gameObject.activeSelf, Is.True);
            Assert.That(panel.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(30000),
                "Pause actions must stay visible above the full-screen skill cinematic.");
            Assert.That(host.TrySelectBattleTarget(enemy.InstanceId), Is.False);
            foreach (string key in new[] { "skill1", "skill2", "skill3", "soulBurn" })
                Assert.That(map.Require<GameButton>(key).interactable, Is.False, key);

            // Wait one frame for the actor's Update ordering to settle, then compare frozen poses.
            yield return null;
            var actorObjects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattlePreviewActor>()).ToArray();
            var positions = actorObjects.Select(actor => actor.transform.position).ToArray();
            var meshObjects = actorObjects.SelectMany(actor => actor.GetComponentsInChildren<MeshFilter>()).ToArray();
            var meshes = meshObjects.Select(mesh => mesh.sharedMesh.vertices).ToArray();
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            var cameraPosition = camera.transform.position;
            float cameraSize = camera.orthographicSize;
            int sounds = host.StartedSkillSounds;
            yield return new WaitForSecondsRealtime(0.2f);
            for (int index = 0; index < actorObjects.Length; index++)
                Assert.That(actorObjects[index].transform.position, Is.EqualTo(positions[index]));
            for (int index = 0; index < meshObjects.Length; index++)
                Assert.That(meshObjects[index].sharedMesh.vertices, Is.EqualTo(meshes[index]), "Paused skeletons must retain their pose.");
            Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(camera.orthographicSize, Is.EqualTo(cameraSize));
            Assert.That(host.StartedSkillSounds, Is.EqualTo(sounds));
            CapturePauseMenu(camera);

            Click(panel.Find("Continue").GetComponent<GameButton>());
            Assert.That(Paused(), Is.False);
            Assert.That(panel.gameObject.activeSelf, Is.False);
            map.Require<GameButton>("pause").onClick.Invoke();
            // Reopening re-registers its Graphics; the user can click only after it has rendered.
            yield return null;
            Click(panel.Find("Restart").GetComponent<GameButton>());
            Assert.That(Paused(), Is.False);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero);
            foreach (var unit in host.SimulationSnapshot.Units)
                Assert.That(unit.CurrentHealth, Is.EqualTo(unit.Stats.MaxHealth));
        }

        [UnityTest]
        public IEnumerator CancelKeepsTheBurnSkillSelectedAndCastsItWithoutSoulBurn()
        {
            if (host.SimulationSnapshot == null) host.StartBattle();
            var burn = map.Require<GameButton>("soulBurn");
            var skills = Enumerable.Range(1, 3).Select(index => map.Require<GameButton>("skill" + index)).ToArray();
            var catalog = JsonUtility.FromJson<BattleCatalogData>(File.ReadAllText(
                "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json"));
            var initial = host.SimulationSnapshot;
            var actor = initial.GetUnit(initial.CurrentActorId);
            int burnIndex = Array.FindIndex(actor.Form.SkillIds.ToArray(),
                id => catalog.skills.Single(skill => skill.id == id).soulBurnCost > 0);
            var burnSkill = catalog.skills.Single(skill => skill.id == actor.Form.SkillIds[burnIndex]);
            int previousIndex = (burnIndex + 1) % skills.Length;
            skills[previousIndex].onClick.Invoke();
            Assert.That(map.Require<Image>("skillSelected" + (previousIndex + 1)).gameObject.activeSelf, Is.True);
            burn.onClick.Invoke();
            for (int index = 0; index < skills.Length; index++)
            {
                Assert.That(skills[index].interactable, Is.EqualTo(index == burnIndex));
                if (index != burnIndex)
                    Assert.That(((Image)skills[index].targetGraphic).color.r, Is.LessThan(0.75f));
                skills[index].onClick.Invoke();
            }
            host.PauseBattle();
            Assert.That(host.IsPaused, Is.True);
            foreach (var skill in skills) Assert.That(skill.interactable, Is.False);
            Assert.That(burn.interactable, Is.False);
            host.ResumeBattle();
            Assert.That(host.IsPaused, Is.False);
            for (int index = 0; index < skills.Length; index++)
                Assert.That(skills[index].interactable, Is.EqualTo(index == burnIndex));
            Assert.That(burn.interactable, Is.True);
            yield return new WaitForSecondsRealtime(0.28f);
            Assert.That(((Image)burn.targetGraphic).sprite.name, Is.EqualTo("game_hud_btn_burnc_d"));
            Assert.That(map.Require<Image>("skillSelected" + (previousIndex + 1)).gameObject.activeSelf, Is.False);
            burn.onClick.Invoke();
            foreach (var skill in skills) Assert.That(skill.interactable, Is.True);
            for (int index = 0; index < skills.Length; index++)
                Assert.That(map.Require<Image>("skillSelected" + (index + 1)).gameObject.activeSelf,
                    Is.EqualTo(index == burnIndex), "Cancel keeps the Soul Burn skill selected in normal mode.");
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero);
            var enemy = initial.Units.First(unit => unit.Slot.Side == BattleSide.Enemy);
            Assert.That(host.TrySelectBattleTarget(enemy.InstanceId), Is.True);
            var result = host.SimulationSnapshot;
            Assert.That(result.LastCommandSequence, Is.EqualTo(1UL));
            Assert.That(result.GetUnit(actor.InstanceId).GetCooldown(burnSkill.id), Is.GreaterThan(0),
                "Selecting a target immediately after Cancel must cast the skill that remained highlighted.");
            Assert.That(result.AllySoul, Is.EqualTo(initial.AllySoul + burnSkill.soulGain),
                "The normal cast earns souls without spending any.");
        }

        [UnityTest]
        public IEnumerator PausedHostDisableAndRepeatedRestartReleaseOwnedPresentationAndUi()
        {
            map.Require<GameButton>("skill3").onClick.Invoke();
            var enemy = host.SimulationSnapshot.Units.First(unit => unit.Slot.Side == BattleSide.Enemy);
            Assert.That(host.TrySelectBattleTarget(enemy.InstanceId), Is.True);
            yield return null;
            host.PauseBattle();
            Assert.That(host.IsPaused, Is.True);
            host.enabled = false;
            yield return null;
            Assert.That(host.SimulationSnapshot, Is.Null);
            var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            Assert.That(units.Find("Battle Skill Effects"), Is.Null);
            foreach (var actor in units.GetComponentsInChildren<BattlePreviewActor>())
            {
                AssertActorField(actor, "paused", false);
                AssertActorField(actor, "presentationPauseCount", 0);
                AssertActorField(actor, "presentationControlCount", 0);
            }
            host.enabled = true;
            yield return null;
            for (int index = 0; index < 2; index++)
            {
                host.PauseBattle();
                host.RestartBattle();
                Assert.That(map.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Pause Panel"), Is.EqualTo(1),
                    "Restart must detach the disposed panel immediately, before deferred Destroy runs.");
                Assert.That(map.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Live Readiness"), Is.EqualTo(1),
                    "Restart must detach the disposed readiness HUD immediately.");
                Assert.That(map.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Battle Feedback"), Is.EqualTo(1),
                    "Restart must detach the disposed feedback layer immediately.");
                yield return null;
                Assert.That(host.IsPaused, Is.False);
                Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero);
                Assert.That(map.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Pause Panel"), Is.EqualTo(1));
                Assert.That(map.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Live Readiness"), Is.EqualTo(1));
                Assert.That(units.GetComponentsInChildren<Transform>(true).Count(item => item.name == "Battle Skill Effects"), Is.EqualTo(1));
                var pause = map.Require<GameButton>("pause");
                Assert.That(pause.GetComponents<Canvas>().Length, Is.EqualTo(1));
                Assert.That(pause.GetComponents<GraphicRaycaster>().Length, Is.EqualTo(1));
            }
            // A leaked Burn listener would toggle twice or access a disposed old simulation.
            var burn = map.Require<GameButton>("soulBurn");
            burn.onClick.Invoke();
            Assert.That(((Image)burn.targetGraphic).sprite.name, Is.EqualTo("game_hud_btn_burnc_d"));
        }

        private static void AssertActorField(BattlePreviewActor actor, string name, object expected)
        {
            var field = typeof(BattlePreviewActor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            Assert.That(field.GetValue(actor), Is.EqualTo(expected), actor.name + " " + name);
        }

        private static void CapturePauseMenu(Camera camera)
        {
            string directory = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory) ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Directory.CreateDirectory(directory);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousRect = camera.rect;
            float previousAspect = camera.aspect;
            var target = RenderTexture.GetTemporary(1920, 1080, 24);
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                camera.targetTexture = target;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = target.width / (float)target.height;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(directory, "pause-menu.png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                if (texture) UnityEngine.Object.Destroy(texture);
            }
        }

        private void Click(GameButton button)
        {
            Canvas.ForceUpdateCanvases();
            var eventSystem = scene.GetRootGameObjects().Select(root => root.GetComponent<EventSystem>())
                .Single(item => item != null);
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            var pointer = new PointerEventData(eventSystem)
            {
                position = RectTransformUtility.WorldToScreenPoint(camera, button.transform.position)
            };
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            if (hits.Count == 0)
            {
                var rect = (RectTransform)button.transform;
                var graphic = button.targetGraphic;
                var canvas = graphic.canvas;
                Debug.Log($"ASTRAL_PAUSE_RAY button={button.name} world={rect.position} screen={pointer.position} " +
                    $"screenSize={Screen.width}x{Screen.height} rect={rect.rect} anchor={rect.anchoredPosition} " +
                    $"active={button.gameObject.activeInHierarchy} enabled={button.enabled} interactable={button.interactable} " +
                    $"raycastTarget={graphic.raycastTarget} cull={graphic.canvasRenderer.cull} depth={graphic.depth} " +
                    $"graphicCanvas={canvas?.name} mode={canvas?.renderMode} root={canvas?.rootCanvas.name} " +
                    $"isRoot={canvas?.isRootCanvas} sorting={canvas?.sortingOrder} " +
                    $"camera={camera.name} pixelRect={camera.pixelRect} aspect={camera.aspect}");
                foreach (var raycaster in RaycasterManager.GetRaycasters().OfType<GraphicRaycaster>())
                {
                    var owner = raycaster.GetComponent<Canvas>();
                    var direct = new List<RaycastResult>();
                    raycaster.Raycast(pointer, direct);
                    Debug.Log($"ASTRAL_PAUSE_RAY caster={raycaster.name} active={raycaster.isActiveAndEnabled} " +
                        $"eventCamera={raycaster.eventCamera?.name} pixels={raycaster.eventCamera?.pixelRect} " +
                        $"mode={owner.renderMode} root={owner.rootCanvas.name} graphics={GraphicRegistry.GetGraphicsForCanvas(owner).Count} " +
                        $"raycastable={GraphicRegistry.GetRaycastableGraphicsForCanvas(owner).Count} " +
                        $"inside={RectTransformUtility.RectangleContainsScreenPoint(rect, pointer.position, raycaster.eventCamera)} " +
                        $"hits={direct.Count} reverse={raycaster.ignoreReversedGraphics}");
                }
            }
            Assert.That(camera.pixelRect.Contains(pointer.position), Is.True,
                $"{button.name} screen position {pointer.position} must stay in the rendered HUD viewport {camera.pixelRect}.");
            Assert.That(hits.Count, Is.GreaterThan(0), button.name);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject),
                Is.EqualTo(button.gameObject), "The visible pause action must receive the pointer.");
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        private bool Paused()
        {
            var property = typeof(BattleHudPreview).GetProperty("IsPaused", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(property, Is.Not.Null, "The scene host must expose pause state.");
            return (bool)property.GetValue(host);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
