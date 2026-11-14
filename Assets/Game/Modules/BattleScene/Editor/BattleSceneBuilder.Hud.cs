using System.Collections.Generic;
using System.Linq;
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
            var track = Picture(order, "Track", "_game_hud_timebg1", 32.5f, 351, 15, 516);
            track.color = new Color(1f, 1f, 1f, 153f / 255f);
            var details = Picture(order, "Details Background", "_btn_small_black", 34.5f, 641.5f, 52, 52);
            details.type = Image.Type.Sliced;
            details.pixelsPerUnitMultiplier = 1f / PixelScale;
            bindings.Add("orderHourglass", Picture(order, "Hourglass", "cm_icon_etctime2", 33.5f, 643.5f, 16.16f, 22.22f));
            var allyIds = BattleHudPreview.DefaultAllyHeroIds;
            for (int i = Slots.Length - 1; i >= 0; i--)
                OrderFace(order, allyIds[i], 32, 184 + i * 47);

            var markers = Group("Unit Status", rect);
            Group("Allies", markers);
            Group("Enemies", markers);

            var active = Group("Active Unit", rect);
            active.SetSiblingIndex(order.GetSiblingIndex());
            var strip = Picture(active, "Information Strip", "_game_hud_timebg2", 261.5f, 49.5f, 471, 87);
            strip.color = new Color(1f, 1f, 1f, 153f / 255f);
            bindings.Add("activePortrait", Picture(active, "Ras Portrait", "Faces/c1001_su", 218, 103, 436, 206));
            Picture(active, "Element", "cm_icon_promfire", 418, 66, 28, 28);
            Picture(active, "Class", "cm_icon_role_knight", 450, 66, 26, 26);
            bindings.Add("activeName", Text(active, "Active Name", "拉斯", 338, 66, 136, 28, 20, TextAlignmentOptions.MidlineRight));
            var activeHp = Picture(active, "Active HP Fill", "game_hud_bar_hp", 437, 34, 85, 6);
            SetFill(activeHp, 1f);
            bindings.Add("activeHpFill", activeHp);
            var hpText = Text(active, "Active HP", "1260/1260", 325, 34, 116, 27, 17, TextAlignmentOptions.MidlineRight);
            ApplyFont(hpText, healthFont);
            hpText.color = new Color32(153, 195, 42, 255);
            bindings.Add("activeHp", hpText);

            var soul = Group("Soul Gauge", rect);
            Picture(soul, "Ornament", "_game_hud_soulbg1", 729, 56, 471, 114);
            Picture(soul, "Track", "game_hud_soulbg2", 703, 29, 390, 37);
            var soulFill = Picture(soul, "Fill", "game_hud_soulbg3", 692, 31.5f, 368, 23);
            SetFill(soulFill, 0f);
            bindings.Add("soulFill", soulFill);
            Button(soul, bindings, "soulBurn", "game_hud_btn_burn_d", 715, 64, 228, 102);
            bindings.Add("soulCount", Text(soul, "Soul Count", "20 / " + BattleHudPreview.StandardSoulCapacity, 690, 19, 110, 21, 14));

            var skills = Group("Skills", rect);
            for (int index = 1; index <= 3; index++)
            {
                float x = 1023 + 99 * (index - 1);
                string file = "Skills/sk_c1001_" + index;
                Rect icon = Asset<Sprite>(HudTextures + file + ".png").rect;
                Button(skills, bindings, "skill" + index, file, x + 3.5f, 54.5f, icon.width * 1.25f, icon.height * 1.25f);
                Picture(skills, "Skill " + index + " Frame", "_cm_skill_frm", x, 57, 104, 107);
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
            ConfigureHudButtons(root.GetComponent<BindingMap>());
            return root;
        }

        private static void OrderFace(RectTransform parent, string id, float x, float y)
        {
            Picture(parent, id + " Base", "_hero_s_bg_ally", x, y, 54, 54);
            Picture(parent, id + " Portrait", "Faces/" + id + "_s", x, y, 54, 54);
            Picture(parent, id + " Frame", "_hero_s_frame_ally", x, y, 57, 57);
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
            float width, float height, float point, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            TMP_FontAsset fontOverride = null)
        {
            var label = Item(parent, name, x, y, width, height).gameObject.AddComponent<GameText>();
            label.text = text;
            label.fontSize = point * PixelScale;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            ApplyFont(label, fontOverride ? fontOverride : font);
            return label;
        }

        private static void ApplyFont(GameText label, TMP_FontAsset asset)
        {
            var serialized = new SerializedObject(label);
            serialized.FindProperty("m_fontAsset").objectReferenceValue = asset;
            serialized.FindProperty("m_sharedMaterial").objectReferenceValue = asset.material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
