# S1 镜头与 S3 目标可见性修复（2026-11-13）

## 范围和复现

基于 main 的 `4bcad3a`，修复拉斯/安洁莉卡 S1 起落镜头突切、S3 旁观者本体隐藏但信息条留下，以及拉斯 S3 五次命中只有血条而缺少敌人。保留用户未提交的伤害字体导入和通用字体修改，没有重新生成场景/预制体，没有修改原始资源、服务端结算或站位数据。

## 通用原因与实现

### 起落事件窗口

拉斯和安洁莉卡 S1 都由 `ANI_SCOPE(jump→landing)` 约束前进与回位。旧实现只把 style2 MOVE 放在事件窗口；同一窗口的缺省时间 CAM/ZOOM 留到落地执行，又因没有 time 被播放器按零秒切换。这是调度丢失时长，不是缺少相机插件。

`BattleSkillPresentation.ReadCues` 对完成连接、有效命名事件窗口、没有独立 time 的 CAM/ZOOM 继承窗口起点和跨度；在准备的节点副本里传入实际毫秒，保持共享源图不可变。显式 time（包括0）、开始连接和边 delay 继续按源定义。拉斯倍率1.3，安洁莉卡1.4及各自偏移、原曲线均保留，不能把通用控制器理解为所有角色完全相同的镜头参数。

正向事件窗口：拉斯 .4666→.8666 秒、安洁莉卡 .3333→.7333 秒；回位局部窗口分别 .0666→.4666 和0→.4333秒。保留原 SHAKE；不对整个镜头增加迟滞或任意补帧。详细源节点、录像旁证和红测见[S1审计](2026-11-13-s1-camera-audit.md)。

### 身体与信息条的共同显示状态

`BattleSimulationPreview.View` 在演出、骨骼和相机推进后的 LateTick 读取**当前骨骼 Renderer** 的 enabled 与 activeInHierarchy，并结合该实体显示状态及死亡标记，决定该单位生命/等级/状态条可见性。不能以 Renderer.isVisible（相机/上一帧结果）判断，也不能拿仍在播放的影子或粒子代表人物身体。

隐藏后仍必须每帧检查恢复条件；不能用 marker.activeInHierarchy 阻断恢复检查。普通攻击保留可见单位的信息条；源 LAYOUT 排除的旁观者本体、信息条同步隐藏。全屏 CUTIN 仍用原幕布覆盖世界和HUD。暂停、布局恢复、末击死亡、重新开始分别测试，布局恢复不得复活死亡单位的信息条。

### 拉斯 S3 的敌方群体

源 LAYOUT away_x=5000 在当前适配中用临时关闭 Renderer 表示离屏。之前遮罩直到末尾 LAYOUT mode1 才解除，源 CAM42 已切向目标，目标却仍关闭，覆盖五段命中。修复在明确目标/目标群 CAM（5/12/16/31/32/41/42）开始时，仅解除本舞台拥有的、属于接受目标的布局遮罩，恢复原先 enabled 值及其槽位影子。旁观者继续隐藏，原先关闭的 Renderer 不会被强开。

源时钟：目标镜头1.848秒，爆炸2.248秒，五个HIT为2.380、2.611、3.370、3.535、3.700秒。CUTIN 增加墙钟而冻结源时钟。梅赛德丝S3 away_x=150 属有限布局，目标一直显示，不能套用拉斯起手的离屏阶段。[S3源审计](2026-11-13-s3-visibility-audit.md)记录节点和所有权规则。

本轮仍使用既有 LAYOUT 显示适配；self_x/away_x 原引擎的完整空间搬移及 fade 曲线未还原。目标镜头解除离屏遮罩由源图和当前缺陷推导，不冒称取得原引擎实现。

## 原作参考的范围

