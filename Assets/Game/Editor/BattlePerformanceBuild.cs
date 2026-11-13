using System;
using System.IO;
using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AstralOath.Editor
{
    // Deliberately separate from signed releases and HybridCLR mother-package baselines.
    public static class BattlePerformanceBuild
    {
        private const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        [MenuItem("Tools/战斗场景/构建 Windows 性能诊断包（Mono）")]
        public static void BuildWindows()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("请退出 Play Mode 后构建性能诊断包。");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("请使用 Windows x64 目标启动此构建；诊断入口不会持久切换活动平台。");
            string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string configuredRoot = Environment.GetEnvironmentVariable("ASTRAL_BUILD_TOOL_ROOT");
            string root = string.IsNullOrWhiteSpace(configuredRoot) ? sourceRoot : Path.GetFullPath(configuredRoot);
            if (!string.Equals(Path.GetPathRoot(root), @"D:\", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("性能构建必须位于 D 盘，避免向 C 盘写入大型产物。");
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new BuildFailedException("缺少战斗场景：" + ScenePath);
            string directory = Path.Combine(root, ".artifacts", "battle-performance-player");
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "AstralOath.BattlePerformance.exe");
            var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            var development = EditorUserBuildSettings.development;
            var scenes = EditorBuildSettings.scenes;
            var fullScreen = PlayerSettings.fullScreenMode;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            string playerSettingsPath = Path.Combine(sourceRoot, "ProjectSettings", "ProjectSettings.asset");
            string hybridSettingsPath = Path.Combine(sourceRoot, "ProjectSettings", "HybridCLRSettings.asset");
            byte[] playerSettings = File.ReadAllBytes(playerSettingsPath);
            byte[] hybridSettings = File.ReadAllBytes(hybridSettingsPath);
            using var hybridScope = new CharacterLabBuildSettingsScope();
            try
            {
                // Enabled HybridCLR forcibly switches Mono to IL2CPP and filters shared Battle out.
                // A diagnostic Mono player instead compiles all referenced scene code normally.
                HybridCLRSettings.instance.enable = false;
                HybridCLRSettings.Save();
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                EditorUserBuildSettings.development = true;
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = executable,
                    target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { "ASTRAL_BATTLE_PERFORMANCE" }
                });
                if (result.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Windows 性能诊断包构建失败：" + result.summary.result);
                File.WriteAllText(Path.Combine(directory, "运行说明.txt"),
                    "此包仅用于 Windows Mono 性能诊断，不是正式 HybridCLR 发布或热更验收。\n" +
                    "手动：启动程序，正常试玩，退出时写入 Results。\n" +
                    "自动：AstralOath.BattlePerformance.exe -astral-perf-auto -logFile Results/player.log\n" +
                    "自动模式最多 240 秒、80 次输入或 8 场，覆盖三角色九技能；失败会写报告并以非零码退出。\n" +
                    "保留窗口和音频，勿加 -nographics。报告中的 runtimeStartToReady 不含 Unity runtime 初始化前的进程启动时间。\n");
                Debug.Log("ASTRAL_BATTLE_PERFORMANCE_BUILT " + executable);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
                PlayerSettings.fullScreenMode = fullScreen;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                EditorUserBuildSettings.development = development;
                EditorBuildSettings.scenes = scenes;
                hybridScope.Dispose();
                // Preserve exact serialized settings as well as their in-memory values.
                File.WriteAllBytes(playerSettingsPath, playerSettings);
                File.WriteAllBytes(hybridSettingsPath, hybridSettings);
            }
        }
    }
}
