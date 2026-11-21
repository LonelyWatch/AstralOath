# 热更与强更构建

游戏内的强更门槛、热更确认、主题标题图和选服流程见[游戏入口、账号与更新](game-entry.md)。

客户端只使用 HybridCLR。首次迁移或稳定 AOT 层发生变化时，需要分发对应平台的新 IL2CPP 母包。Foundation 同一母包的兼容业务代码、配置和资源可随独立热更包发布，在应用完全退出后的下一次启动激活。已经载入的程序集无法在同一进程卸载，启动失败后的恢复也需要新进程。

## 默认选择与最小包

本机 R25 沿用下述 0.1.23 Windows 母包。R24 更新到 R25 下载 7 个变更文件，共 20.26 MiB，可复用 91 项。游戏服 7728 提供房间事件流，5295 提供签名热更。[R25 交付记录](../delivery/2026-11-21-windows-r25.md) · [R24 交付记录](../delivery/2026-11-21-windows-r24.md)

本机 Windows x64 英雄页使用 0.1.23 最小母包，基线 ID 为 `25cda4dbafde4770b98611b1323689a1`。R22 调用旧 Common AOT 中缺少的装备槽、角标及滑条方法，需要强更。游戏 API 为 7728，R22 签名内容为 5295。此前 R20、R21 使用同一 0.1.22 母包，预禁时序修复可直接热更，R21 内容端口为 5294。[本次强更的归档和实测](../delivery/2026-11-21-windows-r22.md) · [R21 热更记录](../delivery/2026-11-20-home-and-arena-ui.md)

Windows x64 可独立解压的 0.1.21 / R18 试玩包附带规则 17 服务端和本机运行时。资源包内的每项源素材都写入本地构建溯源，签名工具对该文件设 8 MiB 上限。客户端下载的签名清单上限为 1 MiB。[该次构建与验签记录](../delivery/2026-11-20-windows-roster70-package.md)

普通「打包」请求先查已分发母包的对应平台和归档，兼容时构建并签名热更包。改动母包信任配置、启动代码及其直接依赖、原生桥接或不兼容协议，或者没有可复用母包时，构建最小强更包。

最小母包包含 IL2CPP 运行时、稳定程序集、第一场景、登录和更新界面、首屏背景、字体及用到的交互图和音效，以及签名公钥和平台需要的着色器、原生插件。更新器在加载业务 DLL 前使用这套内容。角色、战斗场景和演出、主页及活动背景、业务配表、热更程序集和对应 AOT 补充元数据进入签名 YooAsset 发布目录。最小客户端目录不内置 Foundation YooAsset 包。

Windows 最小包与其首发资源在同一次构建生成，为同一个母包 ID。首次启动需要可用的内容端点，先配置同目录的清单 URL、签名 URL 和公钥 JSON。正式地址使用 HTTPS，回环测试地址使用开发包和 `-DevelopmentHttp`。发布者独立签名并提供首发资源。母包归档和签名资源放在客户端安装目录之外。

```powershell
pwsh -File Tools/Invoke-Unity.ps1 -Mode Windows -Minimal `
    -AppVersion 0.1.14 -AndroidVersionCode 1 -ContentVersion 1 `
    -ContentManifestUrl 'https://cdn.example/manifest.json' `
    -ContentSignatureUrl 'https://cdn.example/manifest.sig' `
    -ContentPublicKey '<public.json>' -SigningKey '<private.key>'
```

校验且激活新资源后，客户端在加载业务程序集前提示「更新完成」。点「退出游戏」，重新打开客户端时启用已安装版本。同一版本正常重开不再提示。已有安装被重复打开时继续沿用签名检查及坏包隔离。默认 Windows 完整离线包模式可用于单独的离线验收。

从支持增量下载的母包起，更新器会验签当前与上一版本的本地清单，并按文件路径、长度和 SHA-256 复用已校验的资源。补丁大小只统计仍需从服务器下载的字节。Windows 0.1.18 仍按新发布的完整清单下载，升级到新母包的首次安装要取得新母包对应的资源。

同机双开共享同一母包的签名资源。第一个进程负责更新和确认业务启动，第二个进程等待启动锁释放后继续，已下载的内容由同一份缓存提供。应用在等待「更新完成」界面重启前释放锁。只有异常退出留下的待确认启动记录需要恢复处理。0.1.17 及更早母包需要强更才能启用这项保护。

## 构建配置与程序集登记

当前有两套独立配置，不能互换入口、YooAsset 包、签名设置或母包归档：

