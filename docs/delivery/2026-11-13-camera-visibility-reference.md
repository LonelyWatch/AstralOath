# S1 镜头与 S3 参与者可见性：源数据及录像核对

日期：2026-11-13。本文只记录本轮查阅的证据和推断，不将源图能加载、单元测试通过或旧录像对照称为最新原作完全还原。实际 Unity 回归结果由本轮实施报告另行记录。

## 范围和证据等级

- **已读原始字段**：当前已导入的拉斯、安洁莉卡 S1，以及拉斯、梅赛德丝 S3 的 `graph.source`，保留原节点 ID、参数、连接、延迟和缺省字段。
- **本轮实际查看录像**：安洁莉卡旧录像 5–35 秒的概览、9–13 秒每 0.1 秒一帧；拉斯相关录像第 1 段 180–396 秒每秒一帧。以下只把画面中能辨认的事实列为观察。
- **推断**：原作引擎的 `ANI_SCOPE` 缺省时长继承、`LAYOUT` 的参与者集合和 target 镜头阶段的显示政策没有原引擎源码。因此项目实现规则须同时受原数据、录像和用户报告约束，不能标为原引擎源码复现。
- **未验证**：普通拉斯 S3 的最新原作逐帧素材；全部角色、全部场景和全部平台。本轮没有开展国际服版本追踪，也没有下载新的大视频。

## S1：同一控制规则，保留各自参数

源文件位于 `Assets/Game/Modules/CharacterPresentation/Res/Characters/<角色>/Configs/stagept/`：

- 拉斯 `c1001/c_ras_skill_01.graph.json`：前跳 ANI #11 启动 ANI_SCOPE #10，范围为 `jump → landing`。Scope 分支为 MOVE #9（`location=16, style=2, power=50`）、CAM #12（`location=12, y=-30`）、ZOOM #8（`scale=1.3, curve=0.2911877,0.9548611,1,1`）。后跳 ANI #6 对应 Scope #3，再接 MOVE #2（`location=11, style=2, power=50`）、缺省 CAM #4 和 ZOOM #1（`scale=1`）。
- 安洁莉卡 `c1062/c_angelica_skill_01.graph.json`：前跳 ANI #2 启动 Scope #7，同样为 `jump → landing`。Scope 分支为 MOVE #11（`location=16, style=2, power=50`）、CAM #12（`location=12, x=-20, y=-30`）、ZOOM #8（`scale=1.4`，曲线与拉斯一致）。后跳 ANI #6 对应 Scope #5，再接 MOVE #9（`location=11, style=2, power=50`）、缺省 CAM #10 和 ZOOM #13（`scale=1`）。
- 上述 S1 的 Scope 分支 CAM/ZOOM 都没有 `time` 字段，连接也没有显式 `delay`。它们并不是原数据显式声明的 `time=0` 镜头切换。
- 命中时间仍各自独立：拉斯技能 ANI 启动后 266/566 ms 两个 HIT；安洁莉卡为 400 ms 一个 HIT。镜头修复不应改动这些权威之外的原演出触发点。

因此可使用同一个 Scope 镜头调度器，但不能把两个技能的倍率、偏移、命中次数或时长统一成常量。推荐解释是：当 CAM/ZOOM 没有显式时长、没有边延迟，且直接受有效动画 Scope 约束时，随 Scope 的起止事件调度过渡；保留显式 `time=0` 的切镜和有延迟的独立时序。该推荐属于项目解释规则，须用真实 STG 回归防止以后退化为在 landing 才发出零时长切换。

## S1 录像：支持跳跃期间连续变化，不证明精确曲线

