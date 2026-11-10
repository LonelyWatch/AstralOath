using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class DemoWindow : IClientWindow
    {
        public const string BinderAddress = "foundation/demo-binder";
        public const string ConfigAddress = "foundation/demo-config";
        public const string SampleAddress = "foundation/demo-resource";

        [Serializable]
        private sealed class ConfigRows
        {
            public ConfigRow[] rows;
        }

        [Serializable]
        private sealed class ConfigRow
        {
            public int id;
            public string titleKey;
            public string messageKey;
            public float displaySeconds;
            public string assetAddress;
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
                    throw new InvalidDataException($"Invalid Foundation Lua display key or arguments: {key}.", error);
                }
            }
        }

        private readonly FoundationView view;
        private readonly FoundationLua lua;
        private readonly IAssetService assets;
        private readonly IConfigService config;
        private readonly ILocaleService locale;
        private readonly IEchoService network;
        private readonly IAnimationService animation;
        private readonly CancellationToken owner;
        private BindingScope current;
        private bool disposed;

        public DemoWindow(FoundationView view, FoundationLua lua, IAssetService assets, IConfigService config,
            ILocaleService locale, IEchoService network, IAnimationService animation, CancellationToken owner)
        {
            this.view = view;
            this.lua = lua;
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
                throw new ObjectDisposedException(nameof(DemoWindow));
            if (current != null)
                return;

            var scope = new BindingScope(owner);
            current = scope;
            view.Show();
            view.Title.Bind(locale, scope, "foundation.title");
            view.Status.Bind(locale, scope, "foundation.loading");
            view.CloseLabel.Bind(locale, scope, "foundation.close");
            view.Close.Bind(scope, _ =>
            {
                Close();
                return Task.CompletedTask;
            });

            try
            {
                var result = await ReadOpenModelAsync(assets, config, lua, scope);
                result.title.Bind(view.Title, locale, scope);
                result.description.Bind(view.Description, locale, scope);
                result.status.Bind(view.Status, locale, scope);
                view.EchoLabel.Bind(locale, scope, "foundation.echo");
                view.Echo.Bind(scope, async token => { await EchoAsync(scope, token); });
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch
            {
                if (current == scope)
                    view.Status.Bind(locale, scope, "foundation.load.error");
                throw;
            }
        }

        public static async Task ValidateContentAsync(IAssetService assets, IConfigService config,
            FoundationLua lua, ILocaleService locale, BindingScope scope)
        {
            var model = await ReadOpenModelAsync(assets, config, lua, scope);
            model.title.Validate(locale);
            model.description.Validate(locale);
            model.status.Validate(locale);

            var click = ReadClick(lua);
            click.status.Validate(locale);
            ReadCompletion(lua, new EchoResult(click.message, 1)).Validate(locale);
            ReadClick(lua).status.Validate(locale);
            ReadCompletion(lua, null).Validate(locale);
        }

        private static (Display status, string message) ReadClick(FoundationLua lua)
        {
            object[] result = lua.Call("click");
            string message = StringAt(result, 1);
            ProtobufEchoRequests.ValidateMessage(message);
            return (new Display(StringAt(result, 0)), message);
        }

        private static Display ReadCompletion(FoundationLua lua, EchoResult echo)
        {
            if (echo == null)
                return new Display(StringAt(lua.Call("completed", "error", "", ""), 0));
            var result = lua.Call("completed", "ok", echo.Message, echo.Sequence.ToString(CultureInfo.InvariantCulture));
            return new Display(StringAt(result, 0), StringAt(result, 1), StringAt(result, 2));
        }

        private static async Task<(Display title, Display description, Display status)> ReadOpenModelAsync(
            IAssetService assets, IConfigService config, FoundationLua lua, BindingScope scope)
        {
            var table = await config.ReadAsync<ConfigRows>(ConfigAddress, scope);
            scope.Token.ThrowIfCancellationRequested();
            if (table.rows == null || table.rows.Length != 1 || table.rows[0].id != 1)
                throw new InvalidDataException("Demo config must contain row 1.");
            ConfigRow row = table.rows[0];
            if (row.assetAddress != ConfigAddress || !(row.displaySeconds > 0) || float.IsInfinity(row.displaySeconds))
                throw new InvalidDataException("Demo config references an invalid address or duration.");

            var sample = await assets.LoadAsync<TextAsset>(SampleAddress, scope);
            scope.Token.ThrowIfCancellationRequested();
            var result = lua.Call("open", row.titleKey, row.messageKey, row.displaySeconds, sample.text.Trim());
            return (new Display(StringAt(result, 0)), new Display(StringAt(result, 1)),
                new Display(StringAt(result, 2), StringAt(result, 3), Duration(result)));
        }

        private static double Duration(object[] values)
        {
            double seconds = values.Length > 4 ? values[4] switch
            {
                double number => number,
                int number => number,
                long number => number,
                _ => 0
            } : 0;
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new InvalidDataException("Invalid Foundation Lua duration.");
            return seconds;
        }

        public Task<EchoResult> EchoAsync()
        {
            var scope = current ?? throw new InvalidOperationException("Demo window is closed.");
            return EchoAsync(scope, scope.Token);
        }

        private async Task<EchoResult> EchoAsync(BindingScope scope, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var click = ReadClick(lua);
            click.status.Bind(view.Status, locale, scope);
            animation.Pulse(view.Echo.transform, 0.3f, scope);

            EchoResult result;
            try
            {
                result = await network.EchoAsync(click.message, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                token.ThrowIfCancellationRequested();
                ReadCompletion(lua, null).Bind(view.Status, locale, scope);
                Debug.LogWarning(error.Message);
                return null;
            }

            token.ThrowIfCancellationRequested();
            ReadCompletion(lua, result).Bind(view.Status, locale, scope);
            return result;
        }

        public void Close()
        {
            var scope = current;
            current = null;
            try { scope?.Dispose(); }
            finally { if (view) view.Hide(); }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Close();
        }

        private static string StringAt(object[] values, int index) =>
            index < values.Length && values[index] is string value && !string.IsNullOrWhiteSpace(value)
                ? value : throw new InvalidDataException($"Invalid Foundation Lua value at {index}.");
    }
}