# 角色实验室：转换、骨骼与原生音频验证

日期：2026-11-12。执行目录：`D:/UnityProjects/AstralOath`。本文只记录已执行的转换与 .NET 原生检查；Unity 场景和人耳听感另行验收。外部 `D:/sevenepic/原始资源` 全程只读，没有复制整个解包目录或后台版本追踪。

## 可复现命令

工程使用局部工具链。下面命令均从工程根目录执行：

```powershell
& ./.tools/character-assets/Scripts/python.exe -B -m unittest discover -s Tests/CharacterAssets -v
& ./.tools/character-assets/Scripts/python.exe -B Tools/CharacterAssets/import_characters.py --closure-only
& ./.tools/character-assets/Scripts/python.exe -B Tools/CharacterAssets/import_characters.py
& ./.tools/character-assets/Scripts/python.exe -B Tools/CharacterAssets/import_characters.py --verify

& ./.tools/dotnet/dotnet.exe run --project 'Assets/Game/Tests/EditMode/SkeletonRuntimeSmoke~/SkeletonRuntimeSmoke.csproj' -- --manifest 'Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json' 'D:/UnityProjects/AstralOath'

& ./.tools/dotnet/dotnet.exe run --project Tests/AudioSmoke/AudioSmoke.csproj -- 'D:/UnityProjects/AstralOath/Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudio.dll' 'D:/sevenepic/原始资源/sound'
& ./.tools/dotnet/dotnet.exe run --project Tests/AudioSmoke/AudioSmoke.csproj -- 'D:/UnityProjects/AstralOath/Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudio.dll' 'D:/sevenepic/原始资源/sound' --guard-only
& ./.tools/dotnet/dotnet.exe run --project Tests/AudioSmoke/AudioSmoke.csproj -- 'D:/UnityProjects/AstralOath/Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudio.dll' 'D:/sevenepic/原始资源/sound' --failure-only
```

`AudioSmoke` 编译真实生产服务、BindingScope 和 FMOD C# wrapper；测试仅适配 Unity TextAsset、资产接口和 AOT 特性。必须传入明确的原生 DLL 绝对路径，实际版本检查为 `0x00020315`。未以模拟 FMOD 调用代替原生验证。

本地日志：`.artifacts/character-import/runtime44.log`、`audio-smoke.log`、`audio-failure-smoke.log`。这些是可重跑产物，不作为源代码提交。清单为 `Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json`，正式目录由 Luban 接入。

## 转换与闭包：实际通过

白名单为 c1001 / ras、c1005 / mercedes、c1062 / angelica。174 个源文件转换成 254 个产物，约 285 MB；公共依赖只保留一份，五个 FMOD bank 始终公共。产物包括 44 标准骨骼 JSON 与 44 descriptor、40 atlas（含显式文件别名）、37 源纹理解码、九 STG graph、21 CFX、14 particle、两个原有 timeline，以及两个 cutin 的 36 张合成帧。

Ras 的源目录没有 timeline，清单记录 info，不制造占位文件。STG 保留 entities、connections、entrypoint、eventlisteners 与原始 when/location 字段；graph.assets 加入角色 timeline 触发特效闭包。CFX/particle 保留 plist 结构。九个 skill id 保留原 STG 名。两个 WebP 分别为 16 帧/1.25 秒、20 帧/1.28 秒，帧时长和合成行为由原 WebP 解码器保留。

19 项 Python 回归实际通过，覆盖外层长度、未知格式拒绝、RGB565/alpha、原事件/曲线保留、draw-order、白名单、路径逃逸、显式 atlas 别名、缺失页拒绝猜测、共享输出。完整源文件和输出 SHA256、254 地址依赖均验证通过；37 个实际渲染 skeleton 页的 atlas 尺寸与 PNG 一致。重复完整导入得到相同 manifest SHA256：

`05871b99c9864dae311716b2dcab89876d1b36723d83ade34af523b99026c3d5`

2.1 的 type5/type6 按真实事件和 draw-order 顺序读取，禁止参考查看器的动画扫描、吞错、版本升级、强制 stepped、旋转修补和补关键帧行为。3.8 的九个缓存采样点恢复三次贝塞尔控制点，逐点残差不超过 0.00002，再输出该固定官方运行时接受的 `curve/c2/c3/c4` 字段。每份二进制消费字节数必须与声明相等。

六张主纹理实测符合预乘 alpha 特征，descriptor 使用 `premultipliedAlpha:true` 并保留源 RGB/alpha。SCT1 format4 和七张灰度 format2 为本次真实样本；SCT2 仅合成用例验证，未宣称真实 SCT2 资产验证通过。源引擎辅助字段与缓存位置保存在 conversion-reports 中，不能把它们当作已还原的玩法语义。

## 44 骨骼官方运行时：实际通过

