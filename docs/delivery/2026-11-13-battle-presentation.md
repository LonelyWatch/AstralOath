# 战斗演出修订与资料迁移验收

基线 main 76e2303，主工程 D:/UnityProjects/AstralOath。本轮按用户指定录像和只读原始资源修复；不自动追踪国际服、不升级第三方依赖。录像[第一分P](https://www.bilibili.com/video/BV16ZeU64E3Z/)发布于2026-09-19 00:43:59 UTC+8，3:08起实际是燃魂S2。仅这一段作为本轮拉斯连续帧依据，未证实录制客户端版本。

## 结果与复现

- Unity2022.3.62f3，全量EditMode 246/246通过。原始XML/log：.artifacts/battle-rules-editmode-all.*。
- Foundation实际通过：服务端56通过、1专用MySQL条件用例跳过；内容/代码更新、签名损坏及依赖校验、配表访问器/无效源、协议生成、模块边界通过。运行 Tools/Check-Foundation.ps1；记录 .artifacts/battle-rules-foundation.log。
- 新12张combo原图、2个BMFont文件与combo.csb hash通过；既有音频bank、12头像与3布局hash通过。运行 Tools/BattleScene/ImportDamageFeedback.py --check、ImportBattleAudio.py --check、ImportReadinessHud.py --check。
- 环境初始化在项目PowerShell7和WindowsPowerShell5.1通过；Tools/Open-Unity.ps1实测识别已开主工程，未重复启动或关闭Editor。
- 全量图形PlayMode：28通过、0失败、1可选CharacterAnimationVisualTests截帧跳过（非战斗矩阵），.artifacts/battle-rules-playmode-all.*。九技能各双朝向自然播放，共18次，并在每个朝向中断拉斯S2、梅S3、安S3，共6次中断；断言镜头/角色/影子/背景和Mesh/RT恢复。首轮ScreenCapture在batchmode不出图，最终以同步副相机→主相机渲染生成1920×1080 PNG，不把空截图当通过。
- 重要逻辑新增失败测试再修复，日志包括 battle-rules-red、battle-camera-red、battle-overlap-red、battle-diag-red、battle-feedback-red。全量EditMode首次因人工启动临时目录分隔符及缺少ASTRAL_BUILD_TOOL_ROOT失败/跳过，使用标准工具环境后246项全过；保留最终完整XML。
- 独立静态审查覆盖源调度、镜头/后处理、伤害/释放和缓存路径。发现CUTIN双合成、零伤Miss消失和大帧锚点错误已补回归并修复，最终未发现阻断问题。

## 实际操作与证据

在主工程打开 Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity，进入Play，点击右上“开始试玩”。角色的位移、镜头、特效、声音与已接受的战斗事件播放。查看拉斯S2接近与回位、三名S3、史莱姆攻击、护盾受击、协攻累计和多目标飘字；停止Play或重开场景应恢复作者布局。

真实原片与分析帧在 .artifacts/bilibili-reference、.artifacts/damage-reference；本轮游戏截图在 .artifacts/battle-rules。原片3:14.887到3:15.137所示累计数字1234转1362、1689、2920，配合11Hits用于判定累计与字形表现。当前模型只按源HIT时点均分权威总量，不能据视频11Hits虚构11个STG命中。

矩阵截帧展示独立演出源播放器，保留“开始试玩”是因为未进入规则对局；不能将此截图用作战斗权威流程证明。实际对局由同一套PlayMode的BattleScenePreviewTests覆盖，包含伤害、回合、音效和三角色S3。

相机依据原始camera骨骼而非随机正弦通用抖动；源跃起/落地、固定朝向、CFX生命周期、map可见/隐藏取景各自明确。[详细约定](../development/battle-source-presentation.md)与[源数据调查](2026-11-13-camera-source-audit.md)供新角色沿用。

补充矩阵单独重验1/1通过（.artifacts/battle-rules-source-matrix.*），生成同帧世界/合成图。图形核对：source-playback 中拉斯S2左右各有 blur-world 原世界帧和 blur 合成帧；已目检世界采集不含HUD、世界朝向正常、水平模糊后HUD清晰。安S3的Cut-in保持独立，梅S3隐藏地图广角使用原背景。新矩阵通过只说明当前样本的播放/恢复和渲染路径，不证明原引擎模糊倍率逐帧相同。

## 限制

本轮的Unity图形/自动测试是编辑器证据，未构建Windows/Android Player、未部署生产更新、未重新验证专用MySQL。未宣称全部E7角色或所有技能逐帧等同原作。有限LAYOUT坐标、MOVE原插值、SHAKE倍率字段、SINE原公式和完整遮罩、部分CFX排序与命中偏移仍需源证据/同版本录像；影响具体新角色时必须诊断并补测试。

C盘资料已释放19.05GiB，详见[迁移与启动规则](../development/local-storage.md)。正在使用的Unity安装留在C盘，不把“项目输出迁D”说成“系统盘从此零写入”。原有用户damage_0.png.meta压缩修改保留且不纳入本轮提交。


## 推送后Windows恢复回归

首个战斗提交2c83e61的CI连续两次在同版本修复候选取消后的旧目录恢复（10.repair→10）返回Access denied。匿名API限流后，通过本机既有Git凭据只读取得作业日志，凭据未写文件。具体外部占用进程未确认。

新增真实Windows共享锁用例在本机复现同一第二步恢复失败，再修复：仅回滚rename遇Windows错误5/32/33时最多等待约1秒（若两步均需等待，总计约2秒），不受已取消安装token打断。持续失败保留原安装/激活异常与恢复异常，不删除候选或隔离备份。测试另覆盖第一步候选目录锁、持续隔离锁后下一次修复，以及目的目录冲突。

集成后完整Foundation再次通过，日志 battle-rules-foundation-final.log；Unity全EditMode再次246/246通过，日志 battle-rules-editmode-final.*。Shared netstandard2.1构建0警告0错误。仅追加测试与共享更新器恢复保护；战斗生产代码未再次修改，图形PlayMode结果仍使用上节已运行的版本。

原有状态机边界：若第一步回滚永久失败，候选仍可能占据同版本目录，GetActiveRelease仅验签/哈希，不能作为消费方校验成功的证明。现行StartupContent恢复会再次LoadAsync预检，调用方不得绕过该检查。恢复失败后需要保留目录并处理异常，不能宣称取消后任何文件占用条件下活动目录必然已回原样。此补丁不重构活动版本状态机。

ContentUpdater属于AOT，此保护需要重新构建母包后才能交付旧客户端。本轮未构建Player或部署线上更新。
