# PlayerProfile

11/28追加修订：公开玩家编号改为服务端分配的永久随机十位`uid`（JSON字符串），右上`serverId`绑定显示`UID 1234567890`，点击复制同一公开UID，不回退内部playerId或区服名称。`PlayerProfileApi.FindByUidAsync`接入鉴权`/api/profile/uids/{uid}`，供后续玩家查询使用，本轮未新增好友系统。资料大背景运行时关闭整板按压压暗。界面依赖新021身份初始化；本轮按用户要求不运行客户端回归，由用户验收新包。

玩家账号资料独立模块，普通 `PlayerProfile : UIBaseView` / `PlayerProfileBinder`，预制只挂 `BindingMap` 和基础控件。本人和他人共用 `profile/window`；Foundation拥有登录级API/Session、资源Scope和窗口组合，Home及聊天通过回调打开，不让Common反向引用业务。

制作依据是用户11/28录屏00:13自己的详情、00:04昵称、00:18问候语，以及只读 `D:/sevenepic/原始资源/wnd/user_profile.csb` / `user_profile_nickname.csb` / `_z_input.csb`。问候语实际使用通用 `_z_input` 布局，`user_profile_nickname_msgbox` 是另一个确认弹板，不能拿来代替。原尺寸帧、逐项缺口和源哈希在 `.artifacts/home-profile-chat-review/index.html` / `reference-review.json`。录制画面写日本服务器、中文UI；具体版本/发布日期/倍速未确认，不能标为国服或当前项目玩法通过。

## 接入

- 程序集 `AstralOath.Client.PlayerProfile` 引用Core/Common/Home、UGUI、TMP；Home只提供已有的英雄展示资源，不拥有本模块业务。Foundation/Editor/Edit与Play测试及HybridCLR加载列表由协调者集成。
- `PlayerProfile.Address = "profile/window"`，预制 `Res/Prefab/PlayerProfile.prefab`。注册类型可用别名 `ProfileView = AstralOath.Client.PlayerProfile.PlayerProfile`，避免与同名命名空间歧义。
- `new PlayerProfileApi(endpoint, bearer, optionalHttpMessageHandler)`，Bearer读取有取消，HTTP20秒与128Ki字符上界。JSON沿用Newtonsoft，错误按Social `{code}`读取，不记录昵称、问候语、令牌或响应正文。
- `new PlayerProfileSession(IPlayerProfileApi)`，每次登录独立持有并退出Dispose；`Self` / `Changed`提供当前本人权威摘要。`ReadAsync(null, token)`读本人，传playerId读指定玩家；`SubmitAsync(subject, values, pageToken)`由Session生成requestId与subject.version，不复用页面临时编辑对象；`RetryPendingAsync`重试原意图。
- `new PlayerProfileBinder(view, openingScope, session, close, unavailable, heroArt, portraitSources, optionalCopyId, optionalRealmName)`；英雄资源由已有IAssetService/登录Scope加载后传入，不重复加载完整资源目录。`OpenAsync(playerId, token)`同一Binder可切玩家，旧读被取消并通过generation阻止迟到回写。`unavailable`接稳定功能key `profile.representative`、`profile.support`、`profile.frame`、`profile.customCard`，由组合层开通用未完成提示。`realmName`将realmId解析成当前区服显示名；编号过长按原栏宽截断，点击该行复制完整playerId。

`BindingScope`关闭解绑所有按钮、Session事件、Canvas动画Tick，销毁独立portrait相机/RT并隐藏Toast/窗口。昵称/问候语弹层由本页WindowCoordinator及独立Scope管理；关闭弹层取消查重/页面等待。每次写入尝试有独立取消token，尚未dispatch时关页会取消获取Bearer及后续POST；已dispatch时由登录Session继续等权威回执，不伪造撤销。不确定结果保留同一requestId、expectedVersion及完整输入，重新开页重试；取消一笔尚未dispatch的重试，不会丢掉此前已dispatch的不确定意图。每次尝试复制冻结请求，dispatch标记不沿用到下一次。4xx清除已知失败意图，版本冲突先重新读取本人；较旧幂等收据完成请求而不回滚较新Self版本。

