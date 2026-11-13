# 战斗性能诊断与优化（2026-11-13）

状态：代码优化和当前内容回归完成；本文记录实际测量，不把本机结果推断为所有设备/全部未来角色的性能保证。

代码基线：`59175b07614d3e7862ce871ea9b914cc35ebe673`（main，已推送）。远端 [Foundation checks](https://github.com/LonelyWatch/AstralOath/actions/runs/43350168303) 已实际通过；它验证基础检查，不替代下文 Unity 与独立 Player 的本机结果。

## 验收范围

当前已实现的基础设施演示、角色实验室与战斗预览；重点覆盖三英雄九技能、敌方行动、夹攻、燃魂、暂停、重开、生命周期和字体。尚未实现的玩法不计入通过。编辑器与独立 Windows 诊断包分别测量，正式 HybridCLR/IL2CPP 发布验证单列。

## 可复现基线

- `.artifacts/perf-font-red.xml`：数字字体测试 2 通过、4 失败。BattleHpDigits 与 BattleDamageDigits 主字体均缺 U+005F；TMP 赋值 font 就查询该字符，备用字体不会参与该查询。
- `.artifacts/perf-startup-red.xml`：重复站位的骨骼数据身份测试等 6 项失败；原实现同一角色各实例重新解析。新增探针最初的跨域闭包失效已修复，不能用该次失败推算启动时间。
- `.artifacts/perf-version-startup-baseline.xml`：23 通过、1 失败；仅探测版本头的合成资源导致 **500,030 次 GC.Alloc**，预算 128。计量用当前线程 Unity Recorder，并先以 1 MiB 分配作阳性控制。Mono 的 GC.GetAllocatedBytesForCurrentThread 本机返回 0，已淘汰该无效指标。
- 同次启动探针：打开场景 393.830 ms，进入 Play 至战斗 ready **8,531.404 ms**，域重载保持启用。该值包含测试框架的进入 Play 开销；不是独立程序启动值。
- `.artifacts/perf-input-baseline.log`：场景加载请求至 ready **6,946.094 ms**；真实技能按钮、目标选择、权威 Submit 路径首个提交 9.559 ms，随后 0.211–0.629 ms。四次 S3 后战斗已结束，旧探针继续等待输入超时，已补终局重开；该轮不能声明九次输入验收通过。
- `.artifacts/perf-input-baseline.xml` 中纯音频测试实际通过：九技能各三次共 27 次，保留原事件随机语音。初始化 532.632 ms；PlayEvent 最大 0.370 ms、createSound 最大 0.162 ms、callback 最大 0.186 ms、Studio.update 最大 0.396 ms。**本次数据不支持声音同步解码是百毫秒技能停顿的假设**，不因此更换 FMOD 模式或加入复杂语音缓存。纯音频无渲染的高帧率样本不可视为战斗帧率。

## 编辑器外部阻塞

同轮真实按钮测试出现 30–34 秒帧。逐项核对 Unity 扫描日志：

- 30,409.004 ms 帧前，USB 枚举合计 29,862.430 ms，ADB 534 ms，共 30,396.430 ms。
- 34,330.786 ms 帧前，相邻连续 USB/ADB 扫描合计 34,319.410 ms。

Windows Kernel-PnP/Device Management 同期记录 TCOMAS LG600 复合 HID（VID_1A2C / PID_1520）反复意外删除，约 28–37 秒一次；另见 USB Display Device 断连及未知 USB 设备 Code 43。未记录设备序列号或账号。尚未确定线缆、端口、设备或驱动哪个是物理根因，未修改硬件或驱动。09:02:15 后当次只读查询暂无新断连，后续测量另记录时间窗口。

Unity 2022.3.62f3 本机程序集未提供受支持的关闭 USB 原生枚举开关；不采用反射私有 Stop、替换 adb.exe 或改安装文件。批处理与交互编辑器的 ADB 调度不同，不能把 batch 的 30 秒直接宣称为用户主编辑器的精确延迟。独立程序测量用来排除编辑器设备扫描影响，不掩盖原始最大帧。

## 改动原则

- 活动角色按场景与源资源身份共享不可变解析数据；姿态、皮肤、动画轨道、Mesh 和材质保持实例所有。最后持有者释放后移除条目，初始化失败和禁用域重载都有清理路径。
- 版本探测读取明确 JSON token，不用正则匹配、不改源版本号；正文仍由对应官方核心解析。
- 数值字体保留原图、原始数字 UV 和 advance；从真实横向笔画补 TMP 必需字符，验证真实 GameText 赋字体及下划线网格，不屏蔽警告。
- Profiler 标记覆盖启动、整帧演出和真实 Submit；诊断日志在采样结束后汇总，避免逐帧日志污染测量。
- 不覆盖用户已有 `FoundationFont.asset` 或 `damage_0.png.meta` 修改，不更改原始 D:/sevenepic。

## 官方资料

- [Unity 2022.3 进入 Play 的流程](https://docs.unity3d.com/2022.3/Documentation/Manual/ConfigurableEnterPlayMode.html)：域与场景重载应分开测量。
- [Unity Profiler markers](https://docs.unity3d.com/2022.3/Documentation/Manual/profiler-markers.html)：EditorLoop 与 PlayerLoop 不是同一个范围。
- [Unity GC 最佳实践](https://docs.unity3d.com/2022.3/Documentation/Manual/performance-garbage-collection-best-practices.html)：减少持续分配、复用容器。
- [FMOD 2.03 样本加载](https://www.fmod.com/docs/2.03/api/studio-guide.html#sample-data-loading)：预载或异步选型必须基于实际瓶颈与声音时序需求。
- [Unity 员工解释 USB 事件触发设备扫描](https://discussions.unity.com/t/android-module-slowing-down-editor-for-non-android-projects-now-on-unity-2021/848410/33)；[同帖关闭方式答复](https://discussions.unity.com/t/android-module-slowing-down-editor-for-non-android-projects-now-on-unity-2021/848410/39)。

## 最终验证

- 完整 EditMode：**339/339 通过**，`.artifacts/perf-final-edit.xml`。包含字体真实几何、源数据复用与回收、版本探测分配预算和冲突头拒绝。
- 完整 PlayMode：**39 通过、0 失败、1 可选截图项跳过**，`.artifacts/perf-final-play.xml`；显式开启截图后该项 **1/1 通过**，`.artifacts/perf-character-capture.xml`，合计当前40项均有通过证据。覆盖基础设施场景、UI、角色实验室/双版Spine、原始九技能双朝向演出、暂停/重开、燃魂、镜头/夹攻时序和资源释放。
- GPU截图保存在 `.artifacts/battle-perf-captures` 与 `.artifacts/character-perf-captures`；已人工查看暂停界面及梅赛德丝S3代表帧。截图与加载通过不等于和原作每一帧一致，不将未逐帧对照内容标为还原通过。
- 启动复验：同一配置保持域重载，Editor Play至ready **5,261.832 ms**，较基线8,531.404ms约减少38%；PlayMode场景加载请求至ready **3,323.043 ms**，基线6,946.094ms。打开场景、域重载、加载请求、独立进程是不同指标，不混用。
- Foundation检查：`.artifacts/perf-foundation.log` Exit0，包含模块边界、服务端/共享战斗、配表、协议和更新签名/损坏/回滚检查。数据库真实连接专项没有提供测试连接串，仍为跳过；不冒充已测MySQL部署。
- Windows诊断包：两次构建/运行成功，输出`.artifacts/battle-performance-player`，临时ProjectSettings/HybridCLR设置恢复，未改生产配置、母包或线上服务。

### 独立 Windows 两轮结果

硬件实际记录：RTX 5080、Ryzen 7 9800X3D；Windows x64 Mono Development、真实画面与FMOD音频、目标60FPS。

首轮证据 `.artifacts/battle-performance-runs/run1`：九技能、2场、18条接受命令、7次敌方行动；2790个可操作帧，P95 **17.254ms**、P99 **18.186ms**、最大 **35.850ms**，1帧超过33.33ms，0帧超过100ms。该轮目标提前死亡，没有观察到夹攻，未伪报覆盖。

最终轮 `.artifacts/battle-performance-runs/run2`：九技能、2场、12条接受命令、3次敌方行动、**1次真实夹攻**；2288个可操作帧，P95 **17.293ms**、P99 **18.235ms**、最大 **35.905ms**，1帧超过33.33ms，0帧超过100ms。九次输入均核对实际接受的技能，声音创建和FMOD服务正常，无运行错误。

两轮长帧都在首次按钮操作，提交约11.33/12.51ms；不能宣称所有帧零卡顿，也不能未经CPU逐项采样就把剩余首次成本全认定为JIT。后续技能提交多为0.17–1.20ms，最终轮其余技能区间最大帧18.05–19.85ms。每次技能百毫秒停顿在这两轮独立包未复现。

独立runtime至ready分别 **6.218s / 5.888s**，外部进程创建至ready文件观测为 **9.729s / 6.516s**（100ms轮询误差）；不同启动缓存状态存在差异，且未采同后端优化前Player基线，不能宣传Player启动提升百分比。终局重开准备约 **1.16–1.22s**，独立记录，保留原始峰值，未算作可操作技能帧。

### 保留限制

- 首次操作仍有约36ms单帧；加载/重开有明确准备期，不能保证任意情况下零停顿。
- USB反复断连导致的Editor原生枚举不由游戏代码修复；未更改用户输入设备、驱动或Unity安装。
- 正式HybridCLR/IL2CPP母包、Android设备、本次未接入的完整PVP网络/数据库部署不在这份Mono诊断验收中。Core/Bootstrap代码改变，后续正式发布需要重建母包。
- 既有用户 `FoundationFont.asset`、`damage_0.png.meta` 修改保留，未提交。
## 重复运行的方法

### 编辑器

- 正常使用 `BattleScene.unity`，停止旧 Play 后再次运行以载入新代码和字体。
- 使用 Unity Profiler 的 **Editor** 目标观察进入 Play/EditorLoop，使用 PlayerLoop 与 `Astral.Battle.*`、`Astral.Audio.*` 区分业务阶段；不要只测预热后的 Begin。
- EditMode：`BattlePlayStartupPerformanceTests` 包含真实域重载；`SkeletonVersionReadPerformanceTests` 检查有效分配采样。
- PlayMode：`BattleInputPerformanceTests` 走技能按钮和目标命令入口，战斗结束后重开；`FmodColdWarmPerformanceTests` 单独核查随机声音原事件，不能当作渲染帧率。

### 独立 Windows 诊断包

菜单 `Tools/战斗场景/构建 Windows 性能诊断包（Mono）`，或 Windows 目标下执行 `-executeMethod AstralOath.Editor.BattlePerformanceBuild.BuildWindows`。产物位于 `.artifacts/battle-performance-player`；隔离构建设置 `ASTRAL_BUILD_TOOL_ROOT` 指向主仓库后，产物仍写主仓库 D 盘目录。

```powershell
& 'D:/UnityProjects/AstralOath/.artifacts/battle-performance-player/AstralOath.BattlePerformance.exe' `
  -astral-perf-auto `
  -logFile 'D:/UnityProjects/AstralOath/.artifacts/battle-performance-player/player.log'
```

自动测量使用真实按钮 PointerClick 与目标命令入口，核对接受的 Primary Action；不测试操作系统鼠标射线/遮挡。技能优先 S2/S3/S1，攻击高生命目标，以增加实际夹攻覆盖机会，不修改权威生命、冷却或随机结果。程序设置 60 FPS、保留绘制和声音，不可加 `-nographics`。报告记录九技能、敌方和夹攻实际观察情况；未观察到必须写未覆盖，不伪造事件。

`Results/ready.json` 为首个可操作且经过两次渲染的时间点；`report.json` 保存每次输入、完整原始帧、P95/P99、最大帧、GC、缺失项和错误。`runtimeStartToReadyMs` 从 Unity runtime 回调计时，不是 OS 创建进程时间；外部测量另存 `process-start.json`（100ms轮询误差）。每轮检查 runId/修改时间，运行前归档旧结果，不能读旧文件冒充本轮成功。

自动测试有时限、输入数和场次上限；被外部暂停或运行错误中断时报告失败。手动重开会重绑当前模拟对象，中断的技能标 aborted，不计入完成覆盖。Mono 诊断配置、源码 define 和产物不会写入正式发布母包。