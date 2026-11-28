# 公开频道五星召唤播报与只读资料

## 原版行为和实现范围

用户提供的原片 Screenrecorder-2026-11-28-20-01-10-182.mp4 显示简体中文界面。文件名标记 2026-11-28，原版实际发布版本与播放倍率无法从画面确定。原片位于 C:/Users/Admin/Downloads。以下静帧都已实际查看。

- 约 10 秒，19 频道列表中有两条播报。玩家头像、名字和时间位于气泡上方，气泡内英雄用圆形头像、神器用多边形图标；两者显示五星。昵称、英雄名和神器名使用三种文字颜色。原帧为 .artifacts/arena-tower-review/chat/10.jpg。
- 约 11.4 秒点开英雄图标后，黎洁特的角色资料居中显示。随后可打开技能说明。参考 .artifacts/arena-tower-review/chat/hero.jpg。在项目抽取目录中使用已配置的英雄与技能资料，战斗属性和拥有数量不会随点击而改变。
- 约 21 秒点开神器图标后，神圣牺牲显示原画、技能与属性；后续 24–27 秒可查看大图。参考 .artifacts/arena-tower-review/chat/21.jpg。项目实际显示抽中模板自身的名字、原画和基础数值。
- 已对照 D:/sevenepic/原始资源/wnd/chat_node_reward.csb、reward_unit_detail.csb 和 artifact_detail_sub_popup.csb。浅色播报气泡和两个详情框采用对应原图。首次编辑器图上英雄身后的星空不符；复查 reward_unit_detail.csb 后改用其引用的 img/cm_preview_bg.png（408×305，暗红天空与地景），并让背景避开原图框线。R49 当时仅展示基础资料与三个技能。后续从原表补全的刻印资料及覆盖范围见下方 11 月 29 日记录。原片中对象与工程拥有对象不同，截图不用于逐像素判定角色美术差异。
- 修改前公开聊天只有文字、表情两种消息，抽取事务没有生成对应的记录。此轮没有保存旧客户端同状态截图。此功能沿用普通按钮的交互声音，未新增专属音效与粒子；本轮没有独立音频试听证据。

## 文件与接线

- Server/Host/Summon/SummonStore.cs、SummonEndpoints.cs 完成服务端事务写入和提交后通知。Server/Host/Social/ChannelChatStore.cs、SocialContracts.cs 提供公共流游标及模板字段。
- Server/Host/Foundation/Migrations/022_summon_chat.sql 和 025_chat_announcement_kind_width.sql 维护表约束。025 已在同轮的隔离 MySQL 复现 artifact5 超长后加入。既有迁移及检验和均保留。
- Assets/Game/Modules/Summon/Scripts/SummonApi.cs、SummonWindowBinder.cs 在确认抽取时冻结公共频道号。Assets/Game/Modules/FoundationDemo/Scripts/StartupNavigation.cs、StartupNavigation.Social.cs 连接登录会话、普通与世界竞技场的公共频道入口、目录和弹窗生命周期。
- Assets/Game/Modules/ChannelChat/Scripts/ChannelChatModels.cs、ChannelChatSession.cs、ChannelChatBinder.cs 与 ChannelChatLabels.cs 负责接收模板、增量/未读、气泡与可点击图标。
- Assets/Game/Modules/Home/Scripts/HeroInformation.cs、HeroInformationBinder.cs、ArtifactInformation.cs、ArtifactInformationBinder.cs 提供两个只读资料窗。HomeHeroArt.cs 的技能条目复用权威战斗配置，神器复用既有 ArtifactArtCatalog。资料文案在 Assets/Game/Modules/Arena/Configs/arena-zh-hans.json。
- Assets/Game/Editor/ArenaUiBuilder.CatalogInformation.cs、ArenaUiBuilder.Inventory.cs、ArenaUiBuilder.ChannelChat.cs 生成上述预制、技能图录及播报行。ArenaUiBuilder.cs 注册资源地址。Home/Res/Textures/Inventory/_box_r_navy.png、_box_r_detail.png 均来自用户已提供素材，保留切片；同目录 cm_preview_bg.png 是角色资料原底图。
- 生成后的资料为 Assets/Game/Modules/Home/Res/Prefab/{HeroInformation,ArtifactInformation}.prefab、Assets/Game/Modules/Home/Configs/HomeHeroArt.asset 和 Assets/Game/Modules/ChannelChat/Res/Prefab/ChannelChat.prefab。生成入口为 AstralOath.Editor.ArenaUiBuilder.GenerateChannelChat。

