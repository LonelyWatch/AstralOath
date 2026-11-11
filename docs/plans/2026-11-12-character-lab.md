# 离线 Spine 与角色实验室执行台账

用户批准在 `main` 实施并推送。实际工程为 `D:/UnityProjects/AstralOath`，隔离验证副本为 `.artifacts/UnityCharacterLab`。原始 `D:/sevenepic` 全程只读。当前架构为 C# Binder + HybridCLR，不恢复 Lua。

## 已交付的实现

- 官方 Spine 2.1.25 固定提交 `142e770e5b4b42395a3a0a68ff3c8a21ba2774eb` 与 Spine 3.8 固定提交 `8b4844bd4b193ba9e54487ed397a777993cbad56`，隔离为两个本地 UPM 包；保留原始 2.1.27 / 3.8.99 骨骼版本、许可证、来源及校验记录。
- Unity 2022.3.62f3 / URP Mesh 桥接及统一播放器接口；支持动画、皮肤、事件、暂停、定位、翻转、混合、网格变形与释放。3.8 裁剪、双色着色和材质顺序有针对性回归；未知 atlas 旋转明确拒绝，不宣称覆盖所有 Spine 数据。
- 用户提供的 `fmodstudio20315.unitypackage` 已集成必要的托管接口及 Windows x64 原生库。银行、事件与 programmer sound 生命周期由项目服务管理；中文界面和日语语音分别选择。正常运行不需要 FMOD/Spine 编辑器，也不联网下载运行库。
- 首批拉斯 c1001、梅赛德丝 c1005、安洁莉卡 c1062：174 个来源转换为 254 个产物，含 44 份骨骼、九个 STG 技能图和五个共享声音 bank。每角色资源独立，公共依赖共享；80 个 PNG/bank/DLL 二进制文件使用 Git LFS。
- 可运行 CharacterLab 场景及 `UIBaseView + CharacterLabWindowBinder + BindingMap`；搜索、骨骼/立绘、动画、皮肤、日语语音、技能入口、播放控制及诊断栏。Luban 管理中文文案、角色顺序和初始皮肤；战斗为 `default`，立绘为 `normal`，构建前检查名称在真实骨骼中存在。
- `Tools/角色实验室` 提供场景、环境、导入、校验、Windows 构建和报告入口。独立 YooAsset 包及离线启动，不需要游戏服务器或 MySQL。短哈希包名规避 Windows 路径长度限制，构建后恢复临时全局设置。
- 双核心、渲染桥接、FMOD 和稳定契约属于 AOT 母包；CharacterLab / CharacterPresentation 属于 HybridCLR 业务层。本轮入口生成独立离线测试包，不发布线上热更或部署服务。

## 实际验证

- 转换器 19 项回归通过；254 个输出哈希、地址及依赖闭合；重复导入保持产物一致及既有 GUID。第三方逐文件来源校验通过。
- 44 份骨骼使用对应官方核心遍历全部动画和皮肤，实际计算 7,973 帧、6,203,651 个世界顶点；无皮肤镜头骨骼也实际采样。此项证明可读取与求值，不等于原作视觉一致。
- C# FMOD 原生检查两轮共 38 次播放均有非零 DSP，36 个事件销毁、48 对观测 programmer sound 创建/销毁配对；初始化失败与回调失败回收通过。该音频数值检查使用 NOSOUND 软件混音，不冒充人耳试听。
- Unity EditMode **151/151**、使用实际 D3D11 图形设备的 PlayMode **13/13** 通过。包含真实场景/mesh/音频 DSP、连续切换、关闭卸载、外部销毁顺序、音频故障降级、中文字体保存、实际脸部皮肤、预览尺寸及构建设置恢复。
- 现有 `Tools/Check-Foundation.ps1` 再次通过：模块边界、服务端、配置、协议、内容恢复及签名发布回归。未提供本轮 MySQL 测试连接，数据库验收不计入此次通过项。
- Windows IL2CPP 已成功构建。先前测试包已实际跑通三角色、九技能事件入口、语音、暂停、停止、关闭及出站阻断；最新 4:3 修正版的最终 Windows 截图复验结果见下方记录。

### 最终 Windows 记录

最终包：`.artifacts/characterlab-builds/Win64/player-v1-ad2b0e13047c4ec28e5abf3a48c0a197/CharacterLab.exe`。

