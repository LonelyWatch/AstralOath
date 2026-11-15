# 游戏入口、账号与更新

游戏首场景是 `Assets/Game/Modules/Home/Scenes/GameHome.unity`，可从 **Tools → 游戏主页 → 打开启动场景** 打开。运行时依次显示：天空与湖面标题页 → 登录或邮箱注册 → 选择「不凋零回忆」→ 检查安装包及资源版本 → 显示活动标题图和闪烁的「轻触画面开始」→ 游戏主页。竞技场使用登录账号在区服内的稳定玩家 ID 与会话；正式启动不再自动新建访客。

## 发布边界

- 首场景、`Bootstrap/Entry*.cs`、`Bootstrap/Resources/Entry/` 内的标题图、字号样式及按钮、公共输入反馈和启动界面所需的字体由母包提供。它们在启动业务程序集、初始化 YooAsset 之前即可使用。改动启动样式、更新状态逻辑或账号请求协议需要重新发布母包。
- 「轻触画面开始」后的活动背景从经校验的 Foundation YooAsset 包加载。改动 `Config/Tables/entry_title_themes.csv` 的 `current` 行即可换图；图片放入 `Assets/Game/Modules/Home/Res/Textures/Entry/`，文件名用小写字母、数字或下划线。用 **Tools → 导表** 生成客户端配置；导表校验文件存在与唯一，构建确认配置和 Sprite 都有正确资源地址。首张配置使用 `entry/backgrounds/26_halloween_bg`；若配置或图不可读，则保留母包里的湖景。
- 资源来源和校验记录见 `Assets/Game/Bootstrap/EntryAssetSources.json`。启动背景使用已解包 CFX 的原始图层合成静帧，活动背景是参考图的原始图。目前源包只有英文 Logo；未用参考截图直接裁切素材。

- 外观以 `title.csb` 的节点为资源来源。补丁面板使用 `n_patch_ask_popup/window_frame` 对应的 `renew_popup_bg_2.png`，面板透明度为 216/255，背景黑色遮罩为 127/255。Unity 线性色彩项目按原界面透过的亮度换算这两层透明度；选服使用另一张 `renew_popup_bg.png`。圆角绿钮沿用原始轮廓和按下素材，正常态加强暗部与底部高光；补丁按钮文字使用偏暗的常规字重。
- 启动页通过首场景的 `entryFont` 明确引用 Common 的 `EpicSeven.asset`。其源字体与启动包 `font/daum.ttf` 哈希一致；原包布局额外引用的 `daum_zl.ttf` 不在当前解包目录。共享的 Foundation 演示字体保持原样。

## 服务端配置

先应用 MySQL 迁移 `009_entry_account.sql`。在首场景的 `FoundationBootstrap` 设置服务地址 `baseUrl`，本机也可临时用 `ASTRAL_FOUNDATION_URL` 指向测试服务。生产服务使用 HTTPS。已有内容 CDN 的清单、签名 URL 与公钥仍按[客户端地基](../architecture/client.md)配置；这三项要成组配置。三项为空时仅使用内置资源，仍查询服务端最低应用版本。

- `Entry__MinimumAppVersion` 控制强更，例如 `1.0.1`；为空时不要求强更。版本低于门槛时显示「请前往QQ群下载最新版」，停止进入大厅与下载热更。服务端选服接口再次检查版本以处理配置在两步之间发生变化的情况。应先确认新版安装包已在 QQ 群提供，再上调最低版本。
- SMTP 配置包括 `Entry__Smtp__Host`、`Entry__Smtp__Port`、`Entry__Smtp__From` 和服务商要求的 `Entry__Smtp__Username`、`Entry__Smtp__Password`。默认端口 587，通过 STARTTLS 发信。验证码及认证信息不会写入客户端首选项或操作日志。凭据配置在服务进程环境中。
- 为兼容旧包，旧版竞技场访客入口暂时保留。确认全部客户端升级后，设置 `Entry__AllowLegacyArenaGuests=false`，关闭旧访客通道。迁移、邮箱限制和测试方法见[服务端说明](../../Server/README.md)。

## 热更操作与验收

强更通过后，客户端先获取并验签清单，才会计算还缺多少下载内容。缺少内容时弹出「更新补丁」，列出补丁大小，点「进行」才请求资源文件；点「取消」回到选服，不切换资源版本。已有部分文件时走原有续传。下载页面显示完成量、总量、速率和百分比，然后等待完整性校验与业务代码预检。当前版本的资源通过校验时直接进入主题标题页；补丁失败提示重试或返回。版本标识 `App:` 取自本机应用版本，`R:` 在初始显示安装包内置版本，资源准备后显示实际使用的资源版本。

- 共享内容回归：`dotnet run --project Tests/Content`，验证签名错误、空间不足、断点、取消前无资源网络请求、确认后字节进度、版本可恢复。
- Unity 验证：在 PlayMode 运行 `EntryFlowTests` 与 `EntryScreenTests`，验证安装包过旧被拦截、正常选服后显示配置主题、点击前主页不可见、窗口取消与生命周期。
- 发布前分别使用 Windows 和 Android IL2CPP 母包与**同母包签名热更**演练：无更新 → 触发强更 → 热更取消 → 下载中断重开 → 完成进入主页。当前母包上线后再验证真实 SMTP 发信，不用编辑器预览替代设备验证。
