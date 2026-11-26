# 游戏邮件服务

邮件目录唯一编辑入口为 `Config/Mail/catalog.json`，随 Host 嵌入并在加载时验证。玩家只访问安全目录和自己的区服邮箱，开发 GM 自发仅在 Development 且回环监听的 Host 注册。正文、标题为纯文本；模板用途与 `standard` / `gm` 行为类型分别配置。

收件容量100；历史展示最近90天内最新100条。读取正文不减少收件数，服务器 UTC 到期或权威领取成功才移入历史。奖励通过既有账号锁、同一个 MySQL 事务发放；客户端动画不发奖。详情及回归由 [服务端说明](../../README.md)、[设计](../../../docs/plans/2026-11-22-game-mail.md) 和 [本轮实施计划](../../../docs/plans/2026-11-27-mail-implementation.md) 持续维护。

## 本机运营命令

`Mail.ps1` 调用同一已发布 Host 的 `mail` 命令，在 listener / TLS 初始化前退出，不新增运营 HTTP 接口。必须用 `-ServerDll` 明确选择目标环境；连接串从目标 Host 目录的标准配置或环境变量 `ConnectionStrings__MySql` 读取，不放进命令行。工具自动使用本机 Windows SID 作为审计操作者，其他操作系统使用本机用户名的受限长度 SHA-256 标识。操作系统账号和配置文件 ACL 是运营入口边界。

```powershell
# 在主工程根目录运行；替换为目标环境已发布的 Host 和本机文件路径。
pwsh -File Server/Ops/Mail.ps1 preview -ServerDll D:/AstralOathData/runtime/mail/server/AstralOath.Server.dll `
  -Realm undying-memory -To 00112233445566778899aabbccddeeff,11112233445566778899aabbccddeeff -Template D:/AstralOathData/project-files/maintenance.json

pwsh -File Server/Ops/Mail.ps1 preview -ServerDll D:/AstralOathData/runtime/mail/server/AstralOath.Server.dll `
  -Realm undying-memory -AllowFile D:/AstralOathData/project-files/allow.txt -DenyFile D:/AstralOathData/project-files/deny.txt `
  -Template D:/AstralOathData/project-files/maintenance.json

pwsh -File Server/Ops/Mail.ps1 preview -ServerDll D:/AstralOathData/runtime/mail/server/AstralOath.Server.dll `
  -Realm undying-memory -AllInRealm -DenyFile D:/AstralOathData/project-files/deny.txt -Template D:/AstralOathData/project-files/maintenance.json

pwsh -File Server/Ops/Mail.ps1 confirm -ServerDll D:/AstralOathData/runtime/mail/server/AstralOath.Server.dll -PreviewId 22222222222222222222222222222222
pwsh -File Server/Ops/Mail.ps1 status -ServerDll D:/AstralOathData/runtime/mail/server/AstralOath.Server.dll -BatchId 33333333333333333333333333333333
pwsh -File Server/Ops/Mail.ps1 retry -ServerDll D:/AstralOathData/runtime/mail/server/AstralOath.Server.dll -BatchId 33333333333333333333333333333333
```

无动作运行进入中文交互，可选择目标环境、区服、模板文件或逐项填写类型/标题/正文/附件、收件范围、黑名单，再查看冻结结果并输入“发送”确认。交互临时模板只写到主工程 `.artifacts/mail-ops/` 并在退出时删除。脚本模式 preview 只生成预览；confirm 才发送，`-Json` 返回机器可读结果。

模板可只选既有模板，例如 `{"templateId":"maintenance"}`；也可覆盖受支持字段：

```json
{
  "templateId": "maintenance",
  "title": "维护补偿",
  "body": "维护已结束，请查收奖励。",
  "expiresInDays": 7,
  "attachments": [{"kind":"currency","key":"bookmarks","quantity":5}]
}
```

范围恰选 `-To`、非空 `-AllowFile`、`-AllInRealm` 之一。黑名单只能做集合减法；只有黑名单、重复范围、空文件、无效玩家 ID、不可读输入会拒绝。名单为严格 UTF-8（可有 BOM），每行一个32位无横杠 GUID，最多100000行/4MiB；模板最多256KiB。未知或跨区对象由存储层检查，非法对象不会产生可确认预览。名单重复保留至服务端统计，再按区服与玩家去重。

预览冻结内容与最终目标，输出实际人数、重复/排除数、单人及全批奖励、预览确认截止与到期规则。全区是预览时已进入该区的注册玩家，开发游客不会自动进入整区名册。零人结果可以预览但不可确认。编辑名单、修改模板或预览后新增账号不会改变已有预览；需重新 preview。

confirm 同一预览始终返回同一批次。投递状态分目标、已投递、待投递、失败与已领取；`mailbox_full` 不算投递成功。retry 仅重试原批失败对象，不扩名单、不重复已投递对象、不延长全批到期时间。确认/重试中断后用原预览号确认或原批次号查状态；Host worker 接续未完成目标。CLI 设两分钟取消期限，PowerShell默认同为120秒，可调 `-TimeoutSeconds`；超时并不证明未提交，禁止据此新建一批补发。

