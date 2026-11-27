# 邮件提示、角标、物品说明与确认框复核

本轮从9638ea84开始，承接用户实际R41四处截图反馈。实施中，尚未交付新包。保留361项会话前工作树状态于`.artifacts/mail-ui-followup/preexisting-status.txt`，不得混入本轮提交。

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

43候选显式收集SourceTextVariants，并在原静态数字图集补入`_`/空格，不换font/atlas/material GUID。最初尝试把SVC作为BindingMap资源引用，被通用层级契约真实拒绝；改为标准Foundation资源收集，不放宽验证、不改AOT。42release保留不可变，43实际编译组合/Native图、最终传输和入口验收完成后补齐。

内容43候选最终Editor43/43、Play7/7通过（`final43-{edit,play}.{xml,log}`），图在`after-final43`。43测试通过只证明SVC保存/显式收集和客户端行为；实际包shader程序及Native描边尚待本轮最终确认。
