# 八英雄战斗场景

## 阵容与换人

- 默认我方从前排、下方、上方到后排依次为拉斯 `c1001`、安洁莉卡 `c1062`、梅赛德丝 `c1005`、赛西莉亚 `c1002`。
- 敌方对应位置为萝季 `c1003`、席可 `c1004`、绮世 `c1006`、维德瑞 `c1007`。现有敌方自动回合使用各角色自己的 S1，按共享战斗规则结算。
- `Res/Prefab/BattleActor.prefab` 是唯一的角色预制体，八个站位各引用一个实例。角色唯一编辑入口是实例的 `BattlePreviewActor > Hero Id`，表决定面板、技能和元素，场景资源目录决定模型及演出。设置后直接 Play，运行中修改可重开应用。旧 Inspector 数组仅作迁移兼容，编辑工具把旧配置写回实例并清空数组。
- `Tools/战斗场景/更新角色资源引用` 更新缓存模型，保留站位、实例的已有角色 ID 及队形配置。替换通用 Prefab 实例后可运行该菜单补齐仅用于布局预览的模型缓存。服务端对战预览仍沿用共享工厂现有默认值。

## 附着与显隐

角色表现 View 拥有对应头顶 HUD 和护盾。护盾特效从脚底影子换算一次初始偏移，随后作为角色身体节点的子物体移动和缩放，身体跳起时仍跟随。血条、Buff 和回合数字沿骨骼 `top` 逐帧投影到同一个 HUD Canvas，和护盾共同遵循身体 Renderer 的显隐状态。

## 验收

- 旧场景的回归在后排重复拉斯处失败，日志 `.artifacts/ten-hero-PlayMode-eight-heroes-red.xml`。
- Unity 场景更新命令验证八个实例引用同一 Prefab GUID，场景 YAML 中八个 ID 互不重复。
- Unity EditMode 450 项通过，包含检查共享 Prefab 连接与八个实例 ID。针对性 PlayMode 5 项通过，验证敌方自动使用自身普攻、运行时换角、护盾随身体移动和缩放、与血条同步隐藏。见 `.artifacts/ten-hero-EditMode-eight-heroes-final.xml` 和 `.artifacts/ten-hero-PlayMode-eight-heroes-final-core.xml`。
- 服务端及共享规则测试 77 通过，MySQL 集成 1 项因缺少环境跳过。见 `.artifacts/eight-hero-server-final.log`。
- 开局实拍 `.artifacts/eight-hero-battle-frames/eight-hero-opening.png`。本轮没有重建 Windows 或 Android 客户端。
- 全量 PlayMode 中 Unity 原生层先在 `TextAsset.GetBytes` 崩溃。试验改为读取 FMOD 音频库原生视图后，另一次启动又在骨骼 `TextAsset.text` 处触发相同的原生断言 `arraySize >= len`，因此撤回音频试验。两次整轮均未产生测试 XML，不能记为全量通过。复现见 `.artifacts/ten-hero-PlayMode-eight-heroes-complete.log` 与 `.artifacts/ten-hero-PlayMode-eight-heroes-bank-view.log`。
- 分批 PlayMode 覆盖 20 项不同战斗测试。第一批 14 项通过，1 项旧史莱姆夹具因 Unity JsonUtility 不支持技能系数的 Nullable 字段失败。夹具改用实战同一解析器后，最终 6 项复跑全部通过（含该夹具、八英雄、护盾与死亡）。见 `.artifacts/ten-hero-PlayMode-eight-heroes-gameplay.xml`、`.artifacts/ten-hero-PlayMode-eight-heroes-last.xml`。

2026-11-14 后，角色阴影和头顶状态模板由通用 BattleActor 持有，元素徽标随实战快照设置。详见[预制迁移与验证](2026-11-14-battle-actor-burn.md)。
