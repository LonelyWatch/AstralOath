# 战斗技能报幕

用户提供的拉斯录像 `Screenrecorder-2026-11-14-00-12-36-593.mp4` 中，约 37.00 秒可见左上报幕开始进入，37.25–38.50 秒显示图标、施法者和技能名，38.75 秒向左退场，39.00 秒报幕消失但连击数仍在。31.75 秒全屏演出退场后也可见技能报幕；录像未确认原作客户端版本和精确动画时间曲线。

只读原 `wnd/inbattle_topbar.csb` 的 `n_hud_skill` 含 `skill_icon`、`txt_name`、`txt_desc`、`_box_black_gold.png` 和两条 `_bar_1.png`。两张底板纹理按原字节引入现有 HUD 来源清单，显示使用项目已配的各技能图标和中文名。Unity 版从左侧进入，并在约 1.8 秒后淡出。演出时钟随暂停和 Cut-in 冻结，Cut-in 期间隐藏，夹攻切换时用新施法者和图标覆盖。回到选技能提示时清空报幕，提示仍居中。依附战斗 Top Bar 的安全区缩放且无指针拦截。 报幕占用左上角时暂隐同区域的波次板与文字，退场时恢复其原显示状态。

素材检查用 `Tools/BattleScene/PreparePreviewArt.py`。PlayMode `BattleDualAttackTimingTests` 验证拉斯 S2 开始、最后一段切换梅赛德丝 S1、以及回到指令提示后清除报幕。最终 Unity 验证由协调者统一记录。
