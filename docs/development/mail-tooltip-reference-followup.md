# 邮件物品卡的原图与切片复核（2026-11-27）

本次针对用户 `codex-clipboard-fbd7c9aa-4d08-4cab-bc89-8c80abb08b7a.png` 中物品图片超出可见边框、框线放大模糊和内容不符的问题。协调者负责整体验收与分发；本页记录可复查的源调查，不把源代码完成当作原生画面通过。

## 实际查看的原版与修改前

原片为用户提供的 `Screenrecorder-2026-11-22-15-12-33-596.mp4`，日期取文件名 11/22，原作版本、发布日期和可见倍速未确认。

- [00:25 历史信正文内查看附件](../../.artifacts/mail-system-review/frames/025.000-attachment-tooltip.png)：卡有完整的灰细圆框、上暗下蓝底、内置方形物品底、右上分类与名称、正文描述；不是全屏横板。
- [01:36 邮件礼物箱](../../.artifacts/mail-system-review/frames/096.000-gm-gift-tooltip.png)：右侧附件左边出现卡片，下方正文为物品描述。可见分类为“特别礼物”，名称为“邮件礼物箱”；描述为“内含邮件奖励的礼物箱。在邮箱内领取时可立即获得箱内所有道具。”
- [01:39 行动力](../../.artifacts/mail-system-review/frames/099.000-gm-energy-tooltip.png)：分类为“入场券”，名称为“行动力”，描述为“冒险时必不可少的行动力。”；下方可见持有量和恢复时间。恢复时间需要权威计时输入，本轮不得用录屏的固定 4 分钟伪造。
- 修改前：用户提供截图的原路径为 `C:/Users/Admin/AppData/Local/Temp/codex-clipboard-fbd7c9aa-4d08-4cab-bc89-8c80abb08b7a.png`。当前 R41 生成器为 `_box_black_white` 加人为渐变蒙层，按视口的 `.26 × .38` 分别拉伸。物品没有方形底板，分类统一回退“附件”，正文重复名称和领取数量，均与原片物品卡结构不符。

原片礼物箱外框可见约 524 × 312 像素，行动力约 524 × 410 像素；按原片 1200 高与 720 设计高度换算，宽约 314、高约 187/246。以上是录屏像素测量，不是反推的原运行时精确尺寸。项目采用两个固定模板 `480 × 285` 和 `480 × 375` Canvas 单位（320 × 190/250 原设计单位乘 1.5），不随窗口长宽分别伸展，也不按正文行数无限撑高。

## 只读源依据与原因

已实际查看原 PNG，[源素材联系图](../../.artifacts/mail-ui-followup/tooltip-reference/source-assets.png)保留所有候选。`D:/sevenepic/原始资源/img/_box_equip_0.png` 的灰圆框与上暗下蓝底与录屏相符。`wnd/item_tooltip_simple.csb/Layer/frame` 实际引用此 PNG，原图为 440 × 386，节点九宫格 `capInsets(x,y,w,h)=(65,60,310,266)`，对应 Unity `border=(65,60,65,60)`。原版业务调用脚本未恢复，因此“邮件卡复用这一通用物品卡”是视觉与源布局共同支持的判断，不称已恢复原作调用链。

此前所用 `_box_black_white.png` 本身是中间小白框、外侧黑影的精灵，不能通过更改 tint 和另加全屏渐变变成此物品卡。它也有多个源节点使用不同 cap；不能把本次切片直接全局替换给其他筛选、排序弹框。新卡直接使用正确 PNG，无需修改旧共享资源的 GUID 或切片。

`inventory_equip_popup.csb` 上同名精灵另有 `cap(63,150,314,36)`，`artifact_tooltip.csb` 也有不同 cap。它们不是本次 `item_tooltip_simple` 节点的切片参数，不能因文件名相同而混用。

## 本次编辑入口

