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
            var root = Root("ArenaDraft");
            var refs = new List<(string, Object)>();
            void Add(string key, Object node) => refs.Add((key, node));
            Paint(root, null, Color.black, true);
            Cover(root, "Arena interior", DraftSprite("pvp_rta_ss6_d"));
            var atmosphere = Picture(root, "Red and blue atmosphere", DraftSprite("base_pvplive_eff"), 0, 0, .79f, 1);
            atmosphere.color = new Color(1f, 1f, 1f, .53f);
            Paint(Area(root, "Background shade", 0, 0, 1, 1), null, Black(.36f));

            var stage = Area(root, "Draft stage", 0, 0, .79f, 1);
            var center = Area(stage, "Featured hero", .202f, .133f, .596f, .796f);
            center.gameObject.AddComponent<RectMask2D>();
            var fallback = Picture(center, "Static illustration", null, 0, 0, 1, 1, true);
            fallback.enabled = false;
            Add("featuredFallback", fallback);
            Add("featured", Raw(center, "Spine illustration", -.10f, -.08f, 1.20f, 1.44f));
            var footTone = Picture(stage, "Lower portrait fade", Art("_grow_s"), .194f, .631f, .612f, .292f);
            footTone.color = Black(.63f);
            footTone.rectTransform.localScale = new Vector3(1, -1, 1);
            var formation = Area(stage, "Formation slots", .270f, .244f, .460f, .573f);
            Add("formation", formation.gameObject);
            // Match BattleScene slot indices: front, lower, upper, rear.
            var pads = new[] { (.625f, .273f), (.353f, .527f), (.353f, .012f), (.06f, .267f) };
            for (int index = 0; index < pads.Length; index++)
            {
                var (x, y) = pads[index];
                var pad = Area(formation, "Position " + (index + 1), x, y, .29f, .428f);
                var floor = index == 0 ? "hero_config_panelleader" : index == 3 ? "hero_config_panelback" : "hero_config_bg";
                var map = DraftCard(pad, DraftSprite(floor), null, false, false, true);
                Add("formation" + index, map);
            }

            Picture(stage, "Native left rail", Art("_bg_black_gold_c"), -.112f, .060f, .576f, 1.042f);
            var rightRail = Picture(stage, "Native right rail", Art("_bg_black_gold_c"), .536f, .060f, .576f, 1.042f);
            rightRail.rectTransform.localScale = new Vector3(-1, 1, 1);
            Add("blue", DraftArc(stage, false, "Blue active arc"));
            Add("red", DraftArc(stage, true, "Red active arc"));
            for (int row = 0; row < 5; row++)
            {
                int index = row + 1;
                float width = new[] { .292f, .229f, .198f, .198f, .229f }[row];
                float y = .160f + row * .142f;
                var left = Area(stage, "Ally pick " + index, .018f, y, width, .120f);
                var right = Area(stage, "Enemy pick " + index, .982f - width, y, width, .120f);
                var empty = DraftSprite("pvplive_slot" + index);
                string slot = "pvplive_slot" + index;
                Add("blue" + row, DraftCard(left, empty, DraftSprite(slot + "_blue"), false, true, false, DraftSprite(slot + "_glow_blue")));
                Add("red" + row, DraftCard(right, empty, DraftSprite(slot + "_red"), true, true, false, DraftSprite(slot + "_glow_red")));
            }

            var hostAvatar = ArenaAvatar(stage, "Local profile", .059f, .022f, .061f, .102f);
            var guestAvatar = ArenaAvatar(stage, "Remote profile", .916f, .022f, .061f, .102f);
            Add("hostAvatar", hostAvatar);
            Add("guestAvatar", guestAvatar);
            Add("host", Text(stage, "My player", null, 29, .129f, .025f, .31f, .052f));
            Add("guest", Text(stage, "Opponent player", null, 29, .563f, .025f, .335f, .052f,
                alignment: TextAlignmentOptions.MidlineRight));
            NativeRule(stage, "Blue header", .024f, .083f, .388f, .014f, Gold);
            NativeRule(stage, "Red header", .588f, .083f, .388f, .014f, Gold);
            Add("title", Text(stage, "Draft phase", null, 27, .345f, .021f, .31f, .040f,
                Mist, TextAlignmentOptions.Center));
            Add("timer", DraftTimer(stage, "Draft countdown", .431f, .059f, .138f, .100f, refs, "timer"));
            var exit = Button(Area(stage, "Exit draft", .014f, .019f, .04f, .063f), null, Color.clear);
            Picture((RectTransform)exit.transform, "Exit", Art("cm_icon_etcback"), .22f, .16f, .56f, .68f, true);
            exit.FeedbackSound = GameUiFeedback.Sound.Back;
            Add("exit", exit);
            var hint = Text(stage, "Draft instruction", null, 29, .217f, .733f, .567f, .055f,
                alignment: TextAlignmentOptions.Center);
            hint.fontStyle = FontStyles.Bold;
            Add("hint", hint);
            var mark = Picture(stage, "Final ban symbol", DraftSprite("cm_icon_st_banned"), .456f, .685f, .088f, .09f, true);
            mark.enabled = false;
            Add("banMark", mark);
            var preHeader = Area(stage, "Revealed preban caption", .259f, .854f, .482f, .040f);
            Picture(preHeader, "Preban symbol", DraftSprite("cm_icon_st_banned"), .233f, .05f, .092f, .9f, true);
            Text(preHeader, "Caption", "arena.draft.prebanned", 26, .332f, 0, .66f, 1,
                new Color32(231, 155, 151, 255), TextAlignmentOptions.MidlineLeft);
            Add("preHeader", preHeader.gameObject);
            Add("revealed", DraftHorizontal(stage, "Revealed bans", .304f, .898f, .392f, .097f, 64).gameObject);
            var action = CommonButton(stage, "ConfirmButton", "arena.draft.choose", .716f, .904f, .257f, .082f);
            action.BlockedFeedback = true;
            Add("action", action);
            Add("actionLabel", action.GetComponent<BindingMap>().Require<GameText>("label"));

            var pool = Area(root, "Hero roster sidebar", .79f, 0, .21f, 1);
            Paint(pool, null, new Color32(8, 12, 20, 241));
            VerticalRule(pool, "Border", 0, .016f, .018f, .97f, Gold * .72f);
            Text(pool, "Roster caption", "arena.draft.roster", 23, .065f, .017f, .87f, .047f, Mist);
            Add("search", DraftSearch(pool, "Find hero", .065f, .071f, .885f, .062f));
            var roster = Scroll(pool, "Scroll heroes", .04f, .149f, .96f, .841f);
            var rosterLayout = roster.content.GetComponent<VerticalLayoutGroup>();
            rosterLayout.spacing = 4;
            rosterLayout.padding = new RectOffset(0, 4, 1, 7);
            Add("roster", roster);
            DraftTemplate(root, "Roster hero template", 105, DraftSprite("hero_bg_normal"), true, false, out var rowTemplate);
            Add("row", rowTemplate.gameObject);
            DraftTemplate(root, "Preban hero template", 92, DraftSprite("cm_hero_cirbg2"), false, true, out var gridTemplate);
            Add("gridCell", gridTemplate.gameObject);
            DraftTemplate(root, "Banned portrait template", 62, DraftSprite("cm_hero_cirbg2"), false, true, out var chip);
            Add("chip", chip.gameObject);

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
            Add("preSelected", DraftHorizontal(panel, "Selected preban chips", .268f, .826f, .392f, .136f, 82).gameObject);
            Text(panel, "Remaining time caption", "arena.draft.remaining", 22, .685f, .826f, .126f, .043f,
                Mist, TextAlignmentOptions.Center);
            Add("preTimer", DraftTimer(panel, "Preban timer", .685f, .862f, .126f, .101f, refs, "preTimer"));
            var preAction = Button(Area(panel, "Confirm preban", .814f, .847f, .168f, .104f), DraftSprite("_btn_red"), Color.white);
            PressSprites(preAction, DraftSprite("_btn_red"), DraftSprite("_btn_red_p"));
            preAction.BlockedFeedback = true;
            Add("preAction", preAction);
            Add("preActionLabel", Text((RectTransform)preAction.transform, "Preban action", "arena.draft.choose", 31,
                0, 0, 1, 1, alignment: TextAlignmentOptions.Center));

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
            Bind(root, refs.ToArray());
            return root.gameObject;
        }

        private static BindingMap DraftCard(RectTransform root, Sprite background, Sprite filled, bool reverse, bool horizontal, bool miniature, Sprite outline = null)
        {
            bool rosterRow = filled == null;
            var button = Button(root, null, Color.clear);
            button.BlockedFeedback = true;
            var target = Picture(root, "Card background", background, 0, 0, 1, 1);
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
            if (miniature)
            {
                var model = Raw(root, "Battle model", -.055f, -.31f, 1.11f, 1.36f);
                model.transform.SetSiblingIndex(4);
                keys.Add(("model", model));
                ((Image)button.targetGraphic).sprite = null;
                ((Image)button.targetGraphic).color = Color.clear;
                selection.sprite = DraftSprite("hero_config_bg");
                Place(selection.rectTransform, -.07f, .588f, 1.14f, .46f);
                selection.preserveAspect = true;
                selection.color = new Color(.58f, .78f, 1f, .94f);
                var ring = Picture(root, "Floor ring", background, -.07f, .588f, 1.14f, .46f, true);
                ring.color = new Color32(51, 137, 225, 255);
                ring.transform.SetAsFirstSibling();
            }
            selection.enabled = portrait.enabled = guard.enabled = mark.enabled = role.enabled = element.enabled = false;
            Bind(root, keys.ToArray());
            return root.GetComponent<BindingMap>();
        }

        private static void DraftTemplate(RectTransform parent, string name, int size, Sprite background, bool horizontal, bool circle,
            out RectTransform template)
        {
            template = Area(parent, name, 0, 0, 1, 1);
            DraftCard(template, background, null, false, horizontal, false);
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

        private static Image DraftArc(RectTransform stage, bool reverse, string name)
        {
            var anchor = Area(stage, name + " anchor", reverse ? .657f : .192f, .119f, .151f, .791f);
            var rail = Picture(anchor, "Unlit rail", DraftSprite("pvplive_progress_bg"), 0, 0, 1, 1);
            rail.color = new Color32(135, 105, 68, 225);
            if (reverse) rail.rectTransform.localScale = new Vector3(-1, 1, 1);
            var glow = Picture(anchor, name, DraftSprite(reverse ? "pvplive_progress_bar_red" : "pvplive_progress_bar_blue"),
                -1.04f, .345f, 3.08f, .310f);
            glow.rectTransform.localEulerAngles = new Vector3(0, 0, -90);
            if (!reverse) glow.rectTransform.localScale = new Vector3(1, -1, 1);
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

        private static void BuildDraftArt()
        {
            const string battleCatalog = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";
            const string chars = "Assets/Game/Modules/CharacterPresentation/Res/Characters/";
            var source = JsonUtility.FromJson<DraftCatalog>(File.ReadAllText(battleCatalog));
            var names = source.messages.ToDictionary(row => row.key, row => row.zhCn);
            var catalog = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>(DraftArtPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<ArenaDraftArt>();
                AssetDatabase.CreateAsset(catalog, DraftArtPath);
            }
            catalog.ScoreDigits = Enumerable.Range(0, 10).Select(digit => DraftSprite("Digits/score_" + digit)).ToArray();
            catalog.Heroes = source.heroes.OrderBy(hero => hero.id, StringComparer.Ordinal).Select(hero =>
            {
                string directory = chars + hero.id + "/";
                string model = Path.GetFileName(Directory.GetFiles(directory + "Animations/model", "*.spine.json").Single())
                    .Replace(".spine.json", "");
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
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
