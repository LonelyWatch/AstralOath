# 安洁莉卡 S3 时钟与连续镜头复核

日期：2026-11-13。资料在 D 盘主工作区 `.artifacts/battle-timing-reference`，不提交媒体。此报告区分已读源数据、实际查看的视频、项目解释和测试证据；不代表三名角色已逐帧还原通过。

## 视频来源与实际查看

用户指定 [BV1VB4y137Dt](https://www.bilibili.com/video/BV1VB4y137Dt/) 为「【第七史诗】水奶 安洁莉卡台词 中日双字幕 CV田村由加利」。公开元数据发布时间为 **2022-09-06 09:10:26（UTC+8）**，总长约 231 秒，CID 825101585。这是旧录像，未显示可核实的客户端版本号，不能作为当前国际服版本证明。

页面请求返回 412；使用公开 view/playurl API 成功读取元数据及视频，没有登录、付费或访问控制绕过。保存 `view.json`、`play.json`、`angelica.mp4`。以 FFmpeg 从 52 秒起每 0.25 秒取帧，另从 52.5 秒起每 0.1 秒取帧，实际查看 `s3-sequence.jpg` 与 `s3-detail.jpg`，并核对后两次重复播放。

第一段可见过程（视频绝对时间，取样误差约 ±0.1 秒）：

- 约 52.9–53.0 秒：起手，舞台压暗，角色抬起。
- 约 53.5–54.8 秒：Cut-in 从胸口特写到挥杖，约 1.3 秒。
- 约 54.9–55.0 秒：回到舞台，抬杖及脚下光圈发展仍清晰可见。
- 约 55.6–56.4 秒：治疗光柱自强光到粉色粒子衰减。
- 约 56.5–56.8 秒：角色落下、地图逐渐显露；约 57.3 秒取景回到普通构图。

这些样本用于相对阶段和时长判断；低码率、作者字幕和录像剪辑不足以证明逐像素坐标、颜色和全部镜头缓动公式。

## 本地源数据

`c_angelica_skill_03.graph.json` 的 ANI skill3 从源时钟 0 开始。模型 skill3 最大关键帧为 2.2665999 秒，骨骼 cut_in 事件为 0.6000000 秒。CUTIN #12 通过 ANI 的开始边加 600ms 启动；转换帧总时长 **1.28 秒**。

独立于 Cut-in 完成边，目标 CFX #5 在源时钟 **0.73 秒**启动；PPEFFECT #17 在其开始后 600ms，即 **1.33 秒**启动，持续 0.5 秒；HIT #11 在目标 CFX 开始后 1660ms，即 **2.39 秒**。DARK #4 从源 0 开始，字段 duration=2500、fadeIn=200、fadeOut=700；duration 与 fadeOut 的关系另见下文的连续帧复核。CAM32/ZOOM1.4 为起手 100ms；Cut-in 后 100ms 接 ZOOM1.3（700ms）与 CAM32/y+10（800ms），随后 CAM32/y−30（900ms）。ANI结束进入IDLE，再1000ms后以200ms CAM0/ZOOM1归位。

目标 CFX 的 Spine 时长分别为：`angelica_sk3eff_target_b/animation` 1.9666 秒、`animation2` 2.6333 秒、`angelica_sk3eff_target_f/animation` 2.7333 秒；原 CFX 还包含延迟1.0、1.2、1.4、1.6秒的粒子。施法者 CFX `angelica_sk3eff_self` 为1.9秒，当前角色 timeline 于 skill3 开始时启动。

## 演出压缩根因与修订口径

旧实现让 CUTIN 与 ANI、CFX、DARK、后处理、镜头在同一时钟继续运行。由此目标 CFX 的前 **1.15 秒**（0.73–1.88）被 Cut-in 遮挡，1.33–1.83 的 blur 整段发生在遮罩后。用户看到的 Cut-in 后阶段已经进入后半段，形成过快、被截短的感受。源数据本身并没有要求把安洁莉卡整体慢放。

视频中 Cut-in 后仍能看到完整抬杖、光圈、光柱，与 **Cut-in 单独消费墙钟，冻结舞台源时钟** 的解释相符。本次按此通用规则执行：暂停角色与表现特效，只推进 Cut-in 帧；结束后恢复原源时钟。CUTIN完成边在源时钟上为零耗时，实际墙钟等待由冻结承担，避免后继再多等一次1.28秒。HIT、CFX延迟、CAM/ZOOM时长仍沿原源数据，不按角色ID改速。

因此本技能HIT仍为源2.39秒，墙钟约3.67秒。DARK 的2.5秒字段对应开始渐退，墙钟约3.78秒；加0.7秒fadeOut后退出，墙钟约4.48秒。这与视频的光柱末段、地图开始显露和完全回到普通画面的先后关系一致。录像只能支持这组样本的时钟解释，其他版本和新增节点仍需按通用规则补证。

## CFX 显式动画名复核

`angelica_skill_03_target.cfx` 的第五个 primitive 使用 `source=angelica_sk3eff_target_b`、`ani=animation2`、`globalZ=-1`。同一CFX的第一个primitive省略ani，使用同资源默认animation。这两个实例不是重复资源的冗余项：`animation` 在0秒关闭 eff_outline_1..10、flash 与 flashn，约0.7666秒开始烟雾；`animation2` 在0秒开启这些描边与flash并关闭烟雾。其时长分别为1.9666与2.6333秒。

原播放器只按animation/idle挑选，丢弃primitive.ani，会选错该实例的动画、重复默认动画内容，并缺少显式描边/flash层。应按每个primitive的ani选择并用实际动画时长计算寿命。不能把这个差异概括为“重复了主光柱”，也不能通过角色ID、亮度倍率或隐藏其他友军掩盖。四名受益者各播一套CFX符合当前图location6；为与参考视频单友军画面公平比较，测试另输出`angelica-solo-clock`，它只改变测试受益者集合及可见演员，不代表产品应隐藏其他友军。

## DARK 持有与渐退的独立时长

先前文档把2.5秒称为DARK总寿命，该解释在连续帧复核后不成立。实际查看`dark-release-detail.jpg`（55.6秒起，每0.1秒采样）：55.6–56.5秒的舞台仍近黑，约56.6秒第一次隐约看到建筑、56.7秒地图清楚显露，约57.3秒回到普通地图构图。以52.9–53.0秒起手和1.28秒Cut-in对齐，开始渐退约在墙钟3.7–3.8秒，符合源duration2.5秒加Cut-in；原实现却在源(2.5−.7)=1.8秒开始渐退，即墙钟3.08秒。Unity单人捕获在wall3.2已经显露森林，证明差异不是四目标特效叠加造成。

只读扫描原始stagept的DARK节点得到1853个，其中759个有限duration、135个duration小于fadeOut。例如`c_alensia_skill_03`节点39为duration100/fadeOut300/opacity1，`c_belian_skill_03`节点20为200/800/opacity1，`c_achates_m2_skill_03`节点25为500/1000/opacity.7。旧“总寿命包含渐退”的公式会让这些节点刚出现便被压低透明度，连请求的opacity都不能达到。扫描摘要保存在`dark-source-summary.json`，未导入外部角色资源。

以上录像相位与跨角色源字段共同支持通用解释：有限DARK从开始到duration保留淡入及持有阶段，然后单独消费fadeOut，最终寿命为duration+fadeOut；duration=-1继续持有，直到STOP开始渐退。已有的无限DARK STOP分支本就将fadeOut加在STOP之后。本次舞台层释放与图完成时间按该规则同步修订；STOP与自然渐退取先到的起点，避免对同一层叠乘两次退出包络。这里仍使用线性淡入淡出；源accel与原引擎混合空间没有新增验证，不扩展为“全部DARK语义精确还原”。

## Cut-in 透明区域与幕布

实际查看本轮单人及四人GPU图板后，时序现已显示：wall0.6–1.88为Cut-in，之后完整抬杖/法阵/光柱，wall3.8开始地图渐退，与原视频主要阶段相符。四人版有四套目标CFX；单人版有一套，不能把正常受益者数量差异当作特效错位。

另一个独立问题是Cut-in透明区域的合成。当前单人GPU wall1.6帧里，小模型安洁莉卡出现在大Cut-in角色头顶，普通顶栏与HUD也透出；四人版会透出更多友军。原视频54.5秒同一挥杖姿态（`cutin-tail-original.png`）的对应区域为黑色，没有小模型或普通HUD。单人样本本身已经能证实差异，并不需要假定“多目标时应隐藏队友”。

只读扫描442个CUTIN节点：391个有`curtain_color`，128个有`curtain_opacity`。颜色为黑342个、白44个、其他彩色/带alpha5个、缺省51个；opacity缺省314个、显式0为125个、显式.8为3个。普通Angelica和Ras省略幕布字段，Angelica皮肤节点明确黑色；其他技能显式0保留透明，证明不能一律隐藏舞台演员。摘要见`curtain-source-summary.json`。

本次按这些证据实现独立Cut-in幕布：缺省黑色、opacity1；显式颜色与透明度按源值合成，颜色alpha乘opacity；显式0不产生遮挡。幕布覆盖视口，位于普通HUD之上、动画帧之下，与Cut-in共用独立墙钟和释放路径，并排除sprite_blur世界采集。暂停控制层仍高于Cut-in。`PlayCutinWithNode(asset,node)`接收源配置，旧`PlayCutin(asset)`保留为缺省入口，避免旧反射调用同名重载歧义。该规则不改任何演员Renderer的enabled状态。

WebP本身的ANIM背景信息不能代替幕布：当前Angelica原文件的背景元数据为RGBA(193,194,147,126)，Ras为(0,1,8,1)，尾帧均有真实透明像素，不能把它们臆改成不透明黑底。保持原帧，解析STG专门的幕布字段，更符合已有配置与实际画面。

## 镜头连续性

镜头原来将源请求位置/倍率保存在 `basePosition/requestedSize`，地图覆盖约束后实际显示的是另一组数值。下一段 CAM/ZOOM 却从未显示过的请求值开始插值，造成一段被约束卡住的时间，随后突然开始移动。修订为每段从实际镜头位置（剔除当前摇镜）和实际正交尺寸起步，终点仍采用源锚点、源倍率与曲线。源 `time=0` 的明确硬切保留。

夹攻使用 `BattleCinematicStage.HandoffTo(next)` 交出舞台控制权：保持此刻实际镜头，传递真正的作者基准与地图覆盖元数据；退场舞台后续 CAM、ZOOM、Tick、Reset 不再覆盖新动作。新动作的CAM0/ZOOM1依旧返回作者基准，不能把夹攻开始时的近景误认为基准。动作中断/退出仍有立即恢复路径。

缺少源归位节点时使用 `ReturnToBaseline(seconds)` 从实际取景平滑回作者基准。调用层的0.2秒是项目通用收尾策略，不是从录像推导出的原引擎参数；已有源CAM/ZOOM归位的动作保持源时序。

## 复验状态

`BattleCutinCurtainTests`新增7项配置、层级与生命周期回归，协调者在`.artifacts/battle-curtain-red.xml`确认7项均因缺幕布/源节点入口失败后再实施。实际GPU参考是`.artifacts/battle-timing-captures/angelica-clock`与`angelica-solo-clock`的0–5.2秒、0.1秒间隔截图；原视频提帧与源统计位于`.artifacts/battle-timing-reference`。最终幕布画面与全套GREEN以本轮总验收结果为准。


新增`BattleDarkLifetimeTests`直接读取原Angelica DARK，并覆盖短持有长渐退、无限STOP和图完成时间。协调者在`.artifacts/battle-cfx-dark-red.xml`实际运行：DARK的两项有限视觉寿命、两项有限图完成时间按预期失败；无限STOP与无限图完成通过。本节记录失败基线，最终GREEN及全量复验由总验收补充。


协调者已运行原镜头代码的失败回归：`BattleCameraContinuityTests` 的受约束CAM/ZOOM续接、交接接口、平滑归位四项失败，源零时长硬切一项通过；后追加Cut-in独立时钟回归检查帧推进而CAM与DARK不推进。生产实现已加入这些接口，最终EditMode/PlayMode结果由本轮总验收记录补充。不得把本报告的代码分析或旧录像查看当作当前Player视觉验收。

FFmpeg取样示例（输出路径相对D盘主工作区）：

```powershell
ffmpeg -ss 52.5 -i .artifacts/battle-timing-reference/angelica.mp4 -t 5 -vf "fps=10,scale=426:-1,tile=10x5" -frames:v 1 .artifacts/battle-timing-reference/s3-detail.jpg
```
