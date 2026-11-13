# 当前项目状态

核对日期：2026-11-13。更新链路核对基线：`3fd85ca`，战斗场景现状另见下文。这是事实快照；修改能力、边界、验收状态或后续重点时立即更新。逐次变化见[变更日志](development/change-log.md)。

## 最新血条与死亡表现

2026-11-13：骨架top驱动信息条跟随/缩放，普通条10等分含50%高线、原dhp损血尾。死亡在末击后黑化、淡出并播放源烟雾碎片，开场准备、暂停/复活/重开均有生命周期。近期原作证据、最终结果和未确定时长见[交付记录](delivery/2026-11-13-health-death-presentation.md)。Core挂点和shader需要新母包，普通样本不代表Boss政策。本轮EditMode408通过，PlayMode44通过加截图补跑1通过；Windows Mono两场九技能/4次真实击倒通过，可操作帧P99约18.19ms，无100ms以上帧，启动/重开长帧单独记录。

## 最新战斗表现修复

2026-11-13：普通攻击/技能保留血条、等级和状态，全屏 CUTIN 使用遮幕覆盖；拉斯/梅赛德丝 S3 共用速度线修正源 PNG alpha 与预乘合成；动作回待机增加连续过渡，并在 2.1 AOT 桥接处理缺键、末轨、分帧附件与循环边界。原资源、官方核心、服务器规则保持不变。完整验收与尚未逐帧对齐的参数见[修复记录](delivery/2026-11-13-battle-presentation-fixes.md)。发布此骨骼桥接改变需要新母包。

## 最新性能核对

2026-11-13 已补战斗骨骼源复用、严格版本探测、数字字体兼容修复及独立 Windows 性能诊断入口。完整 EditMode 339 通过，PlayMode 39通过并补跑可选截图1项通过；Editor 进入 Play 的测量从约8.53秒降至5.26秒，Windows两轮九技能含最终真实夹攻，P99约18.24ms，首次操作最大约35.91ms。详细边界、完整场景回归与最终复测以[性能报告](delivery/2026-11-13-battle-performance.md)为准。不能由纯音频或 Begin 函数耗时推断战斗整体流畅；本机另有 USB 设备断连触发 Editor 枚举阻塞的已记录证据。

## 已确定选型

Unity 2022.3.62f3 / URP / UGUI / TMP；Common/AOT 通用设施；普通 C# UIBaseView + C# Binder + BindingMap；HybridCLR 冷启动业务代码更新；YooAsset；Luban 本地源表；免费 DOTween；.NET 10 模块化单体；MySQL 8.4。具体固定版本以锁文件为准，不自动升级。中文界面和日语语音独立。Windows 与 Android ARM64 为客户端目标；云主机 Windows Server 2022、2核4GB、100人以内为容量目标而非压测结论。

## 当前可用

- FoundationDemo：界面绑定、本地化、配置、资源、动画、网络回显和生命周期演示。服务端与客户端分别位于 Server 与 Assets/Game，共享契约在 Shared。
- GameText / GameButton 直接继承 TMP / Button，右键 UI 提供创建入口；63对已有控件已迁移，GUID和配置保留。
- Foundation 更新器：签名清单、哈希、断点、空间预检、原子版本切换、兼容预检、失败版本隔离及下次进程恢复。客户端可从配置的内容服务器取得兼容资源与 C# 业务 DLL。
- 构建工具：Windows x64、Android ARM64 ASTC/ETC2 的完整母包和独立热更包，母包归档与签名发布工具。
- CharacterLab：独立离线角色实验室，首批拉斯/梅赛德丝/安洁莉卡；双版本 Spine 与 FMOD 已集成。该入口只读取内置实验室内容，不等于 Foundation 远端更新入口。

- BattleScene：首阶段独立冒险布局预览。两套原图分层森林、原作 HUD、双方各四个可编辑脚点及 Spine 待机；已按近期实战和本地原版布局修订 HUD，可交互预览 Burn、燃魂 S2 和技能选中效果。见[HUD 复核](delivery/2026-11-12-battle-hud-fidelity.md)。

