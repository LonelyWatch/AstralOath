# 召唤获得页人物取景修订

用户提供R47伊赛丽亚画面与原版席可画面，要求优化人物位置和大小。已实际查看两图，原图和修前分别保存为`.artifacts/summon-reveal-layout/original-user.jpg`、`before-r47-user.png`。两图人物、窗口比例、黑边与对话状态不同，剔除标题栏和左侧黑边比较构图；不声称同人物逐像素还原。沿用用户明确的免回归要求，修后实机画面/连续过程未执行。

修改前的大立绘容器约高1.55倍画面，再配framing .90，人物头顶和下身被过量裁切。获得页专用framing调整为1.20，投影缩至原75%，脸部纵向锚点保持；子区域横向锚点-.22→-.14，使脸部横向位置约从画面79.9%移至83.4%。此为本项目按参考构图的调整参数，不把它们称为原版源码数值；主页、愿望单及UP技能画面未改。

小人此前只采原骨骼的入场位移差，最终站位却固定为`(-.23*aspect,-.77)*ortho`；每帧按动态包围盒重新定脚底，武器/附件和待机会改变落地位置。现在保留原`character_small`入场结束的完整落点（源单位约-461/-604），loop保持该末态；优先用模型`shadow`地面骨骼，缺失时仅初始化一次包围盒锚点。尺寸高度系数.70→.78，初始包围盒投影约增11.4%，随后不按每帧边界改变比例。无英雄ID特例，未修改共享HeroRosterPortrait默认参数、结算或音频。

## 修改文件

- [获得页布局生成器](D:/UnityProjects/AstralOath/Assets/Game/Editor/ArenaUiBuilder.SummonReward.cs)：立绘子区域横移。
- [召唤Binder](D:/UnityProjects/AstralOath/Assets/Game/Modules/Summon/Scripts/SummonWindowBinder.cs)：本页framing。
- [演出与小人定位](D:/UnityProjects/AstralOath/Assets/Game/Modules/Home/Scripts/SummonPresentation.cs)：源落点/地面锚点、一次性尺寸。
- [生成预制](D:/UnityProjects/AstralOath/Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab)：生成后保持已有GUID，编辑入口为上述生成器。
- Summon/Home README、本记录及变更日志同步。

本轮只执行指定预制生成、编译/热更兼容、签名及本机内容包准备；不运行回归、不启动游戏截图或试听，未完成的是用户接手的新包实际视觉验收。原版席可对话关闭状态和当前伊赛丽亚首次对白状态不同，未据此改对白/按钮或占包文案。

## R48新包

入口：[启动R48](D:/UnityProjects/AstralOath/.artifacts/windows-r48/Play-R48.exe)。内容48/app0.1.27，产品源`68ca3fae5e6f14902f4f3e7c86ba327b5be4ca4a`。指定1份预制生成、HotWindows构建及签名逐payload完整性检查均退出0；沿用R37母包`7445597f1ef645db8ef946c4964503fc`和已就绪R47后端，无强更、本轮不改库。母包文件复用哈希及输入来源记在`.artifacts/windows-r48/package-prepared.json`。

本机5305已协作切48，不结束游戏或未知进程。108项总2,353,878,862字节；R47→48理论差量7项27,022,822字节（约25.77MiB），不是实测下载。新包未启动验收、没有修后图/试听；更新完成退出重开后由用户查看。日志与回执在`.artifacts/summon-reveal-layout/{HotWindows48.log,feed48-final.json,final-receipt.json}`。原361项用户改动保留，未提交两份用户字体。
