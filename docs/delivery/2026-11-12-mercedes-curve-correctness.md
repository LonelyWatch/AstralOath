# 梅赛德丝 S3 错位：曲线语义修复

本轮修复用户截图中的身体与书本大幅分离，并把防回归约束落实到通用转换、资源验证与构建入口。上一轮翻转轨及状态修复没有覆盖 SCSP 曲线哨兵语义，不能作为 S3 完整动作正确的证据。

## 根因与独立证据

`model/mercedes.scsp` 的 `skill3 / quarter_view / translate` 在 0.6666 秒与 1.3666 秒之间，从 x=0 切换到 x=535.81；其原始曲线块（解压后骨骼二进制偏移 856524）为 `FE FF`。这组骨骼控制身体绘图，书本使用另一组骨骼。原动作应保持前一关键帧再切换，旧转换却做线性插值，把身体逐渐移离书本。

旧 `_curves_v2` 把 `0xFFFF` 与 `0xFFFE` 都输出为空曲线，Spine JSON 的空曲线默认线性。正确语义是 `FFFF → linear`、`FFFE → stepped`。显式 Bezier 载荷首个 float 也被误注释为缓存段数，实际为目标帧索引；现在按原索引赋值，四个控制点保留源浮点精度。

最初线索来自 [固定提交中的原作者分析](https://github.com/Aikiooo/e7codex/blob/1edf249f5b8dbda734ea96432e22ab108a8e308c/convert/vendor/epic7_scsp2json_v1_0/_epic7_scsp2json_v2_1_27.py#L344-L391)。该参考文件 SHA256 为 `ddc6e475c2e29d0f44d89cade75a7c03d3d1798e2c0957e8d69d9c53663ea5ee`。没有引入其运行库迁移、全局曲线覆盖或旋转修补代码。

随后独立只读分析本机 `D:/Games/EpicSeven/EpicSeven.exe`，未运行、未修改、未提交游戏程序：

- 文件大小 86,052,496 字节，SHA256 `09ca7b4b14863eb98d6e3c0f1650f27d89cc2401ed73e6c744abe7d859cac275`，PE image base `0x140000000`。
- SCSP reader `0x1407387a0` 的 translate 分支 `0x140739415`：`0x1407394a6` 对 FFFF 写曲线指针 -1，`0x1407394ca` 对 FFFE 写 NULL。
- 求值函数 `0x140e845e0`：NULL 返回 0，-1 返回传入插值比例，证实阶梯与线性语义。
- Bezier 分支 `0x140739291` 将首个 float 转为整数帧索引并乘 19，定位曲线缓存。

地址只用于标识这份已哈希程序的证据，不能当作跨版本固定地址。

## 修复范围

转换器升级为 `astraloath-scsp/3`。按源标志处理线性、阶梯、显式 Bezier；拒绝无效曲线数量、类型、索引和非有限控制点。源曲线使用不可变记录保存，写出后检查每条轨、每个关键帧及每段曲线，避免 writer 再次丢失语义。

重导已批准的三角色依赖闭包，共 44 份骨骼、254 产物。其中 27 份骨骼 JSON 的曲线字段变化；独立恢复旧解析器后，44 份旧输出哈希全部命中留存清单，新旧非曲线字段全部一致。唯一 3.8 骨骼、全部纹理／声音／其他产物逐字节不变。资源目录下 335 个已有 meta 哈希全部保持。

2.1 曲线审计共 49,250 轨、238,256 帧、189,006 段：4,606 线性、180,566 阶梯、3,834 Bezier。43 份 2.1 清单项标记 `curveSemantics: scsp-v2-sentinel/1`，附逐动画 `curveTimelines`。Python 校验与 Unity 清单／构建入口拒绝未审计的旧产物，即使文件哈希正确。

播放器、第三方核心、角色坐标、镜头参数均未为了本次修复改变。后续角色执行 [通用资源导入规则](../development/character-asset-import.md)。

## 重现与检查

- 真实 Unity 旧资源回归：5 项中 4 项失败。1.081 秒时 `quarter_view.LocalPosition.x` 期望 0.0026，实际 3.17459488；修复后 5 项通过，关键帧当刻的切换仍保留。
- 独立官方核心求值：同一时刻身体 `quarter_center.WorldX` 从 3.5736947 修正为 0.40169984，消除约 317.2 个源像素的错误位移；书本 WorldX 保持 1.2832。62 秒循环也不再出现同一线性滑移。
- Python 转换／导入：44 项通过；新转换与导入门禁均先观察到失败，再验证修复。
- Unity EditMode：207/207 通过，包含新姿势与构建门禁及已有动画切换、Seek、皮肤和生命周期回归。门禁新增前实际观察到 6 个拒绝用例失败。
- 常规 GPU PlayMode：14/14 通过，并保留默认多角色 54 帧回归。两套固定官方核心读取全部 44 份导入骨骼通过。
- 专项 GPU：修复前、后各捕获 115 张 1920×1080 图像和元数据，均由真实场景、实际 UI 入口及 Direct3D11 渲染。覆盖完整 S3、关键阶段、循环至 62.08 秒、fresh/Seek、严格技能入口。逐张查看全周期的 24 个采样姿势，并检查长循环及技能末帧；报告中的大幅身体偏移已消除。

专项产物位于 `.artifacts/mercedes-curves-before`、`.artifacts/mercedes-curves-after`；失败姿势回归为 `.artifacts/unity/MercedesCurveRed.xml`，最终 EditMode 为 `.artifacts/unity/CurveFinalEditMode.xml`。本机静态证据摘要为 `.artifacts/character-diagnosis/native-curve-proof.json`。

## Windows 测试包

Windows x64 IL2CPP 构建成功，输出 `.artifacts/characterlab-builds/Win64/player-v1-8bd2845a63a14d8caadb9da3faed56d4/CharacterLab.exe`，母包身份 `be67a11f3a3e412d96d6021c632340a4`。

对该进程临时阻断所有出站连接后，离线启动、三角色、九技能事件入口、语音接口、战斗／立绘网格、暂停、停止、关闭释放检查通过，退出码 0；结束后移除防火墙规则，日志 `.artifacts/curve-player-offline/lifecycle.log`。九技能每项只运行 3.5 秒，此处是平台冒烟，不能替代完整技能原作对照。

本轮原生隐藏窗口截图检查失败（全黑图），保留于 `.artifacts/curve-player-offline/player.log`，不记为原生视觉通过。随后明确省略截图参数完成上述生命周期检查，没有禁用失败断言或修改播放器。视觉对照依据实际 Direct3D11 的 Unity GPU 采样。Android 未执行。

Unity 主编辑器如果仍在 Play，需退出 Play、等待资源导入后重新运行 `Tools/角色实验室/打开场景`；已经加载到内存的旧骨骼实例不会因磁盘 JSON 改变自动替换。使用独立程序时启动上面的新目录，旧包仍包含旧数据。

## 仍需区分的范围

循环前后首关键帧晚于零的附件／翻转／FFD 会保留上一圈状态，这是官方 2.1 原生语义；本次不伪造零帧，也不修改 vendor。长时间旋转累计与浮点时钟差异不是截图的大幅分离根因。若以后需要“每圈独立从头重播”，需独立定义并测试该预览策略。

完整 STG 的未确认镜头、条件、目标锚点以及九技能原作音画对照仍未验收；本轮证明曲线误译的具体错误已修复，不能据此宣称所有角色或所有技能完美还原。Android 未执行。本轮没有服务器或数据库变更。
