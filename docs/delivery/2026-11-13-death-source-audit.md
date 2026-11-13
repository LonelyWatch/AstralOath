# 死亡表现来源与边界（2026-11-13）

## 可复现的缺口

原 `BattleSimulationPreview` 在权威 `UnitDefeated` 的最后命中反馈中直接 `View.Show(false)`，`Refresh` 也以 `IsAlive` 直接关闭槽位。即使存在死亡素材，也没有让其完成的表现生命周期。这一问题与战斗伤害结算无关，不能把移除改成动画回调后才宣布死亡。

## 本地原生证据

- `D:/sevenepic/原始资源/system/program/model_blend.frag`：原混合色公式 `texc + (u_BlendColor.rgb * texc.a - texc.rgb) * u_BlendColor.a`。黑色混合是源引擎已有能力；本项目通过只影响实例的亮度属性实现同类黑化，随后透明度退场。
- `effect/death_eff_01.cfx` 明确引用 `death_pati_02.particle`、scale=1.2；`death_eff_02.cfx` 引用 `death_pati_01.particle`。两者各7个发射器（烟雾、碎片、光晕），可复用的纹理闭包只有5张：soft_smoke、ticle_01、tri_pati_01、ticle_02、flash。
- 前6个发射器混合为 `ONE/ONE_MINUS_SRC_ALPHA`，光晕为 `SRC_ALPHA/ONE`；现有粒子桥接只允许后一种，直接导入会明确报不支持，不能静默跳过烟雾。
- 5张SCT解码均为RGBA8888；部分透明像素RGB均不超过alpha，保留源像素编码并按预乘纹理播放。AlphaIsTransparency关闭，Linear项目采用既有骨骼shader的PMA/sRGB处理，不再次把已预乘的纹理当直通纹理乘alpha。

尚未得到原客户端普通英雄/小怪对 `death_eff_01/02` 的默认选择代码，也未发现通用死亡噪声阈值shader。因此默认使用01属于项目策略；不能写成已核实的原客户端默认，更不能凭印象补一个噪声溶解算法并声称原版。死亡粒子可配置替换，未来特殊Boss可提供独立演出而不在通用控制器写角色ID。

## 实现约束

`BattleDeathPresentation` 只消费视觉时钟：先黑化、后消散，淡出身体同时播放原生烟雾和碎片。材质通过每Renderer的PropertyBlock控制；不替换共享材质、不写骨骼姿态、不结算战斗。默认值为1，未使用死亡表现的所有已有骨骼不受影响。

`Tick(0)`冻结；调用者暂停时不推进；重复Begin不重播，Reset用于复活/重开，Dispose恢复材质参数和影子原色并销毁自有粒子。`BodyComplete`表示身体不可见，`IsComplete`包括源粒子最后寿命，物理槽位关闭不能提前截断烟雾。影子使用原SpriteRenderer颜色alpha，不修改共享材质。

当前黑化0.2秒、身体淡出0.4秒是项目过渡参数，等待协调者根据最新原作连续视频校准。粒子寿命来自源delay/interval/repeat/lifespan；一次性burst的duration=-1不解释为无限播放。初次整合曾在 BeginDeath 构造控制器，发现其 JSON/材质分配位于攻击热路径后已改为 View 准备阶段预热；独立 deathStarted 状态区分已预热与已死亡，Begin 只启动表现。死亡 Tick 不创建新材质或纹理。完整复活后 Actor 会重建 Renderer，激活槽位后重建死亡owner并精确传入当前 PresentationRenderer；同帧 Destroy 延迟的旧骨骼、辅助Renderer不在其所有权内，自有特效Dispose时先禁用并脱离层级再延迟销毁。身体淡出完成即可放行后续动作/胜利，原粒子尾继续Tick至IsComplete后才关闭槽位；不能让烟雾寿命阻塞所有输入。此预热/复活整合及新增精确所有权测试仍以协调者最终回归为准。

## 唯一导入入口与验证

`Tools/BattleScene/ImportDeathEffects.py --effect death_eff_01`，`--check`验证生成字节；保留已有meta/GUID，不触碰只读来源。实际导入/复核均完成，共7项原生依赖，清单为 `Assets/Game/Modules/BattleScene/Res/Effects/Death/source.json`，含源/产物SHA256及明确未确认的默认映射。源规模受显式根节点闭包限制。

`BattleDeathPresentationTests`：GPU实际像素验证黑化与淡出，生命周期覆盖重复触发、暂停、重置、共享材质不替换与其他MPB字段保留；真实源检查6个普通混合+1个发光发射器及影子恢复。首次红测5项均失败，证明原实现缺失。后续补充可配置时长和 normal/additive/multiply/screen GPU 方程回归后共15项；focused2 实际15项全部通过。早期3项GPU失败来自测试mesh遗漏TEXCOORD1暗色顶点流，补显式零流后已验证；生产shader无需调整，像素容差未放宽。最终整合与性能结果由协调者填写总报告。

本次骨骼shader属于稳定AOT/母包内置资源，发布需新母包及对应平台构建。编辑器检查不替代原作逐帧验收或Android播放。


## 实帧复核发现的共用色带列序错误

首次本项目 `death-030.png` 出现浓红云，明显不符合官方比赛连续帧中的深色烟雾/碎片。没有更换死亡变体、缩小资源或人为加黑，而是复核原始 `colorBlends`：这项字段使用 **time, alpha, red, green, blue**，旧 `BattleHudParticles.ReadRamp` 错读为 time,R,G,B,A。

- `death_pati_02` 烟雾首 key 为 `0,1,.1019608,.0627451,.1372549`：应是 alpha=1 的深紫烟雾，被错误读成 red=1、alpha=.137 的红云。后续片段仍是接近黑色的紫色；无需额外调色。
- 独立的燃魂原资源交叉佐证：`soul_burn_loop_pati` 首 key `0,0,.5019608,.9843137,1`、末 key `1,0,.3529412,.1176471,1` 的 alpha 都为0，中段alpha=1。旧解析把最后蓝色列当透明度，连淡入淡出都错误。
- 真实源读取与实际烟雾mesh颜色红测共3项均实际失败（`.artifacts/health-render-source-red2.xml`），其中死亡alpha预期1/实际.137、Burn首alpha预期0/实际1。之后仅修共用ReadRamp列序，未改源图、源CFX/particle、规模、变体、发射量或颜色数值；修复后由协调者统一执行完整PlayMode与实帧复核。

两种死亡变体的7发射器尺寸和寿命配置相同，主要区别为颜色系；没有证据支持借切换变体掩盖列序错误。现有burst数量解释（maxParticles与emissionRate、repeat组合）仍须独立核实，不能随颜色修复一起猜改。上述ARGB列序只适用于particle.colorBlends，不得机械修改STG的RGBA颜色字符串。
