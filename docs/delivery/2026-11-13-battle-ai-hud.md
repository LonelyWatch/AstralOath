# 战斗 AI、技能双击与 HUD 清晰度

## 原版资料

- 本地 r4262 `db/skill_ai.db` 的 `default_p` 依序列出无敌过滤、Boss、属性克制和生命损失比例。现有普通角色试玩没有 Boss 优先或无敌选中规则；默认敌方排序使用克制、剩余生命百分比、站位。没有克制目标时按后两项。 [社区数据调查](https://www.reddit.com/r/EpicSeven/comments/cgnxxg/how_e7_ai_works/)
- `skill_player.db` 的安洁莉卡 S2 引用 `heal_70allyhp_30_multi_p`，S3 引用 `default`；萝季 S2 引用 `buff_100_debuff_p`，夏绿蒂 S3 引用 `attack_resource100_100_none_p`。官方补丁另记录 Doris 满血施法、Destina 复活、Lilibet/Aramintha 技能顺序调整。当前按项目指定阈值和已实现机制运行。逐角色、Boss、复活和特殊模式策略需按对应资料补证。 [官方 2022-04-14 公告镜像](https://epic7x.com/4-14-update/) · [官方 2022-06-09 公告镜像](https://epic7x.com/6-9-patch-update-new-balance-changes-implementation/)
- 画面对照为 2026-11-08 E7WC 淘汰赛第六日 49:50 的 RTA 实机。录像画面未显示客户端版本或倍速，字形和位置以本地 CSB 为准。 [官方比赛录像](https://www.youtube.com/watch?v=cIn1sX4JNTU&t=2990s)
- Buff 原图 28×28。Unity 通用导入器把平台纹理重新设为有损压缩。状态 PNG 内容与既有透明底板处理保留，原 Status、Readiness 与等级贴图的导入设置明确使用无压缩、点采样和无 mipmap。
- 原 `wnd/hp_bar.csb` 头顶等级使用 26×34 的 `itxt_num0_b` 至 `itxt_num9_b`，以 0.53 倍摆放。双字 x11、x19，底板中心 x14。Unity 显示沿用既有 HUD 全局倍率，按当前单位等级使用对应源字形。
- 原 `sound/events.yml` 和 `sound/gerneal.yml` 将燃魂准备映射到 `event:/battle/soulburn/ready`，GUID `39f88ea3-22f3-4523-ad8a-16c691a45136`，WAV 为 `se_ui_battle_soul_burn_on.wav`。原事件已在现用 `gerneal.bank`，不新增 bank。

## 本轮决策

- 非精灵使在通过被动、冷却、沉默、个人资源检查后按 S3、S2、S1 选技。进攻先选被克制的存活敌人，其次比较剩余生命百分比，平局按站位。
- 精灵使先用有实际可解除减益目标的净化，再在存在严格低于 50% 且可治疗的队友时考虑支援，其余时间用 S1。优先考虑有净化价值和适合接受治疗的目标。手动双击只代选目标，玩家明确选了治疗技时不受自动治疗阈值限制。
- 右上 Auto 启用己方自动，暂停冻结。全自动不使用魂燃。手动成功准备 Burn 播放一次就绪声，Cancel 不重播，双击可照常施放当前已准备的燃魂技。

## 验证

- Unity 2022.3.62f3 EditMode 462 项通过；分批 PlayMode 10 个不同用例通过，覆盖原字形位置、状态移动、双击超时与切技能、Burn 准备/取消/提交、Auto 和敌方回合 HUD 显隐。Unity 实帧对照保存到 D 盘主工程 `.artifacts/battle-ai-hud-art/hud-before-after.png`。
- .NET 服务端 86 项通过，含新增的 7 项 AI 决策回归。真实 MySQL 用例缺连接配置跳过。Luban JSON、二进制和访问器一致性通过。完整地基门禁复跑通过，前两次内容用例遭遇 Windows 临时目录移动拒绝，最后在独立目录串行复测通过。
- 音频原生探针确认已有 bank 解析到指定 GUID，Burn 准备产生非零 PCM 样本，原 30 技能音频仍通过。HUD 与音频原素材清单验证通过。
- 生产 PvP 自动代打、Android 实机和 Windows IL2CPP 构建未执行。
