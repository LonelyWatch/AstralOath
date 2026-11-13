# 战斗第三步

## 原作规则核对

- 开场行动条每个单位在 0–5% 范围有随机初值，行动条到 100% 可行动，多个单位超过 100% 时优先超出最多者。每次正常行动重置该单位行动条。技能冷却在使用者回合结束递减。原作完全同值的决胜规则与开场随机精度待复测。项目以固定槽位作为最后决胜条件，用万分比和整数速度推进。[官方开场修正](https://page.onstove.com/epicseven/global/view/2913696) · [游戏内帮助转录](https://www.gacha.gg/epic-seven/mechanics)
- 火克木、木克水、水克火，光暗互克。优势加 15 个百分点暴击率并给 10% 伤害修正，劣势增加 50 个百分点的未命中机会。先判未命中，再判暴击，剩余命中有 30% 重击机会。未命中伤害乘 0.75，重击乘 1.3。敌方附加效果先过技能发动概率，再过 `clamp(1 + 效果命中 - 效果抗性, 0, 0.85)`。免疫阻止负面状态，驱散走单独分支。[官方战斗说明](https://page.onstove.com/epicseven/global/main/view/4097326) · [说明转录](https://epic7x.com/10-2-patch-desert-jewel-basar-is-here/) · [官方绝对抵抗说明](https://page.onstove.com/epicseven/global/view/7120355)
- 伤害参考公开计算器模型，用攻击倍率、最大生命加项、Power、1.871、技能强化、属性与命中修正，再除以 `1 + 防御/300`。本版在输出边界取整，个位边界仍待游戏样本校准。护盾先吸收伤害，剩余量扣生命。装备、神器、穿透、分摊与减伤组合尚未建模。[公开计算器实现](https://github.com/tyopoyt/e7calc-beta) · [伤害参数说明](https://epic7x.com/skill-multipliers/)
- 普攻夹攻基础概率的官方修正为 3%。项目把自动夹攻先用于单目标英雄普攻。拉斯 S2 明确选最高攻击的可用友军。反击、夹攻和追加攻击分别记录来源，次级动作不递归触发反击链。多候选抽样、多个反击源的全局顺序和同时多类反应待原作逐条复验。[官方夹攻修正](https://page.onstove.com/epicseven/global/view/8979711) · [官方反击说明](https://page.onstove.com/epicseven/global/main/view/3852405?listType=2)
- 拉斯、梅赛德丝、安洁莉卡的具体技能口径及更新来源见[角色数据](battle-character-data.md)。本版把普通梅赛德丝 S2 的攻击提升检查放在主攻击后，最多追加一次。安洁莉卡 S2 按当前生命百分比选最低友军，同目标去重。单次驱散按现有增益顺序取首个。以上细节由测试固定为当前项目规则，仍待实机校准。

## 技能程序与事件

`Config/Tables/battle_skill_effects.csv` 是技能行为编辑入口。按技能 ID 和行号排序。当前阶段为 `impact`、`after_hit`、`after_attack`，对应伤害或治疗、命中后效果、攻击后的自身／全队效果和次级动作。伤害、治疗、驱散、状态、行动条、夹攻和条件追加均有类型化入口。倍率、魂燃和强化沿用其他战斗表。导表会检查引用、阶段顺序和参数，生成客户端快照和服务端嵌入文件。

技能事件从主动作开始，依次发布魂力、冷却、伤害、护盾吸收、生命变化、状态、次级动作和击倒。对局执行回合结束、冷却递减、已有状态到期、行动条推进、眩晕跳过和胜负。受控客户端提交对局 ID、规则版本、命令序号、命令 ID、角色、技能、目标、是否魂燃。客户端以最终快照为准，按事件序号补播演出或对齐最新状态。[架构决策](../architecture/adr/011-battle-events.md)

Unity 进入战斗场景 Play 后自动试玩，四个敌方史莱姆显示真实60级。右上暂停提供继续游戏和重新开始；编辑器菜单 **Tools → 战斗场景 → 开始试玩** 仍可用于重开。点击技能和战场上的目标。燃魂锁定对应技能，其他技能置暗禁用，只能点击 Burn 位置的 Cancel 取消。敌方使用基础攻击并自动选最低生命比例。试玩阵容固定为四对四，开局我方 20 魂用于练习。命中反馈由权威事件驱动。演出能力与原作对照见下节。

## 战斗演出与 HUD

- 三名英雄的九个技能从既有导入清单读取 STG、CFX、Spine、粒子及角色动画 timeline。近战读取接近目标的 MOVE 与跳跃动画，移动角色的显示节点并在结束后回到原站位。范围技能的演出目标取自已接受的伤害／治疗事件。第一个可达的 HIT 推进本项目的数值反馈，后续源 HIT 可以播放特效。本轮已加入源镜头与 Cut-in，见下方修订记录。逐段数值、后处理、个别锚点及插值仍待原作逐帧复核。
- 日语语音和技能音效复用角色实验室所用的 FMOD 事件、随机日语 Audio Table 与五份本地 bank。STG 与 Spine 样本未找到独立音频标记，当前在动作开始提交事件，事件内部时序由 FMOD 保留。Windows 支持原生播放，其他目标缺少对应插件会记诊断并继续演出。现有 bank 合计约 215 MiB，正式客户端需要按平台做分包和内存预算。
- 行动条头像按权威行动值插值移动。沙漏展开左侧队列详情，包括原头像、行动值、生命、技能冷却和状态图标。冷却按原作教程图采用贴合图标的黑色遮罩和居中剩余回合，保留外框。伤害数字复用原版 damage.fnt 和描边图集。暴击用红色，重击放大，未命中附提示，治疗用绿色；浮字随时间上移、缩放并淡出。浮字不随被击倒单位隐藏。试玩史莱姆使用经原图与模型皮肤目检匹配的 m0001/2/3/5 头像；这属于本项目美术映射，原版怪物数据库尚未解码。
- HUD 依据本地解包的 tutorial/help_contents_6_1_image.png、tutorial/help_contents_8_1_image.png、wnd/hud_skill.csb 与 wnd/hud_order.csb。原图哈希记录在 HUD 来源清单。行动条已改为向下线性映射，详情采用本地原版 CSB 的行结构。数字动效曲线、精确头像重叠规则和未确认的位移坐标仍为本项目重建参数。[原作画面索引](../delivery/2026-11-12-battle-hud-fidelity.md) · [梅赛德丝技能画面对照](https://www.youtube.com/watch?v=qX0K4plyvl8&t=289s)

## 本地自动决策与快捷施放

`BattleAiPolicy` 从 Luban 技能、效果和英雄职业读取决策依据，共用权威解析器的可用性判断。返回技能 ID、目标 ID、燃魂标志，调用者通过正常 `BattleSkillCommand` 提交。策略不读随机流、不写战斗状态。服务器可复用，当前只在本地试玩连接了自动行动入口。

敌方回合和己方 Auto 选技及选目标。手动双击锁定玩家选定的技能、沿用已准备的燃魂状态，再代选目标。窗口用非缩放时间计量，切技能、暂停、燃魂切换、换回合、重开都会清掉上次点击。群体技能的代表目标只负责构图或选中，实际作用名单仍由权威解析器决定。原版资料与默认策略见[本轮交付](../delivery/2026-11-13-battle-ai-hud.md)。

## 单位个人资源

Luban 的英雄表定义 `resourceKind`、上限、回合开始获取和被攻击消耗，技能表定义获取及消耗。快照以 `BattleUnitSnapshot.Resource` 暴露类别、当前值、上限，`ResourceChanged.Amount` 是钳制后的绝对值。空资源表示单位没有个人槽。行动值与队伍魂力各自保留现有契约。技能消耗由共享解析器校验，选中按钮也用已接受快照中的资源值。

回合开始事件后增加该回合的个人资源。一次权威攻击针对同一目标至多触发一次被攻击消耗，即使源演出把该攻击分成多个 HIT。未达到消耗要求时拒绝指令，重发指令沿用原有幂等结果。[本轮范围与原始数据](../delivery/2026-11-13-battle-unit-feedback.md)。

## 服务端双人验证

在仓库根运行开发服务器。双人房间用同一份生成快照和同一个共享内核，阵容、规则版本和随机数由服务器生成。配置只在 loopback Development 生效。

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
& ./.tools/dotnet/dotnet.exe run --project Server/Host -- --BattlePreview:Enabled=true
```

- `POST /api/battle-preview/rooms` 返回房间 ID、配置 SHA-256、开局快照和两侧各自的令牌。每位客户端仅使用自己的 `Authorization: Bearer` 凭据。
- `POST /api/battle-preview/rooms/{id}/commands` 接收 `contentHash`、`rulesVersion`、`sequence`、`commandId`、`actorId`、`skillId`、`targetId` 和 `useSoulBurn`。以返回的快照和命令状态判定接受、重复或拒绝。重复成功请求返回原确认和事件。
- `GET /api/battle-preview/rooms/{id}?after={lastEventSequence}` 返回最新快照及后续事件。游标不在留存窗口时通过 `snapshotReset` 明示从快照恢复。房间存续一小时，最多 32 个。Unity 试玩使用本地内核，双人入口由 HTTP 客户端验证。

当前战斗内容范围为三名标准形态英雄的九技能、样例史莱姆与本地双人验证。真实 PvP 还需账号和匹配接入、行动超时与投降、BP、赛季规则、持久恢复与客户端联网画面。多波次、装备、神器、剧情强化和完整被动库也需要独立证据和回归。

## 前序交付验证记录

- 本地 Foundation 检查通过。导表、生成访问器、协议和发布校验通过。服务端与共享规则自动化 56 项通过，MySQL 集成用例因未提供测试数据库而跳过。
- Unity 2022.3.62f3 EditMode 215 项通过。全量 PlayMode 18 项通过、1 项未启用角色实验室截帧用例。战斗 PlayMode 3 项均通过，验证八槽布局与资产、Burn 和技能选中、实际选目标并执行攻击，以及权威命令已应用后血条仍在 STG 命中时点才变化。
- 本轮演出回归通过 Unity EditMode 215 项和 PlayMode 19 项，1 项角色实验室截帧跳过。战斗 4 项覆盖原布局、Burn、实际点击开局和沙漏、原版 CFX 和 FMOD 播放、冷却遮罩、飘字位移，以及拉斯接近目标与精确归位。Windows 原生 FMOD 探针解析 18 个事件，九个日语事件均触发日语音频回调。
- 本地 Kestrel 双人实测完整对局，47 条有效指令至胜负，最后事件序号为 288。验证跨座位代发被拒绝、命令重发幂等和事件游标补读。
- Windows 和 Android Player 构建、本场景正式发布链和原作逐帧对照留待后续验收。

## 2026-11-12 演出对照修订

- **回合与可见性**：显示跟随正在呈现的 Action.SourceId，不能直接把服务端已推进到下一回合的 Snapshot 当成当前画面。敌方行动隐藏整个 Skills 分组和 Burn，包含边框、选中、冷却层。源镜头演出收起技能与场上固定血条，保留顶栏、行动条及当前角色栏；动作完成、重开或卸载恢复原状态。Cut-in 置于 HUD 上方，保留原图透明合成。
- **攻击与受击音频**：Action 的 PresentationId 为空时回退到 SkillId，史莱姆使用原版 model/slime/ani/skill1。源骨骼 hit 与原 STG 均为0.7秒。已接受的 Damage 呈现时（包括护盾完整吸收、实际掉血为0的命中），PlayHit(targetDefinitionId, sourceSkillId)播放原版 hit_new_blunt_normal 和目标自己的日语 ani/attacked 事件。随机语音仍由 FMOD 原事件决定。新增 sound/gerneal.bank 约14.1 MiB，经 Git LFS 保存，其余五份 bank 复用既有角色资源。
- **源时间线**：从 ENTRY 而非第一个 ANI 开始遍历，保留开始/完成连线、延迟、骨骼事件及同一时刻的源拓扑顺序。零时长 CAM/ZOOM 立即完成，帧时间跨过多个 cue 时分段推进，避免低帧率把 Cut-in 和后继命中推迟到帧末。拉斯16帧 Cut-in 共1.25秒，安洁莉卡20帧共1.28秒；以原 descriptor 的每帧时长为准，等比铺满视口。
- **舞台**：CAM/ZOOM 使用源值和贝塞尔，DARK/BGSHOW/BGHATCH 控制原背景和三张原版背景纹理。BGHATCH shader 采样及混色公式从本地源 shader 移植。出现 LAYOUT 的特写按已接受动作的施法者和实际目标确定显示参与者，旁观单位及影子仅暂隐 Renderer；mode1、动作结束、中断、下一动作和释放恢复原 enabled，不移动逻辑站位或激活已击倒单位。源 away_x 超出整个源画幅时保留非施法者离屏阶段。此隔离规则由源指令和画面对照推导，有限 self_x/away_x 的精确坐标、淡入淡出和移动曲线仍未证实。
- **行动条与浮字**：0%在上、100%在下，当前行动者置于下端，超过100%的真实值仍在详情中显示。取消与存活人数相关的强制纵向间距。详情按本地原版全屏关闭层及左侧队列重建，显示角色、行动值、生命、三技能/冷却/沙漏、五类已支持状态原图；点击行保留面板，点击空白关闭。浮字从实际角色 Bounds 经当前镜头投影到 HUD，避免镜头移动时偏离目标。

本轮可实际查看的最近资料为2026-11-11发布的 RTA #901 时间轴帧。它没有显示客户端版本号，无法证明录制时是精确最新版。标题与画面年份冲突的赛事录像已排除。最近实帧未展示详情展开，详情的依据为本地 CSB，不能宣称已与最新实机逐像素一致。[检索及画面证据](../delivery/2026-11-12-battle-presentation-reference.md)

### 维护与复现

- BattleScene 的 scene-only host 序列化 impactBank、stageBackground、skillAssets 和 battleSprites。预制体仍只有通用 BindingMap/GameButton/GameText；业务不挂在预制体上。
- 运行 Tools/BattleScene/ImportBattleAudio.py --check、ImportReadinessHud.py --check、PreparePreviewArt.py 校验源字节。前两者去掉 --check 导入选定素材；后者 --restore 按固定源哈希恢复背景/字体/图标/演出背景，不重建已有 meta。
- 使用隔离工程执行 AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.InstallHudEffects 安装引用。核对已有场景对象 ID 与 meta GUID，再回传实际变更的场景及首次导入 meta，不能执行 Generate 覆盖已调整的作者场景。
- Windows 音频诊断运行 Tools/BattleScene/AudioProbe，实际生产代码＋原生 FMOD 使用 NOSOUND 输出到 DSP 测量，无需扬声器和服务器。它不代替实际听感和 Android 原生插件验证。
- 业务仍在现有 HybridCLR 模块；新增 shader 使用内置 Resources，本轮独立测试场景不代表已验证正式母包或远程内容包。正式发布需确保目标母包包含 shader 并验证资产收集。

### 尚未证明的原版细节

SHAKE、PPEFFECT、SINE_VALUE、部分 MOVE style2/3 与有限 LAYOUT 坐标仍以每技能/类型一次诊断报告。背景速度驱动、CAM锚点及状态详情的所有交互仍待原作连续录像对照；源 STOP 位于动作终点时的背景淡出尾段当前会被动作重置截断（例如梅赛德丝S1的200ms），尚未建模为独立的收尾阶段。当前三英雄样本不能外推全部 E7 角色，播放、编译和自动测试也不能替代还原度验收。

完整 Unity 回归和本轮检查结果见本次提交的变更日志。Player构建、Android设备、本场景正式更新发布、生产PvP联机不属于此次已验证结果。

## 2026-11-13 时钟与性能修订

当前Cut-in/夹攻/暂停与开战准备规则以[源演出规范](battle-source-presentation.md)和[ADR012](../architecture/adr/012-battle-presentation-clock.md)为准。前述历史表现差异保留来源，不代表本轮新规则已证明所有原版字段。新原始时序证据见[安洁莉卡S3](../delivery/2026-11-13-angelica-timing-reference.md)。
