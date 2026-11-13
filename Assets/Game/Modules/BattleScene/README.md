# BattleScene

战斗子任务一。独立冒险战斗布局预览，使用原作 HUD、两套森林分层素材，以及双方各四个站位的 Spine 待机。我方重复使用拉斯补满，敌方使用冰、火、风、暗史莱姆。阵容、等级、生命、魂力和行动条为场景展示数据。相关规则及画面证据见[战斗资料](../../../../docs/development/epic-seven-battle-reference.md)。

## 打开与编辑

1. 在 Unity 使用 `Tools/战斗场景/打开场景`，或打开 `Scenes/BattleScene.unity`。
2. 将 Game 视图设为 16:9，点击 Play。静态舞台和界面在编辑时可见，Spine 网格在 Play 时创建。
3. 双击 `Res/Prefab/BattleHud.prefab` 可直接编辑。顶部按钮、行动条、单位血条、当前角色、魂力和技能分组排列。通过 `Tools/战斗场景/验证 HUD 绑定` 检查引用。
4. 白天舞台使用 `Res/Prefab/Forest2Stage.prefab`，暮色舞台使用 `Forest3Stage.prefab`。替换场景中舞台实例即可换景。每个图层可单独调整。角色脚点在 `Units/Allies` 与 `Units/Enemies`，每方有前、下、上、后四个组。编辑态信息条使用HUD作者摆位；运行时按动画top挂点投影跟随人物及镜头缩放。

