# 玩家资料与公共频道

`AstralOath.Server.Social` 经 `AddSocial` / `MapSocial` 接入Host；`PlayerProfileStore`、`ChannelChatStore` 分别拥有资料及聊天/偏好事务。`ChannelChatEvents` 是DI拥有、容量1合并失效通知的进程内集合，不存正文。鉴权复用Arena Bearer，区服由服务器固定为 `undying-memory`，拒绝明确属于其他realm的身份；无任何realm/库存记录的旧访客兼容为本区访客。未来多区需扩展会话授权，不接受任意realm参数。

## 前置迁移与身份兼容

发布候选服务前以专用迁移身份依次应用 `018_player_profile.sql`、`019_channel_chat.sql`、`020_channel_chat_order.sql`、`021_player_public_identity.sql`，运行账号需要018–020八表SELECT/INSERT/UPDATE，以及021的 `player_public_identity` SELECT/INSERT，不需要DDL。普通服务不自动迁移。旧 `arena_player.display_name` 仍非唯一、历史重名和库存不改；Entry的 `PlayerNicknameRegistry` 用同一MySQL名字键仲裁创建与改名。访客不能新建成别人已认领的昵称，注册初始展示名碰撞时生成安全默认名；注册后尚未选服期间也可能出现新认领，因此首次入区在同事务再次仲裁，冲突只调整初始展示名，登录user_key、ID及凭据不变。

新昵称先NFC及Trim：纯ASCII英文字母4–12；含CJK汉字时总Unicode标量2–8，组成限汉字、ASCII字母/数字，内部空白/特殊字符/纯数字拒绝。SQL查重及claim统一 `utf8mb4_0900_ai_ci`，大小写/重音不敏感。旧初始名照常读取，改名免费，不引入录像未显示的费用/次数。

## 资料契约

- `GET /api/profile/me`、`GET /api/profile/players/{playerId}` 返回同一DTO，只有本人 `isSelf=true,canEdit=true`，目标ID为N格式GUID。
- `GET /api/profile/uids/{uid}` 按10位公开UID精确查询同一DTO，仍需有效Bearer及同区服授权；只允许ASCII十进制 `1000000000`–`9999999999`，非法400 `invalid_player_uid`，不存在或目标不属于本区404 `profile_not_found`。查询沿用他人战绩隐私，不产生好友关系或库存初始化。
- `POST /api/profile/nickname/check {nickname}` 返回 `{available,code,rulesVersion:1}`，不预留名字。
- `POST /api/profile/me/actions {requestId,expectedVersion,kind,nickname?,greeting?,seasonVisible?,bestVisible?}`，kind仅 `nickname`、`greeting`、`visibility`；只接受对应字段，visibility需要两个bool；直接返回Profile，无外层envelope。

Profile字段：`playerId,uid,realmId,displayName,avatarId,greeting,greetingIsDefault,version,isSelf,canEdit,representativeHero,representativeConfigured,visibility,rank,seasonRecord,bestRecord`。`uid` 是10位JSON字符串，内部GUID和鉴权token保持独立。头像沿现有 `c1001` 格式。代表摘要为本人真实库存 `{instanceId,templateId,level,stars}`，优先匹配头像template，再取获得顺序第一只，无库存null；只读投影，不补发别人英雄。目前没有代表选择存储，`representativeConfigured=false`。没有权威Rank/榜单，`rank={available:false,value:null}`、战绩 `{available:false,visible:<权限>,data:null}`，不造Rank4/铜V。本人可读自己的信息，他人隐藏内容在服务端返回null。代表/辅助选择、边框、个性卡不在首版写接口内。

问候语0–50个Unicode标量，CRLF统一LF并Trim首尾，允许内部LF，拒绝其他控制/坏代理；空串 `greetingIsDefault=true` 交前端本地化默认句。两可见开关独立持久化。不要截断GUID或复制录像编号伪造数字UID。

## 公开UID分配与迁移（2026-11-28）

`Foundation/PlayerPublicIdentity` 为游客创建和邮箱注册在同一身份事务内分配UID，注册尚未选服时已持久化。密码学随机64位拒绝采样覆盖90亿个10位整数；MySQL `player_public_identity` 的player主键、UID唯一索引及范围CHECK共同约束。随机UID碰撞最多尝试16次，耗尽失败并由外层事务回滚；重试不替换已有玩家映射。登录、令牌轮换、改昵称、重启、重新选服均不重抽UID。内部GUID继续服务于外键/权威身份，UID只用于展示和精确查人，不能作为登录凭据。

迁移021只新增公开身份表，未改写001–020。`DatabaseMigrator` 在既有数据库迁移锁内建表后调用同一分配器为已有玩家一次性补齐，并向资料幂等回执补齐字符串uid，再记录021校验和；中途失败可重入，已分配UID保持不变。普通运行服务没有“首次读取才分配”的旧数据分支。本变更无需清空测试数据；若按正式版前策略重建空库，仍由完整迁移链及新身份事务初始化。部署必须先停旧版身份写入、备份并以迁移身份运行完整 `migrate`，再切候选服务并授予新表权限；仅执行021 SQL文件不会执行C#数据初始化，不可代替完整迁移入口。

