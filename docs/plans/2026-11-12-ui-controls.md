# 通用 UI 控件继承迁移实施计划

> 执行：用户已批准直接改造并增加右键 UI 创建入口；按文件所有权并行实施，协调者负责验证与提交。

**Goal:** GameText / GameButton 自身替代 TMP / Button，Hierarchy 右键 UI 可直接创建项目控件。

**Architecture:** Common 的 AOT 控件浅继承官方类型，保留 Bind 契约和生命周期；普通 C# UIBaseView / Binder 不变。现有预制体迁移保留 GUID、显示参数与引用，生成器不再生成双组件。

**Tech Stack:** Unity 2022.3.62f3、TMP 3.0.7、UGUI 1.0.0、HybridCLR。

**Spec:** 本会话用户“改，并在右键的UI创建栏中新增这两种组件的快捷创建方式”。

## 全局约束

- 在主工程 main 完成，中文提交；只用既有依赖。
- 不改第三方代码，不重建已有 meta GUID，不重建整个已编辑预制体。
- 控件负责通用显示/交互，业务保留 C# Binder；Common 不引用业务模块。
- 修改 Common/AOT 后需要新母包；本轮不发布线上资源。

## 检查重点

- 已有字体、材质、布局、Button 事件、InputField 和 BindingMap 引用不得丢失。
- 继承后的 OnDisable / OnDestroy 必须调用基类，保留渲染与 Selectable 生命周期。
- 绑定不能覆盖作者设置的换行、溢出、富文本与样式。
- 创建菜单支持父节点、Canvas、撤销和 Prefab Mode，不污染其他场景。
- 快速点击、关闭取消、再次绑定与键盘提交保持可用。

## 任务

- [x] 先新增并实际运行失败的 PlayMode 控件与 EditMode 菜单用例。
- [x] 改造 Common/GameText.cs、GameButton.cs；更新两个场景生成器与测试构造方式。
- [x] 增加 Editor/GameUICreationMenu.cs 与相关测试；只创建项目组件。
- [x] 精确迁移已有场景/预制体并验证字段、内部引用与 GUID。
- [x] 运行 Unity EditMode / PlayMode、结构检查，完成独立审查。
- [x] 更新 UI 使用说明与架构决策；记录验证及母包边界后中文提交并推送 main。