## 验收

隔离 MySQL 的 .artifacts/arena-tower-review/chat/broadcast-final.trx 已记录该项的服务端用例以及两项竞技场商店用例，共 3 项通过、0 项失败、0 项跳过。召唤用例的成功条件为固定五星英雄、固定五星神器及三星三个分支，十连重试仅播报一次，第二名已登录玩家在同频道从 HTTP 历史读取顺序正确，其他频道为空，监听可以在提交后获知变化，手工上传奖励消息返回 400。未新增用于生成线上抽取结果的测试入口。

客户端 EditMode 可执行 ChannelChatPrefabTests 和 ChannelChatUnreadTests。PlayMode 可执行 AstralOath.Client.Tests.ChannelSummonInformationTests，在生成最新预制后以 ASTRAL_SUMMON_CHAT_CAPTURE_DIR 指定绝对输出路径。该用例以项目英雄伊赛丽亚和神器星辰之歌建立两个公开频道条目，点击后检查实际预制的名字、英雄技能、神器故事及大图，再以当前受控相机尺寸导出各状态。该夹具使用已知消息，不代表两个真实账号端到端连线。

第一次受控 1080p 图与原片同栏对照为 .artifacts/arena-tower-review/channel-check.jpg。此图看出短历史顶对齐、英雄底图错误且框边被覆盖。修订后的消息布局最小高度取自可见区，短历史以最后一条气泡靠底，历史超过一屏时保留顶部锚点；英雄奖励图标补入原 _hero_s_bg_ally。比对了 112×112 的项目英雄小头像、126×126 的框纹理与来源的 SHA-256，内容分别一致。图标实际显示小于源图尺寸。修订后的 Unity 执行结果、Native 比对及打包版本以本次整体交付记录为准。

22:46 统一镜像的 play-final.xml 记录 ChannelSummonInformationTests 1/1 通过。实际查看 .artifacts/arena-tower-review/chat/revision-compare.jpg（从左至右为原片、首轮、修订）与 .artifacts/arena-tower-review/final/01-channel.png、02-hero.png：最后一条气泡已落在输入区上方，资料页出现原暗红地景和边线，已去除首轮的星空。03-hero-skill.png、04-artifact.png、05-artifact-painting.png 为同一受控 Editor 运行产生的技能、神器资料及神器全图状态。以上是独立相机 1920×1080，运行版本由最终包记录关联。

## 2026-11-29 按手机原片可见区复核

录屏物理帧为 2608×1200，最左 144 列为黑边，游戏画幅 2464×1200。聊天参考 10.0 秒，英雄与第三技能参考 11.4、15、16、18 秒，神器资料参考 19.5、20.5、21、24.5 秒，大图查看及整体旋转参考 26、27.5、28.5 秒。两次取得的奖励为黎洁特和神圣牺牲。账号与角色模板不同带来的文案、原画、附加效果内容差异单独记录。录屏内未见人物形态切换，客户端显示抽中模板的通常形态。原片版本、发布日期和播放倍速未标明。原片静帧、CSB 节点摘要与 R49 两栏裁图在 .artifacts/arena-tower-review/chat/strict/。

测量均用录屏的左上角作原点。不同宽高比下，聊天固定在右边缘，详情窗以游戏可见区中心 x=1376 投影；以下为原片实测与原始节点依据。

- 聊天气泡的浅色区域横向约 x=1785–2566。在 x=2450 处，10 秒的两条气泡分别覆盖 y=755–844 和 947–1037。R49 的 1080p 对应同列浅色分别在 y=690–763 与 858–931，比投影后的参考矮约 7–9 像素。现调大气泡、图标及加粗正文，同时增加两行间距。
- 英雄资料可见框约 x=688–2058、y=85–1110。reward_unit_detail.csb 中主体为 816×610，外框 890×683，普通技能中心横距 76。替换首版通用深色边框，按图内外边缘重做背景、边框、介绍、原橙色等级数字和三个技能。
- 原片英雄橙色等级文字在 x=741–922、y=331–369；刻印锁定文字位于 y=545–645，三个技能高亮轮廓在 y=701–821。角色初始刻印来自 character_player.db 的释放类型、站位及自身刻印字段，以 devotion_skill.db 对应每级值。例如 c1024 全四位攻击力，B/A/S/SS/SSS 从 4% 增至 12%；自身为效果命中。70 个已实装普通英雄都有单独的数据，不向战斗应用任何锁定值。
- 神器面板约 x=1056–1695、y=51–1010。卡图约 x=1091–1436、y=85–634。R49 面板居中，顶边设计位置折算到参考高度约 122，且用单一深蓝底。现将中心抬高 65 个 1080p 设计单位，使用 _box_equip_0_linear 原渐变框、arti_frame_box、arti_frame_deco_1、职业图标、五个 arti_gauge_bg 和 icon_menu_zoom。artifact_detail_sub_popup.csb 的五个位置间隔 30 源单位。资料页只读，角色职业限制和基础属性取模板。
- 神器技能标签约 x=1090–1376、y=652–712。紧接的说明文字约在 x=1096、y=730 起排并可滚动。大图在 26 秒保留标题、返回、底部操作说明和右下旋转按钮。27.5 秒整页逆时针旋转 90°，正常与旋转态都可缩放、平移、复位。

