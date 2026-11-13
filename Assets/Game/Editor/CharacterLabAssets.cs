using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AstralOath.Client.CharacterLab;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using YooAsset.Editor;

namespace AstralOath.Editor
{
    public static class CharacterLabAssets
    {
        public const string ManifestPath = "Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json";
        private const string CatalogPath = "Assets/Game/Modules/CharacterLab/Configs/catalog.json";
        private const string LocalizationPath = "Assets/Game/Modules/CharacterLab/Configs/localization.json";
        private const string SpriteBlurMaterialPath = "Assets/Game/Modules/CharacterPresentation/Res/Materials/CharacterSpriteBlur.mat";
        private const string ResourceRoot = "Assets/Game/Modules/CharacterPresentation/Res/";
        private const string ReportPath = ".artifacts/character-import/unity-validation.json";

        [MenuItem("Tools/角色实验室/打开场景", priority = 0)]
        public static void OpenScene()
        {
            if (!File.Exists(CharacterLabSceneBuilder.ScenePath)) { CharacterLabSceneBuilder.CreateScene(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(CharacterLabSceneBuilder.ScenePath);
        }

        [MenuItem("Tools/角色实验室/检查环境", priority = 10)]
        public static void CheckEnvironment()
        {
            foreach (string path in new[] { ManifestPath,
                "Packages/com.astraloath.spine21/UPSTREAM.json", "Packages/com.astraloath.spine38/UPSTREAM.json",
                "Assets/ThirdParty/FMOD/UPSTREAM.json", "Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudio.dll",
                "Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudioL.dll" })
                RequireFile(path);
            Debug.Log("角色实验室：双版本 Spine 与 Windows FMOD 文件齐全。播放验证与原作对照需另行执行。ASTRAL_CHARACTERLAB_ENV_READY");
        }

        [MenuItem("Tools/角色实验室/导入或更新角色资源", priority = 20)]
        public static void ImportCharacters()
        {
            Debug.Log(HotUpdateBuildSettings.RunScript("CharacterAssets/Import.ps1", null, 1800));
            Debug.Log(HotUpdateBuildSettings.RunScript("Config/Generate.ps1", null, 300,
                ("-VerifyAccessors", null), ("-SyncClient", null)));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureCollectors();
            Validate();
        }

        public static void ConfigureCollectors()
        {
            CharacterLabAddressRule.Invalidate();
            var entries = ReadEntries();
            var settings = AssetBundleCollectorSettingData.Setting;
            var package = settings.Packages.SingleOrDefault(p => p.PackageName == "CharacterLab")
                ?? AssetBundleCollectorSettingData.CreatePackage("CharacterLab");
            package.EnableAddressable = true;
            var group = package.Groups.SingleOrDefault(g => g.GroupName == "CharacterLabAssets")
                ?? AssetBundleCollectorSettingData.CreateGroup(package, "CharacterLabAssets");
            group.Collectors.Clear();
            foreach (string path in AddressMap(entries).Keys.OrderBy(p => p, StringComparer.Ordinal))
            {
                RequireFile(path);
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ApplyTextureSettings(path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new BuildFailedException("资源未导入：" + path);
                AssetBundleCollectorSettingData.CreateCollector(group, new AssetBundleCollector
                {
                    CollectPath = path, CollectorGUID = guid,
                    AddressRuleName = nameof(CharacterLabAddressRule),
                    PackRuleName = nameof(CharacterLabPackRule)
                });
            }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/角色实验室/验证资源", priority = 30)]
        public static void Validate()
        {
            CharacterLabAddressRule.Invalidate();
            CheckEnvironment();
            var entries = ReadEntries();
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject entry in entries)
            {
                string path = (string)entry["path"];
                RequireFile(path);
                if (!addresses.Add((string)entry["address"])) throw new BuildFailedException("重复资源地址：" + entry["address"]);
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (actual != (string)entry["outputSha256"]) throw new BuildFailedException("资源哈希不匹配（确认 Git LFS 已拉取）：" + path);
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ValidateTextureSettings(path);
            }
            foreach (JObject entry in entries)
                foreach (string dependency in entry["dependencies"].Values<string>())
                    if (!addresses.Contains(dependency)) throw new BuildFailedException("缺失资源依赖：" + dependency);
            RequireFile(CatalogPath);
            RequireFile(LocalizationPath);
            var resourceTypes = entries.Cast<JObject>().ToDictionary(entry => (string)entry["address"],
                entry => (string)entry["type"], StringComparer.Ordinal);
            var resourcePaths = entries.Cast<JObject>().ToDictionary(entry => (string)entry["address"],
                entry => (string)entry["path"], StringComparer.Ordinal);
            CharacterLabAssetValidation.Validate(File.ReadAllText(CatalogPath), File.ReadAllText(LocalizationPath), resourceTypes,
                address => File.ReadAllText(resourcePaths[address]));
            RequireFile(SpriteBlurMaterialPath);
            var blurMaterial = AssetDatabase.LoadAssetAtPath<Material>(SpriteBlurMaterialPath);
            if (!blurMaterial || !blurMaterial.shader) throw new BuildFailedException("角色实验室模糊材质或 shader 缺失，请重新生成场景。");
            var collected = AssetBundleCollectorSettingData.Setting.BeginCollect("CharacterLab", true, false);
            var collectedAddresses = new HashSet<string>(collected.CollectAssets.Select(a => a.Address), StringComparer.Ordinal);
            foreach (string expected in AddressMap(entries).Values)
                if (!collectedAddresses.Contains(expected)) throw new BuildFailedException("资源包未收集地址：" + expected);
            var report = new JObject { ["utc"] = DateTime.UtcNow.ToString("O"), ["resourceCount"] = entries.Count,
                ["result"] = "passed", ["scope"] = "hashes, Spine 2.1 curve semantic audit markers, dependency closure, catalog schema and typed references, locale keys, texture import, package addresses; playback and source fidelity are separate" };
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log($"ASTRAL_CHARACTERLAB_RESOURCES_VALID count={entries.Count}");
        }

        [MenuItem("Tools/角色实验室/打开验证报告", priority = 50)]
        public static void OpenReport()
        {
            if (!File.Exists(ReportPath)) Validate();
            EditorUtility.RevealInFinder(Path.GetFullPath(ReportPath));
        }

        internal static JArray ReadEntries(string manifestPath = ManifestPath)
        {
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if ((int?)manifest["schemaVersion"] != 1 || manifest["entries"] is not JArray entries || entries.Count == 0)
                throw new BuildFailedException("不支持的角色资源清单。");
            foreach (JObject entry in entries)
            {
                string path = (string)entry["path"], address = (string)entry["address"];
                if (string.IsNullOrEmpty(path) || !path.StartsWith(ResourceRoot, StringComparison.Ordinal) ||
                    path.Contains('\\') || path.Split('/').Any(p => p is "." or "..") ||
                    string.IsNullOrEmpty(address) || !address.StartsWith("characterlab/source/", StringComparison.Ordinal))
                    throw new BuildFailedException("角色清单包含无效路径或地址。");
                ValidateSkeletonConversion(entry);
            }
            return entries;
        }

        internal static void ValidateSkeletonConversion(JObject entry)
        {
            if ((string)entry["type"] != "spine-json" || entry["conversion"] is not JObject conversion ||
                (string)conversion["spineVersion"] != "2.1.27") return;
            var semantics = conversion["curveSemantics"];
            if (semantics?.Type != JTokenType.String || (string)semantics != "scsp-v2-sentinel/1" ||
                conversion["curveTimelines"] is not JObject)
                throw new BuildFailedException("Spine 2.1 曲线语义审计缺失或不兼容（需 curveSemantics=scsp-v2-sentinel/1 且 curveTimelines 为对象），请重新导入：" + entry["path"]);
        }
        internal static Dictionary<string, string> AddressMap(JArray entries)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JObject entry in entries) map.Add((string)entry["path"], (string)entry["address"]);
            map.Add(CharacterLabSceneBuilder.PrefabPath, CharacterLabWindow.PrefabAddress);
            map.Add(CatalogPath, "characterlab/catalog");
            map.Add(LocalizationPath, "characterlab/localization");
            map.Add(SpriteBlurMaterialPath, "characterlab/sprite-blur-material");
            return map;
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path)) throw new BuildFailedException("角色实验室缺少文件：" + path);
        }

