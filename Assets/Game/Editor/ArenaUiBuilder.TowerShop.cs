using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Shop;
using AstralOath.Client.Arena;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        const string ShopModule="Assets/Game/Modules/Shop/";
        const string ShopWindowPath=ShopModule+"Res/Prefab/ShopWindow.prefab";
        const string ShopItemPath=ShopModule+"Res/Prefab/ShopItem.prefab";
        const string ArenaTowerPath=Arena+"Res/Prefab/ArenaTower.prefab";
        static Sprite TowerArt(string name) => Load<Sprite>(Arena+"Res/Textures/Tower/"+name+".png");
        static Sprite ShopArt(string name) => Load<Sprite>(ShopModule+"Res/Textures/Arena/"+name+".png");

        [MenuItem("Tools/游戏主页/生成竞技场关卡和交易所")]
        public static void GenerateTowerShop()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            labels=JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows.ToDictionary(x=>x.key,x=>x.text);
            BuildTowerShopAssets();
            Save(BuildHome(),Assets[0].Path);
            Save(BuildHomeMoreMenu(),HomeMoreMenuPrefabPath);
            Save(BuildDirectory(),Assets[1].Path);
            ConfigureCollectors();
            UIViewRegistry.ValidatePrefab<ArenaTower>(Load<GameObject>(ArenaTowerPath));
            UIViewRegistry.ValidatePrefab<ShopWindow>(Load<GameObject>(ShopWindowPath));
            UIViewRegistry.ValidatePrefab<ShopItem>(Load<GameObject>(ShopItemPath));
        }

        private static void BuildTowerShopAssets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShopWindowPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Save(BuildShopItem(), ShopItemPath);
            Save(BuildShopWindow(), ShopWindowPath);
            Save(BuildArenaTower(), ArenaTowerPath);
        }

        public static void GenerateTowerAndChannel()
        {
            GenerateChannelChat();
            GenerateTowerShop();
            AssetDatabase.SaveAssets();
            Debug.Log("ASTRAL_TOWER_AND_CHANNEL_GENERATED");
        }

        static void TowerSocialEntries(RectTransform top)
        {
            var mail=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(MailEntryPath),top);
            Place((RectTransform)mail.transform,.900f,.12f,.036f,.77f);
            var chat=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(ChatEntryPath),top);
            Place((RectTransform)chat.transform,.951f,.12f,.036f,.77f);
        }

        static GameObject BuildArenaTower()
        {
            var root = Root("ArenaTower");
            Paint(Area(root,"Background",0,0,1,1),null,new Color32(31,34,40,255));
            Cover(root,"Native directory season",Art("pvp_rta_ss21_d"));
            Paint(Area(root,"Directory shading",0,0,1,1),null,Black(.20f));
            var keys = new List<(string,Object)>();
            var top = Area(root,"Header",0,0,1,.092f);
            Paint(top,null,Black(.65f));
            Text(top,"Title","tower.title",37,.049f,.09f,.35f,.82f);
            Paint(Area(top,"Bottom border",0,.98f,1,.015f),null,new Color32(126,119,111,115));
            keys.Add(("back",SharedHeaderBack(top)));
            TowerSocialEntries(top);

            var left = Area(root,"Season profile",0,.092f,.193f,.908f);
            Paint(Area(left,"Season shadow",0,0,.93f,1),null,Black(.72f));
            Picture(left,"Season edge",Art("_bg_black_gold_s"),0,.007f,1,1.009f);
            var gold = new Color32(181,150,108,255);
            Text(left,"Season","tower.rank_heading",33,.075f,.035f,.80f,.066f,gold,TextAlignmentOptions.Center);
            SeasonLine(left,"Season separator","_bar_1",.068f,.127f,.40f,.013f,new Color32(144,122,91,200));
            keys.Add(("rankIcon",Picture(left,"Rank emblem",null,.207f,.156f,.535f,.215f,true)));
            keys.Add(("rank",Text(left,"Rank label",null,26,.078f,.382f,.79f,.055f,new Color32(170,166,159,255),TextAlignmentOptions.Center)));
            keys.Add(("progress",Text(left,"Highest clear",null,32,.06f,.46f,.82f,.055f,Color.white,TextAlignmentOptions.Center)));
            SeasonLine(left,"Profile separator","_bar_4",.08f,.532f,.395f,.019f,new Color32(98,80,56,170));
            keys.Add(("currentStage",Text(left,"Progress target",null,26,.07f,.564f,.81f,.06f,Color.white,TextAlignmentOptions.Center)));
            keys.Add(("stageReward",Text(left,"First clear reward",null,23,.06f,.622f,.84f,.05f,gold,TextAlignmentOptions.Center)));
            var shop = CommonButton(left,"CancelButton","shop.arena",.075f,.817f,.805f,.094f);
            keys.Add(("shop",shop));
            Picture((RectTransform)shop.transform,"Store icon",TowerArt("icon_menu_store"),.055f,.20f,.17f,.60f,true);
            var library=Area(root,"Rank artwork",0,0,0,0);
            foreach(string rank in new[]{"bronze","silver","gold","master","challenger","champion","emperor","legend"})
                keys.Add(("rank."+rank,Picture(library,rank,TowerArt("icon_pvp_sa_league_"+rank),0,0,1,1,true)));
            for(int i=0;i<10;i++)
                keys.Add(("digit."+i,Picture(library,"Level digit "+i,CommonArt("EnhancementDigits/itxt_num"+i+"_b"),0,0,1,1,true)));
            foreach(string element in new[]{"fire","ice","wind","dark","light"})
                keys.Add(("element."+element,Picture(library,"Level "+element,TowerArt("game_hud_bar_"+element),0,0,1,1,true)));
            library.gameObject.SetActive(false);

            var tabs=Area(root,"Mode navigation",.814f,.092f,.186f,.908f);
            Paint(tabs,null,Blue);
            var active=Area(tabs,"Arena",0,0,1,.144f);
            Paint(active,null,new Color32(4,14,34,255));
            var nativeTab=Area(active,"Native selection",0,0,1,1);
            nativeTab.offsetMin=new Vector2(-33,0);
            Paint(nativeTab,Art("directory_tab"),Color.white,sliced:true).pixelsPerUnitMultiplier=2f/3f;
            Picture(active,"Opponent swords",Art("icon_menu_battle"),.075f,.23f,.14f,.54f,true);
            Text(active,"Arena","tower.title",34,.245f,.05f,.71f,.90f);
            foreach(var entry in new[]{("simulation","arena.mock",Art("icon_menu_battle_add")),("record","tower.record",TowerArt("icon_menu_pvplog"))})
            {
                var tab=Area(tabs,entry.Item1,0,entry.Item1=="record"?.288f:.144f,1,.144f);
                keys.Add((entry.Item1,Button(tab,null,Color.clear)));
                Picture(tab,"Icon",entry.Item3,.075f,.23f,.14f,.54f,true);
                Text(tab,"Label",entry.Item2,34,.245f,.05f,.71f,.90f);
            }
            var body=Area(root,"Stage directory",.203f,.096f,.597f,.89f);
            Picture(body,"Opponent heading icon",Art("icon_menu_battle_add"),0,.024f,.045f,.07f,true);
            Text(body,"Heading","tower.stages",38,.055f,.010f,.70f,.10f);
            DirectoryRule(body,"Opponent header separator",.121f,160);

            // pvp_bar.csb authors a 690 x 108 plate in a 128-high slot. Three full plates fill this viewport.
            var list=Scroll(body,"Stage list",0,0,1,1);
            var listRect=(RectTransform)list.transform;
            listRect.anchorMin=listRect.anchorMax=new Vector2(.5f,1);
            listRect.pivot=new Vector2(.5f,1);
            listRect.sizeDelta=new Vector2(1035f,546f);
            listRect.anchoredPosition=new Vector2(0,-150f);
            list.content.GetComponent<VerticalLayoutGroup>().spacing=30f;
            keys.Add(("list",list));
            var row=Area(body,"Stage row",0,0,1,.17f);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight=162;
            var select=Button(row,TowerArt("_box_r_w2"),new Color32(255,255,255,5));
            select.transition=Selectable.Transition.None;
            select.KeepBrightnessOnPress=true;
            var selected=Picture(row,"Selected glow",TowerArt("pvp_slot_select"),0,.222f,1,.778f);
            selected.type=Image.Type.Sliced;
            selected.color=new Color(1,1,1,.9f);
            var stageTitle=Text(row,"Stage",null,26,.148f,.20f,.248f,.33f);
            var stageState=Text(row,"State",null,23,.148f,.565f,.247f,.34f,gold);
            stageTitle.fontSharedMaterial=stageState.fontSharedMaterial=Resources.Load<Material>("AstralOathPlayerNameShadow");
            var rk=new List<(string,Object)>{("choose",select),("selected",selected.gameObject),("label",stageTitle),("state",stageState)};
            rk.Add(("leader",TowerBadge(row,"Defense captain",.076f,.51f,108f,false,false)));
            for(int i=0;i<4;i++)
                rk.Add(("portrait"+i,TowerBadge(row,"Defender "+i,.458f+.115f*i,.51f,113f,true,true)));
            var glass=Button(Area(row,"Clear team",.899f,.30f,.076f,.40f),Art("_box_r_op80"),Color.white);
            rk.Add(("inspectIcon",Picture((RectTransform)glass.transform,"Magnifier",Art("cm_icon_etczoomer"),.19f,.19f,.62f,.62f,true)));
            rk.Add(("inspect",glass));
            Bind(row,rk.ToArray()); row.gameObject.SetActive(false); keys.Add(("row",row.gameObject));
            keys.Add(("status",Text(body,"Selected stage message",null,23,.02f,.768f,.96f,.073f,Mist,TextAlignmentOptions.Center)));
            keys.Add(("challenge",CommonButton(body,"ConfirmButton","tower.challenge",.363f,.873f,.274f,.097f)));
            keys.Add(("overlay",BuildTowerTeamOverlay(root)));
            Bind(root,keys.ToArray());
            return root.gameObject;
        }

        static GameObject BuildShopItem()
        {
            var root=Root("ShopItem");
            // The Unity sprite's outside feather is transparent. Align its visible frame with the footage.
            var face=Area(root,"Card artwork",-.029f,-.028f,1.058f,1.056f);
            Picture(face,"Original product frame",ShopArt("renew_box_shop_short"),0,0,1,1);
            // Coordinates follow the 232 x 282 shop_item_short card (Cocos Y points upward).
            var name=Text(face,"Product name",null,23,14f/232,14f/282,204f/232,30f/282,Color.white,TextAlignmentOptions.Center);
            var icon=Picture(face,"Product art",ShopArt("shop_product_est2e_b"),47.4f/232,68f/282,135.3f/232,128f/282,true);
            var sale=Picture(face,"Reference discount badge",ShopArt("shop_icon_sale70_s"),17f/232,47f/282,48f/232,48f/282,true);
            var count=Area(face,"Remaining purchases",151.5f/232,49f/282,65f/232,34f/282);
            Paint(count,ShopArt("_btn_small_white"),Color.white,sliced:true);
            var quota=Text(count,"Remaining",null,21,0,0,1,1,Color.black,TextAlignmentOptions.Center);
            var period=Text(face,"Period",null,20,.04f,195f/282,.92f,26f/282,Mist,TextAlignmentOptions.Center);
            var action=InventoryAction(face,"Buy","shop.exchange",26,20.2f/232,219.2f/282,193.6f/232,49.6f/282,out var price,"Green");
            ((Image)action.targetGraphic).pixelsPerUnitMultiplier=1.8f;
            price.fontStyle=FontStyles.Normal;
            Place(price.rectTransform,.47f,0,.48f,1);
            price.alignment=TextAlignmentOptions.MidlineLeft;
            Picture((RectTransform)action.transform,"Conquest points",ShopArt("token_pvpgold"),.237f,.025f,.205f,.95f,true);
            RecordInventoryButton(action.gameObject);
            var directory=InventoryAction(face,"Directory","shop.directory",25,20.462f/232,219.4f/282,193.6f/232,49.6f/282,out var directoryLabel,"Blue");
            ((Image)directory.targetGraphic).pixelsPerUnitMultiplier=1.8f;
            directoryLabel.fontStyle=FontStyles.Normal;
            RecordInventoryButton(directory.gameObject);
            directory.gameObject.SetActive(false);
            Bind(root,("name",name),("icon",icon),("quota",quota),("period",period),("action",action),("price",price),("directory",directory),("sale",sale));
            return root.gameObject;
        }

        static GameObject BuildShopWindow()
        {
            var root=Root("ShopWindow");
            var keys=new List<(string Key,Object Target)>();
            var list=BuildArenaShopLayout(root,keys);
            var tile=(GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(ShopItemPath),root);
            tile.SetActive(false);
            keys.Add(("list",list));keys.Add(("item",tile.GetComponent<BindingMap>()));
            var library=Area(root,"Catalog sprites",0,0,0,0);
            string[] ids={"molagora","energy","gear1","gear2","jewel1","jewel2","season0","season1","season2","season3","season4","season5","season6","season7","season8","season9","season10"};
            string[] sprites={"shop_product_muragora_b","shop_product_stamina_b","shop_product_est2e_b","shop_product_est4e_b","shop_product_est2j_b","shop_product_est4j_b","sp_selectbox_pvp_b_immune","sp_selectbox_pvp_b_counter","sp_selectbox_pvp_b_penetrate","sp_selectbox_pvp_b_scar","sp_selectbox_pvp_b_cri_dmg","sp_selectbox_pvp_b_att","sp_selectbox_pvp_b_speed","sp_selectbox_pvp_b_acc","sp_selectbox_pvp_b_cri","sp_selectbox_pvp_b_res","sp_selectbox_pvp_b_max_hp"};
            for(int i=0;i<ids.Length;i++)keys.Add(("art."+ids[i],Picture(library,ids[i],ShopArt(sprites[i]),0,0,1,1,true)));
            foreach(int discount in new[]{10,20,30,40,50,60,70})
                keys.Add(("sale."+discount,Picture(library,"Historical discount "+discount,ShopArt("shop_icon_sale"+discount+"_s"),0,0,1,1,true)));
            library.gameObject.SetActive(false);
            var modal=Area(root,"Exchange confirmation",0,0,1,1);
            Paint(modal,null,Black(.70f),true);
            keys.Add(("confirmation",SourceConfirmation(modal,"Confirm",0,.24f,1,.52f)));
            keys.Add(("modal",modal.gameObject));modal.gameObject.SetActive(false);
            Bind(root,keys.ToArray());return root.gameObject;
        }
    }
}
