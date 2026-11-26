# 邮件系统实施与验收（联调中）

本轮承接R37装备修订，按用户11月22日国服录像新增Mail、开发GM当前账号自发与服务端便捷邮件运营。修改前基线42c8d6da只有顶部信封图片；不存在可冒用的旧邮件页面。后端源码已提交775a5b41；客户端最终联合生成、R37兼容检查及原生R38仍在进行，以下不表示已分发。

## 原版、修改前与本轮目标

原录像已复制到D盘，SHA256 `ff30f333d063fac743de619703919b7957b8e2b6b9a072cd441dc92c9aea18b5`，2608×1200/124.23秒；版本、发布日期及播放倍速未知，不自行补写。实际查看收件、记录、普通单领、GM正文、附件、批领排除GM、空箱与101.329–102.273秒礼包两段。新页面使用对应原行底、胶囊按钮、蓝正文框、NPC头像、金框插画以及源骨骼/粒子特效；此前仅信封静图。

- [完整原片与连续PTS](../../.artifacts/mail-system-review/index.html)和[前后区域对照](../../.artifacts/mail-system-review/delivery.html)。受控Editor图与实际Windows图分别标记；账号、数值和文案不同，比较布局/状态，不宣称逐像素一致。
- 礼包插画原图 `story/bg/img_event_3000days_1.webp`、缩略图 `item/art/img_icon_event_3000days_1.png`、金框 `img/_frame_s.png` 已定位；只证明展示，未伪造未证实的插画解锁业务。
- 声音原入口、单项、确认、礼包、标题已绑定对应源。当前工具没有听觉输入，实际试听未验收；保留带音原片/源音及历史波形匹配，不能用静态截图或事件命中代替听感。

## 行为与配置

Mail独立程序集只依赖Core/Common，View/Binder均普通C#；登录scope持有摘要、事件流与未决请求，页面关闭只取消等待。所有顶栏复用MailEntry，原页保留；确定领取和结果不明均校准真实库存，复用原Binder不重开页面。

`Config/Mail/catalog.json`是类型、发件人、奖励和模板唯一编辑源：业务类型standard/gm（仅standard批领），维护/活动/GM礼包/系统通知是用途模板。可配货币、材料、行动力、英雄、神器、装备与确定性礼包；发信只投递，领取后真实余额和实例才变化。action energy初始0、naturalCap360、绝对上限为项目配置；没有自然回复或关卡消费，360不称原作全账号事实。

GM只给当前已鉴权账号预览和发信，不接受额外收件人。开发版本所有账号可使用，Release隐藏。服务器CLI支持指定多个玩家、白名单减黑名单或明确整区；黑名单本身不隐含群发，冻结名单/内容，原批重试不续期。`Server/Ops/Mail.ps1`为源码入口，当前本机交付额外提供 `.artifacts/windows-r38/server/Send-Mail.ps1`自动选择本地环境，无参数交互。

## 已实际验证

- 后端：独立MySQL完整645/645，0跳过；跨账号权限、容量、过期、历史、重复/并发领取、原批冻结和中断恢复分别有证据。独立复审三项问题实际3红→3绿：提交乱序漏通知、大写ID已读未落库、同邮箱版本资源变化被忽略。最终Host哈希ECC7FF4B…97F614A。[完整回执](../../.artifacts/mail-system-review/server/server-review-final.trx)·[最终复审](../../.artifacts/mail-system-review/server/final-review.md)
- 客户端：隐藏重开邮件按钮实际1红→1绿；原页面与强化选材校准5/5，保留页签/选择/筛选/滚动及未确认请求原ID/version/recipe。[入口](../../.artifacts/mail-system-review/tests/header-reopen-green.xml)·[原页5项](../../.artifacts/mail-system-review/tests/external-five-green.xml)
- API/session：迟到正文不跨页重开，NDJSON在4096字符上限内读入而非先读整行；提交丢回执时登录级校准和原请求重试，明确4xx不误报不确定。最终联合结果将补在下方，不混用历史候选图。

## 文件与生成入口

- `Assets/Game/Modules/Mail/`：API、登录session、View/Binder、表现、prefab/原资源/来源哈希。
- `Assets/Game/Modules/FoundationDemo/Scripts/StartupNavigation.Mail.cs`、`MailEntryBinder.cs`、DevelopmentGmMail系列：登录/跨页/GM；`StartupNavigation.MailVerification.cs`为显式Development loopback原生诊断，正常玩家不准备验收邮件。
- `Assets/Game/Editor/ArenaUiBuilder.Mail*.cs`、`Tools/Mail/ImportMailAssets.py`：原源最小导入、可复现烘帧与预制生成。全页集成使用 `ArenaUiBuilder.GenerateMailIntegration`，不改原GUID。
- Home/Summon新ExternalChanges系列与强化窄刷新：真实数据原地校准。
- `Server/Host/Mail/`、Inventory/Summon的MailRewards、迁移017、`Config/Mail/`、`Server/Ops/Mail.ps1`：权威业务/配置/运营。
- 完整新增/修改清单与准确路径：[task-files.txt](../../.artifacts/mail-system-review/task-files.txt)。本机既有356项改动保留，不混入本轮提交。

## 仍缺参考或未验收

接收时间展开、选一附件、满箱/满背包即时反馈以及打开中到期缺同版录像，已向用户索要时间段；后端边界可验证，未知画面不臆造。纯文字通知可配置为GM正文，但其原版独立流程未称还原。特殊封面身份只确认显示。Android设备、音效实际听感、断电后的MySQL冷启动未实测；Windows原生/签名版本将在最终回执补齐。R37装备仍保留其报告中CP绝对值、旧未知累计次数及部分特效等限制，新增Mail不抹去旧限制。

联合生成后Editor57项、Play9项实际通过。R37实际归档兼容检查先发现热更清单入口字段误改（有效配置回归1红→1绿），随后指出新GM列表使用了R37裁剪掉的UnityEventBase.RemoveAllListeners；现改为只移除本模块所持有的具体listener，避免为一个清理API重复换母包。末次兼容检查与签名包回执待下一步确认，不把失败隐去。

末次R37实际归档兼容检查已通过，日志含`ASTRAL_COMPATIBILITY_OK 7445597f1ef645db8ef946c4964503fc`（.artifacts/mail-system-review/tests/compatibility-final.log）。无需为Mail更换AOT母包；下一步构建内容38并分别验签/原生。兼容通过本身不是已安装或已分发。

## 候选未分发的长正文边界修正

首次fadd0890内容38构建完成但未签名/切源。末次边界复核发现Server列表返回全文；普通中文100×8000为840,632字符，不因UTF8字节数误判。但真实已发布Host的默认编码会将全角空格转义，100封每封3000全角空格+5000汉字是Freeze接受的合法输入，响应2,341,023字符，超过客户端原2Mi字符限制。保留实际Unity 1失败/1通过→修后2/2通过（tests/response-bounds-{red,green}.xml），API仅改为有界8Mi字符，仍在超限时提前拒绝。当前目录保守上界6,695,066字符低于8,388,608；未来增加目录或限额必须重算，不能称任意无限响应。最终包将从修后提交重建，不分发首轮旧候选。

原生诊断另加真实通知校准：独立HTTP生产者投递三封准备信，不把其回执直接写入可见session，也不主动refresh摘要；等待登录事件流自动更新至4及顶栏角标，再继续实收流程。这证明不同HTTP客户的实际流更新，不冒充两个独立Windows进程同屏验证。
