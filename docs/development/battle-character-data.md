# 战斗角色数据与来源

## 维护入口

当前战斗目录包含十名普通形态英雄，按 E7 ID 递增。初始配置采用 **Lv60、六星、六觉醒、技能 +0**，不叠加装备、神器、刻印、专属装备、转职和剧情强化。角色的初始星级与这一面板档位分别保存。此档位用作验收锚点。任意合法等级、星级和觉醒档由共享公式计算，详细口径与证据见[等级面板与战斗单位](battle-growth.md)。

- `Config/Tables/battle_heroes.csv` 管理英雄的身份、属性、职业、星座、成长模板引用、验收面板和个人资源。名称键复用 CharacterLab 源表。
- `Config/Tables/battle_growth_presets.csv` 维护可复用的成长系数，`battle_awakening_steps.csv` 按英雄记录各阶觉醒增量。
- `Config/Tables/battle_skills.csv` 管理 30 个技能槽的目标类型、冷却、魂力、魂燃、个人资源获取／消耗与参考系数。技能 ID 为 `角色ID.s槽位`。`presentationId` 与本地导入的 STG 对应，`iconAsset` 为 Unity 源资源路径。
- `Config/Tables/battle_skill_effects.csv` 定义有序技能行为与阶段，供共享规则内核执行。见[战斗第三步](battle-execution.md)。
- `Config/Tables/battle_skill_enhancements.csv` 管理 146 段已接入的技能强化的战斗属性增量。正的 `cooldown_turns` 表示减少的回合数；强化材料与费用属于成长系统。
- `Config/Tables/battle_messages.csv` 管理技能的中文工作译名与效果摘要。战斗逻辑应消费经核实的类型和参数，技能说明供人阅读。

`Bp` 后缀使用万分比，10000 表示 100%。例如攻击倍率 0.9 写 9000，暴伤 150% 写 15000。技能 `powerBp` 是独立的技能系数；伤害公式中的全局常数、结算顺序、命中、抗性、取整与伤害修正另行验证。CSV 中可空倍率明确写 `null`。锁定版本的 Luban 会把空 CSV 数值单元格读作 0，导表拒绝把 0 当成一个已填写系数。安洁莉卡 S2/S3 的攻击倍率与 Power 不适用。

在仓库根运行 `& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient`。客户端快照写入 `Assets/Game/Modules/BattleScene/Configs/battle-catalog.json`，包含 10 英雄、9 成长模板、60 觉醒节点、30 技能槽（26 个可手动施放）、74 条技能效果、146 条技能强化和 141 条文案（含 10 个复用角色名）。同次生成 `.artifacts/config/json`、`bin` 与 `csharp`；C# 访问器已在 .NET 中逐字段读回二进制。客户端快照和 .NET 产物都由同一批 CSV 生成。试玩场景和开发服务使用同一份生成快照。正式发布时绑定所使用的内容版本。请勿手改生成文件。

## 十人目录扩充（2026-11-13）

