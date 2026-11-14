# 服务端开发与运维

在仓库根目录使用 .NET 10 SDK 和 PowerShell。默认开发监听 `127.0.0.1:5080`。在 MySQL 8.4 创建独立的 UTF-8 数据库，并从本地私密配置加载 `ConnectionStrings__MySql`。迁移账户需要在目标库创建表和读取、写入迁移记录的权限。运行账户需要 `foundation_echo` 的 SELECT、INSERT、UPDATE 权限。启用竞技场后，增加[房间、成员和聊天表权限](../../Server/README.md#模拟战目录房间与竞技场聊天)。远程数据库连接使用验证主机名的 TLS，例如 MySqlConnector 的 `SslMode=VerifyFull` 并配置信任链。

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet restore Server/Tests/AstralOath.Server.Tests.csproj --locked-mode
dotnet run --project Server/Host --no-restore -- migrate
dotnet run --project Server/Host --no-restore
```

迁移独立执行。服务启动后访问 `/health/live` 和 `/health/ready`，后者需要成功读取 Echo 表。修改 `Tools/Protocol/foundation.proto` 后，在 Windows x64 运行 `Tools/Protocol/Generate.ps1` 更新共享 C#。Unity 与服务端使用 Google.Protobuf 3.33.5。

- HTTP `POST /api/foundation/echo` 使用 `application/x-protobuf`，接受版本 1 与最多 8192 字节。成功返回相同媒体类型。请求无效返回 400、媒体类型不匹配返回 415、超限返回 413、ID 与已提交消息冲突返回 409、数据库不可用返回 503。请求处理期限为 10 秒。
- `/ws/foundation` 仅开发环境开放。发送分片或完整的二进制 Protobuf，每条消息最多 8192 字节。回送版本 1，流水号为 0。错误类型、超限和无效请求按 WebSocket 状态码关闭。连接寿命最多两分钟。
- 开发 HTTPS/WSS 可设置 `Server__UseHttps=true`，并用 `dotnet dev-certs https --trust` 建立本机信任，随后通过 `localhost` 访问。使用自有 PFX 可设置 `Server__CertificatePath` 和 `Server__CertificatePassword`。非开发环境强制设置 PFX 并启用 HTTPS，且不会注册开发 WebSocket 路径。使用 `Server__BindAddress` 和 `Server__Port` 配置监听。设置 `preferHostingUrls=true` 或 `Kestrel:Endpoints` 时服务拒绝启动。

## 测试数据库

在专用测试实例或容器中创建空库。可用固定 MySQL 8.4.13 Windows ZIP 在仓库忽略目录中初始化数据，以 `--bind-address=127.0.0.1 --port=3307 --mysqlx=OFF` 运行，设置测试账户并限制到该库。通过私密配置提供该库连接串给 `ASTRALOATH_MYSQL_TEST`，再运行 `dotnet test Server/Tests/AstralOath.Server.Tests.csproj --no-restore`。缺少变量时真实数据库测试显示为跳过。测试执行迁移并写入数据，测试库可在演练后销毁。

## 发布为 Windows 服务

以 Windows x64 .NET 10 SDK 运行 `Server/Ops/Publish.ps1 -Output <新的绝对路径>`，在 Windows Server 2022 目标机安装同系列 ASP.NET Core Runtime。将运行设置保存在发布目录受 ACL 保护的 `appsettings.Production.json` 或由部署系统向该服务注入配置。包含连接串和证书密码的部署文件只放目标机。运行账户需要读取发布目录及证书，以及作为服务登录权限。先使用迁移账户运行发布目录中的 `AstralOath.Server.exe migrate`，再切换为运行账户配置。

使用管理员 PowerShell 执行 `Server/Ops/Install-Service.ps1 -PublishDirectory <绝对路径> -ServiceCredential (Get-Credential)`。注册后的服务为手动启动。配置完成后执行 `Start-Service AstralOath` 并检查 HTTPS 健康探针，维护使用 `Stop-Service AstralOath`。

## 备份与恢复

为备份和恢复分别创建受 ACL 保护的 MySQL 客户端选项文件，用 `[client]` 节配置主机、端口、用户和密码。工具只使用所选文件，不加载用户登录路径。运行期间避免 DDL，以获得一致的 InnoDB 快照。备份 SQL 包含所选库内 Foundation、Arena 的表、数据和迁移历史，不包含切换源库或删除表的语句。

```powershell
Server/Ops/Database.ps1 -Action Backup -MySqlBin <MySQL的bin目录> -DefaultsFile <备份选项文件> -Database astraloath -DumpFile <新备份路径>
Server/Ops/Database.ps1 -Action Restore -MySqlBin <MySQL的bin目录> -DefaultsFile <恢复选项文件> -Database astraloath_restore -DumpFile <备份路径>
```

恢复目标必须是预先创建的空库，操作期间保持该库独占。为恢复创建专用账户，只直接授予目标库的 SELECT、INSERT、CREATE，不授予角色和额外权限。服务器须无强制角色。若 `partial_revokes=OFF`，授权范围中的下划线需转义以匹配字面库名，例如 ``GRANT SELECT, INSERT, CREATE ON `astraloath\_restore`.* TO 'restore_account'@'127.0.0.1';``。若 `partial_revokes=ON`，使用字面库名。仅恢复受信任的备份。恢复后重新运行迁移检查摘要，再切换服务连接串并用原请求 ID 回读流水号。

恢复回归脚本在 Windows 上运行，需要 PowerShell 7.0 或更新版本。使用专用的本机 3307 测试实例时，运行 `pwsh -File Server/Tests/Test-DatabaseRestore.ps1 -MySqlBin <MySQL的bin目录> -TestAdminDefaultsFile <测试管理员选项文件> -ScratchDirectory <测试临时目录>`。脚本使用临时测试库和账户，验证跨库拒绝与正常恢复，并清理创建的测试对象。

## 复用开发 WebSocket 客户端

共享包提供 `AstralOath.Shared.Networking.ProtobufEchoSocket`。每次 `EchoAsync` 仅发送一个请求，并校验响应的版本、请求 ID、消息与开发流水号 0。一个实例同时处理一项操作，默认请求期限 10 秒。失败后该连接会释放，下次显式调用建立新连接。退出时先取消并等待进行中的操作，再调用 `CloseAsync`；`Dispose` 可立即释放套接字。

```csharp
using var socket = new ProtobufEchoSocket(new Uri("wss://localhost:5080/ws/foundation"));
var reply = await socket.EchoAsync(new EchoRequest
{
    RequestId = Guid.NewGuid().ToString("N"),
    ProtocolVersion = FoundationProtocol.Version,
    Message = "hello"
}, cancellationToken);
await socket.CloseAsync(cancellationToken);
```

使用平台证书验证，明文 `ws` 仅允许 loopback。重连仅重建开发回显连接。
