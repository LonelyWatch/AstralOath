# 项目通用规范

适用于新功能、修复、工具、配置和资源。下文区分“已有设施”和“开发要求”，不能把要求写成未经验证的实现保证。入口和现状见[文档入口](../README.md)、[当前状态](../project-status.md)。

## 目录、依赖与版本

- 客户端 `Assets/Game`；服务端 `Server/Host` / `Server/Tests` / `Server/Ops`；纯C#共享 `Shared/com.astraloath.shared/Runtime`。数据库、网络、Unity不得进入共享战斗逻辑。
- Bootstrap 负责启动与组装，Core 负责契约/基础能力，Common 负责可复用组件；业务在 Modules/<Module>，Common 不反向依赖 Modules。
- 模块默认 `Scripts/Configs/Editor/Tests/Res/{Atlas,Prefab,Textures,Sounds,Animations,Materials}`，需要时添加 Effects 等目录。用 Tools/New-Module.ps1 创建，禁止覆盖已有模块；模块 README 必须描述职责、依赖、公开接口、配置/资源地址、生命周期和验收。
- 运行时、Editor、Tests 的 asmdef 按职责隔离；测试源码必须有测试程序集，不能直接落进业务编译。新增UI模块需引用 TMP/UGUI，不能依赖可传递引用的假设。
- 新热更业务 asmdef 设 autoReferenced=false，并登记适用更新清单；Foundation 与 CharacterLab 使用不同清单/入口。AOT 不静态引用热更实现。
- 工具链、Unity和依赖版本锁定；只在明确升级任务中修改锁文件、来源和补丁记录，并复验。禁止编辑Unity生成csproj或直接改第三方代码来绕过错误。

## 生命周期、代码与通用服务

- 优先显式依赖注入和实例所有权；单例必须说明谁创建/释放、Domain Reload与测试如何重置，不以全局状态串联业务模块。
- 所有异步操作有取消、超时和生命周期；Unity对象操作回到主线程。关闭窗口先取消，再解绑事件、停止动画、释放资源句柄；对象池归还恢复状态，旧异步结果不能写入新实例。
- BindingScope 统一管理订阅/句柄/动画；禁止 async void 业务入口，Unity事件适配处必须捕获异常、处理取消并保证释放。
- 通用接口留在 Core/Common，业务实现留模块；涉及协议/配置/共享契约变更先列兼容影响，再实现。事件表示事实，不用不透明全局消息总线绕过依赖。
- 日志可定位模块、操作、版本和关联ID；不得记录口令/令牌/私钥/完整生产连接串。错误不得静默吞掉或用占位成功冒充真实成功。

## UI 与交互

- Foo.prefab 对应普通 Foo.cs / Foo : UIBaseView 与 FooBinder.cs，大小写一致，不额外加 View 后缀。View 重写 PrefabPath；稳定资源地址不随文件重命名自动变化。
- 预制体用通用 BindingMap 保存引用；业务 View/Binder 不作为组件。View只维护显示，C# Binder负责界面业务/订阅，领域规则放业务服务。
- 项目文本用 GameText : TextMeshProUGUI，按钮用 GameButton : Button；不可再叠加原版同类组件。右键 UI 创建完整控件，Add Component只添加组件。官方Inspector排版/导航继续使用。
- GameText.Bind 不覆盖作者排版；实际界面文案使用稳定 key。新建菜单占位不是业务文案。GameButton.Bind提供异步点击去重与取消，奖励/扣费还必须由服务器事务与幂等保障。
- 通用动画通过 IAnimationService / DOTween 管理，池化复用和关闭必须终止并复位；动画回调不得结算战斗或发放奖励。
- 修改BindingMap后检查空引用、重复key、对象归属、生成映射和Prefab保存；移动资产带原.meta，已有GUID不得重建。

## 全面屏、平板、Android、PC

- 横屏设计，1920×1080参考尺寸；背景按视觉需要铺满，关键交互放Safe Area。使用锚点、布局组件和滚动，不写死物理屏幕像素。
- SafeAreaLayout已按Screen.safeArea与窗口尺寸更新；它只应挂在与Canvas归一化坐标一致的安全区容器，不能任意嵌套后假定结果相同。
- CanvasScaler需要按界面选择并记录策略：Foundation为MatchWidthOrHeight/0.5，CharacterLab为Expand。右键自动补的Canvas是Unity默认设置，制作正式界面时必须配置参考尺寸和安全区。
- 平板4:3、常规16:9、长屏、带缺口Android、PC缩放/窗口尺寸变化都需验收。宽屏可扩展背景/列表，窄屏保持按钮可点、文字可读；需要断点布局时由模块明确实现并记录。
- PC测试鼠标/滚轮/键盘导航和焦点，Android测试触摸、防误触、返回/前后台恢复。不要让Hover成为唯一提示或把鼠标行为当成触屏验收。
- Android目标ARM64/IL2CPP，ASTC和ETC2分开产出；Windows x64/IL2CPP。最低Android API/target API与工具链以ProjectSettings和锁定文档为准；平台能力差异要有显式路径。

## 图片、图集、音频与角色资产

