using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string PasswordEntryPath = Arena + "Res/Prefab/ArenaPasswordEntry.prefab";
        private const string ToastPath = Common + "Prefab/GameToast.prefab";

        private static void BuildPasswordEntryPrefabs()
        {
            var toast = Root("GameToast");
            var overlay = toast.gameObject.AddComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = 120;
            var wrap = Area(toast, "Toast bounds", .02f, .817f, .96f, .128f);
            var notice = Area(wrap, "Notice", 0, 0, 1, 1);
            var aspect = notice.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            var background = Load<Sprite>(Common + "Textures/_cm_popnoti_bg.png");
            aspect.aspectRatio = background.rect.width / background.rect.height;
            Picture(notice, "Background", background, 0, 0, 1, 1);
            var message = Text(notice, "Message", null, 27, .08f, .345f, .84f, .445f,
                new Color32(107, 189, 254, 255), TextAlignmentOptions.Center);
            message.enableAutoSizing = true;
            message.fontSizeMin = 18;
            message.fontSizeMax = 27;
            Bind(toast, ("message", message));
            toast.gameObject.SetActive(false);
            Save(toast.gameObject, ToastPath);

            var root = Root("ArenaPasswordEntry");
            Paint(Area(root, "Modal dim base", 0, 0, 1, 1), null, Black(63f / 255f));
            var dismiss = Button(Area(root, "Dismiss", 0, 0, 1, 1), Art("_dim"), Color.white);
            dismiss.transition = Selectable.Transition.None;
            dismiss.FeedbackSound = GameUiFeedback.Sound.Confirm;
            dismiss.navigation = new Navigation { mode = Navigation.Mode.None };

            var bounds = Area(root, "Password bounds", .03f, .332f, .94f, .292f);
            var panel = Area(bounds, "Password card", 0, 0, 1, 1);
            var fit = panel.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 2.35f;
            var card = Area(panel, "Password focus", 0, 0, 1, 1);
            Paint(card, Art("emoji_picker_frame"), Color.white, true, true);

            Text(card, "Title", "arena.password_title", 44, .06f, .06f, .88f, .21f,
                alignment: TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            SeasonLine(card, "Divider", "_bar_1", .058f, .30f, .442f, .056f, new Color32(158, 125, 72, 190));
            var input = Input(card, "Password", "arena.enter_text", 4, .060f, .412f, .575f, .265f, true);
            FollowKeyboard(card, input);
            var confirm = CommonButton(card, "ConfirmButton", "arena.confirm", .646f, .407f, .305f, .282f);
            confirm.FeedbackSound = GameUiFeedback.Sound.Silent;
            Text(card, "Hint", "arena.enter_password_hint", 27, .06f, .735f, .88f, .18f,
                new Color32(141, 113, 79, 255), TextAlignmentOptions.Center);

            var hint = Area(root, "Dismiss hint", .05f, .939f, .900f, .043f);
            var group = Area(hint, "Centered hint", 0, 0, 1, 1);
            var hintAspect = group.gameObject.AddComponent<AspectRatioFitter>();
            hintAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            hintAspect.aspectRatio = 3.8f;
            Picture(group, "Close icon", Art("cm_icon_etcclose"), .03f, .25f, .15f, .50f, true).color
                = new Color32(135, 135, 135, 255);
            Text(group, "Close caption", "arena.touch_close", 21, .25f, 0, .75f, 1,
                new Color32(176, 176, 176, 255));
            Bind(root, ("input", input), ("confirm", confirm), ("dismiss", dismiss));
            root.gameObject.SetActive(false);
            Save(root.gameObject, PasswordEntryPath);
        }

        private static GameObject CreatePasswordEntry(RectTransform parent) => InstantiateOverlay(PasswordEntryPath, parent);

        private static GameObject CreateDirectoryToast(RectTransform parent) => InstantiateOverlay(ToastPath, parent);

        private static GameObject InstantiateOverlay(string path, RectTransform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(path), parent);
            Place((RectTransform)instance.transform, 0, 0, 1, 1);
            return instance;
        }
    }
}
