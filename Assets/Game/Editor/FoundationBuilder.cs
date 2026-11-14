using System;
using System.IO;
using System.Linq;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.FoundationDemo;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using YooAsset;
using YooAsset.Editor;

namespace AstralOath.Editor
{
    public static class FoundationBuilder
    {
        private const string Module = "Assets/Game/Modules/FoundationDemo";
        internal const string PrefabPath = Module + "/Res/Prefab/FoundationWindow.prefab";
        private const string FontPath = Module + "/Res/Fonts/FoundationFont.asset";
        internal const string ScenePath = Module + "/Scenes/FoundationDemo.unity";

        [MenuItem("AstralOath/Foundation/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            if (Shader.Find("TextMeshPro/Distance Field"))
                return;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro");
            string path = Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(path, false);
        }

        [MenuItem("AstralOath/Foundation/Import Generated Demo Config")]
        public static void ImportGeneratedDemoConfig()
        {
            const string argument = "-astral-generated-config";
            string root = Environment.GetCommandLineArgs().Contains(argument)
                ? RequiredArgument(argument) : ".artifacts/config/json";
            var ui = File.ReadAllBytes(Path.Combine(root, "demo_ui.json"));
            var locale = File.ReadAllBytes(Path.Combine(root, "demo_localizationkeys.json"));
            const string uiPath = Module + "/Configs/demo-config.json";
            const string localePath = Module + "/Configs/demo-localization.json";
            File.WriteAllBytes(uiPath, ui);
            File.WriteAllBytes(localePath, locale);
            AssetDatabase.ImportAsset(uiPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(localePath, ImportAssetOptions.ForceSynchronousImport);
        }

        [MenuItem("AstralOath/Foundation/Validate Demo")]
        public static void ValidateDemo()
        {
            var prefab = RequireAsset<GameObject>(PrefabPath);
            UIViewRegistry.ValidatePrefab<FoundationWindow>(prefab);
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(component => !component))
                throw new BuildFailedException("Foundation window prefab contains a missing script. Run CreateDemo.");
            var font = RequireAsset<TMP_FontAsset>(FontPath);
            ValidateFonts(prefab.GetComponentsInChildren<TextMeshProUGUI>(true), font);
            _ = RequireAsset<SceneAsset>(ScenePath);
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                ValidateFonts(roots.SelectMany(go => go.GetComponentsInChildren<TextMeshProUGUI>(true)), font);
                if (!roots.Any(go => go.TryGetComponent<Camera>(out var camera) && camera.isActiveAndEnabled))
                    throw new BuildFailedException("Foundation demo requires an enabled scene camera. Run CreateDemo.");
                var bootstrap = roots.Select(go => go.GetComponent<FoundationBootstrap>()).SingleOrDefault(component => component);
                if (!bootstrap || !new SerializedObject(bootstrap).FindProperty("viewRoot").objectReferenceValue)
                    throw new BuildFailedException("Foundation demo requires a window parent on its Bootstrap. Run CreateDemo.");
            }
            finally
            {
                if (openedHere)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
            var collected = AssetBundleCollectorSettingData.Setting.BeginCollect(YooAssetService.PackageName,
                simulateBuild: true, useAssetDependencyDB: false);
            if (!collected.CollectAssets.Any(asset => asset.Address == FoundationWindow.PrefabAddress &&
                    asset.AssetInfo.AssetPath == PrefabPath))
                throw new BuildFailedException($"Foundation package does not collect '{FoundationWindow.PrefabAddress}'. Run CreateDemo.");
            GameAssetImportRules.ValidateTextureDefaults();
        }

        [MenuItem("AstralOath/Foundation/Create Demo Scene")]
        public static void CreateDemo()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EnsureFolders();
            TMP_FontAsset font = GetFont();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(17, 24, 39, 255);
            camera.cullingMask = 0;
            var canvas = new GameObject("Foundation Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var background = Panel("Background", canvas.transform, new Color32(17, 24, 39, 255));
            Stretch(background);
            var safe = Rect("Safe Area", canvas.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaLayout>();

            var open = ButtonControl("Open", safe, font, new Vector2(0, 330), new Vector2(250, 62), out var openLabel);
            var startup = Label("Startup Status", safe, font, new Vector2(0, 246), new Vector2(780, 54), 24);
            var window = CreateWindow(safe, font);
            if (!PrefabUtility.SaveAsPrefabAsset(window, PrefabPath))
                throw new InvalidOperationException("Failed to save Foundation window.");
            UnityEngine.Object.DestroyImmediate(window);
            ConfigureCollectors();

            _ = new GameObject("Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            var services = new GameObject("Foundation Bootstrap", typeof(AudioSource), typeof(FoundationBootstrap));
            var audio = services.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            var bootstrap = new SerializedObject(services.GetComponent<FoundationBootstrap>());
            Set(bootstrap, "viewRoot", safe);
            Set(bootstrap, "open", open);
            Set(bootstrap, "openLabel", openLabel);
            Set(bootstrap, "startupStatus", startup);
            Set(bootstrap, "audioOutput", audio);
            bootstrap.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Failed to save Foundation demo.");
            AssetDatabase.SaveAssets();
            ValidateDemo();
        }

        // Run in the isolated build project. The public key and endpoints become build inputs.
        public static void ConfigureContentUpdate()
        {
            string manifest = RequiredArgument("-astral-content-manifest");
            string signature = RequiredArgument("-astral-content-signature");
            string key = RequiredArgument("-astral-content-public-key");
            const string keyAsset = Module + "/Configs/FoundationContentPublicKey.json";
            File.Copy(key, keyAsset, overwrite: true);
            AssetDatabase.ImportAsset(keyAsset, ImportAssetOptions.ForceSynchronousImport);

            var scene = EditorSceneManager.OpenScene(ArenaUiBuilder.StartupScene);
            var component = scene.GetRootGameObjects().Select(go => go.GetComponent<FoundationBootstrap>())
                .Single(item => item);
            var bootstrap = new SerializedObject(component);
            bootstrap.FindProperty("manifestUrl").stringValue = manifest;
            bootstrap.FindProperty("signatureUrl").stringValue = signature;
            Set(bootstrap, "contentPublicKey", RequireAsset<TextAsset>(keyAsset));
            bootstrap.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Failed to save Foundation content settings.");
        }

        private static string RequiredArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Missing argument " + name);
            return args[index + 1];
        }

        private static GameObject CreateWindow(Transform parent, TMP_FontAsset font)
        {
            var panel = Panel("Foundation Window", parent, new Color32(32, 46, 68, 255));
            Size(panel, new Vector2(0, -44), new Vector2(820, 416));
            var map = panel.gameObject.AddComponent<BindingMap>();
            var title = Label("Title", panel, font, new Vector2(0, 136), new Vector2(740, 64), 44);
            var description = Label("Description", panel, font, new Vector2(0, 65), new Vector2(740, 56), 28);
            var status = Label("Status", panel, font, new Vector2(0, -23), new Vector2(740, 86), 25);
            var echo = ButtonControl("Echo", panel, font, new Vector2(-132, -133), new Vector2(230, 60), out var echoLabel);
            var close = ButtonControl("Close", panel, font, new Vector2(132, -133), new Vector2(230, 60), out var closeLabel);

            var bindings = new SerializedObject(map);
            var entries = bindings.FindProperty("entries");
            (string key, Component component)[] values =
            {
                ("title", title), ("description", description), ("status", status),
                ("echo", echo), ("echoLabel", echoLabel), ("close", close), ("closeLabel", closeLabel)
            };
            entries.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("key").stringValue = values[i].key;
                entry.FindPropertyRelative("component").objectReferenceValue = values[i].component;
            }
            bindings.ApplyModifiedPropertiesWithoutUndo();
            UIViewRegistry.ValidatePrefab<FoundationWindow>(panel.gameObject);
            panel.gameObject.SetActive(false);
            return panel.gameObject;
        }

        private static GameButton ButtonControl(string name, Transform parent, TMP_FontAsset font,
            Vector2 position, Vector2 size, out GameText label)
        {
            var rect = Panel(name, parent, new Color32(39, 93, 139, 255));
            Size(rect, position, size);
            var button = rect.gameObject.AddComponent<GameButton>();
            button.targetGraphic = rect.GetComponent<Image>();
            label = Label("Label", rect, font, Vector2.zero, size, 26);
            return button;
        }

        private static GameText Label(string name, Transform parent, TMP_FontAsset font,
            Vector2 position, Vector2 size, float pointSize)
        {
            var rect = Rect(name, parent);
            Size(rect, position, size);
            var label = rect.gameObject.AddComponent<GameText>();
            label.fontSize = pointSize;
            label.color = new Color32(233, 242, 255, 255);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.richText = false;
            var control = label;

            // TMP sets its default asset and material during component initialization.
            // Apply the persistent asset pair after adding the label and its binding.
            var serialized = new SerializedObject(label);
            Set(serialized, "m_fontAsset", font);
            Set(serialized, "m_sharedMaterial", font.material);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return control;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var child = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static RectTransform Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        private static void Size(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TMP_FontAsset GetFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing)
                return existing;
            if (!Shader.Find("TextMeshPro/Distance Field"))
                throw new InvalidOperationException("Run ImportTmpEssentials before CreateDemo.");
            var font = RequireAsset<Font>("Assets/ThirdParty/Fonts/NotoSansSC.ttf");
            var asset = TMP_FontAsset.CreateFontAsset(font, 60, 6, GlyphRenderMode.SDFAA, 1024, 1024)
                ?? throw new InvalidOperationException("Could not initialize the Noto Sans SC font.");
            asset.name = "FoundationFont";
            // The dynamic atlas adds glyphs from active package text when labels bind.
            if (!asset.TryAddCharacters("AstralOath0123456789", out string missing))
                throw new InvalidOperationException("Missing demo font glyphs: " + missing);
            AssetDatabase.CreateAsset(asset, FontPath);
            foreach (var atlas in asset.atlasTextures.Where(texture => texture))
                AssetDatabase.AddObjectToAsset(atlas, asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontPath, ImportAssetOptions.ForceSynchronousImport);
            return RequireAsset<TMP_FontAsset>(FontPath);
        }

        private static void ValidateFonts(System.Collections.Generic.IEnumerable<TextMeshProUGUI> labels, TMP_FontAsset font)
        {
            foreach (var label in labels)
            {
                var serialized = new SerializedObject(label);
                if (serialized.FindProperty("m_fontAsset").objectReferenceValue != font ||
                    serialized.FindProperty("m_sharedMaterial").objectReferenceValue != font.material)
                    throw new InvalidOperationException($"Foundation font or material is not saved on {label.name}.");
            }
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { "Res/Fonts", "Res/Prefab", "Scenes" })
                Directory.CreateDirectory(Module + "/" + folder);
            AssetDatabase.Refresh();
        }

