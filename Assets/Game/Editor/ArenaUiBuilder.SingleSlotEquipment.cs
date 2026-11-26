using System.Collections.Generic;
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
        private const string SingleSlotPath = Home + "Res/Prefab/SingleSlotEquipment.prefab";

        private static void BuildSingleSlotEquipment(RectTransform frame, List<(string Key, Object Target)> parentKeys)
        {
            var root = Root("SingleSlotEquipment");
            var keys = new List<(string, Object)>();
            var compare = Area(root, "Single slot comparison", 0, 0, 1, 1);
            keys.Add(("comparison", compare.gameObject));
            Paint(compare, InventoryArt("base_unit_hero"), Color.white, true);
            var backArea = Area(compare, "Equipment title", 0, 0, 1, .092f);
            Paint(backArea, null, Black(.33f));
            Paint(Area(backArea,"Header rule",0,.984f,1,.016f),null,new Color32(121,140,168,55));
            var back = SharedHeaderBack(backArea);
            keys.Add(("back",back));
            var title = Text(backArea, "Equipping", "home.gear.singleTitle", 37, 0, 0, .40f, 1, Color.white);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.offsetMin = new Vector2(((RectTransform)back.transform).rect.width + 4f * HomeDesignScale, 0);
            keys.Add(("title",title));
            DrawNativeTopBarIcons(compare, dimmed: true);
            var body = Area(compare, "Side by side comparison", 0, .091f, 1, .909f);
            Paint(Area(body, "Current equipment shade", 0, 0, .374f, 1), null, Black(.13f));
            Paint(Area(body, "Candidate list shade", .678f, 0, .322f, 1), null, Black(.56f));
            Paint(Area(body, "Candidate list edge", .6775f, .015f, .001f, .974f), null, new Color32(157,171,174,89));
            var current = Area(body, "Currently equipped", .080f, .009f, .286f, .975f);
            var candidate = Area(body, "Selected candidate", .381f, .009f, .284f, .975f);
            keys.Add(("current", SingleSlotColumn(current, "singleWorn", "singleNoWorn", false)));
            keys.Add(("candidate", SingleSlotColumn(candidate, "singleChoosing", "singleNoChoice", false)));

            keys.Add(("unequip", InventoryAction(current, "Remove", "home.gear.singleRemove", 30,
                .013f,.889f,.49f,.110f,out var unequipCaption,"Gold")));
            keys.Add(("equip", InventoryAction(candidate, "Wear", "home.gear.singleEquip", 30,
                .013f,.889f,.49f,.110f,out var equipCaption,"Green")));
            keys.Add(("currentEnhance",InventoryAction(current,"Enhance worn equipment","home.gear.enhance",30,
                .52f,.889f,.47f,.110f,out _)));
            keys.Add(("candidateEnhance",InventoryAction(candidate,"Enhance selected equipment","home.gear.enhance",30,
                .52f,.889f,.47f,.110f,out _)));
            unequipCaption.fontStyle = equipCaption.fontStyle = FontStyles.Bold;
            unequipCaption.characterSpacing = equipCaption.characterSpacing = -6;
            Text(body, "Current sorting", "home.gear.singleSort", 27, .696f, .021f, .099f, .06f,
                new Color32(190,197,204,255), TextAlignmentOptions.Center);
            keys.Add(("capacity", Text(body, "Slot count", null, 27, .800f,.021f,.126f,.06f,
                new Color32(202,213,223,255), TextAlignmentOptions.Center)));
            var list = InventoryScroll(body, "Available equipment", .684f, .11f, .247f, .89f);
            var layout = list.content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 4;
            layout.padding = new RectOffset(4,5,4,6);
            list.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            keys.Add(("candidates", list));
            var row = Area(body, "Single slot candidate template", 0, 0, .26f, .14f);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 150;
            var select = Button(row, null, Color.clear);
            var outline = Picture(row, "Selected row", CommonArt("_select"), 0,0,1,1);
            outline.type = Image.Type.Sliced;
            outline.pixelsPerUnitMultiplier = 1.75f;
            outline.enabled = false;
            var card = SingleSlotThumbnail(row, .015f, .04f, .28f, .90f);
            var icon = Picture(row, "Main property symbol", null, .315f,.17f,.065f,.24f,true);
            var stat = Text(row, "Main property value", null, 39, .390f,.10f,.58f,.38f);
            stat.fontStyle = FontStyles.Bold;
            var name = Text(row, "Candidate name", null, 25, .315f,.48f,.655f,.34f, new Color32(185,187,193,255));
            Bind(row, ("select",select), ("highlight",outline), ("card",card), ("statIcon",icon), ("stat",stat), ("name",name));
            row.gameObject.SetActive(false);
            keys.Add(("row",row.gameObject));

            var popup = Area(root,"Equipped item detail",0,0,1,1);
            keys.Add(("popup",popup.gameObject));
            keys.Add(("dismiss",Button(Area(popup,"Dismiss outside",0,0,1,1),null,Black(.72f))));
            var bounds = Area(popup, "Detail safe area", .034f,.058f,.932f,.831f);
            var panel = Area(bounds,"Equipment details",0,0,1,1);
            // Preserve the original 1080p design card when the available window changes.
            // PlaceHeroControl converts the 720p source units to fixed 585x900 Canvas units.
            PlaceHeroControl(panel, .5f, .5f, 390, 600);
            var nativeFrame = Area(panel,"Native quality frame",0,0,1,1);
            // Full quality sprites include their original transparent outer shadow.
            // Expand that margin outside the content box so the text retains its native inset.
            nativeFrame.offsetMin = -Vector2.one * 31f * HomeDesignScale;
            nativeFrame.offsetMax = Vector2.one * 31f * HomeDesignScale;
            var popupPlate = Paint(nativeFrame, InventoryArt("Equipment/_box_equip_1"), Color.white, true, true);
            popupPlate.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            keys.Add(("popupPlate", popupPlate));
            var qualitySources = Area(root,"Popup quality graphics",0,0,.01f,.01f);
            string[] qualities = { "normal", "good", "rare", "heroic", "epic" };
            for (int i=0; i<qualities.Length; i++)
                keys.Add(("popup_" + qualities[i], Picture(qualitySources, qualities[i],
                    InventoryArt("Equipment/_box_equip_" + (i+1)),0,0,1,1)));
            qualitySources.gameObject.SetActive(false);
            keys.Add(("popupItem",SingleSlotColumn(Area(panel,"Worn item",.060f,.055f,.880f,.832f),null,null,true)));
            keys.Add(("popupRemove",InventoryAction(panel,"Remove equipment","home.gear.singleRemove",27,
                .052f,.857f,.313f,.089f,out var removeCaption,"Black")));
            keys.Add(("change",InventoryAction(panel,"Change equipment","home.gear.singleChange",27,
                .387f,.857f,.23f,.089f,out var changeCaption,"Black")));
            keys.Add(("popupEnhance",InventoryAction(panel,"Enhance worn item","home.gear.enhance",27,
                .641f,.857f,.313f,.089f,out _)));
            foreach (var caption in new[] { removeCaption, changeCaption })
            {
                caption.fontStyle = FontStyles.Bold;
                caption.enableAutoSizing = true;
                caption.fontSizeMin = 16;
                caption.fontSizeMax = 27;
            }
            Text(popup,"Close hint","home.gear.singleDismiss",22,.32f,.931f,.36f,.05f,
                new Color32(180,186,193,255),TextAlignmentOptions.Center);
            keys.Add(("feedback",Text(root,"Request feedback",null,22,.315f,.77f,.39f,.07f,
                new Color32(255,216,138,255),TextAlignmentOptions.Center,true)));
            keys.Add(("retry",Action(root,"Reload inventory","home.gear.retry",23,
                .437f,.841f,.126f,.06f,out _,BagArt("_btn_small_black"),Color.white)));
            var textMaterial = Load<Material>(Home + "Res/Materials/EquipmentDetailText.mat");
            foreach (var label in root.GetComponentsInChildren<GameText>(true))
            {
                if (label.font != font || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(label) == EquipmentItemCardPath)
                    continue;
                label.fontSharedMaterial = textMaterial;
                if (PrefabUtility.IsPartOfPrefabInstance(label))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            }
            Bind(root,keys.ToArray());
            Save(root.gameObject,SingleSlotPath);
            var source = Load<GameObject>(SingleSlotPath);
            UIViewRegistry.ValidatePrefab<SingleSlotEquipment>(source);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source,frame);
            instance.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            parentKeys.Add(("singleSlot",instance.GetComponent<BindingMap>()));
        }

        private static BindingMap SingleSlotColumn(RectTransform root, string heading, string emptyKey, bool popup)
        {
            var keys = new List<(string,Object)>();
            var empty = Area(root,"No equipment",0,.375f,1,.17f);
            if (emptyKey != null)
            {
                Picture(empty,"Information",Art("cm_icon_etcinfor"),.465f,.00f,.07f,.32f,true).color = new Color32(142,153,171,150);
                Text(empty,"Empty message","home.gear."+emptyKey,22,0,.32f,1,.51f,
                    new Color32(151,159,174,230),TextAlignmentOptions.Center);
            }
            keys.Add(("empty",empty.gameObject));
            var detail = Area(root,"Visible item",0,0,1,1);
            keys.Add(("detail",detail.gameObject));
            if (!popup)
            {
                var accent = heading == "singleWorn" ? new Color32(210,154,62,255) : new Color32(97,193,40,255);
                Paint(Area(detail,"Selection side rule",0,0,.007f,.19f),null,accent);
                Text(detail,"Item state","home.gear."+heading,24,.019f,0,.83f,.038f,accent);
            }
            keys.Add(("card",SingleSlotThumbnail(detail,.016f,popup ? 0 : .038f,popup ? .245f : .223f,popup ? .166f : .156f)));
            float textX=popup ? .272f : .255f;
            var grade = Text(detail,"Quality and slot",null,28,textX,popup ? .012f : .044f,1-textX,.049f);
            var name = Text(detail,"Equipment name",null,29,textX,popup ? .053f : .083f,1-textX,.057f);
            keys.Add(("grade",grade));
            keys.Add(("name",name));
            keys.Add(("description",Text(detail,"Item description",null,24,.018f,popup ? .191f : .206f,.956f,.097f,
                new Color32(163,167,180,255), TextAlignmentOptions.TopLeft, true)));
            float mainY = popup ? .300f : .315f;
            float setY = popup ? .764f : .697f;
            Color WhiteBand(byte coverage) => new Color(1,1,1,QualitySettings.activeColorSpace == ColorSpace.Linear
                ? Mathf.GammaToLinearSpace(coverage/255f) : coverage/255f);
            Paint(Area(detail,"Main property band",.005f,mainY,.984f,.069f),Art("_white"),WhiteBand(10));
            if (!popup) keys.Add(("mainIcon",Picture(detail,"Main property icon",null,.022f,mainY+.009f,.068f,.05f,true)));
            BindingMap StatRow(RectTransform parent, string rowName, float x, float top, float width, float height,
                int size, bool primary)
            {
                var obj=AddEquipmentStatRow(parent,rowName);
                var rect=(RectTransform)obj.transform;
                Place(rect,x,top,width,height);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                var map=obj.GetComponent<BindingMap>();
                foreach(string binding in new[]{"label","value"})
                {
                    var label=map.Require<GameText>(binding);
                    label.fontSize=primary && binding == "value" ? size + 3 : size;
                    label.fontStyle=primary ? FontStyles.Bold : FontStyles.Normal;
                    label.color=primary ? Color.white : new Color32(205,208,217,255);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
                return map;
            }
            float mainX = popup ? .02f : .099f;
            keys.Add(("main",StatRow(detail,"main",mainX,mainY,.966f-mainX,.069f,35,true)));
            // Four rolled lines at 01:34 are 24–25 px apart in the 720 px reference crop.
            var stats = EquipmentStatList(detail,.018f,mainY+.083f,.948f,.28f);
            for (int i=0; i<4; ++i)
            {
                string id = "sub" + i;
                var sub = StatRow(stats,id,0,0,1,1,27,false);
                sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 36.5f;
                keys.Add((id,sub));
            }
            var score = AddEquipmentScoreRow(stats,0,0,1,1);
            score.gameObject.AddComponent<LayoutElement>().preferredHeight = 45;
            foreach (var binding in new[] { "label", "value" })
            {
                var text = score.Require<GameText>(binding);
                text.fontSize = 29;
                text.fontStyle = FontStyles.Normal;
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            keys.Add(("score", score));
            Paint(Area(detail,"Set band",.005f,setY,.984f,.066f),Art("_white"),WhiteBand(5));
            keys.Add(("setIcon",Picture(detail,"Set icon",null,.018f,setY+.005f,.080f,.056f,true)));
            var set = Text(detail,"Equipped set pieces",null,29,.103f,setY,.875f,.066f);
            set.richText = true;
            keys.Add(("set",set));
            Bind(root,keys.ToArray());
            return root.GetComponent<BindingMap>();
        }
    }
}
