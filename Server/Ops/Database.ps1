[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Backup','Restore')][string]$Action,
    [Parameter(Mandatory)][string]$MySqlBin,
    [Parameter(Mandatory)][string]$DefaultsFile,
    [Parameter(Mandatory)][ValidatePattern('\A[A-Za-z0-9_]{1,64}\z')][string]$Database,
    [Parameter(Mandatory)][string]$DumpFile
)

$ErrorActionPreference = 'Stop'
$defaults = (Resolve-Path -LiteralPath $DefaultsFile).ProviderPath
if (!(Test-Path -LiteralPath $defaults -PathType Leaf)) { throw 'Defaults file must be a file.' }
$dump = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DumpFile)
$defaultsArg = "--defaults-file=$defaults"
if ($Action -eq 'Backup') {
    if (Test-Path -LiteralPath $dump) { throw "Backup already exists: $dump" }
    if (!(Test-Path -LiteralPath (Split-Path -Parent $dump) -PathType Container)) { throw 'Backup parent directory is missing.' }
    $partial = "$dump.partial.$([guid]::NewGuid().ToString('N'))"
    $mysqldump = (Resolve-Path -LiteralPath (Join-Path $MySqlBin 'mysqldump.exe')).ProviderPath
    try {
        & $mysqldump $defaultsArg --no-login-paths --single-transaction --skip-lock-tables --quick --hex-blob --skip-triggers --no-tablespaces --set-gtid-purged=OFF --skip-add-drop-table --skip-add-locks --skip-disable-keys --skip-add-drop-database --skip-all-databases --tables "--result-file=$partial" -- $Database
        if ($LASTEXITCODE -ne 0) { throw "mysqldump failed ($LASTEXITCODE)." }
        [IO.File]::Move($partial, $dump)
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
    return
}

if (!(Test-Path -LiteralPath $dump -PathType Leaf)) { throw 'Dump file is missing.' }
$mysql = (Resolve-Path -LiteralPath (Join-Path $MySqlBin 'mysql.exe')).ProviderPath
$dbArg = "--database=$Database"
$preflight = @'
SELECT IF(@@GLOBAL.mandatory_roles = '', 0, 1), @@GLOBAL.partial_revokes,
    (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE());
SHOW GRANTS;
'@
$rows = @(& $mysql $defaultsArg --no-login-paths $dbArg --skip-reconnect --batch --raw --skip-column-names "--execute=$preflight")
if ($LASTEXITCODE -ne 0 -or $rows.Count -eq 0) { throw 'Restore preflight could not inspect database and grants.' }
$state = $rows[0] -split '\t'
if ($state.Count -ne 3 -or $state[0] -ne '0' -or $state[1] -notin @('0','1')) {
    throw 'Restore requires a dedicated account on a server without mandatory roles.'
}
if ($state[2] -ne '0') { throw 'Restore requires an existing, empty destination database.' }

# MySQL interprets underscores as schema-grant wildcards unless partial_revokes is enabled.
$scope = if ($state[1] -eq '1') { $Database } else { $Database.Replace('_', '\_') }
$quotedScope = [regex]::Escape('`' + $scope + '`.*')
$account = '`(?:[^`]|``)*`@`(?:[^`]|``)*`'
$usageGrant = '\AGRANT USAGE ON \*\.\* TO ' + $account + '\z'
$schemaGrant = '\AGRANT SELECT, INSERT, CREATE ON ' + $quotedScope + ' TO ' + $account + '\z'
if ($rows.Count -ne 3 -or $rows[1] -cnotmatch $usageGrant -or $rows[2] -cnotmatch $schemaGrant) {
    throw 'Restore requires direct SELECT, INSERT, CREATE grants only on the exact destination database.'
}

# The authenticated account is the write boundary, even if the dump names another schema.
$arguments = @('"' + $defaultsArg + '"', '--no-login-paths', $dbArg, '--binary-mode', '--skip-force', '--skip-reconnect')
$process = Start-Process -FilePath $mysql -ArgumentList $arguments -RedirectStandardInput $dump -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Restore failed ($($process.ExitCode)). Drop the disposable destination and retry after diagnosing." }
