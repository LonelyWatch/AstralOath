# 第 13 名角色科尔布思接入预检

2026-11-14 只读核对。科尔布思 `c1012` 是火属性四星战士、巨蟹宫。应使用 `model/corvus.scsp` 和 `portrait/c1012.scsp`。普通形态与暗属性科尔布思 `c2012`、`corvus_m` 系资源分别维护。现有阵容顺序和发布门禁见 [角色扩充规范](../development/battle-roster-expansion.md)。[Smilegate 简中角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json)

## 已验证的资产与演出

- 在角色导入器进程内临时注册 `c1012`，执行 `Closure.build` 和 `build_catalog`。在 `D:/sevenepic/原始资源` 中共解析 61 个输入，包括 13 个 SCSP、18 个 SCT、10 个 atlas、5 个 CFX、4 个粒子、3 个 STG、1 个 WebP、1 个模型 timeline 和 6 个共享音频 bank。闭包无诊断，全部 SCSP 解码为 Spine 2.1.27，全部 SCT 通过解码。两种头像、三个技能图标和 `buff/stic_indomitable.png` 均可读取。
- 三个技能分别使用 `c_corvus_skill_01`、`c_corvus_skill_02`、`c_corvus_skill_03`。FMOD 特效与默认语音路径分别为 `event:/skill/{图ID}` 和 `event:/voc/skill/{图ID}`，S1 另有 `_c`、`_a` 两个夹攻语音事件。日语目录有 25 个角色键，本次检查无缺少的 programmer key。
- S1 依次使用 `jump_forward → skill1 → jump_back`。接近窗口由前跳的 266.6 ms `jump` 到 700 ms `landing` 决定，同窗包含 CAM12、1.4× 变焦及 MOVE16。主体动作开始时触发 `corvus_sk1eff_cm` 摇镜，HIT 在主体开始后 33、267 ms。模型 timeline 在主体局部 267 ms 通过 `shadow` 挂点播放 `corvus_skill_01`。回位片段有 366.6 ms 的 `landing`，其骨骼未见 `jump`，须在 Unity 检查回位轨迹。
- S2 使用 `skill2`，开场隐藏地图、展示速度线，CAM1 有连续位移和变焦。825 ms 启动 `corvus_skill_02.cfx`，配合三段 `boom_cm_power_up` 摇镜和两段后处理。可达 HIT 的图时点为 895、1905、2055、2255 ms。布局和地图恢复应同时核对；源码的最后一个 BGSHOW 节点未写 `show=true`，当前战斗解释器会在动作结束复位背景。S1/S2 名为 `hit` 的监听节点没有下游连接，命中走独立的延迟边。
- S3 使用 `skill3`，原图 CAM 完成后在舞台 1000 ms 接入 `sk_c1012_3`，之后以舞台时钟继续运行，2600 ms 触发支援表现。原 1280×723 WebP 共 19 帧、1300 ms。模型中也有 `skill3/cutin` 1133.3 ms，但其监听没有连到 CUTIN。无需重复添加立绘。模型 timeline 的 `crovus_skill_03.cfx` 保留源文件的拼写。图含三段上下摇镜、两段后处理和一个需要原作实帧核对的 COLOR_BLEND 节点。

源模型 SHA-256 为 `09c442e09cebd8bcb2f65c6e157d6104310137795f264077f6898fa73fe2eb19`，立绘 WebP 为 `b2b47fbd46786c6ead319b00fb3741d557d6a4a0fdccd3cd42d0b50be786705e`，原不屈图标为 `9a48d04e346e29d2032cf5742f4a27abfc41308d60f9a4ed8b3b6c486746cc33`。正式导入时由现有清单重新记录全部哈希。

## 技能规则与证据

