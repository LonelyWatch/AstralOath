# AstralOath.Server

.NET 10 模块单体入口位于 `Host`。Foundation 模块持久化 Echo 请求并提供健康检查，`Tests` 覆盖协议和真实 MySQL 集成。开发、迁移和运维命令见 [服务端开发手册](../docs/development/server.md)，模块边界和数据访问决定见 [服务端架构](../docs/architecture/server.md)。

战斗验证入口按房间串行处理两侧命令，并通过事件游标与快照续接。它使用构建时嵌入的 Luban 战斗快照，只在本机 Development 显式开启。启动与请求协议见[战斗第三步](../docs/development/battle-execution.md)。

战斗单位快照可选带 `resource`，记录专注或斗志的类别、当前值与上限；`ResourceChanged` 事件携带更新后的绝对值。行动值仍走原有回合顺序。受击资源消耗按权威攻击计算，客户端源动画可以呈现多个 HIT。[字段与范围](../docs/delivery/2026-11-13-battle-unit-feedback.md)。