直接 CLI 等价用法：

```powershell
# 切到目标发布目录，沿该目录配置运行。请勿在参数中传连接串或令牌。
dotnet AstralOath.Server.dll mail preview --realm undying-memory --allow-file D:/AstralOathData/project-files/allow.txt --template D:/AstralOathData/project-files/maintenance.json
dotnet AstralOath.Server.dll mail confirm --preview-id 22222222222222222222222222222222
dotnet AstralOath.Server.dll mail status --batch-id 33333333333333333333333333333333
dotnet AstralOath.Server.dll mail retry --batch-id 33333333333333333333333333333333
```

成功输出一行 camelCase JSON；失败 stderr 只输出稳定错误码，退出码2为受控输入/业务拒绝、1为服务故障、130为取消/超时。审计仅保存稳定操作语义、操作者、批次/投递关联，不保存标题、正文、原名单或凭据。预览内容只在运营终端显示。

## 验证状态

本轮新增前没有游戏内邮件运营命令。`MailOpsTests` 的15项用例先实际失败（`.artifacts/mail-system-review/server/ops-red.trx`），随后15/15实际通过且无跳过（`ops-green.trx`），覆盖黑名单不能独立、范围互斥、无效/稳定 ID、白名单与显式整区、不可读/空/非法 UTF-8 名单。PowerShell AST解析及五种范围/动作拒绝实际通过（`ops-wrapper.json`）。另以真实 Host DLL 在 Production 环境调用三种非法命令，均在 TLS/监听前返回退出码2和受控错误，stdout为空（`ops-cli.json`），没有访问数据库；模板伪造收件字段也被拒绝。

中文子进程输出曾因控制台编码产生乱码，已显式以UTF-8写入/读取并用上述包装器回归验证。证据位于主工程 `.artifacts/mail-system-review/server/`；`ops-cli.json`记录实测Host哈希。完整数据库投递、Host恢复和客户端视听不由这些解析测试证明，整轮最终服务端回归与构建版本由协调者报告。

首轮后端独立MySQL完整Server641项全部通过，无跳过，Mail42项包含99并发容量与原批重试、90天/100历史、白减黑与全区冻结、领奖原子/实例图鉴/流水、目录下架与幂等、行动力越自然/绝对上限及令牌轮换流结束；日志及TRX为 `.artifacts/mail-system-review/server/server-full-final.*`。有效失败与修复证据另存 `mail-red.trx`、`transactions-red.trx`、`boundary-red.trx`、`capacity-red.trx`、`catalog-null-red.trx`。

本机隔离`.6`候选的Production CLI完成真实白名单去重减黑、预览后修改输入文件仍按冻结内容、重复confirm仍同批、status/retry不延期，回执在 `cli-live.json`。刻意卡住第二收件人，1投递/1待投时中断CLI并重启仅本任务`.6`Host，新Host接续到2投递/0待投，原批恰2条delivery，事实在 `host-restart.json`；恢复脚本在等待启动PowerShell返回处曾阻塞，数据由独立SQL和Host状态实测复核，未将整段脚本退出记为通过。既有`.4`服务未动。

后端不包含原版画面或音轨修改，视听不适用；客户端UI、动画、音效、原生与签名包仍需分别按对应模块验证。上述早期Host证据带925A14C6…发布hash，只代表该轮候选；后文记录当前发布与验收，不把旧候选冒充已分发新包。

独立复审后当前源码最终完整645项通过、0跳过（Mail46项，`server-review-final.trx`），三项实际失败→修复→通过证据为`review-red.trx`/`review-green.trx`：全局自增ID提交乱序漏通知、大写GUID已读不持久、GM同邮箱版本资源缺独立revision。通知改为各Host对活跃owner最多100人公平轮转读取已提交mailbox.version，Hub只合并更高版本，不依赖outbox ID排序、不删除全局outbox、不重放历史；205owner实际100/100/5三批全覆盖。mailbox与outbox仍在同一业务事务提交。

摘要新增`energy.revision`与`balances:{revision,gold,crystals}`，均从同owner锁下的真实持久材料/钱包流水条数取得同一个单调资源版本。crystals为skystones，未实现的PVP旗帜不造值。两账本必须保持不裁剪/不改写；旧余额首次快照为基线，未来归档先保存单调版本再迁移，不能删流水后继续Count。旧成功回执balances可空、revision默认0，客户端仅合并更新的资源版本并重读summary。

最新独立Release候选已实际发布及启动，hash`ECC7FF4BADBEF6F6B85D28E637F5E044755420E489727C1CCA56DF97C97F614A`、`.6`PID41392，`qa-review-fixtures.json`含真实GM/资源/已读/领奖/218ms事件复验。Start脚本使用限制句柄继承的隐藏原生进程；`start-return-green.json`记录Python捕获调用1.703秒真实退出0。旧925A…CLI/重启资料仍作为历史，不冒充新候选或客户端分发验收。既有`.4`服务PID41440保持不动。
