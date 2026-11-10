[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$python = Join-Path $repo '.tools/mcp-venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Run Tools/Setup-Toolchain.ps1 -WithMcp first.' }
$env:UNITY_MCP_DISABLE_TELEMETRY = 'true'
$env:DISABLE_TELEMETRY = 'true'
$env:PYTHONUTF8 = '1'
$env:PYTHONDONTWRITEBYTECODE = '1'
Set-Location -LiteralPath (Join-Path $PSScriptRoot 'Server')
& $python -m main --transport stdio
exit $LASTEXITCODE