`BattleSceneBuilder.Generate` 是可从隔离工程调用的作者工具，会重建示例场景、三个预制体和生命数字位图字体。编辑好的正式布局以保存的 Unity 资产为准。其完整批处理方法名是 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.Generate`。

## 结构和后续接入

- `BattleHud` 继承 `UIBaseView` 并验证 BindingMap。场景 Canvas 上的 `BattleHudPreview` 管理普通 C# `BattleHudBinder` 生命周期。预制体使用 GameText、GameButton 和 BindingMap。Burn 与三枚技能按钮接受点击，布局模式的魂力与角色固定为当前拉斯。
- `BattlePreviewActor` 通过现有 SkeletonPlayerFactory 读取原版骨骼及图集，进入 Play 时生成，禁用或卸载时释放网格和材质。三名英雄直接引用 CharacterPresentation 的现有资产。
- `BattlePreviewViewport` 在安全区内保持 16:9，世界与 Screen Space Camera HUD 使用同一摄像机。HUD 作者坐标以 1280×720 左下原点记录，生成时乘 1.5。地图图层位置和站位来自对照后的手调。Forest2／3 的草地按原宽高比布置，底边固定在设计坐标 Y=-2，连续草带覆盖八个站位及其影子。替换舞台时用各槽位的世界坐标核对可站草带。梅赛德丝的待机保留原骨骼悬浮。
- 场景通过独立试玩入口和直接资产引用运行，正式启动和 YooAsset 收集另行接入。正式流程接入时另定资源地址、数据契约和服务端准入。

## Burn 和技能选中预览

- 进入 Play，Burn 播放原版入场和 0.6666 秒闪动。普通技能选中叠加 `hud_skill.csb` 的 `selected` 图层（`cm_select_skill.png`，114×120，中心相对技能图标左 3、上 4 个设计像素）、Spine 闪动和粒子。切换时清除上一枚的效果。
- 点击 Burn 立刻播放魂力闪光并切换为 Cancel，同时拉斯的 S2 播放燃魂起手。231 毫秒后出现蓝色燃魂描边与粒子。点击 Cancel 中断闪光并恢复之前的普通选中。之后点技能会恢复普通选中。
- 这段说明适用于进入 Play 后的布局模式。试玩模式的消耗、目标与技能结算见下节。关闭或隐藏 HUD 时释放粒子、骨骼与按钮监听。
- `Res/Effects/Hud/source.json` 记录原版 SCSP、SCT、atlas、CFX、粒子和按钮 PNG 的来源及哈希。Git 保留原版 atlas 的字节及换行。使用项目素材 Python 环境运行 `Tools/BattleScene/ImportHudEffects.py --check` 验证，去掉 `--check` 复现素材。Unity 编辑器批处理入口 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.InstallHudEffects` 只更新 HUD 点击配置与场景组件，可用于保留已编辑的站位和布局。
- 源 CFX 的顺序、描边颜色和 Spine 曲线沿用原数据。粒子在 Unity 中按原参数播放，专有的发射模式按爆发与持续发射重建。视觉外观参考原版战斗截图，按钮和技能锚点按当前布局对齐，原作连续录像逐帧对照仍待验证。[原作战斗截图](https://d3kxs6kpbh59hp.cloudfront.net/community/COMMUNITY/718883b8c9f34eaca8e89ee116e1e29d/1f0480fa6b5144a584877ad1c1d10df7_1617085685.png)
- Unity 2022.3.62f3 实际 HUD PlayMode 回归 2/2，EditMode 215/215。PlayMode 带图形运行 `BattleScenePreviewTests`，设置 `ASTRAL_BATTLE_CAPTURE_DIR` 可导出 Burn、技能选中和使用反馈的 1920×1080 截帧。

## 战斗试玩

进入 Play 后自动开始本地试玩，无需点击入口按钮。点击技能，再在场景中选择友军或敌军。敌方自动行动，技能冷却、生命、状态和魂力按共享规则更新。右上暂停提供继续与重新开始；编辑器菜单 `Tools/战斗场景/开始试玩` 仍可用于重开。退出并重新进入 Play 会创建新局。

已导入的 STG、CFX、Spine、粒子与角色 timeline 驱动动作、位移和命中特效，FMOD 在 Windows 播放原版技能音效和日语语音。敌方回合隐藏技能组和 Burn。行动条头像按行动值从上向下移动，当前行动者在下端；点击左侧沙漏展开原版行结构的行动值、生命、技能冷却与状态，点击空白关闭。技能冷却使用贴合图标的遮罩与回合数，伤害与治疗使用独立浮字。动画消费有序事件，状态由同源规则决定。试玩场景依赖共享战斗程序集，`BattleScene` 已登记为 HybridCLR 业务程序集。双人 HTTP 验证、规则口径及当前差异见[战斗第三步](../../../../docs/development/battle-execution.md)。

## 头顶状态与护盾

- 状态图标和回合数字挂在血条 Frame 下，沿用角色的骨骼 `top` 投影、镜头缩放和显隐。普通增益、减益由 15 项原图映射支持。左起每行最多五个，向上折行，同类刷新复用格子。
- 白色护盾段紧接绿色生命，盾值由权威事件驱动。多段攻击共用原技能 HIT 时间，先消耗护盾，再变化生命。白色环沿骨架位置播放，盾消失、角色被遮罩或死亡时隐藏。
- 使用 `Tools/BattleScene/ImportStatusHud.py` 和 `Tools/BattleScene/ImportBattleShieldAura.py` 重建小范围源资源，追加 `--check` 核对哈希。通过 `Tools/战斗场景/更新血条、状态与死亡资源引用` 更新 UnitPresentation。配置、数据流和原版证据见[头顶状态与护盾复核](../../../../docs/delivery/2026-11-13-battle-status-shield.md)。

## 角色配置

Luban 快照 `Configs/battle-catalog.json` 含三名英雄、成长模板、觉醒节点、九技能、强化与文案。编辑入口及来源见[战斗角色数据](../../../../docs/development/battle-character-data.md)。Shared Battle 现已支持[面板计算、单位创建与站位](../../../../docs/development/battle-growth.md)。编辑态布局显示作者预设；运行时按队伍与 1–4 号槽映射世界单位和实时快照，四个史莱姆均为60级。

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

## 血条跟随、损血与死亡（2026-11-13）

`BattleUnitStatusPresentation`维护运行时条组；普通条十等分、50%主刻度更高，使用原split/dhp图片。血量变化有红棕损血尾，权威数据不变。当前原作证据与验收见[交付记录](../../../../docs/delivery/2026-11-13-health-death-presentation.md)。

`Res/Configs/UnitPresentation.asset`集中锚点、刻度、过渡和死亡资源配置；特殊Boss政策需独立证据。`Tools/战斗场景/更新血条与死亡资源引用`保留作者布局，`ImportDeathEffects.py --check`核对源依赖。死亡开场预备、末击黑化并淡出，原烟雾碎片到期释放；暂停、复活和重开不污染其他实例。Core shader/挂点接口需要新母包，不改变服务器规则。

## S1 镜头与 S3 目标恢复（2026-11-13）

拉斯/安洁莉卡S1缺省time的CAM/ZOOM继承jump→landing事件窗口，保留各自原倍率、偏移和曲线。拉斯S3起手离屏布局在目标群镜头切入时恢复接受目标，旁观者继续隐藏；梅赛德丝有限布局的目标保持显示。View按当前骨架Renderer联动血条/等级/状态，死亡与布局复位互不覆盖。每个HIT、两朝向、中断和重开回归及证据范围见[本次交付](../../../../docs/delivery/2026-11-13-camera-visibility-fixes.md)。

信息条投影使用根Canvas局部坐标与相对比例，避免ScreenSpaceCamera在Render阶段更新位姿后重复移动/缩放血条。验收检查Camera.Render后的同帧挂点，不只检查渲染前RectTransform；无逐单位Canvas强制重建。

## 十人阵容

Battle Canvas 的 `BattleHudPreview` Inspector 为四个友军站位提供角色下拉列表。退出 Play 后选择并重新运行，或在运行中选择后点重新开始。模型、技能图、头像和音频引用由配表与导入清单生成，`Tools/战斗场景/更新角色资源引用` 可更新现有场景的引用。主动技能按权威流程提交，被动槽仅展示。十人名单、使用方式、源差异和回归见[十名角色接入](../../../../docs/delivery/2026-11-13-ten-hero-roster.md)。
