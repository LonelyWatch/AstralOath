# 配置与签名内容

配置源和生成方式见 [Config/README.md](../../Config/README.md)。客户端的 YooAsset 包构建产物可以与 Luban 数据一起发布。更新核心只依赖 netstandard2.1，安装结束后从 `ContentRelease.DirectoryPath` 读取已校验的内容。

## 构建和验签

从仓库根目录使用 PowerShell 7。开发密钥命令在本地忽略目录写入 PKCS#8 私钥 `private.key` 、SPKI 公钥 `public.pem` 和客户端可直接用 RSAParameters 导入的 `public.json`。可信公钥通过客户端构建或可信配置分发。实际签名密钥由发布端保管。

```powershell
pwsh -File Tools/Content/Release.ps1 -Action NewDevKey -Key .artifacts/keys/dev
pwsh -File Tools/Content/Release.ps1 -Action Build -Key .artifacts/keys/dev/private.key -PayloadRoot .artifacts/payload -Provenance .artifacts/provenance.json -Release .artifacts/releases/1 -Version 1
pwsh -File Tools/Content/Release.ps1 -Action Verify -Key .artifacts/keys/dev/public.pem -Release .artifacts/releases/1
```

`provenance.json` 的 `files` 数组逐项声明 `path`、`origin`、`sourcePath`、`sourceSha256` 和 `dependencies`。例如：

```json
{
  "files": [
    {
      "path": "config/demo_ui.bytes",
      "origin": "project",
      "sourcePath": "Config/Tables/demo_ui.csv",
      "sourceSha256": "<Get-FileHash 输出的小写 SHA-256>",
      "dependencies": []
    }
  ]
}
```

`path` 指向构建产物目录下的单个文件。依赖使用清单中的产物路径，须存在且无循环。工具逐项核对源文件哈希，只打包明确列出的产物。来源为 `project` 时，路径相对仓库且位于 Assets 或 Config。来源为 `sevenepic` 时，路径相对只读 `D:/sevenepic`。原始资源仅供核对来源，不批量导入。国际服版本追踪由用户手动触发。

发布目录中，`manifest.json` 的原始 UTF-8 字节使用 RSA-PKCS#1 v1.5 / SHA-256 签名，`manifest.sig` 保存原始二进制签名。清单见 [release-manifest.schema.json](../../Tools/Content/release-manifest.schema.json)。文件相对于清单 URL 所在目录发布。服务端可支持 HTTP Range，客户端也接受服务器忽略 Range 后返回的完整响应。生产端点使用 HTTPS。

Unity Foundation 的发布用包构建报告限制签名范围，核对暂存 YooAsset 清单与包内哈希、bundle 大小和哈希、源资产与依赖。下例以 Windows 包为例，Android 使用自己的 BuildTarget 产物和发布路径。

```powershell
pwsh -File Tools/Content/Package-Unity.ps1 -PackageDirectory .artifacts/UnityProject/.artifacts/yooasset/StandaloneWindows64/Foundation/foundation-1 -Key .artifacts/keys/dev/private.key -Release .artifacts/releases/foundation-win-1 -Version 1
```

该命令将可运行文件发布到 `yooasset/Foundation/`。客户端把已校验版本的这一目录传为 YooAsset 的 packageRoot。JSON 公钥包含 Base64 编码的 `modulus` 和 `exponent`，签名和验证仍使用同一把 RSA 公钥。演示协议与配置版本均为 1，整数发布版本独立于 YooAsset 的包版本字符串。

## 安装和恢复

更新器先验证清单签名，然后读取版本和路径。客户端协议与配置版本均须精确匹配。每份清单和每个文件有独立的响应大小上限。相同清单的断点文件保存在 staging，重启重试时重新检查偏移、总大小及最终哈希。清单请求最长 30 秒，文件请求闲置超过 60 秒取消。客户端提供会话取消令牌，并复用自己持有的 HttpClient 和可信 RSA 公钥。

更新器以同卷的剩余负载加 8 MiB 安全余量作磁盘准入检查。它通过文件锁串行化更新，避免本更新器的并行任务重复占用预算。其他进程仍可占用磁盘空间。有效负载完整后移动到按版本命名的独立目录，再原子替换记录当前和上一个版本的指针。进程中断后旧指针仍可读取，已下载内容可由下一次安装重用。指针操作依赖文件系统的同卷原子替换，断电持久性取决于系统和文件系统。

启动用 `GetActiveRelease(token)` 读取已签名的安装版本，加载时保持该目录只读。对大包应在后台读取和验证，再由 Unity 主线程切换加载入口。安装时可传入候选校验回调，先验证包能否加载 Lua 和配置。回调失败或取消会保留旧指针。显式回滚重新验证旧版内容和兼容性，仅切换内容指针。同一发布版本的本地文件损坏时，可用相同的签名清单重新下载。完整校验后，旧目录移入隔离区，原版本号和回滚指针保持不变。源清单损坏导致无法核对发布身份时，安装会拒绝覆盖。旧版本及隔离目录由运维控制磁盘清理。

## 验证

```powershell
./Tools/Config/Generate.ps1 -VerifyAccessors
dotnet run --project Tests/Content
pwsh -File Tests/Content/ReleaseSmoke.ps1
```

.NET 行为测试使用本机 Kestrel HTTP 和真实临时目录。Unity 播放器和 Android 设备上的验收分别记录。
