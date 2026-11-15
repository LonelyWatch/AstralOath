[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Player,
    [uri]$BaseUrl = 'http://127.0.0.1:5080',
    [ValidatePattern('^[a-z0-9-]+$')][string]$Name = 'windows-arena-battle',
    [switch]$Preload
)
$ErrorActionPreference = 'Stop'
if (-not $BaseUrl.IsAbsoluteUri -or -not $BaseUrl.IsLoopback -or
    $BaseUrl.Scheme -notin @('http','https') -or $BaseUrl.UserInfo) {
    throw 'Arena battle smoke requires an HTTP(S) loopback server.'
}
$Player = (Resolve-Path -LiteralPath $Player).Path
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$log = Join-Path $repo ('.artifacts/' + $Name + '-' + [Guid]::NewGuid().ToString('N') + '.log')
$application = $null
$roomPath = $null
$hostBearer = $null
$guestBearer = $null
$priorEndpoint = $env:ASTRAL_FOUNDATION_URL
$priorFixture = $env:ASTRAL_ARENA_BATTLE_SMOKE_FIXTURE

function Get-RequestId {
    return [Guid]::NewGuid().ToString('N')
}

function Invoke-Arena([string]$Route, [string]$Bearer, [hashtable]$Body) {
    $request = @{
        Uri = [uri]::new($BaseUrl, '/api/arena/' + $Route)
        TimeoutSec = 8
        MaximumRedirection = 0
    }
    if ($Bearer) { $request.Headers = @{ Authorization = 'Bearer ' + $Bearer } }
    if ($null -ne $Body) {
        $request.Method = 'Post'
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = ConvertTo-Json -InputObject $Body -Depth 6 -Compress
    }
    try { return Invoke-RestMethod @request }
    catch {
        $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'unavailable' }
        throw ('Loopback arena request failed at ' + $Route + ' (HTTP ' + $status + ').')
    }
}

function Submit-Draft([string]$Bearer, $State, [string]$Kind, [string[]]$HeroIds) {
    return Invoke-Arena ($roomPath + '/draft/actions') $Bearer @{
        requestId = Get-RequestId
        draftId = $State.id
        version = $State.version
        kind = $Kind
        heroIds = $HeroIds
    }
}

