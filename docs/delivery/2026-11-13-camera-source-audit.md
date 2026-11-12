# 战斗演出源数据复核

基线为主工作区 main 的 `76e2303`。核对了九个原始 STG、角色 timeline、关联 CFX、已转换的 Spine 动画、源 shader、Forest2/3 图集与 Unity 作者布局。初始调查为源码及数据分析。后续 Unity EditMode 位移复验见末节，Player 构建和原版连续帧验收未执行。

## 基线舞台取景

- 当前地图作者坐标以 1280×720 左下原点定义，`World(x,y)=((x−640)×.015,(y−360)×.015)`。16:9 世界镜头基准中心为 (0,0)，正交半高 5.4，取景 19.2×10.8。该坐标由 Unity 项目作者工具定义。STG 偏移固定按 .01，Spine 导入描述同样使用 .01，角色再乘场景作者缩放。原作未给出 STG 和舞台作者坐标之间的换算，不能把 .015 或 .01 当成所有字段已验证的统一比例。
- 当前 `CAM` 0 返回基准镜头，1/11 用施法者脚点，5/12/16 用主要目标脚点，31 用施法者与首目标中点，32/41/42 用目标平均脚点。近战 `CAM12` 与 `MOVE16` 成对出现在拉斯 S1/S2、安洁莉卡 S1。梅赛德丝 S2 的 `CAM41`、`EFFECT42` 同时出现，安洁莉卡 S3 在同一次演出反复用 `CAM32`。数字相近或目前落在同一分支只证明项目当前行为。各锚点的高度补偿、相机零点、动态跟踪以及 41/42 的选组规则仍待原帧或引擎数据证实。
- CAM 偏移目前不镜像。MOVE 和 CFX 的 X/旋转按施法者与 primary 相对 X 判号。治疗后排或自己时这个判号不等于单位阵营朝向。模型根 `faceLeft` 已给敌方骨骼做镜像，演出需要一个稳定的阵营朝向参数，并分别定义有方向的局部偏移和世界锚点。梅赛德丝 11/+200→16/−150、安洁莉卡 31/−200 是验证左右镜像的有符号样本。
- Forest2/3 运行时六层是从源 `bg/forest{2,3}.atlas` 裁块后手调的 SpriteRenderer。源目录另有对应二进制 .sca 与层动画，当前 BattleScene 不解析它们。Forest2 的 sky 原块 467×161，按 PPU100 及 prefab 缩放后为 19.23×7.68，世界 X 仅约 ±9.615。ground 原块 963×256，宽约 27.535。基准相机已经几乎耗尽 sky 横向余量。例如可见地图上的拉斯 S1 镜头随远处目标落在 x≈7 时，1.3 倍相机右边界≈14.4，超过 sky 和 ground 的作者边界。背景的可覆盖范围必须以可见底图为准，旁边树冠不能扩大可用覆盖。需要按相机半宽/半高与实际覆盖范围约束镜头，或让地图填底层随镜头覆盖并保留作者的平行视差。单纯约束中心对视野超过图层范围的情况无解。
- `ZOOM` 现在按基准正交尺寸除以倍率，原有 .8/.6/.5 均会扩大取景。应保留全屏技能的宽视场，地图可见阶段再验证约束。拉斯 S3 的 .8/.6/.5 全发生在森林隐藏期间。当前调度的 CAM 回零、ZOOM 回 1 与 BGSHOW true 有 200 ms 相隔，地图重新显示时理论上已回到基准取景。不能从 .5 数值独立推断 S3 可见地图露边。梅赛德丝 S3 的 .8 同样位于其隐藏地图段。

## 九技能源节点与基线表现

