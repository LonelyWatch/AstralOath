# 战斗时序、性能与试玩修订验收

日期：2026-11-13。工作区 `D:/UnityProjects/AstralOath`，main，修改前基线 `f6a2719fc3ffb0bd5627cb4cdf52c56bfd57e2a5`。本报告与实现同批提交；视频、日志、截图保存在 D 盘 `.artifacts`，不提交媒体和缓存。用户原有 `damage_0.png.meta` 压缩设置保留，未纳入本次提交。

## 行为与根因

- 攻击入口原先重新读取整份角色骨骼 JSON，效果实例还重复解析同源数据。现在开战前准备当前阵容的技能依赖；事件/时长流式读取，源对象缓存不可变解析结果，实例姿态、皮肤、事件、网格和材质独立。缓存随场景资源释放。首轮准备工作仍然存在，移到可操作之前，不称作消除加载成本。
- Cut-in 原来与舞台同时推进，安洁莉卡 S3 光柱前段和 blur 在遮罩背后已经播完。现在 Cut-in 独立消耗1.28秒，期间冻结舞台源时钟；结束继续原 ANI/CFX/CAM/HIT。用户Pause才同时冻结视觉和本场FMOD；不改权威战斗时钟或结算。
- CFX忽略primitive.ani导致选错片段。安洁莉卡S3的animation2以及公共slash_normal的red均按源选择，时长也按同一选择计算。未知显式ani拒绝该实例，未知actions记录诊断。当前Fire指令未证明等同骨骼动画，不用名称猜实现。另补两版各2例0秒静态姿态回归：存在性与自然时长区分，显式ani及action可选已有静态片段，显式lifeTime控制保持时长。
- Cut-in原帧带透明区域，播放器丢弃源幕布配置，导致自身小模型、队友和HUD透出。现在传递curtain_color/opacity，用独立幕布同Cut-in墙钟释放；缺省黑/1，显式0保留透明，不通过隐藏演员实现。
- DARK原来将fadeOut包含在duration内，提前.7秒露出地图。根据连续帧及跨角色源字段，将duration解释为开始退出时间，fadeOut单独追加；无限节点保留STOP。图完成和视觉释放同步。
- CAM/ZOOM续段原来从被地图边界裁掉的请求值出发，造成先停后跳。现从真实显示取景续接，保留源零时长硬切。夹攻在主攻击最后HIT及其反馈完成时接手，主角色的回位尾段并行，只有新舞台控制相机；缺源归位时用项目通用.2秒恢复策略。
- 场景Play自动进入试玩；四只史莱姆使用共享工厂真实60级。行动条小头像在大头像上层。暂停菜单支持继续与重新开始；重新开始即帧摘除旧HUD名称与交互，避免找到延迟销毁的旧对象。
- 绑定和自动开局在Update，模拟及镜头在LateUpdate，避免EventSystem对新镜头和旧Canvas投影做命中测试。暂停按钮在Cut-in之上，面板屏蔽其他输入。燃魂仅由Burn位置Cancel解除，其他技能置暗且不能点击。

源、录像日期、时间戳及推断边界见[详细复核](2026-11-13-angelica-timing-reference.md)。用户指定安洁莉卡录像发布于2022年；拉斯夹攻录像发布于2026-09-19，二者均没有可确认的客户端版本，不能称为最新版本验证。

## 测量口径与结果

Unity2022.3.62f3 Windows Editor隔离工程，真实本地资产。三名角色九个技能，分别记录准备前首次、准备后首次及重复；另用真实相机、FMOD、HUD和自然帧间隔运行九技能。Unity批处理帧率和Player不同，不由Editor数据承诺设备帧率。

