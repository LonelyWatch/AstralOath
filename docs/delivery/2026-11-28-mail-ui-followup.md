# 邮件提示、角标、物品说明与确认框复核

**用户追加复核：43的文字颜色与按钮字重仍不通过，44修订进行中。** 用户新图`codex-clipboard-8f772d97-9eee-4bb1-bf37-79d5a12c801d.png`明确正文应偏灰、按钮字更细。下文43的功能/编译/启动收据继续保留，但此前代表性静态图通过不再作为最终视觉结论。不得仅因共享预制或shader变体正确就宣称字重还原。

44文字修订现已纳入产品源`044532c6`签名包并验签，尚未激活/分发，Native没有执行。主服迁移/切换被自动审批拒绝，现用43保持，用户明确批准待回复；不是再次回退文字或误报44已可玩。最终66/32联合回归、包身份、Editor对照和审批范围统一见[主页/资料/频道收尾记录](2026-11-28-home-profile-chat.md)。下文“候选尚未随新Native分发”仍成立，但“尚未打包”仅为当时历史状态。

44文字候选已在共享生成器修正：首问/正文源色RGB180、alpha255，正文与首问同29号；两圆角按钮保持原字体/Regular400/FaceDilate0，白描边`.28→.06`。新增实际RED4/8后修复，最终当前MailPrefab8/8实际通过（包含于`client-tests/session-iteration.xml`，该整组31项其余功能仍有8个预期失败，不称整组全绿）；邮件Play7/7通过。第一次预先`.linear`导致实际灰度117，已保留失败候选；改为直接输入源RGB，由Canvas进行色彩转换，`after-color-corrected`实际核心灰度180、按钮255。像素记录在`text-reference44/after-color-corrected/measure-candidate06-color180.json`，已实际看图。文字候选尚未随新Native签名44分发，用户随后追加主页/资料/频道/召唤任务正在独立推进。

本轮从9638ea84开始，承接用户实际R41四处截图反馈。43阶段产品源为`824d49ee9541446b8cda8988db4272df47392c02`，已构建、验签并在本机发布签名内容43，继续使用R37 / Windows0.1.27母包；没有强更或生产部署。它现为已知文字视觉不通过的历史候选，下面证据不外推为44通过。保留361项会话前工作树状态于`.artifacts/mail-ui-followup/preexisting-status.txt`，不得混入本轮提交。

- [启动R43](../../.artifacts/windows-r43/Play-R43.exe)；实际本体为[client/AstralOath.exe](../../.artifacts/windows-r43/client/AstralOath.exe)。本机助手负责准备已有本地服务，不能只把exe独立复制到另一台电脑。
- [原版、修改前、最终Editor及Native对照图录](../../.artifacts/mail-ui-followup/gallery.html)，内含原声片段和失败候选，未用Editor图冒充Player。
- [逐文件清单](../../.artifacts/mail-ui-followup/files-list.md)：基线至产品源共86项，58新增、28修改，包含11份文档；文件均提供准确路径和用途。最终另有8份仅文档收尾，其中7份已在86项内，`docs/development/hot-update.md`为额外1项；整轮唯一修改路径共87项，客户端构建仍对应824d的产品快照。
- [最终聚合回执](../../.artifacts/mail-ui-followup/final-delivery43.json)关联各阶段原始报告；不覆写中间阶段的`native_pending`等历史状态。

## 四处反馈的实际比对

