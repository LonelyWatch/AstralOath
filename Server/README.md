# AstralOath.Server

.NET 10 模块单体入口位于 `Host`。Foundation 模块持久化 Echo 请求并提供健康检查，`Tests` 覆盖协议和真实 MySQL 集成。开发、迁移和运维命令见 [服务端开发手册](../docs/development/server.md)，模块边界和数据访问决定见 [服务端架构](../docs/architecture/server.md)。

战斗验证入口按房间串行处理两侧命令，并通过事件游标与快照续接。它使用构建时嵌入的 Luban 战斗快照，只在本机 Development 显式开启。启动与请求协议见[战斗第三步](../docs/development/battle-execution.md)。

战斗规则版本为 12，当前 30 名英雄使用同一权威内核。客户端与服务器需携带同一生成配表。挑衅引起的强制 S1 由房间在同一锁内自动提交。响应可能带连续多条命令的事件和最后快照，客户端以下一个 `lastCommandSequence + 1` 发新命令。重试返回当时的原快照和事件，按事件序号续接。

标准战斗每队最多 80 点魂力。SoulChanged 携带变更后的绝对值，客户端每 10 点显示一个完整魂。燃魂仍由接受的指令和权威魂力余额决定。

战斗单位快照可选带 `resource`，记录专注或斗志的类别、当前值与上限；`ResourceChanged` 事件携带更新后的绝对值。行动值仍走原有回合顺序。受击资源消耗按权威攻击计算，客户端源动画可以呈现多个 HIT。[字段与范围](../docs/delivery/2026-11-13-battle-unit-feedback.md)。

`UnitRevived` 事件携带复活时的当前及最大生命。复活目标校验、状态周期与追击在共享内核执行。 [本批规则与边界](../docs/delivery/2026-11-14-roster21-30.md)

## 模拟战目录、房间与竞技场聊天

启动前用原有迁移入口应用 `002_arena_rooms.sql`、`003_game_operation_audit.sql`、`004_arena_chat_stickers.sql`、`005_arena_room_member_lease.sql`。服务运行账户需要 `arena_player` 的 SELECT/INSERT/UPDATE（`FOR UPDATE` 行锁需要 UPDATE 权限），`arena_room` 的 SELECT/INSERT/UPDATE/DELETE，`arena_room_member` 的 SELECT/INSERT/DELETE，`arena_room_member_lease` 的 SELECT/INSERT/UPDATE，`arena_room_message` 和 `arena_room_message_sticker` 的 SELECT/INSERT，以及 `game_operation_audit` 的 INSERT。服务启动沿用 `Server` 的现有监听和 TLS 约束。本机明文开发入口为 `http://127.0.0.1:5080`。其他地址使用 HTTPS。

- `POST /api/arena/sessions` 用 `{displayName, avatarId}` 创建独立访客会话。返回 `{token, player}`，后续请求使用 Bearer。服务端持久化随机令牌的 SHA-256，返回给客户端的明文令牌需要由客户端保存以便重连。
- `GET /api/arena/rooms` 支持 `visibility=all|public|private`、`sort=recent|players`、`direction=asc|desc` 和 `query` 名称搜索，返回排序后的前 100 个房间。默认 `sort=recent&direction=desc`。按人数排序的同人数房间优先返回较新的房间。私密房只返回密码标志。创建房间用 `POST /api/arena/rooms` 提交 `{name, password, allowChat, rule}`。名称最多 28 个 Unicode 字符，设密须四位 ASCII 数字，服务器只保存带随机盐的 PBKDF2 摘要。
- `GET /api/arena/rooms/current` 可使用同一个令牌恢复当前房间，无房间时返回 204。成员用 `GET /api/arena/rooms/{id}` 查询状态，用 `POST /api/arena/rooms/{id}/join` 携带 `{password}` 入场，用 `POST /api/arena/rooms/{id}/leave` 离场。每人同时属于一个房间，每房最多 50 人。成功读取房间状态、重新入场或发送聊天会续租成员，旧客户端使用的房间状态接口仍然适用。成员租约为 60 秒，同一成员的租约写入间隔至少 10 秒。服务每 10 秒扫描过期房间。手动刷新目录也会处理已到期房间。进程中止或网络断开超过租约后，成员由服务端回收，后续读取返回 403 `not_in_room` 或 404 `room_not_found`。正常离开或房主租约到期时，房主转给最早加入仍活跃的成员，末位成员离开或过期即删除房间和聊天。迁移为已有成员提供一次完整 60 秒宽限。服务端会话身份与房间租约独立。
- `GET /api/arena/rooms/{id}/messages?after=...` 使用消息 ID 游标，初次最多返回最近 50 条，续读最多返回 100 条。`waitMs` 可选，范围为 0 到 8000。提供正数时，已有消息立即返回，暂无时在同一服务实例中等待已提交消息的通知或超时，然后查询 MySQL。等待期间释放数据库连接。省略该参数或传 0 保持即时查询。成员可通过 `POST /api/arena/rooms/{id}/messages` 提交 `{text}` 或 `{kind:"text",text}`，最多 240 个 Unicode 字符。原版表情使用 `{kind:"sticker",stickerId}`，稳定 ID 见 `Host/Arena/arena-chat-stickers.txt`。响应包含 `kind`，表情消息同时返回 `stickerId` 和空 `text`。旧文本记录读取为 `kind=text`。服务器对两种消息使用相同的成员资格、房间聊天许可和频率校验。表情数据随消息清理，只接受 67 个原图标识。操作审计不保存正文、表情 ID 或资源路径。该频道对应当前竞技场房间。
- `POST /api/arena/telemetry` 需要访客会话，接收 `{eventId,name}`。客户端生成非空 N 格式 GUID，并按操作使用 `directory_refresh`、`directory_filter`、`directory_search` 或 `create_dialog_open`。同一主体重复提交同一 ID 返回 204。接口限制请求体和来源请求频率，只接收明确字段。客户端以尽力方式提交。
- `game_operation_audit` 保存语义操作、服务端时间、主体类型和内部 ID，以及有需要的对象类型和 ID。服务器在创建、实际加入、实际离开、成员租约到期和发送聊天成功的业务事务内写入权威留痕，重复加入、重复离开和失败请求不产生成功记录。客户端观测点包含 `client_event_id`，权威记录该字段为空。表不包含玩家名称、密码、聊天内容、搜索词或 IP。房间被清理后留痕仍可查询。访问日志的人使用独立只读权限。归档和保留期限由运维在上线前配置。
- 五种规则记录到房间。开始选禁、席位准备、战斗编排和正式账号系统属于后续链路。

使用忽略目录中的专用测试配置设置 `ASTRALOATH_MYSQL_TEST`，执行 `dotnet test Server/Tests --filter FullyQualifiedName~ArenaRoomTests`。覆盖两会话的房间发现、公开过滤、错误密码、重复入场、并发入场、房主转移、跨服务重启恢复、聊天读写与禁聊。
