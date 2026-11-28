# 公共频道

## 数据层与生命周期

`ChannelChatModels` 与服务端Social JSON对应；`IChannelChatApi`提供偏好读取/保存、频道消息游标、发送和NDJSON失效监听。`ChannelChatApi`使用既有Bearer，禁用自动重定向；普通请求/流空闲均20秒取消，响应上限1Mi字符、NDJSON单行4096字符。关闭流把中断Socket/IO映射为取消，不输出令牌、正文或关键字。服务端边界和018/019/020部署要求见[Social说明](../../../../Server/Host/Social/README.md)。

`ChannelChatSession`每次登录独立拥有Api；可传入loginToken，登录Owner退出时须Dispose。默认频道1，保存收藏不自动切换。`JoinAsync`先成功读取再切换，attempt generation和channel generation分别防止迟到Join/消息回写；缓存按十进制字符串ID去重/数值排序，最多200条，仅保存当前频道消息。没有用double解析BIGINT，避免大ID精度丢失。

View/Binder通过 `Channel, Messages, Preferences, HasPendingSend, HasPendingPreferences` 与 `Changed/PreferencesChanged/Uncertain/Faulted` 观察状态。公开方法为 `JoinAsync`、`RefreshAsync`、`RefreshPreferencesAsync`、`SendAsync` / `RetryPendingSendAsync`、`SavePreferencesAsync` / `RetryPendingPreferencesAsync`、`ObserveAsync`、`Dispose`。登录Owner持有Observe任务和Session；页面关闭只取消该页等待/解绑，不销毁登录Session，登录观察可继续。重复Observe共用一个任务，切频道取消旧流，退出取消观察；掉线1/2/4/8秒退避，sync/chat失效合并后刷新，heartbeat不重复拉取。

发送和偏好保存生成requestId并深复制完整输入、目标channel和expectedVersion；同类仅允许一个待回执意图。页面取消在尚未dispatch时可取消该次尝试；dispatch后只结束页面等待，网络仍由登录生命周期等到回执。5xx/IO/超时保留原意图，重试沿原ID/原频道/原输入/原版本；明确4xx拒绝清除。较旧偏好响应或重放不会覆盖已知较高version；旧频道发送回执可以完成意图，但不混入当前频道缓存。退出取消网络、清缓存/事件/待回执并释放Api；不把正文写入本地偏好或日志。

## 数据层验证

根已在Unity EditMode实际运行首版七个Session测试，全部因NotImplemented出现RED，证据 `.artifacts/home-profile-chat-review/client-tests/session-iteration.xml`。实现并追加HTTP契约、redirect/响应与行大小上限、取消时Socket映射和未dispatch取消后，根再次串行Unity验证本类11/11 GREEN，证据 `.artifacts/home-profile-chat-review/client-tests/panels-editor.xml` / `.log`。该整组17项有15通过、两项失败属于Home旧预制缺key；不把本类GREEN当作原生客户端/实际频道UI通过。本代理没有启动Unity。

消息行/表情选择子页缺原作参考，数据API和已确认框架的验证不代表这些UI已还原；原生双账号实际聊天、掉线重连、视听及签名包仍由协调者集成验收。

冻结前独立逻辑审阅发现P1：消息缓存最高ID不能兼任权威读游标。缓存已有1、他人2尚未补读、自己3回执先到时，原实现将下一次补读推进至after3，永久漏2；020只保证数据库提交顺序，不能消除此客户端回执竞争。根已实际运行 `OwnSendReceiptCannotSkipUnreadOtherPlayerMessage` 得到0/1 RED，expected after1、actual after3，证据 `client-tests/chat-cursor-red.xml`。修复用独立readCursor，仅List响应中实际确认的合法nextAfter推进；发送回执即时可见但不推进，200条缓存裁剪也不影响。不同频道Join完整读取成功后提交新游标，同频道重开从旧读游标连续分页补齐；失败、取消、迟到Join不发布部分页，迟到并发读取不能回退。追加5个边界回归后根实际串行验证本类17/17 GREEN，证据 `client-tests/chat-cursor-green.xml` / `.log`。

同轮R37母包兼容检查实际发现 `Comparer<string>.Create` 无法解析，修复改用热更内自有IComparer实例保留十进制ID排序，不据此要求强更。API ContentType getter兼容调整由协调者独立处理；根随后实际兼容重验exit0，`compatibility-fixed.log` 记录 `ASTRAL_COMPATIBILITY_OK`，母包仍为R37/baseline7445597f1ef645db8ef946c4964503fc。静态兼容检查不代替下一签名包的实际Native验收。

## UI与参考