API取消时将中断的Socket/IO/ObjectDisposed统一映射为OperationCanceledException，非取消Socket作为可恢复IOException；已知HTTP拒绝仍保留原status/code，不把关闭流产生的SocketException直接送到界面。

本人读取的响应必须 `isSelf=true` 且playerId/realmId非空白；指定玩家读取必须与请求ID一致，同样要求完整身份。非法响应在更新Self或发Changed前以IOException拒绝，不能把未知身份当成功资料，也不要求未实现的Rank资料才能读取。

## 行为与权限

本人且 `isSelf && canEdit` 才可改昵称、问候语、两个公开开关；他人没有这些编辑入口。服务端仍独立鉴权。昵称本地预检与服务端对齐：NFC/Trim，纯ASCII英文字母4–12，或至少一汉字且汉字/ASCII字母数字共2–8 Unicode标量；无内部空格和特殊字符。查重结果仅作用于同一次打开、同一个规范化输入，修改输入立即失效，保存仍由服务端最终认领。问候语CRLF转LF、Trim，最多50 Unicode标量，允许内部LF，空串恢复本地化默认，其余控制与坏代理拒绝。输入、名字和问候语均关闭richText。

Rank/赛季/最佳/好友/骑士团没有完整权威资料：不复刻参考账号的Rank4、好友0/20、铜牌V；Rank显示「—」，无战绩显示「未参加」，他人不可见的战绩显示「不公开」，好友「—」、骑士团「未开放」。当前没有辅助英雄选择的权威字段，辅助槽保持空，不把representativeHero冒充辅助英雄。左侧森林和首个可用英雄是展示默认，`representativeConfigured=false`不声称已经保存个性卡配置。

## 原素材与生成

运行 `Tools/ProjectEnvironment.ps1` 初始化后，用项目Python执行 `Tools/Profile/ImportProfileAssets.py`，仅导入28张所需用途PNG；`Res/profile-source.json`记录原/输出哈希、尺寸、源节点透明度与切片。22张PNG字节相同，6张受控派生：主框白边alpha沿用sRGB覆盖率转Linear（原34→4、115→44），连续蓝底与纯黑阴影改用深色透射换算，保留原RGB、渐变、灰边和几何；新增 `ProfileDim` 恢复完整原 `_dim.png` 径向纹理；`RecordShade` 先折入原tint8/14/23及opacity76，再转换深色覆盖率，运行时Image均为白色、不再重复乘tint/opacity。两个标题Divider及记录区竖Divider先乘原节点透明度，再把白像素有效alpha转Linear覆盖率，非白阴影保留有效alpha。森林精确为 `profile/bg/profile_bg13.png`；主框原 `renew_popup_bg_alpha.png` cap(54,54,258,470)→Unity(54,54,54,54)，昵称原框cap(44,44,24,24)→(44,48,48,44)。主框/昵称框Point采样，其余按UI原尺寸无Mipmap、显式RGBA32、保留GUID。未批量导入外部解包，未修改 `D:/sevenepic`。

深色覆盖率沿用本项目Home/Chat的白参考透射方法：`aLinear=(1-L(1-a+a*g))/(1-L(g))`，`g`为原RGB均值、`a`包含源节点透明度，`L`为sRGB→Linear传递函数。主框原蓝底alpha230在原图x183/y60、289、518分别转换为251、249、245；`ProfileDim`中心保留RGB25、alpha141→202。记录黑底63在Image上使用 `1-L(192/255)`，白色下横线仍用原白覆盖率。固定alpha用途图对彩色背景只能近似还原sRGB混合，必须以实际主页背景叠开资料作最终视觉验收，不能拿独立黑Canvas替代。

