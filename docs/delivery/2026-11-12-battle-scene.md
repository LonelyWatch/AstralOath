# 战斗首阶段场景验收

## 交付范围

按[战斗画面对照](../development/epic-seven-battle-reference.md)中的原作客户端教程图及本地 CSB，重组冒险战斗画面。舞台使用 Forest2 晴天森林，附带可替换的 Forest3 暮色森林。示例阵容是拉斯、梅赛德丝、安洁莉卡和两只史莱姆，位置和状态为布局样例。

在 Unity 选择 `Tools/战斗场景/打开场景`，Game 视图设为 16:9，再点击 Play。HUD、舞台和角色脚点的编辑入口见[模块 README](../../Assets/Game/Modules/BattleScene/README.md)。

## 验证记录

- Unity 2022.3.62f3，隔离工程 `.artifacts/UnityBattleScene`。生成入口 `AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.Generate` 保存场景和三个预制体，通过 UIBaseView/BindingMap 引用检查。
- `BattleScenePreviewTests.AuthoredSceneShowsOriginalActorsAndHud` 初次因场景缺失失败，完成实现后在带图形的 Unity PlayMode 通过。验证场景加载、五个真实骨骼网格、可见位置、摄像机设计画幅、HUD 引用和禁用后释放。
- 设置 `ASTRAL_BATTLE_CAPTURE_DIR=.artifacts/battle-stage` 可让该用例输出实际 Unity 1920×1080 截图。先目检角色比例、血条覆盖和背景接缝，再调整并重跑。最终本机图为 `.artifacts/battle-stage/battle-scene-1920x1080.png`。
- `Tools/BattleScene/PreparePreviewArt.py` 复验 74 张 HUD 与 12 张背景图层。HUD 与来源字节一致，背景按原图集区域重新解码、裁切并复现反预乘结果。来源清单随资源提交。史莱姆的 JSON、图集及纹理有独立的 SHA-256 清单。
- `node Tools/validate.mjs` 检查 17 个程序集，0 错误。
- 完整 Unity 回归通过，EditMode 215/215、PlayMode 17/17。等级数字底色调整后重新生成，并复跑场景 PlayMode 用例与目检最终截图，1/1 通过。

## 原作对照差异和后续任务

- Forest2/Forest3 是原始图集分层重组，地平线、树木、脚点和镜头比例由本轮手调。原始关卡背景配置、视差、镜头移动和背景动画待核对。
- 依据教程图恢复冒险 HUD 的分区和主要控件。波次和顶部控件位置按教程做适配，文字和世界血条当前由预制体摆放。队伍/敌人状态、CR 排序、技能冷却、守护者、状态图标和实际按钮行为需要正式战斗状态驱动。
- 保留原图中的英文燃魂字样。正式中文界面需要按语言资源策略接入本地化燃魂表现。场景采用项目现有字体，角色站位中的名称与数字是展示样例。
- 画幅为安全区内 16:9。平板、超宽屏和 Android 缺口需要在实际窗口或设备上完成视觉验收。
- 下一步是填写三名角色的权威数据表，然后把场景接到服务端权威战斗流程、技能演出与正式资源发布。当前验证只覆盖首阶段场景。
