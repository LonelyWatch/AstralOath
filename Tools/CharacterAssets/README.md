# 角色资源白名单导入

本工具接受显式白名单 c1001–c1011 与 c1062，按 E7 ID 排序。新增角色按[逐人接入规范](../../docs/development/battle-roster-expansion.md)验收。`D:/sevenepic/原始资源` 仅以只读方式访问；不得把整个外部目录复制进仓库。十二套骨骼和立绘、三十六份技能 STG 为根，递归引入其真实 atlas/纹理、CFX、particle、camera、cutin，以及实际存在的 timeline。六份指定 FMOD bank 公共保存一次。Ras 没有 timeline 是源布局信息，不伪造空文件。

## 固定环境与命令

专用环境 `.tools/character-assets`；依赖由同目录 requirements-lock.txt 固定，禁止安装到全局 Python。Windows 示例（在工程根执行）：

```powershell
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B -m unittest discover -s Tests/CharacterAssets -v
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py --closure-only
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py
& ./.tools/character-assets/Scripts/python.exe -X utf8 -B Tools/CharacterAssets/import_characters.py --verify
```

`--source` 可指定本地只读来源，`--project` 指定输出工程；默认就是本工程。输出前验证白名单、所有依赖路径和扩展名。拒绝绝对路径、父目录逃逸、符号链接越界、atlas别名循环。只导入选中根的依赖闭包，不扫描导入整个解包目录。重复运行使用稳定路径，保留已有 `.meta`。改变选择集不会自动删除旧资源，清理需要明确审核。

## API 和资源契约

`convert.py` 提供 `convert_scsp(source: Path, destination: Path) -> dict`、`convert_sct(...) -> dict`；失败抛异常。`decode_scsp(bytes)` 返回 `(标准Spine对象, 诊断)`；`decode_sct(bytes)` 返回 `(Pillow RGBA图像, 诊断)`。未知格式、压缩/段长度不符、未消费尾部、未知时间线、重复动画名、非法索引必须失败，不能静态降级或跳过动画。

SCSP 保留 `2.1.27` / `3.8.99` 原版本。2.1 的 type5/type6 分别为事件和完整 draw-order；type9/type10 分别为骨骼 FlipX/FlipY，依据固定官方 2.1 C 核心枚举和 spFlipTimeline 结构解析。参考查看器对这四个编号的命名错误，严格入口不调用它们的相关分支。翻转帧的骨骼索引、轴标记、时间、布尔值、重复轨道及原始到 JSON 的完整映射全部校验，禁止把这些轨道当作辅助排序或事件丢弃。2.1 曲线、FFD、源浮点精度均保留，不添加关键帧或修补旋转。3.8 缓存曲线的九个采样点经三次方程恢复控制点，并检查所有采样点残差（≤0.00002），输出官方3.8的 `curve/c2/c3/c4` 字段。保留缓存字段输出到 `.artifacts/character-import/conversion-reports.json`；旧报告将 type9 称为 engineDrawOrder 的判断已废止，兼容字段保持为空。当前 SCSP 转换器标记为 `astraloath-scsp/3`，清单记录版本与逐动画、逐骨骼翻转轨/帧数；报告另保留所有原始时间线类型计数。未识别的引擎字段仍不宣称已还原。

纹理解码支持 SCT1 format4 RGB565+A8、format2 RGBA8888；SCT2支持明确编号的ASTC4x4/8x8、ETC2 RGBA8、原RGBA。未知编号不猜测。首批实际37张是SCT1；SCT2仅经过合成数据校验，没有本批真实样本。默认保留源 RGB/alpha。已审核的 SCT1 RGB565+A8 中性白色 PMA 遮罩登记到 `white-pma-regions.json`，由源 atlas 查找矩形（含旋转），检查原像素与 alpha 的量化关系，再设置 RGB=A。alpha 和未登记像素逐字节保留，写入 `whitePmaRegions` 清单字段。六张主纹理的半透明像素检测符合PMA，因此SkeletonDescriptor明确 `premultipliedAlpha:true`，需配合对应PMA渲染。Unity纹理应 `sRGBTexture=true`、`alphaIsTransparency=false`，避免改写透明边缘RGB。粒子format2首批七张均为等值RGBA灰度数据，不能据此宣称所有未知彩色变种已验证。

