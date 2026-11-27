# Common UI

邮件等原版大横幅确认现可嵌套 `Res/Prefab/SourceConfirmation.prefab`，统一 `surface/title/body/cancel/confirm/cancelLabel/confirmLabel` 契约。圆角蓝/金按钮分别嵌套 `Styles/RoundedBlue/ConfirmButton.prefab`、`Styles/RoundedGold/CancelButton.prefab`，保留原节点388×128、.53缩放后的切片端帽与 UiAccept/UiCancel 原声。它们与装备直边样式各自独立，业务不再直接拼底图。编辑入口 `ArenaUiBuilder.SourceMessageBox.cs`，最小导入 `Tools/Arena/ImportMessageBoxButtons.py`；调用者改嵌套文案后须记录 prefab overrides。详情与当前验收边界见[邮件UI复核](../../../docs/delivery/2026-11-28-mail-ui-followup.md)。未修改Common运行时代码，不因新增预制资产自动要求强更。

普通装备强化角标用原 `_upgrade_num_orange` / `_upgrade_num_red` 九宫格纹理，`NotificationBadgeBinder.Set` 接收当前最大强化等级（普通装备 15；神器随突破增加）。+0 隐藏编号，未满橙底、满级红底，通知红点仍使用原通知纹理。复用时重置纹理、颜色和标签；同一等级的上限改变也会重画。生成入口 `ArenaUiBuilder.BuildEquipmentSlotAssets`，来源清单 `Res/Textures/equipment-enhancement-badge-source.json`。[强化方案与验收行为](../../../docs/plans/2026-11-25-equipment-enhancement.md)

`UiAudioPlayback` 为每次二维播放创建独立音源，保留该次播放的音量、音高与混音通道。音源所属的 Canvas、控件或演出关闭时，正在播放的声音按非缩放时间淡出 0.20 秒。重叠点击和新页面的音量各自独立，音效自然结束时立即回收。已接入通用点击、召唤演出与获得语音、战斗合击提示、灵魂燃烧、竞技场门和胜败结算。角色普通日语语音的 `VoiceService` 和 FMOD 原生事件另有各自的播放生命周期。[本轮证据与限制](../../../docs/delivery/2026-11-23-equipment-battle-review.md)

Foundation 通过 `ui/audio/<小写文件名，不含扩展名>` 收集这些音效。每次播放由 Bootstrap 对已安装且与传入对象相同的 AudioClip 单独持有 YooAsset 句柄，播放结束释放。窗口立即释放自身的预制和配置句柄，音效尾声继续引用所需音频包。启动内置音和运行时生成的 AudioClip 使用直接引用。该改动涉及 Common 与 Bootstrap AOT。

原版横幅确认窗使用 `SourceMessageBox` 或 `SourceWideConfirmation`，分别按 `msgbox.csb` 与 `equip_change_popup.csb` 的源图、切片及透明度绘制；源渐变上不再叠实色内层。两张用途副本保留不同 cap，亮白细边单独换算线性覆盖率，来源和哈希见 `Res/Textures/arena-common-source.json`。`SoftModalSurface` 继续供开发 GM 自有弹窗使用，不作为原版业务弹窗的替代。[原素材复核](../../../docs/development/ui-material-reference.md)

`SourceMessageBox`的覆盖率校正图使用Point采样：白色低alpha细线与不透明深色相邻，普通直通alpha双线性插值会在二者交界生成比原线更亮的1px接缝。11/28实际图形复拍中峰值中位约124降至60–86（随底层内容不同），PNG/GUID不改、只修此特定源图的采样，其他UI继续原过滤方式。原帧与修前/修后见本轮邮件复核。

启动 Canvas 保存页面与装备反馈的原音引用，按钮关闭后声音可继续播放，根 Canvas 关闭时才开始淡出。背包和英雄页的返回声与竞技场取消声可分别配置。[原声音轨与验收](../../../docs/delivery/2026-11-22-inventory-audio.md)

