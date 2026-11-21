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
        private static void BuildEquipmentManager(RectTransform frame, List<(string Key,Object Target)> keys, string[] slots,string[] authored)
        {
            var pane = Area(frame,"Manage equipment",0,0,1,1);
            keys.Add(("pickerSurface",pane.gameObject));
            Paint(pane,InventoryArt("base_unit_hero"),Color.white,true);
            Paint(Area(pane,"Current and preview section",0,.094f,.477f,.906f),null,new Color32(2,14,26,140));
            var header = Area(pane,"Management header",0,0,1,.094f);
            Paint(header,null,Black(.34f));
            var back = SharedHeaderBack(header);
            keys.Add(("close",back));
            var title = Text(header,"Title",InventoryPrefix + "roster_manage_equipment",35,0,0,.45f,1);
            title.rectTransform.offsetMin = new Vector2(((RectTransform)back.transform).rect.width + 4f*HomeDesignScale,0);
            keys.Add(("manageHeroIcon",Picture(pane,"Hero portrait",null,.026f,.110f,.034f,.066f,true)));
            keys.Add(("manageHeroName",Text(pane,"Hero name",null,31,.064f,.106f,.38f,.065f,Color.white)));
            var current = Area(pane,"Currently worn loadout",.036f,.195f,.187f,.68f);
            var preview = Area(pane,"Proposed loadout",.263f,.195f,.187f,.68f);
            keys.Add(("manageCurrentGroup",current.gameObject));
            foreach (var group in new[] { ("manageCurrent",current), ("managePreview",preview) })
            {
                for (int i = 0; i < slots.Length; ++i)
                {
                    var step = InstantiateEquipmentSlot(group.Item2,slots[i],CommonArt("_box_equip_0"),RosterArt("cm_icon_par" + authored[i]));
                    Place((RectTransform)step.transform,i % 3 * .35f,i / 3 * .19f,.294f,.17f);
                    var symbol = step.Require<Image>("placeholder");
                    Place(symbol.rectTransform,.12f,.55f,.34f,.34f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(symbol.rectTransform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(step.transform);
                    keys.Add((group.Item1 + "_" + slots[i],step));
                }
                Text(group.Item2,"Stat heading","home.gear." + group.Item1,23,0,.397f,1,.066f,Color.white);
                string[] stats = { "attack","defense","health","speed","critChanceBp","critDamageBp","effectivenessBp","resistanceBp" };
                for (int i = 0; i < stats.Length; ++i)
                {
                    var row = AddEquipmentStatRow(group.Item2,stats[i]);
                    Place((RectTransform)row.transform,0,.486f + .052f * i,1,.048f);
                    var map = row.GetComponent<BindingMap>();
                    if (group.Item1 == "managePreview")
                    {
                        var name = map.Require<GameText>("label");
                        var value = map.Require<GameText>("value");
                        Place(name.rectTransform,0,0,.38f,1);
                        Place(value.rectTransform,.38f,0,.30f,1);
                        foreach (var text in new[] {name,value})
                        {
                            text.enableAutoSizing = true;
                            text.fontSizeMin = 14;
                            text.fontSizeMax = 21;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(text.rectTransform);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                        }
                        var delta = Text(group.Item2,"Difference " + stats[i],null,21,.70f,.486f + .052f*i,.30f,.048f,Color.white,TextAlignmentOptions.MidlineRight);
                        delta.enableAutoSizing = true;
                        delta.fontSizeMin = 14;
                        delta.fontSizeMax = 21;
                        keys.Add(("manageDelta_" + stats[i],delta));
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                    keys.Add((group.Item1 + "_" + stats[i],map));
                }
                keys.Add((group.Item1 + "Set",Text(group.Item2,"Active sets",null,21,0,.924f,1,.073f,Color.white)));
            }
            var info = Area(pane,"Candidate details",.032f,.173f,.218f,.804f);
            keys.Add(("detail",info.gameObject));
            keys.Add(("manageDetailPlate",BagImage(info,InventoryArt("Equipment/_box_equip_1"),Color.white,true)));
            var thumb = SingleSlotThumbnail(info,.13f,.048f,.24f,.15f);
            keys.Add(("manageThumbnail",thumb));
            keys.Add(("portrait",thumb.Require<Image>("icon")));
            keys.Add(("quality",Text(info,"Quality",null,23,.413f,.048f,.44f,.06f,new Color32(247,204,131,255))));
            keys.Add(("name",Text(info,"Name",null,26,.413f,.107f,.44f,.09f,Color.white)));
            keys.Add(("manageDescription",Text(info,"Item description",null,21,.149f,.194f,.692f,.092f,Mist,TextAlignmentOptions.TopLeft,true)));
            var subList = EquipmentStatList(info,.149f,.377f,.692f,.31f);
            for (int i = 0; i < 5; ++i)
            {
                var row = AddEquipmentStatRow(i == 0 ? info : subList,"Item stat " + i);
                if (i == 0) Place((RectTransform)row.transform,.149f,.302f,.692f,.058f);
                else row.AddComponent<LayoutElement>().preferredHeight = 53;
                PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                keys.Add(("manageItemStat"+i,row.GetComponent<BindingMap>()));
            }
            var score = AddEquipmentScoreRow(subList,0,0,1,1);
            score.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
            keys.Add(("manageScore",score));
            keys.Add(("manageSet",Text(info,"Set",null,24,.149f,.71f,.692f,.085f,Color.white)));
            keys.Add(("wearer",Text(info,"Wearer",null,20,.149f,.83f,.692f,.12f,Color.white,TextAlignmentOptions.Center,true)));
            keys.Add(("managePrompt",Text(pane,"Candidate prompt","home.gear.manageSelect",24,.273f,.586f,.17f,.13f,Mist,TextAlignmentOptions.Center,true)));
            for (int i = 0; i < slots.Length; ++i)
            {
                var rect = Area(pane,"Filter " + slots[i],.563f+i*.041f,.102f,.04f,.087f);
                keys.Add(("filter_"+slots[i],Button(rect,null,Color.clear)));
                keys.Add(("active_"+slots[i],Picture(rect,"Selected",BagArt("_tap_select_blue2"),0,0,1,1)));
                Picture(rect,"Slot",RosterArt("cm_icon_par"+authored[i]),.24f,.2f,.52f,.6f,true);
            }
            var scroll = InventoryScroll(pane,"Candidates",.498f,.347f,.475f,.53f);
            var grid = scroll.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(129,166.5f);
            grid.spacing = new Vector2(20,32);
            grid.padding = new RectOffset(6,3,7,3);
            scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            keys.Add(("items",scroll));
            var template = InstantiateEquipmentItemCard(pane,"Equipment item template");
            template.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(template.gameObject);
            keys.Add(("template",template.gameObject));
            var apply = Action(pane,"Apply loadout","home.gear.manageChange",26,.74f,.89f,.165f,.079f,out var label,CommonArt("_btn_green"),Color.white);
            keys.Add(("apply",apply));
            keys.Add(("applyLabel",label));
            var reset=Action(pane,"Reset preview","home.gear.cancel",22,.26f,.9f,.12f,.065f,out _,BagArt("_btn_small_black"),Color.white);
            keys.Add(("manageReset",reset));
            keys.Add(("status",Text(pane,"Equipment status",null,24,.51f,.47f,.44f,.10f,Color.white,TextAlignmentOptions.Center,true)));
            keys.Add(("retry",Action(pane,"Retry","home.gear.retry",23,.64f,.59f,.19f,.07f,out _,BagArt("_btn_small_black"))));
        }

        private static void BuildManagementQuote(RectTransform dialog,List<(string Key,Object Target)> keys)
        {
            var panel=Area(dialog,"Confirm loadout change",0,.188f,1,.623f);
            keys.Add(("managementQuote",panel.gameObject));
            Paint(panel, DraftSprite("renew_popup_bg_2_alpha"),Color.white,true,true);
            Text(panel,"Title","home.gear.manageChange",39,.2f,.04f,.60f,.12f,Color.white,TextAlignmentOptions.Center);
            keys.Add(("manageSummary",Text(panel,"Summary",null,26,.16f,.24f,.68f,.10f,Color.white,TextAlignmentOptions.Center,true)));
            keys.Add(("manageChange",Text(panel,"Stat changes",null,26,.13f,.38f,.74f,.35f,Color.white,TextAlignmentOptions.Center,true)));
            keys.Add(("manageCancel",Action(panel,"Cancel","home.gear.cancel",27,.36f,.806f,.132f,.14f,out _,CommonArt("_btn_gold"),Color.white)));
            keys.Add(("manageConfirm",Action(panel,"Change equipment","home.gear.manageChange",27,.508f,.806f,.165f,.14f,out _,CommonArt("_btn_green"),Color.white)));
        }
    }
}
