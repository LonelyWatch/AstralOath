using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene.Editor
{
    public static class BattleResultAssets
    {
        private const string Module = "Assets/Game/Modules/BattleScene/";
        private const string Art = Module + "Res/Online/AstralOathBattleResult/";
        private const string Hud = Module + "Res/Textures/Hud/";
        private const string Common = "Assets/Game/Common/Res/Textures/";

        [MenuItem("Tools/战斗场景/生成竞技场结算界面")]
        public static void Generate()
        {
            var font = Load<TMP_FontAsset>("Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset");
            var root = new GameObject("BattleResult", typeof(RectTransform), typeof(BindingMap), typeof(Canvas), typeof(GraphicRaycaster));
            try
            {
                var rect = (RectTransform)root.transform;
                Stretch(rect);
                var bindings = new Dictionary<string, Object>();
                var center = Vector2.one * .5f;
                var left = new Vector2(0, .5f);
                var rightBottom = new Vector2(1, 0);
                var shade = ImageAt(rect, "Shade", null, center, 0, 0, 0, 0);
                Stretch(shade.rectTransform);
                shade.color = new Color(0, 0, 0, .3f);
                shade.raycastTarget = true;
                var vignette = ImageAt(rect, "Vignette", Art + "_dim.png", center, 0, 0, 0, 0);
                Stretch(vignette.rectTransform);
                var band = ImageAt(rect, "Emblem Band", null, new Vector2(.5f, 520f / 720f), 0, 0, 0, 136);
                band.color = new Color(0, 0, 0, .32f);
                VerticalBand(band.rectTransform);
                bindings["emblem"] = Rect(rect, "Emblem", new Vector2(.5f, 520f / 720f), 0, 0, 0, 0);
                bindings["draw"] = Text(rect, "Draw", new Vector2(.5f, 520f / 720f), 0, 0, 400, 100, 56);

                // result_base/n_pvp/n_intimacy uses a single authored rail and four of its six positions.
                var rail = ImageAt(rect, "Friendship Rail", Art + "_gradient.png", left, -14.5f, 0, 301, 0);
                rail.color = Color.black;
                FullHeight(rail.rectTransform);
                FullHeight(ImageAt(rect, "Rail Border", Art + "_bar_6.png", left, 136, 0, 4, 0).rectTransform);
                for (int i = 0; i < 4; i++)
                {
                    var row = Rect(rect, "Hero " + i, left, 67, 150 - 108 * i, 100, 108);
                    bindings["hero" + i] = row.gameObject;
                    ImageAt(row, "Base", Hud + "_hero_s_bg_ally.png", center, 0, 0, 86.24f, 86.24f);
                    bindings["face" + i] = ImageAt(row, "Face", null, center, 0, 0, 80.2f, 80.2f);
                    ImageAt(row, "Frame", Hud + "_hero_s_frame_ally.png", center, .77f, -.77f, 91.2f, 91.2f);
                    ImageAt(row, "Heart", Art + "hero_love_icon_1.png", center, 0, 47, 48.6f, 48.6f);
                    bindings["friendship" + i] = Text(row, "Friendship", center, 0, 48.3f, 32, 35, 20);
                }
                Button(rect, "details", rightBottom, -273, 56, 152, 72, false);
                Button(rect, "confirm", rightBottom, -104, 56, 180, 72, true);

                var statistics = Rect(rect, "Statistics", center, 0, 0, 0, 0);
                Stretch(statistics);
                bindings["statistics"] = statistics.gameObject;
                var shield = ImageAt(statistics, "Shield", null, center, 0, 0, 0, 0);
                Stretch(shield.rectTransform);
                shield.color = new Color(0, 0, 0, .76f);
                shield.raycastTarget = true;
                var paper = ImageAt(statistics, "Panel", null, center, 0, 0, 920, 470);
                paper.color = new Color32(22, 29, 38, 252);
                bindings["title"] = Text(statistics, "Title", center, 0, 190, 640, 54, 35);
                bindings["turn"] = Text(statistics, "Turn", center, 0, 145, 480, 44, 23);
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? -215 : 215;
                    var heading = Text(statistics, side == 0 ? "Allies" : "Enemies", center, x, 90, 410, 50, 25);
                    heading.color = side == 0 ? new Color32(133, 211, 255, 255) : new Color32(255, 172, 156, 255);
                    bindings[side == 0 ? "allies" : "enemies"] = heading;
                    for (int i = 0; i < 4; i++)
                        bindings["unit" + (4 * side + i)] = Text(statistics, "Unit " + (4 * side + i), center,
                            x, 37 - 44 * i, 415, 43, 22);
                }
                Button(statistics, "close", center, 0, -177, 180, 65, false);
                statistics.gameObject.SetActive(false);
                var data = new SerializedObject(root.GetComponent<BindingMap>());
                var entries = data.FindProperty("entries");
                entries.arraySize = bindings.Count;
                int index = 0;
                foreach (var binding in bindings)
                {
                    var item = entries.GetArrayElementAtIndex(index++);
                    item.FindPropertyRelative("key").stringValue = binding.Key;
                    item.FindPropertyRelative("component").objectReferenceValue = binding.Value;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                UIViewRegistry.ValidatePrefab<BattleResult>(root);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, Module + "Res/Prefab/BattleResult.prefab");
                var config = new SerializedObject(Load<BattleOnlineTransitionAssets>(Module + "Configs/AstralOathOnlineTransition.asset"));
                config.FindProperty("resultPrefab").objectReferenceValue = prefab;
                var emblem = config.FindProperty("resultEmblem");
                const string source = Art + "eff_pvp_outro_victory";
                emblem.FindPropertyRelative("skeleton").objectReferenceValue = Load<TextAsset>(source + ".spine.json");
                emblem.FindPropertyRelative("atlas").objectReferenceValue = Load<TextAsset>(source + ".atlas.txt");
                emblem.FindPropertyRelative("page").objectReferenceValue = Load<Texture2D>(source + ".png");
                config.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(config.targetObject);

                GameText Text(RectTransform parent, string name, Vector2 anchor, float x, float y, float w, float h, float size)
                {
                    var label = Rect(parent, name, anchor, x, y, w, h).gameObject.AddComponent<GameText>();
                    label.font = font;
                    label.fontSharedMaterial = font.material;
                    label.fontSize = size * 1.5f;
                    label.alignment = TextAlignmentOptions.Center;
                    label.richText = false;
                    label.enableWordWrapping = false;
                    label.raycastTarget = false;
                    return label;
                }
                void Button(RectTransform parent, string key, Vector2 anchor, float x, float y, float w, float h, bool green)
                {
                    string texture = Common + (green ? "_btn_green" : "_btn_gold");
                    var image = ImageAt(parent, key, texture + ".png", anchor, x, y, w, h);
                    image.type = Image.Type.Sliced;
                    image.pixelsPerUnitMultiplier = 1f / 1.5f;
                    image.raycastTarget = true;
                    var button = image.gameObject.AddComponent<GameButton>();
                    button.targetGraphic = image;
                    button.transition = Selectable.Transition.SpriteSwap;
                    button.spriteState = new SpriteState { pressedSprite = Load<Sprite>(texture + "_p.png") };
                    bindings[key] = button;
                    bindings[key + "Label"] = Text(image.rectTransform, "Label", center, 0, 0, w - 8, h, 24);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 anchor, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(w, h) * 1.5f;
            rect.anchoredPosition = new Vector2(x, y) * 1.5f;
            return rect;
        }

        private static Image ImageAt(RectTransform parent, string name, string path, Vector2 anchor, float x, float y, float w, float h)
        {
            var image = Rect(parent, name, anchor, x, y, w, h).gameObject.AddComponent<Image>();
            if (path != null) image.sprite = Load<Sprite>(path);
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void FullHeight(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(0, 1);
        }

        private static void VerticalBand(RectTransform rect)
        {
            float y = rect.anchorMin.y;
            rect.anchorMin = new Vector2(0, y); rect.anchorMax = new Vector2(1, y);
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new System.InvalidOperationException("Missing result asset " + path);
    }
}