        private static void ConfigureCollectors()
        {
            const string settingsPath = Module + "/AssetBundleCollectorSetting.asset";
            if (AssetDatabase.FindAssets("t:AssetBundleCollectorSetting").Length == 0)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AssetBundleCollectorSetting>(), settingsPath);

            var settings = AssetBundleCollectorSettingData.Setting;
            var package = settings.Packages.SingleOrDefault(entry => entry.PackageName == YooAssetService.PackageName)
                ?? AssetBundleCollectorSettingData.CreatePackage(YooAssetService.PackageName);
            package.EnableAddressable = true;
            var group = package.Groups.SingleOrDefault(entry => entry.GroupName == "FoundationDemo")
                ?? AssetBundleCollectorSettingData.CreateGroup(package, "FoundationDemo");
            group.Collectors.Clear();
            foreach (string path in new[] { Module + "/Configs/demo-config.json", Module + "/Configs/demo-localization.json",
                Module + "/Res/Text/demo-resource.txt", PrefabPath })
            {
                _ = RequireAsset<UnityEngine.Object>(path);
                AssetBundleCollectorSettingData.CreateCollector(group, new AssetBundleCollector
                {
                    CollectPath = path,
                    CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                    AddressRuleName = nameof(FoundationDemoAddressRule)
                });
            }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset)
                throw new FileNotFoundException($"Required {typeof(T).Name} asset is unavailable.", path);
            return asset;
        }

        private static void Set(SerializedObject obj, string key, UnityEngine.Object value) =>
            obj.FindProperty(key).objectReferenceValue = value;

        public static void BuildWindows() => HotUpdateBuilder.BuildWindows();
        public static void BuildAndroidAstc() => HotUpdateBuilder.BuildAndroidAstc();
        public static void BuildAndroidEtc2() => HotUpdateBuilder.BuildAndroidEtc2();
    }

    public sealed class FoundationDemoAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            if (data.AssetPath == FoundationBuilder.PrefabPath)
                return FoundationWindow.PrefabAddress;
            return "foundation/" + Path.GetFileNameWithoutExtension(data.AssetPath);
        }
    }
}
