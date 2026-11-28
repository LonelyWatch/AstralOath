# AstralOath.Server

固定防守竞技场新增迁移023。服务端冻结真实装备与出战阵容，执行玩家技能与防守 AI，按账号串行确认首通、段位及征服点。见 [配置、HTTP 与重打/并发行为](Host/ArenaTower/README.md)。

## 玩家资料与公共频道（2026-11-28）

新增公开玩家UID：游客/注册身份创建事务即分配随机且不重复的10位数（1000000000–9999999999），MySQL独立 `player_public_identity` 唯一约束与有界冲突重试，内部GUID/鉴权不变。Profile新增字符串 `uid`，`GET /api/profile/uids/{uid}` 为有效Bearer提供同区服的精确玩家资料查询，沿用他人隐私；尚未实现好友关系。候选启用前必须用完整 `migrate` 应用021及其C#一次性初始化，先停旧身份写入、备份，给运行账号新表SELECT/INSERT；仅执行SQL文件不能补齐旧玩家/资料回执。无需清空测试数据，重建空库也沿同一初始化链。新增6项UID及完整683/683真实隔离MySQL通过，另实际020→021/重入演练保持UID不变。准确格式、数据影响、前后证据及未部署边界见 [Social公开UID](Host/Social/README.md#公开uid分配与迁移2026-11-28)。

新增 [Social模块与准确API契约](Host/Social/README.md)，资料读取复用本人/他人投影，支持免费改昵称/查重、50字问候语、双战绩可见开关、真实头像/库存代表摘要。没有权威账号Rank/榜单时明确available=false。公共聊天独立于竞技场房间，按服务器realm+频道1–9999隔离，支持文字/67目录表情、游标/长轮询/NDJSON、收藏与通知偏好；默认频道1，骑士团广播不冒充实现。

候选服务启用前需专用身份依次应用新增迁移018/019/020（新增八表SELECT/INSERT/UPDATE），旧同名不清洗、不对原列加UNIQUE；Entry初始身份/首次入区与Social改名通过同一数据库名字键仲裁，登录凭据与稳定ID不变。公共频道从分配ID至提交持有独立channel数据库X锁，避免乱序提交永久漏游标，不串行其他频道。写入/幂等/审计同事务，audit不存自由文本，通知四字段缺失400。隔离MySQL实际Social32/32、完整Server677/677通过0跳过，含真实15秒Token复核断流；RED与GREEN收据在 `.artifacts/home-profile-chat-review/server-tests/`。本轮未迁移主库、替换主服务或分发；客户端视听另行验收。[计划与参考边界](../docs/plans/2026-11-28-home-profile-chat.md)。

2026-11-27新增游戏内邮件，入口为 [Mail模块](Host/Mail/README.md)，唯一目录 [Config/Mail/catalog.json](../Config/Mail/catalog.json)，新迁移017（未修改已应用011–016）。按owner→mailbox→delivery顺序锁定，同一个MySQL事务提交领取、真实钱包/材料/行动力、英雄/神器/装备实例、图鉴、回执、版本outbox和审计。收件上限100；历史为最近90天与最新100条的交集，裁剪查询不删除防重账本。打开详情只记已读；服务端UTC到期与权威领取才释放收件容量。

玩家 `/api/mail/catalog`、`summary`、`inbox?cursor=&limit=`、`history?cursor=&limit=`、`{deliveryId}`、POST `claim`/`claim-all` 和NDJSON `events` 均使用Arena Bearer与固定已选区服。列表默认50，最大100；稳定游标由服务端生成。单领请求 `{requestId,deliveryId}`；批领 `{requestId,deliveryIds}` 固定最多100封快照，GM始终略过。每封多附件原子；批领仅对已领/过期/GM及受控容量或安全上限失败跳过，不吞数据库异常。无可领取返回409 `no_eligible_mail`。成功回执包含claimed/skipped、合并rewards、affectedKinds、summary及表现标识；同请求重试回原回执，同ID改参数409，同封换ID也不能再发奖。

`summary.energy` 返回真实balance、naturalCap=360、absoluteCap=10亿及独立`revision`；`summary.balances`为`{revision,gold,crystals}`，crystals读取实际skystones钱包，不伪造未实现的PVP旗帜。两处revision同值，在同一个owner锁内读取持久材料流水与钱包流水总条数；GM设置或其他资源写入不必增长mailbox.version，实际余额改变会追加真实流水并增长资源revision。初始化/迁移前既有余额视为首次快照基线，此后按不裁剪的账本单调增长，不能直接删除/改写流水；未来归档须先迁移保留单调版本，不能裁剪后继续用Count。旧成功原回执缺字段时energy.revision为0、balances可为空，客户端不得覆盖更高资源版本，另重读summary。energy初始0，邮件可超过自然恢复上限，GM资源页设置同一个账本；本轮没有自然恢复或关卡消费。奖励的冻结规格和内部operation由服务器产生，材料流水先建operation并合并同类，装备一封内统一source_index，钱包/实例来源为 `mail-<deliveryId>`。

