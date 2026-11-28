using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        // Positions are measured in the reference's 1232 x 600 usable viewport.
        // Height-scaled rail and card sizes stay fixed as the visible list width changes.
        const float ShopScale=1080f/600f;

        static RectTransform ShopBox(RectTransform parent,string name,float x,float y,float w,float h,bool right=false)
        {
            var rect=Area(parent,name,0,0,0,0);
            rect.anchorMin=rect.anchorMax=right?Vector2.one:Vector2.up;
            rect.pivot=Vector2.up;
            rect.anchoredPosition=new Vector2(x,-y)*ShopScale;
            rect.sizeDelta=new Vector2(w,h)*ShopScale;
            return rect;
        }

        static ScrollRect BuildArenaShopLayout(RectTransform root,List<(string Key,Object Target)> keys)
        {
            var background=Area(root,"Background extent",0,0,1,1);
            background.offsetMin=new Vector2(-100f*HomeDesignScale,0);
            Cover(background,"Original shop background",ShopArt("renew_shop_bg"));
            ((RectTransform)background.GetChild(0)).pivot=new Vector2(0,.5f);
            // shop_exchange_base_renew's bg_clip continues the background beside the list.
            Paint(ShopBox(root,"Exchange list edge",202,116.67f,53.33f,483.33f),ShopArt("renew_shop_bg_clipping"),Color.white);
            var top=Area(root,"Header",0,0,1,55f/600);
            Paint(top,null,Black(.45f));
            NativeRule(top,"Bottom rule",0,.984f,1,.016f,new Color32(82,101,132,110));
            keys.Add(("back",SharedHeaderBack(top)));
            Text(ShopBox(top,"Title",55,4,83,47),"Label","shop.title",37,0,0,1,1,Color.white);
            var update=InventoryAction(ShopBox(top,"Update",151,13,98,30),"Update button","shop.nav.refresh",21,
                0,0,1,1,out var refreshLabel,"Black");
            Picture((RectTransform)update.transform,"Refresh",Art("cm_icon_etcrepeat"),.06f,.20f,.19f,.60f,true).color=new Color32(150,150,150,255);
            Place(refreshLabel.rectTransform,.25f,0,.65f,1);
            refreshLabel.color=new Color32(150,150,150,255);
            RecordInventoryButton(update.gameObject);
            keys.Add(("refresh",update));
            var (more,bag,bell)=BuildNativeTopBarIcons(top);
            keys.Add(("more",more)); keys.Add(("bag",bag)); keys.Add(("bell",bell));
            var money=HomeRightRect(top,"Header currencies",-319,33,0,56,new Vector2(1,.5f));
            var currencyRow=money.gameObject.AddComponent<HorizontalLayoutGroup>();
            currencyRow.childAlignment=TextAnchor.MiddleRight;
            currencyRow.childControlWidth=currencyRow.childControlHeight=true;
            currencyRow.childForceExpandWidth=currencyRow.childForceExpandHeight=false;
            currencyRow.spacing=3f;
            money.gameObject.AddComponent<ContentSizeFitter>().horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            void Currency(string key,Sprite icon,bool plus)
            {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(HeaderTokenPrefab),money);
                go.name=key;
                var map=go.GetComponent<BindingMap>();
                var token=map.Require<Image>("icon");
                token.sprite=icon;
                map.Require<GameObject>("plus").SetActive(plus);
                if(plus)
                {
                    var shortcut=Button(Area(token.rectTransform,"Currency shortcut",0,0,1,1),null,Color.clear);
                    keys.Add((key+"Shop",shortcut));
                }
                keys.Add((key,map.Require<GameText>("value")));
                RecordInventoryButton(go);
            }
            Currency("balance",ShopArt("token_pvpgold"),false);
            Currency("gold",InventoryButtonArt("token_gold"),true);
            Currency("crystals",SummonArt("token_crystal"),true);

            var entries=new[]{("featured1","icon_menu_seasonalpackage"),("featured2","icon_menu_seasonalpackage"),
                ("packages","icon_menu_package"),("charge","icon_menu_heavenlystone"),("store","icon_menu_store"),
                ("exchange","icon_menu_normal"),("skin","icon_menu_skin"),("events","icon_menu_event_shop")};
            var subdued=new Color32(159,164,177,160);
            for(int i=0;i<entries.Length;i++)
            {
                var (key,sprite)=entries[i];
                var row=ShopBox(root,"Shop rail "+key,0,65+i*65.7f-(i==7?6:0),217,65.7f);
                bool selected=key=="exchange";
                if(selected)
                {
                    var glow=Picture(row,"Selected exchange",ShopArt("_tap_select_blue3"),-.078f,0,1.06f,1);
                    glow.rectTransform.localScale=new Vector3(-1,1,1);
                }
                var symbol=Picture(row,"Icon",key=="store"?TowerArt(sprite):ShopArt(sprite),.080f,.272f,.142f,.455f,true);
                symbol.color=selected?Color.white:subdued;
                Text(row,"Title","shop.nav."+key,29,.266f,0,.71f,1,selected?Color.white:subdued);
                if(!selected) keys.Add((key,Button(row,null,Color.clear)));
            }
            var tabs=new[]{"arena","glory","trials","constellation"};
            for(int i=0;i<tabs.Length;i++)
            {
                string key=tabs[i];
                var tab=ShopBox(root,"Exchange tab "+key,259+i*146.7f,55,150,61);
                if(i==0) Picture(tab,"Selected arena",BagArt("_tap_select_blue2"),0,0,1,1);
                Text(tab,"Label",i==0?"tower.title":"shop.nav."+key,27,0,.13f,1,.80f,
                    i==0?Color.white:new Color32(174,179,191,202),TextAlignmentOptions.Center);
                if(i>0) keys.Add((key,Button(tab,null,Color.clear)));
            }
            var content=Area(root,"Exchange content",0,0,1,1);
            content.offsetMin=new Vector2(254*ShopScale,0);
            NativeRule(content,"Tab divider",0,115f/600,1,1f/600,new Color32(116,140,185,105));
            var list=Scroll(content,"Shop offers",0,128f/600,1,399f/600,true);
            Object.DestroyImmediate(list.content.GetComponent<HorizontalLayoutGroup>());
            var grid=list.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint=GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount=2;
            grid.startAxis=GridLayoutGroup.Axis.Vertical;
            grid.cellSize=new Vector2(158.6f,194.4f)*ShopScale;
            grid.spacing=new Vector2(9.5f,9.6f)*ShopScale;
            grid.padding=new RectOffset(Mathf.RoundToInt(1.5f*ShopScale),Mathf.RoundToInt(150*ShopScale),0,0);
            NativeRule(content,"Footer rule",0,538f/600,1,1f/600,new Color32(95,138,209,102));
            var note=ShopBox(content,"Season information",9,550,220,37);
            var info=Picture(note,"Information",ShopArt("icon_menu_info_2"),0,.15f,.115f,.69f,true);
            info.color=new Color32(255,255,255,153);
            Text(note,"Season note","shop.seasonNote",21,.143f,0,.85f,1,new Color32(240,240,240,160));
            Text(ShopBox(content,"Catalog edition",-202,548,188,40,true),"Season","shop.seasonLabel",26,
                0,0,1,1,new Color32(240,240,240,235),TextAlignmentOptions.MidlineRight);
            keys.Add(("status",Text(content,"Shop feedback",null,23,.29f,.912f,.40f,.068f,Mist,TextAlignmentOptions.Center)));
            return list;
        }
    }
}
