using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.Bootstrap
{
    public sealed class JsonConfigService : IConfigService
    {
        private readonly IAssetService assets;

        public JsonConfigService(IAssetService assets) => this.assets = assets;

        public async Task<T> ReadAsync<T>(string address, BindingScope scope) where T : class
        {
            using var read = new BindingScope(scope.Token);
            var asset = await assets.LoadAsync<TextAsset>(address, read);
            string json = asset.text.Trim();
            if (json.StartsWith("[", StringComparison.Ordinal))
                json = "{\"rows\":" + json + "}";
            return JsonUtility.FromJson<T>(json) ??
                throw new InvalidDataException($"Invalid configuration {address}.");
        }
    }

    public sealed class VoiceService : IAudioService
    {
        private readonly AudioSource output;
        private readonly IAssetService assets;
        private readonly ILocaleService locale;
        private BindingScope current;

        public VoiceService(AudioSource output, IAssetService assets, ILocaleService locale)
        {
            this.output = output;
            this.assets = assets;
            this.locale = locale;
        }

        public async Task PlayVoiceAsync(string address, BindingScope scope)
        {
            current?.Dispose();
            var voice = new BindingScope(scope.Token);
            current = voice;
            var unregister = scope.Add(voice.Dispose);
            try
            {
                var clip = await assets.LoadAsync<AudioClip>(locale.VoiceLocale + "/" + address, voice);
                output.clip = clip;
                output.Play();
                voice.Add(() =>
                {
                    if (output)
                    {
                        output.Stop();
                        output.clip = null;
                    }
                });

                double deadline = Time.realtimeSinceStartupAsDouble + clip.length + 2;
                while (output && output.isPlaying && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    voice.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }
            finally
            {
                if (current == voice)
                    current = null;
                unregister();
                voice.Dispose();
            }
        }
    }
}