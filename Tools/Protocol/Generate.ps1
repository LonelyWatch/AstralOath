param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Protocol.Tools.csproj'
& $Dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Protocol tool restore failed.' }
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json
$package = $assets.libraries.'Grpc.Tools/2.76.0'.path
$protoc = $assets.packageFolders.PSObject.Properties.Name |
    ForEach-Object { Join-Path $_ "$package/tools/windows_x64/protoc.exe" } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if (!$protoc) { throw 'Grpc.Tools Windows x64 protoc is missing.' }
$output = Join-Path $PSScriptRoot '../../Shared/com.astraloath.shared/Runtime/Protocol'
[void](New-Item -ItemType Directory -Path $output -Force)
& $protoc "--proto_path=$PSScriptRoot" "--csharp_out=$output" (Join-Path $PSScriptRoot 'foundation.proto')
if ($LASTEXITCODE -ne 0) { throw 'Protocol generation failed.' }
