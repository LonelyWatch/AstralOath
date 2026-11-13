# 血条跟随、损血与死亡表现（2026-11-13）

## 原作证据

本轮实际取得并查看官方 [E7WC 2026 Knockout Stage Day 6](https://www.youtube.com/watch?v=cIn1sX4JNTU)（2026-11-08）的短片和连续帧。2990秒普通英雄条可辨识9道内部刻度、10等分区间，50%线更高；3001–3005秒受击抬升/落回时条组跟随人物；3041–3042秒可见黑化和烟雾碎片，末尾被Victory遮挡。没有找到支持“每1000HP一格”的官方文字定义，不采用该说法。

十等分是本轮普通英雄样本的测量，不代表Boss、盾条或全部历史版本。数值、帧时间、文件哈希及限制见[血条原作审计](2026-11-13-health-reference-audit.md)；本地死亡素材见[死亡来源审计](2026-11-13-death-source-audit.md)。视频/截图/测试产物只在D盘.artifacts，不提交媒体缓存或整个来源目录。

## 实现与维护

- 每单位普通C# View持有 `BattleUnitStatusPresentation`。在骨骼/镜头更新后将Frame中心投影到原top挂点，响应位移、受击、Actor/镜头缩放；朝向翻转不倒置文字。缺失锚点明确告警，不用角色ID或动态Mesh Bounds补偿。
- `BattleHealthSegments` 单Graphic使用原 `game_hud_bar_split.png` 绘制9道线，中线全高、其余半高靠下，空槽仍显示。默认十等分；可选每HP间隔仅为扩展接口，不是当前原作规则。
- 原 `game_hud_bar_dhp.png` 在主HP后方显示损血尾。主条0.16秒，尾条延迟0.12秒后0.28秒追上；治疗0.18秒。连续扣血续接当前显示、相同快照不重启动画、治疗取消旧尾条、最大生命改变重建基线。这些毫秒数是项目参数，未声称来自原客户端。
- 死亡表现消费权威UnitDefeated末击反馈，黑化、身体淡出及原生烟雾碎片独立于结算。默认黑化0.2秒、淡出0.4秒，特效映射与精确时长仍待确认。身体完成允许后续流程继续，粒子尾到期才停用槽位；不把普通淡出说成已提取的原版噪声溶解shader。
- 本场View创建时准备死亡资源，末击不解析粒子/创建材质。只写当前骨骼Renderer的PropertyBlock和独立影子，不改共享材质；复活重绑当前Renderer，自有旧效果立即脱离后销毁。配置预热失败回收已构造View、状态UI与材质。
- 暂停冻结视觉时间；Cut-in跟随舞台时钟暂停；空闲尾迹继续完成。死亡/伤害/治疗/奖励不在动画回调结算，服务端与Shared Battle不变。

配置为 `Assets/Game/Modules/BattleScene/Res/Configs/UnitPresentation.asset`。`Tools/战斗场景/更新血条与死亡资源引用`只更新引用，保护未保存场景并保留作者布局。新增角色先检查骨骼锚点，特殊Boss需要独立政策和证据，不往通用类写角色判断。

素材复现：项目Python执行 `Tools/BattleScene/ImportDeathEffects.py --check` 及 `Tools/BattleScene/PreparePreviewArt.py`。死亡首次导入去掉--check，HUD用--restore。meta/GUID保持，来源清单记录路径与SHA256，D:/sevenepic只读。

## 验证过程与状态

原实现失败复现：死亡能力5项、血条10项、真实场景2项；场景中末击立即停用，Frame与动画top偏离51.5068像素。初版修复27项EditMode通过，真实跟随/暂停场景2项通过。增加资源释放、同帧复活及画面采集后4项PlayMode通过。目检及跨角色坐标复核继续暴露刻度显示/非恒等父骨问题，最终结果完成后记录于此，不能把上述中间绿测冒充最终验收。

`ASTRAL_UNIT_CAPTURE_DIR` + `BattleUnitPresentationIntegrationTests`从真实BattleScene采集1920×1080诊断帧。合成扣血/死亡反馈只验证视觉生命周期，不冒充权威击杀；Windows多轮真实战斗另行验证。

## 目检发现与通用修复

1. 自定义Graphic只有RectTransform并不会自动获得CanvasRenderer。刻度几何虽然可通过手动OnPopulateMesh测试，实际PreRender会跳过。已显式RequireComponent并增加实际CanvasRenderer网格/材质/纹理回归，修复前缺组件确实失败。
2. 原particle.colorBlends列顺序是time、A、R、G、B。旧解析把不透明深紫烟雾读成低透明度亮红色；Burn首尾本应透明也读错。三个真实源/发射mesh回归先失败再修复，只改共用字段顺序，不改原尺寸/数量/变体来掩盖。
3. 旧TryGetBonePose返回相对父骨局部位置；史莱姆root缩放1.6，直接当骨架坐标会少算父变换。新增TryGetBoneSkeletonPosition读取两版官方WorldX/Y，包含父骨/sourceScale/内核翻转，再由Unity Root转世界；镜头localPose契约不变。8项双版本父骨变换/网格对齐/翻转/释放回归先确认缺接口失败。

本轮完整EditMode：408/408通过、0跳过，日志 `.artifacts/health-death-edit-final.log/xml`。初次全量有旧反射时序夹具未初始化views导致失败、隔离工具根未设置导致1跳过，分别补齐夹具和ASTRAL_BUILD_TOOL_ROOT后全量复跑；未降低产品断言或删除测试。完整带图形PlayMode：44通过、1可选截图用例因未设置专用目录跳过；设置ASTRAL_ANIMATION_CAPTURE_DIR后单独补跑1/1通过，合计45项均有通过记录。日志为 `.artifacts/health-death-play-full.log/xml` 和 `health-death-animation.log/xml`。Windows Mono诊断包已实际构建成功，运行结果另列。

源资产校验已执行：7项死亡源依赖；99份HUD来源、12层森林、3份电影背景纹理及shader来源全部通过。模块边界校验17程序集、0错误。Windows诊断增加UnitDefeated事件计数，成功条件要求真实击倒以及九技能覆盖；不把合成反馈截图冒充权威事件。

## 发布边界

骨骼shader与挂点公共接口属于AOT母包变更，正式发布需新母包。Mono诊断不替代IL2CPP/Android验收；精确动画时长、特殊Boss/死亡、复活权威规则、生产PvP不在本次范围。未升级依赖、开启持续外服追踪或部署线上。

## 实际截图复核

最终 `.artifacts/health-death-finalframes/`：idle、moved-scaled-attacked、damage-010、damage-settled、death-010/030/055。目检已确认九道刻度实际可见、空槽保留刻度、红棕尾层追随，死亡烟雾由错误红色恢复原深紫并有碎片。没有仅用数值测试判定显示正确。`.artifacts/health-death-battleframes/`有145份战斗/Burn/源演出截帧；本轮查看Burn激活及对应修复画面，全部原作逐帧一致性仍不能由这些截图推断。

注意：Burn布局独立测试主动关闭自动实战，图中“开始试玩”、5级和静态信息条是夹具模式；正式BattleScene直接Play仍进入60级实战。最终血条/死亡诊断来自已启动的实战Scene和真实View，不使用布局模式替代。

## Windows实际运行与性能

Unity2022.3.62f3 / Windows x64 Development Mono / D3D11 / RTX5080 / Ryzen9800X3D，目标60FPS。构建与运行实际退出0；归档 `.artifacts/health-death-player-run/`，runId `d6262af70bd94534bd0e7f20ee68f58d`。两场、12次接受命令，九技能全部覆盖，3次敌方行动、1次夹攻、4次权威UnitDefeated。音频有10次语音、13次技能音效、6次受击、30次programmer sound创建；非物理扬声器听音验证。

- 可操作帧2288：P99=18.1947ms，最大33.847ms，超过100ms为0；与此前同机诊断处于同一量级，不宣称完全无抖动。
- Runtime初始化到可操作5865.0638ms；启动长帧3842.1593ms，重开长帧761.7512ms，单独分类且没有藏进可操作P99。不能说加载已无等待或所有阶段无长帧。
- 自动输入走EventSystem按钮监听与实际ChooseTarget/权威Submit，没有注入操作系统鼠标，目标raycast与真实UI遮挡不由此验证。PlayMode另覆盖跟随、暂停、同帧复活与释放；原作视觉时长的精确匹配仍待对照。
- 产物 `.artifacts/battle-performance-player/AstralOath.BattlePerformance.exe` 可手动启动；本次为Mono诊断，未构建正式HybridCLR/IL2CPP母包或Android。

本轮代码复核未发现剩余阻断；资产来源、17程序集边界、6项校验工具测试、文档同步与暂存凭据/产物检查通过。用户预存 `damage_0.png.meta`、`FoundationFont.asset` 修改保留且不提交。进入Unity后重新打开BattleScene再Play，以载入已保存的新配置引用；没有强制关闭或覆盖用户编辑器中的未保存场景。

## 2026-11-14 扣血红条时序

- 旧版在伤害序列最后一段才触发血条变化，原 dhp 贴图为 RGB(129,50,45)，收缩仅 0.28 秒。现用独立高对比红段 RGB(231,44,53)，按实际血量损失百分比绘制。原 dhp 文件保留在来源目录及素材记录中。
- 首次实际失血立即更新绿条并显示红段，后续每段扩展红段，外缘保持在本次受击前的血量位置。末段后保留 0.15 秒，随后用 0.50 秒收回到绿色端点。纯护盾吸收无红段，同一击破盾且扣血时清除残留白盾。动画随战斗暂停；新伤害可续接未收完的红段，治疗清除旧红段。
- 当前按每次实际攻击的最后命中释放红段。协攻和追加攻击可接续，各目标独立计时。当前调整只作用于客户端展示。
- Unity 2022.3 专项 EditMode 28/28、PlayMode 10/10。1920×1080 实景逐阶段采样，目标红段约 14 px（首次实际失血）、31 px（末击）、24 px（回收中），随后归零。图在 D 盘 .artifacts/ten-hero-frames/damage-trail-{first-hit,last-hit,retracting}.png。Android 真机尚未复测。
