# 联机入场 IGNITION 两侧断边

审批图 010 的 1920×900 截图在 x=160 和 x=1760 出现两条竖直断边。旧转场将全部 Spine 和粒子画进固定 1280×720 的 RenderTexture，再用 `FitInParent` 按 16:9 显示。900 高的画面里，中间纹理恰好宽 1600 像素，左右各空 160 像素。烟雾和火光抵达纹理边缘时被立刻切掉。原始 CFX 中的 `pvp_ready_font_fast`、`pvp_igni_pati` 仍作为核查依据，原版资源保留原样。

`BattleOnlineGate` 现在按显示目标的比例同步正交相机和 RenderTexture，图像铺满整个转场 Canvas。IGNITION 保持原有的取景比例、字号和位置，超出旧 16:9 区域的烟雾和粒子可以继续绘制。蓝光门继续采用铺满取景。窗口尺寸改变时重新核对投影，只有需要更改纹理像素尺寸时才重新分配，结束后释放。画布用于离屏截图时，取实际目标相机的尺寸。

## 验证

- Unity Editor PlayMode 回归 `IgnitionCoversTheCanvasAtWideAndTabletAspectRatios` 在旧实现中失败。1920×900 要求宽高比 2.1333，旧纹理仍为 1.7778。
- 修复后在实际渲染设备上验证了 1920×900、2240×960 和 1440×1080。转场中间纹理、相机和 Canvas 比例一致，尺寸稳定时复用纹理。
- `ArenaBattlePlaybackTests;ArenaDraftOnlineTests` 共 6 项通过，包括闭门遮挡、开门、双方落地、双方座位视角以及经本地 5080 服务完成选禁到战斗的联机路径。
- 在 1920×900 联机合成截图中复核了烟雾与火光。沿原断边两侧取相邻像素列，y=60–464 的平均红通道跳变从 67.50 / 57.71 降到 1.55 / 2.15（8 位通道值）。修复后与邻列波动相近，画面无原竖直断线。
- 证据见 D 盘主工程的 `.artifacts/arena-ignition-clipping/`，其中 `IgnitionWideRed.xml` 为旧实现回归结果，`IgnitionWideGreen.xml` 为通过报告，`arena-draft-landing.png` 为修复后截图。验证在隔离 Unity Editor 镜像运行，客户端包尚未构建。

## 复验

将主工程最新改动同步到独立 Editor 镜像。运行已启动的本地 Arena 测试服务器，以下命令从 D 盘主工程根目录运行。Unity 保持图形设备启用，以便验证真实渲染输出。

```powershell
. ./Tools/ProjectEnvironment.ps1
Initialize-AstralProjectEnvironment -RepositoryRoot (Get-Location).Path | Out-Null
$env:ASTRAL_ARENA_TEST_URL = 'http://127.0.0.1:5080'
$env:ASTRAL_ARENA_CAPTURE_DIR = (New-Item .artifacts/arena-ignition-clipping -ItemType Directory -Force).FullName
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe'
$project = (Resolve-Path '.artifacts/UnityBattleScene').Path
$log = Join-Path $env:ASTRAL_ARENA_CAPTURE_DIR 'IgnitionWideGreen.log'
$result = Join-Path $env:ASTRAL_ARENA_CAPTURE_DIR 'IgnitionWideGreen.xml'
Start-Process -FilePath $unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
    '-batchmode', '-projectPath', $project, '-logFile', $log, '-runTests',
    '-testPlatform', 'PlayMode', '-testFilter', 'ArenaBattlePlaybackTests;ArenaDraftOnlineTests',
    '-testResults', $result
)
```
