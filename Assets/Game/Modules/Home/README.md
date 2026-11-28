# Home

11/28主页原素材导航、玩家摘要、未完成灰入口已通过联合Editor中的`HomeNavigationPrefabTests`2项；该组53/53，Play14/14中涵盖独立弹窗父子生命周期。Foundation回调打开资料、召唤、现有模拟竞技场和背包；资料昵称回执即时更新左上卡，无权威Rank/经验不写参考账号数值。更多顶部背包与主入口保持可用，所有未来格子/音乐灰态可点并开公共提示。F11快捷键保留，原版没有的可见大型F11调试板默认隐藏。Native与签名交付另验，自动断言不等于原版视觉通过。[本轮记录](../../../../docs/development/home-profile-chat-reference.md)

11/28主页资料/公共频道接入进行中：`HomeWindowBinder`保留原构造，新增`ConfigureNavigation(profile,summon,unavailable)`，前两者为`Func<CancellationToken,Task>`，未完提示为`Func<string,CancellationToken,Task>`。`ApplyPlayerHeader(displayName,avatar,frame=null,rank=null,experience=null)`仅接受组合层已加载权威值：昵称/头像立即刷新，空框回原基本金环，未知Rank显示「—」，无经验数据隐藏填充与百分比，不造参考账号Rank4/29.3%。Home不引用PlayerProfile程序集。区分左上账号详情与英雄详情，左上账号块是独立可点区域。

主右侧恢复Episode/支线/战斗/竞技场/召唤/商店六个原布局入口，竞技场接现有模拟战、召唤接现有召唤，其他项置暗但保持可点。展开菜单的三列四行图标、四个底部入口与既有音乐控件也成为真实GameButton；英雄/竞技场/召唤和顶栏背包保留可用，其余以稳定key `home.feature.<id>`回调统一未完提示。键与中文标题集中`HomeFeatureLabels`，不改Arena本地化配置。`HomeMoreMenuBinder.ConfigureNavigation(arena,unavailable,bag=null)`补齐回调；旧构造保留。未完入口本身Silent，由公共提示播放一次反馈，避免叠加两次点击声。原录像灰暗表示该账号未解锁，本项目灰暗表示功能尚未完成；不复制原作Rank/剧情门槛冒充项目进度。

原参考没有大型黑底「全屏(F11)」调试控制，因此主界面默认隐藏该可见按钮；既有displayMode/displayModeLabel绑定仍保留，DesktopDisplayMode自身的F11键盘功能不改，不新增AOT组件。不会用调试板遮挡顶栏币栏，余额由原权威wallet/Mail组合层继续绑定，不绘制录屏固定余额。

所有`DrawHomeTopIcon(chat)`嵌套同一`ChannelChat/Res/Prefab/ChatEntry.prefab`，契约`button`；登录组合层统一扫描绑定，不向Home引入ChannelChat引用。MailEntry继续原嵌套。生成入口`ArenaUiBuilder.BuildHomeNavigationAssets()`只保存HomeWindow/HomeMoreMenu，需先有ChatEntry；主生成器也复用同一方法组。基础头像环与增益符号由`Tools/Profile/ImportHomeHeaderAssets.py`仅取两张原图，源/输出哈希在`Res/Textures/Header/header-source.json`，没有默认高阶竞技场奖励框或增益次数。

参考已实际查看11/28录屏00:00.5主页和01:10完整抽屉，解析只读`lobby_ui_renew.csb` / `quick_menu_renew.csb` / `top_bar.csb`，源JSON与图录在`.artifacts/home-profile-chat-review/source`及`index.html`。01:07实际是宠物门槛横板，不能把该帧当作抽屉完整状态。当前项目既有音乐条保留置暗入口，新录像01:10未显示该条；它不是本轮原样式完全一致的证据。主背景/角色/装饰仍是当前项目已授权皮肤，不将不同皮肤画面当同源像素比较。未完入口和资料卡在窄窗、1080/4K、热更包中的实际表现尚待统一生成与原生验收；源码不当已分发。

