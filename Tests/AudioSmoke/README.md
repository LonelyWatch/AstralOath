# FMOD 原生音频回归

此控制台程序编译真实 `FmodCharacterAudioService.cs`、`BindingScope.cs`、FMOD 2.03.15 C# wrapper，只有 Unity `TextAsset`、资产读取接口和 AOT 特性由测试适配层替代。原生 DLL 必须用明确绝对路径传入，不使用Python解释器或模拟音频后端。

```powershell
& ./.tools/dotnet/dotnet.exe run --project Tests/AudioSmoke/AudioSmoke.csproj -- "D:/UnityProjects/AstralOath/Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudio.dll" "D:/sevenepic/原始资源/sound"
```

外部sound目录只读。只加载 master.bank、master.strings.bank、sfx1.bank、voc.event.bank、ja/voc1.audio_ja.bank。开始任何事件前原生 `setOutput(NOSOUND)` 必须成功；不改系统音量、不向扬声器输出。软件DSP仍真实运行，逐条事件需要非零峰值才通过。扬声器试听属于单独的Unity验收。

默认两轮完整创建/释放，每轮验证九技能SFX+九技能日语事件、真实ProgrammerSound回调、单条日语audioTable音频、原生暂停状态、恢复、停止、重复Dispose、bank scope释放、GCHandle owner归零。每个事件窗口3.5秒（650毫秒会漏掉原SFX延迟）。`--guard-only` 单独验证已Dispose服务在任何native创建前拒绝重新初始化。

回调观察器在服务第一次启动后安装，然后重新启动同一原生实例；首次启动所创建但在安装观察器后才销毁的sound单列为 `initialSoundDestroys`，不充当已观测CREATE/DESTROY配对。已观测声音按真实native句柄配对，最终必须全部销毁；事件DESTROYED应为36。所有delegate保持引用至系统释放后。

本次修复前真实失败：Dispose后Initialize创建新native系统后抛OperationCanceledException；原语音事件请求 `voc_c1001_shout_1(zhs)`，日语表不存在该key导致回调失败。生产服务现拒绝重复初始化，已知locale suffix始终映射到经过getSoundInfo验证的日语canonical key，未知suffix保留并正常报错，不因原中文版key存在而错播。

2026-11-12实际结果：进程exit0；两轮36事件+2单语音均产生非零DSP，48 programmer CREATE/DESTROY配对，36事件DESTROYED，两个bank scope均释放，无native退出错误。未复现先前Python解释器退出异常。完整日志由运行者保存至 `.artifacts/character-import/audio-smoke.log`，不把构建bin/obj或bank复制到Tests。