开发自发仅在Development+回环listener注册 `/api/development/gm/mail/preview` 与`send`。前者接受模板、普通/GM、标题正文、发件人与附件/天数覆盖，后者仅 `{requestId,previewId}`；额外收件、区服、scope或其他未知字段400。身份只从当前Bearer取；正式或非回环Host404。发信只产生邮件，收信才发奖励。服务端运营只用本机 `mail preview/confirm/status/retry` 和 [中文Mail.ps1](Ops/Mail.ps1)，没有运营HTTP；IDs/白名单/显式全区恰选一项，黑名单只做减法，整区冻结已进区注册玩家。确认最多一批，断进程后worker续投，满箱只允许原批重试且不延到期。区服名册由选服同事务维护，迁移从曾进区注册账号回填；游客仅可自发，不混入全区名册。

事件流先订阅后sync，后续只送版本；每次发送及15秒心跳复验Bearer，令牌轮换关闭旧流。mailbox.version与outbox在同一owner事务提交。MySQL自增outbox ID不是提交顺序，因此每Host仅对当前活跃订阅owner按最多100人的公平批次读取已提交mailbox.version，Hub独立高水位只推更高版本并合并慢连接。该校准完整覆盖跨账号乱序提交，重启只读当前header、不重放全历史、不删除全局outbox，也不需要新增consumer×outbox确认表。最后一个订阅关闭同时移除owner高水位；重新订阅以首次sync和最新header校准。后台和读取都纠正到期。权威审计键为 `mail.delivery`、`mail.claim`、`mail.batch.confirm`、`mail.batch.retry`、`development.gm.mail.send`，不包含标题、正文、名单原文或凭据。

本轮独立MySQL schema为 `astraloath_mail_qa_20261127`，初次路由/事务失败、过期修正、溢出与null失败和最终回归证据位于 `.artifacts/mail-system-review/server/`。首轮完整Server回归641项实际通过（Mail42项），均启用真实MySQL，无跳过，结果为 `server-full-final.trx`。Production真实CLI冻结/名单/确认、隔离`.6` Host断批重启接续亦有 `cli-live.json`、`host-restart.json`；恢复脚本曾在等待启动子进程返回处阻塞，续投结果由独立SQL/Host状态复核，未把脚本退出记为通过。`.4`既有服务和库未修改。客户端画面/声音、签名包及分发由整轮交付另验，本节不把API通过等同视听还原或已分发。

独立复审后新增四项回归，最终当前源码完整645/645通过、0跳过（`server-review-final.trx`，Mail46项）。`review-red.trx`先实际复现跨账号outbox乱序漏发、大写GUID已读未保存、资源摘要缺独立revision三项失败；修后`review-green.trx`三项通过，另验证205活跃owner分三批100/100/5全部收到。新Release候选SHA-256为`ECC7FF4BADBEF6F6B85D28E637F5E044755420E489727C1CCA56DF97C97F614A`，隔离`.6`PID41392；`qa-review-fixtures.json`记录新Host真实GM同邮箱版本资源变化、金币/天空石、规范ID持久已读、领奖与218ms版本事件。启动脚本改用仅继承NUL/日志句柄的隐藏原生进程，`start-return-green.json`实际capture调用1.703秒退出0；旧阻塞证据保留，不抹去历史失败。以上仅后端候选服，不代表客户端已分发。

复验示例（私密连接仅从忽略目录读取，勿输出）：先初始化 `Tools/ProjectEnvironment.ps1`，设置 `ASTRALOATH_MYSQL_TEST` 为独立连接，再用 `.tools/dotnet/dotnet.exe test Server/Tests -p:TargetPlatformSdkPath=D:/UnityProjects/AstralOath/.tools -p:TargetPlatformDisplayName=Windows --logger "trx;LogFileName=server-full-final.trx" --results-directory .artifacts/mail-system-review/server`。SDK7.0探测会尝试读取受限C盘目录，因此本机显式给出D盘SDK探测目录；NuGet漏洞源离线警告不代表依赖升级，锁定包仍实际还原/编译。

2026-11-26：迁移016为独立副属性行新增nullable `enhancement_count`。新生成初始掷值不计次数，既有词条每次实际强化+1，新解锁为0；GM合法配方保存Rolls-1，计数随数值同事务提交及幂等重放。响应新增`enhancementCountKnown`，已知才输出`enhancementCount`，不根据属性值反推旧GM次数。旧+0可确认0；旧+N只有完整唯一且无未来事件的历史才恢复，否则未知。迁移重入通过，迁移专用连接允许脚本变量，业务连接不变。完整服务端599项、独立候选服API的计数/强化/幂等/同类GM余额重读通过。[本轮细节及数据库状态](../docs/delivery/2026-11-26-equipment-followup.md)

