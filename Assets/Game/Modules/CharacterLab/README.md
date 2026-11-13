# CharacterLab

角色实验室业务程序集是 `AstralOath.Client.CharacterLab`，`autoReferenced: false`。稳定启动层经 Common 的 `ICharacterLabEntry` 创建入口并调用 `Tick`，不静态引用业务类型。

`CharacterLabWindow.prefab` 由 `AstralOath.Editor.CharacterLabSceneBuilder.CreateScene` 生成；窗口是普通 C# `CharacterLabWindow : UIBaseView`，业务集中在 `CharacterLabWindowBinder`，预制体只使用 BindingMap 与通用显示组件。场景路径为 `Scenes/CharacterLab.unity`，稳定地址为 `characterlab/character-lab-window`。

## 浏览与控制

- 按 E7 ID 依次显示 c1001–c1009 与 c1062 共十名角色，可按角色名或编号搜索。
- 战斗骨骼 / 动态立绘、实际动画及皮肤枚举，播放、暂停、停止、循环、调速、逐帧、前后定位、缩放、朝向、背景和复位。 动画列表保留原始名称；源时长为 0 的单帧条目显示“静态姿势”，不补造播放时长。
- 独立日语语音条目由 AOT 音频服务播放；不可用时显示具体诊断。
- 调速、seek 和逐帧进入静音检查；切回 1x 不恢复旧声音。“复位 / 有声重播”重新从零启动视觉与声音。纯骨骼支持 seek；STG 条件演出明确提示使用逐帧或复位。
- 关闭窗口、切换角色/模式/技能时取消加载，解绑回调，释放播放器、音频实例、动态 UI、特效、网格和材质。资产加载的取消/超时由注入的 IAssetService 执行。

## 数据接口

`characterlab/catalog` 是 CharacterLabCatalog JSON。角色记录包含 id、name、displayNameKey、battleSkeletonAddress、portraitSkeletonAddress、battleSkin、portraitSkin、timelineAddress、voices 和 skills。完整字段以 `Scripts/CharacterLabCatalog.cs` 为准。初始皮肤必须由源表显式配置并存在于对应骨骼：十名角色的 battleSkin=default、portraitSkin=normal；default 立绘皮肤只含共享附件，不能代替正常表情。Binder 应用配置后再计算预览范围，用户仍可切换皮肤。角色、语音和技能名称优先解析各自 displayNameKey，缺失时回退配置名或原始 id。

骨骼地址指向描述 JSON，包含 jsonAddress、atlasAddress、pages（name/address）、premultipliedAlpha 和 scale。pages.name 严格等于原图集页名，资源地址可以指向转换后的 PNG。镜头与只含骨骼轨道的 `spani` 节点可有空图集。SHAKE 通过只读骨骼姿态接口读取原 camera 骨骼的局部位移曲线，并按 timeScale 驱动相机偏移；CAM/MOVE/ZOOM 仍采用下文所述的实验室映射。

技能 graph 保留原始 STG source 和 assets 全路径映射，不摊平条件图。解析见 CharacterPresentation 模块。CFX 的 spine/particle 原始相对路径固定为 effect/<name>.scsp / .particle；CUTIN 固定为 cut/fhd/<args>.webp。

## 演出边界

默认严格模式报告未验证的 when/location，不冒充原作还原。用户打开“条件映射预览”后，显式采用 when=1 起始、缺省或显式 when=0 连接完成，以及实验室目标位置；CAM/MOVE/ZOOM 使用线性插值，原曲线保留并提示未验证。

