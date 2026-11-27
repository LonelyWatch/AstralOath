# 主页资料、公共频道和召唤复核

R44已完成签名构建，产品源为`044532c60fcdf53c322c575252cd7ab6df8df16f`，已推送`origin/main`；**尚未激活或分发，Native未验收，不能称任务全部完成**。现用Feed仍43，主服仍R38，其账号数据与MySQL保持原样；43文字视觉被用户否定的事实保留。切服及018–020主库迁移被自动审批拒绝，已向用户提出具体升级确认，尚未收到答复。

- [可打开的最终对照图录](../../.artifacts/home-profile-chat-review/final-gallery.html)：原版、修改前、最终Editor候选及缺失Native分开列出，包含失败候选、已知差异和音频未试听说明。
- [机器可读证据与逐文件绝对位置](../../.artifacts/home-profile-chat-review/evidence-index.json)：R43至产品源455个路径，最终产品提交449项；不包括会话前361项用户改动或后续纯文档收尾。
- [R44包说明：尚未激活](../../.artifacts/windows-r44/请先阅读-尚未激活.txt)、[签名包回执](../../.artifacts/home-profile-chat-review/signed-package44-receipt.json)。当前可运行的仍是[R43入口](../../.artifacts/windows-r43/Play-R43.exe)，不能把`Play-R44.exe`当成已可试玩新内容的入口。

## 范围与原版依据

用户11月28日录像`Screenrecorder-2026-11-28-02-29-26-914.mp4`：主页00:00.5、本人详情00:13、昵称00:04、问候语00:18、公共频道00:32、收藏00:37.5、透明背景00:39.5、通知00:42.5/关键词00:45.5、通用横板01:01.5、更多菜单01:10、七人召唤01:28–01:38。画面为中文UI/日本服务器，确切版本、发布日期和可见倍速未确认。消息/原表情行与三列选择另用用户既有11/14录屏01:40/01:42；世界竞技场与新片公共频道的版本差异单列，不冒称同版。

原片、哈希和已查看关键帧在 [原版图录](../../.artifacts/home-profile-chat-review/index.html)、[参考调查](../development/home-profile-chat-reference.md)。`D:/sevenepic`始终只读，仅最小依赖导入。用户明确本次不做录屏没展示的小卡子功能；代表/辅助英雄、边框、个性卡选择页保留灰入口并统一提示，未造选择页。

## 原版／修改前／修改后

- 邮件确认：原版说明灰180、按钮较细；43实拍255/过粗。共享源已改直接RGB180及Normal400/.06细描边，先预线性化成117灰的失败候选保留。Editor实际180和MailPrefab/Play通过，最终Native仍待本轮包。
- 资料页此前不存在。第一批候选昵称内容贴可见白边/按钮越框、整人缩入森林、问候按钮不等宽，见 [首次候选](../../.artifacts/home-profile-chat-review/profile-candidate/01-self-default.png)、[昵称修前](../../.artifacts/home-profile-chat-review/profile-candidate/02-nickname.png)。三项真实失败后按原CSB内坐标、正确`_z_input`及录像取景修复；再补原记录区共享底、原Grow阴影和各自切片的灰色分隔，见 [当前资料](../../.artifacts/home-profile-chat-review/profile-source-final/01-self-default.png)、[昵称修后](../../.artifacts/home-profile-chat-review/profile-source-final/02-nickname.png)。这些是Editor受控数据图，不是历史Native或真实账号证明。
- 公共聊天此前只有房间成员聊天。新增独立频道1..9999、文字和原表情、同频道历史/NDJSON失效补读、收藏/通知/关键词/透明偏好；名字/正文不解析富文本，昵称头像点击共用只读资料。保存收藏不切当前频道，新登录优先收藏，否则默认1。服务端与登录级Session分别防幂等重放、跨频道迟到和游标跳读。
- 主页此前多个入口无实际操作；现原素材右侧及更多菜单未开发项灰态但可点，统一FeatureNotice原素材横板及反馈。已实现背包、英雄、模拟竞技场和召唤仍连接原功能。大F11调试板隐藏，快捷键保留；Rank/经验/战绩/好友/骑士团没有权威值时不伪造原账号数据。
- 七人页此前480²静态RT，模糊来自中间采样和不播放idle。原SCT解码与项目PNG像素相同，未证实源图统一降质；现七人实时idle、按投影重新绘制源骨骼，隐藏暂停，关页释放，其他列表维持480预算。TMP3.0.7需`<i angle=18>`，原`<i=18>`实际仍0.35；几何回归现0.18。源中文召唤Logo未找到同版授权原图，不以日/韩Logo冒充中文。

