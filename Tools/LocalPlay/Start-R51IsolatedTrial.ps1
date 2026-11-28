#requires -Version 7.0
[CmdletBinding()]
param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$trial = Join-Path $repo '.artifacts/windows-r51'
$local = Join-Path $trial 'local'
$pin = Get-Content -LiteralPath (Join-Path $local 'trial-identity.json') -Raw | ConvertFrom-Json
$review = Join-Path $repo '.artifacts/arena-visual-rework'
$feed = Join-Path $review 'feed51'
$client = Join-Path $review 'native-base/Win64/player-v1-3b17cfac23524e48909d9e41301d37f2'
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
$python = Join-Path $repo '.tools/python/cpython-3.12.12-windows-x86_64-none/python.exe'
$dll = Join-Path $trial 'backend/AstralOath.Server.dll'
$hostState = Join-Path $local 'isolated-host.json'
$gameUrl = 'http://127.0.0.1:7849'
$feedUrl = 'http://127.0.0.1:5391'

function SameHash([string]$path,[string]$expected) {
    if ((Get-FileHash -LiteralPath $path).Hash -ne $expected) { throw "试玩文件校验失败：$path" }
}

function Listener([int]$port) {
    # netstat also works in non-administrative desktop sessions. Retain wildcard
    # listeners so an unrelated service occupying the port is never reused.
    $owners = @(netstat.exe -ano -p TCP | ForEach-Object {
        if ($_ -match ('^\s*TCP\s+(127\.0\.0\.1|0\.0\.0\.0|\[::\]):' + $port + '\s+\S+\s+LISTENING\s+(\d+)\s*$')) {
            if ($Matches[1] -ne '127.0.0.1') { throw "端口 $port 已被非隔离服务占用。请关闭冲突后重试。" }
            [int]$Matches[2]
        }
    })
    if ($owners.Count -gt 1) { throw "端口 $port 有多个监听进程。" }
    if ($owners.Count) { return $owners[0] }
    return 0
}

function HostIdentity([int]$owner) {
    $p = Get-Process -Id $owner -ErrorAction Stop
    if ($p.Path -ne $dotnet) { throw '7849 已被其他进程占用。' }
    $ticks = $p.StartTime.ToUniversalTime().Ticks
    if ((Test-Path -LiteralPath $hostState)) {
        $saved = Get-Content -LiteralPath $hostState -Raw | ConvertFrom-Json
        if ($owner -eq $saved.pid -and $ticks -eq $saved.startUtcTicks -and $saved.sha256 -eq $pin.backendSha256) {
            return 'R51'
        }
    }
    if ($owner -eq $pin.priorHost.pid -and $ticks -eq $pin.priorHost.startUtcTicks) {
        SameHash (Join-Path $repo '.artifacts/windows-r49/backend/AstralOath.Server.dll') $pin.priorHost.sha256
        return '已验收的隔离主机'
    }
    throw '7849 的进程身份和本次试玩不匹配。请检查占用后重试。'
}

function CheckHealth {
    $health = Invoke-WebRequest "$gameUrl/health/ready" -NoProxy -TimeoutSec 5
    if ($health.StatusCode -ne 200 -or $health.Content.Trim() -ne 'Healthy') { throw '隔离后端或其数据库尚未就绪。' }
    $version = Invoke-RestMethod "$gameUrl/api/entry/version" -NoProxy -TimeoutSec 5
    if ($version.realmId -ne 'undying-memory') { throw '7849 返回了意外的游戏区服。' }
}

function CheckFeed {
    foreach ($pair in @(@('manifest.json',$pin.manifestSha256),@('manifest.sig',$pin.signatureSha256))) {
        $reply = Invoke-WebRequest "$feedUrl/$($pair[0])" -MaximumRedirection 0 -NoProxy -TimeoutSec 5
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($reply.RawContentStream.ToArray()))
        if ($digest -ne $pair[1]) { throw '5391 正提供其他更新内容，无法用于本次 R51 试玩。' }
    }
}

