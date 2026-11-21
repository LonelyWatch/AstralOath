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
        private static void BuildArtifactBagDetail(RectTransform detail, List<(string Key, Object Target)> refs)
        {
            void Link(string key, Object target) => refs.Add(("artifact" + key, target));
            var frame = Area(detail, "Detail frame", -.06f, -.055f, 1.12f, 1.12f);
            Paint(frame, BagArt("_box_equip_0"), Color.white, false, true).pixelsPerUnitMultiplier = 1f / BagDesignScale;

            var illustration = Area(detail, "Artifact illustration", .051f, .04f, .55f, .576f);
            var painting = Paint(illustration, null, Color.white);
            painting.preserveAspect = true;
            Link("Portrait", painting);
            Picture(illustration, "Card filigree", SummonArt("arti_frame_deco_1"), 0, 0, .44f, .40f);
            Link("DetailEmblem", Picture(illustration, "Emblem", null, -.027f, -.035f, .245f, .195f, true));
            Link("DetailBadge", Prefab(illustration, "NotificationBadge", .126f, -.026f, .166f, .064f).GetComponent<BindingMap>());
            var wearer = Area(illustration, "Wearer portrait medallion", -.046f, .061f, .126f, .084f);
            Picture(wearer, "Portrait background", HudSprite("_hero_s_bg_ally"), 0, 0, 1, 1, true);
            Link("DetailWearer", Picture(wearer, "Hero", null, .07f, .07f, .86f, .86f, true));
            Picture(wearer, "Portrait frame", HudSprite("_hero_s_frame_ally"), 0, 0, 1, 1, true);
            Link("DetailWearerFrame", wearer.gameObject);
            Link("DetailClass", Picture(illustration, "Class", null, .806f, .883f, .159f, .102f, true));
            for (int i = 0; i < 5; i++)
            {
                Link("Rarity" + i, Picture(illustration, "Rarity " + i, DraftSprite("TeamBadges/cm_icon_star"),
                    .856f, .025f + i * .052f, .096f, .051f, true));
                float x = .622f + i * .061f;
                Picture(detail, "Breakthrough track " + i, SummonArt("arti_gauge_bg"), x, .293f, .063f, .019f, true);
                Link("Break" + i, Picture(detail, "Breakthrough " + i, BagArt("arti_gauge_bar"), x, .293f, .063f, .019f, true));
            }
            var zoom = Button(Area(illustration, "Open illustration", .027f, .857f, .217f, .119f), BagArt("_btn_small_black"), Color.white);
            Picture((RectTransform)zoom.transform, "Zoom", RosterArt("icon_menu_zoom"), .19f, .12f, .62f, .76f, true);
            Link("DetailZoom", zoom);
            var lockButton = IconAction("lock", .815f, .039f, .117f, .063f);
            Link("LockOn", Picture(lockButton, "Locked", BagArt("cm_icon_etclocked"), .18f, .11f, .64f, .78f, true));
            Link("LockOff", Picture(lockButton, "Unlocked", BagArt("cm_icon_etclockun"), .18f, .11f, .64f, .78f, true));
            var sell = IconAction("sell", .779f, .536f, .164f, .086f);
            Link("SellGlyph", Picture(sell, "Sell", BagArt("cm_icon_etcbin"), .22f, .14f, .56f, .72f, true));

            Caption("DetailGrade", null, 21, .618f, .11f, .354f, .052f, Mist);
            Caption("Title", null, 25, .618f, .157f, .354f, .093f, Color.white, true);
            Caption("Level", null, 28, .620f, .242f, .355f, .059f, new Color32(255, 166, 48, 255)).fontStyle = FontStyles.Bold;
            Text(detail, "Attack heading", InventoryPrefix + "stat.attack", 23, .624f, .324f, .344f, .045f, Mist);
            Caption("Attack", null, 28, .625f, .357f, .342f, .055f, Color.white);
            Text(detail, "Health heading", InventoryPrefix + "stat.health", 23, .624f, .406f, .344f, .048f, Mist);
            Caption("Health", null, 28, .625f, .441f, .342f, .057f, Color.white);
            var wearerName = Caption("Wearer", null, 20, .618f, .518f, .35f, .052f, Mist);
            wearerName.gameObject.SetActive(false);

            AddTab("Skill", "skill_heading", .046f);
            AddTab("Story", "story_tab", .508f);
            var scroll = InventoryScroll(detail, "Description", .055f, .705f, .903f, .173f);
            var content = scroll.content;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var description = Text(content, "Skill or background", null, 24, 0, 0, 1, 1, Color.white,
                TextAlignmentOptions.TopLeft, wrap: true);
            description.overflowMode = TextOverflowModes.Overflow;
            Link("Skill", description);
            Link("DescriptionScroll", scroll);

            var actions = Area(detail, "Actions", .059f, .883f, .887f, .086f);
            var layoutActions = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            layoutActions.spacing = 5;
            layoutActions.childControlWidth = layoutActions.childControlHeight = true;
            layoutActions.childForceExpandWidth = layoutActions.childForceExpandHeight = true;
            foreach (string action in new[] { "equip", "unequip", "change", "enhance" })
            {
                string label = action == "change" ? "home.gear.singleChange" : action == "unequip"
                    ? "home.gear.singleRemove" : ArtifactInventoryPrefix + action;
                var control = Action(actions, action, label, 24, 0, 0, 1, 1, out var text,
                    action == "enhance" ? SummonArt("_btn_blue") : BagArt("_btn_small_black"), Color.white);
                control.gameObject.AddComponent<LayoutElement>().flexibleWidth = action == "enhance" ? 1.45f : 1;
                if (action == "change") Link("Change", control);
                else { Link("Action_" + action, control); Link("ActionLabel_" + action, text); }
            }

            GameText Caption(string key, string label, int size, float x, float y, float w, float h, Color color, bool wrap = false)
            {
                var text = Text(detail, key, label, size, x, y, w, h, color, TextAlignmentOptions.TopLeft, wrap);
                Link(key, text);
                return text;
            }

            RectTransform IconAction(string key, float x, float y, float w, float h)
            {
                var control = Action(detail, key, ArtifactInventoryPrefix + key, 20, x, y, w, h,
                    out var label, BagArt("_btn_small_black"), Color.white);
                label.gameObject.SetActive(false);
                Link("Action_" + key, control);
                Link("ActionLabel_" + key, label);
                return (RectTransform)control.transform;
            }

            void AddTab(string key, string label, float x)
            {
                var tab = Action(detail, key, ArtifactInventoryPrefix + label, 23, x, .628f, .461f, .063f,
                    out var text, BagArt("_btn_small_black"), Color.white);
                var highlight = Picture((RectTransform)tab.transform, "Selected tab", Art("_btn_white_flat"), 0, 0, 1, 1);
                highlight.type = Image.Type.Sliced;
                if (key == "Skill")
                {
                    Link("DetailSkillGlyph", Picture((RectTransform)tab.transform, "Artifact skill",
                        InventoryArt("icon_menu_pararti"), .24f, .16f, .13f, .68f, true));
                    Place(text.rectTransform, .33f, 0, .42f, 1);
                }
                text.transform.SetAsLastSibling();
                Link("Detail" + key + "Tab", tab);
                Link("Detail" + key + "Active", highlight);
                Link("Detail" + key + "Label", text);
            }
        }
    }
}
