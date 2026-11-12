# AstralOath.Server

.NET 10 模块单体入口位于 `Host`。Foundation 模块持久化 Echo 请求并提供健康检查，`Tests` 覆盖协议和真实 MySQL 集成。开发、迁移和运维命令见 [服务端开发手册](../docs/development/server.md)，模块边界和数据访问决定见 [服务端架构](../docs/architecture/server.md)。

战斗验证入口按房间串行处理两侧命令，并通过事件游标与快照续接。它使用构建时嵌入的 Luban 战斗快照，只在本机 Development 显式开启。启动与请求协议见[战斗第三步](../docs/development/battle-execution.md)。