可复跑的十四项位置核验在 Tools/Chat/CheckSummonLayout.py。PlayMode 的 ChannelSummonInformationTests 先展开真实 ArenaTower 关卡页，在其上依次打开频道与两个资料窗，并将相机视口与每个 RectTransform 的像素边界输出至 ASTRAL_SUMMON_CHAT_CAPTURE_DIR。给脚本传该目录即可读取实拍 PNG 及 bounds.json 并输出参考值、实拍值、按 1200 高度折算的偏差。气泡直接扫描成图浅色边缘，刻印等区域比较实际运行时边界，大图另计精灵保持宽高比后的可见区。

本轮新增位置型刻印七种原片、原银边、白细边 _box_whiteline、旋转图标 icon_menu_auto、原字体等级斜杠与神器等级前缀位于 Assets/Game/Modules/Home/Res/Textures/Inventory/Catalog/。刻印行位于 Assets/Game/Modules/Home/Configs/hero-catalog-imprints.json。两套资料预制及代码在 Home/Res/Prefab/、Home/Scripts/ 和 Assets/Game/Editor/ArenaUiBuilder.CatalogInformation.cs。生成器读取共享战斗配置和 BattleInspectionContent，为每个模板写入该技能的灵魂、冷却、强化和对应效果。神器画廊共用的输入逻辑在 Home/Scripts/ArtifactPaintingGestures.cs。进入时以及返回的按键沿用公共点击声，此轮没有新音效或粒子，未另行试听。

11 月 29 日第二轮在 2464×1200 与 1920×1080 均实际打开六个状态。两个尺寸的频道图标点击与资料页操作测试通过，分别有十四项几何检查在约定容差内。证据在 .artifacts/arena-visual-rework/round2/wide/ 与 desktop/ 下的 PlayMode.xml、channel-layout.json 和原始 PNG。wide 下的六张 *-paired.jpg 按同一游戏可见区展示录像（左）与修改后（右），未将背景替换为录影片段。折算到原图 1200 高，实测如下。

- 两条气泡浅色区的下边缘分别比原图高 4、5 像素，桌面组为 3.9、4.7 像素。头像、昵称行与奖励图位于各自气泡旁；短历史排列在输入区上方。
- 英雄内容框最大边差 8 像素，橙色等级 8.5 像素。刻印站位图与查看按钮分别为 7.1、12 像素，首个技能图标为 5.6 像素。技能弹窗底边贴近原片，其高度按当前角色的技能说明与强化行数决定。示例角色伊赛丽亚有一张状态卡，录像中黎洁特显示两张。
- 神器卡面最大边差 3.3 像素，技能标签 5.9 像素，说明区左上角偏差为向右 1.3、向下 0.4 像素。未旋转原画的最大边差 1.4 像素，旋转后为 10.3 像素。旋转态右边栏的按钮与说明框最大边差均小于 0.5 像素。
- 重新看图发现神器技能可成长数值在原片呈金色。显示层现只给配置中的数值占位着色，固定数值、叙述与剧情不着色。最终源代码同步后，在 .artifacts/arena-visual-rework/approved-source/wide/ 与 desktop/ 重新拍摄，两个尺寸的用例和各十四项几何均通过。实际查看 artifact-growth-color.png，星辰之歌“50%”呈金色，“2回合”及其余正文为白色。按正文区域扫描，宽屏、桌面分别有 409 与 341 个金色像素，第二轮为 0。最终六组同尺度对照图位于 approved-source/wide/*-paired.jpg。服务端 OriginalDescriptionsResolveForAllEnabledArtifactRanks 用例另行实际通过，覆盖现有 42 套模板各 3 个等级的纯文本格式。
