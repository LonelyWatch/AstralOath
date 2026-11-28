# FoundationDemo

当前本机签名内容为R46/app0.1.27（产品d312f43b，R37母包），入口`.artifacts/windows-r46/Play-R46.exe`。1080/4K各Social25+召唤12及Mail14状态实际通过，单项奖励源星粒子真实启用；游客首会话串行修复后两冷启动预取警告为0。普通Limited入口实际显示登录页、服务/Feed身份及0业务重复下载通过，不称无人干预已登录。R44/45以下是候选历史，当前交付、原版/前后图、代码位置和视听缺口以[最终回执](../../../../docs/delivery/2026-11-28-home-social-visual-fix.md)为准；没有用45桌面遮挡录像冒充Native连续奖励。

R45候选的邮件原生诊断先等待共享单项奖励的源粒子启用，再拍第10状态并记录`singleRewardParticleCount>0`，避免只拍到267ms之前的蓝条闪光。仍保留14状态、4封真实领取、资源Delta和原页恢复断言；它只在显式开发验收入口运行，不在正常业务中发测试邮件。角色卡/频道等R44候选随后与本轮视觉修正统一进入R45，实际包及验收状态见[本轮方案](../../../../docs/plans/2026-11-28-home-social-visual-fix.md)。

11/28追加反馈后，43确认文字视觉被否定；44候选修正共享文字，并继续集成主页资料/公共频道，下述43/44仅为各自阶段事实。其后45视觉修订及46游客连接修复已完成当前实包验收，不将旧阶段图录冒作46截图。

11/28邮件UI修订只重存开发GM所在页的共享MailEntry顶栏，保留自发当前账号及原收发协议；无启动/AOT改动。最终产品源824d49ee的签名内容43已本机发布，Editor43/43、Play7/7及真实IL2CPP 1080/4K帧缓冲各14状态通过，旧41/42入口Limited实际启动43通过，原主服务/MySQL不换。人工试听等未完见[本轮复核](../../../../docs/delivery/2026-11-28-mail-ui-followup.md)，不以前内容41或42行为结果替代最终图。

Foundation内容43新增显式资源地址`common/source-text-variants`，仅收集原TMP Mobile描边及两种UI裁切关键字的四组合SVC；由Yoo合入shader bundle。实际43包vertex/fragment各8程序且四个描边组合均保留，解决Editor正常而Player字细；它不是开窗业务组件，不加载新AOT接口，不变更现有业务地址或服务协议。

邮件系统首次本机交付为R37母包/app0.1.27的签名内容41，产品739ec080。IL2CPP 1080p/4K各14状态及完整摘要、GM当前账号自发、收领/历史/原页恢复通过；Limited用户`Play-R41.exe`实际启动原本体通过。后端仍已验证R38路径，41助手仅代理，不复制数据库。暖重开0业务payload；38/39/40失败分别保留。声音/原特效/参考不足等边界见[当时回执](../../../../docs/delivery/2026-11-27-mail-system.md)，下列旧版本记录不作为本次全部通过的证明。

装备开发冒烟增加本轮校验：真实穿训练头盔后左侧总生命增加80、卸下后零值隐藏；新锻造装备实际强化至+15后，GET的四条副属性必须已知且强化次数合计5。成功分别记录`ASTRAL_EQUIPMENT_BONUS_PLAYER_OK`与`ASTRAL_ENHANCEMENT_COUNTS_PLAYER_OK`。+15实际显示的三个数字Image另读取其Sprite纹理格式及26×34源尺寸，必须为RGBA32，记录`ASTRAL_ENHANCEMENT_DIGITS_PLAYER_OK`；不从源meta声明推断Player格式，也不倒推旧版本已发生BC失真。此为显式开发回环验证路径，常规登录不执行；目标Player是否已运行见[本轮回执](../../../../docs/delivery/2026-11-26-equipment-followup.md)。

