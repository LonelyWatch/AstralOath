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
        private static Sprite ArtifactRestoreArt(string key) => Load<Sprite>(InventoryTextures + "ArtifactRestore/" + key + ".png");

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
                Paint(Area(host, "Original card surround", -.008f, -.003f, 1.029f, 1.018f),
                    ArtifactRestoreArt("arti_frame_box"), Color.white, sliced:true).pixelsPerUnitMultiplier = 1f / BagDesignScale;
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
                Caption(host,"cardName",null,38,.04f,.83f,.92f,.083f,Color.white).alignment=TextAlignmentOptions.Center;
                Picture(host,"Name ornament",ArtifactRestoreArt("arti_frame_deco_2"),0,.749f,1,.249f,true);
                Picture(host,"Attack symbol",InventoryArt("Equipment/cm_icon_stat_att"),.052f,.938f,.065f,.046f,true);
                Picture(host,"Health symbol",InventoryArt("Equipment/cm_icon_stat_max_hp"),.77f,.938f,.065f,.046f,true);
                Caption(host,"cardAttack",null,32,.122f,.925f,.18f,.066f,Color.white);
                Caption(host,"cardHealth",null,32,.845f,.925f,.15f,.066f,Color.white);
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
            Caption(content,"grade",null,28,.513f,.034f,.47f,.047f,Color.white);
            Caption(content,"name",null,45,.513f,.084f,.474f,.067f,Color.white).fontStyle=FontStyles.Bold;
            Caption(content,"story",null,24,.513f,.154f,.478f,.208f,Mist,true).alignment=TextAlignmentOptions.TopLeft;
            Caption(content,"class",null,31,.523f,.392f,.46f,.067f,Color.white);
            Caption(content,"skillTitle",ArtifactInventoryPrefix+"skill_heading",31,.521f,.468f,.29f,.067f,new Color32(112,190,231,255));
            Caption(content,"level",null,38,.74f,.467f,.247f,.069f,new Color32(253,166,67,255)).alignment=TextAlignmentOptions.Right;
            Paint(Area(content,"Skill divider",.503f,.531f,.495f,.014f),BagArt("_bar_2"),new Color32(152,166,192,148),sliced:true);
            Caption(content,"body",null,29,.517f,.55f,.477f,.218f,Color.white,true).alignment=TextAlignmentOptions.TopLeft;
            Act(content,"training",ArtifactInventoryPrefix+"enhance",25,.727f,.78f,.247f,.091f,SummonArt("_btn_blue_r_3"));
            Act(content,"equip","home.gear.singleEquip",30,.519f,.881f,.455f,.105f,CommonArt("_btn_green"));
            Act(content,"unequip","home.gear.singleRemove",30,.519f,.881f,.455f,.105f,CommonArt("_btn_gold"));
            Caption(pick,"status",null,25,.13f,.38f,.55f,.16f,Mist,true).alignment=TextAlignmentOptions.Center;
            Act(pick,"retry","home.hero_inventory.retry",27,.315f,.57f,.16f,.068f,SummonArt("_btn_blue_r_3"));

            var popup=Area(page,"Worn artifact details",0,0,1,1);
            Link("popup",popup.gameObject);
            Link("outside",Button(Area(popup,"Dismiss outside",0,0,1,1),null,Black(.62f)));
            var dialog=Area(popup,"Details panel",.50f,.015f,0,.902f);
            var fit=dialog.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode=AspectRatioFitter.AspectMode.HeightControlsWidth;
            fit.aspectRatio=442f/650f;
            Paint(dialog,ArtifactRestoreArt("_box_equip_0_linear"),Color.white,true,true).pixelsPerUnitMultiplier=1f/BagDesignScale;
            var inside=Area(dialog,"Detail contents",0,0,1,1);
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
            var wornSale=Button(Area(inside,"Sell worn artifact unavailable",.807f,.532f,.17f,.088f),
                BagArt("_btn_small_black"),new Color32(255,255,255,90));
            wornSale.interactable=false;
            Picture((RectTransform)wornSale.transform,"Sell",BagArt("cm_icon_etcbin"),.22f,.14f,.56f,.72f,true)
                .color=new Color32(151,165,182,70);
            Act(inside,"skillTab",ArtifactInventoryPrefix+"skill_heading",23,.008f,.631f,.495f,.066f,SummonArt("_btn_white"));
            Act(inside,"storyTab",ArtifactInventoryPrefix+"story_tab",23,.506f,.631f,.485f,.066f,SummonArt("_btn_white"));
            var skillButton=(GameButton)keys.Find(pair=>pair.Item1=="skillTab").Item2;
            var skillLabel=(GameText)keys.Find(pair=>pair.Item1=="skillTabLabel").Item2;
            Place(skillLabel.rectTransform,.29f,0,.63f,1);
            Link("skillTabGlyph",Picture((RectTransform)skillButton.transform,"Artifact skill",InventoryArt("icon_menu_pararti"),.20f,.16f,.13f,.68f,true));
            var popupBody = Caption(inside,"popupBody",null,24,0,0,1,1,Color.white,true);
            popupBody.alignment=TextAlignmentOptions.TopLeft;
            ApplyWornArtifactInsets(popupBody.rectTransform);
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
            Caption(painting,"paintingTitle",null,45,.047f,0,.42f,.091f,Color.white);
            Act(painting,"rotate",ArtifactInventoryPrefix+"rotate",28,.594f,.706f,.104f,.066f,BagArt("_btn_small_black"));
            var rotateButton=(GameButton)keys.Find(pair=>pair.Item1=="rotate").Item2;
            Place(((GameText)keys.Find(pair=>pair.Item1=="rotateLabel").Item2).rectTransform,.30f,0,.68f,1);
            Picture((RectTransform)rotateButton.transform,"Rotate",ArtifactRestoreArt("icon_menu_rotate"),.06f,.20f,.22f,.60f,true).color=Mist;
            Paint(Area(painting,"Painting gesture help",.316f,.795f,.38f,.176f),BagArt("_btn_small_black"),new Color32(255,255,255,153),sliced:true);
            Picture(painting,"Gesture help icon",SummonArt("icon_menu_info"),.495f,.815f,.022f,.042f,true).color=Mist;
            Caption(painting,"gestures",ArtifactInventoryPrefix+"painting_help",26,.35f,.858f,.3f,.099f,Mist,true).alignment=TextAlignmentOptions.Center;
            Bind(page,keys.ToArray());
            owner.Add(("artifactHeroEquipment",page.GetComponent<BindingMap>()));
            page.gameObject.SetActive(false);
        }

        private static void ApplyWornArtifactInsets(RectTransform body)
        {
            var scroll = body.GetComponentInParent<ScrollRect>(true);
            var contents = (RectTransform)(scroll ? scroll.transform.parent : body.parent);
            // The source sprite includes a 31 px transparent surround. Keep the authored
            // corner scale and place content inside its visible frame, rather than shrinking the corners.
            Place(contents, .111f, .072f, .789f, .849f);
            if (!scroll)
            {
                var viewport = Area(contents,"Description viewport",.009f,.716f,.982f,.166f);
                scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = body;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 38f;
                viewport.gameObject.AddComponent<RectMask2D>();
                Paint(viewport,null,Color.clear,true);
                body.SetParent(viewport,false);
                body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            var text = body.GetComponent<GameText>();
            text.fontSize = 24f;
            text.lineSpacing = 12f;
            text.margin = new Vector4(0,5f,0,5f);
            text.color = new Color32(231,235,241,255);
            text.overflowMode = TextOverflowModes.Overflow;
            body.pivot = new Vector2(.5f,1f);
            Place(body,.029f,0,.942f,0);
        }
    }
}
