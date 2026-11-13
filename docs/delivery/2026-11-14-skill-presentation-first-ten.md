# 前十名角色技能演出基线

核对日期为 2026-11-14。覆盖普通形态十名角色、30 个配表技能槽。下文按演出图列出模型动作、可达命中、镜头、场景和特效，适合在技能编辑器里对照某一帧。技能名称取项目中文配表。

## 如何读时间

- 各时间为战斗播放规则下的舞台毫秒，按最接近的整数显示。图中的 `when=1` 从父节点起点计算，`when=0` 从结束计算，并叠加 `delay`。命名事件优先取模型 Spine，缺失再查模型 timeline。缺失落地事件的已知情况按项目规则回退到片段结束。
- `CAM12` 等数字保留原 `location` 值。缩放写为“倍率@舞台时间”。`CAM归位` 表示源节点省略位置，由现有解释器恢复基准。HIT 是演出触发标记，治疗或支援也可以有标记。服务器按技能规则结算伤害、治疗与状态。
- 立绘同时标记舞台触发时间和素材播放时长。播放立绘期间舞台暂停。一个技能有两段立绘时，实际录像时间还须累加此前立绘的时间。模型 timeline 的 `effect`、`shake` 以它们所附的动作片段起点定位。
- 效果名对应 `effect/*.cfx`，可展开其中的 Spine 动画、粒子、延时、寿命、层次和骨骼挂点。每个 HIT 的效果、偏移、随机范围可用节点 GUID 定位。下文对同一技能重复用到的受击效果名去重，保留所有可达 HIT 时点。

## 逐技能索引

### 拉斯 `c1001`

**S1 十字斩** · `c_ras_skill_01`

- 动作 `jump_forward` 0 → `skill1` 867 → `jump_back` 1900。位移 MOVE16 467（400 ms）、MOVE11 1967（400 ms）。HIT 1133, 1433。
- 镜头 CAM12 467、`ras_re_cm_sk1` 摇镜 867、CAM归位 1967。缩放 1.3×@467、1×@1967。
- 特效 `ras_new_skeff_sk1` 867；随 HIT 播放 `hit_new_slash_normal`。
- 舞台 压暗 0。

**S2 指挥一击** · `c_ras_skill_02`

- 动作 `skill2` 0 → `jump_back` 3000。位移 MOVE16 800（350 ms）、MOVE11 3067（400 ms）。HIT 1466, 1766, 2733。
- 镜头 `ras_re_cm_sk2` 摇镜 0、CAM12 1200、CAM归位 3067。缩放 1.3×@1200、1×@3067。
- 特效 `ras_new_skeff_sk2` 0、`ras_new_skeff_sk2_end` 2605、`ras_new_skeff_sk2_2` 2970；随 HIT 播放 `hit_new_slash_normal`、`hit_new_pierce_normal`。
- 舞台 压暗 0、模糊 2733。

**S3 继承者之剑** · `c_ras_skill_03`

- 动作 `skill3` 0。位移 MOVE11 1400（540 ms）、MOVE11 1940（0 ms）。HIT 2380, 2611, 3370, 3535, 3700。
- 镜头 CAM1 0、CAM1 1400、CAM42 1848、CAM42 1848、`ras_re_cm_sk3` 摇镜 2248、CAM归位 5248。缩放 1.5×@0、1.7×@200、1.9×@1400、0.8×@1848、0.6×@2611、0.5×@3304、1×@5248。
- 特效 `ras_re_sk3_ready` 0、`ras_new_skeff_sk3` 2248；随 HIT 播放 `hit_new_blunt_normal`。
- 舞台 地图隐藏 0、速度线 200、立绘 `sk_c1001_3` 1848（16 帧，实际播放 1250 ms）、模糊 2380、模糊 3700、地图恢复 5448。

### 赛西莉亚 `c1002`

**S1 制裁** · `c_cecilia_skill_new_01`

- 动作 `jump_forward` 0 → `skill1` 967 → `jump_back` 1900。位移 MOVE16 267（400 ms）、MOVE11 2033（333 ms）。HIT 1300。
- 镜头 CAM12 267、`cecilia_skill_01_cm` 摇镜 967、CAM归位 2033。缩放 1.3×@267、1×@2033。
- 特效 `cecilia_skeff_sk1` 967；随 HIT 播放 `hit_new_pierce_normal`。
- 舞台 压暗 0。

