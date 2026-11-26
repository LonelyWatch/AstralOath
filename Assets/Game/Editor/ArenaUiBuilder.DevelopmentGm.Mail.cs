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
        private static void BuildGmMailPanel(RectTransform modal, List<(string Key, Object Target)> keys)
        {
            var panel = Area(modal, "Mail module", .08f, .22f, .84f, .58f);
            keys.Add(("mailPanel", panel.gameObject));
            var recipient = Text(panel, "Current recipient", null, 20, 0, 0, 1, .065f, Mist, TextAlignmentOptions.Center);
            recipient.richText = false; keys.Add(("mailRecipient", recipient));
            var compose = Area(panel, "Mail compose", 0, .08f, 1, .92f);
            keys.Add(("mailCompose", compose.gameObject));
            GameButton Choice(RectTransform parent, string key, string caption, float x, float y, float w, float h, bool confirm = false)
            {
                var button = Action(parent, key, caption == null ? null : "gm.mail." + caption, 23, x, y, w, h, out _, CommonArt(confirm ? "_btn_green" : "_btn_small_black"), Color.white);
                keys.Add((key, button));
                if (confirm) button.FeedbackSound = GameUiFeedback.Sound.Confirm;
                return button;
            }
            TMP_InputField Field(RectTransform parent, string key, string placeholder, int limit, float x, float y, float w, float h, bool number = false)
            {
                var field = Input(parent, key, "gm.mail." + placeholder, limit, x, y, w, h);
                field.richText = false; field.textComponent.richText = false; field.textComponent.fontSize = 23;
                if (number) field.contentType = TMP_InputField.ContentType.IntegerNumber;
                keys.Add((key, field)); return field;
            }
            void List(RectTransform parent, string key, float x, float y, float w, float h)
            {
                var scroll = Scroll(parent, key, x, y, w, h);
                scroll.content.GetComponent<VerticalLayoutGroup>().spacing = 6;
                keys.Add((key, scroll));
            }
            Text(compose, "Templates heading", "gm.mail.templates", 23, 0, 0, .235f, .075f, Mist, TextAlignmentOptions.Center);
            List(compose, "mailTemplates", 0, .09f, .235f, .91f);
            var form = Area(compose, "Draft", .255f, 0, .39f, 1);
            Choice(form, "mailType", null, 0, 0, .48f, .11f);
            Choice(form, "mailSender", null, .52f, 0, .48f, .11f);
            Field(form, "mailTitle", "title", 120, 0, .14f, 1, .10f);
            var body = Field(form, "mailBody", "body", 8000, 0, .27f, 1, .18f);
            body.lineType = TMP_InputField.LineType.MultiLineNewline;
            body.textComponent.enableWordWrapping = true; body.textComponent.alignment = TextAlignmentOptions.TopLeft;
            Text(form, "Expiry label", "gm.mail.expiryLabel", 22, 0, .47f, .57f, .08f, Mist);
            Field(form, "mailExpiry", "expiryInput", 3, .6f, .47f, .4f, .09f, true);
            Text(form, "Attachments heading", "gm.mail.attachments", 22, 0, .58f, 1, .07f, Mist);
            List(form, "mailAttachments", 0, .66f, 1, .21f);
            Choice(form, "mailPreviewMake", "preview", 0, .89f, 1, .11f, true);
            var resources = Area(compose, "Reward catalog", .67f, 0, .33f, 1);
            Choice(resources, "mailCategory", null, 0, 0, 1, .11f);
            Field(resources, "mailSearch", "search", 80, 0, .14f, 1, .10f);
            List(resources, "mailResources", 0, .27f, 1, .50f);
            Field(resources, "mailQuantity", "quantityInput", 10, 0, .81f, .37f, .11f, true);
            Choice(resources, "mailAdd", "add", .41f, .81f, .59f, .11f);
            Text(resources, "Catalog hint", "gm.mail.catalogHint", 18, 0, .94f, 1, .06f, Mist, TextAlignmentOptions.Center);
            var preview = Area(panel, "Server frozen preview", 0, .08f, 1, .92f);
            keys.Add(("mailPreview", preview.gameObject));
            var frozen = Scroll(preview, "Frozen mail", 0, 0, 1, .77f);
            keys.Add(("mailFrozen", frozen));
            var frozenText = Text(frozen.content, "Frozen mail text", null, 23, 0, 0, 1, 1, Color.white, TextAlignmentOptions.TopLeft, true);
            frozenText.richText = false; frozenText.overflowMode = TextOverflowModes.Overflow;
            keys.Add(("mailFrozenText", frozenText));
            var receipt = Text(preview, "Send receipt", null, 20, 0, .79f, 1, .075f, Mist, TextAlignmentOptions.Center, true);
            receipt.richText = false; keys.Add(("mailReceipt", receipt));
            Choice(preview, "mailEdit", "edit", .16f, .89f, .30f, .11f);
            Choice(preview, "mailSend", "send", .54f, .89f, .30f, .11f, true);
            Choice(preview, "mailAnother", "another", .35f, .89f, .30f, .11f, true);
            preview.gameObject.SetActive(false); panel.gameObject.SetActive(false);
        }
    }
}
