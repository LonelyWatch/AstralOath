# 原版护盾分层与状态图标底板

> 后续[挂点复核](2026-11-13-battle-shield-anchor-ready.md)确认普通盾应随身体节点中的原 Spine `shadow` 骨骼采样，场景地面影子仅作缺骨骼时的回退。以下为当次实施记录。

基线 `0233315`，Unity 2022.3.62f3，D 盘主工程。直接运行战斗场景，安洁莉卡回合释放 S3，查看战场复位后持续显示的白蓝护盾。

## 普通护盾的来源

- 本机 r4262 的 `db/skill_player.db/sk_c1062_3` 展示效果 ID 为 `120 / 106`，同时含 `CS_ADD_SHIELD_CASTER_MAXHP`。`db/cs_player.db` 的普通盾键 `106` 和 `shield_caster_maxhp` 对应 `cs_icon=stic_protect`、`cs_effect=stse_protect.cfx`、`cs_eff_bone=shadow`。免疫 `120` 使用 `stse_immune_weak.cfx / top`。
- `skill_player.db` 原始 SHA-256 为 `ea7dacc26ce8b544ddab0808b484e7405e464bf0feaa6e219c01652231c73670`，`cs_player.db` 为 `c031c39f2a96af49d1b4240be6c60c37091289e5794300f7b6229369b06b026f`。去外层的 PLPcK 记录中，技能、状态 106、120、`shield_caster_maxhp` 的偏移分别为 399577、37847、41388、64874。状态列索引 69–71 为图标、特效、挂点。
- `stse_protect.cfx` 直接引用 2.1.27 Spine 及原 SCT 图集，偏移 y=20、z=1、scale=1.5，循环约 2.1666 秒。恢复普通混合的盾面及原版加色的 `edge/shield_f/g1/g2/glow`，保留原色、透明度、变形和混合。项目使用现有影子节点承接 `shadow`，继承角色渲染缩放及遮罩。原表记录的 Boss 与元素盾另有特效，扩展时按记录配置。原引擎逐骨骼挂点与跳跃镜头需专门帧对照。
- 原版实战可见盾持续期间两侧亮弧更宽、中心通透。安洁莉卡录像 56.33 秒开始出现盾边，技能光柱、地面环及同期免疫效果很快退去，白蓝盾延续到 61 秒。2026-09-19 录像 3:11 与 3:38 可见同类轮廓。[安洁莉卡 S3](https://www.bilibili.com/video/BV1VB4y137Dt/?t=56) · [2026-09-19 实战](https://www.bilibili.com/video/BV16ZeU64E3Z/?t=191)

## 状态图标底板

- 原版 `buff/stic_*.png` 自带圆角黑底，13 张底板主 Alpha 为 191/255，隐身 190/255，减速 204/255。`hp_bar_r.csb` 图标为白色全强度，使用预乘混合，子节点只有回合数字。2026-11-08 E7WC 49:50 可见浅色石面透入红色减益格，2026-09-19 录像 3:33 强闪时盾图背后也变亮，印证底板保有少量透光。[E7WC 官方 49:50](https://www.youtube.com/watch?v=cIn1sX4JNTU&t=2990s) · [2026-09-19 亮场样本](https://www.bilibili.com/video/BV16ZeU64E3Z/?t=213)
- Unity Linear 下直接使用 A=191 的黑底，在白底上会留下约 137/255 显示亮度。状态导入器把透射率转换到 Linear 目标，白底上改为约 64/255，减速约 51/255。转换同时补偿有色像素，保留源图边缘轮廓与透明端点。精确色彩合成随背景而变，这是一种局部显示校准。数字 PNG 保留原字节，状态图标的 Windows/Android 导入禁用压缩以保护 28×28 像素边缘。
- `Tools/BattleScene/ImportStatusHud.py --check` 和 `ImportBattleShieldAura.py --check` 核对源和产物 SHA-256。原始目录只读，项目保留已有 GUID，未导入整表。

## 验收

- Unity 图形回归覆盖六槽、原色、变形、混合，白底下普通盾和减速图标的黑底亮度及透明圆角。战斗场景回归覆盖实际安洁莉卡 S3、遮罩、影子锚点、位移和吸收结束。
- 基线红测记录只有 edge 槽、4 个顶点，白底图标明显偏亮。最终 EditMode 全量 449 通过，战斗相关 PlayMode 14 通过。结果见 `.artifacts/ten-hero-EditMode-shield-refine-final.xml` 与 `.artifacts/ten-hero-PlayMode-shield-refine-final.xml`。Windows/Android Player 未重建。
- Unity 实拍保存于 `.artifacts/shield-refine-final-frames/angelica-s3-original-shields.png`。另有 `status-barrier-full.png` 与 `status-barrier-consumed.png` 用于复核生命周期。原版参考序列在 D 盘本地 `.artifacts/shield-aura-source-audit/`。