- Foundation：`Config/HotUpdate/assemblies.json` 登记 `AstralOath.Battle`、`AstralOath.Client.BattleScene` 与 `AstralOath.Client.FoundationDemo`，入口为 `AstralOath.Client.FoundationDemo.FoundationEntry`，通过 `IHotUpdateEntry` 启动，资源包名为 `Foundation`。支持本页下述签名发布及冷启动远端更新。
- CharacterLab：`Config/HotUpdate/characterlab-assemblies.json` 登记 `AstralOath.Client.CharacterPresentation` 与 `AstralOath.Client.CharacterLab`，入口为 `AstralOath.Client.CharacterLab.CharacterLabEntry`，通过 `ICharacterLabEntry` 启动，资源包名为 `CharacterLab`。当前提供离线 Windows x64 测试包，使用内置代码和资源，不连接 Foundation 服务端、数据库或远端更新服务。

新模块通过 `Tools/New-Module.ps1` 创建，其运行时 asmdef 使用 `autoReferenced: false`；脚本不会自动改写上述代码清单。完成首个运行时代码后，按实际消费方登记到对应配置的 `assemblies`，并明确依赖与入口。Foundation 使用 `Tools/检查母包兼容性` 检查选定归档；该菜单和 `CheckCompatibility` 批处理目前只面向 Foundation，不作为 CharacterLab 发布验收入口。

CharacterLab 使用 `Tools/角色实验室/验证构建输入` 与 `Tools/角色实验室/构建 Windows 测试包`，批处理模式为 `ValidateCharacterLab` 和 `CharacterLabWindows`。它有独立的 `UserSettings/AstralOathCharacterLabBuild.json`，默认隔离副本 `.artifacts/UnityCharacterLab`、输出 `.artifacts/characterlab-builds`；不读取 Foundation 的私钥或发布基线。现有菜单与脚本未提供 CharacterLab 独立热更包、签名远端发布或 Android 验收流程，不能因为程序集可热加载就声称这些链路已经可用。具体操作见[角色实验室手册](character-lab.md)。

以下 Unity 菜单、强更/热更模式、签名命令及发布验收均针对 Foundation。

## Foundation Unity 菜单

