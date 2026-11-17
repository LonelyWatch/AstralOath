using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string HeroInventoryPrefabPath = Home + "Res/Prefab/HeroInventory.prefab";
        private const string HomeMoreMenuPrefabPath = Home + "Res/Prefab/HomeMoreMenu.prefab";
        private const string HomeHeroArtPath = Home + "Configs/HomeHeroArt.asset";
        private const string InventoryPrefix = "home.hero_inventory.";
        private const string InventoryTextures = Home + "Res/Textures/Inventory/";

        [MenuItem("Tools/游戏主页/生成英雄背包")]
        public static void GenerateInventory()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            labels = JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows
                .ToDictionary(row => row.key, row => row.text);
            BuildInventoryAssets();
            SaveInventoryNavigation();
        }

        private static void BuildInventoryAssets()
        {
            Save(BuildHomeMoreMenu(), HomeMoreMenuPrefabPath);
            Save(BuildHeroInventory(), HeroInventoryPrefabPath);
            BuildHomeHeroArt();
            UIViewRegistry.ValidatePrefab<HomeMoreMenu>(Load<GameObject>(HomeMoreMenuPrefabPath));
            UIViewRegistry.ValidatePrefab<HeroInventory>(Load<GameObject>(HeroInventoryPrefabPath));
        }

        private static Sprite InventoryArt(string stem) => Load<Sprite>(InventoryTextures + stem + ".png");

        private static GameObject BuildHomeMoreMenu()
        {
            var root = Root("HomeMoreMenu");
            var outside = Button(Area(root, "Outside click", 0, 0, 1, 1), null, Black(.24f));
            outside.FeedbackSound = GameUiFeedback.Sound.Back;
            var panel = Area(root, "Quick menu panel", .724f, 0, .276f, 1);
            Paint(panel, null, new Color32(12, 25, 40, 210), true);
            Paint(Area(panel, "Left edge", 0, 0, .006f, 1), null, new Color32(80, 112, 140, 150));

            var close = Button(Area(panel, "Close more", .819f, .005f, .16f, .087f), null, Color.clear);
            close.FeedbackSound = GameUiFeedback.Sound.Back;
            var closeIcon = Picture(close.transform as RectTransform, "Close icon", InventoryArt("quick_esc"), 0, 0, 1, 1, true);
            PressDecorations(close, (closeIcon, InventoryArt("quick_esc_p")));

            var heroes = Button(Area(panel, "Heroes", .669f, .147f, .30f, .130f), null, Color.clear);
            var icon = Picture(heroes.transform as RectTransform, "Hero icon", InventoryArt("renew_icon_quick_hero"), .10f, 0, .80f, .73f, true);
            PressDecorations(heroes, (icon, InventoryArt("renew_icon_quick_hero_p")));
            var text = Text(heroes.transform as RectTransform, "Hero caption", InventoryPrefix + "heroes", 29,
                0, .69f, 1, .30f, alignment: TextAlignmentOptions.Center);
            Bind(root, ("heroes", heroes), ("heroesLabel", text), ("close", close), ("outside", outside));
            return root.gameObject;
        }

        private static GameObject BuildHeroInventory()
        {
            var root = Root("HeroInventory");
            var bag = Area(root, "Bag", 0, 0, 1, 1);
            var roster = Area(root, "Hero roster", 0, 0, 1, 1);
            Paint(bag, null, Deep, true);
            Paint(roster, null, Deep, true);
            Cover(bag, "Vault", InventoryArt("base_inventory"));
            Cover(roster, "Star field", InventoryArt("base_unit_hero"));
            Paint(Area(bag, "Bottom shadows", 0, .092f, 1, .908f), null, Black(.24f));
            Picture(bag, "Toolbar shadow", InventoryArt("_grow_inventory_top"), 0, .083f, 1, .106f);
            var sidebar = Area(bag, "Bag categories", 0, .095f, .115f, .90f);
            Paint(sidebar, null, Black(.38f));
            var heroTab = MakeInventoryTab(sidebar, "Heroes", "heroes", "icon_menu_equip", .01f, out var heroLabel);
            var equipTab = MakeInventoryTab(sidebar, "Equipment", "equipment", "icon_menu_equipment", .134f, out var equipLabel);
            var artifactTab = MakeInventoryTab(sidebar, "Artifacts", "artifacts", "icon_menu_pararti", .258f, out var artifactLabel);
            var bagFilter = Area(bag, "Hero class filter", .126f, .106f, .542f, .058f);
            var filterBindings = BuildInventoryClassFilters(bagFilter);
            var bagScroll = InventoryScroll(bag, "Bag hero rows", .126f, .174f, .843f, .796f);
            GameObject bagRow = InventoryRow(bag, false);

            var detail = Area(roster, "Selected hero detail", .035f, .105f, .700f, .855f);
            var rail = Area(detail, "Hero detail rail", -.049f, 0, .046f, 1);
            Paint(rail, null, new Color32(5, 13, 24, 168));
            var railBg = Picture(rail, "Hero detail active", InventoryArt("_tap_select_blue"), 0, .04f, 1, .095f);
            railBg.color = new Color32(70, 144, 221, 220);
            Picture(rail, "Hero detail icon", InventoryArt("icon_menu_equip"), .13f, .046f, .74f, .079f, true);
            var detailPane = Area(detail, "Hero summary", 0, .014f, .50f, .8f);
            Paint(Area(detailPane, "Summary shade", 0, 0, .97f, .48f), null, Black(.35f));
            var detailIllustration = Paint(Area(detail, "Selected hero illustration", .30f, -.05f, .78f, 1.05f), null, Color.white);
            detailIllustration.preserveAspect = true;
            var detailPortraitRoot = Area(detail, "Native hero portrait", .27f, -.07f, .83f, 1.086f);
            var aspect = detailPortraitRoot.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1;
            var detailPortrait = detailPortraitRoot.gameObject.AddComponent<RawImage>();
            detailPortrait.raycastTarget = false;
            detailPortrait.enabled = false;
            var heroName = Text(detailPane, "Hero name", null, 43, .035f, .047f, .93f, .15f);
            var heroStars = Text(detailPane, "Hero stars", null, 29, .036f, .195f, .93f, .105f, new Color32(255, 190, 75, 255));
            var heroLevel = Text(detailPane, "Hero level", null, 33, .035f, .306f, .93f, .11f, new Color32(255, 154, 68, 255));
            var detailElement = Paint(Area(detailPane, "Element", .039f, .432f, .094f, .071f), null, Color.white);
            var detailRole = Paint(Area(detailPane, "Class", .146f, .432f, .094f, .071f), null, Color.white);
            detailElement.preserveAspect = true;
            detailRole.preserveAspect = true;
            detailPane.SetAsLastSibling();
            var rosterSidebar = Area(roster, "Roster sidebar", .738f, .094f, .247f, .861f);
            Paint(rosterSidebar, null, new Color32(3, 11, 24, 169));
            var search = InventorySearch(rosterSidebar, out var searchHint);
            var expand = Action(rosterSidebar, "Expand roster", InventoryPrefix + "expand", 21, .56f, .011f, .41f, .066f,
                out var expandLabel, Art("_box_r_op80"));
            var rosterScroll = InventoryScroll(rosterSidebar, "Roster hero rows", .017f, .094f, .968f, .883f);
            GameObject rosterRow = InventoryRow(roster, true);

            var top = Area(root, "Title bar", 0, 0, 1, .091f);
            Paint(top, null, new Color32(8, 14, 28, 228), true);
            var back = Button(Area(top, "Back", .014f, .10f, .037f, .79f), null, Color.clear);
            back.FeedbackSound = GameUiFeedback.Sound.Back;
            Picture(back.transform as RectTransform, "Back icon", Art("cm_icon_etcback"), .08f, 0, .84f, 1, true);
            var title = Text(top, "Title", InventoryPrefix + "bag", 42, .061f, .03f, .54f, .92f);
            var capacity = Text(top, "Hero capacity", null, 27, .75f, .04f, .224f, .91f,
                new Color32(196, 212, 226, 255), TextAlignmentOptions.MidlineRight);
            var status = Text(root, "Inventory status", InventoryPrefix + "loading", 31, .30f, .398f, .41f, .074f,
                Mist, TextAlignmentOptions.Center);
            var retry = Action(root, "Retry", InventoryPrefix + "retry", 26, .432f, .486f, .145f, .060f,
                out var retryLabel, CommonArt("_btn_green"));
            retry.gameObject.SetActive(false);
            var bindings = new List<(string Key, Object Target)>
            {
                ("bag", bag.gameObject), ("roster", roster.gameObject), ("back", back), ("title", title),
                ("capacity", capacity), ("tabHeroes", heroTab), ("tabEquipment", equipTab), ("tabArtifacts", artifactTab),
                ("tabHeroesLabel", heroLabel), ("tabEquipmentLabel", equipLabel), ("tabArtifactsLabel", artifactLabel),
                ("bagScroll", bagScroll), ("rosterScroll", rosterScroll), ("bagRow", bagRow), ("rosterRow", rosterRow),
                ("rosterDetail", detail), ("rosterSidebar", rosterSidebar), ("search", search), ("searchHint", searchHint),
                ("expand", expand), ("expandLabel", expandLabel), ("status", status), ("retry", retry), ("retryLabel", retryLabel),
                ("detailName", heroName), ("detailLevel", heroLevel), ("detailStars", heroStars),
                ("detailIllustration", detailIllustration), ("detailPortrait", detailPortrait),
                ("detailElement", detailElement), ("detailRole", detailRole), ("bagFilter", bagFilter.gameObject),
                ("tabActive_heroes", TabSelectionImage(heroTab)), ("tabActive_equipment", TabSelectionImage(equipTab)),
                ("tabActive_artifacts", TabSelectionImage(artifactTab))
            };
            bindings.AddRange(filterBindings);
            Bind(root, bindings.ToArray());
            roster.gameObject.SetActive(false);
            return root.gameObject;
        }

        private static GameButton MakeInventoryTab(RectTransform sidebar, string name, string key, string sprite,
            float y, out GameText text)
        {
            var button = Button(Area(sidebar, name, .025f, y, .96f, .12f), null, Color.clear);
            var selected = Picture(button.transform as RectTransform, "Selected", InventoryArt("_tap_select_blue"), 0, 0, 1, 1);
            selected.color = new Color32(125, 189, 240, 240);
            selected.enabled = key == "heroes";
            Picture(button.transform as RectTransform, "Category icon", InventoryArt(sprite), .24f, .015f, .52f, .69f, true);
            text = Text(button.transform as RectTransform, "Caption", InventoryPrefix + key, 26, .03f, .71f, .94f, .28f,
                alignment: TextAlignmentOptions.Center);
            return button;
        }

        private static Image TabSelectionImage(GameButton button) =>
            button.transform.Find("Selected").GetComponent<Image>();

        private static List<(string Key, Object Target)> BuildInventoryClassFilters(RectTransform bar)
        {
            string[] keys = { "all", "knight", "warrior", "mage", "ranger", "thief", "soul_weaver" };
            string[] spriteIds = { null, "knight", "warrior", "mage", "ranger", "assassin", "manauser" };
            var bindings = new List<(string Key, Object Target)>();
            for (int index = 0; index < keys.Length; ++index)
            {
                var rect = Area(bar, "Class " + keys[index], .132f * index, 0, .123f, 1);
                var button = Button(rect, Art("_box_r_op80"), new Color32(25, 43, 62, 210));
                var active = Picture(rect, "Selected class", InventoryArt("_tap_select_blue"), 0, 0, 1, 1);
                active.enabled = index == 0;
                if (index == 0)
                    Picture(rect, "All classes", Art("icon_menu_all"), .19f, .13f, .62f, .74f, true);
                else
                    Picture(rect, "Class icon", HudSprite("cm_icon_role_" + spriteIds[index]), .19f, .13f, .62f, .74f, true);
                bindings.Add(("filter_" + keys[index], button));
                bindings.Add(("filterActive_" + keys[index], active));
            }
            return bindings;
        }

        private static TMP_InputField InventorySearch(RectTransform parent, out GameText hint)
        {
            var rect = Area(parent, "Search heroes", .025f, .011f, .51f, .066f);
            var plate = Paint(rect, Art("_box_r_op80"), new Color32(91, 118, 140, 194), true, true);
            Picture(rect, "Search icon", Art("cm_icon_etczoomer"), .032f, .12f, .20f, .76f, true);
            var field = rect.gameObject.AddComponent<TMP_InputField>();
            var viewport = Area(rect, "Text viewport", .24f, .02f, .72f, .96f);
            viewport.gameObject.AddComponent<RectMask2D>();
            field.targetGraphic = plate;
            field.textViewport = viewport;
            field.textComponent = Text(viewport, "Search text", null, 23, 0, 0, 1, 1);
            hint = Text(viewport, "Placeholder", InventoryPrefix + "search", 21, 0, 0, 1, 1, Mist);
            field.placeholder = hint;
            field.characterLimit = 24;
            field.richText = false;
            return field;
        }

        private static ScrollRect InventoryScroll(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var frame = Area(parent, name, x, y, w, h);
            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 68;
            var viewport = Area(frame, "Viewport", 0, 0, 1, 1);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = Paint(viewport, null, Color.clear, true);
            hit.canvasRenderer.cullTransparentMesh = false;
            var content = Area(viewport, "Content", 0, 0, 1, 0);
            content.pivot = new Vector2(.5f, 1f);
            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        private static GameObject InventoryRow(RectTransform parent, bool roster)
        {
            var row = Area(parent, roster ? "Roster row template" : "Bag row template", 0, 0, 1, 1);
            if (roster)
                Button(row, null, new Color32(5, 12, 24, 207));
            else
                Paint(row, null, new Color32(5, 12, 24, 196));
            var selection = Paint(Area(row, "Selected", 0, 0, 1, 1), Art("_box_r_op80"),
                new Color32(76, 158, 230, 187), false, true);
            selection.enabled = false;
            var icon = Paint(Area(row, "Portrait", .012f, .03f, roster ? .26f : .078f, .84f), null, Color.white);
            icon.preserveAspect = true;
            var name = Text(row, "Name", null, roster ? 26 : 29, roster ? .31f : .093f, .100f, roster ? .63f : .142f, .27f);
            var level = Text(row, "Level", null, 25, roster ? .465f : .118f, .408f, roster ? .45f : .115f, .22f,
                new Color32(255, 157, 59, 255));
            var element = Paint(Area(row, "Element", roster ? .286f : .093f, .395f, roster ? .075f : .024f, .24f), null, Color.white);
            var role = Paint(Area(row, "Role", roster ? .371f : .190f, .395f, roster ? .075f : .026f, .24f), null, Color.white);
            element.preserveAspect = true;
            role.preserveAspect = true;
            var bindings = new List<(string Key, Object Target)>
            {
                ("name", name), ("level", level), ("portrait", icon), ("selection", selection), ("element", element), ("role", role)
            };
            for (int index = 0; index < 6; ++index)
            {
                float starWidth = roster ? .060f : .013f;
                var star = Paint(Area(row, "Star " + index, (roster ? .313f : .018f) + starWidth * index,
                    roster ? .70f : .755f, starWidth, .21f), null, Color.white);
                star.preserveAspect = true;
                bindings.Add(("star" + index, star));
            }
            if (!roster)
                for (int index = 0; index < 7; ++index)
                    Picture(row, index == 3 ? "Empty artifact slot" : "Empty equipment slot " + index,
                        CommonArt("_box_equip_0"), .250f + .093f * index, .111f, .085f, .76f, true)
                        .color = new Color32(172, 191, 210, 96);
            Bind(row, bindings.ToArray());
            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static void BuildHomeHeroArt()
        {
            const string battleCatalog = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";
            var source = JsonUtility.FromJson<DraftCatalog>(File.ReadAllText(battleCatalog));
            var names = source.messages.ToDictionary(row => row.key, row => row.zhCn);
            var catalog = AssetDatabase.LoadAssetAtPath<HomeHeroArt>(HomeHeroArtPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<HomeHeroArt>();
                AssetDatabase.CreateAsset(catalog, HomeHeroArtPath);
            }
            catalog.star = DraftSprite("TeamBadges/cm_icon_star");
            catalog.heroes = source.heroes.OrderBy(hero => hero.id, StringComparer.Ordinal).Select(hero =>
            {
                string role = hero.heroClass == "soul_weaver" ? "manauser" : hero.heroClass == "thief" ? "assassin" : hero.heroClass;
                string portrait = "Assets/Game/Modules/CharacterPresentation/Res/Characters/" + hero.id + "/";
                return new HomeHeroArt.Hero
                {
                    id = hero.id,
                    name = names[hero.nameKey],
                    heroClass = hero.heroClass,
                    skeleton = Load<TextAsset>(portrait + "Animations/portrait/" + hero.id + ".spine.json"),
                    atlas = Load<TextAsset>(portrait + "Atlas/portrait/" + hero.id + ".atlas.txt"),
                    page = Load<Texture2D>(portrait + "Textures/portrait/" + hero.id + ".png"),
                    portrait = HudSprite("Faces/" + hero.id + "_s"),
                    illustration = AssetDatabase.LoadAssetAtPath<Sprite>(Arena + "Res/Textures/Draft/Heroes/" + hero.id + "_fu.png")
                        ?? AssetDatabase.LoadAssetAtPath<Sprite>(Arena + "Res/Textures/Draft/Heroes/" + hero.id + "_l.png"),
                    element = HudSprite("cm_icon_prom" + hero.element),
                    role = HudSprite("cm_icon_role_" + role)
                };
            }).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
