# 战斗角色数据与来源

## 维护入口

战斗第二步固定三名普通形态英雄。初始配置采用 **Lv60、六星、六觉醒、技能 +0**，不叠加装备、神器、刻印、专属装备、转职和剧情强化。角色的初始星级与这一面板档位分别保存。此档位用作验收锚点。任意合法等级、星级和觉醒档由共享公式计算，详细口径与证据见[等级面板与战斗单位](battle-growth.md)。

- `Config/Tables/battle_heroes.csv` 管理三人的身份、属性、职业、星座、成长模板引用和验收面板。名称键复用 CharacterLab 源表。
- `Config/Tables/battle_growth_presets.csv` 维护可复用的成长系数，`battle_awakening_steps.csv` 按英雄记录各阶觉醒增量。
- `Config/Tables/battle_skills.csv` 管理九个主动技能的目标类型、冷却、魂力、魂燃与参考系数。技能 ID 为 `角色ID.s槽位`。`presentationId` 与本地导入的 STG 对应，`iconAsset` 为 Unity 源资源路径。
- `Config/Tables/battle_skill_enhancements.csv` 管理 45 段 +1 至 +5 的战斗属性增量。正的 `cooldown_turns` 表示减少的回合数；强化材料与费用属于成长系统。
- `Config/Tables/battle_messages.csv` 管理九个技能的中文工作译名与效果摘要。战斗逻辑应消费经核实的类型和参数，技能说明供人阅读。

`Bp` 后缀使用万分比，10000 表示 100%。例如攻击倍率 0.9 写 9000，暴伤 150% 写 15000。技能 `powerBp` 是独立的技能系数；伤害公式中的全局常数、结算顺序、命中、抗性、取整与伤害修正另行验证。CSV 中可空倍率明确写 `null`。锁定版本的 Luban 会把空 CSV 数值单元格读作 0，导表拒绝把 0 当成一个已填写系数。安洁莉卡 S2/S3 的攻击倍率与 Power 不适用。

在仓库根运行 `& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient`。客户端快照写入 `Assets/Game/Modules/BattleScene/Configs/battle-catalog.json`，包含 3 英雄、3 成长模板、18 觉醒节点、9 技能、45 强化和 21 条文案（18 条技能文案加 3 个复用角色名）。同次生成 `.artifacts/config/json`、`bin` 与 `csharp`；C# 访问器已在 .NET 中逐字段读回二进制。客户端快照和 .NET 产物都由同一批 CSV 生成。第三步再接场景和权威服务，正式发布时绑定所使用的内容版本。请勿手改生成文件。

## 身份和面板的证据

Smilegate 公布的角色索引确认了 `c1001`、`c1005`、`c1062` 三个 ID。转职拉斯、暗／光安洁莉卡及月光梅赛德丝属于独立角色。原始模型与 STG 也按这三个 ID 核对。[官方角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json)

满觉醒裸面板从社区资料的对应列一次性抄录。攻击／生命／防御／速度分别如下。

- 拉斯 `c1001` 758／5826／672／95，暴击 15%、暴伤 150%、抗性 12%。[角色面板](https://epic7x.com/character/ras/)
- 梅赛德丝 `c1005` 1187／4491／627／101，暴击 27%、暴伤 160%。[角色面板](https://epic7x.com/character/mercedes/)
- 安洁莉卡 `c1062` 576／5700／743／98，暴击 15%、暴伤 150%。[角色面板](https://epic7x.com/character/angelica/)

本地 `character_player.db`、`skill_player.db`、`skillset_player.db`、`skill_upgrade.db` 等原始二进制可定位，尚无经过验证的数值读取器。上述公开面板和倍率未证实与本地资源包完全同版。本轮只读核对来源；导表无需 `D:/sevenepic` 或访问外服。

## 技能版本和下一步复验

- **拉斯**采用 2025-02-13 调整后普通形态。S3 为群攻后给全体友军加防 2 回合；旧普通拉斯的自疗、转职拉斯的不同规则不计入。S1/S3 的参考生命系数分别 8% 和 12%，S2 的魂燃是 10 魂减自身技能冷却 2 回合。[官方公告](https://page.onstove.com/epicseven/global/view/10595085) · [可读公告镜像](https://epic7db.com/news/213-thu-update-content) · [倍率调查](https://www.reddit.com/r/EpicSeven/comments/1ioyg20/) · [技能资料](https://epic7tools.dev/heroes/ras/)
- **梅赛德丝**采用 2024-07-18 调整后的标准技能口径，符文觉醒已完成，剧情强化状态单独留待核实。S2 55% 是 +0，+2、+4 两段共加 15 个百分点后为 70%；该技能没有旧版 10 魂增伤。魂燃在 S3，消耗 10 魂延长本技能赋予的增益 1 回合。倍率使用该次调整的公开调查值；S2 追加同技能的触发门禁需要实战复验。S2 的模型动画名为 `skill1`，演出仍通过独立 `c_mercedes_skill_02` 图选择。[官方公告](https://page.onstove.com/epicseven/global/view/10317033) · [可读公告镜像](https://epic7db.com/news/718-thu-update-content) · [倍率调查](https://www.reddit.com/r/EpicSeven/comments/1e6bo8c/) · [技能口径](https://www.gacha.gg/epic-seven/characters/mercedes)
- **安洁莉卡**S1 +0 为 20% 概率眩晕 1 回合，+3 后为 25%。S2 治疗选定友军和最低生命友军；S3 全体治疗、屏障与免疫，20 魂将技能赋予的增益延长 2 回合。S2 治疗 20%、S3 治疗与屏障各 15% 的施术者最大生命系数来自社区参考，纳入配置时仍待原作定量复验。最低生命的判定口径、重复目标、同值顺序、抗性和持续回合边界由第三步验证。[技能与强化](https://epic7tools.dev/heroes/angelica/) · [治疗系数参考](https://epic7x.com/character/angelica/)

官方两个更新页面本次只返回脚本外壳；版本差异使用公告镜像正文交叉核对。全局属性克制、伤害结算、暴击、驱散顺序、夹攻、追加攻击和技能动画都应有独立验收，STG 事件和动画次数不决定权威结果。


## 首次角色配表验收

- Luban JSON、二进制、C# 访问器和客户端快照生成通过。读回逐字段核对，英雄、文案、技能和强化引用均可解析。
- 改表回归验证攻击变更透传、快照与生成结果一致，错误技能演出、重复技能槽与空系数被拒绝。拒绝后保留已发布快照。
- 地基总检查通过。服务端 10 通过、1 项真实 MySQL 集成跳过；Unity 2022.3.62f3 隔离工程编译与战斗 JSON TextAsset 导入通过。
