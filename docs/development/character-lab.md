# 角色实验室使用与维护

实验室独立启动，不连接游戏服务器、MySQL 或远程更新服务。Windows 测试包按配置内置角色资源。当前目录有十名角色。它是资源浏览和演出验证工具，未包含伤害、治疗、BP 或其他战斗规则。

## 打开与播放

1. 拉取工程后执行 `git lfs pull`，使用 Unity 2022.3.62f3 打开工程。
2. 选择 `Tools/角色实验室/打开场景`。首次可运行“生成场景”，生成固定 GUID 后应提交场景与 prefab；日常使用不需要重新生成。
3. 点击 Unity Play。左侧按 E7 ID 顺序选择角色，可切换战斗模型与动态立绘，选择实际动画和皮肤；右侧试听日语语音及技能。
4. 正常 1x 播放支持声音、暂停、停止和重播。调速、逐帧、定位进入静音视觉检查；有声重播从零开始。
5. 默认严格模式报告未确认条件。勾选条件映射预览后使用明确标注的实验映射；不能据此宣称与原作一致。拉斯协攻和安洁莉卡目标是实验室测试对象。

当前仅验证 Windows。Android 没有 FMOD 原生库，不应使用本轮实验室包作为 Android 可用性证据。

## 编辑源与目录

角色进入 Unity 的共同要求见 [通用资源导入规则](character-asset-import.md)，包含源曲线、骨骼继承、纹理、审计门禁及完整周期验收。旧转换产物被拒绝时重新执行导入，不能手改清单审计标志绕过。

- `Config/Tables/characterlab_characters.csv`：角色展示顺序、名称 key、battleSkin 和 portraitSkin；由 Luban 检查字段及本地化引用，构建前验证皮肤确实存在。当前十名角色的战斗皮肤为 default，立绘初始表情为 normal，不能使用只含共享附件的 default 立绘皮肤。
- `Config/Tables/characterlab_messages.csv`：中文 UI、角色名、技能名与语音标签。稳定 key 使用小写、下划线和点。
- `Config/CharacterAssets/catalog.generated.json`：导入器生成的资源目录及声音事件；不要手改。
- `Assets/Game/Modules/CharacterLab/Configs/`：导表后的客户端快照；不要手改。
- `Assets/Game/Modules/CharacterPresentation/Res/Characters/<角色编号>/`：角色专用资源；公共依赖存入 `Res/Shared/`。
- `Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json`：原始路径、源哈希、输出哈希、地址和依赖闭包。

运行 `Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient` 更新配置。导表不自动读取解包目录。增加角色时先扩展导入白名单、导入依赖，再添加 Luban 行；目录与表不一致会中止生成。

资源更新使用 `Tools/角色实验室/导入或更新角色资源`，或 `Tools/CharacterAssets/Import.ps1`。源目录默认 `D:/sevenepic/原始资源`，只读访问。导入器重复运行保留现有 `.meta`；不得整体复制原始解包目录。

转换工具 Python 依赖固定于 `Tools/CharacterAssets/requirements-lock.txt`。SCSP 转为保持原骨骼版本的 JSON；SCT 转为 PNG；STG/CFX/粒子数据保留结构；动画 WebP 展开合成帧和实际帧时长。未知字段及缺失依赖进入报告，不以改版本号实现兼容。

## 本地运行库和热更边界

- Spine 2.1.25、Spine 3.8 是两个独立的本地 UPM 包。上游提交、逐文件校验和命名空间补丁见包内 `UPSTREAM.json`，许可证保留。
- 自定义 Unity Mesh 桥接、骨骼接口、启动入口、FMOD 2.03.15 托管及 Windows 原生桥接归入 AOT 母包。变更这些部分必须重建母包。
- `AstralOath.Client.CharacterPresentation` 与 `AstralOath.Client.CharacterLab` 为 HybridCLR 业务程序集，配置在 `Config/HotUpdate/characterlab-assemblies.json`。协议兼容的业务和数据可以沿用更新体系；本实验室构建入口只交付离线自包含测试包。
- FMOD 来自用户提供的官方 `fmodstudio20315.unitypackage`。只集成需要的托管接口和 Windows x64 DLL，未导入第二套音频管理器。正常运行不需要 FMOD 编辑器或联网。
- 原始音频事件可能请求带其他语言后缀的 programmer sound；播放服务按日语音频表查找规范键。只有源目录标记确实缺少的日语片段遇到对应查找失败时播放静音，事件仍继续。其他查找失败按音频错误处理。

