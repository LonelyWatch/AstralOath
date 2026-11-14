using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;

namespace AstralOath.Client.Arena
{
    internal sealed class ArenaPasswordEntryBinder
    {
        private readonly ArenaPasswordEntry view;
        private readonly ILocaleService locale;
        private readonly GameToastBinder toast;

        internal ArenaPasswordEntryBinder(ArenaPasswordEntry view, ILocaleService locale, GameToastBinder toast)
        {
            this.view = view;
            this.locale = locale;
            this.toast = toast;
        }

        internal void Open(BindingScope scope, Func<string, CancellationToken, Task> submit, Action close)
        {
            var input = view.Input;
            var confirm = view.Confirm;
            var dismiss = view.Dismiss;
            input.SetTextWithoutNotify("");
            view.Show();
            scope.Add(() =>
            {
                if (input) input.DeactivateInputField();
                if (view.Root) view.Hide();
            });

            dismiss.Bind(scope, _ =>
            {
                close();
                return Task.CompletedTask;
            });
            confirm.Bind(scope, async token =>
            {
                if (!ArenaCreateRoomBinder.ValidPassword(input.text))
                {
                    Reject(locale.Resolve("arena.error.invalid_password"), scope);
                    return;
                }

                input.DeactivateInputField();
                input.interactable = dismiss.interactable = false;
                try
                {
                    await submit(input.text, token);
                }
                catch (ArenaRequestException error) when (!scope.Token.IsCancellationRequested)
                {
                    Reject(ArenaError.Text(locale, error), scope);
                }
                finally
                {
                    if (input) input.interactable = true;
                    if (dismiss) dismiss.interactable = true;
                }
            });
        }

        private void Reject(string message, BindingScope scope)
        {
            GameUiFeedback.Play(view.Confirm, GameUiFeedback.Sound.Blocked);
            toast.Show(message, scope);
        }
    }
}
