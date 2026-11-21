# FoundationDemo

原生双端房间回归在主页启动成功后驱动操作。房主确认热更已激活且已经入房，脚本才以同一安装缓存打开第二个客户端。每个进程分别检查消息和成员数，再自行退出。

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

账户首次读取最多等待五秒。英雄库存、神器库存和召唤状态各发一次 GET。PlayMode 从正式 FoundationEntry 打开背包、英雄、召唤共请求 12 个资源地址，其中 7 个用于首次进入这三个入口。单项读取失败在进入该入口时重试。召唤首读快照只用一次，超过 30 秒或跨免费刷新、卡池边界时重新读取。测试见 `HomeStartupPreparationTests`。

### 主页与竞技场的会话缓存

登录黑屏期间取得英雄、神器和召唤首屏数据，并预解析默认持有英雄的立绘。英雄页、背包和竞技场目录在同一个 GameHome 场景中切换。持有数据、页面资源、已经解析的英雄立绘由该账号本次登录持有，进入及退出战斗场景后继续复用。关闭页面释放演员、摄像机、渲染目标和页面事件，退出登录释放会话缓存。召唤和保管箱领取取得权威回执后，按实例 ID 把实发奖励加入对应库存。服务端明确拒绝时保留原库存，提交后失联等结果不确定的情况立即发起权威校准。神器穿脱、强化、锁定和出售沿用神器库存失效机制。召唤横幅与免费重置时间保留原有时效检查。[导航与账号隔离](../../../../docs/delivery/2026-11-21-hero-layout-and-navigation.md)
