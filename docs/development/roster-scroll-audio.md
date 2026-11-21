# 英雄与竞技场角色滑条的滚动声

## 原版对照

英雄页依据本机录屏 `Screenrecorder-2026-11-17-22-21-00-778.mp4`。02:07.556 至 02:14.105 可见右侧角色卡连续经过焦点，随后停住。竞技场依据国服秋季赛 P2 `2.3060-3091_43325065328.mp4`。04:23.619、04:23.747、04:23.939 均能在选人目录滚动时辨认出同一短音。这两段使用普通英雄页和竞技场选人形态，素材的公开发布日期和播放倍速未确认。时间取各自文件的播放时间。[竞技场录像片段](https://www.bilibili.com/video/BV1n7x96DETN/?p=2&t=263)

原资源 `sound/events.yml` 和 `sound/ui1.yml` 均将 `event:/ui/list_scroll` 指向 `ui1.bank`，事件 GUID 为 `003d1faa-1642-4b85-9712-3e100a68cfbc`，列有 `se_ui_list_scroll_2_01.wav` 和 `se_ui_list_scroll_2_02.wav`。用 FMOD Studio 2.03.15 的 48 kHz 离线混音器载入原始 `master.bank`、`master.strings.bank` 和 `ui1.bank`，校验事件 GUID 后连续触发十次。默认参数下十次输出的有效波形相同。保留一份原混音的 8,000 个立体声 PCM16 采样帧，时长约 167 毫秒，未调整音量。

将两个录像片段的音轨转为 48 kHz 单声道，与事件起音后约 83 毫秒的波形做一阶差分、归一化互相关。英雄页多次峰值约 0.97，选人目录上述三次约 0.91–0.94。成品为 `Assets/Game/Common/Res/Sounds/UiRosterScroll.wav`，事件和文件校验值记录在 `ui-feedback-source.json`。

## 实现及验收

英雄页与 RTA 通过同一个 `FocusedRosterScroll` 判定经过焦点的卡片。手指或鼠标拖动、滚轮以及松手后的惯性改变最近行时，从根 Canvas 的 `GameUiFeedback` 播放该短音。一个渲染帧最多播放一次，避免滚轮或低帧率造成多个声部同时起音。单行内移动、位于列表边界、绑定或恢复位置、筛选、布局重算及程序吸附均不触发。正式 `GameHome.unity` 与 Canvas 生成器均引用共享音频。

2026-11-22 使用 Unity 2022.3.62f3 的独立 PlayMode 镜像验证。修改前两个专属测试都失败，分别定位到场景缺少音频依赖和拖动跨卡后没有播放。修改后两个测试均通过，原有可选行吸附测试亦通过。可重跑 `FocusedRosterAudioTests` 和 `FocusedRosterSkipsUnavailableRowsAndHandlesEmptyLayout`。本次触及 Common AOT 和启动场景，Windows 原生验收须使用重新构建的母包。