- 拉斯 S1 有 jump_forward 和 jump_back，Spine 的前跳 jump/landing 为 .4666/.8666 s，后跳为 .0666/.4666 s。原图 `MOVE16/style2/power50`、`CAM12/y−30` 和 ZOOM 1.3 来自前跳 ANI_SCOPE，后跳 scope 接回 `MOVE11/style2/power50`、CAM0 和 ZOOM1。当前冲刺从零开始，时长取主动画起点，目标距离固定 .9 世界单位，抛物线高度固定 .33。两次 HIT 为主动画后的 266、566 ms，CFX 同时触发 `ras_re_cm_sk1`。
- 拉斯 S2 的主技能骨骼 jump/landing 为 .8/1.2 s。源 `ONEVENT skill2/jump→ANI_SCOPE→MOVE16/style2/power50/time350`，同组 CAM12/y−30 和 ZOOM1.3 均为 350 ms。当前 MOVE 独立按 firstHit−.35−.12 安排，约为主动画开始后 .996 s，与源图关联丢失。源跳回同样指定 MOVE11/style2。HIT 在 1466、1766、2733 ms，末击触发水平 blur。除 STG 的 `ras_re_cm_sk2` 外，`model/rasselclare.timeline` 还在 670、1330 ms 明确指定两段 shake，当前 timeline 只执行 effect。带 `extractNodes` 的 effect 和 fire 标记仍需单独追踪语义。
- 拉斯 S3 从 #9 并发进入 #5 LAYOUT(away_x=5000, fade/time=200)、#6 CAM(1, y−30, 200ms)、#10 ANI 与 #37 准备 CFX。#5 启动时隐藏森林，200ms 后进入 1.2s BGHATCH。骨骼 jump/jump_end=1.0/1.4s，#7 scope 接 #8 CAM(1,y+50) 与 #11 MOVE(11,style1,h1000,540ms)→#13 MOVE(11,style3)→#14 IDLE。两个 MOVE 均无 x，当前 BattleScene 仅处理 `location=11 && x != null`，三者都没有执行。
- 拉斯 S3 的 CUTIN #19 从 1.848s 播放约 1.250s，随后 #26 CAM(42,−300,+200,瞬移) 接 #16 CAM(42,0,−50,200ms)，并将 ZOOM 拉到 .8。#20 爆炸 CFX 约在 3.498s 开始，倍率 .6 在 3.861s 开始、4.224s 完成，倍率 .5 在 4.554–4.555s。#20 的两段 Spine 分别约 2.2666s、1.0666s，另有三个延迟 1500ms、Y=570/800/1070 的粒子。当前未声明 lifeTime 的粒子回退 1.8s，使项目估算 #20 完成于 6.798s、#22 布局恢复 6.998s、#25 BGSHOW true 7.198s。该完成时间依赖项目回退，需原帧核对。#20 并行的 #21 camera 骨骼 translate 原键持续 2.2666s，X 峰值 86.64、Y 峰值 180；第一/最后一组 HIT 分别带 #33/#34 blur。
- 梅赛德丝 S1 先 CAM11/+200，再 CAM16/−150 后归零。两组目标 CFX 用 `location=6,locationParam=shadow`，命中附带 `mercedes_cm_sk1`。S2 先 CAM11/+200，再 CAM41/+100 后归零，雷球与尘雾均用 `EFFECT42/x−72`，并有 `cm_magic_lightningball` 和 blur。S2 实际 ANI 名为 skill1，源 ONEVENT 却写 skill2/hit，HIT 有独立时间连线，不能按显示技能号替换 ANI。
- 梅赛德丝 S3 先隐藏地图，LAYOUT 的 self_x=.1、away_x=150、log=10、fade/time=200；CAM11 的 X 依次 50、500、300，后两段 Y=−100。其 MOVE11/style1 为 x−144、200ms、log=50，当前仅这段进入 BattleScene 的近似 MOVE 分支。主 CFX 用 `locationParam=root`，包含 `mage_sk3_cm` 摇镜和 blur；BGHATCH 有 x=.1、scale=1.5、三张纹理独立速度。最后 mode1 恢复，CAM/ZOOM 回到基准。
- 安洁莉卡 S1 与拉斯 S1 共用 MOVE16/11 style2、power50、两段跳跃 scope；其 CAM12 多 X−20，ZOOM1.4。S2 用 CAM31/x−200/y−20 与目标 `EFFECT6`，timeline 另有施法者 CFX，HIT 不指定命中特效。S3 的 CAM32 在 Cut-in 前后沿 Y−30→+10→−30，缩放 1.4→1.3→1；目标 CFX 后 +600ms 开始方向为 (0,−1)、ratio=.3 的 blur。安洁莉卡的治疗不应由几何左右判定角色朝向。

## 基线执行边界

