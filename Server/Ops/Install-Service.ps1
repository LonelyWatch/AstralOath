#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][PSCredential]$ServiceCredential
)
$ErrorActionPreference = 'Stop'
$name = 'AstralOath'
if (Get-Service -Name $name -ErrorAction SilentlyContinue) { throw "Service $name already exists." }
$exe = (Resolve-Path -LiteralPath (Join-Path $PublishDirectory 'AstralOath.Server.exe')).ProviderPath
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published apphost is missing.' }
if ($PSCmdlet.ShouldProcess($exe, "Register Windows service $name (Manual)")) {
    New-Service -Name $name -BinaryPathName ('"' + $exe + '"') -Credential $ServiceCredential -StartupType Manual
}
