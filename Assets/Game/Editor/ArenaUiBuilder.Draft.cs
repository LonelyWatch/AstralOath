using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AstralOath.Client.Arena;
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
        private const string DraftArtPath = Arena + "Configs/ArenaDraftArt.asset";
        private const string DraftPrefabPath = Arena + "Res/Prefab/ArenaDraft.prefab";
        private const string BattleHudTextures = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/";

        private static Sprite DraftSprite(string id) => Load<Sprite>(Arena + "Res/Textures/Draft/" + id + ".png");
        private static Sprite HudSprite(string id) => Load<Sprite>(BattleHudTextures + id + ".png");

        private static GameObject BuildDraft()
        {
            var outer = Root("ArenaDraft");
            Paint(outer, null, Color.black, true);
            Cover(outer, "Native selection background", DraftSprite("base_pvplive_vs"));
            var root = Area(outer, "Native reference layout", 0, 0, 1, 1);
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(1920, 1080);
            var refs = new List<(string, Object)>();
            void Add(string key, Object node) => refs.Add((key, node));
            Add("layout", root);
            Picture(root, "Native blue and red backdrop", DraftSprite("base_pvplive_eff"), 0, 0, 998f / 1280, 1);
            var stage = Area(root, "Draft stage", 0, 0, 998f / 1280, 1);
            var center = Area(stage, "Featured hero", 0, 0, 1, 1);
            center.gameObject.AddComponent<RectMask2D>();
            var fallback = Picture(center, "Static illustration", null, -40f / 998, -45f / 720, 1080f / 998, 1140f / 720, true);
            fallback.enabled = false;
            Add("featuredFallback", fallback);
            Add("featured", Raw(center, "Spine illustration", 0, 0, 1, 1));
            var footTone = Picture(stage, "Lower portrait fade", Art("_grow_s"), -.22f, 462f / 720, 1.46f, 258f / 720);
            footTone.rectTransform.localScale = new Vector3(1, -1, 1);
            Picture(stage, "Upper portrait fade", Art("_grow_s"), -.22f, 0, 1.46f, 258f / 720);
            Picture(stage, "Native left rail", Art("_bg_black_gold_c"), -112f / 998, 43f / 720, 576f / 998, 750f / 720);
            var rightRail = Picture(stage, "Native right rail", Art("_bg_black_gold_c"), 531f / 998, 43f / 720, 576f / 998, 750f / 720);
            rightRail.rectTransform.localScale = new Vector3(-1, 1, 1);
            Add("blue", DraftArc(stage, false, "Blue active arc"));
            Add("red", DraftArc(stage, true, "Red active arc"));
            Add("blueGuide", DraftGuide(stage, "Ally pick light", 274f));
            Add("redGuide", DraftGuide(stage, "Enemy pick light", 722f));
            for (int row = 0; row < 5; row++)
            {
                float width = NativeSlotWidths[row];
                float top = 120.5f + row * 100;
                var left = Area(stage, "Ally pick " + (row + 1), 19f / 998, top / 720, width / 998, 85f / 720);
                var right = Area(stage, "Enemy pick " + (row + 1), (976f - width) / 998, top / 720, width / 998, 85f / 720);
                Add("blue" + row, NativeTeamCard(left, row, false));
                Add("red" + row, NativeTeamCard(right, row, true));
            }
            var formation = Area(stage, "Formation slots", 0, 0, 1, 1);
            Add("formation", formation.gameObject);
            // Native CSB places model origins independently of floor plate centers.
            var floors = new[] { new Vector2(610.7f, 259.2f), new Vector2(508.5f, 192), new Vector2(491.7f, 318.7f), new Vector2(390.9f, 250.8f) };
            var changes = new[] { new Vector2(611, 327), new Vector2(509, 267), new Vector2(489, 389), new Vector2(392, 316) };
            var origins = new[] { new Vector2(608, 264), new Vector2(506, 193), new Vector2(493, 321), new Vector2(391, 250) };
            for (int index = 0; index < 4; index++)
            {
                var point = origins[index];
                var pad = Area(formation, "Position " + (index + 1), (point.x - 120) / 998, (720 - point.y - 190) / 720, 240f / 998, 240f / 720);
                string floor = index == 0 ? "hero_config_panelleader" : index == 3 ? "hero_config_panelback" : "hero_config_bg";
                var map = DraftCard(pad, DraftSprite(floor), null, false, false, true);
                Add("formation" + index, map);
                var ring = pad.Find("Floor ring") as RectTransform;
                Place(ring, (floors[index].x - point.x + 120 - 90.3f) / 240, (190 - (floors[index].y - point.y) - 42.7f) / 240, 180.6f / 240, 85.4f / 240);
                Place(map.Require<Image>("selection").rectTransform, ring.anchorMin.x, 1 - ring.anchorMax.y, 180.6f / 240, 85.4f / 240);
                Place(map.Require<Image>("change").rectTransform,
                    (changes[index].x - point.x + 120 - 43) / 240,
                    (190 - (changes[index].y - point.y) - 43) / 240, 86f / 240, 86f / 240);
                // Rear/upper models render first; front/lower overlap them naturally.
            }
            foreach (int index in new[] { 2, 3, 0, 1 })
                ((BindingMap)refs.Find(entry => entry.Item1 == "formation" + index).Item2).transform.SetAsLastSibling();

            BuildDraftHeader(stage, refs);
            var hint = Text(stage, "Draft instruction", null, 29, .285f, .686f, .428f, .073f,
                alignment: TextAlignmentOptions.Center);
            hint.fontStyle = FontStyles.Bold;
            hint.enableWordWrapping = true;
            Add("hint", hint);
            var banGlow = Picture(stage, "Final ban shade", Art("_grow"), 334f / 998, 417f / 720, 328f / 998, 334f / 720);
            banGlow.color = new Color(1, 1, 1, .4f);
            banGlow.enabled = false;
            Add("banGlow", banGlow);
            var mark = Picture(stage, "Final ban symbol", Art("icon_menu_banned"), 448f / 998, 482f / 720, 100f / 998, 100f / 720, true);
            mark.color = new Color32(195, 21, 21, 255);
            mark.enabled = false;
            Add("banMark", mark);
            var banEffect = Raw(stage, "Final ban effect", -21f / 998, 144f / 720, 1040f / 998, 620f / 720);
            banEffect.color = new Color(1f, 1f, 1f, .84f);
            Add("banFeedback", banEffect);
            BuildDraftRevealed(stage, refs);
            var action = CommonButton(stage, "ConfirmButton", "arena.draft.choose", 719f / 998, 628f / 720, 271f / 998, 72f / 720);
            action.BlockedFeedback = true;
            Add("action", action);
            Add("actionLabel", action.GetComponent<BindingMap>().Require<GameText>("label"));
            var arrow = Picture((RectTransform)action.transform, "Pick arrow", Art("cm_icon_etcarrow2"),
                238f / 271, 22.4f / 72, 12f / 271, 19.2f / 72, true);
            Add("actionArrow", arrow);

            var pool = Area(root, "Hero roster sidebar", 998f / 1280, 0, 282f / 1280, 1);
            Paint(pool, null, new Color32(8, 12, 20, 241));
            VerticalRule(pool, "Border", 0, .016f, .018f, .97f, Gold * .72f);
            Text(pool, "Roster caption", "arena.draft.roster", 23, .065f, .017f, .87f, .047f, Mist);
            Add("search", DraftSearch(pool, "Find hero", .52f, .071f, .43f, .062f));
            var roster = Scroll(pool, "Scroll heroes", 0, .149f, 1, .841f);
            // The viewport reveals the overhang. Empty scroll space stays on the sidebar.
            roster.viewport.offsetMin = new Vector2(-90f, 0);
            Object.DestroyImmediate(roster.viewport.GetComponent<Image>());
            var scrollSurface = Paint((RectTransform)roster.transform, null, Color.clear, true);
            scrollSurface.canvasRenderer.cullTransparentMesh = false;
            Object.DestroyImmediate(roster.content.GetComponent<VerticalLayoutGroup>());
            Object.DestroyImmediate(roster.content.GetComponent<ContentSizeFitter>());
            Add("roster", roster);
            Add("row", BuildDraftRosterRow(root).gameObject);
            DraftTemplate(root, "Preban hero template", 92, HudSprite("_hero_s_bg_ally"), false, true, out var gridTemplate, prebanCard: true);
            Add("gridCell", gridTemplate.gameObject);
            DraftTemplate(root, "Banned portrait template", 62, DraftSprite("cm_hero_cirbg2"), false, true, out var chip);
            Add("chip", chip.gameObject);

            BuildDraftFilters(root, pool, refs);

            var dim = Area(root, "Preban modal", 0, 0, 1, 1);
            Paint(dim, null, Black(.45f), true);
            Add("preban", dim.gameObject);
            var panel = Area(dim, "Preban blue panel", .029f, .047f, .942f, .906f);
            Paint(panel, DraftSprite("renew_popup_bg"), Color.white, true, true);
            Text(panel, "Preban title", "arena.draft.preban", 34, .032f, .024f, .49f, .082f).fontStyle = FontStyles.Bold;
            NativeRule(panel, "Preban line", .025f, .114f, .763f, .014f, Mist);
            Add("preSearch", DraftSearch(panel, "Find preban hero", .657f, .031f, .13f, .069f));
            var grid = Scroll(panel, "Preban grid", .027f, .127f, .759f, .672f);
            var sourceLayout = grid.content.GetComponent<VerticalLayoutGroup>();
            Object.DestroyImmediate(sourceLayout);
            var cells = grid.content.gameObject.AddComponent<GridLayoutGroup>();
            cells.cellSize = new Vector2(134, 145);
            cells.spacing = new Vector2(3, 4);
            cells.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cells.constraintCount = 10;
            cells.childAlignment = TextAnchor.UpperLeft;
            Add("grid", grid);
            VerticalRule(panel, "Recent border", .801f, .122f, .006f, .664f, Mist);
            Text(panel, "Recent picks caption", "arena.draft.recent", 24, .817f, .025f, .155f, .08f,
                Mist, TextAlignmentOptions.Center);
            var recent = Area(panel, "Recent choices", .821f, .141f, .153f, .56f);
            var recentLayout = recent.gameObject.AddComponent<GridLayoutGroup>();
            recentLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            recentLayout.constraintCount = 2;
            recentLayout.cellSize = new Vector2(114, 120);
            recentLayout.spacing = new Vector2(6, 4);
            recentLayout.childAlignment = TextAnchor.UpperCenter;
            Add("recent", recent.gameObject);
            Paint(Area(panel, "Selected band", .014f, .805f, .972f, .176f), null, new Color32(21, 39, 66, 195));
            Add("preCount", Text(panel, "Selected hero count", null, 28, .036f, .828f, .21f, .123f, Mist, wrap: true));
            var selected = Area(panel, "Selected preban chips", .268f, .826f, .367f, .136f);
            var selectedLayout = selected.gameObject.AddComponent<GridLayoutGroup>();
            // pvplive_ready_pre_ben: 92 x 97 native pixels, 90 px stride from the right.
            selectedLayout.cellSize = new Vector2(138f, 145.5f);
            selectedLayout.spacing = new Vector2(-3f, 0);
            selectedLayout.startCorner = GridLayoutGroup.Corner.UpperRight;
            selectedLayout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            selectedLayout.constraintCount = 1;
            selectedLayout.childAlignment = TextAnchor.MiddleRight;
            Add("preSelected", selected.gameObject);
            Text(panel, "Remaining time caption", "arena.draft.remaining", 22, .685f, .826f, .126f, .043f,
                Mist, TextAlignmentOptions.Center);
            Add("preTimer", DraftTimer(panel, "Preban timer", .685f, .862f, .126f, .101f, refs, "preTimer"));
            var preAction = Button(Area(panel, "Confirm preban", .814f, .847f, .168f, .104f), DraftSprite("_btn_red"), Color.white);
            PressSprites(preAction, DraftSprite("_btn_red"), DraftSprite("_btn_red_p"));
            preAction.BlockedFeedback = true;
            Add("preAction", preAction);
            Add("preActionLabel", Text((RectTransform)preAction.transform, "Preban action", "arena.draft.choose", 31,
                0, -.054f, 1, 1, alignment: TextAlignmentOptions.Center));

            var reveal = Area(root, "Preban reveal", 0, 0, 1, 1);
            Paint(reveal, DraftSprite("base_pvplive_eff"), Color.white, true);
            var vignette = Picture(reveal, "Reveal shadow", DraftSprite("vignetting2"), 0, 0, 1, 1);
            vignette.color = Color.black;
            Picture(reveal, "Central slash", DraftSprite("cm_diagonal_red"), 0, .168f, 1, .665f);
            Text(reveal, "Preban result caption", "arena.draft.prebanned", 45, .10f, .285f, .8f, .086f,
                new Color32(255, 218, 211, 255), TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            Add("reveal", reveal.gameObject);
            Add("revealHeroes", DraftHorizontal(reveal, "Preban result heroes", .147f, .395f, .706f, .202f, 145).gameObject);
            reveal.gameObject.SetActive(false);
            Add("status", Text(root, "Draft status", null, 25, .208f, .939f, .49f, .050f,
                new Color32(255, 172, 118, 255), TextAlignmentOptions.Center));
            Bind(outer, refs.ToArray());
            return outer.gameObject;
        }

        private static BindingMap DraftCard(RectTransform root, Sprite background, Sprite filled, bool reverse, bool horizontal, bool miniature, Sprite outline = null, bool prebanCard = false)
        {
            bool rosterRow = filled == null;
            var button = Button(root, null, Color.clear);
            button.BlockedFeedback = true;
            var target = prebanCard
                ? Picture(root, "Card background", background, .082f, .095f, .84f, .797f, true)
                : Picture(root, "Card background", background, 0, 0, 1, 1);
            button.targetGraphic = target;
            target.color = Color.white;
            var colors = button.colors;
            colors.disabledColor = horizontal && rosterRow ? new Color32(158, 166, 183, 255) : Color.white;
            button.colors = colors;
            Image book = Picture(root, "Filled state data", filled, 0, 0, 1, 1);
            book.enabled = false;
            var selection = Picture(root, "Selection", outline ? outline :
                horizontal && rosterRow ? DraftSprite("hero_bg_selected") : Art("_select"), 0, 0, 1, 1);
            if (outline)
            {
                float scaleWidth = outline.rect.width / background.rect.width;
                Place(selection.rectTransform, reverse ? 1f - scaleWidth - .035f : .035f, -.035f, scaleWidth, 1.07f);
                selection.color = Color.white;
                selection.transform.SetAsLastSibling();
            }
            if (!outline && selection.sprite && selection.sprite.border != Vector4.zero) selection.type = Image.Type.Sliced;
            if (filled && !reverse)
                foreach (var art in new[] { target, book }) art.rectTransform.localScale = new Vector3(-1, 1, 1);
            RectTransform portraitParent = root;
            if (horizontal && rosterRow)
            {
                var mask = Picture(root, "Native hero clip", DraftSprite("hero_bg_mask"), 0, 0, 1, 1);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                portraitParent = mask.rectTransform;
            }
            var portrait = Picture(portraitParent, "Hero", null, horizontal ? reverse || rosterRow ? .258f : .013f : .084f,
                horizontal ? .073f : .06f, horizontal ? rosterRow ? .726f : .695f : .832f,
                horizontal ? .872f : .822f, !horizontal || rosterRow);
            if (!horizontal && !miniature)
                Picture(root, "Gold face frame", HudSprite("_hero_s_frame_ally"), .025f, .008f, .95f, .923f, true);
            if (prebanCard) Place(portrait.rectTransform, .082f, .095f, .84f, .797f);
            var guard = Picture(root, "Ban protection", DraftSprite("_cm_safeguard"), reverse ? .77f : .035f,
                .08f, .18f, .46f, true);
            var mark = Picture(root, "Banned stamp", DraftSprite("_cm_banned_res"), .32f, .025f, .66f, .94f, true);
            bool rightText = horizontal && !reverse && !rosterRow;
            var element = Picture(root, "Element", null, horizontal ? rightText ? .71f : .19f : .73f,
                .047f, horizontal ? .12f : .22f, .28f, true);
            var role = Picture(root, "Class", null, horizontal ? rightText ? .82f : .07f : .035f,
                .052f, horizontal ? .13f : .24f, .28f, true);
            var name = Text(root, "Hero name or level", null, 25, horizontal ? rightText ? .445f : .065f : .06f,
                horizontal && rosterRow ? .284f : miniature ? .08f : .337f, horizontal ? .51f : .88f, .33f,
                filled ? new Color32(255, 157, 58, 255) : Color.white,
                horizontal ? rightText ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            var stars = Text(root, "Hero grade", null, miniature ? 17 : horizontal ? 19 : 15, rightText ? .438f : .033f,
                horizontal ? .681f : .748f, horizontal ? .54f : .93f, .242f,
                new Color32(254, 194, 64, 255), horizontal ? rightText ? TextAlignmentOptions.MidlineRight :
                TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            stars.overflowMode = TextOverflowModes.Overflow;
            var state = Text(root, "Hero status", null, 23,
                horizontal && rosterRow ? .117f : .035f, horizontal && rosterRow ? .33f : .10f, .79f, .30f, Green,
                horizontal && rosterRow ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            var dim = Picture(root, "State shade", horizontal && rosterRow ? DraftSprite("hero_bg_mask") : null, 0, 0, 1, 1);
            if (horizontal && !rosterRow)
            {
                dim.sprite = background;
                dim.rectTransform.localScale = target.rectTransform.localScale;
            }
            dim.gameObject.SetActive(false);
            dim.transform.SetSiblingIndex(state.transform.GetSiblingIndex());
            if (outline) selection.transform.SetAsLastSibling();
            mark.transform.SetAsLastSibling();
            guard.transform.SetAsLastSibling();
            var keys = new List<(string, Object)> { ("button", button), ("filled", book), ("selection", selection), ("portrait", portrait),
                ("guard", guard), ("mark", mark), ("element", element), ("role", role), ("name", name),
                ("stars", stars), ("state", state), ("dim", dim) };
            if (prebanCard)
            {
                // pvplive_ready_pre_ben_item.csb uses gold cm_icon_star art with a 20/30 stride.
                for (int index = 0; index < 6; index++)
                {
                    var star = Picture(root, "Original rarity star " + (index + 1),
                        DraftSprite("TeamBadges/cm_icon_star"), 0, .796f, .177f, .164f, true);
                    star.enabled = false;
                    keys.Add(("star" + index, star));
                }
                var flash = Picture(root, "Preban red flash", DraftSprite("preban_flash"), -.15f, -.13f, 1.3f, 1.3f, true);
                var glow = Picture(root, "Preban strike glow", DraftSprite("preban_icon_glow"), -.04f, -.02f, 1.08f, 1.08f, true);
                mark.sprite = DraftSprite("preban_banned_icon");
                // The sprite is padded. Its visible red ring extends just beyond the gold frame.
                Place(mark.rectTransform, -.032f, -.111f, 1.064f, 1.161f);
                mark.transform.SetAsLastSibling();
                flash.enabled = glow.enabled = false;
                keys.Add(("prebanFlash", flash));
                keys.Add(("prebanGlow", glow));
            }
            if (miniature)
            {
                var model = Raw(root, "Battle model", 0, 0, 1, 1);
                model.transform.SetSiblingIndex(4);
                keys.Add(("model", model));
                var change = Picture(root, "Change position", DraftSprite("hero_config_btn_change"), .32f, .15f, .3583f, .3583f, true);
                change.enabled = false;
                keys.Add(("change", change));
                foreach (var label in new[] { name, stars, state }) label.color = Color.clear;
                role.color = element.color = Color.clear;

                ((Image)button.targetGraphic).sprite = null;
                ((Image)button.targetGraphic).color = Color.clear;
                selection.sprite = DraftSprite("hero_config_bg");
                Place(selection.rectTransform, -.07f, .588f, 1.14f, .46f);
                selection.preserveAspect = false;
                selection.color = new Color(.58f, .78f, 1f, .94f);
                var ring = Picture(root, "Floor ring", background, -.07f, .588f, 1.14f, .46f, false);
                ring.color = new Color32(51, 137, 225, 255);
                ring.transform.SetAsFirstSibling();
            }
            selection.enabled = portrait.enabled = guard.enabled = mark.enabled = role.enabled = element.enabled = false;
            Bind(root, keys.ToArray());
            return root.GetComponent<BindingMap>();
        }

        private static void DraftTemplate(RectTransform parent, string name, int size, Sprite background, bool horizontal, bool circle,
            out RectTransform template, bool prebanCard = false)
        {
            template = Area(parent, name, 0, 0, 1, 1);
            DraftCard(template, background, null, false, horizontal, false, prebanCard: prebanCard);
            var layout = template.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = size;
            if (circle) layout.preferredWidth = size;
            template.gameObject.SetActive(false);
        }

        private static RectTransform DraftHorizontal(RectTransform parent, string name, float x, float y, float w, float h, int size)
        {
            var list = Area(parent, name, x, y, w, h);
            var layout = list.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(size, size);
            layout.spacing = new Vector2(5, 3);
            layout.childAlignment = TextAnchor.MiddleCenter;
            return list;
        }

        private static RawImage DraftGuide(RectTransform stage, string name, float x)
        {
            // pvplive_ready.csb places both effect origins at y=326 in bottom-left coordinates.
            var light = Raw(stage, name, (x - 210f) / 998f, (720f - 326f - 320f) / 720f, 420f / 998f, 640f / 720f);
            light.color = new Color32(255, 255, 255, 127);
            return light;
        }

        private static Image DraftArc(RectTransform stage, bool reverse, string name)
        {
            float x = reverse ? 709 : 287;
            float fillX = x + (reverse ? -4 : 4);
            var rail = Picture(stage, name + " unlit rail", DraftSprite("pvplive_progress_bg"), (x - 63.5f) / 998, (720 - 325 - 265.5f) / 720, 127f / 998, 531f / 720);
            if (reverse) rail.rectTransform.localScale = new Vector3(-1, 1, 1);
            var glow = Picture(stage, name, DraftSprite(reverse ? "pvplive_progress_bar_red" : "pvplive_progress_bar_blue"),
                (fillX - 259.5f) / 998, (720 - 325 - 58.5f) / 720, 519f / 998, 117f / 720);
            // Cocos clockwise angles have opposite sign in Unity. Red and blue source textures
            // already bend in opposite directions. Offset centers for their 4 px alpha registration.
            glow.rectTransform.localEulerAngles = new Vector3(0, 0, 90);
            glow.type = Image.Type.Filled;
            glow.fillMethod = Image.FillMethod.Horizontal;
            glow.fillOrigin = (int)Image.OriginHorizontal.Left;
            return glow;
        }

        private static TMP_InputField DraftSearch(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var field = Input(parent, name, "arena.search", 28, x, y, w, h);
            var rt = (RectTransform)field.transform;
            var image = (Image)field.targetGraphic;
            image.sprite = Art("_box_r_op80");
            if (image.sprite && image.sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            image.color = new Color32(118, 119, 125, 225);
            field.textComponent.color = Color.white;
            field.placeholder.color = Mist;
            Place(field.textViewport, .23f, .03f, .739f, .94f);
            Picture(rt, "Search icon", Art("cm_icon_etczoomer"), .071f, .184f, .139f, .632f, true).color = Mist;
            return field;
        }

        private static RawImage Raw(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var raw = Area(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            return raw;
        }

        private static GameText DraftTimer(RectTransform parent, string name, float x, float y, float w, float h,
            List<(string, Object)> refs, string key)
        {
            var timer = Area(parent, name, x, y, w, h);
            var left = Picture(timer, "Tens", null, .117f, 0, .49f, 1, true);
            var right = Picture(timer, "Units", null, .390f, 0, .49f, 1, true);
            var single = Picture(timer, "Single", null, .244f, 0, .488f, 1, true);
            left.enabled = right.enabled = single.enabled = false;
            refs.Add((key + "Tens", left));
            refs.Add((key + "Units", right));
            refs.Add((key + "Single", single));
            var label = Text(timer, "Clock text", null, 83, 0, 0, 1, 1, Color.clear, TextAlignmentOptions.Center);
            return label;
        }

        [Serializable] private sealed class DraftCatalog { public DraftHero[] heroes; public DraftMessage[] messages; }
        [Serializable] private sealed class DraftHero { public string id, nameKey, element, heroClass; public int level, stars; }
        [Serializable] private sealed class DraftMessage { public string key, zhCn; }

        private static Dictionary<string,string> ReadRosterBaseModels()
        {
            const string path="Assets/Game/Modules/CharacterLab/Configs/catalog.json";
            var roster=JsonUtility.FromJson<AstralOath.Client.CharacterLab.CharacterLabCatalog>(File.ReadAllText(path));
            return roster.characters.ToDictionary(hero=>hero.id,
                hero=>Path.GetFileNameWithoutExtension(hero.battleSkeletonAddress),StringComparer.Ordinal);
        }

        private static void BuildDraftArt()
        {
            const string battleCatalog = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";
            const string chars = "Assets/Game/Modules/CharacterPresentation/Res/Characters/";
            var source = JsonUtility.FromJson<DraftCatalog>(File.ReadAllText(battleCatalog));
            var baseModels=ReadRosterBaseModels();
            var names = source.messages.ToDictionary(row => row.key, row => row.zhCn);
            var catalog = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>(DraftArtPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<ArenaDraftArt>();
                AssetDatabase.CreateAsset(catalog, DraftArtPath);
            }
            catalog.LevelDigits = Enumerable.Range(0, 10).Select(digit => DraftSprite("TeamBadges/hero_lv_" + digit)).ToArray();
            catalog.ActionGreen = CommonArt("_btn_green");
            catalog.ActionGreenPressed = CommonArt("_btn_green_p");
            catalog.ActionRed = DraftSprite("_btn_red");
            catalog.ActionRedPressed = DraftSprite("_btn_red_p");
            const string feedback = Arena + "Res/Animations/DraftFeedback/";
            catalog.BluePick = Enumerable.Range(1, 5).Select(index => Effect(
                "ui_livepvp_slot_eff_0" + index, "ui_livepvp_slot_blue" + index + "_pati1")).ToArray();
            catalog.RedPick = Enumerable.Range(1, 5).Select(index => Effect(
                "ui_livepvp_slot_eff_0" + index, "ui_livepvp_slot_red" + index + "_pati1")).ToArray();
            catalog.FinalBan = Effect("ui_livepvp_bann_eff", "ui_livepvp_bann_pati1");
            catalog.BlueGuide = Effect("ui_livepvp_arrow_blue");
            catalog.RedGuide = Effect("ui_livepvp_arrow_red");
            catalog.ParticleTextures = new[] { Load<Texture2D>(feedback + "Particles/pati_03.png"),
                Load<Texture2D>(feedback + "Particles/ticle_02.png") };
            ArenaDraftArt.NativeEffect Effect(string rig, string particle = null) => new ArenaDraftArt.NativeEffect
            {
                Skeleton = Load<TextAsset>(feedback + rig + ".spine.json"),
                Atlas = Load<TextAsset>(feedback + rig + ".atlas.txt"),
                Page = Load<Texture2D>(feedback + rig + ".png"),
                Particles = particle == null ? null : Load<TextAsset>(feedback + "Particles/" + particle + ".particle.json")
            };
            catalog.ScoreDigits = Enumerable.Range(0, 10).Select(digit => DraftSprite("Digits/score_" + digit)).ToArray();
            catalog.Heroes = source.heroes.OrderBy(hero => hero.id, StringComparer.Ordinal).Select(hero =>
            {
                string directory = chars + hero.id + "/";
                string model=baseModels[hero.id];
                string role = hero.heroClass == "soul_weaver" ? "manauser" : hero.heroClass == "thief" ? "assassin" : hero.heroClass;
                return new ArenaDraftArt.Hero
                {
                    Id = hero.id, Name = names[hero.nameKey], Element = hero.element, Class = hero.heroClass,
                    Level = hero.level, Stars = hero.stars,
                    Face = HudSprite("Faces/" + hero.id + "_s"),
                    Card = DraftSprite("Heroes/" + hero.id + "_l"),
                    Illustration = AssetDatabase.LoadAssetAtPath<Sprite>(Arena + "Res/Textures/Draft/Heroes/" + hero.id + "_fu.png"),
                    ElementIcon = HudSprite("cm_icon_prom" + hero.element),
                    ClassIcon = HudSprite("cm_icon_role_" + role),
                    PortraitSkeleton = Load<TextAsset>(directory + "Animations/portrait/" + hero.id + ".spine.json"),
                    PortraitAtlas = Load<TextAsset>(directory + "Atlas/portrait/" + hero.id + ".atlas.txt"),
                    PortraitPage = Load<Texture2D>(directory + "Textures/portrait/" + hero.id + ".png"),
                    ModelSkeleton = Load<TextAsset>(directory + "Animations/model/" + model + ".spine.json"),
                    ModelAtlas = Load<TextAsset>(directory + "Atlas/model/" + model + ".atlas.txt"),
                    ModelPage = Load<Texture2D>(directory + "Textures/model/" + model + ".png")
                };
            }).ToArray();
            PopulateDraftFilterData(catalog);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
