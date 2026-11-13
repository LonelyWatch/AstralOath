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

        private static void ConfigureHudScene(BindingMap hud)
        {
            BattleTurnHudAssets.Import();
            var canvas = hud.GetComponentInParent<Canvas>();
            if (!canvas.GetComponent<GraphicRaycaster>())
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (!canvas.TryGetComponent(out BattleHudPreview preview))
                preview = canvas.gameObject.AddComponent<BattleHudPreview>();

            var properties = new SerializedObject(preview);
            SetObject("hud", hud);
            SetObject("unitPresentation", BattleUnitPresentationAssets.Load());
            SetObject("battleCatalog", Asset<TextAsset>(Module + "/Configs/battle-catalog.json"));
            SetObject("battleDamageFont", BuildDamageDigits());
            SetObject("impactBank", Asset<TextAsset>(Module + "/Res/Sounds/gerneal.bank.bytes"));
            SetObject("stageBackground", canvas.gameObject.scene.GetRootGameObjects()
                .Single(root => root.name == "Forest2Stage" || root.name == "Forest3Stage"));
            var heroes = new[] { (Id: "c1001", Stem: "ras"), (Id: "c1005", Stem: "mercedes"), (Id: "c1062", Stem: "angelica") };
            var graphs = heroes.SelectMany(hero => Enumerable.Range(1, 3).Select(slot => Asset<TextAsset>(
                $"Assets/Game/Modules/CharacterPresentation/Res/Characters/{hero.Id}/Configs/stagept/c_{hero.Stem}_skill_0{slot}.graph.json"))).ToArray();
            SetArray("battleGraphs", graphs);
            BattleSkillPresentationAssets.Collect(properties.FindProperty("skillAssets"), graphs);
            const string sounds = "Assets/Game/Modules/CharacterPresentation/Res/Shared/Sounds/sound/";
            SetObject("audioCatalog", Asset<TextAsset>("Assets/Game/Modules/CharacterLab/Configs/catalog.json"));
            SetArray("audioBanks", new[] { "master.bank", "master.strings.bank", "sfx1.bank", "voc.event.bank",
                "ja/voc1.audio_ja.bank" }.Select(name => Asset<TextAsset>(sounds + name + ".bytes")).ToArray());
            SetArray("battleSprites", heroes.SelectMany(hero => Enumerable.Range(1, 3).Select(slot =>
                Asset<Sprite>(HudTextures + $"Skills/sk_{hero.Id}_{slot}.png")))
                .Concat(heroes.SelectMany(hero => new[] { Asset<Sprite>(HudTextures + $"Faces/{hero.Id}_su.png"),
                    Asset<Sprite>(HudTextures + $"Faces/{hero.Id}_s.png") }))
                .Concat(new[] { "cm_icon_promfire", "cm_icon_promice", "cm_icon_role_knight", "cm_icon_role_mage",
                    "cm_icon_role_manauser", "_hero_s_bg_ally", "_hero_s_bg_enemy", "_hero_s_frame_ally",
                    "_hero_s_frame_enemy", "cm_hero_circool1", "game_hud_arrow", "Faces/pet_slime1_n_grade1_1_s" }
                    .Select(name => Asset<Sprite>(HudTextures + name + ".png")))
                .Concat(BattleTurnHudAssets.Sprites()).Concat(BattleDamageFeedbackAssets.Sprites()).ToArray());
            SetObject("burnPressed", Asset<Sprite>(HudTextures + "game_hud_btn_burn_p.png"));
            SetObject("cancelNormal", Asset<Sprite>(HudEffects + "game_hud_btn_burnc_d.png"));
            SetObject("cancelPressed", Asset<Sprite>(HudEffects + "game_hud_btn_burnc_p.png"));
            SetSpine("burnButton", "soul_burn_bt");
            SetSpine("skillSelection", "hud_icon_select");
            SetSpine("burnIntro", "hud_icon_burneff");
            SetSpine("soulUse", "ui_soul_burn_use");
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