本机Windows0.1.26/R36最小母包与签名资源已配对交付，新启动器为 `.artifacts/windows-r36/Play-R36.exe`，本体位于同目录 `client/AstralOath.exe`。原生1080p和4K强化冒烟通过；隐藏截图无效与DPI采集问题单列，启动器冷恢复本地游戏服务和内容源通过，正常登录点击未自动验收。[回执与边界](../../../../docs/delivery/2026-11-26-equipment-enhancement.md)

新GM锻造读取版本化 `MainValues` 和按品质 `GradeRanges`，提交同一 `rulesVersion`，不再向新客户端预览线性近似。资源页同时列装备/饰品强化材料。`Tools/Test-WindowsEquipment.ps1 -Enhancement` 在显式开发/回环访客下，先验旧穿脱，再实际GM发放、背包登记、仅经验、+3橙底、+15红底及返回，截图只写D盘；常规登录不创建这段验收委托。[规则与边界](../../../../docs/development/equipment-enhancement.md)

Foundation 收集设置新增二维音频地址组 `UiAudio`，由 `UiAudioCollector.Refresh` 维护 Common、Summon 与竞技场门的音频目录。声音单独保留加载句柄，尾音结束后释放，详细范围及母包要求见 [复核记录](../../../../docs/delivery/2026-11-23-equipment-battle-review.md)。

Windows 召唤冒烟从主页实际进入卡池，读取实时技能的 GPU 目标与窗口尺寸并写入 `ASTRAL_SUMMON_FEATURED_LIVE`，切换常驻池后再验证愿望单与结果页。[召唤预览来源](../../../../docs/delivery/2026-11-22-client-image-quality.md)

开发构建登录后显示 GM 悬浮入口。资源页设置钱包和材料的绝对余额，库存页可搜索并发放英雄、神器和训练装备，锻造页可为当前账号生成自定义普通装备，诊断页可查看和复制当前运行信息。独立窗口地址为 `development/gm-window`。操作沿用当前账号会话，断线重试沿用请求 ID，账号退出时释放窗口与绑定。

### 开发 GM 邮件

邮件作为独立的第五个模块，沿用用户确认的帮助入口和深色弹窗；制作前实际查看的样式依据是主工作区 `.artifacts/gm-r34/screens/inventory.png`。此前 GM 无发信页。左列滚动选择模板，中列编辑普通/GM行为类型、发件人、纯文本标题正文、有效期与已选附件，右列按服务器目录分类/搜索附件并填数量。维护、活动、通知等为独立模板用途，不从发件人名字判断信件类型。类型、发件人、合法资源、礼包和数量/长度边界统一读取 `GET /api/mail/catalog`，不另写奖励规则。

`DevelopmentGmWindowBinder.ConfigureMail(MailSession session, DevelopmentGmMailApi api)` 在登录安装 GM 前注入；API 使用 `DevelopmentGmMailApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer)`。`Install(owner)` 负责释放此 API 和登录级发送意图，不释放共享 MailSession。目录和预览读取受开窗 scope 取消；发送使用登录生命周期及20秒超时，关窗只取消界面等待，未决请求仍保留原 `previewId/requestId`。重开后可重试原请求，正在提交时锁定草稿并阻止双击重复 POST；收到成功回执后只能显式点「再发一封」才重新预览并创建新 request。明确拒绝后可修改草稿重新预览。

点击「生成真实预览」调用 `/api/development/gm/mail/preview`，只提交类型、模板、正文、附件和有效期，不带收件身份字段。服务器冻结页只读显示当前玩家/区服、一位收件人、发件人、正文、附件、实际合计奖励及预览有效时刻，用户确认后调用 `/api/development/gm/mail/send`。发送只更新共享邮箱摘要，不触发库存/钱包校准；玩家领取后的入账归 MailSession 和主导航处理。多人/白名单/黑名单/整区入口只属于服务端运营工具，不在客户端 GM。

