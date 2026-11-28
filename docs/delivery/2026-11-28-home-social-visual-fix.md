# 主页、提示、聊天与单项奖励复核（R45候选／最终R46）

## 当前交付

本轮已交付本工作区的Windows签名热更**内容R46 / app0.1.27**。启动入口[Play-R46.exe](../../.artifacts/windows-r46/Play-R46.exe)会准备本机服务；[游戏本体](../../.artifacts/windows-r46/client/AstralOath.exe)是复用的原R37母包。不是可脱离本工作区的正式便携安装器，没有重生成相同母包exe/zip或执行强更。客户端产品源`d312f43b55a39676d7a6b3443e402f68de1e000d`，母包基线`7445597f1ef645db8ef946c4964503fc`；后端独立复用R44源`044532c60fcdf53c322c575252cd7ab6df8df16f`，本机助手修复为`92e69506`。源码已推送main，收尾文档另记提交，不改产品源或重签。

[对照图录](../../.artifacts/home-social-visual-fix/gallery.html)含原版／44修前／45候选与Editor／最终46 Native；[逐文件绝对位置与A/M/D](../../.artifacts/home-social-visual-fix/files-index.html)、[最终源与包回执](../../.artifacts/home-social-visual-fix/final-receipt.json)分别列出准确路径、GUID/资源入口、构建与签名SHA、验证日志和未完原因。R45的55条视觉资源路径SHA在46中保持，Editor图仍标45；最终Player图来自46重新实拍。保留用户原361项未提交改动与两个字体，本次构建使用保留工作树，完整输入以Unity实际provenance为准，不能说仅有纯净Git树。

实际通过：HotWindows46、R37兼容、签名与全部108项payload校验；1080与4K各25个Social状态+12帧召唤、14个邮件状态，四轮均exit0；两份46 Social日志的预取警告和`ArenaRequestException`各0。邮件实际GM自发1封、独立API3封、领取4封、历史/原页恢复及金币/行动力/材料增量通过，粒子捕获活跃数76/74（上限80，采样相位不同）。普通Limited启动器成功，Root实际看到无遮挡登录页，不自动登录账号；该页显示App0.1.27/R0是进入业务前状态，46业务激活由前述独立实包日志证明。

实际下载：本轮先43→45为14项182,734,413字节，再45→46为6项3,556,819字节；46后续4K、邮件与普通登录观察阶段新增业务payload均0。两次传输不能相加当直接43→46差量；直接43→46清单理论14项182,737,652字节、复用94项2,170,970,283字节，未实际直测。签名46全部108项共2,353,707,935字节是总内容体积，不是本次全量下载；清单/签名探测不计业务payload。5305 Feed已激活46，33母包文件逐SHA不变。

本机后端已完成45表隔离恢复的行数/EXTENDED CHECKSUM校验和018–020迁移，主库当前20项迁移通过；主库没有恢复或清空，原MySQL PID35128/启动ticks保持。主R44游戏服务和Feed46保留，自有`.6:7728` QA精确清理；其他用户进程未操作。早期审批拒绝、恢复回执误判及45游客并发缺陷均保留历史，后续用户授权后按审查执行，没有绕过拒绝。尚未做整机断电后恢复演练；按用户要求在全部收尾后安排无`/f`的正常关机，实际安排结果单独记录。

## 实包追加复核与最终版本顺延

R45候选实际25个Social状态/12帧实时召唤、14个邮件状态均曾在1080和4K通过；其无声桌面录制后来发现覆盖了其他应用，不能用作奖励连续证据，原文件保留在失败索引且不在主图录默认展示。首次真实YooAsset下载14项182,734,413字节，后续4K及邮件检查计数仍14项。原生奖励峰帧与Editor第11帧(0.366667s)背景ROI平均绝对RGB差0.217/255，不能把未配准的0.550s后续斜纹截图当作材质退化；该数值属于45，不重标46测量。

但实际日志揭示开发游客首次加载的缺陷：`StartupNavigation`并行预读经`ArenaApi.GetSessionTokenAsync`同时创建相同游客会话，服务端昵称事务出现MySQL1213，Social预取收到503；这不是预期未登录拒绝。普通已登录入口复用`GameAccountSession.Token`，后续打开聊天重试使功能脚本通过，不能据此忽略冷启动缺陷。先补真实并发连接回归并修复，再构建不可变内容46作为最终交付；已签名45及其失败/通过证据均保留，不覆盖重签。后续包与Native状态以最终回执为准。

R46追加最小修复：`ArenaApi`按实例串行建立首次游客会话，等待者独立取消，owner/Dispose取消在途与队列，失败后可重试，成功后全部复用既有会话。新`Assets/Game/Tests/PlayMode/ArenaConnectionTests.cs`及原meta保GUID：实际修前6项2通过4失败（8路实际8次sessions），修后联合27/27，其中新并发/取消/销毁6/6。首轮误放EditMode导致编译失败已单列，不充作行为RED；有效RED/GREEN日志分别为`.artifacts/home-social-visual-fix/arena-connection-red46.xml`和`arena-connection-green46.xml`。视觉资源与R45相同，仅会话建立及测试/文档改变；最终兼容构建、签名和原生冷启动已按上节通过。