function WaitReady([Diagnostics.Process]$process,[int]$port,[scriptblock]$probe) {
    for ($i = 0; $i -lt 60; $i++) {
        if ($process.HasExited) { throw "隔离服务启动失败（端口 $port），诊断日志位于 $local。" }
        if ((Listener $port) -eq $process.Id) {
            try { & $probe; return } catch { if ($i -eq 59) { throw } }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "等待隔离服务超时（端口 $port），诊断日志位于 $local。"
}

function StartGameHost {
    $config = Join-Path $repo '.artifacts/identity-social-followup/server/test-connection.txt'
    $db = [System.Data.Common.DbConnectionStringBuilder]::new()
    $db.set_ConnectionString([IO.File]::ReadAllText($config).Trim())
    if ($db['Server'] -ne '127.0.0.1' -or [int]$db['Port'] -ne 3307 -or $db['Database'] -cne 'astraloath_uid_20261128') {
        throw '隔离数据库身份与试玩配置不一致。'
    }
    $log = Join-Path $local ('game-' + [Guid]::NewGuid().ToString('N'))
    $settings = @{
        ConnectionStrings__MySql=$db.get_ConnectionString()
        Server__Port='7849'
        Server__BindAddress='127.0.0.1'
        ASPNETCORE_ENVIRONMENT='Development'
        DOTNET_ENVIRONMENT='Development'
        DOTNET_ROOT=(Split-Path $dotnet)
    }
    # Pass the existing isolated QA secret to this process only.
    $p = Start-Process $dotnet -ArgumentList ('"' + $dll + '"') -WorkingDirectory (Split-Path $dll) -Environment $settings -PassThru -WindowStyle Hidden -RedirectStandardOutput "$log.out" -RedirectStandardError "$log.err"
    $state = [ordered]@{pid=$p.Id;startUtcTicks=$p.StartTime.ToUniversalTime().Ticks;sha256=$pin.backendSha256}
    $tempState = "$hostState.new"
    $state | ConvertTo-Json | Set-Content -LiteralPath $tempState -Encoding utf8
    [IO.File]::Move($tempState,$hostState,$true)
    WaitReady $p 7849 { CheckHealth }
}

function StartFeed {
    $log = Join-Path $local ('feed-' + [Guid]::NewGuid().ToString('N'))
    $p = Start-Process $python -ArgumentList @('-I','-m','http.server','5391','--bind','127.0.0.1','--directory',('"' + $feed + '"')) -PassThru -WindowStyle Hidden -RedirectStandardOutput "$log.out" -RedirectStandardError "$log.err"
    WaitReady $p 5391 { CheckFeed }
}

. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo | Out-Null
SameHash $dll $pin.backendSha256
SameHash (Join-Path $feed 'manifest.json') $pin.manifestSha256
SameHash (Join-Path $feed 'manifest.sig') $pin.signatureSha256
SameHash (Join-Path $client 'AstralOath.exe') $pin.clientSha256
SameHash (Join-Path $client 'player-baseline.json') $pin.baselineSha256
if ((Get-Content -Raw -LiteralPath (Join-Path $feed 'manifest.json') | ConvertFrom-Json).version -ne 51) { throw '需要 R51 签名内容。' }

$guard = [Threading.Mutex]::new($false,'Local\AstralOath-IsolatedTrial-7849-5391')
$held = $false
try {
    try { $held = $guard.WaitOne(60000) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw '另一个试玩启动器仍在检查服务，请稍后再试。' }
    $apiOwner = Listener 7849
    $feedOwner = Listener 5391
    if ($apiOwner) { $identity = HostIdentity $apiOwner; CheckHealth; Write-Output "7849：$identity 已就绪。" }
    if ($feedOwner) { CheckFeed; Write-Output '5391：已核对 R51 签名清单。' }
    if ($VerifyOnly) {
        if (!$apiOwner) { Write-Output '7849：当前空闲，双击时将启动 R51 后端。' }
        if (!$feedOwner) { Write-Output '5391：当前空闲，双击时将启动 R51 资源服务。' }
        return
    }
    if (!$apiOwner) { StartGameHost }
    if (!$feedOwner) { StartFeed }
    $env:ASTRAL_FOUNDATION_URL = $gameUrl
    $env:NO_PROXY = '127.0.0.1,localhost'
    $player = Start-Process -FilePath (Join-Path $client 'AstralOath.exe') -WorkingDirectory $client -PassThru
    # Record the identity for troubleshooting and for closing exactly the client opened by a desktop check.
    $receiptName = 'last-client.json'
    if ($env:ASTRAL_R51_LAUNCH_CHECK -match '^[a-f0-9]{32}$') { $receiptName = "client-$($env:ASTRAL_R51_LAUNCH_CHECK).json" }
    [ordered]@{pid=$player.Id;startUtcTicks=$player.StartTime.ToUniversalTime().Ticks;path=(Join-Path $client 'AstralOath.exe')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $local $receiptName) -Encoding utf8
} finally {
    if ($held) { $guard.ReleaseMutex() }
    $guard.Dispose()
}
