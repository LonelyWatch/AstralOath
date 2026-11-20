# 摇镜方向与骨骼坐标

固定版本 pack-15-r4262 中，亚敏 S1、席德 S3 的 SHAKE 引用 `boom_cm_power_lf`，萝菲的 `c_ravi_skill_02` 引用 `boom_cm_power_rt`。两份 Spine 2.1 轨道在 0.0333 秒都给 camera 写入局部 Y=150。父骨 root 的 setup 旋转分别是 +90°、−90°。按项目资源比例 .01，未镜像时的期望偏移分别为 (−1.5, 0)、(+1.5, 0)。这两个横向摇镜在旧版都向上偏移，原因是 BattleScene 与 CharacterLab 对 camera 使用父骨局部坐标，跳过了父骨变换。

新增角色须从 STG 或已绑定的模型 timeline 定位 SHAKE，检查 camera 骨骼到根的完整链，按 Spine 求值后的骨架空间读取位移。战斗再按施法者固定朝向镜像 X，叠加到当前平移/变焦基线。不得重复乘导入比例，结束或中断要撤掉偏移。源轨的检查至少取零帧、带符号峰值、结束帧；两份相反方向的真实骨架可检出只验证“有震动”时遗漏的错误。

Unity 2022.3.62f3 使用 `.artifacts/UnityTimelineLanes` 隔离副本运行 EditMode。按 `BattleCameraRulesTests.SourceShakeIncludesAuthoredParentRotation` 和 `CharacterPresentationGraphTests.ShakeUsesScaledSourcePoseWithoutAccumulatingCameraDrift` 过滤，修复前 3 项按预期失败。修复后执行 BattleCameraRulesTests、BattleCameraContinuityTests、BattleScopedCameraTests、CharacterPresentationGraphTests 共 33 项全部通过。原始结果见 `.artifacts/character-camera-{red,green}.xml`。这些断言验证相机在源关键帧的坐标和复位。SHAKE 的 powerX/powerY/type、有限 LAYOUT 的编队和淡变、同版本原作实机画幅仍须逐技能对照。

第 31–40 名的 31 张图中，可丽的 `coile_sk3_boom_cm` 提供另一种实证。其 root 的 X/Y 缩放均为 1.07，在 0.0333 秒 camera 局部 Y=−40，战场应偏移 −0.428 世界单位。忽略父骨缩放会错为 −0.4。把此例作为第三组源骨骼加入后，在相同隔离项目中重跑上述四个测试类，34/34 通过，见 `.artifacts/character-camera-batch-green.xml`。与旧角色共用同一采样路径。

## 实战采样与受术者

战场实帧须在执行第一个 STG 节点前设置目标宽高、可见安全区和 HUD 参考分辨率，并在同一投影上演出与读回。第一轮在 4:3 Game 窗口求得相机正交尺寸 7.2 和全屏层宽度，到读回时才切换 1920×1080，白闪与立绘两侧各露出约八分之一战场。16:9 的实际起始正交尺寸为 5.4。角色身体、前景合成、后处理世界层、UI 各有独立渲染关系，须同时保留最终合成图，复核画面四边和 HUD 尺度。

同一截图夹具原先还沿用了 Game 窗口的 CanvasScaler 结果。固定 1920×1080 设计分辨率后，让 Screen Space Camera 的画布在新投影上完成一次渲染，再测量 HUD 四角。首轮断言测到设计宽 1920 投影成 2560 px，校正后落在 1920±2 px。玛雅 S1 命中帧中，左下头像与技能击中位置均可见；前后合成图保存在 `.artifacts/roster-31-40/capture-hud-before-after.jpg`。

图形 PlayMode 运行 `AddedHeroesCanEnterBattleAndPlayAllSourceGraphs`，环境变量 `ASTRAL_BATTLE_HERO_ID` 指定本批十个 ID，`ASTRAL_BATTLE_CAPTURE_DIR` 指向 D 盘采样目录。该夹具先逐人真实入队并提交 S1，再用固定站位依次播放全部 31 张图。直接播放阶段左下 HUD 使用场景的拉斯底图，前排模型和立绘随当前角色更换。每人的第一列在真实回合提交 S1 前取阵容帧，同时断言当前头像为该英雄的 `_su` 素材、三个图标与配表一致；后五列取 S1 首个 HIT、S2 反馈时点、S3 立绘中段、最后反馈与完成复位，共 60 张合成图。玛雅的蓝光、法济斯的爆尘、多米妮尔的群体冰盾、塞丽拉的火焰均能在动作或受术者附近辨认；可丽、赛兹、海斯特的末端白闪覆盖左右边缘，十人的立绘均填满画幅，收尾均恢复林地、双方站位与底部控件。结果见 `.artifacts/roster-31-40/battle-review.jpg` 和 `battle-play-final.xml`（两项通过）。本批有普通形态公开录像对照，同版本、相同队伍与明确倍速的连续原片仍未齐全。

