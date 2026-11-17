# Home

`Scenes/GameHome.unity` 是游戏首场景。开发入口为 Unity 的 **Tools → 游戏主页 → 打开启动场景**。启动入口先登录并选服，校验并加载内容后，还需点击活动标题页进入主页；详见[游戏入口、账号与更新](../../../../docs/development/game-entry.md)。主页按地址 `home/main` 显示 HomeWindow；点竞技场模拟战打开 Arena 模块。旧 FoundationDemo 场景继续用于 Echo 和启动诊断。

`HomeWindow : UIBaseView` 和 `HomeWindowBinder` 分离组件与导航。主页使用原版 `bgpack/lobby_bg3.png` 和夏日夏绿蒂 c5009 的 Spine 3.8.99 立绘，来源见 `Res/Textures/source-manifest.json`。预制体沿安全区锚定，采用高度匹配的 CanvasScaler。退出主页会回收立绘 Camera、RenderTexture 和 Spine 实例。

主页右上「更多」打开快捷浮层，点击「英雄」进入英雄详情及可展开的持有列表。右上「背包」进入独立布局，按英雄分行展示等级、星级与空装备槽、神器槽。装备与神器分页可以切换，首版为空。容量来自服务器，列表按实例 ID 展示并复用可见行，详见[账号与英雄背包](../../../../docs/plans/2026-11-17-hero-inventory.md)。

更多抽屉、英雄详情和背包按原始布局文件复建。英雄背包使用原版装备、神器和专属装备槽，详情显示由已有成长曲线计算的基础属性。未接入的物品页保留原版空面板。

业务程序集由 Foundation 热更清单登记。稳定启动契约和立绘桥改动需要新母包。 [操作与验收](../../../../docs/development/arena-lobby.md)

主页入口显示“竞技场”，直接打开模拟战目录。舞台沿用原背景和原版贴纸资源。页面切换交给 `WindowCoordinator`，点击泛光和普通音效由启动 Canvas 负责。

Windows 独立客户端默认可缩放窗口，主页右上角按钮或 F11 切换无边框全屏。切回时恢复本次运行记住的窗口尺寸，Android 和 Unity 编辑器的主页隐藏该入口。

主页根 Canvas 集中配置普通／确认／关闭、返回／取消和禁止触发三类音色。点击细环与粒子由同一反馈池复用。页面在校验失败时可显示 Common 底栏提示。
