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

2026-11-22 在原生 Windows 进程与 Codex 桌面应用中发现两套 LocalLow 视图。在本机从 Codex 读取普通 C 盘路径，看到的是应用容器的重定向目录。核实磁盘占用时还要检查本机管理共享 `\\localhost\C$\Users\Admin\AppData\LocalLow`，检查目录的 ReparsePoint、实际联接目标与文件数，统计物理占用时跳过联接。

原生 Windows 的整个 `LocalLow/AstralOath` 已改为指向 `D:/AstralOathData/windows-native/LocalLow/AstralOath` 的 NTFS junction。旧目录包含四个产品目录，合计 1,344 个文件、30,020,594,063 字节。复制后核对逐文件 SHA-256，切换后通过旧路径再次核对，删除源副本前复核其内容。操作时 C 盘可用空间由 6,369,255,424 增至 36,377,477,120 字节。原版本的配置、缓存和存档按目录保留，新产品名继承父目录的 D 盘位置。已通过旧路径新建唯一测试文件，从 D 盘读取并核对哈希，再从 D 盘删除并验证旧路径同步消失。清单和回执在 `.artifacts/disk-migration/native-runtime-sha256.json`、`native-runtime-sha256.receipt.json` 及 `native-write-through.json`。

使用项目PowerShell执行 Tools/Open-Unity.ps1（工作目录 D:/UnityProjects/AstralOath）。此入口检查同工程已运行时仅报告，不开第二实例。当前Editor不被追溯修改环境；保存并正常退出后，下一次从新入口启动才继承缓存位置。不要用结束进程代替保存退出。

Tools/ProjectEnvironment.ps1 统一设置进程范围 TEMP/TMP/TMPDIR、UPM、NuGet包及HTTP缓存、dotnet用户目录、Gradle、pip/uv/npm、Python字节码和MCP日志位置。有效非系统盘自定义目录保留并创建；C盘目录、驱动器相对路径、指向C盘的junction回落到仓库本地。Windows PowerShell5.1与项目PowerShell7均有回归；非Windows CI使用仓库相对默认值。不改机器/用户全局环境，不移动已有共享缓存。

Invoke-Unity、Check-Foundation、Config/Generate、Protocol/Generate、Setup-Toolchain、Mcp/Start 均使用初始化入口。临时脚本先 dot-source 并调用 Initialize-AstralProjectEnvironment -RepositoryRoot 主工作区；不得另建C盘下载或构建目录。Unity/Git及操作系统本身仍可能写入不可由本脚本控制的少量系统日志，本约定不声称阻止所有进程的C盘写入。

## 2026-11-22 参考资料、QA 和旧工作树

本次在 `D:/AstralOathData/project-files/` 按用途保存了 3,223 个文件，共 1,224,912,714 逻辑字节（1.14 GiB）。各文件按原物理来源计数一次。既有 D 盘联接指向的数据、旧工作树分项切换后的再次核验、原生 Windows 产品缓存都另列。与同日原生产品缓存的 30,020,594,063 字节合计，本轮迁移量为 31,245,506,777 逻辑字节。

- `recordings/` 收录 Downloads 中 11 月 14、17、18、21 日的 9 段原版录屏，共 1,026,404,292 字节。原 MP4 路径仍可用于取帧、播放和已有文档。
- `screenshots/` 收录 115 张可确认属于本项目的 Codex 临时截图，共 118,836,370 字节。33 张由本次反馈给出文件名，16 张逐张确认了 AstralOath 窗口标题，其余 66 张依据具体游戏界面或原版效果辨认。两种视图均查到同一组 116 个 PNG，另外 1 张网页截图留在原处。
- `qa/` 保存项目命名的特效对照图、字体与名册截图、探针 DLL 和一次 fidelity 探针目录，共 32 个文件、11,425,850 字节。`visual-studio/` 保存 AstralOath 专属的 4 个 Roslyn 缓存文件、5,062,160 字节和项目备份目录。
- 两个原生 `DefaultCompany` 的项目探针目录和 Codex MSIX 的 `LocalCache/LocalLow/AstralOath` 父目录已改为 D 盘联接。切换时两个原生探针的文件数均为零。MSIX 父目录内已有的 10 个 D 盘产品联接保留原目标，后续创建的新产品目录也落在 D 盘。
- 历史工作树落在 `worktrees/astraloath-foundation/AstralOath`，3,063 个文件、63,184,042 字节。`C:/Users/Admin/.codex/worktrees/astraloath-foundation/AstralOath` 保持原 Git 注册路径。原目录作为活动工作目录，普通重命名遭遇占用。先在 D 盘模拟同类占用，验证占用进程能通过相对路径读写、原地取消联接能恢复原目录，再把真实空根原地设为联接。根目录文件 ID 1688849860615809 前后一致。原访问路径的完整目录及文件哈希、`.git` 注册、HEAD、跟踪和未跟踪状态均已核对。原来的 `.artifacts`、`.tools` 仍各自指向既有 D 盘目标。

原路径以目录联接或单文件符号链接保留。核验原件、新件及旧路径访问后才清除暂存副本。验收索引在 `.artifacts/disk-migration/`。附加迁移总览为 `project-files-verification.json`，逐项来源、目标、字节及单文件 SHA-256 在 `project-files-receipts.jsonl`，目录逐文件清单为 `project-files-*.json`，工作树原地联接试验为 `junction-probe/result.json`。原生 Windows 产品缓存使用独立收据 `native-runtime-sha256.receipt.json` 及逐文件清单 `native-runtime-sha256.json`。
