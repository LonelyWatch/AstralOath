# 主页、提示、聊天与单项奖励复核（R45）

追加打包阶段：产品源`f3a826419045a2867c8bba6091177f0abf5a9aba`已完成HotWindows45并签名验签，包回执`.artifacts/windows-r45/package-prepared.json`。实际升级先保存3,053,649字节SQL备份，原失败回执`windows-r44/server/results/f45ae35b6b77471ca36c7d5c976301af.json`保留。经另一个隔离库真实导入0退出，定位为`Tools/LocalPlay/R44Service.Common.ps1:Invoke-R44CapturedChild`泄露`VoidTaskResult`造成误判；只读SELECT1与512KiB真实子进程均复现。修后3/3，原安全24/24；不修改SQL转义或用户数据，不重打已签名客户端。新增回归位于`Tools/LocalPlay/Test-CapturedProcess.ps1`，RED/GREEN在`.artifacts/localplay-captured-process/e318440bffc442c7828459dbd989dece`与`2d6e7d5ed8c4416ea46ad7ce761a7a4d`。当前旧Host已停、MySQL身份保持、主库未迁移；继续恢复演练和升级的结果另行追加。

本轮修正用户对44对照图录提出的五处具体反馈，另收束原已列明的公告箭头比例问题。当前已完成源码、21/21 EditMode、32/32 PlayMode、1080/4K各15张受控Editor图和源时钟奖励连续帧；**签名包、激活、Native与实际下载尚待执行**，不以这些图片替代Player。正式结果将追加于本记录；源/包/文件的最终身份见`.artifacts/home-social-visual-fix/final-receipt.json`。

## 原版、修改前与修改后

- [本轮可打开对照图录](../../.artifacts/home-social-visual-fix/gallery.html)逐栏保留原版、44修前、首次中间候选、最终1080/4K及Native状态。原片为用户11/28主页/聊天录像与11/22邮件录像；版本、发布日期、可见倍速仍未确认，不制造同版事实。主皮肤、测试账号和消息输入不同，不能据此称整页逐像素相同。
- 提示：原片61.5s，旧候选使用错误决策窗底板，两条装饰均拉伸同一小图。改为`msgbox_renewal`专用原底、原_dim及两种九宫格线，按稳定帧高度和位置复建。静态白邮件core透射原0.20785、44前0.52549、45后1080/4K均0.21177；关闭中心归一718.2→718.5/718.25。端部菱形恢复，仍有约2px线位和上条局部对比差异，不称逐像素一致。
- 主页右渐变：旧129×72缩略图被放大且锚点错误，遮到中央人物。恢复1000×720原图及源中心偏移；复拍仍发现Linear合成令灰底偏亮，再做独立连续权重覆盖率用途副本，保留720000个RGB、原图及亮装饰。两点原alpha164/226变214/245，源平滑邻边额外alpha差最大3/255，实际1080/4K未见新切口。白底标定不是任意彩底下数学严格等价；原片也有右暗底，未删除整层。
- 收藏框：原片37.5s，框外形高度原本基本正确。恢复_dim、局部_grow、漏掉的父节点偏移和标题线pivot；范围提示回到框内。原归一Y273.6–749.7，最终1080为273–749、4K为545–1499，保持等比，没有盲目缩框。说明文案未加入尚未实现的满频道自动回退条款，换行及字形与原仍有差异。
- 透明聊天：原片39.5s，旧导入器删除中性黑底alpha，仅留金边，产生薄膜感。恢复完整源底的127 opacity用途变体、正确顶部坐标及原遮罩/阴影；透明时底图可见，普通态仍明显更深，底金角不再错误露出。公告箭头恢复源17×26后旋转90°的宽∨，不先压成窄V。
- 单项奖励：11/22原片55s及54–57s连续取帧，旧蓝条漏接粒子。源`item_get_panel_eff.cfx`明确267ms后播放`item_get_panel_pati1_2`，一次50+30原星粒子；按原五点t/alpha/RGB曲线、寿命与重力/径向/切向加速度实现。原星图RGBA的半透明RGB等于A，按PMA处理；旧64帧底图及材质移动到Common保留GUID。源变差保留，局部随机种子固定用于复核，不声称逐粒子位置与录像相同。

