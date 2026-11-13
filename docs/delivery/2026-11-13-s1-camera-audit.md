# S1 跳跃窗口与镜头突变调查（2026-11-13）

## 源结构及复现

本轮调查用户指出的拉斯、安洁莉卡S1进入攻击/回位镜头突变。读取已导入且保留原字段的 `c_ras_skill_01.graph.json`、`c_angelica_skill_01.graph.json` 及对应原版本骨骼；不修改只读来源或生成图。

两者都以 ANI(jump_forward/jump_back) 的开始边进入 ANI_SCOPE(jump→landing)，再由scope完成边连接style2 MOVE、CAM、ZOOM。原CAM/ZOOM省略time，进入ZOOM都带 `0.2911877,0.9548611,1,1` 曲线。参数有区别：拉斯倍率1.3、CAM y=-30；安洁莉卡倍率1.4、CAM x=-20/y=-30。它们共享调度语义，不能抹平为同一镜头参数。

真实骨骼事件（单位秒）：拉斯jump_forward .4666→.8666，jump_back .0666→.4666；安洁莉卡jump_forward .3333→.7333，jump_back 0→.4333。返回片段整体发生在前向与攻击片段之后，以上是各片段自身事件时间。

原ReadCues只将style2 MOVE提前到jump并继承窗口时长；CAM/ZOOM仍落在landing。Stage把缺省time当0，因此在人物落地时突然移动/变倍，回位同理。这是实际生产调度链中的共有缺口，不能靠每帧相机全局平滑掩盖，也不能更换原SHAKE曲线。

## 用户指定录像的有限旁证

实际查看用户此前指定的 [安洁莉卡录像 BV1VB4y137Dt](https://www.bilibili.com/video/BV1VB4y137Dt/) 的9.0–12.9秒连续帧；其发布日期为2022-09-06，属于旧版本旁证，不能标为当前版本。前跳期间背景/木桩连续平移和变大，回跳期间连续返回，未见等落地才单帧切镜的表现。支持当前源结构解释，但不能仅由10fps采样确定原引擎精确曲线、全部time缺省语义或最新版一致性。联系图位于 `.artifacts/camera-visibility-reference/angelica-s1-009-013.jpg`，仅D盘本地保留。

## 修复边界

对有效ANI_SCOPE完成边上的CAM/ZOOM，仅在源time缺失时沿用该命名窗口的起点与时长；显式time=0仍是源切镜，显式正时长保持原边调度，when=1与边delay不被吞掉。注入执行时长使用独立节点副本，不写回源图或共享节点。角色倍率、偏移、曲线、摇镜仍取各自源数据。

这是当前原图结构、用户观察及近战模式支持的解释；不宣称已得到原引擎所有CAM缺省参数的源码定义。未知/无有效事件窗口不凭空加固定时长。不做角色ID分支，不把该解释推广到无scope的全部镜头。

## 回归与验收状态

新增 `BattleScopedCameraTests`：两角色前向/返回4例，显式0/150ms两例，when=1与70ms边delay一例，真实源生成CAM/ZOOM的120Hz轨迹两例。实际读入两套角色骨骼，由原内核取得时长和事件；轨迹检查进入jump后已开始平移、落地无单帧跳变。协调者已在隔离 Unity 实际执行修复前红测：9 项中 2 项通过、7 项失败（`.artifacts/scoped-camera-red.xml`）。显式 0/150ms 两项通过；其余失败定位到真实调度缺口：拉斯前进 CAM .8666s（应 .4666s）、返回 2.3665s（应 1.9665s）；安洁莉卡前进 .7333s（应 .3333s）、返回 1.8999s（应 1.4666s）。两角色空中相机位移实际为 0。最小生产修复后，协调者运行隔离 Unity 全量 EditMode：427/427 通过，其中本组 9/9 通过（`.artifacts/camera-visibility-edit-full.xml`）。实际结果 XML 已复核，前进/回位时序、显式时长保留、源图不可变与 120Hz 轨迹断言均通过。

完整角色/镜头/显示对象画面、暂停/中断/接力及目标平台由协调者集中验证；数值连续不能替代原作逐帧视觉还原。

## 追加：同帧镜头切换后的血条投影

在实际截图复核中进一步发现：状态条的显隐正确，但镜头改变后的首帧位置发生偏移。只把截图顺序改成 ForceUpdateCanvases→Tick→ForceUpdateCanvases 仍不能消除。固定输出/aspect/viewport 的首轮 4 项测试仅调用 ForceUpdateCanvases，4/4 通过；这不是有效红测，也不能证明渲染正确。

随后在断言前调用真实 Camera.Render，4/4 失败（`.artifacts/camera-status-order-render-red.xml`）。渲染前锚点误差均为 0；渲染后固定 RT 平移误差 109.3632px、平移加缩放 194.5855px；屏幕内嵌 viewport 平移误差 105.4552px、平移加缩放 174.4604px。Canvas 世界位置在 Render 时才从 (0,0,0) 变为 (2,-.75,0)，缩放也同时改变。这证实是生产投影顺序缺陷，不能仅归因于截图换分辨率。

`BattleUnitStatusPresentation` 原来用当前镜头投影，然后通过尚未同步的 ScreenSpaceCamera Canvas 世界矩阵定位；渲染阶段 Canvas 更新又把状态条移动/缩放一次。修复改为根 Canvas 局部坐标的屏幕投影，尺寸使用根 rect 与像素矩形及父到根的相对缩放，不读取待更新的 Canvas 世界缩放，不逐单位调用全局刷新。WorldSpace Canvas 保留原射线算法；屏幕 viewport 与非中心 pivot 由 rect 映射处理，RT 按整张纹理处理。

新增回归保留真实 Render 后的首帧与立即复位位置/尺寸检查。修复后 4/4 实际渲染回归通过，渲染后锚点误差为 0–0.0000153px（`.artifacts/camera-visibility-edit-final.xml`）。该次全量为 430/431；唯一剩余项是旧缩放夹具在 Render 之前读取 Canvas 世界宽度，已改为真实 Render 后比较，等待最终全量复验。官方参考：[Canvas 更新时机](https://docs.unity3d.com/ja/current/ScriptReference/Canvas.ForceUpdateCanvases.html)、[RenderTexture 忽略屏幕 rect 的行为](https://docs.unity3d.com/cn/2022.2/ScriptReference/Camera-targetTexture.html)。本机 Unity 2022.3 的故障结论来自上面的实际渲染测试。