## 实际验证与失败保留

最终冻结前联合Editor **66/66**、Play **32/32**、R37兼容检查实际通过，回执`client-tests/final-editor.xml`、`final-play.xml`、`final-compatibility.log`。Play包括正常登录/原导航/GM/房间14项及新资料/聊天/召唤/父子scope14项和邮件4项，未把接口占位当玩法。旧Home测试夹具仅有4库存接口，不认识Mail/Social，其11请求对4请求断言真实失败；现补合法响应、独立补充请求队列与身份校验，原库存/GM/房间精确次数保持。Profile最终20项和API2项、Chat17项及6个原素材预制契约均在最终组通过。

主工作区已有字体资源改动保留不提交；Unity镜像在生成/测试中会序列化动态字形缓存。已分别记录镜像实际字体输出和Root源的SHA于`font-build-output44.json`，镜像生成输出构建前后相同，不谎称它与Root用户文件逐字节相同；原始字体来源、GUID和主工作区用户文件不替换，不把Editor生成缓存混入用户源码提交。

服务端完整677/677（Social32，零跳过），后端输入124项/发布18文件的SHA已核。三个后端审查缺陷、Chat客户端游标、资料取消/Socket/Me、布局和慢/不确定偏好回执均有真实失败日志，路径统一为`D:/UnityProjects/AstralOath/.artifacts/home-profile-chat-review/{server-tests,client-tests}`。首轮53/53与14/14是历史分组；冻结前最终联合66/66、32/32见上述回执，不重复相加。

R37/app0.1.27基线`7445597f1ef645db8ef946c4964503fc`兼容检查首次拒绝Comparer.Create/MediaType getter，保留失败；改为热更内比较器和等价媒体类型核验后实际通过，未改AOT、Packages或ProjectSettings。最终HotWindows构建exit0，签名验证通过，无需强更。

签名后另做一次不联网的Editor画面补证：`CaptureEditorSocial.ControlledFrozenPrefabCapture`实际1/1通过，1920×1080相机RT保存14张Home/More/Chat/提示/偏好图，见`editor-social-final/editor-capture.json`。使用冻结预制和Binder、内存受控API，没有启动新Player、改Feed、迁移主库或伪造账号。临时测试仅放构建镜像并在finally删除cs/meta。它不计入66/32产品回归，更不是Native或跨客户端收发证明。

## 声音、动态与限制

所有按钮/提示沿用Common原素材与既有音频淡出规则；当前环境没有可核实的音频播放输出，人工听感列为未验收，不再把波形或绑定当试听。Editor动态图回归验证idle/隐藏暂停/释放；Native驱动已准备25个资料/聊天/导航状态及12帧召唤idle/实际RT尺寸，但**没有执行**。静态图不冒称完整动画节奏还原。

没有录到的小卡子页明确不在范围。没有好友/骑士团/Rank/战绩权威业务，不伪造数值或徽章；通知偏好可保存，但5星英雄/神器/骑士团宣传缺广播事件源，不能称这些广播已打通。独立切频道弹窗未在新片展开，项目复用已确认收藏输入布局。当前无Android实机、跨网络多Windows进程或人工试听结论。

## 自行对图后仍未完成的内容

- 聊天公告下拉箭头：Editor图为窄V，原片为宽∨，切片/比例仍有差异；保留在本次候选，不称视觉全部通过。后续按同一版本原CSB和录像修正后重新回归与更高版本签名。
- `09-more.png`是捕获helper直接调用MoreBinder，未经过产品Root的`HideArenaWhile`，因此图中有旧右导航叠影。它是捕获组合限制；产品代码已有隐藏路径，但仍缺正常入口Native证明，不能借此认定正常入口画面通过。
- 资料六图、Home及Chat受控图已查看；不同背景角色/账号输入和宽高比不声称逐像素相同。新片无实际公共消息，旧片消息与表情行来自另一版本，差异在图录标明。
- 召唤1080p/4K的实际RT大小、连续12帧、帧间隔和暖缓存下载尚未测量；邮件灰度/按钮字重尚未以44 Native复核。签名与Editor通过不能补齐这些证据。
- 小卡未录到的选择页按用户要求不做；同版中文召唤Logo源图未找到，现有译名/Logo边界继续保留，不用其他语言图冒充。