战斗响应中的 `cooldownReduction` 随技能结算给出实减的回合数。菲奥雷托满专注使用「蝴蝶斩击」时，三技能未强化减少 6 回合，强化到 +3 减少 5 回合，两种情况下剩余值均为零。旧响应缺少该字段时，新客户端按零读取。[战斗复核](../docs/delivery/2026-11-22-battle-followup.md)

开发版 GM 在回环地址的 Development 游戏服注册 `GET /api/development/gm` 与 `POST /api/development/gm/actions`。已有会话的任一账号可查看和设置自己的目录余额（含装备/饰品强化材料），按目录发放英雄、神器及训练装备。绝对余额的范围为 0 至 10 亿，每次发放 1 至 10 件。账号取自 Bearer 会话，服务端在同一账号锁和事务内检查容量、登记请求、写库存及审计。同一请求 ID 的同参数重试读取当前结果，参数冲突返回 409。正式环境和对外监听不注册这组路由。测试入口为 `DevelopmentGmTests`。

共享战斗规则会随反击动作回传实际触发来源；查尔斯反击状态和技能被动反击分别回归。[动作事件与客户端表现](../docs/delivery/2026-11-22-battle-action-recovery.md)

装备分数从当前主副属性计算。服务端测试直接调用与客户端同源的共享方法，用六组国服实录数值校验。现有库存协议已包含计算所需字段。[装备评分与取整](../Shared/com.astraloath.shared/README.md)

## 开发 GM 自定义锻造

新目录只宣传源规则可支持的70/85级稀有/英雄/传说。`forgeRules.enhancementRulesVersion` 由新客户端随配方 `rulesVersion` 提交，主属性按 `MainValues[enhanceLevel]`，副属性按 `GradeRanges` 的当前品质范围及掷数/开放时机检查，发放带来源profile。旧请求不带版本时仍走下述历史近似规则以兼容旧回执，不获得可强化profile，不将旧属性静默迁为新曲线。[源表、精度/经济策略与 API](../docs/development/equipment-enhancement.md)

### 未带版本的旧客户端兼容配方

开发端读取 `GET /api/development/gm` 的 `forgeRules`，按服务端给出的部位、等级、品质、套装、主属性和副属性范围填写配方。提交到 `POST /api/development/gm/actions`，动作是 `forgeEquipment`，`key` 为六部位之一，`value` 为生成件数（1 至 10）。`forge` 包含 `itemLevel`、`enhanceLevel`、`grade`、`setId`、`mainKind`、按解锁顺序排列的 `substats[{kind,value,rolls}]`。15 级训练装备仍用原有 `equipment` 动作按模板发放。

70 和 85 级可选 +0 至 +15，90 级仅可选 +15。套装可选速度或暴击。武器主攻击，头盔主生命，铠甲主防御。项链、戒指、鞋可选攻防生的固定值或百分比，项链另可选暴击率和暴击伤害，戒指另可选效果命中和抗性，鞋另可选速度。武器副属性排除防御及防御百分比，铠甲副属性排除攻击及攻击百分比。主副属性和同件副属性不能重名。每项主属性值为该档的 `mainBase + mainStep × enhanceLevel`。有 `rolls` 次掷值的副属性，数值必须在 `rollMin × rolls` 和 `rollMax × rolls` 之间，并符合 `step`。接口的百分比数值以基点表示，100 基点为 1%。各档数值以项目的 [DevelopmentEquipmentForge.cs](Host/Inventory/DevelopmentEquipmentForge.cs) 为准。

各档计算可直接用等级索引 `r` 复算，70、85、90 级的 `r` 依次为 0、1、2。`e` 为强化等级，百分比行以界面显示的百分点表示。以下范围为单掷的上下限，单位均为整数。

- 固定攻击主属性为 `60 + 20r + (22 + 4r)e`，副属性每掷为 `12 + 2r` 至 `22 + 4r`。固定生命主属性为 `280 + 80r + (110 + 20r)e`，每掷为 `65 + 10r` 至 `115 + 15r`。
- 固定防御主属性为 `40 + 10r + (15 + 3r)e`，每掷为 `10 + 2r` 至 `20 + 2r`。速度主属性为 `6 + 2r + 2e`，每掷为 `1 + r` 至 `3 + r`。
- 攻防生百分比、效果命中和抗性的主属性为 `8 + 2r + 3e`，每掷为 `3 + r` 至 `6 + r` 个百分点。暴击率主属性为 `4 + r + 3e`，每掷为 `2 + r` 至 `4 + r` 个百分点。暴击伤害主属性为 `10 + 2r + 4e`，每掷为 `3 + r` 至 `6 + r` 个百分点。