编辑入口为 `Assets/Game/Editor/ArenaUiBuilder.DevelopmentGm{,.Mail}.cs`，调用 `ArenaUiBuilder.GenerateDevelopmentGmWindow` 输出同 GUID 的 `Res/Prefab/DevelopmentGmWindow.prefab`，地址仍是 `development/gm-window`。新预制绑定 `mailPanel/mailCompose/mailPreview`、四个滚动区域 `mailTemplates/mailResources/mailAttachments/mailFrozen` 与纯文本 `mailFrozenText`；业务 Binder 不挂预制。

纯逻辑 `GmMailIntentTests` 已通过同一测试源的 .NET10 断言适配运行，实际 RED 为「预览期间 form 未锁」断言失败(exit1)，GREEN 覆盖预览/发送锁定、双击单次提交、关窗、丢回执、同 request/preview 重试、明确再发与关窗后迟到成功仍通知共享邮箱。`GmMailApiTests` 验证类型化自发草稿不带收件字段、send只带冻结意图和稳定错误不回显正文。独立 `.6:7728` QA账号的真实目录→预览→发送→同请求重试返回同delivery/batch，发送前后全部钱包/材料resources完全相同，未领取该信；原始实服回执在 `api-green.log`。命令入口及日志统一见 `.artifacts/mail-system-review/gm-tests/{GmTests.csproj,red.log,green.log,api-green.log}`。

复审实际发现 `GameButton.Bind` 随隐藏区域 `OnDisable` 解绑，切模块返回后预览无响应。`GmMailBindingTests` 先取得有效 PlayMode RED（预览请求 expected1/actual0），改为随开窗作用域清理的 `Listen`、保留业务意图提交门闩后，图形 PlayMode 1/1通过，覆盖切模块、预览→修改→预览、发送→再发→预览→发送，并断言发送不调用钱包/库存校准。联合图形 EditMode 14/14通过，包括状态3/API2/新布局4/旧GM布局5；命令是Unity `-runTests -testPlatform PlayMode -testFilter GmMailBindingTests` 与 `-testPlatform EditMode -testFilter "GmMailIntentTests|GmMailApiTests|GmMailLayoutTests|DevelopmentGmLayoutTests"`。回执为 `gm-tests/{binding-red,binding-green,edit-green}.{log,xml}`，无许可阻塞冒充失败测试。

制作后已实际逐张查看 `gm-tests/screens/gm-compose-{1080,4k,narrow}.png`、`gm-preview-1080.png` 与 `gm-sent-1080.png`，确认 1920×1080、3840×2160和1440×1080布局、模块分类、草稿、冻结预览及成功回执清晰且无重叠；标签字符串按纯文本显示。上述图形使用受控API响应及独立GM窗口，只证明该页的布局/交互，真实发信及余额证据独立记录，不冒充原生登录整合实测。新页此前不存在；旧 GM 图片仅作已认可风格依据。原生整合、本轮邮件热更分发尚未执行。未新增专属动画/音效，按钮复用现有反馈；本页按钮听感尚未实听。

11/27首次原生候选内容38（源码`b9c12fcb`）实际激活后，维护模板预览请求在隔离`.6`返回HTTP200，但原生仍停留草稿页并显示连接失败，25秒等待冻结预览超时；未出现send，未宣称发信/分发验收通过。证据为 `.artifacts/mail-system-review/native-mail/runs/{qa1080,qa1080-diagnostic}/` 的player日志、首图及延迟桌面图。同源Mono Editor对真实`.6`的新QA账号预览回归1/1通过，直接解析mail/reward及Binder可发送状态均有效；见 `gm-tests/native-preview-live.{log,xml}`。该Mono日志另有递归`MailReward.children`的序列化深度警告，是否造成Native失败仍待诊断，不能据此直接改DTO或替换序列化器。新增`GmMailLiveTests`仅在`ASTRAL_GM_MAIL_LIVE=1`时用新账号执行这条隔离预览，不发信。诊断补丁只在Development、HTTP(S)回环和显式`-astral-mail-smoke`同时满足时记录API阶段、异常类型/调用栈及预览字段存在bits；不记录异常Message、响应内容、标题正文、身份值或凭据，普通运行不输出。门槛测试与既有纯逻辑/API回归实际通过；下一次Native候选内容39由协调者构建及执行，诊断本身不代表修复完成。

