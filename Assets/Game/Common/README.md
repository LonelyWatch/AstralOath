# Common UI

Common 提供通用组件和资源，业务文案、图标数据、事件和生命周期由调用方持有。

- `Res/Prefab/ConfirmButton.prefab` 提供九宫格绿底、按下状态、`GameButton`、`GameText` 和本地点击音效。右键 `UI/通用确认按钮` 可创建预制体实例，通过 `BindingMap` 的 `button` 与 `label` 绑定行为及本地化文本。当前战斗暂停菜单复用该按钮。
- `Res/Prefab/InfoPanel.prefab` 与普通 C# `InfoPanel` / `InfoPanelBinder` 配套。父界面传入预制体、Canvas 容器、来源图标和文本。Binder 复用主面板和关联说明，支持纯效果卡和带魂力、冷却、燃魂、强化分区的主卡；页面超长可滚动，弹板被限制在可用安全区。暂停、关闭或销毁所属界面时关闭并释放。
- `GameLongPress` 统一 EventSystem 鼠标左键和触屏，使用非缩放时钟，默认 0.45 秒。移出、拖离、禁用及失去焦点取消。已消费的长按不会触发 `GameButton` 的点击，短按及键盘提交沿用原生 Button。
- Unity 菜单 `Tools/UI/生成通用预制体` 由同一编辑器入口重建。预制体只挂标准 UI、项目基础组件和 BindingMap。战斗通过场景直接引用这两个预制体。长按说明默认使用 Common 的 EpicSeven TMP 字体。其他内容包采用地址加载时，在该包内登记相应预制体及字体依赖。
- 点击声从源 FMOD `event:/ui/btn_normal` 通过原生 WAV 渲染并裁剪静音，播放不依赖场景 FMOD bank。视图贴图与 WAV 的来源见 [本轮验收](../../../docs/delivery/2026-11-14-battle-inspection-ui.md)。

技能说明主卡保留原图透明阴影并由 InfoPanelSkill 材质在 Unity 线性色彩空间校正实心区域。排版参数从可见边框向内留白。跟随锚点移动只更新位置，安全区或内容变化时才重新测量文字。


使用长按触发 InfoPanelBinder 时传入触发源 GameLongPress。面板在该手势结束、移出、取消或控件禁用时关闭；按住期间面板不截获原触发源的指针。短按仍按普通点击处理。

## 竞技场复用组件

- `ConfirmButton` 和 `CancelButton` 使用竞技场原版绿、棕正常及按下贴图。两者通过 `button` / `label` 绑定文本和行为，分别引用原版确认 `UiAccept.wav` 与取消 `UiCancel.wav`，由 Canvas 级音频源播放。快速关闭面板时音效可继续播放。标准尺寸为 288×91.5，对应原版 1280×720 参考系的 192×61。正常图和按下图使用相同的原版九宫格切边，31.5 号粗体近似原版白色细描边，文字中心略向上。业务界面可按参考图覆盖实例尺寸。
- `ChoicePopup` 提供占满父容器的 `dismiss`、可定位 `body` 和模板 `choiceTemplate`。单列选项可用普通 C# `ChoicePopupBinder`，多列筛选可向同一个 body 添加控件并绑定 dismiss。父界面负责将 body 约束在自己的安全区。
- `PlayerAvatar` 通过遮罩、原版圆框和可选的房主标记及徽章显示头像。调用方提供已加载的 Sprite 和资源生命周期。可通过 `PlayerAvatar.FromInstance` 绑定现有实例。预制体自身不拦截点击。
- 菜单 `Tools/UI/生成竞技场通用预制体` 重建这些 Prefab，右键 `UI` 可直接创建实例。原版贴图、源 CSB、尺寸及校验值见 `Res/Textures/arena-common-source.json`。

## 通用触点和键盘反馈

根 Canvas 配置 `GameUiFeedback`。每个 UI 根使用固定上限的光环池，短点显示原版蓝色外扩环和粒子，拖动取消，图像不参与射线。`GameButton` 可指定普通、确认、取消、返回音色，普通按钮默认继承根 Canvas，旧按钮保留其已配置的音源。选择及输入控件由相同 Canvas 处理。资源来源见 `Res/Textures/ui-feedback-source.json`。

聊天输入底栏可使用 `OnScreenKeyboardInset` 获取原生键盘边距。组件只移动拥有焦点的输入容器，收键盘后还原。Android 11 及以上读 IME Inset，Android 10 从窗口可见区域估算。

`ui_touch_def` 主体由原版 Spine 2.1 加权顶点、颜色和混合次序采样为 25 帧纹理，放在同一 Canvas 的反馈池播放。`GameButton` 的 Sprite Swap 使用预制的正常／按下纹理，附属箭头等图片可共用同一按压状态。`DesktopDisplayMode` 管理 Windows F11、无边框全屏和可缩放窗口，窗口尺寸在本次运行内恢复。