function Wait-PlayerMarker([string]$Marker, [int]$TimeoutSeconds) {
    $limit = [Diagnostics.Stopwatch]::StartNew()
    while ($limit.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $output = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
        if ($output -match 'ASTRAL_ARENA_BATTLE_PLAYER_FAILED|different serialization layout|Online battle transition resources are incomplete') {
            throw "The packed Player reported an arena battle failure. Inspect $log"
        }
        if ($output -and $output.Contains($Marker)) { return }
        if ($application.HasExited) { throw "Player exited before $Marker. Inspect $log" }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for $Marker. Inspect $log"
}

try {
    $hostBearer = (Invoke-Arena 'sessions' '' @{ displayName = 'Player smoke host' }).token
    $guestBearer = (Invoke-Arena 'sessions' '' @{ displayName = 'Player smoke guest' }).token
    if (-not $hostBearer -or -not $guestBearer -or $hostBearer -eq $guestBearer) {
        throw 'The arena smoke requires two independent guest sessions.'
    }
    $room = Invoke-Arena 'rooms' $hostBearer @{
        name = 'Player smoke ' + (Get-RequestId).Substring(0,8)
        rule = 'basic'
        prebanCount = 2
        firstPick = 'host'
        banProtection = $true
    }
    $roomPath = 'rooms/' + $room.room.id
    $null = Invoke-Arena ($roomPath + '/join') $guestBearer @{}
    $draft = Invoke-Arena ($roomPath + '/draft/start') $hostBearer @{ requestId = Get-RequestId }
    $pool = @($draft.eligibleHeroIds)
    if ($draft.phase -ne 'preban' -or $pool.Count -lt 14) { throw 'The preban fixture did not start.' }
    $draft = Submit-Draft $hostBearer $draft 'preban' $pool[0..1]
    $draft = Submit-Draft $guestBearer $draft 'preban' $pool[2..3]
    foreach ($hero in $pool[4..13]) {
        if ($draft.phase -ne 'pick' -or $draft.activeSide -notin @('host','guest')) {
            throw 'The draft fixture did not advance through picks.'
        }
        $seat = if ($draft.activeSide -eq 'host') { $hostBearer } else { $guestBearer }
        $draft = Submit-Draft $seat $draft 'pick' @($hero)
    }
    if ($draft.phase -ne 'ban') { throw 'The final ban fixture did not start.' }
    $allies = @($draft.host.picks)
    $enemies = @($draft.guest.picks)
    $draft = Submit-Draft $hostBearer $draft 'ban' @($enemies[0])
    $draft = Submit-Draft $guestBearer $draft 'ban' @($allies[0])
    if ($draft.phase -ne 'formation') { throw 'The formation fixture did not start.' }
    $draft = Submit-Draft $hostBearer $draft 'formation' $allies[1..4]
    $draft = Submit-Draft $guestBearer $draft 'formation' $enemies[1..4]
    if ($draft.phase -ne 'battle' -or -not $draft.battleId -or -not $draft.contentHash) {
        throw 'The draft did not create a battle invitation.'
    }

    $battlePath = $roomPath + '/battle'
    $opening = Invoke-Arena $battlePath $guestBearer
    $snapshot = $opening.snapshot
    if (@($snapshot.units).Count -ne 8 -or $snapshot.battleId -ne $draft.battleId) {
        throw 'The arena server did not create the expected eight-actor battle.'
    }

    # Keep the test bearer in the child process environment, not command arguments or files.
    $env:ASTRAL_FOUNDATION_URL = $BaseUrl.AbsoluteUri
    $env:ASTRAL_ARENA_BATTLE_SMOKE_FIXTURE = ConvertTo-Json -Compress -InputObject @{
        roomId = $room.room.id
        battleId = $draft.battleId
        contentHash = $draft.contentHash
        bearer = $hostBearer
        preload = $Preload.IsPresent
    }
    $arguments = @('-astral-arena-battle-smoke','-screen-fullscreen','0','-logFile',('"' + $log + '"'))
    $application = Start-Process -FilePath $Player -WorkingDirectory (Split-Path -Parent $Player) -WindowStyle Hidden -PassThru -ArgumentList $arguments
    $env:ASTRAL_FOUNDATION_URL = $priorEndpoint
    $env:ASTRAL_ARENA_BATTLE_SMOKE_FIXTURE = $priorFixture

    if ($Preload) { Wait-PlayerMarker 'ASTRAL_ARENA_BATTLE_PRELOAD_OK' 25 }
    Wait-PlayerMarker 'ASTRAL_ARENA_BATTLE_ENTRY_OK actors=8' 55
    $null = Invoke-Arena ($battlePath + '/ready') $guestBearer @{
        battleId = $snapshot.battleId
        commandSequence = $snapshot.lastCommandSequence
        eventSequence = $snapshot.lastEventSequence
        actorId = $snapshot.currentActorId
    }
    Wait-PlayerMarker 'ASTRAL_ARENA_BATTLE_CLOCK_OK' 20

    $actor = $snapshot.units | Where-Object instanceId -eq $snapshot.currentActorId | Select-Object -First 1
    $hostTurn = $snapshot.currentActorId.StartsWith('ally-', [StringComparison]::Ordinal)
    $enemyPrefix = if ($hostTurn) { 'enemy-' } else { 'ally-' }
    $target = $snapshot.units | Where-Object {
        $_.instanceId.StartsWith($enemyPrefix, [StringComparison]::Ordinal) -and $_.currentHealth -gt 0
    } | Select-Object -First 1
    $seat = if ($hostTurn) { $hostBearer } else { $guestBearer }
    $accepted = Invoke-Arena ($battlePath + '/commands') $seat @{
        battleId = $snapshot.battleId
        rulesVersion = $snapshot.rulesVersion
        sequence = [ulong]$snapshot.lastCommandSequence + 1
        commandId = Get-RequestId
        actorId = $actor.instanceId
        skillId = $actor.form.skillIds[0]
        targetId = $target.instanceId
        useSoulBurn = $false
        contentHash = $draft.contentHash
    }
    if ($null -eq $accepted.status -or $accepted.status -ne 0 -or
        $accepted.snapshot.lastCommandSequence -ne 1) { throw 'The first battle command was not accepted.' }
    Wait-PlayerMarker 'ASTRAL_ARENA_BATTLE_PLAYER_OK commandSequence>=1' 20
    if (-not $application.WaitForExit(10000) -or $application.ExitCode -ne 0) {
        throw "The smoke Player did not exit successfully. Inspect $log"
    }
    Write-Output "Windows arena battle smoke passed: $log"
} finally {
    $env:ASTRAL_FOUNDATION_URL = $priorEndpoint
    $env:ASTRAL_ARENA_BATTLE_SMOKE_FIXTURE = $priorFixture
    if ($application -and -not $application.HasExited) {
        $application.Kill()
        $null = $application.WaitForExit(5000)
    }
    if ($roomPath) {
        foreach ($seat in @($guestBearer, $hostBearer)) {
            try { $null = Invoke-Arena ($roomPath + '/leave') $seat @{} }
            catch { Write-Warning 'The smoke room could not be cleaned up immediately.' }
        }
    }
}