- 原 STG 和 timeline 中的 `time/delay/fade` 是毫秒，Spine keys 与导入 Cut-in 帧是秒。播放器应保留节点拓扑、同刻顺序、零时长、显式延迟及骨骼事件，镜头平移和缩放各自插值。依赖 MOVE/CFX 完成的连线要消费同一个动作的实际视觉持续时间。原图中的 when=2 连接只见于这里的 PPEFFECT→SINE_VALUE；着色器没有正弦公式，需验证其相位/振幅/作用方式。
- LAYOUT 当前只硬切 Renderer，暂时隐藏非参与者，abs(away_x)≥1280 时连目标也硬隐藏。原源值能证明偏移、持续与淡入淡出参数存在。对 2554 份 STG 扫描发现 641 个默认/0 mode LAYOUT，away_x 缺省 329、常见 500/200/2000/150/300/100，也有负数，self_x 缺省 572。截断阈值、有限坐标是绝对还是相对、阵型收拢及透明曲线需要连续帧。执行器可使用独立的表现层编组、位置和可见性，保留权威槽位，并在 mode1、取消、下一动作、卸载恢复。
- CFX 的 `locationParam=shadow/root` 当前被丢弃；HIT Y 固定取动态 renderer.bounds.center，offset/rand 再与 CFX 局部偏移叠加。九技能 HIT 的 offset 均为零，无法据此证明非零偏移单位和锚点。原 CFX 的 signed X、target 群组锚、骨骼跟随、各层 localZ/globalZ 以及非均匀 scale 应保留；当前 primitive 深度只看 globalZ 正负，使同号的 1、1.2、1.4、2 排序合并，`z` 不生效。
- CharacterLab 已有基于 camera 骨骼 translate 的 additive SHAKE、应用前撤销上一帧 offset 的逻辑及 shader 移植。BattleScene 不执行 SHAKE、PPEFFECT、SINE_VALUE。生产可复用同类时序，把 shake 叠加在无抖的 pan/zoom 基线上，完成和取消清零。type/powerX/powerY、HUD/场景后处理边界仍需对照。`sprite_blur.frag` 源文件能证明按分辨率归一的方向、三角权重、样本上限 5 与 ratio clamp。`sprite_hatch.frag` 能证明三纹理各自沿 X 采样并相加，原引擎的速度积分与 BGHATCH 的 x/scale 变换需额外证据。
- BGSHOW 的时长目前只影响图调度，视觉仍立即切换；DARK 源 STOP 的渐退可能被动作收尾清除。地图／前景／HUD 的过渡要与显示相位及动作生命周期一起验证。

## 候选回归

1. 使用真实 16:9 Forest2/3 取景与最靠两边站位，取样拉斯 S1/S2、梅赛德丝 S1/S2、安洁莉卡 S1 的 CAM/ZOOM 开始、中点、完成与回位。地图可见的每个样本视野均被底图覆盖。拉斯 S3 黑背景保持 .8/.6/.5 的艺术取景，重新显示森林时回归覆盖约束。
2. 对拉斯 S3 在 0、.2、1.4、1.848、3.098、3.498、3.861、4.554 秒做时间切片。检查非参与者、镜头、MOVE/IDLE、Cut-in、CFX 和 SHAKE 均由原拓扑驱动；用拉斯 S1/S2 和安洁莉卡 S1 验证同类 style2 前后移动及跳事件相位。
3. 将阵营及阵型关于舞台中心镜像，用有符号 CAM/CFX 以及自疗和友方治疗检查镜像 X、保持 Y、稳定施法朝向。验证 shadow/root/hit 锚点在施法者与目标相互移动时跟随正确。
4. 执行 LAYOUT 巨大/有限/缺省偏移、切换 mode1 及中断。检查视觉参与者、阴影、动作站位、可见性恢复，确保逻辑槽位和已击倒单位状态没有被表现修改。
5. 并发 CAM、ZOOM 与原始 SHAKE，比较细帧与跨 cue 大步进，覆盖最短/最长队列、取消、下一动作。PPEFFECT 由真实 STG 连线触发，核对 HUD 与目标渲染层，并逐帧对照原版 blur 覆盖与幅度。


## 用户视频核对

