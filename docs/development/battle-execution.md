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

Unity 进入战斗场景 Play 后，在 Game 画面右上方点击「开始试玩」。编辑器菜单 **Tools → 战斗场景 → 开始试玩** 可用于重开。点击技能和战场上的目标。敌方使用基础攻击并自动选最低生命比例。试玩阵容固定为四对四，开局我方 20 魂用于练习。命中反馈由权威事件驱动。演出能力与原作对照见下节。

## 战斗演出与 HUD

- 三名英雄的九个技能从既有导入清单读取 STG、CFX、Spine、粒子及角色动画 timeline。近战读取接近目标的 MOVE 与跳跃动画，移动角色的显示节点并在结束后回到原站位。范围技能的演出目标取自已接受的伤害／治疗事件。第一个可达的 HIT 推进本项目的数值反馈，后续源 HIT 可以播放特效。原版逐段数值、镜头、Cut-in、后处理、个别锚点及插值仍待原作逐帧复核。
- 日语语音和技能音效复用角色实验室所用的 FMOD 事件、随机日语 Audio Table 与五份本地 bank。STG 与 Spine 样本未找到独立音频标记，当前在动作开始提交事件，事件内部时序由 FMOD 保留。Windows 支持原生播放，其他目标缺少对应插件会记诊断并继续演出。现有 bank 合计约 215 MiB，正式客户端需要按平台做分包和内存预算。
- 行动条头像按权威行动值插值移动。沙漏弹出全场行动值、技能剩余回合和当前状态。冷却按原作教程图采用贴合图标的黑色遮罩和居中剩余回合，保留外框。伤害数字复用原版 damage.fnt 和描边图集。暴击用红色，重击放大，未命中附提示，治疗用绿色；浮字随时间上移、缩放并淡出。浮字不随被击倒单位隐藏。试玩史莱姆使用带来源记录的宠物头像代用。
- HUD 依据本地解包的 tutorial/help_contents_6_1_image.png、tutorial/help_contents_8_1_image.png、wnd/hud_skill.csb 与 wnd/hud_order.csb。原图哈希记录在 HUD 来源清单。行动条避让、详情版式、数字动效曲线以及未确认的位移坐标是本项目重建参数。[原作画面索引](../delivery/2026-11-12-battle-hud-fidelity.md) · [梅赛德丝技能画面对照](https://www.youtube.com/watch?v=qX0K4plyvl8&t=289s)

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

## 验证记录

- 本地 Foundation 检查通过。导表、生成访问器、协议和发布校验通过。服务端与共享规则自动化 56 项通过，MySQL 集成用例因未提供测试数据库而跳过。
- Unity 2022.3.62f3 EditMode 215 项通过。全量 PlayMode 18 项通过、1 项未启用角色实验室截帧用例。战斗 PlayMode 3 项均通过，验证八槽布局与资产、Burn 和技能选中、实际选目标并执行攻击，以及权威命令已应用后血条仍在 STG 命中时点才变化。
- 本轮演出回归通过 Unity EditMode 215 项和 PlayMode 19 项，1 项角色实验室截帧跳过。战斗 4 项覆盖原布局、Burn、实际点击开局和沙漏、原版 CFX 和 FMOD 播放、冷却遮罩、飘字位移，以及拉斯接近目标与精确归位。Windows 原生 FMOD 探针解析 18 个事件，九个日语事件均触发日语音频回调。
- 本地 Kestrel 双人实测完整对局，47 条有效指令至胜负，最后事件序号为 288。验证跨座位代发被拒绝、命令重发幂等和事件游标补读。
- Windows 和 Android Player 构建、本场景正式发布链和原作逐帧对照留待后续验收。
