#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MySqlBin,
    [Parameter(Mandatory)][string]$TestAdminDefaultsFile,
    [Parameter(Mandatory)][string]$ScratchDirectory
)
$ErrorActionPreference = 'Stop'
$bin = (Resolve-Path -LiteralPath $MySqlBin).ProviderPath
$mysql = Join-Path $bin 'mysql.exe'
$inputDefaults = (Resolve-Path -LiteralPath $TestAdminDefaultsFile).ProviderPath
$scratch = (Resolve-Path -LiteralPath $ScratchDirectory).ProviderPath
$ops = Join-Path $PSScriptRoot '../Ops/Database.ps1'
$tag = [guid]::NewGuid().ToString('N').Substring(0, 12)
$source = "ao_source_$tag"
$target = "ao_target_$tag"
$user = "ao_restore_$tag"
$password = [guid]::NewGuid().ToString('N')
$admin = Join-Path $scratch "$tag local-admin.cnf"
$restricted = Join-Path $scratch "$user.cnf"
$cross = Join-Path $scratch "$tag cross-schema.sql"
$backup = Join-Path $scratch "$tag backup.sql"
$localOptions = @('[client]', 'protocol=TCP', 'host=127.0.0.1', 'port=3307')

function Write-PrivateDefaults([string]$Path, [string[]]$Lines) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $security = [Security.AccessControl.FileSecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'Allow'))
    # Create with the restricted ACL atomically, before writing the first byte.
    $stream = [IO.FileSystemAclExtensions]::Create(
        [IO.FileInfo]::new($Path), [IO.FileMode]::CreateNew, [Security.AccessControl.FileSystemRights]::FullControl,
        [IO.FileShare]::None, 4096, [IO.FileOptions]::None, $security)
    try {
        $writer = [IO.StreamWriter]::new($stream)
        try { foreach ($line in $Lines) { $writer.WriteLine($line) } }
        finally { $writer.Dispose() }
    } finally {
        $stream.Dispose()
    }
}

function Invoke-TestSql([string]$Sql) {
    $result = $Sql | & $mysql "--defaults-file=$admin" --no-login-paths --protocol=TCP --host=127.0.0.1 --port=3307 --batch --raw --skip-column-names
    if ($LASTEXITCODE -ne 0) { throw 'MySQL test fixture SQL failed.' }
    return $result
}

# Keep only credentials from the input. Ops must receive the same loopback
# endpoint as the fixture queries, regardless of options in the original file.
$reader = Join-Path $bin 'my_print_defaults.exe'
$options = @(& $reader "--defaults-file=$inputDefaults" --no-login-paths --show client)
if ($LASTEXITCODE -ne 0) { throw 'Could not read test administrator credentials.' }
$adminOptions = foreach ($name in @('user', 'password')) {
    $option = $options | Where-Object { $_.StartsWith("--$name=", [StringComparison]::Ordinal) } | Select-Object -Last 1
    if (!$option) { throw "The input [client] section must specify $name." }
    $value = $option.Substring($name.Length + 3)
    if ($value -match '[\x00-\x1F\x7F]') { throw "The test $name must be a single printable value." }
    $name + '="' + $value.Replace('\', '\\').Replace('"', '\"') + '"'
}
$sourceCreated = $false
$targetCreated = $false
$userCreated = $false
try {
    Write-PrivateDefaults $admin ($localOptions + $adminOptions)
    $scope = if ((Invoke-TestSql 'SELECT @@GLOBAL.partial_revokes;') -eq '1') { $target } else { $target.Replace('_', '\_') }
    Invoke-TestSql "CREATE DATABASE $source;"
    $sourceCreated = $true
    Invoke-TestSql "CREATE DATABASE $target;"
    $targetCreated = $true
    Invoke-TestSql "CREATE USER '$user'@'127.0.0.1' IDENTIFIED BY '$password';"
    $userCreated = $true
    Invoke-TestSql "CREATE TABLE $source.marker (value INT); INSERT INTO $source.marker VALUES (1);"
    Invoke-TestSql ('GRANT SELECT, INSERT, CREATE ON `' + $scope + '`.* TO ' + "'$user'@'127.0.0.1';")
    Write-PrivateDefaults $restricted ($localOptions + @("user=$user", "password=$password"))
    "INSERT INTO $source.marker VALUES (2);" | Set-Content -LiteralPath $cross -Encoding ascii

    try {
        & $ops -Action Restore -MySqlBin $bin -DefaultsFile $admin -Database $target -DumpFile $cross
        throw 'Broad restore credentials were accepted.'
    } catch {
        if ($_.Exception.Message -notmatch 'requires direct SELECT, INSERT, CREATE') { throw }
    }
    try {
        & $ops -Action Restore -MySqlBin $bin -DefaultsFile $restricted -Database $target -DumpFile $cross
        throw 'A restore escaped its target database.'
    } catch {
        if ($_.Exception.Message -notmatch '^Restore failed \(') { throw }
    }
    $original = @(Invoke-TestSql "SELECT value FROM $source.marker;")
    if ($original.Count -ne 1 -or $original[0] -ne '1') { throw 'Source data changed.' }

    & $ops -Action Backup -MySqlBin $bin -DefaultsFile $admin -Database $source -DumpFile $backup
    & $ops -Action Restore -MySqlBin $bin -DefaultsFile $restricted -Database $target -DumpFile $backup
    $restored = @(Invoke-TestSql "SELECT value FROM $target.marker;")
    if ($restored.Count -ne 1 -or $restored[0] -ne '1') { throw 'Restored data differs.' }
    'Cross-schema restore rejected; destination-only restore matched the source.'
} finally {
    try {
        if ($userCreated) { Invoke-TestSql "DROP USER '$user'@'127.0.0.1';" }
        if ($targetCreated) { Invoke-TestSql "DROP DATABASE $target;" }
        if ($sourceCreated) { Invoke-TestSql "DROP DATABASE $source;" }
    } finally {
        foreach ($file in @($admin, $restricted, $cross, $backup)) {
            if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
        }
    }
}