2026-11-13 核对了用户给出的视频第一段。B 站元数据的发布时间为 2026-09-19 00:43:59（UTC+8），画面未显示客户端版本。约 3:08 是拉斯正常站位，3:10 中间技能按钮显示燃魂选中，3:12.5 左上显示「指挥一击」，3:13 拉斯接近对侧，3:16 恢复两侧并显示 11 Hits。这些画面用于核对燃魂 S2。960×540 的 3:13 对照帧中，对侧目标脚点约为 y=469，仍保留底部约 13% 的画面，地图保持覆盖。S3 的判断依据为原 STG 和本地素材，仍需相应录像核对。[用户提供的录像](https://www.bilibili.com/video/BV16ZeU64E3Z/?t=188)


## 版本与寿命补证

- `model/rasselclare.timeline` 随源 `rasselclare.scsp`，与本项目导入的 `model/ras.scsp` 分属两个模型。旧 timeline 的 `rass_skill_02` 与 `rass_skill_02_2` 不在当前 Ras STG 的依赖闭包，当前采用 `ras_new_skeff_sk2*`。上述 670/1330ms 旧 shake 只作字段证据。运行时应解析声明在当前图闭包中的 timeline，当前拉斯 S2 实际摇镜来自 STG。
- 拉斯 S3 的第一组粒子中，最晚有限发射上界为 .9s 开始 + .5s 发射 + 1.3s 最大粒子寿命，CFX primitive 再延迟 .1s，合计 2.8s。后三组 burst 的 `duration=-1` 不代表持续发射，各自在 1.5s 延迟后发出一次，最大寿命 1.2s。这个上界按现有粒子执行器的发射模型计算，能同时约束粒子释放与 CFX 完成连线。它仍需原画面对照。


## 本次位移回归

Unity 2022.3.62f3 在主工作区的 `.artifacts/UnityBattleScene` 隔离副本执行 EditMode。用例以原 Ras STG 和 Spine 事件驱动 `BattleSkillPresentation`，在 `battle-rules-red.xml` 中，两项分别失败于 S2 起跳后仍在原位和 S3 高度始终为零。`battle-rules-green.xml` 中两项均通过，并核对了同场景影子和 S2 的三个源 HIT 时点。位移回归的结果不代替原作画面对照，MOVE 插值、50 的 power 及 1000 的 h 所对应的原引擎曲线仍待验证。


## 源后处理补证

扫描与 PPEFFECT 或 SINE_VALUE 相关的 296 份原 STG，读取到 588 条连入 SINE_VALUE 的边，全部是 `PPEFFECT(sprite_blur) → SINE_VALUE(u_range), when=2`。其中 482 条 `start_rad=.5`、73 条缺省，其余为 .1/.2/.3/.8，`finish_rad` 均为 1。拉斯 S3 #33、#34 各有 300ms blur 以及各自绑定的 `u_range` 调制。原 shader 定义方向、分辨率、三角权重、最多 5 个半区采样和原图 ratio，未定义正弦时间公式。

当前 BattleScene 以指定后处理节点为父，消费其源 when=2 子节点，同步使用各自时长。预览以 `range + power × sin(π × lerp(start_rad, finish_rad, progress))` 重建范围包络，使常见的 .5→1 从峰衰减，范围钳到非负。缺省 start_rad 取 0。缺省 u_ratio 沿用角色实验室的预览值 0。取景镜头仍维持 HUD 绑定，短期启用一台按同构图采集世界的 URP 相机，合成到世界与 HUD 之间，动作结束或中断释放。原引擎的相位函数、叠加方式、缺省参数和影响渲染范围仍需原画面对照。


## 共用规则落点

本轮根据已确认的源图及录像改动了通用解释。style2 MOVE 使用命名起落事件，style1 的时间和 h 参与纵向轨迹，style3 回位。镜头使用动作开始时记录的脚点构图和运动中的表现位置，保持显式 CAM 与 ZOOM 的时序。在地图可见时限制实际取景，全屏阶段使用源请求取景。原始相机骨骼通过同一个源时钟驱动摇镜，地图与 HUD 的释放路径独立于权威战斗状态。具体执行契约见[战斗源演出接入规范](../development/battle-source-presentation.md)。

尚缺有限 LAYOUT 坐标及淡变、MOVE 轨迹和倍率、SHAKE 幅值字段、SINE_VALUE 的原始公式和完整渲染范围、CFX 各层排序及非零 HIT 偏移锚点的验证。原始九技能没有每击权重。现行客户端将单个权威伤害事件的总值按源 HIT 时点累计分配，保持总值并用于浮字和血条。录像中的 11 Hits 覆盖了所见的战斗演出，无法确定其每击权重和额外命中来源。
