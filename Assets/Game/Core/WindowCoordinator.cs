using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.Core
{
    public interface IClientWindow : IDisposable
    {
        Task OpenAsync();
        void Close();
    }

    public sealed class WindowCoordinator : IDisposable
    {
        private readonly Dictionary<string, IClientWindow> windows = new Dictionary<string, IClientWindow>(StringComparer.Ordinal);
        private readonly BindingScope lifetime;
        private BindingScope page, overlay;
        private string overlayName;

        public WindowCoordinator(CancellationToken parent) => lifetime = new BindingScope(parent);

        public void Register(string name, IClientWindow window)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            windows.Add(name, window);
            lifetime.Add(window.Dispose);
        }

        public Task OpenAsync(string name)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            return windows[name].OpenAsync();
        }

        public void Close(string name)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            windows[name].Close();
        }

        // Page and overlay bindings are scoped to one opening. All calls occur on the main thread.
        public async Task ShowPageAsync(Func<BindingScope, Task> open)
        {
            if (open == null) throw new ArgumentNullException(nameof(open));
            lifetime.Token.ThrowIfCancellationRequested();
            ClosePage();
            var opening = new BindingScope(lifetime.Token);
            page = opening;
            try { await open(opening); }
            catch
            {
                if (page == opening) ClosePage();
                throw;
            }
        }

        public bool IsOverlayOpen(string name) => overlay != null && overlayName == name;

        public void ShowOverlay(string name, Action<BindingScope> open)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A window name is required.", nameof(name));
            if (open == null) throw new ArgumentNullException(nameof(open));
            lifetime.Token.ThrowIfCancellationRequested();
            if (page == null) throw new InvalidOperationException("An overlay requires an open page.");
            if (IsOverlayOpen(name)) return;
            CloseCurrentOverlay();
            var opening = new BindingScope(page.Token);
            overlay = opening;
            overlayName = name;
            try { open(opening); }
            catch
            {
                if (overlay == opening) CloseCurrentOverlay();
                throw;
            }
        }

        public void CloseOverlay(string name)
        {
            if (IsOverlayOpen(name)) CloseCurrentOverlay();
        }

        private void CloseCurrentOverlay()
        {
            var previous = overlay;
            overlay = null;
            overlayName = null;
            previous?.Dispose();
        }

        private void ClosePage()
        {
            var previous = page;
            page = null;
            try { CloseCurrentOverlay(); }
            finally { previous?.Dispose(); }
        }

        public void Dispose()
        {
            windows.Clear();
            try { ClosePage(); }
            finally { lifetime.Dispose(); }
        }
    }
}