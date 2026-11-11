# HybridCLR 与构建入口迁移实施计划

> 执行方式：使用 subagent-driven-development，按文件所有权并行实施，由协调者统一集成、检查和提交。用户已明确授权更新并删除 xLua，无需再次确认选型。

**Goal:** 以 HybridCLR C# 热更替代 xLua，并提供 Tools 下完整安装包、独立热更包、兼容检查入口。

**Architecture:** 稳定 AOT 启动器负责下载、验签、母包兼容预检及一次性加载程序集；独立业务程序集负责普通 C# View/Binder；战斗程序集保持纯 C#，客户端热加载、服务端同源编译。代码启动失败必须在新的进程中恢复，不能模拟 Lua VM 的同进程卸载。

**Tech Stack:** Unity 2022.3.62f3、HybridCLR package 9.0.3（提交 6cadd24d101f4cbd00cd80190106f6621132d24d；配套 runtime 9.0.1）、YooAsset、Luban、.NET 10、MySQL 8.4。

**Spec:** 本会话已批准的评估方案及用户“进行更新，之前的 xlua 架构可以删除；明确热更边界；新建 Tools 菜单提供热更包、强更包”的指令。

## 全局约束

- 保留 Unity、YooAsset、Luban、DOTween、MySQL 和服务器架构。用户只授权替换 xLua 热更路线。
- Foo.prefab 对应普通 Foo : UIBaseView 与 FooBinder.cs；预制体只挂通用 BindingMap。保留现有 GUID 和 Binder 编辑体验。
- Common 不引用业务；AOT 不静态引用热更程序集；Battle 不引用 Unity、网络、数据库。
- Windows x64 与 Android ARM64 分别构建、封存母包 AOT DLL；不得以本次新生成 AOT 文件替换旧母包基线。
- 所有构建必须有明确平台、版本、输出和失败结果；不自动上传或生产部署，不将密钥入库。
- 仅协调者操作 Packages、ProjectSettings、Git 和用户 D 盘工程同步。提交信息中文。

## 接口与所有权

- 协调者：Packages/ProjectSettings；Bootstrap；Tools/validate.mjs 及其测试；依赖记录、ADR、文档；集成验证、Git。
- UI 工作者：FoundationDemo/Scripts、Lua 删除、UI 生命周期相关测试、Common/GameLaunchContext.cs 和 IHotUpdateEntry.cs。Editor/Bootstrap 不归此工作者。
- 更新核心工作者：Shared Runtime/Updates 新的代码清单及启动日志、Runtime/Battle 独立 asmdef/csproj、对应 .NET 测试及服务端 project 引用。
- 构建工作者：Assets/Game/Editor 的新构建模块与 FoundationBuilder、Tools/Build、Tools/Invoke-Unity.ps1、Tools/Content/Package-Unity.ps1、构建相关 EditMode 测试。不改第三方包或 ProjectSettings 文件。

### 稳定入口

Common 中 IHotUpdateEntry : IDisposable：Task StartAsync(GameLaunchContext context)、Task<EchoResult> EchoAsync()。GameLaunchContext 包含 Services(ServiceRegistry)、Lifetime(BindingScope)、ViewRoot(Transform)、OpenButton(GameButton)、OpenLabel/StartupStatus(GameText)、TextLocale/VoiceLocale(string)、Recovered(bool)。构造参数顺序同上。

入口为 AstralOath.Client.FoundationDemo.FoundationEntry，程序集 AstralOath.Client.FoundationDemo。启动器先注册 IAssetService、IConfigService、IAnimationService、IEchoService；入口加载 DemoMessages，创建 locale 并注册 ILocaleService，自行注册 View 和创建窗口。音频服务可在入口启动后由启动器用已注册的 locale 初始化。所有业务类引用留在热更程序集或 Editor。

### 代码发布清单

Shared.Updates 中定义 HotUpdateManifest，public 字段使用 lowerCamelCase，DataContract/JsonUtility 使用相同名称：formatVersion=1、baselineId、platform（StandaloneWindows64/Android）、architecture（x64/ARM64）、entryAssembly、entryType、assemblies、aotMetadata。文件条目字段 name（不含 .dll）、address、sha256、size；业务条目附 dependencies（程序集名）。资源地址 code/manifest、code/hot/<name>、code/aot/<name>。