**S2 钢铁暴风** · `c_cecilia_skill_new_02`

- 动作 `skill2` 0。HIT 1667, 1867, 2317, 3233, 3433, 3883。
- 镜头 CAM1 0、CAM42 600、`cecilia_skill_02_new_cm` 摇镜 1500、CAM42 1600、`cecilia_skill_02_new_cm` 摇镜 1700、`cecilia_skill_02_new_cm` 摇镜 2150、CAM归位 4100。缩放 1.4×@0、1.3×@600、1.1×@1600、1×@4100。
- 特效 `cecilia_skill_02_skyglow` 700、`cecilia_skill_02_new_01` 1500、`cecilia_skill_02_new_01` 1700、`cecilia_skill_02_new_01` 2150、`cecilia_skill_02_eff` 0（模型）；随 HIT 播放 `hit_new_slash_normal`、`hit_new_blunt_mult_normal`、`hit_new_blunt_normal`。
- 舞台 压暗 0、模糊 1600、模糊 3233。

**S3 毁灭之枪** · `c_cecilia_skill_new_03`

- 动作 `skill3_1` 200 → `skill3_2` 900。位移 MOVE41 900（1500 ms）、MOVE11 4200（0 ms）。HIT 2800, 3500, 3650, 3800, 3950, 4150, 4490。
- 镜头 CAM1 0、CAM1 200、`cecilia_skill_03_cm` 摇镜 200、CAM1 900、CAM41 2666、CAM41 4200、CAM归位 4200。缩放 1.3×@0、2×@200、1.7×@900、0.94×@2666、1.4×@4200、1×@4200。
- 特效 `cecilia_skill_03_renewal_flashbang` 200、`cecilia_skill_03_renewal_pati` 4300、`cecilia_skill_03_new_botaura` 200（模型）、`cecilia_skill_03_new_lance1` 200（模型）、`cecilia_skill_03_new_lance2` 900（模型）；随 HIT 播放 `hit_new_blunt_normal`。
- 舞台 地图隐藏 0、立绘 `sk_c1002_3` 900（22 帧，实际播放 1610 ms）、速度线 900、模糊 2800、地图恢复 4200、压暗 4200、模糊 4490。

### 萝季 `c1003`

**S1 连续斩击** · `c_rose_skill_01`

- 动作 `jump_forward` 0 → `skill1` 533 → `jump_back` 1667。位移 MOVE16 200（333 ms）、MOVE11 1867（367 ms）。HIT 766, 1233。
- 镜头 CAM12 200、`rose_cm_sk1` 摇镜 533、CAM归位 1867。缩放 1.3×@200、1×@1867。
- 特效 `rose_skill_01` 533（模型）；随 HIT 播放 `hit_new_slash_normal`。

**S2 光之盾** · `c_rose_skill_02`

- 动作 `skill2` 0。HIT 1767。
- 镜头 CAM1 0、CAM32 980、CAM归位 2417。缩放 1.4×@0、1.2×@980、1×@2417。
- 特效 `valkyrie_sk2` 1367、`rose_skill_02` 0（模型）。

**S3 胜利女神** · `c_rose_skill_03`

- 动作 `skill3` 0。位移 MOVE11 1233（300 ms）、MOVE11 2800（300 ms）。HIT 2600。
- 镜头 CAM1 0、CAM归位 3500。缩放 1.4×@0、1×@1533、1×@3500。
- 特效 `rose_skeff_sk3` 0、`valkyrie_sk3_pati2` 2000。
- 舞台 压暗 0、模糊 2050。

### 席可 `c1004`

**S1 单发射击** · `c_silk_skill_01`

- 动作 `skill1` 0。HIT 466。
- 镜头 CAM11 0、CAM41 366、`silk_cm_shot` 摇镜 466、CAM归位 916。缩放 1.4×@0、1×@366、1×@916。
- 特效 `silk_skill_01` 0；随 HIT 播放 `hit_new_pierce_normal`。

**S2 集中** · `c_silk_skill_02`（被动槽）

- 动作 `skill2` 0。HIT 1100, 1300, 1500。
- 镜头 CAM1 0、CAM16 1000、`silk_cm_shot2` 摇镜 1100、CAM归位 1867。缩放 1.4×@0、1×@1000。
- 特效 `elf_eff_sk2_f` 0、`elf_eff_sk2_b` 0；随 HIT 播放 `hit_new_pierce_normal`。
- 舞台 压暗 0、模糊 1050。