- 战斗第三步：共享回合与技能事件内核、本地 Unity 实战试玩和开发态双人 HTTP 房间已接入。BattleScene运行即开始本地试玩，Pause可继续/重开，燃魂仅Cancel解除。试玩包含三角色技能特效、原始 S3 Cut-in/全屏背景与镜头、Windows 日语 FMOD 和史莱姆攻击/受击、近战位移、随镜头定位的浮字、向下行动条及原版行结构详情与冷却遮罩。敌方回合技能组隐藏；已按源图接入命名起落事件、原相机曲线、脚点构图、可见地图约束与短时世界模糊；Cut-in独立时钟冻结舞台，夹攻在末击交接并保留回位尾段，骨骼与图数据在开战前准备；CFX显式片段、DARK退出与Cut-in幕布按源字段执行；浮字使用原字形并按源 HIT 分段累计。精细布局、后处理包络和部分曲线仍未完成原作逐帧验收；攻击入口已预备复用，但Editor设备扫描伴随的偶发长帧仍需环境/Player对照定位。[规则和操作](development/battle-execution.md)、[本轮实测](delivery/2026-11-13-battle-timing.md)

- Battle 配表：三名普通形态英雄、九技能、45 段强化、成长模板与觉醒节点已导出。Shared Battle 提供任意合法等级、星级与觉醒的裸面板计算，以及英雄、怪物、入场与四对四站位校验。见[等级与站位](development/battle-growth.md)和[配表来源](development/battle-character-data.md)。

## 必须知道的缺口

1. 当前 Foundation 场景远端清单 URL、签名 URL 和公钥为空；正式内容服务/CDN未部署。游戏 API 不承担发布资源的静态托管。
2. **强更完整包构建已具备；服务器决定最低应用版本/维护/安装包地址、客户端阻断旧包的闭环未实现。** 现有 Echo 只检查协议版本，更新失败允许回退健康包/内置包。不能把它说成已强制升级。
3. 最近 Common/AOT 控件继承、Shared.Updates回滚保护以及骨骼解析复用/音频暂停变更需要重建母包。2026-11-11 Windows 热更成功是旧母包的本机 HTTP 演练，不能证明当前母包或生产 HTTPS 全链路。
4. Android 历史 APK/热更包构建通过，真机安装、热更与性能仍未验。Windows/Android 本轮未重建 Player。
5. 生产 PvP 的 BP、账号准入、排位、断线裁决和 Unity 联机呈现待接入。演出按战斗结果播放。角色演出仍存在已列明的条件/镜头/粒子语义缺口，三角色样本不能代表全部原作资产。
6. 安全区和窗口尺寸变化已有基础适配；平板/PC断点布局、全设备视觉验收由具体界面负责，不能认为一个 SafeAreaLayout 覆盖全部适配。

## 最近验证与后续入口

- 2026-11-13演出修订：[通用规则](development/battle-source-presentation.md)、[本轮验收](delivery/2026-11-13-battle-presentation.md)。本轮无新增外部插件。
- 本机资料与运行缓存迁移累计释放C盘19.08GiB；主工程及新缓存均走D盘，旧缓存目录保留junction。继续使用C盘的已安装Editor不在迁移范围。[启动与路径规则](development/local-storage.md)

- 控件变更：Unity EditMode 215/215、PlayMode 16/16；见[控件验收](delivery/2026-11-12-ui-controls.md)。本次文档/工具核对没有重跑 Unity。
- 本次服务/内容/配置/协议检查结果：[地基复核](delivery/2026-11-12-foundation-reaudit.md)。MySQL真实集成需专用连接串，本次未重新验证。
- 下一项更新工作：实现无UI的启动策略契约、服务端业务准入与客户端AOT预检；再接更新界面。顺序及验收见[复核记录](delivery/2026-11-12-foundation-reaudit.md)。
- 下一项战斗工作：逐项复验首批技能的取整、抗性、反应顺序和演出，之后扩展配置、模式和生产联网。研究证据见[原作战斗资料](development/epic-seven-battle-reference.md)，演出仍按[战斗验收](delivery/battle-acceptance.md)与 CharacterPresentation 的缺口由用户监督对照。

没有持续外服追踪、自动生产部署或自动依赖升级。需要追踪国际服时等待用户明确指令。
