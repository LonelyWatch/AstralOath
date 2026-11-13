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
- 本地源转换与输出哈希校验通过。全局清单 945→1034，新增 89 个输出地址，既有条目无变化。HUD 63/63 核对通过，含 5 张新增原版图。转换器测试 50/50，Luban 字段读回和裸面板一致。三项专属权威测试通过，包括觉醒前后、减益与增益对照、燃魂、行动值和 S1/S3 目标。全量 .NET 107 通过、1 项 MySQL 集成跳过。日语目录含 26 个键，无该角色待补语音键。原作实录逐帧对照、Android 真机和 Player 构建独立验收，尚未完成。

- Unity 2022.3.62f3 PlayMode 两项通过，覆盖战斗场景接入和三个演出图播放，以及角色实验室加载与切换。场景资源引用使用已校验的主工程 GUID。

## #12 卡琳 c1011 预检

已从只读 r4262 找到 `model/karin.scsp`、`portrait/c1011.scsp`、三份 `c_karin_skill` 源图及基础战斗 HUD。58 项依赖通过闭包检查，16 个 SCSP 和 13 个 SCT 可解码。该模型没有 timeline，使用 STG 和骨骼事件。S3 原图包含两段骨骼动画，第二段 `skill3_2` 在局部 433.3 ms 发出 `cutin`，基础 WebP 存在。图中有 13 个 HIT 节点，需要轨迹确定实际路径。日语目录发现 29 个键，缺 `voc_c1011_skill2_3`，其余表没有该键。没有导入或进行 Unity 验收。

下一次需要通用的暴击后条件与友方自身技能冷却减少操作。S1 暴击减少自身技能冷却 1 回合。S2 暴击附带 1 回合防御降低，第三阶觉醒另获额外回合，额外回合和减益抗性分别验证。S3 暴击增伤与 10 魂燃烧系数核对普通形态，另查是否仅有专属装备带来出血。 [卡琳技能和觉醒](https://epic7tools.dev/heroes/karin/)