**S3 暴风之箭** · `c_silk_skill_03`

- 动作 `skill3` 0。位移 MOVE41 633（700 ms）、MOVE11 1733（467 ms）。HIT 2163, 2361, 2625, 2856, 3120。
- 镜头 CAM1 0、CAM41 633、CAM42 1800、`elf_cm` 摇镜 1800、CAM归位 3600。缩放 1.4×@0、1.5×@1800、1.1×@2100、1×@3600。
- 特效 `eff_elf_sk3_self` 0、`charge_01` 0、`eff_magic_wind2` 1800；随 HIT 播放 `hit_new_slash_normal`。
- 舞台 压暗 0、模糊 1900、模糊 2625、模糊 3120。

### 梅赛德丝 `c1005`

**S1 神圣闪电** · `c_mercedes_skill_01`

- 动作 `skill1` 0。HIT 1225, 1456。
- 镜头 CAM11 0、CAM16 900、`mercedes_cm_sk1` 摇镜 1060、CAM归位 1500。缩放 1.4×@0、1.2×@900、1×@1500。
- 特效 `mercedes_skeff_sk1_magicring` 893、`eff_magic_thunder_01` 1060；随 HIT 播放 `hit_new_blunt_normal`。
- 舞台 压暗 0。

**S2 次元裂隙** · `c_mercedes_skill_02`

- 动作 `skill1` 0。HIT 1456, 1555, 1720, 2215。
- 镜头 CAM11 0、CAM41 1000、`cm_magic_lightningball` 摇镜 1160、CAM41 1200、CAM归位 1900。缩放 1.4×@0、1.4×@1000、1.2×@1200、1×@1900。
- 特效 `eff_magic_lightningball` 1060、`eff_dust_roundbig` 2050；随 HIT 播放 `hit_new_blunt_mult_normal`、`hit_new_blunt_normal`。
- 舞台 压暗 0、模糊 1160。

**S3 卡尔的炽焰之眼** · `c_mercedes_skill_03`

- 动作 `skill3` 200。位移 MOVE11 2180（200 ms）。HIT 2246, 2345, 2543, 2642, 2741, 2873, 2972, 3200。
- 镜头 CAM11 0、`mage_sk3_cm` 摇镜 200、CAM11 2179、CAM11 3679、CAM归位 4467。缩放 1.4×@0、0.8×@2179、1×@3679、1×@4467。
- 特效 `magician_sk3_eff` 200；随 HIT 播放 `hit_new_blunt_normal`、`hit_new_blunt_mult_normal`。
- 舞台 压暗 0、地图隐藏 0、立绘 `sk_c1005_3` 1500（21 帧，实际播放 1350 ms）、速度线 1950、模糊 2200、地图恢复 4667。

### 绮世 `c1006`

**S1 满月之镰** · `c_kise_skill_01`

- 动作 `skill1_1` 0 → `skill1_2` 650 → `skill1_3` 3267。位移 MOVE16 650（0 ms）、MOVE11 3217（0 ms）。HIT 783, 916, 1050, 1183, 1317, 1450, 1584, 1717, 2617。
- 镜头 CAM1 0、CAM12 400、CAM12 650、CAM12 1850、CAM12 2483、CAM归位 3267、`kise_skill_01_cm` 摇镜 650（模型）。缩放 1.5×@0、1.3×@400、1.4×@650、1.6×@1850、1.3×@2483、1×@3267。
- 特效 `kise_teleport` 0（模型）、`kise_skill_01` 650（模型）、`kise_backport` 3267（模型）；随 HIT 播放 `hit_new_slash_normal`。
- 舞台 压暗 0。

**S2 暗之伤痕** · `c_kise_skill_02`