早期打包阶段：产品源`f3a826419045a2867c8bba6091177f0abf5a9aba`完成HotWindows45及签名，包回执`.artifacts/windows-r45/package-prepared.json`保留。升级先保存3,053,649字节SQL备份，原失败回执`windows-r44/server/results/f45ae35b6b77471ca36c7d5c976301af.json`保留。另一个隔离库真实导入0退出，定位为`Tools/LocalPlay/R44Service.Common.ps1:Invoke-R44CapturedChild`泄露`VoidTaskResult`造成误判；只读SELECT1与512KiB真实子进程均复现。修后3/3、安全24/24，不改SQL转义或用户数据。回归`Tools/LocalPlay/Test-CapturedProcess.ps1`的RED/GREEN在`.artifacts/localplay-captured-process/e318440bffc442c7828459dbd989dece`与`2d6e7d5ed8c4416ea46ad7ce761a7a4d`。失败时旧Host已停、主库未迁移；随后按固定失败回执与备份续作成功，实际45表校验/迁移/新Host回执为`windows-r44/server/results/c45f4ca71ca84ca0b5ab0bef19cd381f.json`，当前主服务已就绪。

视觉阶段修正用户对44图录的五处具体反馈及公告箭头比例，实际完成21/21 EditMode、32/32 PlayMode、1080/4K各15张受控Editor图和源时钟奖励连续帧；这些证据只证明各自阶段，最终46的包、激活、Native与下载另见上节，不以Editor替代Player。

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

共享奖励核心及最终46构建都实际通过R37兼容检查，母包`7445597f1ef645db8ef946c4964503fc`。受控Editor两次最终15状态各1/1，奖励执行器exit0产生65个30fps源时钟帧及12关键状态，无HTTP、账号、发奖或音频播放。原生诊断等待真实粒子启用后拍第10状态，最终46两尺寸均核`singleRewardParticleCount>0`通过；Editor连续过程、Native静态与声音分别下结论。

后端没有运行时源码变化：继续复用R44源`044532c6`，124项输入和18个发布文件逐SHA保持；不无故重跑677项后端测试，也不将历史677当新增显示通过。最终签名内容46继续使用app0.1.27/R37母包；客户端源与复用后端源分开记录。

## 声音与未完成边界

本轮单项奖励沿用既有原`SingleReward`声音及通用0.20秒取消淡出，未新增替代声音。环境无可核实播放输出，**实际听感未验收**；文件、绑定、曲线和静默视频不替代试听。Home灰层音效不适用。Editor源时钟65帧/12关键状态与衍生30fps无声视频有效；Native粒子静帧与活跃计数通过，但连续桌面录像因其他应用遮挡不具代表性，**Native连续奖励时序未验收**，后续最小补证是Player自身帧缓冲连续捕获，不能再拿错误桌面录像代替。4K指实际游戏帧缓冲，不指物理面板。

仍有约2px边饰线位与局部对比、收藏文案换行和字形、QA账号/人物皮肤差异；无满频道自动回退、Rank/好友/竞技场权威数据尚未提供时不捏造状态。Common旧字体传递回退是既有债务，两用户字体不改；更换/清理需独立字体及布局验收。各自影响、原因和下一步已在图录/回执逐项列出，不称全画面完全一致。

用户正式版前可废弃旧测试数据的决定已写AGENTS与通用规范；本轮无需无目的清库，但确已按新授权升级主服务和迁移。之前审批拒绝与后续明确授权/真实执行分别保留，不把原拒绝抹掉。关机是用户明确要求，安排正常关机不强制结束其他应用。

## 修改与资源位置

生成入口：`Assets/Game/Editor/ArenaUiBuilder.{FeatureNotice,ChannelChat,HomeMoreLayout,Mail,SingleItemReward}.cs`与主资源地址表。运行时：Mail的`SingleItemReward*`、`MailWindowBinder.cs`；Foundation的`StartupNavigation.MailVerification.cs`只改显式验收入口。资源：Common的FeatureNotice/SingleItemReward及SourceAdditive材质，Home的收起渐变用途副本，ChannelChat遮罩/底/阴影用途图及对应预制。

导入：`Tools/Arena/ImportFeatureNotice.py`、`Tools/Chat/ImportChatAssets.py`、`Tools/Home/ImportHomeNavigationCoverage.py`、`Tools/Mail/ImportSingleItemRewardAssets.py`及原Mail入口。专用回归在`Assets/Game/Tests/EditMode/{FeatureNoticeSourceVisualTests,ChannelChatVisualTests,SingleItemRewardPrefabTests}.cs`；现有MailPrefab测试同步迁移路径。逐文件绝对位置、A/M/D与源提交由最终图录/回执提供；会话前361项改动不入本轮提交。