`SkeletonRuntimeSmoke~` 通过 manifest 加载实际标准 JSON 与已展开别名 atlas。43 份保留 2.1.27，1 份保留 3.8.99。每份都遍历动画与全部 skin，在 30 FPS 时间点应用状态、更新世界变换，并检查世界顶点和骨骼矩阵无 NaN/Infinity。没有 skin 的 camera 也执行一次动画循环，不用零帧解析结果充当采样通过。

最终进程 exit 0，44/44 通过。累计统计：

- 101 个源动画、63 个 skin。
- 4,949 条 FFD/deform 时间线、60 条 draw-order 时间线。
- 7,973 个实际采样帧；每份至少 7 帧，最多 2,736 帧。
- 210,600 次非空 deform slot 观察、1,884 个非默认排序帧、27 次动画事件回调。
- 6,203,651 个世界顶点检查；骨骼矩阵另行检查，不混入顶点计数。

这证明固定官方运行时可完整读取并计算当前闭包，不证明屏幕效果、目标选择、伤害、特效语义或原作玩法已经复原。

## 真实 FMOD：两轮 38 次播放通过

只读取 master.bank、master.strings.bank、sfx1.bank、voc.event.bank 与 ja/voc1.audio_ja.bank。每轮播放九技能 SFX、九技能语音和一条独立音频表语音，共两轮 38 次。任何事件开始前必须成功切换为原生 `NOSOUND` 输出；软件混音仍真实工作，不调整系统音量、不向扬声器输出。

两轮完整创建、加载、暂停/恢复、停止和销毁后进程正常 exit 0。36 个技能事件均测得非零软件 DSP 峰值；两次 `voc_c1001_answer_1` 独立 getSoundInfo / createSound / playSound 峰值均为约 0.579784。每个事件采样窗口为 3.5 秒，最初的 650 毫秒窗口确实漏过原 SFX 延迟，因此没有把早期零值当成音频不存在。

实际观察 48 次 ProgrammerSound CREATE 与 48 次对应 DESTROY，按真实 sound handle 完整配对；36 个事件实例收到 DESTROYED。另有 8 个安装观察器前已创建、之后才销毁的声音单列统计，不算成缺失 CREATE。最后所有已观测声音句柄、callback owner 与 bank 资产 scope 均清空，重复 Stop/Dispose 安全。

源语音事件会随机请求裸 key 与 `(zhs)` 后缀 key，例如 `voc_c1001_shout_1(zhs)`；原事件没有可配置语言参数。仅加载日语表时后者确实导致回调失败。修复在探查原 key 后，针对明确已知 locale 后缀始终查证日语 canonical bare key；即使另一个表恰好存在中文版原 key，也不能因此错播。未知后缀不任意截断，找不到 canonical key仍明确失败。测试实际覆盖裸 key 与中文版后缀随机分支，不把成功回调当作人耳听感对照。

## 生命周期修复与错误路径

以下问题均先运行失败回归，再实现与复测：

1. 已 Dispose 服务再次 Initialize 原先会创建新 native 系统，再被旧取消 token 中断，而 Dispose 因幂等标记无法清理新系统。现在所有 native 调用前检查 disposed 与 initializationStarted，同一实例只允许一次初始化。
2. ProgrammerSound key 失败原先仅把 IsAvailable 设为 false。后续 Tick 不再泵送 SYNCHRONOUS_UPDATE，已有声音与排队 Stop 可能滞留。现在在主线程 Tick 的 native 回调返回后完整 Dispose，并保留 Diagnostic，不在 native callback 栈中重入释放。

`--failure-only` 实际启动 SFX 和独立语音后注入 callbackError，确认两项 playback、Studio 系统、五个 bank scope 均释放，错误说明保留，重复停止安全；又验证第一个 bank 成功、第二个 bank 文件缺失的部分初始化失败会释放 native 系统及已加载资产。该错误路径进程 exit 0。

只读检查确认角色选择先停止音频与演出，旧 selectionScope 取消后按逆序先销毁骨骼、再释放其借用纹理。YooAsset 请求继承 scope token 并有 20 秒期限；旧选择异步完成不会覆盖新 player。速度变化、seek 与逐帧操作停止音频，复位才重新允许 1x 声音。银行由场景服务持有，窗口关闭负责停止 playback；场景卸载最终释放银行。没有为这些检查扩展产品专用测试接口。

## 未执行或不能由本报告替代

Python ctypes 早期探针虽能读取 bank/事件，退出时发生 `PyInterpreterState_Delete ... remaining subinterpreters`，该探针整体不能标为通过。后续 .NET C# 原生进程正常结束，两轮释放和错误恢复均通过，未复现 native 退出错误；这不能反向断言 Python 解释器异常的所有根因已确认。

尚未进行与原作逐段的人耳听感、语种和时机对照，也未以真实扬声器播放验证替代 `NOSOUND` 数据验证。Unity PlayMode 真实场景集成测试已新增，覆盖60秒启动上限、BindingMap、真实mesh、现有UI语音点击与DSP、连续角色切换、立绘切换、关闭和卸载；由协调者统一运行，其实际结果另记。本文不提前宣称Unity整体验收通过。