- 动作 `skill2_1` 200 → `skill2_2` 800 → `skill2_3` 3867。位移 MOVE41 500（0 ms）、MOVE11 2900（0 ms）。HIT 1633, 2250, 2400, 2550。
- 镜头 CAM1 0、CAM1 200、CAM41 500、CAM41 800、CAM41 1567、CAM41 2167、CAM归位 3667、`kise_skill_02_cm` 摇镜 800（模型）。缩放 1.4×@0、1.3×@200、1.5×@800、1.4×@1567、1.05×@2167、1×@3667。
- 特效 `kise_teleport` 200（模型）、`kise_skill_02` 800（模型）、`kise_backport` 3867（模型）；随 HIT 播放 `hit_new_slash_normal`、`hit_new_blunt_mult_normal`、`hit_new_blunt_normal`。
- 舞台 地图隐藏 0、模糊 1600、模糊 2550、地图恢复 3867。

**S3 夜曲** · `c_kise_skill_03`

- 动作 `skill3_0` 0 → `skill3_1` 2827 → `skill3_2` 3260。HIT 3827, 4393, 4513, 4633。
- 镜头 CAM11 0、CAM归位 1460、`kise_m_skill_03_cm` 摇镜 3260、CAM归位 4560。缩放 1.6×@0、1×@1460、1×@4560。
- 特效 `kise_m_skill_03_space` 1260、`kise_m_skill_03_bg_scene1` 2827、`kise_m_skill_03_bg_scene2` 3260、`kise_m_skill_03_0` 0（模型）。
- 舞台 压暗 0、压暗 990、地图隐藏 1460、立绘 `sk_c1006_3_1` 2827（8 帧，实际播放 360 ms）、kise_m_skill_03_node1.scsp 2827、混色待验证 2827、立绘 `sk_c1006_3_2` 3260（11 帧，实际播放 750 ms）、kise_m_skill_03_target_node.scsp 3260、kise_m_skill_03_node2.scsp 3260、混色待验证 3727、压暗 4560、地图恢复 4560。

### 维德瑞 `c1007`

**S1 横扫** · `c_wilderd_skill_01`

- 动作 `skill1` 0。HIT 660, 1386, 1486, 1586。
- 镜头 CAM11 0、`wildred_cm_sk1` 摇镜 0、CAM42 500、CAM42 1200、CAM归位 2400。缩放 1.5×@0、1.4×@500、1.1×@1200、1×@2400。
- 特效 `wildred_sk1eff_target` 0、`wildred_sk1eff_self` 50；随 HIT 播放 `hit_new_pierce_normal`、`hit_new_slash_normal`。
- 舞台 压暗 0。

**S2 剑舞** · `c_wilderd_skill_02`（被动槽）

- 动作 `skill2` 0。HIT 1320, 1485, 1584, 1683, 1782, 1881, 2046。
- 镜头 CAM1 0、`wildred_sk2_cm` 摇镜 0、CAM42 1100、CAM归位 2667。缩放 1.4×@0、1.2×@1100、1×@2667。
- 特效 `wildred_sk2_eff` 0；随 HIT 播放 `hit_new_slash_normal`。
- 舞台 地图隐藏 0、速度线 0、模糊 1000、地图恢复 2867。

**S3 剑刃升腾** · `c_wilderd_skill_03`

- 动作 `skill3` 200。HIT 1850, 2180, 2312, 2444, 2576, 2708, 2840, 3005, 3170, 3434, 4061, 4325, 4490。
- 镜头 CAM1 0、`wildred_sk3_cm` 摇镜 200、CAM1 1633、CAM归位 4820、CAM归位 5200。缩放 1.4×@200、1.2×@1633、1.1×@3333、1×@5200。
- 特效 `wildred_sk3_effB` 200、`wildred_sk3_effF` 200、`wildred_sk3_sword` 200、`wildred_swordineff` 5810；随 HIT 播放 `hit_new_blunt_normal`。
- 舞台 地图隐藏 0、速度线 200、模糊 1750、模糊 2450、模糊 2950、模糊 3450、模糊 3950、地图恢复 5400。

### 亚敏 `c1008`

**S1 盾牌猛击** · `c_aming_skill_01`

- 动作 `skill1_1` 0 → `jump_back` 1867。位移 MOVE16 467（500 ms）、MOVE11 1933（367 ms）。HIT 1040, 1473。
- 镜头 CAM12 467、`boom_cm_power_lf` 摇镜 1473、CAM归位 1933。缩放 1.3×@467、1×@1933。
- 特效 随 HIT 播放 `hit_new_blunt_normal`。

**S2 保护** · `c_aming_skill_02`（被动槽）

