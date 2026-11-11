# AI 开发与维护流程

## 日常任务

1. 人确定目标、验收行为和影响模块。AI 先读根 AGENTS 与模块文档，核对工作区和当前测试结果。
2. 非平凡改动建立 `codex/` 分支，按模块声明所有权；协议、配表、共享类型先约定再并行。只有一个 Agent 写同一 Unity Editor，代码任务可在不同模块并行。
3. 重要行为先写能失败的测试。先修复具体失败，再做最小实现；资源生成、场景绑定和运行结果不能用“文件已创建”代替。
4. 执行下述检查。独立审查关注竞态、取消、资源释放、网络幂等、兼容版本和资产引用；修复后复跑相关检查。
5. 提交源码、源配置、生成器、必要生成产物、meta 和文档。Git 提交标题与正文统一用中文，代码标识符、文件路径和专有名称保留原文；清楚说明改动及目的。交付提交哈希、测试结果、未验证项及复现步骤。

根规则适用于全工程；模块职责、输入输出、生命周期、资源与配置 key 应写入模块 README。新模块用 `pwsh -File Tools/New-Module.ps1 -Name Inventory` 创建标准目录，再由 Unity 导入 meta。禁止让生成器覆盖已存在模块。

新增或修改业务 UI 时，核对 `Foo.prefab`、`Foo.cs`（`Foo : UIBaseView`）与 `FooBinder.cs` 同名关系，并按 [UI 命名约束](ui-binder.md)维护路径注册与 GUID。业务 View/Binder 不挂到预制体上。

按[任务与交接模板](task-template.md)记录每次工作。不同 Agent 的“完成”消息需要由协调者核对改动和实际验证结果，不能自动当作验收通过。

## 检查入口

- `pwsh -File Tools/Check-Foundation.ps1 -RequireMySql -UnityTests`：本地总入口。需先设置隔离测试库连接串；服务端现场回显测试另设 `ASTRAL_FOUNDATION_LIVE_TEST=1` 并启动服务端。
- `node --test Tools/Tests/validate.test.mjs`：模块依赖验证器回归。
- `node Tools/validate.mjs`：内部引用、依赖环、Common 边界、MCP 编辑器隔离和依赖 pin。
- `node Tools/validate.mjs --staged`：额外检查暂存区私钥、缓存和过大文件。
- `pwsh -File Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient`：真实 Luban 生成、严格引用检查、二进制访问器读回及客户端快照同步。CI 使用 `-CheckClient` 检测遗漏生成物。
- 服务端与更新器测试命令见各模块 README；真实数据库用例需要隔离 MySQL，不得以跳过结果冒充通过。
- `pwsh -File Tools/Invoke-Unity.ps1 -Mode EditMode`、`-Mode PlayMode`：隔离 Unity 副本测试。`-Mode Windows`、`-Mode AndroidAstc`、`-Mode AndroidEtc2`：构建。

同时构建不同平台时可传 `-ProjectName UnityWindows` 等独立副本名称。每个副本只运行一个 Unity；`-NoSync` 用于保留该副本的临时构建配置。回同步工具默认只读取 UnityProject，测试用更新公钥和端点不要回写正式场景。

## Unity MCP

编辑器包和本地服务固定为 CoplayDev/unity-mcp 10.3.0。执行 `pwsh -File Tools/Setup-Toolchain.ps1 -WithMcp` 创建项目内 Python 环境；配置参考 `Tools/Mcp/codex.example.toml`。其 cwd 改为实际仓库绝对路径后可写入项目 `.codex/config.toml`。仅在受信任项目启用，重新加载 MCP 连接后验证工具列表和实际 Editor 实例。

Unity 中打开 Window > MCP for Unity 并启动 STDIO bridge。始终先选择项目实例，再操作场景或运行测试；多个 Editor 不得凭默认实例猜测目标。先读状态，做小批次操作，检查 Console 和场景保存结果。MCP 故障可直接走命令行脚本，不阻塞代码维护。

`Tools/Mcp/Smoke.py` 通过实际 MCP SDK 初始化并列举工具；`--editor` 进一步选中 `.artifacts/UnityProject` 隔离实例并读取状态。它用于验证工具链，不会自动启动 Editor，也不把连接其他项目算作成功。

MCP 的 Runtime 帮助程序集在本项目限定为 Editor；远程资产生成和外部账号功能不作为本工程流程的一部分。启动脚本关闭可选遥测。工具选择只涵盖项目编辑、读取 Console 和测试，不把 MCP 发到客户端或业务服务端。

MCP 配置依据 [官方配置说明](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)，Unity bridge 用法依据 [上游文档](https://github.com/CoplayDev/unity-mcp/tree/v10.3.0)。配置文件存在、服务握手成功和 Editor 实际连接是三个不同检查项。

本机项目配置已放入忽略的 `.codex/config.toml`；其他机器使用模板重新配置绝对路径。当前对话工具不会因文件写入自动刷新，需要重新加载连接。已通过 SDK 直接连接隔离 Editor 的验证不等同于当前对话已自动获取这些工具。

仓库 CI 使用[官方 checkout Action](https://github.com/actions/checkout)的固定提交，仅有 contents:read 权限。Windows runner 的 Node 和 PowerShell 环境运行同一个本地检查脚本；Unity 构建需要持有有效授权的构建机，不能把普通源码 CI 绿灯当作玩家包验收。

## 发布与版本追踪

构建/检查可以在提交时运行；国际服数据追踪仅在用户明确提出后开展，禁止定时任务或后台持续采集。一次追踪记录原版本、目标版本、证据、配表差异、资源闭包、协议影响和验收结果。核心战斗行为变化由 C# 应用版本承载；新旧内核不兼容时走维护更新。

生产密钥、云主机凭据和支付功能不进入 AI 上下文或仓库。这里的首次交付只推送开发分支；生产部署是独立操作。
