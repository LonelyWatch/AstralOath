# 新会话接手入口

这是现行文档入口。先理解当前实现和证据，再开始任务；不要从旧聊天或历史计划推断已完成状态。

## 每次会话开始

1. 读取根 [AGENTS.md](../AGENTS.md)、[当前状态](project-status.md)、[通用规范](development/project-standards.md)。
2. 运行 `git status --short`、`git branch --show-current`、`git worktree list`、`git log -5 --oneline`。默认在已检出 main 的主工作区工作；不要在旧 detached worktree 修改并声称已同步。
3. 根据任务读取模块 README、下列专题和相关源代码；先说明可观察的验收行为。
4. 检查当前任务有无已有改动、在跑的测试/Editor 和资源占用，保留用户改动。记录本次文件所有权。
5. 任务中同步维护文档，交付前完成[文档同步检查](development/ai-workflow.md)。未完成时按[交接模板](development/task-template.md)写出阻塞和下一步，不依赖聊天记忆。

本机主工程目前在 `D:\UnityProjects\AstralOath`；`C:\Users\Admin\.codex\worktrees\astraloath-foundation\AstralOath` 为历史工作区。路径是本机事实，不是跨机器固定约束；以 Git worktree 列表为准。

## 按任务阅读

- 初次启动和目录：[根 README](../README.md)、[客户端架构](architecture/client.md)、[服务端 README](../Server/README.md)。
- 新系统/模块：[通用规范](development/project-standards.md)、目标模块 README、[AI 开发流程](development/ai-workflow.md)。
- UI：[UIBaseView / Binder / 控件](development/ui-binder.md)、[控件决策](architecture/adr/009-ui-controls.md)。
- 配表：[Config README](../Config/README.md)、[配置与内容更新](architecture/content.md)。
- 热更/强更：[构建操作](development/hot-update.md)、[更新能力核对](delivery/2026-11-12-foundation-reaudit.md)、[HybridCLR 决策](architecture/adr/007-hybridclr.md)。
- 服务端/数据库：[开发运维](development/server.md)、[服务端架构](architecture/server.md)。
- 角色资源：[角色实验室](development/character-lab.md)、[通用导入规则](development/character-asset-import.md)、[CharacterPresentation](../Assets/Game/Modules/CharacterPresentation/README.md)、[CharacterLab](../Assets/Game/Modules/CharacterLab/README.md)。
- 战斗：[场景模块](../Assets/Game/Modules/BattleScene/README.md)、[原作研究和截图](development/epic-seven-battle-reference.md)、[角色配表](development/battle-character-data.md)、[等级面板与战斗单位](development/battle-growth.md)、[战斗第三步与双人验证](development/battle-execution.md)、[场景验收](delivery/2026-11-12-battle-scene.md)。
- 最近发生了什么：[变更与交接日志](development/change-log.md)、[当前状态](project-status.md)。
- 版本固定来源：`Tools/dependencies.lock.json`、`Packages/manifest.json`/`packages-lock.json`、各 .NET `packages.lock.json`。

## 文档权威与维护

用户最新指令优先。根 AGENTS 规定工作约束；通用规范和相关 ADR 规定现行约定；模块 README 说明实际职责和接入方式。当前状态给出事实快照及未完成项。源码/配置与文档矛盾时核对证据并在同一任务修正，不能只选方便的一份。

`docs/plans` 是实施历史/待办，不是现行能力证明；`docs/delivery` 是特定时间和版本的证据，历史通过不能自动外推到新母包。旧计划中的 xLua、Lua Binder、开发分支规则均已被后来的 HybridCLR、C# Binder、main 规则替代。

只记录必要信息，不复制私钥、密码、访问令牌或生产连接串。`.artifacts` 证据不进 Git；文档必须保留运行命令、结果摘要、代码基线、路径和限制，缺少原始日志时明确写历史记录而非重验。
