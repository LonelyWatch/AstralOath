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
            var surface = Area(frame,"Manage equipment",0,0,1,1);
            keys.Add(("pickerSurface",surface.gameObject));
            Paint(surface,InventoryArt("base_unit_hero"),Color.white,true);
            // Keep the original 2464x1200 game-area composition together when fitting the safe area.
            var pane = Area(surface,"Management composition",0,0,1,1);
            PlaceHeroControl(pane,.5f,.5f,1478.4f,720f);
            keys.Add(("manageComposition",pane));
            // equip_base.csb: these are source scale-nine panels and gradients, not replacement fills.
            Paint(Area(pane,"Loadout source panel",-.20f,.092f,.677f,1.02f),ManagementArt("_btn_small_black_5"),
                ManagementSourceTint(30,144,255,229),false,true).pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            Paint(Area(pane,"Catalogue source gradient",.477f,.092f,.523f,.908f),ManagementArt("_gradient_side"),
                ManagementSourceTint(96,137,203,51),false,true).pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            Picture(pane,"Source blue glow",ManagementArt("_grow_small"),.054f,.10f,.78f,.87f).color = ManagementSourceTint(20,116,230,76);
            Paint(Area(pane,"Loadout bottom ornament",0,.923f,.245f,.015f),ManagementArt("_bar_25"),
                ManagementSourceTint(83,126,178,76),false,true).pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            var header = Area(pane,"Management header",0,0,1,.094f);
            Paint(header,Art("_white"),Black(.20f));
            var back = SharedHeaderBack(header);
            keys.Add(("close",back));
            var title = Text(header,"Title",InventoryPrefix + "roster_manage_equipment",35,0,0,.45f,1);
            title.rectTransform.offsetMin = new Vector2(((RectTransform)back.transform).rect.width + 4f*HomeDesignScale,0);
            var help = Picture(pane,"Management help glyph",Art("cm_hud_top_btn_help"),.153f,.016f,.030f,.057f,true).rectTransform;
            help.anchorMin = new Vector2(0,help.anchorMin.y);
            help.anchorMax = new Vector2(0,help.anchorMax.y);
            help.offsetMin = new Vector2(226f * HomeDesignScale,0);
            help.offsetMax = new Vector2(270f * HomeDesignScale,0);
            keys.Add(("manageHeroIcon",Picture(pane,"Hero portrait",null,.027f,.110f,.031f,.066f,true)));
            var heroCaption = Area(pane,"Hero name and source stars",.064f,.106f,.35f,.065f);
            var captionLayout = heroCaption.gameObject.AddComponent<HorizontalLayoutGroup>();
            captionLayout.childAlignment = TextAnchor.MiddleLeft;
            captionLayout.childControlWidth = captionLayout.childControlHeight = true;
            captionLayout.childForceExpandWidth = captionLayout.childForceExpandHeight = false;
            captionLayout.spacing = 6 * HomeDesignScale;
            var heroName = Text(heroCaption,"Hero name",null,31,0,0,1,1,Color.white);
            heroName.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0;
            keys.Add(("manageHeroName",heroName));
            var stars = Area(heroCaption,"Source star strip",0,0,1,1);
            var starSize = stars.gameObject.AddComponent<LayoutElement>();
            starSize.preferredWidth = 103f * HomeDesignScale;
            starSize.preferredHeight = 25f * HomeDesignScale;
            for (int i = 0; i < 6; i++)
                keys.Add(("manageHeroStar" + i,Picture(stars,"Star " + i,Art("Draft/TeamBadges/cm_icon_star"),i * 15.6f / 103f,0,24.6f / 103f,1,true)));
            var current = Area(pane,"Currently worn loadout",.033f,.1975f,.190f,.676f);
            var preview = Area(pane,"Proposed loadout",.2635f,.1975f,.190f,.676f);
            keys.Add(("manageCurrentGroup",current.gameObject));
            foreach (var group in new[] { ("manageCurrent",current), ("managePreview",preview) })
            {
                for (int i = 0; i < slots.Length; ++i)
                {
                    var step = InstantiateEquipmentSlot(group.Item2,slots[i],CommonArt("cm_item_slot"),RosterArt("cm_icon_par" + authored[i]));
                    AuthorWornEquipmentCard(step, false);
                    step.Require<BindingMap>("wornCard").gameObject.AddComponent<CanvasGroup>();
                    step.gameObject.SetActive(false);
                    var aspect = step.gameObject.AddComponent<AspectRatioFitter>();
                    aspect.enabled = false;
                    aspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                    aspect.aspectRatio = 1f;
                    Place((RectTransform)step.transform,i % 3 * .346f,i / 3 * .191f,.290f,.1625f);
                    step.gameObject.SetActive(true);
                    var symbol = step.Require<Image>("placeholder");
                    Place(symbol.rectTransform,.12f,.53f,.36f,.36f);
                    symbol.color = new Color32(255,255,255,76);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(symbol.rectTransform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(step.transform);
                    keys.Add((group.Item1 + "_" + slots[i],step));
                    var hit = step.Require<Image>("frame");
                    hit.sprite = CommonArt("cm_item_slot");
                    hit.type = Image.Type.Sliced;
                    hit.preserveAspect = false;
                    hit.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
                    hit.raycastTarget = true;
                    var select = step.gameObject.AddComponent<GameButton>();
                    select.targetGraphic = hit;
                    select.transition = Selectable.Transition.None;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hit);
                    keys.Add((group.Item1 + "Select_" + slots[i],select));
                }
                // The project has no authoritative combat-power formula. Keep the real stat heading in its source band.
                Paint(Area(group.Item2,"Attribute band",-.015f,.385f,1.015f,.070f),Art("_white"),ManagementSourceTint(255,255,255,10));
                Text(group.Item2,"Loadout heading","home.gear." + group.Item1,27,.035f,.385f,.93f,.070f,Color.white);
                string[] stats = { "attack","defense","health","speed","critChanceBp","critDamageBp","effectivenessBp","resistanceBp" };
                for (int i = 0; i < stats.Length; ++i)
                {
                    var row = AddEquipmentStatRow(group.Item2,stats[i]);
                    Place((RectTransform)row.transform,0,.477f + .0485f * i,1,.047f);
                    var map = row.GetComponent<BindingMap>();
                    var statIcon = InventoryArt("Equipment/cm_icon_stat_" + new[] { "att", "def", "max_hp", "speed", "cri", "cri_dmg", "acc", "res" }[i]);
                    Picture((RectTransform)row.transform,"Stat icon",statIcon,.02f,.12f,.065f,.76f,true).color = new Color32(255,255,255,127);
                    var statLabel = map.Require<GameText>("label");
                    var statValue = map.Require<GameText>("value");
                    Place(statLabel.rectTransform,.14f,0,.51f,1);
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
                        name.gameObject.SetActive(false);
                        Place(value.rectTransform,.125f,0,.36f,1);
                        foreach (var text in new[] {name,value})
                        {
                            text.enableAutoSizing = true;
                            text.fontSizeMin = 14;
                            text.fontSizeMax = 25;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(text.rectTransform);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                        }
                        var delta = Text(group.Item2,"Difference " + stats[i],null,25,.52f,.477f + .0485f*i,.43f,.047f,Color.white,TextAlignmentOptions.MidlineLeft);
                        delta.enableAutoSizing = true;
                        delta.fontSizeMin = 14;
                        delta.fontSizeMax = 24;
                        keys.Add(("manageDelta_" + stats[i],delta));
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                    keys.Add((group.Item1 + "_" + stats[i],map));
                }
                Paint(Area(group.Item2,"Set background",-.015f,.929f,1.015f,.070f),Art("_white"),ManagementSourceTint(255,255,255,10));
                Picture(group.Item2,"Set status icon",RosterArt("cm_icon_etcbp"),.02f,.941f,.065f,.046f,true).color = new Color32(255,255,255,76);
                keys.Add((group.Item1 + "Set",Text(group.Item2,"Active sets",null,23,.14f,.929f,.84f,.070f,Mist)));
            }
            var info = Area(pane,"Candidate details",.033f,.175f,.217f,.800f);
            info.anchorMin = info.anchorMax = new Vector2(.033f,.825f);
            info.pivot = new Vector2(0,1);
            info.anchoredPosition = Vector2.zero;
            info.sizeDelta = new Vector2(320f,576f) * HomeDesignScale;
            keys.Add(("detail",info.gameObject));
            var plate = Paint(Area(info,"Source equipment grade panel",-.085f,-.035f,1.17f,1.065f),
                InventoryArt("Equipment/_box_equip_1"),Color.white,false,true);
            plate.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            keys.Add(("manageDetailPlate",plate));
            var sourcePlates = Area(info,"Source quality plates",0,0,.01f,.01f);
            string[] grades = { "normal","good","rare","heroic","epic" };
            for (int i = 0; i < grades.Length; i++)
                keys.Add(("manageDetail_" + grades[i],Picture(sourcePlates,grades[i],InventoryArt("Equipment/_box_equip_" + (i+1)),0,0,1,1)));
            sourcePlates.gameObject.SetActive(false);
            var thumb = SingleSlotThumbnail(info,.07f,.035f,.29f,.151f);
            keys.Add(("manageThumbnail",thumb));
            keys.Add(("portrait",thumb.Require<Image>("icon")));
            keys.Add(("quality",Text(info,"Quality",null,27,.40f,.026f,.53f,.075f,new Color32(247,204,131,255),TextAlignmentOptions.TopLeft,true)));
            keys.Add(("name",Text(info,"Name",null,29,.40f,.108f,.53f,.075f,Color.white)));
            keys.Add(("manageDescription",Text(info,"Item description",null,18,.07f,.190f,.86f,.050f,Mist,TextAlignmentOptions.TopLeft,true)));
            Paint(Area(info,"Main property surface",.04f,.240f,.92f,.075f),Art("_white"),ManagementSourceTint(255,255,255,10));
            var subList = EquipmentStatList(info,.07f,.340f,.86f,.255f);
            for (int i = 0; i < 5; ++i)
            {
                var row = AddEquipmentStatRow(i == 0 ? info : subList,"Item stat " + i);
                if (i == 0) Place((RectTransform)row.transform,.07f,.240f,.86f,.075f);
                else row.AddComponent<LayoutElement>().preferredHeight = 36;
                foreach (var statText in row.GetComponentsInChildren<GameText>())
                {
                    statText.fontSize = i == 0 ? 30 : 25;
                    statText.fontStyle = i == 0 ? FontStyles.Bold : FontStyles.Normal;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(statText);
                }
                if (i == 0)
                {
                    var map = row.GetComponent<BindingMap>();
                    Place(map.Require<GameText>("label").rectTransform,.10f,0,.60f,1);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(map.Require<GameText>("label").rectTransform);
                    keys.Add(("manageMainStatIcon",Picture((RectTransform)row.transform,"Source main stat symbol",null,0,.15f,.07f,.70f,true)));
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(row.transform);
                keys.Add(("manageItemStat"+i,row.GetComponent<BindingMap>()));
            }
            var score = AddEquipmentScoreRow(subList,0,0,1,1);
            score.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            foreach (var text in score.GetComponentsInChildren<GameText>())
            {
                text.fontStyle = FontStyles.Normal;
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
            keys.Add(("manageScore",score));
            Paint(Area(info,"Source set divider",.055f,.593f,.89f,.007f),ManagementArt("z_tile_bar"),ManagementSourceTint(255,255,255,76),false,true);
            keys.Add(("manageSetIcon",Picture(info,"Set icon",null,.07f,.614f,.11f,.064f,true)));
            keys.Add(("manageSet",Text(info,"Set",null,27,.20f,.614f,.73f,.064f,Color.white)));
            keys.Add(("manageSetEffect",Text(info,"Set effect",null,24,.07f,.690f,.86f,.105f,Mist,TextAlignmentOptions.TopLeft,true)));
            Paint(Area(info,"Source wearer divider",.055f,.833f,.89f,.007f),ManagementArt("z_tile_bar"),ManagementSourceTint(255,255,255,76),false,true);
            Picture(info,"Wearer information symbol",ManagementArt("icon_menu_info_2"),.45f,.868f,.10f,.05f,true).color = new Color32(255,255,255,76);
            keys.Add(("wearer",Text(info,"Wearer",null,22,.07f,.923f,.86f,.062f,Mist,TextAlignmentOptions.Center,true)));
            keys.Add(("managePrompt",Text(pane,"Candidate prompt","home.gear.manageSelect",23,.278f,.627f,.164f,.085f,Mist,TextAlignmentOptions.Center,true)));
            for (int i = 0; i < slots.Length; ++i)
            {
                var rect = Area(pane,"Filter " + slots[i],.565f+i*.040f,.106f,.040f,.081f);
                keys.Add(("filter_"+slots[i],Button(rect,null,Color.clear)));
                keys.Add(("active_"+slots[i],Picture(rect,"Selected",BagArt("_tap_select_blue2"),0,0,1,1)));
                Picture(rect,"Slot",RosterArt("cm_icon_par"+authored[i]),.24f,.2f,.52f,.6f,true);
            }
            keys.Add(("manageSetFilter",Action(pane,"Filter set",null,24,.565f,.203f,.110f,.054f,
                out var setFilterLabel,CommonArt("_btn_small_black"),Color.white)));
            keys.Add(("manageSetFilterLabel",setFilterLabel));
            Place(setFilterLabel.rectTransform,.22f,.04f,.75f,.92f);
            setFilterLabel.alignment = TextAlignmentOptions.MidlineLeft;
            keys.Add(("manageSetFilterSymbol",Picture((RectTransform)setFilterLabel.transform.parent,"Filter source symbol",Art("icon_menu_all"),.025f,.10f,.18f,.80f,true)));
            keys.Add(("manageSort",Action(pane,"Candidate sort",null,24,.815f,.116f,.090f,.056f,
                out var sortLabel,CommonArt("_btn_small_black"),Color.white)));
            keys.Add(("manageSortLabel",sortLabel));
            sortLabel.enableAutoSizing = true;
            sortLabel.fontSizeMin = 16;
            sortLabel.fontSizeMax = 24;
            Paint(Area(pane,"Catalogue top divider",.565f,.182f,.342f,.009f),CommonArt("_bar_2"),ManagementSourceTint(255,255,255,51),false,true);
            Paint(Area(pane,"Catalogue divider",.494f,.337f,.480f,.006f),ManagementArt("z_tile_bar"),ManagementSourceTint(255,255,255,76),false,true);
            var scroll = InventoryScroll(pane,"Candidates",.494f,.353f,.480f,.521f);
            var grid = scroll.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(86,111) * HomeDesignScale;
            grid.spacing = new Vector2(16,27) * HomeDesignScale;
            grid.padding = new RectOffset(5,3,6,3);
            scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            keys.Add(("items",scroll));
            var template = InstantiateEquipmentItemCard(pane,"Equipment item template");
            template.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(template.gameObject);
            keys.Add(("template",template.gameObject));
            Paint(Area(pane,"Catalogue bottom divider",.494f,.872f,.480f,.006f),ManagementArt("z_tile_bar"),ManagementSourceTint(255,255,255,76),false,true);
            var apply = Action(pane,"Apply loadout","home.gear.manageChange",27,.741f,.894f,.162f,.073f,out var label,CommonArt("_btn_green"),Color.white);
            Place(label.rectTransform,.22f,0,.74f,1);
            Picture((RectTransform)apply.transform,"Equip action icon",InventoryArt("icon_menu_equip"),.075f,.16f,.14f,.68f,true);
            keys.Add(("apply",apply));
            ((Image)apply.targetGraphic).pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            keys.Add(("applyLabel",label));
            var reset=Action(pane,"Reset preview",null,23,.261f,.895f,.043f,.072f,out _,CommonArt("_btn_small_white"),Color.white);
            var resetIcon = Picture((RectTransform)reset.transform,"Reset action icon",Load<Sprite>(Arena + "Res/Textures/Draft/Filters/icon_menu_reset.png"),.11f,.10f,.78f,.80f,true);
            keys.Add(("manageResetIcon",resetIcon));
            keys.Add(("manageReset",reset));
            keys.Add(("status",Text(pane,"Equipment status",null,24,.51f,.47f,.44f,.10f,Color.white,TextAlignmentOptions.Center,true)));
            keys.Add(("retry",Action(pane,"Retry","home.gear.retry",23,.64f,.59f,.19f,.07f,out _,BagArt("_btn_small_black"))));
            BuildManagementFilters(pane,keys);
        }

        private static Sprite ManagementArt(string stem) => InventoryArt("Equipment/Management/" + stem);

        // The source client composites gamma-authored tints. Linear UGUI otherwise brightens its 10/255 white bands
        // and turns the low-opacity blue gradient into a gray rectangle. Keep this compensation local to these source nodes.
        private static Color ManagementSourceTint(byte r, byte g, byte b, byte opacity)
        {
            var color = ((Color)new Color32(r,g,b,255)).linear;
            color.a = Mathf.GammaToLinearSpace(opacity / 255f);
            return color;
        }

        private static void BuildManagementQuote(RectTransform dialog,List<(string Key,Object Target)> keys)
        {
            var panel=Area(dialog,"Confirm loadout change",0,.185f,1,.63f);
            panel.anchorMin = new Vector2(0,.5f);
            panel.anchorMax = new Vector2(1,.5f);
            panel.sizeDelta = new Vector2(0,690f);
            panel.anchoredPosition = Vector2.zero;
            keys.Add(("managementQuote",panel.gameObject));
            PaintSourceMessageBox(panel,true);
            keys.Add(("manageQuoteTitle",Text(panel,"Title","home.gear.manageChange",38,.25f,.058f,.50f,.10f,Color.white,TextAlignmentOptions.Center)));
            var ornament = Picture(panel,"Source title ornament",Art("_bar_23"),.285f,.185f,.43f,.026f);
            ornament.type = Image.Type.Sliced;
            ornament.color = ManagementSourceTint(255,255,255,102);
            keys.Add(("manageSummary",Text(panel,"Summary",null,27,.25f,.29f,.50f,.10f,Mist,TextAlignmentOptions.Center,true)));
            var comparison = Area(panel,"Combat power comparison",0,0,1,1);
            PlaceHeroControl(comparison,.5f,.43f,640f,150f);
            var powerColor = new Color32(51,152,238,255);
            var gainColor = new Color32(255,120,0,255);
            for (int i = 0; i < 2; i++)
            {
                string column = i == 0 ? "Before" : "After";
                float x = i == 0 ? .04f : .68f;
                Picture(comparison,column + " combat power symbol",RosterArt("cm_icon_etcbp"),x,.08f,.043f,.18f,true).color = powerColor;
                keys.Add(("manageQuote" + column + "Title",Text(comparison,column + " title","home.gear.combatPower",29,x+.055f,.06f,.25f,.22f,powerColor)));
                var values = Text(comparison,column + " combat power",null,58,x,.30f,.29f,.43f,Color.white,TextAlignmentOptions.MidlineLeft);
                values.fontStyle = FontStyles.Bold;
                values.enableAutoSizing = true;
                values.fontSizeMin = 34;
                values.fontSizeMax = 58;
                keys.Add(("manageQuote" + column,values));
            }
            var direction = Picture(comparison,"Change direction",ManagementArt("cm_icon_etcarrow"),.475f,.17f,.040f,.16f,true);
            direction.rectTransform.localEulerAngles = new Vector3(0,0,90);
            direction.color = gainColor;
            keys.Add(("manageQuoteDirection",direction));
            var deltaArrow = Picture(comparison,"Combat power delta arrow",RosterArt("cm_icon_etccompu"),.43f,.51f,.023f,.07f,true);
            deltaArrow.color = gainColor;
            keys.Add(("manageQuoteDeltaArrow",deltaArrow));
            keys.Add(("manageQuoteDelta",Text(comparison,"Combat power delta",null,31,.465f,.43f,.19f,.24f,gainColor)));
            keys.Add(("manageChange",Text(panel,"Transaction notes",null,21,.30f,.71f,.40f,.07f,Mist,TextAlignmentOptions.Center,true)));
            var gold = Load<Sprite>(SummonPath + "Res/Textures/_btn_gold_r_2.png");
            var goldPressed = Load<Sprite>(SummonPath + "Res/Textures/_btn_gold_r_p_2.png");
            var cancel = Action(panel,"Cancel","home.gear.cancel",29,.36f,.807f,.135f,.145f,out _,gold,Color.white);
            var confirm = Action(panel,"Change equipment","home.gear.manageChange",29,.505f,.807f,.162f,.145f,out _,ManagementArt("_btn_green_r_2"),Color.white);
            PressSprites(cancel,gold,goldPressed);
            PressSprites(confirm,ManagementArt("_btn_green_r_2"),ManagementArt("_btn_green_r_p_2"));
            foreach (var button in new[] { cancel,confirm }) ((Image)button.targetGraphic).pixelsPerUnitMultiplier = 1f / (.53f * HomeDesignScale);
            keys.Add(("manageCancel",cancel));
            keys.Add(("manageConfirm",confirm));
        }
    }
}
