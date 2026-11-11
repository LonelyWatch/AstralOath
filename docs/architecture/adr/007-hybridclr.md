# ADR 007 — 客户端采用 HybridCLR

状态：已决定，2026-11-11。用户明确授权以 HybridCLR 迁移客户端热更并移除 xLua。此前地基计划和交付记录保留当时的架构与验收结果。

客户端业务使用普通 C# View/Binder，减少 Lua 与 C# 间的导出、桥接和数据适配。采用 HybridCLR Unity 包 9.0.3（提交 `6cadd24d101f4cbd00cd80190106f6621132d24d`）和配套 runtime 9.0.1。原生源码的提交和归档校验值见[依赖锁定文件](../../../Tools/dependencies.lock.json)。HybridCLR Unity 包使用 MIT 许可证，原文与内嵌 UnityHook 的许可证分别随包保存。Unity 2022.3.62f3、YooAsset、Luban、DOTween、.NET 10 和 MySQL 8.4 沿用现有选型。

- 稳定 AOT 包含 Bootstrap、通用 UI 组件、启动契约、验签、下载、资源访问和失败恢复。启动器通过 `IHotUpdateEntry` 进入业务，AOT 和 Common 禁止静态引用热更业务。
- 客户端业务程序集包含 C# View/Binder 及应用流程。预制体通过通用 `BindingMap` 保存组件引用，业务类由代码创建。
- `AstralOath.Battle` 是纯 C#，独立于 Unity、网络与数据库。客户端可随兼容母包加载，服务端编译同一份源并独立发布。战斗权威在服务端，协议与规则变更需协调兼容。
- 迁移先发布新 IL2CPP 母包。每次母包构建自动生成并固化 GUID。后续热更从归档读取身份，按平台和构建变体使用同一母包裁剪后的 AOT DLL。AOT 契约、运行时或内核不兼容时构建新母包。程序集只能在新进程替换，代码启动失败后由下一次冷启动恢复。
- 同批业务程序集、配置、本地化和资源通过 YooAsset 分发，由 schema 3 签名清单覆盖。首次及后续发布步骤见[热更与强更构建](../../development/hot-update.md)。