- 普通满觉醒裸装面板为攻击 903、生命 6635、防御 630、速度 103。觉醒为生命 +3%、+6%、+6%，防御 +3%、+6%。公开成长数据与卡尔图哈 `c1013` 共用四星巨蟹宫战士曲线，攻击/生命/防御成长系数分别 49.8/187.2/49.5，速度锚点 103.75。面板应按现有 Luban 成长口径读回复算。[当前角色详情](https://epic7tools.dev/heroes/corvus/) · [觉醒数据](https://www.gacha.gg/epic-seven/characters/corvus)
- S1 是单体盾击。转移一项自身弱化的基础概率为 50%，满强化 75%。不屈时改用更强的伤害系数并施加一回合眩晕。公开计算器原始实现使用常态攻击 0.7＋防御 0.9、不屈攻击 0.9＋防御 1.2，Power 1。伤害与转移强化分别计入，眩晕有自己的一次效果判定。[官方调整公告](https://page.onstove.com/epicseven/global/view/4213346) · [公告原文镜像](https://epic7x.com/10-31-patch-pets-luna-and-buffs-nerfs/) · [E7Calc 源码](https://github.com/tyopoyt/epic7-damage-calc/)
- S2 攻击全体，伤害为攻击 0.3＋防御 0.7，Power 0.9。行动值基础减少 20 个百分点，满强化 30，不屈使最终减量加倍。目标被成功推至 0 时触发一回合眩晕。基础冷却 4，满强化 3。两次效果是否分别检定抗性，以及目标初始已为 0 时是否仍可触发，应由原作复验，不应从技能文案倒推。[官方调整公告](https://page.onstove.com/epicseven/global/view/4213346) · [公告原文镜像](https://epic7x.com/10-31-patch-pets-luna-and-buffs-nerfs/) · [当前技能与强化](https://epic7tools.dev/heroes/corvus/)
- S3 消耗 100 斗志，无普通冷却，赋予自身三回合不屈并获得额外回合。满觉醒形态的治疗在额外回合前结算，基础治疗为自身最大生命的 50%，+2 强化为治疗量 +20%。20 魂魂燃使治疗量翻倍。开战获得 50 斗志，受击 +10；普通 S1/S2 分别获得 10/30。100 消耗取官方调整后的数值。公开站点简述中的 50 消耗属于相互冲突的资料，旧版确曾消耗 50。当前未读回同版客户端的具体斗志字段，原版实战还需复核资源增长与治疗系数。不屈提供防御和效果抵抗各 +30%，为不可驱散增益。[官方调整公告](https://page.onstove.com/epicseven/global/view/4213346) · [公告原文镜像](https://epic7x.com/10-31-patch-pets-luna-and-buffs-nerfs/) · [日文旧版同口径描述](https://game-rankers.com/epic7/%E3%82%B3%E3%83%AB%E3%83%B4%E3%82%B9/) · [治疗倍率](https://epic7x.com/character/corvus/)

## 接入顺序和验收

1. 复用额外回合、斗志 HUD、弱化免疫、技能强化和命中框架。扩展通用的开局资源和有符号受击资源变化，保留席可原有的受击消耗。在有两次同技能动画命中的情况下，先按一次权威攻击触发一次资源变化。
2. 新增不可驱散的复合属性增益和同类增益检测，并提供可配置的状态触发伤害系数。弱化转移应保留原状态的参数及剩余回合，仅在目标能接收该状态时从施术者移除。免疫、未命中、无可转移状态、驱散不屈及重复状态需测试。
3. 给连续效果提供本次已通过检定的行动值变化和投影值，S2 在单个目标的推条确实满足条件时才进入眩晕步骤。覆盖已为零、恰好到零、免疫或抵抗、不屈与技能强化同时生效的情况。
4. S3 走非攻击、仅自身目标，先应用增益与治疗，再发额外回合事件。验证满斗志入场路径、消耗、魂燃额外治疗、禁疗以及手动与 AI 目标。客户端预览按源图对照 S3 的舞台时钟、实际立绘时长、归位和音效。
5. 资料冲突和未确定行为继续登记。当前只完成本地源依赖与规则预检，尚未改入目录、配表或运行 Unity，也未执行官方录像逐帧对照。
