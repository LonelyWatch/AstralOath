# 竞技场、商店与五星频道播报

**R49 已于 2026-11-29 被用户退回。** 下文的“最终”指 R49 当轮最后留存的候选图。后续按原片重做和 Windows 实机的结论见[新版验收](2026-11-29-arena-visual-rework.md)。

## 录像与可打开的对照

用户提供的 `Screenrecorder-2026-11-28-20-01-10-182.mp4` 是简体中文实机录像，文件名日期为 2026-11-28。录制时使用的客户端版本和游戏内倍速未确认。项目玩家、英雄、账号余额与录像不同。

- **竞技场入口。** [原版 00:34](../../.artifacts/arena-tower-review/frame-34.jpg) 的左侧是段位，中间列出交战对手，右侧切换分类。[修改前的项目窗口](../../.artifacts/arena-tower-review/before-directory.png) 直接进入模拟战房间目录。[调整后的第 55 关](../../.artifacts/arena-tower-review/final/01-tower-stage-55.png) 位于可滚动列表中，当前关已选中，后续关锁定。第二项打开原模拟战房间，第三项保留对战记录入口。
- **战斗与阵容。** [原版 03:26](../../.artifacts/arena-tower-review/frame-206.jpg) 显示双方阵位、VS 和玩家持有的英雄。[项目挑战编队](../../.artifacts/arena-tower-review/final/03-tower-formation.png) 使用拥有的实例，能调整至多四名英雄。[原版 04:29 的放大镜](../../.artifacts/arena-tower-review/frame-269.jpg) 打开攻略。项目按本次要求显示该账号首次获胜时的双方阵容。[首通记录实图](../../.artifacts/arena-tower-review/final/02-tower-clear-team.png)
- **竞技场商店。** [原版 02:13](../../.artifacts/arena-tower-review/frame-133.jpg) 和 [02:16](../../.artifacts/arena-tower-review/frame-136.jpg) 显示商品及目录的顺序和按钮。R48 的主页商店入口只给未开放提示。项目本轮首版用同一绿按钮展示两类商品，最终分别使用绿、蓝两款原素材，并校正季节箱子的套装图标。[首版](../../.artifacts/arena-tower-review/after/shop-arena-reference-133.png) · [最终商品列表](../../.artifacts/arena-tower-review/final/shop-arena-reference-133.png) · [赛季装备目录](../../.artifacts/arena-tower-review/final/shop-season-directory.png) · [兑换结果](../../.artifacts/arena-tower-review/final/shop-exchange-receipt.png)
- **频道与资料。** [原版 00:10](../../.artifacts/arena-tower-review/chat/10.jpg) 可点击五星图标。[原版角色](../../.artifacts/arena-tower-review/chat/hero.jpg) 与 [原版神器](../../.artifacts/arena-tower-review/chat/21.jpg) 对应不同的资料页。R48 公开频道只支持原有消息类型，本轮首版两条消息堆在顶部。最终短记录贴近输入栏，英雄详情使用原预览背景。[首版](../../.artifacts/arena-tower-review/after/01-channel.png) · [最终频道](../../.artifacts/arena-tower-review/final/01-channel.png) · [角色和技能](../../.artifacts/arena-tower-review/final/03-hero-skill.png) · [神器资料](../../.artifacts/arena-tower-review/final/04-artifact.png) · [神器大图](../../.artifacts/arena-tower-review/final/05-artifact-painting.png)

## 数据与玩法边界

