# 角色资源进入 Unity 的通用规则

本规范适用于后续所有角色，不以首批三角色通过代表全部 E7 资源兼容。当前入口为 `Tools/CharacterAssets/Import.ps1` 和 `Tools/角色实验室/导入或更新角色资源`；原始目录只读。排查案例见 [梅赛德丝 S3 曲线修复](../delivery/2026-11-12-mercedes-curve-correctness.md)。

## 1. 来源与模块目录

- 明确批准角色编号后，扩展导入白名单和 Luban 角色表；只递归导入模型、立绘、技能图实际引用的依赖。缺文件报错，不根据相似文件名替代，不复制整个解包目录。
- 独占资源位于 `CharacterPresentation/Res/Characters/<id>/{Animations,Atlas,Textures,Sounds,Effects,Configs,Prefab,Materials}`，按实际类型建目录；共享依赖只保留在 `Res/Shared`。不重复拷贝 bank。已有依赖后来由多个角色引用时继续通过原稳定地址共用，保留原路径和 meta。对新共享依赖使用 Shared。
- 清单记录原始相对路径、源 SHA256、输出 SHA256、稳定地址、依赖、转换器版本及语义审计。重复导入保持文件路径及已有 `.meta`；不删除重建 GUID。
- 原始游戏可执行文件仅作本机只读格式分析，不执行、不修改、不提交；不读取账号配置。固定依赖和参考源码提交，不自动追踪国际服。

## 2. 转换保留原始语义

- 保留骨骼版本：2.1.27 使用隔离的官方 2.1.25 核心，3.8.99 使用固定 3.8 核心。禁止改 JSON 版本号、全局重采样、强制插值、随意补关键帧来制造兼容。
- SCSP 2.1 曲线块：`0xFFFF` 为全线性，`0xFFFE` 为全阶梯；显式块保留 linear / stepped / Bezier。Bezier 第一个 float 是目标帧索引，随后四个 float 才是控制点。禁止把两个哨兵合并，或把所有曲线一律改成 stepped。
- 原始帧时间、层级、父骨骼索引、继承标志、局部翻转、附件身份、网格权重、FFD、绘制顺序、事件负载均保留。2.1 type9/type10 为 FlipX/FlipY，不是排序和事件。曲线作用于骨骼、颜色、IK 和 FFD，不能只修主模型平移。
- 不以循环时的角度大于 360°判定源文件错误；不把官方运行库的历史轨道状态静默改成每圈 setup。单次、循环、Seek、混合和技能调度分别验收。
- 零时长动画允许作为静态姿势，不伪造时长。动画列表与 STG 技能不是同一测试入口；STG 未确认的条件、镜头、锚点必须保留诊断。
- SCT 默认保留源 RGBA 与预乘信息；骨骼 atlas 页名、旋转、裁边来自原始数据。Unity 首轮使用原尺寸 RGBA32、关闭 mipmap 与 alpha 扩边。原始 PMA 不二次预乘；验证完画面后再评估平台压缩。
- 放大的发光贴图出现色阶或绿边时，先核对源像素、采样、PMA、混合和实际播放倍率。仅 RGB565+A8 中已确认的中性白 PMA 区域登记到 `Tools/CharacterAssets/white-pma-regions.json`。验证量化关系后以 A 恢复 RGB，保留原 alpha、彩色区块及图集外像素。新条目需记录证据、源矩形、清单和 Unity 实帧。见[赛西莉亚 S3 案例](../delivery/2026-11-13-battle-visual-refinement.md)。
- 图像 atlas、骨骼单位比例和预览镜头各自有所有者；禁止添加某角色、某技能或某时间点的固定偏移来补偿转换错误。
- 实机出现立绘但 STG 没有 CUTIN 时，核对相同技能的皮肤图、基础骨骼动画事件及正确皮肤的 WebP。证据一致时在导入白名单中登记并生成独立的 `graph.additions`，让新增节点跟随骨骼事件。已有原生 CUTIN 的图优先按源图执行。缺少已登记的动画或事件必须报错。验收需覆盖立绘画面、原时间线暂停及恢复、命中段数和演出资源释放。见[梅赛德丝 S3 案例](../delivery/2026-11-14-mercedes-s3-cutin.md)。

## 3. 自动检查与拒绝条件

转换时先验证压缩和分段长度，必须消费完整二进制；未知格式、未知时间线、非法索引、非有限数、错误曲线数量和重复轨道直接失败。显式曲线数量与关键帧数量不一致时拒绝导入，不尝试跳字节猜测下一条轨。

转换器在写出前独立保存源曲线记录，再核对输出的逐动画、逐目标路径、帧数及每段曲线值；翻转轨同样核对。漏轨、漏帧、改变曲线模式均失败。`curveTimelines` 汇总源标志和线性／阶梯／贝塞尔段数；`boneFlipTimelines` 汇总翻转帧。

