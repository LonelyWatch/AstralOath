# 界面原素材与画面复核

## 2026-11-24 用户要求

用户指出上一轮纯色深蓝面板与原参考差距明显，要求列出全部相关效果图、查找原素材并重新逐项优化。同日用户明确确认拥有或已获授权复用这些界面及素材。本轮以原界面的实际贴图、九宫格、透明度、叠层和几何布局为依据，撤销上一轮自行改为独立面板风格的取舍。

`D:/sevenepic` 继续作为只读来源。已有导入资源优先复用，确缺的依赖逐个定位并最小导入，原目录及整包均不修改、不提交。

## 实际差异

- 已穿普通装备详情原图按物品品质切换 `_box_equip_1` 至 `_box_equip_5` 的边框、底纹与顶部装饰。上一轮统一纯色底和 `SoftModalSurface`，丢失了品质对应的纹理与边缘。
- 管理装备的左右面板、标题分隔、属性条、候选目录及确认窗均须分别查源节点。不能因确认行为可用而把自行设计的居中表格算作原确认画面还原。
- 神器详情、选择与强化使用各自的源面板、卡片与装饰。上一轮新增的纯色覆盖层和空间分配重新核对，正文滚动及真实事务继续回归。
- 英雄左上与属性区的阴影需要查原半透明素材。上一轮矩形衬底可提升对比度，但与参考的边缘过渡明显不同。

## 来源与图目录

普通装备以用户的 2026-11-17、2026-11-21 简中实录为准，神器另含 2026-11-21 22:31 补录。日期来自文件名，公开日期、客户端版本和游戏倍速未标明。逐状态时间见 [装备实录索引](equipment-ui-reference.md)。

本轮完整图目录、参考与客户端截图并排查看页，以及素材节点映射在主工作区 `.artifacts/ui-material-recheck/`，后续在此文记录最终生成、图形及行为验证结果。参考图库中的项目实机与原片严格标注，源截图保留原文件。

已定位原布局目录 `D:/sevenepic/原始资源/wnd`。普通装备使用 `equip_base.csb`、`unit_equip_base.csb`、`item_detail_change.csb`、`item_detail.csb`、`inventory_equip_popup.csb`。神器使用 `artifact_detail.csb`、`artifact_detail_popup.csb`、`artifact_detail_sub_popup.csb`、`artifact_card.csb` 和 `unit_item_upgrade.csb`。通用弹窗使用 `msgbox.csb`、`msgbox_renewal.csb`，英雄详情使用 `unit_detail.csb`。源节点定位辅助识别贴图与拼装，最终效果仍须与对应录像画面一起核对。

2026-11-25 补查解决了英雄筛选的版本差异：17 日 00:33 蓝框对应新版 `filter_3sector.csb` 等布局的 `img/filter_main_bg.png`，不是旧 `sorting_filter_unit.csb` 的黑金框。原片无控件区域八处 RGB 与源图九宫格每通道差不超过 2。原分辨率帧、坐标及像素记录在 `roster-filter-33s-full.png`、`filter-original-vs-source.jpg`、`filter-observed-regions.json`、`filter-pixel-samples.json`。搜索 01:06.800 独立使用 `hero_detail_search.csb` 的黑金框；不能跨版本套用同一窗口底板。

## 已确认的根因与实现

- 普通框 full 品质 PNG 为 446×392，侧栏淡出 `*b` 为 440×386。源 popup 基框 cap 为 `(63,150,314,36)`；完整品质图四周多 3 像素，推导 Unity border 为 `(66,203,66,153)`。推导不是从 CSB 直接发现的运行时换肤参数，需结合实拍核对。原有 GUID 保留，不用 `*b` 充当完整弹窗。
- 神器与普通装备虽然共用 `_box_equip_0` 名称，源 cap 不同。神器清除 `Detail lining` 与正文 viewport 的不透明衬底；卡图保留 `arti_frame_box` 九宫格及 `arti_frame_deco_2` 卷草。详见[神器逐状态记录](artifact-ui-material-review.md)。
- 通用确认窗原图自带上下细边。普通 msgbox 使用 raw 源、node opacity 216、border `(0,30,0,30)`；管理换装使用 `_alpha` 源、白 tint、border `(30,70,30,40)`。后者图内已有约 0.9 的 alpha，不能再重复乘 0.9。白边仅转换覆盖率，不另外画粗白线。
- `_grow_s` 原节点旋转为 Cocos -90°，映射 Unity Z 轴应为 +90°。首次实拍反向时出现右侧硬黑边；已以实拍修正。另一层 `_grow` 保留源范围。两层均使用原黑色渐变，按节点透明度及线性透射率转换用途副本，不叠矩形属性衬底。
- 实拍证明直接使用 source 白色 10/255 alpha 会在 Unity Linear 形成亮灰块。管理页使用局部线性 tint/coverage，普通详情的白条也转换 coverage。只读源图不变，共享素材不批量改色。

