#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
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
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string SlimeAsset = "Assets/Game/Modules/BattleScene/Res/Units/Slime/slime";
        private Scene scene;

        internal static string[] SlowEnemyIds() => Enumerable.Repeat("c1008", 4).ToArray();

        internal static Scene LoadPlayerOpeningScene()
        {
            // These tests require a deterministic first player turn. The default scene can open on either side.
            SceneManager.sceneLoaded += ConfigureOpening;
            try
            {
                return EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            }
            catch
            {
                SceneManager.sceneLoaded -= ConfigureOpening;
                throw;
            }

            static void ConfigureOpening(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.path != ScenePath) return;
                SceneManager.sceneLoaded -= ConfigureOpening;
                var host = loaded.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                typeof(BattleHudPreview).GetField("allyHeroIds", PrivateInstance).SetValue(host,
                    new[] { "c1001", "c1062", "c1005", "c1008" });
                typeof(BattleHudPreview).GetField("enemyHeroIds", PrivateInstance).SetValue(host, SlowEnemyIds());
            }
        }

        private static void BindSlimeModel(BattlePreviewActor actor)
        {
            var asset = new BattleSkillPresentationAsset
            {
                document = AssetDatabase.LoadAssetAtPath<TextAsset>(SlimeAsset + ".spine.json"),
                atlas = AssetDatabase.LoadAssetAtPath<TextAsset>(SlimeAsset + ".atlas.txt"),
                pages = new[] { AssetDatabase.LoadAssetAtPath<Texture2D>(SlimeAsset + ".png") },
                pageNames = new[] { "slime.sct" },
                premultipliedAlpha = true,
                scale = .01f
            };
            typeof(BattlePreviewActor).GetMethod("UseModel", PrivateInstance).Invoke(actor, new object[] { asset, "ice", "Test Slime" });
        }

        [UnityTest]
        public IEnumerator AuthoredSceneShowsOriginalActorsAndHud()
        {
            Assert.That(File.Exists(ScenePath), Is.True, "The authored battle scene must be saved in the project.");
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var units = roots.Single(root => root.name == "Units").transform;
            var actors = units.GetComponentsInChildren<BattlePreviewActor>()
                .Select(actor => actor.GetComponentInChildren<MeshFilter>())
                .Where(mesh => mesh && mesh.sharedMesh && mesh.sharedMesh.name == "Skeleton dynamic mesh").ToArray();
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

            var day = roots.Single(root => root.name == "Forest2Stage");
            AssertSlotsOnGrass(day, units);
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
                Capture(camera, output);

            var duskPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/BattleScene/Res/Prefab/Forest3Stage.prefab");
            Assert.That(duskPrefab, Is.Not.Null);
            var dusk = UnityEngine.Object.Instantiate(duskPrefab);
            SceneManager.MoveGameObjectToScene(dusk, scene);
            try
            {
                day.SetActive(false);
                AssertSlotsOnGrass(dusk, units);
                if (!string.IsNullOrWhiteSpace(output))
                    Capture(camera, output, "forest3-ground.png");
            }
            finally
            {
                dusk.SetActive(false);
                UnityEngine.Object.Destroy(dusk);
                day.SetActive(true);
            }

            var first = actors[0];
            first.transform.parent.gameObject.SetActive(false);
            yield return null;
            Assert.That(!first, Is.True, "Disabling an actor must dispose its Spine mesh object.");
        }

        private static void AssertSlotsOnGrass(GameObject stage, Transform units)
        {
            var ground = stage.transform.Find("Ground").GetComponent<SpriteRenderer>();
            Bounds bounds = ground.bounds;
            // Both atlas cuts have rocks and shrubs in their upper rows. Use the lower
            // 160 of 256 rows where the grass spans the playable width.
            float grassTop = bounds.min.y + bounds.size.y * (160f / 256f);
            Assert.That(bounds.size.x / bounds.size.y,
                Is.EqualTo(ground.sprite.rect.width / ground.sprite.rect.height).Within(0.02f));
            foreach (string team in new[] { "Allies", "Enemies" })
            foreach (Transform slot in units.Find(team))
            {
                Assert.That(slot.position.x, Is.InRange(bounds.min.x, bounds.max.x), $"{stage.name} {team} {slot.name}");
                Assert.That(slot.position.y, Is.InRange(bounds.min.y, grassTop), $"{stage.name} {team} {slot.name}");
                var shadow = slot.GetComponentInChildren<SpriteRenderer>();
                Assert.That(shadow.bounds.center.y, Is.InRange(bounds.min.y, grassTop),
                    $"{stage.name} {team} {slot.name} shadow");
            }
        }

        [UnityTest]
        public IEnumerator SkillSelectionAndBurnEffectsFollowPointerClicksAndReset()
        {
            SceneManager.sceneLoaded += LayoutOnly;
            try
            {
                scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                yield return null;
                yield return null;
            }
            finally { SceneManager.sceneLoaded -= LayoutOnly; }

            void LayoutOnly(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.path != ScenePath) return;
                loaded.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item).AutoStartBattle = false;
            }

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
            bool introBeforeClick = EffectVisible(skill2.transform, "Burn Intro");
            Click(skill2, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.False, "Selecting the skill does not replay the Burn flash.");
            Assert.That(EffectVisible(skill2.transform, "Burn Intro"), Is.EqualTo(introBeforeClick), "Selecting the armed skill neither cancels nor restarts its effect.");
            Assert.That(selected2.gameObject.activeSelf, Is.False, "Clicking the armed skill preserves Soul Burn.");
            Assert.That(burn.spriteState.pressedSprite.name, Is.EqualTo("game_hud_btn_burnc_p"));
            Click(burn, raycaster, eventSystem);
            Click(skill2, raycaster, eventSystem);
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

        [UnityTest]
        public IEnumerator PlayableBattleCommitsOnlyAfterTargetAndPlaysSkill()
        {
            scene = LoadPlayerOpeningScene();
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var start = map.Require<GameButton>("startPreview");
            var raycaster = map.GetComponentInParent<GraphicRaycaster>();
            var eventSystem = roots.Select(root => root.GetComponent<EventSystem>()).Single(item => item);
            Assert.That(preview.SimulationSnapshot, Is.Not.Null, "Opening the scene enters the playable battle automatically.");
            Assert.That(start.gameObject.activeInHierarchy, Is.False);
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
                Capture(raycaster.eventCamera, output, "battle-start.png");
            Assert.That(start.gameObject.activeInHierarchy, Is.False, "The obsolete entry button stays hidden during the fight.");
            var initial = preview.SimulationSnapshot;
            Assert.That(initial.Units, Has.Count.EqualTo(8));
            var readiness = map.transform.Find("Combat Readiness/Live Readiness/Readiness Portraits");
            Assert.That(readiness, Is.Not.Null, "The battle must show live readiness portraits.");
            Assert.That(readiness.childCount, Is.EqualTo(initial.Units.Count));
            yield return null;
            yield return null;
            var detailsButton = map.transform.Find("Combat Readiness/Live Readiness/Readiness Details Button").GetComponent<GameButton>();
            var details = map.transform.Find("Combat Readiness/Live Readiness/Readiness Details");
            Click(detailsButton, raycaster, eventSystem);
            Assert.That(details.gameObject.activeInHierarchy, Is.True);
            Assert.That(details.GetComponentsInChildren<GameText>().Any(text => text.text.Contains("100")), Is.True);
            if (!string.IsNullOrWhiteSpace(output))
                Capture(raycaster.eventCamera, output, "battle-readiness-details.png");
            Click(detailsButton, raycaster, eventSystem);
            Assert.That(details.gameObject.activeInHierarchy, Is.False);
            Assert.That(initial.GetUnit(initial.CurrentActorId).Slot.Side, Is.EqualTo(BattleSide.Ally));
            Assert.That(map.transform.Find("Active Unit/Class").GetComponent<Image>().sprite.name,
                Is.EqualTo("cm_icon_role_mage"));
            var marker = map.transform.Find("Unit Status/Enemies").GetChild(0);
            Assert.That(marker.Find("Level Value").GetComponent<GameText>().text,
                Is.EqualTo(initial.GetUnit("enemy-1").Level.ToString()));
            var health = marker.Find("HP").GetComponent<Image>();
            float originalFill = health.fillAmount;
            Assert.That(preview.TrySelectBattleTarget("ally-1"), Is.False);
            Assert.That(preview.SimulationSnapshot.LastCommandSequence, Is.Zero);
            Assert.That(preview.TrySelectBattleTarget("enemy-1"), Is.True);
            Assert.That(preview.SimulationSnapshot.LastCommandSequence, Is.EqualTo(1UL));
            Assert.That(preview.SimulationSnapshot.GetUnit("enemy-1").CurrentHealth,
                Is.LessThan(initial.GetUnit("enemy-1").CurrentHealth));
            Assert.That(health.fillAmount, Is.EqualTo(originalFill), "The HUD waits for the skill impact.");
            float deadline = Time.realtimeSinceStartup + 4f;
            while (health.fillAmount >= originalFill && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(health.fillAmount, Is.LessThan(originalFill), "The skill impact updates the HUD.");
            var feedback = map.transform.Find("Battle Feedback/enemy-1/Impact").GetComponent<GameText>();
            var digits = feedback.transform.parent.GetComponentsInChildren<RawImage>();
            Assert.That(digits.Length, Is.GreaterThan(0), "Original bitmap glyphs display the hit amount.");
            Assert.That(digits.All(digit => digit.texture && digit.uvRect.width > 0f), Is.True);
            Assert.That(digits.Any(digit => Mathf.Abs(digit.rectTransform.anchoredPosition.y) > 0.01f), Is.True, "The impact starts a digit bounce.");
            var targetActor = roots.Single(root => root.name == "Units").transform.Find("Enemies").GetChild(0)
                .GetComponentInChildren<BattlePreviewActor>();
            var worldCamera = preview.GetComponent<Canvas>().worldCamera;
            float targetScreenX = worldCamera.WorldToScreenPoint(targetActor.Bounds.center).x;
            float feedbackScreenX = RectTransformUtility.WorldToScreenPoint(worldCamera, feedback.rectTransform.position).x;
            Assert.That(feedbackScreenX, Is.EqualTo(targetScreenX).Within(4f),
                "Damage follows the rendered target through source camera motion instead of a fixed HUD marker.");
            Assert.That(feedback.font.name, Is.EqualTo("BattleDamageDigits"));
            var feedbackPosition = feedback.rectTransform.position;
            yield return null;
            yield return null;
            Assert.That(digits.All(digit => float.IsFinite(digit.rectTransform.anchoredPosition.y)), Is.True);
            var effects = roots.Single(root => root.name == "Units").transform.Find("Battle Skill Effects");
            Assert.That(effects.GetComponentsInChildren<MeshFilter>().Any(mesh => mesh.sharedMesh && mesh.sharedMesh.vertexCount > 0),
                Is.True, "Original CFX must be rendered by the battle scene.");
#if UNITY_EDITOR_WIN
            Assert.That(preview.StartedSkillVoices, Is.EqualTo(1));
            Assert.That(preview.StartedSkillSounds, Is.EqualTo(1));
#endif

            var canvas = preview.GetComponent<Canvas>();
            if (!string.IsNullOrWhiteSpace(output))
                Capture(canvas.worldCamera, output, "battle-simulation.png");
            canvas.gameObject.SetActive(false);
            yield return null;
            Assert.That(preview.SimulationSnapshot, Is.Null);
            canvas.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.That(start.gameObject.activeInHierarchy, Is.False, "Reopening the HUD automatically starts a new battle.");
            Assert.That(preview.SimulationSnapshot, Is.Not.Null);
            Assert.That(preview.SimulationSnapshot.LastCommandSequence, Is.Zero);
            yield return null;
            var secondSkill = map.Require<GameButton>("skill2");
            Click(secondSkill, raycaster, eventSystem);
            Assert.That(preview.TrySelectBattleTarget("enemy-1"), Is.True);
            var cooldown = secondSkill.transform.Find("Cooldown Overlay");
            deadline = Time.realtimeSinceStartup + 5f;
            while (!cooldown.gameObject.activeSelf && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(cooldown.gameObject.activeSelf, Is.True, "A used skill retains its cooldown state while the source cinematic hides the controls.");
            Assert.That(cooldown.Find("Cooldown Number").GetComponent<GameText>().text, Is.Not.Empty);
            Assert.That(secondSkill.IsInteractable(), Is.False);
            if (!string.IsNullOrWhiteSpace(output))
                Capture(canvas.worldCamera, output, "battle-skill-cast.png");
        }

        [UnityTest]
        public IEnumerator MeleeActorsMoveToTheTargetAndReturnToTheirSlot()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var units = roots.Single(root => root.name == "Units").transform;
            preview.StartBattle();

            BattlePreviewActor mover = null;
            Vector3 home = Vector3.zero;
            float deadline = Time.realtimeSinceStartup + 22f;
            while (!mover && Time.realtimeSinceStartup < deadline)
            {
                var snapshot = preview.SimulationSnapshot;
                var actor = snapshot.GetUnit(snapshot.CurrentActorId);
                var target = snapshot.Units.FirstOrDefault(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy);
                if (actor?.Slot.Side == BattleSide.Ally && target != null &&
                    preview.TrySelectBattleTarget(target.InstanceId) && actor.DefinitionId == "c1001")
                {
                    mover = units.Find("Allies").GetChild(actor.Slot.Position - 1).GetComponentInChildren<BattlePreviewActor>();
                    home = mover.transform.localPosition;
                }
                yield return null;
            }
            Assert.That(mover, Is.Not.Null, "Ras must reach a playable turn.");
            var slotPosition = mover.transform.parent.position;
            deadline = Time.realtimeSinceStartup + 4f;
            while (mover.transform.localPosition.x - home.x < 0.25f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(mover.transform.localPosition.x - home.x, Is.GreaterThan(0.24f));
            Assert.That(mover.transform.parent.position, Is.EqualTo(slotPosition));
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
                Capture(preview.GetComponent<Canvas>().worldCamera, output, "battle-melee-approach.png");

            deadline = Time.realtimeSinceStartup + 5f;
            while (!mover.transform.localPosition.Equals(home) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(mover.transform.localPosition, Is.EqualTo(home));
        }

        [UnityTest]
        public IEnumerator EnemyActionHidesSkillsAndRestoresTheNextAlliedTurn()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            preview.StartBattle();
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(preview);
            var presentedSkill = simulation.GetType().GetField("presentedSkillId", PrivateInstance);
            var enemySkills = preview.SimulationSnapshot.Units.Where(unit => unit.Slot.Side == BattleSide.Enemy)
                .SelectMany(unit => unit.Form.SkillIds).ToArray();
            float deadline = Time.realtimeSinceStartup + 40f;
            int soundsBeforeEnemy = preview.StartedSkillSounds;
            while (!enemySkills.Contains((string)presentedSkill.GetValue(simulation)) && Time.realtimeSinceStartup < deadline)
            {
                soundsBeforeEnemy = preview.StartedSkillSounds;
                var snapshot = preview.SimulationSnapshot;
                var target = snapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy);
                preview.TrySelectBattleTarget(target.InstanceId);
                yield return null;
            }
            Assert.That(enemySkills, Does.Contain((string)presentedSkill.GetValue(simulation)),
                "Reach an enemy hero skill through normal turn progression.");
            foreach (string key in new[] { "skill1", "skill2", "skill3", "soulBurn" })
                Assert.That(map.Require<GameButton>(key).gameObject.activeInHierarchy, Is.False,
                    "Enemy actions must hide the entire player skill group: " + key);
            Assert.That(map.transform.Find("Skills").gameObject.activeSelf, Is.False,
                "Skill borders and overlays must be hidden as well.");
#if UNITY_EDITOR_WIN
            Assert.That(preview.StartedSkillSounds, Is.GreaterThan(soundsBeforeEnemy),
                "The enemy hero action starts its original skill sound.");
#endif
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
                Capture(preview.GetComponent<Canvas>().worldCamera, output, "battle-enemy-turn.png");
            deadline = Time.realtimeSinceStartup + 15f;
            while (!map.Require<GameButton>("skill1").IsInteractable() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(map.Require<GameButton>("skill1").gameObject.activeInHierarchy, Is.True);
            Assert.That(map.Require<GameButton>("skill1").IsInteractable(), Is.True);
            Assert.That(map.transform.Find("Skills").gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator FullyAbsorbedSlimeHitStillPlaysImpactAudioAndReaction()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            preview.StartBattle();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var simulation = typeof(BattleHudPreview).GetField("simulation", flags).GetValue(preview);
            var source = (TextAsset)typeof(BattleHudPreview).GetField("battleCatalog", flags).GetValue(preview);
            // Unity JsonUtility does not populate nullable skill coefficients. Use the same
            // package parser as the live preview without changing the test assembly references.
            var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", throwOnError: true);
            var deserialize = json.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method =>
                method.Name == "DeserializeObject" && !method.IsGenericMethod && method.GetParameters().Length == 2 &&
                method.GetParameters()[0].ParameterType == typeof(string) && method.GetParameters()[1].ParameterType == typeof(Type));
            var catalog = (BattleCatalogData)deserialize.Invoke(null, new object[] { source.text, typeof(BattleCatalogData) });
            var match = BattlePreviewFactory.Create("slime-audio-test", 271828UL, catalog);
            BattleFlowEvent absorbedHit = null;
            BattleFlowEvent action = null;

            // Advance the real rules synchronously until Angelica's shield fully absorbs a slime hit.
            // Present only that accepted action and impact, without waiting through unrelated animations.
            for (int turn = 0; turn < 32 && absorbedHit == null; turn++)
            {
                var snapshot = match.Snapshot;
                var actor = snapshot.GetUnit(snapshot.CurrentActorId);
                bool shield = actor.DefinitionId == "c1062" && actor.GetCooldown("c1062.s3") == 0;
                var side = shield ? BattleSide.Ally :
                    actor.Slot.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;
                var target = snapshot.Units.Where(unit => unit.IsAlive && unit.Slot.Side == side)
                    .OrderByDescending(unit => unit.Barrier).First();
                string skill = shield ? "c1062.s3" : actor.Form.SkillIds[0];
                ulong sequence = snapshot.LastCommandSequence + 1;
                var result = match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion,
                    sequence, "shield-audio-" + sequence, actor.InstanceId, skill, target.InstanceId));
                Assert.That(result.Status, Is.EqualTo(BattleCommandStatus.Accepted));
                if (actor.Slot.Side != BattleSide.Enemy || target.Barrier == 0) continue;
                absorbedHit = result.Events.SingleOrDefault(item => item.Kind == BattleFlowEventKind.Damage &&
                    item.TargetId == target.InstanceId && item.Amount == 0);
                if (absorbedHit == null) continue;
                Assert.That(result.Events.Any(item => item.Kind == BattleFlowEventKind.BarrierAbsorbed &&
                    item.TargetId == target.InstanceId && item.Amount > 0), Is.True);
                Assert.That(absorbedHit.RemainingHealth, Is.EqualTo(target.CurrentHealth));
                action = result.Events.First(item => item.Kind == BattleFlowEventKind.Action);
            }
            Assert.That(absorbedHit, Is.Not.Null, "A real shield must absorb the entire accepted slime attack.");

            var hitTarget = match.Snapshot.GetUnit(absorbedHit.TargetId);
            var units = roots.Single(root => root.name == "Units").transform;
            var attacker = match.Snapshot.GetUnit(action.SourceId);
            BindSlimeModel(units.Find("Enemies").GetChild(attacker.Slot.Position - 1).GetComponentInChildren<BattlePreviewActor>());
            var targetActor = units.Find("Allies").GetChild(hitTarget.Slot.Position - 1)
                .GetComponentInChildren<BattlePreviewActor>();
            var player = typeof(BattlePreviewActor).GetField("player", flags).GetValue(targetActor);
            var present = simulation.GetType().GetMethod("Present", flags);
            present.Invoke(simulation, new object[] { action });
            int beforeImpact = preview.StartedHitSounds;
            present.Invoke(simulation, new object[] { absorbedHit });
            float impactAt = (float)simulation.GetType().GetField("impactSeconds", flags).GetValue(simulation);
            simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { impactAt + .001f });

