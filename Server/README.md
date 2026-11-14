# AstralOath.Server

.NET 10 模块单体入口位于 `Host`。Foundation 模块持久化 Echo 请求并提供健康检查，`Tests` 覆盖协议和真实 MySQL 集成。开发、迁移和运维命令见 [服务端开发手册](../docs/development/server.md)，模块边界和数据访问决定见 [服务端架构](../docs/architecture/server.md)。

战斗验证入口按房间串行处理两侧命令，并通过事件游标与快照续接。它使用构建时嵌入的 Luban 战斗快照，只在本机 Development 显式开启。启动与请求协议见[战斗第三步](../docs/development/battle-execution.md)。

战斗规则版本为 12，当前 30 名英雄使用同一权威内核。客户端与服务器需携带同一生成配表。挑衅引起的强制 S1 由房间在同一锁内自动提交。响应可能带连续多条命令的事件和最后快照，客户端以下一个 `lastCommandSequence + 1` 发新命令。重试返回当时的原快照和事件，按事件序号续接。

标准战斗每队最多 80 点魂力。SoulChanged 携带变更后的绝对值，客户端每 10 点显示一个完整魂。燃魂仍由接受的指令和权威魂力余额决定。

战斗单位快照可选带 `resource`，记录专注或斗志的类别、当前值与上限；`ResourceChanged` 事件携带更新后的绝对值。行动值仍走原有回合顺序。受击资源消耗按权威攻击计算，客户端源动画可以呈现多个 HIT。[字段与范围](../docs/delivery/2026-11-13-battle-unit-feedback.md)。

`UnitRevived` 事件携带复活时的当前及最大生命。复活目标校验、状态周期与追击在共享内核执行。 [本批规则与边界](../docs/delivery/2026-11-14-roster21-30.md)

## 模拟战目录、房间与竞技场聊天

启动前用原有迁移入口应用 `002_arena_rooms.sql`。服务运行账户需要 `arena_player` 的 SELECT/INSERT，`arena_room` 的 SELECT/INSERT/UPDATE/DELETE，`arena_room_member` 的 SELECT/INSERT/DELETE，以及 `arena_room_message` 的 SELECT/INSERT。服务启动沿用 `Server` 的现有监听和 TLS 约束。本机明文开发入口为 `http://127.0.0.1:5080`。其他地址使用 HTTPS。

- `POST /api/arena/sessions` 用 `{displayName, avatarId}` 创建独立访客会话。返回 `{token, player}`，后续请求使用 Bearer。服务端持久化随机令牌的 SHA-256，返回给客户端的明文令牌需要由客户端保存以便重连。
- `GET /api/arena/rooms` 支持 `visibility=all|public|private`、`sort=recent|players` 和 `query` 名称搜索，返回最多 100 个房间。私密房只返回密码标志。创建房间用 `POST /api/arena/rooms` 提交 `{name, password, allowChat, rule}`。名称最多 28 个 Unicode 字符，设密须四位 ASCII 数字，服务器只保存带随机盐的 PBKDF2 摘要。
- `GET /api/arena/rooms/current` 可使用同一个令牌恢复当前房间，无房间时返回 204。成员用 `GET /api/arena/rooms/{id}` 查询状态，用 `POST /api/arena/rooms/{id}/join` 携带 `{password}` 入场，用 `POST /api/arena/rooms/{id}/leave` 离场。每人同时属于一个房间，每房最多 50 人。正常离开时房主转给最早入场成员，末位成员离开即删除房间和聊天。进程中止或客户端断线保留原会话及成员关系，本阶段未设自动回收期限。
- `GET /api/arena/rooms/{id}/messages?after=...` 使用消息 ID 游标，初次最多返回最近 50 条，续读最多返回 100 条。成员可通过 `POST /api/arena/rooms/{id}/messages` 提交 `{text}`，最多 240 个 Unicode 字符。服务端验证成员资格及房间聊天许可，限制发送频率。该频道只对应当前竞技场房间。
- 五种规则记录到房间。开始选禁、席位准备、战斗编排和正式账号系统属于后续链路。

使用忽略目录中的专用测试配置设置 `ASTRALOATH_MYSQL_TEST`，执行 `dotnet test Server/Tests --filter FullyQualifiedName~ArenaRoomTests`。覆盖两会话的房间发现、公开过滤、错误密码、重复入场、并发入场、房主转移、跨服务重启恢复、聊天读写与禁聊。