用户提供的 [安洁莉卡录像 BV1VB4y137Dt](https://www.bilibili.com/video/BV1VB4y137Dt/)：本地 `view.json` 的发布时间为 **2022-09-06 09:10:26 +08:00**，总长 231 秒、CID 825101585。这是旧参考，不能标为最新客户端效果。

实际查看的 `angelica-s1-009-013.jpg` 为 8 列 5 行、从左到右再从上到下的 40 帧；名义时间为 9.0、9.1……12.9 秒，切片/重采样允许约半个采样间隔的误差。

- 约 9.7 秒开始前跳；随后约 9.8–10.5 秒，角色向目标前移，背景与木桩构图在到达命中阶段之前已经变化。
- 约 10.6–10.8 秒为命中；随后后跳阶段，背景和目标构图逐步恢复，约 11.4 秒已回到普通站位附近。
- 这组画面支持“镜头和缩放随前跳/后跳过程变化”，不支持“先完成位移，再在 landing 才硬切整个镜头”。视频采样不足以恢复精确 Bezier 曲线，也不能独立证明缺省字段的内部实现。
- 录像中可见角色及木桩上方的条组随其所属对象改变位置，普通攻击不是全局隐藏所有信息条的依据。

本地资料：

- `.artifacts/battle-timing-reference/angelica.mp4`（既有视频，本轮复用）。
- `.artifacts/camera-visibility-reference/angelica-005-035.jpg`（2 fps 概览）。
- `.artifacts/camera-visibility-reference/angelica-s1-009-013.jpg`（10 fps 逐帧接触表）。

可复现采样命令（在项目根执行；所有新产物仍放 D 盘）：

```powershell
& '.artifacts/video-reference-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v9.0.2.exe' -ss 9 -i '.artifacts/battle-timing-reference/angelica.mp4' -t 4 -vf 'fps=10,scale=480:-1,tile=8x5' -frames:v 1 '.artifacts/camera-visibility-reference/angelica-s1-009-013.jpg'
```

## 拉斯 S3：开场离场布局不能被解释成整段目标不可见

`c1001/c_ras_skill_03.graph.json` 中关键依赖如下，时间均按原字段保留：

1. ENTRY #9 同时启动 ANI #10、CAM #6 和 LAYOUT #5。LAYOUT #5 为 `away_x=5000, fade=200, time=200`；CAM #6 指向 `location=1, y=-30, time=200`。这是开场强调施法者的明确数据。
2. ANI #10 启动后 1848 ms 触发 CUTIN #19。
3. CUTIN #19 完成后进入 CAM #26（`location=42, x=-300, y=200`，没有 time），再进入 CAM #16（`location=42, y=-50, time=200`）；#26 启动时同时 ZOOM #15 到 0.8/200 ms。
4. CAM #16 完成后延迟 200 ms 触发 EFFECT #20：`ras_new_skeff_sk3.cfx, location=42, scale=1.6`。这已转为目标组上的爆发构图。
5. EFFECT #20 启动后依次触发 HIT #27（+132 ms）、#29（+363 ms）、#31（+1122 ms）；#31 再延迟 165 ms 到 #30，#30 再延迟 165 ms 到 #28。总计五次源 HIT。
6. EFFECT #20 完成再延迟 200 ms 才执行 LAYOUT #22（`mode=1, fade=200, time=200`）回到普通布局，并恢复相机/缩放及背景。

**原字段事实**：CUTIN 后到五次 HIT 期间，没有另一个 LAYOUT 节点撤销开场 `away_x=5000`；却有明确的 location 42 镜头、特效和 HIT 链。

**本项目应采用的有界推断**：不能把“开场离场/隐藏”保持到 mode=1 才全部恢复；进入目标组镜头/命中阶段时，需要恢复本技能的有效目标参与者，然后继续排除无关角色。恢复阶段由源图语义决定，不用 `if heroId == c1001` 或硬编码固定秒数补丁。它解释了用户报告“伤害数字和血条在，敌人本体不在”的症状，但不等同于已看到原引擎的目标显隐实现。

`location=42` 的锚点与各节点时序应继续由现有通用源演出规则负责。若未来素材出现不同组锚点/特殊隐身/镜头排除，需要新增明确语义与回归，不能把所有 `CAM` 都定义成“显示全部人”。

## 梅赛德丝 S3 与统一信息条约束

`c1005/c_mercedes_skill_03.graph.json` 的 LAYOUT #4 为 `fade=200, time=200, log=10, self_x=0.1, away_x=150`，结束回普通布局是 LAYOUT #6（mode=1）。其技能前后镜头有独立的 location 11、x/y、time 参数；不能套用拉斯 5000 的离场值。

本轮用户明确要求：演出中只有施法者及技能目标参与；无关角色本体隐藏时，生命、等级、状态条也必须隐藏。应把“场景布局隐藏”作为角色呈现状态，而不是只关闭 Renderer 或单独操作 HUD。HUD 使用所属角色的同一可见性判定；保留死亡完成、离屏、CUTIN 覆盖和布局隐藏的不同原因。

反之，不应因进入普通技能流程而全局关闭血条；仍可见的参与者继续跟随人物挂点和镜头缩放。CUTIN 幕布可以覆盖界面，不能通过幕布期间清除或重置角色死亡状态。此项是项目通用约束和本次用户验收要求；本轮没有取得最新普通梅赛德丝 S3 的逐帧对照，不能说已经由最新影片逐帧证明。

## 较新拉斯相关录像的适用边界

用户提供的 [BV16ZeU64E3Z](https://www.bilibili.com/video/BV16ZeU64E3Z/) 本地公开元数据发布时间为 **2026-09-19 00:43:59 +08:00**。总视频信息为 964 秒，本地只保存第 1 段，约 396.35 秒。新旧角色形态、技能名应逐项辨认，不能因为标题有拉斯就把所有大招归给普通拉斯。

本轮实际查看 `.artifacts/camera-visibility-reference/ras-180-270.jpg` 与 `ras-270-396.jpg`：

- 用户指定约 188 秒一带的原片已可辨认拉斯攻击过程；约 192.5 秒技能名为“指挥一击”，属于 S2，不能用作本次 S1 或普通拉斯 S3 的直接证据。
- 已看范围包含多角色演出；未确认普通拉斯 S3 的完整可辨认片段。不把未知 CUTIN、其他拉斯形态或其他角色特效作为普通拉斯证据。
- 本轮定向网页检索没有找到并实际验证匹配的最新普通拉斯 S3 录像，因此本文只记录这一证据缺口。

## 实施必须覆盖的回归

- 两个真实 S1：前跳和后跳起止的相机位置/缩放连续；每段参数仍来自各自 STG；显式零时长切镜及独立 delay 不被误平滑或提前。
- Ras S3：开场排除布局、CUTIN 覆盖、目标组镜头、五个 HIT、退场恢复分别检查；每个有效目标在攻击阶段有本体和所属信息条，无关角色两者均隐藏。
- Mercedes S3：caster 和实际 target 集合保留，其余角色的本体/信息条一起隐藏；回普通布局后恢复正确集合。
- 状态边界：暂停、取消、重开、重复播放、死亡、已经消散角色不被后续 LAYOUT/CAM 误复活，HUD 不先于本体恢复；协攻交接不让旧演出重置新演出镜头或可见性。
- 不修改命中结算、服务端权威、事件总伤害或技能目标集合来补视觉症状。人物可见性是呈现结果，不能反向更改战斗状态。

## 核对摘要 SHA-256

下列值锁定本次查阅的数据，不用于宣称全部解包资源通过：

```text
c_ras_skill_01.graph.json       DE2F397EF8F4CF953198246DAE2603EE1D3F8FEB191D0AAB0A515C11239EED2D
c_angelica_skill_01.graph.json  77243FEA8D231B19861E0DB4B38A791532A3B1513EECAE274BD8D56E04D49BDE
c_ras_skill_03.graph.json       449C0B01A05E40BA2A3694277BDAA68FE01994BE4315FC4AE9B9F46C0C97DC5D
c_mercedes_skill_03.graph.json  61A620BCF8A67BAB37B635DD517F5B2CCE2864FB8B7903AC0246B0C248876864
angelica.mp4                   6CA26B225D854585E1F2B17DAF45C5FC5E83787D96BB1294421A84A5C315A3B8
angelica-s1-009-013.jpg         5474CDB7F9935E3CBF76E08205AAC01B57BEE1E34A23F6B44057D15156415535
bilibili-reference/part1.mp4   F8679480E1CF8F50FE294B1FA0BD0D1E4047C8DE5A7BA6524FE697D063A5F04E
```

相关既有记录：[源镜头调查](2026-11-13-camera-source-audit.md)、[安洁莉卡时序核对](2026-11-13-angelica-timing-reference.md)、[通用源演出规则](../development/battle-source-presentation.md)。本地视频、接触表留在 D 盘 `.artifacts`，不提交到 Git。
