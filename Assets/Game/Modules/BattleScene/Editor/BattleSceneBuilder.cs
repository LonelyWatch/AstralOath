using System;
using System.IO;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        private const string Module = "Assets/Game/Modules/BattleScene";
        private const string HudTextures = Module + "/Res/Textures/Hud/";
        private const string BackgroundTextures = Module + "/Res/Textures/Backgrounds/";
        private const string Prefabs = Module + "/Res/Prefab/";
        public const string ScenePath = Module + "/Scenes/BattleScene.unity";
        private const float PixelScale = 1.5f;
        private const float WorldScale = 0.015f;
        private static TMP_FontAsset font;

        [MenuItem("Tools/战斗场景/打开场景")]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/战斗场景/验证 HUD 绑定")]
        public static void ValidateHud()
        {
            UIViewRegistry.ValidatePrefab<BattleHud>(Asset<GameObject>(Prefabs + "BattleHud.prefab"));
            Debug.Log("ASTRAL_BATTLE_HUD_VALID");
        }

        public static void Generate()
        {
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();
            ImportSprites(HudTextures);
            ImportSprites(BackgroundTextures);
            ImportSlimePage();

            font = Asset<TMP_FontAsset>("Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var frameCamera = new GameObject("Frame Camera", typeof(Camera)).GetComponent<Camera>();
            frameCamera.depth = -10;
            frameCamera.cullingMask = 0;
            frameCamera.clearFlags = CameraClearFlags.SolidColor;
            frameCamera.backgroundColor = new Color32(18, 22, 25, 255);
            frameCamera.allowHDR = false;
            frameCamera.allowMSAA = false;

            var camera = new GameObject("Battle Camera", typeof(Camera)).GetComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(143, 180, 192, 255);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.gameObject.AddComponent<BattlePreviewViewport>();

            var daytime = SavePrefab(CreateForest("Forest2", "forest2"), "Forest2Stage");
            SavePrefab(CreateForest("Forest3", "forest3"), "Forest3Stage");
            PrefabUtility.InstantiatePrefab(daytime, scene);

            var units = new GameObject("Units").transform;
            Hero(units, "Mercedes", "c1005", "mercedes", 217, 282, 10, 0.58f);
            Hero(units, "Angelica", "c1062", "angelica", 260, 205, 20, 0.59f);
            Hero(units, "Ras", "c1001", "ras", 424, 234, 16, 0.64f);
            Slime(units, "Wind Slime", "wind", 864, 279, 11);
            Slime(units, "Fire Slime", "fire", 999, 208, 19);

            var canvas = new GameObject("Battle Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var screenCanvas = canvas.GetComponent<Canvas>();
            screenCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            screenCanvas.worldCamera = camera;
            screenCanvas.planeDistance = 2f;
            screenCanvas.sortingOrder = 100;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var hud = SavePrefab(CreateHud(), "BattleHud");
            PrefabUtility.InstantiatePrefab(hud, canvas.transform);
            ValidateHud();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unable to save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("ASTRAL_BATTLE_SCENE_READY");
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            string path = Prefabs + name + ".prefab";
            var result = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (!result)
                throw new IOException("Unable to save " + path);
            return result;
        }

        private static void ImportSprites(string folder)
        {
            foreach (string path in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single &&
                    importer.npotScale == TextureImporterNPOTScale.None && !importer.mipmapEnabled && importer.alphaIsTransparency)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }

        private static void ImportSlimePage()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Module + "/Res/Units/Slime/slime.png");
            if (importer.userData == "AstralOath.ImportOverride")
                return;
            importer.userData = "AstralOath.ImportOverride";
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
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

        private static T Asset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset)
                throw new FileNotFoundException("Missing required battle scene asset.", path);
            return asset;
        }
    }
}