## 签名构建与未激活原因

构建目录为`D:/UnityProjects/AstralOath/.artifacts/b44/Win64/hot-v44-b96467c198384c5b971c87071bac4391`，签名目录为`D:/UnityProjects/AstralOath/.artifacts/windows-r44/release`，本机包装目录为`.artifacts/windows-r44`。完整108个payload共2,352,487,294字节；相对43理论变化16项274,368,677字节（约261.66MiB）、复用92项。实际下载量未测，不能称实际只下载该数或又全量下载。

manifest SHA-256为`f4528dd9c4df0f14e911afff0adf820bed659c62468e2eb827e86d910d105558`，签名文件为`894e63cd1b6c096e9d44b5cac90c0c4536266280c8396ce0d30473ea5af679da`。复用33个母包文件，其中32个静态文件180,889,783字节、32字节运行标记；游戏exe SHA-256为`8d4c804ff605213a298140be3c172eb407e7f5dc78c5082cdfaabaea7b43ba0e`。不重复生成相同母包zip，R37最小包继续可复用。

自动审批在执行前拒绝了“备份现库、隔离恢复演练、迁移astraloath_test的018–020并将R38切为R44”动作，理由是此前开发/打包授权未明确涵盖数据库迁移和服务中断。仅只读预检实际通过：17个旧迁移、未见新表、旧Host未停止；备份、恢复、主库迁移和切服均未执行。回执`.artifacts/home-profile-chat-review/approval-blocker.json`及`windows-r44/server/results/5ce891a22b674c629bfe4f854b426b13.json`保留，不绕过审核另改Feed或旁路加载。

因此R38 Host PID40884、MySQL PID35128和43 Feed身份保持，Feed manifest与原43逐字节一致。`Play-R44.exe`已构建但未激活，直接打开复制的本体仍访问原固定Feed43，不会显示新系统。现用入口仍为R43。

收尾只关闭本轮自行启动的`.6:7728`隔离Social QA进程，停止脚本按PID/启动时刻/路径/DLL哈希复核后exit0。随后只读复核主Host/MySQL启动时刻与Feed43哈希均保持，隔离PID已退出，见`preserved-services-final.json`；非文档冻结文件无变化、361项已有状态保持，见`final-handoff.json`。未停止MySQL、用户游戏或其他应用。

收到明确切服批准后，须重新核对进程/代码/签名并生成新预检（旧回执15分钟过期），执行备份/隔离恢复核表校验后才迁移切服；随后验Native、正常入口、差量/暖缓存与已知视觉差异，更新图录。若改产品源码，另构建更高内容版本，不覆盖已签名44。用户要求全部完成后关机；当前任务仍有审批及验收未完成，**尚未执行关机**，不把受阻交接称全部完成。

## 文件与构建位置

主要源码：`Assets/Game/Modules/PlayerProfile`、`ChannelChat`；Home的Header/菜单Binder与生成器；FoundationDemo的`StartupNavigation.Social*`、`SocialPanel`、FeatureNotice View/Binder及QA驱动；服务端`Server/Host/Social`、Entry昵称认领和018/019/020迁移。原素材来源分别在模块`Res/*source.json`，生成入口`ArenaUiBuilder.GenerateSocialIntegration`，导入器`Tools/Profile`、`Tools/Chat`和`Tools/Arena/ImportFeatureNotice.py`。

Root保存用户已有361项状态且不提交这些既有改动。`Tools/LocalPlay/Ensure-R44GameService.ps1`只恢复已激活新服；初次切换须真实备份、仅向隔离schema恢复并核表行数/校验、再迁移并切换核准R38，未知进程和主库恢复均拒绝。当前仅隔离QA库已迁移，主服尚未替换。逐文件绝对位置见图录及索引；产品提交后仅补验收文档，不改变44包内产品源。后续工具若因当前HEAD为文档收尾而拒绝，不重置main；须先核对差异仅为列明文档，再以明确收尾提交映射运行，不能将文档HEAD冒充包内源。
