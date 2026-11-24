# 神器界面原素材复核（2026-11-25）

本轮用户已确认拥有或获授权复用界面与素材；按用户要求，恢复原素材与实机区域关系。此前在原始 `_box_equip_0` 上覆盖不透明 `Detail lining`、正文 viewport 实色，使原框渐变和圆角丢失，是本轮可复现的还原差异，不能再把纯色替代视为原效果验收通过。

## 原片依据

用户文件 `Screenrecorder-2026-11-21-22-31-26-469.mp4`。文件名标记 2026-11-21，发布日期未知；画面未显示倍速。本轮按文件正常时间轴取帧，神器界面没有角色形态切换。原帧 2608×1200，左侧 144 px 黑边不是游戏区域。

- 00:06：配戴候选页，包括完整神器卡、属性/技能说明和右侧横卡。
- 00:21：单幅神器大图、旋转按钮与操作说明底框。
- 00:28.9：神器背包容量弹窗；本项目按用户已定规则保留 400 格，上限点击仅提示，不复制扩容交易。
- 00:37：已穿神器详情的技能页。
- 00:45：同一详情的背景页。
- 01:28：强化选材，包含符文轮、材料横卡、进度条与金色竖分隔装饰。

原帧输出：`.artifacts/ui-material-recheck/artifact/reference/original-{6,21,28.9,37,45,88}.png`。这些是原片取帧。`current/artifact-material-*.png` 是本项目 UGUI 实际渲染，不能倒置两类证据。

## 修正及来源

- 已穿详情移除不透明 lining 与正文底色，保留 ScrollRect、长文 ContentSizeFitter、技能/背景切页归顶。`_box_equip_0` 本身带 31 px 透明周边；按原切片尺度保留圆角，再对可见框与正文定位，不能把它过度缩小来凑内容宽度。
- 原框中心 alpha 为 246/255。直接放进 Unity Linear 混合会使身后白披风明显透到正文区域，和原片的暗框不一致。神器用途副本 `_box_equip_0_linear` 逐像素保持 RGB，按 `Aout=1-sRGBToLinear(1-Ain)` 转换 alpha；不以纯色 lining 遮盖原梯度。转换后 PNG 与来源的不同哈希及变换公式明确记录在正式清单，不能宣称该副本逐字节等于原图。
- 神器大图恢复 `arti_frame_box` 原卡框。候选卡底部卷草使用 `arti_frame_deco_2`，按源图中包含的透明留白布局，不能只放一个过矮的图片矩形导致卷草缩成一小条。
- 技能/背景页签恢复 `_btn_white` 圆角源图；技能图标颜色随选中页切换。大图帮助区恢复现有黑色圆角底图。
- 强化进度条使用 `_progress_bg` / `_progress_bar`；金色竖饰由 `_bar_4` 两半旋转构成。左侧旋转、颜色及透明度可在 `unit_item_upgrade.csb` 找到；右侧列表边线沿相同素材族放置，具体公用列表注入节点未找到，因此其拼装关系属于依据原片做的表现层推导。
- 右侧横卡共用 `ArtifactInventoryCard.prefab`，候选、强化及背包三入口统一调整等级、名称与数量字号，避免仅修一个页面。

最小新增 **7 张**PNG 位于 `Assets/Game/Modules/Home/Res/Textures/Inventory/ArtifactRestore/`：6 张逐字节匹配原图（含大图旋转图标），1 张为上述保 RGB 的线性透明度副本。正式清单 `source-manifest.json` 保存只读来源、尺寸、源/导入 SHA256、Unity border 与副本变换；没有重绘、上采样或批量导入来源目录。

## 验证与边界

隔离 Unity 项目：`.artifacts/UnityUIReviewProject`。`GenerateEquipmentInventory` 仅重建普通装备并调整已有神器 Insets，不重建神器全部页面；这轮神器修改需完整 `BuildHeroInventory` 或 `GenerateInventory` 才能进入预制体。

2026-11-24 的中间回归 `play-full.xml` 为 3/3 通过，验证六状态、长文滚动与留白。其后又校正圆角原尺度、候选卷草尺寸、文字与共享横卡字号，最终结果以随后 `play-ready.xml` 为准，不能引用中间通过替代最终修改验收。

2026-11-25 01:22（本机时区）最终 alpha 版本：生成 `generate-alpha.log` exit 0；`play-ready.xml` **4/4 通过**。本机实际查看已穿技能/背景、大图、候选、强化及背包 1478×720、1536×960 截图，原梯度框保留，正文后白披风透底已明显压低；横卡文字三个入口没有出现新增重叠。背包快照 fixture 使用独立本地 fetch，截图不再带测试请求失败遮罩。原片和项目所用神器/角色不同，因此此验收比对布局、素材和状态，不能将不同文案/数值看作同一账号逐像素验收。

最终截图目录：`.artifacts/ui-material-recheck/artifact/current/`；主截图为 `artifact-material-worn-skill.png`、`artifact-material-worn-story.png`、`artifact-material-picker.png`、`artifact-material-painting.png`、`artifact-material-enhancement.png`、`artifact-material-bag.png`、`artifact-material-bag-16x10.png`。容量 `artifact-material-capacity.png` 是隔离镜像旧通用框，仅验证 400 格提示行为；通用容量框由主任务集成后另行重拍，该张不作为最终样式验收依据。

本轮固定快照不发起服务端写入，也未修改神器配戴、解除、强化、出售的事务实现。原片已穿弹窗属性栏下方的垃圾桶图标已补齐禁用显示；现有业务本就禁止出售已穿神器，背包详情保留真实出售入口。该禁用显示不引入新交易流程。强化成功动画完整时序、出售/转移的交易流程不由本轮静态图形回归判定已还原。

仍可见的局部差异交由最终集成核对：00:06 的专属职业行有职业图标与符合条件的绿色勾，当前候选页仅显示职业文字；01:28 经验条上方有醒目的「+经验 EXP」预览，当前仅显示经验分数及升级级数，没有独立大号 EXP 预览文本。这两处不能以本轮 4/4 固定快照测试视为已还原。