- 原始入口测量 `battle-timing-red-play.log`：拉斯S1约598.376ms，梅赛德丝S1约212.172ms，安洁莉卡S1约1088.056ms。该早期探针未绑定相机，不能与后来的全场景结果作为严格同条件比率。
- 准备后真实相机、1/4目标、首次/重复共36样本 `battle-timing-green-play.log`：Begin约.202–1.324ms，最慢单次演出Tick为8.717ms。该探针按固定步长主动推进，只代表CPU调用段，不代表GPU整帧。
- 实际音频/HUD/自然帧探针 `battle-timing-focused.log`：九次入口含语音.758–1.713ms，最慢观测帧15.032ms。先前完整套件出现一次拉斯S2整帧68.939ms，后加Shader/GC/Camera/PlayerLoop采样；此轮未复现，不宣称全部帧无尖峰。
- 最终 `battle-timing-final2-play.log` 的36个准备后样本Begin为.205–1.126ms，最慢Tick3.262ms；九个真实音频/HUD样本入口.819–1.568ms，每技能2639–10854帧（最少为梅赛德丝S1），观测最大帧11.175ms，未记录超过33.3ms帧。保留前述68.939ms历史尖峰，不将一次未复现写成永久消失。
- 最后关键复跑 `battle-timing-final3-play.log` 出现梅赛德丝S3单帧1169.668ms（source3.312894），同期记录PlayerLoop1.152ms、GC0Delta0。紧邻之前有13次USB扫描（512.575ms及21–87ms）及ADB扫描172ms，13次USB扫描合计981.862ms，加ADB约1153.862ms，提示Editor设备枚举阻塞，不能归因于技能计算或删除这个样本。Recorder记录上一帧，SourceAt为本帧LateUpdate尚未消费长delta的位置，不能据此精确定位致停cue或证明随后追帧只耗1ms。此轮入口仍.750–1.606ms。为后续定位加入EditorLoop记录；不卸载Android模块、不改SDK文件或系统USB设置。Player与Editor样本区别依据[Unity2022.3官方Profiler说明](https://docs.unity3d.com/2022.3/Documentation/Manual/profiler-markers.html)，当前USB归因是日志支持的推断，尚无受控开关试验。
- `battle-runtime-isolated.xml` 独立九技能探针1/1通过；期间未并行截图、构建或编辑。长帧在不同技能拉斯S2复现1160.344ms，同样紧邻USB/ADB扫描，随后还有70.157ms追帧（PlayerLoop69.643ms）。独立进程首个拉斯S1入口15.494ms、其后8个入口.741–2.473ms；除拉斯S2扫描停顿外，观测最大帧19.907ms。首个调用包含首次方法/音频/渲染路径执行，未以JIT为唯一原因下结论。该复验排除了“只在梅赛德丝某个固定cue出现”的解释，但尚未确认具体设备/驱动。EditorLoop在当前Recorder可用时采样，本次慢帧无该marker有效读数，不能凭空填数。后续应对独立Player/Editor Profiler区分环境停顿；当前不宣称彻底零卡顿。
- Unity Mono的分配接口返回0不可靠，未以其作为无分配证明。事件元数据回归采用真实大骨骼和保留内存，原整份DOM约30.4MB可复现失败，流式实现通过范围约束。

完整回归曾出现首技能只有1帧的无效样本：测试在同步加载/开局同帧注入动作，下一LateUpdate将加载前长dt用于播放。现明确等两个连续小于50ms的可见帧后再注入，每技能至少10个采样帧；不足直接失败，不输出误导性的分位数。此为采样条件修正，不放宽播放或视觉断言。最终复跑结果见下方。

## 验证记录

- 已先运行失败用例：镜头4项、交互/夹攻4项、骨骼缓存/元数据/Cut-in4项；`battle-cfx-dark-red.xml` 12项中9项按预期失败，覆盖两版ani实际姿态、寿命、无效名字、有限DARK和图完成。
- 基础脚本 `battle-timing-foundation.log` 通过，服务端56通过、1项MySQL条件集成测试跳过；配表、协议、模块和更新工具检查通过。该结果不是本次MySQL备份恢复或联网PVP验收。
- `battle-timing-final-edit.xml` 270/270通过（含CFX/DARK）；随后实帧发现透明Cut-in缺幕布，`battle-curtain-red.xml` 7/7预期失败后补实现。`battle-timing-final2-edit.xml` **277/277通过**；最后4个静态姿态用例在`battle-zero-pose-red.xml`实跑失败后修复，最终 `battle-timing-final3-edit.xml` **281/281通过**。
- `battle-timing-final-play.xml` 36通过、1失败、1可选跳过。失败为旧三角色S3用例加载同帧注入点击导致整个首动作被长dt消耗；已按自然帧条件修正，保留真实回合、技能、舞台和Cut-in断言。最终 `battle-timing-final2-play.xml` **37通过、0失败、1可选角色实验室截图跳过**，含首批九技能双向与6种中断、真实三角色S3、有效自然帧探针和全部交互回归。
- 完整PlayMode之后仅补了显式静态片段的存在性判断；`battle-timing-final3-play.xml`关键PlayMode **8/8通过**，覆盖九技能真实音画、Cut-in、夹攻及交互。独立只读审查未发现可证实的新P1/P2，实帧和测试仍是验收依据。
- Windows/Android Player构建、真机帧率和正式热更发布本轮未执行；偶发Editor设备扫描长帧保留为环境待定位项。Core骨骼缓存与Bootstrap FMOD能力变化要求重建AOT母包，不能只向旧母包发业务DLL。

## 实帧与运行入口

打开 `Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity` 后Play即开始试玩。右上暂停可继续或重来；燃魂后点Burn位置Cancel解除。

- `.artifacts/battle-timing-captures/battle-scene-1920x1080.png`：自动试玩、头像层级和当前HUD。
- `.artifacts/battle-timing-captures/pause-menu.png`：实际暂停面板。
- 实际查看最终`angelica-clock/frame-016-wall-1.6.png`：原先Cut-in透明洞中的小角色/队友和常规HUD已被源幕布正确覆盖。单人序列wall3.8开始显露地图，和参考视频相对起手约3.7–3.8秒的恢复相位一致。
- `.artifacts/battle-timing-captures/angelica-clock` / `angelica-solo-clock`：53帧、0–5.2秒及timeline.csv；四受益者正常效果和单人参考对齐分开。单人是测试条件，不隐藏正式战斗中的友军。
- `.artifacts/battle-timing-captures/source-playback`：三角色九技能正反方向阶段图与中断恢复。

复跑先用项目PowerShell `Tools/Invoke-Unity.ps1 -Mode Prepare -ProjectName UnityBattleScene`，再以Unity的 `-batchmode -projectPath D:/UnityProjects/AstralOath/.artifacts/UnityBattleScene -runTests -testPlatform EditMode` 或PlayMode执行，并指定D盘 `-logFile` / `-testResults`。需要截图设置 `ASTRAL_BATTLE_CAPTURE_DIR` 为上述D盘目录；保留图形设备，勿加-nographics。初始化ProjectEnvironment，且同一时间只开一个隔离Unity测试进程，避免显存/内存争用。

## 可扩展规则与剩余边界

通用接入见[源演出规范](../development/battle-source-presentation.md)和[ADR012](../architecture/adr/012-battle-presentation-clock.md)。新增角色先核对同版本闭包、显式动画名、源时序/坐标/生命周期，再录取阶段帧和性能数据；禁止按角色ID补速率、隐藏队友或固定偏移掩盖解析错误。重要发现、决策、测量和未验证差异即时写文档已加入AGENTS及通用规范。

首批九技能可播放与回归通过不等于全E7逐像素还原。有限LAYOUT、MOVE参数、shake powerX/powerY、SINE相位、DARK accel、未映射Fire动作以及原版每击权重仍需补证；本轮没有静默宣布这些语义已确定。场景使用当前共享规则的本地权威模拟；无新增联机协议或服务端结算行为。

## 提交与远端检查

实现提交 `5a8da7f489d7437bf6d9f0a19fe1bd0e95804b29` 已推送origin/main，[Foundation checks](https://github.com/LonelyWatch/AstralOath/actions/runs/43291919658)实际完成并通过。此后仅补本文及实施计划的验收状态，不改变运行时代码。远端检查不包含Unity许可、Player或MySQL设备环境；本机测试结果仍以上述独立日志为准。
