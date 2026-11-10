# AstralOath

Unity 客户端、.NET 服务端及共享 C# 的单仓库地基。当前交付为基础设施演示：中文界面、Lua Binder、DOTween 动画、Luban 配置、YooAsset 资源、Protobuf 回显、MySQL 幂等持久化及签名更新。真实战斗、角色技能、BP 与原作表现尚未实现或验收。

## 启动

需要 Windows、PowerShell 7、Node.js 22+、Unity **2022.3.62f3**。安装 Unity 的 Windows IL2CPP、Android Build Support、SDK/NDK/OpenJDK；Windows 原生构建还需 Visual Studio C++ 工具链和 Windows SDK。首次执行：

本次 Android 构建环境：SDK platform 36、Build Tools 34.0.0、NDK 23.1.7779620、Unity 自带 OpenJDK/Gradle 7.5.1；项目固定 target API 36、minimum API 29。升级这些依赖需单独复验。

```powershell
pwsh -File Tools/Setup-Toolchain.ps1 -WithMySql -WithMcp
pwsh -File Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient
```

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
pwsh -File Tools/Invoke-Unity.ps1 -Mode Windows -DevelopmentHttp
pwsh -File Tools/Invoke-Unity.ps1 -Mode AndroidAstc
pwsh -File Tools/Invoke-Unity.ps1 -Mode AndroidEtc2
```

需要实际 Windows 回显及截图时，将 Windows 构建命令附加 `-ProjectName UnityWindows`，然后执行 `pwsh -File Tools/Test-WindowsPlayer.ps1 -Screenshot -Visible`。它会短暂显示演示窗口，默认连接 `http://localhost:5080`；本机隐藏窗口不呈现，数值 `127.0.0.1` 在 Player 中出现 502，而 Editor 中正常。正式环境仍使用 HTTPS。

总入口要求先提供 `ASTRALOATH_MYSQL_TEST`；现场界面回显另设 `ASTRAL_FOUNDATION_LIVE_TEST=1`，并保持开发服务运行。省略 `-RequireMySql` 可做无数据库检查，但数据库测试可能显示跳过。Unity 脚本使用 `.artifacts/UnityProject` 副本，日志在 `.artifacts/unity`，Player 位于副本内的 `.artifacts/builds`，避免锁住正在编辑的工程。

修改表后运行 `Generate.ps1 -VerifyAccessors -SyncClient` 更新客户端生成快照并一并提交；检查入口使用 `-CheckClient` 拒绝源表与快照不一致。签名更新必须包含同批 Lua、配置、本地化及依赖资源。

GitHub Actions 执行源码边界、协议生成、配表及更新器检查；MySQL、Unity 授权、原生构建和设备运行另行验收。流水线不会部署服务或追踪国际服版本。

## 文档导航

- [交付验证记录](docs/delivery/foundation.md)：通过、阻塞和未执行项。
- [地基决策](docs/architecture/foundation-decisions.md)、[客户端](docs/architecture/client.md)、[服务端](docs/architecture/server.md)、[配表与更新](docs/architecture/content.md)。
- [AI 开发与 MCP](docs/development/ai-workflow.md)、[项目规则](AGENTS.md)、[依赖版本与来源](Tools/dependencies.lock.json)。
- [原始资源证据边界](docs/architecture/resource-evidence.md)、[下一阶段战斗验收](docs/delivery/battle-acceptance.md)。

界面语言与语音语言分别配置，默认中文与日语。`D:\sevenepic` 保持只读，本阶段未导入原作资源。国际服版本追踪仅在用户明确提出时执行。
