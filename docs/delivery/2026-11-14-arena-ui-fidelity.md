# 竞技场界面细节、聊天表情和小窗复核

录像为 2026-11-14 用户提供的 `Screenrecorder-2026-11-14-18-54-07-786.mp4`，2608×1200、约 24 帧／秒，界面未见倍速调整。同时使用用户后来发来的赛季栏截图定位差距。

- `0:27.04–0:27.37` 触点中心先亮，再扩成蓝环并淡出。旧版用两个同心贴图缩放，圆心过早发黑。新版本从 `ui_touch_def` 的 4 层原始 Spine 2.1 网格、颜色、顺序和混合曲线按 60 帧／秒采样 25 帧，约 0.41 秒。运行时在现有池里使用单个透明 UI 图元，外围粒子复用原图并近似时间。记录见 `ui-feedback-source.json`。工具 `Tools/Common/UiTouchPulse/BakePulse.py --check` 可逐字节验证生成。
- `0:26–0:31` 目录左侧使用 `pvplive_base.csb/left_bg` 对应的 `_bg_black_gold_s` 底板及源分隔线，贴合录像靠右的直边和中间凹口。文字颜色按录像调为中性灰及金色。右侧仍只有模拟战入口。
- `0:37–1:02` 的工具条使用原生黑底／按下、搜索暂存白底／按下和独立排序箭头按下图。创建、取消、确认、入场、离开和聊天发送继续对应各自的源按钮状态。
- `1:40–1:44` 录像中的表情浮层为黑底、三列和纵向滚动。客户端使用只读目录 `emoticon_chat` 中的 67 张原版静态图和独立表情消息。源 `chat_emoji_tip.csb` / `chat_node_emoji.csb` 均引用静态图。当前使用素材 ID 排序和本地通用目录，原账号表情解锁、不同地区字样和原版排列仍待对照。迁移 `004_arena_chat_stickers.sql` 扩展数据，沿用权限、限速、历史游标和审计。`Tools/Arena/ImportArenaStickers.py --check` 同时验证素材与服务端白名单。

Windows 第一次启动默认 1280×720 可缩放窗口。主界面右上角或任意主页／竞技场页面按 F11 切换无边框全屏，切回恢复当次窗口尺寸。使用 Unity Player 的屏幕模式接口，按帧读取应用后的状态。 [Unity 窗口接口](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Screen.SetResolution.html)

Unity EditMode 19 项，图形 PlayMode 7 项，真实 MySQL 241 项通过。图形回归包含新旧触点差异的像素断言、窗口生命周期以及 Unity 和另一 HTTP 会话的表情双向收发。安卓实机画面对照待验。测试产物和视频参考落在 D 盘忽略目录。

Windows x64 IL2CPP v0.1.3 构建和冷启动通过。Win32 检查在实际 Player 读到 1280×720 且有标题栏，按 F11 切至 3840×2160 无边框，再按 F11 回到原 1280×720 窗口。
