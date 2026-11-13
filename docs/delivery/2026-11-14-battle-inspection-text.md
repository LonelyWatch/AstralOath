# 战斗长按说明文案

- 技能说明来自现有 Luban `Config/Tables/battle_messages.csv`，通过验证器规定的 `battle.skill.<技能 ID>.description` 读取。全部 30 个已配表技能保留已有中文说明；被动、未接入的原作效果继续在说明中标明。燃魂单列，效果类型和回合数直接读取技能配置。强化明细按现有配表顺序与真实增量显示。
- 15 类状态通过 `battle.status.<状态枚举小写>.name` 和 `.description` 提供中文。文案描述本项目当前的结算语义，例：潜行目前用于技能条件，潜行选敌限制及群攻减伤尚未接入。状态剩余回合数取当前已呈现的状态。
- 本机解包的文本资源仅发现 `原始资源/text/ja/text.db`，本轮没有移植原作中文文案。`wnd/skill_detail.csb` 提供完整技能卡素材及字号，`wnd/unit_skill_tooltip.csb` 实为强化所需资源提示。用户 2026-11-14 提供的简中录像在 9.0 秒展示技能与夹攻副卡，10.5 秒展示技能与防御提升副卡，11.5–12.0 秒展示独立状态说明。技能副卡读取技能效果表的首个夹攻或可说明状态，标签与 Buff 长按共用。
- 修改入口为 `Config/Tables/battle_messages.csv`。从项目根运行 `Tools/Config/Generate.ps1 -SyncClient` 生成客户端快照。状态新增时同时补状态图和两个文案键，`BattleInspectionContentTests` 检查整个已导入目录。

## 原始字体和字段

- 主技能卡使用 `wnd/skill_detail.csb` 的 `_box_equip_0.png`，源画布底板 440×716，九宫格切片 50px。强化分隔线 `_bar_1.png`，燃魂分隔线 `_bar_2.png`，冷却标记 `cm_icon_etctime_b.png`，燃魂图标 `cm_icon_star_s.png`。技能标题源字号 50、节点缩放约 0.44，说明正文 24×0.75。冷却和获得灵魂 24×0.72，强化行 24×0.70。
- `wnd/skill_eff.csb` 是独立效果说明与附属卡的素材证据，默认占位为攻击提升。逻辑卡 310×126，原始底板 `_btn_small_white.png` 是 317×135、14px 九宫格切片、RGB 27/67/104，正文 RGB 101/190/255。标题 24×0.8，正文 24×0.75，图标为 28×28×1.2。录像中棕色夹攻是运行时视觉变体，缺少独立静态 CSB 颜色定义。
- 上述文字字段引用根 `原始资源/font/daum.ttf`，字体内部名称 EpicSeven，SHA-256 为 `2643979E008564E3B59455F7548AA6F844C155B21B3AA42D00DCBA1903C3C1AB`，与启动包同名文件字节一致。读取字体 cmap 得到 42,345 个字符映射，夹攻、防御、强化及灵魂样本文字均有字形。日文语音选择独立于字体。

- 攻击、防御和速度的升降幅度由说明格式键 `battle.status.<kind>.strength` 展示。单位头顶与行动条保留同一个当前已展示的状态快照，数值来自 `BattleStatusSnapshot.Strength`。技能附卡读取该技能效果配置的 `value`。战斗 basis points 按 10000 对应 100%，非百分比状态保留原说明。
