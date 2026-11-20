# AstralOath.Shared

`Runtime/Battle` 是客户端模拟战与服务端共用的纯 C# 战斗内核。服务端的 `BattleMatch.Submit` 校验回合、选定目标和魂力，再由 `BattleSkillResolver` 按正式配表生成事件。战斗种子控制命中、抗性、协攻及随机技能分支。角色表现消费已选定的表现图和事件。

规则版本 17 支持 70 名英雄。按结算时的防御状态和在场亚乌利斯计算防御，受击者的烧伤、出血也读取穿透抗性，自身防御加伤读取施法者的有效防御。进战时保留怪物品阶。冻伤阻止受到的伤害降低及分配，破裂在每次攻击的普通伤害之后逐名结算。璐璐卡「复仇的波动」叠加的攻击加成保存在角色状态快照中，同场技能与重连续接读取同一状态。 [效果复核](../../docs/delivery/2026-11-20-roster61-70-effect-audit.md)

配置入口及测试见 [Config](../../Config/README.md)、[战斗执行](../../docs/development/battle-execution.md) 与[第 61–70 位角色机制](../../docs/delivery/2026-11-20-roster61-70-skills.md)。
