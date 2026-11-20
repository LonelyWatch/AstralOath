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
