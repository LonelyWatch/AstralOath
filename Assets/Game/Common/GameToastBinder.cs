using System;
using System.Threading.Tasks;
using AstralOath.Client.Core;

namespace AstralOath.Client.Common
{
    public sealed class GameToastBinder
    {
        private static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);
        private readonly GameToast view;
        private BindingScope active;

        public GameToastBinder(GameToast view) => this.view = view ?? throw new ArgumentNullException(nameof(view));

        public void Show(string message, BindingScope owner)
        {
            owner.Token.ThrowIfCancellationRequested();
            Hide();

            var display = new BindingScope(owner.Token);
            active = display;
            display.Add(owner.Add(() =>
            {
                if (active == display) Hide();
            }));
            view.Message.text = message;
            view.Show();
            _ = DismissAfterAsync(display);
        }

        public void Hide()
        {
            var previous = active;
            active = null;
            if (view.Root) view.Hide();
            previous?.Dispose();
        }

        private async Task DismissAfterAsync(BindingScope display)
        {
            try
            {
                await Task.Delay(Duration, display.Token);
                if (active == display) Hide();
            }
            catch (OperationCanceledException) when (display.Token.IsCancellationRequested) { }
        }
    }
}
