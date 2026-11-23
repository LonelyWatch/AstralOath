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
            Paint(Area(pane,"Loadout surface",.022f,.106f,.458f,.856f),null,new Color32(6,18,36,240));
            Paint(Area(pane,"Equipment catalogue surface",.491f,.106f,.487f,.856f),null,new Color32(10,18,34,215));
            var header = Area(pane,"Management header",0,0,1,.094f);
            Paint(header,null,Black(.34f));
            var back = SharedHeaderBack(header);
            keys.Add(("close",back));
            var title = Text(header,"Title",InventoryPrefix + "roster_manage_equipment",35,0,0,.45f,1);
            title.rectTransform.offsetMin = new Vector2(((RectTransform)back.transform).rect.width + 4f*HomeDesignScale,0);
            keys.Add(("manageHeroIcon",Picture(pane,"Hero portrait",null,.026f,.110f,.034f,.066f,true)));
            keys.Add(("manageHeroName",Text(pane,"Hero name",null,31,.064f,.106f,.23f,.065f,Color.white)));
            keys.Add(("manageHeroStars",Text(pane,"Hero stars",null,29,.296f,.106f,.164f,.065f,new Color32(255,208,65,255))));
            var current = Area(pane,"Currently worn loadout",.036f,.182f,.195f,.698f);
            var preview = Area(pane,"Proposed loadout",.263f,.182f,.195f,.698f);
            keys.Add(("manageCurrentGroup",current.gameObject));
            foreach (var group in new[] { ("manageCurrent",current), ("managePreview",preview) })
            {
                for (int i = 0; i < slots.Length; ++i)
                {
                    var step = InstantiateEquipmentSlot(group.Item2,slots[i],CommonArt("_box_equip_0"),RosterArt("cm_icon_par" + authored[i]));
                    AuthorWornEquipmentCard(step, false);
                    step.gameObject.SetActive(false);
                    var aspect = step.gameObject.AddComponent<AspectRatioFitter>();
                    aspect.enabled = false;
                    aspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                    aspect.aspectRatio = 1f;
                    Place((RectTransform)step.transform,i % 3 * .35f,i / 3 * .19f + .071f,.29f,.166f);
                    step.gameObject.SetActive(true);
                    var symbol = step.Require<Image>("placeholder");
                    Place(symbol.rectTransform,.29f,.29f,.42f,.42f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(symbol.rectTransform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(step.transform);
                    keys.Add((group.Item1 + "_" + slots[i],step));
                    var hit = step.Require<Image>("frame");
                    hit.sprite = CommonArt("_box_equip_0");
                    hit.type = Image.Type.Sliced;
                    hit.preserveAspect = false;
                    hit.pixelsPerUnitMultiplier = 4f;
                    hit.raycastTarget = true;
                    var select = step.gameObject.AddComponent<GameButton>();
                    select.targetGraphic = hit;
                    select.transition = Selectable.Transition.None;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hit);
                    keys.Add((group.Item1 + "Select_" + slots[i],select));
                }
                Text(group.Item2,"Loadout heading","home.gear." + group.Item1,26,0,0,1,.063f,Color.white);
                string[] stats = { "attack","defense","health","speed","critChanceBp","critDamageBp","effectivenessBp","resistanceBp" };
                for (int i = 0; i < stats.Length; ++i)
                {
                    var row = AddEquipmentStatRow(group.Item2,stats[i]);
                    Place((RectTransform)row.transform,0,.463f + .057f * i,1,.053f);
                    var map = row.GetComponent<BindingMap>();
                    var statIcon = InventoryArt("Equipment/cm_icon_stat_" + new[] { "att", "def", "max_hp", "speed", "cri", "cri_dmg", "acc", "res" }[i]);
                    Picture((RectTransform)row.transform,"Stat icon",statIcon,0,.15f,.065f,.70f,true).color = Mist;
                    var statLabel = map.Require<GameText>("label");
                    var statValue = map.Require<GameText>("value");
                    Place(statLabel.rectTransform,.09f,0,.55f,1);
                    Place(statValue.rectTransform,.64f,0,.36f,1);
                    foreach (var text in new[] {statLabel,statValue})
                    {
                        text.fontSize = 25;
                        text.fontStyle = FontStyles.Normal;
                        text.color = Mist;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(text.rectTransform);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                    }
                    if (group.Item1 == "managePreview")
                    {
                        var name = map.Require<GameText>("label");
                        var value = map.Require<GameText>("value");
                        Place(name.rectTransform,.09f,0,.44f,1);
                        Place(value.rectTransform,.53f,0,.25f,1);
                        foreach (var text in new[] {name,value})
                        {
                            text.enableAutoSizing = true;
                            text.fontSizeMin = 14;
                            text.fontSizeMax = 25;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(text.rectTransform);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                        }
                        var delta = Text(group.Item2,"Difference " + stats[i],null,24,.78f,.463f + .057f*i,.22f,.053f,Color.white,TextAlignmentOptions.MidlineRight);
                        delta.enableAutoSizing = true;
                        delta.fontSizeMin = 14;
                        delta.fontSizeMax = 24;
                        keys.Add(("manageDelta_" + stats[i],delta));
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                    keys.Add((group.Item1 + "_" + stats[i],map));
                }
                Paint(Area(group.Item2,"Set background",0,.941f,1,.069f),null,new Color32(100,142,179,17));
                keys.Add((group.Item1 + "Set",Text(group.Item2,"Active sets",null,24,.025f,.941f,.95f,.069f,Mist)));
            }
            var info = Area(pane,"Candidate details",.032f,.185f,.205f,.762f);
            keys.Add(("detail",info.gameObject));
            Paint(info,null,new Color32(14,25,42,255));
            var thumb = SingleSlotThumbnail(info,.07f,.035f,.29f,.17f);
            keys.Add(("manageThumbnail",thumb));
            keys.Add(("portrait",thumb.Require<Image>("icon")));
            keys.Add(("quality",Text(info,"Quality",null,27,.40f,.045f,.53f,.07f,new Color32(247,204,131,255))));
            keys.Add(("name",Text(info,"Name",null,29,.40f,.115f,.53f,.075f,Color.white)));
            keys.Add(("manageDescription",Text(info,"Item description",null,25,.07f,.223f,.86f,.103f,Mist,TextAlignmentOptions.TopLeft,true)));
            Paint(Area(info,"Main property surface",.04f,.349f,.92f,.071f),null,new Color32(110,152,187,35));
            var subList = EquipmentStatList(info,.07f,.437f,.86f,.32f);
            for (int i = 0; i < 5; ++i)
            {
                var row = AddEquipmentStatRow(i == 0 ? info : subList,"Item stat " + i);
                if (i == 0) Place((RectTransform)row.transform,.07f,.349f,.86f,.071f);
                else row.AddComponent<LayoutElement>().preferredHeight = 41;
                foreach (var statText in row.GetComponentsInChildren<GameText>())
                {
                    statText.fontSize = i == 0 ? 30 : 25;
                    statText.fontStyle = FontStyles.Normal;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(statText);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                keys.Add(("manageItemStat"+i,row.GetComponent<BindingMap>()));
            }
            var score = AddEquipmentScoreRow(subList,0,0,1,1);
            score.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            foreach (var text in score.GetComponentsInChildren<GameText>())
            {
                text.fontStyle = FontStyles.Normal;
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            keys.Add(("manageScore",score));
            Paint(Area(info,"Set surface",.04f,.772f,.92f,.075f),null,new Color32(110,152,187,20));
            keys.Add(("manageSetIcon",Picture(info,"Set icon",null,.07f,.782f,.09f,.055f,true)));
            keys.Add(("manageSet",Text(info,"Set",null,27,.18f,.772f,.75f,.075f,Color.white)));
            keys.Add(("wearer",Text(info,"Wearer",null,24,.07f,.886f,.86f,.075f,Mist,TextAlignmentOptions.Center,true)));
            keys.Add(("managePrompt",Text(pane,"Candidate prompt","home.gear.manageSelect",22,.263f,.899f,.195f,.055f,Mist,TextAlignmentOptions.Center,true)));
            for (int i = 0; i < slots.Length; ++i)
            {
                var rect = Area(pane,"Filter " + slots[i],.51f+i*.056f,.113f,.051f,.068f);
                keys.Add(("filter_"+slots[i],Button(rect,null,Color.clear)));
                keys.Add(("active_"+slots[i],Picture(rect,"Selected",BagArt("_tap_select_blue2"),0,0,1,1)));
                Picture(rect,"Slot",RosterArt("cm_icon_par"+authored[i]),.24f,.2f,.52f,.6f,true);
            }
            keys.Add(("manageSetFilter",Action(pane,"Filter set",null,24,.507f,.204f,.142f,.057f,
                out var setFilterLabel,CommonArt("_btn_small_black"),Color.white)));
            keys.Add(("manageSetFilterLabel",setFilterLabel));
            keys.Add(("manageSort",Action(pane,"Candidate sort",null,21,.859f,.124f,.101f,.053f,
                out var sortLabel,CommonArt("_btn_small_black"),Color.white)));
            keys.Add(("manageSortLabel",sortLabel));
            Paint(Area(pane,"Catalogue divider",.507f,.344f,.453f,.002f),null,new Color32(110,163,212,72));
            var scroll = InventoryScroll(pane,"Candidates",.507f,.360f,.453f,.494f);
            var grid = scroll.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(132,172);
            grid.spacing = new Vector2(16,20);
            grid.padding = new RectOffset(6,3,7,3);
            scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            keys.Add(("items",scroll));
            var template = InstantiateEquipmentItemCard(pane,"Equipment item template");
            template.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(template.gameObject);
            keys.Add(("template",template.gameObject));
            var apply = Action(pane,"Apply loadout","home.gear.manageChange",27,.767f,.880f,.193f,.068f,out var label,CommonArt("_btn_green"),Color.white);
            keys.Add(("apply",apply));
            keys.Add(("applyLabel",label));
            var reset=Action(pane,"Reset preview","home.gear.manageReset",23,.263f,.903f,.126f,.051f,out _,BagArt("_btn_small_black"),Color.white);
            keys.Add(("manageReset",reset));
            keys.Add(("status",Text(pane,"Equipment status",null,24,.51f,.47f,.44f,.10f,Color.white,TextAlignmentOptions.Center,true)));
            keys.Add(("retry",Action(pane,"Retry","home.gear.retry",23,.64f,.59f,.19f,.07f,out _,BagArt("_btn_small_black"))));
            BuildManagementFilters(pane,keys);
        }

        private static void BuildManagementQuote(RectTransform dialog,List<(string Key,Object Target)> keys)
        {
            var panel=Area(dialog,"Confirm loadout change",.20f,.25f,.60f,.50f);
            keys.Add(("managementQuote",panel.gameObject));
            PaintSoftModal(panel);
            keys.Add(("manageQuoteTitle",Text(panel,"Title","home.gear.manageChange",36,.08f,.058f,.84f,.10f,Color.white,TextAlignmentOptions.Center)));
            keys.Add(("manageSummary",Text(panel,"Summary",null,27,.08f,.19f,.84f,.10f,Mist,TextAlignmentOptions.Center,true)));
            var headingBand = Paint(Area(panel,"Comparison headings",.10f,.333f,.80f,.078f),null,new Color32(96,151,199,36));
            keys.Add(("manageQuoteHeadings",headingBand.gameObject));
            string[] columns = { "Metric","Before","After","Delta" };
            for (int i = 0; i < columns.Length; i++)
            {
                string column = columns[i];
                float x = .12f + i * .196f;
                var align = i == 0 ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
                keys.Add(("manageQuoteHeading" + column,Text(panel,column,"home.gear.quote" + column,24,x,.350f,.172f,.055f,Mist,align)));
                var values = Text(panel,column + " values",null,27,x,.43f,.172f,.30f,Color.white,align);
                values.richText = column == "Delta";
                values.lineSpacing = 4;
                values.enableAutoSizing = true;
                values.fontSizeMin = 15;
                values.fontSizeMax = 27;
                keys.Add(("manageQuote" + column,values));
            }
            keys.Add(("manageChange",Text(panel,"Transaction notes",null,22,.10f,.732f,.80f,.064f,Mist,TextAlignmentOptions.Center,true)));
            keys.Add(("manageCancel",Action(panel,"Cancel","home.gear.cancel",27,.15f,.807f,.31f,.145f,out _,CommonArt("_btn_gold"),Color.white)));
            keys.Add(("manageConfirm",Action(panel,"Change equipment","home.gear.manageChange",27,.54f,.807f,.31f,.145f,out _,CommonArt("_btn_green"),Color.white)));
        }
    }
}