`.scsp` 的地址 `characterlab/source/<原相对路径>` 指向 SkeletonDescriptor，内部 JSON 地址追加 `/json`。atlas 保存 `.atlas.txt`，保持页名原 `.sct`；别名 atlas 展开到真实 atlas 文本，pages映射原页名到PNG资源地址。camera 可以没有图集，但必须没有渲染附件。STG graph保存原始实体、连接、entrypoint、eventlisteners，不把未知when/location摊平为确定时间；CFX/particle保留plist结构；timeline保留原JSON。

动画WebP通过Pillow逐帧seek得到已合成RGBA帧，保留每帧毫秒时长；没有时长的帧拒绝输出。 原 STG 缺少演出时按 `ANIMATION_CUTINS` 显式登记，仅在原图缺少 CUTIN 且对应原骨骼标记存在时生成 `graph.additions`，将正确皮肤的 WebP 加入依赖闭包。FMOD bank保存 `.bank.bytes`，禁止当AudioClip播放。语音事件路径依据源YML，不拼接虚构前缀。

## 清单与验证范围

`Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json` 包含 `entries`，每项记录 `source/sourceSha256/path/address/type/dependencies/outputSha256/conversion`。首次导入且共享引用计数大于1的资源位于 `Res/Shared`，银行始终公共。已有依赖保持原位置和地址，扩大引用范围时保持其 meta。目录生成源为 `Config/CharacterAssets/catalog.generated.json`，临时副本为 `.artifacts/character-import/catalog.json`；Luban结合中文文案与角色展示表生成正式catalog。

十角色导入实测 945 产物，包含 172 个骨骼、30 技能图、144 atlas、74 CFX、45 particle、9 timeline、8 CUTIN、6 bank。原首批 254 项的路径及字节哈希保持一致。导入/哈希校验不能替代Unity视觉、音频、真实事件时机或玩法验收。

MIT参考解析结构隔离在 `ThirdParty/scsp_viewer`，固定来源、许可和本地改动见其NOTICE与LICENSE；未保留上游启发式动画扫描、自动版本升级、容错输出入口。

按 SHA256 登记的生成 JSON/atlas 和未改动的 FMOD 源文件使用 `.gitattributes` 保留原字节，禁止 Git 自动转换换行。提交前同时验证工作目录、暂存区与一次独立检出的校验值；不能只验证当前机器的工作目录。

## 曲线语义与后续角色

SCSP 2.1 的 `0xFFFF` 是线性、`0xFFFE` 是阶梯，显式 Bezier 首个 float 是目标帧索引。按源记录保留三种插值，禁止全局强制平滑或强制阶梯。逐轨逐段写出校验与 `curveTimelines` 报告保证曲线未丢失；旧错误转换产物会被 Python 验证和 Unity 构建门禁拒绝。

新增角色必须遵循 [角色资源进入 Unity 的通用规则](../../docs/development/character-asset-import.md)。格式证据、复现与验收见 [梅赛德丝 S3 修复报告](../../docs/delivery/2026-11-12-mercedes-curve-correctness.md)。本轮本机原游戏静态核验支持该语义，不根据“画面看起来顺眼”修改角色偏移。

已证实的角色资源别名及未提供的旧 timeline 依赖、日语音频边界见[角色资源进入 Unity](../../docs/development/character-asset-import.md)。逐项通过 `--verify` 核对输入输出哈希，转换和资源校验以 Unity 实际加载验收补充。

技能语音事件和夹攻呼叫/应答字段只在源 `sound/voc.event.yml` 确有对应事件时导出。语音样本语言及缺失键通过音频表核对，使用技能时只启动一个原生语音事件，FMOD 保留抽样与关联对白时序。单独的样本试听留在角色实验室。
