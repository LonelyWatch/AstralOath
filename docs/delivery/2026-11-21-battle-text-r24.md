# 战斗姓名与效果提示的字体

## 当前行动者姓名

原包 `wnd/unit_plate.csb` 的 `txt_name` 使用 `font/daum.ttf`，字号 24，白色完全不透明，开启黑色 1 源像素描边，关闭阴影。文字区为 206×26，锚点在右边中央。仓内 `Assets/Game/Common/Res/Fonts/EpicSeven.ttf` 与原字体文件的 SHA256 均为 `2643979e008564e3b59455f7548aa6f844c155b21b3aa42d00dcba1903c3c1ab`。源文件以项目现有 CSB 读取器解析，文字配置的字段和缺省边宽可按引擎定义复核。[Cocos2d-x TextOptions](https://github.com/cocos2d/cocos2d-x/blob/v3/cocos/editor-support/cocostudio/CSParseBinary_generated.h)

R23 的姓名已是白色，实际字族为 Noto Sans SC Thin，设计稿字号为 30。新版在 `BattleHud.prefab` 和对应生成器中为姓名绑定 EpicSeven Regular 和专用黑边材质，按 720 至 1080 的比例使用 36 点、309×39 文字区，保留姓名与属性图标之间原有的右边缘。黑边用 SDF 材质在白字外绘制，SDF 参数由项目渲染取样校准。暂停页、联机队伍栏、进场标题、行动条和头顶普通 HUD 继续从 `wave` 读取原有的通用字体。

用户提供的国服秋季赛 P2 文件 `2.3060-3091_43325065328.mp4` 在 07:55.433、07:56.900 和 08:32.267 分别显示长短不同的姓名。原片为 1920×1080、30 fps，未确认发布日期、游戏内倍速或皮肤。08:32.267「梅铃」亮笔画范围为 67×31 像素，工程同字号为 68×32。两组均取 RGB 三通道大于 185 的姓名区域。在同一 HUD 背景上显示用户指出的「尤贝乌斯」时，RGB 三通道大于 215 且比底图亮超过 60 的像素从 85 增至 957。另用「未知开拓者佛里蒂丝」验证长名向左展开，右侧属性图标仍可见。

## 效果提示

`BattleEffectWordOutline.mat` 的黑色描边宽度从 0.12 调为 0.19，保留原字族、颜色、字号及 `FaceDilate=0.29`。缺少预制材质的旧角色配置走 `BattleEffectWords` 的兼容材质，使用相同宽度和黑色。普通效果与被动的金色及浅紫色区分、图标、分行与淡出时间沿用原配置。

使用同一张引擎实渲的史瑞杰思技能金光背景、同一摄像机和同一时刻，分别绘制「攻击力大幅提升」「额外回合」，并单独捕获无文字底图。RGB 均值原先大于 120、加字后低于 90 的像素，从 905 增至 2844。两张无字底图逐像素相同。此图用于在亮特效上比较字边，未将这两行提示当作史瑞杰思该次技能的实战结果。

## 复现与验收

证据目录为 `.artifacts/r24-battle-text/`。同目录 `source.py` 生成原参数记录 `unit-plate.json`；`CaptureBattleFont.cs` 在隔离 Unity 工程中实例化正式预制和效果提示类；`compare.py` 以改前及最终渲染生成 `text-comparison.png`、`metrics.json`。原录像姓名裁图为 `original-hud.jpg`。

旧版本的字族及两处描边断言均失败，见 `.artifacts/summon-result-r23/repro/r24-text-red.xml`。仅替换字族的版本仍在原版字号断言处失败，见 `native-red.xml`。最终 `BattleHeroPrefabTests` 七项全部通过，见 `native-green.xml`。

HUD 组合用例首次通过 15/16，见 `hud-suite.xml`。露露卡的永久攻击成长 `AttackGrowth` 来自隐藏被动 `pa_c1082_3`，旧用例却要求为它提供可见状态详情。修订后用例单独验证此被动保持隐藏，逐项验证其余状态可见且有中文名称及说明，并保留全部技能文案断言。以相同筛选器重跑英雄预制、反馈时序、当前行动者立绘和详情文案，16/16 通过，见 `hud-suite-green.xml`。
