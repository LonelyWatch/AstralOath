using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class DevelopmentGmWindow : UIBaseView
    {
        public const string Address = "development/gm-window";
        public override string PrefabPath => Address;

        internal GameObject Panel => Bindings.Require<GameObject>("panel");
        internal GameObject Editor => Bindings.Require<GameObject>("editor");
        internal GameObject InventoryKinds => Bindings.Require<GameObject>("kinds");
        internal ScrollRect Items => Bindings.Require<ScrollRect>("items");
        internal GameButton Row => Bindings.Require<GameButton>("row");
        internal TMP_InputField Amount => Bindings.Require<TMP_InputField>("amount");
        internal TMP_InputField Filter => Bindings.Require<TMP_InputField>("filter");
        internal GameButton Button(string key) => Bindings.Require<GameButton>(key);
        internal GameText Label(string key) => Bindings.Require<GameText>(key);
        internal GameObject ForgePanel => Bindings.Require<GameObject>("forgePanel");
        internal TMP_InputField Field(string key) => Bindings.Require<TMP_InputField>(key);
        internal CanvasGroup ForgeRow(int index) => Bindings.Require<CanvasGroup>("forgeRow" + index);
        internal GameObject MailPanel => Bindings.Require<GameObject>("mailPanel");
        internal GameObject MailCompose => Bindings.Require<GameObject>("mailCompose");
        internal GameObject MailPreview => Bindings.Require<GameObject>("mailPreview");
        internal ScrollRect MailList(string key) => Bindings.Require<ScrollRect>(key);

        public override void Validate()
        {
            _ = Panel; _ = Editor; _ = InventoryKinds; _ = Items; _ = Row; _ = Amount; _ = Filter;
            foreach (string key in new[] { "entry", "close", "resources", "inventory", "diagnostics", "hero", "artifact",
                "equipment", "submit", "refresh", "copy", "forge", "forgeSlot", "forgeTier", "forgeGrade",
                "forgeSet", "forgeMain", "forgeMake" }) _ = Button(key);
            foreach (string key in new[] { "selection", "status", "report", "submitLabel", "amountLabel" }) _ = Label(key);
            _ = ForgePanel; _ = Field("forgeEnhance"); _ = Field("forgeCount"); _ = Label("forgeBudget");
            for (int index = 0; index < 4; index++)
            {
                _ = ForgeRow(index); _ = Button("forgeSub" + index); _ = Field("forgeRolls" + index);
                _ = Field("forgeValue" + index); _ = Label("forgeRange" + index);
            }
            _ = MailPanel; _ = MailCompose; _ = MailPreview;
            foreach (string key in new[] { "mail", "mailType", "mailSender", "mailCategory", "mailAdd", "mailPreviewMake", "mailSend", "mailEdit", "mailAnother" }) _ = Button(key);
            foreach (string key in new[] { "mailTitle", "mailBody", "mailExpiry", "mailQuantity", "mailSearch" }) _ = Field(key);
            foreach (string key in new[] { "mailTemplates", "mailResources", "mailAttachments", "mailFrozen" }) _ = MailList(key);
            _ = Label("mailFrozenText"); _ = Label("mailRecipient"); _ = Label("mailReceipt");
        }
    }
}
