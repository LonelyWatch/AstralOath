using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        [MenuItem("Tools/游戏主页/更新英雄背包装备界面")]
        public static void GenerateEquipmentInventory()
        {
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            labels = JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows.ToDictionary(row => row.key, row => row.text);
            var root = PrefabUtility.LoadPrefabContents(HeroInventoryPrefabPath);
            try
            {
                var properties = new SerializedObject(root.GetComponent<BindingMap>()).FindProperty("entries");
                var retained = new List<(string Key, Object Target)>();
                for (int i = 0; i < properties.arraySize; ++i)
                {
                    var entry = properties.GetArrayElementAtIndex(i);
                    string key = entry.FindPropertyRelative("key").stringValue;
                    if (!key.StartsWith("gear",System.StringComparison.Ordinal))
                        retained.Add((key,entry.FindPropertyRelative("component").objectReferenceValue));
                }
                var previous = root.transform.Find("Equipment browser");
                if (previous) Object.DestroyImmediate(previous.gameObject);
                var roster = (RectTransform)retained.First(item => item.Key == "rosterComposition").Target;
                BuildOwnedEquipment((RectTransform)root.transform,roster,retained);
                Bind((RectTransform)root.transform,retained.ToArray());
                if (!PrefabUtility.SaveAsPrefabAsset(root,HeroInventoryPrefabPath))
                    throw new IOException("Unable to save the equipment inventory panel.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            UIViewRegistry.ValidatePrefab<HeroInventory>(Load<GameObject>(HeroInventoryPrefabPath));
        }

        private static void BuildOwnedEquipment(RectTransform root, RectTransform roster, List<(string Key, Object Target)> outer)
        {
            string[] slots = { "weapon", "helmet", "armor", "necklace", "ring", "boots" };
            string[] authored = { "weapon", "helm", "armor", "neck", "ring", "boots" };
            string[] icons = { "weapon", "helmet", "armor", "neck", "ring", "boot" };
            var detail = (RectTransform)roster.Find("Selected hero detail");
            GameButton Clickable(Image plate)
            {
                var button = plate.GetComponent<GameButton>() ?? plate.gameObject.AddComponent<GameButton>();
                plate.raycastTarget = true;
                button.targetGraphic = plate;
                button.transition = Selectable.Transition.None;
                PrefabUtility.RecordPrefabInstancePropertyModifications(plate);
                return button;
            }
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = (BindingMap)outer.First(pair => pair.Key == "equipmentSlot_" + authored[i]).Target;
                var button = slot.GetComponent<GameButton>() ?? slot.gameObject.AddComponent<GameButton>();
                var plate = slot.Require<Image>("frame");
                plate.raycastTarget = true;
                button.targetGraphic = plate;
                button.transition = Selectable.Transition.None;
                PrefabUtility.RecordPrefabInstancePropertyModifications(plate);
                outer.Add(("gearOpen_" + slots[i], button));
            }
            outer.Add(("gearManage", Clickable(detail.Find("roster_manage_equipment").GetComponent<Image>())));
            outer.Add(("gearSets", detail.Find("No equipment set").GetComponent<GameText>()));

            var frame = Area(root, "Equipment browser", 0, 0, 1, 1);
            var shade = Button(Area(frame, "Dismiss backdrop", 0, 0, 1, 1), null, Black(.68f));
            var keys = new List<(string Key, Object Target)> { ("shade", shade) };
            var surface = Area(frame, "Equipment inventory panel", .106f, .112f, .873f, .852f);
            Paint(surface, BagArt("_box_equip_none"), new Color32(22, 36, 54, 249), true, true);
            void Label(RectTransform parent, string key, float x, float y, float w, float h,
                float size, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
                => keys.Add((key, Text(parent, key, null, size, x, y, w, h, Color.white, align, true)));
            void Control(RectTransform parent, string key, string label, float x, float y, float w, float h, bool primary = false)
            {
                var control = Action(parent, key, label == null ? null : "home.gear." + label, 25, x, y, w, h,
                    out var caption, primary ? CommonArt("_btn_green") : BagArt("_btn_small_black"), Color.white);
                keys.Add((key, control));
                keys.Add((key + "Label", caption));
            }
            var catalog = Area(frame, "Equipment item graphics", 0, 0, .01f, .01f);
            for (int i = 0; i < slots.Length; ++i)
            {
                var sprite = InventoryArt("Equipment/icon_eq_" + icons[i] + "_arena026");
                keys.Add(("icon_" + slots[i], Picture(catalog, slots[i], sprite, 0, 0, 1, 1, true)));
            }
            catalog.gameObject.SetActive(false);
            for (int i = 0; i <= slots.Length; ++i)
            {
                string key = i == 0 ? "all" : slots[i - 1];
                var rect = Area(surface, "Filter " + key, .012f + .083f * i, .014f, .078f, .091f);
                var filter = Button(rect, BagArt("_btn_small_black"), Color.white);
                keys.Add(("filter_" + key, filter));
                keys.Add(("active_" + key, Picture(rect, "Selected filter", BagArt("_tap_select_blue2"), 0, 0, 1, 1, true)));
                Picture(rect, "Slot symbol", i == 0 ? BagArt("cm_icon_all") : RosterArt("cm_icon_par" + authored[i-1]),
                    .25f,.17f,.50f,.66f,true);
            }
            Label(surface, "capacity", .62f, .018f, .24f, .075f, 23, TextAlignmentOptions.MidlineRight);
            Control(surface, "close", "close", .88f, .015f, .103f, .083f);
            var scroll = InventoryScroll(surface, "Equipment items", .014f,.13f,.565f,.84f);
            var grid = scroll.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(133,132);
            grid.spacing = new Vector2(8,10);
            scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            keys.Add(("items", scroll));
            var card = Area(surface, "Equipment item template", 0, 0, .1f,.15f);
            var cardButton = Button(card, CommonArt("_box_equip_0"), Color.white);
            var icon = Picture(card, "Equipment art", null, .09f,.09f,.82f,.76f,true);
            var highlight = Picture(card, "Selection", Art("_select"), 0,0,1,1);
            highlight.type = Image.Type.Sliced;
            var enhancement = Text(card,"Enhancement",null,21,.04f,.01f,.90f,.21f,new Color32(255,215,109,255));
            var badge = Text(card,"Item level",null,18,.04f,.78f,.90f,.2f,Color.white,TextAlignmentOptions.MidlineRight);
            Bind(card,("select",cardButton),("icon",icon),("highlight",highlight),("enhancement",enhancement),("level",badge));
            keys.Add(("template",card.gameObject));
            card.gameObject.SetActive(false);

            var info = Area(surface,"Equipment details",.60f,.122f,.386f,.85f);
            Paint(info,BagArt("_box_equip_none"),new Color32(24,36,50,250),true,true);
            keys.Add(("detail",info.gameObject));
            keys.Add(("portrait",Picture(info,"Selected equipment",null,.04f,.03f,.25f,.20f,true)));
            Label(info,"name",.315f,.04f,.65f,.095f,31);
            Label(info,"quality",.315f,.14f,.65f,.075f,22);
            Label(info,"attributes",.07f,.27f,.87f,.39f,25,TextAlignmentOptions.TopLeft);
            Label(info,"wearer",.07f,.68f,.87f,.12f,23);
            Control(info,"apply",null,.05f,.84f,.54f,.113f,true);
            Control(info,"remove","remove",.615f,.84f,.335f,.113f);
            Label(surface,"status",.035f,.24f,.51f,.46f,25,TextAlignmentOptions.Center);
            Control(surface,"retry","retry",.185f,.59f,.20f,.085f);
            var dialog = Area(frame,"Equipment confirmation",0,0,1,1);
            Paint(dialog,null,Black(.73f),true);
            var box = Area(dialog,"Equipment action",.26f,.18f,.48f,.65f);
            Paint(box,BagArt("_box_equip_none"),new Color32(24,39,62,255),true,true);
            keys.Add(("dialog",dialog.gameObject));
            Label(box,"summary",.065f,.055f,.87f,.24f,28);
            var targets=InventoryScroll(box,"Hero choices",.065f,.31f,.87f,.46f);
            var vertical=targets.content.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.childControlHeight = false;
            vertical.childForceExpandHeight = false;
            vertical.spacing = 4f;
            targets.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            keys.Add(("targets",targets));
            var target=Action(box,"Hero choice template",null,23,0,0,.90f,.08f,out var title,BagArt("_btn_small_black"),Color.white);
            ((RectTransform)target.transform).sizeDelta=new Vector2(0,54);
            Bind((RectTransform)target.transform,("select",target),("title",title));
            keys.Add(("targetTemplate",target.gameObject));
            target.gameObject.SetActive(false);
            Label(box,"change",.065f,.315f,.87f,.43f,24,TextAlignmentOptions.TopLeft);
            Control(box,"cancel","cancel",.10f,.81f,.36f,.13f);
            Control(box,"confirm","confirm",.54f,.81f,.36f,.13f,true);
            dialog.gameObject.SetActive(false);
            Bind(frame,keys.ToArray());
            outer.Add(("gearBrowser",frame.GetComponent<BindingMap>()));
            frame.gameObject.SetActive(false);
        }
    }
}
