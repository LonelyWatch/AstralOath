[CmdletBinding()]
param([switch]$VerifyOnly,[switch]$SwitchToR44,[switch]$Execute,[string]$PreflightReceipt,
      [string]$ResultId=([Guid]::NewGuid().ToString('N')))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'R44Service.Identity.ps1')
. (Join-Path $PSScriptRoot 'R44Service.Common.ps1')
if($MyInvocation.InvocationName -ne '.') {
    Invoke-R44GameService -VerifyOnly:$VerifyOnly -SwitchToR44:$SwitchToR44 -Execute:$Execute `
        -PreflightReceipt $PreflightReceipt -ResultId $ResultId|ConvertTo-Json -Depth 14 -Compress
}
