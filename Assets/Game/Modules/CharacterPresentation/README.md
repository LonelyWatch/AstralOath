# CharacterPresentation

热更业务程序集 `AstralOath.Client.CharacterPresentation`，不结算战斗和奖励。

`PresentationClock` 统一管理播放时间、速度和有声资格。暂停不推进时间；seek、逐帧和速度变化会锁定静音，只有从零 reset 恢复正常 1x 声画播放。`PresentationGraphScheduler` 是已验证时序数据的独立纯 C# 调度器，支持并行、延迟和按 actor/name 匹配的事件相对时序；未验证节点不会执行。

`StgPlaybackSession` 直接保留和遍历导入 graph.source 的 entities/connections/entrypoint/eventlisteners。ANI 完成和 ONEVENT 匹配分开处理，ONEVENT 保留源动画名；不把未知 when/location 改写成绝对时间。

默认严格模式对编码条件给出诊断；实验模式明确提示 when=1 起始、缺省连接完成，以及外部显式注入的试验目标锚点。实验 CAM/MOVE/ZOOM 不声称原始曲线一致。CFX/timeline/粒子/CUTIN 的资源预加载与视觉执行位于 CharacterLab 的 CharacterEffectStage，与本调度器消费相同 Tick。

`Tests~/CharacterPresentationClockTests.cs` 是 .NET 10 无 Unity 依赖的实际行为断言；最初空实现已观察到播放时钟断言失败，再实现后通过。Unity 图回归在 `Assets/Game/Tests/EditMode/CharacterPresentationGraphTests.cs`，由协调者统一执行。

本模块的测试与编译不能证明九技能原作还原。运行库兼容性、目标 Player 资源加载、正常速度音画和用户监督的原作对照需要分别记录结果。


回归包括 PresentationFade 的纯 C# 预览包络断言，以及 SHAKE 不累积漂移、STOP 按 ID 清理的 Unity 图回归；具体数量与结果以当次测试输出为准。DARK的总时长包含淡出是实验室明确选择，不作为原作时序已验证。


SpriteBlurSettings 校验原始 PPEFFECT uniform 并保留 shader 的样本上限与 ratio clamp；PresentationScalar 在入队前识别未知范围语法与非有限值。新增真实 CFX 范围回归和 PPEFFECT 仅按源连接触发的回归。后处理 shader 数学移植有源码证据，整体 sprite 作用范围和 SINE_VALUE 参数驱动仍保留差异。
