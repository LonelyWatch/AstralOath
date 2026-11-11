# 角色动画错位修复与回归

本轮接续离线 Spine、FMOD 和角色实验室交付，针对拉斯待机持剑、梅赛德丝 skill3 错位及零时长条目。实际工程为 `D:/UnityProjects/AstralOath`；原始 `D:/sevenepic` 始终只读。修复包括转换器语义错误和播放器状态错误，没有给角色添加专属坐标偏移、重绑骨骼或伪造关键帧。

## 原始翻转轨道被误识别

默认战斗待机实际为 `b_idle`，并非列表中的 `idle`。只修复切换和定位后，Windows 包的默认待机仍出现剑脱手。继续核对发现，参考 SCSP 解析器将 type9/type10 误标为辅助排序/事件。type9 用骨骼索引访问 slot 列表，较大的骨骼索引被忽略，因此完整消费二进制、成功加载、顶点没有 NaN 都没能发现丢失的动作语义。

固定上游版本的 [Animation.h](https://github.com/EsotericSoftware/spine-runtimes/blob/142e770e5b4b42395a3a0a68ff3c8a21ba2774eb/spine-c/include/spine/Animation.h) 明确规定 9/10 是 FlipX/FlipY；`spFlipTimeline` 和 [Animation.c](https://github.com/EsotericSoftware/spine-runtimes/blob/142e770e5b4b42395a3a0a68ff3c8a21ba2774eb/spine-c/src/spine/Animation.c) 的帧结构、轴标志及求值方式与 SCSP 原始记录吻合。现在由严格入口解析，不再委托错误的参考分支，第三方源文件保持不变。

从原始文件恢复了 **364 条翻转轨、1133 个关键帧**：

- 拉斯：FlipX 260 轨／806 帧。
- 梅赛德丝：FlipX 53 轨／172 帧。
- 安洁莉卡：FlipX 50 轨／153 帧，FlipY 1 轨／2 帧。

既有闭包另外 41 份骨骼没有这些轨。44 份全部重新解码，并核对原始记录、输出 JSON 和统计；实际动作内容只改变上述三份战斗模型 JSON；安洁莉卡 b_idle 还移除了旧 type10 误生成的两条 cut_in 事件。重新导入保持全部 347 个既有资源 meta 的字节和 GUID 不变。

转换器标记为 `astraloath-scsp/2`，清单记录版本及逐动画、逐骨骼翻转统计。校验拒绝非法索引、错误轴标记、非布尔值、非法时间、残缺帧、重复轨和未知类型；写出后独立比较不可变源帧，漏轨、漏帧或改值均失败。旧报告的 `engineDrawOrder` 辅助语义判断已废止，兼容字段保留为空。

## 播放状态引发的第二类错位

- 2.1 Seek 从 setup pose 开始，却把 LastTime 设为目标时间，跳过了之前的附件切换。现在从起点参与计算附件轨，跳过的事件不补发。
- 零混合切换未清除前一个动画未覆盖的骨骼、附件及 FFD。现在从所选皮肤的 setup pose 开始，保留全局翻转；显式正混合仍由官方核心求值。
- 换皮肤恢复 setup 附件会破坏动作；再次应用整段混合又可能重复叠加骨骼权重。现在保留真实骨骼、颜色、绘制顺序和混合状态，只为新网格求当前变形，同一附件对象保留 FFD。缺失命名附件用内部不可渲染标记与显式隐藏区分；内部默认皮肤不进入公开列表。

新网格不回放换肤前的逐帧混合历史，也不继承已退出轨道的跨网格 FFD。完成态换肤在临时骨架求末帧，不复活真实轨道或重发完成事件。接口约定见 Core/Skeletons/README.md。

## 0 秒条目是静态姿势

10 个条目的原始时长字节就是零，关键帧都位于时间零：三个角色的 `appear`、`attacked_damage`、`flying_damage`，以及安洁莉卡 `run_start`。列表保留名称并显示“静态姿势”，不补造时长。梅赛德丝 skill3 骨骼时长为 4.0666 秒，另一个空 timeline 文件不决定该动画时长。

## 验证过程

- 转换测试先失败再修复，完整 Python 回归 **30 项通过**。44 份源骨骼均完整消费，翻转轨和帧数逐项对应。
- 44 份骨骼在固定官方核心中重新遍历动画、皮肤和时间点，**44/44 通过**。读取/求值检查不能单独作为画面还原证据。
- 播放状态首次 Unity 失败报告为 `.artifacts/unity/AnimationIsolationRed.xml`。同一独立几何检查在接入版本有 496/1017 组切换不一致，修复后 1017/1017 一致，加入正确翻转轨的三模型也通过这 1017 组。
- 仅修播放器时完成 34 帧对照：20 组定位、重复播放、换肤操作由 15 组像素不一致变为 0；14 张独立启动和 3.8 立绘基线不变。此阶段仍遗漏默认 `b_idle` 转换错误，不能当作最终还原通过。
- 最终扩大为 **54 帧**：保留原 34 帧，为 `b_idle`、`b_idle_ready` 各取时长 0/20/40/60/80%，比较连续 60Hz 与单次采样。实际 GPU 图已确认默认待机双手握剑、idle 手剑关系及梅赛德丝 skill3 人物／书本相对关系得到修正。

GPU 工具为仅在 Editor 编译的 `CharacterAnimationVisualTests`，使用真实场景、YooAsset、UI 按钮、D3D11/URP 和 1920×1080 RT，固定播放时钟并记录骨骼、附件、相机。未设置 `ASTRAL_ANIMATION_CAPTURE_DIR` 时明确跳过；采集成功和原作一致性分别记账。

证据目录：

- 播放状态：`.artifacts/animation-visual-before-final`、`animation-visual-after`、`animation-comparison/report.json`。
- 转换翻转：`.artifacts/animation-flip-before/20261111T191418395Z-7dd974130c8740e6a4292ffd2e746e00`、`.artifacts/animation-flip-after/20261111T192011208Z-51c8789e8b994eb4a02ef5ccb0b605ab`。
- 原始语义：`.artifacts/character-diagnosis/flip-closure-report.json`、`flip-tests-red.log`、`flip-runtime44.log`。来源及输出哈希见正式 import-manifest.json。

拉斯 3.8 立绘另核对了 78 骨、82 附件、86 条时间线、387 个关键帧和曲线缓存，未发现该版本转换错误；剑直接继承右手。未因 2.1 缺陷修改 3.8 核心。

## 演出边界

本轮确认骨骼转换和播放基础，不宣称所有 E7 资产及完整 STG 技能已完美还原。“条件映射预览”的 CAM/MOVE/ZOOM、目标锚点、部分特效和参数仍是明示实验行为，需要用户监督与原作逐段对照；不能靠挪动角色掩盖差异。实验室不计算伤害、治疗、奖励或 BP。

桥接修改属于 AOT，必须使用重新构建的 Windows 母包；只更新资源不能修复旧包的播放状态缺陷。Android 不在本轮验收范围。


## 最终检查记录

- 最终资源上的 Unity EditMode：189/189 通过，包含 38 项新增状态/皮肤用例，以及三角色全部 1017 组动画切换。报告 `.artifacts/unity/AnimationFlipEditMode.xml`。
- 最终 GPU PlayMode：14/14 通过（本次显式开启截图诊断），报告 `.artifacts/unity/AnimationFlipPlayMode.xml`。
- 54 帧图形比较：原 20 组定位、重播和换肤对照全部逐像素一致；新增 10 组连续 60Hz 与单次 Tick 对照中，9 组完全一致，b_idle_ready 的 40% 时刻有 22/2,073,600 像素的微小边缘差，最大 RGB8 通道差 4，MAE 约 4.82e-6。附件、alpha、本地位置和缩放无差异，世界坐标残差不超过 7.16e-7，属于逐次 float 累积差异，未伪称逐位完全一致。
- 3.8 立绘六帧的像素和元数据保持不变。修正 2.1 翻转后有 40 帧相对旧资源发生像素变化，这正是本轮需要验证的变化，不要求保留旧错误姿态。比较报告 `.artifacts/flip-comparison/report.json`。
- 30 项 Python、44 份源数据及清单哈希均通过独立复核；没有改动官方 Spine/FMOD 源码或原始解包资源。


## Windows 包与启动

最终包：`.artifacts/characterlab-builds/Win64/player-v1-355c435d2ef948c8a9f569edcba823d9/CharacterLab.exe`。母包身份 `95169c2336ff479bab54fe53dd0b4428`。不要使用前面诊断阶段的 ad2b0e / 07f7b3 包，它们还没有最终翻转资源。

Windows x64 IL2CPP 构建 exit 0。最终 exe 在仅针对自身的临时出站阻断下运行，三角色、九技能事件入口、语音、暂停、停止、立绘及关闭回收检查通过，进程 exit 0；外部脚本核对完整退出日志，未出现空引用、缺字或验收失败标记。结束后进程和临时防火墙规则均已清理。九技能入口各观察 3.5 秒，不能用此项宣称完整技能演出已还原。

最终截图实际为 1440×900（命令请求 1600×900，但窗口受桌面尺寸限制）；已目视核对默认 b_idle 双手握剑、中文静态姿势标签和 normal 立绘。日志与图片位于 `.artifacts/character-player-final-flip`。此轮没有重复 Android 或服务器数据库验收。

在 Unity 中使用 `Tools/角色实验室/打开场景`，然后点击 Play；也可直接运行上述 exe。资源已按固定源重新导入，不需要重新下载运行库、登录服务器或启动 Spine/FMOD 编辑器。

提交前暂存区的 254 个产物 SHA256/LFS oid 与清单全部一致，26 个提交文件不含构建产物、缓存、原始 SCSP 或解包目录。独立审查未发现阻断问题。
