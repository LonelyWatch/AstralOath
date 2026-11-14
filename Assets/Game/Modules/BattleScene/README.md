# BattleScene

战斗场景使用原作 HUD、两套森林背景和双方各四个站位。默认八名英雄互不重复，使用同一个 `Res/Prefab/BattleActor.prefab` 的八个实例。相关规则及画面证据见[战斗资料](../../../../docs/development/epic-seven-battle-reference.md)。

模拟战在线入口接收服务器创建的 4 对 4 对局，按本方视角显示阵型。蓝光闸门遮住加载，随后在原版决斗场背景上播放下落、落地烟尘和 IGNITION。参见[联机选禁与入场](../../../../docs/development/arena-draft.md)。

## 打开与编辑

1. 在 Unity 使用 `Tools/战斗场景/打开场景`，或打开 `Scenes/BattleScene.unity`。
2. 点击 Play 自动开始。Game 视图可用 16:9、全面屏横屏或平板比例。静态舞台和界面在编辑时可见，Spine 网格在 Play 时创建。
3. 双击 `Res/Prefab/BattleHud.prefab` 可直接编辑。顶部按钮、行动条、单位血条、当前角色、魂力和技能分组排列。通过 `Tools/战斗场景/验证 HUD 绑定` 检查引用。
4. 白天舞台使用 `Res/Prefab/Forest2Stage.prefab`，暮色舞台使用 `Forest3Stage.prefab`。替换场景中舞台实例即可换景。每个图层可单独调整。角色脚点在 `Units/Allies` 与 `Units/Enemies`，每方有前、下、上、后四个组。编辑态信息条使用HUD作者摆位；运行时按动画top挂点投影跟随人物及镜头缩放。

