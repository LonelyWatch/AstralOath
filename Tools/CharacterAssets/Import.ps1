#requires -Version 5.1
[CmdletBinding()]
param([string]$Source = 'D:/sevenepic/原始资源', [switch]$Verify)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$python = Join-Path $repo '.tools/character-assets/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    throw '缺少转换工具环境。执行：py -3.12 -m venv .tools/character-assets；然后 .tools/character-assets/Scripts/python.exe -m pip install -r Tools/CharacterAssets/requirements-lock.txt'
}
$arguments = @('-X', 'utf8', '-B', (Join-Path $PSScriptRoot 'import_characters.py'), '--source', $Source, '--project', $repo)
if ($Verify) { $arguments += '--verify' }
& $python @arguments
if ($LASTEXITCODE -ne 0) { throw "角色资源转换/校验失败：$LASTEXITCODE" }
