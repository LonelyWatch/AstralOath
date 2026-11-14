using System;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    [Preserve]
    public sealed class FoundationEntry : IHotUpdateEntry
    {
        private const string Demo = "foundation";
        private BindingScope lifetime;
        private Action detach;
        private FoundationWindowBinder window;
        private bool disposed;

        public async Task StartAsync(GameLaunchContext context)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(FoundationEntry));
            if (lifetime != null)
                throw new InvalidOperationException("Foundation entry is already starting or active.");

            context.Lifetime.Token.ThrowIfCancellationRequested();
            var scope = new BindingScope(context.Lifetime.Token);
            lifetime = scope;
            detach = context.Lifetime.Add(Dispose);
            try
            {
                if (context.ShowHome)
                {
                    await StartupNavigation.StartAsync(context, scope);
                    return;
                }
                var services = context.Services;
                var assets = services.Get<IAssetService>();
                var config = services.Get<IConfigService>();
                var animation = services.Get<IAnimationService>();
                var echo = services.Get<IEchoService>();
                var messages = await DemoMessages.LoadAsync(config, scope);
                var locale = new LocaleService(messages, context.TextLocale, context.VoiceLocale, "ja");

                var views = new UIViewRegistry();
                views.Register<FoundationWindow>();
                var view = await views.CreateAsync<FoundationWindow>(assets, context.ViewRoot, scope);
                var windows = new WindowCoordinator(scope.Token);
                scope.Add(windows.Dispose);
                var binder = new FoundationWindowBinder(view, assets, config, locale, echo, animation, scope.Token);
                windows.Register(Demo, binder);
                await windows.OpenAsync(Demo);
                scope.Token.ThrowIfCancellationRequested();

                context.OpenLabel.Bind(locale, scope, FoundationWindowBinder.OpenKey);
                context.OpenButton.Bind(scope, _ => windows.OpenAsync(Demo));
                if (context.Recovered)
                    context.StartupStatus.Bind(locale, scope, FoundationWindowBinder.RecoveredKey);
                else
                    context.StartupStatus.gameObject.SetActive(false);

                // Fixed-seed sample exercises the separately loaded battle assembly at startup.
                Debug.Log($"ASTRAL_BATTLE_SAMPLE {new BattleRandom(1).NextUInt64():x16}");
                services.Add<ILocaleService>(locale);
                window = binder;
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
                throw;
            }
        }

        public Task<EchoResult> EchoAsync() =>
            (window ?? throw new InvalidOperationException("Foundation entry has no active window.")).EchoAsync();

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            window = null;
            detach?.Invoke();
            detach = null;
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
