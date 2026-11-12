#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleScenePreviewTests
    {
        private const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private Scene scene;

        [UnityTest]
        public IEnumerator AuthoredSceneShowsOriginalActorsAndHud()
        {
            Assert.That(File.Exists(ScenePath), Is.True, "The authored battle scene must be saved in the project.");
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var units = roots.Single(root => root.name == "Units").transform;
            var actors = units.GetComponentsInChildren<MeshFilter>()
                .Where(mesh => mesh.sharedMesh && mesh.sharedMesh.name == "Skeleton dynamic mesh").ToArray();
            Assert.That(actors.Length, Is.EqualTo(8), "Four slots on each side must have a visible preview actor.");
            Assert.That(units.Find("Allies").childCount, Is.EqualTo(4));
            Assert.That(units.Find("Enemies").childCount, Is.EqualTo(4));
            foreach (var actor in actors)
                Assert.That(actor.sharedMesh.vertexCount, Is.GreaterThan(0), actor.transform.parent.name);

            var hudCanvas = roots.Single(root => root.name == "Battle Canvas").GetComponent<Canvas>();
            foreach (var actor in actors)
                Assert.That(actor.GetComponent<MeshRenderer>().sortingOrder, Is.LessThan(hudCanvas.sortingOrder),
                    "A world actor must not cover the HUD.");

            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            map.Validate();
            foreach (string key in new[] { "skill1", "skill2", "skill3", "auto", "speed", "pause", "soulBurn" })
                Assert.That(map.Require<GameButton>(key).targetGraphic, Is.Not.Null, key);
            Assert.That(map.Require<Image>("activePortrait").sprite, Is.Not.Null);
            Assert.That(map.Require<Image>("orderHourglass").sprite.name, Is.EqualTo("cm_icon_etctime2"));
            Assert.That(map.Require<GameText>("activeHp").font.name, Is.EqualTo("BattleHpDigits"));
            foreach (string side in new[] { "Allies", "Enemies" })
            {
                var bars = map.transform.Find("Unit Status/" + side);
                Assert.That(bars.childCount, Is.EqualTo(4));
                foreach (Transform bar in bars)
                    Assert.That(bar.Find("Frame").GetComponent<Image>().sprite.name, Is.EqualTo("game_hud_bar_bg"));
            }
            Assert.That(map.Require<GameText>("wave").text, Is.Not.Empty);

            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Single(item => item.name == "Battle Camera");
            Assert.That(camera.pixelRect.width / camera.pixelRect.height, Is.EqualTo(16f / 9f).Within(0.01f));
            foreach (var actor in actors)
            {
                Vector3 point = camera.WorldToViewportPoint(actor.transform.TransformPoint(actor.sharedMesh.bounds.center));
                Assert.That(point.x, Is.InRange(0f, 1f), actor.transform.parent.name);
                Assert.That(point.y, Is.InRange(0f, 1f), actor.transform.parent.name);
            }

            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
                Capture(camera, output);

            var first = actors[0];
            first.transform.parent.gameObject.SetActive(false);
            yield return null;
            Assert.That(!first, Is.True, "Disabling an actor must dispose its Spine mesh object.");
        }

        [UnityTest]
        public IEnumerator SkillSelectionAndBurnEffectsFollowPointerClicksAndReset()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var canvas = map.GetComponentInParent<Canvas>();
            var eventSystem = roots.Select(root => root.GetComponent<EventSystem>()).SingleOrDefault(item => item != null);
            Assert.That(eventSystem, Is.Not.Null, "The battle scene must own an EventSystem.");
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            Assert.That(raycaster, Is.Not.Null, "The preview must route actual pointer input.");
            var burn = map.Require<GameButton>("soulBurn");
            var skill1 = map.Require<GameButton>("skill1");
            var skill2 = map.Require<GameButton>("skill2");
            var skill3 = map.Require<GameButton>("skill3");
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            var camera = canvas.worldCamera;
            Assert.That(EffectVisible(burn.transform, "Burn Button"), Is.True);
            Assert.That(burn.targetGraphic.canvasRenderer.GetAlpha(), Is.Zero, "The Spine button must not be drawn over a second caption.");
            if (!string.IsNullOrWhiteSpace(output))
            {
                yield return new WaitForSecondsRealtime(0.18f);
                Capture(camera, output, "burn-ready.png");
            }

            var selected1 = map.Require<Image>("skillSelected1");
            var selected2 = map.Require<Image>("skillSelected2");
            var selected3 = map.Require<Image>("skillSelected3");
            Assert.That(selected1.sprite.name, Is.EqualTo("cm_select_skill"));
            Assert.That(selected1.raycastTarget, Is.False);

            Click(skill1, raycaster, eventSystem);
            Assert.That(selected1.gameObject.activeSelf, Is.True);
            Assert.That(EffectVisible(skill1.transform, "Skill Selection"), Is.True);
            Click(skill3, raycaster, eventSystem);
            Assert.That(EffectVisible(skill1.transform, "Skill Selection"), Is.False);
            Assert.That(selected1.gameObject.activeSelf, Is.False);
            Assert.That(selected3.gameObject.activeSelf, Is.True);
            Assert.That(EffectVisible(skill3.transform, "Skill Selection"), Is.True);
            if (!string.IsNullOrWhiteSpace(output))
            {
                yield return new WaitForSecondsRealtime(0.18f);
                Capture(camera, output, "skill-selected.png");
            }

            Click(burn, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.True, "The flash starts with the Burn click.");
            Assert.That(EffectVisible(skill2.transform, "Burn Intro"), Is.True);
            Assert.That(EffectVisible(skill3.transform, "Skill Selection"), Is.False);
            Assert.That(burn.spriteState.pressedSprite.name, Is.EqualTo("game_hud_btn_burnc_p"));
            Assert.That(selected3.gameObject.activeSelf, Is.False);
            if (!string.IsNullOrWhiteSpace(output))
            {
                yield return new WaitForSecondsRealtime(0.15f);
                Capture(camera, output, "burn-use.png");
            }
            yield return new WaitForSecondsRealtime(0.26f);
            Assert.That(EffectVisible(skill2.transform, "Skill Selection"), Is.True);
            Assert.That(selected2.gameObject.activeSelf, Is.False);
            if (!string.IsNullOrWhiteSpace(output))
            {
                Capture(camera, output, "burn-impact.png");
                yield return new WaitForSecondsRealtime(0.85f);
                Capture(camera, output, "burn-armed.png");
            }
            Click(burn, raycaster, eventSystem);
            Assert.That(EffectVisible(skill2.transform, "Burn Intro"), Is.False);
            Assert.That(selected3.gameObject.activeSelf, Is.True);

            Click(burn, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.True);
            Click(burn, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.False);

            Click(burn, raycaster, eventSystem);
            canvas.gameObject.SetActive(false);
            yield return null;
            Assert.That(EffectVisible(skill2.transform, "Burn Intro"), Is.False);
            canvas.gameObject.SetActive(true);
            yield return null;
            Assert.That(EffectVisible(burn.transform, "Burn Button"), Is.True);
            Assert.That(EffectVisible(skill2.transform, "Burn Intro"), Is.False);
            Click(skill3, raycaster, eventSystem);
            Assert.That(EffectVisible(skill3.transform, "Skill Selection"), Is.True);

            Click(burn, raycaster, eventSystem);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.False);
            Click(skill2, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.False, "Selecting the skill does not replay the Burn flash.");
            Assert.That(EffectVisible(skill2.transform, "Burn Intro"), Is.False);
            Assert.That(selected2.gameObject.activeSelf, Is.True);

            map.gameObject.SetActive(false);
            yield return null;
            map.gameObject.SetActive(true);
            yield return null;
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.False);
            Assert.That(EffectVisible(burn.transform, "Burn Button"), Is.True);
            Assert.That(selected2.gameObject.activeSelf, Is.False);

            // The battle screen may replace the HUD under the same Canvas.
            UnityEngine.Object.Destroy(map.gameObject);
            yield return null;
            yield return null;
            Assert.That(canvas, Is.Not.Null);
        }

        private static void Click(GameButton button, GraphicRaycaster raycaster, EventSystem eventSystem)
        {
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.TransformPoint(rect.rect.center))
            };
            var results = new System.Collections.Generic.List<RaycastResult>();
            raycaster.Raycast(pointer, results);
            Assert.That(results.FirstOrDefault().gameObject, Is.SameAs(button.gameObject), button.name);
            Assert.That(button.IsInteractable(), Is.True, button.name);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            if (button.transition == Selectable.Transition.SpriteSwap)
                Assert.That(((Image)button.targetGraphic).overrideSprite, Is.SameAs(button.spriteState.pressedSprite));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            Assert.That(ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler), Is.True, button.name);
        }

        private static bool EffectVisible(Transform anchor, string name)
        {
            Transform effect = anchor.Find(name);
            return effect && effect.gameObject.activeInHierarchy &&
                   effect.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0;
        }

        private static void Capture(Camera camera, string directory, string file = "battle-scene-1920x1080.png")
        {
            Directory.CreateDirectory(directory);
            var target = RenderTexture.GetTemporary(1920, 1080, 24);
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousRect = camera.rect;
            try
            {
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(directory, file), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(texture);
            }
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
