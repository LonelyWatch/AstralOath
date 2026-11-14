using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.Arena
{
    internal sealed class ArenaCreateRoomBinder
    {
        private readonly ArenaCreateRoom view;
        private readonly ILocaleService locale;
        private readonly Func<ArenaCreateRequest, CancellationToken, Task> submit;
        private readonly Action close;
        private readonly GameToastBinder toast;
        private BindingScope screen;
        private bool chat, hostFirst, protection;
        private int prebanCount;
        private string rule;

        internal ArenaCreateRoomBinder(ArenaCreateRoom view, ILocaleService locale, Func<ArenaCreateRequest, CancellationToken, Task> submit, Action close, GameToastBinder toast)
        {
            this.view = view;
            this.locale = locale;
            this.submit = submit;
            this.close = close;
            this.toast = toast;
        }

        internal void Open(BindingScope screen)
        {
            this.screen = screen;
            view.Show();
            view.Confirm.FeedbackSound = GameUiFeedback.Sound.Silent;
            screen.Add(() => { if (view.Root) view.Hide(); });
            view.RoomName.SetTextWithoutNotify("");
            view.Locked.SetIsOnWithoutNotify(false);
            PasswordChanged(false);
            view.Status.text = "";
            SetChat(true);
            prebanCount = 2;
            hostFirst = protection = true;
            SetRule("basic");
            UpdateDraftOptions();
            Canvas.ForceUpdateCanvases();
            view.Rules.horizontalNormalizedPosition = 1;
            var locked = view.Locked;
            locked.onValueChanged.AddListener(PasswordChanged);
            screen.Add(() => { if (locked) locked.onValueChanged.RemoveListener(PasswordChanged); });
            view.Allow.Bind(screen, _ => { SetChat(true); return Task.CompletedTask; });
            view.Deny.Bind(screen, _ => { SetChat(false); return Task.CompletedTask; });
            view.Cancel.Bind(screen, Close);
            view.Dismiss.Bind(screen, Close);
            view.Confirm.Bind(screen, Confirm);
            view.DraftOption("prebanCount").Bind(screen, _ => { prebanCount = (prebanCount + 1) % 3; UpdateDraftOptions(); return Task.CompletedTask; });
            view.DraftOption("firstPick").Bind(screen, _ => { hostFirst = !hostFirst; UpdateDraftOptions(); return Task.CompletedTask; });
            view.DraftOption("banProtection").Bind(screen, _ => { protection = !protection; UpdateDraftOptions(); return Task.CompletedTask; });
            foreach (string name in ArenaLabels.Rules)
                view.Rule(name).Bind(screen, _ => { SetRule(name); return Task.CompletedTask; });
        }

        private void PasswordChanged(bool enabled)
        {
            var state = view.PasswordState;
            state.alpha = enabled ? 1f : .30f;
            state.interactable = enabled;
            state.blocksRaycasts = enabled;
            var input = view.Password;
            input.interactable = enabled;
            var fieldColor = input.targetGraphic.color;
            // UGUI blends in linear space. The bright field needs extra attenuation independent of its hint.
            fieldColor.a = enabled ? 1f : .26f;
            input.targetGraphic.color = fieldColor;
            input.placeholder.color = enabled ? new Color32(74, 70, 62, 255) : new Color32(12, 19, 25, 255);
            if (!enabled) input.SetTextWithoutNotify("");
        }

        private void SetChat(bool allowed)
        {
            chat = allowed;
            view.AllowSelection.enabled = allowed;
            view.DenySelection.enabled = !allowed;
            Color muted = new Color32(185, 185, 185, 255);
            view.AllowCaption.color = allowed ? Color.white : muted;
            view.DenyCaption.color = allowed ? muted : Color.white;
        }

        private void SetRule(string value)
        {
            rule = value;
            foreach (string name in ArenaLabels.Rules) view.Selection(name).enabled = value == name;
            view.DraftOptions.SetActive(value == "basic");
            var scroll = (RectTransform)view.Rules.transform;
            var min = scroll.anchorMin;
            min.y = value == "basic" ? .225f : .145f;
            scroll.anchorMin = min;
        }

        private void UpdateDraftOptions()
        {
            view.DraftLabel("prebanLabel").text = GameText.Format(locale, "arena.draft.setting_preban", prebanCount);
            view.DraftLabel("firstPickLabel").text = locale.Resolve(hostFirst ? "arena.draft.setting_host" : "arena.draft.setting_guest");
            view.DraftLabel("protectionLabel").text = locale.Resolve(protection ? "arena.draft.setting_protected" : "arena.draft.setting_unprotected");
        }

        private Task Close(CancellationToken _) { close(); return Task.CompletedTask; }

        private async Task Confirm(CancellationToken token)
        {
            string name = view.RoomName.text.Trim();
            string password = view.Locked.isOn ? view.Password.text : "";
            if (name.Length < 1 || name.Length > 28 || (view.Locked.isOn && !ValidPassword(password)))
            {
                Reject(locale.Resolve(name.Length < 1 || name.Length > 28
                    ? "arena.error.invalid_name" : "arena.error.invalid_password"));
                return;
            }
            view.Status.text = "";
            view.Cancel.interactable = view.Dismiss.interactable = false;
            try { await submit(new ArenaCreateRequest
            {
                name = name, password = password, rule = rule, allowChat = chat, prebanCount = prebanCount,
                firstPick = hostFirst ? "host" : "guest", banProtection = protection
            }, token); }
            catch (ArenaRequestException error) when (!token.IsCancellationRequested)
            {
                Reject(ArenaError.Text(locale, error));
            }
            finally
            {
                if (view.Root) view.Cancel.interactable = view.Dismiss.interactable = true;
            }
        }

        private void Reject(string message)
        {
            GameUiFeedback.Play(view.Confirm, GameUiFeedback.Sound.Blocked);
            toast.Show(message, screen);
        }

        internal static bool ValidPassword(string value) =>
            value.Length == 4 && Array.TrueForAll(value.ToCharArray(), digit => digit >= '0' && digit <= '9');
    }
}
