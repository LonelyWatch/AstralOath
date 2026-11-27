# Windows 本机试玩入口

0.1.23 / R22 完整解压后，首次运行双击包根目录的 `启动试玩.cmd`。服务启动后，每次双击同目录的 `AstralOath-Play.exe` 都会另开一个客户端。该版本的游戏、签名内容与数据库端口分别为 7728、5295、5327。[新母包、旧版升级与验证记录](../../docs/delivery/2026-11-21-windows-r22.md)

已有 0.1.21 / R18 试玩包可使用 `AstralOath-Play.exe`。先启动本机试玩服务，再从解压目录双击该文件，每次打开一个游戏窗口。多个窗口共用同一套服务，关闭其中一个窗口后，其他窗口和服务可以继续运行。

启动器从自身所在目录查找 `client\AstralOath.exe`，把游戏工作目录设为 `client`。游戏进程继承启动器指定的 `ASTRAL_FOUNDATION_URL=http://localhost:7728`。使用 `localhost` 是因为验收主机的代理例外覆盖该名称，R18 的 UnityWebRequest 对数字地址发起回显时收到 HTTP 502。安装路径及验证参数支持 Unicode 和空格。内容地址随 R18 母包配置，使用本机 5293 端口。

在装有 Visual Studio C++ x64 构建工具的 Windows 主机运行以下命令，将原生 GUI 启动器直接放进已解压的试玩包根目录。

```powershell
pwsh -File Tools/LocalPlay/Build-Launcher.ps1 -OutputDirectory ".artifacts/win-roster70/delivery"
python Tools/LocalPlay/Test-Launcher.py ".artifacts/win-roster70/delivery"
```

双击运行依赖 Windows 自带的 Kernel32 和 User32。可向启动器传入 `-astral-demo-echo -logFile "D:\试玩 记录\echo.log"`，从游戏日志核实 `ASTRAL_CONTENT_ACTIVE version=18` 与服务端返回的 `ASTRAL_ECHO_OK sequence=`。普通游戏窗口从无参数启动器进入。

## R41 本机游戏服务启动

R41 邮件客户端继续使用本工作区已激活的 R38 后端（`127.0.0.4:7728`、最低应用版本 `0.1.27`），不复制数据库或另建服务目录。启动前运行 `Ensure-R41GameService.ps1`；`-VerifyOnly` 只检查，发现合法 R36 占用时返回 `r41_switch_required` / `R41_R36_OWNED_PREFLIGHT_OK`，不把尚未切换的后端报告为就绪。

正常启动在原有命名互斥锁内处理：无监听或已有 R38 所有者时，直接调用原 R38 `ensure-host.ps1`，保持其所有权守卫和新进程失败清理。仅旧 R36 可自动切换：固定原发布目录、DLL SHA-256 `1F2901919517DC94AFA89105997E222527893B2B273D59BA3110C30453CB2E63`、dotnet 路径、完整两参数命令、PID、启动时间和 `qa-host.json` 必须全部匹配，路径不允许重解析点。先检查 R38 发布树、激活记录、现有 MySQL 身份/数据目录与已应用的 017 迁移，再重核旧进程身份，仅停止该 R36 Host，随后调用 R38 原启动器。其他旧版、未知监听、状态不匹配或无法读取进程身份一律失败，不停止它们。

该切换不停止 MySQL，不迁移、初始化或还原数据库。预检失败保留旧 Host；旧 Host 已退出而新 Host 启动失败时明确报错，由 R38 原助手清理自己创建的新子进程，不自动恢复数据库或重新拉起旧 Host。锁等待上限 55 秒，旧 Host 退出等待上限 10 秒；凭据内容不输出。

```powershell
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Ensure-R41GameService.ps1 -VerifyOnly
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Test-R41Service.ps1
```

`Test-R41Service.ps1 -LegacyPolicy` 单独执行原 R38 所有权分支，应以 `EXPECTED_RED_VALID_R36_REJECTED_BY_ACTUAL_OLD_GUARD` 失败，保留旧行为的可复现红证据。默认测试以静态身份和惰性进程对象验证白名单、预检顺序、只读、重核、拒绝和超时清理，不启停真实服务。实际冷启动、HTTP 邮件能力与普通启动器仍需现场验收，离线通过不能替代它们。

### R41助手安装与本次实际结果

`Install-R41StartupFix.ps1`把`ServiceLauncher.cpp`、`Start-R41Trial.ps1.in`、`R38.Runtime.Common.ps1.in`部署到本机可变助手路径并用现有MSVC编译`Play-R41.exe`，不改变`client`母包或签名内容41。模板安装到原路径后才执行，不能直接在Tools目录运行Runtime模板。失败写`state/startup-last.json`与安全代码文本；Windows弹窗展示阶段/代码/诊断位置，凭据不入日志。安装命令：

```powershell
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Install-R41StartupFix.ps1
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Test-R41StartupDiagnostics.ps1
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Test-MySqlProcessIdentity.ps1
```

MySQL Windows监护父与服务子分别核PID、启动时间、完整参数/路径和父子关系，再核原数据目录；不改`--no-monitor`或重启语义。真实旧R36占用→助手切换、游戏Host停止→助手恢复、最终暖启动已在Limited用户下通过，原MySQL身份全程未变，实际看见登录页。服务身份30项、诊断分类6项及MySQL身份28项通过；未把离线测试当作实际停库冷启动。[详细错误前后、回执与限制](../../docs/delivery/2026-11-27-r41-startup-fix.md)

完整Runtime流程可用`Test-MySqlProcessIdentity.ps1 -RuntimeFlowPath .artifacts/windows-r38/server/Runtime.Common.ps1`回归：真实Ensure/不可覆盖写入器执行，OS/SQL边界由fixture替换，不操作真实进程或数据库。初始monitor与最终ready回执各自独立文件，不覆盖已有记录；该流程4项在PS7与PS5.1通过。
