# 客户端地基

Bootstrap 是场景内的组合入口，通过实例级 ServiceRegistry 按逆序释放服务，并用 WindowCoordinator 管理窗口。YooAsset 包、xLua 环境及 DOTween 包装器的生命周期随 Bootstrap。Core 定义服务接口和绑定作用域。Common 提供 BindingMap、GameText、GameButton 和安全区适配。FoundationDemo 持有演示流程、文案和显示绑定，Common 不引用业务模块。

窗口每次打开都新建 BindingScope。关闭先取消操作，再按逆序解除监听、释放资源句柄和复位动画。GameButton 的单次点击在等待期间锁定交互，禁用时取消监听和请求。GameText 按文案 key 订阅文字语言变动，统一使用 TMP 字体及其回退，超长文案按行换行和省略。缺字显示问号并记录码点，语音语言单独选择。演示文字默认为 zh-Hans，语音默认为 ja，演示无需语音素材。

FoundationWindow 继承普通 C# UIBaseView，不作为 MonoBehaviour 挂在预制体上。UIViewRegistry 记录 View 类型与重写的 PrefabPath，并通过 IAssetService 加载预制体。预制体仅用通用 BindingMap 保存引用；Inspector 每项单行展示，Hierarchy 支持 + / − 增删和撤销。使用方式见 [UIBaseView 与 Binder](../development/ui-binder.md)。演示 Lua Binder 通过 xLua 在无宿主全局表的环境执行，只接收原始数据并返回显示、点击和完成阶段的数据。C# 负责异步请求、资源、生命周期和 View 适配。编辑器按 `FoundationWindowBinder.exports.txt` 生成允许的 Lua 导出名，校验导出和序列化绑定。演示桥接不依赖运行时动态生成 C# 委托、CS 反射或全局热更。Lua 仍是受信任的发布内容。

## 演示及构建

1. TMP 必需资源已入库。需要恢复时先用 Unity 解析 UPM 包，再执行 `Tools/Import-Tmp.ps1`，它从锁定 TMP 3.0.7 的官方 unitypackage 提取资产并保留 GUID。不要在带 `-quit` 的批处理中依赖异步导入完成。
2. 运行 `Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient` 生成并同步 UI 和本地化 JSON。编辑器入口 `AstralOath.Editor.FoundationBuilder.ImportGeneratedDemoConfig` 也可从本地 `.artifacts/config/json` 同步两份快照。隔离编辑器可附加 `-astral-generated-config <JSON 输出目录的绝对路径>`。
3. 打开 `Assets/Game/Modules/FoundationDemo/Scenes/FoundationDemo.unity`。`AstralOath.Editor.FoundationBuilder.CreateDemo` 可重建场景和窗口预制体，`ValidateDemo` 检查其绑定及 Lua 导出。
4. 播放场景后验证中文、安全区、资源加载、关闭重开及按钮状态。启动服务端后点击发送回显，窗口显示实际返回的序号。可通过启动环境变量 `ASTRAL_FOUNDATION_URL` 或场景 Bootstrap 的 `baseUrl` 改变服务器地址，默认开发地址为 `http://127.0.0.1:5080`。传输仅接受 HTTPS 或开发用 loopback HTTP，且禁止重定向。
5. 通过 Unity 2022.3.62f3 批处理分别指定 `-buildTarget Win64 -executeMethod AstralOath.Editor.FoundationBuilder.BuildWindows`、`-buildTarget Android -executeMethod AstralOath.Editor.FoundationBuilder.BuildAndroidAstc` 或 `BuildAndroidEtc2`。输出在 `.artifacts/builds`。Android 为 API 29、ARM64、横屏。构建在相同目标上先生成 YooAsset 内置包，再生成 IL2CPP Player。
6. 进行 loopback HTTP 联调时向构建命令附加 `-astral-dev-http` 生成开发版本。运行 Windows Player 时附加 `-astral-demo-echo -logFile <绝对路径>`，程序在资源及 Lua Binder 就绪后发起实际请求，输出 `ASTRAL_ECHO_OK sequence=...` 或 `ASTRAL_ECHO_FAILED ...` 并退出。正式连接需要部署端提供可验证的 HTTPS 证书。

未配置远端更新时，YooAsset 编辑器模式使用资产数据库生成的模拟清单，Player 使用 StreamingAssets 中的内置内容。窗口预制体、Lua、配置、本地化和示例资源均来自当前包。启动等待和初始化失败提示有独立的内置简体中文文案。

## 资源导入

导入规则只作用于 `Assets/Game`。默认音频格式是 Vorbis。短音效使用 `DecompressOnLoad`，`Voices` 目录的短语音使用 `CompressedInMemory`，`Music` 和 `Long` 目录使用 `Streaming`。资源接入时将长语音和其他长音频放到 `Long` 目录，例如 `Res/Sounds/Voices/Long`。导入器按目录分类，重导入会再次应用规则。

纹理默认关闭 Read/Write，Sprite 关闭 Mipmap。Windows（Standalone）和 Android 默认启用压缩、最大边长 2048、格式 Automatic。Unity 根据纹理内容选择 Windows 压缩格式，Android 随 ASTC 或 ETC2 构建子目标选择格式。构建前校验这些设置。明确的例外在资源 importer 的 User Data 填入 `AstralOath.ImportOverride` 后保存设置并重导入，设置随该资产的 .meta 保存。

## 已签名更新

在 Bootstrap 配置清单 URL、签名 URL 和发布公钥 JSON（Base64 的 `modulus` 和 `exponent`）。开发联调也可在隔离项目运行 `ConfigureContentUpdate`，传入 `-astral-content-manifest`、`-astral-content-signature` 和 `-astral-content-public-key`。公钥由客户端发布固定。生产使用 HTTPS，开发版本允许 loopback HTTP。发布包将 YooAsset 文件放在签名负载的 `yooasset/Foundation/` 路径下。

启动以协议版本 1 和配置 schema 版本 2 验签并核对文件。候选包的 YooAsset 清单、Lua 导出、配置及资源验证通过后才切换活动版本。Lua 和配置用候选包自身的本地化数据验证，活动版本的文案随其资源一起保留。更新失败时验证并加载已有活动版本，窗口上方显示更新失败提示。远端已配置且没有可用活动版本时，启动显示失败。运行中的窗口使用启动时确定的版本。
每次构建保留独立的 YooAsset 包目录。Windows 首次输出为 `.artifacts/yooasset/StandaloneWindows64/Foundation/foundation-1`，后续使用 `foundation-2` 等未用编号。Android 在根目录下按 `ASTC/Android` 或 `ETC2/Android` 分开保存。签名工具的 `-PackageDirectory` 使用构建日志 `ASTRAL_YOOASSET_PACKAGE` 给出的绝对目录。此本地 YooAsset 构建编号与发布工具的签名版本各自独立。

图形环境的 Windows 回显演练可附加 `-astral-demo-screenshot <PNG绝对路径>`。程序在回显成功且启动画面结束后等待渲染完成，截取包含窗口和状态提示的画面。写入截图后退出，遇到全幅单色画面则记录失败。
