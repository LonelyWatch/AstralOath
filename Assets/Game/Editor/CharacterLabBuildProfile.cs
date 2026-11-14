using System;
using System.Globalization;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using UnityEngine;
namespace AstralOath.Editor
{
    // Explicit profiles prevent a lab package or baseline from being mistaken for Foundation.
    public sealed class CharacterLabBuildProfile
    {
        public static readonly CharacterLabBuildProfile Foundation = new CharacterLabBuildProfile(
            "foundation", "Foundation", "foundation-", ArenaUiBuilder.StartupScene,
            "Config/HotUpdate/assemblies.json", "AstralOath", "AstralOath.Client.FoundationDemo",
            "AstralOath.Client.FoundationDemo.FoundationEntry", "AstralOath.Client.Common.IHotUpdateEntry");
        public static readonly CharacterLabBuildProfile CharacterLab = new CharacterLabBuildProfile(
            "characterlab", "CharacterLab", "characterlab-", "Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity",
            "Config/HotUpdate/characterlab-assemblies.json", "CharacterLab", "AstralOath.Client.CharacterLab",
            "AstralOath.Client.CharacterLab.CharacterLabEntry", "AstralOath.Client.Common.ICharacterLabEntry");
        public string Id { get; }
        public string PackageName { get; }
        public string VersionPrefix { get; }
        public string ScenePath { get; }
        public string AssemblyConfigPath { get; }
        public string ExecutableName { get; }
        public string EntryAssembly { get; }
        public string EntryType { get; }
        public string EntryContract { get; }
        private CharacterLabBuildProfile(string id, string package, string prefix, string scene, string config,
            string executable, string assembly, string type, string contract)
        {
            Id=id; PackageName=package; VersionPrefix=prefix; ScenePath=scene; AssemblyConfigPath=config;
            ExecutableName=executable; EntryAssembly=assembly; EntryType=type; EntryContract=contract;
        }
        public bool AcceptsBaseline(string profileId, string package, string assembly, string type)
        {
            // Old completed Foundation archives predate the profile fields.
            if (this == Foundation && string.IsNullOrEmpty(profileId) && string.IsNullOrEmpty(package) &&
                string.IsNullOrEmpty(assembly) && string.IsNullOrEmpty(type)) return true;
            return profileId == Id && package == PackageName && assembly == EntryAssembly && type == EntryType;
        }
        internal bool AcceptsEntry(TypeDef type) => type.FullName == EntryType && type.IsPublic && !type.IsAbstract &&
            type.Interfaces.Any(item => item.Interface.FullName == EntryContract) &&
            type.Methods.Any(method => method.IsInstanceConstructor && method.IsPublic && method.MethodSig.Params.Count == 0);
        internal HotUpdateBuildSettings LoadSettings() => this == Foundation ? HotUpdateBuildSettings.Load() :
            CharacterLabBuildSettings.Load().ToSharedOptions();
        internal void RememberBaseline(string id, string directory)
        {
            if (this == Foundation) HotUpdateBuildWindow.RememberBaseline(id, directory);
            else CharacterLabBuildSettings.RememberBaseline(id, directory);
        }
    }
    [Serializable]
    public sealed class CharacterLabBuildSettings
    {
        public const string SettingsPath = "UserSettings/AstralOathCharacterLabBuild.json";
        public string baselineId = "";
        public string baselinePath = "";
        public string outputRoot = ".artifacts/characterlab-builds";
        public string appVersion = "0.1.0";
        public string signingKey = "";
        public long contentVersion = 1;
        public bool development;
        public bool developmentHttp;
        [NonSerialized] public string expectedBaselineId;
        public static CharacterLabBuildSettings Parse(string json, string[] args)
        {
            var settings = new CharacterLabBuildSettings();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, settings);
            // Never consume Foundation's local settings or ASTRAL_SIGNING_KEY.
            if (!string.IsNullOrWhiteSpace(settings.signingKey))
                throw new InvalidDataException("角色实验室测试包不使用 Foundation 签名发布器。");
            settings.expectedBaselineId = ReadArg(args, "-astral-baseline-id", null);
            settings.baselinePath = ReadArg(args, "-astral-baseline-path", settings.baselinePath);
            settings.outputRoot = ReadArg(args, "-astral-output-root", settings.outputRoot);
            settings.appVersion = ReadArg(args, "-astral-app-version", settings.appVersion);
            string version = ReadArg(args, "-astral-content-version", settings.contentVersion.ToString(CultureInfo.InvariantCulture));
            if (!long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out settings.contentVersion) || settings.contentVersion <= 0)
                throw new ArgumentException("角色实验室资源版本必须为正整数。");
            settings.developmentHttp |= args.Contains("-astral-dev-http");
            settings.development |= settings.developmentHttp || args.Contains("-astral-development");
            return settings;
        }
        internal static CharacterLabBuildSettings Load() => Parse(File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null,
            Environment.GetCommandLineArgs());
        internal HotUpdateBuildSettings ToSharedOptions() => new HotUpdateBuildSettings
        {
            baselineId=baselineId, baselinePath=baselinePath, expectedBaselineId=expectedBaselineId,
            outputRoot=outputRoot, appVersion=appVersion, contentVersion=contentVersion,
            development=development, developmentHttp=developmentHttp, signingKey=""
        };
        internal static void RememberBaseline(string id, string directory)
        {
            var local = Parse(File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null, Array.Empty<string>());
            local.baselineId = id; local.baselinePath = directory;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(local, true));
        }
        private static string ReadArg(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return fallback;
            if (index + 1 >= args.Length || args[index+1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("缺少参数值：" + name);
            return args[index+1];
        }
    }
}