防守资料来自作者公开的 **2020-07-11 国际服普通竞技场百强**。100 行都保留来源序号，合计 400 个位置、42 个英雄形态、85 种不计站位的四人组合。可逐行追溯原帖和映射结果。榜单没有装备或技能等级。项目另行设计关卡配置，对尚未实装的形态使用同角色原型或近似职责英雄。实际配置逐步增加人数和等效装备属性，前五关为一名一级空装敌人，前二十关全员空装，第 31 关起四人。第 100 关为 60 级。首通一次性发征服点，全部首通共 2,201 点。八档段位分别在通关 0、10、25、40、55、70、85 和 100 关时达到。[公开防守名单](https://m.hungryapp.co.kr/bbs/bbs_view.php?bcode=epicseven&pid=125785) · [数据来源与映射](../../Config/ArenaTower/README.md) · [实际 100 关配置](../../Config/ArenaTower/catalog.json)

可兑换商品包括魔罗戈拉、40 行动力、上下级装备强化石及上下级饰品强化石。魔罗戈拉每周一次，行动力每日三次，项目限购按 UTC+8 零点和周一零点重置。11 个赛季目录各含六件物品，可浏览全部 66 件。购买请求重试会用原编号，余额、限购、发货和审计在同一事务提交。[商店明细与原素材出处](../../Config/Shop/README.md)

频道以权威抽取结果发消息，五星普通英雄、光暗英雄和神器均覆盖，仅对抽取者当时所在频道可见。点击打开只读的公共资料预制。频道偏好可以独立屏蔽对应未读提示。[频道实现与原版逐帧记录](../development/channel-summon-announcements.md)

## 文件位置

- 配置、采集及导入位于 [Config/ArenaTower](../../Config/ArenaTower/)、[Config/Shop](../../Config/Shop/)、[公共名单采集程序](../../Tools/Arena/ImportTowerDefenses.py) 和 [界面原图导入程序](../../Tools/Arena/ImportTowerShopArt.py)。
- 爬塔预制、显示与交互位于 [Arena 模块](../../Assets/Game/Modules/Arena/)。页面和编队由 [TowerShop 生成器](../../Assets/Game/Editor/ArenaUiBuilder.TowerShop.cs) 及 [编队生成器](../../Assets/Game/Editor/ArenaUiBuilder.TowerFormation.cs) 维护。[主页、模拟战、战后和商店路由](../../Assets/Game/Modules/FoundationDemo/Scripts/StartupNavigation.TowerShop.cs)
- 商店窗口和商品卡位于 [ShopWindow.prefab](../../Assets/Game/Modules/Shop/Res/Prefab/ShopWindow.prefab) 和 [ShopItem.prefab](../../Assets/Game/Modules/Shop/Res/Prefab/ShopItem.prefab)。绑定、资源及兑换请求归属 [Shop 模块](../../Assets/Game/Modules/Shop/README.md)。热更程序集清单位于 [assemblies.json](../../Config/HotUpdate/assemblies.json)。
- 五星播报的服务端写入位于 [SummonStore.cs](../../Server/Host/Summon/SummonStore.cs) 和 [ChannelChatStore.cs](../../Server/Host/Social/ChannelChatStore.cs)。显示位于 [ChannelChat](../../Assets/Game/Modules/ChannelChat/)，共用资料预制位于 [HeroInformation.prefab](../../Assets/Game/Modules/Home/Res/Prefab/HeroInformation.prefab) 和 [ArtifactInformation.prefab](../../Assets/Game/Modules/Home/Res/Prefab/ArtifactInformation.prefab)。
- 服务端挑战、战斗恢复和首通结算位于 [Server/Host/ArenaTower](../../Server/Host/ArenaTower/)，交易位于 [InventoryStore.Shop.cs](../../Server/Host/Inventory/InventoryStore.Shop.cs)。数据库 [022–025](../../Server/Host/Foundation/Migrations/) 增加播报写入、关卡进度、战斗与钱包、购买记录，并扩宽频道消息类型。已有开发库可以顺序应用迁移，无需重建。服务端测试位于 [Server/Tests](../../Server/Tests/)，客户端交互用例位于 [PlayMode](../../Assets/Game/Tests/PlayMode/)。

## 实测与剩余范围

隔离数据库和真实 HTTP 用例验证了首通、重打、越关拒绝、账号隔离、服务端重启后恢复，以及并发首通只发一次奖励。100 关均能构造战斗，初始一级三人空装队对前五关各运行三种种子，15 场均获胜。商店用例逐个检查六种商品进入实际库存，同时检查并发兑换、重试和限购。两个独立身份测试了同频道收取五星消息、跨频道隔离及重复提交去重。[爬塔 4 项结果](../../.artifacts/arena-tower-qa/tower-formation-final.trx) · [播报与购买 3 项结果](../../.artifacts/arena-tower-review/chat/broadcast-final.trx)

推进难度来自设计配置，仅前五关做了以上通关抽样。中后段的玩家实际胜率尚待试玩。此次未制作通行证、赛季装备购买和战斗回放。回放后续需用户提供参考。音频沿用控件的点击、返回与阻止反馈，本轮没有新增专用音轨；已提取参考音频，尚未逐项试听比对。[点开资料的原声片段](../../.artifacts/arena-tower-review/chat/reference-open.mp3) · [商店原声片段](../../.artifacts/arena-tower-review/shop-reference.mp3)
