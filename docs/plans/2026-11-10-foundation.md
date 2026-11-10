# AstralOath 首次地基实施计划

用户已批准实施并推送 `origin/codex/foundation`。地基完成后才进入真实战斗场景及用户监督验收。

## 全局约束

Unity 2022.3.62f3 / URP 14.0.12 / TMP 3.0.7 / UGUI；.NET 10 模块单体；MySQL 8.4 InnoDB；Android 10 ARM64、Windows 10/11 x64；Windows Server 2022 2C4G。
共享纯 C# netstandard2.1，Unity 通过本地 UPM 包引用。中文 UI、日文语音独立。YooAsset + Luban + xLua（只更新客户端 UI/业务）+ 免费 DOTween；Unity MCP 仅开发使用。依赖锁版本。
原始资源只读，按模块依赖引入。国际服仅手动追踪。真实战斗规则、BP、演出、服务器容量不作未经实测的保证。无生产部署。

## 可独立检查的任务

1. 仓库及依赖：建立开发分支、锁定工具和包、保留许可证、模块边界及 AI 约定。
2. 服务端与共享协议：协议/战斗契约、HTTPS/WSS 示例、MySQL 迁移/事务/幂等/健康检查、运维脚本和行为测试。
3. 客户端：服务生命周期、View/Lua Binder、通用 TMP/Button、窗口、音频、本地化、动画、资源及网络适配；可运行基础设施场景。
4. 配表与更新：Luban 配置生成、资源清单和引用验证、签名/校验/断点恢复/磁盘检查/原子切换；失败恢复测试。
5. 集成与交付：真实依赖导入，.NET 和 Unity 测试，客户端到服务端数据库闭环，Windows/Android 构建；独立代码审查、修复、提交和推送。

## 接口约定

- 包 `Shared/com.astraloath.shared`：运行时代码命名空间 `AstralOath.Shared`；程序集 `AstralOath.Shared`。服务端 project 引用同一 Runtime 源目录。
- Protobuf 命名空间 `AstralOath.Protocol`：`EchoRequest { request_id, protocol_version, message }` / `EchoResponse { request_id, protocol_version, message, sequence }`。协议版本初始为 1。
- `POST /api/foundation/echo`：application/x-protobuf，请求 ID 用于数据库幂等，响应相同格式。开发 WebSocket `/ws/foundation` 使用同一编码并要求版本一致。
- 默认本地服务监听 loopback，客户端基础 URL 可配置。生产 HTTPS/WSS 需要真实证书配置；不得跳过证书校验。
- 演示不依赖原作角色或声音。演示配置使用稳定文案 key；原作资产和中文文本缺口保留在后续验收清单。
- 更新核心位于共享包 Runtime/Updates；独立于 Unity，资源运行时加载由 YooAsset 适配层负责。

## 验收

服务事务/幂等及恢复；窗口解绑、关闭取消、重复点击及动画复用；配置引用校验；更新篡改/坏签名/断网/空间不足/路径越界/进程重启恢复；Windows 和 Android IL2CPP 构建。真实检查结果记录到交付报告，不能用测试替代未执行的目标环境演练。

## 推送规则

协调者按任务提交，通过检查后推送同名开发分支；不合并 main、不强推、不部署。若环境限制导致个别验收无法执行，保留明确限制和复现命令，不标记全部完成。
