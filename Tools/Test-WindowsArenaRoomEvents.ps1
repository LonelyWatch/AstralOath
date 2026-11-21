[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Player,
    [Parameter(Mandatory)][int]$ExpectedRelease,
    [uri]$Endpoint = 'http://127.0.0.1:7728'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $root | Out-Null
if (!$Endpoint.IsLoopback) { throw 'Room UI tests require a loopback game server.' }
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$run = [guid]::NewGuid().ToString('N')
$out = Join-Path $root ".artifacts/room-events-player/$run"
New-Item -ItemType Directory -Path $out | Out-Null

function Open-TestPlayer([string]$role) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.FileName = $playerPath
    $start.WorkingDirectory = Split-Path $playerPath
    $start.EnvironmentVariables['ASTRAL_FOUNDATION_URL'] = $Endpoint.AbsoluteUri
    $start.EnvironmentVariables['ASTRAL_ARENA_PLAYER_NAME'] = "$role-$($run.Substring(0,8))"
    $start.EnvironmentVariables['ASTRAL_ROOM_SMOKE_ROLE'] = $role
    $start.EnvironmentVariables['ASTRAL_ROOM_SMOKE_ID'] = $run
    $log = Join-Path $out "$role.log"
    $start.Arguments = "-screen-fullscreen 0 -screen-width 960 -screen-height 540 -force-d3d11 -astral-startup-smoke -astral-arena-room-smoke -logFile `"$log`""
    [Diagnostics.Process]::Start($start)
}

$players = @{}
try {
    $players.host = Open-TestPlayer 'host'
    $hostLog = Join-Path $out 'host.log'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $hostLog) -or
        !(Select-String -LiteralPath $hostLog -SimpleMatch 'ASTRAL_ROOM_ENTERED host' -Quiet)) {
        if ($players.host.HasExited -or $clock.Elapsed.TotalSeconds -gt 180) { throw "Host did not enter its room. See $hostLog" }
        Start-Sleep -Milliseconds 150
    }
    $players.guest = Open-TestPlayer 'guest'
    foreach ($role in @('host','guest')) {
        $p = $players[$role]
        if (!$p.WaitForExit(135000)) { throw "Timed out waiting for $role. See $out" }
        $log = Get-Content -Raw -LiteralPath (Join-Path $out "$role.log")
        if ($p.ExitCode -ne 0 -or !$log.Contains("ASTRAL_ROOM_EVENTS_OK $role") -or
            !$log.Contains("ASTRAL_CONTENT_ACTIVE version=$ExpectedRelease recovered=False") -or
            $log -match '(?m)^(?:MissingMethodException|ExecutionEngineException|Unhandled Exception|NullReferenceException):') {
            throw "Room UI smoke failed for $role. See $out"
        }
    }
    [ordered]@{ release=$ExpectedRelease; endpoint=$Endpoint.AbsoluteUri; hostExit=$players.host.ExitCode;
        guestExit=$players.guest.ExitCode; elapsedSeconds=[Math]::Round($clock.Elapsed.TotalSeconds,2) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'verified.json')
    Write-Output "Native host and guest passed create/join, bidirectional chat, member updates and leave. $out"
} finally {
    foreach ($p in $players.Values) {
        if (!$p.HasExited) { $p.Kill(); $p.WaitForExit() }
        $p.Dispose()
    }
}
