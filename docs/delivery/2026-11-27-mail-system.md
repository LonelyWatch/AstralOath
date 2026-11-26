# 邮件系统本机交付：Windows0.1.27／内容41

本轮承接R37装备修订，新增Mail、开发GM当前账号自发与服务端便捷邮件运营。签名内容41已在本机发布并实际加载，IL2CPP/D3D11的1080p与4K各14状态、真实收发领取和余额断言通过；普通用户权限启动助手也已启动本体。未称所有原作边界、声音听感和全部特效均还原。产品源码739ec0806bd79dab33b6132c3c3c7f73e13ea86e，后端产品775a5b41；后续文档提交不改变签名产品快照。

## 最终入口与实际验收

- 启动：[Play-R41.exe](../../.artifacts/windows-r41/Play-R41.exe)；本体：[client/AstralOath.exe](../../.artifacts/windows-r41/client/AstralOath.exe)。助手准备本机服务后开本体，不是另一款游戏。
- 服务端：[发送邮件.cmd](../../.artifacts/windows-r41/发送邮件.cmd)，无参数中文交互；现有私密环境自动读取，不要求填连接串。支持多个玩家、白名单减黑名单、明确整区；只列黑名单不隐含群发。[本机操作说明](../../.artifacts/windows-r41/README.md)
- GM「邮件」可选模板/类型/发件人，编辑标题正文、有效期和附件，服务器冻结预览后发当前登录账号；所有开发版账号可用，Release隐藏。发送只投递，领取才入账。业务类型standard/gm，用途模板maintenance/event_reward/gm_gift/system_notice；货币、材料、行动力、英雄、神器、装备与确定性礼包由[目录](../../Config/Mail/catalog.json)配置，八层礼包树保留。选一附件尚未实现。
- [最终逐区域原版／修改前／Native41对照](../../.artifacts/mail-system-review/delivery.html)、[全部原片及连续PTS](../../.artifacts/mail-system-review/index.html)、[完整改动绝对路径](../../.artifacts/mail-system-review/changed-files.md)与[源SHA快照](../../.artifacts/mail-system-review/final-source-files.json)可直接打开。此前基线42c8d6da仅信封静图，无邮件业务页面，未重建假想历史画面。

1080p使用`.6`独立库新测试访客，4K使用当前`.4`主开发服的新测试访客，不给正常账号发验收奖励。两次均退出0、14张正确尺寸非空PNG、完整initial/final摘要。这里4K为实际3840×2160游戏渲染与Framebuffer截图；桌面采集器暴露1440×2560逻辑坐标，受DPI及窗口裁切影响，未宣称物理4K屏幕完整呈现。实际由GM界面发1封，第二HTTP客户准备3封；可见session只经事件流自动到4，未直接写生产者回执/手动强刷。数量4→3→2→0；总增量10书签、2传说石、5上级石、10000金币、100行动力；礼包两阶段成功领取回执仅1次，历史回看不重发，关闭恢复Home同一实例且空箱可重开。按钮通过真实listener调用，未冒充人工鼠标测试；两个HTTP客户也不是两个独立Windows进程。

[1080p回执](../../.artifacts/mail-system-review/native-mail/runs/final41-qa1080/verified.json) · [1080p全部状态](../../.artifacts/mail-system-review/native-mail/runs/final41-qa1080/native-14-state-contact.png) · [4K回执](../../.artifacts/mail-system-review/native-mail/runs/final41-main4k/verified.json) · [4K全部状态](../../.artifacts/mail-system-review/native-mail/runs/final41-main4k/native-14-state-contact.png) · [Limited普通启动回执](../../.artifacts/mail-system-review/native-mail/launcher-r41-verified.json)

后端645/645、0跳过；联合图形Edit57/57、Play9/9，原页校准5/5；JSON修复后受影响API/session/八层树/响应界联合Edit20/20、0跳过，范围有重叠不相加。[20项](../../.artifacts/mail-system-review/gm-tests/wire-depth-green.xml) 服务端便捷包装在真实`.4`用三新账号验证A/B/B减黑B只投A；预览后改原文件，确认仍冻结的A与37行动力，重复确认/重试同批不续期，A收1、B/C0、发送不发奖。[运营回执](../../.artifacts/mail-system-review/server/ops-main-qa/final-receipt.json) 注册因既有SMTP配置503，使用同ECC7Host+真实MySQL+回环SMTP夹具后再真实`.4`登录选服，没有手写账号/成员，不称外部SMTP已正常。

