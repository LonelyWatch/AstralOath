# 战斗角色从 10 名扩充到 100 名

## 顺序和计数

保留已接入的 c1001–c1009 与 c1062，按官方简中英雄索引中尚未接入的 `c1xxx` 实际条目递增。安洁莉卡 c1062 已占首批一个名额，到该 ID 时直接跳过。索引缺号、重名演出记录 c0001/c0002、NPC、换装和同英雄的演出变体不增加人数。以索引和现有资源的角色身份校验元素，光之瑞儿 c1022 等在此编号段的可用独立英雄照常纳入。按此口径 #11 杰拉图 c1010，#12 卡琳 c1011，#13 科尔布思 c1012，#100 札哈克 c1119。原始索引的本地快照位于 `.artifacts/roster-data/official.source`，SHA-256 为 `a37c7a85128deef78ab8865eec2e7da76f1992c79b571c3001eb13add4998580`。采用固定索引与本地 r4262 素材基线。 [Smilegate 角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json)

顺序检查确认后续 90 名候选的基础立绘 SCSP、atlas、SCT 和两类头像路径存在。技能图标以实际技能表选型，例如索尔 c1092 的第二槽为 `pa_c1092_2.png`。以上检查只覆盖根文件名，逐人确认战斗模型、正式 STG、分支、音频和闭包。

## 逐人交付门槛

1. **身份与来源。** 记录基础角色、源模型名、技能图名、源哈希、目标规则版本、参考素材及其日期、实录时间段和倍速。源程序的字段证据与实录证据分别标明。补录可编入的变体须明确单独编号或形态。
2. **资源闭包。** 在 `Tools/CharacterAssets/import_characters.py` 增加明确根资源与必要别名，先对该角色执行 `--characters <ID> --closure-only`，处理真实缺失或未识别的节点。写入主工程时用完整白名单生成目录和清单，避免单角色调用覆盖全量目录。源图和转换产物受哈希校验。创作性调参进入编辑器维护的覆写配置。通过转换器测试、`--verify` 和 HUD `--check`，核对既有地址、GUID 与字节哈希。
3. **权威配表。** 写角色及成长模板、觉醒、技能、效果、强化、本地化。优先复用通用条件与操作，新增机制必须有正反例。依次运行 `Tools/BattleScene/ImportRosterHud.py` 和 `Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient`。测试满觉醒面板、技能目标、命中／抗性、强化和魂燃、增减益时点以及无效指令。每个新技能至少检验一个真实命令或被动触发，并明确权威数值和演出 ID 的映射。
4. **Unity 与音频。** 刷新 CharacterLab 收集器及 BattleScene 角色引用，保留编辑者保存的阵容，完整验证场景使用生成目录。PlayMode 实测新角色入队、模型渲染、真实技能提交及各源图完整播放，验证 CUTIN 前中后、目标显隐、受击、收尾和暂停清理。用 FMOD 原生 AudioProbe 检查日语样本和 DSP 产声。按需使用 `ASTRAL_BATTLE_CAPTURE_DIR` 采样关键帧和源时间，分别检验单体、全体、反向站位和条件变化。原作还原以同形态同倍速对照确认。
5. **记录与发布。** 每个角色分别记录配表、原版对照、资源、FMOD、Editor 和 Player 的实际通过、失败、阻塞或未执行情况，再推进编号。重要共用规则写入项目文档。存放实录和测试产物用 D 盘 `.artifacts`。需要强更的 AOT 或原生变化另行构建和验证。

专项命令在主工程根目录执行：

```powershell
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py --characters c1010 --closure-only
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B -m unittest discover -s Tests/CharacterAssets
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py --verify
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/BattleScene/ImportRosterHud.py --check
& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Config/Generate.ps1 -VerifyAccessors -CheckClient
& ./.tools/dotnet/dotnet.exe test Server/Tests --no-restore --filter FullyQualifiedName~BattleZeratoTests
& ./.tools/dotnet/dotnet.exe run --project Tools/BattleScene/AudioProbe -- $PWD.Path 'D:/sevenepic/原始资源/sound' c_jerato_skill_01 c_jerato_skill_02 c_jerato_skill_03@1
```

## #11 杰拉图 c1010

