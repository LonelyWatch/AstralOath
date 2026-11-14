using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Arena;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using YooAsset.Editor;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string Arena = "Assets/Game/Modules/Arena/";
        private const string Home = "Assets/Game/Modules/Home/";
        private const string Common = "Assets/Game/Common/Res/";
        public const string StartupScene = Home + "Scenes/GameHome.unity";
        private static readonly (string Path, string Address)[] Assets =
        {
            (Home + "Res/Prefab/HomeWindow.prefab", HomeWindow.Address),
            (Arena + "Res/Prefab/ArenaDirectory.prefab", ArenaDirectory.Address),
            (Arena + "Res/Prefab/ArenaCreateRoom.prefab", ArenaCreateRoom.Address),
            (Arena + "Res/Prefab/ArenaRoom.prefab", ArenaRoom.Address),
            (Arena + "Configs/arena-zh-hans.json", ArenaLabels.Address),
        };
        private static readonly Color Gold = new Color32(166, 139, 92, 255);
        private static readonly Color Mist = new Color32(180, 206, 216, 255);
        private static readonly Color Green = new Color32(137, 201, 62, 255);
        private static TMP_FontAsset font;
        private static Dictionary<string, string> labels;

        [Serializable] private sealed class LabelFile { public LabelRow[] rows; }
        [Serializable] private sealed class LabelRow { public string key, text; }

        [MenuItem("Tools/游戏主页/生成主页和模拟战")]
        public static void Generate()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string path in Assets.Select(item => item.Path).Append(StartupScene))
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            labels = JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows
                .ToDictionary(row => row.key, row => row.text);
            Save(BuildHome(), Assets[0].Path);
            BuildPasswordEntryPrefabs();
            Save(BuildDirectory(), Assets[1].Path);
            Save(BuildCreate(), Assets[2].Path);
            Save(BuildRoom(), Assets[3].Path);
            ConfigureCollectors();
            BuildStartupScene();
            Validate();
        }

        [MenuItem("Tools/游戏主页/打开启动场景")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(StartupScene);
        }

        public static void Validate()
        {
            UIViewRegistry.ValidatePrefab<HomeWindow>(Load<GameObject>(Assets[0].Path));
            UIViewRegistry.ValidatePrefab<ArenaDirectory>(Load<GameObject>(Assets[1].Path));
            UIViewRegistry.ValidatePrefab<ArenaCreateRoom>(Load<GameObject>(Assets[2].Path));
            UIViewRegistry.ValidatePrefab<ArenaRoom>(Load<GameObject>(Assets[3].Path));
            UIViewRegistry.ValidatePrefab<ArenaPasswordEntry>(Load<GameObject>(PasswordEntryPath));
            UIViewRegistry.ValidatePrefab<GameToast>(Load<GameObject>(ToastPath));
            _ = Load<SceneAsset>(StartupScene);
            var addresses = AssetBundleCollectorSettingData.Setting.BeginCollect("Foundation", true, false)
                .CollectAssets.ToDictionary(asset => asset.Address, asset => asset.AssetInfo.AssetPath);
            foreach (var (path, address) in Assets)
                if (!addresses.TryGetValue(address, out string registered) || registered != path)
                    throw new BuildFailedException("缺少主页资源地址 " + address);
        }

        internal static string AddressFor(string path) => Assets.Single(item => item.Path == path).Address;

        private static void ConfigureCollectors()
        {
            var settings = AssetBundleCollectorSettingData.Setting;
            var package = settings.Packages.Single(entry => entry.PackageName == "Foundation");
            var group = package.Groups.SingleOrDefault(entry => entry.GroupName == "HomeArena")
                ?? AssetBundleCollectorSettingData.CreateGroup(package, "HomeArena");
            group.Collectors.Clear();
            foreach (var (path, _) in Assets)
                AssetBundleCollectorSettingData.CreateCollector(group, new AssetBundleCollector
                {
                    CollectPath = path,
                    CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                    AddressRuleName = nameof(HomeArenaAddressRule)
                });
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void BuildStartupScene()
        {
            var scene = EditorSceneManager.OpenScene(FoundationBuilder.ScenePath);
            var root = scene.GetRootGameObjects().Single(item => item.GetComponent<FoundationBootstrap>());
            var bootstrap = new SerializedObject(root.GetComponent<FoundationBootstrap>());
            bootstrap.FindProperty("showGameHome").boolValue = true;
            bootstrap.ApplyModifiedPropertiesWithoutUndo();
            var canvas = scene.GetRootGameObjects().Single(item => item.GetComponent<CanvasScaler>());
            canvas.AddComponent<DesktopDisplayMode>();
            var feedback = canvas.AddComponent<GameUiFeedback>();
            feedback.Configure(Load<Sprite>(Common + "Textures/UiTouchRing.png"), Load<Sprite>(Common + "Textures/UiTouchGlow.png"),
                Load<AudioClip>(Common + "Sounds/UiConfirm.wav"), Load<AudioClip>(Common + "Sounds/UiConfirm.wav"),
                Load<AudioClip>(Common + "Sounds/UiCancel.wav"), Load<AudioClip>(Common + "Sounds/UiCancel.wav"),
                Load<Sprite>(Common + "Textures/UiTouchSpark.png"), Load<Texture2D>(Common + "Textures/UiTouchPulse.png"),
                Load<Material>(Common + "Materials/UiTouchPulse.mat"), Load<AudioClip>(Common + "Sounds/UiBlocked.wav"));
            EditorUtility.SetDirty(feedback);
            canvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvas.GetComponent<CanvasScaler>().matchWidthOrHeight = 1f;
            var launch = (GameButton)bootstrap.FindProperty("open").objectReferenceValue;
            launch.gameObject.SetActive(false);
            if (!EditorSceneManager.SaveScene(scene, StartupScene, true))
                throw new IOException("无法保存游戏启动场景。");
            EditorSceneManager.OpenScene(StartupScene);
            AssetDatabase.SaveAssets();
        }

        private static void Save(GameObject root, string path)
        {
            try
            {
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException("无法保存 " + path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) is T item && item ? item
                : throw new FileNotFoundException("需要资源 " + path, path);

        private static Sprite Art(string name, string module = Arena) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(module + "Res/Textures/" + name + ".png");

        private static RectTransform Root(string name)
        {
            var item = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            item.anchorMin = Vector2.zero;
            item.anchorMax = Vector2.one;
            item.sizeDelta = Vector2.zero;
            return item;
        }

        // Normalized canvas areas use a top-left design origin. All prefabs share their parent's Safe Area.
        private static RectTransform Area(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var result = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            result.SetParent(parent, false);
            Place(result, x, y, w, h);
            return result;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = new Vector2(x, 1f - y - h);
            rect.anchorMax = new Vector2(x + w, 1f - y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Paint(RectTransform rect, Sprite sprite, Color color, bool raycast = false, bool sliced = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sliced && sprite && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = raycast;
            return image;
        }

        private static Image Picture(RectTransform parent, string name, Sprite sprite, float x, float y, float w, float h,
            bool aspect = false)
        {
            var image = Paint(Area(parent, name, x, y, w, h), sprite, Color.white);
            image.preserveAspect = aspect;
            image.enabled = sprite;
            return image;
        }

        private static void Cover(RectTransform parent, string name, Sprite sprite)
        {
            if (!sprite) return;
            var image = Picture(parent, name, sprite, 0, 0, 1, 1);
            var aspect = image.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        private static GameText Text(RectTransform parent, string name, string key, float size, float x, float y, float w, float h,
            Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var t = Area(parent, name, x, y, w, h).gameObject.AddComponent<GameText>();
            t.font = font;
            t.fontSharedMaterial = font.material;
            t.text = key == null ? "" : labels[key];
            t.color = color ?? Color.white;
            t.fontSize = size;
            t.alignment = alignment;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        private static GameButton Button(RectTransform rect, Sprite sprite, Color color)
        {
            var image = Paint(rect, sprite, color, true, true);
            image.canvasRenderer.cullTransparentMesh = false;
            var button = rect.gameObject.AddComponent<GameButton>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            return button;
        }

        private static void PressSprites(GameButton button, Sprite normal, Sprite pressed)
        {
            if (!normal || !pressed) throw new InvalidOperationException("Missing button state artwork: " + button.name);
            var graphic = (Image)button.targetGraphic;
            graphic.sprite = normal;
            graphic.type = normal.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            var states = button.spriteState;
            states.pressedSprite = pressed;
            button.spriteState = states;
            button.transition = Selectable.Transition.SpriteSwap;
        }

        private static void PressDecorations(GameButton button, params (Image Image, Sprite Sprite)[] decorations)
        {
            var serialized = new SerializedObject(button);
            var entries = serialized.FindProperty("pressDecorations");
            entries.arraySize = decorations.Length;
            for (int i = 0; i < decorations.Length; i++)
            {
                var item = entries.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("image").objectReferenceValue = decorations[i].Image;
                item.FindPropertyRelative("pressed").objectReferenceValue = decorations[i].Sprite;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PressSmallBlack(GameButton button) =>
            PressSprites(button, CommonArt("_btn_small_black"), CommonArt("_btn_small_black_p"));

        private static GameButton Action(RectTransform parent, string name, string key, float size, float x, float y, float w, float h,
            out GameText label, Sprite sprite = null, Color? color = null)
        {
            var rect = Area(parent, name, x, y, w, h);
            var button = Button(rect, sprite, color ?? new Color32(16, 22, 28, 230));
            label = Text(rect, "Caption", key, size, .03f, .04f, .94f, .92f, alignment: TextAlignmentOptions.Center);
            return button;
        }

        private static RectTransform Prefab(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var item = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(Common + "Prefab/" + name + ".prefab"), parent);
            var rect = (RectTransform)item.transform;
            Place(rect, x, y, w, h);
            return rect;
        }

        private static GameButton CommonButton(RectTransform parent, string prefab, string key, float x, float y, float w, float h)
        {
            var rect = Prefab(parent, prefab, x, y, w, h);
            var map = rect.GetComponent<BindingMap>();
            var label = map.Require<GameText>("label");
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.text = labels[key];
            var button = map.Require<GameButton>("button");
            button.FeedbackSound = key switch
            {
                "arena.confirm" => GameUiFeedback.Sound.Confirm,
                "arena.cancel" => GameUiFeedback.Sound.Cancel,
                _ => GameUiFeedback.Sound.Normal,
            };
            if (button.FeedbackSound == GameUiFeedback.Sound.Normal)
            {
                var settings = new SerializedObject(button);
                settings.FindProperty("clickAudio").objectReferenceValue = null;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            return button;
        }

        private static BindingMap Avatar(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rect = Prefab(parent, "PlayerAvatar", x, y, w, h);
            rect.name = name;
            var map = rect.GetComponent<BindingMap>();
            var portrait = map.Require<Image>("portrait");
            portrait.sprite = Load<Sprite>("Assets/Game/Modules/BattleScene/Res/Textures/Hud/Faces/c1001_s.png");
            return map;
        }

        private static void Rule(RectTransform parent, string name, float x, float y, float width, float height, Sprite source = null)
        {
            var image = Paint(Area(parent, name, x, y, width, height), source, Gold);
            image.type = source && source.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }

        private static ScrollRect Scroll(RectTransform parent, string name, float x, float y, float w, float h, bool horizontal = false)
        {
            var root = Area(parent, name, x, y, w, h);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = horizontal;
            scroll.vertical = !horizontal;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48;
            var viewport = Area(root, "Viewport", 0, 0, 1, 1);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = Paint(viewport, null, Color.clear, true);
            hit.canvasRenderer.cullTransparentMesh = false;
            var content = Area(viewport, "Content", 0, 0, horizontal ? 0 : 1, horizontal ? 1 : 0);
            content.pivot = horizontal ? new Vector2(0, .5f) : new Vector2(.5f, 1);
            if (horizontal)
            {
                var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 20;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
            }
            else
            {
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 12;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            if (horizontal) fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            else fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        private static TMP_InputField Input(RectTransform parent, string name, string placeholder, int limit,
            float x, float y, float w, float h, bool password = false)
        {
            var root = Area(parent, name, x, y, w, h);
            var image = Paint(root, Art("_btn_small_white"), Color.white, true, true);
            var input = root.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            var viewport = Area(root, "Text Area", .034f, .03f, .932f, .94f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Text(viewport, "Text", null, 27, 0, 0, 1, 1, new Color32(26, 26, 25, 255));
            text.overflowMode = TextOverflowModes.Overflow;
            var hint = Text(viewport, "Placeholder", placeholder, 26, 0, 0, 1, 1, new Color32(74, 70, 62, 255));
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = limit;
            input.richText = false;
            if (password)
            {
                input.contentType = TMP_InputField.ContentType.Pin;
                input.keyboardType = TouchScreenKeyboardType.NumberPad;
            }
            return input;
        }

        private static void FollowKeyboard(RectTransform panel, TMP_InputField input)
        {
            var keyboard = panel.gameObject.AddComponent<OnScreenKeyboardInset>();
            var serialized = new SerializedObject(keyboard);
            serialized.FindProperty("input").objectReferenceValue = input;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Bind(RectTransform root, params (string Key, Object Target)[] keys)
        {
            var map = root.GetComponent<BindingMap>() ?? root.gameObject.AddComponent<BindingMap>();
            var obj = new SerializedObject(map);
            var entries = obj.FindProperty("entries");
            entries.arraySize = keys.Length;
            for (int i = 0; i < keys.Length; i++)
            {
                var row = entries.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("key").stringValue = keys[i].Key;
                row.FindPropertyRelative("component").objectReferenceValue = keys[i].Target;
            }
            obj.ApplyModifiedPropertiesWithoutUndo();
            map.Validate();
        }
    }

    public sealed class HomeArenaAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data) => ArenaUiBuilder.AddressFor(data.AssetPath);
    }
}