- 原始解包 D:/sevenepic 只读；只导入已开发模块及依赖，来源/哈希/转换器版本/依赖闭包必须记录。角色每人独立目录，共享依赖集中维护。
- 现有导入器为 Assets/Game/Editor/GameAssetImportRules.cs，默认作用于 Assets/Game；UI默认关闭Read/Write和Mipmap，常规纹理最大2048、平台压缩。源图和显示需求决定最终尺寸，不能未经检查统一降质。
- Android ASTC/ETC2独立构建；Windows按透明度/内容选择平台格式。文字、小图标、渐变、PMA边缘必须目检，不能只看文件体积。
- 骨骼图集/动画WebP等已有明确不压缩或尺寸例外时保留；Importer UserData 精确等于 AstralOath.ImportOverride 的覆盖标记 需记录用途和验收。例外不等于所有新资源默认RGBA32。
- 图集按窗口/模块/生命周期拆分，避免一个常驻大图集装全游戏；共享资源有唯一归属，资源地址稳定。大型二进制用Git LFS，不把Library、构建产物或完整源解包提交。
- 短音效、短语音、长语音/音乐分别制定压缩和加载策略；现有Unity导入器按目录选择加载方式。FMOD bank按事件引用持有并释放，不能事件仍播放就卸载共享bank。
- SCSP/SCT、Spine版本、曲线语义、atlas旋转/裁边/PMA必须遵守[角色导入规则](character-asset-import.md)；未知字段和依赖明确失败/诊断，不硬改版本号或逐角色打补丁掩盖通用转换错误。

## 多语言与配表

- 文本locale与语音locale独立，当前zh-Hans与ja；点分稳定key（各段小写、数字、下划线）、格式占位符、字体回退和缺字日志是通用契约。新增语言需字体、排版、文本长度和音频资源一起验收。
- 每张表只有一个权威编辑源。当前本地Luban CSV/XML为实际入口，未接Google Sheets同步；未来换编辑器必须定义唯一来源和可复现导出。
- schema/源表改后运行 Generate.ps1 -VerifyAccessors -SyncClient；提交源、生成器及必要客户端快照。生成物禁止手改；CI -CheckClient拒绝快照失配。
- 配置ID、枚举、引用、范围、资源地址都需校验。破坏性schema变更要版本化并审计客户端/服务端；数值变化不能擅自改变已开始战斗的配置版本。

## 服务端、数据与通信

- .NET模块化单体，MySQL8.4/InnoDB；初始不引入Redis/消息集群。SQL参数化、异步取消/超时、事务与幂等；同一请求ID不同内容必须拒绝。
- 迁移脚本不可修改已应用历史，执行前备份，使用迁移账户与最小权限运行账户。恢复演练使用空隔离库；不可用资源回滚指针回滚数据库。
- 权威战斗/货币/奖励在服务端；客户端表现不作为结算依据。Protobuf源在Tools/Protocol，生成共享C#；字段编号不得复用，破坏兼容时维护更新。
- 生产HTTPS/WSS必须验证证书，不跳过TLS校验；明文仅限开发loopback。现有WebSocket为开发回显，不代表正式对战连接、鉴权或断线重连已完成。
- 游戏API、内容分发、安装包分发是不同职责；连接串/私钥/证书只在本地私密配置或部署环境。容量用压测确认，不能以目标100人保证2核4GB始终足够。

## 更新、构建与发布

- 兼容C#业务、配置、资源通过签名发布清单/YooAsset热更；冷启动激活。程序集不在同一进程卸载替换。失败恢复是下次进程行为。
- AOT/Core/Common/启动器、原生库、裁剪能力或不兼容契约改变要重建母包；原母包归档、平台/架构、AOT元数据必须对应，不混用Windows/Android/压缩变体。
- 客户端Battle可热更与服务端同源发布是两件事。规则、配置或协议不兼容时维护协调，服务端程序集不通过客户端HybridCLR/YooAsset更新。
- 资源先上传不可变版本，再切换发布入口；签名私钥不交客户端。拒绝坏签名/哈希、错误母包、低磁盘、部分下载和路径越界，并验证恢复。
- 强更安装包构建与服务器强制升级是两种能力；当前缺失策略/准入闭环，详见[核对结果](../delivery/2026-11-12-foundation-reaudit.md)。更新UI可以后做，但准入规则必须前后一致。
- 任何已验收结论都记录代码基线、母包、平台、命令和结果。普通CI/Editor通过不能替代实际Player/设备；不自动部署生产。

## 文档、AI与Git

- 默认在main主工作区修改、中文提交并推送origin/main；不强推，不覆盖用户已有改动。开启新会话先读现行入口，不从旧计划恢复过时架构。
- 功能/配置/资源/构建行为改变，完成一个可验收小步就更新对应模块README和变更日志；不能等整个系统做完才补文档。重要约定写ADR，状态变化更新project-status。
- 排查中的重要原因、源字段解释、性能测量、视频时间戳、取舍与未验证差异随结果立即入文档；新会话不能依赖聊天记录恢复这些结论。事实、录像支持的推断、项目回退策略分别说明。
- 提交前执行文档同步检查、适用测试和差异检查；CI复核。机械检查只保证相应文件同批修改，语义正确由审查负责。
- 交接记录目标、完成内容、未完成项、文件、命令、结果、环境限制、临时进程及下一步；没有验证写未验证，阻塞写具体原因。
- 国际服版本追踪只由用户显式发起；禁止后台轮询、定时跟踪、自动升级依赖。Unity MCP仅用于Editor，命令行入口必须可用。

## 本机存储与演出维护

项目资料与临时产物遵守[D盘规则](local-storage.md)，工具入口统一初始化 ProjectEnvironment，不新增独立的C盘缓存路径。源驱动战斗演出遵守[通用规则](battle-source-presentation.md)：角色ID不参与临时修正、节点未知必须诊断、视觉命中不得结算权威伤害。
