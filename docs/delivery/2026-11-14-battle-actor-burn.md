# 角色表现预制、燃魂与回合语音

日期：2026-11-14。基线：5ca702b。

- 烧魂准备会选择对应技能。取消只解除燃魂及燃魂描边，立即恢复该技能的普通高亮和正常目标选择。两项旧行为回归先失败，修复后三项 Burn PlayMode 通过，含取消后直接普通施放、普通魂力收入以及真正烧魂双击的扣费。
- 魂消散使用本机原 effect/hud_soul_consume.cfx、soul_grow 和 soul_consume_pati，源闭包与哈希见 Res/Effects/Hud/source.json。权威接受施放后读取己方的扣费事件绝对值，每个消失的整魂槽原位播放骨骼和两组粒子；约两秒回收。暂停冻结，重开释放。未重建飞魂轨迹。
- 通用 BattleActor 预制包含 Shadow 和禁用的 StatusTemplate（Frame、HP、Element、Level、Shield Fill、Status Icons）。护盾、无敌等表现资源由预制上的 BattleActorStatusTemplate 引用 UnitPresentation 配置。实战在现有 Canvas 下创建并拥有每位角色的状态条，重开换实例，长按/死亡/跟随走现有流程。Unity 迁移两份场景均移除了八个旧场景阴影；工作场景角色 ID、位置、旋转、缩放的覆盖逐项保持。原件位于 D 盘 .artifacts/burn-selection/original-assets。
- 近战 approach 从默认 0.9 扩到 1.18～2.2 世界单位，按地面占位并限制最终前冲，保留镜像和已经很近时不后撤。零偏移与 80 源像素前冲各跑双向回归，旧代码分别只剩 0.9 和 0.1，修复后分别保留 1.18 和 0.9；普通归位时点仍按原图。具体通用限制见[源演出规则](../development/battle-source-presentation.md)。
- 20 名角色的已导入语音目录都含 ani/b_idle_ready。原事件由 FMOD 选择日语样本；技能语音、烧魂声音和模型衣物动作音分开。首次显示开局以及前一技能演出结束、实际交接回合时，按回合编号和行动者实例去重。Windows 原生 FMOD 探针 20/20 收到日语 Programmer Sound 回调且主混音非静音；日志位于 .artifacts/roster-data/turn-ready-audio/native.log。未确认原引擎精确调度点。

## 复验

- 原始 HUD 资源闭包校验 39/39；图形 EditMode 72/72。实际 Unity 执行 MigrateActorStatusTemplates 和 MigrateActorShadows；重新生成时仍使用共用预制，资源 GUID 保留。
- 实景 PlayMode 第一轮 26/31；5 项失败分别涉及重开后旧状态条引用、测试反射参数或裂伤测试夹具。修正夹具后，5 项均复验通过；联合日语待命、尘土、烧魂消散和取消共 10/10 通过。开局日语一次，重复刷新/暂停继续不增加，下一回合在实际交接时播放。
- 交付的是客户端表现；未改共享战斗规则/协议。客户端资源和程序集应一起发布，未构建目标平台更新包。未执行本轮的原作逐帧对照。

项目命令：Tools/Invoke-Unity.ps1 的 EditMode 图形过滤 BattleActorShadowTests、BattleSourceMotionTests、BattleHeroPrefabTests、BattleSkillTimeline、BattleParticleColorRamp、BattleDeathPresentation、BattleCfxAnimationSelection；PlayMode 重点过滤 BattleInteractionTests、BattleSoulGaugeConsumptionTests、BattleHeroFormationTests、BattleUnitPresentationIntegrationTests、BattleCinematicStatusTests、BattleViewportLayoutTests、BattleCutinTimelineTests、BattleTurnReadyVoiceTests。结果在 D 盘 .artifacts/burn-selection。
