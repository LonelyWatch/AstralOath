# 普通装备强化

## 来源与支持范围

用户 `Screenrecorder-2026-11-25-19-54-45-217.mp4` 的普通强化流程，配合只读原包 `pack-15-r4262`。制作前实际查看入口/材料登记/自动阶段/成功/仅经验，见[方案](../plans/2026-11-25-equipment-enhancement.md)。录像2608×1200、2:13.38，文件名日期2026-11-25，游戏版本和播放倍速未显示。原帧、声音、API正文与表图保存在 D 盘 `.artifacts/equipment-enhancement-reference/` 和 `.artifacts/equipment-enhancement-research/`。

规则版本 `equipment.enhancement.source-r4262.recorded-economy-v1` 支持 Lv70/Lv85 稀有/英雄/传说、六部位合法主属性。CSV含Lv90等主曲线供核对，不代表Lv90能从+0普通强化。白绿、特殊来源例外和未带源profile的训练/旧GM装备明确拒绝按新曲线强化，保留其属性与穿戴能力。魔控只见预览/取消，本页灰色入口不执行；再炼、改造、强化转移未实现。400格神器容量与神器强化沿用各自规则。

## 曲线、经验和概率

选择性解析 `itemenhance/equip_stat/equip_item/itemgrade` 等相关表；身份/哈希/转换边界见 `Config/Tables/equipment_enhancement_sources.json`。`item_equip_upgrade` 是再炼，不能当普通强化表。原目录没有修改或批量导入。

主曲线先对固定属性修正基值取整数，再应用强化倍率。90铠甲 `floor(25×2.5)=62`，+15为310，原片红紫品质一致，不能再统一乘品质倍率。百分比主词条使用**项目整数百分点策略**：逐级ROUND_HALF_UP后转BP。85生命百分比+3/+6/+9为19/26/34%，+15为60%；原+9预览`12% ▲22%`确认显示34%，已修旧floor为33%。原内部保留3360还是3400BP尚未恢复，不把项目量化当原作事实。中间速度取整无独立原片，保留floor。

累计经验用 `ceil(基础XP×品质need_xp_rate×前一级exp)`。85传说T1/T3/T4为525/1969/3019，录像1982到+3剩13、下一级需1050吻合。魔控满级预览82031与项目阈值82032差1，特殊路径不能证明普通强化内部精度；ceil是当前确定性项目策略，仍需同版本单变量样本。

[官方概率公告](https://page.onstove.com/epicseven/global/view/7902683)原发2021-10-27、页头更新2026-11-17，正文声明2022-05-25维护后适用。普通/大成功/超大成功89/10/1%，每次投入抽一个互斥档，经验×1/1.5/2；不是失败概率，也不增加词条掷数。官方已显示的小数三位权重由项目归一化，不宣称恢复原RNG整数权重。

每跨+3/+6/+9/+12/+15处理一次词条事件：稀有+9/+12新增、英雄+12新增、传说升级已有词条。已有类型1/n选中，新类型排除部位禁止项、主属性和重复类型再1/n选择。数值增量按等级/品质分布；例如72–85稀有百分比4/4/5/6/7须保留重复权重，传说速度5约0.332%，不能按唯一整数均匀抽样。

材料基础经验100/1500/4500/13500；金币采用录像账号的**有效成本**1600/14400/43200/129600。+9/+12/+15组合价147200/328000/684800交叉吻合。原账号折扣与经验加成没展开，项目额外经验加成为0；录像1700显示1982不能用来把粉末基础经验改成116或给全部账号强加16.6%加成。

超过+15剩余需求的**基础投入经验**按每百返粉，成功倍率新增部分不返。先验完整gross投入余额，再合并同种粉末净流水；未来退款不能支付未持有材料。满级返还依据页面提示实现，缺原版完整执行录像，未称原返还内部算法逐帧验证通过。

## 权威操作和兼容

启动前应用 `015_equipment_enhancement.sql`，普通启动不自动迁移。所有接口取当前Bearer账号，不接受玩家/区服覆盖。账号行锁和同一个ReadCommitted事务覆盖版本、主副词条、XP、roll历史、材料/金币、审计与原回执。

- `GET /api/inventory/equipment/enhancement-rules` 返回 `rulesVersion/economyScope/materials/profiles/successes`。profile给16档 `mainValues/xpThresholds`，客户端据此预览，随机结算留服务器。
- 库存追加 `materials{gold,items[{kind,quantity}]}`，单件追加 `xp/enhancementSupported/enhancementBlockReason`，旧字段兼容。XP为累计值。
- `POST /api/inventory/equipment/{instanceId}/enhance` 接受 `{requestId,expectedVersion,materials:[{kind,quantity}]}`，单种一次、每项1–999、总数至多1000。返回 `{inventory,result}`，含前后等级/XP、实得XP、金币、成功档、词条变化及返粉。
- 指纹包含动作、目标和完整请求；同ID同内容返回原回执，不再抽样/扣费/退粉。改内容、旧版本、跨账号、余额不足、满级、不兼容profile等拒绝并回滚。

新GM `forge.rulesVersion` 必须匹配服务器，主属性用 `MainValues[enhance]`，副属性按品质 `GradeRanges` 与掷数/开放时机校验。旧未带版本请求只为历史兼容保留原近似配方，不获得新profile。GM初始值目前用公开强化增量上下界检查可达范围，不能声称原版初始掉落分布已还原。

## 界面、音频和生命周期

`EquipmentEnhancement.prefab` / 普通 `EquipmentEnhancement` / `EquipmentEnhancementBinder` 只用通用BindingMap和控件。背包、已穿详情、单槽当前/候选有入口，协调器每次开独立Scope，返回恢复原页。规则预览、数量/自动登记、提交和视觉时钟分开；回执立即更新权威缓存，约2.25秒后呈现本页结果，动画不发奖励。

`EquipmentInventoryApi` 在登录生命周期保存未决ID、版本和配方。取消、超时、408、5xx保留；重开恢复只读配方并重试同请求。明确拒绝或有效回执后解除。关闭解除Canvas回调、输入与网络等待，声音走非缩放0.20秒渐出。

静态图取自 `unit_item_upgrade.csb`、`item_select_slidebar_popup.csb`、原v2符文盘与材料图集；选择性导入入口 `Tools/Arena/ImportEquipmentEnhancementArt.py`，不以纯色替代原框。共用角标用原橙/红九宫格：+0隐藏、未满橙底、普通+15红底；神器按突破后的上限传入，复用会复位。

原声音事件为 `ui/upgrade/slot_in/slot_out`、`effect/itemupgrade_main_v2_start/itemupgrade_main_v2_circle`、`effect/ui_enchant_bonus/ui_enchant_ultimate`。未升级只加经验同样有起声/收束。大/超大成功原片没明确触发，候选来自原库。新Home声音加入Foundation的UiAudio地址组，窗口释放后尾声仍持有独立资源句柄。

工具明确返回不支持音频输入，代理实际听感验收为**环境阻塞**；原轨、匹配、录音及事件时序不能替代试听，均提供用户复核。符文盘曲线和连续过程另留实机帧，未称仅凭静态图还原全部原Spine曲线。

## 验证记录

完整服务器真实MySQL546通过、0失败/跳过；协调者另重跑强化8项通过，覆盖取整、跨档、幂等、余额/跨账号、gross退款与满级。通用角标/原装备入口/新页24项图形预制通过。受控HTTP交互、真实隔离服画面与最终原生包分别记录，不以中间失败图或代理消息当通过。最终图录、文件清单、包/母包与启动重开结果写入交付报告；此文不提前宣称已打包。