`HomeNavigationPrefabTests`两项覆盖六入口、未知数据、ChatEntry真实嵌套和未完按钮可点。生成器改前实际持久化YAML契约RED（非Unity执行）在`.artifacts/home-profile-chat-review/client-tests/home-static-red.json`，原Home缺10键、More缺25键，进程退出1并保留旧预制哈希。协调者随后实际Unity RED位于`client-tests/panels-editor.xml`：整组17项15过2失败，且两失败仅Home缺feature.episode、More缺feature.guide；不可把该组称全绿。生成后的GREEN、回调行为及视听请以协调者后续实际回执为准。

11/28邮件角标修订后，主页、更多菜单及装备入口的顶栏继续嵌套同一个MailEntry预制，随生成器重建保存；布局与业务无新增分支。角标原字体/切片与本轮实际验证见[邮件UI复核](../../../../docs/delivery/2026-11-28-mail-ui-followup.md)。

内容43再次保存上述嵌套入口，包含原静态数字图集的初始化特殊字形；TMP描边程序由Foundation显式SVC收集。Home本身没有新增运行时逻辑，实际包效果按43单独复核。

邮件领取后的外部快照校准使用 `HeroInventoryBinder.RefreshExternalAsync`，原页面及 Binder 保持存活；英雄、装备、神器重新读取权威库存，不调用 `OpenAsync` 或重建页面。已有页签、搜索、筛选、排序、实例选择及合法滚动位置保留，原实例不再存在时由原列表逻辑选择可用项。已失效的装备确认报价取消并提示冲突，候选选择仍保留，下一次提交使用新版本。取消沿用调用方与当前页面的联合作用域，旧页面读回不能更新新开页面。

本项受控实测：`MailExternalStateTests` 三项 3 红→3 绿、无跳过，分别覆盖英雄、装备及神器在新增一件库存后的状态；证据 `.artifacts/mail-system-review/tests/external-state-valid-position-red.xml` 与 `external-state-green.xml`。首轮英雄输入超出网格滚动上限，已改用合法 40 单位重验；不将 UGUI 正常夹界记录为产品缺陷。此处为 Editor 数据/状态验收，不替代服务端、原生包或音效验收。

已打开的普通装备强化页同样由当前 Binder 原地刷新：新增材料和金币保留合法已选数量，材料被外部减少时只收缩超出余额的选量。装备目标自身发生变化时丢弃旧报价并提示冲突；已发出而尚无确定回执的强化请求继续保留原 requestId、expectedVersion 与完整配方，外部读回不能重写重试。`MailExternalStateEnhancementTests` 实际 1 红（余额未刷新）/1 保护通过→2 绿；最终与上述三个状态用例联合 5/5、0 跳过，Unity 退出 0，证据 `.artifacts/mail-system-review/tests/external-enhancement-red.xml` 与 `external-five-green.xml`。此处新增材料与金币使用受控快照，真实邮件服务领取和原生结果另由最终集成验收确认。

2026-11-26装备复核：英雄白色最终面板右侧显示当前六件装备、套装及神器形成的橙色总增加，替换/卸装/切换角色均从当前快照重算；零值隐藏。神器读失败或模板未知时攻/血总增加隐藏，白色攻/血仍保留既有装备-only回退，不能视为完整已确认值。管理候选卡固定480×864、已穿详情固定585×900设计单位；整页安全适配统一等比，不分别拉伸卡片。通用规则已同步。

管理换装提交前横板改为项目CP的前/后值与增减，保留转移、替换及套装说明；神器星级/等级必须来自已加载实例，未知显示「—」。计算与原版绝对值的区别见[CP规则](../../../../docs/development/combat-power.md)。普通强化副属性与分数相对页面打开时累计显示增加，已知强化次数标橙色(N)，旧未知不猜；原曲线横扫1.0333秒后及窗口关闭均清理。普通/神器强化入口复用Common样式，自动登记恢复原素材实黑板与金棕选中。

