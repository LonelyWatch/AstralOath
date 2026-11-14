using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AstralOath.Client.Bootstrap;
using AstralOath.Shared.Updates;
using dnlib.DotNet;
using HybridCLR;
using HybridCLR.Commands;
using HybridCLR.HotUpdate;
using HybridCLR.Installer;
using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace AstralOath.Editor
{
    public static class HotUpdateBuilder
    {
        internal const string Generated = "Assets/Game/HotUpdate/Generated";
        internal const string BuildReceiptName = "package.complete.json";
        private const ApiCompatibilityLevel BuildApiLevel = ApiCompatibilityLevel.NET_Unity_4_8;
        private const string RuntimeVersion = "9.0.1";
        private const string PackageVersion = "9.0.3";
        private const string RuntimeCommit = "83929787581bce6c7219418f62348a10c33582ee";
        private const string Il2CppCommit = "ba3a76c9979d14cee404fb5d44301773e0768866";
        private static readonly string BaselineAsset =
            "Assets/Game/Bootstrap/Resources/" + PlayerBaseline.ResourceName + ".json";

        [Serializable]
        private sealed class AssemblyConfig
        {
            public string[] assemblies;
            public string entryAssembly;
            public string entryType;
        }

        [Serializable]
        private sealed class BaselineArchive
        {
            public string profileId, packageName, entryAssembly, entryType;
            public PlayerBaseline player;
            public string unityVersion;
            public string packageVersion;
            public string runtimeCommit;
            public string il2cppCommit;
            public string variant;
            public string apiCompatibilityLevel;
            public bool development;
            public long contentVersion;
            public HotUpdateFile[] strippedAssemblies;
            public HotUpdateFile[] metadataAssemblies;
        }

        [Serializable]
        internal sealed class PackageReceipt
        {
            public string profileId, packageName, entryAssembly, entryType;
            public string packageDirectory;
            public string baselineId;
            public long contentVersion;
        }

        [MenuItem("Tools/环境/检查")]
        public static void CheckEnvironment()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            CheckInstalled(target);
            ReadAssemblyConfig();
            Debug.Log($"ASTRAL_HYBRIDCLR_READY {Application.unityVersion} HybridCLR {PackageVersion}/{RuntimeVersion} {target}");
        }

        [MenuItem("Tools/环境/从固定源码安装 HybridCLR")]
        public static void Install()
        {
            var options = HotUpdateBuildSettings.Load();
            var path = options.offlinePath;
            if (string.IsNullOrWhiteSpace(path))
                path = HotUpdateBuildSettings.RunScript("Setup-HybridCLR.ps1", null, 900)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Last().Trim();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.Combine(path, "hybridclr")))
                throw new BuildFailedException("先执行 Tools/Setup-HybridCLR.ps1，提供准备好的 libil2cpp 路径。");
            ConfigureHybridCLR(ReadAssemblyConfig());
            new InstallerController().InstallFromLocal(Path.GetFullPath(path));
            CheckInstalled(EditorUserBuildSettings.activeBuildTarget);
        }

        [MenuItem("Tools/强更包/Windows x64")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, 0, true);

        [MenuItem("Tools/强更包/Android ARM64 ASTC")]
        public static void BuildAndroidAstc() => Build(BuildTarget.Android, (int)MobileTextureSubtarget.ASTC, true);

        [MenuItem("Tools/强更包/Android ARM64 ETC2")]
        public static void BuildAndroidEtc2() => Build(BuildTarget.Android, (int)MobileTextureSubtarget.ETC2, true);

        [MenuItem("Tools/热更包/Windows x64")]
        public static void BuildHotWindows() => Build(BuildTarget.StandaloneWindows64, 0, false);

        [MenuItem("Tools/热更包/Android ARM64 ASTC")]
        public static void BuildHotAndroidAstc() => Build(BuildTarget.Android, (int)MobileTextureSubtarget.ASTC, false);

        [MenuItem("Tools/热更包/Android ARM64 ETC2")]
        public static void BuildHotAndroidEtc2() => Build(BuildTarget.Android, (int)MobileTextureSubtarget.ETC2, false);

        [MenuItem("Tools/检查母包兼容性")]
        public static void CheckCompatibility()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var options = HotUpdateBuildSettings.Load();
            var profile = CharacterLabBuildProfile.Foundation;
            var config = ReadAssemblyConfig(profile);
            var archive = ReadBaseline(profile, options, target, null);
            string compiled = Path.Combine("Temp", "AstralOath", Guid.NewGuid().ToString("N"));
            try
            {
                CompileHotAssemblies(compiled, target, archive.development);
                string aot = AotDirectory(options.baselinePath);
                CheckAssemblies(profile, config, compiled, aot);
                RequiredHotMetadata(compiled, config, aot, archive);
                Debug.Log("ASTRAL_COMPATIBILITY_OK " + archive.player.baselineId);
            }
            finally
            {
                if (Directory.Exists(compiled))
                    Directory.Delete(compiled, true);
            }
        }

        private static void Build(BuildTarget target, int subtarget, bool full) =>
            Build(CharacterLabBuildProfile.Foundation, target, subtarget, full);

        internal static void Build(CharacterLabBuildProfile profile, BuildTarget target, int subtarget, bool full)
        {
            RequireActiveTarget(target);
            var options = profile.LoadSettings();
            var config = ReadAssemblyConfig(profile);
            string variant = Variant(target, subtarget);
            ValidateOptions(options, full);
            var archive = full ? null : ReadBaseline(profile, options, target, variant);
            if (full)
                CheckInstalled(target);
            else if (options.contentVersion <= archive.contentVersion)
                throw new BuildFailedException("热更资源版本必须高于母包内置版本。");
            if (profile == CharacterLabBuildProfile.Foundation)
            {
                FoundationBuilder.ValidateDemo();
                ArenaUiBuilder.Validate();
            }
            else CharacterLabBuild.Validate();

            string output = Path.GetFullPath(Path.Combine(options.outputRoot, variant,
                (full ? "player" : "hot") + "-v" + options.contentVersion.ToString(CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N")));
            string bundleCachePrefix = Path.Combine(output, "bundles", target.ToString(),
                profile.PackageName, "OutputCache") + Path.DirectorySeparatorChar;
            if (Application.platform == RuntimePlatform.WindowsEditor &&
                !HotUpdateBuildInputs.FitsWindowsPathBudget(bundleCachePrefix, 64))
                throw new BuildFailedException("输出目录太长，YooAsset 需要在其下创建缓存文件。请选择较短的目录，例如 .artifacts/b：" + output);
            Directory.CreateDirectory(output);
            bool development = full ? options.development : archive.development;
            var previousTexture = EditorUserBuildSettings.androidBuildSubtarget;
            try
            {
                if (target == BuildTarget.Android)
                    EditorUserBuildSettings.androidBuildSubtarget = (MobileTextureSubtarget)subtarget;
                PackageReceipt receipt;
                if (full)
                    receipt = BuildPlayer(profile, target, variant, options, config, output, development);
                else
                    receipt = BuildHotPackage(profile, target, options, config, output, archive);

                WriteJson(Path.Combine(output, BuildReceiptName), receipt);
                if (full)
                {
                    profile.RememberBaseline(receipt.baselineId, output);
                    Debug.Log("ASTRAL_PLAYER_BASELINE " + receipt.baselineId);
                }
                if (profile == CharacterLabBuildProfile.Foundation && !string.IsNullOrEmpty(options.signingKey))
                    HotUpdateBuildSettings.Sign(output, options);
                Debug.Log("ASTRAL_BUILD_OK " + output);
            }
            catch (Exception ex)
            {
                Debug.LogError($"AstralOath 构建或签名失败，产物目录 {output}\n{ex.Message}");
                throw;
            }
            finally
            {
                EditorUserBuildSettings.androidBuildSubtarget = previousTexture;
                AssetDatabase.DeleteAsset(Generated);
            }
        }

        private static PackageReceipt BuildHotPackage(CharacterLabBuildProfile profile, BuildTarget target, HotUpdateBuildSettings options,
            AssemblyConfig config, string output, BaselineArchive archive)
        {
            string compiled = Path.Combine(output, "compiled");
            CompileHotAssemblies(compiled, target, archive.development);
            string aot = AotDirectory(options.baselinePath);
            var assemblies = CheckAssemblies(profile, config, compiled, aot);
            return BuildPackage(profile, output, target, options.contentVersion, archive.player, config, assemblies,
                compiled, aot, RequiredHotMetadata(compiled, config, aot, archive), false);
        }

        private static PackageReceipt BuildPlayer(CharacterLabBuildProfile profile, BuildTarget target, string variant, HotUpdateBuildSettings options,
            AssemblyConfig config, string output, bool development)
        {
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            var hybridClrScope = new CharacterLabBuildSettingsScope();
            byte[] previousBaseline = File.Exists(BaselineAsset) ? File.ReadAllBytes(BaselineAsset) : null;
            bool previousBaselineMeta = File.Exists(BaselineAsset + ".meta");
            string previousPlayerPath = FoundationStreamingAssets.PlayerPath;
            string previousStagedRoot = FoundationStreamingAssets.StagedRoot;
            string previousPackage = FoundationStreamingAssets.PackageName;
            bool previousInjected = FoundationStreamingAssets.Injected;
            var previousBackend = PlayerSettings.GetScriptingBackend(namedTarget);
            var previousApi = PlayerSettings.GetApiCompatibilityLevel(namedTarget);
            var previousArchitectures = PlayerSettings.Android.targetArchitectures;
            var previousMinSdk = PlayerSettings.Android.minSdkVersion;
            var previousAppVersion = PlayerSettings.bundleVersion;
            var previousVersionCode = PlayerSettings.Android.bundleVersionCode;
            var previousOrientation = PlayerSettings.defaultInterfaceOrientation;
            var previousInsecureHttp = PlayerSettings.insecureHttpOption;
            var previousDevelopment = EditorUserBuildSettings.development;
            var previousScenes = EditorBuildSettings.scenes;
            var previousAppBundle = EditorUserBuildSettings.buildAppBundle;
            var previousExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            const string createSolution = "CreateSolution";
            var previousWindowsSolution = target == BuildTarget.StandaloneWindows64
                ? EditorUserBuildSettings.GetPlatformSettings("Standalone", createSolution) : null;
            var player = new PlayerBaseline
            {
                formatVersion = 1,
                baselineId = HotUpdateBuildInputs.NewBaselineId(),
                platform = target.ToString(),
                architecture = target == BuildTarget.Android ? "ARM64" : "x64",
                runtimeVersion = RuntimeVersion,
                appVersion = options.appVersion
            };

            try
            {
                ConfigureHybridCLR(config);
                PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(namedTarget, BuildApiLevel);
                PlayerSettings.bundleVersion = options.appVersion;
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
                EditorUserBuildSettings.development = development;
                var scenePaths = profile == CharacterLabBuildProfile.Foundation
                    ? new[] { profile.ScenePath, "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity" }
                    : new[] { profile.ScenePath };
                EditorBuildSettings.scenes = scenePaths.Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
                if (options.developmentHttp)
                    PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
                if (target == BuildTarget.StandaloneWindows64)
                    EditorUserBuildSettings.SetPlatformSettings("Standalone", createSolution, "false");
                if (target == BuildTarget.Android)
                {
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                    PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
                    PlayerSettings.Android.bundleVersionCode = options.androidVersionCode;
                    EditorUserBuildSettings.buildAppBundle = false;
                    EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                }

                WriteJson(BaselineAsset, player);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string compiled = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
                foreach (string name in config.assemblies)
                {
                    string prior = Path.Combine(compiled, name + ".dll");
                    if (File.Exists(prior))
                        File.Delete(prior);
                }
                PrebuildCommand.GenerateAll();

                string generatedAot = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
                var assemblies = CheckAssemblies(profile, config, compiled, generatedAot);
                var metadataNames = HotUpdateAotMetadata.GetRequiredNames(compiled, config.assemblies, generatedAot,
                    Directory.EnumerateFiles(generatedAot, "*.dll").Select(Path.GetFileNameWithoutExtension), new[] { "mscorlib" });
                var metadata = ReadMetadata(generatedAot, metadataNames);
                var receipt = BuildPackage(profile, output, target, options.contentVersion, player, config, assemblies,
                    compiled, generatedAot, metadataNames, true);
                var playerFile = Path.Combine(output, profile.ExecutableName + (target == BuildTarget.Android ? ".apk" : ".exe"));
                FoundationStreamingAssets.PlayerPath = playerFile;
                FoundationStreamingAssets.PackageName = profile.PackageName;
                FoundationStreamingAssets.Injected = false;
                FoundationStreamingAssets.StagedRoot = Path.Combine(output, "embedded");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    subtarget = target == BuildTarget.StandaloneWindows64 ? (int)StandaloneBuildSubtarget.Player : 0,
                    scenes = scenePaths,
                    locationPathName = playerFile,
                    options = development ? BuildOptions.Development : BuildOptions.None
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded ||
                    !File.Exists(playerFile) || !FoundationStreamingAssets.Injected)
                    throw new BuildFailedException($"IL2CPP 母包构建失败（{report.summary.result}）。");

                // HybridCLR resets the stripped directory at the start of each real player build.
                RequireMatchingMetadata(generatedAot, metadata);
                CheckAssemblies(profile, config, compiled, generatedAot);
                var stripped = ArchiveStripped(generatedAot, AotDirectory(output), config.assemblies);
                RequireMatchingMetadata(AotDirectory(output), metadata);
                WriteJson(Path.Combine(output, "player-baseline.json"), player);
                WriteJson(Path.Combine(output, HotUpdateBuildInputs.CompletionFile), new BaselineArchive
                {
                    profileId = profile.Id, packageName = profile.PackageName,
                    entryAssembly = config.entryAssembly, entryType = config.entryType,
                    player = player,
                    unityVersion = Application.unityVersion,
                    packageVersion = PackageVersion,
                    runtimeCommit = RuntimeCommit,
                    il2cppCommit = Il2CppCommit,
                    variant = variant,
                    apiCompatibilityLevel = BuildApiLevel.ToString(),
                    development = development,
                    contentVersion = options.contentVersion,
                    strippedAssemblies = stripped,
                    metadataAssemblies = metadata
                });
                return receipt;
            }
            finally
            {
                try
                {
                    FoundationStreamingAssets.StagedRoot = previousStagedRoot;
                    FoundationStreamingAssets.PlayerPath = previousPlayerPath;
                    FoundationStreamingAssets.PackageName = previousPackage;
                    FoundationStreamingAssets.Injected = previousInjected;
                    PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
                    PlayerSettings.SetApiCompatibilityLevel(namedTarget, previousApi);
                    PlayerSettings.Android.targetArchitectures = previousArchitectures;
                    PlayerSettings.Android.minSdkVersion = previousMinSdk;
                    PlayerSettings.Android.bundleVersionCode = previousVersionCode;
                    PlayerSettings.bundleVersion = previousAppVersion;
                    PlayerSettings.defaultInterfaceOrientation = previousOrientation;
                    PlayerSettings.insecureHttpOption = previousInsecureHttp;
                    EditorUserBuildSettings.development = previousDevelopment;
                    EditorBuildSettings.scenes = previousScenes;
                    EditorUserBuildSettings.buildAppBundle = previousAppBundle;
                    EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExport;
                    if (target == BuildTarget.StandaloneWindows64)
                        EditorUserBuildSettings.SetPlatformSettings("Standalone", createSolution, previousWindowsSolution ?? "");
                }
                finally
                {
                    try
                    {
                        if (previousBaseline != null) File.WriteAllBytes(BaselineAsset, previousBaseline);
                        else if (previousBaselineMeta) File.Delete(BaselineAsset);
                        else AssetDatabase.DeleteAsset(BaselineAsset);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    }
                    finally { hybridClrScope.Dispose(); }
                }
            }
        }

        private static void CompileHotAssemblies(string destination, BuildTarget target, bool development)
        {
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            var previousApi = PlayerSettings.GetApiCompatibilityLevel(namedTarget);
            try
            {
                PlayerSettings.SetApiCompatibilityLevel(namedTarget, BuildApiLevel);
                CompileDllCommand.CompileDll(destination, target, development);
            }
            finally
            {
                PlayerSettings.SetApiCompatibilityLevel(namedTarget, previousApi);
            }
        }

        private static HotUpdateAssembly[] CheckAssemblies(CharacterLabBuildProfile profile, AssemblyConfig config, string compiled, string aot)
        {
            var checker = new MissingMetadataChecker(aot, config.assemblies);
            var result = new List<HotUpdateAssembly>();
            foreach (string name in config.assemblies)
            {
                string path = Path.Combine(compiled, name + ".dll");
                if (!File.Exists(path) || !checker.Check(path))
                    throw new BuildFailedException($"热更程序集与母包 AOT 不兼容或未生成：{name}。");
                using var module = ModuleDefMD.Load(File.ReadAllBytes(path));
                if (module.Assembly?.Name?.String != name)
                    throw new BuildFailedException("热更程序集身份与配置不一致：" + name);
                if (name == config.entryAssembly && !module.GetTypes().Any(profile.AcceptsEntry))
                    throw new BuildFailedException("热更入口须有公共无参构造函数并实现 " + profile.EntryContract + "：" + config.entryType);
                result.Add(new HotUpdateAssembly
                {
                    name = name,
                    address = "code/hot/" + name,
                    size = new FileInfo(path).Length,
                    sha256 = HotUpdateBuildInputs.Hash(path),
                    dependencies = module.GetAssemblyRefs().Select(reference => reference.Name.String)
                        .Where(config.assemblies.Contains).Distinct(StringComparer.Ordinal).ToArray()
                });
            }
            return result.ToArray();
        }

        private static string[] RequiredHotMetadata(string compiled, AssemblyConfig config, string aot, BaselineArchive archive) =>
            HotUpdateAotMetadata.GetRequiredNames(compiled, config.assemblies, aot,
                archive.strippedAssemblies.Select(item => item.name), archive.metadataAssemblies.Select(item => item.name));

        private static HotUpdateFile[] ReadMetadata(string folder, IEnumerable<string> names) =>
            names.Select(name => DescribeAot(Path.Combine(folder, name + ".dll"))).ToArray();

        private static HotUpdateFile DescribeAot(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!HotUpdateBuildInputs.IsSafeId(name) || !File.Exists(path))
                throw new BuildFailedException("缺少有效的 AOT DLL：" + path);
            return new HotUpdateFile
            {
                name = name,
                address = "code/aot/" + name,
                size = new FileInfo(path).Length,
                sha256 = HotUpdateBuildInputs.Hash(path)
            };
        }

        private static void RequireMatchingMetadata(string aot, IEnumerable<HotUpdateFile> files)
        {
            foreach (var file in files)
            {
                string path = Path.Combine(aot, file.name + ".dll");
                if (!HotUpdateBuildInputs.IsSafeId(file.name) || !HotUpdateBuildInputs.HasMatchingHash(path, file.sha256) ||
                    new FileInfo(path).Length != file.size)
                    throw new BuildFailedException("母包 AOT DLL 缺失或与记录不符：" + file.name);
            }
        }

        private static HotUpdateFile[] ArchiveStripped(string source, string destination, string[] hotAssemblies)
        {
            Directory.CreateDirectory(destination);
            var files = Directory.GetFiles(source, "*.dll")
                .OrderBy(path => path, StringComparer.Ordinal).Select(DescribeAot).ToArray();
            if (files.Length == 0 || files.Any(file => hotAssemblies.Contains(file.name)))
                throw new BuildFailedException("实际 IL2CPP 裁剪结果缺失或包含热更程序集。");
            foreach (var file in files)
                File.Copy(Path.Combine(source, file.name + ".dll"), Path.Combine(destination, file.name + ".dll"));
            RequireMatchingMetadata(destination, files);
            return files;
        }

        internal static string ReadArchivedBaselineId(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !HotUpdateBuildInputs.HasCompletedBaseline(directory))
                return "";
            try
            {
                string path = Path.Combine(directory, "player-baseline.json");
                return JsonUtility.FromJson<PlayerBaseline>(File.ReadAllText(path))?.baselineId ?? "";
            }
            catch (IOException)
            {
                return "";
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        private static BaselineArchive ReadBaseline(CharacterLabBuildProfile profile, HotUpdateBuildSettings options, BuildTarget target, string variant)
        {
            if (string.IsNullOrWhiteSpace(options.baselinePath) ||
                !HotUpdateBuildInputs.HasCompletedBaseline(options.baselinePath) ||
                !File.Exists(Path.Combine(options.baselinePath, BuildReceiptName)))
                throw new BuildFailedException("请选择已完成的母包归档。");
            var archive = JsonUtility.FromJson<BaselineArchive>(
                File.ReadAllText(Path.Combine(options.baselinePath, HotUpdateBuildInputs.CompletionFile)));
            var receipt = JsonUtility.FromJson<PackageReceipt>(File.ReadAllText(Path.Combine(options.baselinePath, BuildReceiptName)));
            if (archive == null || receipt == null ||
                !profile.AcceptsBaseline(archive.profileId, archive.packageName, archive.entryAssembly, archive.entryType) ||
                !profile.AcceptsBaseline(receipt.profileId, receipt.packageName, receipt.entryAssembly, receipt.entryType) ||
                receipt.baselineId != archive.player?.baselineId || receipt.contentVersion != archive.contentVersion)
                throw new BuildFailedException("母包归档与当前构建 profile 或包回执不一致。");
            var player = archive.player;
            if (player == null || player.formatVersion != 1 || !Guid.TryParseExact(player.baselineId, "N", out _) ||
                (options.expectedBaselineId != null && player.baselineId != options.expectedBaselineId) ||
                !HotUpdateBuildInputs.SupportsTarget(player.platform, player.architecture, archive.variant, target.ToString(), variant) ||
                player.runtimeVersion != RuntimeVersion || archive.packageVersion != PackageVersion ||
                archive.unityVersion != Application.unityVersion || archive.runtimeCommit != RuntimeCommit ||
                archive.il2cppCommit != Il2CppCommit || archive.apiCompatibilityLevel != BuildApiLevel.ToString() ||
                archive.contentVersion <= 0 ||
                archive.strippedAssemblies == null || archive.strippedAssemblies.Length == 0 ||
                archive.metadataAssemblies == null || archive.metadataAssemblies.Length == 0 ||
                File.ReadAllText(Path.Combine(options.baselinePath, "player-baseline.json")) != JsonUtility.ToJson(player, true))
                throw new BuildFailedException("母包 ID、平台、纹理变体或工具链与当前构建不兼容。");
            string aot = AotDirectory(options.baselinePath);
            RequireMatchingMetadata(aot, archive.strippedAssemblies);
            RequireMatchingMetadata(aot, archive.metadataAssemblies);
            if (!Directory.GetFiles(aot, "*.dll").Select(Path.GetFileNameWithoutExtension)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(archive.strippedAssemblies.Select(item => item.name)))
                throw new BuildFailedException("母包 AOT 目录包含未归档的 DLL。");
            return archive;
        }

        private static PackageReceipt BuildPackage(CharacterLabBuildProfile profile, string output, BuildTarget target, long contentVersion,
            PlayerBaseline player, AssemblyConfig config, HotUpdateAssembly[] assemblies,
            string compiled, string aot, IEnumerable<string> metadataNames, bool embed)
        {
            string packageVersion = profile.VersionPrefix + contentVersion.ToString(CultureInfo.InvariantCulture);
            var manifest = new HotUpdateManifest
            {
                formatVersion = 1,
                baselineId = player.baselineId,
                platform = player.platform,
                architecture = player.architecture,
                entryAssembly = config.entryAssembly,
                entryType = config.entryType,
                assemblies = assemblies,
                aotMetadata = ReadMetadata(aot, metadataNames)
            };
            manifest.Validate(player.baselineId, player.platform, player.architecture);
            AssetDatabase.DeleteAsset(Generated);
            WriteJson(Generated + "/manifest.json", manifest);
            foreach (var item in assemblies)
                CopyCode(item, compiled, "hot");
            foreach (var item in manifest.aotMetadata)
                CopyCode(item, aot, "aot");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string sourceSnapshot = Path.Combine(output, "sources", Generated);
            foreach (string file in Directory.GetFiles(Generated, "*", SearchOption.AllDirectories)
                         .Where(file => !file.EndsWith(".meta", StringComparison.Ordinal)))
            {
                string destination = Path.Combine(sourceSnapshot, file.Substring(Generated.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination);
            }

            string bundleRoot = Path.Combine(output, "bundles");
            string embeddedRoot = Path.Combine(output, "embedded");
            var package = AssetBundleCollectorSettingData.Setting.Packages.Single(entry => entry.PackageName == profile.PackageName);
            var collector = new AssetBundleCollectorGroup { GroupName = "HotUpdate" };
            collector.Collectors.Add(new AssetBundleCollector
            {
                CollectPath = Generated,
                CollectorGUID = AssetDatabase.AssetPathToGUID(Generated),
                AddressRuleName = nameof(HotUpdateAddressRule),
                PackRuleName = nameof(HotUpdatePackRule)
            });
            package.Groups.Add(collector);
            try
            {
                var result = new BuiltinBuildPipeline().Run(new BuiltinBuildParameters
                {
                    BuildOutputRoot = bundleRoot,
                    BuildinFileRoot = embeddedRoot,
                    BuildPipeline = EBuildPipeline.BuiltinBuildPipeline.ToString(),
                    BuildBundleType = (int)EBuildBundleType.AssetBundle,
                    BuildTarget = target,
                    PackageName = profile.PackageName,
                    PackageVersion = packageVersion,
                    CompressOption = ECompressOption.LZ4,
                    BuildinFileCopyOption = embed ? EBuildinFileCopyOption.ClearAndCopyAll : EBuildinFileCopyOption.None,
                    VerifyBuildingResult = true
                }, false);
                if (!result.Success)
                    throw new BuildFailedException(result.ErrorInfo);
                Debug.Log("ASTRAL_YOOASSET_PACKAGE " + result.OutputPackageDirectory);
                return new PackageReceipt
                {
                    profileId = profile.Id, packageName = profile.PackageName,
                    entryAssembly = config.entryAssembly, entryType = config.entryType,
                    packageDirectory = Path.GetRelativePath(output, result.OutputPackageDirectory).Replace('\\', '/'),
                    baselineId = player.baselineId,
                    contentVersion = contentVersion
                };
            }
            finally
            {
                package.Groups.Remove(collector);
            }
        }

        private static void CopyCode(HotUpdateFile item, string directory, string kind)
        {
            string path = $"{Generated}/{kind}/{item.name}.bytes";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, item.name + ".dll"));
            HotUpdateManifest.VerifyFile(item, bytes);
            File.WriteAllBytes(path, bytes);
        }

        private static AssemblyConfig ReadAssemblyConfig(CharacterLabBuildProfile profile = null)
        {
            profile = profile ?? CharacterLabBuildProfile.Foundation;
            var config = JsonUtility.FromJson<AssemblyConfig>(File.ReadAllText(profile.AssemblyConfigPath));
            if (config?.assemblies == null || config.assemblies.Length == 0 ||
                config.assemblies.Any(name => !HotUpdateBuildInputs.IsSafeId(name)) ||
                config.assemblies.Distinct(StringComparer.Ordinal).Count() != config.assemblies.Length ||
                !config.assemblies.Contains(config.entryAssembly) || config.entryAssembly != profile.EntryAssembly || config.entryType != profile.EntryType)
                throw new BuildFailedException(profile.AssemblyConfigPath + " 无效或入口不属于当前 profile。");
            return config;
        }

        private static void ConfigureHybridCLR(AssemblyConfig config)
        {
            var settings = HybridCLRSettings.instance;
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            settings.hotUpdateAssemblyDefinitions = Array.Empty<UnityEditorInternal.AssemblyDefinitionAsset>();
            settings.hotUpdateAssemblies = config.assemblies;
            settings.preserveHotUpdateAssemblies = Array.Empty<string>();
            HybridCLRSettings.Save();
        }

        private static void CheckInstalled(BuildTarget target)
        {
            var installer = new InstallerController();
            if (installer.PackageVersion != PackageVersion || installer.HybridclrLocalVersion != "v" + RuntimeVersion ||
                !installer.HasInstalledHybridCLR() || installer.InstalledLibil2cppVersion != PackageVersion)
                throw new BuildFailedException("未安装项目所需的 HybridCLR 9.0.3 / 运行时 9.0.1。");
            if ((target != BuildTarget.Android && target != BuildTarget.StandaloneWindows64) ||
                !BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new BuildFailedException("当前平台未安装所需的 Unity 构建模块：" + target);
        }

        private static void RequireActiveTarget(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;
            if (!Application.isBatchMode && EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(target), target))
                return;
            throw new BuildFailedException($"无法切换到 {target}，请确认已安装对应模块并使用 Tools/Invoke-Unity.ps1 的对应模式。");
        }

        private static void ValidateOptions(HotUpdateBuildSettings options, bool full)
        {
            if (options.contentVersion <= 0 || string.IsNullOrWhiteSpace(options.outputRoot))
                throw new BuildFailedException("构建需要正数资源版本及输出目录。");
            if (full && options.expectedBaselineId != null)
                throw new BuildFailedException("强更包会生成新的母包 ID。请移除 -BaselineId。");
            if (full && (!HotUpdateBuildInputs.IsSafeId(options.appVersion) || options.androidVersionCode <= 0))
                throw new BuildFailedException("强更包需要有效应用版本及正数 Android versionCode。");
        }

        private static string Variant(BuildTarget target, int subtarget)
        {
            if (target == BuildTarget.StandaloneWindows64)
                return "Win64";
            if (target == BuildTarget.Android && subtarget == (int)MobileTextureSubtarget.ASTC)
                return "Android-ASTC";
            if (target == BuildTarget.Android && subtarget == (int)MobileTextureSubtarget.ETC2)
                return "Android-ETC2";
            throw new BuildFailedException("只支持 Win64、Android ASTC 和 Android ETC2。");
        }

        private static string AotDirectory(string archive) => Path.Combine(archive, "stripped-aot");

        private static void WriteJson<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }
    }

    public sealed class HotUpdateAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            string path = data.AssetPath.Substring(HotUpdateBuilder.Generated.Length + 1);
            return "code/" + path.Substring(0, path.LastIndexOf('.'));
        }
    }

    public sealed class HotUpdatePackRule : IPackRule
    {
        public PackRuleResult GetPackRuleResult(PackRuleData data)
        {
            string relative = data.AssetPath.Substring(HotUpdateBuilder.Generated.Length + 1);
            int slash = relative.IndexOf('/');
            string name = slash >= 0 ? relative.Substring(0, slash) : Path.GetFileNameWithoutExtension(relative);
            return new PackRuleResult("code_" + name, "bundle");
        }
    }

    public sealed class FoundationStreamingAssets : BuildPlayerProcessor
    {
        internal static string PlayerPath { get; set; }
        internal static string StagedRoot { get; set; }
        internal static bool Injected { get; set; }
        internal static string PackageName { get; set; } = YooAssetService.PackageName;

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            if (StagedRoot == null || !string.Equals(Path.GetFullPath(context.BuildPlayerOptions.locationPathName), PlayerPath, StringComparison.OrdinalIgnoreCase))
                return;
            string path = Path.Combine(StagedRoot, PackageName);
            string yooRoot = Path.GetRelativePath(Application.streamingAssetsPath, AssetBundleBuilderHelper.GetStreamingAssetsRoot());
            foreach (string file in Directory.GetFiles(path))
            {
                string destination = Path.Combine(yooRoot, PackageName, Path.GetFileName(file)).Replace('\\', '/');
                context.AddAdditionalPathToStreamingAssets(file, destination);
                Injected = true;
            }
        }
    }
}
