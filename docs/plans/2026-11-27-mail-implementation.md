# 游戏邮件实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按11/22原片实现真实收信/记录/领奖、配置类型、开发GM当前账号自发及服务端名单发送，完成对照与打包后关机。

**Architecture:** 沿用用户已明确要求继续的[11/22设计](2026-11-22-game-mail.md)，新建独立Mail业务模块；Foundation协调登录、跨页入口、GM及已有库存失效。服务端Mail通过窄事务奖励接口接入现有库存/钱包，不从动画或公开GM接口发奖。运营使用同一Host程序集的本机CLI与PowerShell交互包装，无新增运营HTTP入口。

**Tech Stack:** Unity2022.3.62f3、HybridCLR9.0.1 runtime、.NET10、MySQL8.4；沿用原素材、普通C# View/Binder与现有UI基础组件，不新增第三方依赖。

**Spec:** [已记录设计](2026-11-22-game-mail.md)；[本轮原片](../development/mail-ui-reference.md)；实施前源码调查 `.artifacts/mail-system-review/domain-audit.md`。

## Global Constraints

- GM只给已鉴权当前账号发送；多人、白名单、黑名单、显式全区仅运营工具。只有黑名单不隐含群发，预览冻结名单及内容。
- 原片仅standard/gm行为类型、两个页签。维护、活动、通知、行动力、礼包等为独立模板用途，不凭发件人昵称判断类型。
- 收件100；历史最近90天且最多100；开正文不减角标，领奖权威成功才入账/减数。批领GM除外、混合多附件整封原子、后来的信不进旧快照。
- owner→mailbox→delivery固定锁顺序，领取与余额/实例/图鉴/幂等/审计同事务；正文、标题、名单原文、凭据不进操作日志。
- 真实energy余额沿现有通用材料账本；项目配置naturalCap默认360，初始0，明确这不是全玩家原作上限结论。本轮无自然回复或关卡消耗，邮件可以超过naturalCap，不超过绝对安全上限。
- `Config/Mail/catalog.json`为类型/发件人/资源/礼包/模板唯一编辑源，Host嵌入并验证，客户端读安全目录API，不复制另一套奖励规则。
- 接收时间按钮展开、纯文字/选一、满箱/满背包及开信时到期画面缺参考；已异步询问既有文件/时间。先实现后端边界与已拍区域，不臆造并声称缺失原状态还原。
- 新代码在main提交推送；原355项构建相关未提交输入保持，禁止混入。当前R37母包7445597f1ef645db8ef946c4964503fc为邮件后续兼容检查目标，母包包路径见装备交付报告；邮件候选不改已运行0.4，先在独立schema/.6验收。

## Review Focus

- 一封混合奖励中某类满仓：整封不发，其他批领普通信可处理，不把DB异常当容量skip。
- 提交成功丢回执、关窗、换requestId、目录改版及到期后重试：不重复发奖且返回真实原回执。
- 旧token、A/B账号、GM额外收件字段：不读领代发，不延续旧订阅。
- 99封并发投递、过期释放容量、历史裁剪：不超收、不破坏领奖防重记录。
- 原CG素材圆角绿/蓝/金按钮与装备直边是不同样式；仅使用对应原图、正确切片，Overlay保留GM正文下层，不用纯色替代。

## Task 1：后端目录、事务奖励与玩家邮件

**所有权：** backend agent独占`Server/Host/Mail/`、`Server/Host/Inventory/InventoryStore.MailRewards.cs`、必要Summon事务适配、`Server/Host/Foundation/Migrations/017_game_mail.sql`、DatabaseMigrator、Program、EntryStore、Server项目文件及`Server/Tests/Mail*`；`Config/Mail/`与Config/Server README、`Server/Ops/Mail.ps1`。更改其他既有文件先向协调者声明，不碰0.4已运行host。

**接口：** `/api/mail/{catalog,summary,inbox,history,events}`、`/api/mail/{id}`、POST`/api/mail/claim`、`/api/mail/claim-all`；开发`/api/development/gm/mail/{preview,send}`。玩家身份从Bearer解析；同一个DTO契约先写`.artifacts/mail-system-review/api-contract.json`，立即交前端与GM集成者。正文展示字段为受限纯文本。

