# 本机资料与缓存路径

本机主工作区为 D:/UnityProjects/AstralOath，main 的实际位置每次以 git worktree list 为准。新资源转换、中间文件、日志、截图、视频参考、测试镜像和工具下载写入主工作区 .artifacts 或 .tools；长期保留的外部安装包放 D:/AstralOathData/installers。禁止重新写入 C 盘历史工作区、Downloads 或用户 Temp。D:/sevenepic 保持只读。

## 已完成迁移（2026-11-13）

旧工作区 C:/Users/Admin/.codex/worktrees/astraloath-foundation/AstralOath 的 .artifacts 和 .tools 移到 D:/AstralOathData/legacy-worktree 下，旧路径是 NTFS junction。前者139185个文件、17542281188字节，后者14859个文件、2104091648字节。复制后逐文件 SHA-256、长度和源稳定性复核；源在首轮复制期间变化后重新全量核验，未直接删除活动源。

fmodstudio20315.unitypackage、UnityHubSetup-x64.exe、UnityHubSetup-x64 (1).exe 从 Downloads 搬到 D:/AstralOathData/installers，保留两个安装包。累计释放 C 盘20459810816字节（19.05 GiB）；迁移后可用约29.21 GiB。未删除代码、Git数据或其他项目共享缓存。

未搬动正在使用的 C:/Program Files/Unity/Hub/Editor/2022.3.62f3 编辑器，也未关闭有潜在未保存工作的 Editor。系统安装和共享缓存不属于已完成迁移；迁移Unity安装应退出编辑器后通过Hub识别D盘安装并单独验证。

原始验证记录在主工程 .artifacts/disk-migration：legacy-migration-20261112-161856.log、artifacts-verified-sha256.tsv、tools-verified-sha256.tsv、installers-verified.json。巨量文件清单不进入Git。删除junction必须针对链接本身，不能递归删除其目标。

## 运行缓存补迁

C:/Users/Admin/AppData/LocalLow/AstralOath/AstralOath 已迁至 D:/AstralOathData/runtime/AstralOath，原产品路径为junction，未来persistentDataPath写入通过该链接落D盘。仅此产品目录，未移动整个LocalLow或其他项目数据。38个文件、28441884逻辑字节，复制前独占可读检查、双端SHA-256校验及切换后通过旧链接重验均通过。Editor保持运行，无强制终止进程。

该次实测额外释放28512256字节（27.19 MiB），连前期合计20488323072字节（19.08 GiB）。记录为 .artifacts/disk-migration/runtime-verified.json。Unity安装与系统共享缓存仍是上述例外。

## 后续启动

使用项目PowerShell执行 Tools/Open-Unity.ps1（工作目录 D:/UnityProjects/AstralOath）。此入口检查同工程已运行时仅报告，不开第二实例。当前Editor不被追溯修改环境；保存并正常退出后，下一次从新入口启动才继承缓存位置。不要用结束进程代替保存退出。

Tools/ProjectEnvironment.ps1 统一设置进程范围 TEMP/TMP/TMPDIR、UPM、NuGet包及HTTP缓存、dotnet用户目录、Gradle、pip/uv/npm、Python字节码和MCP日志位置。有效非系统盘自定义目录保留并创建；C盘目录、驱动器相对路径、指向C盘的junction回落到仓库本地。Windows PowerShell5.1与项目PowerShell7均有回归；非Windows CI使用仓库相对默认值。不改机器/用户全局环境，不移动已有共享缓存。

Invoke-Unity、Check-Foundation、Config/Generate、Protocol/Generate、Setup-Toolchain、Mcp/Start 均使用初始化入口。临时脚本先 dot-source 并调用 Initialize-AstralProjectEnvironment -RepositoryRoot 主工作区；不得另建C盘下载或构建目录。Unity/Git及操作系统本身仍可能写入不可由本脚本控制的少量系统日志，本约定不声称阻止所有进程的C盘写入。
