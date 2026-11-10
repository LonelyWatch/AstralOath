[CmdletBinding()]
param(
    [string]$Player,
    [ValidatePattern('^[a-z0-9-]+$')][string]$Name = 'windows-smoke',
    [string]$BaseUrl = 'http://localhost:5080',
    [int]$Width = 1600,
    [int]$Height = 900,
    [switch]$Screenshot,
    [switch]$Visible,
    [long]$ContentVersion = 0,
    [switch]$Recovered
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Player) { $Player = Join-Path $repo '.artifacts/UnityWindows/.artifacts/builds/Windows/AstralOath.exe' }
if (-not (Test-Path -LiteralPath $Player -PathType Leaf)) { throw 'Build the Windows IL2CPP player first.' }
$log = Join-Path $repo ".artifacts/$Name.log"
$png = Join-Path $repo ".artifacts/$Name.png"
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
if ($Screenshot -and (Test-Path -LiteralPath $png)) { Remove-Item -LiteralPath $png -Force }
$arguments = @('-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-astral-demo-echo','-logFile',"`"$log`"")
if ($Screenshot) { $arguments += @('-astral-demo-screenshot',"`"$png`"") }
$previous = $env:ASTRAL_FOUNDATION_URL
try {
    $env:ASTRAL_FOUNDATION_URL = $BaseUrl
    $style = if ($Visible) { 'Normal' } else { 'Hidden' }
    $process = Start-Process -FilePath $Player -ArgumentList $arguments -WindowStyle $style -PassThru
    if (-not $process.WaitForExit(90000)) {
        # This handle belongs to the player started above, never an unrelated Unity process.
        $process.Kill()
        throw "Player smoke timed out; inspect $log"
    }
    $output = Get-Content -LiteralPath $log -Raw
    if ($process.ExitCode -ne 0 -or $output -notmatch 'ASTRAL_ECHO_OK sequence=[1-9][0-9]*') {
        throw "Player echo failed (exit=$($process.ExitCode)); inspect $log"
    }
    if ($ContentVersion -gt 0) {
        $expected = "ASTRAL_CONTENT_ACTIVE version=$ContentVersion recovered=$([bool]$Recovered)"
        if (-not $output.Contains($expected)) { throw "Missing '$expected'; inspect $log" }
    }
    if ($Screenshot -and (-not (Test-Path -LiteralPath $png) -or $output -notmatch 'ASTRAL_SCREENSHOT_OK')) {
        throw 'Player did not produce the requested screenshot.'
    }
    Write-Output "Windows player smoke passed: $log"
    if ($Screenshot) { Write-Output "Inspect actual rendered image: $png" }
} finally { $env:ASTRAL_FOUNDATION_URL = $previous }
