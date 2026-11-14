# Home

`Scenes/GameHome.unity` 是游戏首场景。开发入口为 Unity 的 **Tools → 游戏主页 → 打开启动场景**。Foundation Bootstrap 校验并加载内容后，按地址 `home/main` 显示 HomeWindow；点竞技场模拟战打开 Arena 模块。旧 FoundationDemo 场景继续用于 Echo 和启动诊断。

`HomeWindow : UIBaseView` 和 `HomeWindowBinder` 分离组件与导航。主页使用原版 `bgpack/lobby_bg3.png` 和夏日夏绿蒂 c5009 的 Spine 3.8.99 立绘，来源见 `Res/Textures/source-manifest.json`。预制体沿安全区锚定，采用高度匹配的 CanvasScaler。退出主页会回收立绘 Camera、RenderTexture 和 Spine 实例。

业务程序集由 Foundation 热更清单登记。稳定启动契约和立绘桥改动需要新母包。 [操作与验收](../../../../docs/development/arena-lobby.md)

主页入口显示“竞技场”，直接打开模拟战目录。舞台沿用原背景和原版贴纸资源。页面切换交给 `WindowCoordinator`，点击泛光和普通音效由启动 Canvas 负责。

Windows 独立客户端默认可缩放窗口，主页右上角按钮或 F11 切换无边框全屏。切回时恢复本次运行记住的窗口尺寸，Android 和 Unity 编辑器的主页隐藏该入口。
