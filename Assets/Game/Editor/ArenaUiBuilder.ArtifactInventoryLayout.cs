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
        private const string ArtifactInventoryPrefix = "home.artifacts.";

        private static void BuildNativeArtifactManagement(RectTransform root, RectTransform bag, RectTransform roster,
            List<(string Key, Object Target)> bindings)
        {
            var chrome = (RectTransform)bag.Find("Empty Artifact native screen");
            for (int index = 0; index < 8; ++index)
            {
                float center = 183 + 75 * index;
                var key = "artifactFilter" + index;
                var filter = Button(BagArea(chrome, key, center - 37.5f, 83, 75, 58), null, Color.clear);
                bindings.Add((key, filter));
                bindings.Add(("artifactFilterActive" + index,
                    chrome.Find("Category " + index + " selected").GetComponent<Image>()));
                if (index > 0)
                    bindings.Add(("artifactClassIcon" + index, chrome.Find("Category " + index + " icon").GetComponent<Image>()));
            }

            var sortLabel = (GameText)bindings.Find(item => item.Key == "bagArtifactSortLabel").Target;
            var sort = sortLabel.transform.parent.gameObject.AddComponent<GameButton>();
            var sortImage = (Image)sortLabel.transform.parent.GetComponent<Graphic>();
            sortImage.raycastTarget = true;
            sort.targetGraphic = sortImage;
            sort.transition = Selectable.Transition.None;
            bindings.Add(("artifactSort", sort));
            bindings.Add(("artifactSortLabel", sortLabel));
            bindings.Add(("artifactEmptyIcon", chrome.Find("Empty category detail/Empty information icon").gameObject));
            bindings.Add(("artifactEmptyMessage", chrome.Find("Empty category detail/Empty inventory information").gameObject));
            bindings.Add(("artifactEmptyPlate", chrome.Find("Empty category detail").GetComponent<Image>()));
            var capacity = Text((RectTransform)chrome.Find("Empty capacity"), "Owned artifact count", null, 22,
                .30f, 0, .67f, 1, Mist, TextAlignmentOptions.Center);
            bindings.Add(("artifactCapacity", capacity));

            var nativeSlot = (RectTransform)roster.Find("Selected hero detail/Hero equipment slots/Empty artifact slot");
            var slot = nativeSlot.gameObject.AddComponent<GameButton>();
            slot.targetGraphic = nativeSlot.GetComponent<Image>();
            slot.targetGraphic.raycastTarget = true;
            slot.transition = Selectable.Transition.None;
            var equipped = Paint(Area(nativeSlot, "Equipped artifact art", .04f, .05f, .92f, .90f), null, Color.white);
            equipped.preserveAspect = true;
            equipped.enabled = false;
            var level = Text(nativeSlot, "Equipped artifact level", null, 24, .40f, 0, .55f, .2f,
                new Color32(255, 182, 66, 255), TextAlignmentOptions.Right);
            bindings.Add(("artifactSlot", slot));
            bindings.Add(("artifactSlotImage", equipped));
            bindings.Add(("artifactSlotLevel", level));

            var heroDetail = (RectTransform)roster.Find("Selected hero detail");
            var trial = Action(heroDetail, "Artifact Trial", InventoryPrefix + "artifact_trial", 23,
                .072f, .843f, .135f, .061f, out var trialLabel, CommonArt("_btn_green"));
            var trialStatus = Text(heroDetail, "Artifact trial failure", InventoryPrefix + "artifact_trial_failed",
                19, .072f, .902f, .236f, .032f, new Color32(242, 177, 101, 255));
            trialStatus.enabled = false;
            trial.gameObject.SetActive(false);
            bindings.Add(("artifactTrial", trial));
            bindings.Add(("artifactTrialLabel", trialLabel));
            bindings.Add(("artifactTrialStatus", trialStatus));

            var pickerShade = Button(Area(root, "Equip artifact backdrop", 0, 0, 1, 1), null, Black(.72f));
            pickerShade.FeedbackSound = GameUiFeedback.Sound.Back;
            pickerShade.gameObject.SetActive(false);
            bindings.Add(("artifactPickerShade", pickerShade));

            BuildArtifactBrowser(root, bindings);

            var confirmation = Button(Area(root, "Artifact action backdrop", 0, 0, 1, 1), null, Black(.75f));
            confirmation.FeedbackSound = GameUiFeedback.Sound.Back;
            bindings.Add(("artifactConfirmShade", confirmation));
            var dialog = Area((RectTransform)confirmation.transform, "Artifact confirmation panel", .23f, .22f, .54f, .56f);
            Paint(dialog, BagArt("_box_equip_none"), new Color32(20, 37, 61, 255), true, true);
            bindings.Add(("artifactConfirmation", confirmation.gameObject));
            bindings.Add(("artifactConfirmTitle", Text(dialog, "Action title", null, 33, .06f, .041f, .88f, .13f, Color.white, TextAlignmentOptions.Center)));
            bindings.Add(("artifactConfirmText", Text(dialog, "Action summary", null, 25, .06f, .18f, .88f, .25f, Mist,
                TextAlignmentOptions.Center, wrap: true)));
            var materials = Area(dialog, "Enhancement choices", .042f, .414f, .914f, .299f);
            bindings.Add(("artifactMaterials", materials.gameObject));
            foreach (var entry in new[] { ("lesserCharm", 0f), ("greaterCharm", .25f), ("epicCharm", .50f), ("bottle", .75f) })
                AddArtifactButton(bindings, materials, entry.Item1, entry.Item2, .00f, .234f, BagArt("_btn_small_black"));
            AddArtifactButton(bindings, materials, "chooseCopy", .25f, .55f, .50f, BagArt("_btn_small_black"));
            AddArtifactButton(bindings, dialog, "cancel", .12f, .773f, .356f, Art("_btn_gold"));
            AddArtifactButton(bindings, dialog, "confirm", .52f, .773f, .356f, Art("_btn_blue"));
            confirmation.gameObject.SetActive(false);
        }

        private static void BuildArtifactBrowser(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var frame = Area(root, "Artifact inventory browser", .102f, .197f, .878f, .79f);
            var panel = Paint(frame, Art("_box_blue2"), new Color32(217, 228, 248, 255), false, true);
            panel.enabled = false;
            bindings.Add(("artifactBrowser", frame));
            bindings.Add(("artifactBrowserPlate", panel));
            var heading = ArtifactLabel(frame, "Artifact picker title", "picker_title", 31, .033f, .015f, .85f, .100f);
            bindings.Add(("artifactPickerTitle", heading));
            var close = Button(Area(frame, "Close artifact picker", .946f, .022f, .036f, .08f), Art("cm_icon_etcclose"), Color.white);
            close.FeedbackSound = GameUiFeedback.Sound.Back;
            bindings.Add(("artifactPickerClose", close));

            var scroll = InventoryScroll(frame, "Owned artifact cards", .018f, .005f, .646f, .986f);
            bindings.Add(("artifactItems", scroll));
            bindings.Add(("artifactCard", NativeArtifactInventoryCard(frame)));

            var status = Text(frame, "Artifact status", null, 27, .02f, .40f, .63f, .095f, Mist, TextAlignmentOptions.Center);
            bindings.Add(("artifactStatus", status));
            var retry = Action(frame, "Retry artifact inventory", InventoryPrefix + "retry", 25, .24f, .51f, .19f, .084f,
                out var retryLabel, CommonArt("_btn_green"));
            bindings.Add(("artifactRetry", retry));
            bindings.Add(("artifactRetryLabel", retryLabel));

            var detail = Area(frame, "Artifact detail", .678f, .005f, .305f, .975f);
            bindings.Add(("artifactDetail", detail.gameObject));
            var detailFrame = Area(detail, "Original artifact detail frame", -.06f, -.055f, 1.12f, 1.12f);
            Paint(detailFrame, BagArt("_box_equip_0"), Color.white, false, true).pixelsPerUnitMultiplier = 1f / BagDesignScale;
            var portrait = Paint(Area(detail, "Artifact painting", .045f, .04f, .555f, .575f), null, Color.white);
            portrait.preserveAspect = true;
            bindings.Add(("artifactPortrait", portrait));
            foreach (var label in new[]
            {
                ("artifactTitle", .617f, .128f, .35f, .122f, 25f),
                ("artifactLevel", .617f, .261f, .35f, .06f, 22f),
                ("artifactAttack", .617f, .358f, .35f, .058f, 25f),
                ("artifactHealth", .617f, .455f, .35f, .058f, 25f),
                ("artifactWearer", .617f, .531f, .35f, .079f, 18f),
                ("artifactSkill", .055f, .704f, .89f, .168f, 22f)
            })
            {
                var text = Text(detail, label.Item1, null, label.Item6, label.Item2, label.Item3, label.Item4,
                    label.Item5, label.Item1 == "artifactTitle" ? Color.white : Mist, wrap: true);
                text.alignment = TextAlignmentOptions.TopLeft;
                text.richText = false;
                bindings.Add((label.Item1, text));
            }
            Text(detail, "Attack heading", InventoryPrefix + "stat.attack", 19, .617f, .318f, .36f, .05f, Mist);
            Text(detail, "Health heading", InventoryPrefix + "stat.health", 19, .617f, .415f, .36f, .05f, Mist);
            for (int i = 0; i < 5; ++i)
            {
                float x = .617f + i * .061f;
                Picture(detail, "Breakthrough slot " + i, SummonArt("arti_gauge_bg"), x, .296f, .065f, .015f, true);
                bindings.Add(("artifactBreak" + i, Picture(detail, "Breakthrough " + i, BagArt("arti_gauge_bar"), x, .296f, .065f, .015f, true)));
                bindings.Add(("artifactRarity" + i, Picture(detail, "Artifact rarity " + i,
                    DraftSprite("TeamBadges/cm_icon_star"), .534f, .063f + i * .036f, .048f, .036f, true)));
            }
            var skillHeading = Area(detail, "Artifact skill heading", .045f, .632f, .912f, .062f);
            Paint(skillHeading, BagArt("_btn_small_black"), Color.white, false, true);
            Paint(Area(skillHeading, "Active skill heading", 0, 0, .55f, 1), Art("_btn_white_flat"), Color.white, false, true);
            Text(skillHeading, "Skill heading", ArtifactInventoryPrefix + "skill_heading", 22, 0, 0, .55f, 1, Color.black, TextAlignmentOptions.Center);
            AddArtifactButton(bindings, detail, "lock", .69f, .023f, .26f, BagArt("_btn_small_black"));
            var actions = Area(detail, "Artifact management actions", .04f, .894f, .92f, .08f);
            var group = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 7;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = true;
            AddArtifactButton(bindings, actions, "equip", 0, 0, 1, CommonArt("_btn_green"));
            AddArtifactButton(bindings, actions, "unequip", 0, 0, 1, BagArt("_btn_small_black"));
            AddArtifactButton(bindings, actions, "sell", 0, 0, 1, BagArt("_btn_small_black"));
            AddArtifactButton(bindings, actions, "enhance", 0, 0, 1, Art("_btn_blue"));

            frame.gameObject.SetActive(false);
        }

        private static GameObject NativeArtifactInventoryCard(RectTransform parent)
        {
            // artifact_bar.csb uses a 358 x 135 card, a 260 x 92 strip and a separate 116 x 128 emblem.
            var rect = Area(parent, "Native artifact inventory card", 0, 0, 1, 1);
            var card = Button(rect, null, Color.clear);
            var banner = Picture(rect, "Artifact banner", null, 88f/358, 24f/135, 260f/358, 92f/135, true);
            Picture(rect, "Artifact banner rim", SummonArt("arti_frame_small"), 88f/358, 14f/135, 272f/358, 114f/135, true);
            var portrait = Picture(rect, "Artifact thumbnail", null, 0, .012f, 116f/358, 128f/135, true);
            var role = Picture(rect, "Artifact class", null, .351f, .163f, .077f, .206f, true);
            var level = Text(rect, "Enhancement", null, 23, .163f, .018f, .147f, .226f,
                new Color32(255, 220, 116, 255), TextAlignmentOptions.Center);
            var limit = Text(rect, "Artifact level", null, 22, .347f, .385f, .63f, .223f,
                new Color32(255, 183, 76, 255));
            var name = Text(rect, "Artifact name", null, 23, .347f, .603f, .63f, .229f, Color.white);
            ApplySummonTextOutline(level);
            ApplySummonTextOutline(limit);
            ApplySummonTextOutline(name);
            var locked = Picture(rect, "Locked", Art("cm_icon_etclock"), .005f, .062f, .083f, .22f, true);
            var selection = Picture(rect, "Selected artifact frame", Art("_select"), -.018f, -.01f, 1.044f, 1.018f);
            selection.type = Image.Type.Sliced;
            selection.pixelsPerUnitMultiplier = 1f / BagDesignScale;
            selection.enabled = false;
            var keys = new List<(string Key, Object Target)> { ("artifactCardButton", card), ("artifactCardPortrait", portrait),
                ("artifactCardBanner", banner), ("artifactCardRole", role), ("artifactCardLevel", level), ("artifactCardLimit", limit),
                ("artifactCardName", name), ("artifactCardLock", locked), ("artifactCardSelection", selection) };
            var star = DraftSprite("TeamBadges/cm_icon_star");
            for (int i = 0; i < 5; i++)
            {
                keys.Add(("star" + i, Picture(rect, "Rarity star " + i, star, .014f + i * .059f, .787f, .071f, .185f, true)));
                float x = .35f + i * .084f;
                Picture(rect, "Breakthrough slot " + i, SummonArt("arti_gauge_bg"), x, .825f, .088f, .075f, true);
                keys.Add(("break" + i, Picture(rect, "Breakthrough " + i, BagArt("arti_gauge_bar"), x, .825f, .088f, .075f, true)));
            }
            Bind(rect, keys.ToArray());
            card.gameObject.SetActive(false);
            return card.gameObject;
        }

        private static GameText ArtifactLabel(RectTransform parent, string name, string key, float size,
            float x, float y, float width, float height) =>
            Text(parent, name, ArtifactInventoryPrefix + key, size, x, y, width, height, Color.white);

        private static void AddArtifactButton(List<(string Key, Object Target)> bindings, RectTransform parent,
            string key, float x, float y, float width, Sprite sprite)
        {
            float height = parent.name == "Enhancement choices" ? .45f : parent.name == "Artifact detail" ? .09f : .15f;
            var button = Action(parent, key, ArtifactInventoryPrefix + key, 22, x, y, width, height,
                out var label, sprite ? sprite : BagArt("_btn_small_black"));
            bindings.Add(("artifactAction_" + key, button));
            bindings.Add(("artifactActionLabel_" + key, label));
        }
    }
}
