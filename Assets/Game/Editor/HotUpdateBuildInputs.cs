using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor.Build;
using UnityEngine;

namespace AstralOath.Editor
{
    public static class HotUpdateBuildInputs
    {
        public const string CompletionFile = "baseline.complete.json";

        public static string NewBaselineId() => Guid.NewGuid().ToString("N");

        public static bool FitsWindowsPathBudget(string prefix, int suffixLength) =>
            prefix.Length + suffixLength < 260;

        public static bool IsSafeId(string value) =>
            value != null && Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,63}\z") &&
            !value.EndsWith(".", StringComparison.Ordinal) &&
            !Regex.IsMatch(value, @"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)", RegexOptions.IgnoreCase);

        public static bool SupportsTarget(string platform, string architecture, string variant, string target, string targetVariant)
        {
            if (platform != target || (targetVariant != null && variant != targetVariant))
                return false;
            return platform switch
            {
                "StandaloneWindows64" => architecture == "x64" && variant == "Win64",
                "Android" => architecture == "ARM64" && (variant == "Android-ASTC" || variant == "Android-ETC2"),
                _ => false
            };
        }

        [Serializable]
        private sealed class PlayerScriptingAssemblies
        {
            public string[] names;
        }

        public static void RequireRegisteredScriptAssemblies(string registry,
            IEnumerable<(string Address, string AssetPath, string AssemblyName)> assets)
        {
            string[] names = JsonUtility.FromJson<PlayerScriptingAssemblies>(registry)?.names;
            if (names == null || names.Length == 0)
                throw new BuildFailedException("母包 ScriptingAssemblies.json 缺少程序集登记。");
            var registered = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (var (address, assetPath, assemblyName) in assets)
            {
                string assembly = assemblyName + ".dll";
                if (!registered.Contains(assembly))
                    throw new BuildFailedException(
                        $"母包未登记 {assembly}，无法加载脚本资源 {address} ({assetPath})。");
            }
        }

        public static bool HasCompletedBaseline(string directory) => File.Exists(Path.Combine(directory, CompletionFile));

        public static bool HasMatchingHash(string path, string expected) =>
            File.Exists(path) && string.Equals(Hash(path), expected, StringComparison.Ordinal);

        public static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var input = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
