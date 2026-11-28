# 竞技场商店配表

`arena.json` 按商店横向列表的展示顺序排列。前六项使用征服点数兑换。`period` 决定每日、每周或不限购，`limit`、`price`、`quantity` 分别是限次、单次售价和发放数量。交易后的钱包、材料持有量和可兑换次数均取服务端回执。

## 赛季目录

十一项 `directory` 对应 66 个装备条目。`entries` 依次列武器、头盔、铠甲、项链、戒指、鞋子的简体中文装备名。不同赛季可复用装备名称。目录的 `quantity`、`price`、`limit` 均为零，`material` 为空。客户端用共用弹板查阅目录，服务端拒绝以目录 ID 兑换。

用户提供的 `Screenrecorder-2026-11-28-20-01-10-182.mp4` 在 02:13 显示常规商品和第一批赛季卡片，在 02:14–02:16 横向滚动可见其他赛季名称及箱子图标。实际游戏画面存在左侧设备黑边。录像中没有打开赛季装备目录，项目采用通用弹板列出装备。各赛季原版兑换规则及限额没有据此配置。录屏日期取文件名，录屏时的游戏版本及速度设置未显示。

赛季分组取本地官方简中 `text/zhs/text.db` 的 `ns_pvp_group_<N>`。用 `db/equip_item.db` 对应赛季装备行的 `name` 键回查同一份简中表。按文件顺序，`season0` 至 `season10` 的 N 分别是 17、18、21、22、24、25、26、27、19、20、23。装备 ID 模板为 `eah<N+5><部位>`，部位依次为 `w/h/a/n/r/b`。物品的图标还包含赛季序号 `arenaNNN`，可独立交叉核对。

- 装备表本地审阅导出 `.artifacts/equipment-enhancement-research/equip_item.source.json` 的 SHA-256 为 `892e8732907966708ae03abd8027c81d3bcb2cadc3cadbf1c6ab4e961afa0380`。
- 简中文本导出 `.artifacts/roster-data/text.zhs.json` 的 SHA-256 为 `723a4426b761a6e4ce80f7e85aeb930b023cbacdecf278be37435bfd4cf33301`。
- 卡片原图位于只读来源 `D:/sevenepic/原始资源/shop`，文件前缀 `sp_selectbox_pvp_b_`。按十一项的顺序，后缀依次为 `immune`、`counter`、`penetrate`、`scar`、`cri_dmg`、`att`、`speed`、`acc`、`cri`、`res`、`max_hp`。参考帧为 `.artifacts/arena-tower-review/frame-133.jpg` 与 `frame-136.jpg`。

`Server/Tests/ShopRulesTests.cs` 检查类目、赛季目录的只读数据和限购周期。`Assets/Game/Tests/PlayMode/ShopWindowPresentationTests.cs` 在实际商店预制上展示模拟回执提供的数据，检查十一个目录逐一打开和关闭、普通兑换刷新征服点数与剩余次数。设置环境变量 `ASTRAL_SHOP_CAPTURE_DIR` 会在该目录写入交易前、目录、交易后三帧。
