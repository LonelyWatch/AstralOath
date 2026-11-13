# 战斗长按、通用控件与录像对照

## 参考与实现

- 拉斯手机录像 9.0/9.5/10.5 秒展示行动条详情中的技能长按。9.0 秒出现技能主体、蓝色燃魂区和右侧棕色夹攻说明。11.5–12.0 秒为独立蓝色增益说明。27–39 秒可见左上角施放提示及击数。梅赛德丝录像 12.5/15.25/18.5 秒展示底部技能按钮的说明面板、左侧关联效果及强化技能列表。两段视频均是 2608×1200 的横屏录制，手机标注 Android 16，容器平均约 24.3 帧/秒。可见速度为 ×1，录屏帧率不可直接当作原作运行帧率。
- 网络曾查到站内实战技能说明帧和按住图标交互记录，最终以用户提供的录像与本机资源为主要视觉依据。[STOVE 实战截图](https://d2x8kymwjom7h7.cloudfront.net/live/application_no/96001/default/aeb8810bcde04f1daa6b30263f18ebc1.png) · [实机操作记录](https://www.bluestacks.com/tw/blog/game-guides/epic-seven/es-bluestacks-setup-tw.html)
- 技能主卡按源 `wnd/skill_detail.csb` 使用 `_box_equip_0.png` 黑到深蓝渐变、`_bar_2.png` 燃魂分隔、`_bar_1.png` 强化分隔、`cm_icon_star_s.png` 蓝魂和 `cm_icon_etctime_b.png` 白色冷却钟。 `unit_skill_tooltip.csb` 对应强化所需材料。主卡的源资源切片带超出纹理的高度，Unity 的主卡使用缩短后的固定边保住圆角与柔边；辅助卡沿用源 14 px 切片。
- Buff 与相关效果小卡按源 `wnd/skill_eff.csb` 使用 `_btn_small_white.png`，蓝色为源 RGB(27,67,104)，正文 RGB(101,190,255)；夹攻棕色来自录屏取样。回合数字继续显示在触发图标上。底部技能栏的关联效果卡贴主卡左侧，行动条入口在右侧有空间时展开，狭窄视口可纵向排布。
- 所有说明使用 `原始资源/font/daum.ttf` 的 EpicSeven 字体，SHA-256 为 `2643979e008564e3b59455f7548aa6f844c155b21b3aa42d00dcba1903c3c1ab`。源主标题、正文、元信息、辅助标题的有效字号分别约 22、18、17、19 px，映射至现有 HUD 比例；TMP 动态图集保留原字形。中文配表文本按当前规则展示，试玩零强化不会硬贴参考录像中的 +5 角标。
- 游戏目录只读。使用的原图保持源字节，绿色通用按钮使用 `cm_btn_norgreend.png` 与 `cm_btn_norgreenp.png`。面板宽度、滚动、边界处理及 TMP 渲染由 Unity 适配。
- 通用按钮使用本机 FMOD 2.03.15 `event:/ui/btn_normal`，来自 `sound/ui1.bank`（SHA-256 `005c2f5479c9bd7c9557f4fbda6cca0dc5fdfcce3d5b67ffe13a74198f23e804`）。Unity 导入的是原事件的 PCM16 双声道 48 kHz 渲染，裁掉前后静音，无增益改写。WAV SHA-256 `2888aad5cb9853f51cc46dcf8b14e940e54040b72605efe8b8cf0eaac3ac0d87`。复现入口见音频探针 README。
- 主技能区显示魂力、冷却、说明、可用的燃魂及强化条目。行动条紧凑说明省略强化列表。长按技能、头顶 Buff 或行动条图标使用同一弹板，短按行为继续由原 HUD 控制。鼠标和触屏使用同一个 EventSystem 指针协议，松手不产生额外点击；长按期间允许滚动文本，点空白关闭。
- 当前项目仍使用森林试玩与八英雄编队。录屏训练场、木桩、局内连续试招、回合脚下光圈、魂力蓝光飞行轨迹等需独立对照。录像出现的原游戏技能强化配置与当前配表不完全相同，界面以项目实际规则和配表文案为准。

## 测试与发布

- 变更横跨 Common 与共享战斗规则。Common 新组件和规则版本升级需要新母包，并协调客户端与服务端版本。Unity 2022.3 完整 EditMode 481 项、服务端战斗 61 项、角色导入 50 项通过。宽屏测试覆盖 16:9、2608×1200 和 4:3；Android 真机手势、刘海安全区和 Windows 独立包仍需复测。

- 战斗截图保存在 D 盘主工程的 .artifacts/ten-hero-frames，文件名为 long-press-skill.png、long-press-status.png。素材的原路径和 SHA-256 记录在 Common/Res/ui-source.json。
- 最终图形 PlayMode 批次覆盖 37 项，36 项首次通过，剩余原场景草面断言在改用舞台原始草面边界后单项复跑通过。长按截图验证 EpicSeven 字体、技能原图与燃魂分隔、状态真实 60%、取消误触、2608×1200 截图和关闭行为。
