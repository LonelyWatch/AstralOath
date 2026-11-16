# 魂燃头像与派生行动反馈

本项对应同日画面对照清单 07、08。主工程在 D 盘，D:/sevenepic 与原录像只读。权威事件只触发一次，视觉时钟随暂停停止；反馈不进入技能 Busy、不改变命中排程或伤害结算。

## 原录像证据与限制

证据来自用户提供的完整 P2：`2.3060-3091_43325065328.mp4`，1920×1080、30fps、34:20.83。提取目录为 `.artifacts/arena-audit-2026-11-17/reference/p2-action-check/`，其中 `source.json`、各段 `timing.json`、`measurements.json` 记录来源与采样规则。

- 第一战 07:38 入场、约 08:47 胜利。两名角色在 08:04.4 出现“反击”文字，其后动作起闪分别在 **08:05.133、08:06.833**（逐帧定位精度 1/30 秒）。都是红白闪电、径向光线与圆环，对应 `stse_chain` 图集构件；不是 `stse_counter` 的蓝色拳头状态图标。连续帧见 `counter-onset-1-30fps.jpg`、`counter-onset-2-30fps.jpg`。
- 08:26.0 起出现原生金色 `ONE MORE TURN` 环，08:26.2–08:26.667 可辨文字，约 08:26.9 主环消失，粒子尾迹随后结束。它位于行动者躯干下方，在上一技能收尾后出现；08:29 同一角色再次获得决策回合。见 `extra-turn-30fps.jpg`。
- 第一战双方头像每 8 帧抽取，共 263 帧，未观察到魂燃头像火焰。后续 12:30–34:20 每半秒抽取 2622 帧并用蓝色变化筛选，再查看完整场景确认：对方在 **13:40.9 尚未起燃、13:41.0 已出现覆盖画面的蓝白入场光线**，13:41.3 后持续燃烧，覆盖初次攻击及随后黑龙派生演出，约 13:47.0 中心光晕停止，粒子尾迹至约 13:47.9 消失；我方另一次在 15:41–15:49.5 持续，15:50 已消失。不能把 1 秒 intro 加一轮 0.3333 秒 loop 当作完整魂燃寿命。
- 13:44.5 黑背景下，对方火焰蓝色像素范围 x=1431..1690，宽 **260px**，顶部被画面裁切。筛选条件和取样区域见 `measurements.json`。原头像约 130px；入场光线能跨越战斗画面。此测量用于选择 .36 原像素缩放，仍需新 GPU 图核对。
- 反击圆环的手工范围约 900–1200px，角色约 280–310px；旧项目截图约 400–420px、角色约 260px。不同角色、镜头和动画相位不支持精确像素相等，但明确支持撤销 .003 的缩小，恢复 .01 原像素缩放。新夹具分别截 .10 与 .18 秒，供同相位复核。
- **夹攻、追加攻击的独立原版发生例仍未确认**。二者保留已有短闪复用，事件类型和文案独立，不能记为录像还原已通过，也不使用额外回合箭头。

## 素材与行为

`ImportActionFeedback.py` 只导入四个原生 CFX 闭包，26 个文件和 `source.json`。CFX、粒子字段保留；SCSP 使用既有转换器；atlas 原字节保留；manifest 记录源和输出 SHA-256。

