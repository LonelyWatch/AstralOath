# 主界面与竞技场模拟战

## 本机使用

1. 配置本地 MySQL 连接串后，在仓库根目录执行 `dotnet run --project Server/Host -- migrate`，再执行 `dotnet run --project Server/Host`。开发环境与运行账户权限见[服务端说明](../../Server/README.md)。
2. Unity 使用 **Tools → 游戏主页 → 打开启动场景** 后点击 Play。右侧 **竞技场** 直接进入模拟战目录。开发默认连接 `http://127.0.0.1:5080`；可通过 `ASTRAL_FOUNDATION_URL` 或场景 Bootstrap 的 Base Url 指定 HTTPS 服务器。
3. 目录可滚动、筛选、搜索及手动刷新，默认人数降序。搜索浮层保留至主动取消或打开其他浮层，输入框需主动点击。创建时输入房名、选择规则，按需启用四位密码和参与者聊天。其他玩家使用独立客户端刷新后可发现房间并入场。
4. 房间右侧只显示竞技场频道。按规则显示聊天许可，当前房主和第二名参与者显示在对阵两侧，其余参与者在下方列表。正常离开会移除成员，最后一名离开清理房间。

当前游客名称由本机生成，`ASTRAL_ARENA_PLAYER_NAME` 可为本机测试指定名称，竞技场游客共用拉斯默认头像。会话令牌在进程内存中维护，同一次运行可经当前房间接口恢复。强制结束客户端后服务器保留成员和房间，本批尚无正式账号、自动回收、BP 或联机对战入口。

## 资源与布局

基准是用户提供的前七张截图和本地 `pack-15-r4262`。主界面沿用 `lobby_bg3` 及 c5009 原版模型。房间庭院用原地图集中的树、花、地面、城墙和天空图层组合；位置按截图校准。目录、弹窗、规则图、VS、通用按钮和头像均使用已存在的资源。未接入的其他主页功能入口保持省略。房间主题、美术比例与手机画面可继续实机对照。

编辑器重新生成入口为 **Tools → 游戏主页 → 生成主页和模拟战**。它重建四个预制体及启动场景，原 CSB 只作为资源与排版证据。手动编辑布局前先保留预制体变更。Canvas 使用 1920×1080 参考、高度匹配和安全区；目录、规则卡、成员和聊天各自拥有滚动裁剪。

Foundation 包和既有冷启动验签链继续使用。新增 Home、Arena 业务程序集。更新稳定启动契约、Unity 首场景和 AOT 立绘桥需要重新构建母包。发布需同步服务端迁移。

## 验证

- 服务端使用实际 MySQL 8.4，238 项通过，覆盖两会话、刷新、设密加入、聊天权限、服务重启、容量与并发末席。
- Common EditMode 14 项通过，覆盖实际通用预制体、复用状态和 UI 创建菜单。下述图形 PlayMode 同时覆盖点击音效和鼠标／触屏长按。
- Unity 主页启动、四个预制体 Binder 与 Foundation 收集地址通过生成校验。图形 PlayMode 5 项通过，覆盖双会话创建、九房间溢出与滚动、名称搜索、错误密码、正确入场、双向聊天和离开。验证可用 `ASTRAL_ARENA_TEST_URL=http://127.0.0.1:5080` 后运行 `Tools/Invoke-Unity.ps1 -Mode PlayMode -Graphics -TestFilter AstralOath.Client.Tests.GameHomeArenaTests`。
- Unity 图形截图检查主页 1920×900、1600×900、1200×900，以及宽屏目录、创建和房间。手机实机和带刘海设备尚未验证。
- Windows x64 IL2CPP 开发母包通过构建和默认主页冷启动，日志有 `ASTRAL_CODE_BOOT_OK release=builtin recovered=False`。本次的两端房间流程在 Unity 图形 PlayMode 运行。开发包支持本机明文连接，发布客户端需配置 HTTPS 服务地址。

本次录像复核、原版音效和泛光来源、窗口约定以及本机测试范围见[录像对照交付](../delivery/2026-11-14-arena-recording.md)。更新服务时应用 `003_game_operation_audit.sql`，权限及打点语义见[操作留痕](operation-audit.md)。

房间表情使用第 `004_arena_chat_stickers.sql` 号迁移。Windows 包默认可缩放小窗，主页右上角或 F11 切换显示模式。录像对照、表情素材和平台验证见[本轮复核](../delivery/2026-11-14-arena-ui-fidelity.md)。
