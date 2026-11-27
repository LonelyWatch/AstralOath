[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo|Out-Null
$release=(Resolve-Path (Join-Path $repo '.artifacts/windows-r41')).Path
$manifest=Get-Content -LiteralPath (Join-Path $release 'release/manifest.json') -Raw|ConvertFrom-Json
if($manifest.version -ne 41){throw 'R41_INSTALL_WRONG_CONTENT_VERSION'}
foreach($name in @('Ensure-R41GameService.ps1','R41Service.Identity.ps1','MySqlProcessIdentity.ps1','R38.Runtime.Common.ps1.in')){
    if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $name))){throw 'R41_STARTUP_FIX_SOURCE_INCOMPLETE'}
}
# These are mutable local helpers, separate from the signed release and client mother files.
$files=@(
    @('ServiceLauncher.cpp',(Join-Path $release 'local/Play.cpp')),
    @('Start-R41Trial.ps1.in',(Join-Path $release 'local/start-play.ps1')),
    @('R38.Runtime.Common.ps1.in',(Join-Path $repo '.artifacts/windows-r38/server/Runtime.Common.ps1'))
)
foreach($entry in $files){
    $target=[IO.Path]::GetFullPath($entry[1])
    if(!$target.StartsWith((Join-Path $repo '.artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'R41_STARTUP_FIX_TARGET_OUTSIDE_ARTIFACTS'
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $entry[0]) -Destination $target -Force
}
& (Join-Path $release 'local/Build-Launcher.ps1')
[ordered]@{status='local_helpers_installed';atUtc=[DateTime]::UtcNow.ToString('o');contentVersion=41;
    launcher=(Join-Path $release 'Play-R41.exe');launcherSha256=(Get-FileHash (Join-Path $release 'Play-R41.exe')).Hash;
    gameMotherChanged=$false;signedContentChanged=$false}|ConvertTo-Json
