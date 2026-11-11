# HybridCLR 迁移验证记录

截至 2026-11-11 的迁移验证结果。临时测试密钥、安装包、日志和故障注入源码均留在被忽略的 `.artifacts` 目录，未进入正式源码。

## 已通过

- Node 结构检查用例 6 项。
- .NET 内容行为测试 18 项。
- .NET 服务端测试 10 项。另有 1 项 MySQL 集成测试因未提供测试连接串而跳过。
- Unity 2022.3.62f3 批处理 Compile 和项目本地 InstallHybridCLR 通过。
- Unity EditMode 55/55，包括入口加载失败后的同进程重试拒绝，以及 AOT 泛型需求分析未收敛时拒绝构建。
- Unity PlayMode 7/7。

Unity 的本地日志与结果位于 `.artifacts/unity`。项目使用 HybridCLR Unity 包 9.0.3 和 runtime 9.0.1。

## Windows Player 已通过

- Windows x64 IL2CPP 母包实际构建成功，完成实际裁剪 AOT 哈希核对与归档。测试母包身份为 `19353c6af6eb4034b9b8beece8716107`。
- 内置内容启动成功。远端返回 404 时能使用安装包内置代码和资源。
- 保留同一个 EXE，独立构建并签名发布版本 2，只在隔离项目修改纯 C# BattleRandom 返回值。新进程从热更包加载后，固定种子输出从 `910a2dec89025cc1` 变为 `910a2dec89025cc0`，日志确认 `release=2 recovered=False`。正式源码的随机算法保持原样。
- 破坏测试发布包签名后，客户端拒绝它并使用已验签的本地版本 2，日志确认 `recovered=True`。
- 版本 3 注入入口构造失败。第一次启动明确退出失败；下次进程启动隔离版本 3，即便服务端仍发布版本 3，也恢复到健康版本 2。未在同一进程尝试卸载或替换程序集。

证据位于 `.artifacts/hybridclr/player-builtin.log`、`player-hot-v2.log`、`player-bad-signature.log`、`player-failed-entry-v3.log` 和 `player-cold-recovery.log`。这些自动化启动测试使用无图形模式，不能替代设备画面与性能验收。

## Android 构建已通过

Android ARM64 ASTC 和 ETC2 的完整 APK、独立热更包均已构建成功；各自包含完成标记与独立母包归档。日志为 `.artifacts/unity/AndroidAstc.log`、`AndroidEtc2.log`、`HotAndroidAstc.log`、`HotAndroidEtc2.log`。

## 尚未通过或未执行

- `adb devices` 未发现设备，Android 安装运行和旧母包冷启动热更尚未验证。
- 本轮 Windows 图形模式回显联调已尝试，但本机服务端未配置运行中的 MySQL，返回 503，未得到回显成功及截图验收。此限制不计为通过；与服务端测试中的真实 MySQL 跳过项一并保留。
- 错误母包、平台、架构、DLL 哈希及依赖关系已由共享测试覆盖；这些错误的逐项目标 Player 注入演练尚未全部执行。
- Android 真机内存、泛型补充元数据开销、运行性能及实际业务玩法还原仍属后续验收。

本记录只证明地基与列出的热更行为，不代表《第七史诗》战斗玩法、角色技能或 BP 流程已经还原。
