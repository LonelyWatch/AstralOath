# 主页资料、公共频道和召唤复核

本记录当前为R44候选收尾，尚未签名分发，不能以此称实包完成。当前本机Feed仍43，其文字视觉被用户否定的事实保留。正式入口、源提交、包SHA和Native结果将在实际执行后追加。

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

主工作区已有字体资源改动保留不提交；Unity镜像在生成/测试中会序列化动态字形缓存。构建将冻结镜像实际字体输出和Root源的各自SHA，不谎称二者逐字节相同；原始字体来源、GUID和主工作区用户文件不替换，不能把Editor生成缓存混入用户源码提交。

服务端完整677/677（Social32），后端输入124项/发布18文件的SHA已核。三个后端审查缺陷、Chat客户端游标、资料取消/Socket/Me、布局和慢/不确定偏好回执均有真实失败日志，路径统一为`D:/UnityProjects/AstralOath/.artifacts/home-profile-chat-review/{server-tests,client-tests}`。首轮联合Editor53/53、Play14/14与Chat修后17/17有效；最终受影响联合结果待补，不将旧绿色外推成新源完整通过。

R37/app0.1.27基线`7445597f1ef645db8ef946c4964503fc`兼容检查首次拒绝Comparer.Create/MediaType getter，保留失败；改为热更内比较器和等价媒体类型核验后实际通过，未改AOT、Packages或ProjectSettings。后续签名构建仍须再检查。

## 声音、动态与限制

所有按钮/提示沿用Common原素材与既有音频淡出规则；当前环境无法实际试听，人工听感列为未验收。音频文件存在、事件绑定、波形或脚本断言均不替代试听。Editor动态图回归验证idle/隐藏暂停/释放；准备中的Native驱动将保存25个资料/聊天/导航状态及12帧召唤idle和实际RT尺寸，静态图不冒称完整动画节奏还原。

没有录到的小卡子页明确不在范围。没有好友/骑士团/Rank/战绩权威业务，不伪造数值或徽章；通知偏好可保存，但5星英雄/神器/骑士团宣传缺广播事件源，不能称这些广播已打通。独立切频道弹窗未在新片展开，项目复用已确认收藏输入布局。当前无Android实机、跨网络多Windows进程或人工试听结论。

## 文件与构建位置

主要源码：`Assets/Game/Modules/PlayerProfile`、`ChannelChat`；Home的Header/菜单Binder与生成器；FoundationDemo的`StartupNavigation.Social*`、`SocialPanel`、FeatureNotice View/Binder及QA驱动；服务端`Server/Host/Social`、Entry昵称认领和018/019/020迁移。原素材来源分别在模块`Res/*source.json`，生成入口`ArenaUiBuilder.GenerateSocialIntegration`，导入器`Tools/Profile`、`Tools/Chat`和`Tools/Arena/ImportFeatureNotice.py`。

Root保存用户已有361项状态且不提交这些既有改动。`Tools/LocalPlay/Ensure-R44GameService.ps1`只恢复已激活新服；初次切换须真实备份、仅向隔离schema恢复并核表行数/校验、再迁移并切换核准R38，未知进程和主库恢复均拒绝。当前仅隔离QA服已迁移，主服尚未替换。完整逐文件绝对位置、最终源、签名包、实际传输与Native证据在执行后生成清单。
