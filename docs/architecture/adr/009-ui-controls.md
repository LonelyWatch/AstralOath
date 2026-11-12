# ADR 009：通用 UI 控件直接继承 TMP 与 Button

日期：2026-11-12。状态：用户已确认。

## 决策

GameText 继承 TextMeshProUGUI，GameButton 继承 UnityEngine.UI.Button。每个文本/按钮只保留一份对应控件；RectTransform、CanvasRenderer、Image 等显示依赖继续使用 Unity 原生组件。通用控件属于 Common/AOT，业务逻辑继续由普通 C# UIBaseView 与 Binder 组织。

## 原因与取舍

项目需要统一制作大量界面。直接继承可将原属性与项目行为集中到同一组件，避免漏挂附加脚本；代价是维护基类生命周期以及迁移序列化引用。沿用官方 Inspector、排版、布局、导航和交互，不复制第三方源代码、不新增通用控件继承层。

GameText.Bind 只更新本地化内容与订阅，不覆盖作者设置的字体大小、换行、溢出和富文本。默认排版在 Editor Reset / 创建入口设置。GameButton.Bind 保留单次异步操作互斥、关闭取消和解绑。OnDisable / OnDestroy 使用 try/finally 保证基类清理执行。

## 制作与迁移

Hierarchy 右键 UI 提供“项目文本 (GameText)”和“项目按钮 (GameButton)”，支持当前父节点、Canvas、EventSystem、Undo 与 Prefab Mode。菜单占位使用默认字体可显示的 New Text / Button，实际业务文案由本地化 key 提供。

现有两份场景与两份预制体共 63 对控件迁移：保留项目组件 fileID、原生控件完整序列化配置和全部 meta GUID；删除冗余原生控件，映射搜索输入框对原文本组件的引用。生成器同步使用项目组件，避免再次生成双组件。

## 发布边界

继承关系和 Common/AOT 已改变，必须重建并分发母包，不能仅向旧母包发布新预制体或业务 DLL。后续兼容母包的 UI 业务与资源继续使用既有 HybridCLR / YooAsset 流程。
