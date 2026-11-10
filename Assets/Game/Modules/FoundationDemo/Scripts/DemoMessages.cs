using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    public static class DemoMessages
    {
        public const string Address = "foundation/demo-localization";

        [Serializable, Preserve]
        private sealed class Rows
        {
            [Preserve] public Entry[] rows;
        }

        [Serializable, Preserve]
        private sealed class Entry
        {
            [Preserve] public string key;
            [Preserve] public string zhCn;
        }

        public static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> LoadAsync(
            IConfigService config, BindingScope scope)
        {
            var table = await config.ReadAsync<Rows>(Address, scope);
            scope.Token.ThrowIfCancellationRequested();
            if (table.rows == null || table.rows.Length == 0)
                throw new InvalidDataException("Foundation localization must contain messages.");

            var chinese = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in table.rows)
            {
                if (row == null || row.key == null || !Regex.IsMatch(row.key, @"\A[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)+\z") ||
                    string.IsNullOrWhiteSpace(row.zhCn) || !chinese.TryAdd(row.key, row.zhCn))
                    throw new InvalidDataException($"Invalid or duplicate Foundation localization key: {row?.key}.");
            }
            return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["zh-Hans"] = chinese
            };
        }
    }
}
