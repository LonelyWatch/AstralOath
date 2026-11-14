using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Arena
{
    public static class ArenaLabels
    {
        public const string Address = "arena/zh-hans";
        [Serializable, Preserve] private sealed class Entry { public string key, text; }
        [Serializable, Preserve] private sealed class Source { public Entry[] rows; }

        public static async Task<ILocaleService> LoadAsync(IAssetService assets, BindingScope lifetime, string textLocale, string voiceLocale)
        {
            var asset = await assets.LoadAsync<TextAsset>(Address, lifetime);
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in JsonUtility.FromJson<Source>(asset.text).rows)
                labels.Add(row.key, row.text);
            return new LocaleService(new Dictionary<string, IReadOnlyDictionary<string, string>> { ["zh-Hans"] = labels },
                textLocale, voiceLocale, "ja");
        }

        internal static string[] Rules { get; } = { "wild", "draft", "round", "no_duplicate_class", "basic" };
    }
}
