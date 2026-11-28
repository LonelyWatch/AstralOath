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
- 账号和英雄背包：[录像、数据模型与验收](plans/2026-11-17-hero-inventory.md)、[服务端接口与迁移](../Server/README.md)。
- 游戏内邮件：[玩家收件、开发 GM 自发、服务端名单与整区投递的设计](plans/2026-11-22-game-mail.md)。
- 普通装备：[背包、单槽配戴和管理页的原版画面与时间点](development/equipment-ui-reference.md)、[原作六部位、强化与套装规则及项目实施方案](plans/2026-11-21-equipment-system.md)、[11 月 25 日普通强化、角标与验收方案](plans/2026-11-25-equipment-enhancement.md)。
- 当前本机试玩：[Windows0.1.27 / 内容R47，UID、频道未读与召唤追加修订](delivery/2026-11-28-identity-social-followup.md)，入口`.artifacts/windows-r47/Play-R47.exe`。签名热更兼容R37，无强更；按用户明确要求本轮未运行回归，修后实机和视听由用户验收。R46的1080p/4K证据仅证明此前版本；[R46历史](delivery/2026-11-28-home-social-visual-fix.md)保留。
- R41打不开：[旧R36占端口、启动助手安全切换与实际恢复验证](delivery/2026-11-27-r41-startup-fix.md)。只更新本机助手，原游戏本体和签名内容不变。
- 邮件UI复核：[提示自动消失、角标、固定物品卡与共享确认框](delivery/2026-11-28-mail-ui-followup.md)。43功能/包证据保留，用户复核文字视觉不通过，44候选已修灰度/字重而尚待Native/分发；不要把历史图录当新增系统完成。
- 后续装备修订：[角标/按钮、累计次数、CP横板、固定卡与英雄总增加](delivery/2026-11-26-equipment-followup.md)。本轮源码/Editor通过与签名包/原生/分发状态分别记录。
- 角色战斗力：[历史公式、官方变更、原包系数与复算边界](development/combat-power.md)。CP 与装备分数、实际战斗强弱分别讨论。
- 主页与竞技场：[启动、房间与聊天](development/arena-lobby.md)、[Arena](../Assets/Game/Modules/Arena/README.md)。
- 主页卡与公共频道：[原录像、最小素材和还原边界](development/home-profile-chat-reference.md)、[11/28资料/频道/召唤交付记录](delivery/2026-11-28-home-profile-chat.md)。44已签名打包，切服迁移待明确批准，现用43保留；Native与视觉缺口未完成。原版/修前/Editor候选、文件位置与未执行项分列，未录到的小卡子页按用户要求不做。
- R46最终视觉修订：[提示/主页右灰/收藏/透明聊天与共享奖励粒子](delivery/2026-11-28-home-social-visual-fix.md)。原版、44修前、45候选与46原生状态分列；最终客户端d312f43b和复用后端R44分别固定来源，保留45发现的游客并发缺陷及46先红后绿，不重标旧截图。
- 操作留痕：[规则和接口](development/operation-audit.md)、[窗口与交互 ADR](architecture/adr/016-window-feedback-operation-audit.md)。
- 配表：[Config README](../Config/README.md)、[配置与内容更新](architecture/content.md)。
- 热更/强更：[构建操作](development/hot-update.md)、[更新能力核对](delivery/2026-11-12-foundation-reaudit.md)、[HybridCLR 决策](architecture/adr/007-hybridclr.md)。
- 服务端/数据库：[开发运维](development/server.md)、[服务端架构](architecture/server.md)。
- 角色资源：[角色实验室](development/character-lab.md)、[通用导入规则](development/character-asset-import.md)、[CharacterPresentation](../Assets/Game/Modules/CharacterPresentation/README.md)、[CharacterLab](../Assets/Game/Modules/CharacterLab/README.md)。
- 技能演出编辑器：[操作、时钟与覆盖发布](development/battle-skill-timeline-editor.md)、[前十名技能演出基线](delivery/2026-11-14-skill-presentation-first-ten.md)。
- 八英雄战斗场景：[换人、通用 Prefab 与跟随回归](delivery/2026-11-13-eight-hero-battle.md)。
- 十名角色：[操作与验收](delivery/2026-11-13-ten-hero-roster.md)、[源图和资源审计](delivery/2026-11-13-ten-hero-source-audit.md)。
- S1/S3当前修复：[镜头事件窗口、目标身体和信息条](delivery/2026-11-13-camera-visibility-fixes.md)、[原作参考范围](delivery/2026-11-13-camera-visibility-reference.md)。
- 头顶状态与护盾：[排序、资源、事件和画面验证](delivery/2026-11-13-battle-status-shield.md)、[原版六层护盾和底板校正](delivery/2026-11-13-battle-shield-refinement.md)。
- 血条与死亡：[跟随/分格/损血/消散及验收](delivery/2026-11-13-health-death-presentation.md)、[近期原作画面证据](delivery/2026-11-13-health-reference-audit.md)。
- 战斗表现修复：[信息条、速度线与待机衔接](delivery/2026-11-13-battle-presentation-fixes.md)、[BGHATCH 纹理编码审计](delivery/2026-11-13-hatch-alpha-audit.md)。
- 战斗性能：[测量与操作](delivery/2026-11-13-battle-performance.md)、[缓存和诊断边界](architecture/adr/013-battle-performance.md)。
- 本机磁盘与缓存：[D盘资料与启动规则](development/local-storage.md)。
- 战斗演出通用规则：[源数据、镜头与飘字](development/battle-source-presentation.md)、[逐角色的帧与镜头验收](development/battle-source-presentation.md#逐角色的帧与镜头验收)、[第 61–70 名角色交付](delivery/2026-11-20-roster61-70.md)、[第 51–60 名角色实测](delivery/2026-11-20-roster51-60.md)、[第 41–50 名角色实测](delivery/2026-11-20-roster41-50.md)、[第 31–40 名角色实测](delivery/2026-11-20-roster31-40.md)、[时钟与缓存决策](architecture/adr/012-battle-presentation-clock.md)、[安洁莉卡录像复核](delivery/2026-11-13-angelica-timing-reference.md)、[时序与试玩验收](delivery/2026-11-13-battle-timing.md)。
- 战斗：[场景模块](../Assets/Game/Modules/BattleScene/README.md)、[原作研究和截图](development/epic-seven-battle-reference.md)、[角色配表](development/battle-character-data.md)、[等级面板与战斗单位](development/battle-growth.md)、[战斗第三步与双人验证](development/battle-execution.md)、[场景验收](delivery/2026-11-12-battle-scene.md)。
- 最近发生了什么：[变更与交接日志](development/change-log.md)、[当前状态](project-status.md)。
- 版本固定来源：`Tools/dependencies.lock.json`、`Packages/manifest.json`/`packages-lock.json`、各 .NET `packages.lock.json`。

## 文档权威与维护

用户最新指令优先。根 AGENTS 规定工作约束；通用规范和相关 ADR 规定现行约定；模块 README 说明实际职责和接入方式。当前状态给出事实快照及未完成项。源码/配置与文档矛盾时核对证据并在同一任务修正，不能只选方便的一份。

`docs/plans` 是实施历史/待办，不是现行能力证明；`docs/delivery` 是特定时间和版本的证据，历史通过不能自动外推到新母包。旧计划中的 xLua、Lua Binder、开发分支规则均已被后来的 HybridCLR、C# Binder、main 规则替代。

只记录必要信息，不复制私钥、密码、访问令牌或生产连接串。`.artifacts` 证据不进 Git；文档必须保留运行命令、结果摘要、代码基线、路径和限制，缺少原始日志时明确写历史记录而非重验。
