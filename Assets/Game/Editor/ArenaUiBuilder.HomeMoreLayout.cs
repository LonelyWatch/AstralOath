using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const float HomeDesignScale = 1080f / 720f;
        private const string MorePrefix = "home.more.";
        private static readonly Color MoreCaptionColor = new Color32(192, 225, 243, 255);

        private static Sprite HomeMoreArt(string name) =>
            Load<Sprite>(InventoryTextures + "More/" + name + ".png");

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
            return Paint(rect, Art(texture, Home), dimmed ? new Color32(255, 255, 255, 100) : Color.white);
        }

        private static void DrawQuietHomeBell(RectTransform root, bool dimmed = false)
        {
            var rect = HomeRightRect(root, "Notifications", -272, 33, 48, 48, new Vector2(.5f, .5f));
            Paint(rect, HomeMoreArt("cm_icon_account_alram"), dimmed ? new Color32(255, 255, 255, 100) : Color.white);
        }

        private static void DrawNativeTopBarIcons(RectTransform root, bool dimmed = false, bool drawerOpen = false)
        {
            DrawQuietHomeBell(root, dimmed);
            DrawHomeTopIcon(root, "Mail", "cm_hud_top_btn_mail", -125, 59, 66, dimmed);
            DrawHomeTopIcon(root, "Chat", "cm_hud_top_btn_chatting", -68, 59, 66);
            DrawHomeTopIcon(root, "Bag", "cm_hud_top_btn_bag", -184, 59, 66);
            if (!drawerOpen)
                DrawHomeTopIcon(root, "More", "cm_hud_top_btn_quick", 0, 68, 66, dimmed);
        }

        private static (GameButton More, GameButton Bag) BuildNativeTopBarIcons(RectTransform root)
        {
            DrawQuietHomeBell(root);
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
            return (more, bag);
        }

        private static void DrawHomeNavigationBackground(RectTransform root)
        {
            var background = HomeRightRect(root, "Native lobby navigation background", 0, 0, 1000, 720, Vector2.one);
            Paint(background, HomeMoreArt("renew_glow_quick_2"), Color.white);
        }

        private static void DrawHomeShortcutRail(RectTransform root)
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
                Paint(icon, Art(sprite, Home), Color.white);
                var caption = HomeRightRect(root, key + " caption", 57, y + 33, 112, 28, center);
                caption.anchorMin = caption.anchorMax = Vector2.up;
                Text(caption, "Text", MorePrefix + key, 25, 0, 0, 1, 1, color, TextAlignmentOptions.Center);
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
            DrawNativeTopBarIcons(root, drawerOpen: true);
            var closeRect = HomeRightRect(root, "Close more", -9, 0, 64, 66, Vector2.one);
            var closeIcon = InventoryArt("quick_esc");
            var close = Button(closeRect, closeIcon, new Color32(116, 171, 253, 255));
            close.FeedbackSound = GameUiFeedback.Sound.Back;
            PressSprites(close, closeIcon, InventoryArt("quick_esc_p"));

            var summon = DrawMoreMainLinks(drawer);
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
                bool locked = key == "imprint";
                var tint = locked ? new Color32(255, 255, 255, 64) : new Color32(255, 255, 255, 255);
                var icon = HomeRightRect(drawer, key + " icon", x, y, 64, 64, new Vector2(.5f, .5f));
                Paint(icon, HomeMoreArt(sprite), tint);
                DrawMoreCaption(drawer, key + " caption", MorePrefix + key, x, y, (byte)(locked ? 82 : 255));
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
            var heroCaption = DrawMoreCaption(drawer, "Hero caption", InventoryPrefix + "heroes", -77, 140);
            DrawMoreBottom(drawer);

            Bind(root, ("heroes", heroes), ("heroesLabel", heroCaption), ("close", close), ("outside", outside),
                ("drawer", drawer), ("shade", shade), ("summon", summon));
            return root.gameObject;
        }

        private static GameText DrawMoreCaption(RectTransform root, string name, string key, float x, float y, byte alpha = 255)
        {
            var rect = HomeRightRect(root, name, x, y + 36, 121, 29, new Vector2(.5f, .5f));
            var color = MoreCaptionColor;
            color.a = alpha / 255f;
            return Text(rect, "Text", key, 24, 0, 0, 1, 1, color, TextAlignmentOptions.Center);
        }

        private static GameButton DrawMoreMainLinks(RectTransform root)
        {
            var entries = new (string Key, int CenterY, int Width)[]
            {
                (MorePrefix + "episode", 184, 109), (MorePrefix + "side_story", 259, 104),
                (MorePrefix + "battle", 324, 54), ("home.arena", 389, 81),
                (MorePrefix + "summon", 454, 54), (MorePrefix + "shop", 519, 54)
            };
            GameButton summon = null;
            foreach (var (key, y, width) in entries)
            {
                var background = HomeRightRect(root, key + " backing", -413, y + 6, width, 27,
                    new Vector2(1f, .5f));
                Paint(background, HomeMoreArt("renew_main_hud_btn_deco_1"), new Color32(255, 255, 255, 76), sliced: true);
                var rect = HomeRightRect(root, key, -422, y, 182, 46, new Vector2(1f, .5f));
                if (key == MorePrefix + "summon") summon = Button(rect, null, Color.clear);
                Text(rect, "Text", key, key == MorePrefix + "episode" ? 45 : 33,
                    0, 0, 1, 1, alignment: TextAlignmentOptions.MidlineRight);
            }
            return summon;
        }

        private static void DrawMoreBottom(RectTransform root)
        {
            var icons = new[] { "renew_icon_quick_notice", "renew_icon_quick_lobby_custom",
                "renew_icon_quick_pub", "renew_icon_quick_setting" };
            for (int i = 0; i < icons.Length; ++i)
            {
                var icon = HomeRightRect(root, icons[i], -339 + i * 92, 590, 48, 48, new Vector2(.5f, .5f));
                Paint(icon, HomeMoreArt(icons[i]), new Color32(197, 211, 219, 255));
            }
            var player = HomeRightRect(root, "Music player", -385, 628, 370, 74, new Vector2(0, 1));
            Paint(player, HomeMoreArt("renew_music_player_bg"), Color.white, sliced: true);
            Picture(player, "Music album art", HomeMoreArt("music_s_epi1_intro1"), .026f, .20f, .122f, .61f);
            Text(player, "Music title", MorePrefix + "music", 20, .163f, .246f, .50f, .30f);
            Text(player, "Music empty", MorePrefix + "music_empty", 16, .163f, .535f, .50f, .25f,
                new Color32(163, 178, 198, 255));
            var previous = Picture(player, "Previous track", HomeMoreArt("icon_menu_playerff"), .686f, .370f, .052f, .260f);
            var play = Picture(player, "Play track", HomeMoreArt("icon_menu_play_music"), .765f, .297f, .081f, .405f);
            var next = Picture(player, "Next track", HomeMoreArt("icon_menu_playerff"), .884f, .370f, .052f, .260f);
            next.rectTransform.localScale = new Vector3(-1, 1, 1);
            previous.color = play.color = next.color = new Color32(255, 255, 255, 140);
        }
    }
}
