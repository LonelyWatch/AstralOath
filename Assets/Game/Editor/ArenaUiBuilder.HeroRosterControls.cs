using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void ApplyHeroRosterPresentation(List<(string Key, Object Target)> bindings)
        {
            T Find<T>(string key) where T : Object => (T)bindings.First(entry => entry.Key == key).Target;
            void Remove(Transform parent, string name)
            {
                var previous = parent.Find(name);
                if (previous) Object.DestroyImmediate(previous.gameObject);
            }

            var detail = Find<RectTransform>("rosterDetail");
            var header = Find<RectTransform>("rosterHeaderComposition");
            var controls = (RectTransform)header.Find("Roster controls");
            bindings.RemoveAll(entry => entry.Key == "gearSets" || entry.Key.StartsWith("heroSet")
                || entry.Key == "heroIdentityShade" || entry.Key == "rosterTools" || entry.Key == "rosterFilterButton"
                || entry.Key == "rosterSearchButton" || entry.Key == "rosterSearchCaption" || entry.Key == "rosterSortCaption" || entry.Key == "search"
                || entry.Key == "searchHint");
            foreach (var name in new[] { "No set icon", "No equipment set", "Active equipment sets", "Hero identity shade" })
                Remove(detail, name);
            foreach (var name in new[] { "Search heroes", "Sorting icon", "Sort caption", "Open search", "Filter roster" })
                Remove(controls, name);
            Remove(header, "Roster tools");

            var shade = Picture(detail, "Hero identity shade", RosterArt("HeroIdentityShade"), .056f, .089f, .335f, .36f);
            shade.color = new Color32(10, 15, 39, 236);
            shade.rectTransform.SetAsFirstSibling();
            bindings.Add(("heroIdentityShade", shade));

            var readingInk = Resources.Load<Material>("AstralOathPlayerNameShadow");
            Remove(detail, "Attribute contrast");
            var firstStat = Find<GameText>("detailStatLabel_attack").rectTransform;
            var statShade = Area(detail,"Attribute contrast",.058f,.557f,.170f,.279f);
            Paint(statShade,null,new Color32(10,17,32,202));
            statShade.SetSiblingIndex(firstStat.GetSiblingIndex());
            foreach (var entry in bindings.Where(item => item.Key.StartsWith("detailStat")))
            {
                var stat = (GameText)entry.Target;
                stat.fontSharedMaterial = readingInk;
                stat.color = new Color32(219, 227, 238, 255);
            }
            foreach (var key in new[] { "roster_manage_equipment", "roster_auto_equip", "roster_remove_all" })
            {
                var action = detail.Find(key);
                var label = action.Find("Action caption").GetComponent<GameText>();
                Remove(action, "Caption backing");
                var backing = Paint(Area((RectTransform)action, "Caption backing", -.105f, .944f, 1.21f, .35f),
                    null, new Color32(12, 20, 35, 225));
                backing.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
                Place(label.rectTransform, -.13f, .940f, 1.26f, .35f);
                label.fontSize = 23;
                label.fontSharedMaterial = readingInk;
            }
            var inspect = detail.Find("Inspect equipment").GetComponent<Image>();
            inspect.type = Image.Type.Sliced;
            inspect.preserveAspect = false;
            // Keep the training action and its failure text clear of all three set lines.
            Place((RectTransform)Find<GameButton>("artifactTrial").transform, .072f, .398f, .135f, .061f);
            Place(Find<GameText>("artifactTrialStatus").rectTransform, .072f, .464f, .236f, .038f);

            var sets = Area(detail, "Active equipment sets", .072f, .855f, .215f, .113f);
            for (int i = 0; i < 3; ++i)
            {
                float row = i / 3f;
                var icon = Picture(sets, "Set icon " + i, RosterArt("cm_icon_etcbp"), 0, row + .025f, .074f, .265f, true);
                icon.color = RosterMuted;
                var label = Text(sets, "Set name " + i, InventoryPrefix + "roster_no_set", 23, .105f, row, .89f, .32f, RosterMuted);
                bindings.Add(("heroSetIcon" + i, icon));
                bindings.Add(("heroSetLabel" + i, label));
            }

            var searchButton = Button(Area(controls, "Open search", .738f, .102f, .084f, .76f), CommonArt("_btn_small_black"), Color.white);
            PressSmallBlack(searchButton);
            Picture((RectTransform)searchButton.transform, "Search icon", Art("cm_icon_etczoomer"), .065f, .18f, .23f, .64f, true);
            var searchCaption = Text((RectTransform)searchButton.transform, "Caption", InventoryPrefix + "roster_search", 24, .29f, .03f, .65f, .94f,
                alignment: TextAlignmentOptions.Center);
            searchCaption.richText = false;
            var filter = Button(Area(controls, "Filter roster", .826f, .102f, .088f, .76f), CommonArt("_btn_small_black"), Color.white);
            PressSmallBlack(filter);
            Picture((RectTransform)filter.transform, "Filter icon", Art("_sorting2"), .06f, .18f, .22f, .64f, true);
            var caption = Text((RectTransform)filter.transform, "Sort caption", InventoryPrefix + "roster_sort_level", 24,
                .29f, .03f, .66f, .94f, alignment: TextAlignmentOptions.Center);
            bindings.Add(("rosterSortCaption", caption));
            bindings.Add(("rosterSearchButton", searchButton));
            bindings.Add(("rosterSearchCaption", searchCaption));
            bindings.Add(("rosterFilterButton", filter));

            var tools = Area(header, "Roster tools", 0, 0, 1, 1);
            var popup = new List<(string Key, Object Target)>();
            RectTransform Panel(string key, string title, float x, float w, float height)
            {
                var overlay = Area(tools, title, 0, 0, 1, 1);
                var dismiss = Button(Area(overlay, "Outside", 0, .159f, 1, .841f), null, Color.clear);
                dismiss.transition = Selectable.Transition.None;
                var face = Area(overlay, "Panel", x, .161f, w, height);
                Paint(face, null, new Color32(19,30,48,253), true);
                PaintSoftModal(Area(face,"Frame",0,0,1,1)).raycastTarget = false;
                popup.Add((key, overlay.gameObject));
                popup.Add((key + "Dismiss", dismiss));
                overlay.gameObject.SetActive(false);
                return face;
            }
            GameButton Choice(RectTransform parent, string key, string label, float x, float y, float w, float h)
            {
                var button = Action(parent, key, label, 22, x, y, w, h, out _, CommonArt("_btn_small_black"), Color.white);
                PressSmallBlack(button);
                popup.Add((key, button));
                return button;
            }

            var search = Panel("searchPanel", "Search by hero name", .617f, .30f, .123f);
            var input = BuildRosterSearch(search, out var hint);
            Place((RectTransform)input.transform, .035f, .18f, .545f, .64f);
            var clear = Button(Area(search, "Clear search", .588f, .18f, .12f, .64f), CommonArt("_btn_small_black"), Color.white);
            PressSmallBlack(clear);
            Picture((RectTransform)clear.transform, "Clear", Art("cm_icon_etcclose"), .20f, .2f, .60f, .60f, true);
            popup.Add(("clear", clear));
            Choice(search, "submit", InventoryPrefix + "roster_search", .74f, .18f, .225f, .64f);
            bindings.Add(("search", input));
            bindings.Add(("searchHint", hint));
            var options = Panel("filters", "Roster filters", .457f, .459f, .362f);
            void Mark(GameButton button, string key)
            {
                var indicator = Paint(Area((RectTransform)button.transform, "Selected", .10f, .88f, .80f, .045f),
                    null, new Color32(93, 205, 245, 255));
                popup.Add((key + "Mark", indicator));
                indicator.enabled = false;
            }
            Text(options, "Heading", "arena.draft.filter.button", 29, .04f, .035f, .30f, .135f);
            Choice(options, "reset", InventoryPrefix + "roster_filter_reset", .735f, .03f, .22f, .135f);

            Text(options, "Sorting", InventoryPrefix + "roster_sort", 22, .04f, .208f, .16f, .15f, RosterMuted);
            Mark(Choice(options, "sort_level", "arena.draft.filter.level", .20f, .195f, .225f, .16f), "sort_level");
            Mark(Choice(options, "sort_acquired", "arena.draft.filter.acquired", .435f, .195f, .225f, .16f), "sort_acquired");
            var order = Choice(options, "direction", InventoryPrefix + "roster_sort_desc", .67f, .195f, .285f, .16f);
            popup.Add(("directionLabel", order.GetComponentInChildren<GameText>()));
            void Row(string group, string title, string[] values, float y)
            {
                Text(options, title, "arena.draft.filter." + title, 22, .04f, y, .16f, .17f, RosterMuted);
                float step = .76f / values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    string value = values[i];
                    string key = group + "_" + value;
                    var button = Button(Area(options, key, .20f + step * i, y, step - .008f, .182f),
                        CommonArt("_btn_small_black"), Color.white);
                    PressSmallBlack(button);
                    popup.Add((key, button));
                    Sprite sprite = group == "element" ? HeroCardElementArt(value) : HudSprite("cm_icon_role_" +
                        (value == "thief" ? "assassin" : value == "soul_weaver" ? "manauser" : value));
                    Picture((RectTransform)button.transform, value, sprite, .18f, .12f, .64f, .76f, true);
                    Mark(button, key);
                }
            }
            Row("element", "element", new[] { "fire", "ice", "wind", "light", "dark" }, .455f);
            Row("role", "class", new[] { "knight", "warrior", "mage", "ranger", "thief", "soul_weaver" }, .705f);
            Bind(tools, popup.ToArray());
            bindings.Add(("rosterTools", tools.GetComponent<BindingMap>()));
        }
    }
}