- 两次原生进程实际 exit 0：普通窗口关闭与保持界面打开直接退出。均在仅针对该 exe 的临时出站阻断下运行，结束后删除临时规则；不修改其他应用联网权限。
- 截图实际尺寸为 1440×900 和 1200×900；中文、战斗模型及 normal 立绘表情可见，4:3 预览与界面同比缩放。较早修正版另已验证 1600×900。
- 最新日志：`.artifacts/character-player-delivery-169-visible/player.log` 与 `.artifacts/character-player-delivery-43-visible/player.log`；同目录有 battle.png / portrait.png。前者包含 SMOKE_PASSED；后者先记录 REQUESTED_QUIT_OPEN，再由外部脚本检查进程退出码和完整退出日志，无 TMP 空引用或缺字警告。退出前标记本身不代表验收通过。
- 截图测试使用可见窗口；隐藏窗口试验产生全黑截图，已按失败拒绝，未计通过。
- 已检查 1,683 个 meta 无 GUID 重复；80 个二进制为 LFS 指针；对暂存区及独立 Git/LFS 检出复核 330 条运行库/资源哈希全部一致。按字节校验的生成资源与未改动 FMOD 原文件禁止 Git 自动转换换行。

用户随后报告拉斯 idle 剑脱手、梅赛德丝 S3 错位和 0 秒条目。本次接入不视为这些表现已经验收，后续专门修复。初查 10 条 0 秒记录确为源文件单帧姿势；播放跳转和切换残留已有数值复现，修复另行提交。

## 已定位并修复的问题

1. SCSP 事件/排序与 3.8 曲线恢复遵循实际数据，未知格式和缺失 atlas 页拒绝猜测；原始字段保留用于后续对照。
2. FMOD 原事件随机请求其他语种后缀时，明确映射并验证日语音频表；服务在初始化或回调失败后完整回收，避免事件和银行滞留。
3. 预览相机只输出 RenderTexture 导致 Windows 画面全黑：增加独立屏幕输出相机，验收截图拒绝纯黑或单色。
4. 字体保存成 LiberationSans、材质却属于中文字体：生成器持久化匹配的字体和材质，原生验收把缺字警告计为失败。
5. 立绘默认皮肤只有共享附件而缺脸：初始皮肤改为显式 Luban 配置，并验证三份真实立绘的脸部网格。
6. 直接销毁时 Binder 尝试重绘已被 Unity 销毁的对象：释放路径只解除订阅和回收资源，异常不阻断其余清理；外部销毁 PlayMode 回归通过。另发现 TMP 子网格在程序退出时访问已销毁父文本；现在 OnApplicationQuit 提前隐藏界面并幂等回收，新增场景回归与原生开窗退出日志均通过。
7. 4:3 下界面缩放而全屏预览未同步，武器进入标题栏：预览与界面统一 1920×1080 设计尺寸，RT 按实际显示像素分配，比例和释放行为有五项回归。

早期失败日志保存在本地 `.artifacts`，不纳入源代码。原 Python ctypes 音频探针曾在退出时出错，未计为通过；后续真实 C# 服务与原生库干净退出，不反向宣称 Python 异常根因全部确定。

## 未验证及需要用户监督的内容

- **原作一致性尚未验收。** 三角色九技能的完整动作、镜头、目标锚点、随机语音时机、CUTIN、特效和结束状态仍须逐段对照。自动入口测试每技能观察 3.5 秒，不能证明整段技能或所有条件分支已还原。
- 默认严格模式报告未确认的 STG `when/location`；“条件映射预览”是明示的实验映射。CAM/MOVE/ZOOM 的部分曲线、SHAKE 振幅参数、DARK/STOP 深度与重叠、部分粒子字段未确认。
- LAYOUT、SINE_VALUE 驱动、CFX Fire 及部分数值范围抽样语义尚未重现，会诊断并跳过相关原语，不伪装完整效果。sprite_blur 已移植源采样公式，但实验室对整幅预览图处理，与原作 sprite 作用范围仍待对照。
- SCT2 仅合成用例验证，当前真实来源主要是 SCT1；首批样本不代表全部 E7 资源兼容。
- Windows 自动程序及 PlayMode 可以检测异常和 DSP，但日语内容、人耳听感及原作时机仍由用户监督确认。Android 不在本轮验收范围，也未添加 Android FMOD 原生库。
- 实验室不计算伤害、治疗、奖励或 BP；协攻同伴与治疗目标为明确标注的测试对象。无线上部署、后台版本追踪或自动升级。

## 维护与验收入口

操作及配表说明见 `docs/development/character-lab.md`；转换与音频详细证据见 `docs/delivery/character-lab-audio-and-conversion.md`；架构边界见 `docs/architecture/adr/008-character-lab.md`。

本地 Unity 报告：`.artifacts/unity/EditMode.xml`、`PlayMode.xml`、`CharacterLabWindows.log`；资源报告：`.artifacts/character-import/unity-validation.json`。这些检查和构建产物不提交到 Git。后续原作对照应逐项记录源版本、操作、截图/录屏、差异及结论。
