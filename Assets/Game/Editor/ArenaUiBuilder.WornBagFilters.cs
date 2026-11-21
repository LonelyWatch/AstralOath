using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static BindingMap BuildWornBagFilters(RectTransform bag)
        {
            var overlay = Area(bag, "Worn hero sort and filters", 0, 0, 1, 1);
            var close = Button(Area(overlay, "Outside", 0, 0, 1, 1), null, Color.clear);
            var refs = new List<(string, Object)> { ("close", close) };
            var panel = BagRightArea(overlay, "Filter panel", 23, 146, 856, 296);
            PaintFilterPanel(panel);
            var divide = Area(panel, "Sort column divider", .671f, .057f, .0015f, .89f);
            Paint(divide, null, new Color(1f, 1f, 1f, .42f));
            AddGroup("element", FilterLabelPrefix + "element", .058f,
                new[] { "all", "fire", "ice", "wind", "light", "dark" });
            AddGroup("stars", InventoryPrefix + "bag_grade", .382f, new[] { "all", "6", "5", "4", "3", "2" });
            NativeRule(panel, "Options divider", .024f, .69f, .63f, .014f, new Color(1, 1, 1, .20f), "_bar_5");
            AddToggle("hideMax", .034f);
            AddToggle("hideFull", .354f);
            string[] sorts = { "stars", "level", "element", "name", "acquired", "ability" };
            for (int i = 0; i < sorts.Length; i++)
            {
                string key = "sort_" + sorts[i];
                var rect = Area(panel, key, .681f, .034f + .1515f * i, .313f, .144f);
                refs.Add((key, Button(rect, null, Color.clear)));
                var active = Picture(rect, "Selected", Art("_btn_small_white"), 0, 0, 1, 1);
                active.type = Image.Type.Sliced;
                refs.Add((key + "_on", active));
                var label = Text(rect, "Sort", sorts[i] == "ability" ? InventoryPrefix + "sort_power" : FilterLabelPrefix + sorts[i],
                    26, .045f, 0, .69f, 1);
                refs.Add((key + "_label", label));
                if (sorts[i] == "ability")
                    refs.Add(("sort_attackIcon", Picture(rect, "Attack", InventoryArt("Equipment/cm_icon_stat_att"), .286f, .18f, .107f, .65f, true)));
            }
            var direction = Area(panel, "Sort direction", .909f, .80f, .077f, .111f);
            var reverse = Button(direction, FilterSprite("_btn_small_black"), Color.white);
            refs.Add(("reverse", reverse));
            refs.Add(("up", Picture(direction, "Ascending", Art("cm_btn_sorting_up"), .04f, .09f, .46f, .82f, true)));
            refs.Add(("down", Picture(direction, "Descending", Art("cm_btn_sorting_down"), .50f, .09f, .46f, .82f, true)));
            Bind(overlay, refs.ToArray());
            overlay.gameObject.SetActive(false);
            return overlay.GetComponent<BindingMap>();

            void AddGroup(string key, string heading, float y, string[] values)
            {
                PaintFilterHeadingShade(panel, .028f, y + .027f, .29f, .041f);
                Text(panel, key + " heading", heading, 21, .032f, y, .30f, .089f, FilterMuted);
                for (int i = 0; i < values.Length; i++)
                {
                    string value = values[i];
                    var group = Area(panel, key + " " + value, .030f + i * .0784f, y + .112f, .076f, .158f);
                    var icon = key == "element" && i > 0 ? FilterSprite("cm_icon_pro" + value) : null;
                    string label = i == 0 ? labels["arena.filter.all"] : icon ? null : "★" + value;
                    BuildFilterChoice(group, value, label, icon, 0, 0, 1, 1);
                    refs.Add((key + "_" + value, group.GetChild(0).GetComponent<BindingMap>()));
                }
            }

            void AddToggle(string key, float x)
            {
                var rect = Area(panel, key, x, .727f, .312f, .118f);
                refs.Add((key, Button(rect, null, Color.clear)));
                Picture(rect, "Checkbox", FilterSprite("easy_setting_checkbg"), 0, .11f, .086f, .78f, true);
                refs.Add((key + "Check", Picture(rect, "Check", CommonArt("cm_checkicon2"), 0, .11f, .086f, .78f, true)));
                Text(rect, "Option", InventoryPrefix + "bag_" + key, 22, .107f, 0, .89f, 1, FilterMuted);
            }
        }
    }
}
