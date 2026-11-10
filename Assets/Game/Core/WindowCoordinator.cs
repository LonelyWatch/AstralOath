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

        public void Dispose()
        {
            windows.Clear();
            lifetime.Dispose();
        }
    }
}