生成入口 `ArenaUiBuilder.BuildPlayerProfileAssets()`（或Tools→游戏主页→生成玩家资料）；固定主框1470×946.5设计单位，原980×631乘1.5。整组在标准AspectRatioFitter/AuthoredHeightLayout下等比适配；卡片不随窗口分别变长变宽。森林378×558按.95×1.5，portrait方形RawImage由森林RectMask裁切，避免方形RenderTexture拉成窄长人物；独立positionOffset隔离底层英雄页相机。昵称/问候语确认及取消嵌套Common Green/Gold按钮，未另画业务底图。普通页面黑按钮、隐私选中、输入、铅笔、徽章、关闭符号均为原图。三个底部操作按00:13运行时行位，而非CSB中默认隐藏的n_btn位置。

首批候选自行对照失败后按源坐标修正：昵称原PNG为116×116，明显白线在图内约32px处，1.5倍九切后可见边缘自然内缩约48px；原cap正确，错误是把包含透明边的整框600高作为百分比布局基准。现保留原图/cap，标题按CSB的282/331放到top82.83，两个底部按钮top417、bottom514.5，均在可见下白线约544.5之内。问候语按 `_z_input` 的frame556×406→834×609及原位置642/137还原，两按钮原192×61→288×91.5等宽，文字左上对齐并换行。动态人物不在 `user_profile.csb` 中；对比00:13与候选01，采用森林高1.3倍方形RT、top31的录像构图测量，使头部下移、脚由森林窗口裁切；这些数字是当前录像测量，不能标为原CSB人物常数。

第二轮实看 `profile-layout-fixed` 六图，三项几何修正确认成立，但发现右侧记录区域漏源背景/分隔，以及标题分隔成亮白，故仍未作为最终视觉通过。现补 `user_profile.csb` 原节点：`bg` 是原 `_white.png` 黑色/63透明度的单块共享底（537×205，Canvas位置653.8785/289.5、805.5×307.5），并非自造两个纯色框；`line_0` 为原白25的537×1下横线；`bar1_l` 为原 `z_tile_bar.png` 185×4、顺时针90°、白76，cap精确(32,1,1,2)→Unity(32,1,31,1)，不可误用装备管理页的cap5。底下 `_grow_0` 复用原 `_grow_s_white.png`、色8/14/23、透明76、scale30.89/4.73、原旋转-90°作为右区阴影；本次追加修订将tint/opacity和Linear覆盖率折进用途PNG。

两个标题分隔均是原 `_bar_23.png`、原节点opacity127，不能把Image留255直接显示：昵称cap(30,4,14,4)，问候cap(32,4,10,4)，因cap不同保留两个小用途副本 `Divider` / `GreetingDivider`。每像素先算 `原alpha×127/255`，再按sRGB传递函数转线性coverage：星点255→54、细线128→13；运行Image alpha1，不重复乘节点127。记录竖条同法折入76（白点255→18）；纯白下横用 `GammaToLinearSpace(25/255)`。原PNG、现有meta GUID及RGB不动，来源和派生说明均入manifest。

## 验收与未完成

11/28 R46资料底板透出主页人物的追加修订：已实际查看用户原片 `frames/013.000.png` 与 `.artifacts/home-social-visual-fix/native46/runs/social1080/captures/02-profile-self.png`，并将原版和修前冻结到 `.artifacts/identity-social-followup/profile/`。原版外部主页受径向渐变压暗，右侧深蓝卡底仅微透；R46外部与卡内均明显露出主页大立绘。`user_profile.csb` 的 `dim_img` 使用完整 `img/_dim.png`、opacity255，旧生成器却省掉原图，仅画黑色alpha.60；主框源蓝底alpha230直接进入Linear framebuffer，黑白背景的上中下三个红通道对比为83/79/71，远高于Cocos sRGB原意图25。旧导入器只处理白边，未处理蓝底透射。现已修复上述原Dim、蓝底及右区源层的深色覆盖率，实际运行28张最小素材导入，既有资源GUID无变化；未额外画纯色盖板。原片/修前/源哈希与推导限制见 `source-audit.json`。

