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
            foreach (string key in new[] { "soulBurn", "skill1", "skill2", "skill3" })
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
            var canvas = hud.GetComponentInParent<Canvas>();
            if (!canvas.GetComponent<GraphicRaycaster>())
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            if (!canvas.TryGetComponent(out BattleHudPreview preview))
                preview = canvas.gameObject.AddComponent<BattleHudPreview>();

            var properties = new SerializedObject(preview);
            SetObject("hud", hud);
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