每个品质的四个副属性槽按强化等级开放，0 表示初始开放。普通为 [3, 6, 9, 12]，高级为 [0, 3, 6, 9]，稀有为 [0, 0, 9, 12]，英雄为 [0, 0, 0, 12]，传说为 [0, 0, 0, 0]。普通的解锁顺序由本项目设定。每个 +3 里程碑只增加一掷，用于解锁新词条或提升已开放的词条。全件掷数须等于初始词条数加强化等级整除 3，较晚解锁的词条还要共享其后可用的里程碑。例如英雄品质第 4 条于 +12 获得 1 掷，到 +15 最多有 2 掷。高级至传说的开放顺序参照[工具作者公开记录](https://github.com/meowyih/meowyih.github.io/blob/master/epic7-gear/README.md#number-of-substats-for-different-gears)。

请求只向当前认证账号发放。配方不合法返回 `400 invalid_gm_forge`，数量非法返回 `400 invalid_gm_action`，装备容量不足返回 `409 equipment_inventory_full`。账号锁内一次提交库存、库存版本和审计。配方纳入请求摘要，同 ID 同配方重试不会重复入库。开发路由、旧训练发放、并发重试、容量回滚和锻造规则的 15 项服务端测试已通过，含隔离 MySQL 用例，报告位于 `.artifacts/equipment-manage-followup/server/all.trx`。Windows 开发母包内的实际发放和库存刷新需另行验收。

## 服务端运行与测试

.NET 10 模块单体入口位于 `Host`。Foundation 模块持久化 Echo 请求并提供健康检查，`Tests` 覆盖协议和真实 MySQL 集成。开发、迁移和运维命令见 [服务端开发手册](../docs/development/server.md)，模块边界和数据访问决定见 [服务端架构](../docs/architecture/server.md)。

战斗验证入口按房间串行处理两侧命令，并通过事件游标与快照续接。它使用构建时嵌入的 Luban 战斗快照，只在本机 Development 显式开启。启动与请求协议见[战斗第三步](../docs/development/battle-execution.md)。

战斗规则版本为 15，当前 60 名英雄使用同一权威内核。客户端与服务器需携带同一生成配表。挑衅引起的强制 S1 由房间在同一锁内自动提交。响应可能带连续多条命令的事件和最后快照，客户端以下一个 `lastCommandSequence + 1` 发新命令。重试返回当时的原快照和事件，按事件序号续接。

标准战斗每队最多 80 点魂力。SoulChanged 携带变更后的绝对值，客户端每 10 点显示一个完整魂。燃魂仍由接受的指令和权威魂力余额决定。

战斗单位快照可选带 `resource`，记录专注或斗志的类别、当前值与上限；`ResourceChanged` 事件携带更新后的绝对值。行动值仍走原有回合顺序。受击资源消耗按权威攻击计算，客户端源动画可以呈现多个 HIT。[字段与范围](../docs/delivery/2026-11-13-battle-unit-feedback.md)。

`UnitRevived` 事件携带复活时的当前及最大生命。复活目标校验、状态周期与追击在共享内核执行。 [本批规则与边界](../docs/delivery/2026-11-14-roster21-30.md)

## 模拟战目录、房间与竞技场聊天

房间选禁设置和当前回合候选的写入、读取及迁移见[联机选禁与入场](../docs/development/arena-draft.md)。

启动前用原有迁移入口应用 `002_arena_rooms.sql`、`003_game_operation_audit.sql`、`004_arena_chat_stickers.sql`、`005_arena_room_member_lease.sql`。服务运行账户需要 `arena_player` 的 SELECT/INSERT/UPDATE（`FOR UPDATE` 行锁需要 UPDATE 权限），`arena_room` 的 SELECT/INSERT/UPDATE/DELETE，`arena_room_member` 的 SELECT/INSERT/DELETE，`arena_room_member_lease` 的 SELECT/INSERT/UPDATE，`arena_room_message` 和 `arena_room_message_sticker` 的 SELECT/INSERT，以及 `game_operation_audit` 的 INSERT。服务启动沿用 `Server` 的现有监听和 TLS 约束。本机明文开发入口默认为 `http://127.0.0.1:7728`，可通过 `Server__Port` 覆盖端口。其他地址使用 HTTPS。

- `POST /api/arena/sessions` 用 `{displayName, avatarId}` 创建独立访客会话。返回 `{token, player}`，后续请求使用 Bearer。服务端持久化随机令牌的 SHA-256，返回给客户端的明文令牌需要由客户端保存以便重连。
- `GET /api/arena/rooms` 支持 `visibility=all|public|private`、`sort=recent|players`、`direction=asc|desc` 和 `query` 名称搜索，返回排序后的前 100 个房间。默认 `sort=recent&direction=desc`。按人数排序的同人数房间优先返回较新的房间。私密房只返回密码标志。创建房间用 `POST /api/arena/rooms` 提交 `{name, password, allowChat, rule}`。名称最多 28 个 Unicode 字符，设密须四位 ASCII 数字，服务器只保存带随机盐的 PBKDF2 摘要。
- `GET /api/arena/rooms/current` 可使用同一个令牌恢复当前房间，无房间时返回 204。成员用 `GET /api/arena/rooms/{id}` 查询状态，用 `POST /api/arena/rooms/{id}/join` 携带 `{password}` 入场，用 `POST /api/arena/rooms/{id}/leave` 离场。每人同时属于一个房间，每房最多 50 人。成功读取房间状态、重新入场或发送聊天会续租成员，旧客户端使用的房间状态接口仍然适用。成员租约为 60 秒，同一成员的租约写入间隔至少 10 秒。服务每 10 秒扫描过期房间。手动刷新目录也会处理已到期房间。进程中止或网络断开超过租约后，成员由服务端回收，后续读取返回 403 `not_in_room` 或 404 `room_not_found`。正常离开或房主租约到期时，房主转给最早加入仍活跃的成员，末位成员离开或过期即删除房间和聊天。迁移为已有成员提供一次完整 60 秒宽限。服务端会话身份与房间租约独立。
- `GET /api/arena/rooms/{id}/messages?after=...` 使用消息 ID 游标，初次最多返回最近 50 条，续读最多返回 100 条。`waitMs` 可选，范围为 0 到 8000。提供正数时，已有消息立即返回，暂无时在同一服务实例中等待已提交消息的通知或超时，然后查询 MySQL。等待期间释放数据库连接。省略该参数或传 0 保持即时查询。成员可通过 `POST /api/arena/rooms/{id}/messages` 提交 `{text}` 或 `{kind:"text",text}`，最多 240 个 Unicode 字符。原版表情使用 `{kind:"sticker",stickerId}`，稳定 ID 见 `Host/Arena/arena-chat-stickers.txt`。响应包含 `kind`，表情消息同时返回 `stickerId` 和空 `text`。旧文本记录读取为 `kind=text`。服务器对两种消息使用相同的成员资格、房间聊天许可和频率校验。表情数据随消息清理，只接受 67 个原图标识。操作审计不保存正文、表情 ID 或资源路径。该频道对应当前竞技场房间。
- `POST /api/arena/telemetry` 需要访客会话，接收 `{eventId,name}`。客户端生成非空 N 格式 GUID，并按操作使用 `directory_refresh`、`directory_filter`、`directory_search` 或 `create_dialog_open`。同一主体重复提交同一 ID 返回 204。接口限制请求体和来源请求频率，只接收明确字段。客户端以尽力方式提交。
- 模拟战的房间准备与设置通知依赖迁移 `008_arena_room_ready.sql`。设置真实变动时，服务端在同一事务中保存配置、清除准备并追加对应系统通知；通知随聊天游标广播。支持准备的客户端在开局请求中声明需要客席准备。

- `game_operation_audit` 保存语义操作、服务端时间、主体类型和内部 ID，以及有需要的对象类型和 ID。服务器在创建、实际加入、实际离开、成员租约到期和发送聊天成功的业务事务内写入权威留痕，重复加入、重复离开和失败请求不产生成功记录。客户端观测点包含 `client_event_id`，权威记录该字段为空。表不包含玩家名称、密码、聊天内容、搜索词或 IP。房间被清理后留痕仍可查询。访问日志的人使用独立只读权限。归档和保留期限由运维在上线前配置。
- 房间记录五种规则。基本规则的选禁及对战链路见下节。

使用忽略目录中的专用测试配置设置 `ASTRALOATH_MYSQL_TEST`，执行 `dotnet test Server/Tests --filter FullyQualifiedName~ArenaRoomTests`。覆盖两会话的房间发现、公开过滤、错误密码、重复入场、并发入场、房主转移、跨服务重启恢复、聊天读写与禁聊。

### 房间实时通知

房间成员用竞技场 Bearer 连接 `GET /api/arena/rooms/{id}/events`。响应为 `application/x-ndjson`，每条 JSON 独占一行并立即刷新。服务端先登记订阅，再校验成员资格，首次连接及重连的第一条消息均为 `{"kind":"sync"}`。客户端据此读取自己的房间、草案与聊天快照。

- `room` 表示已经提交的成员进出、房主移交、租约回收、准备状态、房间设置或选禁开始。客户端重新读取房间和本人的草案视角。
- `chat` 表示文字、表情或系统通知已提交。客户端按最后收到的消息 ID 查询后续历史。房主修改设置时，同一事务产生的两个通知依次为 `room` 和 `chat`。
- `heartbeat` 每 15 秒重新验证身份和成员资格并续租。房间动态变化时仍执行这个周期。退出、租约失效或会话失效会关闭连接。新连接缺少身份时返回 401，非成员返回 403，房间已清理时返回 404。

订阅由当前服务进程按房间管理，等待通知期间释放数据库连接。每个订阅只保存待刷新的两类标记，读取缓慢时合并同类通知。服务重启、跨实例写入和网络中断由客户端重连及周期性重新读取快照校准。原有房间读取、消息游标和聊天长轮询继续供旧客户端使用，无新增数据库迁移。

在隔离库执行 `dotnet test Server/Tests --filter FullyQualifiedName~ArenaChatWaitTests` 可复验双会话推送、设置系统消息、租约清理、重连初始同步及旧聊天长轮询。

2026-11-21 用本机独立 Kestrel 和 MySQL 8.4，通过两条持续 HTTP 连接测量从一端发起 POST 到另一端读完通知行的时间。入场 14.68 毫秒、聊天 7.08 毫秒、离场 6.91 毫秒，连续十次准备切换为 3.74–39.82 毫秒。客户端完成这些操作后又等待 14.865 秒收到首个心跳，随后仍能收到离场消息。此组计时止于网络通知，不含 Unity 绘制。

## 基本模拟战选禁和联机战斗

更新服务前应用 `006_arena_draft.sql`。为运行账号增加 `arena_room_draft_settings` 的 SELECT/INSERT，`arena_room_draft` 的 SELECT/INSERT/UPDATE/DELETE，以及 `arena_battle_command` 的 SELECT/INSERT。迁移由原有专用账户执行。选禁和战斗使用与目录、成员相同的 Arena Bearer，联机接口无需开启独立的本机战斗验证开关。

- 创建房间额外接收 `prebanCount`（默认 2，范围 0–2）、`firstPick`（默认 `host`，也可为 `guest`）和 `banProtection`（默认开启）。目录和房间快照返回这些设置，已有房间采用默认设置。只有 `basic` 当前实现选禁，其他规则仍可创建对应房间。
- 房主向 `POST /api/arena/rooms/{id}/draft/start` 提交 `{requestId}`。服务器锁定房主和最早入场的其他活跃成员为两侧，其他成员不能发送选禁与战斗指令。发起请求通过非空 N 格式 GUID 去重。
- 两侧通过 `GET /api/arena/rooms/{id}/draft` 获取各自视角。不存在草案时为 204，尚未公开的预禁、后禁和站位对对手不可见。快照携带 `id`、`version`、阶段、两侧、可选英雄 ID、当前选人方、窗口内剩余选择次数以及 UTC 截止时间。选禁过程中房主或对手离开／租约到期后取消本局。房主可发起下一局。
- `POST /api/arena/rooms/{id}/draft/actions` 接受 `{requestId,draftId,version,kind,heroIds}`。预禁 `preban` 提交整组，选人 `pick` 提交一个，后禁 `ban` 提交一个对手英雄，站位 `formation` 提交按战位排序的四个存活英雄 ID。重复提交相同请求只返回当前一方视角，同请求 ID 改内容返回 409。轮流选人遵循 1–2–2–2–2–1，每个单选窗口 30 秒、双选窗口共 60 秒，其他阶段各 30 秒。开启保护时第三名选中的英雄免于后禁。超时后服务端在下一次有效读取或提交时补齐本窗口的合法选择。
- 两侧完成站位即建立权威战斗。房主是 `Ally`，对手是 `Enemy`。通过 `GET /api/arena/rooms/{id}/battle?after={eventSequence}` 取快照及新增事件。提交命令到 `POST /api/arena/rooms/{id}/battle/commands`，沿用开发战斗命令格式并填写当前 `battleId` 与 `contentHash`。配置 SHA-256 基于去除 BOM 并将 CRLF 统一为 LF 的 UTF-8 文本。双方都用同一快照，服务端校验当前行动方、英雄归属、内容哈希、规则版本和命令幂等。旧局指令会被拒绝。战斗请求也续期成员资格。
- 已接受的命令按序存储，服务端重启后用固定配置与随机种子重放。轮询从有界只读缓存读取已提交状态，修改以 MySQL 房间锁和事务为准。草案、战斗命令随所属房间清理，审计保留。现有战斗版本需一致，发布不兼容规则前先结束在途局。
- 权威审计记录草案开始、实际提交的阶段、超时推进、取消以及战斗开始／结束，在同一事务写入，不含选择内容。GUI、BP 超时和重开检查见项目验收记录。观战、其他房间玩法另行接入。
- 在线战斗响应增加版本 1 的 `clock`；双方在完成对应事件演出后向 `/battle/ready?after={eventSequence}` 提交 `{battleId,commandSequence,eventSequence,actorId}`。两侧就绪后才设置决策截止时间。`Arena:BattleTurnSeconds` 与 `Arena:BattlePresentationWaitSeconds` 默认分别为 15 秒和 30 秒，配置范围 5–120 秒，并在本局首次生成时固定。演出等待到达上限时自动开启计时。行动席在计时开始前即可提交当前回合的合法技能，另一席可读到相同快照。若技能、目标或指令序号无效，响应携带当前权威快照及完整事件序列供客户端恢复。到期后下一次有效请求先按共享 AI 选择合法动作并同事务保存，再处理手动命令。重复 ready 或重连不延时，状态随草案 JSON 持久化，无新增迁移。新客户端要求计时协议版本 1。详见[回合计时与验证](../docs/delivery/2026-11-15-online-turn-clock.md)。

真实 MySQL 回归用 `dotnet test Server/Tests --filter FullyQualifiedName~ArenaDraftTests`。测试覆盖选禁隐私、同时抢选、选人计时与保护、双方站位、战斗权限、重启重放、退出取消及旧局请求拒绝。

## 启动登录、注册与选服

发布新客户端前应用迁移 `009_entry_account.sql`。运行账号需要新增表 `entry_account` 的 SELECT/INSERT/UPDATE、`entry_email_challenge` 的 SELECT/INSERT/UPDATE/DELETE，沿用 `arena_player` 与 `game_operation_audit` 的权限。

- 客户端通过 `GET /api/entry/version` 取得 `minimumAppVersion` 与区服 ID `undying-memory`。设置 `Entry__MinimumAppVersion` 指定最低母包版本，留空表示未设置。
- `POST /api/entry/email-code` 发送注册验证码。发送成功返回 204。验证码十分钟有效，单地址六十秒内仅能请求一次，每次申请允许五次校验。服务进程重启后，重新申请验证码。
- `POST /api/entry/register` 使用 `{username,email,password,code}`，`POST /api/entry/login` 使用 `{account,password}`。账号名采用 3–24 位 ASCII 英文字母、数字或下划线，也可用注册邮箱登录。密码长度为 12–128 个字符。接口成功后返回短期入口令牌。
- `POST /api/entry/select-realm` 使用入口 Bearer 和 `{realmId:"undying-memory",appVersion:"1.0.0"}`，返回现有竞技场会话结构 `{token,player}`。入口令牌有效期十五分钟，竞技场令牌有效期二十四小时。再次选服后生成的新令牌替换旧令牌，玩家 ID 保持一致。服务端配置最低版本时，选服会再次校验客户端报送版本，欠缺或过旧则返回 426 `client_update_required`。
- 注册、登录和选服成功后在对应数据库事务内记入 `game_operation_audit`，记录内部账户 ID 与操作类型。凭据、邮箱地址、验证码和令牌均不进入审计内容。
- 现有访客客户端仍可使用竞技场会话接口。所有客户端更新后，将 `Entry__AllowLegacyArenaGuests` 设置为 `false` 关闭新建和使用访客会话。

SMTP 使用 `Entry__Smtp__Host`、`Entry__Smtp__Port`、`Entry__Smtp__From` 以及服务商要求的 `Entry__Smtp__Username`、`Entry__Smtp__Password`。端口默认 587，使用 STARTTLS。把凭据配置到服务器环境变量。测试 SMTP 可以在 Development 对回环 IP 显式设置 `Entry__Smtp__UseStartTls=false`，供本地捕获验证码邮件。

使用已有的隔离 MySQL 8.4 测试配置运行 `dotnet test Server/Tests --filter FullyQualifiedName~EntryAccountTests`。测试会在本地回环监听器接收验证码，并验证注册、防重放、选服及竞技场身份。

## 区服英雄背包

服务更新前应用迁移 `010_player_inventory.sql`。运行账户需具有 `inventory_capacity` 的 SELECT、INSERT、UPDATE 权限，以及 `inventory_hero` 的 SELECT、INSERT 权限。初始发放与审计写入共用事务。迁移使用专用账户，生产运行账户无需 DDL 权限。

- `GET /api/inventory/heroes` 使用选服返回的 Bearer。在测试服务开启访客时，也接受该进程的独立竞技场访客会话。玩家取自服务端 Bearer 校验结果，区服为 `undying-memory`。客户端提交的其他玩家或区服参数不会改变结果。未授权返回 401，数据库不可用返回 503，响应禁止缓存。
- 返回 `{capacity,used,heroes:[{instanceId,templateId,level,stars}]}`，以获得顺序排列。当前区服默认 1000 格。同模板可重复，每只英雄用独立实例 ID，计入一格。正式账号首次成功选服时发放拉斯、梅赛德丝、安洁莉卡各一只，已有账号或开发访客也可首次查看背包时补发。再次选服、并发访问或服务重启保留原实例。
- 目前仅提供读取。后续写入入口须在同一事务内锁定对应背包头并检查剩余容量。既有选禁模板池仍独立。
- 实际测试命令为 `dotnet test Server/Tests --filter 'FullyQualifiedName~InventoryTests|FullyQualifiedName~EntryAccountTests'`。须在隔离 MySQL 8.4 测试库设置 `ASTRALOATH_MYSQL_TEST`。覆盖首次发放、并发、重启、重复模板、两账号隔离、失效令牌，以及相同密码对应不同随机盐和实际派生摘要。

## 普通装备

`014_player_equipment.sql` 建立区服装备容量、实例、实际副属性、穿戴槽和请求回执。默认每名玩家有 500 格，穿在英雄身上的物品同样占一格。升级现有账号后，装备库存从空、版本 0 开始。迁移会把英雄原有的归属与取得顺序索引声明为唯一，以便数据库外键约束英雄和装备属于同一区服、同一玩家。英雄 ID 始终使用实例 ID。

- `GET /api/inventory/equipment` 使用选服 Bearer，返回 `{capacity,used,version,items}`。每件包含 `instanceId,templateId,slot,setId,itemLevel,enhanceLevel,grade,mainStat,substats,equippedHeroId`。空闲物品的 `equippedHeroId` 为 null。词条结构为 `{kind,value}`，百分比采用万分比整数。接口随获得顺序返回物品，各件副词条按固定位置排列。
- `HEAD /api/inventory/equipment` 使用同一 Bearer，只读当前玩家和区服的版本，返回十进制加双引号的 `ETag` 及 `Cache-Control: no-store`，无记录时为 `"0"`。回前台时可将已知版本作为 `If-None-Match` 传入。匹配时返回 304，版本改变返回 200 和新标签，再按需读取完整装备。探测不发放奖励，也不创建背包。
- `POST /api/inventory/equipment/actions` 传入 `{requestId,action,instanceId,heroInstanceId,slot,expectedVersion}`。动作支持 `equip` 和 `unequip`，ID 使用 32 位无横杠 GUID。服务端从物品实例读取实际部位并核对可选的 `slot`。换装从原穿戴者取下物品，并替换目标英雄同部位的旧件。卸装检查请求中的英雄确实正在穿戴该件。阶段一穿脱免费。
- 成功返回完整装备快照。账号锁覆盖鉴权后库存操作中的归属检查、换槽、版本更新、回执和审计。相同请求重试会取回第一次操作的完整回执。复用请求号但改参数返回 409 `request_conflict`。使用过期版本返回 409 `equipment_version_conflict`，客户端刷新后用新请求号重新确认。部位不符返回 400 `equipment_slot_mismatch`，英雄或物品不属于当前玩家返回 404。
- 开发环境显式设置 `Inventory__DevelopmentEquipmentGrant=true` 后，首次访问装备接口可一次领取七件 Lv.15、+0 的训练装备。包括速度四件、暴击两件以及一把替换武器。训练数值保存在实例与副属性行，目录源在 [training-equipment.json](Host/Inventory/training-equipment.json)。默认配置与生产环境均无训练发放。
- 在独立 MySQL 8.4 实例设置 `ASTRALOATH_MYSQL_TEST`，执行 `dotnet test Server/Tests --filter FullyQualifiedName~EquipmentInventoryTests`。用例通过真实 HTTP 和数据库检查六槽穿脱、换装、重复英雄模板、两端争抢、跨账号访问、固定奖励及精确回执。阶段边界和后续养成见[普通装备方案](../docs/plans/2026-11-21-equipment-system.md)。

### 本机联调服务复启

已有测试数据库、已发布的服务 DLL 和已签名的资源目录时，在主工程根目录运行：

```powershell
pwsh -File Server/Ops/Start-LocalTestStack.ps1 -ServerDll ".artifacts/local-game-server/r17-summon/server/AstralOath.Server.dll" -FeedDirectory ".artifacts/update-and-battle-2026-11-17/local-release" -MinimumAppVersion "0.1.20"
```

脚本复用开发环境已安装的 MySQL、.NET、Python，在本机回环按需启动 MySQL（3307）、游戏 API（7728）和内容服务（5093）。本地栈可通过 `-ApiPort` 指定游戏端口，Windows 验收脚本通过 `-BaseUrl` 指向同一地址。它检查 /health/ready、/api/entry/version 与签名目录对应的清单版本。最低客户端版本与内容发布必须配套，0.1.20 门槛只在对应母包已分发后启用。现有 0.1.19/R16 环境使用空门槛。此命令不注册开机计划或系统服务。

灭亡事件及快照保存复活资格。反击可携带专用演出 ID，护盾、反射、吸血之手和炸弹由共享内核结算。[第 31–40 名角色与实测记录](../docs/delivery/2026-11-20-roster31-40.md)

济纽亚的一次致命保护随单位快照同步，A 计划的冷却独立计数。肯恩魄力和济纽亚主动、追加、被动的实际演出 ID 都由共享内核确定。服务端与客户端须配套发布规则版本 14。[第 41–50 名角色交付](../docs/delivery/2026-11-20-roster41-50.md)

## 第 51–60 名英雄

共享战斗规则版本 15 使用本批技能、资源和状态，选禁和战斗消费同一目录。战斗事件返回实际选中的技能图号，重放用此图号复现。 [本批权威行为](../docs/delivery/2026-11-20-roster51-60.md)

## 第 61–70 名战斗规则

客户端和房间服务使用同一份战斗目录和规则版本 17。迪埃妮至薇薇安的回归通过正式命令核对技能触发顺序、燃魂、强化及神器对实际血量的影响。 [批次记录](../docs/delivery/2026-11-20-roster61-70.md) · [技能效果复核](../docs/delivery/2026-11-20-roster61-70-effect-audit.md)
