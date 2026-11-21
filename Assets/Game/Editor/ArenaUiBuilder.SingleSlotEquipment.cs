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
            var title = Text(backArea, "Equipping", "home.gear.singleTitle", 34, 0, 0, .40f, 1, Color.white);
            title.rectTransform.offsetMin = new Vector2(((RectTransform)back.transform).rect.width + 4f * HomeDesignScale, 0);
            keys.Add(("title",title));
            DrawNativeTopBarIcons(compare, dimmed: true);
            var body = Area(compare, "Side by side comparison", 0, .091f, 1, .909f);
            Paint(Area(body, "Current equipment shade", 0, 0, .374f, 1), null, Black(.13f));
            Paint(Area(body, "Candidate list shade", .678f, 0, .322f, 1), null, Black(.56f));
            Paint(Area(body, "Candidate list edge", .6775f, .015f, .001f, .974f), null, new Color32(157,171,174,89));
            var current = Area(body, "Currently equipped", .077f, .009f, .288f, .975f);
            var candidate = Area(body, "Selected candidate", .376f, .009f, .289f, .975f);
            keys.Add(("current", SingleSlotColumn(current, "singleWorn", "singleNoWorn", false)));
            keys.Add(("candidate", SingleSlotColumn(candidate, "singleChoosing", "singleNoChoice", false)));

            keys.Add(("unequip", Action(current, "Remove", "home.gear.singleRemove", 26,
                .015f,.891f,.963f,.10f,out _,CommonArt("_btn_gold"),Color.white)));
            keys.Add(("equip", Action(candidate, "Wear", "home.gear.singleEquip", 26,
                .015f,.891f,.963f,.10f,out _,CommonArt("_btn_green"),Color.white)));
            Text(body, "Current sorting", "home.gear.singleSort", 24, .696f, .021f, .099f, .06f,
                new Color32(190,197,204,255), TextAlignmentOptions.Center);
            keys.Add(("capacity", Text(body, "Slot count", null, 24, .800f,.021f,.126f,.06f,
                new Color32(202,213,223,255), TextAlignmentOptions.Center)));
            var list = InventoryScroll(body, "Available equipment", .684f, .11f, .274f, .89f);
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
            var stat = Text(row, "Main property value", null, 34, .390f,.10f,.58f,.38f);
            var name = Text(row, "Candidate name", null, 23, .315f,.48f,.655f,.34f, new Color32(169,174,182,255));
            Bind(row, ("select",select), ("highlight",outline), ("card",card), ("statIcon",icon), ("stat",stat), ("name",name));
            row.gameObject.SetActive(false);
            keys.Add(("row",row.gameObject));

            var popup = Area(root,"Equipped item detail",0,0,1,1);
            keys.Add(("popup",popup.gameObject));
            keys.Add(("dismiss",Button(Area(popup,"Dismiss outside",0,0,1,1),null,Black(.72f))));
            // The original board artwork has transparent margins outside its visible corners.
            var panel = Area(popup,"Equipment details",.352f,.034f,.296f,.87f);
            keys.Add(("popupPlate",Paint(panel,null,Color.white,true)));
            keys.Add(("popupItem",SingleSlotColumn(Area(panel,"Worn item",.091f,.064f,.818f,.85f),null,null,true)));
            keys.Add(("popupRemove",Action(panel,"Remove equipment","home.gear.singleRemove",24,
                .11f,.872f,.242f,.081f,out _,BagArt("_btn_small_black"),Color.white)));
            keys.Add(("change",Action(panel,"Change equipment","home.gear.singleChange",24,
                .369f,.872f,.242f,.081f,out _,BagArt("_btn_small_black"),Color.white)));
            Text(popup,"Close hint","home.gear.singleDismiss",22,.32f,.931f,.36f,.05f,
                new Color32(180,186,193,255),TextAlignmentOptions.Center);
            string[] grades = { "normal","good","rare","heroic","epic" };
            var catalog = Area(root,"Popup plates",0,0,.01f,.01f);
            for (int i=0; i<grades.Length; ++i)
                keys.Add(("plate_"+grades[i], Picture(catalog,grades[i],InventoryArt("Equipment/_box_equip_"+(i+1)),0,0,1,1)));
            catalog.gameObject.SetActive(false);
            keys.Add(("feedback",Text(root,"Request feedback",null,22,.315f,.77f,.39f,.07f,
                new Color32(255,216,138,255),TextAlignmentOptions.Center,true)));
            keys.Add(("retry",Action(root,"Reload inventory","home.gear.retry",23,
                .437f,.841f,.126f,.06f,out _,BagArt("_btn_small_black"),Color.white)));
            Bind(root,keys.ToArray());
            Save(root.gameObject,SingleSlotPath);
            var source = Load<GameObject>(SingleSlotPath);
            UIViewRegistry.ValidatePrefab<SingleSlotEquipment>(source);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source,frame);
            instance.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            parentKeys.Add(("singleSlot",instance.GetComponent<BindingMap>()));
        }

        private static BindingMap SingleSlotThumbnail(RectTransform parent, float x, float y, float w, float h)
        {
            var holder = Area(parent,"Square equipment art",x,y,w,h);
            var square = Area(holder,"Square",0,0,1,1);
            square.gameObject.SetActive(false);
            var aspect = square.gameObject.AddComponent<AspectRatioFitter>();
            // In edit-mode None measures width/height, both zero before the prefab is on a canvas.
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1;
            square.gameObject.SetActive(true);
            var card = InstantiateEquipmentItemCard(square,"Equipment thumbnail");
            var rect = (RectTransform)card.transform;
            Place(rect,0,0,1,1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            // Single-slot rows carry the value beside the artwork, so use the square portion of the common card.
            var select = card.Require<GameButton>("select");
            select.interactable = false;
            select.targetGraphic.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(select);
            PrefabUtility.RecordPrefabInstancePropertyModifications(select.targetGraphic);
            foreach (var key in new[] { "icon", "gradeFrame", "setIcon", "wearerIcon", "level", "badge" })
            {
                var child = key == "level" ? card.Require<GameText>(key).rectTransform :
                    key == "badge" ? (RectTransform)card.Require<BindingMap>(key).transform : card.Require<Image>(key).rectTransform;
                if (key == "icon" || key == "gradeFrame") Place(child,0,0,1,1);
                else if (key == "setIcon") Place(child,.69f,.65f,.36f,.38f);
                else if (key == "wearerIcon") Place(child,-.055f,.65f,.42f,.43f);
                else if (key == "level") Place(child,.07f,.035f,.60f,.24f);
                else Place(child,.64f,-.05f,.44f,.28f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child);
            }
            var footer = card.Require<GameText>("stat").transform.parent.gameObject;
            footer.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(footer);
            return card;
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
                Text(detail,"Item state","home.gear."+heading,22,.019f,0,.83f,.035f,accent);
            }
            keys.Add(("card",SingleSlotThumbnail(detail,.016f,popup ? 0 : .046f,popup ? .245f : .21f,popup ? .166f : .147f)));
            float textX=popup ? .272f : .255f;
            keys.Add(("grade",Text(detail,"Quality and slot",null,25,textX,popup ? .014f : .047f,1-textX,.045f)));
            keys.Add(("name",Text(detail,"Equipment name",null,27,textX,popup ? .059f : .092f,1-textX,.061f)));
            keys.Add(("owner",Text(detail,"Equipped by",null,21,.02f,.201f,.96f,.05f,new Color32(163,172,182,255))));
            float mainY=popup ? .304f : .319f, setY=popup ? .726f : .698f;
            Paint(Area(detail,"Main property band",.005f,mainY,.984f,.069f),null,new Color32(105,119,147,17));
            if (!popup) keys.Add(("mainIcon",Picture(detail,"Main property icon",null,.022f,mainY+.009f,.068f,.05f,true)));
            BindingMap StatRow(string name,float x,float top,float height,int size)
            {
                var obj=AddEquipmentStatRow(detail,name);
                var rect=(RectTransform)obj.transform;
                Place(rect,x,top,.966f-x,height);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                var map=obj.GetComponent<BindingMap>();
                foreach(string binding in new[]{"label","value"})
                {
                    var label=map.Require<GameText>(binding);
                    label.fontSize=size;
                    label.fontStyle=name == "main" ? FontStyles.Bold : FontStyles.Normal;
                    label.color=name == "main" ? Color.white : new Color32(200,204,210,255);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                }
                return map;
            }
            keys.Add(("main",StatRow("main",popup ? .02f : .099f,mainY,.069f,31)));
            for(int i=0;i<4;i++) keys.Add(("sub"+i,StatRow("sub"+i,.018f,mainY+.087f+i*.041f,.04f,24)));
            Paint(Area(detail,"Set band",.005f,setY,.984f,.066f),null,new Color32(78,82,98,10));
            keys.Add(("setIcon",Picture(detail,"Set icon",null,.021f,setY+.008f,.072f,.05f,true)));
            keys.Add(("set",Text(detail,"Equipped set pieces",null,25,.102f,setY,.87f,.066f)));
            Bind(root,keys.ToArray());
            return root.GetComponent<BindingMap>();
        }
    }
}
