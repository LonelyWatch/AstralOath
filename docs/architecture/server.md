# 服务端架构

`Server/Host` 是 .NET 10 ASP.NET Core 模块单体，`Foundation` 持有 Echo 请求处理、存储、迁移及数据库探针。客户端与服务端共同使用 `Shared/com.astraloath.shared/Runtime` 源文件。该目录由 Unity 本地包与 netstandard2.1 项目各自编译。战斗契约与 SplitMix64 随机数流只依赖纯 C#。重放需要保留战斗规则版本、种子和随机数调用顺序。

## ADR 0001 · Foundation 数据访问

使用锁定版本 MySqlConnector 2.6.2 与参数化 SQL，数据库为 MySQL 8.4 InnoDB。Oracle EF Core 10.0.12 的 MySql.Data 26.10.0 要求 Google.Protobuf >= 3.35.0，与 Unity 和共享项目锁定的 3.33.5 产生 NU1107。Foundation 只有一个事务写模型和一个迁移流。显式 SQL 使唯一键、行锁与提交顺序可审查。

Echo 请求 ID 区分大小写，长度为 1 到 64 个 ASCII 字母、数字、下划线或连字符。事务在该 ID 的唯一键上执行插入或无操作更新，再锁定并读取持久化的版本、消息 UTF-8 字节及流水号。并发重试取得同一条记录。同 ID 同版本同消息返回原流水号，同 ID 不同消息返回 HTTP 409 并回滚。未知 Protobuf 字段不参与等价比较。流水号可能跳号，客户端只依赖同一请求的流水号保持稳定。

迁移用独立连接持有数据库级命名锁并按文件名排序执行。记录已应用 SQL 的 SHA-256。MySQL DDL 会隐式提交，所以每个迁移必须支持从中断处安全重跑。运维使用迁移账户预先应用 SQL，服务启动不执行 DDL。运行账户只需访问 `foundation_echo`。