API：HotUpdateManifest.Parse(byte[])；Validate(string baselineId,string platform,string architecture)；GetLoadOrder()；static VerifyFile(HotUpdateFile file,byte[] bytes)。Validate 拒绝重复名称/地址、非法值、错误母包/平台、缺失依赖、循环、无入口。母包身份是构建时生成并固定的 ID，不以新热更数据推导；资源清单 schema 升为 3。

PlayerBaseline public 字段 formatVersion=1、baselineId、platform、architecture、runtimeVersion、appVersion；Resources 名 AstralOathPlayerBaseline。母包归档同时保存此 JSON、实际 AOT DLL、哈希和工具链版本。热更构建只接受已完成的母包基线。

CodeBootJournal：Begin(long version)、Confirm(long version)、RecoverInterruptedBoot() 返回 long?、IsRejected(long)、LastHealthyVersion。未确认的尝试在下次进程启动被隔离，不重新加载同一失败版本。ContentUpdater 增加显式读取/激活已签名本地版本入口，供冷启动恢复；不绕过验签。

## Review Focus

- DLL 载入后异常不能同进程回退；重启恢复必须隔离失败版本，避免每次启动再次下载/执行它。
- 老母包、错误平台、AOT 基线变化、缺少 DLL/依赖必须在业务代码执行前拒绝。
- 完整包构建失败不能留下“可用于发布”的基线；热更构建不能隐式执行 BuildPlayer 或刷新基线。
- C# Binder 关闭取消、重复点击、事件释放不能因去 Lua 回归。
- 当前 Unity Editor 能运行不能替代 Windows IL2CPP 和 Android 实际旧包更新验证。

## 任务

### 1. 依赖与测试基线（协调者）
- [x] 现有内容/.NET/结构测试基线，固定并导入官方 HybridCLR 包及许可证。
- [x] 保存依赖 SHA/提交和配套运行时信息，按项目局部目录安装，避免修改全局 Unity 安装。

### 2. 更新清单与冷启动恢复
- [x] 先写并运行失败测试：错误母包/平台、依赖循环、DLL 哈希、启动失败隔离、成功确认、日志损坏。
- [x] 实现上述纯 C# 契约、日志和已签名版本恢复，拆分 Battle 程序集，验证 .NET 行为。

### 3. C# Binder 与模块入口
- [x] 先补 Binder 取消/重复点击/显示、入口加载测试，再移除 Lua 路径。
- [x] 用 FoundationWindowBinder 接管原 DemoWindow/FoundationLua 职责；实现 FoundationEntry 和稳定入口接口。
- [x] 保留 View/预制体/BindingMap 命名和编辑功能，更新测试与资源引用。

### 4. 构建与 Tools 菜单
- [x] 增加构建参数、基线完整性、兼容检查的失败测试。
- [x] 提供 Tools 菜单：构建设置、环境安装/检查、导表、强更包 Windows/Android ASTC/ETC2、热更包同平台、兼容检查、打开产物。
- [x] 菜单与命令行共用实现；输出新目录，失败中止，热更不重建母包、不自动上传。
- [x] 将 DLL/代码清单加入 YooAsset 收集；更新签名打包工具支持 schema 3。

### 5. 启动集成与旧架构清理（协调者）
- [x] AOT 启动器预检候选、补充 AOT 元数据、按依赖加载 DLL，再通过稳定入口启动；Editor 使用已编译程序集。
- [x] 接入启动健康日志与冷启动恢复；删除 xLua 第三方库、生成器、依赖、旧运行时规则，保留历史计划作为历史记录。
- [x] 更新 ADR、AI 规则、依赖记录、使用说明和验证台账。

### 6. 验证与交付
- [x] .NET、结构检查、Unity EditMode/PlayMode；Windows 母包与独立热更包实测。
- [x] Android ARM64 构建；有设备则进行不重装热更，没有设备明确记录未执行。
- [x] 检查错签名/不兼容/坏 DLL/启动失败恢复，独立审查后修复。
- 交付操作：检查源码/密钥/资源/缓存边界，中文提交，安全同步用户工程并推送开发分支（沿用既有授权；具体提交以最终交付回复为准）。
