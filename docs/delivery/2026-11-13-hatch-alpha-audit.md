# BGHATCH 背景透明度调查（2026-11-13）

## 现象和证据

用户指出拉斯 S3 的蓝白条、梅赛德丝 S3 的红白条过亮且像硬色块滚动。两份原 STG 都明确含 `BGHATCH`，因此速度线本身是源演出的一部分，不能用删除该节点或替换纯色背景掩盖问题。

已检查以下只读来源，并核对现有 PNG 保持原样：

- `D:/sevenepic/原始资源/effect/bg_hatch_1.png`：RGBA；alpha 0–192，339484 个像素为 (0,0,0,0)，81531 个像素为 (255,255,255,1)。
- `bg_hatch_2.png`：RGBA；alpha 0–242，89644 个像素为 (255,255,255,0)。
- `bg_hatch_3.png`：所有 RGB 都是 (255,255,255)，alpha 只有 14–126。实际渐变全部由 alpha 承载。
- `D:/sevenepic/原始资源/system/program/sprite_hatch.frag`：分别滚动采样三层，将每层 RGB 乘源颜色及颜色 alpha 后相加；输出 alpha 为三层颜色 alpha 之和，再乘顶点色。源 shader 使用 `CC_Texture0`、`CC_PMatrix`，属于 Cocos 渲染接口。

现有 Unity 移植只采 `.rgb`，在未预乘的 PNG 上完全丢弃纹理 alpha。因此透明白像素也变成满亮色块，第 3 层更成为整屏恒定高亮底色。这是共同的纹理编码契约遗漏，与两名角色的颜色参数、骨骼坐标无关。

另一个相关遗漏是当前 `Blend SrcAlpha OneMinusSrcAlpha`。原 shader 的 RGB 已乘颜色 alpha；再次用合成后的 alpha 乘源颜色会改变层叠强度和渐退曲线。对于三层颜色 alpha 大于 1 的情况，更不能把累加 alpha 当额外 RGB 增益。

## 原引擎契约参考

2026-11-13 查阅官方 Cocos2d-x 源码，限定图形技术核对，没有执行游戏版本追踪：

- [CCImage.cpp](https://github.com/cocos2d/cocos2d-x/blob/v3/cocos/platform/CCImage.cpp)：PNG/RGBA 解码路径与 `premultiplyAlpha` 负责将 RGB 乘 alpha；可以由构建开关关闭。说明不能只机械复制 fragment shader 而忽略上传纹理前的处理。
- [ccTypes.cpp](https://github.com/cocos2d/cocos2d-x/blob/v3/cocos/base/ccTypes.cpp)：`ALPHA_PREMULTIPLIED` 使用 `GL_ONE, GL_ONE_MINUS_SRC_ALPHA`。
- [CCSprite.cpp](https://github.com/cocos2d/cocos2d-x/blob/v3/cocos/2d/CCSprite.cpp)：Sprite 根据纹理预乘状态选择混合，并可让顶点 RGB 跟随节点透明度。

以上是原 shader 所属框架的官方契约证据，并非已获取原游戏运行时全部渲染开关。实际导入 PNG 的 alpha 丢失与过亮问题则可直接由本地像素复现。

## 修复范围和边界

仅修复通用 `BattleCinematicHatch` 的采样及预乘合成。保持原三张 PNG、GUID、STG 颜色、速度、时长和对应关系，不添加角色 ID 特判，不额外分配纹理副本，也不在每帧读回图像。继续在现有项目 Linear 色彩空间工作。

BGHATCH 的原引擎速度单位、`x/y/scale` 的精确解释和跨宽高比构图尚未获取完整原运行时证据；本轮不能据此声称逐帧还原通过，也不猜参数补偿透明度问题。采样后预乘与原引擎上传前预乘在双线性透明边缘处可能有亚像素差别，后续若需逐像素对齐，应在统一导入管线记录纹理编码后处理，不能每角色手改图像。

Shader 位于内置 Resources，须随项目规定的母包/平台 Shader 构建验证；本次编辑器测试不能替代 Windows/Android 正式 Player 构建。

## 回归验收

新增 `BattleHatchRenderingTests` 使用真实 shader 绘制到线性 RenderTexture 并读回像素，覆盖透明白像素、单层 alpha 与顶点渐变叠加，以及三张真实源纹理的低亮度样本。此检查需要图形设备，`-nographics` 会明确跳过；跳过不记为通过。

实际验证由协调者在 Unity 2022.3.62f3 的带图形隔离工程执行：

- 修复前 `.artifacts/presentation-hatch-red.xml`：6 例中 1 通过、5 失败。透明白像素预期黑色，实际为白色；三张真实源纹理都未保留 alpha 强度。
- 红测同时发现 shader 的 Color 属性会做 sRGB→Linear 转换，即使 fixture 通过 SetVector 传值也如此。测试色改为纯红/白端点，避免把测试公式的颜色空间误差当成产品问题。
- 修复后 `.artifacts/presentation-focused-green.xml`：`BattleHatchRenderingTests` 6/6 通过。回归包括带底色合成和顶点渐变，验证预乘不会再乘第二次合成 alpha。
- 只读 SHA-256 校验：三张导入 PNG、对应原始 PNG 和清单一致，原 `sprite_hatch.frag` 也与清单一致。本轮未改图像、GUID、角色 STG 或 shader 来源哈希。
- 三角色九技能实帧由本轮总验收记录；此处的像素回归不能替代原游戏连续录像或正式 Player 平台对照。
独立审查后补充三层同时组合的 GPU 用例：颜色 alpha 合计 2.75，顶点 fade=.4 时合计覆盖度 1.1 钳为 1，仅阻挡底色而不再次放大 RGB；fade=.2 时覆盖度 .55，核对底色按剩余覆盖混合。三层使用不同纹理 alpha 和独立纯色 tint，同时检查 RGB 与输出 alpha。补充后 8/8 实际通过（包含在最终367项EditMode中）。已检查修复后的拉斯/梅赛德丝 S3 完成淡入实帧：背景保留蓝/红速度线，透明度承载的明暗层次恢复，普通生命/等级标记继续可见；对应双向九技能源演出回归也通过。图片位于 `.artifacts/presentation-final-captures/source-playback/*-s3-*-hatch.png`。
