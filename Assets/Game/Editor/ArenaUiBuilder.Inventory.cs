using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using AstralOath.Shared.Battle;
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

        private static GameObject BuildHeroInventory()
        {
            var root = Root("HeroInventory");
            var bag = Area(root, "Bag", 0, 0, 1, 1);
            var bagBindings = BuildNativeHeroBag(bag);
            var capacity = Text(bag, "Hero capacity", null, 24, .776f, .124f, .085f, .06f,
                new Color32(171, 179, 191, 255), TextAlignmentOptions.MidlineRight);
            bagBindings.Add(("capacity", capacity));
            var roster = Area(root, "Hero roster", 0, 0, 1, 1);
            var rosterBindings = BuildHeroRosterLayout(roster);
            DrawNativeTopBarIcons(Area(bag, "Bag top bar icons", 0, 0, 1, 1), dimmed: true);
            Picture(bag, "Bag help glyph", Art("cm_hud_top_btn_help"), .112f, .013f, .043f, .067f, true);
            DrawNativeTopBarIcons(Area(roster, "Roster top bar icons", 0, 0, 1, 1));

            var top = Area(root, "Title bar", 0, 0, 1, .092f);
            Paint(Area(top, "Header rule", 0, .985f, 1, .015f), null, new Color32(255, 255, 255, 34));
            var back = Button(Area(top, "Back", .011f, .035f, .030f, .84f), null, Color.clear);
            back.FeedbackSound = GameUiFeedback.Sound.Back;
            Picture(back.transform as RectTransform, "Back icon", Art("cm_icon_etcback"), .12f, .1f, .76f, .8f, true);
            var title = Text(top, "Title", InventoryPrefix + "bag", 36, .048f, .035f, .28f, .84f);
            title.fontStyle = FontStyles.Bold;
            var status = Text(root, "Inventory status", InventoryPrefix + "loading", 31, .30f, .398f, .41f, .074f,
                Mist, TextAlignmentOptions.Center);
            var retry = Action(root, "Retry", InventoryPrefix + "retry", 26, .432f, .486f, .145f, .060f,
                out var retryLabel, CommonArt("_btn_green"));
            retry.gameObject.SetActive(false);
            var bindings = new List<(string Key, Object Target)>
            {
                ("bag", bag.gameObject), ("roster", roster.gameObject), ("back", back), ("title", title),
                ("status", status), ("retry", retry), ("retryLabel", retryLabel)
            };
            bindings.AddRange(bagBindings);
            bindings.AddRange(rosterBindings);
            Bind(root, bindings.ToArray());
            roster.gameObject.SetActive(false);
            return root.gameObject;
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

        private static void BuildHomeHeroArt()
        {
            const string battleCatalog = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";
            var source = JsonUtility.FromJson<BattleCatalogData>(File.ReadAllText(battleCatalog));
            var growth = source.growthPresets.ToDictionary(preset => preset.id, StringComparer.Ordinal);
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
                    elementId = hero.element,
                    zodiac = hero.zodiac,
                    zodiacIcon = HeroZodiacArt(hero.zodiac),
                    growth = growth[hero.growthPresetId],
                    skeleton = Load<TextAsset>(portrait + "Animations/portrait/" + hero.id + ".spine.json"),
                    atlas = Load<TextAsset>(portrait + "Atlas/portrait/" + hero.id + ".atlas.txt"),
                    page = Load<Texture2D>(portrait + "Textures/portrait/" + hero.id + ".png"),
                    portrait = HudSprite("Faces/" + hero.id + "_s"),
                    cardPortrait = AssetDatabase.LoadAssetAtPath<Sprite>(Arena + "Res/Textures/Draft/Heroes/" + hero.id + "_l.png"),
                    illustration = AssetDatabase.LoadAssetAtPath<Sprite>(Arena + "Res/Textures/Draft/Heroes/" + hero.id + "_fu.png")
                        ?? AssetDatabase.LoadAssetAtPath<Sprite>(Arena + "Res/Textures/Draft/Heroes/" + hero.id + "_l.png"),
                    element = HudSprite("cm_icon_prom" + hero.element),
                    cardElement = HeroCardElementArt(hero.element),
                    role = HudSprite("cm_icon_role_" + role)
                };
            }).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