- 动作 `skill2` 0。HIT 2021, 2221, 2421, 2621, 2821, 3021, 3143。
- 镜头 CAM1 0、CAM42 900、CAM归位 3900、`aming_skill_02_new_cm` 摇镜 1130（模型）。缩放 1.4×@0、1.1×@900、1×@3900。
- 特效 `aming_skill_02_new_pillar` 1130、`aming_skill_02_new` 0（模型）；随 HIT 播放 `hit_new_blunt_normal`、`hit_new_blunt_mult_normal`。
- 舞台 压暗 0、模糊 2021。

**S3 盾墙** · `c_aming_skill_03`

- 动作 `skill3` 0 → `jump_back` 5100。位移 MOVE31 433（600 ms）、MOVE11 5167（367 ms）。HIT 3300。
- 镜头 CAM31 433、CAM31 1433、`aming_skill_03_cm` 摇镜 1552、CAM31 3033、CAM31 3533、CAM归位 5167。缩放 1.4×@433、1.6×@1433、1.1×@3033、1.35×@3533、1×@5167。
- 特效 `aming_shield_ready` 1519（模型）、`aming_skill_03_back` 1552（模型）、`aming_skill_03_shield` 1552（模型）、`aming_skill_03_front` 1552（模型）。
- 舞台 地图隐藏 0、混色待验证 0、速度线 3033、模糊 3119、地图恢复 5100。

### 夏绿蒂 `c1009`

**S1 双剑** · `c_charlotte_skill_01`

- 动作 `jump_forward` 0 → `skill1` 767 → `jump_back` 1567。位移 MOVE16 400（367 ms）、MOVE11 1567（367 ms）。HIT 1133, 1267。
- 镜头 CAM12 400、`charlotte_skill_01_cm` 摇镜 767、CAM归位 1567。缩放 1.3×@400、1×@1567。
- 特效 随 HIT 播放 `hit_new_slash_normal`。

**S2 沼泽的意志** · `c_charlotte_skill_02`（被动槽）

- 动作 `skill2` 0 → `jump_back` 2200。位移 MOVE41 233（967 ms）、MOVE41 1200（133 ms）、MOVE11 2200（367 ms）。HIT 1468, 1700, 2100。
- 镜头 CAM1 233、CAM41 1200、`cm_explosion_charlotte_sk2` 摇镜 1368、CAM归位 2200。缩放 1.4×@233、1.2×@1200、1×@2200。
- 特效 `eff_charlotte_sk2_sword` 0、`darkfire_pati` 1368、`eff_explosion_dark_small01` 1368、`eff_explosion_dark_small02` 1600、`eff_explosion_dark_small01` 2000；随 HIT 播放 `hit_new_blunt_normal`、`hit_new_blunt_mult_normal`。
- 舞台 压暗 0、模糊 1468。

**S3 旋风** · `c_charlotte_skill_03_05`

- 动作 `skill3_1` 0 → `skill3_2_5` 1867 → `jump_back` 5133。位移 MOVE42 1433（0 ms）、MOVE11 5133（367 ms）。HIT 1933, 2098, 2329, 2461, 2659, 2791, 2989, 3088, 3220, 3319, 4210, 4342, 4408, 4517, 4667。
- 镜头 CAM11 0、`charlotte_sk3ready_cm` 摇镜 0、CAM12 1133、`charlotte_sk3_cm5` 摇镜 1867、`boom_cm_power_up` 摇镜 4210、`boom_cm_power_up` 摇镜 4408、`boom_cm_power_up` 摇镜 4667、CAM归位 5133。缩放 1.3×@0、1.3×@1133、1.1×@1867、1×@5133。
- 特效 `charlotte_skill3_ready` 0（模型）、`charlotte_skill3_ready_d1` 0（模型）、`charlotte_skill3_boost5` 1867（模型）；随 HIT 播放 `hit_new_slash_normal`、`hit_new_blunt_mult_normal`、`hit_new_blunt_normal`。
- 舞台 压暗 0、立绘 `sk_c1009_3_1` 933（18 帧，实际播放 1240 ms）、速度线 1600、模糊 3319、速度线 4133、立绘 `sk_c1009_3_2` 4133（17 帧，实际播放 1200 ms）、模糊 4197。

### 安洁莉卡 `c1062`

**S1 神圣打击** · `c_angelica_skill_01`