候选39已实际定位`phase=validate;decoded=True`、预览/区服/玩家基础字段存在、人数1，但`mail=False;rewards=False`（`native-mail/runs/diagnostic39/player.log`）。修复统一覆盖GM及玩家MailApi：沿现行SummonApi使用已安装/母包保留的Newtonsoft JsonConvert普通读写重载，绕过Unity原生TypeTree；保持服务器允许的八层`MailReward.children`完整树，不新增JSON库或AOT，不改模板规则。非法JSON转固定IOException，诊断门槛和未决请求语义保留。`GmMailWireTests`在旧实现实际RED2/2（Mono虽读到末层但出现2/3次TypeTree深度警告）后验证深层leaf数量137、list/read/claim/GM预览及复合回执；回执在`gm-tests/wire-depth-{red,green}.{log,xml}`。Native最终修复闭环和内容40签名分发状态由协调者随后记录；不得将该Editor验证写成已Native交付。

在 GM 的「锻造」页依次选择部位、装备等级、品质、套装和主属性，输入强化等级，页面会预览主属性数值，显示已经解锁的副属性行。每行选择属性、填写累计掷数和最终数值，百分比属性直接按百分数填写。70、85 级装备可选 +0 至 +15，90 级固定 +15。一次可生成 1 至 10 件。界面从当前开发服获取规则，检查重复属性、数值区间和可分配掷数，成功后再次进入背包可查看装备。缺少锻造规则的旧服会显示不可用。服务端的 15 项规则和接口测试已通过，Windows 开发母包中的可见性、生成、关窗返回与库存刷新需在目标版本实测。[锻造规则与验收范围](../../../../Server/README.md#开发-gm-自定义锻造)

点击「生成」后，数量、强化等级、属性行和生成按钮会锁定。若回执丢失，按钮变为「重试原请求」，沿用原请求 ID 和完整配方。收到成功回执后可编辑下一件。PlayMode 用例 `GmActionsRetryAndRefreshTheAccount` 拦住一笔两件的请求，尝试点击数量框改为八件，再模拟 HTTP 503 和重试。修复前提交期间仍能编辑，修复后第一次和第二次请求的完整内容相同，界面始终显示两件。两次结果分别在 `.artifacts/equipment-manage-followup/revision/gm-forge-freeze-red.xml` 和 `gm-forge-freeze-green.xml`。

GM 的帮助图标与文字共用一块深色按钮背景。按住拖动可以将入口放在安全区内，轻点打开。位置随本次登录会话从大厅带入战斗及带回。资源与库存共用可伸缩的左列，收起库存分类后，搜索框和列表自动向上填充。图形回归以 12 种资源覆盖长列表，验证 720p、810p、1080p 下能显示至少六个完整条目并能滚到末项。本次反馈和改动前后截图在主工作区的 `.artifacts/gm-r34/reported-list.png`、`.artifacts/gm-ui/` 和 `.artifacts/gm-r34/screens/`。

登录及大厅到英雄页共用 `ScreenFadeTransition`。加载期间保持全黑，目标准备完成后淡出遮罩；取消或失败会清理遮罩并恢复输入。普通背包入口遵循原录像直接切页。[录屏和回归](../../../../docs/delivery/2026-11-22-screen-fade.md)

普通装备原生回归也会读出背包中头盔的描述和分数，再打开单槽候选，要求两处数据一致后才继续穿脱。

原生双端房间回归在主页启动成功后驱动操作。房主确认热更已激活且已经入房，脚本才以同一安装缓存打开第二个客户端。每个进程分别检查消息和成员数，再自行退出。

普通装备的原生回归可用 `Tools/Test-WindowsEquipment.ps1` 运行。`-Player` 指向隔离测试安装中的 Windows Player，`-ExpectedRelease` 指定待验签名内容版本，`-Endpoint` 指向显式开启训练装备的本机开发服。脚本以独立访客从背包筛选头盔，再点英雄空槽，选择同一件装备并配戴。回到英雄页后，再点已穿槽，从单件详情解除配戴，检查生命增加 80 后还原。

角色资源收集清单纳入萝季、席可、维德瑞和亚敏的三技能切入描述与 78 张原生帧。战斗场景的显式引用与内容包使用同一份导入结果。[录像与依赖核对](../../../../docs/delivery/2026-11-21-s3-video-review.md)

模拟战结算的确认按钮先读取当前房间成员关系，仍在房内就直接恢复原房间，房间已经关闭则返回目录。旧局的选禁 ID 会在这一登录会话内跳过，直到房主开启新局。[返回流程回归](../../../../docs/delivery/2026-11-21-battle-and-summon-r23.md)

演示场景的 Echo 和游戏主页共用本机游戏服务 `http://localhost:7728`。进程环境变量 `ASTRAL_FOUNDATION_URL` 可覆盖场景中保存的地址。

正式 GameHome 先由母包完成登录、选服与更新，再调用 FoundationEntry。YooAsset 的 `entry/theme` 和活动背景由 Luban 源表及收集器登记，随签名资源发布。 [启动和更新边界](../../../../docs/development/game-entry.md)

`Scripts` 定义 `FoundationEntry`、普通 C# `FoundationWindow` 与 `FoundationWindowBinder`，`Configs` 保存 Luban 演示生成物，`Res` 保存字体、窗口预制体与示例资源。场景位于 `Scenes/FoundationDemo.unity`。

YooAsset 包 `Foundation` 提供下列业务资源。构建另外生成代码清单、业务程序集和母包 AOT 补充元数据。

- `foundation/demo-config` 提供 Luban 生成的演示行。
- `foundation/demo-localization` 提供 Luban 生成的简体中文业务文案。
- `foundation/demo-resource` 提供可见的文本资源。
- `foundation/foundation-window` 提供只挂通用 BindingMap 的窗口预制体；普通 C# FoundationWindow 通过 UIViewRegistry 注册、加载并访问其组件。

GameHome 在点击「更多」或「背包」时加载对应预制体，进入英雄或背包后查询当前区服的持有实例。更多抽屉的遮罩关闭后恢复主页竞技场入口。页面退出取消加载并释放视图和资源。收集地址为 `home/more-menu`、`home/hero-inventory` 和 `home/hero-art`。

正式 GameHome 导航在进入选禁时才加载 ArenaDraft 和角色预览资源。Foundation 母包的内置场景为 GameHome。从当前 Foundation YooAsset 包预载 `battle/online-transition` 和 `battle/online-scene`；选禁完成且权威快照有效后，才在闸门遮挡下激活对局。Windows 冒烟脚本的 `-Preload` 选项演练预载及正式衔接。新增场景加载服务属于 AOT 层，0.1.9 及更早版本需安装新 Windows 母包。

窗口每次打开建立新作用域，关闭时取消请求并释放资源、事件和动画。GameHome 入口作用域持有竞技场 HTTP 客户端，退出入口时统一取消请求并释放连接。演示文案使用 key，默认为简体中文，语音选择独立。详细启动、更新和构建步骤见 `docs/architecture/client.md`。

`AssetBundleCollectorSetting.asset` 同时保存 CharacterLab 包的收集配置。十名角色接入后，角色实验室组由导入清单重新生成，继续使用原稳定地址和角色分包规则。见[十名角色接入](../../../../docs/delivery/2026-11-13-ten-hero-roster.md)。

共享的 YooAsset 收集器配置另有 CharacterLab 包，梅赛德丝基础皮肤 S3 的 21 帧和帧时长描述已登记在该包。详情见 [立绘回归](../../../../docs/delivery/2026-11-14-mercedes-s3-cutin.md)。

CharacterLab 的 YooAsset 收集设置由编辑器按导入清单维护，本次增量包含 c1010 的依赖。

卡琳 c1011 已接入角色内容；三技能、音频及已知缺项见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

萝菲 c1019 已接入三张演出图及原版日语。新增原版 voc3 银行，战斗按阵容选择；裂伤有效生命独立于基础面板。规则、表现诊断与音频来源见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

实验室 YooAsset 收集配置已接入第 21–30 名角色及相关依赖，已有地址和 GUID 保持一致。 [角色批次](../../../../docs/delivery/2026-11-14-roster21-30.md)

正式启动使用 Home/Scenes/GameHome.unity，FoundationDemo 场景保留作诊断。Foundation 包新增 HomeArena 收集组及两个业务程序集。 [启动与模拟战](../../../../docs/development/arena-lobby.md)

正式主页、模拟战目录及房间切换使用现有 `WindowCoordinator`。启动根拥有窗口协调器、内容加载和竞技场会话生命周期。旧 Foundation 诊断窗口仍使用 `IClientWindow` 注册入口。
正式启动的 Windows Canvas 提供 `DesktopDisplayMode`，由 Home Binder 显示切换入口。Arena 房间的表情浮层与其他窗口共用启动阶段创建的 `WindowCoordinator`。

BP 复用主页 `WindowCoordinator` 统一管理选人筛选浮层，窗口退出释放筛选订阅；选人舞台本身不显示返回箭头。

Windows 双端联机验证应为独立访客会话。开发 Player 测试入口使用预生成房间，按实际房主或客席身份进入同一场战斗，验证双方座位、回合时钟和事件同步。

Windows 开发包同时传入 `-astral-startup-smoke -astral-summon-smoke` 可用回环测试服检查召唤入口。检查会等待两个卡池就绪及伊赛丽亚技能预览显现，再切到誓约池，确认技能收起并显示七人愿望单立绘。通过时写入 `ASTRAL_SUMMON_PLAYER_OK`。

Windows Development 客户端在回环测试服上可使用 `-astral-startup-smoke -astral-summon-resize-smoke`。使用专属访客十连并切换原生窗口尺寸，成功写入 ASTRAL_SUMMON_RESIZE_OK 和 ASTRAL_SUMMON_PLAYER_OK。

使用 `Tools/Test-WindowsArenaRoomEvents.ps1 -Player <客户端程序路径> -ExpectedRelease <版本号>` 验证已打包的房间界面。传入发行目录中 `client/AstralOath.exe` 的路径。脚本以独立测试身份启动两个 Windows 图形进程，房主从主页创建临时房，客方从目录加入。双方各发一条消息，在实际聊天列表检查各显示一次，并验证成员数和离房。成功记录在 `.artifacts/room-events-player` 下。此开关限定 Development 客户端及回环服务。

第 31–40 名批次将实验室收集清单扩大到 3722 项。第 41–50 名新增 1114 项，合计 4836 项。角色实验室构建输入校验读取相同的收集器、地址及资源目录。[第 31–40 名角色与实测记录](../../../../docs/delivery/2026-11-20-roster31-40.md)

第 51–60 名新增 1,253 个收集地址，清单现含 6,089 项。塔玛林尔的两种战斗模型与对应时间轴分别登记，旧地址继续解析到原资源。 [本批角色交付](../../../../docs/delivery/2026-11-20-roster51-60.md)

第 61–70 名新增 1,199 个收集地址，现有 7,288 项。原有 6,089 个目录项逐字段核对一致。 [批次记录](../../../../docs/delivery/2026-11-20-roster61-70.md)

### 登录后首次进入英雄、背包与召唤

登录准入成功后，`StartupNavigation` 用全屏黑幕遮住主页并拦截输入，在同一个登录生命周期内准备更多菜单、英雄和背包共用的库存视图、召唤视图以及四份美术目录。三个视图各创建一次，页面打开时才绑定按钮、创建角色演出、RenderTexture 和条目。登录取消或首页加载失败会移除黑幕并释放已取得的句柄。

账户首次读取最多等待五秒。英雄库存、神器库存、普通装备库存和召唤状态各发一次 GET。PlayMode 从正式 FoundationEntry 打开背包、英雄、召唤共请求 12 个资源地址，其中 7 个用于首次进入这三个入口。单项读取失败在进入该入口时重试。召唤首读快照只用一次，超过 30 秒或跨免费刷新、卡池边界时重新读取。测试见 `HomeStartupPreparationTests`。

### 主页与竞技场的会话缓存

登录黑屏期间并行取得英雄、神器、普通装备和召唤首屏数据，并预解析默认持有英雄的立绘。英雄页、背包和竞技场目录在同一个 GameHome 场景中切换。持有数据、页面资源、已经解析的英雄立绘由该账号本次登录持有，进入及退出战斗场景后继续复用。关闭页面释放演员、摄像机、渲染目标和页面事件，退出登录释放会话缓存。召唤和保管箱领取取得权威回执后，按实例 ID 把实发奖励加入对应库存。服务端明确拒绝时保留原库存，提交后失联等结果不确定的情况立即发起权威校准。神器穿脱、强化、锁定和出售沿用神器库存失效机制。召唤横幅与免费重置时间保留原有时效检查。[导航与账号隔离](../../../../docs/delivery/2026-11-21-hero-layout-and-navigation.md)

普通装备在当前登录持有带版本的快照。穿戴、替换和卸下成功后，权威回执直接更新缓存。重复送达的旧版本回执不能覆盖新值。窗口回到前台时，用 HEAD 和已知版本验证装备库存。304 保留快照，版本变化才补读并通知打开的装备页。写入冲突或提交结果不确定也触发装备补读，英雄和神器继续使用各自快照。

### 邮件登录协调（11/27，联调中）

`StartupNavigation.Mail.cs`在登录作用域创建MailApi/MailSession，订阅失效、确定回执与已发送但结果不明事件。初次摘要读取限5秒，订阅断线由Mail重连补读；正文和列表是独立overlay，关闭保留底下原页面。领取/不明结果使FirstReads失效并原地刷新现有Binder，不能通过重开页面丢弃页签、筛选、实例选择或滚动。此项正与Home/Summon联合回归，尚非原生分发结论。

`MailEntryBinder.cs`为缓存顶栏持有页面作用域监听，避免GameButton.Bind在更多菜单隐藏后解绑导致第二次点击失效；手动监听同样在页面销毁时移除并防重复打开。真实Unity失败1项→绿色1项回执在`.artifacts/mail-system-review/tests/header-reopen-{red,green}.xml`。图标和角标共用MailEntry嵌套预制，生成源ArenaUiBuilder.HomeMoreLayout；MailWindow由独立sorting100 Canvas持有，开发GM为150。登录退出销毁portal、取消订阅与未决请求。

同一原地刷新也用于开发GM资源写后的页面校准，GM关闭后不再重建英雄/装备/召唤页面；登录缓存先失效，真实材料和钱包重新读取，再补邮件摘要的独立资源revision。邮件发送本身不触发此发奖校准；仅领取/写入资源才改变真实数据。

原生邮件诊断额外验证第二HTTP生产者投递后的事件流自动摘要/角标更新，准备信回执不直接写可见MailSession、不用手动摘要刷新掩盖通知失效。使用`-astral-arena-battle-smoke -astral-mail-smoke`；该显式Development回环诊断创建独立测试访客，不修改正常账号，不将同进程两个HTTP实例称作两个独立Windows进程。

内容40原生已实际完成14个状态与余额断言，但末尾验收JSON仍以JsonUtility写入，跨程序集initial/final摘要漏写，外部脚本因此正确拒绝通过。仅诊断回执改用同一既有JsonConvert路径保留摘要，重新构建更高内容41，不覆盖40签名。历史失败在`.artifacts/mail-system-review/native-mail/runs/final40-qa1080/`；最终结果以41原生重跑为准。

### 主页资料和公共频道组合（11/28，R44候选）

`StartupNavigation.Social.cs`按登录拥有PlayerProfileSession/ChannelChatSession。首次读取限5秒；新账号默认频道1，有收藏时仅新登录优先进入收藏频道，打开/关闭抽屉保留当前频道。聊天观察由登录Owner启动，正文、关键词和令牌不进诊断日志。本人资料回执立即更新Home名字/头像，未知Rank/经验不伪造值。普通角色页、召唤、背包、邮件及更多菜单顶栏共用ChatEntry原素材预制，页面监听在自己的Scope解绑；缓存Mail入口只由每次Mail opening持有，避免底下页面扫描后让聊天越过Mail关闭仍残留。

资料、聊天和通用未完成提示分别使用`SocialPanel`的独立WindowCoordinator/BindingScope及Canvas层。未完成提示盖住更多菜单，点击关闭恢复原菜单；聊天头像打开共用只读资料页，关闭只回到原聊天。退出登录或父页面关闭立即取消输入/加载、销毁portal并释放资源，不用主协调器的单一overlay强行替换原窗口。Common只保存FeatureNotice素材预制，普通View/Binder留在既有热更FoundationDemo，Home通过回调访问业务模块，未新增Common/AOT类型。

正式原生诊断使用Development显式`-astral-social-smoke`且严格限制`127.0.0.6`，入口`SocialPlayerSmoke`及`SocialChatSettingsSmoke`。它创建独立QA身份，真实UI修改昵称/问候语/隐私并用独立API核对，测试主页及更多所有灰入口、两账号文字/原表情和事件流入站、头像只读资料、收藏/频道/通知/关键词/透明设置及重开。25状态截图是Player帧缓冲，另保存召唤12帧连续idle及实际七个RT尺寸、名字几何斜度、短时idle帧间隔；不把HTTP第二账号称作第二个Windows进程，不把诊断断言当视觉或人工试听通过。输出必须为D盘新目录，失败保留证据且不覆盖旧回执。

当前真实联合Editor53/53、Play14/14通过；修复前布局三项、关键词慢回执/不确定回执、TMP斜体及首次诊断编译失败均保留。主服务安全切服、签名构建和Native实包仍待协调者执行，结果入口见[本轮参考和边界](../../../../docs/development/home-profile-chat-reference.md)。

### FeatureNotice 原素材复核（R45候选，11/28）

用户否定44受控提示候选后，按同一录像61.5秒稳定帧重新查看并测量；原版版本、发布日期和可见倍速未知。`ArenaUiBuilder.FeatureNotice.cs`不再复用`msgbox.csb`的共享决策面板，改用`msgbox_renewal.csb`自己的`renew_popup_bg_2.png`（cap54/54/148/302，节点opacity229）。上横线`_bar_23`（cap30/4/14/4、opacity127）和下横线`_bar_2`（cap32/4/34/2、opacity51）分别九宫格，保留原菱形端部；旧实现把74像素图直接拉至960，且上下用了同一图，不能只因已找到原图就判还原通过。

`Tools/Arena/ImportFeatureNotice.py`只导入五张该用途图，先把节点opacity并入PNG，再将白色装饰覆盖率转换到Linear；渐变、暗影RGB和`_dim`的源色保留，运行时Image白色不重复乘节点opacity。已有`_bar_23`和关闭箭头meta GUID保持。原图/修前及源节点、像素测量保存在`.artifacts/home-social-visual-fix/notice/`。同片静态邮件白图标原版透射约0.208，旧候选约0.525，旧`Black(.76)`把sRGB透明度意图直接当Linear alpha；本候选转换该透射并恢复原`_dim`/127。额外遮罩的运行时语义仅是录像校准，不声称源CSB包含该常量。

原CSB的380初始高度与录像稳态运行尺寸不同；按实际稳定帧取390原设计单位（585项目设计单位），标题、上下线、正文和关闭区域按高度归一摆放，关闭文字采用原帧测得的蓝灰而非旧中性136灰。这些是候选参数，必须由同一背景/相机的实际捕获比较确认。离线像素/切片合同已实际3失败→3通过；专用`FeatureNoticeSourceVisualTests`旧预制3项RED由协调者实际运行，当前生成、受影响功能和画面GREEN仍待统一Unity。此次修改进入更高内容R45，不覆盖已签名44；未声称Native或声音试听通过，现用服务/数据库升级仍待审批。
