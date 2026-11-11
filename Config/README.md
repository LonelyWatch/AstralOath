# 演示配置

`Schema/foundation.xml` 定义三张表和引用关系，每张表只有一个 `Tables/*.csv` 数据源。改动后从仓库根目录运行 `./Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient`。脚本使用锁定 SHA-256 的 Luban v5.1.0，首次运行需要 GitHub 下载权限。它以 .NET 10 运行，允许 Luban 的 .NET 8 入口跨主版本运行。

Luban 的 `.7z` 包由项目内的 7-Zip reduced console 解压，版本、官方地址与 SHA-256 固定在 `Tools/dependencies.lock.json`。导表时按需下载至 `.tools/7zip-<版本>/7zr.exe`，每次执行前校验哈希，无需全局安装。Windows Server 2022 自带 `tar` 不支持该包的 LZMA 编码，因此本地与 CI 均使用这一固定解压器。

- `demo_ui.csv` 的记录 1 是演示界面。修改停留秒数时保持在 1–30。标题、消息和资源使用表内引用。
- `demo_localization.csv` 维护演示界面、按钮及业务状态的简体中文文本。稳定 key 由配表或 C# Binder 引用。保留带参文案的格式占位符。语音选择由客户端单独管理。
- `demo_assets.csv` 维护演示地址和对应的项目源文件。新增地址时应在 YooAsset 收集配置中使用同一地址。

Luban 以严格模式校验范围、引用与主键。脚本另外校验文案 key 格式、中文文本和项目资源路径。输出位于 `.artifacts/config` 的 `json`、`bin` 和 `csharp`。C# 入口是 `AstralOath.Config.Tables`，接收按表名打开 Luban `ByteBuf` 的委托。访问器验收用官方示例中固定提交的运行时源码编译，逐表对照本次生成的 JSON 检查二进制读回数据，并检查 UI 行引用。

`-SyncClient` 将生成的 UI 与本地化 JSON 同步到 FoundationDemo 的 `Configs`，供 Unity 导入和 YooAsset 收集。`-CheckClient` 检查仓库快照与生成结果一致。配表回归入口为 `pwsh -File Tests/Content/ConfigSmoke.ps1`，在独立副本验证新增引用、文案修改及非法时长。
