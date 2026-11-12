# 客户端地基

Bootstrap 是场景内的组合入口，通过实例级 ServiceRegistry 按逆序释放服务。YooAsset 包及 DOTween 包装器的生命周期随 Bootstrap。Core 定义服务接口和绑定作用域。Common 提供 BindingMap、直接继承 TMP 的 GameText、直接继承 Button 的 GameButton 和安全区适配。FoundationDemo 持有演示流程、文案和显示绑定，Common 不引用业务模块。

窗口每次打开都新建 BindingScope。关闭先取消操作，再按逆序解除监听、释放资源句柄和复位动画。GameButton 的单次点击在等待期间锁定交互，禁用时取消监听和请求。GameText 按文案 key 订阅文字语言变动，统一使用 TMP 字体及其回退，新建文本默认换行和省略；绑定时保留 Inspector 中的排版选择。缺字显示问号并记录码点，语音语言单独选择。演示文字默认为 zh-Hans，语音默认为 ja，演示无需语音素材。

FoundationWindow 继承普通 C# UIBaseView，不作为 MonoBehaviour 挂在预制体上。UIViewRegistry 记录 View 类型与重写的 PrefabPath，并通过 IAssetService 加载预制体。预制体仅用通用 BindingMap 保存引用；Inspector 每项单行展示，Hierarchy 支持 + / − 增删和撤销。使用方式见 [UIBaseView 与 Binder](../development/ui-binder.md)。普通 C# `FoundationWindowBinder` 负责界面状态、事件、异步回显和清理。`FoundationEntry` 实现 Common 的 `IHotUpdateEntry`，通过 `GameLaunchContext` 接收稳定服务和场景引用，并注册业务窗口。Bootstrap 按清单入口加载业务程序集，Editor 使用已编译程序集。

## 演示及构建

1. TMP 必需资源已入库。需要恢复时先用 Unity 解析 UPM 包，再执行 `Tools/Import-Tmp.ps1`，它从锁定 TMP 3.0.7 的官方 unitypackage 提取资产并保留 GUID。不要在带 `-quit` 的批处理中依赖异步导入完成。
2. 运行 `Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient` 生成并同步 UI 和本地化 JSON。编辑器入口 `AstralOath.Editor.FoundationBuilder.ImportGeneratedDemoConfig` 也可从本地 `.artifacts/config/json` 同步两份快照。隔离编辑器可附加 `-astral-generated-config <JSON 输出目录的绝对路径>`。
3. 打开 `Assets/Game/Modules/FoundationDemo/Scenes/FoundationDemo.unity`。`AstralOath.Editor.FoundationBuilder.CreateDemo` 可重建场景和窗口预制体，`ValidateDemo` 检查其绑定。
4. 播放场景后验证中文、安全区、资源加载、关闭重开及按钮状态。启动服务端后点击发送回显，窗口显示实际返回的序号。可通过启动环境变量 `ASTRAL_FOUNDATION_URL` 或场景 Bootstrap 的 `baseUrl` 改变服务器地址，默认开发地址为 `http://127.0.0.1:5080`。传输仅接受 HTTPS 或开发用 loopback HTTP，且禁止重定向。
5. Windows x64 与 Android ARM64 的完整安装包及独立热更包由[热更与强更构建](../development/hot-update.md)提供菜单和批处理入口。
6. 进行 loopback HTTP 联调时向 `Invoke-Unity.ps1` 强更构建附加 `-Development -DevelopmentHttp` 生成开发版本。运行 Windows Player 时附加 `-astral-demo-echo -logFile <绝对路径>`，程序在资源及 C# Binder 就绪后发起实际请求，输出 `ASTRAL_ECHO_OK sequence=...` 或 `ASTRAL_ECHO_FAILED ...` 并退出。正式连接需要部署端提供可验证的 HTTPS 证书。

未配置远端更新时，YooAsset 编辑器模式使用资产数据库生成的模拟清单，Player 使用 StreamingAssets 中的内置内容。业务程序集、窗口预制体、配置、本地化和示例资源均来自当前包。启动等待和初始化失败提示有独立的内置简体中文文案。

## 资源导入

导入规则只作用于 `Assets/Game`。默认音频格式是 Vorbis。短音效使用 `DecompressOnLoad`，`Voices` 目录的短语音使用 `CompressedInMemory`，`Music` 和 `Long` 目录使用 `Streaming`。资源接入时将长语音和其他长音频放到 `Long` 目录，例如 `Res/Sounds/Voices/Long`。导入器按目录分类，重导入会再次应用规则。

纹理默认关闭 Read/Write，Sprite 关闭 Mipmap。Windows（Standalone）和 Android 默认启用压缩、最大边长 2048、格式 Automatic。Unity 根据纹理内容选择 Windows 压缩格式，Android 随 ASTC 或 ETC2 构建子目标选择格式。构建前校验这些设置。明确的例外在资源 importer 的 User Data 填入 `AstralOath.ImportOverride` 后保存设置并重导入，设置随该资产的 .meta 保存。

## 已签名更新

在 Bootstrap 配置清单 URL、签名 URL 和发布公钥 JSON（Base64 的 `modulus` 和 `exponent`）。开发联调也可在隔离项目运行 `ConfigureContentUpdate`，传入 `-astral-content-manifest`、`-astral-content-signature` 和 `-astral-content-public-key`。公钥由客户端发布固定。生产使用 HTTPS，开发版本允许 loopback HTTP。发布包将 YooAsset 文件放在签名负载的 `yooasset/Foundation/` 路径下。

启动按协议版本 1 和外层 schema 3 验签，预检母包、平台、架构、代码清单和文件后加载业务。程序集、配置、本地化和资源来自同一发布版本。已载入的程序集和已补充的元数据留在当前进程内。若启动未确认，下次启动会隔离该版本并尝试恢复兼容且验签有效的健康版本。

图形环境的 Windows 回显演练可附加 `-astral-demo-screenshot <PNG绝对路径>`。程序在回显成功且启动画面结束后等待渲染完成，截取包含窗口和状态提示的画面。写入截图后退出，遇到全幅单色画面则记录失败。
