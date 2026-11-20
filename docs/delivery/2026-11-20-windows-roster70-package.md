# Windows x64 本机试玩包 0.1.21 / R18

游戏与服务端从 `96944bcec867d9203d34802332a31f6b11a7b99a` 构建。上次分发的 0.1.20 / R17 早于 `5be848e8` 对 Spine 2.1 读取器与着色器的修改，因此本次重新构建了最小 IL2CPP 母包。基线为 `9ff5a88da1fb4a1b97b874dd02a0d2e2`，平台为 `StandaloneWindows64`，内置内容版本为 0。签名资源 R18 和规则 17 服务端配套使用。

## 本机启动

交付目录为 `.artifacts/win-roster70`。完整解压 `AstralOath-Windows-x64-0.1.21-R18.zip`，双击 `启动试玩.cmd`。首次点击登录后，按提示安装资源并退出。在试玩命令窗口按回车重新打开游戏。该窗口也可启动第二个客户端，支持两个访客在本机模拟战。

试玩包自带 Python 3.12.12、MySQL 8.4.13 和 ASP.NET Core 10.0.14 x64。游戏服务、内容和数据库分别监听 `127.0.0.1` 的 5280、5293、5327 端口。账号进度和日志写入解压目录之外。运行时授权说明在 `runtime/NOTICE.txt`，MySQL 对应源码 `mysql-8.4.13.tar.gz` 随 ZIP 同目录提供。本次验收环境为项目 Windows x64 主机，使用专用本地数据库和测试签名。

## 交付校验

直接运行解压后的 `启动试玩.cmd`，用独立测试用户数据打开可响应的游戏窗口。输入 `Q` 后启动器返回 0，Player 退出，MySQL 正常关闭，三个本地端口均已释放。证据为 `qa/normal-launcher.json`。

- ZIP 为 2,019,762,606 字节，共 1,168 个文件。完整解压并逐文件与交付目录比较 SHA-256 通过。ZIP SHA-256 为 `75bf48f8aeefcf205f7b993168327ecd4bd83c9232259d06ff0ae4d3960c0f5b`。
- R18 清单为 15,937 字节，包含 98 个文件、共 2,142,037,157 字节。公钥验签和所有载荷的长度、SHA-256 校验通过。直接读取 UnityFS，确认 70 人、210 个常规技能、本批 29 张演出图和 10 份角色骨骼与目标提交一致。目录 SHA-256 为 `afa393d3b419fe3b10c27623c1b175fc19e19571a507ac751d3fb221a50129c3`。
- 从新母包的空缓存启动真实 Windows Player，实际从 5293 安装并激活 R18，加载 Foundation 入口，日志记录 `ASTRAL_CONTENT_ACTIVE version=18 recovered=False`。证据在 `qa/Windows-R18-cold-start.log` 和 `qa/roster70-20261120-200621.txt`。
- 从含中文与空格的 ZIP 解压目录启动随包运行时、独立数据库、服务端、内容服务及图形 Player。两次八人对战均完成双方选禁、布阵、模型入场、回合计时和首条命令。第一场使用资源预载，第二场反向选人并走常规入场。本批十人都实际出场，两场首条命令均为帕贝尔 `c1080.s1`。对应日志为 `qa/windows-extracted-r18-rules17-b9e0c04595bc4c978d0c954b51412090.log` 和 `qa/windows-extracted-r18-reverse-68c3a433c1054b009aaa326eb277264d.log`。本项验证覆盖进场和首条指令，技能逐槽核对见下方专题。

## 构建复现

1. 在与目标提交一致的工程副本，使用 `Tools/Invoke-Unity.ps1 -Mode Windows -BuildTarget Win64 -Development -DevelopmentHttp -Minimal -AppVersion 0.1.21 -ContentVersion 18`，传入本机内容地址、签名公钥与私钥。Unity 出具 `baseline.complete.json` 和 `package.complete.json` 后可独立执行签名。
2. R17 的本地构建溯源为 905,686 字节。R18 的 95 个资源包记录了 7,876 条来源，生成的溯源为 1,874,066 字节。旧签名工具在读取该文件时拒绝签发。`Tools/Content/Release.ps1` 现允许最多 8 MiB 的本地构建溯源。下载清单的生成与验签仍分别校验 1 MiB 上限。
3. 从本次构建副本使用 `Tools/Content/Package-Unity.ps1 -BuildOutput` 读取同一份归档和实际构建源文件，重新签发，然后用 `Tools/Content/Release.ps1 -Action Verify -ProtocolVersion 1 -SchemaVersion 3` 和公钥验签。`Tests/Content/ReleaseSmoke.ps1` 覆盖可签发超过 1 MiB 的合法审计文件、拒绝超 8 MiB 的审计文件和超 1 MiB 的传输清单。

角色技能效果和仍待补齐的原作素材见[本批技能复核](2026-11-20-roster61-70-effect-audit.md)。
