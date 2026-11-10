[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]{1,47}$')][string]$Name)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$module = Join-Path $repo "Assets/Game/Modules/$Name"
if (Test-Path -LiteralPath $module) { throw "Module already exists: $Name" }
foreach ($folder in @('Scripts','Lua','Configs','Editor','Tests','Res/Atlas','Res/Prefab','Res/Textures','Res/Sounds','Res/Animations','Res/Materials')) {
    New-Item -ItemType Directory -Force (Join-Path $module $folder) | Out-Null
    Set-Content -LiteralPath (Join-Path $module "$folder/.gitkeep") -Value '' -Encoding utf8NoBOM
}
@{name="AstralOath.Client.$Name";rootNamespace="AstralOath.Client.$Name";references=@('AstralOath.Client.Core','AstralOath.Client.Common')} | ConvertTo-Json | Set-Content (Join-Path $module "Scripts/AstralOath.Client.$Name.asmdef") -Encoding utf8NoBOM
Set-Content (Join-Path $module 'README.md') -Encoding utf8NoBOM -Value "# $Name`n`nDocument ownership, public contracts, config/resource keys, cancellation and disposal rules, and acceptance tests before implementation. Business state belongs to the Binder/application service. Import assets and their meta using Unity before committing."
Write-Output "Created $module. Open Unity to import metadata, then run node Tools/validate.mjs."