保留拉斯、梅赛德丝和安洁莉卡，按普通可编入角色的最小未收录 ID 增加七人。`c0001` 与 `c0002` 是官方索引中同名的梅赛德丝记录，普通可编入条目使用 `c1005`。新增名单与 Lv60 六星六觉醒的攻、血、防、速如下。角色身份和中文名来自 Smilegate 的 zh-CN 记录。面板锚点由公开资料的 Awakened Stats 栏、各阶觉醒和同口径成长系数交叉核对。 [官方角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json) [成长系数](https://gacha-data.com/epic-seven/stat_calc/)

- `c1002` 赛西莉亚，821 / 6751 / 648 / 110。 [面板](https://epic7x.com/character/cecilia/)
- `c1003` 萝季，821 / 6266 / 703 / 97。 [面板](https://epic7x.com/character/rose/)
- `c1004` 席可，1188 / 4693 / 518 / 121。 [面板](https://epic7x.com/character/silk/)
- `c1006` 绮世，1283 / 5138 / 522 / 116。 [面板](https://epic7x.com/character/kise/)
- `c1007` 维德瑞，1283 / 5138 / 522 / 116。与绮世共用狮子座五星盗贼模板，原模型和演出分别使用 `wildred` 和 `c_wilderd_skill_*` 拼写。 [面板](https://epic7x.com/character/vildred/)
- `c1008` 亚敏，721 / 6189 / 785 / 91。原模型和演出分别使用 `arming` 和 `c_aming_skill_*` 拼写。 [面板](https://epic7x.com/character/armin/)
- `c1009` 夏绿蒂，1134 / 5825 / 662 / 99。S3 消耗 100 点斗志，使用源 `c_charlotte_skill_03_05`。 [面板](https://epic7x.com/character/charlotte/)

技能表采用满觉醒。赛西莉亚 S2 的降攻在 +0 时为 85%，满强化为 100%，驱散独立于降攻检定。资料中的低血量增率是未觉醒分支。萝季 S3 提供全队攻击与自身防御，席可 S3 提升全体友军速度，绮世 S3 执行两次冷却增加检定。低觉醒版本的技能行为尚无单独表项。技能数值和强化主要参考 GachaData/Epic7Tools 的同期技能页。绮世的燃魂按移至 S2 的版本。夏绿蒂 S3 的 Power 在 2026-03 调查与现有技能表中分别为 1.0 和 0.9，项目保留 0.9，待同版复验。 [赛西莉亚觉醒档位](https://www.gacha.gg/epic-seven/characters/cecilia) [技能表](https://gacha-data.com/epic-seven/skills/) [技能强化与觉醒](https://epic7tools.dev/tools/skill-multipliers/) [2026-03-12 数值调查](https://www.reddit.com/r/EpicSeven/comments/1rrsw92/datamine_20260312/)

`casterDefenseDamageBp` 为防御乘数，`casterSpeedDamagePpm` 为每点速度乘以百万分比的伤害增幅，燃魂可覆盖速度系数。`casterHealthRatioDamageBp` 为满生命时的最大额外比例。防御穿透、必暴、增益条件群攻、回合外群攻以字段表示，必暴仍经过未命中判定。屏障分别支持最大生命、攻击、防御来源，同一技能只能选择一个来源。萝季和亚敏屏障暂用资料所列的 100% 和 60% 防御。赛西莉亚 S3 和绮世 S3 的精确屏障系数缺少已核对的同版数值，试玩暂取 15% 最大生命、100% 攻击。净化与驱散保持独立操作，降低行动值与冷却增加检查命中、免疫及抗性，行动值与魂力强化分别作用于自身的行为。 [萝季倍率](https://epic7x.com/character/rose/) [亚敏倍率](https://epic7x.com/character/armin/)

## 十人战斗机制（2026-11-14）

席可、维德瑞、亚敏、夏绿蒂的 S2 使用 `target=passive`。效果表按阶段触发，权威内核拒绝对这些槽直接下达施放指令。

- **席可** 自身回合开始获得 1 点专注，受击时有专注则消耗 1 点、净化自身一个减益并提高行动值 15%。至少 2 点专注时，S1 提高伤害、穿透 20% 防御，保留基本技能效果。当前以 +3333 Bp 近似由 0.9 到 1.2 的攻击倍率变化，与速度加成叠加时的精确系数待实测。S1 消耗 20 魂获得额外回合。资源库中的 `c_silk_skill_02` 使用独立动画、镜头和声音事件，自动射击的演出选择待与原作同条件实录核对。 [技能行为](https://www.gacha.gg/epic-seven/characters/silk) [数值调查](https://www.reddit.com/r/EpicSeven/comments/15h7dne/)
- **维德瑞** 剑舞采用 2026-03-12 调整版本。同队角色攻击后有存活受击目标生命不超过 50%，且自身可行动、被动已就绪时，先获得攻击提升 2 回合，再发动群攻并提高自身行动值 20%。被动冷却 2 回合，伤害随速度提高。当前将同队角色解释为包括维德瑞本人，待原作实机复验自身攻击的触发。 [官方更新预告](https://page.onstove.com/epicseven/global/view/12254577?boardKey=987&combined=false) [日文调整正文](https://game8.jp/epic-seven/765944) [数值调查](https://www.reddit.com/r/EpicSeven/comments/1rrsw92/datamine_20260312/)
- **亚敏** 满觉醒 +0 的受击反击概率为 20%，满强化为 25%。友方承受单体攻击时降低 30% 伤害，同类被动减伤取最高值。回合外的基本技能使用群攻目标。 [技能行为](https://www.gacha.gg/epic-seven/characters/armin) [强化数据](https://epic7x.com/character/armin/)
- **夏绿蒂** 在开战和自身回合结束时取得 1 回合屏障，屏障为自身攻击力的 50%，满强化为 75%。自身有增益时造成伤害提高 50%，承受攻击伤害降低 50%。S3 消耗 100 斗志，燃烧 10 魂在施放后回满斗志。 [日文调整正文](https://game8.jp/epic-seven/765944) [技能行为](https://epic7tools.dev/heroes/charlotte/) [数值调查](https://www.reddit.com/r/EpicSeven/comments/1rrsw92/datamine_20260312/)
- **挑衅与潜行** 萝季 S1 在生命至少一半时有 50% 基础概率挑衅 1 回合，低于一半时为自己加防。赛西莉亚 S3 有 85% 基础概率挑衅 1 回合。挑衅使单位以基本技能攻击仍存活且有效的施加者。潜行使单位在有其他可选敌人时避开单体主动选中，承受全体攻击时减伤，受到生命伤害后解除。绮世 S2 继续读取自身潜行，给予额外穿透。 [萝季技能](https://epic7tools.dev/heroes/rose/) [赛西莉亚技能](https://game8.jp/epic-seven/304274) [状态参考](https://www.gacha.gg/epic-seven/mechanics)

个人资源的字段及范围见[配置源表](../../Config/README.md)。r4262 的 `character_player.db` 标记席可为 `cp`、夏绿蒂为 `bp`。日文 `text.db` 的 `sk_c1004_2_de` 确认专注的回合开始及受击触发，专注上限为 5。`skill_player.db` 的 `sk_c1009_1` 和 `sk_c1009_1_ss_1` 均为 25 点斗志，`sk_c1009_1_ss_2` 为 50 点，`sk_c1009_3` 消耗 100 点。其他当前目录英雄没有个人资源栏。

`BattlePreviewFactory.Create` 可分别接收四个合法的 `allyHeroIds` 和 `enemyHeroIds`，按传入顺序填站位并允许重复。战斗场景显式指定双方英雄。工厂省略名单时使用 `DefaultHeroIds`，且敌方未指定时按单人四只史莱姆或双人镜像场创建。服务器与客户端应使用同一内容快照。新字段由 HybridCLR 战斗程序集读取，服务端相同源码随服务端独立更新。

Luban 生成已验证 JSON、二进制与 C# 访问器逐字段读回、图标与演出引用，以及十人完整面板。技能规则、状态交互与 Unity 演出分别验收，结果见[战斗第三步](battle-execution.md)。

## 首批三人身份和面板的证据

Smilegate 公布的角色索引确认了 `c1001`、`c1005`、`c1062` 三个 ID。转职拉斯、暗／光安洁莉卡及月光梅赛德丝属于独立角色。原始模型与 STG 也按这三个 ID 核对。[官方角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json)

满觉醒裸面板从社区资料的对应列一次性抄录。攻击／生命／防御／速度分别如下。

- 拉斯 `c1001` 758／5826／672／95，暴击 15%、暴伤 150%、抗性 12%。[角色面板](https://epic7x.com/character/ras/)
- 梅赛德丝 `c1005` 1187／4491／627／101，暴击 27%、暴伤 160%。[角色面板](https://epic7x.com/character/mercedes/)
- 安洁莉卡 `c1062` 576／5700／743／98，暴击 15%、暴伤 150%。[角色面板](https://epic7x.com/character/angelica/)

本地 r4262 的 `character_player.db` 和 `skill_player.db` 已逐项核对本文所述的个人资源标记及数值。面板、完整技能倍率和强化参数仍采用上述来源，尚待同版逐项复验。本轮只读核对来源；导表无需 `D:/sevenepic` 或访问外服。

## 技能版本和下一步复验

- **拉斯**采用 2025-02-13 调整后普通形态。S3 为群攻后给全体友军加防 2 回合；旧普通拉斯的自疗、转职拉斯的不同规则不计入。S1/S3 的参考生命系数分别 8% 和 12%，S2 的魂燃是 10 魂减自身技能冷却 2 回合。[官方公告](https://page.onstove.com/epicseven/global/view/10595085) · [可读公告镜像](https://epic7db.com/news/213-thu-update-content) · [倍率调查](https://www.reddit.com/r/EpicSeven/comments/1ioyg20/) · [技能资料](https://epic7tools.dev/heroes/ras/)
- **梅赛德丝**采用 2024-07-18 调整后的标准技能口径，符文觉醒已完成，剧情强化状态单独留待核实。S2 55% 是 +0，+2、+4 两段共加 15 个百分点后为 70%；该技能没有旧版 10 魂增伤。魂燃在 S3，消耗 10 魂延长本技能赋予的增益 1 回合。倍率使用该次调整的公开调查值；S2 追加同技能的触发门禁需要实战复验。S2 的模型动画名为 `skill1`，演出仍通过独立 `c_mercedes_skill_02` 图选择。[官方公告](https://page.onstove.com/epicseven/global/view/10317033) · [可读公告镜像](https://epic7db.com/news/718-thu-update-content) · [倍率调查](https://www.reddit.com/r/EpicSeven/comments/1e6bo8c/) · [技能口径](https://www.gacha.gg/epic-seven/characters/mercedes)
- **安洁莉卡**S1 +0 为 20% 概率眩晕 1 回合，+3 后为 25%。S2 治疗选定友军和最低生命友军；S3 全体治疗、屏障与免疫，20 魂将技能赋予的增益延长 2 回合。S2 治疗 20%、S3 治疗与屏障各 15% 的施术者最大生命系数来自社区参考，纳入配置时仍待原作定量复验。最低生命的判定口径、重复目标、同值顺序、抗性和持续回合边界由第三步验证。[技能与强化](https://epic7tools.dev/heroes/angelica/) · [治疗系数参考](https://epic7x.com/character/angelica/)

官方两个更新页面本次只返回脚本外壳；版本差异使用公告镜像正文交叉核对。全局属性克制、伤害结算、暴击、驱散顺序、夹攻、追加攻击和技能动画都应有独立验收，STG 事件和动画次数不决定权威结果。


## 首次角色配表验收

- Luban JSON、二进制、C# 访问器和客户端快照生成通过。读回逐字段核对，英雄、文案、技能和强化引用均可解析。
- 改表回归验证攻击变更透传、快照与生成结果一致，错误技能演出、重复技能槽与空系数被拒绝。拒绝后保留已发布快照。
- 地基总检查通过。服务端 10 通过、1 项真实 MySQL 集成跳过；Unity 2022.3.62f3 隔离工程编译与战斗 JSON TextAsset 导入通过。