- [ ] 写有效失败测试并实际跑：配置非法、发送不加余额、单领一次、相同请求冲突、整封容量回滚、批领5→2/仅GM、过期/历史/99并发、跨账号与正式GM404。
- [ ] 实现017与持久区服名册、冻结规格/名单/批次、版本outbox、owner锁事务奖励facade；复用真实钱包、材料流水前置operation与图鉴，不开第二事务。
- [ ] 实现可配置energy、GM真实设置、各奖励类型与确定性礼包。目录下架后已发信仍用冻结规格，新增实例来源索引唯一。
- [ ] 实现运营CLI preview/confirm/status/retry与中文交互；只读预览不发、确认幂等、失败原名单重试、到期不续期；无运维HTTP或默认全服。
- [ ] 在独立MySQL/schema、新QA账号跑绿及完整Server回归，保留命令/XML/API与操作工具证据；生产/非回环禁路由、旧token事件流终止、Host中断接续。

## Task 2：原素材Mail模块与真实界面流程

**所有权：** client agent独占`Assets/Game/Modules/Mail/`、`Assets/Game/Editor/ArenaUiBuilder.Mail*.cs`、`Tools/Mail/ImportMailAssets.py`、新`Mail*Tests`及必要Common圆角样式**资源**；不改Common运行时代码。模块由协调者用New-Module建立并登记程序集，客户端只依赖Core/Common。

**接口：** `MailApi(Uri, Func<CancellationToken,Task<string>>)`；公开账号级`MailSession`持有摘要、版本通知与未决请求；`MailWindow : UIBaseView`/`MailWindowBinder`消费IMailApi与session，注入关闭动作和领奖后资源校准委托。具体签名随api-contract冻结后写模块README并通知协调者。详情/附件/奖励各自独立BindingScope，由所属WindowCoordinator维护。

- [ ] 制作前实际查看对应参考图/连续帧。按mail_list/mail_item/mail_event_popup等原CSB，最小导入原PNG/音轨及来源清单，不导入整个解包目录。
- [ ] 先写绑定/流程失败测试：收件/历史、正文/附件、取消、权威回执后减数、5→2→1→0、关窗/重开/过期/超时重试、跨页同摘要。
- [ ] 实现全屏列表、原胶囊领取按钮、GM原蓝面板、附件说明、源批领确认、顶部单项奖励条/礼包汇总窗及空箱/历史。源界面过渡约0.2秒交叠，不强套黑屏。
- [ ] 接入账号级version事件与断线补读，取消/超时/旧响应丢弃/关闭清理，所有文本关闭自由正文richText；奖励只从权威响应展示。
- [ ] 图形Editor对照1080p/4K与窄屏，逐帧比原版/修改前（原为未实现）/修改后；实际查看连续动画，音轨实测与听感分开报告。

## Task 3：Foundation、GM与所有信封入口

**所有权：** 协调者独占Foundation相关StartupNavigation/HomeFirstReads/DevelopmentGmWindow、GM生成器与测试，Home既有顶栏按钮/摘要桥接、Config/HotUpdate/assemblies.json及YooAsset登记、Packages/ProjectSettings/Git。

- [ ] 登录scope持有MailApi/Session；退出解绑/取消/清缓存。Home与英雄/背包的既有信封真正可点，统一角标，在原页关闭邮箱返回。
- [ ] GM增加邮件模块，普通/GM与模板、标题/正文、附件数量/有效期/预览/发送；当前区服与账号只读，提交锁定，未决重试同request，明确再发一封才新request。列表保持足够滚动高度与可拖动入口。
- [ ] 发信不改钱包；收信后仅校准受影响库存及材料/energy/钱包，真实余额即时更新，重登一致。两个账号验证隔离与旧会话。
- [ ] 与后端/客户端联合实服，写回模块README、配置源、审计稳定键、状态和change-log，列实际未完成状态及原因。

## Task 4：复审、签名包与原生交付

- [ ] 独立审查权限、事务、重试、生命周期、配置与源素材；先修具体发现再跑受影响回归。
- [ ] 对R37归档实际兼容检查；兼容时构建R38签名热更，不为邮件重复换母包。必要AOT变化先说明具体证据再新母包。
- [ ] 真实桌面新QA账号完成GM发信→角标→列表→单领/批领→余额/实例→历史→重登，1080p/4K及两个客户端通知/旧token回归；安装后退出重开和暖缓存下载量保留。
- [ ] 交付可打开原版/改前/改后图录、音效/特效事实、准确文件清单及包哈希，更新所有文档，`node Tools/check-docs.mjs --staged`，中文提交并推送。
- [ ] 全部已授权内容完成并验收后执行用户要求的关机；未完成不得提前标整轮完成。