1. **确认框和两按钮**：原片084.000是低亮细边横幅、金棕取消和蓝色确认；R41截图为显眼的双白边、椭圆按钮。最终使用Common完整`SourceConfirmation.prefab`及嵌套圆角样式，保留原图切片、按下图、字号/间距和UiAccept/UiCancel。原生43两种分辨率的`11-claim-all-confirm.png`已实际查看，端帽和文字细描边恢复；白边压回源效果的低亮细线。图录保留42“Editor正常而Player字细”的失败图，不把行为通过当视觉通过。
2. **物品说明**：原片025/096/099有细圆框、方形物品底、类型/名称/说明；R41是模糊放大的框，图标压框且名称×数量代替说明。最终使用`_box_equip_0`原板与固定内容尺寸，把源透明阴影放在内容区域外，按所点附件定位并约束安全区。实际查看43两轮`06-attachment-tooltip.png`，图标和文字位于边框内。该Native输入是“GM礼物”，原片同类名为“邮件礼物箱”，属于配置输入差异，不能宣称同文案逐像素一致；书签另有Editor图和原DB来源，尚无单独Native近照。
3. **顶部角标**：原片001.800/003.000为源九宫格数字角标；R41采用比例锚点和通用粗字。最终恢复top_bar.csb的固定中心、源cap和Dinmed数字字体，零隐藏、一位与两位按源宽度。实际查看43两轮`04`角标图，确认位置/字形/底板，Native无初始化缺下划线警告。三位宽度是本项目100封容量扩展，原作三位参考仍缺。
4. **提示条**：R41仅设置显隐，没有通用计时。现嵌套Common `GameToast`，复用`GameToastBinder`的2秒计时、替换重新计时与关页取消；排序普通声只触发一次，拒绝使用原UiBlocked。实际先红后绿回归验证生命周期；原片拒绝声音波形匹配另列。原片排序提示未定位，2秒来自用户要求和项目契约，尚未录到Native排序提示完整2秒的连续视听过程，不写成原作实测时长。

## 原版依据与验收目标

11月22日用户录屏，文件名日期2026-11-22；原游戏具体版本、发布日期、可见倍速未确认。实际查看001.800收件角标、025.000附件说明和084.000全部领取确认框。文件在`.artifacts/mail-system-review/frames/`。当前四张用户截图复制至`.artifacts/mail-ui-followup/before/`；这是R41玩家实机反馈，不是重建的修改前画面。

确认框复用Common原版SourceMessageBox及独立圆角按钮样式，不把装备直边蓝按钮混入邮件；校正源按钮缩放后的九宫格宽高与字重。固定物品卡不得按视口分别拉伸宽高，使用实际原素材和类型/名称/描述。角标核对源cap、位置与数字字体。提示复用GameToastBinder，2秒关闭，替换消息重新计时、关页立即取消；声音单独核对。

原排序提示未在录屏定位到，2秒与普通操作音来自用户明确要求及现有通用组件契约，不写成原作实测时长。声音绑定、波形与实际试听分开验收。

## 调查记录

Mail原实现直接拼局部提示，Message只设置active而无计时，未使用现有GameToastBinder。新增PlayMode回归实际RED：2秒后预期隐藏，实测仍显示，结果保存在`.artifacts/mail-ui-followup/notice-red.xml`与日志。首次受限Unity运行许可证IPC失败(199)，允许访问本机许可证后测试实际运行并得到上述行为失败(2)，两者不能混为同一失败原因。

角标旧预制实际EditMode RED 0/5；生成后邮件Edit37/37，连续页交互和外部状态Play6/6通过。原始XML保留在本任务目录。首轮确认图仍有1px亮接缝，独立对图实测中位峰约124；仅SourceMessageBox改Point后顶边约62–86、底边60–63，接缝消失，PNG/GUID不变。按钮按源缩放恢复端帽，按原Normal29.8125、源1px白描边使用独立材质，未改共享字库材质。图库保留所有候选，不拿早期候选作最终。

物品框首轮也查出源阴影内缩导致图标盖框，已把原图阴影移到固定内容范围之外；最终实际查看`.artifacts/mail-ui-followup/after-outlined/gm-attachment-retains-detail.png`，原方形物品底、分类、说明和细圆框均在框内。该图为受控Editor，非Native；真实原片参考025/096/099，不把与书签不同物品的图当作同输入像素对齐。

确认嵌套的标题/正文/两按钮中文保存后重载检查通过。试听未完成；原拒绝声与UiBlocked在93.0424s/94.2584s波形相关.874/.8585，属于来源定位；本轮Play实际触发原普通/拒绝/确认/取消音，但不能代替人耳听感。原排序状态缺参考，以用户要求与Common2秒契约实施。

扩展Common Edit检查曾42/43通过，一项ChoiceBinder在EditMode触发真实播放而调用DontDestroyOnLoad失败。该用例移到PlayMode进行真实生命周期验证，不修改音频运行时或静音掩盖错误；最终Edit42/42、Play7/7实际通过，XML与日志为`.artifacts/mail-ui-followup/final-{edit,play}.{xml,log}`。首次新材质生成用严格Load导致缺文件异常，改为AssetDatabase首次加载/创建后生成通过，失败日志generate5保留。

