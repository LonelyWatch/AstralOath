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
        private static void BuildWornArtifactMarkers(RectTransform slot, string prefix, List<(string Key, Object Target)> owner)
        {
            var star = DraftSprite("TeamBadges/cm_icon_star");
            for (int i = 0; i < 5; ++i)
            {
                var icon = Picture(slot, "Worn artifact rarity " + i, star, .10f + i * .15f, .825f, .20f, .17f, true);
                icon.enabled = false;
                owner.Add((prefix + "Star" + i, icon));
            }
            var locked = Picture(slot, "Worn artifact lock", Art("cm_icon_etclock"), .015f, .08f, .22f, .21f, true);
            locked.enabled = false;
            owner.Add((prefix + "Lock", locked));
        }

        private static void BuildArtifactHeroEquipment(RectTransform root, RectTransform slot,
            List<(string Key, Object Target)> owner)
        {
            BuildWornArtifactMarkers(slot, "artifactSlot", owner);
            var star = DraftSprite("TeamBadges/cm_icon_star");
            var page = Area(root, "Hero artifact equipment", 0, 0, 1, 1);
            var keys = new List<(string, Object)>();
            void Link(string key, Object target) => keys.Add((key, target));
            GameText Caption(RectTransform parent, string key, string sourceKey, float size, float x, float y,
                float width, float height, Color color, bool wrap = false)
            {
                var text = Text(parent, key, sourceKey, size, x, y, width, height, color,
                    TextAlignmentOptions.MidlineLeft, wrap);
                text.richText = true;
                Link(key, text);
                return text;
            }
            void Act(RectTransform parent, string key, string caption, float size, float x, float y, float w, float h,
                Sprite sprite)
            {
                var button = Action(parent, key, caption, size, x, y, w, h, out var label, sprite, Color.white);
                if (sprite && sprite.name == "_btn_blue_r_3")
                {
                    PressSprites(button,sprite,SummonArt("_btn_blue_r_p_3"));
                    ((Image)button.targetGraphic).pixelsPerUnitMultiplier=2.5f;
                }
                label.fontStyle = FontStyles.Bold;
                Link(key, button);
                Link(key + "Label", label);
            }
            void CardFace(RectTransform host, string prefix, bool full)
            {
                Link(prefix + "Portrait", Picture(host, "Card illustration", null, 0, 0, 1, 1));
                Picture(host, "Emblem filigree", SummonArt("arti_frame_deco_1"), 0,0,.45f,.405f);
                Link(prefix + "Emblem", Picture(host, "Artifact emblem", null, .005f,.002f,.22f,.16f,true));
                Link(prefix + "Badge", Prefab(host,"NotificationBadge",.157f,-.007f,.141f,.060f).GetComponent<BindingMap>());
                Link(prefix + "Wearer", Picture(host,"Wearing hero",null,-.015f,.078f,.096f,.066f,true));
                for (int i=0; i<5; ++i)
                    Link(prefix+"Star"+i,Picture(host, "Star "+i,star,.896f,.016f+.047f*i,.086f,.049f,true));
                var zoomArea = Area(host,"View illustration",full ? .735f : .02f,full ? .014f : .875f,
                    full ? .129f : .175f,full ? .079f : .102f);
                var zoom = Button(zoomArea,BagArt("_btn_small_black"),Color.white);
                Picture(zoomArea,"Magnify",Art("cm_icon_etczoomer"),.13f,.13f,.74f,.74f,true);
                Link(prefix+"Zoom",zoom);
                if (!full) return;
                Caption(host,"cardName",null,33,.04f,.83f,.92f,.083f,Color.white).alignment=TextAlignmentOptions.Center;
                Paint(Area(host,"Name ornament",.12f,.914f,.76f,.003f),null,new Color32(223,168,79,190));
                Picture(host,"Attack symbol",InventoryArt("Equipment/cm_icon_stat_att"),.052f,.938f,.065f,.046f,true);
                Picture(host,"Health symbol",InventoryArt("Equipment/cm_icon_stat_max_hp"),.77f,.938f,.065f,.046f,true);
                Caption(host,"cardAttack",null,26,.122f,.925f,.18f,.066f,Color.white);
                Caption(host,"cardHealth",null,26,.845f,.925f,.15f,.066f,Color.white);
            }

            var pick = Area(page,"Equip artifact",0,0,1,1);
            Link("picker",pick.gameObject);
            Paint(pick,InventoryArt("base_unit_hero"),Color.white,true);
            var bar=Area(pick,"Header",0,0,1,.092f);
            Paint(bar,null,Black(.42f));
            Link("back",SharedHeaderBack(bar));
            Caption(bar,"title","home.gear.singleTitle",36,.049f,0,.22f,1,Color.white).fontStyle=FontStyles.Bold;
            DrawNativeTopBarIcons(pick,dimmed:true);
            var plate=Area(pick,"Candidate background",.677f,.092f,.323f,.908f);
            Paint(plate,null,Black(.61f));
            Act(plate,"sort",ArtifactInventoryPrefix+"sort_enhancement",24,.125f,.03f,.29f,.062f,BagArt("_btn_small_black"));
            Caption(plate,"capacity",null,25,.423f,.028f,.34f,.068f,Mist).alignment=TextAlignmentOptions.Center;
            var count=Area(plate,"Capacity plate",.423f,.031f,.32f,.061f);
            Paint(count,BagArt("_btn_small_black"),Color.white,false,true);
            count.SetSiblingIndex(0);
            Link("capacityClick", Button(Area(plate, "Inspect capacity", .423f, .028f, .34f, .068f), null, Color.clear));
            var scroll=InventoryScroll(plate,"Compatible artifacts",.014f,.111f,.775f,.889f);
            Link("list",scroll);
            Link("row",NativeArtifactInventoryCard(page));
            var content=Area(pick,"Artifact presentation",.106f,.103f,.567f,.872f);
            Link("content",content.gameObject);
            Link("stateRule",Paint(Area(content,"State stripe",0,0,.004f,.178f),null,Color.white));
            Caption(content,"state",null,21,.012f,-.006f,.32f,.035f,Color.white);
            var face=Area(content,"Vertical card",.014f,.033f,.45f,.955f);
            face=Area(face,"Card proportions",0,0,1,1);
            var faceFit=face.gameObject.AddComponent<AspectRatioFitter>();
            faceFit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            faceFit.aspectRatio=378f/598f;
            CardFace(face,"card",true);
            Caption(content,"grade",null,24,.513f,.034f,.47f,.047f,Color.white);
            Caption(content,"name",null,36,.513f,.084f,.474f,.067f,Color.white).fontStyle=FontStyles.Bold;
            Caption(content,"story",null,21,.513f,.154f,.478f,.208f,Mist,true).alignment=TextAlignmentOptions.TopLeft;
            Caption(content,"class",null,26,.523f,.392f,.46f,.067f,Color.white);
            Caption(content,"skillTitle",ArtifactInventoryPrefix+"skill_heading",25,.521f,.468f,.29f,.067f,new Color32(112,190,231,255));
            Caption(content,"level",null,32,.74f,.467f,.247f,.069f,new Color32(253,166,67,255)).alignment=TextAlignmentOptions.Right;
            Paint(Area(content,"Skill divider",.513f,.536f,.482f,.002f),null,new Color32(152,166,192,148));
            Caption(content,"body",null,29,.517f,.55f,.477f,.218f,Color.white,true).alignment=TextAlignmentOptions.TopLeft;
            Act(content,"training",ArtifactInventoryPrefix+"enhance",25,.727f,.78f,.247f,.091f,SummonArt("_btn_blue_r_3"));
            Act(content,"equip","home.gear.singleEquip",30,.519f,.881f,.455f,.105f,CommonArt("_btn_green"));
            Act(content,"unequip","home.gear.singleRemove",30,.519f,.881f,.455f,.105f,CommonArt("_btn_gold"));
            Caption(pick,"status",null,25,.13f,.38f,.55f,.16f,Mist,true).alignment=TextAlignmentOptions.Center;
            Act(pick,"retry","home.hero_inventory.retry",27,.315f,.57f,.16f,.068f,SummonArt("_btn_blue_r_3"));

            var popup=Area(page,"Worn artifact details",0,0,1,1);
            Link("popup",popup.gameObject);
            Link("outside",Button(Area(popup,"Dismiss outside",0,0,1,1),null,Black(.69f)));
            var dialog=Area(popup,"Details panel",.50f,.054f,0,.825f);
            var fit=dialog.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode=AspectRatioFitter.AspectMode.HeightControlsWidth;
            fit.aspectRatio=232f/362f;
            Paint(dialog,BagArt("_box_equip_0"),Color.white,true,true);
            var inside=Area(dialog,"Detail contents",.045f,.035f,.91f,.923f);
            var portrait=Area(inside,"Thumbnail",.013f,.012f,.605f,.603f);
            portrait=Area(portrait,"Card proportions",0,0,1,1);
            var portraitFit=portrait.gameObject.AddComponent<AspectRatioFitter>();
            portraitFit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            portraitFit.aspectRatio=378f/598f;
            CardFace(portrait,"popup",false);
            var lockButton=Button(Area(inside,"Toggle lock",.848f,.013f,.132f,.066f),BagArt("_btn_small_black"),Color.white);
            Link("lock",lockButton);
            Link("lockGlyph",Picture((RectTransform)lockButton.transform,"Lock",Art("cm_icon_etclock"),.22f,.12f,.56f,.76f,true));
            Caption(inside,"popupGrade",null,21,.644f,.092f,.344f,.052f,Color.white);
            Caption(inside,"popupName",null,27,.644f,.134f,.345f,.09f,Color.white,true).alignment=TextAlignmentOptions.TopLeft;
            Caption(inside,"popupLevel",null,28,.645f,.226f,.35f,.072f,new Color32(254,164,64,255));
            for (int i=0; i<5; ++i)
            {
                float x=.645f+.064f*i;
                Picture(inside,"Break slot "+i,SummonArt("arti_gauge_bg"),x,.291f,.063f,.022f,true);
                Link("popupBreak"+i,Picture(inside,"Break "+i,BagArt("arti_gauge_bar"),x,.291f,.063f,.022f,true));
                Picture(face,"Break slot "+i,SummonArt("arti_gauge_bg"),.313f+.078f*i,.95f,.077f,.027f,true);
                Link("cardBreak"+i,Picture(face,"Break "+i,BagArt("arti_gauge_bar"),.313f+.078f*i,.95f,.077f,.027f,true));
            }
            Caption(inside,"attackCaption",InventoryPrefix+"stat.attack",23,.645f,.318f,.34f,.045f,Mist);
            Caption(inside,"popupAttack",null,28,.645f,.357f,.34f,.058f,Color.white);
            Caption(inside,"healthCaption",InventoryPrefix+"stat.health",23,.645f,.405f,.34f,.045f,Mist);
            Caption(inside,"popupHealth",null,28,.645f,.444f,.34f,.058f,Color.white);
            Act(inside,"skillTab",ArtifactInventoryPrefix+"skill_heading",23,.008f,.631f,.495f,.066f,Art("_btn_white_flat"));
            Act(inside,"storyTab",ArtifactInventoryPrefix+"story_tab",23,.506f,.631f,.485f,.066f,Art("_btn_white_flat"));
            Caption(inside,"popupBody",null,24,.023f,.706f,.959f,.178f,Color.white,true).alignment=TextAlignmentOptions.TopLeft;
            Act(inside,"remove","home.gear.singleRemove",24,.016f,.902f,.269f,.088f,BagArt("_btn_small_black"));
            Act(inside,"change","home.gear.singleChange",24,.294f,.902f,.27f,.088f,BagArt("_btn_small_black"));
            Act(inside,"enhance",ArtifactInventoryPrefix+"enhance",26,.652f,.902f,.328f,.088f,SummonArt("_btn_blue_r_3"));
            Caption(popup,"dismissHint","home.gear.singleDismiss",21,.3f,.936f,.4f,.055f,Mist).alignment=TextAlignmentOptions.Center;

            var painting=Area(page,"View painting",0,0,1,1);
            Link("painting",painting.gameObject);
            Paint(painting,InventoryArt("base_unit_hero"),Color.white,true);
            var art=Picture(painting,"Full painting",null,.25f,.018f,.5f,.963f,true);
            Link("paintingImage",art);
            var gesture=Area(painting,"Inspect painting",0,0,1,1);
            Paint(gesture,null,Color.clear,true);
            Link("gesture",gesture);
            Link("paintBack",SharedHeaderBack(Area(painting,"Painting header",0,0,1,.092f)));
            Caption(painting,"paintingTitle",null,34,.047f,0,.42f,.091f,Color.white);
            Act(painting,"rotate",ArtifactInventoryPrefix+"rotate",21,.594f,.706f,.104f,.066f,BagArt("_btn_small_black"));
            Caption(painting,"gestures",ArtifactInventoryPrefix+"painting_help",21,.35f,.795f,.3f,.174f,Mist,true).alignment=TextAlignmentOptions.Center;
            Bind(page,keys.ToArray());
            owner.Add(("artifactHeroEquipment",page.GetComponent<BindingMap>()));
            page.gameObject.SetActive(false);
        }
    }
}