正在实施公共频道UI。已实际查看用户11月28日录像32.0秒抽屉、37.5秒收藏、39.5秒透明背景、42.5秒通知与45.5秒关键词；公开频道默认1，原片Rank4禁输入不作为项目功能限制。源布局为只读 `D:/sevenepic/原始资源/wnd/chat.csb`、`chat_section.csb`、`chat_favorite.csb`、`chat_filter.csb`。

补充实际查看11月14日用户原录像 `Screenrecorder-2026-11-14-18-54-07-786.mp4` 100.0秒文字行和102.0秒文字/表情消息及三列表情选择。它是世界竞技场页，版本/发布日期/可见倍速未确认，不能称为11月28日公共频道同版实录；只用共用的 `chat_node_text.csb`、`chat_node_emoji.csb`、`chat_emoji_tip.csb`、`chat_emoji_tip_item.csb` 及对应原素材补足消息和表情布局。原片抽帧与哈希在 `.artifacts/home-profile-chat-review/chat-ui/reference/`。

按钮切片按各自源控件保留：发送绿底cap(40,11,72,39)，收藏/切频道输入按钮cap(14,12,124,35)，相同PNG不能因名字相同就复用错误切片；抽屉cap(54,54,52,48)，黑金框cap(44,44,24,24)，原生720坐标统一按1.5映射1080 Canvas。制作前已实际查看最小素材联系图 `chat-ui/source/minimal-source-contact.png`。

已新增普通 `ChannelChat`/`ChannelChatBinder`、`ChatEntry` 与独立生成器 `ArenaUiBuilder.GenerateChannelChat()`；资源地址 `chat/window`、`chat/header-entry`。入口BindingMap的 `button` 配置普通点击声，`source` 为唯一自定义音源；Root在Canvas层接入，不挂业务组件。业务控件使用同一GameButton反馈，提示沿Common GameToast自动2秒关闭，加载提示无声、保存普通确认、错误/待确认使用现有阻止提示声；本轮原声尚未试听核对。

Binder接受登录Owner持有的Session、`Action close`、`Action<string> unavailable`/`playerSelected`、可选 `Func<string> ownPlayerId` 与 `Func<string,Sprite> avatarArt`。新账号默认频道1；登录Owner先读取收藏并Join(favoriteChannel??1)，重新打开Chat沿当前Session.Channel，不重复跳收藏；读/发/表情/当前频道历史来自Session；关闭只解绑页面、覆盖层和头像回调，已dispatch写入等登录回执，不销毁Session或Observe。窗口覆盖层由Binder独立WindowCoordinator管理，每次开窗独立BindingScope。自己以服务器player.id匹配ownPlayerId显示绿名与[me]，其他玩家白名；所有名字/正文/关键词关闭richText，头像只回传playerId交外层统一资料页。

收藏1..9999只保存偏好，不静默跳频道；切频道入口复用已实际看清的收藏输入框架，真正成功Join后才换标题和列表，未构造未见原片的n_channel_layer弹板。关键词编辑使用已确认的五个原黑底输入行，去空槽、最多5个、每个20 Unicode标量；没有冒造录像未显示的独立编辑弹板。通知提交完整四个布尔字段，骑士团两个入口和暂无权威公告的折叠公告入口调用外层通用未完成功能提示。目前没有获得5★英雄/神器或骑士团宣传的权威广播事件源，此处只真实保存/读取偏好，未宣称这些广播已连通。generalChat关闭不删除当前频道可读历史；登录级新消息通知由Owner调度，页面不另开后台监听。透明背景保留完整原图的灰黑底与阴影，只降低原节点opacity，不能按像素颜色抹除中性背景；转换与录像推定边界见下文。

`Tools/Chat/ImportChatAssets.py`仅导入37个最小UI变体和服务端白名单67张原表情，`Res/chat-source.json`记录源/输出SHA、用途opacity、转换和cap；默认RGBA32、无Mip、源尺寸，D:/sevenepic只读。发送/收藏使用同PNG的不同切片变体，其他业务模块无反向依赖。原图三列表情固定原比例；窄窗将整个Picker移回可见范围，不拉伸单格。

### 收藏与透明背景复核（内容45候选，未覆盖44）

实际查看原录屏37.5秒收藏、39.0/39.5秒透明切换及48.5秒无Chat底图，与 `editor-social-final/17-chat-favorite-input.png` / `24-chat-transparent.png` 对照。标题双分隔重叠左半、范围提示出框、透明底被删，均为失败候选。原素材中PNG中性灰不是透明孔；旧导入器以 `r>g+3 && g>b+3` 留金色、清零其他alpha，导致源中心(18,18,18,230)变成alpha0，这不是原版透明效果。39.0与39.5的静态文字ROI相对48.5底图透过率约.056与.309，比5.49，支持原节点opacity约127/255的半透明用途副本；这是录像推定值，不冒称chat.csb默认常数。原normal仍opacity255。