`BattleSceneBuilder.Generate` 是可从隔离工程调用的作者工具，会重建示例场景、三个预制体和生命数字位图字体。编辑好的正式布局以保存的 Unity 资产为准。其完整批处理方法名是 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.Generate`。

技能演出编辑器的时间轴按帧显示。重叠片段在同轨道的不同子行可单独点选，悬停同时显示播放帧和源时点。双目标演出可换边预览，支持撤销整套还原。见[编辑器操作](../../../../docs/development/battle-skill-timeline-editor.md)。

## 结构和后续接入

- `BattleHud` 继承 `UIBaseView` 并验证 BindingMap。场景 Canvas 上的 `BattleHudPreview` 管理普通 C# `BattleHudBinder` 生命周期。预制体使用 GameText、GameButton 和 BindingMap。Burn 与三枚技能按钮接受点击，布局模式的魂力与角色固定为当前拉斯。
- `BattlePreviewActor` 通过现有 SkeletonPlayerFactory 读取原版骨骼及图集，进入 Play 时生成，禁用或卸载时释放网格和材质。每个 `BattleActor` 实例用 `Hero Id` 指定角色。站位保存朝向、大小和排序，角色模型、技能、头像、语音及战斗面板从现有角色目录与配表解析。
- `BattlePreviewViewport` 在安全区内保持 16:9，世界与 Screen Space Camera HUD 使用同一摄像机。HUD 作者坐标以 1280×720 左下原点记录，生成时乘 1.5。地图图层位置和站位来自对照后的手调。Forest2／3 的草地按原宽高比布置，底边固定在设计坐标 Y=-2，连续草带覆盖八个站位及其影子。替换舞台时用各槽位的世界坐标核对可站草带。梅赛德丝的待机保留原骨骼悬浮。
- 场景通过独立试玩入口和直接资产引用运行，正式启动和 YooAsset 收集另行接入。正式流程接入时另定资源地址、数据契约和服务端准入。

## Burn 和技能选中预览

- 进入 Play，Burn 播放原版入场和 0.6666 秒闪动。普通技能选中叠加 `hud_skill.csb` 的 `selected` 图层（`cm_select_skill.png`，114×120，中心相对技能图标左 3、上 4 个设计像素）、Spine 闪动和粒子。切换时清除上一枚的效果。
- 点击 Burn 立刻播放魂力闪光并切换为 Cancel，同时拉斯的 S2 播放燃魂起手。231 毫秒后出现蓝色燃魂描边与粒子。点击 Cancel 中断闪光并解除燃魂，刚才选中的燃魂技能继续以普通样式选中。
- 这段说明适用于进入 Play 后的布局模式。试玩模式的消耗、目标与技能结算见下节。关闭或隐藏 HUD 时释放粒子、骨骼与按钮监听。
- `Res/Effects/Hud/source.json` 记录原版 SCSP、SCT、atlas、CFX、粒子和按钮 PNG 的来源及哈希。Git 保留原版 atlas 的字节及换行。使用项目素材 Python 环境运行 `Tools/BattleScene/ImportHudEffects.py --check` 验证，去掉 `--check` 复现素材。Unity 编辑器批处理入口 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.InstallHudEffects` 只更新 HUD 点击配置与场景组件，可用于保留已编辑的站位和布局。
- 源 CFX 的顺序、描边颜色和 Spine 曲线沿用原数据。粒子在 Unity 中按原参数播放，专有的发射模式按爆发与持续发射重建。视觉外观参考原版战斗截图，按钮和技能锚点按当前布局对齐，原作连续录像逐帧对照仍待验证。[原作战斗截图](https://d3kxs6kpbh59hp.cloudfront.net/community/COMMUNITY/718883b8c9f34eaca8e89ee116e1e29d/1f0480fa6b5144a584877ad1c1d10df7_1617085685.png)
- Unity 2022.3.62f3 实际 HUD PlayMode 回归 2/2，EditMode 215/215。PlayMode 带图形运行 `BattleScenePreviewTests`，设置 `ASTRAL_BATTLE_CAPTURE_DIR` 可导出 Burn、技能选中和使用反馈的 1920×1080 截帧。

## 战斗试玩

进入 Play 后自动开始本地试玩，无需点击入口按钮。单击技能可手动选择友军或敌军。0.5 秒内再次点击同一技能时，AI 仅代选该技能的目标。敌方自动行动；右上 Auto 可将己方交给同一套 AI。燃魂准备期间保留已选技能，只允许该技能出手或使用 Cancel。点击 Burn 准备时播放原版 FMOD 音效，实际魂力仍在接受施放指令时结算。
技能冷却、生命、状态和魂力按共享规则更新。右上暂停提供继续与重新开始；编辑器菜单 `Tools/战斗场景/开始试玩` 仍可用于重开。退出并重新进入 Play 会创建新局。

已导入的 STG、CFX、Spine、粒子与角色 timeline 驱动动作、位移和命中特效，FMOD 在 Windows 播放原版技能音效和日语语音。敌方回合隐藏技能组和 Burn。行动条头像按行动值从上向下移动，当前行动者在下端；点击左侧沙漏展开原版行结构的行动值、生命、技能冷却与状态，点击空白关闭。技能冷却使用贴合图标的遮罩与回合数，伤害与治疗使用独立浮字。动画消费有序事件，状态由同源规则决定。试玩场景依赖共享战斗程序集，`BattleScene` 已登记为 HybridCLR 业务程序集。双人 HTTP 验证、规则口径及当前差异见[战斗第三步](../../../../docs/development/battle-execution.md)。

非行动角色循环播放 `idle`；轮到角色时单次播放 `b_idle_ready`，随后循环 `b_idle`。受击或技能可以中断入场准备，同一回合恢复时沿用当前待机。多段命中逐段播放受击、护盾消耗、绿条扣血和飘字，同一目标的红色损血区间在末段命中后才开始收缩。死亡由已接受的事件触发，末击动作继续，角色逐渐变黑，再与头顶信息一同淡出，粒子保留尾迹。见[战斗演出修订](../../../../docs/delivery/2026-11-13-battle-visual-refinement.md)。

## 魂力和战斗说明

- 标准魂力上限 80，每满 10 点点亮一个原版蓝色火魂。剩余点数显示在进度槽，燃魂按权威事件消耗。原版 Burn 和 Cancel 在对应阶段暂时覆盖右侧槽位。
- 长按底部技能、角色头顶状态或行动条详情图标查看说明。技能附带关联状态、夹攻和燃魂信息，主技能区还展示强化列表。松手、拖离或取消按压立即关闭说明，长按松手不触发施法。
- 世界铺满横屏，互动 HUD 对齐安全区。高宽比、技能标题和音频的验证见 [说明与录像对照](../../../../docs/delivery/2026-11-14-battle-inspection-ui.md)、[全面屏](../../../../docs/delivery/2026-11-14-battle-wide-viewport.md)、[魂力](../../../../docs/delivery/2026-11-14-battle-soul-gauge.md)、[日语变体](../../../../docs/delivery/2026-11-14-battle-voice-variants.md)、[技能标题](../../../../docs/delivery/2026-11-14-battle-skill-banner.md)。

## 头顶状态与护盾

头顶 Buff 图标与回合数字保留源分辨率和无压缩，使用双线性采样缓和跟随镜头时的非整数缩放锯齿。行动条详情状态图和等级贴图沿用点采样。等级显示使用原 `itxt_num*_b` 位图和 `hp_bar.csb` 的位置；左下 HP 和伤害字体维持各自字体。AI、双击、资源及验收见[本次交付](../../../../docs/delivery/2026-11-13-battle-ai-hud.md)。

- 状态图标和回合数字挂在血条 Frame 下，沿用角色的骨骼 `top` 投影、镜头缩放和显隐。普通增益、减益由 15 项原图映射支持。左起每行最多三个，从贴近血条的一排向上折行，同类刷新复用格子。
- 白色护盾段紧接绿色生命，盾值由权威事件驱动。多段攻击共用原技能 HIT 时间逐段消耗护盾，末段触发生命扣减。普通护盾播放原版 `stse_protect.cfx` 的六层白蓝护盾，挂在身体节点下，每帧读取原 `shadow` 骨骼位置并叠加 CFX 偏移。缺骨骼时使用初始化的地面锚点。盾消失、角色被遮罩或死亡时隐藏。见[挂点与待机复核](../../../../docs/delivery/2026-11-13-battle-shield-anchor-ready.md)。状态图标在导入时对齐 Linear 工程的底板透射率，保留原始轮廓与回合数字。
- 使用 `Tools/BattleScene/ImportStatusHud.py` 和 `Tools/BattleScene/ImportBattleShieldAura.py` 重建小范围源资源，追加 `--check` 核对哈希。通过 `Tools/战斗场景/更新血条、状态与死亡资源引用` 更新 UnitPresentation。配置、数据流和原版证据见[头顶状态与护盾复核](../../../../docs/delivery/2026-11-13-battle-status-shield.md)及[原版六层护盾和底板校正](../../../../docs/delivery/2026-11-13-battle-shield-refinement.md)。

## 角色配置

Luban 快照 `Configs/battle-catalog.json` 含十名英雄、成长模板、觉醒节点、30 个技能槽及文案。编辑入口及来源见[战斗角色数据](../../../../docs/development/battle-character-data.md)。换人时在场景选择 `Units/Allies` 或 `Units/Enemies` 下对应槽位的角色实例，修改 `BattlePreviewActor > Hero Id`，进入 Play 或重开应用。可用 `Tools/战斗场景/更新角色资源引用` 刷新布局预览使用的缓存引用。默认阵容及验收见[八英雄通用预制体](../../../../docs/delivery/2026-11-13-eight-hero-battle.md)。

## 素材和验证

- `Res/Textures/Hud` 含原图、三人头像和九张技能图标。来源清单记录 13 份原始 CSB 布局证据和 PNG 的尺寸、透明边界、SHA-256。燃魂按钮的英文是原图的一部分。
- `Res/Fonts/BattleHpDigits.asset` 用本地原版 epic7num 图集和度量生成。原作行动者动态节点的实际字体绑定尚未证实。沙漏、完整头顶条、技能框与半透明底条的源图和摆放依据见[HUD 复核](../../../../docs/delivery/2026-11-12-battle-hud-fidelity.md)。
- `Res/Textures/Backgrounds` 含 Forest2/Forest3 各六层。裁块保持原尺寸，PMA 边缘转换为常规透明，适用于 Unity SpriteRenderer。完整原图集及其余关卡留在只读来源。
- `Res/Units/Slime` 含一个怪物的 SCSP/图集/SCT 转换产物，保留原骨骼版本。其 PMA 图集保持无压缩且关闭透明边缘扩展，复用现有 Spine 渲染桥接。
- 用项目角色素材 Python 环境执行 `Tools/BattleScene/PreparePreviewArt.py` 可核对 HUD/背景；加 `--restore` 按同一来源复现。怪物导入入口为 `Tools/BattleScene/ImportPreviewUnit.py`。原始目录始终只读，GUID 由已提交的 meta 保持。
- 实际执行见[HUD 复核](../../../../docs/delivery/2026-11-12-battle-hud-fidelity.md)和[首次场景验收](../../../../docs/delivery/2026-11-12-battle-scene.md)。试玩验收记录见[战斗第三步](../../../../docs/development/battle-execution.md)。

## 全屏演出与音频维护

战斗读取现有角色的原始 CUTIN 帧、镜头、背景、CFX与骨骼事件。拉斯和安洁莉卡 S3 播放各自的 Cut-in，梅赛德丝 S3 采用源背景与镜头；部分 S2 的镜头/压暗也由各自源数据驱动。有 LAYOUT 的特写暂隐非参与单位的 Renderer，完成或中断恢复，不改站位及权威结算。镜头期间浮字投影到实际目标位置。

史莱姆攻击使用原始 model/slime/ani/skill1，命中按原 hit 时间0.7秒播放钝击和目标自己的日语受击事件。新增 Res/Sounds/gerneal.bank.bytes 通过 Git LFS 保存，来源在相邻 source.json。行动条四种史莱姆头像与本场皮肤目检匹配，记录于 Hud/Readiness/source.json，不等同于原版怪物数据库已解码。

素材校验与恢复入口分别为 Tools/BattleScene/ImportBattleAudio.py、ImportReadinessHud.py、PreparePreviewArt.py，使用项目的 .tools/character-assets/Scripts/python.exe。场景序列化更新仍使用 InstallHudEffects，保留作者布局和现有 GUID。[完整调用、复现与限制](../../../../docs/development/battle-execution.md#2026-11-12-演出对照修订)

本轮网络参考限定近期实际查看的画面，并明确录像版本不可确认的范围；详情面板以本地原版 CSB 为结构依据。[参考记录](../../../../docs/delivery/2026-11-12-battle-presentation-reference.md)。部分后处理、镜头锚点、移动曲线和精细布局仍有诊断，不能称所有技能逐帧还原通过。

## 2026-11-13 通用演出规则

源 ANI_SCOPE 的 jump/landing 驱动接近与归位；camera Spine 曲线与基础镜头分离，原脚点构图和可见地图边界约束统一处理。PPEFFECT sprite_blur 使用短时世界相机合成，CUTIN 与HUD独立叠加；SINE调制为明确标记的重建公式。角色和地面影子分别移动，不按角色ID修补。

原 damage.fnt 位图字形按实际片段逐字弹跳、渐变和短暂停留；每段显示额按原 HIT 时间从权威总额分配，协攻继续累计，死亡显示等待末击。ImportDamageFeedback.py --check 校验12张原 combo 图及字体/CSB来源，InstallHudEffects 更新资源引用并保留场景布局。

新增角色必须遵守[源演出通用规则](../../../../docs/development/battle-source-presentation.md)，证据和推断见[镜头调查](../../../../docs/delivery/2026-11-13-camera-source-audit.md)。本轮无需新增外部插件。Shader/内置资源更新仍遵守母包与热更边界，Player平台另行验收；EditMode/Editor画面不能替代目标平台构建。

## 自动试玩与连续表现（2026-11-13）

直接运行BattleScene进入本地实战，敌方读取真实60级实体。右上暂停提供继续/重新开始，暂停保留选技和燃魂；燃魂后仅对应技能可操作，其他图标变暗，只有Cancel撤销。大行动头像在行动条后方。AutoStartBattle=false仅用于编辑布局和独立源演出测试。

源数据预备在进入交互前执行。Cut-in有独立时钟，舞台和命中等待其结束；夹攻在主末击开始，旧演员回位和新动作并行，只有新舞台拥有镜头。角色、镜头、粒子和反馈使用源时间；暂停不修改全局时间或权威状态。音频暂停限定本场FMOD。操作和时序规则见[源演出规范](../../../../docs/development/battle-source-presentation.md)与[录像复核](../../../../docs/delivery/2026-11-13-angelica-timing-reference.md)。最终测试和性能数值以本轮交付记录为准。

本次通用时序还包括：CFX primitive显式ani与寿命一致、有限DARK的duration后追加fadeOut、CUTIN按源幕布颜色/透明度覆盖普通HUD但保留暂停层。完整失败回归、实帧和性能条件见[时序与试玩验收](../../../../docs/delivery/2026-11-13-battle-timing.md)。

## 性能诊断与字体维护

2026-11-13 起，重复站位共享场景内不可变骨骼源，播放实例保持独立，最后持有者禁用后释放源缓存。版本头不再反序列化整个骨骼 DOM；完整原生解析结果须核对版本，避免冲突头选择错误运行库。

`Tools/战斗场景/重建数值字体` 只重建 HP 与伤害数字字体，保持原图和数字指标，补齐 TMP 保留字符。`Tools/战斗场景/构建 Windows 性能诊断包（Mono）` 生成独立性能包；不修改正式母包、热更或发布配置。用法与实际验证见[性能报告](../../../../docs/delivery/2026-11-13-battle-performance.md)，长期规则见 [ADR 013](../../../../docs/architecture/adr/013-battle-performance.md)。

## 信息条与动作恢复

普通攻击/技能的 CAM、BGHATCH 不整体隐藏生命、等级和状态；各单位信息条随身体Renderer显示，LAYOUT排除旁观者时其条组同步隐藏；全屏 CUTIN 使用幕布覆盖这些信息。技能与 Burn 控制仍按回合/演出管理。BGHATCH 同时保留原 PNG alpha 与预乘混合，不能通过换图或角色参数调整掩盖纹理编码问题。

动作回待机须连续衔接并收敛到完整的目标姿态，检查旧版运行库遗漏键、尾轨清理、循环、暂停和实例隔离。源码、红绿测试、平台边界见[本轮修复记录](../../../../docs/delivery/2026-11-13-battle-presentation-fixes.md)；后续角色导入遵守[源演出规范](../../../../docs/development/battle-source-presentation.md)。Core 桥接改变需新母包，不能只发布业务 DLL。

## 多段受击、个人资源与切换骨骼视角

- 每个源 HIT 重播通用受击，包括完全被护盾吸收的命中。源 NODE_ANI 优先，终结命中交给击倒；受击音仍沿用原有首段触发规则。
- 角色表现 View 统一拥有血条、等级、Buff、个人资源条和护盾。头顶组沿骨骼 `top` 投影到共享 HUD Canvas，护盾光效挂在角色身体 Mesh 下；两者读取同一身体显隐条件。共用 Canvas 让文字和图标保持正向并使用统一排序。
- 普通单位使用原版无资源槽底板。角色快照含专注或斗志时显示原版黄条或红条，行动值在左侧行动条维护。席可头像按原图等比缩放并固定左下角。
- Spine 2.1 兼容桥根据当前与入场姿态的可见附件识别互斥的同级视角分支。跨分支按源关键帧切换，兼容姿态继续混合。测试与发布边界见[本轮交付](../../../../docs/delivery/2026-11-13-battle-unit-feedback.md)。

## 血条跟随、损血与死亡（2026-11-13）

`BattleUnitStatusPresentation`维护运行时条组；普通条十等分、50% 主刻度更高，沿用原 split 图片。伤害由命中事件逐段表现，鲜红区间从首次实际失血累积，固定外缘，等同一攻击链对该目标的末段命中后再回收。夹攻、追加攻击和攻击型被动延续该链，反击、下一次主动作和治疗有独立边界。全额护盾吸收不生成红段，权威数据不变。当前原作证据与验收见[交付记录](../../../../docs/delivery/2026-11-13-health-death-presentation.md)。

`Res/Configs/UnitPresentation.asset`集中锚点、刻度、过渡和死亡资源配置；特殊Boss政策需独立证据。`Tools/战斗场景/更新血条与死亡资源引用`保留作者布局，`ImportDeathEffects.py --check`核对源依赖。死亡开场预备、末击黑化并淡出，原烟雾碎片到期释放；暂停、复活和重开不污染其他实例。Core shader/挂点接口需要新母包，不改变服务器规则。

## S1 镜头与 S3 目标恢复（2026-11-13）

拉斯/安洁莉卡S1缺省time的CAM/ZOOM继承jump→landing事件窗口，保留各自原倍率、偏移和曲线。拉斯S3起手离屏布局在目标群镜头切入时恢复接受目标，旁观者继续隐藏；梅赛德丝有限布局的目标保持显示。View按当前骨架Renderer联动血条/等级/状态，死亡与布局复位互不覆盖。每个HIT、两朝向、中断和重开回归及证据范围见[本次交付](../../../../docs/delivery/2026-11-13-camera-visibility-fixes.md)。

信息条投影使用根Canvas局部坐标与相对比例，避免ScreenSpaceCamera在Render阶段更新位姿后重复移动/缩放血条。验收检查Camera.Render后的同帧挂点，不只检查渲染前RectTransform；无逐单位Canvas强制重建。

## 十人阵容

双方各四个站位使用同一个 BattleActor 预制体。退出 Play，在场景里选中站位下的实例并修改 `Hero Id`，再运行场景。模型、技能图、头像和音频引用由配表与导入清单生成，`Tools/战斗场景/更新角色资源引用` 可更新现有场景的引用。主动技能按权威流程提交，被动槽按配表触发。十人名单、使用方式、源差异和回归见[十名角色接入](../../../../docs/delivery/2026-11-13-ten-hero-roster.md)。

技能、头顶状态与行动条长按共用 Common 弹板。折叠行动条时只更新可见头像和进度，展开时刷新详情行。技能和效果卡的字体、底板及分区按原始 CSB，详见 [录屏和素材对照](../../../../docs/delivery/2026-11-14-battle-inspection-ui.md)。梅赛德丝 S3 立绘及入回合的 Spine 扬尘事件分别见 [立绘](../../../../docs/delivery/2026-11-14-mercedes-s3-cutin.md) 和 [交接帧报告](../../../../docs/delivery/2026-11-14-battle-turn-handoff.md)。

长按浮层的世界坐标由角色状态条重投影后传给 Common Binder。独立 Buff 卡在触发图标右上并跟随移动，技能关联效果卡按主框可见边缘对齐。原版帧的测量值与边距见 [长按对照](../../../../docs/delivery/2026-11-14-battle-inspection-ui.md#长按浮层位置和透明度复核)。

## 状态规则与客户端反馈

- 本地手动点选、双击代选、自动行动都读取共享战斗的合法目标规则。挑衅只在来源仍存活时触发自动普攻，强制动作期间隐藏技能与燃魂组。原版挑衅图标使用可复跑的 ImportStatusHud.py 导入并登记来源。
- 状态应用、抵抗及回合变化都从已接受的战斗事件排入演出。状态回合数在当前动作的末段命中后更新，行动交接再按权威快照校对。共享规则保持纯 C#，HUD 不计算概率、不写战斗状态。
- 免疫、效果抵抗与禁疗按权威拒绝原因分别显示，未命中沿用命中反馈，随机未触发保持安静。潜行使用既有骨骼透明度参数，死亡开始前归还该参数；0.55 的透明度和状态粒子范围尚需原作画面对照。
- 维德瑞的攻击型被动使用对应源演出与技能图标，等待前一动作反馈后播放并延续组合计数。被动技能槽保持不可点，存在冷却时仍可查看遮罩与回合数。

技能编辑入口为 **Tools → 战斗场景 → 技能演出编辑器**。轨道与视口复用战斗播放器，并显示模型时间轴附带的特效、震动和命名事件。拖动节点时按 Cut-in 暂停区间换算回源时间。保存的节点覆盖随场景资源发布。见[操作说明](../../../../docs/development/battle-skill-timeline-editor.md)和[前十名演出基线](../../../../docs/delivery/2026-11-14-skill-presentation-first-ten.md)。

第 11 名杰拉图 c1010 的场景引用包含三个技能图及递归特效依赖。更新角色资源引用只重建目录，不修改作者已设的八个站位。

卡琳 c1011 已接入角色内容；三技能、音频及已知缺项见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

萝菲 c1019 已接入三张演出图及原版日语。新增原版 voc3 银行，战斗按阵容选择；裂伤有效生命独立于基础面板。规则、表现诊断与音频来源见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

## 角色预制与回合反馈（2026-11-14）

- 八个站位继承同一个 [BattleActor.prefab](Res/Prefab/BattleActor.prefab)。预制根下的 Shadow 管理共用阴影；StatusTemplate 管理头顶框、血条、等级、护盾填充和 Buff 挂点，预制组件引用共用 UnitPresentation，其中包含护盾与无敌光晕。角色 ID 仍在站位实例上配置。
- 战斗把头顶模板复制到 BattleHud 的 Unit Status/阵营/站位，沿用骨骼跟随投影与长按。场景共用一个 Canvas；光晕跟随身体，阴影走地面路径。重开释放旧状态条与粒子，再建立八个实例。
- 取消燃魂保留刚才选中的技能，可直接点目标普通施放。己方燃魂在权威指令确认扣费时，于减少的整魂槽原位播放 hud_soul_consume；准备、取消和魂力收入不触发消散。
- 近身 MOVE 根据角色地面尺寸拉开距离并限制过度前冲，保留源时点。实际显示新回合时尝试一次该角色的日语 b_idle_ready 事件，刷新与暂停继续不重复触发。梅赛德丝尘土事件仍由骨骼触发。

操作与验证见[本轮验收](../../../../docs/delivery/2026-11-14-battle-actor-burn.md)。

## 技能演出编辑器交互修订（2026-11-14）

技能列表、预览与参数区分栏可调，底部时间轴独占整行；播放/暂停、逐帧、循环、声音和速度统一位于轴上方。F聚焦、A看全、C定位播放头；选中节点在回退重建、重播和循环后保持关联。编辑器区分主演出与完整余辉范围；原始粒子多组颜色渐变分别解析，不再因第二组从0开始而丢弃发射器。操作、实测与边界见[技能演出编辑器](../../../../docs/development/battle-skill-timeline-editor.md)及[本轮验收](../../../../docs/delivery/2026-11-14-timeline-usability.md)。


### 被动技能、状态说明与受击反馈

底部技能栏和行动条详情使用同一被动判定。被动使用原生切角框与图标，只有进入冷却才显示对应的黑色遮罩，长按可查详情。当前 30 名角色默认为六星六觉醒，技能强化档仍是 +0。技能、燃魂和状态正文来自与本机原始素材同版本的官方简中资源；已知差异以单独的项目说明记录。

红色血条以视觉回合开始时的生命为上边缘。每次实际受击推进绿色血条并闪亮红段。最后一次命中后，所有当回合受击角色的红段开始缓收，群攻、夹攻、追击和反击沿用同一回合边界。行动条对应头像随命中短暂闪动，行序与站位不受影响。

场景目录已覆盖 30 人。复活与新增状态通过权威事件更新角色、血条及行动条；燃魂独立演出和额外攻击仍走同一动作播放器。 [第 21–30 名角色验收](../../../../docs/delivery/2026-11-14-roster21-30.md)

## 在线决斗场入场与队伍 HUD（2026-11-15）

在线入口通过 `BattleOnlinePlayers` 传入对局双方的显示名、头像 ID 和已分类的头像 Sprite；头像由大厅持有到叠加战斗场景退出。无资料时使用本地化的阵营称呼，不填入虚构玩家。离线场景继续使用原有波次 HUD。

`BattleOnlineHud` 在战斗安全区上缘展示双方头像、名称、分段总生命条和原版 VS。生命聚合当前实际呈现的单位生命，随受击演出更新；回合文字与原版红／蓝计时环只在权威决策窗口显示，入场、动作播放、断线和结束时隐藏。双方演出完成通过会话 ready 接口同步，服务器保存期限并处理超时合法默认动作，客户端只投影剩余秒数。服务版本不支持计时时显示明确错误并停止输入。该图层不接收射线，不改变鼠标和触摸命中。协议、等待上限和验收见[在线计时记录](../../../../docs/delivery/2026-11-15-online-turn-clock.md)。

入场标题在 IGNITION 下方显示金色竞技场与白色决斗场，入场结束释放标题，队伍 HUD 持续显示。独立图层避免被普通 HUD 的入场淡出一同隐藏，退出战斗恢复原波次与速度按钮布局。

素材入口为 `Tools/BattleScene/ImportOnlineHud.py`；`--check` 验证十张选定 PNG 与只读原始来源逐字节一致，`Resources/AstralOathOnlineHud/source.json` 记录每张文件与 `wnd/pvplive_global_hp.csb` 布局证据的 SHA-256。CSB 仅作布局依据，不进入运行时。Unity 编译与 16:9、21:9 实帧截图分别验收；资源校验不能替代视觉对照。
