# 第 18 名角色雅卡泰丝接入预检

固定队列中的 `c1017` 是普通四星火属性精灵师雅卡泰丝（Achates），双子座。模型和台词事件以官方 ID 交叉核对，皮肤 `achates_s01`、`c_acha_skin01_skill`，以及月光 `achates_m/m2` 各自保留。资产基线为本地只读 `pack-15-r4262`。 [Smilegate 简中角色索引](https://static-pubcomm.onstove.com/gameRecord/epic7/epic7_hero.json) · [职业和星座](https://epic7tools.dev/heroes/achates/)

## 配表与权威结算

- 成长模板 `soul_weaver_gemini_4`，攻／血／防／速系数分别为 30000／143800／56700／103125。4★ 1 级零觉醒攻／血／防／速 42／234／78／103，60 级 6★ 零觉醒 453／4152／662／103，满觉醒 603／4945／662／103。觉醒 1／4／5 阶分别加 6／12／12 个百分点抗性，2／6 阶加 3%／6% 生命，3 阶替换 S2 增益。各阶固定攻、血沿用四星通用表。裸装暴击 15%、暴伤 150%、效果命中 0%、抗性 30%。 [成长表](https://gacha-data.com/epic-seven/stat_calc/) · [觉醒和面板](https://epic7x.com/character/achates/)
- S1 单体，倍率 ATK 1、Power 0.95，+1 魂。命中后 35% 基础概率降攻 2 回合，+2／+4 增加 5／10 个百分点，+1／+3／+5 增伤 5／10／15%。专属装备额外 +25 个百分点，与裸技能分开。 [倍率、技能及强化](https://epic7tools.dev/heroes/achates/)
- S2 选一个存活友军，可选自己。按施术者最大生命 15% 治疗，赋予一回合保护，再使目标所有已有冷却的技能各减一。三阶前赋予护盾，三阶后赋予无敌。+2 魂，基础冷却 3。五阶治疗强化为 5／5／5／5／10%。现有结算器先登记本次技能冷却，自选时可同时缩短本次 S2 和正在冷却的 S3，符合 S3→S2→S3 循环。2019 官方调整早期文字曾排除自身，实际更新后游戏内描述和同周期实战包含自身。尚未获得未觉醒护盾的确切生命倍率，不能把治疗 15% 当作护盾系数的证据。 [2019 官方调整](https://page.onstove.com/epicseven/global/main/view/3866328?listType=2) · [公告镜像](https://epic7x.com/9-4-patch-roaming-warrior-leo-and-new-events/) · [同周期实录索引](https://www.reddit.com/r/EpicSeven/comments/d1or3p) · [觉醒分支](https://epic7tools.dev/heroes/achates/) · [技能数值](https://epic7x.com/character/achates/)
- S3 群体非攻击，先解除全体存活友军的可解除弱化，再按施术者最大生命 30% 治疗。10 魂燃烧将治疗基数改为 50%。+2 魂，基础冷却 4；强化为治疗 +5／+5%、冷却 -1、治疗 +10／+10%。禁疗应被本次净化清掉后再治疗。部分英文镜像只写解除一个，且同一页的概述仍写全解。日文现行技能页和实战记录支持全解，目标版本实录尚待用至少两个可净化弱化同场复验。三种专属装备效果独立。 [日文技能和强化](https://game8.jp/epic-seven/304391) · [治疗系数](https://epic7x.com/character/achates/) · [英文资料差异](https://epic7tools.dev/heroes/achates/)

本地 `db/skill_player.db` 和日文 `text/ja/text.db` 在现有解包里不可直接读取技能字段，因此上述系数、觉醒与原版边界按列出的来源核对。共享战斗库已有治疗、净化、减冷却和护盾。接入时需补通用无敌状态及权威伤害入口的保护规则。原图标 `buff/stic_invincible.png` 为 28×28，SHA-256 `57a00da491c05c5594ce7d46429c1e1bd771190d1363bc7310b68901a64cc958`，原生持续特效 `effect/stse_invincible.cfx` 同名依赖齐全。分别核对直接伤害、可驱散性、与护盾共存时是否消耗盾、弱化是否仍可施加，以及毒伤、转移伤害、额外伤害等边界。 [无敌状态数据](https://www.epic7helper.com/effects#invincible)

## 原生资源和技能演出

- 基础骨骼 `model/achates.scsp`、`portrait/c1017.scsp` 均为 Spine 2.1.27。模型 timeline 在 `b_idle_ready` 的 1000 ms 启动 `achates_bidle_pati`，由 `eff_pos` 挂点持有，技能或离开待机时使用 `remove` 清除。战斗用普通待机 `b_idle` 和行动待机 `b_idle_ready`。基础头像和三个技能图标齐全。
- 三图为 `c_acha_skill_01/02/03`。S1 前跳接近、`skill1`、后跳返回，主动作局部 266 ms 触发唯一 HIT，骨骼对应事件为 266.6 ms。源相机摇镜 `achates_skill_01_cm`，模型 timeline 在 267 ms 为法杖挂接粒子。
- S2 `skill2` 局部 1100 ms 有已处理的 `fire` 事件。原图独立于该监听，在 1500 ms 播放 `achatest_skill_02.cfx`，再过 500 ms 触发唯一 HIT 标记。源拼写 `achatest` 保留。镜头有 1200 ms 贴近及变焦 2×、2333 ms 返回段和 200 ms 恢复。原图 HIT 应用于治疗反馈的时间锚，不能增加权威伤害、受击反应或夹攻。
- S3 原生连线 `ANI skill3_1 #2 → CUTIN #27 → ANI skill3_2 #4`。CUTIN 使用同形态 `cut/fhd/sk_c1017_3.webp`，1280×723、21 帧、1640 ms。`skill3_2` 的零时刻 `cutin` 已标记 `handled`，无需添加第二个监听。第二段局部 733.3 ms 落地，750 ms 触发 `achates_skill_03.cfx`，其后并行触发 HIT、全体治疗特效和延迟 900 ms 的后处理。原图包含 200 ms 隐地图、700 ms 恢复地图、黑色调制与连续 CAM/ZOOM。后续用原版同形态画面复核幕布、落地和相机恢复，确认治疗目标是全体存活友军。
- 在进程内临时注入 `c1017` 角色和图名，使用正式导入器得到 66 个输入，含 11 SCSP、17 SCT、10 atlas、3 STG、7 CFX、10 粒子、1 WebP、1 timeline、6 bank，闭包无告警，全部骨骼与纹理解码成功。模型 SHA-256 `6465fea3b73f9dfd225f5293643d094b17f4e02d70ffe552ea123cc15991f68a`，三图依次为 `b3a61e2a4db2d4386ece390c7e5e14768436d978e96026eb3c96bcc0b607285d`、`dd78571eec05f3c98b9cf182e32faef9c241adb3b891e680d5dc2a52bc5a502c`、`97491040695314fd9b6830e03969ba8d465285dd94196546875396fc1a5ce65e`。CUTIN SHA-256 `e10fd9fd5283554e1bd39da166c0fb9a23da28f35b5cebf951979757b876d575`。

## 日语回调

三图均有同名的原生特效和语音事件，S1 另有夹攻呼叫和应答。日语表有 28 个角色键。原 `@master.yml` 60431–60449 行表明 S3 的裸 `_2`／`_3` 音频只列于英、韩、简中，日语使用两句式 `_2_1+_2_2`、`_3_1+_3_2`。隔离的 Windows 原生 FMOD 测试中，8 次均检出 DSP 输出，最小峰值 1.0121。四套 `_1`、`_2_1+_2_2`、`_3_1+_3_2`、`_4` 各两次。裸键回调触发静音占位后，同一次施放仍完整播放日语两句，保持原事件和现有静音逻辑，无需日语别名。记录位于 D 盘工程 `.artifacts/roster-data/c1017-fmod/fmod.log`。

## 接入验收

权威测试覆盖 S1 概率强化，S2 对自身及队友的冷却减免，三阶前后增益互斥与治疗量，S3 两项以上弱化、禁疗清除后治疗及燃魂。Unity 测试覆盖两种待机的粒子生命周期、S2／S3 只产生治疗反馈、S3 原立绘单次进入、地图和镜头复位。FMOD 测试要求 S3 观测到四套取样。此次只完成资源预检和隔离原生音频验证，正式入表、Unity、Player 和原版逐帧均待执行。