本轮证据在 `.artifacts/identity-social-followup/server/`：`uid-red-final.trx` 实际4项RED（缺uid/缺表），`uid-migration-red.trx` 复现旧回执uid为null；禁用碰撞重试的 `uid-collision-mutant-red.trx` 实际失败。恢复实现后的 `uid-green-restored.trx` 为6/6，完整Server `server-full-uid-final.trx` 为683/683、0失败/0跳过。覆盖8个HTTP同时创建、真实数据库唯一/范围拒绝、拒绝采样、碰撞重抽/16次耗尽回滚、重命名/重启/重登录稳定、鉴权/区服/隐私、旧身份初始化与回执重放。专用空库 `astraloath_uid_20261128`；另在 `astraloath_uid_upgrade_20261128` 实际CLI演练020状态→021：2个旧玩家得到不同10位UID，旧回执为STRING/10，第二次migrate后映射和回执完全一致，见 `migration-before.txt`、`migration-after.txt`、`migration-reentry.txt`。初次测试因受限Windows SSL凭据失败，私密loopback连接显式关闭TLS后才开始有效RED/GREEN；未触及主库/现有Host或MySQL进程。NU1900为离线漏洞源警告。本节仅服务端行为证据，视听不适用；UID右上角布局与原生包交付由客户端整轮另验。

## 公共频道契约

- `GET /api/chat/preferences` → `{version,favoriteChannel,notifications:{hero5,artifact5,guildPromotion,generalChat},keywords,transparentBackground}`；默认收藏null、四通知true、关键字空、背景false。
- `POST /api/chat/preferences/actions {requestId,expectedVersion,kind,...}`，kind为 `favorite` / `notifications` / `keywords` / `background`；对应 `favoriteChannel:null|1..9999` / 完整notifications / 最多五项1–20标量关键字 / `transparentBackground:bool`。默认进1，保存收藏不自动切频道。
- `GET /api/chat/channels/{channel}/messages?after=0&waitMs=0` → `{channel,messages,nextAfter,hasMore}`。channel1–9999；id/after为十进制BIGINT字符串。首屏最近50条升序，后续正向100条，hasMore时立即补页；waitMs0–8000，等待不持有数据库连接/事务。
- `POST /api/chat/channels/{channel}/messages {requestId,kind:'text'|'sticker',text?,stickerId?}`。文字1–240标量或现有67个 `ArenaChatStickers` ID，互斥；返回 `{id,channel,player:{id,displayName,avatarId},kind,text,stickerId,createdAt}`，UTC时间与sender来自服务端，不能伪造system消息。
- `GET /api/chat/channels/{channel}/events` 为NDJSON `sync/chat/heartbeat` 失效通知，不含正文；先订阅再读，提交后发布，每15秒复核Bearer及realm，取消/断开/关闭Host释放订阅。客户端按游标重读；当前是单体部署，不冒称跨进程消息总线。

公共消息不要求竞技场房间成员，也不续房间租约。所有SQL按realm+channel隔离；每玩家+realm至少500ms间隔、每分钟30条，换频道不能绕过；同请求重放优先于限流。偏好只控制客户端对已授权公共流的本地通知，不过滤历史、不上传命中正文。五星英雄/神器及骑士团宣传尚无公开广播生产者，可保存偏好不等于实现这些广播。

频道写入在分配消息自增ID前通过020的 `(realm_id,channel_no)` 行执行 `INSERT ... ON DUPLICATE KEY UPDATE` 获取事务X锁，一直持有到提交；同频道的提交顺序与游标顺序一致，避免不同玩家“较大ID先提交→游标推进→较小ID后提交”永久漏信。不同频道不共享该锁，幂等重放在获取锁和限流之前返回。真实HTTP双发送者回执gap锁演练先复现漏信RED，再验证修复及第三账号其他频道独立提交，无生产测试hook。

## 幂等、隐私、验证与部署

Profile/偏好独立version。非空N格式requestId与expectedVersion、本人行锁覆盖更新/回执/audit；聊天fingerprint包含channel。同ID同参数回原收据，换参数409 `request_conflict`；版本冲突 `profile_version_conflict` / `chat_preferences_version_conflict`。no-op存回执但不加版本/成功audit。重放可能低于客户端已知的新版本，客户端应完成原请求并刷新，不能覆盖更高版本。

事务审计只有 `profile.nickname.change`、`profile.greeting.change`、`profile.visibility.change`、`chat.preferences.change`、`chat.message.send` 和标识；昵称、问候语、关键字、正文及凭据不进入audit或结构化异常日志。自由文本仅存业务表/幂等回执；数据库异常只记编号。普通接口no-store、10秒超时、4096字节体限制，写JSON拒绝未知顶层字段。

2026-11-28真实隔离MySQL：新增6集成项首先404 RED，realm查名、乱序提交、待选服改名及通知缺字段各有实际RED；最终Social32/32、完整Server677/677，0失败/0跳过。覆盖旧重名、并发认领/新身份抢名、50标量、本人/他人、幂等重启/冲突、realm/channel隔离、225条分页、目录表情、限流、完整四通知字段、NDJSON/长轮询取消、订阅归零、失效token读写与真实15秒复核断流、旧Entry/Inventory/Arena回归。通知缺任一bool返回400，不隐式关掉其他开关、不写版本/audit。证据 `.artifacts/home-profile-chat-review/server-tests/social-server-reviewed-final.trx`；NU1900仅离线漏洞源警告，不代表改依赖。

复验先初始化 `Tools/ProjectEnvironment.ps1`，私下设置独立 `ASTRALOATH_MYSQL_TEST`，执行 `.tools/dotnet/dotnet.exe test Server/Tests --no-restore -p:TargetPlatformSdkPath=D:/UnityProjects/AstralOath/.tools -p:TargetPlatformDisplayName=Windows --filter FullyQualifiedName~Social`。未配置变量的MySqlFact为Skip，不算通过。本轮未迁移主库、未替换主服务、未打包分发/部署生产；协调者需备份/迁移/发布候选并另验实际客户端与视听，不能用服务端测试代替原版画面对照。