当前源码25项Edit、11项联合图形/真实强化及另5项管理实服回归通过；原生和签名交付状态见[本轮验收](../../../../docs/delivery/2026-11-26-equipment-followup.md)，不以Editor结论代替已分发。

普通强化已随本机Windows0.1.26/R36交付，1920×1080与3840×2160原生真实选材、仅经验、+3/+15及返回通过；完整原特效与实际听感仍有限制。[包、原版/前后对照及缺口](../../../../docs/delivery/2026-11-26-equipment-enhancement.md)

普通装备新增源素材强化页 `EquipmentEnhancement` / `EquipmentEnhancementBinder`，由装备协调器从背包、已穿详情和单槽以独立Scope打开。预览、数量/自动登记、权威回执、仅经验、橙/红角标与返回分开处理，未决请求跨窗口保留，关闭取消网络并渐出声音。[规则、来源、实际检查及精度边界](../../../../docs/development/equipment-enhancement.md)。魔控灰色入口、白绿和旧GM曲线不当作已实现强化。编辑入口为同名生成器与 `Tools/Arena/ImportEquipmentEnhancementArt.py`。

11 月 24–25 日按授权原素材重新复核普通装备、管理页、神器与英雄阴影。普通已穿详情恢复品质纹理和原框完整收口；管理使用原蓝色面板、七列目录、六槽、筛选框和横幅确认；神器移除覆盖源梯度的实色层并恢复原卡框、卷草、进度槽和竖饰。共享横卡字号和长文滚动分别核验。英雄阴影沿用源图、源节点范围及引擎旋转转换，搜索恢复黑金框和白输入，蓝底筛选恢复新版原图。[来源、图录与未完成边界](../../../../docs/development/ui-material-reference.md)

源材质中的淡白条与黑阴影不能直接沿用 Cocos 的 alpha 到 Unity Linear：管理页在表现层换算 tint/coverage；英雄阴影的用途副本将源节点透明度合入 alpha 后按黑色透射率转换，源 RGB 与共享原图保持。记录位于 Inventory 的 `material-restoration-source.json`、Equipment/Management 与 ArtifactRestore 各自的来源清单。

装备管理补齐主、副属性选择，套装与属性组合筛选使用实际库存。左右面板持续显示当前与候选数值，候选选择只更新本地预览，确认时按库存版本提交原有装备事务。取消保持库存版本不变，提交后留在管理页。英雄搜索和筛选使用独立弹层，已穿普通装备和神器详情单独控制文字区、操作区和宽窄屏留白。[复核依据和验收](../../../../docs/delivery/2026-11-23-equipment-battle-review.md)

主页复用召唤首屏资源。伊赛丽亚技能播放器现由 Summon 管理，入口地址继续使用 `summon/iseria-skill`。[召唤模块](../Summon/README.md)

大厅背景、常用入口和部分背包图标在 Windows 上按 PNG 的原尺寸和颜色导入。小尺寸源图的可用细节仍由原图决定。[签名包像素对照](../../../../docs/delivery/2026-11-22-client-image-quality.md)

英雄头像、已穿神器及神器清单以图框底部水平中点为星排中心。物品复用时按实际可见星数重新布局，清除上一个物品的星数和偏移。[星排实机对照](../../../../docs/delivery/2026-11-22-centered-stars.md)

大厅动态壁纸的纹理尺寸跟随实际屏幕像素，缩放窗口时保留角色取景。[大厅清晰度对照](../../../../docs/delivery/2026-11-22-lobby-clarity.md)

快捷菜单、打开背包、英雄入场和页面返回使用录屏对应的声音。普通装备按六个槽位发声，神器、解除与锁独立取样，配戴音由服务端成功回包触发。[操作时间与原音依据](../../../../docs/delivery/2026-11-22-inventory-audio.md)