正式来源清单为 `Assets/Game/Common/Res/Textures/arena-common-source.json`，以及 Home Inventory 下的 `material-restoration-source.json`、`Equipment/Management/source-manifest.json`、`ArtifactRestore/source-manifest.json`；包含源路径、SHA、用途切片与转换。允许同 PNG 因源节点 cap 不同保留独立用途副本，不能通过修改共享 border 破坏其他页面。

## 仍需区分的边界

本轮没有确认原素材缺失；剩余差距主要是未实现能力，而不是用纯色补图的理由：普通装备推荐、强化/重铸/转换及整套一键配戴；原版总战斗力与第九项夹攻率的完整面板公式；英雄筛选的套装、技能效果、星级等完整分组及开关。当前英雄筛选仍只开放已实现的等级/获得、元素和职业条件，恢复原材质不等于全部分组已实现。搜索恢复长面板和空历史区域，尚不持久化搜索历史，也不提供假的历史操作。

神器已穿详情的出售图标按当前服务端禁止出售已穿实例的规则显示禁用状态。原片中的转移、出售等条件确认未覆盖全部过程，不能仅凭静态参考宣称流程完全相同。完整图录保留这些边界，已实现流程继续以服务端实际事务验收。

还需后续补齐的可见细节：神器候选职业行的职业图标与绿色勾、强化大号「+经验 EXP」预览；管理页完整顶部资源/角色信息绑定。神器强化成功动画完整时序和交易写入不由本轮固定快照测试证明。原片已经明确这些画面，不能归因于素材缺失，也不能把目前通过数写成所有界面完全还原。

## 验收边界

本轮源材质修订已生成并联合复拍，上一轮 16 项测试不作为本轮依据。主镜像 `.artifacts/UnityProject` 的完整 `BuildHeroInventory` 与 `BuildSummonWindow` 生成退出 0；2026-11-25 最终联合图形 `Play-joint.xml` **10/10 通过、零跳过**，涵盖真实训练服普通装备穿脱、管理取消/确认/转移、六槽套装预览、神器固定快照、容量和英雄搜索。随后修正普通品质框外侧透明余量，受影响的单槽实服与大厅召唤两项 `Play.xml` **2/2 通过**，其中大厅召唤检查实际实时预览及确认窗。图形设备下预制/装备卡/召唤控件 `Edit.xml` **12/12 通过**，最终留白修订后重验仍为 12/12。管理确认恢复箭头格式后的三项 `Play-management-final.xml` 也全部通过。

命令脚本 `.artifacts/ui-material-recheck/run-root.ps1 -Mode Generate|Edit|Play` 使用原生 Unity 2022.3.62f3、D3D11 与独立训练服 `http://127.0.0.4:7728`，显式设置 `ASTRAL_EQUIPMENT_TEST_URL`、`ASTRAL_INVENTORY_TEST_URL`、`ASTRAL_SUMMON_TEST_URL`。中途遗漏装备 URL 和无图形设备的跳过均已在最终图形运行中补测，不计入最终通过数。管理候选套装件数另有先红后绿证据 `SetProgress-red.xml`：原来误按候选原持有人计为 0/2，现按目标英雄预览实际配装计数。

本轮图形证据位于 `.artifacts/ui-material-recheck/verified/`，原图、修改前、修改后图录为 `index.html` 与 `references.json`。普通、神器、英雄及管理的宽窄屏、1080p、4:3、21:9 按各用例分别覆盖，不能扩写成所有页面均在所有分辨率逐像素一致。原片角色、物品品质和账号数据不同，因此主要比对材质、边缘、几何和状态；未把不同数值当成同账号画面对比。

这轮未构建或发布新的 Windows 签名内容；当前已分发的 R35 不因源码提交自动改变。本地试服、图形镜像与生产分开。源码、生成预制、来源清单和文档同批提交，原始影片及完整解包目录不进入仓库。