签名42已构建/验签并临时接入5305，1080实际14状态行为通过；仍未交付最终效果。原生截图显示白描边消失，解析实际TMP Mobile shader bundle发现vertex/fragment各只有4个无OUTLINE的程序，四种所需描边组合全部缺失；材质仍为OUTLINE_ON/.28、原TTF/cmap也支持四字，不归咎fallback。真实42编译RED在`badge-reference/compiled-variants42-red.json`。另外原生有MailCountDigits缺下划线特殊字形警告，新增Editor断言实际RED，保存在`underscore-red.xml`。

43显式收集SourceTextVariants，并在原静态数字图集补入`_`/空格，不换font/atlas/material GUID。最初尝试把SVC作为BindingMap资源引用，被通用层级契约真实拒绝；改为标准Foundation资源收集，不放宽验证、不改AOT。42release保留不可变。实际43包的DX11 vertex/fragment各8个程序，四种OUTLINE组合均存在，`badge-reference/compiled-variants43-green.json`与42红报告对应；原生43确认图也恢复白色细描边。

## 最终验证与包的一致性

- 最终Editor **43/43**、Play **7/7**实际通过：`.artifacts/mail-ui-followup/final43-{edit,play}.{xml,log}`。图形复拍在`after-final43`，独立实包shader检查见上文，未只以SVC.Contains作证明。
- Windows IL2CPP / D3D11真实客户端1920×1080与3840×2160帧缓冲各 **14状态**通过：开发GM自发1封、独立HTTP生产者3封、事件流角标、4封领取、礼包仅一次权威回执、历史、空箱和原页重开。两轮均核对实际增量10书签、2传说石、5上级石、10000金币、100行动力。证据为`native43-1080/verified.json`和`native43-4k/verified.json`及各自`captures/`；4K指游戏帧缓冲，未声称物理桌面显示器为4K。
- 原生关键静态帧04/06/11已由协调者和独立代理实际查看；未发现本轮角标初始化警告或异常。普通启动进程的桌面截图被另一前台应用遮挡，未操作该应用，也未将该图用作登录页证据；普通启动只按进程、D3D11、版本及服务收据验收。
- [源码文件指纹](../../.artifacts/mail-ui-followup/final-source-files43.json)记录构建前86/86匹配产品源；[构建输入](../../.artifacts/mail-ui-followup/build-inputs43.json)431/431与构建镜像/冻结源一致。文档收尾后75项非文档产品文件仍匹配824d；8份验收文档的后续哈希/提交单独记录，不声称当前86项哈希全部未变。会话前356份已有文件哈希未变，另5份原未跟踪文件仍在。
- 构建目录`.artifacts/b43/Win64/hot-v43-d1a911e0f1744dd086893c14cd40d7e2`；发布目录`.artifacts/windows-r43/release`。签名manifest SHA-256为`efc358910dff0e2ab4c131a607ba3892be94bb09a61520fd8601ced1d5225016`，签名文件SHA-256为`dea61fd06f175610f00034fce5d133d3ac6f8cbc3209387096efde50ff8b7944`。完整阶段回执在`package43/execution-verified.json`；它记录包装当时尚未做Native，不回写成事后通过。
- 33个复用文件共180,889,815字节，其中32个静态母包文件180,889,783字节，1个运行标记`ApplicationFootPrint.bytes`32字节。33项跨实际R37/R42/R43客户端逐SHA相同；32静态文件另与R37不可变构建归档核对，运行标记不假称归档文件。baseline `7445597f1ef645db8ef946c4964503fc`，app0.1.27/runtime9.0.1不变。实际游戏exe SHA-256为`8d4c804ff605213a298140be3c172eb407e7f5dc78c5082cdfaabaea7b43ba0e`。无需再打相同母包zip。
- 内容总105项2,284,588,514字节；41→43或42→43清单理论复用94项，变化11项113,618,231字节（约108.35MiB）。保留描边造成shader及其依赖重打包，不是重新下载全量2.28GB。首轮43尚未开启传输标记，**未测得首轮实际下载字节**；4K暖重开前开启标记，内容源`state/server.log`实际0字节，即该轮新增Yoo业务payload为0，不包含签名清单探测。
- 5305本机内容源安全切换到43，旧R41/R42本机助手脚本只做经过身份/签名校验的43转发，旧exe和旧签名release不变。两旧入口分别以普通Limited桌面账户实际启动43，通过D3D11/ready200/最低0.1.27/Mail未登录401/Feed43校验，见`normal-start43/from41/verified.json`和`from42/verified.json`。未声称自动登录账号或全玩法通过。
- 主游戏服务仍是原`.4:7728` R38 Host PID40884，MySQL PID35128及启动时刻未变，无数据库恢复。独立`.6:7728` QA服务仅供本轮专用访客，验收后按PID/ticks/路径/DLL哈希核对关闭；第一次受限会话读不到进程路径而拒绝停止，随后在具备读取权限的会话重核成功，未跳过身份检查。未停止用户其他游戏或主服务。

