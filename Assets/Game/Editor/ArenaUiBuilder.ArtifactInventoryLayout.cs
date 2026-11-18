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

            var frame = Area(root, "Artifact inventory browser", .102f, .197f, .878f, .79f);
            var panel = Paint(frame, BagArt("_box_equip_none"), new Color32(16, 28, 50, 252), false, true);
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
            Paint(detail, BagArt("_box_equip_none"), new Color32(15, 23, 39, 230), false, true);
            var portrait = Paint(Area(detail, "Artifact painting", .045f, .034f, .36f, .39f), null, Color.white);
            portrait.preserveAspect = true;
            bindings.Add(("artifactPortrait", portrait));
            foreach (var label in new[]
            {
                ("artifactTitle", .425f, .075f, .53f, .14f, 30f),
                ("artifactLevel", .425f, .229f, .53f, .08f, 23f),
                ("artifactStats", .425f, .312f, .53f, .07f, 21f),
                ("artifactWearer", .058f, .435f, .887f, .068f, 21f),
                ("artifactSkill", .058f, .52f, .89f, .205f, 21f)
            })
            {
                var text = Text(detail, label.Item1, null, label.Item6, label.Item2, label.Item3, label.Item4,
                    label.Item5, label.Item1 == "artifactTitle" ? Color.white : Mist, wrap: true);
                text.alignment = TextAlignmentOptions.TopLeft;
                text.richText = false;
                bindings.Add((label.Item1, text));
            }

            AddArtifactButton(bindings, detail, "equip", .032f, .761f, .455f, CommonArt("_btn_green"));
            AddArtifactButton(bindings, detail, "unequip", .514f, .761f, .455f, BagArt("_btn_small_black"));
            AddArtifactButton(bindings, detail, "lock", .032f, .867f, .29f, BagArt("_btn_small_black"));
            AddArtifactButton(bindings, detail, "enhance", .341f, .867f, .313f, Art("_btn_blue"));
            AddArtifactButton(bindings, detail, "sell", .674f, .867f, .295f, Art("_btn_gold"));

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
            frame.gameObject.SetActive(false);
        }

        private static GameObject NativeArtifactInventoryCard(RectTransform parent)
        {
            var card = Button(Area(parent, "Native artifact inventory card", 0, 0, 1, 1),
                RosterArt("cm_item_slot_arti_0"), Color.white);
            var selection = Picture((RectTransform)card.transform, "Selected artifact frame", InventoryArt("_tap_select_blue"),
                .015f, .77f, .97f, .21f);
            selection.enabled = false;
            var portrait = Paint(Area(card.image.rectTransform, "Artifact thumbnail", .075f, .038f, .85f, .85f),
                null, Color.white);
            portrait.preserveAspect = true;
            var level = Text(card.image.rectTransform, "Enhancement", null, 21, .50f, .015f, .45f, .17f,
                new Color32(255, 190, 79, 255), TextAlignmentOptions.Right);
            var stars = Text(card.image.rectTransform, "Stars", null, 18, .035f, .77f, .93f, .19f,
                new Color32(255, 203, 82, 255), TextAlignmentOptions.Center);
            var locked = Picture(card.image.rectTransform, "Locked", Art("cm_icon_etclock"), .045f, .012f, .21f, .20f, true);
            Bind((RectTransform)card.transform, ("artifactCardButton", card), ("artifactCardPortrait", portrait),
                ("artifactCardLevel", level), ("artifactCardStars", stars), ("artifactCardLock", locked),
                ("artifactCardSelection", selection));
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
