# 第 19 名埃德勒接入预检

2026-11-14。基础形态 c1018。依据固定角色顺序和本地只读 pack-15-r4262。以下为源审计和实现建议，未给本轮场景登记角色。

## 素材与身份

- 埃德勒模型用 model/aither.scsp，立绘用 portrait/c1018.scsp。原名别名保留，STG 用 c_iterre_skill_01/02/03，普通模型和 STG 不混入引导者及皮肤。
- 结合模型 skill3 的 cutin 标记，采用项目现有补图规则，加入 S3 的 cut/fhd/sk_c1018_3.webp。标记 1.2000 s，源 STG 没有 CUTIN。WebP 1280×723，19 帧，共 1660 ms。三图各有一个 HIT。补图后依赖闭包 67 项，14 SCSP、15 SCT 均解码，未报告依赖缺失。
- 源 SHA-256。模型 a441d58fcd57678f4893af9acde39d6282aec06715f8e9deac0d91d5f0cf827c；立绘 c2f65cc747d36742f39510d1a76bfa64fbbaab76242e4df0960b5e45500b4695；模型 timeline 00afdf7ed5db99f7d31b13593824951c50630fdf2211c5793df87d593b4e0845；S1 1db754ea10746ba5321bfb9f9d65293a7944cc4c06cc2d034a0f222639ee580e；S2 a723eab0c04d26e7229be023fe46a36ffd0eb9eda9e18fad60504b3636d6ded7；S3 081deaa5e43713b31efd238990ab84186abae55b7c61064047d4a85696b34dad；CUTIN 171f132ad9954ed067a89ba95cb082875b38b8be848df42d6c2d59b631216df5。
- 原始目录存在 S1／S2／S3 三对原生音效和语音事件。日语表 22 键。普通 S1 用 voc_c1018_shout_1/_2/_3，S2 用 sk2，S3 用 sk3。唯一缺键为另一事件的 voc_c1018_defeat_2，本次没有技能语音缺键。原事件尚待完整播放验证。

## S1 投射物

- 模型 timeline 在 skill1 开始先挂蓄力 i_terre_skill_01_burst 和 i_terre_skill_01_shot；后者的角色锚为 root，横向偏移 100 源单位，粒子各延迟 700/1400 ms。两者骨骼 pivot 没有空间位移，属于固定施法特效。
- 模型 timeline 在 1.400 s 发送 shot，区别于骨骼自身 1.3333 s 的 effon。STG 的 ONEVENT guid5 接收 shot 后开始 SHOT guid13。其源节点仅包含 selfBone=hit、source=i_terre_skill_01_ball.cfx、y=-60、time=300.0、rotate=0。角色真实 hit 为 root 子骨骼，原始坐标 (203.89,306.99)，在 skill1 中保持固定。节点的两条开始边分别延迟 300 ms，于 1.700 s 到达 HIT guid15 和摇镜 guid14。
- 弹体 CFX SHA-256 f4d67e3ea340ca4e84673731e696887839ad685d468f5d97cd21d39713320607。由 Spine intro、loop 与挂在 pivot 的粒子组成。intro、loop 各 .5333 s，原始 pivot 始终无位移，必须由 SHOT 系统提供轨迹和 .300 s 生命周期。蓄力 i_terre_skill_01_shot.cfx SHA-256 45dd305de9ba58ddae8ada2b91ec42a639f4914320384933fe071293e85be42a，资源不能互代。
- BattleSkillPresentation 当前能读取 SHOT 的 time 用于图调度，但播放分支不会创建弹体。CharacterLab 的 StgPlaybackSession 只把无源 SHOT 当作延迟，有源 SHOT 当前告警。源集合中找到 54 个 SHOT，time 全部存在，其中 3 条埃德勒形态同时写 selfBone 和 y，未发现该类节点的重力、曲线或指定目标骨字段。空源 SHOT 保留其定时行为。
- 先实现有源单目标线性 flight。首帧从施法者 hit 世界坐标采样，移动到选定受击目标的现有受击锚，使用项目源单位转换并保留 y 源偏移字段。现有证据没有确定 y 是目标、发射端还是全段偏移，默认受击骨及 y 语义待同版本录屏复核，验收报告标明。rotate=0 不主动把原贴图旋向目标。时间交给共享演出时钟，停止、切场、暂停与逐帧定位沿已有演出生命周期释放或重放；逻辑结算继续只由权威事件驱动。保留 HIT 和摇镜的原 1.700 s 时点。
- 可复用 CFX 调度及每帧跟随 position 入口。弹体和尾迹同一持有者，生命上限取 SHOT duration，避免 CFX 的 .5333 s 自身动画延长飞行。建议专项逐帧断言 1.39 s 无弹体、1.40 s 出生于源骨骼、1.55 s 已离开发射端、1.70 s 抵达并触发 HIT，以及结束和重播后无遗留。每个方向取样，核对同屏仍有蓄力层。

## 规则

- 三星寒气属性精灵师、天秤座。成长系数 37200／132600／54300／97500，满级满觉醒攻／血／防／速 712／4592／672／103。觉醒 1/5 加生命 3/6%，2/6 加速度 2/4，4 加防御 6%，3 增强 S2。 [面板和觉醒](https://www.gacha.gg/epic-seven/characters/aither) · [成长来源](https://gacha-data.com/epic-seven/stat_calc/)
- S1 单体，攻击率 1、Power 1.05，1 魂。40% 基础降速两回合，强化为加伤 10%、概率 +5 点、概率 +5 点、加伤 15%。
- 2024-07-18 调整已从治疗和屏障移除施法者攻击系数。S2 单个友方，清除两个减益、按被治疗者最大生命 19% 回复，10 魂燃烧改为 29%。完成第三阶觉醒才增加施法者 20% 行动值。基础 CD 2／1 魂，强化加治疗 5/5/10/10%。原公告没有规定治疗与驱散谁先提交，同版原作需用禁疗目标确认顺序。
- S3 全体友方，各按自身最大生命 15% 计算治疗及两回合屏障。基础 CD 3／2 魂，无觉醒或魂燃分支。七阶强化只增加治疗 5/5/5/5/5/5/10%，没有冷却阶。先实现按各目标最大生命取值，S2 魂燃用显式系数。测试两个生命上限不同的友方以及同一技能烧魂前后的数值。 [版本调整](https://epic7db.com/news/718-thu-update-content) · [调整倍率来源](https://www.reddit.com/r/EpicSeven/comments/1e6bo8c/) · [当前技能与强化](https://www.gacha.gg/epic-seven/characters/aither)
