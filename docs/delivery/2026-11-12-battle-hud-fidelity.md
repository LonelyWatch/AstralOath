# 四对四站位与战斗 HUD 复核

近期对照使用 2026-11-05 发布的[深渊 120 实战 00:35](https://www.youtube.com/watch?v=LDnoGRK7M50&t=35s)及 2026-11-01 发布的[深渊实战 23:30](https://www.youtube.com/watch?v=xpziWL0BXX0&t=1410s)。抽样帧可辨认左下行动者信息、绿色生命条、行动条顶端的沙漏及各单位头顶条。视频右下处于自动战斗状态，手动三技能的几何以本地 `hud_skill.csb` 为依据。视频上传日期与可辨认画面见[战斗研究 R10](../development/epic-seven-battle-reference.md)。

- `Units/Allies`、`Units/Enemies` 各有前、下、上、后四个可编辑脚点。四名我方使用拉斯、安洁莉卡、梅赛德丝、另一名拉斯，敌方使用冰、火、风、暗史莱姆。位置依据对照手工调整，编队 UI 的局部坐标不足以证明原作世界阵位。
- `inbattle_order.csb` 的左下底条和行动条 alpha 为 153/255。底条先绘制，再绘制头像和角色信息。左下生命数字为紧凑绿色的 `1260/1260`，与右侧短生命条同排。项目使用原版 `epic7num.fnt` 和 PNG 的 glyph 矩形、基线与 advance 生成 TMP 位图字体。原作动态行动者节点选择的字体仍待核实。
- 行动条上方加入 `cm_icon_etctime2` 与 `_btn_small_black`。底图按原版 cap inset 转换为 Unity Sliced 边距。
- 头顶条按 `hp_bar.csb` 的 86×26 完整底框、24×21 元素底、原版等级数字、56×6 生命条和 56×4 行动条还原。演示使用绿色当前生命和蓝色行动条。角色世界绘制顺序保留在 HUD 之下。
- 手动技能图以原图尺寸的 1.25 倍绘制，相对 104×107 边框右移 3.5、下移 2.5 个设计像素。保留原图的透明边与宽高比。

布局坐标采用 1280×720 左下原点。Unity 打开方式、可编辑层级与资源复现见[战斗模块 README](../../Assets/Game/Modules/BattleScene/README.md)。阵容、等级、HP、CR 和按钮状态为布局样例。


## 验证

- 站位回归先在原场景失败（期望 8，实际 5）。改造后真实场景验证八个骨骼网格、双方各四个 HUD 标记、原版沙漏与血条引用、生命数字字体和世界/HUD 绘制顺序通过。
- 完整 Unity 2022.3.62f3 回归通过，EditMode 215/215、PlayMode 17/17。目检 1920×1080 最终渲染 `.artifacts/battle-stage/battle-scene-1920x1080.png`。
- 素材复现通过，94 个 HUD 源文件与 12 张背景图层。BMFont 度量保留上游换行，确保提交后的 SHA-256 一致。
