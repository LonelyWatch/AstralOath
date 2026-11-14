# 技能演出编辑器交互与尾段修订

日期：2026-11-14。修改前代码基线：5ca702b。主工程 D:/UnityProjects/AstralOath 的 main；保留场景站位、字体及资源导入 meta 的原有未提交改动。

## 问题与原因

- 旧列表固定165像素，技能槽与完整演出ID挤在一行，选中通过禁用变灰；三栏无独立间距。时间轴被限在中栏，缺少显式聚焦、看全和播放头定位。
- 时间轴选中通过 JObject 引用识别。向前播放保持同一批节点，向后拖动则重新构建播放器和 Cues；旧引用不再指向轨道，导致高亮丢失、摘要回到0秒，F聚焦可能异常。此前只在完整场景 rebuild 时重绑，遗漏普通Seek、循环和从尾部重播。
- 亚敏 S3 角色动作5.1秒加jump_back 0.9666秒；模型事件1.552秒发出 aming_skill_03_front。其bubble持续发射3秒、粒子最大生命7秒，完整寿命到11.552秒。
- 亚敏 aming_skill_03_patis 与科尔布思 corvus_skill_03_pati1 含多条独立colorBlends。旧ReadRamp串接各组，遇到第二条从0开始时报 Unordered HUD particle color key，发射器未生成，但时长已计入，形成不可见尾段。
- 雅卡泰丝 S2实测3.733秒：角色动作3.5333秒，源镜头末段0.2秒；当前样本未复现超长空尾。不硬剪其原始时长。

## 实现与通用约束

三栏保持独立间距与可调宽度，下方时间轴全宽，可调高度。播放/暂停、循环、声音、速度和逐帧位于时间轴上方。技能卡片与轨道均有明确选中样式；轨道显示编号，参数区显示选中摘要。

F聚焦、A看全、C播放头定位；指针锚定缩放、中键平移、固定帧标尺。片段、标尺、分隔线分别持有拖动状态，捕获鼠标并在失焦时释放，防止跨区域拖动抢事件。输入文字时不抢键。

所有预览时间移动走同一入口。只有Cues集合实际改变时，才按GUID或模型事件内容+出现序号恢复选中，避免每个播放帧重新遍历/序列化所有节点。选择、聚焦、前后拖动不写演出覆盖。当前模型只读事件身份不承诺跨完全相同父ANI的时间重排，详见操作文档。

编辑器提供主演出与含余辉两范围，完整Seek与CFX生命周期保留。主范围由播放器同源时间推导，保留源依赖、镜头、Spine与发射时长，不用角色ID特判。实战等待时序本轮未变。多渐变按组解析，每粒子出生选一次，保持确定性重放；精确源引擎选组策略待对照。

## 验证

交互、时长与粒子联合 EditMode 60/60 通过；合并角色表现和近战距离回归后，EditMode 72/72 通过。桌面操作由用户停止，没有补做拖动和窄窗口的手动验证。失败日志位于 D 盘主工程 .artifacts/timeline-ux。

失败证据：
- navigation-red.xml：初版缺少聚焦/锚定缩放且选中画外节点不滚入视野。
- duration-red.xml：亚敏完整11.552秒与主演出6.0666秒未区分。
- duration-empty-tail-red.xml/.log：7秒不存在源bubble粒子，并有多渐变倒序报错。
- scrubbing-red.xml：真实拉斯S1和亚敏S3前进到90%再回到10%时，选中节点不属于当前Cues，2项失败。

统一命令：

~~~powershell
& ./.tools/powershell/pwsh.exe -NoProfile -File Tools/Invoke-Unity.ps1 -Mode EditMode -ProjectName UnityTimelineLanes -Graphics -TestFilter 'BattleSkillTimeline|BattleParticleColorRamp|BattleDeathPresentation|BattleCfxAnimationSelection'
~~~

范围：Unity Editor专项，不代表Player平台验收或所有角色还原通过。编辑器代码不进Player；BattleHudParticles粒子修复属于业务程序集，按现有HybridCLR流程发布，目标平台更新包本轮未构建。没有AOT、协议、共享战斗规则版本变更。
