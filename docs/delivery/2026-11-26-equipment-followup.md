# 装备界面、强化结果与英雄总加成复核

本轮于11/26开始，跨夜11/27接续Windows0.1.26/R36，核对角标、强化按钮、金币底、自动登记、词条跳值/次数、换装战力、详情固定尺寸及英雄总加成。追加精确角标复核后修改了Common/AOT，交付改为Windows0.1.27/R37新母包。当前原生、签名构建及本机分发尚未执行，不引用旧R36原生回执冒充。

## 11/27角标重点复核

用户再次指出数字差异后，按同图标可见框归一，不再仅看整页。00:13前的实现虽然修好切片/字图来源，却仍用固定29.07字号配较小图标，字高相对原版大约20%，+3/+15板同宽，+15白字右/下留白仅3/4px。`badge-focus/same-icon-width-comparison.png`和测量JSON保留这一真实失败阶段，不作为通过图。

修复在Common内让底板与数字共同缩放，恢复39/44源宽与通知点31宽。三个几何回归先实际失败后通过；宿主只改变宽不拉伸内容，高变化同时缩字与板。第一阶段同比字高从24.63降到+3 19.44/+15 20.74，原片为20.70/20.42；见`badge-before-after-same-icon140.png`，该阶段仍为TMP字图桥。

数字最终改由`BitmapNumber`原Sprite直接绘制，保留26×34完整像素、15/16步长及原字间阴影遮盖顺序，避免TMP的重排/半像素取整路径；GameHome的pixelPerfect与override均为0。原RGBA自带描边，不增加人工描边。兼容GameText隐藏，只保留业务读取字符串。此阶段新增3项实际红测后实现，最终`ui-sprite-final-green.xml`33/33通过；随后`final-sprite-combined17.xml`17/17通过、0跳过，重拍34帧受控横扫和90帧真实请求。不以Editor图形通过代替Player已通过。

同图标140px的原+3/+15字高20.70/20.42，最终Sprite均19.44；约1px栅格差与原片压缩/阈值相关，不称逐像素一致。三方图`badge-focus/badge-original-tmp-sprite-same-icon140.png`保留原版、TMP几何修复阶段及最终Image；固定源步长与阴影绘制顺序，不凭有损录像的白区间隙反推新的字距。

Common布局组件与数字渲染新增AOT类型、Binder调用改变，R36母包不能通过热更替换这些实现；按默认构建边界采用R37新母包。旧图、原始附件、同框裁图、每阶段源码/回执分别保留。

### 构建输入发现的导入覆盖

首轮R37构建一致性复核发现11张新数字PNG虽然源meta写无压缩，`GameAssetImportRules`实际导入又把Standalone/Android配置覆盖为Automatic/Compressed。`ImportEnhancementDigits.py --check`此前仅核PNG哈希和meta存在，未证明实际导入格式。保留完整171文件到`before/image-compressed-phase/`，停止精确核验属于本轮的UnityWindows PID39448；该目录没有package.complete，未验收、未签名分发。停止记录`r37-first-build-stopped.json/.log`。目录名记录的是配置阶段，**不能据此认定图像已发生GPU BC失真**：后续有效红测实际Texture2D为RGBA32 26×34，而导入配置为Compressed；无图形环境可能解压或回退，最终需图形Editor和Native Player分别验证。

修复采用项目现有`AstralOath.ImportOverride`，只针对11张26×34数字，保持GUID与原PNG字节。有效`digit-import/import-red.xml`11/11失败于缺override；旧importer --check当时仍绿，补检查后在同一旧输入真实失败，再生成及复核绿。图形Editor强制重导的`import-graphics-44-green.xml`44/44通过，11张实际Texture2D均RGBA32、26×34、单mip，Default/Standalone/Android显式RGBA32/Uncompressed且目标平台override。新增原生开发烟测将读取+15显示的三个Image纹理，不能以Editor代替Player；重新打包结果随后记录。

## 原版、修改前与修改后

可打开的逐项图录：`D:/UnityProjects/AstralOath/.artifacts/badge-followup/index.html`。原版来自用户11/21、11/25国服实录和只读`D:/sevenepic`，版本/可见倍速未知，不从文件名日期推断客户端版本。时间及节点记录于`reference/report.md`、`reference-evidence.json`、`extended-layout-evidence.json`。本轮图与R36修前图的装备/角色/余额并非全部相同，比较布局与行为，不宣称不同输入逐像素一致。

