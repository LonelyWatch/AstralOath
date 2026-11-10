# 演示配置

`Schema/foundation.xml` 定义三张表和引用关系，每张表只有一个 `Tables/*.csv` 数据源。改动后从仓库根目录运行 `./Tools/Config/Generate.ps1 -VerifyAccessors`。脚本使用锁定 SHA-256 的 Luban v5.1.0，首次运行需要 GitHub 下载权限。它以 .NET 10 运行，允许 Luban 的 .NET 8 入口跨主版本运行。

- `demo_ui.csv` 的记录 1 是演示界面。修改停留秒数时保持在 1–30。标题、消息和资源使用表内引用。
- `demo_localization.csv` 维护稳定文案 key 和中文文本。语音选择由客户端单独管理。
- `demo_assets.csv` 维护演示地址和对应的项目源文件。新增地址时应在 YooAsset 收集配置中使用同一地址。

Luban 以严格模式校验范围、引用与主键。脚本另外校验文案 key 格式、中文文本和项目资源路径。输出位于 `.artifacts/config` 的 `json`、`bin` 和 `csharp`。C# 入口是 `AstralOath.Config.Tables`，接收按表名打开 Luban `ByteBuf` 的委托。访问器验收用官方示例中固定提交的运行时源码编译并读回生成的二进制。
