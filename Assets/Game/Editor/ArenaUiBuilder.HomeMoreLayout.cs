using AstralOath.Client.Common;
using AstralOath.Client.Home;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const float HomeDesignScale = 1080f / 720f;
        private const string MorePrefix = "home.more.";
        private static readonly Color MoreCaptionColor = new Color32(192, 225, 243, 255);

        private static Sprite HomeMoreArt(string name) =>
            Load<Sprite>(InventoryTextures + "More/" + name + ".png");

        [MenuItem("Tools/游戏主页/生成主页导航")]
        public static void BuildHomeNavigationAssets()
        {
            font=Load<TMP_FontAsset>(Common+"Fonts/EpicSeven.asset");
            labels=JsonUtility.FromJson<LabelFile>(File.ReadAllText(Arena+"Configs/arena-zh-hans.json")).rows.ToDictionary(x=>x.key,x=>x.text);
            Save(BuildHome(),Home+"Res/Prefab/HomeWindow.prefab");Save(BuildHomeMoreMenu(),HomeMoreMenuPrefabPath);
            UIViewRegistry.ValidatePrefab<HomeWindow>(Load<GameObject>(Home+"Res/Prefab/HomeWindow.prefab"));
            UIViewRegistry.ValidatePrefab<HomeMoreMenu>(Load<GameObject>(HomeMoreMenuPrefabPath));AssetDatabase.SaveAssets();
        }

        private static void QuietUnavailableHomeButtons(List<(string Key,Object Target)> keys)
        {
            foreach(var row in keys)
                if(row.Key.StartsWith("feature.")&&row.Key!="feature.arena"&&row.Key!="feature.summon"&&row.Key!="feature.shop"&&row.Key!="feature.heroes"&&row.Target is GameButton button)
                    button.FeedbackSound=GameUiFeedback.Sound.Silent;
        }

        // top_bar.csb and quick_menu_renew.csb use a 720-high design, anchored against the usable right edge.
        private static RectTransform HomeRightRect(RectTransform parent, string name, float right, float down,
            float width, float height, Vector2 pivot)
        {
            var rect = Area(parent, name, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = pivot;
            rect.anchoredPosition = new Vector2(right, -down) * HomeDesignScale;
            rect.sizeDelta = new Vector2(width, height) * HomeDesignScale;
            return rect;
        }

        private static Image DrawHomeTopIcon(RectTransform root, string name, string texture,
            float right, float width, float height, bool dimmed = false)
        {
            var rect = HomeRightRect(root, name, right, (66 - height) / 2, width, height, Vector2.one);
            if (texture == "cm_hud_top_btn_mail" || texture == "cm_hud_top_btn_chatting")
            {
                var source=texture=="cm_hud_top_btn_mail"?MailEntryPath:"Assets/Game/Modules/ChannelChat/Res/Prefab/ChatEntry.prefab";
                var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(source), rect);
                var nested = (RectTransform)go.transform;
                nested.anchorMin = Vector2.zero;
                nested.anchorMax = Vector2.one;
                nested.offsetMin = nested.offsetMax = Vector2.zero;
                var image = (Image)go.GetComponent<BindingMap>().Require<GameButton>("button").targetGraphic;
                image.color = dimmed ? new Color32(255, 255, 255, 100) : Color.white;
                return image;
            }
            return Paint(rect, Art(texture, Home), dimmed ? new Color32(255, 255, 255, 100) : Color.white);
        }

        private static GameButton DrawQuietHomeBell(RectTransform root, bool dimmed = false)
        {
            var rect = HomeRightRect(root, "Notifications", -272, 33, 48, 48, new Vector2(.5f, .5f));
            return Button(rect, HomeMoreArt("cm_icon_account_alram"), new Color32(180, 180, 180, 100));
        }

        private static GameButton DrawNativeTopBarIcons(RectTransform root, bool dimmed = false, bool drawerOpen = false)
        {
            var bell=DrawQuietHomeBell(root, dimmed);
            DrawHomeTopIcon(root, "Mail", "cm_hud_top_btn_mail", -125, 59, 66, dimmed);
            DrawHomeTopIcon(root, "Chat", "cm_hud_top_btn_chatting", -68, 59, 66);
            DrawHomeTopIcon(root, "Bag", "cm_hud_top_btn_bag", -184, 59, 66);
            if (!drawerOpen)
                DrawHomeTopIcon(root, "More", "cm_hud_top_btn_quick", 0, 68, 66, dimmed);
            return bell;
        }

        private static (GameButton More, GameButton Bag,GameButton Bell) BuildNativeTopBarIcons(RectTransform root)
        {
            var bell=DrawQuietHomeBell(root);
            DrawHomeTopIcon(root, "Mail", "cm_hud_top_btn_mail", -125, 59, 66);
            DrawHomeTopIcon(root, "Chat", "cm_hud_top_btn_chatting", -68, 59, 66);

            var moreRect = HomeRightRect(root, "More", 0, 0, 68, 66, Vector2.one);
            var moreSprite = Art("cm_hud_top_btn_quick", Home);
            var more = Button(moreRect, moreSprite, Color.white);
            PressSprites(more, moreSprite, HomeMoreArt("cm_hud_top_btn_quick_p"));

            var bagRect = HomeRightRect(root, "Bag", -184, 0, 59, 66, Vector2.one);
            var bagSprite = Art("cm_hud_top_btn_bag", Home);
            var bag = Button(bagRect, bagSprite, Color.white);
            PressSprites(bag, bagSprite, HomeMoreArt("cm_hud_top_btn_bag_p"));
            return (more, bag,bell);
        }

        private static void DrawHomeNavigationBackground(RectTransform root)
        {
            // quick_menu_renew.csb grow2: full-size1000x720, center38.516 right of
            // n_quick_menu's collapsed right anchor. Only461.484 source pixels are visible.
            // renew_glow_quick_2 is a129x72 thumbnail and is not referenced by this layout.
            // The expanded drawer below moves this same source layer left377 units.
            var background = HomeRightRect(root, "Native lobby navigation background", 38.516f, 0, 1000, 720, new Vector2(.5f,1));
            // Purpose alpha variant preserves the source neutral-dark contrast in
            // Linear; the full source PNG and expanded drawer stay unchanged.
            Paint(background, HomeMoreArt("renew_glow_quick_collapsed_linear"), Color.white);
        }

        private static void DrawHomeShortcutRail(RectTransform root,List<(string Key,Object Target)> keys)
        {
            // lobby_ui_renew.csb LEFT: retain the rendered shortcut order and their original icons.
            var shade = HomeRightRect(root, "Shortcut side shade", -40, 0, 520, 720, new Vector2(0, 1));
            shade.anchorMin = shade.anchorMax = Vector2.up;
            Paint(shade, HomeMoreArt("renew_glow_4"), new Color32(34, 36, 43, 178));

            var entries = new (string Key, string Sprite, float Width, float Height, Color Color)[]
            {
                ("sanctuary", "renew_icon_quick_sanctuary", 64, 64, MoreCaptionColor),
                ("milestones", "renew_cm_hud_coreloop", 64, 64, new Color32(255, 238, 152, 242)),
                ("secret_shop", "renew_icon_quick_secretshop", 64, 64, MoreCaptionColor),
                ("epic_pass", "renew_cm_hud_epicpass", 67, 67, MoreCaptionColor),
                ("events", "renew_cm_hud_evnet", 67, 67, MoreCaptionColor),
                ("bunny_hideout", "main_hud_btn_bunny", 76, 84, new Color32(255, 201, 201, 242))
            };
            for (int i = 0; i < entries.Length; ++i)
            {
                var (key, sprite, width, height, color) = entries[i];
                var center = new Vector2(.5f, .5f);
                float y = 161 + 78 * i;
                var icon = HomeRightRect(root, key + " icon", 57, y, width, height, center);
                icon.anchorMin = icon.anchorMax = Vector2.up;
                Paint(icon, Art(sprite, Home), new Color32(180,180,180,100));
                var caption = HomeRightRect(root, key + " caption", 57, y + 33, 112, 28, center);
                caption.anchorMin = caption.anchorMax = Vector2.up;
                var text=Text(caption, "Text", null, 25, 0, 0, 1, 1, new Color32(180,180,180,100), TextAlignmentOptions.Center);text.text=HomeFeatureLabels.Text(key);text.richText=false;
                var hit=HomeRightRect(root,key+" unavailable",57,y+16,112,106,center);hit.anchorMin=hit.anchorMax=Vector2.up;
                keys.Add(("feature."+key,Button(hit,null,Color.clear)));
            }
        }

        private static void DrawHomeTopRule(RectTransform root)
        {
            var rule = Area(root, "Top bar hairline", 0, 0, 1, 0);
            rule.offsetMin = new Vector2(0, -67 * HomeDesignScale);
            rule.offsetMax = new Vector2(0, -66 * HomeDesignScale);
            Paint(rule, null, new Color32(223, 232, 243, 28));
        }

        private static GameObject BuildHomeMoreMenu()
        {
            var root = Root("HomeMoreMenu");
            var keys=new List<(string Key,Object Target)>();
            root.gameObject.AddComponent<RectMask2D>();
            var outside = Button(Area(root, "Outside click", 0, 0, 1, 1), null, Black(.24f));
            outside.FeedbackSound = GameUiFeedback.Sound.Back;
            var shade = outside.gameObject.AddComponent<CanvasGroup>();
            var drawer = Area(root, "Drawer contents", 0, 0, 1, 1);

            // The pattern and border come from the same translucent layer in the original.
            var drawerArt = HomeRightRect(drawer, "Native quick menu background", -338.484f, 0, 1000, 720,
                new Vector2(.5f, 1));
            Paint(drawerArt, HomeMoreArt("renew_glow_quick"), Color.white);
            Paint(HomeRightRect(drawer, "Drawer hit area", 0, 66, 399, 654, Vector2.one), null, Color.clear, true);

            DrawHomeTopRule(root);
            var bell=DrawNativeTopBarIcons(root, drawerOpen: true);keys.Add(("feature.notifications",bell));
            var bagImage=drawer.parent.GetComponentsInChildren<Image>(true).First(image=>image.name=="Bag");
            var bag=bagImage.gameObject.AddComponent<GameButton>();bag.targetGraphic=bagImage;PressSprites(bag,Art("cm_hud_top_btn_bag",Home),HomeMoreArt("cm_hud_top_btn_bag_p"));keys.Add(("bag",bag));
            var closeRect = HomeRightRect(root, "Close more", -9, 0, 64, 66, Vector2.one);
            var closeIcon = InventoryArt("quick_esc");
            var close = Button(closeRect, closeIcon, new Color32(116, 171, 253, 255));
            close.FeedbackSound = GameUiFeedback.Sound.Back;
            PressSprites(close, closeIcon, InventoryArt("quick_esc_p"));

            DrawHomeMainLinks(drawer,true,keys);
            var entries = new (string Key, string Sprite)[]
            {
                ("guide", "icon_lobby_master_guide"), ("collection", "renew_icon_quick_dictionary"), (null, null),
                ("growth", "renew_icon_quick_blessing"), ("pets", "renew_icon_quick_pet"), ("recruit", "renew_icon_quick_recruit"),
                ("friends", "renew_icon_quick_friend"), ("guild", "renew_icon_quick_knightage"), ("forge", "renew_icon_quick_create"),
                ("achievements", "renew_icon_quick_achieve"), ("waiting_room", "renew_icon_quick_storage"), ("imprint", "soul_imprint_board"),
            };
            for (int i = 0; i < entries.Length; ++i)
            {
                var (key, sprite) = entries[i];
                if (key == null) continue;
                var x = -327 + 125 * (i % 3);
                var y = 140 + 116 * (i / 3);
                bool locked = true;
                var tint = new Color32(180,180,180,100);
                var icon = HomeRightRect(drawer, key + " icon", x, y, 64, 64, new Vector2(.5f, .5f));
                Paint(icon, HomeMoreArt(sprite), tint);
                DrawMoreCaption(drawer, key + " caption", key, x, y, 100);
                var hit=HomeRightRect(drawer,key+" unavailable",x,y+16,112,106,new Vector2(.5f,.5f));keys.Add(("feature."+key,Button(hit,null,Color.clear)));
                if (locked)
                {
                    var badge = HomeRightRect(drawer, "Imprint locked", x + 20, y + 12, 23, 26, new Vector2(.5f, .5f));
                    Paint(badge, Art("cm_icon_etclock"), Color.white);
                }
            }

            var heroRect = HomeRightRect(drawer, "Heroes", -77, 152, 112, 98, new Vector2(.5f, .5f));
            var heroes = Button(heroRect, null, Color.clear);
            var heroIcon = Picture(heroRect, "Hero icon", InventoryArt("renew_icon_quick_hero"),
                24f / 112, 5f / 98, 64f / 112, 64f / 98);
            PressDecorations(heroes, (heroIcon, InventoryArt("renew_icon_quick_hero_p")));
            var heroCaption = DrawMoreCaption(drawer, "Hero caption", "heroes", -77, 140);
            keys.Add(("feature.heroes",heroes));
            DrawMoreBottom(drawer,keys);

            keys.AddRange(new (string Key,Object Target)[]{("heroes",heroes),("heroesLabel",heroCaption),("close",close),("outside",outside),("drawer",drawer),("shade",shade)});
            QuietUnavailableHomeButtons(keys);Bind(root,keys.ToArray());
            return root.gameObject;
        }

        private static GameText DrawMoreCaption(RectTransform root, string name, string key, float x, float y, byte alpha = 255)
        {
            var rect = HomeRightRect(root, name, x, y + 36, 121, 29, new Vector2(.5f, .5f));
            var color = MoreCaptionColor;
            color.a = alpha / 255f;
            var text=Text(rect,"Text",null,24,0,0,1,1,color,TextAlignmentOptions.Center);text.text=HomeFeatureLabels.Text(key);text.richText=false;return text;
        }

        private static void DrawHomeMainLinks(RectTransform root,bool expanded,List<(string Key,Object Target)> keys)
        {
            // CN labels have different advances from the Korean template. Plate widths below
            // are measured in the 720-high recording, with the phone's left margin excluded.
            var entries = new (string Key, int CenterY, int Width)[]
            {
                ("episode",184,112),("side_story",259,102),("battle",324,60),("arena",389,80),("summon",454,60),("shop",519,60)
            };
            foreach (var (key, y, width) in entries)
            {
                bool episode = key == "episode",available=HomeFeatureLabels.MainAvailable(key);
                float right = (key == "shop" ? -421f : episode ? -424f : -422f)+(expanded?0:377);
                var background = HomeRightRect(root, key + " backing", right + 12, y + 14, width,
                    episode ? 28f : 28f * .85f, new Vector2(1f, .5f));
                var plate = Paint(background, HomeMoreArt("renew_main_hud_btn_deco_1"),
                    new Color32(255, 255, 255, QualitySettings.activeColorSpace == ColorSpace.Linear ? (byte)22 : (byte)76), sliced: true);
                plate.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
                var rect = HomeRightRect(root, key, right, episode ? y + 3 : y, 182, 46, new Vector2(1f, .5f));
                var button=Button(rect,null,Color.clear);keys.Add(("feature."+key,button));

                // quick_menu_renew.csb has a second faint white label two pixels right
                // and three pixels below the foreground. Keep both on the same reference grid.
                float size = 50f * (episode ? .57f : .47f) * HomeDesignScale;
                var echo = Text(rect, "Label decoration", null, size, 0, 0, 1, 1,
                    new Color32(255, 255, 255,available?(byte)25:(byte)9), TextAlignmentOptions.MidlineRight);echo.text=HomeFeatureLabels.Text(key);echo.richText=false;
                echo.rectTransform.anchoredPosition += new Vector2(2f, -3f) * HomeDesignScale;
                var label=Text(rect,"Text",null,size,0,0,1,1,available?Color.white:new Color32(180,180,180,100),TextAlignmentOptions.MidlineRight);label.text=HomeFeatureLabels.Text(key);label.richText=false;keys.Add(("featureLabel."+key,label));
                if(!available)Picture(rect,"Unavailable source lock",Art("cm_icon_etclock"),1.015f,.31f,.075f,.43f,true);
                if(key=="arena"){keys.Add(("arena",button));keys.Add(("arenaLabel",label));}
                if(key=="summon")keys.Add(("summon",button));
            }
        }

        private static void DrawMoreBottom(RectTransform root,List<(string Key,Object Target)> keys)
        {
            var icons = new[] { "renew_icon_quick_notice", "renew_icon_quick_lobby_custom",
                "renew_icon_quick_pub", "renew_icon_quick_setting" };
            for (int i = 0; i < icons.Length; ++i)
            {
                var icon = HomeRightRect(root, icons[i], -339 + i * 92, 590, 48, 48, new Vector2(.5f, .5f));
                var ids=new[]{"notice","lobby_custom","pub","settings"};
                keys.Add(("feature."+ids[i],Button(icon,HomeMoreArt(icons[i]),new Color32(180,180,180,100))));
            }
            var player = HomeRightRect(root, "Music player", -385, 628, 370, 74, new Vector2(0, 1));
            var music=Button(player,HomeMoreArt("renew_music_player_bg"),new Color32(180,180,180,100));((Image)music.targetGraphic).type=Image.Type.Sliced;keys.Add(("feature.music",music));
            Picture(player, "Music album art", HomeMoreArt("music_s_epi1_intro1"), .026f, .20f, .122f, .61f);
            var title=Text(player,"Music title",null,20,.163f,.246f,.50f,.30f,new Color32(180,180,180,100));title.text=HomeFeatureLabels.Text("music");title.richText=false;
            var previous = Picture(player, "Previous track", HomeMoreArt("icon_menu_playerff"), .686f, .370f, .052f, .260f);
            var play = Picture(player, "Play track", HomeMoreArt("icon_menu_play_music"), .765f, .297f, .081f, .405f);
            var next = Picture(player, "Next track", HomeMoreArt("icon_menu_playerff"), .884f, .370f, .052f, .260f);
            next.rectTransform.localScale = new Vector3(-1, 1, 1);
            previous.color = play.color = next.color = new Color32(255, 255, 255, 140);
            foreach(var item in new[]{("music_prev",previous),("music_play",play),("music_next",next)})
            {var button=item.Item2.gameObject.AddComponent<GameButton>();button.targetGraphic=item.Item2;keys.Add(("feature."+item.Item1,button));}
        }
    }
}
