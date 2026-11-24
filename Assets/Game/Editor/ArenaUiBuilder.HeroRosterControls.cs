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
            Remove(Find<RectTransform>("rosterShading"), "Summary shadow");
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

            // unit_detail.csb left_equipment(74,0)/n_dim, in 1280x720 source coordinates.
            Image Shadow(string name, string sprite, Vector2 center, Vector2 size, float angle)
            {
                var image = Picture(detail,name,RosterArt("SourceShadows/"+sprite),0,0,0,0);
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(.5f,.5f);
                rect.sizeDelta = size * HomeDesignScale;
                rect.anchoredPosition = center * HomeDesignScale;
                rect.localEulerAngles = new Vector3(0,0,angle);
                // These local copies include source node opacity in their linear black coverage.
                image.color = Color.white;
                rect.SetAsFirstSibling();
                return image;
            }
            Remove(detail,"Hero summary glow");
            Shadow("Hero summary glow","_grow",new Vector2(154,399),new Vector2(164*3.19f,167*7),0);
            // Cocos positive rotations are clockwise; Unity's Z rotation is counterclockwise.
            var shade = Shadow("Hero identity shade","_grow_s",new Vector2(154,361.998f),new Vector2(20*38.8f,113*5),90);
            bindings.Add(("heroIdentityShade", shade));

            var readingInk = Resources.Load<Material>("AstralOathPlayerNameShadow");
            Remove(detail, "Attribute contrast");
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
                var nativeFrame = Area(face,"Native frame",0,0,1,1);
                // Both source sprites include a 31–34px shadow outside the visible frame.
                // Keep that padding outside the content area instead of drawing inputs across the border.
                nativeFrame.offsetMin = -Vector2.one * 32f * HomeDesignScale;
                nativeFrame.offsetMax = Vector2.one * 32f * HomeDesignScale;
                if (key == "searchPanel")
                {
                    var sourceFrame = Paint(nativeFrame,RosterArt("SourceSearch/_box_black_gold"),Color.white,true,true);
                    sourceFrame.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
                }
                else
                {
                    var sourceFrame = Paint(nativeFrame,RosterArt("SourceSearch/filter_main_bg"),Color.white,true,true);
                    sourceFrame.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
                }
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

            var search = Panel("searchPanel", "Search by hero name", .617f, .30f, .55f);
            var input = BuildRosterSearch(search, out var hint);
            var inputPlate = input.GetComponent<Image>();
            inputPlate.sprite = RosterArt("SourceSearch/_btn_white");
            inputPlate.type = Image.Type.Sliced;
            inputPlate.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            input.textComponent.color = new Color32(30,27,24,255);
            hint.color = new Color32(84,81,77,255);
            hint.text = labels[InventoryPrefix+"roster_search_input_hint"];
            input.transform.Find("Search icon").gameObject.SetActive(false);
            Place(input.textViewport,.045f,.03f,.80f,.94f);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            Place((RectTransform)input.transform, .035f, .04f, .66f, .145f);
            var clear = Button(Area(search, "Clear search", .603f, .04f, .085f, .145f), null, Color.clear);
            Picture((RectTransform)clear.transform, "Clear", Art("cm_icon_etcclose"), .20f, .2f, .60f, .60f, true)
                .color = new Color32(67,55,47,255);
            popup.Add(("clear", clear));
            var submit = Action(search,"submit",InventoryPrefix+"roster_search",22,.715f,.04f,.25f,.145f,
                out _,CommonArt("_btn_green"),Color.white);
            popup.Add(("submit",submit));
            Paint(Area(search,"History divider",.035f,.205f,.93f,.008f),Art("_bar_2"),new Color32(255,255,255,45),sliced:true);
            Text(search,"Recent searches",InventoryPrefix+"roster_recent_searches",19,.045f,.22f,.79f,.08f,RosterMuted);
            Text(search,"Empty search history",InventoryPrefix+"roster_empty_history",19,
                .06f,.55f,.88f,.15f,RosterMuted,TextAlignmentOptions.Center);
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
