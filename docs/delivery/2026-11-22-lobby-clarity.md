# 大厅动态立绘的 1080p 和 4K 清晰度

用户的 AstralEQ32 截图显示夏日夏绿蒂 `c5009`。参照本机国服录屏 `Screenrecorder-2026-11-17-22-21-00-778.mp4` 00:06 左右的大厅和 pack-15-r4262 原始图集复核构图。录屏的游戏版本、发布日期和播放倍速未确认。

大厅角色由相机逐帧渲染到正方形纹理，再由 Canvas 合成。旧版将纹理固定为 1024×1024，而显示区域边长约为窗口高度的 110%。1920×1080 下需要 1188 像素，3840×2160 下需要 2376 像素。旧图在两个分辨率上分别被放大约 1.16 倍和 2.32 倍。

`HomeWindowBinder` 在大厅角色相机绘制前，通过 `UiRenderSurface.ScreenPixels` 测量投影后的像素，必要时调整原渲染纹理。相机局部中心保持 (0, 4.85)，正交半高保持 3.10。退出大厅时解除绘制回调，原组件继续负责释放摄像机和纹理。大厅热更预制体绑定 `lobbyPortrait`，已分发母包的 `UISkeletonPortrait` 无须修改。

原角色图集为 1636×1032，其中 `head` 图块为 118×109，普通表情的 `normal/face_nm` 为 44×44。桌面角色纹理禁用 mipmap，保留原尺寸，原版图集标注线性采样。舞台背景独立使用 1580×720 原图。提高渲染分辨率后，4K 仍会看见原纹理的细节上限。[原图尺寸与溯源](../../Assets/Game/Modules/Home/Res/Textures/source-manifest.json) · [图集分块](../../Assets/Game/Modules/Home/Res/Atlas/c5009.atlas.txt)

## 验收

Unity 2022.3.62f3、Windows D3D11 PlayMode 通过真实 `HomeWindowBinder.OpenAsync` 打开大厅，先后输出 1920×1080、3840×2160 和 1920×1080。旧实现的首项期望 1188，而读取到 1024；另一次修前采样三个尺寸均读到 1024。改后专测 1/1 通过，边长依次为 1188、2376 和 1188，正交范围、机位、纹理对象身份及退出时的资源释放均核对通过。实机 PNG 与 1:1 人脸局部对照在本机 `.artifacts/lobby-clarity-r33/{before,after}` 和 `comparison.jpg`。两次采样的待机动画独立推进。测试报告在 `.artifacts/black-transition/resolution-red.xml`、`resolution-green.xml`。

复现时可设置 `ASTRAL_LOBBY_CAPTURE` 为 D 盘输出目录，再运行 `HomeStartupPreparationTests.LobbyPortraitMatches1080pAnd4kWithoutChangingFraming`。
