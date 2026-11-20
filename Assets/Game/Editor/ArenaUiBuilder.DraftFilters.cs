using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string FilterLabelPrefix = "arena.draft.filter.";
        private const string FilterCatalogPath = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";
        private static readonly Color FilterMuted = new Color32(126, 149, 166, 255);
        private static Sprite FilterSprite(string name) => DraftSprite("Filters/" + name);

        private static void BuildDraftFilters(RectTransform root, RectTransform pool, List<(string, Object)> refs)
        {
            var catalog = ReadDraftFilterCatalog();
            var toggle = FilterButton(Area(pool, "Open roster filters", .042f, .071f, .465f, .062f));
            Picture((RectTransform)toggle.transform, "Filter icon", Art("_sorting2"), .052f, .18f, .18f, .64f, true);
            refs.Add(("openRosterFilters", toggle));
            refs.Add(("rosterFilterLabel", FilterText((RectTransform)toggle.transform, "Active sorting", "level", 21, .255f, 0, .69f, 1)));
            var empty = FilterText(pool, "No matching heroes", "empty", 25, .055f, .20f, .89f, .075f,
                FilterMuted, TextAlignmentOptions.Center);
            empty.gameObject.SetActive(false);
            refs.Add(("rosterEmpty", empty.gameObject));

            var overlay = Area(root, "Roster filter overlay", 0, 0, 1, 1);
            var outside = Button(Area(overlay, "Dismiss roster filters", 0, 0, 1, 1), null, Color.clear);
            outside.transition = Selectable.Transition.None;
            var panel = Area(overlay, "Native roster filter panel", .015f, .136f, .94f, .566f);
            PaintFilterPanel(panel);
            var controls = new List<(string, Object)> { ("close", outside) };

            FilterHeading(panel, "set", .021f, .034f, .276f);
            var equipment = FilterText(panel, "Equipment availability", "unavailable", 23, .032f, .124f, .24f, .093f, FilterMuted);
            var equipmentPlate = Picture(panel, "Equipment filter disabled", FilterSprite("_btn_small_black"), .020f, .122f, .28f, .098f);
            equipmentPlate.type = Image.Type.Sliced;
            equipment.transform.SetAsLastSibling();
            FilterHeading(panel, "effect", .021f, .266f, .276f);
            var reset = FilterButton(Area(panel, "Reset skill filters", .255f, .257f, .044f, .078f));
            Picture((RectTransform)reset.transform, "Reset graphic", FilterSprite("icon_menu_reset"), .21f, .19f, .58f, .62f, true);
            controls.Add(("resetEffects", reset));

            string[] groups = { "enhance", "weaken", "general" };
            for (int index = 0; index < groups.Length; index++)
            {
                string group = groups[index];
                var button = FilterButton(Area(panel, "Skill group " + group, .020f, .362f + index * .111f, .28f, .103f));
                var row = (RectTransform)button.transform;
                var badge = Picture(row, "Effect count plate", Art("_btn_small_white"), .035f, .19f, .082f, .62f);
                badge.type = Image.Type.Sliced;
                controls.Add(("show_" + group, button));
                controls.Add(("count_" + group, Text(row, "Effect count", null, 19, .034f, .08f, .085f, .84f,
                    new Color32(21, 36, 45, 255), TextAlignmentOptions.Center)));
                FilterText(row, "Effect name", group, 25, .142f, 0, .79f, 1);
            }

            BuildAttributeOptions("element", .037f, new[] { "fire", "ice", "wind", "light", "dark" },
                new[] { "cm_icon_profire", "cm_icon_proice", "cm_icon_prowind", "cm_icon_prolight", "cm_icon_prodark" });
            BuildAttributeOptions("class", .265f, new[] { "warrior", "knight", "thief", "ranger", "mage", "soul_weaver" },
                new[] { "icon_menu_warrior", "icon_menu_roleknight", "icon_menu_roleassassin", "icon_menu_ranger", "icon_menu_rolemage", "icon_menu_rolremanauser" });
            BuildAttributeOptions("rarity", .493f, new[] { "6", "5", "4", "3", "2" }, null);

            NativeRule(panel, "Options divider", .019f, .752f, .714f, .014f, new Color(1, 1, 1, .20f), "_bar_5");
            var max = Button(Area(panel, "Hide max level toggle", .028f, .795f, .224f, .063f), null, Color.clear);
            var maxRow = (RectTransform)max.transform;
            var maxMark = FilterCheckbox(maxRow, "hideMax", Color.white);
            controls.Add(("hideMax", max));
            controls.Add(("hideMaxCheck", maxMark));
            FilterCheckbox(Area(panel, "Lock filter unavailable", .269f, .795f, .226f, .063f), "owned", FilterMuted).enabled = false;
            FilterCheckbox(Area(panel, "Friendship filter unavailable", .505f, .795f, .225f, .063f), "bond15", FilterMuted).enabled = false;
            FilterText(panel, "Unavailable account filters", "unavailable", 18, .277f, .867f, .43f, .063f, FilterMuted);

            var sortDivider = Area(panel, "Sort column divider", .749f, .042f, 0, .917f);
            sortDivider.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 1.5f);
            Paint(sortDivider, null, new Color32(167, 190, 208, 75));
            string[] sort = { "stars", "level", "name", "bond", "imprint", "acquired" };
            for (int index = 0; index < sort.Length; index++)
            {
                var key = sort[index];
                float y = .033f + index * .080f;
                if (index < 3) BuildSort(key, .765f, y, .217f, .075f, 26);
                else FilterText(panel, "Unavailable sort " + key, key, 24, .773f, y, .21f, .075f, FilterMuted);
            }
            FilterHeading(panel, "ability", .77f, .537f, .20f);
            BuildSort("attack", .766f, .635f, .101f, .095f, 22);
            BuildSort("defense", .876f, .635f, .101f, .095f, 22);
            BuildSort("health", .766f, .744f, .101f, .095f, 22);
            BuildSort("speed", .876f, .744f, .101f, .095f, 22);
            var compact = Button(Area(panel, "Compact roster toggle", .768f, .892f, .206f, .062f), null, Color.clear);
            controls.Add(("compact", compact));
            controls.Add(("compactCheck", FilterCheckbox((RectTransform)compact.transform, "compact", Color.white)));

            var effects = Area(panel, "Skill effect choices", .305f, .081f, .432f, .657f);
            PaintFilterPanel(effects);
            controls.Add(("effectList", effects.gameObject));
            var effectLabels = catalog.messages.ToDictionary(row => row.key, row => row.zhCn, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var effectGroup = Area(effects, "Effect group " + group, 0, 0, 1, 1);
                FilterHeading(effectGroup, group, .045f, .039f, .90f);
                var list = Scroll(effectGroup, "Effect list", .036f, .16f, .925f, .78f);
                var column = list.content.GetComponent<VerticalLayoutGroup>();
                Object.DestroyImmediate(column);
                var grid = list.content.gameObject.AddComponent<GridLayoutGroup>();
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 3;
                grid.cellSize = new Vector2(218, 44);
                grid.spacing = new Vector2(6, 6);
                BuildFilterChoice(list.content, "all", labels["arena.filter.all"], null, 0, 0, 0, 0);
                foreach (string key in CollectEffectKeys(catalog, group))
                {
                    string label;
                    if (key.StartsWith("status_", StringComparison.Ordinal))
                    {
                        string status = key.Substring(7);
                        label = effectLabels.TryGetValue("battle.status." + status.ToLowerInvariant() + ".name", out string translated)
                            ? translated : status;
                    }
                    else label = labels[FilterLabelPrefix + key];
                    BuildFilterChoice(list.content, key, label, null, 0, 0, 0, 0);
                }
                Bind(effectGroup, ("options", list.content));
                controls.Add((group, effectGroup.GetComponent<BindingMap>()));
                effectGroup.gameObject.SetActive(false);
            }
            effects.gameObject.SetActive(false);
            Bind(overlay, controls.ToArray());
            refs.Add(("rosterFilters", overlay.gameObject));
            overlay.gameObject.SetActive(false);

            void BuildAttributeOptions(string key, float y, string[] options, string[] icons)
            {
                var area = Area(panel, "Filter " + key, .318f, y, .412f, .208f);
                PaintFilterHeadingShade(area, 0, .085f, .53f, .11f);
                FilterText(area, "Filter group heading", key, 22, 0, 0, .90f, .37f, new Color32(180, 191, 199, 255));
                var row = Area(area, "Options", 0, .435f, 1, .54f);
                const float cell = .135f, gap = .009f;
                BuildFilterChoice(row, "all", labels["arena.filter.all"], null, 0, 0, cell, 1);
                for (int i = 0; i < options.Length; i++)
                    BuildFilterChoice(row, options[i], icons == null ? "★" + options[i] : null,
                        icons == null ? null : FilterSprite(icons[i]), (i + 1) * (cell + gap), 0, cell, 1);
                Bind(area, ("options", row));
                controls.Add((key, area.GetComponent<BindingMap>()));
            }

            void BuildSort(string key, float x, float y, float w, float h, int fontSize)
            {
                var row = Area(panel, "Sort " + key, x, y, w, h);
                string prefix = "sort_" + key;
                var button = Button(row, null, Color.clear);
                var selected = Picture(row, "Selected", Art("_btn_small_white"), 0, 0, 1, 1);
                selected.type = Image.Type.Sliced;
                controls.Add((prefix, button));
                controls.Add((prefix + "_on", selected));
                controls.Add((prefix + "_label", FilterText(row, "Sort name", key, fontSize, .05f, 0, .67f, 1)));
                controls.Add((prefix + "_up", Picture(row, "Ascending", Art("cm_btn_sorting_up"), .69f, .14f, .25f, .72f, true)));
                controls.Add((prefix + "_down", Picture(row, "Descending", Art("cm_btn_sorting_down"), .69f, .14f, .25f, .72f, true)));
            }
        }

        private static void PaintFilterPanel(RectTransform panel)
        {
            var hit = Paint(panel, null, Color.clear, true);
            hit.canvasRenderer.cullTransparentMesh = false;
            var background = Area(panel, "Native filter gradient and frame", 0, 0, 1, 1);
            // The CSB texture has a soft shadow beyond its visible frame: left/right 34,
            // bottom 37, top 31 native pixels. Keep those margins outside the control area.
            const float designScale = 1.5f;
            background.offsetMin = new Vector2(-34f, -37f) * designScale;
            background.offsetMax = new Vector2(34f, 31f) * designScale;
            var image = Paint(background, FilterSprite("filter_main_bg"), Color.white, sliced: true);
            image.pixelsPerUnitMultiplier = 1f / designScale;
        }

        private static GameButton FilterButton(RectTransform rect)
        {
            var button = Button(rect, FilterSprite("_btn_small_black"), Color.white);
            PressSprites(button, FilterSprite("_btn_small_black"), FilterSprite("_btn_small_black_p"));
            return button;
        }

        private static GameText FilterText(RectTransform parent, string name, string key, int size, float x, float y, float w, float h,
            Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft) =>
            Text(parent, name, FilterLabelPrefix + key, size, x, y, w, h, color, alignment);

        private static void FilterHeading(RectTransform parent, string key, float x, float y, float w)
        {
            PaintFilterHeadingShade(parent, x, y + .02f, w * .82f, .032f);
            FilterText(parent, "Filter group heading", key, 22, x + .004f, y, w, .11f, new Color32(180, 191, 199, 255));
        }

        private static void PaintFilterHeadingShade(RectTransform parent, float x, float y, float w, float h)
        {
            Paint(Area(parent, "Heading shade", x, y, w, h), FilterSprite("filter_img_deco"),
                new Color32(255, 255, 255, 25), sliced: true);
        }

        private static Image FilterCheckbox(RectTransform row, string key, Color color)
        {
            Picture(row, "Checkbox", FilterSprite("easy_setting_checkbg"), .02f, .10f, .093f, .80f, true);
            FilterText(row, "Checkbox label", key, 22, .135f, 0, .85f, 1, color);
            return Picture(row, "Checkmark", CommonArt("cm_checkicon2"), .02f, .10f, .093f, .80f, true);
        }

        private static void BuildFilterChoice(RectTransform group, string key, string name, Sprite icon,
            float x, float y, float w, float h)
        {
            var choice = Area(group, key, x, y, w, h);
            var button = FilterButton(choice);
            var selection = Picture(choice, "Selected filter state", Art("_btn_small_white"), 0, 0, 1, 1);
            selection.type = Image.Type.Sliced;
            if (icon) Picture(choice, "Filter choice icon", icon, .17f, .13f, .66f, .74f, true);
            var label = Text(choice, "Filter option", null, 23, .035f, 0, .93f, 1, alignment: TextAlignmentOptions.Center);
            label.text = name ?? "";
            Bind(choice, ("button", button), ("on", selection), ("label", label));
        }

        private static RosterFilterCatalog ReadDraftFilterCatalog() => JsonUtility.FromJson<RosterFilterCatalog>(File.ReadAllText(FilterCatalogPath));

        private static void PopulateDraftFilterData(ArenaDraftArt target)
        {
            var data = ReadDraftFilterCatalog();
            var heroes = data.heroes.ToDictionary(hero => hero.id, StringComparer.Ordinal);
            var effects = data.effects.ToLookup(effect => effect.skillId, StringComparer.Ordinal);
            var skills = data.skills.ToLookup(skill => skill.heroId, StringComparer.Ordinal);
            foreach (var hero in target.Heroes)
            {
                var row = heroes[hero.Id];
                hero.BaseStars = row.baseStars;
                hero.Attack = row.attack;
                hero.Defense = row.defense;
                hero.Health = row.health;
                hero.Speed = row.speed;
                hero.FilterEffects = skills[hero.Id].SelectMany(skill => effects[skill.id])
                    .Select(EffectFilterKey).Where(key => key != null).Distinct(StringComparer.Ordinal).ToArray();
            }
        }

        private static IEnumerable<string> CollectEffectKeys(RosterFilterCatalog catalog, string group) => catalog.effects
            .Where(effect => EffectGroup(effect) == group).Select(EffectFilterKey).Where(key => key != null)
            .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal);

        private static string EffectGroup(RosterFilterEffect effect)
        {
            if(effect.operation=="resource_nullifier")return "enhance";
            if (effect.operation is "apply_status" or "random_buff" or "random_buff_option" or "random_status_option")
            {
                return effect.status switch
                {
                    "AttackDown" or "DefenseDown" or "SpeedDown" or "Stun" or "Silence" or "Provoke" or "Burn" or "Bleed" or
                    "Poison" or "Unhealable" or "Unbuffable" or "HitChanceDown" or "Bomb" or "VampiricTouch" or
                    "MagicNail" or "Sleep" or "Restrict" or "Bind" or "Venom" or "Frostbite" or "Rupture" => "weaken",
                    "Indomitable" or "Rage" or "SpiritlordProtection" or "DamageLimit" => "general",
                    _ => "enhance"
                };
            }
            return "general";
        }

        private static string EffectFilterKey(RosterFilterEffect effect) => effect.operation switch
        {
            "apply_status" or "random_buff" or "random_buff_option" or "random_status_option" when !string.IsNullOrWhiteSpace(effect.status) && effect.status != "none" => "status_" + effect.status,
            "resource_nullifier" => "status_MagicBarrier",
            "heal" or "heal_target_health" or "lifesteal" => "effect_heal",
            "cleanse" or "cleanse_all" or "cleanse_once_on_critical" or "transfer_debuff" => "effect_cleanse",
            "dispel" or "dispel_all" => "effect_dispel",
            "readiness" or "readiness_bonus" or "readiness_per_critical" or "readiness_per_target" or "readiness_per_status_applied"
                or "readiness_per_target_status" or "readiness_fixed" or "off_turn_readiness" or "trigger_readiness" => "effect_raise",
            "reduce_readiness" or "reduce_readiness_double" => "effect_reduce",
            "grant_extra_turn" => "effect_extra_turn",
            "revive" => "effect_revive",
            "target_health_barrier" or "caster_health_barrier" or "caster_attack_barrier" => "effect_barrier",
            "reduce_cooldown" or "reduce_own_cooldown" or "reduce_third_cooldown" or "reset_cooldown" => "effect_reduce_cd",
            "extend_cooldown" or "max_cooldowns" => "effect_extend_cd",
            "injury" => "effect_injury",
            "grant_soul" => "effect_soul",
            _ => null
        };

        [Serializable] private sealed class RosterFilterCatalog
        {
            public RosterFilterHero[] heroes;
            public RosterFilterSkill[] skills;
            public RosterFilterEffect[] effects;
            public DraftMessage[] messages;
        }

        [Serializable] private sealed class RosterFilterHero { public string id; public int baseStars, attack, defense, health, speed; }
        [Serializable] private sealed class RosterFilterSkill { public string id, heroId; }
        [Serializable] private sealed class RosterFilterEffect { public string skillId, operation, status; }
    }
}
