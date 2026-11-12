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
    references = @('AstralOath.Client.Core','AstralOath.Client.Common','Unity.TextMeshPro','UnityEngine.UI')
    autoReferenced = $false
} | ConvertTo-Json | Set-Content (Join-Path $module "Scripts/$assemblyName.asmdef") -Encoding utf8NoBOM
$registration = "完成首个运行时代码后，将 $assemblyName 加入 Config/HotUpdate/assemblies.json 的 assemblies 列表，运行 Tools/检查母包兼容性再发布。登记前该模块尚未纳入热更构建。"
Set-Content (Join-Path $module 'README.md') -Encoding utf8NoBOM -Value "# $Name`n`n状态：仅生成目录与程序集，尚未实现/验收。开始实现时填写下列内容。`n`n## 职责与入口`n`n- 职责、公开契约、入口、依赖方向和文件所有权：待填写。`n- Foo.prefab 对应普通 Foo : UIBaseView 与 FooBinder，预制体用 BindingMap / GameText / GameButton。`n`n## 配置与资源`n`n- 唯一源表、生成命令、客户端/服务端快照：待填写。`n- 资源地址、来源哈希、图集与共享归属：待填写。`n- 点分文案key；zh-Hans界面与ja语音独立。`n`n## 生命周期与更新`n`n- 取消、超时、关闭解绑、资源释放与对象池复位：待填写。`n- $registration`n- Editor/Tests代码需要适用程序集，不得混入业务运行时。AOT改动需新母包，协议/数据库不兼容需维护发布。`n`n## 验收与交接`n`n- 成功/失败/取消/复用用例、命令及实际结果：待填写。`n- 当前所有业务验收均未执行。`n- 每次改动同批更新本README和docs/development/change-log.md；状态变化更新docs/project-status.md。`n- 提交前运行node Tools/check-docs.mjs --staged。遵守通用规范与交接模板；资产经Unity导入并保留meta。`n"
Write-Output "Created $module. Open Unity to import metadata, then run node Tools/validate.mjs."
Write-Output $registration
