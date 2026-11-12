# 通用文本与按钮迁移验收（2026-11-12）

## 交付

- GameText 直接继承 TextMeshProUGUI；GameButton 直接继承 Button。
- Hierarchy 右键 UI / 项目文本 (GameText)、项目按钮 (GameButton)，顶部 GameObject/UI 同步可用。
- 新建控件支持父节点、Canvas、EventSystem 去重、Undo/Redo 和 Prefab Mode；保留官方 Inspector。
- 两份场景与两份预制体共 63 对控件已迁移，原 GUID、配置、BindingMap 与输入框引用保留；生成器同步使用新组件。
- GameText 绑定保留作者排版设置，控件禁用/销毁调用基类清理，C# Binder 契约保持不变。

## 已验证

- Unity 2022.3.62f3 隔离工程 EditMode：215/215，通过。
- Unity PlayMode（启用 Graphics 与截图用例）：16/16，通过，零失败、零跳过。
- 菜单 8 项用例移除实现时均因缺少菜单失败，恢复实现后通过。
- 新增文本/按钮用例在旧包装实现上失败，迁移后通过；覆盖组件唯一性、作者排版、语言订阅、基类注册清理、关闭取消、重复点击、重绑与键盘提交。
- 项目结构验证：15 个程序集、0 错误；结构验证器测试 6/6，通过。
- 独立审查逐项核对 63 对控件序列化字段与引用映射，确认非控件配置无额外变化、原 meta GUID 未变、无悬空跨资产引用。
- 原有字体测试改为基类匹配，继续校验 GameText 中文字体与材质。

第一次 PlayMode 复验有 15 项通过，按需截图用例因未设置输出目录跳过，严格脚本将整体标记未通过；随后设置 ASTRAL_ANIMATION_CAPTURE_DIR 重跑，最终为 16/16。截图是角色预览 RenderTexture，不代替右键菜单或 Inspector 的人工界面验收，也不作原作动画还原结论。

本地证据：.artifacts/unity/EditMode.xml、PlayMode.xml、UIMenu-Red.xml、UIControls-Red.xml；.artifacts/ui-controls-migration/report.json 与原始资产备份。证据、缓存及截图不提交 Git。

## 发布与未执行项

本轮未重新构建 Windows/Android Player，也未发布线上更新。Common/AOT 基类发生变化，已有安装包必须重新构建母包；不能仅向旧母包推送新预制体。用户可在 Unity 编译完成后通过上述右键菜单制作界面。