## 共用预制与生命周期

`Common/Res/Prefab/SingleItemReward.prefab`对应普通`SingleItemReward.cs`和`SingleItemRewardBinder.cs`，位于已登记Mail热更程序集，不给Common新增业务引用或AOT组件。资源地址`common/single-item-reward`；Common只放预制和最小用途资源，邮件嵌套同一预制。View/Binder只接图标、数量、类型、名称和累计unscaled时间，267ms之前无粒子，最大1.2秒寿命后归零，重播不叠加，Stop/owner关闭清理；权威领取和发奖仍在原服务端事务。

既有Common字体的传递回退包含FoundationDemo旧目录字体；两字体均有会话前修改，本轮未动。依赖回归核对字体仍来自Common，并禁止新增继承字体图之外的业务素材依赖。初始依赖测试先后误拒绝第三方字体、TMP基础Shader及既有回退，失败日志全部保留；完整核对依赖图后修正测试边界，不为让测试通过改用户字体。

## 实际验证

提示/聊天旧预制实际6/6失败，奖励旧预制4/4失败；修后联合Edit21/21、Play32/32，零跳过。Play包含原正常主页、资料、频道、邮件和窗口生命周期回归。静态Home最终8/8及覆盖率5/5；新Home/Notice/Chat用途图来源、节点opacity和变换公式均写入各模块manifest。

共享奖励核心曾通过R37真实兼容检查，原母包`7445597f1ef645db8ef946c4964503fc`；最终签名构建仍再检查。受控Editor捕获两次最终15状态各1/1，奖励执行器exit0产生65个30fps源时钟帧及12关键状态，明确无HTTP、账号、发奖或音频播放。原生诊断新增等待真实粒子启用后拍第10状态，并核`singleRewardParticleCount>0`，尚未执行的阶段不能标通过。

后端没有运行时源码变化：继续复用R44源`044532c6`，124项输入和18个发布文件逐SHA保持；本轮不无故重跑677项后端测试，也不将历史677当新增显示通过。客户端拟发内容45，继续使用app0.1.27/R37母包，不重复生成相同exe/母包zip；客户端源与复用后端源分开记录。

## 声音与未完成边界

本轮单项奖励沿用既有原`SingleReward`声音及通用0.20秒取消淡出，未新增替代声音。当前环境无可核实播放输出，**实际听感未验收**；文件、绑定、曲线和静默视频不替代试听。Home灰层本身音效不适用。Native、普通入口、实际差量/暖缓存待签名包后执行；4K指相机/游戏帧缓冲，不指物理桌面面板。

用户已明确正式版前不为旧测试数据兼容妥协，可采用更合理方案废弃旧测试数据；已写AGENTS与通用规范。本轮是显示修正，没有清库理由，现用服务/数据尚未操作。之前本机升级被审批拒绝的事实仍保留；待实际执行的新审批结果再更新，禁止旁路。全部任务完成后才执行关机。

## 修改与资源位置

生成入口：`Assets/Game/Editor/ArenaUiBuilder.{FeatureNotice,ChannelChat,HomeMoreLayout,Mail,SingleItemReward}.cs`与主资源地址表。运行时：Mail的`SingleItemReward*`、`MailWindowBinder.cs`；Foundation的`StartupNavigation.MailVerification.cs`只改显式验收入口。资源：Common的FeatureNotice/SingleItemReward及SourceAdditive材质，Home的收起渐变用途副本，ChannelChat遮罩/底/阴影用途图及对应预制。

导入：`Tools/Arena/ImportFeatureNotice.py`、`Tools/Chat/ImportChatAssets.py`、`Tools/Home/ImportHomeNavigationCoverage.py`、`Tools/Mail/ImportSingleItemRewardAssets.py`及原Mail入口。专用回归在`Assets/Game/Tests/EditMode/{FeatureNoticeSourceVisualTests,ChannelChatVisualTests,SingleItemRewardPrefabTests}.cs`；现有MailPrefab测试同步迁移路径。逐文件绝对位置、A/M/D与源提交由最终图录/回执提供；会话前361项改动不入本轮提交。
