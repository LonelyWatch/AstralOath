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
            BuildEquipmentStatRow();
            BuildEquipmentItemCard();
            var bag = (GameObject)outer.First(pair => pair.Key == "bag").Target;
            var help = (RectTransform)bag.transform.Find("Bag help glyph");
            // The bag caption starts at a fixed offset from Back on every aspect ratio.
            help.anchorMin = new Vector2(0,help.anchorMin.y);
            help.anchorMax = new Vector2(0,help.anchorMax.y);
            help.offsetMin = new Vector2(165f * BagDesignScale,0);
            help.offsetMax = new Vector2(228f * BagDesignScale,0);
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
            void Catalog(string key, string sprite)
            {
                keys.Add((key, Picture(catalog, key, InventoryArt("Equipment/" + sprite), 0, 0, 1, 1, true)));
            }
            string[] grades = { "normal", "good", "rare", "heroic", "epic" };
            for (int i = 0; i < grades.Length; ++i)
            {
                Catalog("grade_" + grades[i], "_itembg_equip_" + (i + 1));
                Catalog("detail_" + grades[i], "_box_equip_" + (i + 1) + "b");
                Catalog("managePlate_" + grades[i], "_box_equip_" + (i + 1));
            }
            Catalog("set_speed", "icon_set_speed");
            Catalog("set_critical", "icon_set_cri");
            foreach (var pair in new[] { ("attack", "att"), ("health", "max_hp"), ("defense", "def"),
                         ("speed", "speed"), ("critChanceBp", "cri"), ("critDamageBp", "cri_dmg"),
                         ("effectivenessBp", "acc"), ("resistanceBp", "res") })
                Catalog("stat_" + pair.Item1, "cm_icon_stat_" + pair.Item2);
            catalog.gameObject.SetActive(false);
            BuildEquipmentManager(frame,keys,slots,authored);
            BuildOrdinaryEquipmentBag(frame, keys, slots, authored);
            BuildSingleSlotEquipment(frame,keys);
            var dialog = Area(frame,"Equipment confirmation",0,0,1,1);
            Paint(dialog,null,Black(.73f),true);
            var box = Area(dialog,"Equipment action",.26f,.18f,.48f,.65f);
            Paint(box,BagArt("_box_equip_none"),new Color32(24,39,62,255),true,true);
            keys.Add(("dialog",dialog.gameObject));
            keys.Add(("ordinaryQuote",box.gameObject));
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
            BuildManagementQuote(dialog,keys);
            dialog.gameObject.SetActive(false);
            Bind(frame,keys.ToArray());
            outer.Add(("gearBrowser",frame.GetComponent<BindingMap>()));
            frame.gameObject.SetActive(false);
        }

        private static void BuildOrdinaryEquipmentBag(RectTransform frame, List<(string Key, Object Target)> keys,
            string[] slots, string[] authored)
        {
            var bag = Area(frame, "Ordinary equipment bag", 0, 0, 1, 1);
            var controls = new List<(string Key, Object Target)>();
            var filters = Area(bag,"Equipment slot filters",0,83f/720,.473f,58f/720);
            filters.offsetMin = new Vector2(145f * BagDesignScale,0);
            var rule = Area(bag,"Filter rule",0,136f/720,.683f,10f/720);
            rule.offsetMin = new Vector2(133f * BagDesignScale,0);
            BagImage(rule,BagArt("_bar_2"),new Color32(255,255,255,7),true);
            for (int i = 0; i <= slots.Length; ++i)
            {
                string key = i == 0 ? "all" : slots[i - 1];
                float column = 1f/(slots.Length+1);
                var hit = Area(filters,"Slot filter " + key,i*column,0,column,1);
                controls.Add(("filter_" + key, Button(hit, null, Color.clear)));
                var icon = i == 0 ? BagArt("cm_icon_all") : RosterArt("cm_icon_par" + authored[i - 1]);
                Picture(hit,"Slot type",icon,.27f,.32f,.46f,.56f,true).color = BagInactiveIcon;
                var active = Picture(hit,"Selected slot",BagArt("_tap_select_blue2"),.02f,.07f,.96f,.93f);
                Picture(active.rectTransform,"Selected type",icon,.26f,.27f,.48f,.60f,true);
                active.gameObject.SetActive(i == 0);
                controls.Add(("active_" + key,active));
            }
            var count = Area(bag,"Inventory capacity",.801f,87f/720,.093f,41f/720);
            BagImage(count, BagArt("_btn_small_black"), BagPlateTint, true);
            Picture(count, "Equipment capacity icon", InventoryArt("icon_menu_equip"), .03f, .14f, .24f, .70f, true);
            var countText = Text(count, "Count", null, 26, .29f, .02f, .66f, .96f, Color.white, TextAlignmentOptions.Center);
            controls.Add(("capacity",countText));
            var sort = Area(bag,"Order by item level",.703f,87f/720,.095f,41f/720);
            controls.Add(("sort",Button(sort,BagArt("_btn_small_black"),BagPlateTint)));
            Picture(sort,"Sort icon",BagArt("_sorting2"),.07f,.2f,.18f,.58f,true);
            var sortText = Text(sort,"Item level order",null,24,.27f,0,.70f,1,Color.white,TextAlignmentOptions.Center);
            controls.Add(("sortLabel",sortText));
            foreach (var text in new[] {countText,sortText})
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = 13;
                text.fontSizeMax = text.fontSize;
            }

            // Source recording at 720 px high: x=152 for the first card, x=1055 for the detail.
            // The list flows to the next row as the available width decreases.
            var list = InventoryScroll(bag, "Owned equipment grid", 0, .204f, .6835f, .796f);
            ((RectTransform)list.transform).offsetMin = new Vector2(148f * BagDesignScale,0);
            var grid = list.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(86,111) * BagDesignScale;
            grid.spacing = new Vector2(13,22) * BagDesignScale;
            grid.padding = new RectOffset(4, 3, 7, 4);
            list.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            controls.Add(("items",list));
            keys.Add(("bagItems",list));

            var info = Area(bag, "Selected equipment detail", .713f, .186f, .264f, .814f);
            controls.Add(("detail",info.gameObject));
            controls.Add(("plate",BagImage(info,InventoryArt("Equipment/_box_equip_1b"),Color.white,true)));
            var preview = SingleSlotThumbnail(info,.105f,.064f,.21f,.151f);
            controls.Add(("thumbnail",preview));
            controls.Add(("portrait",preview.Require<Image>("icon")));
            controls.Add(("quality",Text(info,"Grade and slot",null,25,.34f,.065f,.54f,.046f,new Color32(246,196,119,255))));
            controls.Add(("name",Text(info,"Item name",null,29,.34f,.113f,.55f,.064f,Color.white)));
            controls.Add(("wearer",Text(info,"Wearer",null,20,.11f,.211f,.78f,.075f,new Color32(171,164,159,255))));
            var statBand = new Color32(82,81,77,18);
            Paint(Area(info,"Main stat highlight",.095f,.32f,.81f,.075f),null,statBand);
            void StatRow(string key, float top, float height, bool primary)
            {
                var row = AddEquipmentStatRow(info,key);
                Place((RectTransform)row.transform,.119f,top,.757f,height);
                var map = row.GetComponent<BindingMap>();
                foreach (string part in new[] { "label", "value" })
                {
                    var text = map.Require<GameText>(part);
                    text.fontSize = primary ? 27 : 23;
                    text.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
                    text.color = primary ? Color.white : new Color32(216,214,211,255);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                keys.Add((key,map));
            }
            StatRow("bagMain",.32f,.075f,true);
            for (int i = 0; i < 4; ++i) StatRow("bagSub" + i,.405f + .045f*i,.045f,false);
            Paint(Area(info,"Set band",.095f,.704f,.81f,.066f),null,statBand);
            controls.Add(("setIcon",Picture(info,"Set icon",null,.119f,.712f,.064f,.047f,true)));
            controls.Add(("set",Text(info,"Set name and count",null,23,.190f,.705f,.68f,.066f,Color.white)));
            var unequip = Action(info,"Unequip", "home.gear.remove",24,.06f,.883f,.40f,.08f,out _,BagArt("_btn_small_black"),Color.white);
            var apply = Action(info,"Equip",null,24,.51f,.883f,.43f,.08f,out var applyText,CommonArt("_btn_green"),Color.white);
            controls.Add(("apply",apply));
            controls.Add(("applyLabel",applyText));
            controls.Add(("remove",unequip));
            controls.Add(("status",Text(bag,"Empty or unavailable",null,26,.12f,.35f,.56f,.18f,Color.white,TextAlignmentOptions.Center)));
            var retry = Action(bag,"Retry", "home.gear.retry",24,.32f,.53f,.14f,.066f,out _,BagArt("_btn_small_black"),Color.white);
            controls.Add(("retry",retry));
            Bind(bag,controls.ToArray());
            keys.Add(("bagPane",bag.GetComponent<BindingMap>()));
        }
    }
}
