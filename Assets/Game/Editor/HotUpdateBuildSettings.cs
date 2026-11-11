using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AstralOath.Editor
{
    [Serializable]
    internal sealed class HotUpdateBuildSettings
    {
        private const string SettingsPath = "UserSettings/AstralOathBuild.json";
        public string baselineId = "";
        [NonSerialized] public string expectedBaselineId;
        public string baselinePath = "";
        public string appVersion = "0.1.0";
        public int androidVersionCode = 1;
        public long contentVersion = 1;
        public string outputRoot = ".artifacts/builds";
        public string signingKey = "";
        public string offlinePath = "";
        public bool development;
        public bool developmentHttp;

        internal static HotUpdateBuildSettings LoadLocal() => File.Exists(SettingsPath)
            ? JsonUtility.FromJson<HotUpdateBuildSettings>(File.ReadAllText(SettingsPath))
            : new HotUpdateBuildSettings();

        internal static HotUpdateBuildSettings Load()
        {
            var settings = LoadLocal();
            string[] args = Environment.GetCommandLineArgs();
            settings.expectedBaselineId = ReadArg(args, "-astral-baseline-id", null);
            settings.baselinePath = ReadArg(args, "-astral-baseline-path", settings.baselinePath);
            settings.appVersion = ReadArg(args, "-astral-app-version", settings.appVersion);
            settings.outputRoot = ReadArg(args, "-astral-output-root", settings.outputRoot);
            settings.offlinePath = ReadArg(args, "-astral-offline-path", settings.offlinePath);
            settings.signingKey = Environment.GetEnvironmentVariable("ASTRAL_SIGNING_KEY") ?? settings.signingKey;
            var content = ReadArg(args, "-astral-content-version", settings.contentVersion.ToString(CultureInfo.InvariantCulture));
            var android = ReadArg(args, "-astral-android-version-code", settings.androidVersionCode.ToString(CultureInfo.InvariantCulture));
            if (!long.TryParse(content, NumberStyles.None, CultureInfo.InvariantCulture, out settings.contentVersion) ||
                !int.TryParse(android, NumberStyles.None, CultureInfo.InvariantCulture, out settings.androidVersionCode))
                throw new BuildFailedException("资源版本和 Android versionCode 必须为整数。");
            settings.developmentHttp |= args.Contains("-astral-dev-http");
            settings.development |= settings.developmentHttp || args.Contains("-astral-development");
            return settings;
        }

        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
        }

        internal static string Sign(string output, HotUpdateBuildSettings options)
        {
            if (string.IsNullOrWhiteSpace(options.signingKey) || !File.Exists(options.signingKey))
                throw new BuildFailedException("请在构建设置中选择 RSA 私钥文件。");
            string release = Path.Combine(ToolRoot, ".artifacts", "releases", Path.GetFileName(output));
            RunScript("Content/Package-Unity.ps1", Path.GetFullPath(options.signingKey), 300,
                "-BuildOutput", Path.GetFullPath(output), "-Release", release);
            Debug.Log("ASTRAL_SIGNED_RELEASE " + release);
            return release;
        }

        internal static string ToolRoot => Environment.GetEnvironmentVariable("ASTRAL_BUILD_TOOL_ROOT") ??
            Directory.GetParent(Application.dataPath).FullName;

        internal static string RunScript(string relative, string privateKey, int timeoutSeconds, params string[] args)
        {
            string path = Path.Combine(ToolRoot, "Tools", relative);
            if (!File.Exists(path))
                throw new BuildFailedException("找不到工具脚本：" + path);
            var arguments = new[] { "-NoProfile", "-NonInteractive", "-File", path }.Concat(args);
            var info = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = string.Join(" ", arguments.Select(Quote)),
                WorkingDirectory = ToolRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (privateKey != null)
                info.EnvironmentVariables["ASTRAL_SIGNING_KEY"] = privateKey;
            using var process = Process.Start(info) ?? throw new BuildFailedException("无法运行 PowerShell 7。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                process.Kill();
                throw new BuildFailedException("工具执行超时：" + relative);
            }
            if (process.ExitCode != 0)
                throw new BuildFailedException($"工具执行失败（{process.ExitCode}）：{relative}\n{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}");
            return stdout.GetAwaiter().GetResult();
        }

        private static string Quote(string argument)
        {
            if (argument.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new BuildFailedException("参数不能包含引号或换行。");
            int trailingSlashes = argument.Length - argument.TrimEnd('\\').Length;
            return "\"" + argument + new string('\\', trailingSlashes) + "\"";
        }

        private static string ReadArg(IReadOnlyList<string> args, string name, string fallback)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] != name)
                    continue;
                if (i + 1 == args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new BuildFailedException("缺少参数值：" + name);
                return args[i + 1];
            }
            return fallback;
        }
    }

    internal sealed class HotUpdateBuildWindow : EditorWindow
    {
        private HotUpdateBuildSettings settings;

        [MenuItem("Tools/AstralOath/构建设置")]
        private static void ShowWindow() => GetWindow<HotUpdateBuildWindow>("AstralOath 构建设置");

        [MenuItem("Tools/AstralOath/导表")]
        private static void GenerateConfig()
        {
            string output = HotUpdateBuildSettings.RunScript("Config/Generate.ps1", null, 600, "-VerifyAccessors", "-SyncClient");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(output);
        }

        [MenuItem("Tools/AstralOath/签名发布…")]
        private static void SignRelease()
        {
            var options = HotUpdateBuildSettings.LoadLocal();
            string file = EditorUtility.OpenFilePanel("选择 package.complete.json", options.outputRoot, "json");
            if (string.IsNullOrEmpty(file))
                return;
            if (Path.GetFileName(file) != HotUpdateBuilder.BuildReceiptName)
                throw new BuildFailedException("请选择 package.complete.json。");
            HotUpdateBuildSettings.Sign(Path.GetDirectoryName(file), options);
        }

        [MenuItem("Tools/AstralOath/打开构建输出")]
        private static void OpenOutput()
        {
            string output = Path.GetFullPath(HotUpdateBuildSettings.LoadLocal().outputRoot);
            Directory.CreateDirectory(output);
            EditorUtility.RevealInFinder(output);
        }

        internal static void RememberBaseline(string id, string path)
        {
            var local = HotUpdateBuildSettings.LoadLocal();
            local.baselineId = id;
            local.baselinePath = path;
            local.Save();
            foreach (var window in Resources.FindObjectsOfTypeAll<HotUpdateBuildWindow>())
            {
                window.settings.baselineId = id;
                window.settings.baselinePath = path;
                window.Repaint();
            }
        }

        private void OnEnable() => settings = HotUpdateBuildSettings.LoadLocal();

        private void OnGUI()
        {
            string selectedBaseline = PathField("母包归档", settings.baselinePath, folder: true);
            if (selectedBaseline != settings.baselinePath)
            {
                settings.baselinePath = selectedBaseline;
                settings.baselineId = HotUpdateBuilder.ReadArchivedBaselineId(selectedBaseline);
            }
            EditorGUILayout.SelectableLabel("母包 ID（强更自动生成） " + settings.baselineId, GUILayout.Height(20));
            settings.appVersion = EditorGUILayout.TextField("应用版本", settings.appVersion);
            settings.androidVersionCode = EditorGUILayout.IntField("Android versionCode", settings.androidVersionCode);
            settings.contentVersion = EditorGUILayout.LongField("资源版本", settings.contentVersion);
            settings.outputRoot = PathField("输出目录", settings.outputRoot, folder: true);
            settings.signingKey = PathField("本地 RSA 私钥", settings.signingKey);
            settings.offlinePath = PathField("离线 libil2cpp 源", settings.offlinePath, folder: true);
            settings.development = EditorGUILayout.Toggle("开发构建", settings.development);
            settings.developmentHttp = EditorGUILayout.Toggle("开发 HTTP", settings.developmentHttp);
            if (GUILayout.Button("保存"))
                settings.Save();
        }

        private static string PathField(string label, string value, bool folder = false)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value);
                if (!GUILayout.Button("选择", GUILayout.Width(52)))
                    return value;
                string selected = folder ? EditorUtility.OpenFolderPanel(label, value, "") :
                    EditorUtility.OpenFilePanel(label, Path.GetDirectoryName(value ?? ""), "");
                return string.IsNullOrEmpty(selected) ? value : selected;
            }
        }
    }
}
