[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo
$output = [IO.Directory]::CreateDirectory($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)).FullName
$intermediate = [IO.Directory]::CreateDirectory((Join-Path $repo '.artifacts/LocalPlay/x64')).FullName
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Install the Visual Studio C++ x64 build tools.' }
& (Join-Path $visualStudio 'Common7/Tools/Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$exe = Join-Path $output 'AstralOath-Play.exe'
$obj = Join-Path $intermediate 'Play.obj'
& cl.exe /nologo /O1 /MT /GS /W4 /WX /EHsc /std:c++17 /utf-8 (Join-Path $PSScriptRoot 'Play.cpp') "/Fo$obj" "/Fe$exe" /link /SUBSYSTEM:WINDOWS /MACHINE:X64 /Brepro user32.lib
if ($LASTEXITCODE -ne 0) { throw "Building the Windows x64 launcher failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $exe
