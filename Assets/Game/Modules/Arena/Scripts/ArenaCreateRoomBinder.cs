using System;
using System.Threading;
using System.Threading.Tasks;
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
        private bool chat;
        private string rule;

        internal ArenaCreateRoomBinder(ArenaCreateRoom view, ILocaleService locale, Func<ArenaCreateRequest, CancellationToken, Task> submit, Action close)
        {
            this.view = view;
            this.locale = locale;
            this.submit = submit;
            this.close = close;
        }

        internal void Open(BindingScope screen)
        {
            view.Show();
            screen.Add(() => { if (view.Root) view.Hide(); });
            view.RoomName.SetTextWithoutNotify("");
            view.Locked.SetIsOnWithoutNotify(false);
            PasswordChanged(false);
            view.Status.text = "";
            SetChat(true);
            SetRule("wild");
            Canvas.ForceUpdateCanvases();
            view.Rules.horizontalNormalizedPosition = 0;
            var locked = view.Locked;
            locked.onValueChanged.AddListener(PasswordChanged);
            screen.Add(() => { if (locked) locked.onValueChanged.RemoveListener(PasswordChanged); });
            view.Allow.Bind(screen, _ => { SetChat(true); return Task.CompletedTask; });
            view.Deny.Bind(screen, _ => { SetChat(false); return Task.CompletedTask; });
            view.Cancel.Bind(screen, Close);
            view.Dismiss.Bind(screen, Close);
            view.Confirm.Bind(screen, Confirm);
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
        }

        private Task Close(CancellationToken _) { close(); return Task.CompletedTask; }

        private async Task Confirm(CancellationToken token)
        {
            string name = view.RoomName.text.Trim();
            string password = view.Locked.isOn ? view.Password.text : "";
            if (name.Length < 1 || name.Length > 28 || (view.Locked.isOn && !ValidPassword(password)))
            {
                view.Status.text = locale.Resolve(name.Length < 1 || name.Length > 28 ? "arena.error.invalid_name" : "arena.error.invalid_password");
                return;
            }
            view.Status.text = "";
            view.Cancel.interactable = view.Dismiss.interactable = false;
            try { await submit(new ArenaCreateRequest { name = name, password = password, rule = rule, allowChat = chat }, token); }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
            finally
            {
                if (view.Root) view.Cancel.interactable = view.Dismiss.interactable = true;
            }
        }

        internal static bool ValidPassword(string value) =>
            value.Length == 4 && Array.TrueForAll(value.ToCharArray(), digit => digit >= '0' && digit <= '9');
    }
}
