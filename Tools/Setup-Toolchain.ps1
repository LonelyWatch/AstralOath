[CmdletBinding()]
param([switch]$WithMySql, [switch]$WithMcp)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localTools = Join-Path $repo '.tools'
$downloads = Join-Path $localTools 'downloads'
New-Item -ItemType Directory -Force $downloads | Out-Null
$lock = Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
function Expand-PinnedTool([string]$name, [string]$directory) {
    $item = @($lock.dependencies | Where-Object name -eq $name)
    if ($item.Count -ne 1) { throw "Dependency lock entry missing or duplicated: $name" }
    $item = $item[0]
    $archive = Join-Path $downloads $item.file
    if (-not (Test-Path -LiteralPath $archive)) {
        $partial = "$archive.part"
        Invoke-WebRequest -Uri $item.url -OutFile $partial
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $item.sha256) { throw "Download checksum mismatch: $name" }
        Move-Item -LiteralPath $partial -Destination $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $item.sha256) { throw "Cached archive checksum mismatch: $name" }
    New-Item -ItemType Directory -Force $directory | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force
}
$dotnet = Join-Path $localTools 'dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { Expand-PinnedTool 'dotnet-sdk' (Join-Path $localTools 'dotnet') }
$version = & $dotnet --version
if ($version -ne '10.0.403') { throw "Expected .NET SDK 10.0.403, found $version" }
Write-Output ".NET SDK $version ready"
if ($WithMySql) {
    $mysql = Join-Path $localTools 'mysql/mysql-8.4.13-winx64/bin/mysqld.exe'
    if (-not (Test-Path -LiteralPath $mysql)) { Expand-PinnedTool 'mysql' (Join-Path $localTools 'mysql') }
    & $mysql --version
    if ($LASTEXITCODE -ne 0) { throw 'MySQL executable check failed' }
}
if ($WithMcp) {
    $uv = Join-Path $localTools 'uv/uv.exe'
    if (-not (Test-Path -LiteralPath $uv)) { Expand-PinnedTool 'uv' (Join-Path $localTools 'uv') }
    $env:UV_PYTHON_INSTALL_DIR = Join-Path $localTools 'python'
    $env:UV_CACHE_DIR = Join-Path $localTools 'uv-cache'
    $env:UV_PROJECT_ENVIRONMENT = Join-Path $localTools 'mcp-venv'
    & $uv sync --frozen --project (Join-Path $PSScriptRoot 'Mcp/Server') --python 3.12.12 --no-dev
    if ($LASTEXITCODE -ne 0) { throw 'MCP environment installation failed' }
    & (Join-Path $localTools 'mcp-venv/Scripts/python.exe') -c 'import importlib.metadata; print("Unity MCP " + importlib.metadata.version("mcpforunityserver"))'
    if ($LASTEXITCODE -ne 0) { throw 'MCP environment verification failed' }
}