- `Tools/Mail/ImportMailTooltip.py` 最小导入正确原图；只写 Mail 自己的 `MailItemTooltipFrame.png`、其 meta 和 `tooltip-source.json`，保留已有 GUID，源 PNG 字节相同，RGBA32、无纹理压缩、双线性，记录源 CSB 与 PNG 哈希。
- `Assets/Game/Editor/ArenaUiBuilder.MailTooltip.cs` 提供独立 `BuildMailTooltip`，包括固定卡尺寸、方形底板、分类、名称、正文和可隐藏的真实余额区；去除 tooltip 自己叠加的黑幕，正文层自己的遮罩继续由所属窗口管理。
- `Assets/Game/Modules/Mail/Scripts/MailItemDescriptions.cs` 集中已实际看清的两种分类和描述，并加入下述源表书签说明。其他尚无参考的物品分类、描述留空，不再拼“物品名 × 数量”冒充原描述，也不根据礼包内容生成虚构物品说明。
- `Assets/Game/Tests/EditMode/MailTooltipTests.cs` 检查固定锚点和尺寸、正确切片、保比例物品及方底，以及未知道具不产生虚构描述。

导入命令：`. Tools/ProjectEnvironment.ps1; Initialize-AstralProjectEnvironment -RepositoryRoot (Get-Location).Path | Out-Null; .tools/character-assets/Scripts/python.exe Tools/Mail/ImportMailTooltip.py`。父生成器与 Binder 的集成、Unity 生成和测试运行由协调者完成。

## 验证与待验

已通过只读源解析与哈希核对、原 PNG 实际查看；修改前保存预制的真实锚点静态解析检查实际失败为 `anchorMin=(0.52,0.46)`、`anchorMax=(0.78,0.84000003)`，说明卡在随视口分别拉伸。这不是 Unity 运行测试的 RED，不能替代随后实际运行。完整命令与原输出写于 `.artifacts/mail-ui-followup/tooltip-reference/prefab-static-red.json`，源节点审计和脚本亦在该目录。

## 书签说明的补充源（11/28）

继续只读复用既有经过校验的 CDBM 解析器，导出 `db/item_token.db`（97 行）和 `text/ja/text.db`（146,138 行）。完整头尾、遍历、行数、列数和严格 UTF-8 校验通过；文本表存在长度 87 的 key，旧调查脚本的 64 字节软限制会提前停止，本次只读文本脚本允许真实格式的 255 字节 key 上限，保留首次失败日志。没有放宽结构、行数或尾部校验。

`to_ticketrare` 原行指定 `icon=token_ticket_rare`、`desc=ticketrare_de`、`desc_category=item_category_ticket`。本地唯一已存语言表 `ja` 对应分类原文为“召喚”，描述原文为“継承者が特別なページを開いて、英雄を呼び出すことができるようにする栞。”，名称为“聖約の栞”。项目中文说明使用明确标注的翻译“继承者可打开特殊书页，以此召唤英雄的书签。”，分类“召唤”。**此翻译不是已核对的 11/22 国服逐字原文**；若须逐字匹配，还需要同版简中文本表或书签详情截图。当前用户报告的书签卡不再为空说明。

只保存最小行摘录与源哈希供交付定位：[书签来源摘录](../../.artifacts/mail-ui-followup/tooltip-reference/bookmark-source.json)。完整只读调查输出在同目录，不导入全部源表、不提交原表。当前服务器没有行动力恢复时刻，补充时间一行仍不可伪造。卡片实际位置由协调者 Binder 相对点击附件放置并约束安全区，生成器固定锚点只是默认预制位置。

本子任务未启动 Unity，也未修改已分发的 R41 预制或签名包，因此不能声称测试、修改后图像或分发已通过。物品卡新增无音效或粒子改动，视听音效对本子项不适用；整个邮件的提示音由协调者另行验收。未知物品的同版说明和行动力恢复计时仍缺权威输入，相关内容明确不造数。
