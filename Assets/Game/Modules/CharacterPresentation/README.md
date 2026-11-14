# CharacterPresentation

热更业务程序集 `AstralOath.Client.CharacterPresentation`，不结算战斗和奖励。

`PresentationClock` 统一管理播放时间、速度和有声资格。暂停不推进时间；seek、逐帧和速度变化会锁定静音，只有从零 reset 恢复正常 1x 声画播放。`PresentationGraphScheduler` 是已验证时序数据的独立纯 C# 调度器，支持并行、延迟和按 actor/name 匹配的事件相对时序；未验证节点不会执行。

`StgPlaybackSession` 直接保留和遍历导入 graph.source 的 entities/connections/entrypoint/eventlisteners。ANI 完成和 ONEVENT 匹配分开处理，ONEVENT 保留源动画名；不把未知 when/location 改写成绝对时间。

若原始 STG 缺少已被原骨骼事件和实机共同确认的演出节点，导入工具可以生成独立的 `graph.additions`。战斗与角色实验室以相同规则读取追加的实体与连接。原始 `graph.source` 保留不动。当前仅登记梅赛德丝基础皮肤 S3。Foundation 发布时把支持 `additions` 的 BattleScene 代码与新资源放入同一热更包，并按选定母包执行兼容检查。角色实验室随现有离线 Windows 测试包一起构建。此立绘补充不涉及 AOT、原生库或网络协议。参见[演出排查记录](../../../../docs/delivery/2026-11-14-mercedes-s3-cutin.md)。

默认严格模式对编码条件给出诊断；实验模式明确提示 when=1 起始、缺省连接完成，以及外部显式注入的试验目标锚点。实验 CAM/MOVE/ZOOM 不声称原始曲线一致。CFX/timeline/粒子/CUTIN 的资源预加载与视觉执行位于 CharacterLab 的 CharacterEffectStage，与本调度器消费相同 Tick。

`Tests~/CharacterPresentationClockTests.cs` 是 .NET 10 无 Unity 依赖的实际行为断言；最初空实现已观察到播放时钟断言失败，再实现后通过。Unity 图回归在 `Assets/Game/Tests/EditMode/CharacterPresentationGraphTests.cs`，由协调者统一执行。

本模块的验证区分导入、播放及原作逐帧对照。运行库兼容性、目标 Player 资源加载、正常速度音画和用户监督的原作对照需要分别记录结果。


回归包括 PresentationFade 的纯 C# 预览包络断言，以及 SHAKE 不累积漂移、STOP 按 ID 清理的 Unity 图回归；具体数量与结果以当次测试输出为准。DARK的总时长包含淡出是实验室明确选择，不作为原作时序已验证。


SpriteBlurSettings 校验原始 PPEFFECT uniform 并保留 shader 的样本上限与 ratio clamp；PresentationScalar 在入队前识别未知范围语法与非有限值。新增真实 CFX 范围回归和 PPEFFECT 仅按源连接触发的回归。后处理 shader 数学移植有源码证据，整体 sprite 作用范围和 SINE_VALUE 参数驱动仍保留差异。

SCT1 的中性白光遮罩按源 atlas 区域审计和登记，RGB 由未量化的 alpha 恢复，其他区块按原解码输出。新增角色及转换器修改必须执行 `docs/development/character-asset-import.md`；SCSP 曲线类型、局部翻转和继承语义在转换层保留，不由演出层添加逐角色偏移补偿。

2026-11-13 已将十名角色及三十个技能槽演出导入资源清单，按 E7 ID 顺序供实验室和战斗使用。原资源名和技能图名不一致时按源数据记录映射。`NODE_ANI` 读取 `spani/` 原骨骼轨道，无渲染附件时可使用空图集。命名 timeline 事件、DELAY、空 SHOT 与完成连接按源时刻调度。十名角色的模型、立绘、资源地址、音频表及源闭包边界见[角色资源进入 Unity](../../../../docs/development/character-asset-import.md)。

杰拉图的原骨骼使用 zerato 拼写，技能演出图使用 jerato。第三技能的立绘由本形态骨骼命名事件定位，作为导入图的附加节点登记。见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

卡琳 c1011 已接入角色内容；三技能、音频及已知缺项见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

萝菲 c1019 已接入三张演出图及原版日语。新增原版 voc3 银行，战斗按阵容选择；裂伤有效生命独立于基础面板。规则、表现诊断与音频来源见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

新增 32 张源演出图。导入闭包保留 10 个模型、立绘与依赖的源版本；克莱莉莎补入骨骼事件驱动的原版立绘。 [第 21–30 名角色验收](../../../../docs/delivery/2026-11-14-roster21-30.md)