- **角标**：原版11/25的81.037s为+3，109.012s为既有+15；原底单连续圆角、数字保留暗边。修前用户见两团红底且字体普通。源cap现为正确border(12,11,15,11)；最终使用前述Common共同缩放与原Sprite直接绘制。最初TMP桥的初始化/下划线修复及25项历史回执仅证明各自事实，不能替代重点同比及最终Sprite显示。新图`after/followup-controls-plus3-plus15.png`须与该阶段回执一起阅读。
- **普通/神器强化按钮**：原版`_btn_blue`保留直边和小圆角；修前装备强化入口用大胶囊。现在背包、配戴候选、已穿与神器对应入口复用Common样式变体，正常/按下用源图及同一切片。修后图同上及`followup-detail-wide/narrow.png`。不把不同原样式全部压成一类。
- **金币底**：原`_btn_small_black_p`36×34、cap(14,14,8,6)，此前拉伸。改Common GreenCost的独立切片底/金币/金额，0和22,400两种长度均实际渲染；`followup-enhancement-green-cost.png`及`followup-result-plus6.png`。
- **自动登记**：原`equip_belt.csb`五档黑按钮、真勾框、金棕选中、实黑板。首轮修后截图仍透材料文字，像素先失败：探针(251,0,251)，选中(45,21,0)。原116×116框含32px透明阴影，正确做法是Native frame外扩，不能叠纯色替代原图。最终黑域6点均(0,0,0)，选中(115,83,37)，原片(112,80,38)；三项先红后绿。`buttons/auto-opacity-red.xml`、`auto-opacity-green.xml`及`auto-pixel-comparison.json`保留证据。
- **结果与次数**：原+3约79.514–79.554s主值直接12→19，+6约92.063–92.104s直接19→26。副属性/分数的橙增加相对本次开页起点累计，(1)为该词条额外强化一次；+6仍保留先前防御+5，分数+8→+16。修前逐整数跳、每请求重置差值、没有可靠次数。现主值直接切权威终值，累计基线不被后续回执覆盖；灰/白、次数及源横扫更新。`followup-result-plus6.png`、`followup-evidence.json`及实际候选服`actual-server-result/reopened.png`。
- **换装横板**：11/21约228.276s原版为提交前CP24,012→25,876、▲1,864，231.195s关闭、231.442s回管理页；第二笔236.290s为25,876→27,937。没有看到成功后第二个CP横板。修前显示八维表；现显示项目CP前后与增减，保留转移/替换/套装说明，未知神器为「—」。实际训练输入1,103→1,240、137已通过并保存`equipment-manager-confirm.png`；算法区别见下节。
- **固定详情**：原戒指/项链/鞋的框不随描述行数/按钮变化。修前随父宽高变化，已实测宽585.157→541.812。现管理候选480×864、已穿585×900设计单位；常规宽屏固定逻辑尺寸，极窄安全矩形统一缩整个管理composition，不单独拉卡。`equipment-manager-preview-1080p/4x3/21x9.png`及`followup-detail-wide/narrow.png`。
- **英雄总增加**：原11/21 01:19.2左列为白总值＋橙总增加，不是最近换装差值。修前只有白值；现从当前装备、套装、神器与裸基准重算，卸装零值隐藏，切英雄不残留。真实两次换武器总增加20→30（不是第二次只显示10），卸装隐藏；`equipment-equipped-hero.png`、`equipment-equipped-hero-narrow.png`。未知神器时攻/血橙值不冒充完整总增加。

## 数据与规则

60件Lv70/85稀有/英雄/传说装备、300次实际掷值，各组均出现非最小值；未复现固定最小值，未改RandomNumberGenerator或权重。原始分布与边界为`numeric/investigation.md`的第一阶段调查，不把该阶段“未改代码”误当后来计数实现也未改。

迁移016新增`inventory_equipment_substat.enhancement_count` nullable列。初始生成与新解锁不计额外次数；既有词条被强化一次+1，GM合法Rolls保存Rolls-1，数值和次数同事务/幂等。响应以Known布尔区分0和未知。旧+N仅完整唯一且无未来事件的历史恢复；缺失历史的旧GM装备不猜。当前完整服务端599项通过。

CP规则是`combat-power.project-v1.historical-p1.source-r4262-artifact.owned-progress-zero`，纯C#47项通过，33条神器源系数核对。最终八维已加一次神器攻血，P2仅消费真实星级/等级；当前未实现的技能强化/专武/转职进度显式传0。原片CP25,876所见八维的历史P1只有19,748.502592，完整原P2/精度未确认，因此不称2026原版绝对值还原。[CP专题](../development/combat-power.md)

## 实际验证