战斗技能图标可保持按住时的原亮度。共用角色滑条在玩家输入或惯性带动卡片跨过焦点时播放原版短音，初始化、筛选和代码定位保持安静。[原声匹配及回归](../../../docs/development/roster-scroll-audio.md)

GameButton 的按压覆盖整颗按钮的子图和 TMP 文本。控件已有按下态贴图时，底图使用原图自身的明暗；没有该贴图时，底图随文字、价格和图标同时变暗。这个通用控件位于 AOT 程序集，Windows 旧母包无法通过业务热更替换其实现。

Common 提供通用组件和资源，业务文案、图标数据、事件和生命周期由调用方持有。

2026-11-26–27：装备及神器入口共用 `Res/Prefab/Styles/{Blue,Black,Green,GreenCost,Check}/ConfirmButton.prefab` 和 `Styles/Gold/CancelButton.prefab`，继承现有通用 button/label 契约；金币底、勾框、选中底作为嵌套绑定管理。编辑入口为 `ArenaUiBuilder.CommonInventoryButtons.cs`，源图由 `Tools/Arena/ImportInventoryButtons.py` 最小导入。大横幅或其他原版样式不自动合并为装备蓝按钮，范围见[本轮验收](../../../docs/delivery/2026-11-26-equipment-followup.md)。

强化角标两张源图均为30×30，cap(12,11,3,8)换算为Unity border(12,11,15,11)，不能以显示宽39/44计算右边界。`AuthoredHeightLayout`按宿主高度统一缩放原尺寸组，数字和底板不分别拉伸；`NotificationBadgeBinder`按当前上限选择普通39/满级44宽，通知点恢复31宽。原图坐标补偿保留暗边/阴影留白。

`BitmapNumber`直接以默认UGUI Image绘制原`itxt_num*_b.png`，每字26×34、源比例.57、前缀步长15/数字16，个位→十位→加号的遮盖顺序与原节点层次对应；字位按需创建并复用。隐藏的GameText只保留旧`label.text`读取契约，不重复绘制。早期`EquipmentEnhancementDigits.asset`/生成器仍供该兼容字段使用，不作为最终数字显示路径。两项通用组件只做显示，无业务模块引用或全局事件。它们及Binder变更位于AOT，须R37新母包交付，不能当作兼容R36业务热更。

11张数字字图使用现有`AstralOath.ImportOverride`，显式RGBA32/Uncompressed、26×34、不缩NPOT、不生成mip；保持原PNG及GUID。只写meta无压缩会被通用导入器重新覆盖，`ImportEnhancementDigits.py --check`现同时检查导入配置，`EnhancementDigitImportTests`强制走实际导入后检查Texture2D及目标平台设置。11例有效先红后绿，图形Editor44项通过；不能从旧Compressed配置倒推旧小图实际发生GPU BC失真，Native格式另由开发Player验收。

装备卡左上物品等级使用 `Res/Fonts/EquipmentItemLevelDigits.asset`。这个静态 TMP 字体从项目已有的 EpicSeven.ttf 烘焙 0–9，内含数字图集和带细暗边、短阴影的材质。方形缩略图与网格共用同一份预制。右上红标显示该实例的强化等级。

普通装备网格使用 `EquipmentItemCard.prefab`，包含品质底、装备图、等级、套装、佩戴者、选中框、主属性以及嵌套的强化角标。装备详情、单槽比较使用 `EquipmentStatRow.prefab`，名称靠左，数值靠右。两份预制只挂基础控件与 BindingMap，使用它们的装备 View/Binder 归属 Home 热更程序集。父页面可在嵌套实例上调整尺寸和字级。[原版录屏及复用范围](../../../docs/development/equipment-ui-reference.md)

国服 11 月 21 日录像 01:34 的左侧戒指，品质底可见宽约 139 像素，套装标约 55 像素，佩戴者圆框约 73 像素。R28 把 84×84 物品图与自带透明边的 96×96 品质底拉到同宽，武器遮住了底色和部分边框。网格与方形缩略图现共用一个方形父节点，按底图 96、物品 84、套装标 44、头像 48 的设计尺寸排版。头像使用嵌套的 `PlayerAvatar`，蓝底、圆形裁切和金框随佩戴者一起显隐。

