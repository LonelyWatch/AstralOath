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

        internal static Scene LoadPlayerOpeningScene(string firstHero = null, bool useDefaultRoster = false)
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

            void ConfigureOpening(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.path != ScenePath) return;
                SceneManager.sceneLoaded -= ConfigureOpening;
                var host = loaded.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                if (useDefaultRoster)
                {
                    var units = loaded.GetRootGameObjects().Single(root => root.name == "Units").transform;
                    ConfigureTeam(units.Find("Allies"), BattleHudPreview.DefaultAllyHeroIds);
                    ConfigureTeam(units.Find("Enemies"), BattleHudPreview.DefaultEnemyHeroIds);
                    return;
                }

                var allies = firstHero == null ? new[] { "c1001", "c1062", "c1005", "c1008" }
                    : Enumerable.Repeat(firstHero, 4).ToArray();
                typeof(BattleHudPreview).GetField("allyHeroIds", PrivateInstance).SetValue(host, allies);
                typeof(BattleHudPreview).GetField("enemyHeroIds", PrivateInstance).SetValue(host, SlowEnemyIds());
            }

            void ConfigureTeam(Transform team, System.Collections.Generic.IReadOnlyList<string> ids)
            {
                var field = typeof(BattlePreviewActor).GetField("heroId", PrivateInstance);
                for (int slot = 0; slot < ids.Count; slot++)
                {
                    var actor = team.GetChild(slot).GetComponentInChildren<BattlePreviewActor>(true);
                    field.SetValue(actor, ids[slot]);
                }
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
                    Assert.That(bar.Find("Frame").GetComponent<Image>().sprite.name, Is.EqualTo(bar.Find("HP/Personal Resource") ? "game_hud_bar_bg" : "game_hud_bar_bg_n"));
            }
            Assert.That(map.Require<GameText>("wave").text, Is.Not.Empty);

            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Single(item => item.name == "Battle Camera");
            Assert.That(camera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(camera.pixelRect.width / camera.pixelRect.height, Is.EqualTo(Screen.width / (float)Screen.height).Within(0.01f));
            foreach (var actor in actors)
            {
                Vector3 point = camera.WorldToViewportPoint(actor.transform.TransformPoint(actor.sharedMesh.bounds.center));
                Assert.That(point.x, Is.InRange(0f, 1f), actor.transform.parent.name);
                Assert.That(point.y, Is.InRange(0f, 1f), actor.transform.parent.name);
            }

            var day = roots.Single(root => root.name == "Forest2Stage");
            var forest = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/BattleScene/Res/Prefab/Forest2Stage.prefab");
            var authoredGround = forest.transform.Find("Ground").GetComponent<SpriteRenderer>();
            AssertSlotsOnGrass(day, units, authoredGround);
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

        private static void AssertSlotsOnGrass(GameObject stage, Transform units, SpriteRenderer authored = null)
        {
            var ground = stage.transform.Find("Ground").GetComponent<SpriteRenderer>();
            Bounds bounds = ground.bounds;
            // Both atlas cuts have rocks and shrubs in their upper rows. Use the lower
            // 160 of 256 rows where the grass spans the playable width.
            Bounds sourceBounds = authored ? authored.bounds : bounds;
            float grassTop = sourceBounds.min.y + sourceBounds.size.y * (160f / 256f);
            Assert.That(sourceBounds.size.x / sourceBounds.size.y,
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
            Assert.That(selected2.gameObject.activeSelf, Is.True);
            Assert.That(selected3.gameObject.activeSelf, Is.False);
            Assert.That(EffectVisible(skill2.transform, "Skill Selection"), Is.True);

            Click(burn, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.True);
            Click(burn, raycaster, eventSystem);
            Assert.That(EffectVisible(burn.transform, "Soul Burn Use"), Is.False);
            Assert.That(selected2.gameObject.activeSelf, Is.True, "Cancel during the intro also retains the Soul Burn skill.");

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
            var unitGroups = roots.Single(root => root.name == "Units").transform;
            AstralOath.Client.Skeletons.ISkeletonPlayer actingSkeleton = null;
            foreach (var unit in initial.Units)
            {
                var group = unitGroups.Find(unit.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies");
                var unitActor = group.GetChild(unit.Slot.Position - 1).GetComponentInChildren<BattlePreviewActor>();
                var skeleton = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                    .GetField("player", PrivateInstance).GetValue(unitActor);
                string idle = unit.InstanceId == initial.CurrentActorId ? "b_idle_ready" : "idle";
                Assert.That(skeleton.CurrentAnimation, Is.EqualTo(idle), unit.InstanceId);
                if (unit.InstanceId == initial.CurrentActorId) actingSkeleton = skeleton;
            }
            Assert.That(actingSkeleton, Is.Not.Null);
            float entryDeadline = Time.realtimeSinceStartup + actingSkeleton.Duration + 1f;
            while (actingSkeleton.CurrentAnimation == "b_idle_ready" && Time.realtimeSinceStartup < entryDeadline)
                yield return null;
            Assert.That(actingSkeleton.CurrentAnimation, Is.EqualTo("b_idle"), "A waiting player sees the entry cue exactly once.");
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
            var level = marker.Find("Level");
            var tens = level.Find("Level Tens")?.GetComponent<Image>();
            var ones = level.Find("Level Ones")?.GetComponent<Image>();
            int enemyLevel = initial.GetUnit("enemy-1").Level;
            Assert.That(tens, Is.Not.Null);
            Assert.That(ones, Is.Not.Null);
            Assert.That(tens.sprite.name, Is.EqualTo("itxt_num" + enemyLevel / 10 + "_b"));
            Assert.That(ones.sprite.name, Is.EqualTo("itxt_num" + enemyLevel % 10 + "_b"));
            Assert.That(tens.rectTransform.anchoredPosition.x, Is.EqualTo(-4.5f).Within(.001f));
            Assert.That(ones.rectTransform.anchoredPosition.x, Is.EqualTo(7.5f).Within(.001f));
            var health = marker.Find("HP").GetComponent<Image>();
            float originalFill = health.fillAmount;
            Assert.That(EffectVisible(map.Require<GameButton>("skill1").transform, "Skill Selection"), Is.True);
            float BodyBrightness(BattleSide side, int position = 1)
            {
                var units = unitGroups.Find(side == BattleSide.Ally ? "Allies" : "Enemies");
                var unit = units.GetChild(position - 1).GetComponentInChildren<BattlePreviewActor>();
                var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", PrivateInstance).GetValue(unit);
                var properties = new MaterialPropertyBlock();
                body.GetPropertyBlock(properties);
                int brightness = Shader.PropertyToID("_PresentationBrightness");
                return properties.HasFloat(brightness) ? properties.GetFloat(brightness) : 1f;
            }
            var caster = initial.GetUnit(initial.CurrentActorId);
            var nonTarget = initial.Units.First(unit => unit.Slot.Side == BattleSide.Ally && unit.InstanceId != caster.InstanceId);
            Assert.That(BodyBrightness(BattleSide.Ally, nonTarget.Slot.Position), Is.LessThan(1f));
            Assert.That(BodyBrightness(BattleSide.Ally, caster.Slot.Position), Is.EqualTo(1f), "The acting hero remains lit.");
            Assert.That(BodyBrightness(BattleSide.Enemy), Is.EqualTo(1f));
            var legalBadge = marker.Find("Target Number");
            var invalidMarker = map.transform.Find("Unit Status/Allies").GetChild(0);
            Assert.That(legalBadge, Is.Not.Null, "A legal target has a numbered click surface beside the level badge.");
            Assert.That(legalBadge.gameObject.activeInHierarchy, Is.True);
            Assert.That(invalidMarker.Find("Target Number").gameObject.activeInHierarchy, Is.False);
            var railBadge = readiness.Find("enemy-1/Target Number");
            Assert.That(railBadge, Is.Not.Null);
            Assert.That(railBadge.Find("Number").GetComponent<Image>().sprite,
                Is.SameAs(legalBadge.Find("Number").GetComponent<Image>().sprite));
            var rankedEnemies = initial.Units.Where(unit => unit.Slot.Side == BattleSide.Enemy)
                .OrderByDescending(unit => unit.ReadinessBp).ThenBy(unit => unit.Slot.Position).ToArray();
            for (int rank = 0; rank < rankedEnemies.Length; ++rank)
            {
                var target = rankedEnemies[rank];
                var badge = map.transform.Find("Unit Status/Enemies").GetChild(target.Slot.Position - 1).Find("Target Number");
                var compactBadge = readiness.Find(target.InstanceId + "/Target Number");
                Assert.That(badge.GetComponent<Image>().sprite.name, Is.EqualTo("game_hud_target1"));
                Assert.That(compactBadge.GetComponent<Image>().sprite.name, Is.EqualTo("game_hud_target2"));
                var badgeRect = (RectTransform)badge;
                var digitRect = (RectTransform)badge.Find("Number");
                // Centers of the visible red face and the white digits in the original HUD art.
                Vector2 faceCenter = new Vector2(-3f, 5f) * (badgeRect.rect.height / 39f);
                Vector2 inkCenter = new Vector2(rank == 0 ? -1.5f : -1f, 1.5f)
                    * (digitRect.rect.height / 34f);
                Assert.That(Vector2.Distance(digitRect.anchoredPosition + inkCenter, faceCenter), Is.LessThan(1f),
                    $"Target rank {rank + 1} must sit in the red face.");
                Assert.That(badge.Find("Number").GetComponent<Image>().sprite.name, Is.EqualTo($"itxt_num{rank + 1}_b"));
                Assert.That(compactBadge.Find("Number").GetComponent<Image>().sprite,
                    Is.SameAs(badge.Find("Number").GetComponent<Image>().sprite));
            }
            Assert.That(preview.TrySelectBattleTarget("ally-1"), Is.False);
            Assert.That(preview.SimulationSnapshot.LastCommandSequence, Is.Zero);
            Click(legalBadge.GetComponent<GameButton>(), raycaster, eventSystem);
            Assert.That(legalBadge.gameObject.activeInHierarchy, Is.False, "Submitting hides the target click surface immediately.");
            Assert.That(BodyBrightness(BattleSide.Ally, nonTarget.Slot.Position), Is.EqualTo(1f), "Submitting restores dimmed models before presentation.");
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
        public IEnumerator RosesS3ShowsStatusIconsAndReadinessWordsOnTheAffectedActor()
        {
            scene = LoadPlayerOpeningScene("c1003");
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var actor = host.SimulationSnapshot.GetUnit(host.SimulationSnapshot.CurrentActorId);
            Assert.That(actor.DefinitionId, Is.EqualTo("c1003"));
            var layer = map.transform.Find("Unit Status/Allies").GetChild(actor.Slot.Position - 1).Find("Effect Words");
            Assert.That(layer, Is.Not.Null);
            Assert.That(layer.childCount, Is.Zero);

            var skill = map.Require<GameButton>("skill3");
            skill.onClick.Invoke();
            Assert.That(host.TrySelectBattleTarget(actor.InstanceId), Is.True, "Submit Rose's real all-allies skill.");
            Assert.That(host.SimulationSnapshot.GetUnit(actor.InstanceId).GetStatus(BattleStatusKind.AttackUp), Is.Not.Null);
            Assert.That(layer.childCount, Is.Zero, "Effects must wait until the authored feedback cue.");

            var expected = new[] { "攻击力提升", "速攻值提升", "防御力提升" };
            var shown = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var illustrated = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            float limit = Time.realtimeSinceStartup + 15f;
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            bool captured = false;
            float peakAlpha = 0f;
            int countActive = 0, countLayerVisible = 0;
            var allNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            while (Time.realtimeSinceStartup < limit)
            {
                if (layer.gameObject.activeInHierarchy) countLayerVisible++;
                foreach (var word in layer.GetComponentsInChildren<GameText>(true))
                {
                    var root = word.transform.parent;
                    allNames.Add(word.text);
                    if (!root.gameObject.activeInHierarchy) continue;
                    countActive++;
                    float alpha = root.GetComponent<CanvasGroup>().alpha;
                    peakAlpha = Mathf.Max(peakAlpha, alpha);
                    if (alpha < .8f) continue;
                    shown.Add(word.text);
                    var icon = root.Find("Icon").GetComponent<Image>();
                    if (icon.enabled && icon.sprite) illustrated.Add(word.text);
                }
                if (!captured && shown.Count >= 2 && !string.IsNullOrWhiteSpace(output))
                {
                    Capture(host.GetComponent<Canvas>().worldCamera, output, "battle-actor-effect-words.png");
                    captured = true;
                }
                if (expected.All(shown.Contains)) break;
                yield return null;
            }
            Assert.That(expected.All(shown.Contains), Is.True,
                $"Visible words: {string.Join(", ", shown)}. Pooled rows: {layer.childCount}. " +
                $"All text: {string.Join(", ", allNames)}. Peak alpha: {peakAlpha}. Active samples: {countActive}. " +
                $"Visible layer samples: {countLayerVisible}.");
            Assert.That(illustrated, Is.EquivalentTo(new[] { "攻击力提升", "防御力提升" }));
        }

        [UnityTest]
        public IEnumerator AutoButtonLetsTheAllyUseItsHighestAvailableSkill()
        {
            scene = LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var raycaster = map.GetComponentInParent<GraphicRaycaster>();
            var eventSystem = roots.Select(root => root.GetComponent<EventSystem>()).Single(item => item);
            var actor = host.SimulationSnapshot.GetUnit(host.SimulationSnapshot.CurrentActorId);
            Assert.That(actor.DefinitionId, Is.EqualTo("c1005"));
            Click(map.Require<GameButton>("auto"), raycaster, eventSystem);
            Assert.That(host.TrySelectBattleTarget("enemy-1"), Is.False);
            yield return null;
            yield return null;
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.EqualTo(1UL));
            Assert.That(host.SimulationSnapshot.GetUnit(actor.InstanceId).GetCooldown("c1005.s3"), Is.GreaterThan(0));
            Assert.That(host.SimulationSnapshot.AllySoul, Is.EqualTo(22), "Auto earns the skill's two points without consuming Soul Burn.");
            Click(map.Require<GameButton>("auto"), raycaster, eventSystem);
        }

        [UnityTest]
        public IEnumerator SoulGaugeFillsEightNativeSlotsAndLoopsOnlyEarnedSouls()
        {
            scene = LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
            var gauge = simulation.GetType().GetField("soulGauge", PrivateInstance).GetValue(simulation);
            var present = simulation.GetType().GetMethod("Present", PrivateInstance);
            var constructor = typeof(BattleFlowEvent).GetConstructors(PrivateInstance).Single();
            var root = map.transform.Find("Soul Gauge/Soul Indicators");
            var fill = map.Require<Image>("soulFill");

            void SetSoul(int amount, BattleSide side = BattleSide.Ally) =>
                present.Invoke(simulation, new[] { constructor.Invoke(new object[] { 1UL, 1, BattleFlowEventKind.SoulChanged,
                    "ally-1", null, amount, null, null, BattleActionOrigin.Primary, null, null, side, null,
                    BattleHitKind.None, null, false, null, null }) });
            int VisibleSouls() => root.Cast<Transform>().Count(slot => slot.gameObject.activeSelf);

            Assert.That(root.childCount, Is.EqualTo(8));
            Assert.That(VisibleSouls(), Is.EqualTo(2));
            Assert.That(map.Require<GameText>("soulCount").text, Is.EqualTo("20 / 80"));
            SetSoul(79, BattleSide.Enemy);
            Assert.That(VisibleSouls(), Is.EqualTo(2), "Enemy soul changes must not repaint the player gauge.");
            SetSoul(9);
            Assert.That(VisibleSouls(), Is.Zero);
            Assert.That(fill.fillAmount, Is.EqualTo(.9f).Within(.001f));
            SetSoul(10);
            Assert.That(VisibleSouls(), Is.EqualTo(1));
            Assert.That(fill.fillAmount, Is.Zero);
            SetSoul(79);
            Assert.That(VisibleSouls(), Is.EqualTo(7));
            SetSoul(80);
            Assert.That(VisibleSouls(), Is.EqualTo(8));
            Assert.That(fill.fillAmount, Is.Zero);
            gauge.GetType().GetMethod("Tick").Invoke(gauge, new object[] { 1.25f });
            foreach (Transform slot in root)
                Assert.That(slot.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0), slot.name);
            var first = root.GetChild(0);
            var last = root.GetChild(7);
            Assert.That(last.localPosition.x - first.localPosition.x, Is.EqualTo(445.0071f).Within(.01f));

            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var burn = map.Require<GameButton>("soulBurn");
                bool previous = burn.gameObject.activeSelf;
                burn.gameObject.SetActive(false);
                try { Capture(map.GetComponentInParent<Canvas>().worldCamera, output, "battle-soul-eight.png"); }
                finally { burn.gameObject.SetActive(previous); }
            }
            SetSoul(68);
            Assert.That(VisibleSouls(), Is.EqualTo(6));
            Assert.That(fill.fillAmount, Is.EqualTo(.8f).Within(.001f));
            Assert.That(map.Require<GameText>("soulCount").text, Is.EqualTo("68 / 80"));
            host.RestartBattle();
            Assert.That(root.gameObject.activeSelf, Is.False, "Restart must detach the old effect roots immediately.");
            Assert.That(map.transform.Find("Soul Gauge/Soul Indicators").childCount, Is.EqualTo(8));
        }

        [UnityTest]
        public IEnumerator SecondSkillClickUsesAiTargetOnlyInsideHalfSecond()
        {
            scene = LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var raycaster = map.GetComponentInParent<GraphicRaycaster>();
            var eventSystem = roots.Select(root => root.GetComponent<EventSystem>()).Single(item => item);
            var skill = map.Require<GameButton>("skill1");
            var otherSkill = map.Require<GameButton>("skill2");

            Click(skill, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero, "An already highlighted basic skill still requires the first real click.");
            yield return new WaitForSecondsRealtime(.55f);
            Click(skill, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero, "An expired first click starts a fresh selection.");
            Click(otherSkill, raycaster, eventSystem);
            Click(skill, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero, "Switching skill breaks the double-click pair.");
            Click(skill, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.EqualTo(1UL));
            Assert.That(host.SimulationSnapshot.GetUnit("enemy-1").CurrentHealth,
                Is.LessThan(host.SimulationSnapshot.GetUnit("enemy-1").Stats.MaxHealth));
        }

        [UnityTest]
        public IEnumerator BurnSelectionDoubleClickCastsTheArmedSkillOnce()
        {
            scene = LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var raycaster = map.GetComponentInParent<GraphicRaycaster>();
            var eventSystem = roots.Select(root => root.GetComponent<EventSystem>()).Single(item => item);
            var burn = map.Require<GameButton>("soulBurn");
            var skill = map.Require<GameButton>("skill3"); // Mercedes is the fixed opening actor.
            var initial = host.SimulationSnapshot;
            Assert.That(initial.GetUnit(initial.CurrentActorId).DefinitionId, Is.EqualTo("c1005"));
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
            var voice = simulation.GetType().GetField("voice", PrivateInstance).GetValue(simulation);
            var burnCount = voice.GetType().GetProperty("StartedBurnReadyCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(burnCount, Is.Not.Null);
            Click(burn, raycaster, eventSystem);
#if UNITY_EDITOR_WIN
            Assert.That((int)burnCount.GetValue(voice), Is.EqualTo(1));
#endif
            Click(burn, raycaster, eventSystem); // Cancel leaves the count untouched.
#if UNITY_EDITOR_WIN
            Assert.That((int)burnCount.GetValue(voice), Is.EqualTo(1));
#endif
            Click(burn, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero);
            Click(skill, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero, "Arming Burn does not count as a skill click.");
            Click(skill, raycaster, eventSystem);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.EqualTo(1UL));
            Assert.That(host.SimulationSnapshot.AllySoul, Is.EqualTo(initial.AllySoul - 10 + 2));
#if UNITY_EDITOR_WIN
            Assert.That((int)burnCount.GetValue(voice), Is.EqualTo(2));
#endif
        }

        [UnityTest]
        public IEnumerator MeleeActorsMoveToTheTargetAndReturnToTheirSlot()
        {
            scene = LoadPlayerOpeningScene("c1001");
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var units = roots.Single(root => root.name == "Units").transform;
            var skill = preview.GetComponentInChildren<BindingMap>().Require<GameButton>("skill1");
            float readyDeadline = Time.realtimeSinceStartup + 10f;
            while ((!skill.IsInteractable() || Time.unscaledDeltaTime >= .05f) && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            Assert.That(skill.IsInteractable(), Is.True, "Ras must reach a playable turn.");

            var snapshot = preview.SimulationSnapshot;
            var actor = snapshot.GetUnit(snapshot.CurrentActorId);
            Assert.That(actor.DefinitionId, Is.EqualTo("c1001"));
            var mover = units.Find("Allies").GetChild(actor.Slot.Position - 1).GetComponentInChildren<BattlePreviewActor>();
            var home = mover.transform.localPosition;
            var slotPosition = mover.transform.parent.position;
            var target = snapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy);
            skill.onClick.Invoke();
            Assert.That(preview.TrySelectBattleTarget(target.InstanceId), Is.True, "Ras submits S1 through the battle controller.");

            float deadline = Time.realtimeSinceStartup + 4f;
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
            // Several enemies can play full S3 cinematics before the next ally turn.
            deadline = Time.realtimeSinceStartup + 45f;
            while (!map.Require<GameButton>("skill1").IsInteractable() && Time.realtimeSinceStartup < deadline)
                yield return null;
            var returned = preview.SimulationSnapshot;
            string current = returned.CurrentActorId + " / turn " + returned.TurnNumber + " / " + returned.Outcome;
            Assert.That(map.Require<GameButton>("skill1").gameObject.activeInHierarchy, Is.True, current);
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
                scene = LoadPlayerOpeningScene(wanted);
                yield return null;
                yield return null;
                var roots = scene.GetRootGameObjects();
                var preview = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
                var camera = preview.GetComponent<Canvas>().worldCamera;
                var background = roots.Single(root => root.name == "Forest2Stage");
                Vector3 originalPosition = camera.transform.position;
                float originalSize = camera.orthographicSize;
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
                var skill = map.Require<GameButton>("skill3");
                float deadline = Time.realtimeSinceStartup + 15f;
                while ((!skill.gameObject.activeInHierarchy || !skill.IsInteractable()) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(skill.gameObject.activeInHierarchy && skill.IsInteractable(), Is.True, wanted + " reaches its real playable turn.");
                var snapshot = preview.SimulationSnapshot;
                Assert.That(snapshot.GetUnit(snapshot.CurrentActorId).DefinitionId, Is.EqualTo(wanted));
                var targetSide = wanted == "c1062" ? BattleSide.Ally : BattleSide.Enemy;
                string target = snapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side == targetSide).InstanceId;
                skill.onClick.Invoke();
                Assert.That(preview.TrySelectBattleTarget(target), Is.True, wanted + " submits S3 through the battle controller.");

                var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(preview);
                var presentedSkill = simulation.GetType().GetField("presentedSkillId", PrivateInstance);
                string skillId = wanted + ".s3";
                deadline = Time.realtimeSinceStartup + 2f;
                while ((!preview.IsCinematicVisible || (string)presentedSkill.GetValue(simulation) != skillId) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(presentedSkill.GetValue(simulation), Is.EqualTo(skillId));
                Assert.That(preview.IsCinematicVisible, Is.True, wanted + " enters its source cinematic.");
                Assert.That(map.transform.Find("Skills").gameObject.activeInHierarchy, Is.False);
                bool sawCutin = false, sawStageReplacement = false;
                string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
                deadline = Time.realtimeSinceStartup + 15f;
                while (preview.IsCinematicVisible && Time.realtimeSinceStartup < deadline)
                {
                    var cutin = camera.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(item => item.name.StartsWith("CUTIN ", StringComparison.Ordinal));
                    if (cutin && cutin.sharedMaterial.mainTexture)
                    {
                        Assert.That(cutin.bounds.size.x + .001f, Is.GreaterThanOrEqualTo(camera.orthographicSize * 2 * camera.aspect));
                        Assert.That(cutin.bounds.size.y + .001f, Is.GreaterThanOrEqualTo(camera.orthographicSize * 2));
                        if (!sawCutin && !string.IsNullOrWhiteSpace(output))
                            Capture(camera, output, "battle-" + wanted + "-s3.png");
                        sawCutin = true;
                    }
                    if (wanted == "c1005" && sawCutin && !cutin && !background.activeSelf &&
                        camera.transform.Find("BGHATCH") is Transform hatch && hatch.gameObject.activeInHierarchy)
                    {
                        if (!sawStageReplacement && !string.IsNullOrWhiteSpace(output))
                            Capture(camera, output, "battle-c1005-s3-resumed-stage.png");
                        sawStageReplacement = true;
                    }
                    yield return null;
                }
                Assert.That(preview.IsCinematicVisible, Is.False, "The cinematic ends without blocking the next turn.");
                Assert.That(sawCutin, Is.True, wanted + " renders its original CUTIN frames.");
                if (wanted == "c1005")
                    Assert.That(sawStageReplacement, Is.True, "Mercedes resumes the original skill stage after the CUTIN.");
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

        [UnityTest]
        public IEnumerator StunStarsFollowTheCharacterAndStopWhenTheStatusClears()
        {
            scene = LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
            var views = (IDictionary)simulation.GetType().GetField("views", PrivateInstance).GetValue(simulation);
            var view = views["ally-1"];
            var type = view.GetType();
            var actor = (BattlePreviewActor)type.GetProperty("Actor").GetValue(view);
            var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", PrivateInstance).GetValue(actor);
            var status = (BattleStatusSnapshot)typeof(BattleStatusSnapshot)
                .GetConstructor(PrivateInstance, null, new[] { typeof(BattleStatusKind), typeof(int), typeof(int), typeof(string), typeof(ulong) }, null)
                .Invoke(new object[] { BattleStatusKind.Stun, 1, 0, "enemy-1", 7UL });
            void Apply() => type.GetMethod("ApplyStatus").Invoke(view, new object[] { status });
            void Remove() => type.GetMethod("RemoveStatusInstance").Invoke(view, new object[] { status });

            const string effectName = "Battle stun stars";
            Assert.That(body.transform.Find(effectName), Is.Null, "Unstunned units must not allocate stars.");
            Apply();
            yield return null;
            var stars = body.transform.Find(effectName);
            Assert.That(stars, Is.Not.Null, "Stun must play the native star loop under the character skeleton.");
            Assert.That(stars.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(12));
            Assert.That(stars.GetComponent<MeshRenderer>().sortingOrder, Is.GreaterThan(body.sortingOrder));
            var getAnchor = typeof(BattlePreviewActor).GetMethod("TryGetPresentationAnchor", PrivateInstance);
            Vector3 Top()
            {
                var args = new object[] { "top", Vector3.zero };
                Assert.That(getAnchor.Invoke(actor, args), Is.True, "The native top bone must be available.");
                return (Vector3)args[1];
            }
            Assert.That(Vector3.Distance(stars.position, Top()), Is.LessThan(.002f));
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Single(item => item.name == "Battle Camera");
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                yield return new WaitForSecondsRealtime(.22f);
                Capture(camera, output, "battle-stun-stars.png");
            }

            Vector3 before = stars.position;
            actor.transform.position += Vector3.right * .5f;
            yield return null;
            Assert.That(stars.position.x, Is.EqualTo(before.x + .5f).Within(.16f));
            body.forceRenderingOff = true;
            yield return null;
            Assert.That(stars.gameObject.activeSelf, Is.False, "Skill layouts must hide the stars with the actor.");
            body.forceRenderingOff = false;
            yield return null;
            Assert.That(stars.gameObject.activeSelf, Is.True);
            Remove();
            yield return null;
            Assert.That(stars.gameObject.activeSelf, Is.False, "Removing the status must hide residual stars.");
            Apply();
            yield return null;
            Assert.That(body.transform.Find(effectName), Is.SameAs(stars), "Reapplying stun should reuse the effect object.");
            Assert.That(stars.gameObject.activeSelf, Is.True);

            type.GetMethod("BeginDeath").Invoke(view, null);
            Assert.That(stars.gameObject.activeSelf, Is.False);
            // Revive before the defeat animation disables the actor object.
            Remove();
            type.GetMethod("Show").Invoke(view, new object[] { true });
            yield return null;
            var player = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                .GetField("player", PrivateInstance).GetValue(actor);
            Assert.That(player.CurrentAnimation, Is.Not.EqualTo("groggy"), "Revival must not retain the old stun pose.");
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
