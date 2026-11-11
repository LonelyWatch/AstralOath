# AstralOath

Unity 客户端、.NET 服务端及共享 C# 的单仓库地基。当前交付为基础设施演示：中文界面、C# Binder、HybridCLR 冷启动热更、DOTween 动画、Luban 配置、YooAsset 资源、Protobuf 回显、MySQL 幂等持久化及签名更新。另提供离线角色实验室，浏览首批角色骨骼、动态立绘、日语语音与技能演出。真实战斗规则、BP 与原作表现一致性仍待后续验收。

## 启动

需要 Windows、Node.js 22+、Unity **2022.3.62f3**。项目工具使用 PowerShell 7，首次初始化可由 Windows 自带的 PowerShell 5.1 引导安装。安装 Unity 的 Windows IL2CPP、Android Build Support、SDK/NDK/OpenJDK；Windows 原生构建还需 Visual Studio C++ 工具链和 Windows SDK。

本次 Android 构建环境：SDK platform 36、Build Tools 34.0.0、NDK 23.1.7779620、Unity 自带 OpenJDK/Gradle 7.5.1；项目固定 target API 36、minimum API 29。升级这些依赖需单独复验。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Setup-Toolchain.ps1 -WithMySql -WithMcp
& .\.tools\powershell\pwsh.exe -NoProfile -File .\Tools\Config\Generate.ps1 -VerifyAccessors -SyncClient
```

初始化按 `Tools/dependencies.lock.json` 的固定版本和 SHA256 安装 PowerShell 至 `.tools/powershell`，不修改全局 PATH。Unity 的导表、签名和 HybridCLR 安装入口优先使用该项目副本，因此不依赖 Unity Hub 启动时继承的 PATH。离线 ZIP 或下载代理可先通过 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Setup-PowerShell.ps1 -ArchivePath <ZIP路径>` 或 `-Proxy <代理地址>` 安装，再执行上面的工具链初始化。下文的 `pwsh` 命令也可替换为 `& .\.tools\powershell\pwsh.exe`。

用 Unity 打开仓库，加载 `Assets/Game/Modules/FoundationDemo/Scenes/FoundationDemo.unity` 后播放。场景与 TMP 必需资源已经入库；重建演示可执行编辑器入口 `AstralOath.Editor.FoundationBuilder.CreateDemo`，或 `pwsh -File Tools/Invoke-Unity.ps1 -Mode Generate` 在隔离副本生成并用 `Tools/Sync-UnityGenerated.ps1` 同步回来。

按[服务端手册](docs/development/server.md)准备 MySQL 8.4 独立数据库与账户，通过环境变量 `ConnectionStrings__MySql` 提供连接串。开发终端运行：

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ROOT = "$PWD/.tools/dotnet"
& ./.tools/dotnet/dotnet.exe restore Server/Host --locked-mode
& ./.tools/dotnet/dotnet.exe run --project Server/Host -- migrate
& ./.tools/dotnet/dotnet.exe run --project Server/Host --no-restore
```

迁移成功后再启动服务。默认监听 `127.0.0.1:5080`，演示按钮发送实际 Protobuf 请求。HTTP 仅用于开发 loopback，生产使用有效 HTTPS 证书。数据库口令、更新私钥与证书不要写入仓库。

## 检查与构建

```powershell
# 数据库测试连接串指向专用测试库，不得指向正式库。
pwsh -File Tools/Check-Foundation.ps1 -RequireMySql -UnityTests
```

Windows x64、Android ARM64 ASTC/ETC2 的强更包、热更包和签名命令见[热更与强更构建](docs/development/hot-update.md)。首次迁移需安装对应平台的新母包。

总入口要求先提供 `ASTRALOATH_MYSQL_TEST`；现场界面回显另设 `ASTRAL_FOUNDATION_LIVE_TEST=1`，并保持开发服务运行。省略 `-RequireMySql` 可做无数据库检查，但数据库测试可能显示跳过。Unity 脚本使用 `.artifacts/UnityProject` 副本，日志在 `.artifacts/unity`，构建默认输出至仓库的 `.artifacts/builds`，避免锁住正在编辑的工程。

修改表后运行 `Generate.ps1 -VerifyAccessors -SyncClient` 更新客户端生成快照并一并提交；检查入口使用 `-CheckClient` 拒绝源表与快照不一致。签名更新必须包含同批业务程序集、配置、本地化及依赖资源。

GitHub Actions 执行源码边界、协议生成、配表及更新器检查；MySQL、Unity 授权、原生构建和设备运行另行验收。流水线不会部署服务或追踪国际服版本。

## 角色实验室

先执行 `git lfs pull` 获取角色资产及 FMOD 原生库，再选择 Unity `Tools/角色实验室/打开场景` 并运行。此场景独立于 FoundationDemo，不需要启动服务端。详细操作、配表、导入和 Windows 构建见[角色实验室手册](docs/development/character-lab.md)，已通过与未验证项见[执行台账](docs/plans/2026-11-12-character-lab.md)。

## 文档导航

- [历史地基交付验证记录](docs/delivery/foundation.md)：通过、阻塞和未执行项。
- [HybridCLR 迁移验证记录](docs/development/hybridclr-validation.md)：本轮构建、旧包更新和冷启动恢复实测及未验收项。
- [地基决策](docs/architecture/foundation-decisions.md)、[HybridCLR 决策](docs/architecture/adr/007-hybridclr.md)、[客户端](docs/architecture/client.md)、[服务端](docs/architecture/server.md)、[配表与更新](docs/architecture/content.md)。
- [AI 开发与 MCP](docs/development/ai-workflow.md)、[项目规则](AGENTS.md)、[依赖版本与来源](Tools/dependencies.lock.json)。
- [UIBaseView 与预制体 Binder](docs/development/ui-binder.md)：统一命名、路径注册、单行绑定与 Hierarchy 快捷增删。
- [原始资源证据边界](docs/architecture/resource-evidence.md)、[下一阶段战斗验收](docs/delivery/battle-acceptance.md)。

界面语言与语音语言分别配置，默认中文与日语。`D:\sevenepic` 保持只读，仅导入首批三角色及其依赖，不提交完整解包目录。国际服版本追踪仅在用户明确提出时执行。