- 动作 `jump_forward` 0 → `skill1` 733 → `jump_back` 1467。位移 MOVE16 333（400 ms）、MOVE11 1467（433 ms）。HIT 1133。
- 镜头 CAM12 333、`angelica_cm` 摇镜 733、CAM归位 1467。缩放 1.4×@333、1×@1467。
- 特效 `angelica_skill_01` 1133（模型）；随 HIT 播放 `hit_new_blunt_normal`。
- 舞台 压暗 0。

**S2 生命起源** · `c_angelica_skill_02`

- 动作 `skill2` 0。HIT 1600。
- 镜头 CAM31 0、CAM归位 2300。缩放 1.3×@0、1.4×@900、1×@2300。
- 特效 `angelica_skill_02_target` 900、`angelica_skill_02_self` 900（模型）。
- 舞台 压暗 0。

**S3 女神的指引** · `c_angelica_skill_03`

- 动作 `skill3` 0。HIT 2390。
- 镜头 CAM32 0、CAM32 700、CAM32 1500、CAM归位 3267。缩放 1.4×@0、1.3×@700、1×@3267。
- 特效 `angelica_skill_03_target` 730、`angelica_skill_03_self` 0（模型）。
- 舞台 压暗 0、立绘 `sk_c1062_3` 600（20 帧，实际播放 1280 ms）、模糊 1330。

## 音轨来源与特殊分支

- 30 个槽的特效音和默认语音分别映射到 `event:/skill/{图ID}` 与 `event:/voc/skill/{图ID}`。输入为 [角色实验室音频目录](../../Assets/Game/Modules/CharacterLab/Configs/catalog.json)。原生 FMOD 事件在动作开始启动，事件内部的偏移、随机取样和 programmer key 由 FMOD 管理。剪辑层的关键信息应包含触发点和事件路径，音轨内部帧级标记需要 bank 的进一步证据。夹攻呼叫和应答路径单独记录。缺录音兼容名单中包括维德瑞 S3 的两个日语取样和夏绿蒂 S3 的 `voc_c1009_sk3_1_p2`。
- 席可 S2 源图在实验室可独立播放，图内有三个 100 ms 的空源 SHOT 等待。当前战斗的专注强化仍沿用 S1 演出，独立自动射击图的触发对应需要原作实录核对。维德瑞 S2 由战斗的追击事件使用独立演出。亚敏 S2、夏绿蒂 S2 属于机制被动，表中的源图留供演出核查。亚敏的反击在实战走其 S1。
- 梅赛德丝 S2 使用名为 `skill1` 的骨骼片段，镜头和闪电走独立图。梅赛德丝 S3 的 `additions` 保留从 `skill3/cutin` 骨骼事件补回的立绘。编辑或重导时应保留此来源。绮世 S3 三条 NODE_ANI 分别控制施术者路径和受击身体路径，其中目标路径携带 `flying_damage`。夏绿蒂 S3 固定使用满斗志的 `_05` 分支；其余旧图的不同 HIT 数与立绘变化待接入。
- 拉斯 S2 的夹攻和梅赛德丝 S2 的额外施放由权威动作事件触发。预览单张源图的时长不含后续动作。绮世 S3、亚敏 S3 的混色目标位、部分 LAYOUT 的位移与淡出、空源 SHOT 的弹道、PPEFFECT 的随机采样规则仍有原引擎语义待验证。

## 本次核对

从 [技能配表](../../Config/Tables/battle_skills.csv) 解析每槽 `presentationId`，对照音频目录和 [导入清单](../../Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json)。只读比较 `D:/sevenepic/原始资源/stagept` 的 30 份 STG SHA-256 和项目图输出哈希，检查图依赖地址、转换资产哈希与立绘帧时长。按 [BattleSkillPresentation](../../Assets/Game/Modules/BattleScene/Scripts/BattleSkillPresentation.cs) 规则离线展开可达节点、骨骼和模型 timeline，复核亚敏 S2 的七次 HIT、绮世 S3 的四次、夏绿蒂 S3 的 15 次。全表得到 125 个可达 HIT 节点和 8 段 CUTIN。本次证据为静态资源和规则核对；逐帧渲染、录音、设备与原版画面对照沿用各自的独立验收。历史实测见 [十名角色交付](2026-11-13-ten-hero-roster.md) 和 [十角色源审计](2026-11-13-ten-hero-source-audit.md)。
