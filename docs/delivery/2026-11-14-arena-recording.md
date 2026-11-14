# 模拟战手机录像对照与验收

## 对照依据

用户录制的 `Screenrecorder-2026-11-14-18-54-07-786.mp4` 为 2608×1200、约 24 帧／秒，全长 2:42.96。此录屏为用户提供的 2026-11-14 国服实机。主页、目录、设置与聊天未见界面倍速控制。

- `0:00–0:03` 对照主页舞台、人物构图及漫画贴纸，沿用已解包的原图。
- `0:26–0:31`、`1:04–1:11` 对照目录和刷新，包含季节侧栏、人数图标和原版横线装饰。
- `0:37–0:50` 对照保留筛选层和箭头；`0:51–1:02` 对照白色搜索状态、用户主动进入输入及搜索后保留浮层。
- `0:31–0:36`、`1:16–1:24` 对照创建窗口和规则卡，默认展示疯狂征召。`1:24` 起对照房间，`1:31–1:38` 对照聊天头像、发送者、正文和输入位置。
- `0:27.04–0:27.37` 逐帧见浅蓝光团变为外扩蓝环并淡出。Unity 用原版 `ui_touch_def` 环、光晕和粒子贴图，采用同一量级的持续时间，Spine 网格变形用 UGUI 近似。
- 本地 `sound/ui1.bank` 对应普通 `event:/ui/btn_normal`、确认 `event:/ui/msgbox/btn_yes`、取消 `event:/ui/msgbox/btn_cancel` 和返回 `event:/ui/btn_back`。录像约 `0:19.29`、`0:22.55` 的波形与返回音匹配，`1:56.22` 与取消音匹配。源路径、导出方式及 SHA-256 写入 Common 的 `ui-feedback-source.json`。

## 发布边界

保留直接进入模拟战。邀请、原版竞技场中间页、联机开战属于其他流程。房间以目录、创建、进出房与文本聊天为当前链路。Common/Core 和正式首场景更新随新母包，服务端迁移先应用。Android 软键盘位置和手机实机音画仍需设备对照。Unity Android 的键盘区域返回空 Rect，组件改用原生视图边距。 [Unity 2022.3 键盘区域](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/TouchScreenKeyboard-area.html) [Android IME Insets](https://developer.android.com/reference/android/view/WindowInsets.Type#ime())

## 本地验收

- Unity 2022.3.62f3 生成四个预制体和启动场景，Binder 与 YooAsset 地址校验通过。素材工具 `Tools/Arena/ImportArenaArt.py --check` 通过，覆盖竞技场 68 项、主页 27 项。
- EditMode 19 项、图形 PlayMode 6 项通过。覆盖页面替换取消旧请求、浮层互斥、泛光池复用、拖动取消与音效关闭后继续播放，以及真实服务器上的刷新、筛选、搜索、设密创建、错误密码、双人入场、双向聊天和离场。房间写入中保持弹窗，显式更新目录会恢复已提交的当前房间。
- 服务端与真实 MySQL 8.4 的 240 项回归全部通过。用 Unity 双会话后的数据库记录核对目录语义事件和创建、进出房、聊天审计。服务运行在本机开发地址 `127.0.0.1:5080`。测试报告、日志和截图位于 D 盘 `.artifacts/arena-recording-review`。