2.1 生成产物必须包含 `curveSemantics: scsp-v2-sentinel/1` 与对象型 `curveTimelines`。Python `--verify`、Unity 清单读取／资源验证／构建入口拒绝旧未审计产物，即使文件 SHA256 自洽也不接受。标志说明经过指定转换流程，不是原作画面一致性证书，也不能抵御人为篡改整套清单。

命令在仓库根目录执行：

```powershell
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B -m unittest discover -s Tests/CharacterAssets -v
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py --closure-only
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py --verify
```

任何转换器语义修复都重新生成已导入依赖闭包；对比变更前后输出哈希、非目标字段和既有 meta。不能只替换触发报告的那一个角色。

## 4. 验收顺序

1. 先获得能重现具体错误的失败用例，固定源哈希、动画、皮肤、时间、循环设置、播放入口和相机。
2. 验证两版运行库读取所有导入骨骼，以及依赖、图集、初始皮肤和材质设置。此阶段不代表视觉通过。
3. 每个新角色检查完整动作周期；在姿势切换关键帧前、关键帧上、关键帧后及区间中部采样。只看 setup 或几个整数秒不够。分别检查单次、长循环、切换动画、Seek、换肤、翻转。
4. Unity 用实际 GPU 采集图像和骨骼／网格／附件元数据；对比固定画面。保留原版事件、骨骼变形、透明边缘、混合和绘制顺序。数值自洽和匹配同一运行库都不能代替源语义核验。
5. 再验完整 STG：特效、镜头、CUTIN、实际出声、目标位置与结束状态；原作监督对照单独登记。不能把“动画正常”升级为“技能已完全还原”。
6. 构建本地 Windows IL2CPP 包并运行；修改 AOT／原生层必须重建母包，兼容的转换数据修复可走既有资源更新。本轮客户端播放器与第三方核心无需改动。

现有专项 GPU 入口：设置 `ASTRAL_ANIMATION_CAPTURE_DIR` 为 `.artifacts` 下绝对目录，`ASTRAL_ANIMATION_CAPTURE_GROUP=mercedes-s3`，运行 PlayMode 的 `CharacterAnimationVisualTests`（开启图形设备）。覆盖整段 S3、62.08 秒循环和实际技能按钮。省略 group 时保留既有多角色 54 帧组；新增角色需扩充对照样本。

## 5. AI 修改与交接

每次修改记录“原始字节／源程序证据 → 转换字段 → 官方运行库求值 → Unity 图像”的链路。参考查看器只能提供线索，不能直接继承其容错、版本迁移和视觉修补。修复前保留失败测试，修复后提交测试、转换器、清单、必要生成资源与报告。

交付分别列出实际通过、失败、未执行及仍需原作对照的范围。所有诊断大文件、游戏程序、临时 PNG、Unity 缓存和测试包留在忽略目录；只提交项目代码、已批准资产及必要文档。只有协调者修改包配置和执行中文 Git 提交。

## 十角色资源闭包

- 白名单按 c1001–c1009 与 c1062 排序。维德瑞模型 `wildred` 对应 `c_wilderd_skill_*`，亚敏 `arming` 对应 `c_aming_skill_*`，赛西莉亚使用日语事件同名的 `c_cecilia_skill_new_*`。夏绿蒂 S3 配置保留 `c_charlotte_skill_03_05` 的显式源图名。
- STG `NODE_ANI` 的 `source` 解析为 `spani/` 骨骼。镜头及节点骨骼没有非空 atlas 时必须确实不含渲染附件。模型 timeline 中明确的 `shake.source` 归为 camera 骨骼。赛西莉亚旧 `skill3` 轨引用的 `effect/cecilia_skill_03_new_lance.cfx` 缺失，原模型只有 `skill3_1`、`skill3_2`，因此保留原 timeline 并登记无可触发动画的缺失引用。活动动画上的缺文件继续阻断导入。
- SCSP 2.1 的槽位 2 和 3 对应 multiply 与 screen。保留原槽位值并由 AOT 骨骼桥接识别。席可 `portrait/c1004.scsp` 的 `1/hairs` 乘色槽位已用于真实转换检查，原文件 144396 字节完整消费。官方 2.1 核心和旧输出文件无需修改。
- 用源 YAML 目录核对技能效果、语音事件和已加载日语音频表。若源事件的日语样本存在于另一张表，应先登记正确 bank。确实无日语版本的四个原片段保留在 `missingJapaneseProgrammerKeys`。`gerneal.bank` 同时服务史莱姆受击和维德瑞技能，旧 GUID 迁至 CharacterPresentation 的 Shared 下并复用。
