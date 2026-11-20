# Start the existing development database, game server and signed content feed after a reboot.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ServerDll,
    [Parameter(Mandatory)][string]$FeedDirectory,
    [AllowEmptyString()][string]$MinimumAppVersion = '',
    [int]$ApiPort = 7728,
    [int]$FeedPort = 5093
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repo 'Tools/ProjectEnvironment.ps1')
Initialize-AstralProjectEnvironment -RepositoryRoot $repo | Out-Null
$server = (Resolve-Path -LiteralPath $ServerDll).Path
$feed = (Resolve-Path -LiteralPath $FeedDirectory).Path
$runtime = Join-Path $repo '.artifacts/local-game-server'
$logStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$mysql = (Get-Item (Join-Path $repo '.tools/mysql/mysql-*/bin/mysqld.exe')).FullName
$data = (Resolve-Path (Join-Path $repo '.artifacts/mysql/data')).Path
$python = (Get-Item (Join-Path $repo '.tools/python/cpython-*/python.exe')).FullName
$dotnet = (Resolve-Path (Join-Path $repo '.tools/dotnet/dotnet.exe')).Path
$connection = [IO.File]::ReadAllText((Join-Path $repo '.artifacts/mysql/arena-ui-connection.txt')).Trim()
$manifest = Get-Content -LiteralPath (Join-Path $feed 'manifest.json') -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath (Join-Path $feed 'manifest.sig'))) { throw 'Missing signed feed' }

function Get-LocalListener([int]$port) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0) { return $null }
    if ($listeners.Count -ne 1 -or $listeners[0].LocalAddress -ne '127.0.0.1') {
        throw "Port $port already belongs to a different listener"
    }
    return $listeners[0]
}
function Start-TestProcess([string]$name, [string]$exe, [string[]]$arguments, [string]$directory) {
    Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $directory -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runtime "$name-$logStamp.stdout.log") `
        -RedirectStandardError (Join-Path $runtime "$name-$logStamp.stderr.log")
}
function Wait-Listener([Diagnostics.Process]$proc, [int]$port) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($proc.HasExited) { throw "Process $($proc.Id) exited. See logs under $runtime" }
        $listener = Get-LocalListener $port
        if ($listener -and $listener.OwningProcess -eq $proc.Id) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for localhost:$port"
}
if (!(Get-LocalListener 3307)) {
    $base = Split-Path (Split-Path $mysql)
    $proc = Start-TestProcess 'mysql' $mysql @('--no-defaults', "--basedir=`"$base`"", "--datadir=`"$data`"",
        '--bind-address=127.0.0.1', '--port=3307', '--mysqlx=OFF', "--log-error=`"$runtime/mysql-$logStamp.log`"") $base
    Wait-Listener $proc 3307
}
if (!(Get-LocalListener $FeedPort)) {
    $proc = Start-TestProcess 'feed' $python @('-m', 'http.server', "$FeedPort", '--bind', '127.0.0.1', '--directory', "`"$feed`"") $feed
    Wait-Listener $proc $FeedPort
}
if (!(Get-LocalListener $ApiPort)) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__MySql = $connection
    $env:Server__Port = "$ApiPort"
    $env:Server__BindAddress = '127.0.0.1'
    $env:Entry__AllowLegacyArenaGuests = 'true'
    $env:Entry__MinimumAppVersion = $MinimumAppVersion
    & $dotnet $server migrate
    if ($LASTEXITCODE) { throw 'Local database migration failed' }
    $proc = Start-TestProcess 'server' $dotnet @("`"$server`"") (Split-Path $server)
    Wait-Listener $proc $ApiPort
}
$api = "http://127.0.0.1:$ApiPort"
$ready = Invoke-WebRequest "$api/health/ready" -UseBasicParsing -TimeoutSec 10
$policy = Invoke-RestMethod "$api/api/entry/version" -TimeoutSec 10
$online = Invoke-RestMethod "http://127.0.0.1:$FeedPort/manifest.json" -TimeoutSec 10
if ($ready.StatusCode -ne 200 -or [string]$policy.minimumAppVersion -ne $MinimumAppVersion -or
    $online.version -ne $manifest.version) { throw 'Running local settings differ from the requested release' }
Write-Output "Ready: $api, minimum app '$MinimumAppVersion', http://127.0.0.1:$FeedPort/manifest.json R$($online.version)"