角色纹理第一轮使用 Windows RGBA32、关闭 mipmap 和透明边缘扩色、保留原尺寸与 sRGB。SCT 的 RGB 已预乘透明度，不能再次预乘。画质对照完成后再评估压缩；Android ASTC/ETC2 是后续独立验收项目。PNG、bank 和原生 DLL 使用 Git LFS，JSON/代码/meta 保持普通 Git。

## 构建与验证入口

菜单 `Tools/角色实验室/构建 Windows 测试包` 构建 Windows x64 IL2CPP。它使用独立的 CharacterLab YooAsset 包、入口、输出及母包身份，不继承 Foundation 的签名密钥或发布基线。构建临时设置在结束和异常时恢复，不执行部署。

命令行从项目根目录执行：

```powershell
& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Invoke-Unity.ps1 -Mode GenerateCharacterLab
& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Invoke-Unity.ps1 -Mode ValidateCharacterLab -NoSync
& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Invoke-Unity.ps1 -Mode CharacterLabWindows -NoSync -Development
```

首次在新的隔离副本构建前，运行 `Tools/Invoke-Unity.ps1 -Mode InstallHybridCLR -ProjectName UnityCharacterLab -NoSync -OfflinePath <已准备的libil2cpp目录>`；源码准备由 `Tools/Setup-HybridCLR.ps1` 按固定版本执行。

默认构建副本为 `.artifacts/UnityCharacterLab`，输出为 `.artifacts/characterlab-builds`，可执行文件为 `CharacterLab.exe`。`-NoSync` 只适用于副本已同步且没有新的源码变更时；修改代码后重新同步。不要同时让多个 Unity 进程访问同一副本。生成完成后，`Tools/Sync-UnityGenerated.ps1 -Profile CharacterLab` 可把场景、预制体、材质、收集配置与纹理导入设置同步回主工程；已有GUID冲突会中止。

使用 `Tools/Invoke-Unity.ps1 -Mode PlayMode -ProjectName UnityCharacterLab -Graphics` 验证实际图形设备，不能用无图形运行替代shader验收。Windows程序可显式加 `-astral-lab-smoke -astral-lab-capture <截图目录>` 运行启动/控件/关闭回收验收，成功退出码0、失败2。增加 `-astral-lab-quit-open` 会保留界面直接退出，必须在进程结束后检查完整日志，不能把 REQUESTED_QUIT_OPEN 当成功。截图需要可见窗口；探针只在显式 smoke 模式启用后台运行。这些模式不替代九技能原作对照。

`Tools/角色实验室/验证资源` 校验哈希、依赖、YooAsset 地址、初始皮肤和纹理导入设置，报告输出 `.artifacts/character-import/unity-validation.json`。播放、生命周期、平台运行和原作对照另行记录。详细验收状态见 `docs/plans/2026-11-12-character-lab.md`。

## 后续由用户监督的原作对照

- 拉斯 2.1 战斗模型与 3.8 立绘：网格变形、绘制顺序、atlas 旋转、透明边缘与混合。
- 三角色九技能：角色动作、日语随机语音、音效、特效触发时序、目标锚点、镜头、CUTIN、技能结束状态。
- 每项记录源版本、操作步骤、截图/录屏、差异及处理结论；未知条件、曲线或粒子字段仍按未验证项处理。
- 首批样本通过不代表全部 E7 数据兼容。国际服版本追踪仍只在用户明确要求时进行。

十角色目录的 E7 ID 为 c1001–c1009 与 c1062。技能演出列表保留被动槽位的原版触发演出供独立检查。试玩的主动技能可用性以战斗表为准。夏绿蒂 S3 当前演出选择有真实资源的 `c_charlotte_skill_03_05` 分支。席可的 2.1 动态立绘乘色槽位需要当前 AOT 母包。新增角色导入和重复生成的规则见[通用资源导入规则](character-asset-import.md)。
