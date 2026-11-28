# Windows 本机试玩入口

## R47 UID后端准备与切换

`Prepare-R47Backend.ps1` 将当前.NET Release后端独立发布到 `.artifacts/windows-r47/backend`；清单 `.artifacts/windows-r47/backend-release-manifest.json` 记录MSBuild实际Compile/EmbeddedResource/Content、工程及锁文件的哈希、全部输出、21个迁移和复用助手。默认只准备归档，`sourceCommit=null/readyForActivation=false`，不读取连接串、不连接SQL、不启停服务。Root提交最终源码后用 `-PinSourceCommit <40位提交>` 复核源/助手/产物哈希并钉源；此参数由Root提供，不由脚本操作Git。已有输出拒绝覆盖，失败保留日志，不擅自清理后重发。

迁移校验和严格复用生产 `DatabaseMigrator` 语义：`StreamReader`识别并去掉BOM、CRLF规范为LF后对UTF-8文本求SHA-256；不能用SQL原始字节哈希。R47首次预检因此失败于 `R47_MIGRATION_CHECKSUM_MISMATCH`，尚未停Host或写主库；失败回执和修前清单保留于R47的 `server/results` 与 `backend-preparation`，仅重算清单并撤销旧钉源，二进制不重发。

`Ensure-R47GameService.ps1` 默认只恢复已激活R47，`-VerifyOnly`不启动进程。一次性R44→R47必须先 `-SwitchToR47` 只读预检，再用15分钟内同输入回执 `-SwitchToR47 -Execute -PreflightReceipt <结果JSON>`。它复用R44/R38路径、完整命令/PID/启动时间/DLL/监听所有权守卫、现有MySQL数据目录确认、备份和隐藏启动；旧R44助手及其发布树不修改。切换仅停止已核准的R44 Host，不停止MySQL；先冻结写入并创建完整新备份，再运行**新R47 DLL的完整 `migrate`**，使021同时建表、为旧玩家分配UID、补资料幂等回执并记录迁移。SQL文件单独执行不能代替该入口。迁移前后确认原MySQL身份及完整校验和，完成后检查全部旧玩家与回执的UID已初始化，运行身份原有schema级SELECT/INSERT/UPDATE须覆盖新表，助手不自动提权或修改GRANT。

用户本轮明确免回归、要求尽快打包由其验收，因此新R47脚本只作静态语法/代码审查及后端publish编译；**未执行新增回归、恢复演练、主库预检/备份/迁移或服务切换**。执行路径保留必要备份，明确不再跑隔离恢复演练；失败保留备份和主库，不自动恢复、不清空库、不重新抽UID、不停止未知进程。Root负责最终钉源、人工复核、切服及发布；本段与准备归档不能冒称实际部署或实机通过。

```powershell
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Prepare-R47Backend.ps1
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Prepare-R47Backend.ps1 -PinSourceCommit '<Root最终40位提交>'
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Ensure-R47GameService.ps1 -SwitchToR47
# Root确认上一步JSON中的resultId后再执行；准备阶段没有执行这两步。
.\.tools\powershell\pwsh.exe -NoProfile -File Tools/LocalPlay/Ensure-R47GameService.ps1 -SwitchToR47 -Execute -PreflightReceipt '<.artifacts/windows-r47/server/results/<resultId>.json>'
```

当前入口为`.artifacts/windows-r46/Play-R46.exe`，普通Limited用户实际启动本体、D3D11、主R44/Feed46/原MySQL身份通过，Root实看无遮挡登录页；此观察不自动登录账号。最终四轮Native业务均46、暖缓存0下载。R44已获后续开发升级授权并完成45表隔离恢复校验、018–020迁移；失败恢复误判修复后实际成功，原备份/失败回执保留。仅自有.6 QA已停止，主服务/MySQL/Feed保留；没有执行全机断电重启演练。[本轮启动、包及限制](../../docs/delivery/2026-11-28-home-social-visual-fix.md)

11/28 R45实际升级演练发现进程包装器把 `CopyToAsync` 的 `VoidTaskResult` 输出到管道，真实导入退出0仍被当作失败。已显式丢弃异步完成值，只返回整数退出码；新增 `Test-CapturedProcess.ps1` 运行自有短子进程，512KiB输入的成功/失败和无输入共3项。修前1/3、修后3/3，原24项服务安全契约仍通过。原SQL备份不修改，主库未恢复；实际恢复/迁移/启动结果见R45交付记录。此改动仅本机PowerShell助手，不改变已签名客户端或后端DLL。

R44新增独立`Ensure-R44GameService.ps1`和Identity/Common/Test。默认只恢复已激活44；主库迁移与R38替换须先独立`-SwitchToR44`只读预检，再用15分钟内同输入回执显式`-Execute`。执行核准旧Host并只停该Host，保持MySQL，真实备份并只向唯一隔离schema恢复演练，逐表行数/EXTENDED CHECKSUM匹配才应用018/019/020和启动新Host；失败不向主库恢复、不初始化数据目录、不杀未知进程。以Social路由401、Entry0.1.27和实际SHA判断新服，无虚构Version字段。历史R38工具只复用不改动；作用域阻断有真实RED、安全24/24。后续实际切服/隔离恢复已按上文46完成，全机断电后重开未演练；保留原阶段命令和失败记录，不把离线绿当实际通过。[任务依据](../../docs/development/home-profile-chat-reference.md)

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
