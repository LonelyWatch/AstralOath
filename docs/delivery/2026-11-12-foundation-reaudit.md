# 地基复核与后续接手（2026-11-12）

运行时代码基线：3fd85ca。核对主工程 D:/UnityProjects/AstralOath，本轮重点为能力审计、规则与文档流程；不制作更新界面、不部署生产。

## 结论

Foundation 的签名内容下载、HybridCLR 冷启动加载/恢复以及各平台完整母包/热更包构建已实现，设计允许配合外部内容服务器发布更新。**服务器控制最低版本并阻止旧客户端进入的强更闭环尚未实现，不能写成全部完成。**

CharacterLab 是独立离线测试入口，不是现有 Foundation 远端更新演示。后续正式系统应接入统一启动链路，不能把实验室的离线启动当成网络发布验收。

## 当前代码对应的能力

- FoundationBootstrap → StartupContent → ContentUpdater：从场景配置的清单URL/签名URL/公钥下载，检查签名、文件哈希、磁盘、断点和候选内容，再切换版本。
- HotUpdateCodeLoader / HotUpdateManifest：检查母包、平台、架构、DLL、依赖关系，程序集只在当前进程加载一次；失败版本隔离及恢复需要下次启动。
- HotUpdateBuilder：完整安装包构建和实际AOT归档；兼容热更基于该归档构建并签名，不能跨母包混用。
- Server/Host/Program.cs：仅注册健康检查和Foundation Echo；EchoEndpoints仅校验协议版本，不检查客户端应用版本、baseline或最低内容版本。
- FoundationDemo.unity：远端清单URL、签名URL及公钥当前为空；需发布者配置。游戏API没有静态资源发布端点，内容和安装包由独立HTTPS文件服务/CDN承载。
- StartupContent更新失败允许健康包/内置包启动；该恢复行为不能用于要求停服或强制升级时的准入判断。
- CharacterLabBootstrap直接加载内置YooAsset及业务DLL，没有走Foundation远端更新与健康版本日志。

## 本次重新执行

命令：`& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Check-Foundation.ps1`，退出0。本地记录 `.artifacts/foundation-reaudit.log`。

- 服务端测试10通过、0失败、1跳过。跳过项为真实MySQL并发/重启幂等集成，因为本次未提供ASTRALOATH_MYSQL_TEST；不计为数据库验证通过。
- 更新行为测试通过：本机Kestrel HTTP、真实临时文件、签名/哈希/路径/断点/空间/取消/候选拒绝/同版本修复/兼容与启动恢复检查。
- Luban生成、访问器读回、客户端快照检查及非法配置回归通过。日志中故意注入的非法字段报错属于预期拒绝，不能单看ERROR字样判断总检查失败。
- 协议生成与签名发布CLI演练通过；YooAsset打包封装使用报告fixture，不等于本轮重新构建Unity资源包。
- 新文档同步检查器18例与原模块检查器6例通过（共24例）；已接入本地总检查的测试步骤及CI变更门禁。覆盖多提交推送、新分支共同祖先、根工具链配置及删除/重命名；提交时另运行实际暂存检查。
- 新模块模板在隔离目录实际生成和检查通过：热更程序集autoReferenced=false、TMP/UGUI引用、中文职责/交接模板与未验证状态均符合预期。模板并不代表新业务已实现。

最近Unity结果沿用前一任务的已验事实：EditMode215/215、PlayMode16/16（控件提交3fd85ca）。本轮未修改运行时代码，没有重跑Unity/Player/设备。

## 历史证据的适用范围

2026-11-11 Windows旧母包19353c6af6eb4034b9b8beece8716107保持同一EXE加载版本2业务DLL、拒绝坏签名并恢复2、入口失败后下次冷启动隔离3并恢复2已有日志。历史分发服务使用node:http与127.0.0.1:18084，只证明loopback HTTP演练，不能冒充生产HTTPS端到端验证。

原始日志当前位于本机历史工作区 `C:/Users/Admin/.codex/worktrees/astraloath-foundation/AstralOath/.artifacts/hybridclr/`，文件包括player-hot-v2.log、player-bad-signature.log、player-cold-recovery.log和serve-release.mjs。此路径是证据位置，不是继续开发目录。历史摘要见[HybridCLR验证](../development/hybridclr-validation.md)。

Android ASTC/ETC2历史完整包与热更包构建通过，但未见真机安装/冷启动热更验收。Common/GameText/GameButton最新AOT继承变更要求重新构建母包；不能将旧母包测试外推到当前源码。

## 更新界面之前应补的下一步（未实施）

1. 定义无UI启动策略：平台/渠道、协议、应用构建号、母包、内容/配置兼容条件、维护状态与安装包HTTPS地址。应用构建号应可可靠排序，不能直接按字符串比较版本大小。服务端和客户端共享状态契约，保留版本演进方式。
2. 服务端提供策略并在正式业务准入处再次执行兼容判断；客户端AOT在加载业务前请求策略，输出Ready、HotUpdateRequired、ForceUpdateRequired、Maintenance或可诊断的连接失败。策略请求超时/离线是否允许进入需明确区分实验室与正式联网玩法，不能静默回退绕过强更或维护。
3. 更新界面只消费状态与进度，显示重试/下载/退出；不承担版本规则。Android安装需系统流程或商店；Windows替换运行文件需要退出后安装/启动器机制，不能在运行时覆盖自身。
4. 基于当前AOT重建母包，配置可信公钥与HTTPS发布地址；上传不可变内容后切换发布入口，维护期按协议/数据库兼容顺序发布服务端。数据库迁移及恢复与客户端资源回退独立。
5. 实测允许旧包、兼容热更、强更阻断、维护阻断、策略超时、签名错误、错误母包、断网/空间不足、新版本安装和服务端准入。Windows与Android分别记录，Android必须真机。

以上是已识别的接入缺口与后续验收顺序，不是已存在的接口或实现。自动化CI成功只能证明其实际执行的源码/服务/内容检查，不能替代上述发布验收。

## 新会话维护方式

从[接手入口](../README.md)读取[当前状态](../project-status.md)和[通用规范](../development/project-standards.md)。模块README维护实际职责和接口；变更日志维护最近变化、验证与后续事项；状态变化同步project-status，重要架构选择写ADR。功能改动与文档同批提交，提交前和CI分别执行文档门禁，语义仍需人工/Agent审查。
