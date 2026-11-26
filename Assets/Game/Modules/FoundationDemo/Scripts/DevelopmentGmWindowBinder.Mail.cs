using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class DevelopmentGmWindowBinder
    {
        private MailSession mailSession;
        private DevelopmentGmMailApi mailApi;
        private DevelopmentGmMailIntent mailIntent;
        private GmMailCatalog mailCatalog;
        private GmMailDraft mailDraft = new GmMailDraft();
        private string mailCategory, mailResource;
        private readonly List<(GameButton button, UnityAction action)> mailRows = new List<(GameButton, UnityAction)>();
        private BindingScope mailReading;

        internal void ConfigureMail(MailSession session, DevelopmentGmMailApi api)
        {
            mailSession = session ?? throw new ArgumentNullException(nameof(session));
            mailApi = api ?? throw new ArgumentNullException(nameof(api));
            mailIntent = new DevelopmentGmMailIntent(api);
            // Sending creates a mail and updates only the shared mailbox summary. Claiming owns rewards.
            mailIntent.Sent += receipt => mailSession.ApplySummary(receipt.summary);
        }
        private void OwnMail(BindingScope owner)
        {
            owner.Add(() => { mailIntent?.Dispose(); mailApi?.Dispose(); mailCatalog = null; ClearMailRows(); });
        }
        private string MT(string key, params object[] args) => T("mail." + key, args);
        private void MailStatus(string key, params object[] args) { if (kind == "mail") view.Label("status").text = MT(key, args); }
        private void BindMail(BindingScope scope)
        {
            scope.Add(ClearMailRows);
            if (mailIntent != null)
            {
                void Updated() { if (opening == scope && kind == "mail") DrawMail(); }
                mailIntent.Changed += Updated;
                scope.Add(() => mailIntent.Changed -= Updated);
            }
            void Edit(string key, Action<string> action)
            {
                var field = view.Field(key);
                UnityAction<string> changed = value => { if (mailIntent?.FormLocked != true) action(value); };
                field.onValueChanged.AddListener(changed);
                scope.Add(() => field.onValueChanged.RemoveListener(changed));
            }
            Edit("mailTitle", value => mailDraft.title = value);
            Edit("mailBody", value => mailDraft.body = value);
            Edit("mailExpiry", value => { if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int days)) mailDraft.expiresInDays = days; });
            Edit("mailQuantity", _ => { });
            Edit("mailSearch", _ => { mailResource = null; DrawMail(); });
            Listen(view.Button("mailType"), scope, () =>
            {
                if (mailIntent?.FormLocked != false || mailCatalog == null) return;
                mailDraft.letterType = Next(mailCatalog.letterTypes.Select(row => row.id).ToArray(), mailDraft.letterType); DrawMail();
            });
            Listen(view.Button("mailSender"), scope, () =>
            {
                if (mailIntent?.FormLocked != false || mailCatalog == null) return;
                mailDraft.senderId = Next(mailCatalog.senders.Select(row => row.id).ToArray(), mailDraft.senderId); DrawMail();
            });
            Listen(view.Button("mailCategory"), scope, () =>
            {
                if (mailCatalog == null) return;
                mailCategory = Next(new[] { "all" }.Concat(mailCatalog.resources.Select(row => row.kind)).Concat(new[] { "bundle" }).Distinct().ToArray(), mailCategory); mailResource = null; DrawMail();
            });
            Listen(view.Button("mailAdd"), scope, AddMailAttachment);
            // These regions are hidden within one opening. Their actions belong to the opening,
            // while the login-owned intent supplies submission locking and double-click protection.
            Listen(view.Button("mailPreviewMake"), scope, () => { _ = MakeMailPreviewAsync(scope); });
            Listen(view.Button("mailSend"), scope, () => { _ = SendMailAsync(scope); });
            Listen(view.Button("mailEdit"), scope, () => { if (mailIntent?.CanEdit == true) { mailIntent.EditDraft(); DrawMail(); } });
            Listen(view.Button("mailAnother"), scope, () => { if (mailIntent?.Receipt != null) { mailIntent.SendAnother(); DrawMail(); MailStatus("ready"); } });
            if (mailCatalog == null) _ = LoadMailCatalogAsync(scope);
            else SetMailFields();
        }
        private static string Next(string[] values, string current) => values.Length == 0 ? current : values[(Array.IndexOf(values, current) + 1) % values.Length];
        private async Task LoadMailCatalogAsync(BindingScope scope)
        {
            if (mailApi == null) { MailStatus("unavailable"); return; }
            if (mailReading == scope || mailIntent.IsBusy) return;
            mailReading = scope;
            try
            {
                var catalog = await mailApi.CatalogAsync(scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                if (catalog?.limits == null || catalog.templates == null || catalog.resources == null || catalog.senders?.Length == 0 || catalog.letterTypes?.Length == 0 || catalog.bundles == null)
                    throw new System.IO.InvalidDataException("Incomplete GM mail catalog.");
                mailCatalog = catalog;
                if (mailDraft.letterType == null && catalog.templates.Length > 0) ApplyMailTemplate(catalog.templates[0]);
                else SetMailFields();
                DrawMail(); MailStatus(mailIntent.Uncertain ? "uncertain" : mailIntent.Receipt != null ? "sent" : "ready");
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (GmMailRejectedException error) { if (opening == scope) MailError(error); }
            catch (Exception) { if (opening == scope) MailStatus("offline"); }
            finally { if (mailReading == scope) mailReading = null; }
        }
        private void ApplyMailTemplate(GmMailCatalog.Template template)
        {
            if (mailIntent?.FormLocked != false) return;
            mailDraft = new GmMailDraft
            {
                templateId = template.id, letterType = template.letterType, senderId = template.senderId,
                title = template.title, body = template.body, expiresInDays = template.expiresInDays,
                attachments = (template.attachments ?? Array.Empty<GmMailAttachment>()).Select(row => new GmMailAttachment { kind = row.kind, key = row.key, quantity = row.quantity }).ToArray()
            };
            SetMailFields(); DrawMail();
        }
        private void SetMailFields()
        {
            view.Field("mailTitle").SetTextWithoutNotify(mailDraft.title ?? "");
            view.Field("mailBody").SetTextWithoutNotify(mailDraft.body ?? "");
            view.Field("mailExpiry").SetTextWithoutNotify(mailDraft.expiresInDays > 0 ? mailDraft.expiresInDays.ToString(CultureInfo.InvariantCulture) : "7");
            if (string.IsNullOrEmpty(view.Field("mailQuantity").text)) view.Field("mailQuantity").SetTextWithoutNotify("1");
            if (mailCatalog != null)
            {
                view.Field("mailTitle").characterLimit = mailCatalog.limits.maxTitleLength;
                view.Field("mailBody").characterLimit = mailCatalog.limits.maxBodyLength;
            }
        }
        private void AddMailAttachment()
        {
            if (mailCatalog == null || mailIntent.FormLocked || mailResource == null) return;
            if (!int.TryParse(view.Field("mailQuantity").text, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) || quantity < 1 || quantity > mailCatalog.limits.maxQuantity)
            { MailStatus("quantity", mailCatalog.limits.maxQuantity); return; }
            if (mailDraft.attachments.Length >= mailCatalog.limits.maxAttachments) { MailStatus("attachmentLimit", mailCatalog.limits.maxAttachments); return; }
            var parts = mailResource.Split(':');
            var resource = mailCatalog.resources.FirstOrDefault(row => row.kind == parts[0] && row.key == parts[1]);
            if (resource != null && resource.maxQuantity > 0 && quantity > resource.maxQuantity) { MailStatus("quantity", resource.maxQuantity); return; }
            mailDraft.attachments = mailDraft.attachments.Concat(new[] { new GmMailAttachment { kind = parts[0], key = parts[1], quantity = quantity } }).ToArray();
            DrawMail();
        }
        private async Task MakeMailPreviewAsync(BindingScope scope)
        {
            if (mailCatalog == null || mailIntent.FormLocked) return;
            if (!int.TryParse(view.Field("mailExpiry").text, NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days < 1 || days > mailCatalog.limits.maxExpiryDays)
            { MailStatus("expiry", mailCatalog.limits.maxExpiryDays); return; }
            mailDraft.expiresInDays = days;
            if (string.IsNullOrWhiteSpace(mailDraft.title) || string.IsNullOrWhiteSpace(mailDraft.body)) { MailStatus("required"); return; }
            try
            {
                var task = mailIntent.PreviewAsync(mailDraft, scope.Token); DrawMail(); MailStatus("previewing");
                await task;
                if (opening == scope) { DrawMail(); MailStatus("confirm"); }
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (GmMailRejectedException error) { if (opening == scope) { DrawMail(); MailError(error); } }
            catch (Exception) { if (opening == scope) { DrawMail(); MailStatus("offline"); } }
        }
        private async Task SendMailAsync(BindingScope scope)
        {
            if (mailIntent == null || mailIntent.Preview == null || mailIntent.IsBusy) return;
            try
            {
                var task = mailIntent.SendAsync(scope.Token); DrawMail(); MailStatus("sending");
                await task;
                if (opening == scope) { DrawMail(); MailStatus("sent"); }
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (GmMailRejectedException error) { if (opening == scope) { DrawMail(); MailError(error); } }
            catch (Exception) { if (opening == scope) { DrawMail(); MailStatus("uncertain"); } }
        }
        private void MailError(GmMailRejectedException error)
        {
            if (error.Status == 401) MailStatus("expired");
            else if (error.Status == 404) MailStatus("unavailable");
            else if (error.Code == "mailbox_full") MailStatus("full");
            else if (error.Code == "mail_preview_expired") MailStatus("previewExpired");
            else MailStatus("invalid");
        }
        private void DrawMail()
        {
            if (opening == null) return;
            ClearMailRows();
            bool frozen = mailIntent?.Preview != null;
            bool editable = mailCatalog != null && mailIntent?.FormLocked == false;
            view.MailCompose.SetActive(!frozen); view.MailPreview.SetActive(frozen);
            foreach (var control in view.MailCompose.GetComponentsInChildren<Selectable>(true)) control.interactable = editable;
            view.Button("mailPreviewMake").interactable = editable;
            if (mailCatalog == null) { MailStatus("unavailable"); return; }
            SetCaption("mailType", MT("type", MT("type." + (mailDraft.letterType ?? "standard"))));
            SetCaption("mailSender", MT("sender", mailCatalog.senders.FirstOrDefault(row => row.id == mailDraft.senderId)?.name ?? ""));
            SetCaption("mailCategory", MT("category", MT("kind." + (mailCategory ?? "all"))));
            view.Label("mailRecipient").text = MT("recipient", mailSession?.Summary?.realmId ?? snapshot?.realmId ?? T("unavailable"), mailSession?.Summary?.playerId ?? snapshot?.playerId ?? T("unavailable"));
            if (frozen)
            {
                var preview = mailIntent.Preview; var letter = preview.mail;
                view.Label("mailFrozenText").text = MT("frozen", preview.realmId, preview.playerId, preview.recipientCount,
                    MT("type." + letter.letterType), letter.senderName, letter.title, letter.body, preview.expiresInDays,
                    string.Join("\n", (letter.attachments ?? Array.Empty<MailReward>()).Select(row => row.name + " × " + row.quantity)),
                    string.Join("\n", (preview.totalRewards ?? Array.Empty<MailReward>()).Select(row => row.name + " × " + row.quantity)), preview.previewExpiresAt);
                bool sent = mailIntent.Receipt != null;
                view.Button("mailSend").gameObject.SetActive(!sent);
                view.Button("mailSend").interactable = !mailIntent.IsBusy;
                SetCaption("mailSend", MT(mailIntent.Request == null ? "send" : "retry"));
                view.Button("mailEdit").gameObject.SetActive(!sent);
                view.Button("mailEdit").interactable = mailIntent.CanEdit;
                view.Button("mailAnother").gameObject.SetActive(sent);
                view.Label("mailReceipt").text = sent ? MT("receipt", mailIntent.Receipt.deliveryId) : mailIntent.Uncertain ? MT("uncertain") : "";
                MailStatus(sent ? "sent" : mailIntent.IsBusy ? "sending" : mailIntent.Uncertain ? "uncertain" : "confirm");
                return;
            }
            foreach (var template in mailCatalog.templates)
                MailRow("mailTemplates", MT("template", template.title, MailPurpose(template.purpose)), () => ApplyMailTemplate(template), editable);
            string filter = view.Field("mailSearch").text.Trim();
            foreach (var resource in mailCatalog.resources)
                ResourceRow(resource.kind, resource.key, resource.name);
            foreach (var bundle in mailCatalog.bundles) ResourceRow("bundle", bundle.id, bundle.name);
            void ResourceRow(string category, string key, string name)
            {
                if (mailCategory != null && mailCategory != "all" && mailCategory != category) return;
                if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
                string identity = category + ":" + key;
                MailRow("mailResources", name, () => { mailResource = identity; DrawMail(); }, editable && identity != mailResource);
            }
            for (int index = 0; index < mailDraft.attachments.Length; index++)
            {
                int remove = index; var attachment = mailDraft.attachments[index];
                string name = attachment.kind == "bundle" ? mailCatalog.bundles.FirstOrDefault(row => row.id == attachment.key)?.name :
                    mailCatalog.resources.FirstOrDefault(row => row.kind == attachment.kind && row.key == attachment.key)?.name;
                MailRow("mailAttachments", MT("remove", name ?? attachment.key, attachment.quantity), () =>
                { mailDraft.attachments = mailDraft.attachments.Where((_, i) => i != remove).ToArray(); DrawMail(); }, editable);
            }
            view.Button("mailAdd").interactable = editable && mailResource != null;
            MailStatus(mailIntent.IsBusy ? "previewing" : "ready");
        }
        private void SetCaption(string key, string text) => view.Button(key).GetComponentInChildren<GameText>(true).text = text;
        private string MailPurpose(string purpose)
        {
            try { return MT("purpose." + purpose); }
            catch (KeyNotFoundException) { return MT("purpose.other"); }
        }
        private void MailRow(string list, string label, UnityAction action, bool interactable)
        {
            var row = Object.Instantiate(view.Row, view.MailList(list).content, false);
            var text = row.GetComponentInChildren<GameText>(true); text.richText = false; text.text = label; text.fontSize = 22;
            row.interactable = interactable; row.onClick.AddListener(action); row.gameObject.SetActive(true); mailRows.Add((row, action));
            row.GetComponent<LayoutElement>().preferredHeight = 48;
        }
        private void ClearMailRows()
        {
            foreach (var binding in mailRows)
            {
                var row = binding.button;
                if (!row) continue;
                row.onClick.RemoveListener(binding.action); row.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(row.gameObject); else Object.DestroyImmediate(row.gameObject);
            }
            mailRows.Clear();
        }
    }
}
