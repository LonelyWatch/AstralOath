# 热更与强更构建

首次迁移需要分发对应平台的新 IL2CPP 母包。之后同一母包的兼容业务代码、配置和资源可随独立热更包发布，在应用完全退出后的下一次启动激活。已经载入的程序集无法在同一进程卸载，启动失败后的恢复也需要新进程。

新业务模块的 asmdef 由 `Tools/New-Module.ps1` 设为 `autoReferenced: false`。完成首个运行时代码后，将程序集加入 `Config/HotUpdate/assemblies.json` 的 `assemblies` 列表，再运行 `Tools/AstralOath/检查母包兼容性`。该清单是热更程序集的登记入口。模板创建时保留清单原样，登记前模块尚未纳入热更构建。

## Unity 菜单

先切换 Unity 当前目标到 Windows 或 Android。使用 `Tools/AstralOath/构建设置` 保存母包归档、应用版本、Android versionCode、资源版本及输出目录。选择母包归档后，窗口显示只读母包 ID。按需设置本地 RSA 私钥、离线 libil2cpp 源和开发选项。

1. 使用 `环境/从固定源码安装 HybridCLR` 和 `环境/检查` 安装并检查项目本地环境。安装版本为包 9.0.3 / runtime 9.0.1。用 `导表` 同步 Luban 配置。
2. 在 `强更包` 下选择 `Windows x64`、`Android ARM64 ASTC` 或 `Android ARM64 ETC2`。每次构建自动生成新 GUID 作为母包 ID，成功后记录在 `ASTRAL_PLAYER_BASELINE` 日志。完成目录包含安装包、内置内容、`package.complete.json`、`baseline.complete.json`、`player-baseline.json` 和 `stripped-aot`。按平台及变体封存完整目录。
3. 改动业务后，在构建设置中选择已完成的母包归档，设置大于已发布版本的新资源版本。运行 `检查母包兼容性`，然后在 `热更包` 下选择与母包对应的平台和变体。热更构建读取归档内的母包身份，生成独立 YooAsset 发布包。
4. `打开构建输出` 定位产物。构建时配置私钥可直接生成本地签名发布目录，也可在 `签名发布…` 中选择已完成产物的 `package.complete.json`。上传和部署由发布者执行。

## PowerShell 批处理

在仓库根目录使用 PowerShell 7。示例使用 Windows x64，默认输出至 `.artifacts/builds`。从 `ASTRAL_BUILD_OK` 日志取得对应的实际构建目录。

```powershell
$offline = pwsh -File Tools/Setup-HybridCLR.ps1
pwsh -File Tools/Invoke-Unity.ps1 -Mode InstallHybridCLR -BuildTarget Win64 -OfflinePath $offline
pwsh -File Tools/Invoke-Unity.ps1 -Mode CheckHybridCLR -BuildTarget Win64
pwsh -File Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient
pwsh -File Tools/Invoke-Unity.ps1 -Mode Windows -AppVersion 1.0.0 -AndroidVersionCode 1 -ContentVersion 1

$baseline = '<强更包完成目录>'
pwsh -File Tools/Invoke-Unity.ps1 -Mode CheckCompatibility -BuildTarget Win64 -BaselinePath $baseline
pwsh -File Tools/Invoke-Unity.ps1 -Mode HotWindows -BaselinePath $baseline -ContentVersion 2
```

Android 对应强更模式 `AndroidAstc` / `AndroidEtc2`，与 Windows 强更命令一样需传正数 `-AndroidVersionCode`。热更模式为 `HotAndroidAstc` / `HotAndroidEtc2`。Android 的环境检查与兼容检查使用 `-BuildTarget Android`。批处理在隔离项目中运行，安装和构建使用相同的 `-ProjectName`。

如需在 CI 中断言归档身份，可在热更或兼容检查时传 `-BaselineId` 和该归档的实际 GUID。强更构建由工具生成身份，传入 `-BaselineId` 会失败。Windows 路径预算不足时可用 `-OutputRoot .artifacts/b` 缩短输出路径。

```powershell
$hotOutput = '<热更包完成目录>'
pwsh -File Tools/Content/Release.ps1 -Action NewDevKey -Key .artifacts/keys/dev
pwsh -File Tools/Content/Package-Unity.ps1 -BuildOutput $hotOutput -Key .artifacts/keys/dev/private.key -Release .artifacts/releases/win-v1-content-2
pwsh -File Tools/Content/Release.ps1 -Action Verify -Key .artifacts/keys/dev/public.pem -Release .artifacts/releases/win-v1-content-2 -SchemaVersion 3
```

示例开发公钥须预先作为可信公钥配置到用于联调的母包。配置清单 URL、签名 URL 和公钥的方式见[客户端地基](../architecture/client.md)。按母包和平台维护兼容发布地址。发布目录包含 schema 3 外层签名清单，客户端验签后从同一 YooAsset 版本加载业务程序集和资源。保管私钥及母包归档，不提交到仓库。

## 构建边界

- 母包固定启动器、稳定 AOT 接口、原生插件、裁剪结果与桥接能力。改动这些内容或引入归档里缺失的 AOT 类型和成员时，需要重新构建母包。
- 热更重编译已登记的程序集，并重新分析新 DLL 所需的 AOT 泛型元数据。锁定的 HybridCLR 9.0.3 分析器分别运行 20 和 21 轮上限，比较公开的单调增长泛型集合计数，若仍增长则拒绝构建。发布包包含母包初始元数据和新增所需模块，全部补充 DLL 从原母包的 `stripped-aot` 取用。元数据模块缺失、归档哈希不符或兼容分析失败时中止构建。反射动态引入的泛型及原生桥接行为仍需目标 Player 验收。
- 配置改动从 Luban 源表生成，资源改动进入对应平台和变体的 YooAsset 包。即使只改配置或资源，也以该母包为目标生成同批代码与资源的完整签名发布包。

## 验收

Windows IL2CPP 和 Android ARM64 需分别演练。先用新母包验证内置内容，再签名发布更高资源版本，保留该安装包并冷启动，验证可见的 C# 业务变化、回显和关闭清理。分别验证错签名、错误母包或平台、损坏程序集和入口启动失败后的下次冷启动恢复。Editor、共享测试及兼容检查的结果不能充当目标 Player 热更验收。实际结果以交付验证记录为准。

本轮实际通过与待验收项见 [HybridCLR 迁移验证记录](hybridclr-validation.md)。
