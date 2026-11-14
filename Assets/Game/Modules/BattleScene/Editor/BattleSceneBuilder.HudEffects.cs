using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        private const string HudEffects = Module + "/Res/Effects/Hud/";

        // Run in an isolated editor project to preserve existing layout and prefab references.
        public static void InstallHudEffects()
        {
            ImportHudEffectTextures();
            ImportSprites(HudTextures + "Faces");
            string path = Prefabs + "BattleHud.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureHudButtons(prefab.GetComponent<BindingMap>());
                if (!PrefabUtility.SaveAsPrefabAsset(prefab, path))
                    throw new IOException("Unable to save " + path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }

            var scene = EditorSceneManager.OpenScene(ScenePath);
            var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>(true))
                .Single(map => map.gameObject.name == "BattleHud");
            ConfigureHudScene(hud);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Unable to save " + ScenePath);
            AssetDatabase.SaveAssets();
            ValidateHud();
            Debug.Log("ASTRAL_BATTLE_HUD_EFFECTS_READY");
        }

        [MenuItem("Tools/战斗场景/更新角色资源引用")]
        public static void InstallHeroRoster()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportSprites(HudTextures + "Faces");
            ImportSprites(HudTextures + "Skills");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var hud = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>(true))
                .Single(map => map.gameObject.name == "BattleHud");
            ConfigureHudScene(hud, rosterOnly: true);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unable to save " + ScenePath);
            Debug.Log("ASTRAL_BATTLE_ROSTER_READY");
        }

        private static void ConfigureHudButtons(BindingMap map)
        {
            var active = map.Require<Image>("activePortrait").transform.parent;
            var readiness = map.Require<Image>("orderHourglass").transform.parent;
            if (active.GetSiblingIndex() > readiness.GetSiblingIndex())
                active.SetSiblingIndex(readiness.GetSiblingIndex());
            foreach (string key in new[] { "pause", "soulBurn", "skill1", "skill2", "skill3" })
            {
                var button = map.Require<GameButton>(key);
                button.targetGraphic.raycastTarget = true;
                button.interactable = true;
            }

            var burn = map.Require<GameButton>("soulBurn");
            burn.transition = Selectable.Transition.SpriteSwap;
            var sprites = burn.spriteState;
            sprites.pressedSprite = Asset<Sprite>(HudTextures + "game_hud_btn_burn_p.png");
            burn.spriteState = sprites;
            ConfigureSkillSelectionArtwork(map);
            ConfigurePreviewEntry(map);
        }

        private static void ConfigurePreviewEntry(BindingMap map)
        {
            const string key = "startPreview";
            var serialized = new SerializedObject(map);
            var entries = serialized.FindProperty("entries");
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue == key)
                    return;

            var background = Picture((RectTransform)map.transform, "Start Preview", "_btn_small_black",
                1190f, 621f, 148f, 48f);
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 1f / PixelScale;
            background.raycastTarget = true;
            var button = background.gameObject.AddComponent<GameButton>();
            button.targetGraphic = background;

            var label = Text(background.rectTransform, "Caption", "开始试玩", 0f, 0f, 142f, 40f, 21f,
                fontOverride: map.Require<GameText>("wave").font);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            background.gameObject.SetActive(false);

            var entry = entries.GetArrayElementAtIndex(entries.arraySize++);
            entry.FindPropertyRelative("key").stringValue = key;
            entry.FindPropertyRelative("component").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSkillSelectionArtwork(BindingMap map)
        {
            var serialized = new SerializedObject(map);
            var entries = serialized.FindProperty("entries");
            for (int slot = 1; slot <= 3; slot++)
            {
                string key = "skillSelected" + slot;
                SerializedProperty entry = null;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var candidate = entries.GetArrayElementAtIndex(i);
                    if (candidate.FindPropertyRelative("key").stringValue == key)
                    {
                        entry = candidate;
                        break;
                    }
                }

                var artwork = entry?.FindPropertyRelative("component").objectReferenceValue as Image;
                if (!artwork)
                {
                    var icon = (RectTransform)map.Require<GameButton>("skill" + slot).transform;
                    // hud_skill.csb draws the 114×120 selected layer 3 px left and 4 px above the icon.
                    Vector2 center = icon.anchoredPosition / PixelScale + new Vector2(-3f, 4f);
                    artwork = Picture((RectTransform)icon.parent, $"Skill {slot} Selected", "cm_select_skill",
                        center.x, center.y, 114f, 120f);
                    if (entry == null)
                    {
                        int index = entries.arraySize++;
                        entry = entries.GetArrayElementAtIndex(index);
                    }
                    entry.FindPropertyRelative("key").stringValue = key;
                    entry.FindPropertyRelative("component").objectReferenceValue = artwork;
                }
                artwork.gameObject.SetActive(false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureHudScene(BindingMap hud, bool rosterOnly = false)
        {
            if (!rosterOnly) BattleTurnHudAssets.Import();
            var canvas = hud.GetComponentInParent<Canvas>();
            if (!canvas.GetComponent<GraphicRaycaster>())
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (!canvas.TryGetComponent(out BattleHudPreview preview))
                preview = canvas.gameObject.AddComponent<BattleHudPreview>();

            var properties = new SerializedObject(preview);
            var catalog = Asset<TextAsset>(Module + "/Configs/battle-catalog.json");
            var audio = Asset<TextAsset>("Assets/Game/Modules/CharacterLab/Configs/catalog.json");
            var battle = JObject.Parse(catalog.text);
            var lab = JObject.Parse(audio.text);
            var paths = BattleSkillPresentationAssets.AddressMap();
            var characters = lab["characters"].ToDictionary(row => (string)row["id"], StringComparer.Ordinal);
            var heroes = battle["heroes"].OrderBy(row => (string)row["id"], StringComparer.Ordinal).ToArray();
            var heroIds = heroes.Select(row => (string)row["id"]).ToHashSet(StringComparer.Ordinal);
            var rows = properties.FindProperty("heroModels");
            var models = new Dictionary<string, SerializedProperty>(StringComparer.Ordinal);
            rows.arraySize = heroes.Length;
            for (int i = 0; i < heroes.Length; i++)
            {
                string id = (string)heroes[i]["id"];
                var character = characters[id];
                var row = rows.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("id").stringValue = id;
                var label = id switch
                {
                    "c1001" => "Ras",
                    "c1062" => "Angelica",
                    "c1005" => "Mercedes",
                    _ => (string)character["name"]
                };
                row.FindPropertyRelative("label").stringValue = label;
                row.FindPropertyRelative("skin").stringValue = (string)character["battleSkin"];
                BattleSkillPresentationAssets.SetSkeleton(row.FindPropertyRelative("skeleton"), (string)character["battleSkeletonAddress"], paths);
                models.Add(id, row);
            }
            if (!rosterOnly) ConfigureAuthoredTeams(hud, properties, GetBattleActorPrefab(), models);
            var graphs = characters.Values.Where(row => heroIds.Contains((string)row["id"]))
                .SelectMany(row => row["skills"]).Select(row => (string)row["graphAddress"])
                .Distinct(StringComparer.Ordinal).OrderBy(address => address, StringComparer.Ordinal)
                .Select(address => BattleSkillPresentationAssets.Load<TextAsset>(address, paths)).ToArray();
            var graphIds = graphs.Select(graph => (string)JObject.Parse(graph.text)["id"]).ToHashSet(StringComparer.Ordinal);
            var skills = battle["skills"].Where(row => heroIds.Contains((string)row["heroId"])).ToArray();
            foreach (var skill in skills)
            {
                if ((string)skill["target"] != "passive" && !graphIds.Contains((string)skill["presentationId"]))
                    throw new InvalidOperationException("Missing original battle graph for " + skill["id"]);
                string alternate = (string)skill["speedUpPresentationId"];
                if (!string.IsNullOrWhiteSpace(alternate) && !graphIds.Contains(alternate))
                    throw new InvalidOperationException("Missing alternate battle graph for " + skill["id"]);
            }

            SetObject("battleCatalog", catalog);
            SetObject("audioCatalog", audio);
            SetObject("impactBank", null); // Current catalog includes the shared impact/effect bank.
            SetArray("audioBanks", lab["sharedBanks"].Values<string>()
                .Select(address => BattleSkillPresentationAssets.Load<TextAsset>(address, paths)).ToArray());
            SetArray("battleGraphs", graphs);
            SetObject("skillTimelineOverrides", Asset<TextAsset>(SkillTimelineOverrides.AssetPath));
            BattleSkillPresentationAssets.Collect(properties.FindProperty("skillAssets"), graphs);
            var classIcons = heroes.Select(row => (string)row["heroClass"]).Distinct(StringComparer.Ordinal)
                .Select(role => "cm_icon_role_" + (role == "soul_weaver" ? "manauser" : role == "thief" ? "assassin" : role));
            var elementIcons = heroes.Select(row => "cm_icon_prom" + (string)row["element"]).Distinct(StringComparer.Ordinal);
            var markerElements = new[] { "fire", "ice", "wind", "light", "dark" }
                .Select(element => "game_hud_bar_" + element);
            SetArray("battleSprites", skills.SelectMany(row => new[] { (string)row["iconAsset"], (string)row["speedUpIconAsset"] })
                .Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.Ordinal)
                .Select(path => Asset<Sprite>(HudTextures + "Skills/" + Path.GetFileName(path)))
                .Concat(heroes.SelectMany(row => new[] { Asset<Sprite>(HudTextures + $"Faces/{row["id"]}_su.png"),
                    Asset<Sprite>(HudTextures + $"Faces/{row["id"]}_s.png") }))
                .Concat(classIcons.Concat(elementIcons).Concat(markerElements).Concat(new[] { "_hero_s_bg_ally", "_hero_s_bg_enemy", "_hero_s_frame_ally",
                    "_hero_s_frame_enemy", "cm_hero_circool1", "game_hud_arrow", "Faces/pet_slime1_n_grade1_1_s" })
                    .Distinct(StringComparer.Ordinal).Select(name => Asset<Sprite>(HudTextures + name + ".png")))
                .Concat(BattleTurnHudAssets.Sprites()).Concat(BattleDamageFeedbackAssets.Sprites()).Distinct().ToArray());
            if (rosterOnly)
            {
                properties.ApplyModifiedPropertiesWithoutUndo();
                return;
            }

            SetObject("hud", hud);
            SetObject("inspectionPrefab", Asset<GameObject>("Assets/Game/Common/Res/Prefab/InfoPanel.prefab"));
            SetObject("confirmPrefab", Asset<GameObject>("Assets/Game/Common/Res/Prefab/ConfirmButton.prefab"));
            SetObject("battleDamageFont", BuildDamageDigits());
            SetObject("stageBackground", canvas.gameObject.scene.GetRootGameObjects()
                .Single(root => root.name == "Forest2Stage" || root.name == "Forest3Stage"));
            SetObject("burnPressed", Asset<Sprite>(HudTextures + "game_hud_btn_burn_p.png"));
            SetObject("cancelNormal", Asset<Sprite>(HudEffects + "game_hud_btn_burnc_d.png"));
            SetObject("cancelPressed", Asset<Sprite>(HudEffects + "game_hud_btn_burnc_p.png"));
            SetSpine("burnButton", "soul_burn_bt");
            SetSpine("skillSelection", "hud_icon_select");
            SetSpine("burnIntro", "hud_icon_burneff");
            SetSpine("soulUse", "ui_soul_burn_use");
            SetSpine("soulDisplay", "soul_01");
            SetSpine("soulConsume", "soul_grow");
            const string sharedDustAtlas = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1003/";
            var dust = properties.FindProperty("readyDust");
            dust.FindPropertyRelative("skeleton").objectReferenceValue = Asset<TextAsset>(
                "Assets/Game/Modules/BattleScene/Res/Effects/TurnEntry/dust.spine.json");
            dust.FindPropertyRelative("atlas").objectReferenceValue = Asset<TextAsset>(
                sharedDustAtlas + "Atlas/effect/effect.atlas.txt");
            dust.FindPropertyRelative("page").objectReferenceValue = Asset<Texture2D>(
                sharedDustAtlas + "Textures/effect/effect.png");
            SetArray("particles", Directory.GetFiles(HudEffects + "Particles", "*.particle.json")
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(path => Asset<TextAsset>(path.Replace('\\', '/'))).ToArray());
            SetArray("particleTextures", Directory.GetFiles(HudEffects + "Particles", "*.png")
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(path => Asset<Texture2D>(path.Replace('\\', '/'))).ToArray());
            properties.ApplyModifiedPropertiesWithoutUndo();

            var scene = canvas.gameObject.scene;
            if (!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<EventSystem>(true)))
                _ = new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));

            void SetObject(string field, Object value) => properties.FindProperty(field).objectReferenceValue = value;

            void SetSpine(string field, string name)
            {
                var property = properties.FindProperty(field);
                property.FindPropertyRelative("skeleton").objectReferenceValue = Asset<TextAsset>(HudEffects + name + ".spine.json");
                property.FindPropertyRelative("atlas").objectReferenceValue = Asset<TextAsset>(HudEffects + name + ".atlas.txt");
                property.FindPropertyRelative("page").objectReferenceValue = Asset<Texture2D>(HudEffects + name + ".png");
            }

            void SetArray(string field, Object[] assets)
            {
                var property = properties.FindProperty(field);
                property.arraySize = assets.Length;
                for (int i = 0; i < assets.Length; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = assets[i];
            }
        }

        private static void ConfigureAuthoredTeams(BindingMap hud, SerializedObject properties, GameObject prefab,
            IReadOnlyDictionary<string, SerializedProperty> models)
        {
            var allies = properties.FindProperty("allyHeroIds");
            var enemies = properties.FindProperty("enemyHeroIds");
            var oldDefaults = new[] { "c1001", "c1062", "c1005", "c1001" };
            if (enemies.arraySize == 0 && allies.arraySize == oldDefaults.Length &&
                Enumerable.Range(0, oldDefaults.Length).All(i => allies.GetArrayElementAtIndex(i).stringValue == oldDefaults[i]))
                allies.arraySize = 0;

            var units = hud.gameObject.scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            ConfigureTeam("Allies", allies, BattleHudPreview.DefaultAllyHeroIds, false);
            ConfigureTeam("Enemies", enemies, BattleHudPreview.DefaultEnemyHeroIds, true);
            allies.arraySize = 0;
            enemies.arraySize = 0;

            void ConfigureTeam(string name, SerializedProperty overrides, IReadOnlyList<string> defaults, bool enemy)
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    var slot = units.Find(name).GetChild(i);
                    var currentActor = slot.GetComponentInChildren<BattlePreviewActor>(true);
                    var currentId = currentActor
                        ? new SerializedObject(currentActor).FindProperty("heroId").stringValue
                        : null;
                    string requested = i < overrides.arraySize ? overrides.GetArrayElementAtIndex(i).stringValue : null;
                    string id = !string.IsNullOrEmpty(requested) ? requested :
                        string.IsNullOrEmpty(currentId) ? defaults[i] : currentId;
                    if (!models.TryGetValue(id, out var model))
                        throw new InvalidOperationException("Unknown battle hero ID: " + id);
                    PlaceHero(slot, prefab, id, model.FindPropertyRelative("label").stringValue, model, enemy, i);
                }
            }
        }

        private static void ImportHudEffectTextures()
        {
            foreach (string path in Directory.GetFiles(HudEffects, "*.png", SearchOption.AllDirectories))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                bool isButtonSprite = Path.GetFileName(path).StartsWith("game_hud_btn_burnc_", System.StringComparison.Ordinal);
                if (importer.userData == "AstralOath.ImportOverride")
                    continue;
                importer.userData = "AstralOath.ImportOverride";
                importer.textureType = isButtonSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                if (isButtonSprite)
                    importer.spriteImportMode = SpriteImportMode.Single;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = isButtonSprite;
                importer.isReadable = false;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                foreach (string platform in new[] { "Standalone", "Android" })
                {
                    var settings = importer.GetPlatformTextureSettings(platform);
                    settings.overridden = true;
                    settings.format = TextureImporterFormat.RGBA32;
                    settings.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SetPlatformTextureSettings(settings);
                }
                importer.SaveAndReimport();
            }
        }
    }
}