## 修改位置与复现

主要入口是`Assets/Game/Editor/ArenaUiBuilder.SourceMessageBox.cs`、`.Mail.cs`、`.MailBadge.cs`、`.MailTooltip.cs`、`.SourceTextVariants.cs`；公共资产位于`Assets/Game/Common/Res/Prefab/SourceConfirmation.prefab`、`Styles/RoundedBlue`、`Styles/RoundedGold`与`Res/Materials/SourceTextVariants.shadervariants`。Mail运行时修改为`Assets/Game/Modules/Mail/Scripts/MailWindowBinder.cs`、`MailEntryBinder.cs`、`MailItemDescriptions.cs`；字体/物品框及来源清单在该模块`Res/`。显式shader资源收集在`ArenaUiBuilder.Assets.cs`和`Modules/FoundationDemo/AssetBundleCollectorSetting.asset`。完整路径、M/A及用途见上述86项清单。

先初始化`Tools/ProjectEnvironment.ps1`，仅运行`AstralOath.Editor.ArenaUiBuilder.GenerateMailIntegration`重建相关原预制；不对只读`D:/sevenepic`写入或整目录导入。原图最小导入脚本为`Tools/Arena/ImportMessageBoxButtons.py`、`Tools/Mail/ImportMailBadge.py`、`Tools/Mail/ImportMailTooltip.py`。Unity图形检查沿`Tools/Invoke-Unity.ps1`对`.artifacts/UnityProject`执行，本次完整命令和日志保留于各`.log`。

原生复现脚本`.artifacts/mail-system-review/native-mail/Run-DesktopMail.ps1`，传R43实际Player、隔离`.6:7728`、`-ExpectedRelease 43`及两组`-Width/-Height`；仅显式QA访客运行，常规启动不自动发信。发布、Feed切换、旧入口转发脚本和签名/身份校验位于`package43/`。本轮最后只改文档，无需重跑相同产品构建。

## 未完成与适用边界

- **人工试听未完成**：现有工具不能将音频交给代理实际聆听。图录提供普通/拒绝/确认/取消音频及原录像片段；波形匹配、按钮绑定及真实播放回归已做，不能等同人耳确认音色、音量、尾声。影响是听感仍需用户实听；下一步按图录逐项试听后记录差异。原R41没有对应完整录音，修改前声音证据明确缺失。
- **Native排序提示完整2秒连续视听未拍**：当前是实际计时回归、静态图及播放路径验证；缺原片对应排序状态，未把2秒称原作参数。下一步补一段Native连续录屏/声音并核对替换、关页时机。
- **书签国服逐字说明及单独Native特写未核实**：已定位原日文DB并译为简中，不假称国服原文；未知物品分类/说明保留空值。下一步需要同版本国服书签说明画面，再核字并补Native图。
- **行动力恢复倒计时未实现**：服务端摘要没有权威下次恢复时刻；显示真实余额，不制造计时。补权威时间字段后再实现并验证离线/时差。
- **原作三位角标缺参考**：31/38为一位/两位源宽，48为项目三位扩展，需原作三位画面后复核。
- 本轮没有Android、两台客户端同步、全新机器冷装或所有邮件类型逐像素验收；此前邮件系统267ms附带粒子、聚合顺序等限制仍见[内容41交付](2026-11-27-mail-system.md)。本轮未修改这些范围，不以四处UI修复概括全部邮件表现完全还原。