取绿色品质底 Alpha 大于 128 的 7,036 个像素，在 96×96 坐标系查看上层物品 Alpha 小于 16 的位置。R28 铺满的竞技场武器特写仅露出 35 个底纹像素，恢复源尺寸后可露出 674 个。训练武器现按模板分别使用全剑图，两件各露出约 63% 的品质底。实际绘制还包括角标和佩戴者。1478×720 复拍中，套装标可见宽从 25 增至 34 像素，佩戴者金框宽 44 像素；原片按相同高度折算约为 34 和 46 像素。

`EquipmentSlot.prefab` 内置底板、空位符号、装备图和一个嵌套的 `NotificationBadge.prefab`。各界面在自己的预制体中摆放槽位实例并指定底板、占位图和位置，`EquipmentSlotBinder.Set` 绑定当前物品和强化值。空槽只显示占位图，有装备时显示实际装备图，强化等级大于零时角标显示 `+N`。`NotificationBadgeBinder` 也可独立用于无数字红点；重复绑定会清除上一件物品的图和角标。

- `Res/Prefab/ConfirmButton.prefab` 提供九宫格绿底、按下状态、`GameButton`、`GameText` 和本地点击音效。右键 `UI/通用确认按钮` 可创建预制体实例，通过 `BindingMap` 的 `button` 与 `label` 绑定行为及本地化文本。当前战斗暂停菜单复用该按钮。
- `Res/Prefab/InfoPanel.prefab` 与普通 C# `InfoPanel` / `InfoPanelBinder` 配套。父界面传入预制体、Canvas 容器、来源图标和文本。Binder 复用主面板和关联说明，支持纯效果卡和带魂力、冷却、燃魂、强化分区的主卡；页面超长可滚动，弹板被限制在可用安全区。暂停、关闭或销毁所属界面时关闭并释放。
- `GameLongPress` 统一 EventSystem 鼠标左键和触屏，使用非缩放时钟，默认 0.45 秒。移出、拖离、禁用及失去焦点取消。已消费的长按不会触发 `GameButton` 的点击，短按及键盘提交沿用原生 Button。
- Unity 菜单 `Tools/UI/生成通用预制体` 由同一编辑器入口重建。预制体只挂标准 UI、项目基础组件和 BindingMap。战斗通过场景直接引用这两个预制体。长按说明默认使用 Common 的 EpicSeven TMP 字体。其他内容包采用地址加载时，在该包内登记相应预制体及字体依赖。
- 点击声从源 FMOD `event:/ui/btn_normal` 通过原生 WAV 渲染并裁剪静音，播放不依赖场景 FMOD bank。视图贴图与 WAV 的来源见 [本轮验收](../../../docs/delivery/2026-11-14-battle-inspection-ui.md)。

技能说明主卡保留原图透明阴影并由 InfoPanelSkill 材质在 Unity 线性色彩空间校正实心区域。排版参数从可见边框向内留白。跟随锚点移动只更新位置，安全区或内容变化时才重新测量文字。


使用长按触发 InfoPanelBinder 时传入触发源 GameLongPress。面板在该手势结束、移出、取消或控件禁用时关闭；按住期间面板不截获原触发源的指针。短按仍按普通点击处理。

## 竞技场复用组件

