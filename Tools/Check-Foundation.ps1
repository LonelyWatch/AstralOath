[CmdletBinding()]
param([switch]$RequireMySql, [switch]$UnityTests)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Run Tools/Setup-Toolchain.ps1 first.' }
if ($RequireMySql -and [string]::IsNullOrWhiteSpace($env:ASTRALOATH_MYSQL_TEST)) {
    throw 'RequireMySql needs ASTRALOATH_MYSQL_TEST for a disposable MySQL 8.4 database.'
}
$env:DOTNET_ROOT = Split-Path $dotnet
$env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
Push-Location $repo
try {
    & node --test Tools/Tests/validate.test.mjs
    if ($LASTEXITCODE) { throw 'Validator tests failed.' }
    & node Tools/validate.mjs
    if ($LASTEXITCODE) { throw 'Project boundaries failed.' }
    & $dotnet restore Server/Tests/AstralOath.Server.Tests.csproj --locked-mode
    if ($LASTEXITCODE) { throw 'Locked server restore failed.' }
    & $dotnet test Server/Tests/AstralOath.Server.Tests.csproj -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Server tests failed.' }
    & $dotnet run --project Tests/Content -c Release
    if ($LASTEXITCODE) { throw 'Content tests failed.' }
    & pwsh -NoProfile -File Tools/Config/Generate.ps1 -Dotnet $dotnet -VerifyAccessors -CheckClient
    if ($LASTEXITCODE) { throw 'Config generation failed.' }
    & pwsh -NoProfile -File Tests/Content/ConfigSmoke.ps1
    if ($LASTEXITCODE) { throw 'Config editing regression failed.' }
    & pwsh -NoProfile -File Tools/Protocol/Generate.ps1 -Dotnet $dotnet
    if ($LASTEXITCODE) { throw 'Protocol generation failed.' }
    & pwsh -NoProfile -File Tests/Content/ReleaseSmoke.ps1
    if ($LASTEXITCODE) { throw 'Release smoke failed.' }
    if ($UnityTests) {
        foreach ($mode in @('EditMode','PlayMode')) {
            & pwsh -NoProfile -File Tools/Invoke-Unity.ps1 -Mode $mode
            if ($LASTEXITCODE) { throw "Unity $mode failed." }
        }
    }
    if (-not $RequireMySql) { Write-Output 'Database acceptance is separate unless ASTRALOATH_MYSQL_TEST was provided; inspect skipped tests.' }
    Write-Output 'Foundation checks completed; player builds and deployment are separate gates.'
} finally { Pop-Location }
