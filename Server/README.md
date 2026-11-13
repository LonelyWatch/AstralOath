# AstralOath.Server

.NET 10 模块单体入口位于 `Host`。Foundation 模块持久化 Echo 请求并提供健康检查，`Tests` 覆盖协议和真实 MySQL 集成。开发、迁移和运维命令见 [服务端开发手册](../docs/development/server.md)，模块边界和数据访问决定见 [服务端架构](../docs/architecture/server.md)。

战斗验证入口按房间串行处理两侧命令，并通过事件游标与快照续接。它使用构建时嵌入的 Luban 战斗快照，只在本机 Development 显式开启。启动与请求协议见[战斗第三步](../docs/development/battle-execution.md)。

战斗规则版本为 3。挑衅引起的强制 S1 由房间在同一锁内自动提交。响应可能带连续多条命令的事件和最后快照，客户端以下一个 `lastCommandSequence + 1` 发新命令。重试返回当时的原快照和事件，按事件序号续接。

标准战斗每队最多 80 点魂力。SoulChanged 携带变更后的绝对值，客户端每 10 点显示一个完整魂。燃魂仍由接受的指令和权威魂力余额决定。

战斗单位快照可选带 `resource`，记录专注或斗志的类别、当前值与上限；`ResourceChanged` 事件携带更新后的绝对值。行动值仍走原有回合顺序。受击资源消耗按权威攻击计算，客户端源动画可以呈现多个 HIT。[字段与范围](../docs/delivery/2026-11-13-battle-unit-feedback.md)。