- 普通四星冰属性魔导士、白羊座，满级满觉醒无装备攻 1159、血 4733、防 627、速 104，暴击 27%、暴伤 150%、效果命中 18%。S1 单体，40% 基础概率降速两回合。S2 单体，推进自身行动值 50%，第三阶觉醒后，对已有减益目标额外伤害 30%。10 魂燃烧把攻击倍率从 1.5 改为 2.0，Power 仍为 0.95。S3 群攻，每名目标分别进行基础 50% 眩晕一回合的检定。已同步配置 15 阶技能强化。简中技能名采用项目译名。 [成长系数](https://gacha-data.com/epic-seven/stat_calc/) · [技能与觉醒](https://epic7tools.dev/heroes/zerato/) · [倍率交叉核对](https://epic7x.com/character/zerato/)
- 骨骼是 `model/zerato.scsp`，STG 保留 `c_jerato_skill_01/02/03` 原拼写。S1 源图有 3 个 HIT 节点、3 个 CAM 和原生 SHAKE；S2 有 4 个 HIT，含进退、PPEFFECT 及编号 29 的事件监听；S3 有 14 个 HIT，含背景、腾空落地、两组方向模糊及镜头位移。节点总量包含分支，实际命中时点以运行轨迹为准。S3 原 STG 无 CUTIN 节点，但同名模型 skill3 在 1866.6 ms 发出 `cutin`，并有本形态 `cut/fhd/sk_c1010_3.webp`。转换时按骨骼事件登记附加 CUTIN，原 STG 保留。WebP 解码为 17 帧，1280×723，持续 1.27 秒。 [S3 独立立绘旁证](https://www.hideoutgacha.com/games/epic-seven/characters/zerato)
- 本地源转换与输出哈希校验通过。全局清单 945→1034，新增 89 个输出地址，既有条目无变化。HUD 63/63 核对通过，含 5 张新增原版图。转换器测试 50/50，Luban 字段读回和裸面板一致。三项专属权威测试通过，包括觉醒前后、减益与增益对照、燃魂、行动值和 S1/S3 目标。全量 .NET 107 通过、1 项 MySQL 集成跳过。日语目录含 26 个键，无该角色待补语音键。Windows 原生 FMOD 单独运行 24 次，三技能音效和日语事件均成功启动且 DSP 产声。S1、S2 各覆盖三个原声变体，S3 每次按原事件播放固定双段。Unity 模型入队、全源图播放和角色实验室目录回归通过。原作实录逐帧对照、Android 真机和 Player 构建独立验收，尚未完成。

- Unity 2022.3.62f3 PlayMode 两项通过，覆盖战斗场景接入和三个演出图播放，以及角色实验室加载与切换。场景资源引用使用已校验的主工程 GUID。

## #12 卡琳 c1011

- 普通四星冰属性刺客、狮子座，满级满觉醒无装备攻 1188、血 4855、防 508、速 113、暴击 23%。S1 暴击额外减少自身冷却 1 回合。S2 暴击使单个目标防御降低 1 回合，第三阶觉醒后另获额外回合。目标抵抗降防时仍会触发已满足条件的额外回合。S3 基础攻击倍率 1.6，10 魂燃烧后为 2.3，Power 0.85，暴击倍率额外加 50 个百分点。原版出血属于专属装备分支，普通形态不附加。简中技能名采用项目译名。 [技能与觉醒](https://epic7tools.dev/heroes/karin/) · [倍率及专属装备](https://epic7x.com/character/karin/)
- 依赖本地 `model/karin.scsp`、`portrait/c1011.scsp`、三份 `c_karin_skill` 源图。模型无 timeline，使用 STG 与骨骼事件。S3 原图包含两段骨骼动画，第二段 `skill3_2` 在局部 433.3 ms 发出 `cutin`。基础 WebP 解码为 17 帧，1280×723，持续 1.32 秒。附加立绘节点订阅该事件，保留源图已有同名事件分支。原始节点数为 S1 2 个 HIT、S2 4 个 HIT、S3 13 个 HIT，实际命中以运行分支和轨迹为准。
- 日语表可用 29 个键，源事件引用的 `voc_c1011_skill2_3` 在所有现有日语表中缺失。该取样按已有的缺键机制处理，保留其他录音。Windows FMOD 24 次原声播放退出 0，每次都启动技能音效与语音且有 DSP 输出。S1 观察到 4 套抽样，S2 观察到 3 套，第三套在缺键时仍播放两段现存日语，S3 观察到 2 套各两段。源截图同版逐帧复核和 Player 构建另行验收。
- 全局清单 1034→1105，新增 71 个输出地址，既有条目无变化。Luban 字段读回和裸面板一致。转换器测试 50/50、全量清单和 HUD 68/68 校验通过。专属权威测试 9/9，覆盖暴击／非暴击、觉醒前后、抗性、冷却和燃魂，服务端全量 116 通过、1 项 MySQL 集成跳过。
- Unity 战斗场景与角色实验室两个 PlayMode 专项均通过。卡琳 S3 的 MOVE(location=12, style=3, x=-400) 仍有源锚点告警，暂保留播放器诊断。补齐同版画面证据之前不计为原作表现一致。

卡琳专项用 `ASTRAL_BATTLE_HERO_ID=c1011` 运行，通过 S1 实际指令及三张图播放和场景恢复。下一名见[科尔布思预检](../delivery/2026-11-14-corvus-preflight.md)。百人阵容的打包与加载成本见[资源拆分提案](../architecture/adr/015-battle-roster-assets.md)。

## #13 科尔布思 c1012

- 普通四星火属性战士、巨蟹座，满级满觉醒无装备攻 903、血 6635、防 630、速 103。S1 按攻防计算伤害，命中后独立检定转移一项自身减益。不屈时提高伤害并检定一回合眩晕。S2 群攻后降低行动值，不屈使包含强化的减量加倍。S3 消耗 100 斗志，赋予自身三回合不屈并获得额外回合，第三阶觉醒后恢复生命，20 魂燃烧使治疗量翻倍。不屈提高防御和效果抗性且无法被驱散。该状态的图标来自原生 `buff/stic_indomitable.png`。权威规则版本 4，客户端与服务端需同步启用。 [来源与分支预检](../delivery/2026-11-14-corvus-preflight.md)
- 开局斗志 50，S1/S2 获得 10/30，存活且受击后每次攻击获得 10。减益转移使用当前最早的一项减益，成功后才移除施法者身上的减益。当前实现以行动值从正数减为零作为眩晕触发条件，并分别检定行动值和眩晕抗性。减益选择、嘲讽转移后来源、零行动值分支、斗志和治疗系数等待同版原作实机复核。禁强化仍属于共享战斗规则待接入项。
- 保留 `model/corvus.scsp`、`portrait/c1012.scsp` 和三张 `c_corvus_skill` 原图。S3 立绘由原 STG 的 CUTIN 节点直接驱动，WebP 为 19 帧，持续 1.30 秒。全局资源清单由 1105 增至 1173，新增 68 个地址，既有地址及哈希不变。转换器 54/54、全量源与输出清单、HUD 74/74 和 Luban 生成及读回通过。
- .NET 专属回归 12/12，覆盖开局及多段受击斗志、额外回合、目标限制、觉醒与魂燃治疗、强化后不屈减条、独立抗性、减益转移成功与抵抗、不可驱散。服务端全量 128 项通过，MySQL 集成跳过 1 项。Windows FMOD 24/24，三个技能各观察到三个日语取样，均实际有音效事件和 DSP 输出。Unity 战斗场景与角色实验室 PlayMode 2/2，覆盖 S1–S3 三图播放和收尾复位，技能时间轴 EditMode 9/9。
- S2 的 CFX 动作 `Fire` 未映射，S3 的第 15 号混色方式仍待比对。原作同版逐帧、Player 构建、Android 及实机复核未执行。

百人清单用 `Tools/CharacterAssets/ExportRosterQueue.py` 从固定官方索引和当前配表导出到 `.artifacts/roster-data/roster-100.json`。当前 17 人已入表、83 人待入表。状态仅反映配表，逐人通过及实机边界以本页为准。

## #14 卡尔图哈 c1013

- 普通四星木属性战士、巨蟹座，共用现有 `warrior_cancer_4` 曲线。满级满觉醒无装备攻 903、血 6635、防 630、速 103。S1 单体，按攻击和最大生命值计算伤害，35% 至 50% 概率降低防御两回合。S2 是受击被动，按单次攻击增加 10% 至 15% 行动值，受击结算后生命值不高于 50% 时额外增加 10%；现有原版 S2 演出图可在实验室和时间轴查看。S3 群体降攻、觉醒后先治疗自身、随后给予自身两回合防御提升，普通冷却五回合，可强化至四回合。10 魂魂燃同时提高攻击和最大生命倍率。 [技能表及强化](https://gacha-data.com/epic-seven/skills/) · [技能和觉醒资料](https://epic7db.com/heroes/cartuja)
- S3 伤害阈值取 2019 调整说明和倍率数据的严格低于 75%。不同资料对等于 75% 的边界、治疗补至 50% 或恢复最大生命的 50% 存在分歧。项目当前按严格低于 75%、第三阶觉醒后按自身最大生命值恢复 50% 实现；S2 按受击结算后判定。等待同版原作实战复核这些分支。 [2019 调整公告镜像](https://epic7x.com/12-13-hero-balances-kise-dingo-cartuja-and-apocalypse-ravi/) · [觉醒详情](https://epic7x.com/character/cartuja/) · [倍率数据](https://e7calc.xyz/)
- 读取 `model/kartua.scsp`、`model/kartua.timeline`、`portrait/c1013.scsp` 与 `c_cartuja_skill_01/02/03`。基础 S3 的 STG 无 CUTIN 节点。同形态骨骼 `skill3` 在 1766.6 ms 发出 `cutin`，导入器据此连接同形态 `sk_c1013_3.webp`，保留原 STG。原版 WebP 为 1280×723、18 帧、1.45 秒。等待同形态实录复核触发时机与镜头。
- 全局清单 1173→1236，新增 63 个输出地址，既有条目无变化。导表和成长裸面板、转换器 54/54、全量源与输出清单、HUD 79/79 均通过。.NET 专属 10/10，验证受击行动值每次攻击只结算一次、阈值及强化，禁止直接释放被动，S3 四种倍率组合及觉醒治疗顺序；全量服务端 138 项通过、1 项 MySQL 集成跳过。
- 日语目录有 25 个键，缺少原事件引用的 `voc_c1013_skill3_3`。Windows FMOD 三个源演出各播放八次，24/24 均启动原生特效音和语音，并检测到 DSP 输出。S1、S2、S3 分别观察到 3、2、3 套取样，S3 缺键的分支仍按顺序播放两段现有日语。原作同版逐帧、Player 构建和真机复核未执行。

- 规则版本 5。Unity 战斗场景和角色实验室 PlayMode 各通过一项，战斗场景覆盖入场、S1 权威指令和三个源图播放、收尾复位。S1 的 `jump_forward/landing` 缺失时沿用源动画结束；S3 LAYOUT 的排除目标、可见性掩码已播放，站位参数与淡出曲线仍待同形态实录验证。

## #15 席德 c1014

- 四星木属性刺客、白羊座。满级满觉醒裸装攻 1029、血 5097、防 473、速 126、暴击 23%。普通 S1 与提速后的强化普攻共用技能槽。规则版本 6 在动作开始时确定分支，使用对应倍率、降防、演出 ID 和图标。本次暴击新获提速从下一次攻击起影响分支。S2 为暴击被动，提速并检定一次随机强化；S3 根据觉醒及提速取得属性优势，击倒后获额外回合，10 魂燃烧增伤。随机强化候选权重暂按等权处理，同回合失败重试和回合外行为待原作复核。 [源图、数值与版本证据](../delivery/2026-11-14-cidd-preflight.md)
- 三张源图为 `c_cedars_skill_01_new`、`c_cedars_skill_02_new`、`c_cedars_skill_03_new`。第二张在时间轴标为「S1 · 提速」。S3 保留原生 CUTIN，源图仍监听骨骼未发出的 `skill3_1/cutin`，同时已有到达立绘的独立连线。源位移曲线、LAYOUT 参数与实机同版画面仍待对照。简中技能名为项目译名。
- 导表访问器、全量源与输出校验、转换器 57/57、HUD 85/85、状态图标 30/30。席德权威回归 12/12，服务端全量 150 项通过、1 项 MySQL 集成跳过。Unity 战斗场景通过入场、普通技能图标及已加载的替换图标、被动不可点、真实 S1 指令、三张源图和场景复位。角色实验室的加载、渲染、切换与关闭专项通过。时间轴 10/10，首次批处理在测试收尾的 Unity NewScene 回调原生退出，换独立镜像复跑通过。
- Windows 原生 FMOD 24/24，三张图各播放八次且均有 DSP 输出，观察到 3/3/2 套取样。S3 日语缺少 `voc_c1014_skill3_1_2` 和 `voc_c1014_skill3_2_2`，保留原声分支中现有片段。Windows / Android Player 与原作同版逐帧对照未执行。巴尔＆塞尚使用的三份有序位移轨已单独修复并回归。 [骨骼源证据](../delivery/2026-11-14-baal-duplicate-spine.md)

## #16 巴尔＆塞尚 c1015

- 五星火属性魔导士、双子座。满级满觉醒无装备攻 1197、血 4572、防 683、速 106、效果命中 30%。S1 攻击指定敌人与随机另一敌人，分别检定中毒，35% 基础概率，满强化 50%，持续两回合。S2 群攻，以每名目标自身的减益实例数计算伤害，每项提高 15%；本次攻击直接击倒任意敌人时仅重置 S3 冷却。S2 攻击倍率 1.1，10 魂燃烧后 1.35，Power 0.9。S3 群攻，各目标分别检定降速、禁疗和觉醒三阶后解锁的降防，持续两回合，基础 65%，满强化 85%。旧倍率页的 S2 0.85／1.1 和每项减益 10% 留作同版本对照。简中技能名为项目译名。 [官方角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json) · [技能与觉醒](https://epic7tools.dev/heroes/baal-sezan/) · [倍率数据](https://e7calc.xyz/)
- 权威规则版本 7。中毒使用原生 `buff/stic_dot.png`，每层保留独立来源、实例和回合数，在受影响角色的回合开始时按其最大生命值 5% 结算。客户端通过状态实例更新或移除单个图标，并独立呈现毒伤。项目沿用普通护盾吸收规则；Boss 百分比伤害免疫、状态总数上限和中毒与特殊免伤的原作边界仍待同版核验。 [中毒数值](https://www.gacha.gg/epic-seven/mechanics) · [状态图标映射](https://www.epic7helper.com/effects)
- 基础模型为 `model/baal_sezan.scsp`，三图为 `c_baal_skill_01/02/03`。S3 的源 STG 未附 CUTIN，按同形态骨骼 `skill3` 在 1166.6 ms 发出的 `cutin` 接入 `sk_c1015_3.webp`，21 帧、1280×723、1.59 秒。S2 两份特效及 S3 镜头的重复位移轨按源顺序独立求值，使用支持 `astraloath-scsp/4` 的 AOT 母包。 [骨骼源证据](../delivery/2026-11-14-baal-duplicate-spine.md)
- 资源清单 1279→1385，新增 106 个输出，既有条目无变化。转换器 57/57、原始与输出哈希、阵容 HUD 90/90、状态 HUD 31/31、Luban 生成和访问器读回通过。.NET 专属 10/10，服务端全量 160 项通过，1 项 MySQL 集成跳过。Windows 原生 FMOD 24/24，三个技能分别观察到 3、2、2 套日语取样，无缺键。Unity PlayMode 4/4，覆盖进阵、S1 权威指令、三个源图播放与复位、实验室、独立毒层图标和单次毒伤显示。
- Unity 诊断保留 S2 的 `locationParam=0` 在亚敏上找不到源锚点，当前使用角色原点。S3 的 LAYOUT 保留参与者显隐掩码，`self_x/away_x` 位置和淡出曲线未确认，MOVE 曲线仍为重建结果。原作同版逐帧、Player 构建和真机验证未执行。

## #17 优芬妮 c1016

- 五星木属性战士、金牛座。满级满觉醒无装备攻 1228、血 5784、防 553、速 109、暴击 23%、暴伤 165%。S1 单体降防。S2 群体攻击后每名目标先判定驱散一个强化，再独立检定降防和沉默。20 魂燃烧跳过效果抗性检定，命中、技能概率和仍存在的免疫继续生效。S3 先获得两回合攻击力提升，再结算单体伤害，直接击倒可获得额外回合，三阶觉醒后对已有减益目标增伤 50%。 [技能、面板和强化](https://epic7tools.dev/heroes/yufine/) · [平衡公告镜像](https://epic7x.com/12-19-patch-bystander-hwayoung-release-balance-changes/)
- 权威规则版本 8。S3 主动槽独立注册队友施法监听。本技能可用时，另一名己方角色主动技能结算后，只要仍存活的任一敌人带有减益，即推进一次 20% 行动值。含刚造成减益和支援技能；多目标仅触发一次，夹攻、反击及额外攻击不计为独立主动施法。该判定范围、非行动监听与眩晕的关系及触发时序等待同版本原作实机复核。 [规则与原始演出预检](../delivery/2026-11-14-yufine-preflight.md)
- 使用 `model/yupine.scsp` 和 `c_yupine_skill_01/02/03`。S3 原 STG 直接引用 CUTIN，1280×723、16 帧、890 ms。清单 1385→1454，新增 69 项，既有项无变化。导表读回和成长面板通过，转换器 61/61，HUD 95/95。.NET 专属 11/11，服务端全量 171 项通过，1 项 MySQL 集成跳过。Unity 战斗和实验室 PlayMode 2/2，含入阵、真实指令、三图播放与复位。
- Windows 原生 FMOD 24/24，S2 原事件的三句日语均经事件限定映射选中真实 `_j` 键，S3 保留双段组合。映射来源与独立试听见预检文档，使用新映射的热更包依赖已含对应 AOT 音频桥接的母包。
- Unity 保留 S1/S2 的 `skill1/hit` 与 `skill2/hit` 骨骼事件缺失诊断，S1/S3 使用重建 MOVE 曲线。S3 LAYOUT 的 `self_x/away_x` 及淡出仍待确认，EFFECT 55 暂回退主目标。原作同版逐帧、Player 构建和真机验证未执行。