恢复 `_dim.png`（160×90）作为Chat全屏dim/opacity127、收藏黑tint全屏dim/opacity255，恢复 `_grow.png` 的Chat顶部153、收藏局部178阴影。Unity Linear下直接黑alpha.65显著偏浅；暗中性像素使用白底参照coverage `(1-linear(1-a+a*g))/(1-linear(g))`，其中a包含原图alpha和节点opacity，g保留原灰RGB；纯黑退化为 `1-linear(1-a)`。原灰18的Drawer中心输出alpha为251(normal)和180(透明)，不是把Image.color.a设成128，也不清掉背景像素；金色和彩色边缘保留源RGB及节点opacity。派生PNG、原节点tint、来源SHA均已记账。

收藏原frame558×386、中心Canvas偏移(+3,+24)已正确，保留高度；原n_channel组(37,30)换算后输入/按钮top246、范围top307.38，恢复到原可见边框之内。标题按n_top偏移及原scale还原；右分隔使用原180°和中心旋转，避免绕左上pivot反向覆盖左条。Chat大底原y805.348，对720高设计应top−85.348；旧值−128.348又乘1.5，额外上移64.5px，使本应在屏外的底部金角露出。现修正原位置。公告折叠箭头保留 `chat_section.csb` 的17×26原PNG，再按原90°方向旋转一次，投影宽高26:17；不先把它压成24×17再旋转造成窄V。

根已实际跑旧预制RED：`.artifacts/home-social-visual-fix/notice-chat-red.xml` 联合6/6失败，其中本模块3项分别确认sourceTop192.522应128.022、右分隔bounds错、FavoriteDim未赋值。像素离线RED为2失败1缺图，修后实际3/3 GREEN在 `chat/pixels-green.log`；4项新 `ChannelChatVisualTests` 等根统一生成/运行GREEN并复拍17/24不同背景。未启动Unity、迁移/清理数据库或切Feed；该候选交内容45，不能将44旧图或像素检查当新Native/人工听感通过。

根已实际运行 `ChannelChatPrefabTests` 得3/3失败（预制缺失），证据 `.artifacts/home-profile-chat-review/client-tests/chat-prefab-red.xml`。生产候选及5项 `ChannelChatBinderTests` 已写，待协调者统一编译、生成预制、运行Edit/Play并做1080/4K/窄窗及真实两账号Native对照；源码完成、数据层GREEN不代表这些界面/视听已通过。

独立审阅发现慢偏好回执期间关键词仍可编辑、第二笔输入可能被第一回执旧快照覆盖。根实际Play复现：`.artifacts/home-profile-chat-review/client-tests/chat-preference-red.xml` 共7项6过1失败，新增慢回执项断言interactable=false而实得true；原3项Chat行为通过。当前最小修复在发写入前立即禁用关键词字段、保存期间保留已提交文本，回执后恢复/重绘，不排队第二笔意图；等待该项重新GREEN。

随后扩展不确定IO边界的真实RED：`chat-uncertain-red.xml` 单项失败（关键词仍可编辑）；当前候选在HasPendingPreferences时同时锁关键词/收藏输入及所有偏好写控件，不接受第二意图。重新打开Chat先按原requestId/expectedVersion/冻结字段自动RetryPendingPreferences，再Join/读偏好；若仍IO保留锁定和准确pending提示，关闭并重新打开为明确重试路径。保存中保留草稿，已知回执后才重绘解锁；待5项完整Play GREEN与原生验证。

根统一Editor已实际53/53通过，其中包括3项Chat预制合同（`client-tests/integrated-editor.xml`）；这不替代Native视听。另新增仅由Root QA入口调用的 `FoundationDemo/Scripts/SocialChatSettingsSmoke.cs`：Development显式参数及127.0.0.6双重准入，同账号独立API核收藏123（不切当前1）、UI频道2→1、完整通知四值、关键词离焦/去空槽、透明开关和最终持久化；追加17–25共9个实际Player状态截图，capture路径/不覆盖规则由Root传入回调控制。未触碰普通账号、未删除历史，当前尚未在Native执行。

最新已实际查看统一Play回执 `client-tests/integrated-play.xml`：总14/14通过，其中ChannelChatBinder五项5/5，包含慢回执禁输入/保留草稿、两次IO后重开同原意图重试解锁。该回执只证明当前UI绑定行为；另行Session游标审阅/新回归、最终Native25状态、源图逐图比对和音频试听仍由根继续验收，不能据此称完整还原。
