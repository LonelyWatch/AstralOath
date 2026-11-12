# BattleScene

战斗子任务一。独立冒险战斗布局预览，使用原作 HUD、两套森林分层素材，以及双方各四个站位的 Spine 待机。我方重复使用拉斯补满，敌方使用冰、火、风、暗史莱姆。阵容、等级、生命、魂力和行动条为场景展示数据。相关规则及画面证据见[战斗资料](../../../../docs/development/epic-seven-battle-reference.md)。

## 打开与编辑

1. 在 Unity 使用 `Tools/战斗场景/打开场景`，或打开 `Scenes/BattleScene.unity`。
2. 将 Game 视图设为 16:9，点击 Play。静态舞台和界面在编辑时可见，Spine 网格在 Play 时创建。
3. 双击 `Res/Prefab/BattleHud.prefab` 可直接编辑。顶部按钮、行动条、单位血条、当前角色、魂力和技能分组排列。通过 `Tools/战斗场景/验证 HUD 绑定` 检查引用。
4. 白天舞台使用 `Res/Prefab/Forest2Stage.prefab`，暮色舞台使用 `Forest3Stage.prefab`。替换场景中舞台实例即可换景。每个图层可单独调整。角色脚点在 `Units/Allies` 与 `Units/Enemies`，每方有前、下、上、后四个组。当前头顶血条使用 HUD 内同名分组的静态摆位。

`BattleSceneBuilder.Generate` 是可从隔离工程调用的作者工具，会重建示例场景、三个预制体和生命数字位图字体。编辑好的正式布局以保存的 Unity 资产为准。其完整批处理方法名是 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.Generate`。

## 结构和后续接入

- `BattleHud` 继承 `UIBaseView` 并验证 BindingMap。场景 Canvas 上的 `BattleHudPreview` 管理普通 C# `BattleHudBinder` 生命周期。预制体使用 GameText、GameButton 和 BindingMap。Burn 与三枚技能按钮接受点击，预览的魂力与角色固定为当前拉斯。
- `BattlePreviewActor` 通过现有 SkeletonPlayerFactory 读取原版骨骼及图集，进入 Play 时生成，禁用或卸载时释放网格和材质。三名英雄直接引用 CharacterPresentation 的现有资产。
- `BattlePreviewViewport` 在安全区内保持 16:9，世界与 Screen Space Camera HUD 使用同一摄像机。HUD 作者坐标以 1280×720 左下原点记录，生成时乘 1.5。地图图层位置和站位来自对照后的手调。
- 预览场景使用直接资产引用和独立程序集，未纳入正式启动场景、YooAsset 发布收集或 HybridCLR 业务登记。正式流程接入时另定资源地址、数据契约和服务端准入。

## Burn 和技能选中预览

- 进入 Play，Burn 播放原版入场和 0.6666 秒闪动。普通技能选中叠加 `hud_skill.csb` 的 `selected` 图层（`cm_select_skill.png`，114×120，中心相对技能图标左 3、上 4 个设计像素）、Spine 闪动和粒子。切换时清除上一枚的效果。
- 点击 Burn 立刻播放魂力闪光并切换为 Cancel，同时拉斯的 S2 播放燃魂起手。231 毫秒后出现蓝色燃魂描边与粒子。点击 Cancel 中断闪光并恢复之前的普通选中。之后点技能会恢复普通选中。
- 此预览只展示 HUD，消耗、选目标和技能执行由后续服务端流程负责。关闭或隐藏 HUD 时释放粒子、骨骼与按钮监听。
- `Res/Effects/Hud/source.json` 记录原版 SCSP、SCT、atlas、CFX、粒子和按钮 PNG 的来源及哈希。Git 保留原版 atlas 的字节及换行。使用项目素材 Python 环境运行 `Tools/BattleScene/ImportHudEffects.py --check` 验证，去掉 `--check` 复现素材。Unity 编辑器批处理入口 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.InstallHudEffects` 只更新 HUD 点击配置与场景组件，可用于保留已编辑的站位和布局。
- 源 CFX 的顺序、描边颜色和 Spine 曲线沿用原数据。粒子在 Unity 中按原参数播放，专有的发射模式按爆发与持续发射重建。视觉外观参考原版战斗截图，按钮和技能锚点按当前布局对齐，原作连续录像逐帧对照仍待验证。[原作战斗截图](https://d3kxs6kpbh59hp.cloudfront.net/community/COMMUNITY/718883b8c9f34eaca8e89ee116e1e29d/1f0480fa6b5144a584877ad1c1d10df7_1617085685.png)
- Unity 2022.3.62f3 实际 HUD PlayMode 回归 2/2，EditMode 215/215。PlayMode 带图形运行 `BattleScenePreviewTests`，设置 `ASTRAL_BATTLE_CAPTURE_DIR` 可导出 Burn、技能选中和使用反馈的 1920×1080 截帧。

## 角色配置

Luban 快照 `Configs/battle-catalog.json` 含三名英雄、成长模板、觉醒节点、九技能、强化与文案。编辑入口及来源见[战斗角色数据](../../../../docs/development/battle-character-data.md)。Shared Battle 现已支持[面板计算、单位创建与站位](../../../../docs/development/battle-growth.md)。当前场景继续显示作者预设。正式接入时，按队伍与 1–4 号槽映射脚点，并通过服务端快照更新界面。

## 素材和验证

- `Res/Textures/Hud` 含原图、三人头像和九张技能图标。来源清单记录 13 份原始 CSB 布局证据和 PNG 的尺寸、透明边界、SHA-256。燃魂按钮的英文是原图的一部分。
- `Res/Fonts/BattleHpDigits.asset` 用本地原版 epic7num 图集和度量生成。原作行动者动态节点的实际字体绑定尚未证实。沙漏、完整头顶条、技能框与半透明底条的源图和摆放依据见[HUD 复核](../../../../docs/delivery/2026-11-12-battle-hud-fidelity.md)。
- `Res/Textures/Backgrounds` 含 Forest2/Forest3 各六层。裁块保持原尺寸，PMA 边缘转换为常规透明，适用于 Unity SpriteRenderer。完整原图集及其余关卡留在只读来源。
- `Res/Units/Slime` 含一个怪物的 SCSP/图集/SCT 转换产物，保留原骨骼版本。其 PMA 图集保持无压缩且关闭透明边缘扩展，复用现有 Spine 渲染桥接。
- 用项目角色素材 Python 环境执行 `Tools/BattleScene/PreparePreviewArt.py` 可核对 HUD/背景；加 `--restore` 按同一来源复现。怪物导入入口为 `Tools/BattleScene/ImportPreviewUnit.py`。原始目录始终只读，GUID 由已提交的 meta 保持。
- 实际执行见[HUD 复核](../../../../docs/delivery/2026-11-12-battle-hud-fidelity.md)和[首次场景验收](../../../../docs/delivery/2026-11-12-battle-scene.md)。下一阶段接入选目标、回合、技能演出及权威结算。