        // Imported SCT RGB is already premultiplied; do not bleed/dilate transparent pixels.
        private static void ApplyTextureSettings(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) throw new BuildFailedException("缺失纹理导入器：" + path);
            importer.userData = "AstralOath.ImportOverride";
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true; importer.alphaIsTransparency = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.isReadable = false; importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone",
                overridden = true, maxTextureSize = 8192, format = TextureImporterFormat.RGBA32,
                textureCompression = TextureImporterCompression.Uncompressed });
            importer.SaveAndReimport();
        }

        private static void ValidateTextureSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var platform = importer?.GetPlatformTextureSettings("Standalone");
            if (!importer || importer.userData != "AstralOath.ImportOverride" || !importer.sRGBTexture ||
                importer.alphaIsTransparency || importer.mipmapEnabled || importer.isReadable ||
                importer.npotScale != TextureImporterNPOTScale.None || platform == null || !platform.overridden ||
                platform.format != TextureImporterFormat.RGBA32 || platform.maxTextureSize != 8192)
                throw new BuildFailedException("角色纹理导入设置不符，请重新生成实验室场景：" + path);
        }
    }

    public sealed class CharacterLabAddressRule : IAddressRule
    {
        private static string cachedManifestPath;
        private static DateTime cachedWriteTimeUtc;
        private static long cachedLength;
        private static Dictionary<string, string> cachedAddresses;

        public static void Invalidate()
        {
            cachedAddresses = null;
            cachedManifestPath = null;
        }

        private static Dictionary<string, string> GetAddressMap(string manifestPath)
        {
            string fullPath = Path.GetFullPath(manifestPath);
            var file = new FileInfo(fullPath);
            DateTime writeTimeUtc = file.LastWriteTimeUtc;
            long length = file.Length;
            if (cachedAddresses == null || cachedManifestPath != fullPath ||
                cachedWriteTimeUtc != writeTimeUtc || cachedLength != length)
            {
                // Only replace a cache entry after the new manifest has parsed successfully.
                var addresses = CharacterLabAssets.AddressMap(CharacterLabAssets.ReadEntries(fullPath));
                cachedManifestPath = fullPath;
                cachedWriteTimeUtc = writeTimeUtc;
                cachedLength = length;
                cachedAddresses = addresses;
            }
            return cachedAddresses;
        }

        public string GetAssetAddress(AddressRuleData data)
        {
            if (!GetAddressMap(CharacterLabAssets.ManifestPath).TryGetValue(data.AssetPath, out string address))
                throw new BuildFailedException("未登记的角色资源：" + data.AssetPath);
            return address;
        }
    }
}
