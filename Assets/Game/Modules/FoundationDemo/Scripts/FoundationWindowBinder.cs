using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class FoundationWindowBinder : IClientWindow
    {
        public const string ConfigAddress = "foundation/demo-config";
        public const string SampleAddress = "foundation/demo-resource";
        private const string EchoMessage = "AstralOath";
        internal const string OpenKey = "foundation.open";
        internal const string RecoveredKey = "foundation.update.recovered";
        private const string CloseKey = "foundation.close";
        private const string EchoKey = "foundation.echo";
        private const string PendingKey = "foundation.pending";

        [Serializable, Preserve]
        private sealed class ConfigRows
        {
            [Preserve] public ConfigRow[] rows;
        }

        [Serializable, Preserve]
        private sealed class ConfigRow
        {
            [Preserve] public int id;
            [Preserve] public string titleKey;
            [Preserve] public string messageKey;
            [Preserve] public int displaySeconds;
            [Preserve] public string assetAddress;
        }

        private sealed class Display
        {
            private readonly string key;
            private readonly object[] args;

            public Display(string key, params object[] args)
            {
                this.key = key;
                this.args = args;
            }

            public void Bind(GameText label, ILocaleService locale, BindingScope scope) =>
                label.Bind(locale, scope, key, args);

            public void Validate(ILocaleService locale)
            {
                try { _ = GameText.Format(locale, key, args); }
                catch (Exception error) when (error is FormatException || error is KeyNotFoundException)
                {
                    throw new InvalidDataException($"Invalid Foundation display key or arguments: {key}.", error);
                }
            }
        }

        private readonly FoundationWindow view;
        private readonly IAssetService assets;
        private readonly IConfigService config;
        private readonly ILocaleService locale;
        private readonly IEchoService network;
        private readonly IAnimationService animation;
        private readonly CancellationToken owner;
        private BindingScope current;
        private Task<EchoResult> echoRequest;
        private bool disposed;

        public FoundationWindowBinder(FoundationWindow view, IAssetService assets, IConfigService config,
            ILocaleService locale, IEchoService network, IAnimationService animation, CancellationToken owner)
        {
            this.view = view;
            this.assets = assets;
            this.config = config;
            this.locale = locale;
            this.network = network;
            this.animation = animation;
            this.owner = owner;
        }

        public async Task OpenAsync()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(FoundationWindowBinder));
            if (current != null)
                return;

            var scope = new BindingScope(owner);
            current = scope;
            try
            {
                ValidateDisplays(locale);
                var result = await ReadOpenModelAsync(assets, config, locale, scope);
                scope.Token.ThrowIfCancellationRequested();
                view.Show();
                result.title.Bind(view.Title, locale, scope);
                result.description.Bind(view.Description, locale, scope);
                result.status.Bind(view.Status, locale, scope);
                view.CloseLabel.Bind(locale, scope, CloseKey);
                view.Close.Bind(scope, _ =>
                {
                    Close();
                    return Task.CompletedTask;
                });
                view.EchoLabel.Bind(locale, scope, EchoKey);
                view.Echo.Bind(scope, token => StartEchoAsync(scope, token));
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch
            {
                if (current == scope)
                    Close();
                throw;
            }
        }

        public static async Task ValidateContentAsync(IAssetService assets, IConfigService config,
            ILocaleService locale, BindingScope scope)
        {
            ValidateDisplays(locale);
            await ReadOpenModelAsync(assets, config, locale, scope);
        }

        private static void ValidateDisplays(ILocaleService locale)
        {
            new Display(OpenKey).Validate(locale);
            new Display(CloseKey).Validate(locale);
            new Display(EchoKey).Validate(locale);
            new Display(RecoveredKey).Validate(locale);
            new Display(PendingKey).Validate(locale);
            ProtobufEchoRequests.ValidateMessage(EchoMessage);
            Completion(new EchoResult(EchoMessage, 1)).Validate(locale);
            Completion(null).Validate(locale);
        }

        private static Display Completion(EchoResult echo) => echo == null
            ? new Display("foundation.echo.error")
            : new Display("foundation.echo.ok", echo.Message, echo.Sequence.ToString(CultureInfo.InvariantCulture));

        private static async Task<(Display title, Display description, Display status)> ReadOpenModelAsync(
            IAssetService assets, IConfigService config, ILocaleService locale, BindingScope scope)
        {
            var table = await config.ReadAsync<ConfigRows>(ConfigAddress, scope);
            scope.Token.ThrowIfCancellationRequested();
            if (table.rows == null || table.rows.Length != 1 || table.rows[0]?.id != 1)
                throw new InvalidDataException("Demo config must contain row 1.");
            ConfigRow row = table.rows[0];
            if (row.assetAddress != ConfigAddress || row.displaySeconds < 1 || row.displaySeconds > 30 ||
                string.IsNullOrWhiteSpace(row.titleKey) || string.IsNullOrWhiteSpace(row.messageKey))
                throw new InvalidDataException("Demo config references an invalid address or duration.");
            var title = new Display(row.titleKey);
            var description = new Display(row.messageKey);
            title.Validate(locale);
            description.Validate(locale);

            var sample = await assets.LoadAsync<TextAsset>(SampleAddress, scope);
            scope.Token.ThrowIfCancellationRequested();
            if (!sample || string.IsNullOrWhiteSpace(sample.text))
                throw new InvalidDataException("Foundation sample text is empty.");
            var status = new Display("foundation.ready", sample.text.Trim(), (double)row.displaySeconds);
            status.Validate(locale);
            return (title, description, status);
        }

        public Task<EchoResult> EchoAsync()
        {
            var scope = current ?? throw new InvalidOperationException("Demo window is closed.");
            return StartEchoAsync(scope, scope.Token);
        }

        private Task<EchoResult> StartEchoAsync(BindingScope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (echoRequest == null || echoRequest.IsCompleted)
                echoRequest = SendEchoAsync(scope, token);
            return echoRequest;
        }

        private async Task<EchoResult> SendEchoAsync(BindingScope scope, CancellationToken token)
        {
            view.Status.Bind(locale, scope, PendingKey);
            animation.Pulse(view.Echo.transform, 0.3f, scope);

            EchoResult result;
            try
            {
                result = await network.EchoAsync(EchoMessage, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                token.ThrowIfCancellationRequested();
                Completion(null).Bind(view.Status, locale, scope);
                Debug.LogWarning(error.Message);
                return null;
            }

            token.ThrowIfCancellationRequested();
            Completion(result).Bind(view.Status, locale, scope);
            return result;
        }

        public void Close()
        {
            var scope = current;
            current = null;
            echoRequest = null;
            try { scope?.Dispose(); }
            finally { if (view.Root) view.Hide(); }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Close();
        }
    }
}