预览会消费 timeline 帧、CFX 骨骼特效、部分手动粒子字段和按 durationSeconds 播放的 CUTIN 帧。粒子的 colorBlends、burningType、positionType、repeat 仍有明确诊断；SHAKE 使用原 camera 骨骼的位移曲线和 timeScale，powerX/powerY/type 的振幅含义仍未应用；DARK/STOP 支持颜色、透明度与按 id 停止，持续时间/淡出重叠和深度语义仍属于明示实验映射。LAYOUT 和 SINE_VALUE 驱动仍未重现。PPEFFECT 的 sprite_blur 已按只读原始 shader 移植加权方向采样，仅读取原 STG 触发的 uniform 和时长；缺省 u_ratio=0 明确提示为实验选择。整个角色相机画面经过 RawImage 后处理，这只是实验室预览路径，不证明与原作 sprite 应用范围相同。CFX Fire 没有足够语义证据，相关原语明确不播放，不擅自启动默认动画。

拉斯 S2 的实验协攻同伴明确为梅赛德丝，只播放动作、不结算战斗。安洁莉卡 S2 选己方两个试验角色，S3 显示四个己方试验角色。梅赛德丝 S2 保留 ANI=skill1 与 ONEVENT ani=skill2 的源不一致，不擅自改写；梅赛德丝基础皮肤 S3 从导入器生成的 `graph.additions` 读取原骨骼 `skill3/cutin` 事件与基础皮肤的立绘序列。

界面文案经 characterlab/localization 读取 Luban 生成资源，key 使用点分命名，各段统一小写 snake_case，BindingMap 组件 key 保持原名。界面中文与语音 ja 分开。资源导入器/生成目录不作为手工编辑入口。

## 检查

独立播放时钟/调度断言：

```powershell
& ./.tools/dotnet/dotnet.exe run --project Assets/Game/Modules/CharacterPresentation/Tests~/CharacterPresentation.Tests.csproj
```

Unity 回归位于 `Assets/Game/Tests/EditMode/CharacterPresentationGraphTests.cs`。测试、实际 Editor/Player 播放与原作对照分别记账；单独编译或调度测试不能替代演出一致性验收。



PPEFFECT 材质由场景生成器创建或更新并保留现有 GUID：Assets/Game/Modules/CharacterPresentation/Res/Materials/CharacterSpriteBlur.mat，地址 characterlab/sprite-blur-material；Shader 为同目录 CharacterSpriteBlur.shader。相机 RenderTexture 与实例材质随窗口释放，UI 面板不参与模糊。
CFX 的 scale/rotate 等字段包含 "~" 范围时，当前不会推测抽样分布；在调度前诊断并跳过该原语，继续处理合法兄弟原语。真实 hit_new_blunt_normal.cfx.json 已用于回归，避免 FormatException 终止实验室。

十角色接入时，命名 timeline 事件沿源动画及毫秒时刻进入 ONEVENT；亚敏 `skill2/eff_on` 为 1130ms。EFFECT 的 41/42 在实验模式只生成一次群体效果，位置取试验目标中心。DELAY 与空 source 的 SHOT 按节点 time 推进。绮世 `NODE_ANI` 骨骼及裁剪资源已收齐，其节点约束、原版精确全屏层级、条件位置与镜头在实验室仍属诊断范围。BattleScene 另有自身演出验收。

日语音频使用资源目录明确列出的银行。源事件请求的无日语版本的片段列在 `missingJapaneseProgrammerKeys`，片段播放静音并按原事件继续。Windows 音频服务对每个缺失键记录一次诊断。

共享技能目录还包含 `dualCallVoiceEventPath` 和 `dualAnswerVoiceEventPath`，由原 FMOD 目录生成，供 BattleScene 根据已接受的夹攻事件播放呼叫和应答。实验室直接预览技能时仍使用该技能的一条原生语音事件，FMOD 内部选择日语变体并维持多句对白的时序。见[战斗日语语音复核](../../../../docs/delivery/2026-11-14-battle-voice-variants.md)。

杰拉图 c1010 已纳入角色目录、日语音频和三个演出入口。扩充顺序及验证证据见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。

卡琳 c1011 已接入角色内容；三技能、音频及已知缺项见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。
