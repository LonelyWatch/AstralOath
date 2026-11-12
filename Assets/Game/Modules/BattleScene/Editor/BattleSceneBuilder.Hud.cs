using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        private static GameObject CreateHud()
        {
            var root = new GameObject("BattleHud", typeof(RectTransform), typeof(BindingMap));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var bindings = new Dictionary<string, Component>();

            var top = Group("Top Bar", rect);
            Picture(top, "Wave Plate", "game_wave_bg", 152, 692, 96, 53);
            bindings.Add("wave", Text(top, "Wave", "1 / 3", 152, 698, 75, 25, 17));
            Picture(top, "Help", "cm_hud_top_btn_help", 1008, 687, 50, 58);
            Button(top, bindings, "speed", "cm_hud_top_btn_speed", 1068, 687, 49, 55);
            Button(top, bindings, "auto", "cm_hud_top_btn_auto", 1127, 687, 59, 66);
            Picture(top, "Chat", "cm_hud_top_btn_chatting", 1187, 687, 56, 63);
            Button(top, bindings, "pause", "cm_hud_top_btn_pause", 1245, 687, 56, 62);

            var order = Group("Combat Readiness", rect);
            Picture(order, "Track", "_game_hud_timebg1", 32.5f, 351, 15, 516);
            OrderFace(order, "Mercedes", "c1005", 32, 254);
            OrderFace(order, "Angelica", "c1062", 32, 312);
            OrderFace(order, "Ras", "c1001", 32, 192);

            var markers = Group("Unit Status", rect);
            UnitStatus(markers, "Mercedes", 217, 474, false, 0.86f, 0.59f);
            UnitStatus(markers, "Angelica", 260, 394, false, 0.92f, 0.74f);
            UnitStatus(markers, "Ras", 424, 434, false, 1f, 1f);
            UnitStatus(markers, "Wind Slime", 864, 389, true, 0.8f, 0.48f);
            UnitStatus(markers, "Fire Slime", 999, 321, true, 0.69f, 0.77f);

            var active = Group("Active Unit", rect);
            bindings.Add("activePortrait", Picture(active, "Ras Portrait", "Faces/c1001_su", 218, 103, 436, 206));
            Picture(active, "Information Strip", "_game_hud_timebg2", 261.5f, 49.5f, 471, 87);
            Picture(active, "Element", "cm_icon_promfire", 292, 38, 28, 28);
            Picture(active, "Class", "cm_icon_role_knight", 320, 38, 26, 26);
            bindings.Add("activeName", Text(active, "Active Name", "拉斯", 409, 42, 134, 24, 20, TextAlignmentOptions.MidlineLeft));
            Picture(active, "Active HP Track", "game_hud_bar_bg", 408, 12, 130, 9);
            var activeHp = Picture(active, "Active HP Fill", "game_hud_bar_hp", 408, 12, 126, 5);
            SetFill(activeHp, 1f);
            bindings.Add("activeHpFill", activeHp);
            bindings.Add("activeHp", Text(active, "Active HP", "1,260 / 1,260", 408, 23, 130, 20, 12));

            var soul = Group("Soul Gauge", rect);
            Picture(soul, "Ornament", "_game_hud_soulbg1", 729, 56, 471, 114);
            Picture(soul, "Track", "game_hud_soulbg2", 703, 29, 390, 37);
            var soulFill = Picture(soul, "Fill", "game_hud_soulbg3", 692, 31.5f, 368, 23);
            SetFill(soulFill, 1f);
            bindings.Add("soulFill", soulFill);
            Button(soul, bindings, "soulBurn", "game_hud_btn_burn_d", 715, 64, 228, 102);
            bindings.Add("soulCount", Text(soul, "Soul Count", "20 / 20", 690, 19, 110, 21, 14));

            var skills = Group("Skills", rect);
            for (int index = 1; index <= 3; index++)
            {
                float x = 1023 + 99 * (index - 1);
                Button(skills, bindings, "skill" + index, "Skills/sk_c1001_" + index, x, 57, 92, 92);
                Picture(skills, "Skill " + index + " Frame", "_cm_skill_frm", x, 57, 103, 106);
            }

            var map = new SerializedObject(root.GetComponent<BindingMap>());
            var entries = map.FindProperty("entries");
            entries.arraySize = bindings.Count;
            int row = 0;
            foreach (var item in bindings)
            {
                var entry = entries.GetArrayElementAtIndex(row++);
                entry.FindPropertyRelative("key").stringValue = item.Key;
                entry.FindPropertyRelative("component").objectReferenceValue = item.Value;
            }
            map.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static void OrderFace(RectTransform parent, string name, string id, float x, float y)
        {
            Picture(parent, name + " Base", "_hero_s_bg_ally", x, y, 54, 54);
            Picture(parent, name + " Portrait", "Faces/" + id + "_s", x, y, 54, 54);
            Picture(parent, name + " Frame", "_hero_s_frame_ally", x, y, 57, 57);
        }

        private static void UnitStatus(RectTransform parent, string name, float x, float y, bool enemy, float hp, float readiness)
        {
            var group = Group(name, parent);
            Picture(group, "Frame", "game_hud_bar_norm", x, y, 72, 19);
            Picture(group, "Track", "game_hud_bar_bg", x + 1, y + 2, 56, 7);
            SetFill(Picture(group, "HP", enemy ? "game_hud_bar_hp_red" : "game_hud_bar_hp", x + 1, y + 2, 55, 6), hp);
            SetFill(Picture(group, "Combat Readiness", "game_hud_bar_mp", x + 1, y - 5, 55, 4), readiness);
            Picture(group, "Level Plate", "game_hud_bar_bg", x - 42, y, 20, 17)
                .color = new Color(1f, 1f, 1f, 0.9f);
            Text(group, "Level", "5", x - 42, y, 22, 18, 14);
        }

        private static void SetFill(Image image, float fill)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillAmount = fill;
        }

        private static RectTransform Group(string name, RectTransform parent)
        {
            var group = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            group.SetParent(parent, false);
            group.anchorMin = Vector2.zero;
            group.anchorMax = Vector2.one;
            group.offsetMin = group.offsetMax = Vector2.zero;
            return group;
        }

        private static RectTransform Item(RectTransform parent, string name, float x, float y, float width, float height)
        {
            var item = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            item.SetParent(parent, false);
            item.anchorMin = item.anchorMax = Vector2.zero;
            item.anchoredPosition = new Vector2(x, y) * PixelScale;
            item.sizeDelta = new Vector2(width, height) * PixelScale;
            return item;
        }

        private static Image Picture(RectTransform parent, string name, string file, float x, float y, float width, float height)
        {
            var image = Item(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = Asset<Sprite>(HudTextures + file + ".png");
            image.raycastTarget = false;
            return image;
        }

        private static void Button(RectTransform parent, Dictionary<string, Component> bindings,
            string key, string file, float x, float y, float width, float height)
        {
            var image = Picture(parent, key, file, x, y, width, height);
            var button = image.gameObject.AddComponent<GameButton>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.interactable = false;
            bindings.Add(key, button);
        }

        private static GameText Text(RectTransform parent, string name, string text, float x, float y,
            float width, float height, float point, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = Item(parent, name, x, y, width, height).gameObject.AddComponent<GameText>();
            label.text = text;
            label.fontSize = point * PixelScale;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            var serialized = new SerializedObject(label);
            serialized.FindProperty("m_fontAsset").objectReferenceValue = font;
            serialized.FindProperty("m_sharedMaterial").objectReferenceValue = font.material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return label;
        }
    }
}
