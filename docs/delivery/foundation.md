# 首次地基交付记录

> 首次地基的历史证据。Lua/libxlua、schema 2、旧分支与安装包结论不能代表当前主分支；现状见docs/project-status.md。

验证日期：2026-11-10。本记录只描述本仓库基础设施，不证明《第七史诗》的战斗规则、资源或演出已完整还原。

## 已通过

- 本机 .NET 10 Release 测试：11 通过、0 失败、0 跳过。使用真实 MySQL 8.4.13，覆盖迁移、并发幂等、重建宿主后回读、冲突与事务回滚。
- 实际 Kestrel HTTP/HTTPS/WSS 演练；生产 HTTPS 拒绝监听配置覆盖，生产不暴露开发回显 WebSocket。临时 TLS 信任仅限测试客户端。
- 真实 MySQL 备份与空库恢复，逐行核对内容；拒绝宽授权恢复、跨库 SQL 和登录路径覆盖，恢复夹具限定本机 3307 并保护临时凭据 ACL。
- 更新器 10 组真实 HTTP/磁盘场景：RSA 签名、文件哈希、异常路径与额外文件、大小不符、断流/Range 续传、服务器忽略 Range、取消、磁盘不足、兼容性、候选拒绝、版本切换、回滚及缓存损坏修复。
- 实际 Luban 5.1.0 生成 JSON/二进制/C#，使用生成访问器读回，校验引用与范围；协议生成通过，脚本失败不覆盖已发布配表输出。
- 原生 Unity 场景生成；中文 Noto Sans SC TMP 字体、Lua Binder、配置、资源及真实服务端回显闭环。最终 EditMode 18 项、PlayMode 4 项通过，包括关闭/重开、重复点击、取消、复用、DOTween 复位、有状态 Binder 验证、中文 Protobuf 8192/8193 字节边界、候选文案隔离与真实音频/纹理导入重导。
- Unity MCP 服务初始化、48 项工具目录、明确选择隔离项目并查询 Editor 状态通过。当前桌面会话重新加载项目 MCP 配置后方可直接使用工具。
- Node 模块边界验证器 4 项通过；完整暂存区凭据、缓存、大文件检查通过。
- 已用真实 Android YooAsset 包完成来源校验、签名及验签。
- 合法修改配表为 10 秒、增加文案 key、修改现有中文，生成访问器和客户端快照同步通过；0/31 秒仍被拒绝。CI 检查源表与生成快照一致。

## 构建与最新复验

Android ASTC 与 ETC2 两份 ARM64 IL2CPP APK 已构建成功，ASTC 重复构建也通过。aapt 与 ZIP 内容检查确认最低 API 29、arm64-v8a，并实际包含 libil2cpp.so、libunity.so 和 libxlua.so。未连接 Android 真机。

Windows x64 IL2CPP 构建及实际 Player 回显通过。签名版本 2 的实际下载、空间查询、候选 Lua/配置/本地化验证、激活及回显通过；破坏签名、停止更新服务器后均恢复到有效版本 2，并继续完成回显。1600×900、1200×900 和 2100×900 的真实截图已目检，中文、按钮、状态和恢复提示均可见，无重叠。

首次 Windows 构建缺少 IL2CPP 模块和 Windows SDK，已校验官方安装器签名并补齐。验证使用 Windows SDK 10.0.26100.9457 桌面 C++ x64。真实 Player 又暴露了 IL2CPP 的 DriveInfo 不支持问题，已用 Windows GetDiskFreeSpaceExW 和 Android 主线程 StatFs 适配器替换客户端空间探针。Windows 更新实际通过；Android 实机 StatFs 尚待设备验证。

截图需使用 `Test-WindowsPlayer.ps1 -Screenshot -Visible`，本机隐藏窗口不呈现图像。Player 联调使用 `localhost:5080`，数值 `127.0.0.1` 在本机 Player 返回 502，Editor 中正常。默认场景未配置远端更新，验收用公钥和 localhost 资源地址只写入隔离副本。

本机证据在忽略目录：`.artifacts/foundation-final-checks.log`、`config-smoke-final.log`、`unity/{EditMode,PlayMode}.xml`、`unity/{Windows,AndroidAstc,AndroidEtc2}.log`、`windows-signed-final.log`、`windows-bad-signature.log`、`windows-offline-recovery.log` 及同名 PNG。Windows 产物在 `.artifacts/UnityWindows/.artifacts/builds/Windows`，Android 在 `.artifacts/UnityProject/.artifacts/builds/Android`。

## 使用与交付边界

入口见根 README。源码、协议、配表源、生成工具、必要 Unity 场景/预制体/字体、插件及其许可证进入版本库。原始解包资源、测试数据库、密钥、日志、构建产物和 SDK 缓存保持本地忽略。开发分支推送后不自动合并 main，不部署云主机。

Windows Server 2022 服务注册、云主机备份恢复、百人容量及 Android 真机表现未在本机测试中验收。资源更新磁盘不足已通过注入剩余空间的真实下载流程验证；移动设备断电、平台存储驱逐和长时间运行仍待设备验收。演示没有账号、完整战斗或实际日语配音。

本地完整检查已实际执行。GitHub Actions 工作流随本次代码推送，其远端结果以仓库 Actions 页面为准；普通源码 CI 不配置真实 MySQL 或 Unity 授权。尚未在一台全新机器执行完整安装。

后续由用户监督[战斗场景验收](battle-acceptance.md)。源资源已有明确疑点和缺失引用，见[资源证据](../architecture/resource-evidence.md)，不能把资源数量或格式识别视为还原完成。

## 本次执行决策

- 托管 worktree 创建后无法写入，改在已授权的原仓库 `codex/foundation` 实施，Unity 验证使用忽略目录内副本。保留主工作区和用户进程；代价是依赖文件所有权保证并行编辑隔离。
- 按用户最新决定撤下资源/战斗还原前置门槛。代价是后续实际战斗接入仍可能要求补资源、解析器或调整规则契约。
- Oracle EF 数据提供者的 Protobuf 下限与共享协议锁定版本不兼容，按计划中的兼容依赖要求采用 MySqlConnector 的参数化显式 SQL。MySQL 选型未变；代价是迁移与查询由项目显式维护。
