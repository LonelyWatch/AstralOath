# 头顶状态图标与护盾表现

> 本文记录初版交付。普通盾原表映射、完整六层与图标底板合成已在[后续修正](2026-11-13-battle-shield-refinement.md)更新。

基线 `0b0231e`，Unity 2022.3.62f3，D 盘主工程。直接打开战斗场景并 Play，安洁莉卡回合选择 S3，队友头顶出现免疫及护盾图标，HP 轨显示冷白盾段，人物周围出现白色亮边。敌方后续命中会消耗可见盾段。

## 原作依据

- 2024-04-11 的原作公告规定，增益在授予时按持续时间长者靠左，同回合先授予者靠左。减益及特殊效果按施加时间排列，正常回合推进不重排。公告另记录原作可驱散效果从左下开始的规则，此轮只实现 HUD 排列，项目权威驱散仍使用现有结算策略。[STOVE 官方原帖](https://page.onstove.com/epicseven/global/view/10111857?boardKey=986) · [原公告正文镜像](https://epic7db.com/news/411-thu-update-content)
- 2026-11-08 E7WC Knockout Stage Day 6 49:50 的 1080p 帧可见同一单位多个图标从血条左侧排开，回合数字重叠在左上角，蓝色增益与暖红减益有对应数字色。证据文件为 `.artifacts/health-death-reference/battle-2990.png`。[官方比赛回放](https://www.youtube.com/watch?v=cIn1sX4JNTU&t=2990s) · [赛事公告](https://newsroom.smilegate.com/en/bbs/board.php?bo_table=eng&wr_id=508)
- 本地 `wnd/hp_bar.csb` 的 `n_buff` 相对原框左边 +1、中心上方 15，`hp_bar_r.csb` 提供 28×28 图标、.9 缩放和 12×16 `itxt_num3` 数字参考。两类血条的白盾节点都用 `img/game_hud_bar_sh.png`。`condition_bar.csb` 对应 Boss 怒气、削韧与弱点槽。
- 2026-09-19 发布的用户参考视频 3:09–3:21 右侧角色可见绿段后拼冷白盾段，3:23–3:24 白段缩短，角色周围是通透的冷白蓝椭圆。安洁莉卡旧版视频 58–61 秒补充验证了满血时绿段被压缩、免疫在左护盾在右和绕身体的持续亮边。两段画面均未标注精确版本或盾量。[2026 年盾段变化](https://www.bilibili.com/video/BV16ZeU64E3Z/?t=189) · [安洁莉卡 S3](https://www.bilibili.com/video/BV1VB4y137Dt/?t=57)
- 历史玩家直接测试提供了盾值挤压和显示宽度的交叉参考。[2018 年实测](https://www.reddit.com/r/EpicSeven/comments/a8fpiy/barrier_information/) · [2021 年复测](https://www.reddit.com/r/EpicSeven/comments/l3u2bv/question_reguarding_barrier_mechanism/)

## 项目行为与扩展点

- 状态表仅由 `ImportStatusHud.py` 的映射维护，直接复制 `buff/` 的 15 种源 PNG 和 `img/itxt_num0…9.png`。生成的 `Statuses/source.json` 记录分类、尺寸约束的布局来源及 SHA-256。配置集中在 `UnitPresentation`，现有视图使用原生 Image 槽和数字。5 列换行、同类重新施加保留原位、特殊效果纳入按施加顺序显示的后组属于当前项目布局决策。原作在超长混合列表中的组间优先级、上限与同类刷新细节仍需实机核对。
- 盾条拼在实际生命右端，可用尺度为 `max(最大生命, 可见生命 + 可见护盾)`。超出上限时压缩绿色、白色与生命刻度，未超出时保留轨尾空位。护盾量随视觉 HIT 消费，在盾耗尽后继续扣生命。该公式及 HUD 缓动是项目实现，精确原版归一化及缓动参数尚无直接测量。
- 白边由 `stse_protect` 的 CFX、Spine 2.1.27、SCT 和图集导入。保留源边缘几何、Alpha 时间线和加色模式，项目白边采用白色 tint，其他源槽不参与。通用护盾在原客户端对该特效的具体映射仍待证实。资源哈希与转换依据在 `Res/Effects/Shield/source.json`。光晕随身体变换和排序，隐藏时暂停采样，盾被耗尽、角色死亡或重开时关闭。
- 运行时先处理接受事件，行动结束使用权威快照校对数值及剩余回合。状态图标和血条复用同一世界投影与可见性。该场景继续使用直接 Unity 资产引用，业务代码、源图和配置应同批发布；本次没有重新验证生产 YooAsset 包。

## 验收

- 新增场景回归在基线失败，日志显示缺失头顶状态图标，结果见 `.artifacts/ten-hero-PlayMode-statuses-red.xml`。
- Unity EditMode 446 通过。PlayMode 全量 47 通过、1 项可选截图跳过。启用截图后发现角色列表按 E7 ID 扩充导致诊断用例选错梅赛德丝，改用角色 ID 后单独补跑通过。结果见 `.artifacts/ten-hero-EditMode-statuses-final.xml`、`.artifacts/ten-hero-PlayMode-statuses-final.xml`、`.artifacts/ten-hero-PlayMode-statuses-capture-fixed.xml`。
- 本轮验证实际安洁莉卡 S3、原图和回合数字、同类刷新、随角色平移/缩放/隐藏、盾条和光晕的生命周期、盾吸收分段、攻击后加状态及自身状态到期。1920×1080 实拍见 `.artifacts/ten-hero-frames/battle-c1062-s3-shields.png` 和 `.artifacts/status-shield-frames/`。
- `Tools/Check-Foundation.ps1` 通过本地配置、协议、服务端与内容测试。MySQL 实库、Windows/Android Player 和生产热更未执行。
- 使用项目的角色素材 Python 环境执行 `Tools/BattleScene/ImportStatusHud.py --check` 和 `Tools/BattleScene/ImportBattleShieldAura.py --check`。用 Unity Test Runner 执行 EditMode、PlayMode，带图形设备并配置 `ASTRAL_UNIT_CAPTURE_DIR`、`ASTRAL_BATTLE_CAPTURE_DIR` 可捕获受盾、耗尽和安洁莉卡 S3 画面。截图保存在 D 盘本地 `.artifacts`。
