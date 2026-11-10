# FoundationDemo

`Scripts` 定义窗口与 DTO 适配，`Lua` 保存演示 Binder 及导出契约，`Configs` 保存 Luban 演示生成物，`Res` 保存字体、窗口预制体与示例资源。场景位于 `Scenes/FoundationDemo.unity`。

YooAsset 包 `Foundation` 提供四个地址。

- `foundation/demo-binder` 提供 xLua 文本与白名单导出。
- `foundation/demo-config` 提供 Luban 生成的演示行。
- `foundation/demo-localization` 提供 Luban 生成的简体中文业务文案。
- `foundation/demo-resource` 提供可见的文本资源。

窗口每次打开建立新作用域，关闭时取消请求并释放资源、事件和动画。演示文案使用 key，默认为简体中文，语音选择独立。详细启动、更新和构建步骤见 `docs/architecture/client.md`。
