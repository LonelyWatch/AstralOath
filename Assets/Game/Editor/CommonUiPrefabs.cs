using System.IO;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    internal static class CommonUiPrefabs
    {
        internal const string Root = "Assets/Game/Common/Res/";
        internal const string ButtonPath = Root + "Prefab/ConfirmButton.prefab";
        internal const string PanelPath = Root + "Prefab/InfoPanel.prefab";
        private const string PanelFontPath = Root + "Fonts/EpicSeven.asset";
        private const string FontPath = "Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset";

        [MenuItem("Tools/UI/生成通用预制体")]
        public static void Generate()
        {
            var font = Load<TMP_FontAsset>(FontPath);
            Save(BuildButton(font), ButtonPath);
            Save(BuildPanel(GetPanelFont(font)), PanelPath);
            UIViewRegistry.ValidatePrefab<InfoPanel>(Load<GameObject>(PanelPath));
        }

        private static GameObject BuildButton(TMP_FontAsset font)
        {
            var root = new GameObject("ConfirmButton", typeof(RectTransform), typeof(Image), typeof(GameButton), typeof(AudioSource));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(260f, 64f);
            var image = root.GetComponent<Image>();
            image.sprite = Load<Sprite>(Root + "Textures/cm_btn_norgreend.png");
            image.type = Image.Type.Sliced;
            var button = root.GetComponent<GameButton>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            var swap = button.spriteState;
            swap.pressedSprite = Load<Sprite>(Root + "Textures/cm_btn_norgreenp.png");
            button.spriteState = swap;
            var audio = root.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.clip = Load<AudioClip>(Root + "Sounds/UiConfirm.wav");
            var data = new SerializedObject(button);
            data.FindProperty("clickAudio").objectReferenceValue = audio;
            data.ApplyModifiedPropertiesWithoutUndo();
            var label = Text(Child(rect, "Label", Vector2.zero, Vector2.one,
                new Vector2(20f, 0f), new Vector2(-20f, 0f)), font, 28f, Color.white);
            label.text = string.Empty;
            label.alignment = TextAlignmentOptions.Center;
            Bind(root, ("label", label), ("button", button));
            return root;
        }

        private static TMP_FontAsset GetPanelFont(TMP_FontAsset fallback)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PanelFontPath);
            if (existing) return existing;
            var font = TMP_FontAsset.CreateFontAsset(Load<Font>(Root + "Fonts/EpicSeven.ttf"),
                56, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (!font) throw new InvalidDataException("Cannot initialize the EpicSeven typeface.");
            font.name = "EpicSeven";
            font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            if (!font.TryAddCharacters("技能攻击防御提升燃烧获得灵魂冷却时间强化回合0123456789+%_", out var missing))
                throw new InvalidDataException("Missing EpicSeven glyphs: " + missing);
            AssetDatabase.CreateAsset(font, PanelFontPath);
            foreach (var texture in font.atlasTextures)
                if (texture) AssetDatabase.AddObjectToAsset(texture, font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        private static GameObject BuildPanel(TMP_FontAsset font)
        {
            var root = new GameObject("InfoPanel", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(560f, 320f);
            var background = root.GetComponent<Image>();
            background.sprite = Load<Sprite>(Root + "Textures/_box_equip_0.png");
            background.material = Load<Material>(Root + "Materials/InfoPanelSkill.mat");
            background.type = Image.Type.Sliced;

            var effectBackground = PanelImage(rect, "Effect Background", "_btn_small_white.png", true);
            effectBackground.rectTransform.anchorMin = Vector2.zero;
            effectBackground.rectTransform.offsetMin = effectBackground.rectTransform.offsetMax = Vector2.zero;
            effectBackground.raycastTarget = true;
            var icon = PanelImage(rect, "Icon");
            var soulIcon = PanelImage(rect, "Soul Icon", "cm_icon_star_s.png");
            var cooldownIcon = PanelImage(rect, "Cooldown Icon", "cm_icon_etctime_b.png");
            var title = PanelText(rect, "Title", font, 27.5f, Color.white);
            var blue = new Color32(77, 167, 255, 255);
            var metadata = PanelText(rect, "Metadata", font, 21.6f, blue);
            var cooldown = PanelText(rect, "Cooldown", font, 21.6f, new Color32(229, 229, 229, 255));
            var scroll = Child(rect, "Scroll", Vector2.zero, Vector2.one,
                new Vector2(24f, 16f), new Vector2(-24f, -104f)).gameObject.AddComponent<ScrollRect>();
            var viewport = Child((RectTransform)scroll.transform, "Viewport", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<RectMask2D>();
            var surface = viewport.gameObject.AddComponent<Image>();
            surface.color = Color.clear;
            surface.canvasRenderer.cullTransparentMesh = false;
            var content = Child(viewport, "Content", Vector2.up, Vector2.one, new Vector2(0f, -160f), Vector2.zero);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            var body = PanelText(content, "Description", font, 22.5f, new Color32(136, 136, 136, 255));
            var burnDivider = PanelImage(content, "Burn Divider", "_bar_2.png", true);
            burnDivider.color = new Color32(255, 255, 255, 76);
            var burnTitle = PanelText(content, "Burn Title", font, 21.9f, blue);
            var burnCost = PanelText(content, "Burn Cost", font, 21.9f, blue);
            burnCost.alignment = TextAlignmentOptions.TopRight;
            var burnIcon = PanelImage(content, "Burn Soul Icon", "cm_icon_star_s.png");
            var burnBody = PanelText(content, "Burn Description", font, 22.5f, body.color);
            var upgradeDivider = PanelImage(content, "Upgrade Divider", "_bar_1.png", true);
            upgradeDivider.color = burnDivider.color;
            var upgrades = PanelText(content, "Upgrades", font, 21f, new Color32(229, 229, 229, 255));

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            Bind(root, ("background", background), ("effectBackground", effectBackground), ("icon", icon),
                ("soulIcon", soulIcon), ("cooldownIcon", cooldownIcon), ("title", title), ("metadata", metadata),
                ("cooldown", cooldown), ("description", body), ("burnDivider", burnDivider), ("burnTitle", burnTitle),
                ("burnCost", burnCost), ("burnIcon", burnIcon), ("burnDescription", burnBody),
                ("upgradeDivider", upgradeDivider), ("upgrades", upgrades), ("scroll", scroll));
            return root;
        }

        private static Image PanelImage(RectTransform parent, string name, string path = null, bool sliced = false)
        {
            var image = Child(parent, name, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            if (path != null) image.sprite = Load<Sprite>(Root + "Textures/" + path);
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = !sliced;
            image.raycastTarget = false;
            return image;
        }

        private static GameText PanelText(RectTransform parent, string name, TMP_FontAsset font, float size, Color color)
        {
            var label = Text(Child(parent, name, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero), font, size, color);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.richText = true;
            return label;
        }

        private static RectTransform Child(RectTransform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static GameText Text(RectTransform rect, TMP_FontAsset font, float size, Color color)
        {
            var text = rect.gameObject.AddComponent<GameText>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static void Bind(GameObject root, params (string Key, Component Value)[] bindings)
        {
            var map = root.AddComponent<BindingMap>();
            var data = new SerializedObject(map);
            var entries = data.FindProperty("entries");
            entries.arraySize = bindings.Length;
            for (int i = 0; i < bindings.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("key").stringValue = bindings[i].Key;
                entry.FindPropertyRelative("component").objectReferenceValue = bindings[i].Value;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            map.Validate();
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException(path);

        private static void Save(GameObject root, string path)
        {
            try
            {
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException(path);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