- `ui_livepvp_soulburn_eff.cfx`：原 `intro` 1 秒、`loop` 0.3333 秒、粒子 scale=.75。双方玩家头像使用 .36 原像素缩放。只在首次消费 primary `Action.UseSoulBurn` 时启动。独立 opt-in `PlayUntilFinished` 保留完整入场，然后持续原循环；完整动作队列、派生动作、反馈及恢复尾段均结束后，沿既有 `!Busy` 边界调用幂等 `Finish`，Spine 淡出 .12 秒，同时停止粒子发射；已有粒子继续按原寿命、颜色和运动曲线排空。原粒子寿命 .4±.3 / .8±.5 秒，最大 1.3 秒，实际可见尾迹由源透明度决定，匹配录像约 .9 秒尾迹这一机制。Spine 的 .12 秒仍是项目短收束参数，不声称逐像素一致。极短动作也先完成原 intro。清快照、退出和销毁立即停止。
- `stse_chain.cfx`：前后层原 Spine 各 .5 秒，前层 scale=1.2。用于录像证实的反击行动；夹攻、追加攻击保持暂定复用。使用 .01 原像素缩放，跟随当前模型边界中心，禁止镜像字图。
- `stse_counter.cfx`：源 `cs_player` 122/122k 证明它是反击状态附加特效，`top` 骨点、y=150、scale=1.4。资源仍在原闭包中，但不再作为反击动作起闪使用。
- `stse_addturn.cfx`：原 `stse_addturn_renew` 1.1666 秒，y=-100、scale=1.2，粒子延迟 167ms。保留既有最后命中后调度，只响应 `ExtraTurnGranted`，以当前模型边界中心为根并保留原偏移。图形夹具复拍见 `fixture-extra-turn.png`。

角色在全屏演出中隐藏时，角色提示也隐藏，时钟继续前进，不在演出后补闪。头像预选、取消和魂量快照不触发魂燃。

## 实现与接线

`BattleFeedbackCueTracker` 对权威事件序号去重，允许晚于后续行动执行的额外回合回调；按 `LastEventSequence` 清旧事件，新局从 0 开始。纯 source selector 单独回归反击不得误用状态图标。

`BattleHudParticles.StopEmission` 是新 opt-in：只阻止后续 burst/连续发射，保留当前粒子，`HasLiveParticles` 供宿主判断尾迹结束。已有 `Stop` 仍立即清空，`Play` 重置发射状态；其他调用者未切换到该模式。

`BattleNativeFeedbackEffect` 使用独立 Spine/粒子实例，默认 `Play` 仍是有限 one-shot，只有显式 `PlayUntilFinished` 才允许持有原 intro/loop。`Finish` 重入不重启淡出。它不借用角色播放器。Spine 透明度使用 renderer property block；粒子拥有私有材质并允许修改自身透明度，不得污染其他实例。五张粒子纹理保持源字节并按 PMA 播放。

`BattleActionFeedback` 管理角色提示，`BattleOnlineHud` 管理头像。`BattleSimulationPreview` 仅在现有 `AdvancePresentation` 后确认 `!Busy` 时结束头像循环；不把提示本身加入 Busy。场景资源仍经 `BattleActionFeedbackAssets.AppendTo` 并入既有 `skillAssets`，不增加全局单例或常驻 Resources。

## 本轮验证状态

- 实际纯 C# 回归：反击素材用例先 10/11 失败（得到 stse_counter），修复后 **11/11 通过**。日志 `.artifacts/battle-action-feedback/feedback-counter-{red,green}.log`。
- 历史素材闭包/PMA 3/3 与 26 文件 `--check` 已通过；本轮未改源素材或导入器。
- 当前 runtime、全部 PlayMode 源及 **9 个 focused EditMode 用例编译通过**。增加了长演出循环、早结束保留 intro、幂等淡出、禁止无原循环 one-shot 无限播放，以及自然排空/旧即时 Stop/独立实例/重播回归；修正了原测试把私有粒子材质误判为共享 Spine 材质的断言，并补实例隔离检查。这些用例包含在最终真实 GPU 的 EditMode 34/34 通过记录中。
- PlayMode 夹具使用手动 Camera.Render/RenderTexture，不依赖可见窗口。最终图形 PlayMode 24/24 通过，输出位于 `.artifacts/arena-audit-2026-11-17/p2-verified/feedback/`，共 11 张：双方 intro、双方 loop、双方 drain、counter onset、counter .18 秒、dual、extra attack、extra turn。它验证本项目可渲染与生命周期，不能替代权威在线全链路或未观察到的录像事件。
- 旧 `.artifacts/arena-audit-2026-11-17/verified/feedback/` 6 张图在本轮之前生成，包含错误反击图标及旧缩放，仅作修复前对照，不能代表当前验收。
