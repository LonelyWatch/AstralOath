# FoundationDemo

`Scripts` 定义 `FoundationEntry`、普通 C# `FoundationWindow` 与 `FoundationWindowBinder`，`Configs` 保存 Luban 演示生成物，`Res` 保存字体、窗口预制体与示例资源。场景位于 `Scenes/FoundationDemo.unity`。

YooAsset 包 `Foundation` 提供下列业务资源。构建另外生成代码清单、业务程序集和母包 AOT 补充元数据。

- `foundation/demo-config` 提供 Luban 生成的演示行。
- `foundation/demo-localization` 提供 Luban 生成的简体中文业务文案。
- `foundation/demo-resource` 提供可见的文本资源。
- `foundation/foundation-window` 提供只挂通用 BindingMap 的窗口预制体；普通 C# FoundationWindow 通过 UIViewRegistry 注册、加载并访问其组件。

窗口每次打开建立新作用域，关闭时取消请求并释放资源、事件和动画。演示文案使用 key，默认为简体中文，语音选择独立。详细启动、更新和构建步骤见 `docs/architecture/client.md`。

`AssetBundleCollectorSetting.asset` 同时保存 CharacterLab 包的收集配置。十名角色接入后，角色实验室组由导入清单重新生成，继续使用原稳定地址和角色分包规则。见[十名角色接入](../../../../docs/delivery/2026-11-13-ten-hero-roster.md)。

共享的 YooAsset 收集器配置另有 CharacterLab 包，梅赛德丝基础皮肤 S3 的 21 帧和帧时长描述已登记在该包。详情见 [立绘回归](../../../../docs/delivery/2026-11-14-mercedes-s3-cutin.md)。

CharacterLab 的 YooAsset 收集设置由编辑器按导入清单维护，本次增量包含 c1010 的依赖。

卡琳 c1011 已接入角色内容；三技能、音频及已知缺项见[角色扩充手册](../../../../docs/development/battle-roster-expansion.md)。