- `ui-sprite-final-green.xml`：最终33项Edit通过，0失败/跳过，包括Common几何与Image数字、旧神器快照置信、源切片、按钮、累计/次数、固定卡、安全适配、总增益及CP。`ui-final-green.xml`25项、`ui-geometry-confidence-green.xml`30项是各自历史阶段，不冒充最终源码。
- `final-sprite-combined17.xml/.log`：最终17项Play通过，0失败/跳过，包括真实预制像素、两组连续重拍、隔离候选服强化/重开、超时和拒绝、英雄两次换装与卸装，以及5项管理/缩放/真实提交；`sprite-graphics-green.xml`5项是此前独立图形回执。
- `final-play.xml`11项、`online-first.xml`5项仅为此前阶段历史回执；最终截图已由上述17项重新生成。
- `numeric/cp-server-all.trx`：599项Server通过；含计数与CP纯计算。`server-candidate/api-counts-receipts.json`：独立0.5候选服7训练件初始0、新GM源件0、真实+15后总计5、GET重读、同请求重放、同类GM材料73及独立饰品41均实际核对。
- 早期CP两个红XML被测试夹具异常挡住，不能作业务先红；有效业务红是`cp-quote-valid-red.xml`四项。结果最初五红XML被后续覆盖，仅保留实际观察记录，未伪造历史回执。新增未知神器总增加红为`bonus-unknown-red.xml`。

候选服为`127.0.0.5:7728`、独立schema/最小权限用户；0.4旧服尚未替换。只读核对发现现有测试库已含016，来源为此前运行迁移回归，不能再称“尚未迁移”或“未触及该schema”。本轮后续未再写用户装备/余额；已保存当前一致性SQL及旧R36完整host备份，**这是含016的当前状态备份，不是迁移前旧结构**，恢复演练未执行。私密备份/连接/fixture不进Git、不放公开图录。

## 视听、文件与剩余项

结果横扫来自原`itemupgrade_statup`曲线32帧/30fps、1.0333秒；与三套候选的颜色/轮廓/原片时长交叉推断相符，缺Lua调用证据，不称已证实原调用。追加投影复核确认旧亮区约58×25px明显偏小，改为固定等比662.3232×248.3712设计矩形；最终上方ROI亮区202×101px，原归一约216×93.6px。邻近分数光效及ROI截断影响高度，未把101当纯单条alpha高度。见`buttons/sweep-projection-actual.json`与三方裁图。生产Binder关闭清理、最终34帧尾部与真实请求90帧分别保留；无声视频不冒充扬声器录音。本轮未改音频PCM/事件，也未重新试听，沿用R36声音证据仅供历史参考，未验收听感。

独立审查发现神器旧快照在GET失败或写入未决后仍可能被当成已确认；现对同一失效引用返回未知，读取成功或权威新引用才恢复。作用域解绑保留，新增两项有效红测后随最终33项通过；未用异步测试夹具失败代替业务红测。

准确文件清单将保存`D:/UnityProjects/AstralOath/.artifacts/badge-followup/task-files.txt`，按本轮实际Git变更生成，既有356项左右其他工作区改动不混入提交。主要编辑入口：

- `Assets/Game/Editor/ArenaUiBuilder.{CommonInventoryButtons,EnhancementDigits,EquipmentEnhancement,EquipmentManager,EquipmentSlot,HeroRosterLayout,SingleSlotEquipment}.cs`及普通/神器入口生成器；输出Common Styles、NotificationBadge/EquipmentSlot及Home三份预制。
- `Assets/Game/Modules/Home/Scripts/EquipmentEnhancement{,Binder}.cs`、`EquipmentInventoryBinder{,.Management,.CombatPower}.cs`、`HeroInventory{,Binder}.cs`、`ArtifactInventoryBinder.cs`及DTO；业务仍为普通C# Binder，预制只用基础组件/BindingMap。
- `Server/Host/Inventory`计数实现、016与DatabaseMigrator；`Shared/.../CombatPowerCalculator.cs`；相应Server和Unity回归。
- `Tools/Arena/Import{InventoryButtons,EnhancementDigits,EquipmentCombatPowerArt,HeroInventoryArt}.py`、`EquipmentResultEffect/`，原图和来源清单均最小导入。生成入口`ArenaUiBuilder.GenerateInventory`；子集生成不会更新独立强化页及所有神器入口。

尚未完成/未验证：

- 神器旧业务确认与容量横幅两个蓝色入口缺该状态原图，可能本就采用不同源样式，未擅自改成装备小蓝按钮。下一步补对应录像，再逐状态归并。
- 旧缺历史装备次数保留未知；不会倒推。白色攻/血在神器GET失败时仍按既有equipment-only回退，橙总增加隐藏；下一步为白总值增加明确失败状态及重试，当前不称完整已确认值。
- +15完整成功过程、大/超大成功、魔控、重铸仍缺本轮完整同状态参考/实现；原+15截图是既有装备，不能证明全过程。主强化大演出未在本轮全面恢复。
- CP绝对数值/原作精度未核，当前为明示项目规则；音频听感未重新验收。
- 原生、兼容签名包、安装升级、缓存复用和本机分发尚未执行。不得因文件已生成就称交付完成。
