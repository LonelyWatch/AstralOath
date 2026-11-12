#requires -Version 5.1
[CmdletBinding()]
param([string]$Unity)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo | Out-Null

$windows = [IO.Path]::DirectorySeparatorChar -eq '\'
if ($windows) {
    $separators = [char[]]@('\', '/')
    $expected = $repo.TrimEnd($separators)
    foreach ($editor in Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue) {
        $projectArgument = [regex]::Match([string]$editor.CommandLine, '(?i)(?:^|\s)-projectPath(?:\s+|=)(?:"(?<path>[^"]+)"|(?<path>\S+))')
        if (-not $projectArgument.Success) {
            continue
        }

        try {
            $editorProject = [IO.Path]::GetFullPath($projectArgument.Groups['path'].Value).TrimEnd($separators)
        }
        catch [ArgumentException], [IO.IOException], [NotSupportedException] {
            continue
        }
        if ($editorProject -ieq $expected) {
            Write-Output "Unity already has this project open (process $($editor.ProcessId))."
            return
        }
    }
}

$lockFile = Join-Path $repo 'Temp/UnityLockfile'
if ([IO.File]::Exists($lockFile)) {
    try {
        $lock = [IO.File]::Open($lockFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $lock.Dispose()
    }
    catch [IO.FileNotFoundException], [IO.DirectoryNotFoundException] {
        # The previous editor closed after the lock file was discovered.
    }
    catch [IO.IOException] {
        Write-Output 'Unity already holds this project open.'
        return
    }
}

if (-not $Unity) {
    if ($windows) {
        $Unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
    }
    else {
        throw 'Supply the Unity 2022.3.62f3 editor executable with -Unity.'
    }
}
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) {
    throw "Unity 2022.3.62f3 editor executable was not found at $Unity."
}
$Unity = (Resolve-Path -LiteralPath $Unity).Path
$reports = Join-Path $repo '.artifacts/unity'
[IO.Directory]::CreateDirectory($reports) | Out-Null
$log = Join-Path $reports 'Editor.log'
$launch = @{
    FilePath         = $Unity
    WorkingDirectory = $repo
    ArgumentList     = ('-projectPath "{0}" -logFile "{1}"' -f $repo, $log)
    PassThru         = $true
}
if ($windows) {
    $launch.WindowStyle = 'Normal'
}
$editor = Start-Process @launch
Write-Output "Opened Unity for $repo (process $($editor.Id)). Editor log is at $log."