## 最终包、增量与本机服务

复用已分发R37母包，Windows x64/IL2CPP、应用0.1.27/runtime9.0.1、baseline7445597f1ef645db8ef946c4964503fc。33个母包文件180,889,815字节逐SHA相同；无AOT变化，未强更。内容41为105项2,284,270,002字节；清单SHA256 d2425fa5a3f6a892fdcee66029b1e7c9ec940489011b341b148ccdec70c90711，签名SHA256 5ee1aee2f41870593c3f47d2f438313b961eff880b5ddba5dab84f48b05671e8。

R37→41签名清单理论复用92项，新增/变化13项119,978,118字节。本次已有40缓存，实际下载6项3,440,222字节；随后4K、重跑与普通启动新增业务payload为0。理论清单差异和实际网络日志分开，未重新制作另一份相同全量安装ZIP，原R37最小包可复用。[签名/Feed总回执](../../.artifacts/mail-system-review/package/final41/execution-verified.json) · [最终包与运行回执](../../.artifacts/windows-r41/delivery-verified.json) · [37→41理论差异](../../.artifacts/windows-r41/payload-diff-r37-r41.json) · [最终直接视觉复核](../../.artifacts/mail-system-review/visual-review-final.json)

当前`.4:7728`后端实际仍windows-r38/server/host，PID34944；Host SHA256 ECC7FF4BADBEF6F6B85D28E637F5E044755420E489727C1CCA56DF97C97F614A。R41启动/发信仅代理已验证R38入口，不复制另一份数据库。017迁移已应用；切换前有一致性备份，未停止/还原MySQL或回退用户数据。5305合作切源41，只在本机开发服，未部署生产。356项既有工作区改动逐SHA保留，355项构建输入与R37保留快照/镜像一致，不混入任务提交。

## 最终未完成与证据边界

- 参考不足：排序按钮展开、无附件独立原流程、选一、即时满箱/满背包、打开中到期。已索要同版时间段；当前只接收时间新→旧并提示该事实，选一未实现。服务端边界绿不能代替未知画面还原。
- 原演出差异：单项蓝条原CFX/SCSP和裁切已接入，约267ms附带粒子、业务精确倍率和逐帧绝对同步未全恢复；原标题/三组粒子使用源素材，但随机种子/单位缩放/淡入为项目参数。最终礼包权威聚合顺序书签5→石2→石5，与原片/Editor石2→书签5→石5不同，未称顺序完全一致。书签已改正确token_ticket_rare，旧绿色图仅历史。
- 连续原生录制：前两次GDI窗口/桌面尝试分别得到残留Splash、超出DPI逻辑区域，判为失败并保留。最终按1440×810桌面逻辑区域录得实际1080渲染过程，完整解码并实际查看17–22.5秒的55张10fps连续取样，另保留14–22.5秒静默剪段；与源蓝条连续PTS实际比看。桌面受安全提示整体变暗，颜色以Framebuffer原图为准；只证明实际演出过程，未称原绝对同步/全部附带粒子通过。[原生连续剪段](../../.artifacts/mail-system-review/native-mail/runs/final41-desktopcrop1080/mail-flow-native-silent.mp4) · [连续帧](../../.artifacts/mail-system-review/native-mail/runs/final41-desktopcrop1080/effects-17-22.5-contact.png)。未自动授权公共网络，音频没有采入。
- 听感未验收：原入口/单项/确认/礼包/标题声音已定位绑定，关窗0.20秒淡出沿公共播放路径；当前没有音频输入完成实际试听，音量、叠加、取消尾声不以波形/绑定冒充通过，源音与原片供复听。
- 未执行/范围：双独立Windows进程通知、Android、关机后MySQL冷启动未实测；外部SMTP受既有配置限制。行动力未接自然恢复/关卡消费，naturalCap360是项目配置。R37旧装备的CP绝对值、未知累计次数和特殊演出限制继续保留。

## 阶段记录（以下历史待办以最终结果为准）

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
- `Assets/Game/Modules/FoundationDemo/Scripts/StartupNavigation.Mail.cs`与DevelopmentGmMail系列：登录/跨页/GM；`StartupNavigation.MailVerification.cs`为显式Development loopback原生诊断，正常玩家不准备验收邮件。`MailEntryBinder.cs`实际归属`Assets/Game/Modules/Mail/Scripts/`，与MailEntry预制命名一致。
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
