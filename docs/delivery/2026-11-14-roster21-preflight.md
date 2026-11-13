# 第 21 名角色修里接入预检

普通修里 `c1020` 是四星火属性射手、狮子座。基础模型为 `model/schuri.scsp`，立绘为 `portrait/c1020.scsp`。三张演出图用 `c_schuri_skill_01/02/03`。观察者修里和西风处刑者修里分别使用 `schuri_m`、`schuri_m2`，各自的技能与资源独立。预检以只读 `pack-15-r4262` 为素材基线。 [Smilegate 简中角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json) · [职业和星座](https://epic7tools.dev/heroes/schuri/)

## 规则与配表

2025-11-20 调整后的普通修里已移除 S1 中毒、S2 暴击获得专注和旧版 S3 三格切换「全弹发射」。部分角色页仍显示旧技能，依据当期官方公告和 2026-10 日文资料采用现行规则。 [Smilegate 调整预告](https://page.onstove.com/epicseven/global/view/11202043?boardKey=987&combined=false) · [正式更新公告镜像](https://epic7db.com/news/1120-thu-update-content) · [日文现行技能](https://game8.jp/epic-seven/304401)

- 成长模板 `ranger_leo_4`，攻／血／防／速系数 55800／166200／45900／110000。60 级 6★ 零觉醒 843／4799／536／110，满觉醒 1068／5650／536／110，暴击 23%、暴伤 150%、命中和抗性为 0%。觉醒 1／5 阶攻击 +3%／+6%，2／6 阶生命 +3%／+6%，4 阶暴击 +8 个百分点，3 阶增强 S3。各阶固定攻、血沿用通用表。 [成长数据](https://gacha-data.com/epic-seven/stat_calc/) · [面板](https://epic7tools.dev/heroes/schuri/) · [觉醒](https://www.gacha.gg/epic-seven/characters/schuri)
- S1 单体，ATK 1、Power 1，另加目标最大生命的 1.5%。获得 1 魂及 1 格专注，暴击时本次专注获得量为 2，最高 5 格。五阶强化增伤依次为 5／5／5／5／10%。当前版本中毒效果已删除。未命中和回合外触发时的专注边界等待原版实测。 [官方改版镜像](https://epic7db.com/news/1120-thu-update-content) · [数值调查](https://www.reddit.com/r/EpicSeven/comments/1p6sl88/) · [技能强化](https://epic7tools.dev/heroes/schuri/)
- S2 为被动，首场战斗开局获得 3 格专注。修里造成暴击后，其他存活友军的行动值增加，基础 15%，五阶分别增加 1／2／2／2／3 个百分点，最高 25%。现有三图中的第二张用于演出预览，实际被动不发动图中的四段攻击。群攻有多个目标暴击时应重点验证队友收到一次还是多次推进，不能由十个视觉 HIT 推导触发次数。 [官方改版镜像](https://epic7db.com/news/1120-thu-update-content) · [强化数值](https://epic7tools.dev/heroes/schuri/)
- S3 群攻，ATK 1、Power 1，+2 魂，基础冷却 4、+3 强化后 3。非满专注正常释放，满 5 格时在动作开始确定强化分支，消耗全部专注，ATK 改为 1.25，仍按通用暴击判定。三阶觉醒增加 30% 敌方行动值降低，满专注同时忽略效果抵抗。忽抗是否仍保留命中与免疫门槛需用同版场景单独核实。20 魂燃烧赋予额外回合。+1／+2／+4／+5 分别增伤 5／5／10／10%。 [正式更新公告镜像](https://epic7db.com/news/1120-thu-update-content) · [数值调查](https://www.reddit.com/r/EpicSeven/comments/1p6sl88/) · [强化与觉醒](https://www.gacha.gg/epic-seven/characters/schuri)

权威实现可复用开局专注与暴击事件、条件倍率及额外回合。需要在一次技能事务保存动作开始时是否满专注，让消耗、各目标伤害和觉醒忽抗读取同一个状态。S1 的目标最大生命伤害项、暴击倍增资源，S2 排除施术者的队友推进，应做通用配置条件。验收覆盖 3→4/5、溢出、四格与满格 S3、低觉醒、高抗或免疫目标，以及带燃魂的额外回合。

## 原生资源与演出

- 两套主骨骼均为 Spine 2.1.27。角色骨骼有 `b_idle`、`b_idle_ready`、`skill1`、`skill2`、`skill3_1`、`skill3_2`；技能片段无骨骼事件。闭包含 `model/schuri.timeline`，头像和三张技能图标齐全，被动槽用 `skill/pa_c1020_2.png`。
- S1 三个图内 HIT。主片段局部 1086、2086 ms 直接触发，第三段在第二段完成后延迟 158 ms。模型 timeline 另有 1320／2542／2700 ms 的 `fire`，原图的 `skill1/fire` 监听无下游边。S2 四个 HIT 与 timeline 的 `fire` 同为 1122／1322／2310／2510 ms。现有战斗解释器调度模型 timeline 的 `effect`、`shake`，尚未处理该 `fire` 函数。查清模型射击闪光和帧偏差的原始含义后再决定是否接入，避免重复图内命中。
- S3 由 `skill3_1` 及前段枪裂 CFX 进入原生 CUTIN #23，直接连接 `skill3_2`。原 `cut/fhd/sk_c1020_3.webp` 为 1280×723、22 帧、1560 ms，无需添加触发。十个 HIT 分两链，第一链从前段 CFX 开始后 300 ms 起，再按 233／567／266／334 ms 的边延迟推进；第二链从后段 CFX 开始后 533 ms 起，再四次延迟 200 ms。准确累计须按节点完成语义求值。前后 CFX 分别使用 `fiveshot`、`final` 骨骼动画。原图含 10 CAM、10 ZOOM、两组画面后处理、背景显隐及颜色调制；重点核对 CUTIN 结束后枪裂第二段、各目标可见性及镜头复位。

在进程内临时注册修里，用正式导入器核对 85 项输入。包含 18 SCSP、12 atlas、23 SCT、3 STG、9 CFX、12 particle、1 WebP、1 timeline、6 bank，闭包无告警，41 份骨骼和纹理全部解码成功。模型 SHA-256 `ce7996163043e11cc84bfbc4d0a82063d66285f068a0a91f0543e1b4759ab9b6`。三张源图依次为 `bf15815d89e1bae18c3b86dc082da226628e16015c89a0e14ee48624c096de06`、`b9ba6363a286bc2826338222cd9a3874122a8003de44674d18acd350ebbfd9e3`、`1b67fb75c7c0b52ab08411b929188b5973e3a6344c266b599d0c632e3e0f188c`。CUTIN SHA-256 `1f94f118f0c447209fcfb4541ee61724e6611bf26b7459e1cb795b6559460b8c`。

现有日语 `voc1.audio_ja.bank` 含该角色 28 个键，目录校验无缺项。S1 有两条、S2 有三条、S3 有两组连续台词，分别含三句和两句。S1 夹攻呼叫与应答各两条。三个源技能的特效与语音事件均可由目录解析，接入后用 FMOD 探针确认全部取样与 DSP。此次完成静态预检，正式入表、Unity、原生音频播放和同版实录对照待执行。