#if UNITY_EDITOR_WIN
            Assert.That(preview.StartedHitSounds, Is.EqualTo(beforeImpact + 2),
                "A shield blocks health loss, not the original blunt hit and Japanese attacked voice.");
#endif
            Assert.That(player.GetType().GetProperty("CurrentAnimation").GetValue(player), Is.EqualTo("attacked"),
                "A fully absorbed hit still plays the target's reaction animation.");
            var feedbackRoot = map.transform.Find("Battle Feedback");
            Assert.That(feedbackRoot.GetComponentsInChildren<RawImage>().Length, Is.Zero,
                "Fully absorbed damage must not create a zero-damage floating number.");
            Assert.That(feedbackRoot.GetComponentsInChildren<GameText>().Any(text => text.text == "0"), Is.False);
            if (absorbedHit.HitKind == BattleHitKind.Miss)
                Assert.That(feedbackRoot.GetComponentsInChildren<GameText>().Any(text => text.text == "未命中"), Is.True,
                    "An absorbed miss still shows its localized caption.");
        }

        [UnityTest]
        public IEnumerator OriginalSlimeSkeletonExposesItsHitEventTiming()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            var actor = units.Find("Enemies").GetChild(0).GetComponentInChildren<BattlePreviewActor>();
            BindSlimeModel(actor);
            var method = typeof(BattlePreviewActor).GetMethod("GetBattleAnimationEventTime", PrivateInstance);
            Assert.That(method, Is.Not.Null, "Impact scheduling needs the original animation's hit timing.");
            Assert.That((float)method.Invoke(actor, new object[] { "skill1", "hit" }), Is.EqualTo(.7f).Within(.0001f));
            Assert.That((float)method.Invoke(actor, new object[] { "skill1", "missing" }), Is.EqualTo(-1f));
        }

        [UnityTest]
        public IEnumerator ThreeOriginalUltimatePresentationsRenderAndRestoreTheStage()
        {
            foreach (string wanted in new[] { "c1005", "c1001", "c1062" })
            {
                scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                yield return null;
                yield return null;
                var roots = scene.GetRootGameObjects();
                var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
                var camera = preview.GetComponent<Canvas>().worldCamera;
                var background = roots.Single(root => root.name == "Forest2Stage");
                Vector3 originalPosition = camera.transform.position;
                float originalSize = camera.orthographicSize;
                preview.StartBattle();
                // Scene loading and synchronous preparation can occupy the frame before this
                // test's synthetic click. Wait for visible frames, as a real user would, so the
                // next LateUpdate cannot consume the loading interval as the entire skill.
                int stableFrames = 0;
                float readyDeadline = Time.realtimeSinceStartup + 10f;
                while (stableFrames < 2)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(readyDeadline), "The battle must become ready for an observable input frame.");
                    stableFrames = Time.unscaledDeltaTime > 0f && Time.unscaledDeltaTime < .05f ? stableFrames + 1 : 0;
                }
                bool submitted = false;
                float deadline = Time.realtimeSinceStartup + 45f;
                while (!submitted && Time.realtimeSinceStartup < deadline)
                {
                    var snapshot = preview.SimulationSnapshot;
                    var actor = snapshot.GetUnit(snapshot.CurrentActorId);
                    var first = map.Require<GameButton>("skill1");
                    if (actor?.Slot.Side == BattleSide.Ally && first.gameObject.activeInHierarchy && first.IsInteractable())
                    {
                        bool ultimate = actor.DefinitionId == wanted;
                        map.Require<GameButton>(ultimate ? "skill3" : "skill1").onClick.Invoke();
                        var targetSide = ultimate && wanted == "c1062" ? BattleSide.Ally : BattleSide.Enemy;
                        string target = snapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side == targetSide).InstanceId;
                        submitted = preview.TrySelectBattleTarget(target) && ultimate;
                    }
                    yield return null;
                }
                Assert.That(submitted, Is.True, wanted + " reaches its real playable turn.");
                yield return null;

                deadline = Time.realtimeSinceStartup + 2f;
                while (!preview.IsCinematicVisible && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(preview.IsCinematicVisible, Is.True, wanted + " enters its source cinematic.");
                Assert.That(map.transform.Find("Skills").gameObject.activeInHierarchy, Is.False);
                bool sawCutin = false, sawStageReplacement = false, captured = false;
                string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
                deadline = Time.realtimeSinceStartup + 15f;
                while (preview.IsCinematicVisible && Time.realtimeSinceStartup < deadline)
                {
                    var cutin = camera.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(item => item.name.StartsWith("CUTIN "));
                    sawCutin |= cutin && cutin.sharedMaterial.mainTexture;
                    if (cutin)
                    {
                        Assert.That(cutin.bounds.size.x + .001f, Is.GreaterThanOrEqualTo(camera.orthographicSize * 2 * camera.aspect));
                        Assert.That(cutin.bounds.size.y + .001f, Is.GreaterThanOrEqualTo(camera.orthographicSize * 2));
                    }
                    sawStageReplacement |= !background.activeSelf;
                    if (!captured && ((wanted == "c1005" && !background.activeSelf &&
                        camera.transform.Find("BGHATCH")) || cutin))
                    {
                        captured = true;
                        if (!string.IsNullOrWhiteSpace(output)) Capture(camera, output, "battle-" + wanted + "-s3.png");
                    }
                    yield return null;
                }
                Assert.That(preview.IsCinematicVisible, Is.False, "The cinematic ends without blocking the next turn.");
                if (wanted == "c1005") Assert.That(sawStageReplacement, Is.True, "Mercedes replaces the stage instead of using a CUTIN.");
                else Assert.That(sawCutin, Is.True, wanted + " renders its original CUTIN frames.");
                Assert.That(camera.transform.position, Is.EqualTo(originalPosition));
                Assert.That(camera.orthographicSize, Is.EqualTo(originalSize));
                Assert.That(background.activeSelf, Is.True);
                if (wanted == "c1062")
                {
                    deadline = Time.realtimeSinceStartup + 4f;
                    var markers = map.transform.Find("Unit Status/Allies");
                    while (!markers.Cast<Transform>().Any(marker =>
                        marker.Find("HP/Shield Fill").gameObject.activeInHierarchy) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    var recipients = preview.SimulationSnapshot.Units.Where(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Ally);
                    var allies = roots.Single(root => root.name == "Units").transform.Find("Allies");
                    foreach (var unit in recipients)
                    {
                        var marker = markers.GetChild(unit.Slot.Position - 1);
                        var glyphs = marker.Find("Frame/Status Icons").Cast<Transform>()
                            .Where(icon => icon.gameObject.activeInHierarchy).Select(icon => icon.GetComponent<Image>().sprite.name).ToArray();
                        CollectionAssert.Contains(glyphs, "stic_debuf_impossible");
                        CollectionAssert.Contains(glyphs, "stic_protect");
                        Assert.That(Array.IndexOf(glyphs, "stic_debuf_impossible"), Is.LessThan(Array.IndexOf(glyphs, "stic_protect")));
                        Assert.That(marker.Find("HP/Shield Fill").gameObject.activeInHierarchy, Is.True);
                        Assert.That(allies.GetChild(unit.Slot.Position - 1).GetComponentsInChildren<MeshRenderer>()
                            .Any(renderer => renderer.name == "Battle shield aura"), Is.True);
                    }
                    if (!string.IsNullOrWhiteSpace(output)) Capture(camera, output, "battle-c1062-s3-shields.png");
                }
                yield return SceneManager.UnloadSceneAsync(scene);
                scene = default;
            }
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
