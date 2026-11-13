# 战斗魂槽

标准战斗双方各自最多 80 点魂力。共享 Battle 的 `BattleFlowRules` 提供上限与每槽 10 点的规则，结算仍由服务端同源实现。客户端消费己方 `SoulChanged` 的绝对值并用快照对齐。模式可配置初始魂力，当前本地试玩保留 20 点，联机验证房开场仍为 0 点。战斗规则版本从 1 提升到 2，避免旧版上限产生不同回放结果；协议字段和燃魂费用保持既有契约。

魂槽使用本机原资源 `wnd/inbattle_hud.csb` 的八个坐标，横坐标 526、568.5、611、653.5、696、738.5、780.1714、822.6714，纵坐标约 44，以 1280×720 设计坐标投影至现有 1920×1080 安全视口。满十点显示一个独立蓝魂，底部蓝色条显示余数，例如 79 点为七个蓝魂与 90% 底条，80 点为八个蓝魂与空底条。原作 Burn/Cancel 在可使用时覆盖魂槽右侧；魂槽、按钮及技能组仍在各自生命周期更新。对照来自 2026-11-14 的原版实录 00:01–00:04，录制帧 2608×1200、简中、1×，客户端版本未确认。参考帧保存在 D 盘 `.artifacts/battle-inspection-reference`。

蓝魂从 `effect/soul_01.cfx` 导入闭包。原 `intro` 约 0.9666 秒，之后循环 `loop` 约 4.9333 秒，Spine 比例 0.8，并在 200 毫秒后启动原粒子。开局已有的蓝魂直接显示循环，已消耗的蓝魂立即停止并回收显示。此处保留了原魂本身的动画，未接入原作飞魂轨迹与消耗时额外的爆发特效。运行 `Tools/BattleScene/ImportHudEffects.py --check` 可核对源和生成物哈希，`AstralOath.Client.BattleScene.Editor.BattleSceneBuilder.InstallHudEffects` 可重建当前场景引用。

共享规则新增测试实测先在上限 20 的基线失败，修改后筛选 Battle 的服务端测试 59/59 通过。Unity 2022.3 PlayMode 实景用例 `SoulGaugeFillsEightNativeSlotsAndLoopsOnlyEarnedSouls` 已通过，验证 9/10/79/80/68、对方事件过滤、真实 Spine 网格、槽距和重开回收。采样画面位于 D 盘 `.artifacts/ten-hero-frames/battle-soul-eight.png`。