首次使用先在仓库根目录运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Setup-Toolchain.ps1`。它用 Windows 自带 PowerShell 5.1 引导安装锁定的 PowerShell 7 至 `.tools/powershell`，并准备本地 .NET SDK。Unity 的导表、签名和 HybridCLR 安装共用脚本入口，依次查找项目副本、常规安装目录和当前 PATH，不依赖 Unity Hub 是否已刷新 PATH，也不会回退到 Windows PowerShell 5.1 执行业务脚本。离线安装或代理下载见 [启动说明](../../README.md#启动)。

先切换 Unity 当前目标到 Windows 或 Android。使用 `Tools/构建设置` 保存母包归档、应用版本、Android versionCode、资源版本及输出目录。选择母包归档后，窗口显示只读母包 ID。按需设置本地 RSA 私钥、离线 libil2cpp 源和开发选项。

1. 使用 `环境/从固定源码安装 HybridCLR` 和 `环境/检查` 安装并检查项目本地环境。安装版本为包 9.0.3 / runtime 9.0.1。用 `导表` 同步 Luban 配置。
2. 在 `强更包` 下选择 `Windows x64`、`Android ARM64 ASTC` 或 `Android ARM64 ETC2`。每次构建自动生成新 GUID 作为母包 ID，成功后记录在 `ASTRAL_PLAYER_BASELINE` 日志。完成目录包含安装包、内置内容、`package.complete.json`、`baseline.complete.json`、`player-baseline.json` 和 `stripped-aot`。按平台及变体封存完整目录。
3. 改动业务后，在构建设置中选择已完成的母包归档，设置大于已发布版本的新资源版本。运行 `检查母包兼容性`，然后在 `热更包` 下选择与母包对应的平台和变体。热更构建读取归档内的母包身份，生成独立 YooAsset 发布包。
4. `打开构建输出` 定位产物。构建时配置私钥可直接生成本地签名发布目录，也可在 `签名发布…` 中选择已完成产物的 `package.complete.json`。上传和部署由发布者执行。

## Foundation PowerShell 批处理

在仓库根目录使用 PowerShell 7；未配置全局 PATH 时，把下文的 `pwsh` 替换为 `& .\.tools\powershell\pwsh.exe`。示例使用 Windows x64，默认输出至 `.artifacts/builds`。从 `ASTRAL_BUILD_OK` 日志取得对应的实际构建目录。

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

示例开发公钥须预先作为可信公钥配置到用于联调的母包。配置清单 URL、签名 URL 和公钥的方式见[客户端地基](../architecture/client.md)。按母包和平台维护兼容发布地址。发布目录包含 schema 3 外层签名清单，客户端验签后从同一 YooAsset 版本加载业务程序集和资源。保管私钥及母包归档，不提交到仓库。 Unity 包依赖（例如 URP 材质和着色器）由构建按实际解析位置归档，签名时核对 YooAsset 报告、素材与 `.meta` 哈希及 GUID。签名使用构建归档，不从当前编辑器缓存重新猜测依赖。

## 构建边界

- 母包固定 Bootstrap、Core、Common、稳定骨骼桥接、原生插件、裁剪结果与桥接能力。GameText、GameButton、UIBaseView 和稳定入口接口属于此边界。改动这些内容或引入归档里缺失的 AOT 类型和成员时，需要重新构建母包；业务 View/Binder 所在的已登记程序集才属于热更业务。
- Foundation Windows 兼容检查与热更打包读取母包的 ScriptingAssemblies.json。Foundation 中可直接寻址的 ScriptableObject 必须使用母包登记过的程序集。0.1.19 的召唤演出和技能资源使用已登记的 AstralOath.Client.Home，业务界面代码仍在 AstralOath.Client.Summon。
- 热更重编译已登记的程序集，并重新分析新 DLL 所需的 AOT 泛型元数据。锁定的 HybridCLR 9.0.3 分析器分别运行 20 和 21 轮上限，比较公开的单调增长泛型集合计数，若仍增长则拒绝构建。发布包包含母包初始元数据和新增所需模块，全部补充 DLL 从原母包的 `stripped-aot` 取用。元数据模块缺失、归档哈希不符或兼容分析失败时中止构建。反射动态引入的泛型及原生桥接行为仍需目标 Player 验收。
- 配置改动从 Luban 源表生成，资源改动进入对应平台和变体的 YooAsset 包。Foundation 即使只改配置或资源，也以该母包为目标生成同批代码与资源的完整签名发布包。CharacterLab 当前重新构建独立离线测试包，按其实验室流程验证。

Foundation 构建将战斗角色资源按角色分包，共享音频和素材按目录分包。Unity 构建缓存按工程、平台变体和母包分别保留。发布前用前后两版的签名清单按路径和 SHA-256 核对差异，并以已有旧版缓存的客户端测下载量。纯业务代码改动若带动大资源包换哈希，先排查依赖和构建缓存。

## 验收

Windows 开发包使用 `-astral-startup-smoke -astral-summon-smoke` 走实际主页和召唤加载流程。保留图形支持并接入回环测试服，检查日志中的 ASTRAL_SUMMON_PLAYER_OK 和预期 ASTRAL_CONTENT_ACTIVE 版本。这个检查会用测试访客读取卡池。

Windows IL2CPP 和 Android ARM64 需分别演练。先用新母包验证内置内容，再签名发布更高资源版本，保留该安装包并冷启动，验证可见的 C# 业务变化、回显和关闭清理。分别验证错签名、错误母包或平台、损坏程序集和入口启动失败后的下次冷启动恢复。Editor、共享测试及兼容检查的结果不能充当目标 Player 热更验收。实际结果以交付验证记录为准。

2026-11-11 迁移时的实际结果见 [HybridCLR 迁移验证记录](hybridclr-validation.md)，属于当时源码与母包的历史证据。2026-11-12 [通用控件迁移](../delivery/2026-11-12-ui-controls.md)改变 Common/AOT 基类，该轮只验证 Editor，未重建 Windows/Android Player。历史母包结果不能证明当前源码的 Player 已通过；新母包需重新构建并执行对应平台验收。CharacterLab 的离线包结果另见其[手册及专项记录](character-lab.md)，不代替 Foundation 冷启动热更验收。

## Windows回滚与文件占用

同版本资源修复若候选验证失败或取消，必须恢复原隔离目录。Windows短暂目录占用会使重命名返回访问/共享/锁冲突，恢复路径执行有时限的等待重试；不重试目标已存在等无关错误、不修改ACL。回滚不接受已经取消的安装token，持续失败保留安装与回滚两项异常及原/新资源目录，禁止清理脚本把它们当作无用缓存删除。

2026-11-13的保护属于Shared.Updates/AOT变更，需新母包才能交付旧客户端；不能仅用业务热更覆盖更新器。[触发经过与验证](../delivery/2026-11-13-battle-presentation.md)。

Windows 完整包入口临时关闭构建设置中的 `Create Visual Studio Solution`，确保产生可运行的 `.exe`，并在结束时恢复原设置。若本机编译峰值触发 Windows 页面文件不足，可在同一终端仅为该次构建设置 `BEE_BUILD_THREADS=2`。

正式启动场景为 Home/Scenes/GameHome.unity，Foundation 仍使用原 FoundationEntry 及签名流程。清单增加 AstralOath.Client.Home 和 AstralOath.Client.Arena，HomeArena 组随同发布。启动契约、内置启动场景或立绘桥变更按新母包构建。含热更新脚本的联机 BattleScene 及转场配置在 Foundation YooAsset 中交付，0.1.9 升级该入场路径需要带新场景加载器的母包。 [主界面与模拟战](arena-lobby.md)

第 41–50 名角色使用更新的 Spine 2.1 AOT 读取器处理同一槽的多条颜色轨和同一骨骼的多条缩放轨。对仍内置旧读取器的平台，交付这批内容需要重建相应 IL2CPP 母包，并与规则 14 服务端、配置和表现资源配套发布。本次角色制作未发布安装包。[角色交付记录](../delivery/2026-11-20-roster41-50.md)