本轮用户随后明确「不需要回归验证，尽快改好并打包，由用户验收」，覆盖此前等待失败测试/视觉复验的安排。`PlayerProfilePrefabTests` 新增两项视觉契约及原记录底期望已保留，**本轮未执行RED/GREEN或其他新增回归**；本代理未启动Unity/服务，未重新生成预制、未取得修改后Editor/Native截图，也未打包/分发。当前只有源码、源像素换算与资源导入，根协调者负责生成/编译/打包；不能把旧R46图或离线数值当作本轮修后视觉通过。修改后应在真实主页上以1080/4K打开本人/他人资料及问候语层由用户验收，独立黑Canvas不能暴露原透射问题。原录制具体版本、发布日期及倍速仍未确认；此项无新增音效/动画，不涉及试听或连续特效验收。

冻结前独立审阅发现两处取消P2，根已实际RED2/2：`client-tests/profile-cancellation-red.xml` / `.log`。受控Bearer未完成即关编辑层仍POST（期望0、实际1）；关闭Profile响应流后SocketException10004直接逸出。之后才补每次尝试的dispatch边界/everDispatched及API取消映射，并增加不确定重试取消、非取消Socket和已知400拒绝回归。当前等待根统一GREEN；没有连接真实网络、修改账号或启动Unity来伪造这两项证据。

Me响应合同也由根实际RED确认，`client-tests/profile-me-contract-red.xml` 单项失败：原实现接受isSelf=false并静默不更新本人。之后只补最小self/非空白身份校验，回归同时覆盖空playerId、空或空白realmId，不在此处扩张Rank/头像或其他UI功能；等待同组收尾GREEN。

协调者实际运行首批Session/Rules回归RED15/15失败、0跳过，证据 `.artifacts/home-profile-chat-review/profile-tests/red.xml` / `red.log`，之后实现；生产实现后的15/15实际GREEN在 `.artifacts/home-profile-chat-review/client-tests/session-iteration.xml` / `session-iteration.log`。该联合回执整组31项仅23过8失败（其余Chat占位7、FeatureNotice缺预制1是当时预期RED），不能把整组称全绿。首次生成后的 `PlayerProfileApiTests` 两项与 `PlayerProfilePrefabTests` 一项实际通过在 `client-tests/panels-editor.xml`；该联合组17项15过2失败，另两项为旧Home缺按钮的真实RED。`PlayerProfileBinderTests`验证真实预制的迟到切玩家、他人权限、隐私、复制及关闭，在 `client-tests/panels-play.xml` 实际通过；该联合组6项5过1失败，失败为另一模块Summon文字斜度，不能称整组全绿。设置 `ASTRAL_PROFILE_CAPTURE_DIR` 时该Fixture另产本人1080/3840×2160/窄窗、昵称、问候语和他人只读六图，使用真实英雄资源及受控资料；capture-scope.txt明确非真实数据库/Native、目标尺寸不是物理桌面声明、未试听。

初次六图在 `.artifacts/home-profile-chat-review/profile-candidate/`，实际对照发现昵称越框、人物缩全身、问候两按钮宽度和文字对齐问题，因此不是视觉通过证据。修复前已真实RED：`client-tests/profile-layout-red.xml` 联合10项8过2失败（昵称anchor、人物scale），`profile-greeting-red.xml` 单项1失败（问候frame825×594应为834×609）。随后补正生成器，新的三项回归、重生后截图及Native结果待协调者执行并补录；旧候选保留为修改前，不混入最终交付图。声音尚未实际试听，源码与预检不能当实机还原通过。

漏源层与分隔透明度另先增加两个回归，协调者真实RED `client-tests/profile-source-layers-red.xml` 两项均失败：缺 `Original record backing`，以及标题用途PNG maxAlpha255应54。之后才补源层/用途副本；新生成、回归和视听复核仍待协调者，不能用这份源码或像素离线核对代替实机效果通过。

本轮排除：用户11/28进一步明确「角色小卡中，录屏里面不存在的内容暂时不做」。代表/辅助英雄、边框、个性小卡选择子页及他人特有操作没有录到，因此不继续等待补录、不制作这些子页；只保留已看见入口，置暗并交给通用未完成提示，不造临时选择页。原视频未展示改名成功、重名错误、问候语保存重进、隐私对另一账号的效果，这些服务端/持久化行为按项目规则另验，不能写作原作事实。没有录屏对应音效的实际试听；按钮沿用Common反馈，尚不称原声匹配通过。
