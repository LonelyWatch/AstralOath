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

Unity 进入场景 Play 后，点击 **Tools → 战斗场景 → 开始试玩**。点击技能和战场上的目标。敌方使用基础攻击并自动选最低生命比例。试玩阵容固定为四对四，开局我方 20 魂用于练习。命中反馈由权威事件驱动。当前从导入的 STG 读取主技能动画和可达的首个 HIT 延时，未解析的连线采用演示时点。多段打击、原版镜头、完整粒子、Cut-in 与音频的逐段验收仍沿用演出工作流。

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
- 本地 Kestrel 双人实测完整对局，47 条有效指令至胜负，最后事件序号为 288。验证跨座位代发被拒绝、命令重发幂等和事件游标补读。
- Windows 和 Android Player 构建、本场景正式发布链和原作逐帧对照留待后续验收。
