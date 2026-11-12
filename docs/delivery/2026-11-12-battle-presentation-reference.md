# 战斗演出近期画面与本地结构核对

核对时间：2026-11-12（工具 UTC 14:53:55，上海当日22:53:55）。代码基线为主工作区 `D:/UnityProjects/AstralOath` 的 `50083c8`。本次为用户明确要求的一次性核对，不启动外服轮询。公开视频的发布日期、录像内容版本和本地解包版本分别记录，不相互替代。

## 近期实际查看材料

[azallea：Epic Seven - Take It With A Grain Of Sand - RTA #901](https://www.youtube.com/watch?v=IE2fCJXrK-4)。从公开频道视频列表取得链接，再读取原视频元数据：`upload_date=20261111`、时长1657秒（27:37）。2026-11-12核对时频道最新四条依次为RTA #901、#900、#899、#898。

本次实际查看的是YouTube提供的 `sb0` 时间轴预览帧，每格320×180，每张3×3。没有成功下载连续视频，不称为逐帧录像验收。元数据由yt-dlp 2026.10.26读取。具体帧时间按官方storyboard元数据折算，单格间隔约9.922秒，均为近似定位：

- 约03:48–04:18：技能舞台占满游戏区域；可见左侧行动条、顶条与左下行动者；约04:08的红发角色动画帧覆盖整个游戏区域。不同阶段HUD可见性并不相同，不能以一次全屏开关概括全部技能。
- 约04:28–05:47：行动条圆头像位于左侧纵轨；当前行动角色位于下端，其余头像可相邻重叠。约04:38我方Haru回合显示技能，约04:48 Genesis Ras回合技能区为空；约05:07 Dark Corvus回合技能区为空；约05:27–05:37我方Estelle回合显示三技能及Burn/Cancel。
- 约05:57–07:16：重复观察到敌方Genesis Ras回合下方技能区为空，我方Desert Jewel Basar、Schniel、Estelle回合显示技能。行动者头像位于轨道下端。

原始证据保留于忽略目录 `.artifacts/battle-latest/rta901.info.json`、`rta901.mhtml`、`rta901-2.jpg`、`rta901-3.jpg`、`rta901-4.jpg`。可见角色包括Genesis Ras、Haru、Schniel、Estelle等，但画面没有客户端版本号、录制日期或最新角色实装标记。因此可确认“11月11日发布的近期实战”，无法单凭该片确定其录制时的精确客户端版本，更不能称“当前最新版已逐帧验证”。本轮没有看到沙漏展开详情的近期画面。

## 排除与受限材料

[官方赛事链接](https://www.youtube.com/watch?v=cIn1sX4JNTU)的公开频道列表和视频元数据给出 `[Epic Seven] E7WC 2026 Knockout Stage Day 6`、`upload_date=20261108`、时长11495秒。本次取得并查看时间轴图片 `.artifacts/battle-latest/storyboard-30.jpg`、`31.jpg`、`32.jpg`、`35.jpg`、`37.jpg`、`40.jpg`；画面赛事标识可读作 **E7WC25**，与2026标题存在冲突。未查明冲突原因，排除其作为“最新版客户端”的依据，不用发布日期覆盖画面证据。

[官方兔女郎黑帮优芬妮预览](https://www.youtube.com/watch?v=nnn3L8gc3w0)由[11月8日报道中的嵌入视频](https://www.otakustudy.com/video-game/2026/11/epic-seven-bunny-girl-mafia-yufine-secret-rabbit-hole/)定位。公开视频读取要求年龄登录，未进一步访问、未查看战斗画面，不作为HUD证据。浏览器路径亦未完成：Chrome不可用、内置浏览器超时、Windows浏览器无法可靠确定URL后工具主动停止。后续仅使用独立公开HTTP媒体元数据及时间轴资源，没有导出浏览器Cookie或绕过登录。

## 只读本地原版布局

原始目录 `D:/sevenepic` 只读。通过已有FlatBuffers读取器解析三份源CSB，源SHA-256与所需12张PNG记录在 `Assets/Game/Modules/BattleScene/Res/Textures/Hud/Readiness/source.json`。不复制CSB进入客户端。布局采用1280×720左下原点，项目显示坐标乘1.5。

- `wnd/inbattle_order_detail.csb`：全屏1280×720透明关闭按钮；左侧纵轨中心x32、底y31、缩放后高648；详情位置第一行(33,48)，第二行(33,128)，随后每64递增，直至第十行y640。背景为 `_grow_s_white.png`，源颜色RGB(23,29,35)。这支持“展开左侧队列”的结构，不支持原项目540×402纯文字浮窗。
- `wnd/inbattle_detail.csb`：角色头像在行原点；沙漏(60,0)，百分比文本(70,0)、字体24、缩放0.79；生命条动态挂点(146,0)；分隔线x261与446、高28；三技能中心x302/353/404、y=-2、缩放0.65；冷却含数字与`cm_icon_etctime_b2`；状态挂点(486,0)、源缩放1.5。项目行内技能使用现有三英雄原图，史莱姆未找到对应技能图时不伪造英雄技能。
- `wnd/hud_face.csb`：外层头像缩放0.7；头像原图112×112缩放0.69；底图112×112缩放0.66；行动圈82×82。项目恢复上述尺寸关系，不对头像做AI放大或锐化。

详情面板按这些本地结构重建；生命条动态挂点的内部布局、长状态队列、未知状态图标及实际详情打开过程仍待近期实机核对。当前仅为AttackUp、DefenseUp、Immunity、Barrier、Stun提供对应原版小图。截图中头像重叠的精确避让曲线尚未确认；本次先移除人数相关强制间距，保留权威行动值的线性向下映射，100%与超100%显示在下端，详情保留真实百分比。

## 本轮回归与复现

生产修改前，Unity PlayMode `AstralOath.Client.Tests.BattleReadinessHudTests` 三项全部按预期失败：当前行动者被置于顶部、位置与存活人数相关、详情缺少原版行结构。红灯失败已在本轮工具记录核对；集成报告 `.artifacts/battle-fidelity-playmode.xml` 会由后续执行覆盖。修正后全量 PlayMode 中该组三项及头像映射一项全部通过，最终计数见[变更日志](../development/change-log.md)。

原图校验：`.tools/character-assets/Scripts/python.exe Tools/BattleScene/ImportReadinessHud.py --check`。去掉`--check`按同一来源重现12张小图和来源清单。Unity通过 `BattleTurnHudAssets.Import()`配置Sprite导入，`Sprites()`供场景Builder收集。原始截图、媒体、下载工具与缓存均留在`.artifacts`，不进Git。

## 史莱姆头像补充核对

本轮只读检查确认 `db/character.db` 是 `YUNADBSET` 分表索引，而 `character_monster_normal.db` 是579016字节二进制文件（SHA-256 `3d60744d5c2416a4be46df74a862d1f9c064e59b9395d3e79947920b6d1e403c`），未取得可验证的字段解码。公开单位索引亦未给出本组模型对应记录，不能宣称已读出原版怪物ID和数值。

在这一限制下，按用户授权采用**项目预览美术映射**：逻辑定义仍为 `preview.slime`，按其现有元素选择本地原版头像。实际并排查看模型 `slime.png`、`slime.spine.json` 各skin附件，以及四张112×112头像，确认：

- fire → `face/m0001_s.png`：红色胶体、木枝头部与绿色圆核；skin引用`fire_body_1a`、`fire_head_*`及`wind_core_*`。
- ice → `face/m0002_s.png`：蓝色胶体与深蓝龙形头骨；skin引用`ice_body_1a`、`ice_head_*`与`ice_core_*`。
- wind → `face/m0003_s.png`：绿色胶体与绿眼骷髅；skin引用`wind_body_1a`、`wind_skull_*`与`wind_skull_eye_1a`。
- dark → `face/m0005_s.png`：紫色胶体与裂开石罐、紫色内核；skin引用`dark_body_1a`与`dark_head_*`。

曾初筛的`m0251..m0255`是另一组带熔岩壳、冰滴、木冠及白骨的头像，实看后与本场模型不符，已排除。接触表仅保留于`.artifacts/battle-latest/monster-contact.png`与`monster-contact-2.png`。四张采用头像的源SHA-256已加入同一`Readiness/source.json`，导入器总计12张图片，不修改原始资源。

替换前，新增 `BattleReadinessHudTests.PreviewSlimePortraitsMatchTheFourAuthoredModelSkins` 实际失败，期望`m0002_s`而实际为`pet_slime1_n_grade1_1_s`。替换后的统一Unity回归已通过，最终计数见变更日志。本修改不将这些文件名写入共享战斗逻辑、不修改数值、不冒充已解析原版单位表。
