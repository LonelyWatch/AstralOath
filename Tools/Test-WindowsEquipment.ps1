[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Player,
    [Parameter(Mandatory)][int]$ExpectedRelease,
    [Parameter(Mandatory)][uri]$Endpoint
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $root | Out-Null
if (!$Endpoint.IsLoopback) { throw 'Use an isolated Development training server.' }
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$run = [guid]::NewGuid().ToString('N')
$out = Join-Path $root ".artifacts/equipment-player/$run"
New-Item -ItemType Directory -Path $out | Out-Null
$log = Join-Path $out 'player.log'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.FileName = $playerPath
$start.WorkingDirectory = Split-Path $playerPath
$start.EnvironmentVariables['ASTRAL_FOUNDATION_URL'] = $Endpoint.AbsoluteUri
$start.EnvironmentVariables['ASTRAL_ARENA_PLAYER_NAME'] = 'Equip' + $run.Substring(0, 12)
$start.Arguments = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -force-d3d11 -astral-arena-battle-smoke -astral-equipment-smoke -logFile `"$log`""
$playerProcess = [Diagnostics.Process]::Start($start)
try {
    if (!$playerProcess.WaitForExit(210000)) { throw "Timed out waiting for equipment smoke. See $log" }
    $body = Get-Content -LiteralPath $log -Raw
    if ($playerProcess.ExitCode -ne 0 -or !$body.Contains("ASTRAL_CONTENT_ACTIVE version=$ExpectedRelease recovered=False") -or
        !$body.Contains('Direct3D 11') -or $body -notmatch 'ASTRAL_EQUIPMENT_PLAYER_OK items=7 health=(\d+),(\d+),(\d+)' -or
        $body -match '(?m)^(?:MissingMethodException|ExecutionEngineException|Unhandled Exception|NullReferenceException):') {
        throw "Equipment UI smoke failed. See $log"
    }
    $values = [regex]::Match($body, 'ASTRAL_EQUIPMENT_PLAYER_OK items=7 health=(\d+),(\d+),(\d+)').Groups
    $health = @([int]$values[1].Value, [int]$values[2].Value, [int]$values[3].Value)
    if ($health[1] -ne $health[0] + 80 -or $health[2] -ne $health[0]) { throw "Unexpected HP change. See $log" }
    [ordered]@{release=$ExpectedRelease; endpoint=$Endpoint.AbsoluteUri; player=$playerPath; cards=7; health=$health; exitCode=$playerProcess.ExitCode} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'verified.json')
    Write-Output "Native equipment inventory, equip and unequip passed. $out"
} finally {
    if (!$playerProcess.HasExited) { $playerProcess.Kill(); $playerProcess.WaitForExit() }
    $playerProcess.Dispose()
}
