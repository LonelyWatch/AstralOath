[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Output,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Publish the Windows service from a Windows x64 SDK host.' }
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if ((Test-Path -LiteralPath $outputPath) -and (Get-ChildItem -LiteralPath $outputPath -Force)) {
    throw 'Publish to a new or empty directory.'
}
$project = Join-Path $PSScriptRoot '../Host/AstralOath.Server.csproj'
& $Dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $Dotnet publish $project --configuration Release --no-restore --self-contained false --output $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
