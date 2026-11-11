[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]{1,47}$')][string]$Name)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$module = Join-Path $repo "Assets/Game/Modules/$Name"
if (Test-Path -LiteralPath $module) { throw "Module already exists: $Name" }
foreach ($folder in @('Scripts','Configs','Editor','Tests','Res/Atlas','Res/Prefab','Res/Textures','Res/Sounds','Res/Animations','Res/Materials')) {
    New-Item -ItemType Directory -Force (Join-Path $module $folder) | Out-Null
    Set-Content -LiteralPath (Join-Path $module "$folder/.gitkeep") -Value '' -Encoding utf8NoBOM
}
$assemblyName = "AstralOath.Client.$Name"
@{
    name = $assemblyName
    rootNamespace = $assemblyName
    references = @('AstralOath.Client.Core','AstralOath.Client.Common')
    autoReferenced = $false
} | ConvertTo-Json | Set-Content (Join-Path $module "Scripts/$assemblyName.asmdef") -Encoding utf8NoBOM
$registration = "完成首个运行时代码后，将 $assemblyName 加入 Config/HotUpdate/assemblies.json 的 assemblies 列表，运行 Tools/检查母包兼容性再发布。登记前该模块尚未纳入热更构建。"
Set-Content (Join-Path $module 'README.md') -Encoding utf8NoBOM -Value "# $Name`n`nDocument ownership, public contracts, config/resource keys, cancellation and disposal rules, and acceptance tests before implementation. For Foo.prefab, put Foo.cs (Foo : UIBaseView) and FooBinder.cs in Scripts. Match filename and type casing. Use the shared BindingMap for prefab bindings. Business state belongs to the C# Binder or application service. Import assets and their meta using Unity before committing.`n`n$registration"
Write-Output "Created $module. Open Unity to import metadata, then run node Tools/validate.mjs."
Write-Output $registration