实际查看用户提供的安洁莉卡 [BV1VB4y137Dt](https://www.bilibili.com/video/BV1VB4y137Dt/) 9.0–12.9秒连续帧：起落时背景、木桩和人物随相机连续变化。该视频发布于2022-09-06，只作为旧版通用运动旁证。近期拉斯 [BV16ZeU64E3Z](https://www.bilibili.com/video/BV16ZeU64E3Z/) 发布2026-09-19，其中3:08是S2指挥一击，不能当S3参考。本轮没有获得确认版本的近期普通拉斯S3逐帧样本，不宣称最新原作逐像素一致。[参考调查](2026-11-13-camera-visibility-reference.md)含时间点、帧文件和源哈希。

## 验证记录

所有结果在 `D:/UnityProjects/AstralOath/.artifacts`，主 Unity Editor 未关闭、未代替用户保存场景。独立镜像 `UnityBattleScene` 用 Unity 2022.3.62f3 带图形运行。

- 红测 `scoped-camera-red.xml`：9项，7失败、2通过；真实前进/回位窗口错误，空中相机不动。
- 红测 `layout-target-red.xml`：10项，9失败、1通过；目标镜头之后原布局仍关闭全部接受目标。
- 红测 `cinematic-status-red2.xml`：1项失败；身体关闭后信息条继续显示。此前首次测试夹具编译缺 Newtonsoft 引用，已改用既有公开方法反射，未修改程序集依赖。
- 完整 EditMode 初轮 `camera-visibility-edit-full.xml`：427/427通过；追加真实渲染投影回归并修复后，最终 **`camera-visibility-edit-verified.xml` 431/431通过，0跳过**。
- 完整 PlayMode `camera-visibility-play-full.xml`：**46/46通过，0跳过**，约478秒，包含九技能两朝向、正常完成/中断、音频与生命周期。
- 补充真实View的离屏/目标切入截图 `camera-visibility-status-final.xml`：1/1通过；加无CFX身体诊断后的 `camera-visibility-captures-final.xml`：2/2通过，包含重新播放九技能两朝向。
- 投影修复后，最终 **`camera-visibility-play-verified.xml` 21/21通过，0跳过**，覆盖真实信息条/死亡、暂停重开、战斗输入、HUD及截图；Render后所有可见单位top像素检查通过。与完整46项有重叠，不能相加声称67种用例。最终改动不涉及源图、音频或骨骼内核，未再次重复整套46项。
- S1的四份真实播放CSV显示前进与回位窗口内相机位置及倍率连续变化（正向拉斯X跨度约2.80、倍率投影尺寸跨度约1.24，安洁莉卡约2.60/1.54世界单位），不是等落地一帧完成；120Hz数值回归另外检查帧间步长。这些是不含统一60fps限帧的测试轨迹，不作为性能数据。
- 最终Windows Mono诊断包 `camera-visibility-windows-final-build.log` 构建成功、自动运行退出0。runId `53e2d9bb30e1421fa8f878ec418e79bb`，2场/12次接受输入/3次敌方动作/1次夹攻/4次击倒，九技能全覆盖，errors为空。
- RTX5080/9800X3D、60fps目标下，可操作2288帧P99 **18.1543ms**、最大 **33.5371ms**、1帧超过33ms、0帧超过100ms。运行时到可交互 **5865.11ms**，不含runtime之前进程初始化；启动最大帧 **3838.37ms** 单独记录，不能把可操作0长帧宣传为启动无等待。报告 `.artifacts/camera-visibility-player-run/report.json` 保留所有样本。此为Mono诊断，不是IL2CPP母包/Android/生产更新验收。

`BattleSourcePlaybackTests` 额外记录拉斯/安洁莉卡两朝向S1的源时间、相机位置/倍率、角色位置CSV，检查两名伤害型S3每个HIT的四个身体Renderer，并在HIT边界截帧。此夹具为隔离源演出关闭自动实战，图中的静态作者HUD不作为信息条跟随/隐藏证据。`BattleCinematicStatusTests` 在真正的实战View中暂停权威推进，应用有限/离屏布局、目标镜头及恢复，分别证明信息条生命周期；其截图标识为受控布局，不冒充完整S3实战截图。

运行入口：先 `Tools/Invoke-Unity.ps1 -Mode Prepare -ProjectName UnityBattleScene`；初始化 `Tools/ProjectEnvironment.ps1`，设置 `ASTRAL_BUILD_TOOL_ROOT` 为主工程，Unity `-batchmode -projectPath .artifacts/UnityBattleScene -runTests -testPlatform EditMode|PlayMode -testResults <绝对路径> -logFile <绝对路径>`。PlayMode设置 `ASTRAL_BATTLE_CAPTURE_DIR`、`ASTRAL_UNIT_CAPTURE_DIR`、`ASTRAL_ANIMATION_CAPTURE_DIR` 输出到D盘；不要加 `-nographics`。运行时禁止同步镜像或并行启动另一Unity占用它。

## 截图复核发现的附加边界

真实源图每个HIT都检查目标Renderer状态，但启用不代表不被特效遮挡。正常RasS3截图里爆炸的亮白CFX覆盖Ice/Wind/Dark，较靠前的Fire可辨。只读核对当前排序：Ice55/Wind47/Dark54/Fire61，群体CFX以首目标为锚点，正globalZ映射到57；主Spine正常透明混合的白色不透明部分会覆盖后方。没有发现此次目标恢复移动四个target。诊断截图临时隐藏CFX以确认四体位置，再原样恢复，不改变正常播放。

源globalZ的完整层序（包括同号1/1.2/1.4/2与群体锚点语义）尚未取得原引擎/近期原作连续帧证据。本次不为强显目标擅自改sorting，也不把当前特效遮挡宣称为原作完全正确。此项继续作为精细演出对照待验。

相机→Canvas同帧顺序经真实渲染确认存在生产缺陷：只ForceUpdate的4项测试通过，但增加Camera.Render后4项全部失败，误差105.46–194.59像素（`camera-status-order-render-red.xml`）；Canvas世界位置和缩放在Render时更新，将旧世界矩阵中定位的血条又移动一次。

`BattleUnitStatusPresentation` 对屏幕Canvas改为根Canvas局部投影，尺寸由根rect/像素矩形及父到根的相对比例计算；WorldSpace保留射线。处理屏幕pixelRect与固定RT，不逐单位调用ForceUpdate。真实Render后的首帧与立即复位位置/尺寸4例通过。首轮完整431例中430通过，唯一旧缩放测试仍在Render前测量；已调整该测试按其“rendered width”定义在实际渲染后断言，生产无需再次改变。

截图切换RenderTexture后仍同步Canvas并重新投影信息条；真正场景截图新增Render之后所有可见top挂点的像素断言，不能只验证未渲染的RectTransform。详细证据见[S1及同帧投影审计](2026-11-13-s1-camera-audit.md)。

## 最终证据路径

- 源演出 `.artifacts/camera-visibility-battleframes/source-playback/`：正常CFX/HIT帧、`*-actors.png`诊断帧及两角色两朝向`*-camera.csv`。实际查看Ras第0/2/4击及Mercedes末击的正常帧、Ras首/末击去CFX帧，四个目标在镜头内保持独立站位；原CFX强光遮挡边界见上。
- 真正实战View `.artifacts/camera-visibility-battleframes/status-visibility/`：`ras-layout-offscreen.png`、`ras-target-focus.png`、`mercedes-layout.png`，最终复验重写为修复投影后的图。旁观者无悬空条，目标恢复后条组与当前top对齐。
- 真实状态/死亡复验图 `.artifacts/camera-visibility-unitframes/`；实际渲染轨迹红绿XML均保留。
- 运行包 `.artifacts/battle-performance-player/AstralOath.BattlePerformance.exe`；运行参数 `-astral-perf-auto -logFile <D盘日志绝对路径>`。本轮结果见上。

同帧投影回归的实际覆盖是战斗使用的同一正交摄像机+ScreenSpaceCamera、全RT/屏幕内嵌视口；Overlay/WorldSpace、非均匀旋转父层和多摄像机不同输出没有实测，不宣称全模式验证。

## 发布与后续

本次生产修改限于 BattleScene 热更业务，不新增AOT接口、shader或原生依赖；兼容当前母包时可随业务DLL发布。此前骨架挂点/Core shader等改变仍要求新母包，此结论不能豁免历史母包兼容检查。本轮不部署资源服务器或生产后端。

后续角色复用命名事件窗口、目标焦点与显示所有权规则，不按角色ID补偿。必须覆盖两方向、每个接受目标、每个HIT、原本隐藏单位、正常完成、中断、暂停、死亡及重开；源录像必须注明日期和技能形态。最新版本的精细镜头/布局参数一致性仍需用户监督逐帧验收。

## 提交检查

本轮源代码、测试、文档与缺失的Configs目录meta一起提交，保留已有目录GUID。`node Tools/validate.mjs --staged`（17程序集）、`node Tools/check-docs.mjs --staged` 和暂存diff空白检查通过后提交。用户伤害字体meta、FoundationFont及两个既有纹理meta规范化改动不纳入本次提交。截图、日志、构建物、凭据和原始解包目录不提交；不关闭主Editor或保存用户未保存场景。
