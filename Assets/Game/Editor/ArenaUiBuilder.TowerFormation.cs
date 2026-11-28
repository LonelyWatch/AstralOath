using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        // Cocos hit areas and square badges use the same height-based design units as the source.
        private static RectTransform TowerPoint(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rect = Area(parent, name, x, y, 0, 0);
            rect.anchorMin = rect.anchorMax = new Vector2(x, 1f-y);
            rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = new Vector2(w,h);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static Image TowerBadge(RectTransform parent, string name, float x, float y, float size, bool details, bool stars)
        {
            var badge=TowerPoint(parent,name,x,y,size,size);
            Picture(badge,"Native face ground",CommonArt("_hero_s_bg_ally"),.055f,.055f,.89f,.89f,true);
            var face=Picture(badge,"Hero face",null,.055f,.055f,.89f,.89f,true);
            Picture(badge,"Gold frame",CommonArt("_hero_s_frame_ally"),0,0,1,1,true);
            var role=Picture(badge,"Class",null,-.026f,.025f,.315f,.315f,true);
            role.gameObject.SetActive(details);
            var plate=Area(badge,"Element level",.695f,.045f,.31f,.255f);
            Picture(plate,"Backing",TowerArt("battle_pvp_hud_bar_bg"),0,0,1,1);
            var tint=Picture(plate,"Element",TowerArt("game_hud_bar_none"),.075f,.082f,.85f,.83f);
            var tens=Picture(plate,"Tens",null,.06f,.03f,.48f,.90f,true);
            var ones=Picture(plate,"Units",null,.46f,.03f,.48f,.90f,true);
            plate.gameObject.SetActive(stars);
            var rail=Area(badge,"Grade stars",0,.76f,1,.245f);
            rail.gameObject.SetActive(stars);
            var keys=new List<(string,Object)>{("role",role),("plate",tint),("tens",tens),("ones",ones),("stars",rail.gameObject)};
            for (int i=0;i<6;i++) keys.Add(("star"+i,Picture(rail,"Star "+i,DraftSprite("TeamBadges/cm_icon_star"),i*.137f,0,.205f,1,true)));
            Bind(badge,keys.ToArray());
            return face;
        }

        private static GameObject BuildTowerTeamOverlay(RectTransform root)
        {
            var overlay=Area(root,"Challenge and first clear",0,0,1,1);
            var keys=new List<(string,Object)>();
            var scene=Area(overlay,"Challenge formation",0,0,1,1);
            keys.Add(("scene",scene.gameObject));
            Cover(scene,"Original arena backdrop",TowerArt("base_pvp"));
            var header=Area(scene,"Formation header",0,0,1,.092f);
            Paint(header,null,Black(.62f));
            keys.Add(("back",SharedHeaderBack(header)));
            TowerSocialEntries(header);
            Text(header,"Arena","tower.title",37,.049f,.09f,.35f,.82f);
            DirectoryRule(header,"Header edge",.995f,100);
            var squad=Area(scene,"Squad navigation",0,.092f,.096f,.908f);
            Paint(squad,null,new Color32(10,24,42,234));
            Text(squad,"Squad","tower.squad",23,.06f,.006f,.89f,.09f,Mist,TextAlignmentOptions.Center);
            var squadTab=Area(squad,"Current squad",0,.088f,1,.096f);
            Paint(squadTab,Art("directory_tab"),Color.white,sliced:true);
            Text(squadTab,"Squad title","tower.own_team",24,.05f,0,.90f,1,Color.white,TextAlignmentOptions.Center);
            for(int i=0;i<4;i++) keys.Add(("slot"+i,Text(squad,"Selected member "+i,null,23,.05f,.203f+.093f*i,.89f,.069f,Mist,TextAlignmentOptions.Center)));
            var x=new[]{.329f,.253f,.244f,.174f};
            var foot=new[]{.528f,.618f,.442f,.534f};
            foreach(bool enemy in new[]{false,true})
            {
                string team=enemy?"enemy":"ally";
                foreach(int i in new[]{2,3,0,1})
                {
                    float center=enemy?.915f-x[i]:x[i];
                    var pad=TowerPoint(scene,team+" position "+(i+1),center,foot[i],268,110);
                    string floor=i==0?"hero_config_panelleader":i==3?"hero_config_panelback":"hero_config_bg";
                    var ring=Picture(pad,"Floor",DraftSprite(floor),0,0,1,1,true);
                    ring.color=enemy?new Color32(222,23,22,255):new Color32(54,159,235,255);
                    if(enemy) ring.rectTransform.localScale=new Vector3(-1,1,1);
                    var modelSeat=Area(pad,"Actor",-.04f,-1.91f,1.08f,2.81f);
                    keys.Add((team+"Face"+i,Picture(modelSeat,"Hero face fallback",null,.27f,.03f,.46f,.54f,true)));
                    var model=Raw(modelSeat,"Original hero battle model",0,0,1,1);
                    if(enemy) model.uvRect=new Rect(1,0,-1,1);
                    keys.Add((team+"Model"+i,model));
                    var tag=Area(pad,"Unit info",.22f,-2.52f,.57f,.43f);
                    keys.Add((team+"Tag"+i,tag.gameObject));
                    keys.Add((team+"Class"+i,Picture(tag,"Class",null,0,0,.38f,1,true)));
                    var levelBg=Area(tag,"Element level",.34f,.08f,.34f,.81f);
                    Picture(levelBg,"Frame",TowerArt("battle_pvp_hud_bar_bg"),0,0,1,1);
                    keys.Add((team+"Tint"+i,Picture(levelBg,"Element tint",TowerArt("game_hud_bar_none"),.08f,.08f,.84f,.84f)));
                    keys.Add((team+"Level"+i,Text(levelBg,"Level",null,26,0,0,1,1,Color.white,TextAlignmentOptions.Center)));
                }
            }
            // pvp_team_formation.csb sizes the VS image itself (including its transparent border) at 207 square.
            Paint(TowerPoint(scene,"Versus",.466f,.56f,310.5f,310.5f),Art("_battle_pvp_icon_vs"),Color.white).preserveAspect=true;
            var directory=Area(scene,"Owned heroes",.817f,.092f,.183f,.908f);
            Paint(directory,null,Black(.55f));
            VerticalRule(directory,"Roster edge",0,.017f,.027f,.95f,new Color32(129,101,64,190));
            var order=Area(directory,"Sort owned heroes",.053f,.012f,.417f,.061f);
            keys.Add(("sort",Button(order,Art("_box_r_op80"),Color.white)));
            keys.Add(("order",Text(order,"Sort label","tower.level_order",23,.035f,0,.93f,1,Mist,TextAlignmentOptions.Center)));
            var search=Input(directory,"Search heroes","arena.search",24,.52f,.01f,.46f,.072f);
            ((Image)search.targetGraphic).sprite=Art("_box_r_op80");
            search.textComponent.color=Color.white;
            search.placeholder.color=Mist;
            Place(search.textViewport,.29f,.04f,.69f,.92f);
            Picture((RectTransform)search.transform,"Magnifier",Art("cm_icon_etczoomer"),.037f,.13f,.21f,.74f,true);
            keys.Add(("query",search));
            var list=Scroll(directory,"Owned hero list",-.255f,.254f,1.255f,.744f);
            list.content.GetComponent<VerticalLayoutGroup>().spacing=0;
            keys.Add(("list",list));
            var row=Area(directory,"Owned hero template",0,0,1,.15f);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight=105;
            var choose=Button(row,DraftSprite("hero_bg_normal"),Color.white);
            var mark=Picture(row,"Selected plate",DraftSprite("hero_bg_selected"),0,0,1,1);
            var crop=Picture(row,"Hero clip",DraftSprite("hero_bg_mask"),0,0,1,1);
            crop.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var portrait=Picture(crop.rectTransform,"Card portrait",null,.49f,.012f,.53f,.982f,true);
            var roleIcon=Picture(row,"Class",null,.078f,.19f,.087f,.32f,true);
            var label=Text(row,"Owned hero name",null,25,.167f,.091f,.61f,.46f);
            var level=Text(row,"Level",null,24,.11f,.48f,.27f,.39f,Gold);
            var stars=new List<(string,Object)>{("choose",choose),("label",label),("portrait",portrait),("selected",mark.gameObject),("role",roleIcon),("level",level)};
            for(int i=0;i<6;i++) stars.Add(("star"+i,Picture(row,"Star "+i,DraftSprite("TeamBadges/cm_icon_star"),.33f+i*.036f,.51f,.056f,.29f,true)));
            Bind(row,stars.ToArray());row.gameObject.SetActive(false);keys.Add(("row",row.gameObject));
            var footer=Area(scene,"Formation information",.11f,.69f,.696f,.30f);
            keys.Add(("title",Text(footer,"Stage detail",null,28,.005f,.09f,.30f,.22f,Gold)));
            Text(footer,"Attack caption","tower.lineup",24,.31f,.07f,.33f,.16f,new Color32(100,171,222,255));
            Picture(footer,"Team symbol",TowerArt("cm_icon_etcbp"),.28f,.073f,.026f,.12f,true).color=new Color32(100,171,222,255);
            keys.Add(("teamSize",Text(footer,"Team size",null,47,.315f,.19f,.35f,.25f,Color.white)));
            Text(footer,"Opponent","tower.defending",24,.72f,.05f,.277f,.17f,Gold,TextAlignmentOptions.MidlineRight);
            keys.Add(("enemyInfo",Text(footer,"Enemy stage",null,25,.69f,.20f,.306f,.22f,Mist,TextAlignmentOptions.MidlineRight)));
            DirectoryRule(footer,"Footer divider",.516f,135);
            keys.Add(("reward",Text(footer,"First clear reward",null,24,.014f,.60f,.50f,.24f,Gold)));
            keys.Add(("confirm",CommonButton(scene,"ConfirmButton","tower.start",.589f,.872f,.163f,.094f)));

            var history=Area(overlay,"First clear",0,0,1,1);
            keys.Add(("history",history.gameObject));
            var close=Button(Area(history,"Dismiss backdrop",0,0,1,1),null,Black(.70f));
            close.transition=Selectable.Transition.None;
            keys.Add(("historyBack",close));
            var info=TowerPoint(history,"First clear overview",.5f,.505f,1200f,867f);
            var panelArt=Art("renew_popup_bg_alpha");
            Paint(info,panelArt,Color.black,true,true);
            Picture(info,"Native blue gradient",panelArt,0,0,1,1).type=Image.Type.Sliced;
            keys.Add(("historyTitle",Text(info,"First clear title",null,36,.05f,.034f,.90f,.101f,Color.white,TextAlignmentOptions.Center)));
            NativeRule(info,"Rule",.033f,.134f,.934f,.023f,new Color32(202,209,221,148));
            Picture(info,"Shield",TowerArt("icon_filter_knight"),.049f,.174f,.033f,.053f,true);
            Text(info,"Defenders","tower.defending",24,.088f,.164f,.6f,.071f);
            var defense=Area(info,"Defense strip",.036f,.242f,.928f,.181f);
            Paint(defense,TowerArt("_btn_white"),new Color32(104,160,208,14),sliced:true);
            for(int i=0;i<4;i++) keys.Add(("historyEnemy"+i,TowerBadge(defense,"Defending hero "+i,.25f+i*.165f,.5f,129,true,false)));
            Text(info,"Winning formation","tower.winning",24,.049f,.452f,.80f,.065f,Mist);
            var attack=Area(info,"Winning formation strip",.037f,.557f,.926f,.119f);
            Paint(attack,TowerArt("_btn_white"),new Color32(108,158,207,19),sliced:true);
            for(int i=0;i<4;i++) keys.Add(("historyAlly"+i,TowerBadge(attack,"Winning hero "+i,.066f+i*.095f,.5f,84,true,false)));
            Picture(attack,"Win",Art("battle_pvp_icon_win"),.426f,.21f,.076f,.59f,true);
            Text(attack,"Clear result","tower.cleared",25,.511f,.06f,.45f,.88f,Mist);
            Picture(history,"Close cross",Art("cm_icon_etcclose"),.47f,.953f,.018f,.025f,true);
            Text(history,"Tap to close","tower.tap_close",22,.488f,.938f,.16f,.05f,Mist);
            Bind(overlay,keys.ToArray());
            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }
    }
}