- 英雄详情和模拟战目录使用同一个 `FocusedRosterScroll`。它按舞台高度排布 361×112 卡片，常规模式在 720 高参考画面中让实色牌面高 89、相邻中心距 59，焦点上方容纳两张前序卡。焦点卡覆盖邻卡，右边缘延伸出屏幕。改变窗口比例会保留当前滚动位置，鼠标滚轮和拖拽松手吸附到对应的可选行，由业务 Binder 决定如何预览或选中。
- `ConfirmButton` 和 `CancelButton` 使用竞技场原版绿、棕正常及按下贴图。两者通过 `button` / `label` 绑定文本和行为，分别引用原版普通确认声 `UiConfirm.wav` 与取消声 `UiCancel.wav`，由 Canvas 级音频源播放。快速关闭面板时音效可继续播放。标准尺寸为 288×91.5，对应原版 1280×720 参考系的 192×61。正常图和按下图使用相同的原版九宫格切边，31.5 号粗体近似原版白色细描边，文字中心略向上。业务界面可按参考图覆盖实例尺寸。
- `ChoicePopup` 提供占满父容器的 `dismiss`、可定位 `body` 和模板 `choiceTemplate`。单列选项可用普通 C# `ChoicePopupBinder`，多列筛选可向同一个 body 添加控件并绑定 dismiss。父界面负责将 body 约束在自己的安全区。
- `PlayerAvatar` 通过遮罩、原版圆框和可选的房主标记及徽章显示头像。调用方提供已加载的 Sprite 和资源生命周期。可通过 `PlayerAvatar.FromInstance` 绑定现有实例。预制体自身不拦截点击。
- 菜单 `Tools/UI/生成竞技场通用预制体` 重建这些 Prefab，右键 `UI` 可直接创建实例。原版贴图、源 CSB、尺寸及校验值见 `Res/Textures/arena-common-source.json`。

- `GameToast.prefab` 在安全区下方显示已有本地化提示。使用 `GameToast.ForInstance` 绑定嵌套预制体，并让 `GameToastBinder.Show(message, owner)` 持有页面或浮层的 `BindingScope`，最新消息替换前一条并在 2 秒后关闭。底板不拦截点击，音效由触发控件决定。蓝色装饰使用原版 `img/_cm_popnoti_bg.png`，SHA-256 为 `c777f0234957bd5107edacb105cc3f6c93796d099802c9ad50c63dd08c39b897`。

## 通用触点和键盘反馈

根 Canvas 配置 `GameUiFeedback`。普通点击、确认和关闭弹窗用 `UiConfirm.wav`（`event:/ui/btn_normal`），返回和取消用 `UiCancel.wav`（`event:/ui/msgbox/btn_cancel`），受限操作用 `UiBlocked.wav`（`event:/ui/popup/down_neg`）。可见但不可点击的控件按需启用 `GameButton.BlockedFeedback`。需要等输入校验再决定是否播放反馈的按钮可设为 `Sound.Silent`，由 Binder 在得到校验结果后调用 `GameUiFeedback.Play` 播放对应音色。返回图标按返回语义，点击弹窗外的关闭区按关闭语义。各反馈在 Canvas 级音频源播放，关闭窗口时声音正常播完。资源来源见 `Res/Textures/ui-feedback-source.json`。

聊天输入底栏可使用 `OnScreenKeyboardInset` 获取原生键盘边距。组件只移动拥有焦点的输入容器，收键盘后还原。Android 11 及以上读 IME Inset，Android 10 从窗口可见区域估算。

`ui_touch_def` 主体采用原版 Spine 2.1 网格和淡出曲线，按录屏收细外圈。外围粒子采用原版贴图、两组发射参数、色带与叠加混合，环淡出后仍短暂可见。32 帧共用一个图集和一组固定池容量，每次点击有不同粒子方向，拖动取消且图元不参与射线。`GameButton` 的 Sprite Swap 使用预制的正常／按下纹理，附属箭头等图片可共用同一按压状态。`DesktopDisplayMode` 管理 Windows F11、无边框全屏和可缩放窗口，窗口尺寸在本次运行内恢复。

玩家名细阴影共用 `Res/Materials/Resources/AstralOathPlayerNameShadow.mat`，与 EpicSeven TMP 字体及同一图集配套。它使用细黑底阴影，不对全局字体实例改写；新增使用场景应为对应 GameText 指定该字体与材质。

HeaderBackButton 与 TopBarToken 供顶部导航复用。全屏子页由 UIPageLayers 独占显示；透明确认窗可以压在任一页之上，BindBack 跟随窗口的 BindingScope。GameButton 的可选 pressedContent 与按下图同步移动并在释放时复位。
