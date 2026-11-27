# R41启动失败：旧服务占用与本机入口修复

用户11月27日反馈R41助手弹出「本机试玩未能启动」。已实际复现，不是签名资源损坏或必须重下全量资源：`.4:7728`监听者PID35692完整命令为本仓库dotnet加`windows-r36/server/host/AstralOath.Server.dll`，SHA256 `1F2901919517DC94AFA89105997E222527893B2B273D59BA3110C30453CB2E63`；R36状态文件的PID、创建ticks与目录均匹配。API最低版本仍0.1.26、邮件接口404。R38状态还是上次PID34944，原R41代理实际抛`R38_RUNTIME_REFUSED_UNOWNED_4_BEFORE_MYSQL`，因此尚未启动5305和游戏。

## 修前、目标与实际红

- 原来预期：助手准备与内容41匹配的本机服务并启动游戏。此前只实际验证已就绪服务下的Limited启动；当时「关机后冷启动未演练」有列明，但未覆盖旧R36先启动的本次场景。
- 修前：未知占用保护正确拒绝旧服务；缺少识别已有本项目旧包并安全切换的路径，而且隐藏PowerShell错误只剩通用弹窗。[用户错误图](../../.artifacts/startup-r41-fix/reference/user-error.png) · [真实红回执](../../.artifacts/startup-r41-fix/actual-red.json)
- 修复范围：只对白名单R36、匹配状态且完整进程身份一致的旧开发Host切换。必须先验证R38发布树/激活、原MySQL身份与已应用017，再次核对旧PID/创建时间/完整命令/DLL哈希，才结束该旧Host；未知进程保持拒绝。不停止MySQL，不初始化/还原数据库。无旧Host时仍使用原R38准备流程，未放宽其原有守卫。

## 独立冷启动分支

当前真实MySQL为监护父35056→实际监听子35128。原R38新建MySQL分支要求监听PID等于CreateProcess返回PID，不支持这种Windows默认模式；此项是代码加真实进程形态确认的独立隐患，不是本次已实际触发的报错。保持monitor语义，增加父子关系、创建顺序、父PID/ticks、双方路径/完整参数的严格校验，再沿原流程验证`@@datadir`和重复监听身份；不改参数为no-monitor。[MySQL官方Windows说明](https://dev.mysql.com/doc/refman/8.4/en/windows-installation.html) · [官方选项](https://dev.mysql.com/doc/refman/8.4/en/server-options.html)

## 文件与验收入口

- `Tools/LocalPlay/Ensure-R41GameService.ps1`、`R41Service.Identity.ps1`：旧服务受控切换及身份边界；`Test-R41Service.ps1`为离线回归。
- `Tools/LocalPlay/MySqlProcessIdentity.ps1`、`Test-MySqlProcessIdentity.ps1`：MySQL进程身份及监护父子边界；`R38.Runtime.Common.ps1.in`为部署到原R38路径的完整模板，不直接从Tools执行。
- `Tools/LocalPlay/Start-R41Trial.ps1.in`、`ServiceLauncher.cpp`：本机助手分阶段日志及安全错误代码弹窗；不输出连接串、令牌或异常正文。
- `Tools/LocalPlay/Install-R41StartupFix.ps1`：部署上述本机可变助手并实际重编`Play-R41.exe`；不改签名内容41、游戏本体或母包。各脚本准确路径见[LocalPlay](../../Tools/LocalPlay/README.md)。

## 最终实际验证

- 旧R36占用→运行修复后的`Play-R41.exe`：普通用户Limited交互任务实际成功。旧Host35692退出，新R38 Host15172就绪，资源源41启动，本体原生D3D11进程成功。[旧服务切换回执](../../.artifacts/startup-r41-fix/legacy-switch/verified.json)
- 在无游戏窗口时，仅停止本次测试创建且完整身份核实的新Host15172，再从助手启动：实际恢复为Host40884，原生本体成功。[停止后恢复回执](../../.artifacts/startup-r41-fix/server-restart/verified.json)
- 最终暖启动：实际成功并保留游戏进程23408，随后CUA实际查看窗口790400，看见App0.1.27/RC0及「登录」按钮，未代替用户登录账号或声称新一轮玩法全流程验收。[最终启动回执](../../.artifacts/startup-r41-fix/final-warm/verified.json)
- 上述三次MySQL均为原35128、ticks639313774372227137，没有停止、初始化或还原数据库。服务健康200、最低版本0.1.27、邮件匿名接口401（存在且要求鉴权，原旧服为404）、资源清单41。
- 旧严格策略对合法R36有实际红，新服务身份/时序/未知监听/退出及句柄边界30项通过。数据库错误被误写为端口占用的消息分类也实际红→修后6项通过。MySQL进程身份28项在PS7/PS5.1分别通过、当前真实父子只读验证通过；完整MySQL启动流程离线回归另行记录，不冒充本机停库冷启动。

MySQL完整实际`Ensure`加真实不可覆盖写入器的离线流程，又发现初始和最终回执写同路径会失败（3通过/1失败，`R38_RESULT_ALREADY_EXISTS`）。现分别写不可变`mysql-child.json`/`mysql-ready.json`，完整4/4通过，PS5.1也4/4；陌生监听者在SQL前被拒绝。只替换OS进程/SQL边界，不启真实MySQL，当前正常游戏未受影响。[原失败](../../.artifacts/startup-r41-fix/receipt-regression/runtime-red.json) · [根复跑4项](../../.artifacts/startup-r41-fix/runtime-flow-green-final.json) · [最终模板/部署一致](../../.artifacts/startup-r41-fix/receipt-regression/runtime-freeze.json)

修后助手SHA256 `CDDE9EF2E9F7867733A7055921EDCD32F832097DD8A43AA3E68618B18CC0866B`；运行状态/失败阶段/安全代码写入`windows-r41/state/startup-last.json`，失败弹窗会显示阶段、代码和诊断文件。历史通用错误和修前助手已保存在`.artifacts/startup-r41-fix/before/`。所有修改位置见[准确文件清单](../../.artifacts/startup-r41-fix/changed-files.md)。

## 限制与未执行

没有为了演练而停止现有MySQL；全机断电重启及原数据库从停止状态恢复仍未实测。已有R36→R38切换和游戏Host从停止恢复分别已实际通过。未知占用者及非白名单旧版本仍明确拒绝，不会强行结束；日志目录不可写等启动基础环境故障可能只能回退通用弹窗。历史R38 PID恰被合法R36重用时可能安全拒绝自动切换，不会误停未知进程。声音/特效不适用此修复，使用实际错误前后、进程身份、健康接口、原生启动与已实际看到的登录页验收。