英雄列表与竞技场选人目录通过同一滚动组件播放跨卡声。[英雄页录屏与原声匹配](../../../../docs/development/roster-scroll-audio.md)

英雄详情六个普通装备槽、背包英雄行和管理页的当前／预览均嵌套同一个 EquipmentItemCard。绑定读取服务端快照里的品质、物品等级、强化等级、套装和主属性。英雄行保留卡底主属性，方形装备位收起这一行。卸下会清除物品数据，恢复原槽位符号。

神器背包以两列显示 ArtifactInventoryCard；英雄配戴页复用横卡并排成单列。横卡分别显示六边形徽章、横幅、星数、所属职业、技能等级、突破进度及有值时的强化红标和佩戴者。背包容量与配戴页都读取同一份神器快照；点击容量显示 400 格上限提示。英雄已穿详情和全页配戴按不同入口管理返回。录屏时间点、项目用值与待对照页面见[界面参考](../../../../docs/development/equipment-ui-reference.md#已穿装备与神器入口复核)。

单槽「配戴」和已穿装备详情使用 `Res/Materials/EquipmentDetailText.mat` 控制小字号笔画边缘。物品名、品质和装备分数采用常规字重，主属性及操作按钮加粗，套装行只强调套装名。同页装备卡的金色等级沿用卡片自己的材质。[原帧与客户端文字对照](../../../../docs/development/equipment-ui-reference.md#配戴页文字与物品等级的清晰度)

背包和管理的单件详情、英雄单槽的左右比较及已穿详情都显示装备描述和金色装备分数。描述由物品模板决定，分数由共享规则按当前主副属性计算。切换候选时只更新对应物品，不额外请求库存。[单槽录屏与复拍](../../../../docs/development/equipment-ui-reference.md)

英雄页与竞技场的角色列表在卡片底板上共用一份投影设置。阴影沿上层卡片底缘落到下一张卡片，字号、点击区域和行距由预制及滚动组件管理。[重叠区渲染对照](../../../../docs/delivery/2026-11-21-battle-and-summon-r23.md)

神器背包按原版效果图使用双列列表。每件神器分别使用徽章、横幅与完整竖卡，详情独立显示增强等级、突破、攻击、生命和当前技能。锁定、解锁、出售及强化使用当前选中实例。验收包括连续操作和两种窗口比例。[本轮界面复核](../../../../docs/delivery/2026-11-20-home-and-arena-ui.md)

主页启动场景的本机游戏服务默认为 `http://localhost:7728`。需要切换测试服时，在启动进程前设置 `ASTRAL_FOUNDATION_URL`。

`Scenes/GameHome.unity` 是游戏首场景。开发入口为 Unity 的 **Tools → 游戏主页 → 打开启动场景**。启动入口先登录并选服，校验并加载内容后，还需点击活动标题页进入主页；详见[游戏入口、账号与更新](../../../../docs/development/game-entry.md)。主页按地址 `home/main` 显示 HomeWindow；点竞技场模拟战打开 Arena 模块。旧 FoundationDemo 场景继续用于 Echo 和启动诊断。

`HomeWindow : UIBaseView` 和 `HomeWindowBinder` 分离组件与导航。主页使用原版 `bgpack/lobby_bg3.png` 和夏日夏绿蒂 c5009 的 Spine 3.8.99 立绘，来源见 `Res/Textures/source-manifest.json`。预制体沿安全区锚定，采用高度匹配的 CanvasScaler。退出主页会回收立绘 Camera、RenderTexture 和 Spine 实例。

主页右上「更多」打开快捷浮层，点击「英雄」进入英雄详情及可展开的持有列表。右上「背包」按英雄分行展示等级、星级、六件装备与神器。普通装备和神器分页分别显示账户在当前区服的实例及容量。[账号与英雄背包](../../../../docs/plans/2026-11-17-hero-inventory.md)

更多抽屉、英雄详情和背包按原始布局文件复建。英雄背包使用原版装备、神器和专属装备槽，详情显示由已有成长曲线计算的基础属性。未接入的物品页保留原版空面板。

英雄详情右栏与 RTA 共用焦点、卡片尺寸、间距和右侧裁切。选中第三名或更后的英雄时，上方保留两张前序卡的名字。三人库存滑到末项仍能看到全部三张，最近的卡绘制在前。滚轮、拖拽松手或直接点卡都能切换到对应持有实例，同一角色的多份副本可分别查看。[原版画面及复现](../../../../docs/development/arena-draft.md#角色滑条与引导光复核)

英雄详情的六个普通装备栏和神器栏是预制中的 `EquipmentSlot` 实例，按服务器回执更新物品图及强化角标。英雄、装备及神器在登录时读取，跨页和战斗返回继续复用。列表姓名沿用原布局的常规字重，等级使用原数字和 MAX 图。中央立绘依源骨骼原点与 720 高参考画布构图。[窗口、缩放和数据复核](../../../../docs/delivery/2026-11-21-hero-layout-and-navigation.md)

普通装备背包提供部位筛选、装备等级排序、物品详情及佩戴者选择。英雄详情点空槽进入单槽「配戴」，点已穿槽先显示单件详情，可更换或解除配戴。单槽配戴页并排显示当前与候选装备，候选列表位于右侧。同模板英雄按实例区分。训练服的免费穿脱收到服务端回执后返回英雄页。转移其他英雄已穿物品时显示原佩戴者，库存冲突要求重新选择。同一份装备快照驱动背包、配戴和英雄面板。[装备系统方案](../../../../docs/plans/2026-11-21-equipment-system.md) · [各入口原版画面](../../../../docs/development/equipment-ui-reference.md)

固定面板及六槽点击区域由 `HeroInventory.prefab` 持有，单槽页嵌套 `SingleSlotEquipment.prefab`。装备卡与属性行使用 Common 的 `EquipmentItemCard.prefab` 和 `EquipmentStatRow.prefab`，绑定类在 Home 程序集，各页面传入实例、数值及图标。修改生成器后可单独运行 **Tools → 游戏主页 → 更新英雄背包装备界面**。训练装备的词条、等级、品质和套装以库存实例为准。两把训练武器使用各自的物品图。[背包素材与校验值](Res/Textures/Inventory/source-manifest.json)

「管理装备」初始显示当前与预览两组六槽，右侧按部位浏览。选择一件后显示固定详情卡、替换后的六槽和面板差值，确认写入，取消保留原配装。现阶段每次提交一件，确认窗显示文首项目CP前后值及事务说明。装备分数排序已实现；批量配戴仍需配套事务后接入。普通强化从背包、已穿详情和单槽进入独立页面，范围见本文开头。

业务程序集由 Foundation 热更清单登记。稳定启动契约和立绘桥改动需要新母包。 [操作与验收](../../../../docs/development/arena-lobby.md)

主页入口显示“竞技场”，直接打开模拟战目录。舞台沿用原背景和原版贴纸资源。页面切换交给 `WindowCoordinator`，点击泛光和普通音效由启动 Canvas 负责。

Windows 独立客户端默认可缩放窗口，主页右上角按钮或 F11 切换无边框全屏。切回时恢复本次运行记住的窗口尺寸，Android 和 Unity 编辑器的主页隐藏该入口。

主页根 Canvas 集中配置普通／确认／关闭、返回／取消和禁止触发三类音色。点击细环与粒子由同一反馈池复用。页面在校验失败时可显示 Common 底栏提示。

召唤演出资源类型保存在 Windows 0.1.19 母包已登记的 Home 程序集。概率 UP 的海报、技能画面与返回立绘由 `SummonSkillLoop` 管理。伊赛丽亚立绘先展示 3.25 秒，再淡出并播放 S3。收尾时立绘盖回战场，随后恢复海报及右侧神器。

更多菜单六个主入口按 quick_menu_renew.csb 的右侧锚点、原字号 50 和独立缩放渲染。Episode 使用 0.57，其余五项使用 0.47。每个主标题有右下偏移的低透明装饰字，中文底板按 2026-11-17 22:21 录屏 00:05 的 720 高画面校正。伊赛丽亚 S3 预览的唯一敌方使用原版冰史莱姆模型。
在 1478×720 实机图和去掉左黑边的录屏帧中，六项主标题的可见白字边缘误差为水平 0–3 像素、垂直 0–1 像素。对照图位于 .artifacts/summon-revision/verified/more-links-comparison.png。

英雄背包按账号拥有的实例显示，角色图像与模型由 `HomeHeroArt` 按模板 ID 查找。战斗目录每次扩充后同步生成这份资源，现覆盖 40 名英雄的卡图、骨骼立绘和战斗模型。十名新增英雄的中文获得台词、简介、故事及日语 CV 显示条目随资源同步；召唤可用模板以服务端卡池清单为准。[角色目录验收](../../../../docs/delivery/2026-11-20-roster31-40-assets.md#主页与竞技场目录)

英雄目录更新至 50 人。第 41–50 位通过真实英雄背包及详情立绘测试，账号拥有实例、模板 ID 和对应卡图继续分别管理。[第 41–50 名角色验收](../../../../docs/delivery/2026-11-20-roster41-50-assets.md)

英雄目录覆盖 60 名普通英雄。主页资源生成器从角色目录的 battleSkeletonAddress 选择基础模型；塔玛林尔同目录的偶像骨骼随变身演出载入，英雄详情默认展示普通形态。新增十人的中文台词及日语 CV 与卡面一并登记。 [第 51–60 名角色资源与验收](../../../../docs/delivery/2026-11-20-roster51-60-assets.md)

## 主页右侧渐变复核（11/28）

实际对照用户新录屏00:00.500与 `editor-social-final/01-home.png`，当前主页右侧灰幕过宽、覆盖到中央人物。根因是生成器把未被本次原布局引用的 `renew_glow_quick_2.png`（129×72缩略图）放大到1000×720设计范围，并以右上角pivot(1,1)整张放进屏内。原 `lobby_ui_renew.csb` 右侧接入 `quick_menu_renew.csb`；该布局 `Layer/n_quick_menu/notch_grow2/grow2` 使用完整 `renew_glow_quick.png`（1000×720）、source中心x38.516/y360、anchor(.5,.5)、白色/opacity255。收起时应以屏右为中心参考，只露出461.484源像素；1080设计下可见692.226宽，在1920帧中从x1227.774开始，而旧错误层从x420开始洗灰人物。抽屉是同组左移377后的已有位置，保持其展开布局。

第一步更正 `ArenaUiBuilder.HomeMoreLayout.cs` 的主主页背景helper，复用完整原PNG，修前后由实际Editor截图确认中央洗灰消失；原PNG与GUID保持，抽屉原布局保持。原00:00.500与新图都能看到距右边约39px（1200高原图）/35px（1080高项目图）的竖线菱星，和源x438及收起坐标相符，右侧暗底确实用于收起主页，不能整层删除。录屏角色皮肤和项目背景不同，不把不同底色的截图RGB直接当同场景对照。

第二步针对实际复拍仍偏灰的右侧暗底，引入唯一用途副本 `Res/Textures/Inventory/More/renew_glow_quick_collapsed_linear.png`，仅收起主页引用；展开抽屉仍引用未经修改的 `renew_glow_quick.png`。编辑入口 `Tools/Home/ImportHomeNavigationCoverage.py` 只换算alpha，1000×720尺寸、全部RGB、原PNG/GUID及几何不变，sprite以sRGB RGB、RGBA32无压缩导入。可见范围是461.484源像素；mask精确定义为x<462、0<a<255、R≤G≤B，权重 `(1-smoothstep(76,112,B))*(1-smoothstep(30,48,B-R))`。76/30是已核实蓝灰暗底的亮度/色差上界，连续衰减到112/48以保护亮灰装饰核心和彩色内容，避免硬palette阈值制造轮廓；x462以外完全保留。暗底按灰值 `g=(R+G+B)/(3*255)`，使用与Chat同式 `aLinear=(1-sRGBtoLinear(1-a+a*g))/(1-sRGBtoLinear(g))`，再按权重和原alpha插值。该标定匹配白底参考的中性对比度，不宣称所有彩色背景的混合数学完全等价。

源(350,360)的alpha164→214，白底原sRGB合成114.153/116.725/119.941，旧Linear163.393/163.854/164.499，新Linear116.292/117.216/118.497；(430,544)的alpha226→245，原66.224/74.200/84.835、旧102.500/105.630/110.612、新70.123/75.348/83.220。装饰核心(438,544)的132/138/145/241完整保留，全部720000个RGB保持，317618个像素只增加暗底alpha，没有清除像素。实际查看白底三列、mask和alpha差图未见新的硬轮廓；源相邻RGBA变化≤2/255的边上，校正量相邻变化最大3/255。最大的55/255位于源横装饰本身的RGB35/37/42→101/102/106跳变，不把既有装饰边认作新轮廓。

第一次修改前真实Prefab静态合同8项4过4失败（sprite、原像素尺寸、中心位置、pivot），证明在 `before-contract.json`；首次几何修后Editor图 `after-1080/01-home.png` 保留为第二步修前。第二步先实际执行专用源像素回归RED2/2（164≠214、226≠245），再实现并执行 `Tools/Home/test_home_navigation_coverage.py`，覆盖两个白底数值、原图/抽屉不变、全部RGB/透明/屏外像素不变及连续边界。回执 `coverage-red.log` / `coverage-green.log`，用途导入器 `--evidence-dir .artifacts/home-social-visual-fix/coverage-purpose-01` 生成可打开的 `white-reference-comparison.png`、`coverage-mask.png`、`alpha-difference-x4.png` 和 `coverage-audit.json`。来源清单 `source-manifest.json` 的 `collapsedLobbyNavigation.coverageVariant` 记录hash、公式、mask和两点值。

第二步修改后的Unity重生成/实际复拍及Native尚待协调者执行；静态像素通过不等于Unity或Native视觉通过。重新生成后用 `verify-home-source.py --receipt .artifacts/home-social-visual-fix/after-coverage-contract.json` 检查实际预制引用、坐标及原图完整性，再查看同背景项目修前后和原版右侧。未改任何音效，本项声音不适用。

## 第 61–70 名普通角色

第 61–70 名的普通立绘、战斗模型、头像、选人图及简中语音演员/获得文案均加入详情资料。持有列表的 PlayMode 用例覆盖本批十人与前两批角色，按名字搜索、点击实际详情并检查立绘 RenderTexture。这些模板的持有实例仍由服务端快照决定。 [本批来源与验收](../../../../docs/delivery/2026-11-20-roster61-70-assets.md)

### R47召唤演出追加

既有Home程序集的SummonPresentation承载原星星CFX与粒子；新增内部SummonParticles复用BattleHudParticles的源参数解释逻辑，暂不改Common/AOT或原BattleScene。普通五星英雄/神器为金星，五星光暗英雄为紫星且十连优先级更高。源文件及边界见Summon README；本轮用户免回归，未录制/观看修改后的连续过程。
11/28召唤获得小人改用原character_small末态绝对位置与模型shadow地面锚点；尺寸仅初始化一次，idle时不再按动态mesh.bounds重推脚底。仅SummonPresentation调用受影响，不改共用HeroRosterPortrait默认取景。按用户要求免回归。
