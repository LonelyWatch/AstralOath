# 十名角色接入

在拉斯、梅赛德丝、安洁莉卡的基础上，按原版普通英雄 ID 递增接入赛西莉亚 `c1002`、萝季 `c1003`、席可 `c1004`、绮世 `c1006`、维德瑞 `c1007`、亚敏 `c1008`、夏绿蒂 `c1009`。角色实验室列表从 `c1001` 排到 `c1009`，末尾为 `c1062`。七人名称使用原版中文索引，资源保持各自源命名，包括 `wildred/wilderd` 与 `arming/aming`。

## 操作

- `Tools/角色实验室/打开场景`，Play 后可按 ID 或中文名搜索。每个角色有战斗模型、立绘、独立动画、日语语音及三个技能源图入口。
- `Tools/战斗场景/打开场景`，在 `Battle Canvas` 的 `BattleHudPreview` Inspector 为前排、下方、上方、后排分别选择角色，然后 Play。该版本的操作由[八英雄战斗场景](2026-11-13-eight-hero-battle.md)的实例 Hero Id 配置取代。
- 扩充表和资产后，依次执行角色导入、Luban `Generate.ps1 -VerifyAccessors -SyncClient`、角色实验室收集器和 `Tools/战斗场景/更新角色资源引用`。场景生成入口读取清单和目录，不按角色英文别名猜路径。

## 共用规则和边界

- 新增 669 项清单输出（含从战斗模块移到共享目录的 bank），总计 923 个唯一地址和 30 个技能源图。原 254 项路径和哈希一致，移动的 `gerneal.bank` 保留 GUID 和 LFS 对象。图集、模型、场景资产均留在 D 盘。语音表有四个原版缺少日语录音的片段，按清单只对相应片段留白；FMOD 原有随机事件与其他音轨继续正常运行。
- 技能目标优先取权威伤害/治疗事件，单体支援以明确选择的目标为准，群体支援按同侧/对侧过滤状态、净化与行动值事件。被动槽位保留图标、资料和独立演出，在实战不可手动施放；席可、维德瑞、亚敏、夏绿蒂的 S2 自动触发尚未接入。夏绿蒂 S3 使用满斗志的 `c_charlotte_skill_03_05` 预览。专注、斗志动态门槛、挑衅强制目标、完整潜行和个别数值边界见[战斗配表](../development/battle-character-data.md)。
- 动画事件优先由同名骨骼片段提供，缺失时查同模型 timeline 的同名事件。亚敏 S2 `skill2/eff_on` 来自 1130ms 的原事件。找不到起落窗口终点时用原片段末尾，并输出可定位诊断。逐段 ANI 读取自己的 timeline 动作、特效与镜头。
- 绮世 S3 使用原 `spani` 节点骨骼移动身体显示，隐去的目标在受击节点开始时恢复。原 `flying_damage` 轨期间避免普通受击覆盖。节点复位只影响它拥有的显示变换；场景站位与权威计算保持独立。屏幕中心加源偏移是当前的屏幕锚点解释，源色彩叠加、随机模糊采样周期、空源 SHOT 和部分 CFX 字段仍待原作画面验证。新增场景兼容规则详见[源审计](2026-11-13-ten-hero-source-audit.md)。
- 旧母包需重建以包含 Spine 2.1 乘色和 FMOD 源缺录音兼容。战斗目录与新版战斗程序集同批发布，旧解析器无法载入被动槽。在线服务发布须同时更新共享规则及配表。当前验收使用本地 Unity、服务端测试与 Windows 原生音频，原版逐帧一致性独立核对。

## 检查记录

- 导入器 `--verify` 与 Python 47 项测试通过。HUD 工具 58/58 引用可复现。Unity 角色实验室验证 923 项哈希、导入参数、地址与依赖通过。
- Luban JSON、二进制、C# 读回、引用和十人满级面板一致；服务端 76 通过、1 项 MySQL 环境跳过、0 失败，覆盖 17 个新增主动槽。
- Windows 原生 FMOD 探针通过 30 对技能事件与语音事件、73 次回调采样与 DSP 非零输出，并验证缺少日语录音时服务仍健康。日志 `.artifacts/ten-hero-audioprobe-final.log`。
- Unity EditMode `ten-hero-EditMode-verified.xml` 共 439 项通过。PlayMode `ten-hero-PlayMode-verified.xml` 共 6 项通过，覆盖七名新英雄各自实际入队、S1 提交、21 条新源图、旧九技能两向、角色实验室十人切换和 Cut-in/信息条回归。最终支援与音频调整后，`ten-hero-PlayMode-support-audio.xml` 共 8 项通过，两轮有交集。
- 绮世 S3 双 Cut-in 红测 `ten-hero-EditMode-cutin-red.xml` 复现亚微秒残余。统一结束时间与幕布释放边界，并按墙钟推进 Cut-in，最终通过 Kise 的四段命中、可见性与复位检查。截图位于 `.artifacts/ten-hero-frames/source-playback/`，其中作者 HUD 只用于源图播放夹具，实际信息条另有回归。
- Windows IL2CPP 独立构建曾遇 Unity Bee 崩溃和 Windows 页面文件不足。已隔离该镜像的旧 Bee 缓存，使用进程级 `BEE_BUILD_THREADS=2`、`DOTNET_PROCESSOR_COUNT=4` 重试，后续构建越过内存故障。发现镜像原有的 CreateSolution 设置会生成 Visual Studio 工程，完整包入口现临时关闭该设置并在结束时恢复。

- Windows IL2CPP 构建退出 0，生成母包 `5a042890a1bd45ac9cb3ce4894b92529` 与 `baseline.complete.json`、`package.complete.json`。运行包在 `.artifacts/ten-hero-lab-windows/Win64/player-v1-7a55b036c3ed463e861b1fdcf6dad562/CharacterLab.exe`。`-astral-lab-smoke` 退出 0，覆盖十人、30 个技能入口、语音、模型、立绘、暂停、停止和关闭释放。运行日志 `.artifacts/ten-hero-player-smoke.log`。隐藏进程的可选整屏截图验收返回黑图，播放器复跑使用真实网格和交互断言，图像证据取自带图形的 Unity PlayMode 回归。