多米妮尔 S2 的原图给 `EFFECT #13` 指定 `location=6`。暴击触发者只有一人，服务端可另向四个存活队友发放护盾。旧战斗演出把被动均当作进攻，仅把作为触发条件的队友送进特效采样。将四条同次护盾结果送入正常表现入口，原先只有一个目标，修正后有四个独立目标。图与事件之间应核对具体受术者和对应的骨骼挂点；人数变化须跟随本次权威结果。图形 PlayMode 的回归结果保存在 `.artifacts/character-camera-ally-red2.xml` 和 `.artifacts/character-camera-ally-green.xml`。

## 速度线旋转与目标显隐

赛兹 S3 的 2025-10-03 普通形态实录在 17:09.90 出现近竖直蓝线，17:10.40 改成向右上倾斜，17:12.90 后呈竖向紫带。视频中未确认倍速。首轮项目同阶段的线近似水平。固定包 BGHATCH #17／18／19 分别写入 rotate=90／315／50，scale=2.4／2.0／2.4；后段 #52／53 均写 90°。三个 1024×1024 源纹理原本横向卷动，旧代码只应用了颜色和三个卷动速度，整张纹理直接铺满视口。

每个 BGHATCH 应按当段源角度和统一缩放采样；以中心为轴，在 720 高的逻辑舞台上逆推纹理坐标，保持全屏四角覆盖和三个独立速度。以 #17 接 #18 在 21:9 取样，旧实现第一段沿屏幕水平的 UV 单位向量 X 为 1，源 90° 应为 0。修复后的回归同时核对 315° 的正负方向、两段缩放、四个视口角和复位；与原纹理透明、亮度 GPU 回归合计 17/17 通过。原始结果保存在 `.artifacts/roster-31-40/battle-edit-hatch-{red,green}.xml`。该跨版本视频用于比对线条方向，源字段决定切换时刻。[普通形态实录与时间点](2026-11-20-roster31-40-assets.md#其余九人的公开战斗实录复看)

另在 #17、#18、#52 三段各经过四分之一时长后抓取第一帧。实际可见蓝色竖线、向右上倾斜的蓝线、紫色竖带，方向和顺序与参考片相符。第三张的中央粉亮区域比参考片大，覆盖亮度与叠加特效仍待同条件核对。三张分段图保存在 `.artifacts/roster-31-40/sez-hatch-nodes.jpg`，原始 1920×1080 PNG 在同目录的 `battle-frames/source-playback/c1038-s3-hatch-*.png`。

史瑞杰思 S3 旧版在源舞台 0.8／1.8 秒能看见蓝电与紫柱，右侧仍显示敌方身体。2021-06-23 官方普通形态片的媒体时间 01:37.85／01:38.90 仅见施法者，未读到播放倍速。固定包开场 `BGSHOW #5` 隐藏背景，同时启动 `LAYOUT #14`（away_x=800、away_y=20、fade=100）与 `COLOR_BLEND #10`（blend_target=66、alpha_channel=0、id=t1）。同包其他时间轴把相同参数命名为 target_hide1／target_hide2，确认是具名的目标身体隐藏层。#20 在 233 ms 延迟后使用同一 t1，#26 在立绘结束后停止 t1。`NODE_ANI #32 target_node=76` 在 CUTIN 后启动。独立处理这类立即生效的隐藏层，可保持有限 LAYOUT、权威槽位和未验证的颜色合成各自原有语义。修复后再次实拍，0.8／1.8 秒右侧敌方身体隐去，蓝电、紫柱和施法者保持可见，对照见 `.artifacts/roster-31-40/straze-targets-before-after.jpg`。4.8／5.8 秒的劈裂行星、橙色光环以源时钟标注，录像时间还包含立绘停表段。[视频取样、版本与关键阶段](2026-11-20-roster31-40-assets.md#史瑞杰思官方历史演示)

法济斯反击源图 #22 黑幕淡入 200 ms、透明度 0.85，STOP 后淡出 400 ms，#23 独立叠加前、中、后身体特效与延迟粒子。2025-01-15 普通形态公会战原片在 04:06.8 可见红场与多个黄爆中心。项目三处 HIT 样本可见爆光与白尘，未覆盖整个短闪区间；直接播放夹具也不会产生该场战斗的暴击与受击 UI。需在阵容、暴击和真实反击入口一致时重验红场来源，以及白尘与目标的遮挡程度。[两段反击录像与命中采样](2026-11-20-roster31-40-assets.md#法济斯普通形态反击)
