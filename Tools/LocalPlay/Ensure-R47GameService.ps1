#requires -Version 7.0
[CmdletBinding()]
param([switch]$VerifyOnly,[switch]$SwitchToR47,[switch]$Execute,[string]$PreflightReceipt,
      [string]$ResultId=([Guid]::NewGuid().ToString('N')))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'R44Service.Identity.ps1')
. (Join-Path $PSScriptRoot 'R44Service.Common.ps1')
. (Join-Path $PSScriptRoot 'R47Service.Identity.ps1')
. (Join-Path $PSScriptRoot 'R47Service.Common.ps1')
if($MyInvocation.InvocationName -ne '.'){
    Invoke-R47GameService -VerifyOnly:$VerifyOnly -SwitchToR47:$SwitchToR47 -Execute:$Execute -PreflightReceipt $PreflightReceipt -ResultId $ResultId|ConvertTo-Json -Depth 14 -Compress
}
